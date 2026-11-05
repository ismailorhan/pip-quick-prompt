// QuickPromptSpeech: local speech-to-text for Quick Prompt (whisper.cpp, on this Mac only).
//
// The app starts it on the first mic press and talks to it with one JSON object per line:
//   in  (stdin):  {"cmd":"start","languages":["en","tr"],"prompt":"words to expect","conversation":true}  {"cmd":"stop"}  {"cmd":"cancel"}
//                 conversation mode only: {"cmd":"play","id":3,"wav":"<base64 16-bit mono WAV>"}  {"cmd":"hush"}
//   out (stdout): {"type":"ready"}  {"type":"listening"}  {"type":"level","value":0.4}
//                 {"type":"partial","text":"…"}  {"type":"final","text":"…"}  {"type":"error","message":"…"}
//                 conversation mode only: {"type":"played","id":3}  {"type":"barge"}
// Conversation mode: the microphone runs through macOS's voice processing (echo cancellation), and the answer is
// played through the same engine, so the microphone hardly hears it. While it plays, the voice detector watches
// the microphone; when someone speaks over it, the playback stops ("barge") and listening starts from there.
// While listening it writes the text as it hears it (partial: the words so far, the last ones may still change);
// on stop it transcribes the whole recording once more, with voice detection, and sends the final text.
// Usage: QuickPromptSpeech <model.bin> <vad.bin>
import AVFoundation
import Foundation
import Whisper

setvbuf(stdout, nil, _IOLBF, 0)
let output = DispatchQueue(label: "out")
func send(_ object: [String: Any]) {
    guard let data = try? JSONSerialization.data(withJSONObject: object), let line = String(data: data, encoding: .utf8) else { return }
    output.sync { print(line); fflush(stdout) }
}
func log(_ text: String) { FileHandle.standardError.write((text + "\n").data(using: .utf8)!) }

let args = CommandLine.arguments
guard args.count >= 3 else { send(["type": "error", "message": "usage: QuickPromptSpeech <model> <vad>"]); exit(2) }

// whisper.cpp prints a lot while loading; only errors go to the app's log.
whisper_log_set({ level, text, _ in
    if level == GGML_LOG_LEVEL_ERROR, let text = text { FileHandle.standardError.write(Data(String(cString: text).utf8)) }
}, nil)

var contextParams = whisper_context_default_params()
contextParams.use_gpu = true
contextParams.flash_attn = true
guard let ctx = whisper_init_from_file_with_params(args[1], contextParams) else {
    send(["type": "error", "message": "Could not load the speech model"]); exit(3)
}
var vadParams = whisper_vad_default_context_params()
vadParams.n_threads = 2
vadParams.use_gpu = false
guard let vad = whisper_vad_init_from_file_with_params(args[2], vadParams) else {
    send(["type": "error", "message": "Could not load the voice detector"]); exit(3)
}
let vadPath = args[2]
let threads = Int32(max(2, min(8, ProcessInfo.processInfo.activeProcessorCount - 2)))
send(["type": "ready"])

// MARK: audio

let rate = 16000.0
let frame = 512                       // the voice detector's step (32 ms)
let lock = NSLock()
var samples: [Float] = []             // everything heard since start, 16 kHz mono
var speechProbs: [Float] = []         // one per 512 samples
var vadDone = 0                       // samples given to the voice detector so far
var engine: AVAudioEngine?
var listening = false
var conversation = false              // the engine runs with voice processing and can play answers
var watching = false                  // conversation: playing an answer, waiting for the user to speak over it
var player: AVAudioPlayerNode?
let playFormat = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: 48000, channels: 1, interleaved: false)!
var playing = 0                       // buffers scheduled and not yet played
var playStarted = Date.distantPast
var watchLoop = false                 // one watchStep chain at a time
var aborting = false                  // stops a running decode (cancel, or stop while a live decode runs)

/// How loud, 0…1, as the ear hears it: -50 dB (a quiet room) is 0, -10 dB (speaking up close) is 1.
func loudness<C: Collection>(_ chunk: C) -> Double where C.Element == Float {
    guard !chunk.isEmpty else { return 0 }
    let rms = sqrt(chunk.reduce(0) { $0 + $1 * $1 } / Float(chunk.count))
    let db = 20 * log10(max(Double(rms), 1e-6))
    return min(1, max(0, (db + 50) / 40))
}

/// Test mode (QP_SPEECH_FILE=<16 kHz mono wav>): the file plays in as if spoken, at real-time speed.
var fileTimer: DispatchSourceTimer?
func startFile(_ path: String) throws {
    let file = try AVAudioFile(forReading: URL(fileURLWithPath: path))
    guard let buffer = AVAudioPCMBuffer(pcmFormat: file.processingFormat, frameCapacity: AVAudioFrameCount(file.length)) else { return }
    try file.read(into: buffer)
    let all = Array(UnsafeBufferPointer(start: buffer.floatChannelData![0], count: Int(buffer.frameLength)))
    var at = 0
    let timer = DispatchSource.makeTimerSource(queue: DispatchQueue.global())
    timer.schedule(deadline: .now(), repeating: 0.1)
    timer.setEventHandler {
        let n = min(1600, all.count - at)
        guard n > 0 else { return }
        lock.lock(); samples.append(contentsOf: all[at..<at + n]); lock.unlock()
        let chunk = all[at..<at + n]
        send(["type": "level", "value": loudness(chunk)])
        at += n
    }
    timer.resume()
    fileTimer = timer
}

func startAudio() throws {
    if engine != nil || fileTimer != nil { return }   // conversation: already running (watching an answer)
    if let path = ProcessInfo.processInfo.environment["QP_SPEECH_FILE"] { try startFile(path); return }
    let engine0 = AVAudioEngine()
    let input = engine0.inputNode
    if conversation {
        // Echo cancellation: what this engine plays is taken out of what the microphone hears.
        try input.setVoiceProcessingEnabled(true)
        if #available(macOS 14.0, *) {
            input.voiceProcessingOtherAudioDuckingConfiguration = AVAudioVoiceProcessingOtherAudioDuckingConfiguration(enableAdvancedDucking: false, duckingLevel: .min)
        }
        let node = AVAudioPlayerNode()
        engine0.attach(node)
        engine0.connect(node, to: engine0.mainMixerNode, format: playFormat)
        player = node
    }
    let inFormat = input.outputFormat(forBus: 0)
    guard inFormat.sampleRate > 0, let target = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: rate, channels: 1, interleaved: false),
          let converter = AVAudioConverter(from: inFormat, to: target) else {
        throw NSError(domain: "speech", code: 1, userInfo: [NSLocalizedDescriptionKey: "No microphone"])
    }
    var lastLevel = 0.0
    input.installTap(onBus: 0, bufferSize: 2048, format: inFormat) { buffer, _ in
        let capacity = AVAudioFrameCount(Double(buffer.frameLength) * rate / inFormat.sampleRate) + 32
        guard let out = AVAudioPCMBuffer(pcmFormat: target, frameCapacity: capacity) else { return }
        var given = false
        var error: NSError?
        converter.convert(to: out, error: &error) { _, status in
            if given { status.pointee = .noDataNow; return nil }
            given = true
            status.pointee = .haveData
            return buffer
        }
        guard error == nil, let data = out.floatChannelData?[0], out.frameLength > 0 else { return }
        let chunk = Array(UnsafeBufferPointer(start: data, count: Int(out.frameLength)))
        lock.lock(); samples.append(contentsOf: chunk); lock.unlock()
        // The level for the mic button (about 12 a second).
        let now = Date().timeIntervalSince1970
        if now - lastLevel > 0.08 {
            lastLevel = now
            send(["type": "level", "value": loudness(chunk)])
        }
    }
    engine0.prepare()
    try engine0.start()
    engine = engine0
}

func stopAudio() {
    fileTimer?.cancel()
    fileTimer = nil
    player?.stop()
    player = nil
    playing = 0
    watching = false
    engine?.inputNode.removeTap(onBus: 0)
    engine?.stop()
    engine = nil
}

// MARK: conversation: playing answers, hearing the user speak over them

/// 16-bit mono PCM WAV (as the engine makes them) to float audio at 48 kHz.
func decodeWav(_ data: Data) -> AVAudioPCMBuffer? {
    guard data.count > 44, data.prefix(4) == Data("RIFF".utf8) else { return nil }
    let rate = data.subdata(in: 24..<28).withUnsafeBytes { $0.loadUnaligned(as: UInt32.self) }
    var at = 12
    var pcm = Data()
    while at + 8 <= data.count {
        let id = String(data: data.subdata(in: at..<at + 4), encoding: .ascii) ?? ""
        let size = Int(data.subdata(in: at + 4..<at + 8).withUnsafeBytes { $0.loadUnaligned(as: UInt32.self) })
        if id == "data" { pcm = data.subdata(in: at + 8..<min(data.count, at + 8 + size)); break }
        at += 8 + size
    }
    let count = pcm.count / 2
    guard count > 0, rate > 0, let source = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: Double(rate), channels: 1, interleaved: false),
          let buffer = AVAudioPCMBuffer(pcmFormat: source, frameCapacity: AVAudioFrameCount(count)) else { return nil }
    pcm.withUnsafeBytes { raw in
        let ints = raw.bindMemory(to: Int16.self)
        for i in 0..<count { buffer.floatChannelData![0][i] = Float(Int16(littleEndian: ints[i])) / 32768 }
    }
    buffer.frameLength = AVAudioFrameCount(count)
    if Double(rate) == playFormat.sampleRate { return buffer }
    guard let converter = AVAudioConverter(from: source, to: playFormat),
          let out = AVAudioPCMBuffer(pcmFormat: playFormat, frameCapacity: AVAudioFrameCount(Double(count) * playFormat.sampleRate / Double(rate)) + 64) else { return nil }
    var given = false
    var error: NSError?
    converter.convert(to: out, error: &error) { _, status in
        if given { status.pointee = .endOfStream; return nil }
        given = true
        status.pointee = .haveData
        return buffer
    }
    return error == nil ? out : nil
}

/// Plays one piece of an answer (conversation mode). The microphone keeps running, cleaned of it.
func play(_ body: [String: Any]) {
    work.async {
        guard conversation, let text = body["wav"] as? String, let data = Data(base64Encoded: text), let buffer = decodeWav(data) else {
            send(["type": "played", "id": body["id"] ?? 0]); return
        }
        if engine == nil {
            // Between turns: start the engine to watch for the user speaking over the answer.
            lock.lock(); samples = []; lock.unlock()
            speechProbs = []; vadDone = 0
            whisper_vad_reset_state(vad)
            do { try startAudio() } catch { send(["type": "played", "id": body["id"] ?? 0]); return }
        }
        guard let node = player else { send(["type": "played", "id": body["id"] ?? 0]); return }
        if !listening { watching = true }
        playing += 1
        let id = body["id"] ?? 0
        node.scheduleBuffer(buffer) {
            work.async {
                playing = max(0, playing - 1)
                send(["type": "played", "id": id])
            }
        }
        if !node.isPlaying { node.play(); playStarted = Date() }
        if !watchLoop { watchLoop = true; work.asyncAfter(deadline: .now() + 0.1) { watchStep() } }
    }
}

func hush() {
    work.async {
        player?.stop()
        playing = 0
    }
}

/// While an answer plays: the voice detector over the (echo-cancelled) microphone. A quarter second of speech,
/// not in the first moment of playback, is the user speaking: stop the answer and listen from just before it.
func watchStep() {
    guard watching, !listening else { watchLoop = false; return }
    updateVoice()
    let recent = speechProbs.suffix(8)
    if playing > 0, Date().timeIntervalSince(playStarted) > 0.4, recent.count == 8, recent.reduce(0, +) / 8 > 0.6 {
        player?.stop()
        playing = 0
        watching = false
        lock.lock(); samples = Array(samples.suffix(Int(rate * 0.8))); lock.unlock()
        speechProbs = []; vadDone = 0; committed = []; commitAt = 0
        whisper_vad_reset_state(vad)
        stopRequested = false; aborting = false
        listening = true
        watchLoop = false
        send(["type": "barge"])
        send(["type": "listening"])
        work.asyncAfter(deadline: .now() + 0.4) { liveStep() }
        return
    }
    if playing == 0 && !listening {
        // The answer ended without the user speaking: free the microphone until the next turn.
        watching = false
        watchLoop = false
        stopAudio()
        return
    }
    work.asyncAfter(deadline: .now() + 0.1) { watchStep() }
}

/// Runs the voice detector over the samples it has not seen yet (whole frames only).
func updateVoice() {
    lock.lock()
    let end = samples.count - (samples.count - vadDone) % frame
    let fresh = vadDone < end ? Array(samples[vadDone..<end]) : []
    lock.unlock()
    guard !fresh.isEmpty else { return }
    if fresh.withUnsafeBufferPointer({ whisper_vad_detect_speech_no_reset(vad, $0.baseAddress, Int32($0.count)) }) {
        let n = Int(whisper_vad_n_probs(vad))
        if let p = whisper_vad_probs(vad) { speechProbs.append(contentsOf: UnsafeBufferPointer(start: p, count: n)) }
    }
    vadDone = end
}

func hasSpeech(from start: Int, to end: Int) -> Bool {
    let a = start / frame, b = min(speechProbs.count, end / frame)
    return a < b && speechProbs[a..<b].contains { $0 > 0.5 }
}

/// Silence at the end of what was heard, in samples (from the voice detector).
func trailingSilence() -> Int {
    var n = 0
    for p in speechProbs.reversed() { if p < 0.35 { n += 1 } else { break } }
    return n * frame
}

// MARK: transcription

var languages = ["en"]                // the ones the user speaks (Settings → Dictation)
var hints = ""
var lockedLanguage: String?           // several languages: decided after the first seconds of speech, then kept
var provisional = "en"                // before that: the better guess so far

/// Picks among the user's languages only: Whisper's open detection over a second of speech picks Russian or
/// Azerbaijani for Turkish too easily.
func detectLanguage(_ audio: [Float]) -> String? {
    let clip = Array(audio.suffix(Int(rate * 30)))
    guard clip.withUnsafeBufferPointer({ whisper_pcm_to_mel(ctx, $0.baseAddress, Int32($0.count), threads) }) == 0 else { return nil }
    var probs = [Float](repeating: 0, count: Int(whisper_lang_max_id()) + 1)
    guard whisper_lang_auto_detect(ctx, 0, threads, &probs) >= 0 else { return nil }
    return languages.max { probs[Int(whisper_lang_id($0))] < probs[Int(whisper_lang_id($1))] }
}

func speechSeconds() -> Double { Double(speechProbs.filter { $0 > 0.5 }.count * frame) / rate }

/// Phrases Whisper is known to invent over silence or noise (subtitle credits, sign-offs).
let invented = ["altyazı m.k.", "altyazı m.k", "altyazı", "thank you.", "thanks for watching!", "thanks for watching.",
                "abone olmayı unutmayın.", "izlediğiniz için teşekkürler.", "teşekkürler.", "you"]

func clean(_ text: String) -> String {
    var t = text.trimmingCharacters(in: .whitespacesAndNewlines)
    if t.hasPrefix("[") && t.hasSuffix("]") { return "" }      // [Music], [BLANK_AUDIO]
    if t.hasPrefix("(") && t.hasSuffix(")") { return "" }
    if invented.contains(t.lowercased()) { return "" }
    // "- " starts a new speaker in Whisper's subtitles style; dictation is one speaker.
    while t.hasPrefix("-") { t = String(t.dropFirst()).trimmingCharacters(in: .whitespaces) }
    while t.contains("  ") { t = t.replacingOccurrences(of: "  ", with: " ") }
    return t
}

struct Segment { let text: String; let end: Int }   // end in samples, from the start of the decoded audio

/// Transcribes the audio. `prompt` conditions the decoder (expected words, the text before this part).
func transcribe(_ audio: [Float], prompt: String, useVoiceDetection: Bool) -> [Segment]? {
    // Live: greedy, fast. The final pass: beam search (as whisper-cli does), which also keeps the punctuation.
    var p = whisper_full_default_params(useVoiceDetection ? WHISPER_SAMPLING_BEAM_SEARCH : WHISPER_SAMPLING_GREEDY)
    if useVoiceDetection { p.beam_search.beam_size = 5; p.greedy.best_of = 5 }
    p.n_threads = threads
    p.print_progress = false
    p.print_realtime = false
    p.print_special = false
    p.print_timestamps = false
    p.no_context = true
    p.suppress_blank = true
    p.suppress_nst = !useVoiceDetection
    p.no_speech_thold = 0.6
    p.abort_callback = { _ in aborting }
    let lang = lockedLanguage ?? provisional
    var segments: [Segment]?
    lang.withCString { langPtr in
        prompt.withCString { promptPtr in
            vadPath.withCString { vadPtr in
                p.language = langPtr
                p.detect_language = false
                p.initial_prompt = prompt.isEmpty ? nil : promptPtr
                p.vad = useVoiceDetection
                if useVoiceDetection {
                    p.vad_model_path = vadPtr
                    p.vad_params = whisper_vad_default_params()
                }
                let status = audio.withUnsafeBufferPointer { whisper_full(ctx, p, $0.baseAddress, Int32($0.count)) }
                guard status == 0, !aborting else { return }
                var list: [Segment] = []
                for i in 0..<whisper_full_n_segments(ctx) {
                    let text = clean(String(cString: whisper_full_get_segment_text(ctx, i)))
                    let end = Int(whisper_full_get_segment_t1(ctx, i)) * 160     // 10 ms units
                    if !text.isEmpty { list.append(Segment(text: text, end: end)) }
                }
                segments = list
            }
        }
    }
    return segments
}

func join(_ parts: [String]) -> String { parts.filter { !$0.isEmpty }.joined(separator: " ") }

// MARK: the live loop

let work = DispatchQueue(label: "work")
var committed: [String] = []          // finished parts of the live text
var commitAt = 0                      // where the unfinished audio starts (samples)
var stopRequested = false

func prompt(after text: [String]) -> String {
    // The expected words, then the end of what was said so far (keeps names and style consistent).
    let tail = String(join(text).suffix(180))
    return join([hints, tail])
}

func liveStep() {
    guard listening, !stopRequested else { return }
    updateVoice()
    lock.lock(); let total = samples.count; lock.unlock()
    let pending = total - commitAt
    if pending >= Int(rate * 0.6), hasSpeech(from: commitAt, to: total) {
        if lockedLanguage == nil {
            lock.lock(); let heard = Array(samples[0..<total]); lock.unlock()
            if let guess = detectLanguage(heard) { provisional = guess }
            if speechSeconds() >= 3 { lockedLanguage = provisional }
        }
        lock.lock(); let audio = Array(samples[commitAt..<total]); lock.unlock()
        if let segs = transcribe(audio, prompt: prompt(after: committed), useVoiceDetection: false), !stopRequested {
            let silence = trailingSilence()
            if silence >= Int(rate * 0.7) || pending >= Int(rate * 14) {
                // A pause (or a long stretch): what is decoded so far is settled; keep the last segment open
                // unless the speaker has paused.
                let keepLast = silence < Int(rate * 0.7) && segs.count > 1
                let done = keepLast ? Array(segs.dropLast()) : segs
                if let last = done.last {
                    committed.append(join(done.map(\.text)))
                    commitAt += keepLast ? min(last.end, audio.count) : audio.count
                }
                let open = keepLast ? segs.last!.text : ""
                send(["type": "partial", "text": join(committed + [open])])
            } else {
                send(["type": "partial", "text": join(committed + segs.map(\.text))])
            }
        }
    }
    work.asyncAfter(deadline: .now() + 0.35) { liveStep() }
}

func start(_ body: [String: Any]) {
    work.async {
        guard !listening else { return }
        let asked = (body["languages"] as? [String] ?? []).filter { whisper_lang_id($0) >= 0 }
        languages = asked.isEmpty ? ["en"] : Array(asked.prefix(8))
        hints = (body["prompt"] as? String ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
        lockedLanguage = languages.count == 1 ? languages[0] : nil
        provisional = languages[0]
        let wanted = body["conversation"] as? Bool ?? false
        if wanted != conversation || watching { player?.stop(); stopAudio() }   // a new turn starts clean
        conversation = wanted
        lock.lock(); samples = []; lock.unlock()
        speechProbs = []; vadDone = 0; committed = []; commitAt = 0
        stopRequested = false; aborting = false
        whisper_vad_reset_state(vad)
        do {
            try startAudio()
        } catch {
            send(["type": "error", "message": "Could not use the microphone: \(error.localizedDescription)"])
            return
        }
        listening = true
        send(["type": "listening"])
        work.asyncAfter(deadline: .now() + 0.4) { liveStep() }
    }
}

func stop() {
    stopRequested = true
    work.async {
        guard listening else { return }
        listening = false
        stopAudio()
        aborting = false
        lock.lock(); let audio = samples; lock.unlock()
        if lockedLanguage == nil, let guess = detectLanguage(audio) { lockedLanguage = guess }
        // The whole recording once more, with voice detection: better words and no invented ones over silence.
        let segs = audio.count > Int(rate * 0.3) ? transcribe(audio, prompt: hints, useVoiceDetection: true) : []
        send(["type": "final", "text": join((segs ?? []).map(\.text))])
    }
}

func cancel() {
    stopRequested = true
    aborting = true
    work.async {
        if listening { listening = false; stopAudio() }
        aborting = false
        send(["type": "cancelled"])
    }
}

// MARK: commands

/// Frees the model before leaving: ggml's Metal device asserts at exit if its buffers are still alive.
func quit() -> Never {
    stopRequested = true
    aborting = true
    work.sync {
        if listening { listening = false; stopAudio() }
        whisper_vad_free(vad)
        whisper_free(ctx)
    }
    exit(0)
}

DispatchQueue.global().async {
    while let line = readLine() {
        guard let data = line.data(using: .utf8), let body = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any] else { continue }
        switch body["cmd"] as? String {
        case "start": start(body)
        case "stop": stop()
        case "cancel": cancel()
        case "play": play(body)
        case "hush": hush()
        case "quit": quit()
        default: break
        }
    }
    quit()   // the app went away
}
dispatchMain()
