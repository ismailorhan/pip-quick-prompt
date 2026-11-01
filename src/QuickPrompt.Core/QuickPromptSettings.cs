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

        public Int32 TimeoutSeconds { get; set; } = 300;

        public Int32 WindowWidth { get; set; } = 520;

        public Int32 WindowHeight { get; set; } = 560;

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

        private void Normalize()
        {
            this.ClaudePath ??= "";
            this.CodexPath ??= "";
            this.BrowserPath ??= "";
            this.PopupPath ??= "";
            this.SystemPrompt ??= "";
            if (this.TimeoutSeconds < 10)
            {
                this.TimeoutSeconds = 300;
            }

            this.WindowWidth = Math.Clamp(this.WindowWidth, 360, 2000);
            this.WindowHeight = Math.Clamp(this.WindowHeight, 300, 2000);
        }
    }
}
