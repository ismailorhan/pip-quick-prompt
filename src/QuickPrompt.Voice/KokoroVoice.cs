namespace QuickPrompt.Voice
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using Microsoft.ML.OnnxRuntime;
    using Microsoft.ML.OnnxRuntime.Tensors;

    /// <summary>
    /// Kokoro-82M (English, Apache-2.0) on ONNX Runtime, fp32: phonemes (EnglishG2P) -> token ids -> 24 kHz audio,
    /// with a voice's style vector picked by the number of tokens, as kokoro-js does.
    /// </summary>
    public sealed class KokoroVoice : IDisposable
    {
        public const Int32 Rate = 24000;
        private const Int32 MaxTokens = 510;

        public static readonly IReadOnlyList<(String Id, String Name)> Voices = new[]
        {
            ("af_heart", "Heart, US"), ("af_bella", "Bella, US"), ("am_michael", "Michael, US"),
            ("bf_emma", "Emma, UK"), ("bm_george", "George, UK"),
        };

        private readonly InferenceSession _session;
        private readonly Dictionary<Char, Int64> _vocab;
        private readonly Dictionary<String, Single[]> _styles = new Dictionary<String, Single[]>();
        private readonly String _folder;
        private readonly EnglishG2P _g2p = new EnglishG2P();

        public KokoroVoice(String folder, SessionOptions options, String model = "model.onnx")
        {
            this._folder = folder;
            this._session = new InferenceSession(Path.Combine(folder, model), options);
            this._vocab = LoadVocab();
        }

        public static Dictionary<Char, Int64> LoadVocab()
        {
            using var stream = typeof(KokoroVoice).Assembly.GetManifestResourceStream("QuickPrompt.Voice.kokoro-config.json")
                ?? throw new InvalidOperationException("Missing Kokoro config");
            using var config = JsonDocument.Parse(stream);
            return config.RootElement.GetProperty("vocab").EnumerateObject()
                .Where(p => p.Name.Length == 1).ToDictionary(p => p.Name[0], p => (Int64)p.Value.GetInt32());
        }

        public String Phonemes(String text) => this._g2p.Phonemes(text);

        public Single[] Speak(String text, String voice, Double speed)
        {
            if (!Voices.Any(v => v.Id == voice))
            {
                voice = Voices[0].Id;
            }

            var style = this.Style(voice);
            var audio = new List<Single>();
            foreach (var part in Parts(this.Phonemes(text)))
            {
                var ids = new List<Int64> { 0 };
                ids.AddRange(part.Where(this._vocab.ContainsKey).Select(c => this._vocab[c]).Take(MaxTokens));
                ids.Add(0);
                var tokens = ids.Count - 2;
                if (tokens == 0)
                {
                    continue;
                }

                var row = new Single[256];
                Array.Copy(style, Math.Min(tokens, 509) * 256, row, 0, 256);
                using var output = this._session.Run(new[]
                {
                    NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<Int64>(ids.ToArray(), new[] { 1, ids.Count })),
                    NamedOnnxValue.CreateFromTensor("style", new DenseTensor<Single>(row, new[] { 1, 256 })),
                    NamedOnnxValue.CreateFromTensor("speed", new DenseTensor<Single>(new[] { (Single)speed }, new[] { 1 })),
                });
                audio.AddRange(output.First().AsTensor<Single>());
            }

            return audio.ToArray();
        }

        /// <summary>Phoneme strings short enough for one pass: cut after sentences, then clauses.</summary>
        private static IEnumerable<String> Parts(String phonemes)
        {
            var rest = phonemes.Trim();
            while (rest.Length > MaxTokens)
            {
                var window = rest.Substring(0, MaxTokens);
                var cut = new[] { ". ", "! ", "? ", "; ", ", ", " " }.Select(m => window.LastIndexOf(m, StringComparison.Ordinal)).FirstOrDefault(i => i > 0);
                cut = cut > 0 ? cut + 1 : MaxTokens;
                yield return rest.Substring(0, cut).Trim();
                rest = rest.Substring(cut).Trim();
            }

            if (rest.Length > 0)
            {
                yield return rest;
            }
        }

        private Single[] Style(String voice)
        {
            if (!this._styles.TryGetValue(voice, out var style))
            {
                var bytes = File.ReadAllBytes(Path.Combine(this._folder, voice + ".bin"));
                style = new Single[bytes.Length / 4];
                Buffer.BlockCopy(bytes, 0, style, 0, style.Length * 4);
                this._styles[voice] = style;
            }

            return style;
        }

        public void Dispose() => this._session.Dispose();
    }
}
