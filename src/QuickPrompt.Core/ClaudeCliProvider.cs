namespace QuickPrompt.Core
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Text.Json;

    /// <summary>
    /// Claude Code in print mode:
    /// claude -p --output-format stream-json --verbose --include-partial-messages --tools "" ...
    /// The prompt goes in on stdin. Uses the user's existing Claude Code login.
    /// </summary>
    public sealed class ClaudeCliProvider : CliProviderBase
    {
        public ClaudeCliProvider(Func<QuickPromptSettings> settings, String workingDirectory)
            : base(settings, workingDirectory)
        {
        }

        public override String Id => ProviderIds.Claude;

        public override String DisplayName => "Claude";

        protected override String ExecutableName => "claude";

        protected override String InstallHint =>
            "Claude Code CLI not found. Install it and run 'claude' once in a terminal to sign in " +
            "(https://code.claude.com/docs). If it is installed, put its full path in \"ClaudePath\" in settings.json.";

        protected override String GetOverridePath(QuickPromptSettings settings) => settings.ClaudePath;

        protected override CliInvocation CreateInvocation(AiRequest request)
        {
            var choice = request.Setup ?? this.Settings.ClaudeSetup;
            var setup = choice != null && choice.Enabled
                ? CliSetup.ClaudeArguments(choice, CliSetup.DiscoverClaude(this.LocateExecutable()))
                : null;
            return new CliInvocation(BuildArguments(request, setup), request.Prompt ?? "", new ClaudeStreamParser());
        }

        /// <param name="setup">The tool / MCP flags for "use my setup"; null for plain question and answer.</param>
        public static IReadOnlyList<String> BuildArguments(AiRequest request, IReadOnlyList<String> setup = null)
        {
            var args = new List<String>
            {
                "-p",
                "--output-format", "stream-json",
                "--verbose",
                "--include-partial-messages",
                "--no-session-persistence",
            };

            // Default: no tools and none of the user's MCP servers (plain question and answer, fastest).
            args.AddRange(setup ?? new[] { "--tools", "", "--strict-mcp-config" });

            if (!String.IsNullOrWhiteSpace(request.Model))
            {
                args.Add("--model");
                args.Add(request.Model.Trim());
            }

            if (!String.IsNullOrWhiteSpace(request.SystemPrompt))
            {
                args.Add("--append-system-prompt");
                args.Add(request.SystemPrompt);
            }

            return args;
        }

    }

    public sealed class ClaudeStreamParser : ICliStreamParser
    {
        private readonly StringBuilder _text = new StringBuilder();
        private Boolean _sawDelta;

        public Boolean IsFinished { get; private set; }

        public IEnumerable<AiEvent> Feed(String line)
        {
            if (this.IsFinished || String.IsNullOrWhiteSpace(line) || line.TrimStart()[0] != '{')
            {
                return Array.Empty<AiEvent>();
            }

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                return Array.Empty<AiEvent>();
            }

            using (doc)
            {
                var root = doc.RootElement;
                var type = Json.GetString(root, "type");
                var events = new List<AiEvent>();

                switch (type)
                {
                    case "stream_event":
                        if (root.TryGetProperty("event", out var ev)
                            && Json.GetString(ev, "type") == "content_block_delta"
                            && ev.TryGetProperty("delta", out var delta)
                            && Json.GetString(delta, "type") == "text_delta")
                        {
                            var piece = Json.GetString(delta, "text");
                            if (piece.Length > 0)
                            {
                                this._sawDelta = true;
                                this._text.Append(piece);
                                events.Add(new AiEvent(AiEventKind.Delta, piece));
                            }
                        }

                        break;

                    case "assistant":
                        // Without partial messages we only get whole messages.
                        if (!this._sawDelta && root.TryGetProperty("message", out var message))
                        {
                            var whole = ExtractText(message);
                            if (whole.Length > 0)
                            {
                                if (this._text.Length > 0)
                                {
                                    this._text.Append("\n\n");
                                }

                                this._text.Append(whole);
                                events.Add(new AiEvent(AiEventKind.Snapshot, this._text.ToString()));
                            }
                        }

                        break;

                    case "result":
                        this.IsFinished = true;
                        var isError = root.TryGetProperty("is_error", out var e) && e.ValueKind == JsonValueKind.True;
                        var subtype = Json.GetString(root, "subtype");
                        var result = Json.GetString(root, "result");
                        if (isError || (subtype.Length > 0 && subtype != "success"))
                        {
                            var msg = result.Length > 0 ? result : $"Claude returned an error ({subtype}).";
                            events.Add(new AiEvent(AiEventKind.Error, msg));
                        }
                        else
                        {
                            events.Add(new AiEvent(AiEventKind.Final, result.Length > 0 ? result : this._text.ToString()));
                        }

                        break;
                }

                return events;
            }
        }

        public IEnumerable<AiEvent> Complete(Int32 exitCode, String stderr, Boolean cancelled)
        {
            if (this.IsFinished)
            {
                yield break;
            }

            this.IsFinished = true;
            if (cancelled)
            {
                yield return new AiEvent(AiEventKind.Error, "Cancelled.");
                yield break;
            }

            if (exitCode == 0 && this._text.Length > 0)
            {
                yield return new AiEvent(AiEventKind.Final, this._text.ToString());
                yield break;
            }

            var details = ProcessHelpers.Summarize(stderr);
            yield return new AiEvent(
                AiEventKind.Error,
                $"Claude CLI exited without an answer (exit code {exitCode})." + (details.Length > 0 ? "\n\n" + details : ""));
        }

        private static String ExtractText(JsonElement message)
        {
            if (!message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                return "";
            }

            var sb = new StringBuilder();
            foreach (var block in content.EnumerateArray())
            {
                if (Json.GetString(block, "type") == "text")
                {
                    sb.Append(Json.GetString(block, "text"));
                }
            }

            return sb.ToString();
        }
    }

    internal static class Json
    {
        public static String GetString(JsonElement element, String name) =>
            element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : "";
    }
}
