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

        [Fact]
        public async Task A_session_a_window_is_watching_is_kept_overnight_and_an_idle_one_is_forgotten()
        {
            var dir = TempDir.Create();
            using var manager = new ChatManager(dir, new FakeHost(), new IAiProvider[] { new FakeProvider(ProviderIds.Claude), new FakeProvider(ProviderIds.Codex) }, new IAiProvider[0]);
            manager.Launcher.OverrideOpen = _ => { };
            var pip = manager.CreateSession(ProviderIds.Claude, "", "");
            var idle = manager.CreateSession(ProviderIds.Claude, "", "");

            // Pip's page is waiting on the long-poll (as it does all night).
            using var stop = new CancellationTokenSource();
            var poll = pip.WaitForChangeAsync(pip.Version, TimeSpan.FromMinutes(5), stop.Token);
            manager.RemoveStaleSessions(DateTime.UtcNow.AddHours(8));
            Assert.True(manager.TryGetSession(pip.Token, out _));
            Assert.False(manager.TryGetSession(idle.Token, out _));

            // Once the window is gone and nothing has looked at it for three hours, it is forgotten too.
            stop.Cancel();
            await poll;
            manager.RemoveStaleSessions(DateTime.UtcNow.AddHours(2));
            Assert.True(manager.TryGetSession(pip.Token, out _));
            manager.RemoveStaleSessions(DateTime.UtcNow.AddHours(4));
            Assert.False(manager.TryGetSession(pip.Token, out _));
        }
    }
}
