namespace QuickPrompt.Core
{
    using System;
    using System.IO;
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

        /// <summary>Keep the last conversations on this computer so they can be reopened and continued.</summary>
        public Boolean SaveHistory { get; set; } = true;

        /// <summary>Restore the previous clipboard text after reading the selection or pasting an answer.</summary>
        public Boolean RestoreClipboard { get; set; } = true;

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
                return new QuickPromptSettings();
            }

            var settings = new QuickPromptSettings();
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

            this.WindowWidth = Math.Clamp(this.WindowWidth, 360, 2000);
            this.WindowHeight = Math.Clamp(this.WindowHeight, 300, 2000);
        }
    }
}
