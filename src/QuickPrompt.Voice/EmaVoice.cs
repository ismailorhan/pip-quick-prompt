namespace QuickPrompt.Voice
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using Microsoft.ML.OnnxRuntime;
    using Microsoft.ML.OnnxRuntime.Tensors;

    /// <summary>
    /// EMA Lightning 1.1.2 (Turkish, Apache-2.0) on ONNX Runtime: a port of its frontend, chunker and engine
    /// (ema_lightning/frontend.py, chunker.py, engine.py) over our ONNX export of its three stages.
    /// Text -> letters -> durations (text) -> latents in four steps (sound) -> 48 kHz audio (decoder).
    /// </summary>
    public sealed class EmaVoice : IDisposable
    {
        public const Int32 Rate = 48000;
        private const Int32 MaxWordFrames = 250, MaxFrames = 3000;

        private readonly InferenceSession _text, _sound, _decoder;
        private readonly Dictionary<Char, Int64> _stoi;
        private readonly HashSet<Char> _vocab;
        private readonly Int32 _steps, _latentDim, _hop;

        public EmaVoice(String folder, SessionOptions options)
        {
            using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "ema.json")));
            var vocab = config.RootElement.GetProperty("vocab").EnumerateArray().Select(v => v.GetString()).ToList();
            this._stoi = new Dictionary<Char, Int64>();
            for (var i = 0; i < vocab.Count; i++)
            {
                if (vocab[i].Length == 1)
                {
                    this._stoi[vocab[i][0]] = i;
                }
            }

            this._vocab = new HashSet<Char>(this._stoi.Keys);
            this._steps = config.RootElement.GetProperty("times").GetArrayLength();
            this._latentDim = config.RootElement.GetProperty("latent_dim").GetInt32();
            this._hop = config.RootElement.GetProperty("hop").GetInt32();
            this._text = new InferenceSession(Path.Combine(folder, "ema-text.onnx"), options);
            this._sound = new InferenceSession(Path.Combine(folder, "ema-sound.onnx"), options);
            this._decoder = new InferenceSession(Path.Combine(folder, "ema-decoder.onnx"), options);
        }

        /// <summary>48 kHz float audio for one text.</summary>
        public Single[] Speak(String text, Double speed, Int32 seed)
        {
            var spoken = Alphabet(TurkishNormalizer.Normalize(Clean(text)), this._vocab);
            var audio = new List<Single>();
            var i = 0;
            foreach (var (piece, pause) in Chunk(spoken, speed))
            {
                audio.AddRange(this.SpeakPiece(piece, speed, (seed * 1_000_003L + i++) & 0x7FFFFFFF));
                audio.AddRange(new Single[(Int32)Math.Round(pause * Rate)]);
            }

            return audio.ToArray();
        }

        // ---------- frontend.py ----------
        private static readonly Regex Unsafe = new Regex("[\u0000-\u0008\u000b-\u001f\u007f-\u009f؜‎‏‪-‮⁦-⁩]");
        private const String Turkish = "çğıöşüÇĞİÖŞÜ";

        private static String Clean(String text) => Unsafe.Replace(text ?? "", " ");

        /// <summary>Lowercase the Turkish way, fold other accents, drop what the model cannot read.</summary>
        public static String Alphabet(String text, ISet<Char> vocab)
        {
            var t = new StringBuilder(text);
            foreach (var (a, b) in Typography)
            {
                t.Replace(a, b);
            }

            var lower = t.ToString().Replace("İ", "i").Replace("I", "ı").ToLower(CultureInfo.InvariantCulture);
            var out_ = new StringBuilder(lower.Length);
            foreach (var ch in lower)
            {
                var s = Turkish.IndexOf(ch) >= 0 ? ch.ToString()
                    : new String(ch.ToString().Normalize(NormalizationForm.FormKD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
                out_.Append(s.Length > 0 && s.All(vocab.Contains) ? s : " ");
            }

            return Regex.Replace(out_.ToString(), @"\s+", " ").Trim();
        }

        private static readonly (String, String)[] Typography =
        {
            ("’", "'"), ("‘", "'"), ("ʼ", "'"), ("´", "'"), ("`", "'"), ("“", "\""), ("”", "\""), ("„", "\""), ("«", "\""), ("»", "\""),
            ("–", "-"), ("—", "-"), ("−", "-"), ("…", "..."),
        };

        // ---------- chunker.py ----------
        private static readonly (Regex, Double)[] Cuts =
        {
            (new Regex("[.!?]+[\"')]*(?= )"), 0.25), (new Regex("[,;:](?= )"), 0.12), (new Regex(@"\S(?= )"), 0.12),
        };

        private static readonly Regex Letter = new Regex(@"[^\W\d_]");

        /// <summary>Pieces of at most ~10 s of speech, each ending on a sentence end, with the pause after it.</summary>
        public static List<(String Piece, Double Pause)> Chunk(String text, Double speed)
        {
            var limit = (Int32)Math.Min(250, 18.0 * 10.0 * speed);
            var pieces = new List<(String, Double)>();
            var rest = text.Trim();
            while (rest.Length > 0)
            {
                Int32 cut = rest.Length;
                Double pause = 0;
                if (rest.Length > limit)
                {
                    cut = limit;
                    var window = rest.Substring(0, Math.Min(rest.Length, limit + 1));
                    foreach (var (pattern, gap) in Cuts)
                    {
                        var ends = pattern.Matches(window).Select(m => m.Index + m.Length).Where(e => e <= limit + 1).ToList();
                        if (ends.Count > 0)
                        {
                            cut = ends[^1];
                            pause = gap;
                            break;
                        }
                    }
                }

                var piece = rest.Substring(0, cut).Trim();
                rest = rest.Substring(cut).Trim();
                if (Letter.IsMatch(piece))
                {
                    pieces.Add((Finish(piece), pause));
                }
            }

            if (pieces.Count > 0)
            {
                pieces[^1] = (pieces[^1].Item1, 0.0);
            }

            return pieces;
        }

        private static String Finish(String piece)
        {
            var end = piece.TrimEnd('"', '\'', ')');
            if (end.EndsWith('.') || end.EndsWith('!') || end.EndsWith('?'))
            {
                return piece;
            }

            return piece.TrimEnd(',', ';', ':', '-', ' ') + ".";
        }

        // ---------- engine.py: piece, plan, think, decode ----------
        private Single[] SpeakPiece(String text, Double speed, Int64 seed)
        {
            var L = text.Length;
            var ids = text.Select(ch => this._stoi.TryGetValue(ch, out var id) ? id : 1L).ToArray();
            var starts = Enumerable.Range(0, L).Where(i => text[i] != ' ' && (i == 0 || text[i - 1] == ' ')).ToList();
            if (starts.Count == 0)
            {
                starts.Add(0);
            }

            var bounds = new List<Int32> { 0 };
            bounds.AddRange(starts.Skip(1));
            bounds.Add(L);
            var cw = new Int64[L];
            var wstart = new Int64[L];
            for (var w = 0; w + 1 < bounds.Count; w++)
            {
                for (var i = bounds[w]; i < bounds[w + 1]; i++)
                {
                    cw[i] = w;
                    wstart[i] = bounds[w];
                }
            }

            var mask = Enumerable.Repeat(true, L).ToArray();
            using var textOut = this._text.Run(new[]
            {
                NamedOnnxValue.CreateFromTensor("ids", new DenseTensor<Int64>(ids, new[] { 1, L })),
                NamedOnnxValue.CreateFromTensor("mask", new DenseTensor<Boolean>(mask, new[] { 1, L })),
            });
            var h = textOut.First(v => v.Name == "h").AsTensor<Single>();
            var dur = textOut.First(v => v.Name == "dur").AsTensor<Single>().ToArray().Select(d => (Single)(d / speed)).ToArray();

            // plan: frames per word, then each frame's word and position inside it
            var words = (Int32)cw[^1] + 1;
            var sums = new Double[words];
            for (var i = 0; i < L; i++)
            {
                sums[cw[i]] += dur[i];
            }

            var counts = sums.Select(s => (Int32)Math.Clamp(Math.Round(s, MidpointRounding.ToEven), 1, MaxWordFrames)).ToArray();
            var T = Math.Min(counts.Sum(), MaxFrames);
            var fw = new Int64[T];
            var fp = new Single[T];
            var f = 0;
            for (var w = 0; w < words && f < T; w++)
            {
                for (var k = 0; k < counts[w] && f < T; k++, f++)
                {
                    fw[f] = w;
                    fp[f] = (Single)((Double)k / counts[w]);
                }
            }

            // think: four steps from seeded noise
            var noise = Gaussian(seed, this._steps * T * this._latentDim);
            var dims = h.Dimensions.ToArray();
            using var soundOut = this._sound.Run(new[]
            {
                NamedOnnxValue.CreateFromTensor("h", new DenseTensor<Single>(h.ToArray(), dims)),
                NamedOnnxValue.CreateFromTensor("dur", new DenseTensor<Single>(dur, new[] { 1, L })),
                NamedOnnxValue.CreateFromTensor("mask", new DenseTensor<Boolean>(mask, new[] { 1, L })),
                NamedOnnxValue.CreateFromTensor("cw", new DenseTensor<Int64>(cw, new[] { 1, L })),
                NamedOnnxValue.CreateFromTensor("wstart", new DenseTensor<Int64>(wstart, new[] { 1, L })),
                NamedOnnxValue.CreateFromTensor("fw", new DenseTensor<Int64>(fw, new[] { 1, T })),
                NamedOnnxValue.CreateFromTensor("fp", new DenseTensor<Single>(fp, new[] { 1, T })),
                NamedOnnxValue.CreateFromTensor("fmask", new DenseTensor<Boolean>(Enumerable.Repeat(true, T).ToArray(), new[] { 1, T })),
                NamedOnnxValue.CreateFromTensor("noise", new DenseTensor<Single>(noise, new[] { 1, this._steps, T, this._latentDim })),
            });
            var latents = soundOut.First().AsTensor<Single>();   // [1, T, D]

            // decode: [1, D, T]
            var z = new Single[this._latentDim * T];
            for (var t = 0; t < T; t++)
            {
                for (var d = 0; d < this._latentDim; d++)
                {
                    z[d * T + t] = latents[0, t, d];
                }
            }

            using var decoded = this._decoder.Run(new[] { NamedOnnxValue.CreateFromTensor("z", new DenseTensor<Single>(z, new[] { 1, this._latentDim, T })) });
            return decoded.First().AsTensor<Single>().ToArray();
        }

        private static Single[] Gaussian(Int64 seed, Int32 count)
        {
            var rng = new Random((Int32)(seed % Int32.MaxValue));
            var out_ = new Single[count];
            for (var i = 0; i < count; i += 2)
            {
                var u1 = 1.0 - rng.NextDouble();
                var u2 = rng.NextDouble();
                var r = Math.Sqrt(-2.0 * Math.Log(u1));
                out_[i] = (Single)(r * Math.Cos(2 * Math.PI * u2));
                if (i + 1 < count)
                {
                    out_[i + 1] = (Single)(r * Math.Sin(2 * Math.PI * u2));
                }
            }

            return out_;
        }

        public void Dispose()
        {
            this._text.Dispose();
            this._sound.Dispose();
            this._decoder.Dispose();
        }
    }
}
