namespace QuickPrompt.Core
{
    using System;

    public static class InputModes
    {
        /// <summary>Copy the current selection (Ctrl/Cmd+C). Falls back to the clipboard when nothing is selected.</summary>
        public const String Selection = "selection";

        public const String Clipboard = "clipboard";

        public const String None = "none";

        /// <summary>Open the popup with an empty box; the user pastes or types the text, Enter sends it with the preset prompt.</summary>
        public const String Popup = "popup";

        public static String Normalize(String value) =>
            value == Clipboard || value == None || value == Popup ? value : Selection;
    }

    public static class FlowModes
    {
        /// <summary>Open the window and send right away.</summary>
        public const String Send = "send";

        /// <summary>Open the window with the prompt filled in, so it can be edited before sending.</summary>
        public const String Edit = "edit";

        /// <summary>No window: replace the selection with the answer.</summary>
        public const String Replace = "replace";

        /// <summary>No window: put the answer on the clipboard.</summary>
        public const String Copy = "copy";
    }

    public static class ConnectionModes
    {
        /// <summary>Use the provider's connection from Settings.</summary>
        public const String Default = "default";

        /// <summary>Claude Code / Codex CLI: uses the user's subscription.</summary>
        public const String Cli = "cli";

        /// <summary>Anthropic / OpenAI API with the user's API key: faster, billed per use.</summary>
        public const String Api = "api";

        public static String Normalize(String value, String fallback) =>
            value?.Trim().ToLowerInvariant() switch
            {
                Cli => Cli,
                Api => Api,
                Default => Default,
                _ => fallback,
            };
    }

    /// <summary>What one Actions Ring button is configured to do.</summary>
    public sealed class Preset
    {
        public String ProviderId { get; set; } = ProviderIds.Claude;

        public String Title { get; set; } = "";

        public String Instruction { get; set; } = "";

        public String Input { get; set; } = InputModes.Selection;

        public String Flow { get; set; } = FlowModes.Send;

        public String Model { get; set; } = "";

        public String Connection { get; set; } = ConnectionModes.Default;

        /// <summary>The action's own CLI setup (plugins, MCP servers, skills); null = as in Settings.</summary>
        public CliSetupChoice Setup { get; set; }
    }

    /// <summary>OS-specific operations the plugin host provides (clipboard, keystrokes, notifications).</summary>
    public interface IHostBridge
    {
        String GetClipboardText();

        void SetClipboardText(String text);

        void SendCopyShortcut();

        void SendPasteShortcut();

        void Notify(String title, String message, Boolean isError);

        void Log(String message, Exception exception = null);

        /// <summary>Reads a secret (API key). Stored encrypted by the host; null when not set.</summary>
        String GetSecret(String name);

        /// <summary>Stores a secret; null or empty deletes it.</summary>
        void SetSecret(String name, String value);

        /// <summary>The picture on the clipboard, scaled down and with a small preview; null when there is none
        /// (or the host cannot read pictures).</summary>
        ImageAttachment GetClipboardImage() => null;
    }
}
