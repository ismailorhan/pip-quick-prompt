namespace QuickPrompt.Tests
{
    using System.Runtime.CompilerServices;

    using QuickPrompt.Core;

    /// <summary>Simulates an app with a text selection and a clipboard.</summary>
    internal sealed class FakeHost : IHostBridge
    {
        private readonly object _lock = new object();

        public string Clipboard { get; set; } = "";

        /// <summary>What Ctrl/Cmd+C copies. Null = nothing selected.</summary>
        public string Selection { get; set; }

        public List<string> Pasted { get; } = new List<string>();

        public List<string> Notifications { get; } = new List<string>();

        public List<string> Logs { get; } = new List<string>();

        public string GetClipboardText()
        {
            lock (this._lock)
            {
                return this.Clipboard;
            }
        }

        public void SetClipboardText(string text)
        {
            lock (this._lock)
            {
                this.Clipboard = text;
            }
        }

        /// <summary>How many Ctrl/Cmd+C presses to ignore (simulates focus not being back yet).</summary>
        public int IgnoreCopies { get; set; }

        public int CopyCount { get; private set; }

        public void SendCopyShortcut()
        {
            this.CopyCount++;
            if (this.IgnoreCopies > 0)
            {
                this.IgnoreCopies--;
                return;
            }

            if (this.Selection != null)
            {
                // Real apps update the clipboard a little later.
                var selection = this.Selection;
                Task.Delay(30).ContinueWith(_ => this.SetClipboardText(selection));
            }
        }

        public void SendPasteShortcut()
        {
            lock (this._lock)
            {
                this.Pasted.Add(this.Clipboard);
            }
        }

        public void Notify(string title, string message, bool isError) => this.Notifications.Add($"{title}: {message}");

        public void Log(string message, Exception exception = null) => this.Logs.Add(message + (exception != null ? " " + exception.Message : ""));
    }

    /// <summary>Echoes the prompt back in small pieces.</summary>
    internal sealed class FakeProvider : IAiProvider
    {
        private readonly string _id;

        public FakeProvider(string id = ProviderIds.Claude, string failWith = null, int delayMs = 5)
        {
            this._id = id;
            this.FailWith = failWith;
            this.DelayMs = delayMs;
        }

        public string Id => this._id;

        public string DisplayName => this._id == ProviderIds.Claude ? "Claude" : "ChatGPT";

        public string FailWith { get; set; }

        public int DelayMs { get; set; }

        public List<AiRequest> Requests { get; } = new List<AiRequest>();

        public Func<AiRequest, string> Answer { get; set; } = r => "ANSWER: " + r.Prompt;

        public async IAsyncEnumerable<AiEvent> RunAsync(AiRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            this.Requests.Add(request);
            yield return new AiEvent(AiEventKind.Status, "Waiting for the answer…");
            if (this.FailWith != null)
            {
                yield return new AiEvent(AiEventKind.Error, this.FailWith);
                yield break;
            }

            var answer = this.Answer(request);
            for (var i = 0; i < answer.Length; i += 7)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    yield return new AiEvent(AiEventKind.Error, "Cancelled.");
                    yield break;
                }

                await Task.Delay(this.DelayMs);
                yield return new AiEvent(AiEventKind.Delta, answer.Substring(i, Math.Min(7, answer.Length - i)));
            }

            yield return new AiEvent(AiEventKind.Final, answer);
        }
    }

    internal static class TempDir
    {
        public static string Create()
        {
            var dir = Path.Combine(Path.GetTempPath(), "quick-prompt-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
