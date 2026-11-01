namespace QuickPrompt.Core
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;

    /// <summary>
    /// OpenAI Codex CLI (signed in with a ChatGPT account):
    /// codex exec --skip-git-repo-check --sandbox read-only --json --ephemeral -o last.txt -
    /// The prompt goes in on stdin.
    /// </summary>
    public sealed class CodexCliProvider : CliProviderBase
    {
        private static Int32 _counter;

        public CodexCliProvider(Func<QuickPromptSettings> settings, String workingDirectory)
            : base(settings, workingDirectory)
        {
        }

        public override String Id => ProviderIds.Codex;

        public override String DisplayName => "ChatGPT";

        protected override String ExecutableName => "codex";

        protected override String InstallHint =>
            "Codex CLI not found. Install it with 'npm i -g @openai/codex', then run 'codex' once in a terminal and " +
            "sign in with your ChatGPT account. If it is installed, put its full path in \"CodexPath\" in settings.json.";

        protected override String GetOverridePath(QuickPromptSettings settings) => settings.CodexPath;

        protected override CliInvocation CreateInvocation(AiRequest request)
        {
            var lastFile = Path.Combine(this.WorkingDirectory, $".codex-last-{Environment.ProcessId}-{Interlocked.Increment(ref _counter)}.txt");
            return new CliInvocation(
                BuildArguments(request, this.WorkingDirectory, lastFile),
                BuildStdin(request),
                new CodexStreamParser(() => ReadAndDelete(lastFile)));
        }

        public static IReadOnlyList<String> BuildArguments(AiRequest request, String workingDirectory, String lastFile)
        {

            var args = new List<String>
            {
                "exec",
                "--skip-git-repo-check",
                "--sandbox", "read-only",
                "--json",
                "--color", "never",
                "--ephemeral",
                "-C", workingDirectory,
                "-o", lastFile,
            };

            if (!String.IsNullOrWhiteSpace(request.Model))
            {
                args.Add("-m");
                args.Add(request.Model.Trim());
            }

            args.Add("-");   // read the prompt from stdin
            return args;
        }

        public static String BuildStdin(AiRequest request)
        {
            // codex exec has no system-prompt flag; put the instructions in front.
            if (String.IsNullOrWhiteSpace(request.SystemPrompt))
            {
                return request.Prompt ?? "";
            }

            return request.SystemPrompt.Trim() + "\n\n---\n\n" + (request.Prompt ?? "");
        }

        private static String ReadAndDelete(String file)
        {
            try
            {
                if (File.Exists(file))
                {
                    var text = File.ReadAllText(file);
                    File.Delete(file);
                    return text;
                }
            }
            catch (Exception)
            {
                // Ignore.
            }

            return "";
        }
    }

    public sealed class CodexStreamParser : ICliStreamParser
    {
        private readonly Func<String> _readLastMessageFile;
        private readonly List<String> _order = new List<String>();
        private readonly Dictionary<String, String> _messages = new Dictionary<String, String>();
        private String _lastError = "";
        private Boolean _turnCompleted;

        public CodexStreamParser(Func<String> readLastMessageFile = null)
        {
            this._readLastMessageFile = readLastMessageFile ?? (() => "");
        }

        public Boolean IsFinished { get; private set; }

        private String CurrentText => String.Join("\n\n", this._order.Select(id => this._messages[id]).Where(t => t.Length > 0));

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
                    case "item.started":
                    case "item.updated":
                    case "item.completed":
                        if (!root.TryGetProperty("item", out var item))
                        {
                            break;
                        }

                        var itemType = Json.GetString(item, "type");
                        if (itemType == "agent_message")
                        {
                            var id = Json.GetString(item, "id");
                            if (id.Length == 0)
                            {
                                id = "msg" + this._order.Count;
                            }

                            if (!this._messages.ContainsKey(id))
                            {
                                this._order.Add(id);
                            }

                            this._messages[id] = Json.GetString(item, "text");
                            events.Add(new AiEvent(AiEventKind.Snapshot, this.CurrentText));
                        }
                        else if (itemType == "reasoning")
                        {
                            events.Add(new AiEvent(AiEventKind.Status, "Thinking…"));
                        }
                        else if (itemType == "command_execution")
                        {
                            events.Add(new AiEvent(AiEventKind.Status, "Running a command…"));
                        }
                        else if (itemType == "error")
                        {
                            var warn = Json.GetString(item, "message");
                            if (warn.Length > 0)
                            {
                                events.Add(new AiEvent(AiEventKind.Status, warn));
                            }
                        }

                        break;

                    case "error":
                        // Usually transient ("Reconnecting... 2/5"); keep it in case nothing else arrives.
                        this._lastError = Json.GetString(root, "message");
                        if (this._lastError.Length > 0)
                        {
                            events.Add(new AiEvent(AiEventKind.Status, this._lastError));
                        }

                        break;

                    case "turn.failed":
                        this.IsFinished = true;
                        var message = root.TryGetProperty("error", out var err) ? Json.GetString(err, "message") : "";
                        events.Add(new AiEvent(AiEventKind.Error, message.Length > 0 ? message : "The Codex request failed."));
                        break;

                    case "turn.completed":
                        this._turnCompleted = true;
                        break;
                }

                return events;
            }
        }

        public IEnumerable<AiEvent> Complete(Int32 exitCode, String stderr, Boolean cancelled)
        {
            var fileText = (this._readLastMessageFile() ?? "").Trim();
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

            var text = this.CurrentText;
            if (text.Length == 0)
            {
                text = fileText;
            }

            if (text.Length > 0 && (this._turnCompleted || exitCode == 0))
            {
                yield return new AiEvent(AiEventKind.Final, text);
                yield break;
            }

            var details = this._lastError.Length > 0 ? this._lastError : ProcessHelpers.Summarize(stderr);
            yield return new AiEvent(
                AiEventKind.Error,
                $"Codex CLI exited without an answer (exit code {exitCode})." + (details.Length > 0 ? "\n\n" + details : ""));
        }
    }
}
