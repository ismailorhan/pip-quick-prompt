namespace QuickPrompt.Voice
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Text.Json;
    using Microsoft.ML.OnnxRuntime;
    using QuickPrompt.Core;

    /// <summary>
    /// Read aloud for the app's engine: Turkish with EMA Lightning, English with Kokoro, both on this computer.
    /// The models are downloaded once (VoiceModels); sessions load on the first sentence and stay.
    /// </summary>
    public sealed class LocalVoices : IReadAloud, IDisposable
    {
        private readonly VoiceModels _models;
        private readonly Action<String, Exception> _log;
        private readonly Object _lock = new Object();
        private EmaVoice _ema;
        private KokoroVoice _kokoro;
        private Int32 _seed = 1;

        public LocalVoices(String dataDir, Action<String, Exception> log)
        {
            this._models = new VoiceModels(dataDir);
            this._log = log ?? ((_, _) => { });
        }

        /// <summary>ONNX Runtime ships no macOS x64 library (1.31): Intel Macs have no voices.</summary>
        public static Boolean Supported => !(OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture != Architecture.Arm64);

        public String StatusJson()
        {
            var state = !Supported ? "unsupported" : this._models.Ready ? "ready" : this._models.Downloading ? "downloading" : "missing";
            return JsonSerializer.Serialize(new
            {
                available = Supported,
                state,
                received = this._models.Received,
                size = VoiceModels.TotalSize,
                error = this._models.Error,
                languages = new[] { "tr", "en" },
                normalizer = TurkishNormalizer.Available,
                voices = new { en = KokoroVoice.Voices.Select(v => new { id = v.Id, name = v.Name }).ToArray() },
            });
        }

        public void Download()
        {
            if (Supported)
            {
                this._models.Start(this._log);
            }
        }

        public void CancelDownload() => this._models.Cancel();

        public Byte[] Speak(String lang, String text, String voice, Double speed)
        {
            if (!Supported || !this._models.Ready || String.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            speed = Math.Clamp(speed, 0.5, 2.0);
            try
            {
                lock (this._lock)
                {
                    switch (lang)
                    {
                        case "tr":
                            this._ema ??= new EmaVoice(Path.Combine(this._models.Root, "ema"), Options());
                            return Wav.Encode(this._ema.Speak(text, speed, this._seed++), EmaVoice.Rate);
                        case "en":
                            this._kokoro ??= new KokoroVoice(Path.Combine(this._models.Root, "kokoro"), Options());
                            return Wav.Encode(this._kokoro.Speak(text, voice, speed), KokoroVoice.Rate);
                        default:
                            return null;
                    }
                }
            }
            catch (Exception ex)
            {
                this._log("Read aloud failed", ex);
                return null;
            }
        }

        private static SessionOptions Options() => new SessionOptions
        {
            IntraOpNumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4),
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
        };

        public void Dispose()
        {
            this._ema?.Dispose();
            this._kokoro?.Dispose();
        }
    }

    /// <summary>16-bit mono PCM WAV.</summary>
    public static class Wav
    {
        public static Byte[] Encode(Single[] audio, Int32 rate)
        {
            using var ms = new MemoryStream(44 + audio.Length * 2);
            using var w = new BinaryWriter(ms);
            w.Write("RIFF"u8.ToArray());
            w.Write(36 + audio.Length * 2);
            w.Write("WAVEfmt "u8.ToArray());
            w.Write(16);
            w.Write((Int16)1);
            w.Write((Int16)1);
            w.Write(rate);
            w.Write(rate * 2);
            w.Write((Int16)2);
            w.Write((Int16)16);
            w.Write("data"u8.ToArray());
            w.Write(audio.Length * 2);
            foreach (var s in audio)
            {
                w.Write((Int16)Math.Round(Math.Clamp(s, -1f, 1f) * 32767f));
            }

            w.Flush();
            return ms.ToArray();
        }
    }
}
