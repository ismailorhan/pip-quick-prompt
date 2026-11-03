namespace QuickPrompt.Core
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    public sealed class ChatMessage
    {
        public String Role { get; set; } = "user";            // user | assistant

        public String Text { get; set; } = "";                // assistant answer, or the user's instruction

        public String Context { get; set; } = "";             // user message only: the captured text

        public String State { get; set; } = "done";           // streaming | done | error

        public List<ImageAttachment> Images { get; set; } = new List<ImageAttachment>();   // user message only
    }

    /// <summary>One conversation shown in one response window.</summary>
    public sealed class ChatSession
    {
        private readonly Object _lock = new Object();
        private readonly List<ChatMessage> _messages = new List<ChatMessage>();
        private readonly Func<QuickPromptSettings> _settings;
        private TaskCompletionSource<Boolean> _changed = NewTcs();
        private CancellationTokenSource _turnCts;
        private Int64 _version = 1;
        private String _status = "";
        private Boolean _busy;
        private Int32 _watchers;
        private DateTime _lastSeenUtc = DateTime.UtcNow;

        public ChatSession(IAiProvider provider, Func<QuickPromptSettings> settings)
        {
            this.Provider = provider;
            this._settings = settings;
            this.Token = CreateToken();
            this.LastActivityUtc = DateTime.UtcNow;
        }

        public String Token { get; }

        public IAiProvider Provider { get; private set; }

        /// <summary>Stable id of this conversation in the history (unlike Token, which is per popup).</summary>
        public String HistoryId { get; private set; } = Guid.NewGuid().ToString("N");

        public DateTime CreatedUtc { get; private set; } = DateTime.UtcNow;

        /// <summary>Raised after every finished turn (answer or error), e.g. to save the conversation.</summary>
        public event Action<ChatSession> TurnFinished;

        public String Title { get; set; } = "";

        public String Model { get; set; } = "";

        public String DraftInstruction { get; set; } = "";

        public String DraftContext { get; set; } = "";

        /// <summary>Pictures waiting to go with the next message (from the clipboard or the selection).</summary>
        public List<ImageAttachment> DraftImages { get; set; } = new List<ImageAttachment>();

        /// <summary>Hint shown above the input (e.g. "selection could not be read"). Cleared when a turn starts.</summary>
        public String Notice { get; set; } = "";

        /// <summary>
        /// "Paste in popup" presets: the preset prompt is kept here and applied to the first message;
        /// whatever the user pastes or types becomes the text. Cleared once the first turn starts.
        /// </summary>
        public String PresetInstruction { get; set; } = "";

        /// <summary>"cli" or "api" (shown in the popup header).</summary>
        public String Connection { get; set; } = ConnectionModes.Cli;

        /// <summary>Open the popup on the Settings view.</summary>
        public Boolean ShowSettings { get; set; }

        /// <summary>Open the popup on the History view.</summary>
        public Boolean ShowHistory { get; set; }

        /// <summary>The CLI setup this conversation uses (from its action); null = as in Settings.</summary>
        public CliSetupChoice Setup { get; set; }

        /// <summary>Open as Pip only (no conversation showing).</summary>
        public Boolean StartCollapsed { get; set; }

        /// <summary>Open on the Prompt templates editor.</summary>
        public Boolean ShowActions { get; set; }

        /// <summary>Snapshot of the finished messages, for the history.</summary>
        public ConversationRecord ToRecord()
        {
            lock (this._lock)
            {
                return new ConversationRecord
                {
                    Id = this.HistoryId,
                    ProviderId = this.Provider.Id,
                    Connection = this.Connection,
                    Model = this.Model,
                    Title = this.Title,
                    CreatedUtc = this.CreatedUtc,
                    UpdatedUtc = DateTime.UtcNow,
                    Messages = this._messages
                        .Where(m => m.State != "streaming")
                        .Select(m => new ChatMessage { Role = m.Role, Text = m.Text, Context = m.Context, State = m.State, Images = ForHistory(m.Images) })
                        .ToList(),
                };
            }
        }

        /// <summary>Switches this popup to a saved preset: a new conversation waiting for pasted text.</summary>
        public Boolean ApplyPreset(IAiProvider provider, String connection, String title, String model, String instruction)
        {
            lock (this._lock)
            {
                if (this._busy)
                {
                    return false;
                }

                this.Provider = provider;
                this.Connection = connection;
                this.Title = title ?? "";
                this.Model = model ?? "";
            }

            if (!this.StartNew())
            {
                return false;
            }

            lock (this._lock)
            {
                this.PresetInstruction = instruction ?? "";
                this.TouchLocked();
                return true;
            }
        }

        /// <summary>Starts a new, empty conversation in this popup (same provider and preset title).</summary>
        public Boolean StartNew()
        {
            lock (this._lock)
            {
                if (this._busy)
                {
                    return false;
                }

                this.HistoryId = Guid.NewGuid().ToString("N");
                this.CreatedUtc = DateTime.UtcNow;
                this.PresetInstruction = "";
                this.DraftInstruction = "";
                this.DraftContext = "";
                this.DraftImages = new List<ImageAttachment>();
                this.Notice = "";
                this._messages.Clear();
                this.TouchLocked();
                return true;
            }
        }

        /// <summary>Continues a saved conversation in this popup. Not allowed while a turn is running.</summary>
        public Boolean ResumeFrom(ConversationRecord record, IAiProvider provider)
        {
            lock (this._lock)
            {
                if (this._busy || record == null)
                {
                    return false;
                }

                this.Provider = provider;
                this.HistoryId = record.Id;
                this.CreatedUtc = record.CreatedUtc;
                this.Connection = record.Connection;
                this.Model = record.Model ?? "";
                this.Title = record.Title ?? "";
                this.PresetInstruction = "";
                this.DraftInstruction = "";
                this.DraftContext = "";
                this.DraftImages = new List<ImageAttachment>();
                this.Notice = "";
                this.ShowSettings = false;
                this.ShowHistory = false;
                this.ShowActions = false;
                this._messages.Clear();
                this._messages.AddRange(record.Messages.Select(m => new ChatMessage { Role = m.Role, Text = m.Text, Context = m.Context, State = m.State, Images = m.Images ?? new List<ImageAttachment>() }));
                this.TouchLocked();
                return true;
            }
        }

        public DateTime LastActivityUtc { get; private set; }

        /// <summary>
        /// True when nothing has happened for a while and no window is looking at the session. A window that is
        /// open polls the state all the time, so Pip (or a popup) left on screen overnight keeps its session.
        /// </summary>
        public Boolean IsStale(DateTime cutoffUtc)
        {
            lock (this._lock)
            {
                return !this._busy && this._watchers == 0 && this.LastActivityUtc < cutoffUtc && this._lastSeenUtc < cutoffUtc;
            }
        }

        public Boolean IsBusy
        {
            get
            {
                lock (this._lock)
                {
                    return this._busy;
                }
            }
        }

        public Int64 Version
        {
            get
            {
                lock (this._lock)
                {
                    return this._version;
                }
            }
        }

        /// <summary>Completes when the turn that is running (if any) ends. Returns the answer or throws with the error text.</summary>
        public Task<String> CurrentTurn { get; private set; } = Task.FromResult("");

        public String LastAnswer
        {
            get
            {
                lock (this._lock)
                {
                    return this._messages.LastOrDefault(m => m.Role == "assistant" && m.State == "done")?.Text ?? "";
                }
            }
        }

        /// <summary>Starts a turn. Returns false when another turn is still running or there is nothing to send.</summary>
        /// <summary>
        /// A message sent from the popup. When a preset prompt is pending ("Paste in popup"), the typed
        /// text and any added text become the input of that prompt.
        /// </summary>
        /// <summary>A picture of the conversation, for the large preview (message index, picture index).</summary>
        /// <summary>Removes a waiting picture (its ✕ in the popup).</summary>
        public void DropDraftImage(Int32 index)
        {
            lock (this._lock)
            {
                if (index >= 0 && index < this.DraftImages.Count)
                {
                    this.DraftImages = this.DraftImages.Where((_, i) => i != index).ToList();
                    this.TouchLocked();
                }
            }
        }

        public ImageAttachment GetImage(Int32 message, Int32 index)
        {
            lock (this._lock)
            {
                if (message == -1)
                {
                    return index >= 0 && index < this.DraftImages.Count ? this.DraftImages[index] : null;
                }

                if (message < 0 || message >= this._messages.Count)
                {
                    return null;
                }

                var images = this._messages[message].Images;
                return images != null && index >= 0 && index < images.Count ? images[index] : null;
            }
        }

        public Boolean StartTurnFromPopup(String typed, String context, IReadOnlyDictionary<String, String> fields = null, IReadOnlyList<ImageAttachment> images = null)
        {
            List<ImageAttachment> waiting;
            lock (this._lock)
            {
                waiting = this.DraftImages;
            }

            IReadOnlyList<ImageAttachment> pictures = waiting.Concat(images ?? Array.Empty<ImageAttachment>()).ToList();
            String preset;
            lock (this._lock)
            {
                preset = this.PresetInstruction;
            }

            if (String.IsNullOrWhiteSpace(preset))
            {
                return this.StartTurn(typed, context, pictures);
            }

            var hasFields = PromptBuilder.GetParameters(preset).Count > 0;
            var instruction = hasFields ? PromptBuilder.FillParameters(preset, fields) : preset;
            var text = String.Join("\n\n", new[] { context ?? "", typed ?? "" }.Where(t => t.Trim().Length > 0));

            // Without fields the preset is applied to pasted text, so there must be some. With fields, the
            // filled-in prompt can stand on its own unless it has a {text} spot to fill.
            if (text.Trim().Length == 0 && pictures.Count == 0 && (!hasFields || PromptBuilder.HasToken(instruction)))
            {
                return false;
            }

            var started = this.StartTurn(instruction, text, pictures);
            if (started)
            {
                lock (this._lock)
                {
                    this.PresetInstruction = "";
                    this.TouchLocked();
                }
            }

            return started;
        }

        public Boolean StartTurn(String instruction, String context, IReadOnlyList<ImageAttachment> images = null)
        {
            instruction = (instruction ?? "").Trim();
            context = context ?? "";
            var pictures = (images ?? Array.Empty<ImageAttachment>()).Where(i => i != null && i.Data.Length > 0).ToList();
            if (instruction.Length == 0 && context.Trim().Length == 0 && pictures.Count == 0)
            {
                return false;
            }

            if (instruction.Length == 0 && context.Trim().Length == 0)
            {
                instruction = pictures.Count == 1 ? "Look at this image." : "Look at these images.";
            }

            ChatMessage answer;
            String prompt;
            List<KeyValuePair<String, String>> history;
            List<IReadOnlyList<ImageAttachment>> historyImages;
            var message = PromptBuilder.Build(instruction, context);
            CancellationTokenSource cts;
            lock (this._lock)
            {
                if (this._busy)
                {
                    return false;
                }

                var turns = this._messages
                    .Where(m => m.State == "done" || m.Role == "user")
                    .ToList();

                // Drop user turns whose answer failed, so the history stays question/answer pairs.
                turns = PairUp(turns);
                history = turns
                    .Select(m => new KeyValuePair<String, String>(m.Role, m.Role == "user" ? PromptBuilder.Build(m.Text, m.Context) : m.Text))
                    .ToList();
                historyImages = turns.Select(m => (IReadOnlyList<ImageAttachment>)(m.Images ?? new List<ImageAttachment>())).ToList();

                prompt = PromptBuilder.BuildWithHistory(history, message);
                this._messages.Add(new ChatMessage { Role = "user", Text = instruction, Context = context.Trim(), Images = pictures });
                answer = new ChatMessage { Role = "assistant", State = "streaming" };
                this._messages.Add(answer);
                this._busy = true;
                this._status = "Starting…";
                this.DraftInstruction = "";
                this.DraftContext = "";
                this.DraftImages = new List<ImageAttachment>();
                this.Notice = "";
                this._turnCts?.Dispose();
                cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(10, this._settings().TimeoutSeconds)));
                this._turnCts = cts;
                this.TouchLocked();
            }

            var request = new AiRequest
            {
                Prompt = prompt,
                History = history,
                Message = message,
                Model = this.Model,
                SystemPrompt = this._settings().SystemPrompt,
                Setup = this.Setup,
                Images = pictures,
                HistoryImages = historyImages,
            };

            var turn = Task.Run(() => this.RunTurnAsync(request, answer, cts.Token));
            // Window turns are not awaited by anyone; observe failures so they are not reported as unobserved.
            _ = turn.ContinueWith(t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            this.CurrentTurn = turn;
            return true;
        }

        public void Cancel()
        {
            lock (this._lock)
            {
                try
                {
                    this._turnCts?.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // Turn already finished.
                }
            }
        }

        /// <summary>
        /// Waits until the session changes past <paramref name="sinceVersion"/> or the timeout passes. A window is
        /// watching the session while it waits (also while the Mac sleeps with the request open).
        /// </summary>
        public async Task WaitForChangeAsync(Int64 sinceVersion, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Task changed;
            lock (this._lock)
            {
                this._lastSeenUtc = DateTime.UtcNow;
                if (this._version > sinceVersion)
                {
                    return;
                }

                changed = this._changed.Task;
                this._watchers++;
            }

            try
            {
                await Task.WhenAny(changed, Task.Delay(timeout, cancellationToken)).ConfigureAwait(false);
            }
            finally
            {
                lock (this._lock)
                {
                    this._watchers--;
                    this._lastSeenUtc = DateTime.UtcNow;
                }
            }
        }

        public String ToJson(Boolean isAppWindow)
        {
            lock (this._lock)
            {
                var snapshot = new
                {
                    v = this._version,
                    busy = this._busy,
                    status = this._status,
                    title = this.Title,
                    provider = this.Provider.Id,
                    providerName = this.Provider.DisplayName,
                    model = this.Model,
                    appWindow = isAppWindow,
                    draft = new { instruction = this.DraftInstruction, context = this.DraftContext, images = this.DraftImages.Select(i => i.Thumb).ToArray() },
                    notice = this.Notice,
                    pasteMode = this.PresetInstruction.Trim().Length > 0,
                    connection = this.Connection,
                    showSettings = this.ShowSettings,
                    showHistory = this.ShowHistory,
                    showActions = this.ShowActions,
                    fields = PromptBuilder.GetParameters(this.PresetInstruction).Select(p => new { name = p.Name, options = p.Options }).ToArray(),
                    presetPrompt = this.PresetInstruction,
                    popupScale = this._settings().PopupScale ?? QuickPromptSettings.DefaultPopupScale,
                    iconScale = this._settings().IconScale,
                    glassTint = this._settings().GlassTint,
                    keepPip = this._settings().KeepPip,
                    startCollapsed = this.StartCollapsed,
                    messages = this._messages.Select(m => new
                    {
                        role = m.Role, text = m.Text, context = m.Context, state = m.State,
                        images = (m.Images ?? new List<ImageAttachment>()).Select(i => i.Thumb).ToArray(),
                    }).ToArray(),
                };
                return JsonSerializer.Serialize(snapshot);
            }
        }

        /// <summary>Adds a finished error exchange (used when a background run fails and we show it in a window).</summary>
        public void AddError(String instruction, String context, String error)
        {
            lock (this._lock)
            {
                this._messages.Add(new ChatMessage { Role = "user", Text = instruction ?? "", Context = (context ?? "").Trim() });
                this._messages.Add(new ChatMessage { Role = "assistant", Text = error ?? "", State = "error" });
                this.TouchLocked();
            }
        }

        public void AddAnswer(String instruction, String context, String answer)
        {
            lock (this._lock)
            {
                this._messages.Add(new ChatMessage { Role = "user", Text = instruction ?? "", Context = (context ?? "").Trim() });
                this._messages.Add(new ChatMessage { Role = "assistant", Text = answer ?? "", State = "done" });
                this.TouchLocked();
            }
        }

        private async Task<String> RunTurnAsync(AiRequest request, ChatMessage answer, CancellationToken cancellationToken)
        {
            String finalText = null;
            String errorText = null;
            try
            {
                await foreach (var ev in this.Provider.RunAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    lock (this._lock)
                    {
                        switch (ev.Kind)
                        {
                            case AiEventKind.Status:
                                this._status = ev.Text;
                                break;
                            case AiEventKind.Delta:
                                answer.Text += ev.Text;
                                this._status = "Writing…";
                                break;
                            case AiEventKind.Snapshot:
                                answer.Text = ev.Text;
                                this._status = "Writing…";
                                break;
                            case AiEventKind.Final:
                                answer.Text = ev.Text.Length > 0 ? ev.Text : answer.Text;
                                finalText = answer.Text;
                                break;
                            case AiEventKind.Error:
                                errorText = ev.Text;
                                break;
                        }

                        this.TouchLocked();
                    }

                    if (finalText != null || errorText != null)
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                errorText = cancellationToken.IsCancellationRequested ? "Cancelled or timed out." : ex.Message;
            }

            if (finalText == null && errorText == null)
            {
                errorText = cancellationToken.IsCancellationRequested ? "Cancelled or timed out." : "No answer received.";
            }

            lock (this._lock)
            {
                if (errorText != null)
                {
                    // Keep any partial text, followed by the error.
                    answer.Text = String.IsNullOrWhiteSpace(answer.Text) ? errorText : answer.Text + "\n\n---\n" + errorText;
                    answer.State = "error";
                }
                else
                {
                    answer.State = "done";
                }

                this._busy = false;
                this._status = "";
                this.TouchLocked();
            }

            try
            {
                this.TurnFinished?.Invoke(this);
            }
            catch (Exception)
            {
                // Saving history must never break the conversation.
            }

            if (errorText != null)
            {
                throw new AiTurnException(errorText);
            }

            return finalText;
        }

        /// <summary>
        /// Pictures as kept in the history file: the full picture while it is small enough, otherwise the
        /// preview stands in for it (so history files stay small; a reopened conversation sends the preview).
        /// </summary>
        private static List<ImageAttachment> ForHistory(List<ImageAttachment> images)
        {
            const Int32 MaxKept = 600 * 1024;
            return (images ?? new List<ImageAttachment>()).Select(i =>
            {
                if (i.Data.Length <= MaxKept || !i.Thumb.StartsWith("data:image/", StringComparison.Ordinal))
                {
                    return i;
                }

                var comma = i.Thumb.IndexOf(',');
                var mediaType = i.Thumb.Substring(5, i.Thumb.IndexOf(';') - 5);
                return new ImageAttachment { MediaType = mediaType, Data = i.Thumb.Substring(comma + 1), Thumb = i.Thumb };
            }).ToList();
        }

        private static List<ChatMessage> PairUp(List<ChatMessage> items)
        {
            var result = new List<ChatMessage>();
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i].Role == "user" && i + 1 < items.Count && items[i + 1].Role == "assistant")
                {
                    result.Add(items[i]);
                    result.Add(items[i + 1]);
                    i++;
                }
            }

            return result;
        }

        private void TouchLocked()
        {
            this._version++;
            this.LastActivityUtc = DateTime.UtcNow;
            var old = this._changed;
            this._changed = NewTcs();
            old.TrySetResult(true);
        }

        private static TaskCompletionSource<Boolean> NewTcs() =>
            new TaskCompletionSource<Boolean>(TaskCreationOptions.RunContinuationsAsynchronously);

        private static String CreateToken()
        {
            var bytes = RandomNumberGenerator.GetBytes(18);
            return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }
    }

    public sealed class AiTurnException : Exception
    {
        public AiTurnException(String message)
            : base(message)
        {
        }
    }
}
