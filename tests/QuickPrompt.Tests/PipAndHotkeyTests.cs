namespace QuickPrompt.Tests
{
    using System.Text.Json;

    using QuickPrompt.Core;

    using Xunit;

    public class PipAndHotkeyTests
    {
        [Theory]
        [InlineData("alt+shift+Space", true)]
        [InlineData("cmd+KeyK", true)]
        [InlineData("ctrl+alt+F5", true)]
        [InlineData("shift+KeyK", false)]   // needs cmd, ctrl or alt
        [InlineData("KeyK", false)]
        [InlineData("alt+", false)]
        [InlineData("alt+shift+Space; rm -rf", false)]
        public void Hotkey_format_is_checked(string spec, bool valid)
        {
            Assert.Equal(valid, QuickPromptSettings.IsValidHotkey(spec));
        }

        [Fact]
        public void Pip_shows_only_while_keep_pip_is_on_and_show_turns_it_back_on()
        {
            var dir = TempDir.Create();
            var opened = new List<string>();
            using var manager = new ChatManager(dir, new FakeHost(), new IAiProvider[] { new FakeProvider(ProviderIds.Claude), new FakeProvider(ProviderIds.Codex) }, new IAiProvider[0]);
            manager.Launcher.OverrideOpen = url => opened.Add(url);
            manager.Start();

            Assert.True(manager.ShowPip(false));
            manager.SaveSettings(new Dictionary<string, string> { ["keepPip"] = "false", ["hotkey"] = "cmd+KeyJ" });
            Assert.False(manager.ShowPip(false));
            Assert.Equal("cmd+KeyJ", manager.Settings.Hotkey);

            Assert.True(manager.ShowPip(true));
            Assert.True(manager.Settings.KeepPip);
            Assert.Equal(2, opened.Count);
            using var doc = JsonDocument.Parse(manager.GetSettingsJson());
            Assert.True(doc.RootElement.GetProperty("keepPip").GetBoolean());
            Assert.Equal("cmd+KeyJ", doc.RootElement.GetProperty("hotkey").GetString());
        }
    }
}
