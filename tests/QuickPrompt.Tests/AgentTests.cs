namespace QuickPrompt.Tests
{
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;
    using QuickPrompt.Core;
    using Xunit;

    public sealed class AgentTests
    {
        private static (ChatManager Manager, FakeProvider ClaudeCli, FakeProvider CodexCli, FakeProvider ClaudeApi, FakeProvider CodexApi) Create(String dir)
        {
            var claudeCli = new FakeProvider(ProviderIds.Claude);
            var codexCli = new FakeProvider(ProviderIds.Codex);
            var claudeApi = new FakeProvider(ProviderIds.Claude) { Answer = r => "API: " + r.Message };
            var codexApi = new FakeProvider(ProviderIds.Codex) { Answer = r => "API: " + r.Message };
            var manager = new ChatManager(dir, new FakeHost(), new IAiProvider[] { claudeCli, codexCli }, new IAiProvider[] { claudeApi, codexApi });
            manager.Launcher.OverrideOpen = _ => { };
            return (manager, claudeCli, codexCli, claudeApi, codexApi);
        }

        private static JsonElement Agents(ChatManager manager) => JsonDocument.Parse(manager.GetAgentsJson()).RootElement;

        private static String IdOf(JsonElement agents, String name) =>
            agents.GetProperty("agents").EnumerateArray().First(a => a.GetProperty("name").GetString() == name).GetProperty("id").GetString();

        [Fact]
        public void The_older_settings_become_two_agents_with_Claude_as_the_default()
        {
            var dir = TempDir.Create();
            File.WriteAllText(Path.Combine(dir, "settings.json"),
                "{\"ClaudeConnection\":\"api\",\"ClaudeApiModel\":\"claude-sonnet-5-5\",\"ClaudeApiEffort\":\"high\",\"ChatGptConnection\":\"cli\","
                + "\"ClaudeSetup\":{\"Enabled\":true,\"Web\":false}}");
            var (manager, _, _, _, _) = Create(dir);
            using (manager)
            {
                var agents = Agents(manager);
                Assert.Equal("claude", agents.GetProperty("defaultId").GetString());
                var list = agents.GetProperty("agents").EnumerateArray().ToList();
                Assert.Equal(new[] { "Claude", "ChatGPT" }, list.Select(a => a.GetProperty("name").GetString()));
                Assert.Equal("api", list[0].GetProperty("connection").GetString());
                Assert.Equal("claude-sonnet-5-5", list[0].GetProperty("model").GetString());
                Assert.Equal("high", list[0].GetProperty("effort").GetString());
                Assert.True(list[0].GetProperty("setup").GetProperty("enabled").GetBoolean());
                Assert.False(list[0].GetProperty("setup").GetProperty("web").GetBoolean());
                Assert.Equal("codex", list[1].GetProperty("provider").GetString());
                Assert.Equal("", list[1].GetProperty("model").GetString());
            }
        }

        [Fact]
        public void Templates_from_before_agents_point_at_the_matching_agent_or_a_new_one()
        {
            var dir = TempDir.Create();
            File.WriteAllText(Path.Combine(dir, "actions.json"), JsonSerializer.Serialize(new[]
            {
                new { Id = "a1", Title = "Plain", Instruction = "Fix: {$selected}", ProviderId = "claude", Connection = "default", Model = "", Custom = true, Enabled = true },
                new { Id = "a2", Title = "GPT", Instruction = "Fix: {$selected}", ProviderId = "codex", Connection = "default", Model = "", Custom = true, Enabled = true },
                new { Id = "a3", Title = "Fast", Instruction = "Fix: {$selected}", ProviderId = "claude", Connection = "api", Model = "claude-haiku-5-5", Custom = true, Enabled = true },
                new { Id = "a4", Title = "Fast too", Instruction = "Fix: {$selected}", ProviderId = "claude", Connection = "api", Model = "claude-haiku-5-5", Custom = true, Enabled = true },
            }));
            var (manager, _, _, _, _) = Create(dir);
            using (manager)
            {
                var actions = JsonDocument.Parse(manager.GetActionsJson()).RootElement.EnumerateArray().ToDictionary(a => a.GetProperty("id").GetString());
                Assert.Equal("", actions["a1"].GetProperty("agent").GetString());          // the default agent
                Assert.Equal("chatgpt", actions["a2"].GetProperty("agent").GetString());
                var fast = actions["a3"].GetProperty("agent").GetString();
                Assert.StartsWith("g", fast);                                                // a new agent…
                Assert.Equal(fast, actions["a4"].GetProperty("agent").GetString());          // …shared by the same setup

                var made = Agents(manager).GetProperty("agents").EnumerateArray().First(a => a.GetProperty("id").GetString() == fast);
                Assert.Equal("api", made.GetProperty("connection").GetString());
                Assert.Equal("claude-haiku-5-5", made.GetProperty("model").GetString());
                Assert.Equal(3, Agents(manager).GetProperty("agents").GetArrayLength());
            }

            // Only once: a second start changes nothing.
            var (again, _, _, _, _) = Create(dir);
            using (again)
            {
                Assert.Equal(3, Agents(again).GetProperty("agents").GetArrayLength());
            }
        }

        [Fact]
        public async Task New_chats_use_the_default_agent_and_the_chip_switches_it_with_the_conversation_so_far()
        {
            var (manager, claudeCli, codexCli, _, codexApi) = Create(TempDir.Create());
            using (manager)
            {
                var agents = JsonDocument.Parse(manager.SaveAgent(new Dictionary<string, string>
                {
                    ["name"] = "GPT fast", ["provider"] = "codex", ["connection"] = "api", ["model"] = "gpt-5-mini", ["makeDefault"] = "true",
                })).RootElement;
                var gpt = IdOf(agents, "GPT fast");
                Assert.Equal(gpt, agents.GetProperty("defaultId").GetString());

                var session = manager.CreateSession(null, "");
                Assert.Same(codexApi, session.Provider);
                Assert.True(session.StartTurn("hello", ""));
                await session.CurrentTurn;
                Assert.Equal("gpt-5-mini", codexApi.Requests.Last().Model);

                // The chip: the next message goes to Claude, with what was said so far.
                Assert.True(manager.SetSessionAgent(session, "claude"));
                Assert.Same(claudeCli, session.Provider);
                Assert.True(session.StartTurn("and now?", ""));
                await session.CurrentTurn;
                Assert.Contains("hello", claudeCli.Requests.Last().Prompt);
                using var state = JsonDocument.Parse(session.ToJson(true));
                Assert.Equal("Claude", state.RootElement.GetProperty("agent").GetProperty("name").GetString());

                // New Chat with an agent from Pip's menu; without one, the default agent.
                Assert.True(manager.ResetSession(session, "chatgpt"));
                Assert.Same(codexCli, session.Provider);
                Assert.True(manager.ResetSession(session));
                Assert.Same(codexApi, session.Provider);
                Assert.False(manager.SetSessionAgent(session, "nope"));
            }
        }

        [Fact]
        public void The_default_agent_cannot_be_deleted_and_a_deleted_agents_templates_use_the_default()
        {
            var (manager, _, _, _, _) = Create(TempDir.Create());
            using (manager)
            {
                var helper = IdOf(JsonDocument.Parse(manager.SaveAgent(new Dictionary<string, string> { ["name"] = "Helper", ["provider"] = "claude" })).RootElement, "Helper");
                manager.SaveAction(new Dictionary<string, string> { ["title"] = "Tidy", ["instruction"] = "Tidy {$selected}", ["agent"] = helper });

                Assert.Contains("error", manager.DeleteAgent("claude"));
                Assert.Contains("already", manager.SaveAgent(new Dictionary<string, string> { ["name"] = "helper", ["provider"] = "codex" }));

                manager.DeleteAgent(helper);
                var tidy = JsonDocument.Parse(manager.GetActionsJson()).RootElement.EnumerateArray().First(a => a.GetProperty("title").GetString() == "Tidy");
                Assert.Equal("", tidy.GetProperty("agent").GetString());
                Assert.Equal("Claude", tidy.GetProperty("agentName").GetString());
            }
        }

        [Fact]
        public void An_exported_templates_agent_comes_along_on_import()
        {
            var (manager, _, _, _, _) = Create(TempDir.Create());
            using (manager)
            {
                var writer = IdOf(JsonDocument.Parse(manager.SaveAgent(new Dictionary<string, string>
                {
                    ["name"] = "Writer", ["provider"] = "claude", ["connection"] = "api", ["model"] = "claude-opus-5-5", ["instruction"] = "Be brief.",
                })).RootElement, "Writer");
                manager.SaveAction(new Dictionary<string, string> { ["title"] = "Mail", ["instruction"] = "Mail: {$selected}", ["agent"] = writer });
                var mail = JsonDocument.Parse(manager.GetActionsJson()).RootElement.EnumerateArray().First(a => a.GetProperty("title").GetString() == "Mail").GetProperty("id").GetString();
                var file = manager.ExportActions(new[] { mail });

                var (other, _, _, _, _) = Create(TempDir.Create());
                using (other)
                {
                    other.ImportActions(file);
                    var agent = Agents(other).GetProperty("agents").EnumerateArray().First(a => a.GetProperty("name").GetString() == "Writer");
                    Assert.Equal("Be brief.", agent.GetProperty("instruction").GetString());
                    var imported = JsonDocument.Parse(other.GetActionsJson()).RootElement.EnumerateArray().First(a => a.GetProperty("title").GetString() == "Mail");
                    Assert.Equal(agent.GetProperty("id").GetString(), imported.GetProperty("agent").GetString());

                    // Imported again: the same agent, not a second one.
                    other.ImportActions(file.Replace("Mail: {", "Post: {"));
                    Assert.Equal(1, Agents(other).GetProperty("agents").EnumerateArray().Count(a => a.GetProperty("name").GetString().StartsWith("Writer")));
                }
            }
        }
    }
}
