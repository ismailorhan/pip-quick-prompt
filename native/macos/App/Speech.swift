import AVFoundation
import AppKit
import CryptoKit

/// Dictation: runs the speech helper (QuickPromptSpeech, whisper.cpp on this Mac) for the popup that asks, and
/// downloads its model once. The page talks to it through the popup's "speech" messages; events go back to the
/// page as window.__quickPromptSpeech({type, …}).
final class Speech {
    static let shared = Speech()

    /// The model files, downloaded once into Application Support (never shipped with the app).
    struct ModelFile {
        let name: String
        let url: URL
        let size: Int64
        let sha256: String
    }
    static let files = [
        ModelFile(name: "ggml-large-v3-turbo-q5_0.bin",
                  url: URL(string: "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3-turbo-q5_0.bin")!,
                  size: 574_041_195, sha256: "394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2"),
        ModelFile(name: "ggml-silero-v5.1.2.bin",
                  url: URL(string: "https://huggingface.co/ggml-org/whisper-vad/resolve/main/ggml-silero-v5.1.2.bin")!,
                  size: 885_098, sha256: "29940d98d42b91fbd05ce489f3ecf7c72f0a42f027e4875919a28fb4c04ea2cf"),
    ]
    static var totalSize: Int64 { files.reduce(0) { $0 + $1.size } }

    static let modelsDirectory: URL = ProcessInfo.processInfo.environment["QP_SPEECH_MODELS"].map { URL(fileURLWithPath: $0, isDirectory: true) }   // tests
        ?? FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent("Quick Prompt/models", isDirectory: true)

    private static var helperURL: URL? {
        #if arch(arm64)
        let url = ProcessInfo.processInfo.environment["QP_SPEECH_HELPER"].map { URL(fileURLWithPath: $0) }   // tests
            ?? Bundle.main.bundleURL.appendingPathComponent("Contents/MacOS/QuickPromptSpeech")
        return FileManager.default.isExecutableFile(atPath: url.path) ? url : nil
        #else
        return nil   // Intel Macs: whisper is too slow there to dictate live
        #endif
    }

    private var process: Process?
    private var input: FileHandle?
    private var ready = false
    private var queued: [[String: Any]] = []
    private weak var owner: PopupController?
    private var idleTimer: Timer?
    private var download: Downloader?
    private let known = NSHashTable<PopupController>.weakObjects()   // popups to tell about the download

    // MARK: from the page

    func handle(_ popup: PopupController, _ body: [String: Any]) {
        known.add(popup)
        switch body["action"] as? String {
        case "probe":
            popup.speechEvent(status())
        case "start":
            start(popup, body)
        case "stop":
            command(["cmd": "stop"])
        case "cancel":
            command(["cmd": "cancel"])
        case "play":
            // Conversation mode: the answer plays through the helper, so its echo cancellation knows it.
            guard process != nil else { popup.speechEvent(["type": "played", "id": body["id"] ?? 0]); return }
            idleTimer?.invalidate()
            command(["cmd": "play", "id": body["id"] ?? 0, "wav": body["wav"] as? String ?? ""])
        case "hush":
            command(["cmd": "hush"])
        case "download":
            startDownload(popup)
        case "cancelDownload":
            download?.cancel()
            download = nil
            broadcast(status())
        case "deleteModel":
            quitHelper()
            for file in Speech.files {
                try? FileManager.default.removeItem(at: Speech.modelsDirectory.appendingPathComponent(file.name))
            }
            popup.speechEvent(status())
        case "openPrivacy":
            NSWorkspace.shared.open(URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Microphone")!)
        default:
            break
        }
    }

    private func modelReady() -> Bool {
        Speech.files.allSatisfy { file in
            let path = Speech.modelsDirectory.appendingPathComponent(file.name).path
            return ((try? FileManager.default.attributesOfItem(atPath: path)[.size] as? NSNumber)?.int64Value ?? 0) == file.size
        }
    }

    private func status() -> [String: Any] {
        var s: [String: Any] = ["type": "status", "available": Speech.helperURL != nil, "size": Speech.totalSize]
        if let d = download {
            s["model"] = "downloading"
            s["received"] = d.received
        } else {
            s["model"] = modelReady() ? "ready" : "missing"
        }
        return s
    }

    private func broadcast(_ event: [String: Any]) {
        known.allObjects.forEach { $0.speechEvent(event) }
    }

    // MARK: dictation

    private func start(_ popup: PopupController, _ body: [String: Any]) {
        guard Speech.helperURL != nil else { popup.speechEvent(["type": "error", "message": "Dictation needs a Mac with Apple silicon."]); return }
        guard modelReady() else { popup.speechEvent(status()); return }
        let fromFile = ProcessInfo.processInfo.environment["QP_SPEECH_FILE"] != nil   // tests: no microphone
        switch fromFile ? .authorized : AVCaptureDevice.authorizationStatus(for: .audio) {
        case .authorized:
            break
        case .notDetermined:
            NSApp.activate(ignoringOtherApps: true)
            AVCaptureDevice.requestAccess(for: .audio) { granted in
                DispatchQueue.main.async {
                    if granted { self.start(popup, body) } else { popup.speechEvent(["type": "error", "code": "permission", "message": "Quick Prompt may not use the microphone."]) }
                }
            }
            return
        default:
            popup.speechEvent(["type": "error", "code": "permission", "message": "Quick Prompt may not use the microphone."])
            return
        }
        if let previous = owner, previous !== popup { previous.speechEvent(["type": "cancelled"]) }
        owner = popup
        idleTimer?.invalidate()
        launchIfNeeded()
        popup.speechEvent(["type": ready ? "starting" : "loading"])
        command(["cmd": "start", "languages": body["languages"] as? [String] ?? ["en"], "prompt": body["prompt"] as? String ?? "",
                 "conversation": body["conversation"] as? Bool ?? false])
    }

    private func command(_ body: [String: Any]) {
        guard process != nil else { return }
        if !ready { queued.append(body); return }
        guard let data = try? JSONSerialization.data(withJSONObject: body) else { return }
        input?.write(data + Data("\n".utf8))
    }

    /// The helper's own messages (audio formats, voice processing on or off, errors), appended; over 1 MB it starts over.
    static func logFile() -> FileHandle? {
        let dir = FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Library/Logs/Quick Prompt", isDirectory: true)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        let url = dir.appendingPathComponent("speech.log")
        if ((try? FileManager.default.attributesOfItem(atPath: url.path)[.size] as? NSNumber)?.intValue ?? 0) > 1_000_000 {
            try? FileManager.default.removeItem(at: url)
        }
        if !FileManager.default.fileExists(atPath: url.path) { FileManager.default.createFile(atPath: url.path, contents: nil) }
        guard let handle = try? FileHandle(forWritingTo: url) else { return nil }
        handle.seekToEndOfFile()
        handle.write(Data("--- \(Date()) dictation helper started\n".utf8))
        return handle
    }

    private func launchIfNeeded() {
        guard process == nil, let helper = Speech.helperURL else { return }
        let p = Process()
        p.executableURL = helper
        p.arguments = Speech.files.map { Speech.modelsDirectory.appendingPathComponent($0.name).path }
        let stdin = Pipe(), stdout = Pipe()
        p.standardInput = stdin
        p.standardOutput = stdout
        p.standardError = Speech.logFile() ?? FileHandle.standardError   // ~/Library/Logs/Quick Prompt/speech.log
        var buffer = Data()
        stdout.fileHandleForReading.readabilityHandler = { [weak self] handle in
            let chunk = handle.availableData
            guard !chunk.isEmpty else { return }
            buffer.append(chunk)
            while let nl = buffer.firstIndex(of: 0x0A) {
                let line = buffer.subdata(in: buffer.startIndex..<nl)
                buffer.removeSubrange(buffer.startIndex...nl)
                if let event = (try? JSONSerialization.jsonObject(with: line)) as? [String: Any] {
                    DispatchQueue.main.async { self?.helperEvent(event) }
                }
            }
        }
        p.terminationHandler = { [weak self] _ in
            DispatchQueue.main.async {
                guard let self = self, self.process === p else { return }
                self.process = nil
                self.ready = false
                self.queued = []
                self.owner?.speechEvent(["type": "error", "message": "Dictation stopped unexpectedly."])
            }
        }
        do {
            try p.run()
        } catch {
            owner?.speechEvent(["type": "error", "message": "Could not start dictation."])
            return
        }
        process = p
        input = stdin.fileHandleForWriting
        ready = false
    }

    private func helperEvent(_ event: [String: Any]) {
        let type = event["type"] as? String ?? ""
        if type == "ready" {
            ready = true
            let waiting = queued
            queued = []
            waiting.forEach(command)
            return
        }
        owner?.speechEvent(event)
        if type == "final" || type == "cancelled" || type == "error" {
            scheduleIdleQuit()
        }
    }

    /// The model takes about 1 GB of memory: the helper leaves after ten minutes without dictation.
    private func scheduleIdleQuit() {
        idleTimer?.invalidate()
        idleTimer = Timer.scheduledTimer(withTimeInterval: 600, repeats: false) { [weak self] _ in self?.quitHelper() }
    }

    private func quitHelper() {
        guard let p = process else { return }
        process = nil
        ready = false
        input?.write(Data("{\"cmd\":\"quit\"}\n".utf8))
        try? input?.close()
        DispatchQueue.main.asyncAfter(deadline: .now() + 3) { if p.isRunning { p.terminate() } }
    }

    func shutdown() {
        quitHelper()
    }

    // MARK: model download

    private func startDownload(_ popup: PopupController) {
        guard download == nil, !modelReady() else { popup.speechEvent(status()); return }
        let d = Downloader(files: Speech.files, into: Speech.modelsDirectory)
        download = d
        d.onProgress = { [weak self] in self?.broadcast(self?.status() ?? [:]) }
        d.onFinish = { [weak self] error in
            guard let self = self else { return }
            self.download = nil
            if let error = error { self.broadcast(["type": "error", "code": "download", "message": error]) }
            self.broadcast(self.status())
        }
        d.start()
        broadcast(status())
    }
}

/// Downloads the model files one after another, checks size and SHA-256, and only then moves each into place.
final class Downloader: NSObject, URLSessionDownloadDelegate {
    private let files: [Speech.ModelFile]
    private let directory: URL
    private var index = 0
    private var done: Int64 = 0
    private(set) var received: Int64 = 0
    private var session: URLSession?
    private var lastReport = Date.distantPast
    private var cancelled = false
    var onProgress: (() -> Void)?
    var onFinish: ((String?) -> Void)?

    init(files: [Speech.ModelFile], into directory: URL) {
        self.files = files
        self.directory = directory
    }

    func start() {
        try? FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        session = URLSession(configuration: .default, delegate: self, delegateQueue: .main)
        next()
    }

    func cancel() {
        cancelled = true
        session?.invalidateAndCancel()
    }

    private func next() {
        while index < files.count, isInPlace(files[index]) {
            done += files[index].size
            index += 1
        }
        guard index < files.count else { finish(nil); return }
        session?.downloadTask(with: files[index].url).resume()
    }

    private func isInPlace(_ file: Speech.ModelFile) -> Bool {
        let path = directory.appendingPathComponent(file.name).path
        return ((try? FileManager.default.attributesOfItem(atPath: path)[.size] as? NSNumber)?.int64Value ?? 0) == file.size
    }

    private func finish(_ error: String?) {
        session?.finishTasksAndInvalidate()
        session = nil
        if !cancelled { onFinish?(error) }
    }

    func urlSession(_ session: URLSession, downloadTask: URLSessionDownloadTask, didWriteData bytesWritten: Int64,
                    totalBytesWritten: Int64, totalBytesExpectedToWrite: Int64) {
        received = done + totalBytesWritten
        if Date().timeIntervalSince(lastReport) > 0.25 {
            lastReport = Date()
            onProgress?()
        }
    }

    func urlSession(_ session: URLSession, downloadTask: URLSessionDownloadTask, didFinishDownloadingTo location: URL) {
        let file = files[index]
        guard let data = try? Data(contentsOf: location, options: .alwaysMapped), Int64(data.count) == file.size,
              SHA256.hash(data: data).map({ String(format: "%02x", $0) }).joined() == file.sha256 else {
            finish("The downloaded speech model did not check out. Try again.")
            return
        }
        let target = directory.appendingPathComponent(file.name)
        try? FileManager.default.removeItem(at: target)
        do {
            try FileManager.default.moveItem(at: location, to: target)
        } catch {
            finish("Could not save the speech model.")
            return
        }
        done += file.size
        index += 1
        next()
    }

    func urlSession(_ session: URLSession, task: URLSessionTask, didCompleteWithError error: Error?) {
        if let error = error, !cancelled { finish("Download failed: \(error.localizedDescription)") }
    }
}
