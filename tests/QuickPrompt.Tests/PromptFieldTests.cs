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
    }
}
