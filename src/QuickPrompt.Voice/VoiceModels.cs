namespace QuickPrompt.Voice
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Security.Cryptography;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// The voice model files, downloaded once into &lt;data&gt;/models/voice and checked by size and SHA-256.
    /// EMA Lightning: our ONNX export of the original weights (canberkkkkkk/ema-lightning e7c96f2, Apache-2.0;
    /// tools/voice/export_ema.py), published on this repository's "voice-models-1" release.
    /// Kokoro-82M: onnx-community/Kokoro-82M-v1.0-ONNX at a fixed revision (Apache-2.0), int8.
    /// </summary>
    public sealed class VoiceModels
    {
        public sealed record ModelFile(String Folder, String Name, String Url, Int64 Size, String Sha256);

        private const String Ema = "https://github.com/ismailorhan/pip-quick-prompt/releases/download/voice-models-1/";
        private const String Kokoro = "https://huggingface.co/onnx-community/Kokoro-82M-v1.0-ONNX/resolve/1939ad2a8e416c0acfeecc08a694d14ef25f2231/";

        public static readonly IReadOnlyList<ModelFile> Files = new[]
        {
            new ModelFile("ema", "ema.json", Ema + "ema.json", 350, "5b4ea5d874e3cac190f8cd048144fa5b29835848c4e5a823f7965e43be72307b"),
            new ModelFile("ema", "ema-text.onnx", Ema + "ema-text.onnx", 4808026, "571bb40499b7573331514fc99ce46a984d0ed2382696c6998aa38f4794a0084f"),
            new ModelFile("ema", "ema-sound.onnx", Ema + "ema-sound.onnx", 18134408, "198cfee514ccb76f3574ed0f4f91ba057935008c394c6a78223a35cca1217cfb"),
            new ModelFile("ema", "ema-decoder.onnx", Ema + "ema-decoder.onnx", 12031945, "396c06a8e5711fd40bfd2c3f4f33851feb2ad07a86485833844e0ae8ff637b17"),
            new ModelFile("kokoro", "model_quantized.onnx", Kokoro + "onnx/model_quantized.onnx", 92361116, "fbae9257e1e05ffc727e951ef9b9c98418e6d79f1c9b6b13bd59f5c9028a1478"),
            new ModelFile("kokoro", "af_heart.bin", Kokoro + "voices/af_heart.bin", 522240, "d583ccff3cdca2f7fae535cb998ac07e9fcb90f09737b9a41fa2734ec44a8f0b"),
            new ModelFile("kokoro", "af_bella.bin", Kokoro + "voices/af_bella.bin", 522240, "f69d836209b78eb8c66e75e3cda491e26ea838a3674257e9d4e5703cbaf55c8b"),
            new ModelFile("kokoro", "am_michael.bin", Kokoro + "voices/am_michael.bin", 522240, "1d1f21dd8da39c30705cd4c75d039d265e9bc4a2a93ed09bc9e1b1225eb95ba1"),
            new ModelFile("kokoro", "bf_emma.bin", Kokoro + "voices/bf_emma.bin", 522240, "669fe0647f9dd04fcab92f1439a40eeb4c8b4ab1f82e4996fe3d918ce4a63b73"),
            new ModelFile("kokoro", "bm_george.bin", Kokoro + "voices/bm_george.bin", 522240, "c4b235a4c1f2cd3b939fed08b899ce9385638b763f7b73a59616c4fc9bd6c9bc"),
        };

        private readonly Object _lock = new Object();
        private CancellationTokenSource _cts;
        private Int64 _received;
        private String _error = "";

        public VoiceModels(String dataDir)
        {
            this.Root = Path.Combine(dataDir, "models", "voice");
        }

        public String Root { get; }

        public static Int64 TotalSize => Files.Sum(f => f.Size);

        public String PathOf(String folder, String name) => Path.Combine(this.Root, folder, name);

        /// <summary>Every file there at its size (the hash was checked when it was downloaded).</summary>
        public Boolean Ready => Files.All(f => new FileInfo(this.PathOf(f.Folder, f.Name)) is { Exists: true } i && i.Length == f.Size);

        public Boolean Downloading { get { lock (this._lock) { return this._cts != null; } } }

        public Int64 Received { get { lock (this._lock) { return this._received; } } }

        public String Error { get { lock (this._lock) { return this._error; } } }

        public void Start(Action<String, Exception> log)
        {
            lock (this._lock)
            {
                if (this._cts != null || this.Ready)
                {
                    return;
                }

                this._cts = new CancellationTokenSource();
                this._error = "";
                this._received = 0;
            }

            _ = Task.Run(() => this.RunAsync(log));
        }

        public void Cancel()
        {
            lock (this._lock)
            {
                this._cts?.Cancel();
            }
        }

        private async Task RunAsync(Action<String, Exception> log)
        {
            CancellationToken token;
            lock (this._lock)
            {
                token = this._cts.Token;
            }

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("QuickPrompt");
                foreach (var file in Files)
                {
                    var target = this.PathOf(file.Folder, file.Name);
                    if (new FileInfo(target) is { Exists: true } done && done.Length == file.Size)
                    {
                        this.Add(file.Size);
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    var part = target + ".part";
                    using (var response = await http.GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        await using var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                        await using var output = File.Create(part);
                        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                        var buffer = new Byte[1 << 16];
                        Int32 n;
                        while ((n = await source.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                        {
                            await output.WriteAsync(buffer.AsMemory(0, n), token).ConfigureAwait(false);
                            sha.AppendData(buffer, 0, n);
                            this.Add(n);
                        }

                        var hash = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
                        if (output.Length != file.Size || hash != file.Sha256)
                        {
                            throw new InvalidDataException(file.Name + " did not download correctly (size or checksum).");
                        }
                    }

                    File.Move(part, target, true);
                }
            }
            catch (OperationCanceledException)
            {
                lock (this._lock) { this._error = ""; }
            }
            catch (Exception ex)
            {
                log("Voice model download failed", ex);
                lock (this._lock) { this._error = "The voices could not be downloaded. Check the connection and try again."; }
            }
            finally
            {
                lock (this._lock)
                {
                    this._cts?.Dispose();
                    this._cts = null;
                }
            }
        }

        private void Add(Int64 n)
        {
            lock (this._lock)
            {
                this._received += n;
            }
        }
    }
}
