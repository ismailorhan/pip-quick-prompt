namespace QuickPrompt.Voice
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;

    /// <summary>
    /// English text to Kokoro's phonemes without eSpeak: misaki's US lexicons (Apache-2.0) and CMUdict (BSD-2),
    /// merged by tools/voice/build_en_lexicon.py; then -s / -ed / -ing on a known stem; then two known words
    /// joined (keyboard = key + board); then letter by letter. Numbers, %, $ and &amp; are said as words first.
    /// </summary>
    public sealed class EnglishG2P
    {
        private static readonly Lazy<Dictionary<String, String>> Shared = new Lazy<Dictionary<String, String>>(LoadLexicon);

        private static readonly Dictionary<Char, String> Letters = new Dictionary<Char, String>
        {
            ['a'] = "ˈA", ['b'] = "bˈi", ['c'] = "sˈi", ['d'] = "dˈi", ['e'] = "ˈi", ['f'] = "ˈɛf", ['g'] = "ʤˈi", ['h'] = "ˈAʧ",
            ['i'] = "ˈI", ['j'] = "ʤˈA", ['k'] = "kˈA", ['l'] = "ˈɛl", ['m'] = "ˈɛm", ['n'] = "ˈɛn", ['o'] = "ˈO", ['p'] = "pˈi",
            ['q'] = "kjˈu", ['r'] = "ˈɑɹ", ['s'] = "ˈɛs", ['t'] = "tˈi", ['u'] = "jˈu", ['v'] = "vˈi", ['w'] = "dˈʌbᵊlju",
            ['x'] = "ˈɛks", ['y'] = "wˈI", ['z'] = "zˈi",
        };

        private const String Punctuation = ";:,.!?—…\"()“”";
        private static readonly Regex Token = new Regex(@"[A-Za-z]+(?:['’][A-Za-z]+)*|[;:,.!?—…""()“”]");

        private readonly Dictionary<String, String> _lex;

        public EnglishG2P()
        {
            this._lex = Shared.Value;
        }

        public String Phonemes(String text)
        {
            var sb = new StringBuilder();
            foreach (Match m in Token.Matches(EnglishNumbers.Spell(text)))
            {
                var t = m.Value.Replace('’', '\'');
                if (t.Length == 1 && Punctuation.IndexOf(t[0]) >= 0)
                {
                    sb.Append(t);
                    continue;
                }

                if (sb.Length > 0)
                {
                    sb.Append(' ');
                }

                sb.Append(this.Word(t));
            }

            return sb.ToString();
        }

        public String Word(String word)
        {
            if (this.Entry(word) is { } known)
            {
                return known;
            }

            // An abbreviation in capitals, letter by letter (URL, CI).
            if (word.Length <= 5 && word.All(Char.IsUpper))
            {
                return Spell(word);
            }

            var low = word.ToLowerInvariant();
            if (this.Inflected(low) is { } inflected)
            {
                return inflected;
            }

            // Two known words written together.
            for (var cut = low.Length - 3; cut >= 3; cut--)
            {
                if (this.Entry(low.Substring(0, cut)) is { } a && (this.Entry(low.Substring(cut)) ?? this.Inflected(low.Substring(cut))) is { } b)
                {
                    return a + b;
                }
            }

            return Spell(low);
        }

        private String Entry(String word)
        {
            if (this._lex.TryGetValue(word, out var p))
            {
                return p;
            }

            var low = word.ToLowerInvariant();
            if (this._lex.TryGetValue(low, out p))
            {
                return p;
            }

            var cap = low.Length > 0 ? Char.ToUpperInvariant(low[0]) + low.Substring(1) : low;
            return this._lex.TryGetValue(cap, out p) ? p : null;
        }

        /// <summary>-s, -es, -ed, -ing on a known stem, as misaki does.</summary>
        private String Inflected(String low)
        {
            String Try(String stem) => stem.Length >= 2 ? this.Entry(stem) : null;
            if (low.EndsWith("'s") && Try(low[..^2]) is { } own)
            {
                return own + Plural(own);
            }

            if (low.EndsWith("ies") && Try(low[..^3] + "y") is { } y)
            {
                return y + "z";
            }

            if (low.EndsWith("es") && Try(low[..^2]) is { } es && "szʃʒʧʤ".IndexOf(es[^1]) >= 0)
            {
                return es + "ɪz";
            }

            if (low.EndsWith('s') && !low.EndsWith("ss") && Try(low[..^1]) is { } s)
            {
                return s + Plural(s);
            }

            if (low.EndsWith("ed"))
            {
                foreach (var stem in new[] { low[..^2], low[..^1], low.Length > 4 && low[^3] == low[^4] ? low[..^3] : null })
                {
                    if (stem != null && Try(stem) is { } ed)
                    {
                        return ed + ("td".IndexOf(ed[^1]) >= 0 ? "ᵻd" : "pkfθsʃʧ".IndexOf(ed[^1]) >= 0 ? "t" : "d");
                    }
                }
            }

            if (low.EndsWith("ing"))
            {
                foreach (var stem in new[] { low[..^3], low[..^3] + "e", low.Length > 5 && low[^4] == low[^5] ? low[..^4] : null })
                {
                    if (stem != null && Try(stem) is { } ing)
                    {
                        return ing + "ɪŋ";
                    }
                }
            }

            return null;
        }

        private static String Plural(String p) => "szʃʒʧʤ".IndexOf(p[^1]) >= 0 ? "ɪz" : "ptkfθ".IndexOf(p[^1]) >= 0 ? "s" : "z";

        private static String Spell(String word) =>
            String.Join(" ", word.ToLowerInvariant().Where(Letters.ContainsKey).Select(c => Letters[c]));

        private static Dictionary<String, String> LoadLexicon()
        {
            var lex = new Dictionary<String, String>(260_000, StringComparer.Ordinal);
            using var stream = typeof(EnglishG2P).Assembly.GetManifestResourceStream("QuickPrompt.Voice.en-lexicon.txt.gz")
                ?? throw new InvalidOperationException("Missing English lexicon");
            using var gz = new GZipStream(stream, CompressionMode.Decompress);
            using var reader = new StreamReader(gz, Encoding.UTF8);
            String line;
            while ((line = reader.ReadLine()) != null)
            {
                var tab = line.IndexOf('\t');
                if (tab > 0)
                {
                    lex[line.Substring(0, tab)] = line.Substring(tab + 1);
                }
            }

            return lex;
        }
    }

    /// <summary>Numbers and a few symbols as English words, before the lexicon: "401" -> "four hundred one".</summary>
    public static class EnglishNumbers
    {
        private static readonly String[] Ones = "zero one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen sixteen seventeen eighteen nineteen".Split(' ');
        private static readonly String[] Tens = "_ _ twenty thirty forty fifty sixty seventy eighty ninety".Split(' ');

        public static String Spell(String text)
        {
            text = Regex.Replace(text, @"\$(\d+(?:\.\d+)?)", "$1 dollars");
            text = Regex.Replace(text, @"(\d+(?:\.\d+)?)\s?%", "$1 percent");
            text = text.Replace("&", " and ").Replace("+", " plus ").Replace("@", " at ");
            text = Regex.Replace(text, @"(\d),(\d{3})", "$1$2");
            text = Regex.Replace(text, @"\b(\d{1,2}):(\d{2})\b", m => Number(Int64.Parse(m.Groups[1].Value)) + " " + (m.Groups[2].Value == "00" ? "o'clock" : Minutes(m.Groups[2].Value)));
            text = Regex.Replace(text, @"\b(\d+)(st|nd|rd|th)\b", m => Ordinal(Int64.Parse(m.Groups[1].Value)));
            text = Regex.Replace(text, @"\b(\d+)\.(\d+)\b", m => Number(Int64.Parse(m.Groups[1].Value)) + " point " + String.Join(" ", m.Groups[2].Value.Select(c => Ones[c - '0'])));
            text = Regex.Replace(text, @"\b(1[1-9]|20)(\d{2})\b(?!\d)", m => Year(m.Value));
            text = Regex.Replace(text, @"\d+", m => m.Value.Length > 15 ? String.Join(" ", m.Value.Select(c => Ones[c - '0'])) : Number(Int64.Parse(m.Value)));
            return text;
        }

        private static String Minutes(String mm) => mm[0] == '0' ? "oh " + Ones[mm[1] - '0'] : Number(Int64.Parse(mm));

        private static String Year(String y)
        {
            var n = Int32.Parse(y, CultureInfo.InvariantCulture);
            if (n >= 2000 && n < 2010)
            {
                return Number(n);
            }

            var hi = n / 100;
            var lo = n % 100;
            return Number(hi) + " " + (lo == 0 ? "hundred" : lo < 10 ? "oh " + Ones[lo] : Number(lo));
        }

        public static String Number(Int64 n)
        {
            if (n < 20)
            {
                return Ones[n];
            }

            if (n < 100)
            {
                return Tens[n / 10] + (n % 10 > 0 ? " " + Ones[n % 10] : "");
            }

            foreach (var (size, name) in new[] { (1_000_000_000_000L, "trillion"), (1_000_000_000L, "billion"), (1_000_000L, "million"), (1000L, "thousand"), (100L, "hundred") })
            {
                if (n >= size)
                {
                    return Number(n / size) + " " + name + (n % size > 0 ? " " + Number(n % size) : "");
                }
            }

            return n.ToString(CultureInfo.InvariantCulture);
        }

        private static String Ordinal(Int64 n)
        {
            var words = Number(n);
            var last = words.Split(' ')[^1];
            var map = new Dictionary<String, String> { ["one"] = "first", ["two"] = "second", ["three"] = "third", ["five"] = "fifth", ["eight"] = "eighth", ["nine"] = "ninth", ["twelve"] = "twelfth" };
            var ord = map.TryGetValue(last, out var o) ? o : last.EndsWith('y') ? last[..^1] + "ieth" : last + "th";
            return words.Substring(0, words.Length - last.Length) + ord;
        }
    }
}
