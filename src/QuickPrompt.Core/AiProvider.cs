namespace QuickPrompt.Core
{
    using System;
    using System.Collections.Generic;
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

    public sealed class AiRequest
    {
        public String Prompt { get; set; } = "";

        public String Model { get; set; } = "";

        public String SystemPrompt { get; set; } = "";
    }

    public interface IAiProvider
    {
        String Id { get; }

        String DisplayName { get; }

        /// <summary>Runs one request. The stream always ends with exactly one Final or Error event.</summary>
        IAsyncEnumerable<AiEvent> RunAsync(AiRequest request, CancellationToken cancellationToken);
    }
}
