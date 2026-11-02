namespace QuickPrompt.Core
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;

    public static class ProviderIds
    {
        public const String Claude = "claude";
        public const String Codex = "codex";
    }

    public enum AiEventKind
    {
        /// <summary>Progress information ("thinking", "reconnecting"...). Not part of the answer.</summary>
        Status,

        /// <summary>Text to append to the answer.</summary>
        Delta,

        /// <summary>The full answer so far; replaces everything received before.</summary>
        Snapshot,

        /// <summary>The final, complete answer. Ends the stream.</summary>
        Final,

        /// <summary>A fatal error. Ends the stream.</summary>
        Error,
    }

    public sealed class AiEvent
    {
        public AiEvent(AiEventKind kind, String text)
        {
            this.Kind = kind;
            this.Text = text ?? "";
        }

        public AiEventKind Kind { get; }

        public String Text { get; }

        public override String ToString() => $"{this.Kind}: {this.Text}";
    }

    /// <summary>A picture sent with a message (pasted, dropped, or from the clipboard).</summary>
    public sealed class ImageAttachment
    {
        public String MediaType { get; set; } = "image/png";

        /// <summary>Base64 picture sent to the AI (already scaled down by whoever made it).</summary>
        public String Data { get; set; } = "";

        /// <summary>Small preview as a data: URL, for the conversation and history.</summary>
        public String Thumb { get; set; } = "";

        public static Boolean IsSupported(String mediaType) =>
            mediaType is "image/png" or "image/jpeg" or "image/gif" or "image/webp";

        public static String Extension(String mediaType) => mediaType switch
        {
            "image/jpeg" => ".jpg",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            _ => ".png",
        };
    }

    public sealed class AiRequest
    {
        /// <summary>
        /// Everything in one text: earlier turns written out, then the new message. Used by the CLIs,
        /// which take a single prompt per call.
        /// </summary>
        public String Prompt { get; set; } = "";

        /// <summary>
        /// Earlier turns as (role, text) pairs, oldest first, alternating "user" / "assistant". Used by
        /// the APIs, which take the conversation as separate messages. Empty for the first message.
        /// </summary>
        public IReadOnlyList<KeyValuePair<String, String>> History { get; set; } = Array.Empty<KeyValuePair<String, String>>();

        /// <summary>The new user message on its own (preset prompt and text combined). Empty = use Prompt.</summary>
        public String Message { get; set; } = "";

        public String Model { get; set; } = "";

        public String SystemPrompt { get; set; } = "";

        /// <summary>CLI setup for this request (an action's own choice); null = the one in Settings.</summary>
        public CliSetupChoice Setup { get; set; }

        /// <summary>Pictures with the new message.</summary>
        public IReadOnlyList<ImageAttachment> Images { get; set; } = Array.Empty<ImageAttachment>();

        /// <summary>Pictures of the earlier turns, one list per History entry (same order).</summary>
        public IReadOnlyList<IReadOnlyList<ImageAttachment>> HistoryImages { get; set; } = Array.Empty<IReadOnlyList<ImageAttachment>>();

        /// <summary>Every picture in the conversation, oldest first (for the CLIs, which take one prompt).</summary>
        public IReadOnlyList<ImageAttachment> AllImages() =>
            (this.HistoryImages ?? Array.Empty<IReadOnlyList<ImageAttachment>>())
                .SelectMany(list => list ?? Array.Empty<ImageAttachment>())
                .Concat(this.Images ?? Array.Empty<ImageAttachment>())
                .ToList();
    }

    public interface IAiProvider
    {
        String Id { get; }

        String DisplayName { get; }

        /// <summary>Runs one request. The stream always ends with exactly one Final or Error event.</summary>
        IAsyncEnumerable<AiEvent> RunAsync(AiRequest request, CancellationToken cancellationToken);
    }
}
