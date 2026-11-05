namespace QuickPrompt.Core
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;

    /// <summary>
    /// User-editable settings, stored as settings.json in the plugin data directory.
    /// Created with defaults on first run.
    /// </summary>
    public sealed class QuickPromptSettings
    {
        public const String DefaultSystemPrompt =
            "This request comes from a desktop shortcut. " +
            "Do not use tools, read files or run commands; answer directly, based only on the text provided. " +
            "Do not add introductions or closing remarks. " +
            "Answer in the language of the request unless the user asks for another language.";

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        /// <summary>Full path to the claude executable. Empty = auto-detect.</summary>
        public String ClaudePath { get; set; } = "";

        /// <summary>Full path to the codex executable. Empty = auto-detect.</summary>
        public String CodexPath { get; set; } = "";

        /// <summary>Full path to a Chromium-based browser (Edge, Chrome, Brave). Empty = auto-detect.</summary>
        public String BrowserPath { get; set; } = "";

        /// <summary>macOS popup helper (QuickPromptPopup). Empty = the copy shipped with the plugin.</summary>
        public String PopupPath { get; set; } = "";

        public String SystemPrompt { get; set; } = DefaultSystemPrompt;

        // ---- Connections. API keys are not stored here (see IHostBridge.GetSecret). ----

        /// <summary>How "Ask Claude" presets connect unless a preset overrides it: "cli" or "api".</summary>
        public String ClaudeConnection { get; set; } = ConnectionModes.Cli;

        /// <summary>How "Ask ChatGPT" presets connect unless a preset overrides it: "cli" or "api".</summary>
        public String ChatGptConnection { get; set; } = ConnectionModes.Cli;

        /// <summary>Anthropic API model when a preset does not name one.</summary>
        public String ClaudeApiModel { get; set; } = "claude-opus-5-5";

        /// <summary>Anthropic API effort: low, medium, high, xhigh or max. Low keeps short answers fast.</summary>
        public String ClaudeApiEffort { get; set; } = "low";

        /// <summary>OpenAI API model when a preset does not name one.</summary>
        public String OpenAiApiModel { get; set; } = "gpt-5-mini";

        public Int32 TimeoutSeconds { get; set; } = 300;

        public Int32 WindowWidth { get; set; } = 520;

        public Int32 WindowHeight { get; set; } = 560;

        /// <summary>Popup size, 0 (smallest) … 100 (largest): sets the width and the answer height.</summary>
        public Int32? PopupScale { get; set; }

        /// <summary>Older setting ("small" / "medium" / "large"); read once to seed PopupScale.</summary>
        public String PopupSize { get; set; } = "";

        public const Int32 DefaultPopupScale = 30;

        /// <summary>Size of the floating icon and its toolbar, 0 (smallest) … 100 (largest).</summary>
        public Int32 IconScale { get; set; } = DefaultIconScale;

        public const Int32 DefaultIconScale = 38;

        /// <summary>How dense the glass is (0 = clear … 100 = nearly solid), for easier reading.</summary>
        public Int32 GlassTint { get; set; } = DefaultGlassTint;

        public const Int32 DefaultGlassTint = 35;

        /// <summary>Keep the last conversations on this computer so they can be reopened and continued.</summary>
        public Boolean SaveHistory { get; set; } = true;

        /// <summary>Restore the previous clipboard text after reading the selection or pasting an answer.</summary>
        public Boolean RestoreClipboard { get; set; } = true;

        /// <summary>Keep Pip (the floating icon) on screen: closing a conversation leaves Pip, and Pip shows at start.</summary>
        public Boolean KeepPip { get; set; } = true;

        /// <summary>Dictation languages, Whisper codes ("en,tr"). One: always that language; several: each
        /// recording picks among them. Empty: English and the system's language (the page fills it in).</summary>
        public String SpeechLanguages { get; set; } = "";

        /// <summary>Words dictation should expect (names, product terms), given to the speech model as a hint.</summary>
        public String SpeechHints { get; set; } = "";

        /// <summary>Read every new answer aloud as it is written (the speaker button on an answer works either way).</summary>
        public Boolean ReadAloud { get; set; }

        /// <summary>The voice per language for reading aloud, "tr=Yelda;en=Samantha" (names of the system's voices).
        /// A language not listed uses the system's best local voice for it.</summary>
        public String ReadVoices { get; set; } = "";

        /// <summary>Reading speed, 50–200 (percent of the voice's normal speed).</summary>
        public Int32 ReadRate { get; set; } = 100;

        public static String CleanReadVoices(String? value)
        {
            if (String.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            var parts = value.Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Split('=', 2))
                .Where(p => p.Length == 2 && System.Text.RegularExpressions.Regex.IsMatch(p[0], "^[a-z]{2,3}$") && p[1].Length is > 0 and <= 120 && p[1].IndexOfAny(new[] { ';', '=' }) < 0)
                .Take(20)
                .Select(p => p[0] + "=" + p[1].Trim());
            return String.Join(";", parts);
        }

        public static Boolean IsValidSpeechLanguages(String? value) =>
            value != null && System.Text.RegularExpressions.Regex.IsMatch(value, "^([a-z]{2,3}|haw|yue)(,([a-z]{2,3}|haw|yue)){0,7}$");

        /// <summary>The global shortcut that asks about the selection: modifiers and a key code, e.g. "alt+shift+Space".</summary>
        public String Hotkey { get; set; } = DefaultHotkey;

        public const String DefaultHotkey = "alt+shift+Space";

        /// <summary>The global shortcut that opens the chat and starts dictating (again: stops).</summary>
        public String DictationHotkey { get; set; } = DefaultDictationHotkey;

        public const String DefaultDictationHotkey = "alt+shift+KeyM";

        /// <summary>"cmd|ctrl|alt|shift" (at least one of cmd, ctrl, alt) + a key code like KeyK, Digit1, Space, F5.</summary>
        public static Boolean IsValidHotkey(String value) =>
            !String.IsNullOrEmpty(value)
            && System.Text.RegularExpressions.Regex.IsMatch(value, @"^((cmd|ctrl|alt|shift)\+)+[A-Za-z0-9]+$")
            && System.Text.RegularExpressions.Regex.IsMatch(value, @"(^|\+)(cmd|ctrl|alt)\+");

        /// <summary>Use the user's Claude Code plugins / MCP servers / skills in CLI calls (off: plain Q&amp;A).</summary>
        public CliSetupChoice ClaudeSetup { get; set; } = new CliSetupChoice();

        /// <summary>Use the user's Codex MCP servers in CLI calls (off: none are started).</summary>
        public CliSetupChoice CodexSetup { get; set; } = new CliSetupChoice();

        /// <summary>The user's agents (named AI setups). Filled from the older per-AI settings on first run.</summary>
        public List<Agent> Agents { get; set; }

        /// <summary>The agent new chats, Pip and the shortcuts use.</summary>
        public String DefaultAgentId { get; set; } = "";

        /// <summary>The default agent (never null after Normalize).</summary>
        public Agent DefaultAgent() =>
            this.Agents?.FirstOrDefault(a => a.Id == this.DefaultAgentId) ?? this.Agents?.FirstOrDefault() ?? LegacyAgents(this)[0];

        /// <summary>An agent by id; empty or unknown (deleted): the default agent.</summary>
        public Agent FindAgent(String id) =>
            String.IsNullOrEmpty(id) ? this.DefaultAgent() : this.Agents?.FirstOrDefault(a => a.Id == id) ?? this.DefaultAgent();

        /// <summary>The agents the older settings described: Claude and ChatGPT as they were set up.</summary>
        public static List<Agent> LegacyAgents(QuickPromptSettings s) => new List<Agent>
        {
            new Agent
            {
                Id = "claude", Name = "Claude", Color = Agent.ClaudeColor, ProviderId = ProviderIds.Claude,
                Connection = ConnectionModes.Normalize(s.ClaudeConnection, ConnectionModes.Cli) == ConnectionModes.Api ? ConnectionModes.Api : ConnectionModes.Cli,
                Model = ConnectionModes.Normalize(s.ClaudeConnection, ConnectionModes.Cli) == ConnectionModes.Api ? s.ClaudeApiModel ?? "" : "",
                Effort = Agent.Efforts.Contains(s.ClaudeApiEffort ?? "") ? s.ClaudeApiEffort : "",
                Setup = Agent.CloneSetup(s.ClaudeSetup),
            },
            new Agent
            {
                Id = "chatgpt", Name = "ChatGPT", Color = Agent.ChatGptColor, ProviderId = ProviderIds.Codex,
                Connection = ConnectionModes.Normalize(s.ChatGptConnection, ConnectionModes.Cli) == ConnectionModes.Api ? ConnectionModes.Api : ConnectionModes.Cli,
                Model = ConnectionModes.Normalize(s.ChatGptConnection, ConnectionModes.Cli) == ConnectionModes.Api ? s.OpenAiApiModel ?? "" : "",
                Setup = Agent.CloneSetup(s.CodexSetup),
            },
        };

        public static QuickPromptSettings LoadOrCreate(String filePath, Action<String> log = null)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    var json = File.ReadAllText(filePath);
                    var loaded = JsonSerializer.Deserialize<QuickPromptSettings>(json, JsonOptions);
                    if (loaded != null)
                    {
                        loaded.Normalize();
                        return loaded;
                    }
                }
            }
            catch (Exception ex)
            {
                log?.Invoke($"Could not read settings.json, using defaults: {ex.Message}");
                var fallback = new QuickPromptSettings();
                fallback.Normalize();
                return fallback;
            }

            var settings = new QuickPromptSettings();
            settings.Normalize();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                File.WriteAllText(filePath, JsonSerializer.Serialize(settings, JsonOptions));
            }
            catch (Exception ex)
            {
                log?.Invoke($"Could not write settings.json: {ex.Message}");
            }

            return settings;
        }

        public void Save(String filePath)
        {
            this.Normalize();
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            File.WriteAllText(filePath, JsonSerializer.Serialize(this, JsonOptions));
        }

        private void Normalize()
        {
            this.ClaudePath ??= "";
            this.CodexPath ??= "";
            this.BrowserPath ??= "";
            this.PopupPath ??= "";
            this.PopupScale ??= (this.PopupSize ?? "").Trim().ToLowerInvariant() switch
            {
                "small" => 5,
                "large" => 85,
                _ => DefaultPopupScale,
            };
            this.PopupScale = Math.Clamp(this.PopupScale.Value, 0, 100);
            this.PopupSize = "";
            this.IconScale = Math.Clamp(this.IconScale, 0, 100);
            this.GlassTint = Math.Clamp(this.GlassTint, 0, 100);
            this.SystemPrompt ??= "";
            this.ClaudeConnection = ConnectionModes.Normalize(this.ClaudeConnection, ConnectionModes.Cli);
            this.ChatGptConnection = ConnectionModes.Normalize(this.ChatGptConnection, ConnectionModes.Cli);
            this.ClaudeApiModel = String.IsNullOrWhiteSpace(this.ClaudeApiModel) ? "claude-opus-5-5" : this.ClaudeApiModel.Trim();
            this.ClaudeApiEffort = String.IsNullOrWhiteSpace(this.ClaudeApiEffort) ? "low" : this.ClaudeApiEffort.Trim().ToLowerInvariant();
            this.OpenAiApiModel = String.IsNullOrWhiteSpace(this.OpenAiApiModel) ? "gpt-5-mini" : this.OpenAiApiModel.Trim();
            if (this.TimeoutSeconds < 10)
            {
                this.TimeoutSeconds = 300;
            }

            // Agents: made once from the older per-AI settings; ids unique, exactly one default.
            if (this.Agents == null || this.Agents.Count == 0)
            {
                this.Agents = LegacyAgents(this);
                this.DefaultAgentId = "claude";
            }

            this.Agents.RemoveAll(a => a == null);
            var ids = new HashSet<String>(StringComparer.Ordinal);
            foreach (var agent in this.Agents)
            {
                agent.Normalize();
                if (agent.Id.Length == 0 || !System.Text.RegularExpressions.Regex.IsMatch(agent.Id, "^[A-Za-z0-9]{1,24}$") || !ids.Add(agent.Id))
                {
                    agent.Id = Agent.NewId();
                    ids.Add(agent.Id);
                }
            }

            if (!this.Agents.Any(a => a.Id == this.DefaultAgentId))
            {
                this.DefaultAgentId = this.Agents[0].Id;
            }

            this.WindowWidth = Math.Clamp(this.WindowWidth, 360, 2000);
            this.WindowHeight = Math.Clamp(this.WindowHeight, 300, 2000);
        }
    }
}
