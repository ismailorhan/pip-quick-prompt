namespace QuickPrompt.Core
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Entry point used by the plugin: runs presets, owns the providers, the sessions and the local UI server.
    /// </summary>
    public sealed class ChatManager : IDisposable
    {
        private readonly ConcurrentDictionary<String, ChatSession> _sessions = new ConcurrentDictionary<String, ChatSession>(StringComparer.Ordinal);
        private readonly Dictionary<String, IAiProvider> _providers;
        private readonly IHostBridge _host;
        private readonly String _dataDirectory;
        private readonly String _settingsPath;
        private readonly UiServer _server;
        private readonly Timer _cleanupTimer;
        private readonly SemaphoreSlim _clipboardGate = new SemaphoreSlim(1, 1);
        private QuickPromptSettings _settings;
        private DateTime _settingsReadUtc;

        public ChatManager(String dataDirectory, IHostBridge host, IEnumerable<IAiProvider> providers = null)
        {
            this._dataDirectory = dataDirectory;
            this._host = host;
            this._settingsPath = Path.Combine(dataDirectory, "settings.json");
            Directory.CreateDirectory(dataDirectory);
            this._settings = QuickPromptSettings.LoadOrCreate(this._settingsPath, m => host.Log(m));
            this._settingsReadUtc = SafeLastWrite(this._settingsPath);

            var workspace = Path.Combine(dataDirectory, "workspace");
            Directory.CreateDirectory(workspace);
            var list = providers?.ToList() ?? new List<IAiProvider>
            {
                new ClaudeCliProvider(() => this.Settings, workspace),
                new CodexCliProvider(() => this.Settings, workspace),
            };
            this._providers = list.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);

            this._server = new UiServer(this);
            this.Launcher = new WindowLauncher(() => this.Settings, Path.Combine(dataDirectory, "browser-profile"), host);
            this._cleanupTimer = new Timer(_ => this.Cleanup(), null, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
        }

        public String SettingsPath => this._settingsPath;

        public WindowLauncher Launcher { get; }

        public IHostBridge Host => this._host;

        /// <summary>Settings, re-read when settings.json changes on disk.</summary>
        public QuickPromptSettings Settings
        {
            get
            {
                var stamp = SafeLastWrite(this._settingsPath);
                if (stamp != this._settingsReadUtc)
                {
                    this._settingsReadUtc = stamp;
                    this._settings = QuickPromptSettings.LoadOrCreate(this._settingsPath, m => this._host.Log(m));
                    CliLocator.ResetCache();
                }

                return this._settings;
            }
        }

        public Int32 Port => this._server.Port;

        public void Start()
        {
            this._server.Start();
            // Resolving PATH can spawn a login shell; do it now rather than on the first button press.
            _ = Task.Run(() => CliLocator.GetEnrichedPath());
        }

        public IAiProvider GetProvider(String id) =>
            this._providers.TryGetValue(id ?? "", out var provider) ? provider : this._providers.Values.First();

        public Boolean TryGetSession(String token, out ChatSession session) =>
            this._sessions.TryGetValue(token ?? "", out session);

        public ChatSession CreateSession(String providerId, String title, String model)
        {
            var session = new ChatSession(this.GetProvider(providerId), () => this.Settings)
            {
                Title = title ?? "",
                Model = model ?? "",
            };
            this._sessions[session.Token] = session;
            return session;
        }

        public String GetSessionUrl(ChatSession session, Boolean appWindow) =>
            $"http://127.0.0.1:{this.Port}/s/{session.Token}/" + (appWindow ? "" : "?tab=1");

        public void OpenWindow(ChatSession session) =>
            this.Launcher.Open(appWindow => this.GetSessionUrl(session, appWindow));

        /// <summary>Runs one Actions Ring preset. Never throws.</summary>
        public async Task ExecutePresetAsync(Preset preset)
        {
            try
            {
                await this.ExecutePresetCoreAsync(preset).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                this._host.Log("Preset failed", ex);
                this._host.Notify("Quick Prompt", ex.Message, true);
            }
        }

        private async Task ExecutePresetCoreAsync(Preset preset)
        {
            var instruction = preset.Instruction ?? "";
            var flow = preset.Flow ?? FlowModes.Send;
            var capture = await this.CaptureInputAsync(preset.Input).ConfigureAwait(false);
            var context = capture.Text;
            var nothingToSend = instruction.Trim().Length == 0 && context.Trim().Length == 0;

            // The preset needs the selection but none could be read: never send the stale clipboard.
            // Open the popup instead so the text can be pasted or typed.
            var selectionMissing = preset.Input == InputModes.Selection && !capture.Captured;

            if (flow == FlowModes.Edit || nothingToSend || selectionMissing)
            {
                var draft = this.CreateSession(preset.ProviderId, preset.Title, preset.Model);
                draft.DraftInstruction = instruction;
                draft.DraftContext = context;
                if (selectionMissing && flow != FlowModes.Edit)
                {
                    draft.Notice = "Could not read the selected text. Paste it below and send.";
                }

                this.OpenWindow(draft);
                return;
            }

            if (flow == FlowModes.Send)
            {
                var session = this.CreateSession(preset.ProviderId, preset.Title, preset.Model);
                session.StartTurn(instruction, context);
                this.OpenWindow(session);
                return;
            }

            // Background flows: no window unless something goes wrong.
            var background = this.CreateSession(preset.ProviderId, preset.Title, preset.Model);
            var providerName = background.Provider.DisplayName;
            this._host.Notify(providerName, $"{(String.IsNullOrWhiteSpace(preset.Title) ? "Request" : preset.Title)} sent…", false);
            background.StartTurn(instruction, context);

            String answer;
            try
            {
                answer = (await background.CurrentTurn.ConfigureAwait(false) ?? "").Trim();
            }
            catch (AiTurnException)
            {
                // The session already holds the error; show it.
                this.OpenWindow(background);
                return;
            }

            if (flow == FlowModes.Replace)
            {
                await this.PasteTextAsync(answer, capture.PreviousClipboard).ConfigureAwait(false);
            }
            else
            {
                this._host.SetClipboardText(answer);
                this._host.Notify(providerName, "Answer copied to the clipboard.", false);
            }
        }

        /// <summary>Result of reading the input text.</summary>
        public sealed class CaptureResult
        {
            public String Text { get; set; } = "";

            /// <summary>Clipboard text from before we touched it (null when unknown).</summary>
            public String PreviousClipboard { get; set; }

            /// <summary>For selection mode: true when Ctrl/Cmd+C actually produced text.</summary>
            public Boolean Captured { get; set; }
        }

        /// <summary>
        /// For "selection" we press Ctrl/Cmd+C and watch the clipboard. When the clipboard already holds
        /// text we first replace it with a unique marker, so a copy of the very same text is still detected
        /// and a failed copy is never mistaken for a selection. The old text is put back afterwards.
        /// </summary>
        public async Task<CaptureResult> CaptureInputAsync(String inputMode)
        {
            if (inputMode == InputModes.None)
            {
                return new CaptureResult();
            }

            if (inputMode == InputModes.Clipboard)
            {
                var clip = this.SafeGetClipboard();
                return new CaptureResult { Text = clip, PreviousClipboard = clip, Captured = clip.Length > 0 };
            }

            await this._clipboardGate.WaitAsync().ConfigureAwait(false);
            try
            {
                var before = this.SafeGetClipboard();
                var marker = before.Length > 0 ? "\u2063quickprompt-" + Guid.NewGuid().ToString("N") : "";
                if (marker.Length > 0)
                {
                    this._host.SetClipboardText(marker);
                }

                // Let the Actions Ring overlay close and focus return to the app.
                await Task.Delay(this.CopyDelayMs).ConfigureAwait(false);
                this._host.SendCopyShortcut();

                var started = DateTime.UtcNow;
                var retried = false;
                var text = "";
                while ((DateTime.UtcNow - started).TotalMilliseconds < 1500)
                {
                    await Task.Delay(40).ConfigureAwait(false);
                    var current = this.SafeGetClipboard();
                    if (current.Length > 0 && !String.Equals(current, marker, StringComparison.Ordinal))
                    {
                        text = current;
                        break;
                    }

                    // Focus may not have been back yet: press Ctrl/Cmd+C once more.
                    if (!retried && (DateTime.UtcNow - started).TotalMilliseconds > 600)
                    {
                        retried = true;
                        this._host.SendCopyShortcut();
                    }
                }

                var captured = text.Length > 0;
                if (marker.Length > 0 && (!captured || this.Settings.RestoreClipboard))
                {
                    this._host.SetClipboardText(before);
                }

                if (!captured)
                {
                    this._host.Log("Could not read the selection (Ctrl/Cmd+C did not change the clipboard).");
                }

                return new CaptureResult { Text = text, PreviousClipboard = before, Captured = captured };
            }
            finally
            {
                this._clipboardGate.Release();
            }
        }

        /// <summary>Wait before pressing Ctrl/Cmd+C, so the Actions Ring has closed. Lower in tests.</summary>
        public Int32 CopyDelayMs { get; set; } = 250;

        /// <summary>Puts text on the clipboard and presses Ctrl/Cmd+V in the frontmost app.</summary>
        public async Task PasteTextAsync(String text, String restoreAfter, Int32 delayBeforePasteMs = 0)
        {
            await this._clipboardGate.WaitAsync().ConfigureAwait(false);
            try
            {
                var before = restoreAfter ?? this.SafeGetClipboard();
                this._host.SetClipboardText(text ?? "");
                if (delayBeforePasteMs > 0)
                {
                    await Task.Delay(delayBeforePasteMs).ConfigureAwait(false);
                }

                await Task.Delay(60).ConfigureAwait(false);
                this._host.SendPasteShortcut();

                if (this.Settings.RestoreClipboard && !String.IsNullOrEmpty(before))
                {
                    // Give the target app time to read the clipboard before restoring it.
                    await Task.Delay(1000).ConfigureAwait(false);
                    this._host.SetClipboardText(before);
                }
            }
            finally
            {
                this._clipboardGate.Release();
            }
        }

        private String SafeGetClipboard()
        {
            try
            {
                return this._host.GetClipboardText() ?? "";
            }
            catch (Exception ex)
            {
                this._host.Log("Could not read the clipboard", ex);
                return "";
            }
        }

        private void Cleanup()
        {
            var cutoff = DateTime.UtcNow.AddHours(-3);
            foreach (var pair in this._sessions)
            {
                if (pair.Value.LastActivityUtc < cutoff && !pair.Value.IsBusy)
                {
                    this._sessions.TryRemove(pair.Key, out _);
                }
            }
        }

        private static DateTime SafeLastWrite(String path)
        {
            try
            {
                return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            }
            catch (Exception)
            {
                return DateTime.MinValue;
            }
        }

        public void Dispose()
        {
            this._cleanupTimer.Dispose();
            foreach (var session in this._sessions.Values)
            {
                session.Cancel();
            }

            this._server.Dispose();
        }
    }
}
