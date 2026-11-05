namespace QuickPrompt.Tests
{
    using System;
    using System.IO;
    using System.Text.Json;
    using QuickPrompt.Voice;
    using Xunit;

    public sealed class VoiceTests
    {
        [Fact]
        public void English_words_come_from_the_lexicon_with_inflections_and_numbers_as_words()
        {
            var g2p = new EnglishG2P();
            Assert.Equal("həlˈO!", g2p.Phonemes("Hello!"));
            Assert.Equal("tˈOkᵊn", g2p.Word("token"));
            Assert.EndsWith("z", g2p.Word("expires"));            // expire + s
            Assert.Equal("ˌɛksˌɛmˈɛl", g2p.Word("XML"));                    // in the lexicon
            Assert.Equal("zˈi kjˈu kˈA", g2p.Word("ZQK"));                  // capitals it lacks: letter by letter
            Assert.Equal("four hundred one", EnglishNumbers.Spell("401"));
            Assert.Equal("nineteen ninety nine", EnglishNumbers.Spell("1999"));
            Assert.Equal("three point five percent", EnglishNumbers.Spell("3.5%"));
            Assert.Equal("twenty first", EnglishNumbers.Spell("21st"));
        }

        [Fact]
        public void Every_phoneme_the_lexicon_gives_is_in_Kokoros_vocabulary()
        {
            var vocab = KokoroVoice.LoadVocab();
            var g2p = new EnglishG2P();
            foreach (var text in new[] { "The quick brown fox jumps over the lazy dog.", "Kubernetes schedules pods; the Service Layer answers requests.", "It's 3:45 and I've got 2 tickets." })
            {
                var missing = g2p.Phonemes(text).Replace(" ", "");
                foreach (var c in missing)
                {
                    Assert.True(vocab.ContainsKey(c), $"'{c}' is not in Kokoro's vocabulary ({text})");
                }
            }
        }

        [Fact]
        public void Turkish_text_is_lowercased_the_Turkish_way_and_cut_into_pieces()
        {
            var vocab = new System.Collections.Generic.HashSet<char>(" !\"%&'(),-./:;?abcdefghijklmnopqrstuvwxyzçöüğış");
            Assert.Equal("ıspanak ile istanbul, çok güzel.", EmaVoice.Alphabet("ISPANAK İle İstanbul, Çok Güzel.", vocab));
            Assert.Equal("cafe", EmaVoice.Alphabet("Café", vocab));
            var pieces = EmaVoice.Chunk("birinci cümle", 1.0);
            Assert.Equal("birinci cümle.", Assert.Single(pieces).Piece);
            var long_ = String.Join(" ", System.Linq.Enumerable.Repeat("uzun bir cümle daha geliyor.", 20));
            var parts = EmaVoice.Chunk(long_, 1.0);
            Assert.True(parts.Count > 1);
            Assert.All(parts, p => Assert.True(p.Piece.Length <= 251));
            Assert.Equal(0.0, parts[^1].Pause);
        }

        /// <summary>normalizer-tr through native/normalizer, when the library is built (cargo build --release).</summary>
        [Fact]
        public void Turkish_numbers_and_times_are_spoken_when_the_normalizer_is_there()
        {
            if (!TurkishNormalizer.Available)
            {
                return;
            }

            var spoken = TurkishNormalizer.Normalize("Toplantıyı 12 Kasım saat 14.30'a aldım.");
            Assert.Contains("on iki", spoken);
            Assert.Contains("on dört", spoken);
        }

        /// <summary>With the models in QP_VOICE_DATA (a data folder holding models/voice), both voices speak.</summary>
        [Fact]
        public void Both_voices_speak_when_the_models_are_there()
        {
            var data = Environment.GetEnvironmentVariable("QP_VOICE_DATA");
            if (String.IsNullOrEmpty(data) || !Directory.Exists(Path.Combine(data, "models", "voice")) || !LocalVoices.Supported)
            {
                return;
            }

            using var voices = new LocalVoices(data, (m, e) => throw new Exception(m, e));
            Assert.Equal("ready", JsonDocument.Parse(voices.StatusJson()).RootElement.GetProperty("state").GetString());
            var tr = voices.Speak("tr", "Merhaba, cevapları ben okuyacağım. Sörvis Leyır bağlantısı hazır.", "", 1.0);
            var en = voices.Speak("en", "Hello, I will read the answers to you.", "af_heart", 1.0);
            Assert.True(tr.Length > 48000 * 2, "Turkish audio too short");
            Assert.True(en.Length > 24000 * 2, "English audio too short");
            var outDir = Environment.GetEnvironmentVariable("QP_VOICE_OUT");
            if (!String.IsNullOrEmpty(outDir))
            {
                File.WriteAllBytes(Path.Combine(outDir, "cs-tr.wav"), tr);
                File.WriteAllBytes(Path.Combine(outDir, "cs-en.wav"), en);
            }
        }
    }
}
