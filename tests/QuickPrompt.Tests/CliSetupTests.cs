namespace QuickPrompt.Tests
{
    using System.Text.Json;

    using QuickPrompt.Core;

    using Xunit;

    public class CliSetupTests
    {
        private static CliSetupInventory Inventory()
        {
            var inventory = new CliSetupInventory();
            inventory.Plugins.Add(new CliPluginInfo { Id = "notes@market", EnabledInCli = true });
            inventory.Plugins.Add(new CliPluginInfo { Id = "deploy@market", EnabledInCli = true });
            using var doc = JsonDocument.Parse("{\"crm\":{\"type\":\"http\",\"url\":\"https://crm.example/mcp\"},\"mail\":{\"command\":\"mail-mcp\"}}");
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                inventory.Servers[p.Name] = p.Value.Clone();
            }

            return inventory;
        }

        [Fact]
        public void OffMeansNoToolsAndNoServers()
        {
            Assert.Equal(new[] { "--tools", "", "--strict-mcp-config" }, CliSetup.ClaudeArguments(new CliSetupChoice(), Inventory()));
            var args = ClaudeCliProvider.BuildArguments(new AiRequest());
            Assert.Contains("--strict-mcp-config", args);
            Assert.Equal("", args[args.ToList().IndexOf("--tools") + 1]);
        }

        [Fact]
        public void OnPassesOnlyChosenServersAndSwitchesOffChosenPlugins()
        {
            var choice = new CliSetupChoice { Enabled = true, Web = false, DisabledPlugins = { "deploy@market" }, DisabledServers = { "mail" } };
            var args = CliSetup.ClaudeArguments(choice, Inventory());

            Assert.Equal("Skill,Read,Glob,Grep", args[args.IndexOf("--tools") + 1]);
            var mcp = JsonDocument.Parse(args[args.IndexOf("--mcp-config") + 1]).RootElement.GetProperty("mcpServers");
            Assert.True(mcp.TryGetProperty("crm", out _));
            Assert.False(mcp.TryGetProperty("mail", out _));
            var settings = JsonDocument.Parse(args[args.IndexOf("--settings") + 1]).RootElement.GetProperty("enabledPlugins");
            Assert.False(settings.GetProperty("deploy@market").GetBoolean());
            Assert.False(settings.TryGetProperty("notes@market", out _));
            Assert.Contains("mcp__crm", args);
            Assert.DoesNotContain("mcp__mail", args);
            Assert.DoesNotContain("Bash", string.Join(",", args));
        }

        [Fact]
        public void SkillsSwitchedOffOneByOneAreDenied()
        {
            var inventory = Inventory();
            inventory.Skills.Add(new CliSkillInfo { Name = "fruit", Folder = "/home/me/.claude/skills/fruit" });
            inventory.Skills.Add(new CliSkillInfo { Name = "notes:todo", Folder = "/p/notes/skills/todo", Plugin = "notes@market" });
            var args = CliSetup.ClaudeArguments(new CliSetupChoice { Enabled = true, DisabledSkills = { "fruit" } }, inventory);
            Assert.Contains("--disallowedTools", args);
            Assert.Contains("Skill(fruit)", args);
            Assert.Contains("Read(//home/me/.claude/skills/fruit/**)", args);
            Assert.DoesNotContain("Skill(notes:todo)", args);

            var codex = new CliSetupInventory();
            codex.Skills.Add(new CliSkillInfo { Name = "fruit", Folder = "/home/me/.codex/skills/fruit" });
            var codexArgs = CliSetup.CodexArguments(new CliSetupChoice { Enabled = true, DisabledSkills = { "fruit" } }, codex);
            Assert.Equal(new[] { "-c", "skills.config=[{path=\"/home/me/.codex/skills/fruit/SKILL.md\", enabled=false}]" }, codexArgs);
        }

        [Fact]
        public void SkillsOffDisablesSlashCommands()
        {
            var args = CliSetup.ClaudeArguments(new CliSetupChoice { Enabled = true, Skills = false }, new CliSetupInventory());
            Assert.Contains("--disable-slash-commands", args);
            Assert.Equal("WebSearch,WebFetch", args[args.IndexOf("--tools") + 1]);
        }

        [Fact]
        public void ParsesPluginListAndCodexServers()
        {
            var plugins = CliSetup.ParseClaudePlugins("warning\n[{\"id\":\"a@m\",\"enabled\":true},{\"id\":\"b@m\",\"enabled\":false}]");
            Assert.Equal(2, plugins.Count);
            Assert.False(plugins[1].EnabledInCli);

            var servers = CliSetup.ParseCodexServers("model = \"x\"\n[mcp_servers.docs]\ncommand = \"d\"\n[mcp_servers.\"my server\"]\n[mcp_servers.docs.env]\nA = \"1\"\n");
            Assert.Equal(new[] { "docs", "my server" }, servers);
        }

        [Fact]
        public void CodexStartsNoServersWhenOffAndOnlyChosenOnesWhenOn()
        {
            var inventory = new CliSetupInventory();
            inventory.Servers["docs"] = default;
            inventory.Servers["crm"] = default;
            Assert.Equal(new[] { "-c", "mcp_servers.docs.enabled=false", "-c", "mcp_servers.crm.enabled=false" }, CliSetup.CodexArguments(new CliSetupChoice(), inventory));
            Assert.Equal(new[] { "-c", "mcp_servers.crm.enabled=false" }, CliSetup.CodexArguments(new CliSetupChoice { Enabled = true, DisabledServers = { "crm" } }, inventory));
        }
    }
}
