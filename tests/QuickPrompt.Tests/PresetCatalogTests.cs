namespace QuickPrompt.Tests
{
    using System.Text.Json;

    using QuickPrompt.Core;

    using Xunit;

    public class PresetCatalogTests : IDisposable
    {
        private readonly string _dir = TempDir.Create();
        private readonly FakeHost _host = new FakeHost();
        private readonly FakeProvider _claude = new FakeProvider(ProviderIds.Claude);
        private readonly FakeProvider _codexApi = new FakeProvider(ProviderIds.Codex);
        private readonly ChatManager _manager;

        public PresetCatalogTests()
        {
            this._manager = new ChatManager(this._dir, this._host,
                new IAiProvider[] { this._claude, new FakeProvider(ProviderIds.Codex) },
                new IAiProvider[] { new FakeProvider(ProviderIds.Claude), this._codexApi });
            this._manager.Launcher.OverrideOpen = _ => { };
            this._manager.CopyDelayMs = 10;
            this._manager.Start();
        }

        public void Dispose() => this._manager.Dispose();

        private List<JsonElement> Presets() => JsonDocument.Parse(this._manager.GetPresetsJson()).RootElement.EnumerateArray().ToList();

        [Fact]
        public async Task A_templates_agent_and_its_setup_reach_the_provider()
        {
            var agents = JsonDocument.Parse(this._manager.SaveAgent(new Dictionary<string, string>
            {
                ["name"] = "Researcher", ["provider"] = "claude", ["connection"] = "cli", ["model"] = "claude-sonnet-5-5",
                ["instruction"] = "Cite your sources.",
                ["setup"] = "{\"enabled\":true,\"web\":false,\"disabledServers\":[\"mail\"]}",
            })).RootElement;
            var agentId = agents.GetProperty("agents").EnumerateArray().First(a => a.GetProperty("name").GetString() == "Researcher").GetProperty("id").GetString();
            var json = this._manager.SaveAction(new Dictionary<string, string>
            {
                ["title"] = "Lookup", ["instruction"] = "Look up {text}", ["agent"] = agentId,
            });
            var id = JsonDocument.Parse(json).RootElement.EnumerateArray().First(a => a.GetProperty("title").GetString() == "Lookup").GetProperty("id").GetString();

            var session = this._manager.CreateSession(ProviderIds.Claude, "", "");
            Assert.True(this._manager.UsePreset(session, id));
            Assert.True(session.StartTurnFromPopup("Acme Ltd", ""));
            await session.CurrentTurn;

            var request = this._claude.Requests.Single();
            Assert.True(request.Setup.Enabled);
            Assert.False(request.Setup.Web);
            Assert.Equal(new[] { "mail" }, request.Setup.DisabledServers);
            Assert.Equal("claude-sonnet-5-5", request.Model);
            Assert.EndsWith("Cite your sources.", request.SystemPrompt);
            using var state = JsonDocument.Parse(session.ToJson(true));
            Assert.Equal("Researcher", state.RootElement.GetProperty("agent").GetProperty("name").GetString());
        }

        [Fact]
        public void Built_ins_are_offered_before_anything_is_used()
        {
            var titles = Presets().Select(p => p.GetProperty("title").GetString()).ToList();
            Assert.Equal(new[] { "Summarize", "Translate", "Fix grammar", "Explain" }, titles);
        }

        [Fact]
        public async Task Ring_presets_are_remembered_most_recent_first_and_replace_built_ins_with_the_same_title()
        {
            this._host.Selection = "x";
            await this._manager.ExecutePresetAsync(new Preset { Title = "Translate", Instruction = "To German: {text}", ProviderId = ProviderIds.Codex, Connection = ConnectionModes.Api, Flow = FlowModes.Copy });
            await this._manager.ExecutePresetAsync(new Preset { Title = "Mail", Instruction = "Make it an email: {text}", Flow = FlowModes.Copy });
            await this._manager.ExecutePresetAsync(new Preset { Title = "Mail", Instruction = "Make it an email: {text}", Flow = FlowModes.Copy });

            var list = Presets();
            Assert.Equal(new[] { "Mail", "Translate", "Summarize", "Fix grammar", "Explain" }, list.Select(p => p.GetProperty("title").GetString()));
            Assert.Equal("ChatGPT", list[1].GetProperty("providerName").GetString());
            Assert.Equal("api", list[1].GetProperty("connection").GetString());
            Assert.False(list[1].GetProperty("builtIn").GetBoolean());
        }

        [Fact]
        public async Task Using_a_preset_turns_the_popup_into_it()
        {
            this._host.Selection = "x";
            await this._manager.ExecutePresetAsync(new Preset { Title = "Translate", Instruction = "To German: {text}", ProviderId = ProviderIds.Codex, Connection = ConnectionModes.Api, Flow = FlowModes.Copy });
            var id = Presets()[0].GetProperty("id").GetString();

            var popup = this._manager.CreateSession(ProviderIds.Claude, "", "");
            Assert.True(this._manager.UsePreset(popup, id));
            Assert.Same(this._codexApi, popup.Provider);
            using var state = JsonDocument.Parse(popup.ToJson(true));
            Assert.True(state.RootElement.GetProperty("pasteMode").GetBoolean());
            Assert.Equal("Translate", state.RootElement.GetProperty("title").GetString());

            Assert.True(popup.StartTurnFromPopup("Guten Tag?", ""));
            await popup.CurrentTurn;
            Assert.Equal("To German: Guten Tag?", this._codexApi.Requests.Last().Message);
            Assert.False(this._manager.UsePreset(popup, "nope"));
        }
    }
}
