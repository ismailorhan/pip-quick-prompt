namespace QuickPrompt.Tests
{
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Text.Json;
    using QuickPrompt.Core;
    using Xunit;

    public sealed class ReadAloudTests
    {
        /// <summary>The embedded autolex.js over the embedded table respells English words in Turkish
        /// (the readings the owner approved by ear). Needs Node.js; skipped without it.</summary>
        [Fact]
        public void Autolex_respells_English_words_in_Turkish_text()
        {
            var dir = TempDir.Create();
            File.WriteAllBytes(Path.Combine(dir, "autolex.js"), VoiceAssets.Script);
            File.WriteAllBytes(Path.Combine(dir, "table.json"), VoiceAssets.TurkishTable);
            var cases = new Dictionary<string, string>
            {
                ["Service layer uygulamasını kurdum, controller'lar artık doğrudan veritabanına gitmiyor."] =
                    "Sörvis leyır uygulamasını kurdum, kontrolırlar artık doğrudan veritabanına gitmiyor.",
                ["Bugün iki bug'ı kapattık, üçüncüsü için issue açtım."] = "Bugün iki bagı kapattık, üçüncüsü için işu açtım.",
                ["API endpoint'i 401 döndürüyor; token'ın süresi dolmuş olabilir."] = "Ey pi ay endpoynti 401 döndürüyor; tokının süresi dolmuş olabilir.",
                ["Commitledim ama push etmeyi unuttum, CI kırmızı yandı."] = "Komitledim ama puş etmeyi unuttum, si ay kırmızı yandı.",
                ["Test ortamında set ettiğimiz değerler farklı çıktı."] = "Test ortamında set ettiğimiz değerler farklı çıktı.",
                ["Katılımcılar Ayşe, Mehmet ve SAP Business One ekibi."] = "Katılımcılar Ayşe, Mehmet ve es ey pi Biznıs Van ekibi.",
            };
            File.WriteAllText(Path.Combine(dir, "cases.json"), JsonSerializer.Serialize(cases));
            File.WriteAllText(Path.Combine(dir, "run.js"),
                "const fs = require('fs'); const { AutoLex } = require('./autolex.js');\n"
                + "const lex = new AutoLex(JSON.parse(fs.readFileSync(__dirname + '/table.json', 'utf8')));\n"
                + "const cases = JSON.parse(fs.readFileSync(__dirname + '/cases.json', 'utf8'));\n"
                + "let bad = 0; for (const [a, want] of Object.entries(cases)) { const got = lex.apply(a); if (got !== want) { bad++; console.error('want ' + want + '\\n got  ' + got); } }\n"
                + "process.exit(bad ? 1 : 0);\n");

            Process node;
            try
            {
                node = Process.Start(new ProcessStartInfo("node") { ArgumentList = { Path.Combine(dir, "run.js") }, RedirectStandardError = true, UseShellExecute = false });
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return; // Node.js not installed.
            }

            var stderr = node.StandardError.ReadToEnd();
            node.WaitForExit();
            Assert.True(node.ExitCode == 0, stderr);
        }

        [Fact]
        public void Read_aloud_settings_are_saved_and_cleaned()
        {
            var manager = new ChatManager(TempDir.Create(), new FakeHost(), new IAiProvider[] { new FakeProvider(ProviderIds.Claude) });
            using (manager)
            {
                manager.SaveSettings(new Dictionary<string, string>
                {
                    ["readAloud"] = "true", ["readRate"] = "900", ["readVoices"] = "tr=Yelda;en=Samantha (Enhanced);xx1=Bad;de=",
                });
                using var json = JsonDocument.Parse(manager.GetSettingsJson());
                var root = json.RootElement;
                Assert.True(root.GetProperty("readAloud").GetBoolean());
                Assert.Equal(200, root.GetProperty("readRate").GetInt32());
                Assert.Equal("tr=Yelda;en=Samantha (Enhanced)", root.GetProperty("readVoices").GetString());

                var session = manager.CreateSession(null, "");
                using var state = JsonDocument.Parse(session.ToJson(true));
                Assert.True(state.RootElement.GetProperty("readAloud").GetBoolean());
            }
        }
    }
}
