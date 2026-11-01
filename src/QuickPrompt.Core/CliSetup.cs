namespace QuickPrompt.Core
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Which parts of the user's own CLI setup (plugins, MCP servers, skills, web) a CLI call may use.
    /// Off by default: plain question and answer, fastest, nothing can act on the computer.
    /// Lists hold what the user switched off, so new plugins and servers are on by default (like the desktop apps).
    /// </summary>
    public sealed class CliSetupChoice
    {
        public Boolean Enabled { get; set; }

        public List<String> DisabledPlugins { get; set; } = new List<String>();

        public List<String> DisabledServers { get; set; } = new List<String>();

        public Boolean Skills { get; set; } = true;

        public Boolean Web { get; set; } = true;
    }

    public sealed class CliPluginInfo
    {
        public String Id { get; set; } = "";

        /// <summary>Enabled in the CLI itself (a plugin switched off there stays off).</summary>
        public Boolean EnabledInCli { get; set; }
    }

    /// <summary>What is installed: read from the CLIs' own configuration.</summary>
    public sealed class CliSetupInventory
    {
        public List<CliPluginInfo> Plugins { get; set; } = new List<CliPluginInfo>();

        /// <summary>MCP server name → its JSON configuration (Claude: from ~/.claude.json, user scope).</summary>
        public Dictionary<String, JsonElement> Servers { get; set; } = new Dictionary<String, JsonElement>(StringComparer.Ordinal);

        public List<String> Skills { get; set; } = new List<String>();

        public String Error { get; set; } = "";
    }

    public static class CliSetup
    {
        private static readonly Object CacheLock = new Object();
        private static CliSetupInventory _claudeCache;
        private static DateTime _claudeCacheUtc;

        private static String Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        private static String ClaudeDir =>
            Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } dir ? dir : Path.Combine(Home, ".claude");

        private static String CodexDir =>
            Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } dir ? dir : Path.Combine(Home, ".codex");

        // ---------- Claude Code ----------

        /// <summary>Plugins (via `claude plugin list --json`), user MCP servers and user skills. Cached for a minute.</summary>
        public static CliSetupInventory DiscoverClaude(String claudeExecutable, Boolean refresh = false)
        {
            lock (CacheLock)
            {
                if (!refresh && _claudeCache != null && DateTime.UtcNow - _claudeCacheUtc < TimeSpan.FromMinutes(1))
                {
                    return _claudeCache;
                }
            }

            var inventory = new CliSetupInventory();
            try
            {
                inventory.Plugins = ReadClaudePlugins(claudeExecutable);
            }
            catch (Exception ex)
            {
                inventory.Error = "Could not list Claude Code plugins: " + ex.Message;
            }

            inventory.Servers = ReadClaudeServers(Path.Combine(Home, ".claude.json"));
            inventory.Skills = ReadSkills(Path.Combine(ClaudeDir, "skills"));

            lock (CacheLock)
            {
                _claudeCache = inventory;
                _claudeCacheUtc = DateTime.UtcNow;
            }

            return inventory;
        }

        private static List<CliPluginInfo> ReadClaudePlugins(String executable)
        {
            if (String.IsNullOrEmpty(executable))
            {
                return new List<CliPluginInfo>();
            }

            // Same start-up as the real calls (Windows .cmd shims, a PATH that finds node).
            var start = ProcessHelpers.CreateStartInfo(executable, new[] { "plugin", "list", "--json" }, Path.GetTempPath(), CliLocator.GetEnrichedPath());
            start.RedirectStandardInput = false;
            using var process = Process.Start(start) ?? throw new InvalidOperationException("claude did not start");
            var output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(20000))
            {
                try { process.Kill(true); } catch (Exception) { }
                throw new TimeoutException("claude plugin list took too long");
            }

            return ParseClaudePlugins(output.Result);
        }

        public static List<CliPluginInfo> ParseClaudePlugins(String json)
        {
            var result = new List<CliPluginInfo>();
            var start = (json ?? "").IndexOf('[');
            if (start < 0)
            {
                return result;
            }

            using var doc = JsonDocument.Parse(json.Substring(start));
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                {
                    result.Add(new CliPluginInfo
                    {
                        Id = id.GetString(),
                        EnabledInCli = !item.TryGetProperty("enabled", out var enabled) || enabled.ValueKind != JsonValueKind.False,
                    });
                }
            }

            return result;
        }

        public static Dictionary<String, JsonElement> ReadClaudeServers(String claudeJsonPath)
        {
            var result = new Dictionary<String, JsonElement>(StringComparer.Ordinal);
            try
            {
                if (!File.Exists(claudeJsonPath))
                {
                    return result;
                }

                using var doc = JsonDocument.Parse(File.ReadAllText(claudeJsonPath));
                if (doc.RootElement.TryGetProperty("mcpServers", out var servers) && servers.ValueKind == JsonValueKind.Object)
                {
                    foreach (var server in servers.EnumerateObject())
                    {
                        result[server.Name] = server.Value.Clone();
                    }
                }
            }
            catch (Exception)
            {
                // Unreadable config: no servers to offer.
            }

            return result;
        }

        private static List<String> ReadSkills(String dir)
        {
            try
            {
                return Directory.Exists(dir)
                    ? Directory.GetDirectories(dir).Where(d => File.Exists(Path.Combine(d, "SKILL.md"))).Select(Path.GetFileName).OrderBy(n => n).ToList()
                    : new List<String>();
            }
            catch (Exception)
            {
                return new List<String>();
            }
        }

        /// <summary>
        /// The claude flags that replace the plain "--tools '' --strict-mcp-config" when the setup is on.
        /// No shell and no file-writing tools: it can look things up but not change files.
        /// </summary>
        public static List<String> ClaudeArguments(CliSetupChoice choice, CliSetupInventory inventory)
        {
            if (choice == null || !choice.Enabled)
            {
                return new List<String> { "--tools", "", "--strict-mcp-config" };
            }

            var tools = new List<String>();
            if (choice.Skills)
            {
                tools.AddRange(new[] { "Skill", "Read", "Glob", "Grep" });
            }

            if (choice.Web)
            {
                tools.AddRange(new[] { "WebSearch", "WebFetch" });
            }

            var args = new List<String> { "--tools", String.Join(",", tools) };

            // Only the chosen MCP servers, with their own configuration.
            var servers = (inventory?.Servers ?? new Dictionary<String, JsonElement>())
                .Where(s => !choice.DisabledServers.Contains(s.Key))
                .ToDictionary(s => s.Key, s => s.Value);
            args.Add("--strict-mcp-config");
            if (servers.Count > 0)
            {
                args.Add("--mcp-config");
                args.Add(JsonSerializer.Serialize(new { mcpServers = servers }));
            }

            // Plugins switched off here (the rest stay as they are in Claude Code).
            var off = (inventory?.Plugins ?? new List<CliPluginInfo>()).Where(p => choice.DisabledPlugins.Contains(p.Id)).Select(p => p.Id).ToList();
            if (off.Count > 0)
            {
                args.Add("--settings");
                args.Add(JsonSerializer.Serialize(new { enabledPlugins = off.ToDictionary(id => id, _ => false) }));
            }

            if (!choice.Skills)
            {
                args.Add("--disable-slash-commands");
            }

            // There is nobody to answer permission prompts: allow exactly the chosen tools and servers.
            var allowed = tools.Concat(servers.Keys.Select(name => "mcp__" + name)).ToList();
            if (allowed.Count > 0)
            {
                args.Add("--allowedTools");
                args.AddRange(allowed);
            }

            return args;
        }

        // ---------- Codex (ChatGPT) ----------

        private static readonly Regex CodexServerRegex = new Regex(
            @"^\s*\[mcp_servers\.(?:""([^""]+)""|([A-Za-z0-9_\-]+))\]\s*$",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);

        public static CliSetupInventory DiscoverCodex()
        {
            var inventory = new CliSetupInventory();
            try
            {
                var path = Path.Combine(CodexDir, "config.toml");
                if (File.Exists(path))
                {
                    foreach (var name in ParseCodexServers(File.ReadAllText(path)))
                    {
                        inventory.Servers[name] = default;
                    }
                }
            }
            catch (Exception ex)
            {
                inventory.Error = "Could not read the Codex configuration: " + ex.Message;
            }

            inventory.Skills = ReadSkills(Path.Combine(CodexDir, "skills"));
            return inventory;
        }

        public static List<String> ParseCodexServers(String toml) =>
            CodexServerRegex.Matches(toml ?? "")
                .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)
                .Distinct()
                .ToList();

        /// <summary>
        /// "-c mcp_servers.NAME.enabled=false" for every server that should not start: all of them when the
        /// setup is off (faster), the ones switched off otherwise. Codex stays in its read-only sandbox.
        /// </summary>
        public static List<String> CodexArguments(CliSetupChoice choice, CliSetupInventory inventory)
        {
            var args = new List<String>();
            foreach (var name in (inventory?.Servers.Keys ?? Enumerable.Empty<String>()).Where(n => Regex.IsMatch(n, "^[A-Za-z0-9_-]+$")))
            {
                if (choice == null || !choice.Enabled || choice.DisabledServers.Contains(name))
                {
                    args.Add("-c");
                    args.Add($"mcp_servers.{name}.enabled=false");
                }
            }

            return args;
        }
    }
}
