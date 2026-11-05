namespace QuickPrompt.Tests
{
    using QuickPrompt.Core;

    using Xunit;

    public class PromptFieldTests
    {
        [Fact]
        public void FindsFieldsInOrderSkippingTextTokensAndDuplicates()
        {
            var fields = PromptBuilder.GetParameters("Write a {tone:friendly| formal |short} reply in {language} to {text}. Keep {tone} and {Language}.");
            Assert.Equal(new[] { "tone", "language" }, fields.Select(f => f.Name));
            Assert.Equal(new[] { "friendly", "formal", "short" }, fields[0].Options);
            Assert.Empty(fields[1].Options);
        }

        [Fact]
        public void IgnoresJsonLikeBraces()
        {
            Assert.Empty(PromptBuilder.GetParameters("Return {\"a\": 1} for {text}"));
        }

        [Fact]
        public void FillsValuesAndDefaultsListsToFirstOption()
        {
            var filled = PromptBuilder.FillParameters("A {tone:friendly|formal} note in {language} about {text}",
                new Dictionary<string, string> { ["Language"] = "Turkish" });
            Assert.Equal("A friendly note in Turkish about {text}", filled);
            Assert.Equal("A friendly note in Turkish about cats", PromptBuilder.Build(filled, "cats"));
        }

        [Fact]
        public void CustomActionsAreSavedEditedAndDeleted()
        {
            var dir = TempDir.Create();
            var catalog = new PresetCatalog(Path.Combine(dir, "presets.json"));
            var saved = catalog.Save(new SavedPreset { Title = "Reply", Instruction = "Reply {tone:short|long}: {text}", Input = "clipboard" });
            Assert.True(saved.Custom);
            Assert.Equal("clipboard", saved.Input);
            Assert.Equal("Reply", catalog.List()[0].Title);

            // Editing a built-in makes a copy that replaces it in the lists.
            var builtIn = catalog.ListEditable().First(p => p.BuiltIn && p.Title == "Summarize");
            catalog.Save(new SavedPreset { Id = builtIn.Id, Title = "Summarize", Instruction = "Summarize in {count} bullets: {text}" });
            var summaries = catalog.ListEditable().Where(p => p.Title == "Summarize").ToList();
            Assert.Single(summaries);
            Assert.False(summaries[0].BuiltIn);

            catalog.Delete(saved.Id);
            Assert.DoesNotContain(catalog.ListEditable(), p => p.Title == "Reply");
        }

        [Fact]
        public void Templates_switch_off_export_and_import_without_overwriting()
        {
            var dir = TempDir.Create();
            using var manager = new ChatManager(dir, new FakeHost(), new IAiProvider[] { new FakeProvider(ProviderIds.Claude), new FakeProvider(ProviderIds.Codex) }, new IAiProvider[0]);
            manager.SaveAction(new Dictionary<string, string> { ["title"] = "Reply", ["instruction"] = "Reply {tone:short|long}: {$selected}" });
            var reply = System.Text.Json.JsonDocument.Parse(manager.GetActionsJson()).RootElement.EnumerateArray().First(a => a.GetProperty("title").GetString() == "Reply").GetProperty("id").GetString();
            var summarize = System.Text.Json.JsonDocument.Parse(manager.GetActionsJson()).RootElement.EnumerateArray().First(a => a.GetProperty("title").GetString() == "Summarize").GetProperty("id").GetString();

            // Off: gone from Pip's list, still in the editor (a built-in becomes a copy that remembers it).
            manager.SetActionEnabled(reply, false);
            manager.SetActionEnabled(summarize, false);
            var offered = System.Text.Json.JsonDocument.Parse(manager.GetPresetsJson()).RootElement.EnumerateArray().Select(p => p.GetProperty("title").GetString()).ToList();
            Assert.DoesNotContain("Reply", offered);
            Assert.DoesNotContain("Summarize", offered);
            var editor = System.Text.Json.JsonDocument.Parse(manager.GetActionsJson()).RootElement.EnumerateArray().ToList();
            Assert.False(editor.First(a => a.GetProperty("title").GetString() == "Reply").GetProperty("enabled").GetBoolean());

            var file = manager.ExportActions(new[] { reply });
            Assert.Contains("\"quickPromptTemplates\": 2", file);

            // Into another computer: added; again: skipped; a changed prompt with the same title: "(2)".
            var other = new ChatManager(TempDir.Create(), new FakeHost(), new IAiProvider[] { new FakeProvider(ProviderIds.Claude), new FakeProvider(ProviderIds.Codex) }, new IAiProvider[0]);
            using var first = System.Text.Json.JsonDocument.Parse(other.ImportActions(file));
            Assert.Equal(1, first.RootElement.GetProperty("added").GetInt32());
            using var again = System.Text.Json.JsonDocument.Parse(other.ImportActions(file));
            Assert.Equal(1, again.RootElement.GetProperty("skipped").GetInt32());
            using var changed = System.Text.Json.JsonDocument.Parse(other.ImportActions(file.Replace("Reply {tone", "Answer {tone")));
            Assert.Equal(1, changed.RootElement.GetProperty("added").GetInt32());
            var titles = System.Text.Json.JsonDocument.Parse(other.GetActionsJson()).RootElement.EnumerateArray().Select(a => a.GetProperty("title").GetString()).ToList();
            Assert.Contains("Reply", titles);
            Assert.Contains("Reply (2)", titles);
            Assert.Contains("error", other.ImportActions("{\"nope\":1}"));
            other.Dispose();
        }
    }
}
