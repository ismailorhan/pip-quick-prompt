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

    /// <summary>What one Actions Ring button is configured to do.</summary>
    public sealed class Preset
    {
        public String ProviderId { get; set; } = ProviderIds.Claude;

        public String Title { get; set; } = "";

        public String Instruction { get; set; } = "";

        public String Input { get; set; } = InputModes.Selection;

        public String Flow { get; set; } = FlowModes.Send;

        public String Model { get; set; } = "";
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
    }
}
