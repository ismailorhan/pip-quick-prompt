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

        /// <summary>Skills switched off one by one (Claude: invocation names; Codex: SKILL.md paths).</summary>
        public List<String> DisabledSkills { get; set; } = new List<String>();

        public Boolean Web { get; set; } = true;
    }

    public sealed class CliPluginInfo
    {
        public String Id { get; set; } = "";

        /// <summary>Enabled in the CLI itself (a plugin switched off there stays off).</summary>
        public Boolean EnabledInCli { get; set; }

        public String InstallPath { get; set; } = "";
    }

    public sealed class CliSkillInfo
    {
        /// <summary>The name the CLI calls it by ("my-skill", or "plugin:skill" for a plugin's skill).</summary>
        public String Name { get; set; } = "";

        /// <summary>The skill's folder (holds SKILL.md).</summary>
        public String Folder { get; set; } = "";

        /// <summary>The plugin it comes from, if any (off with its plugin).</summary>
        public String Plugin { get; set; } = "";
    }

    /// <summary>What is installed: read from the CLIs' own configuration.</summary>
    public sealed class CliSetupInventory
    {
        public List<CliPluginInfo> Plugins { get; set; } = new List<CliPluginInfo>();

        /// <summary>MCP server name → its JSON configuration (Claude: from ~/.claude.json, user scope).</summary>
        public Dictionary<String, JsonElement> Servers { get; set; } = new Dictionary<String, JsonElement>(StringComparer.Ordinal);

        public List<CliSkillInfo> Skills { get; set; } = new List<CliSkillInfo>();

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
            // Your skills, then each plugin's skills ("plugin:skill"). Skills built into Claude Code are not listed.
            inventory.Skills = ReadSkills(Path.Combine(ClaudeDir, "skills"), "", "", 1);
            foreach (var plugin in inventory.Plugins.Where(p => p.InstallPath.Length > 0))
            {
                var pluginName = plugin.Id.Split('@')[0];
                inventory.Skills.AddRange(ReadSkills(Path.Combine(plugin.InstallPath, "skills"), pluginName + ":", plugin.Id, 1));
            }

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
                        InstallPath = item.TryGetProperty("installPath", out var path) && path.ValueKind == JsonValueKind.String ? path.GetString() : "",
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

        /// <summary>Folders holding a SKILL.md, down to <paramref name="depth"/> levels (hidden folders only at the second).</summary>
        public static List<CliSkillInfo> ReadSkills(String dir, String prefix, String plugin, Int32 depth)
        {
            var result = new List<CliSkillInfo>();
            try
            {
                if (!Directory.Exists(dir))
                {
                    return result;
                }

                foreach (var folder in Directory.GetDirectories(dir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                {
                    var name = Path.GetFileName(folder);
                    if (File.Exists(Path.Combine(folder, "SKILL.md")))
                    {
                        if (!name.StartsWith(".", StringComparison.Ordinal))
                        {
                            result.Add(new CliSkillInfo { Name = prefix + name, Folder = folder, Plugin = plugin });
                        }
                    }
                    else if (depth > 1)
                    {
                        result.AddRange(ReadSkills(folder, prefix, plugin, depth - 1));
                    }
                }
            }
            catch (Exception)
            {
                // Unreadable folder: nothing to offer from it.
            }

            return result;
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

            var denied = new List<String>();
            if (!choice.Skills)
            {
                args.Add("--disable-slash-commands");
            }
            else
            {
                // Skills switched off one by one: neither invoked nor read from disk.
                foreach (var skill in (inventory?.Skills ?? new List<CliSkillInfo>()).Where(k => choice.DisabledSkills.Contains(k.Name)))
                {
                    denied.Add($"Skill({skill.Name})");
                    denied.Add("Read(//" + skill.Folder.Replace('\\', '/').TrimStart('/') + "/**)");   // "//" = absolute path
                }
            }

            if (denied.Count > 0)
            {
                args.Add("--disallowedTools");
                args.AddRange(denied);
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

        private static String TomlString(String value) =>
            "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

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

            // Codex keeps its own built-in skills in skills/.system.
            inventory.Skills = ReadSkills(Path.Combine(CodexDir, "skills"), "", "", 2);
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

            // Skills switched off one by one (Codex matches them by the path of their SKILL.md).
            var offSkills = (inventory?.Skills ?? new List<CliSkillInfo>())
                .Where(k => choice != null && choice.Enabled && choice.DisabledSkills.Contains(k.Name))
                .Select(k => "{path=" + TomlString(Path.Combine(k.Folder, "SKILL.md")) + ", enabled=false}")
                .ToList();
            if (offSkills.Count > 0)
            {
                args.Add("-c");
                args.Add("skills.config=[" + String.Join(", ", offSkills) + "]");
            }

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
