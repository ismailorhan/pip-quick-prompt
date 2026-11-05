namespace QuickPrompt.Core
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// The local API the macOS / Windows app uses to drive the engine (QuickPrompt.Agent):
    ///   GET  /api/events?since=N   long-poll: things the app must do (open a popup, press ⌘C/⌘V, notify)
    ///   POST /api/hotkey           the global shortcut: read the selection, open a popup with it
    ///   POST /api/new | /api/history | /api/settings   open a popup on that view
    ///   POST /api/run {presetId}   run a saved preset (asks for its fields first, if it has any)
    ///   POST /api/actions          open the Prompt templates editor; GET /api/presets lists them
    /// Every call needs the X-QuickPrompt-Control header with the token the app passed at start.
    /// </summary>
    public sealed class AppControl
    {
        private readonly Object _lock = new Object();
        private readonly List<(Int64 Id, Object Payload)> _events = new List<(Int64, Object)>();
        private TaskCompletionSource<Boolean> _changed = NewTcs();
        private Int64 _nextId = 1;

        public AppControl(String token)
        {
            this.Token = token ?? "";
        }

        public String Token { get; }

        public ChatManager Manager { get; set; }

        /// <summary>Queues an event for the app (kept for a while so a reconnecting app can catch up).</summary>
        public void Publish(Object payload)
        {
            lock (this._lock)
            {
                this._events.Add((this._nextId++, payload));
                if (this._events.Count > 200)
                {
                    this._events.RemoveRange(0, this._events.Count - 200);
                }

                var old = this._changed;
                this._changed = NewTcs();
                old.TrySetResult(true);
            }
        }

        public async Task<String> WaitForEventsAsync(Int64 since, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Task changed;
            lock (this._lock)
            {
                if (this._events.Any(e => e.Id > since))
                {
                    return this.EventsJsonLocked(since);
                }

                changed = this._changed.Task;
            }

            await Task.WhenAny(changed, Task.Delay(timeout, cancellationToken)).ConfigureAwait(false);
            lock (this._lock)
            {
                return this.EventsJsonLocked(since);
            }
        }

        private String EventsJsonLocked(Int64 since)
        {
            var items = this._events.Where(e => e.Id > since).ToList();
            return JsonSerializer.Serialize(new
            {
                last = items.Count > 0 ? items[^1].Id : Math.Max(since, this._nextId - 1),
                events = items.Select(e => e.Payload).ToArray(),
            });
        }

        /// <summary>Handles /api/{action}. Returns (status, json).</summary>
        public async Task<(Int32 Status, String Json)> HandleAsync(String method, String action, String query, IReadOnlyDictionary<String, String> body, CancellationToken cancellationToken)
        {
            var manager = this.Manager;
            if (manager == null)
            {
                return (503, "{\"error\":\"starting\"}");
            }

            String Get(String key) => body.TryGetValue(key, out var v) ? v ?? "" : "";

            switch ((method, action))
            {
                case ("GET", "events"):
                    Int64.TryParse(query, out var since);
                    return (200, await this.WaitForEventsAsync(since, TimeSpan.FromSeconds(25), cancellationToken).ConfigureAwait(false));

                case ("POST", "hotkey"):
                    // Global shortcut: take the selection if there is one and open the popup to ask about it.
                    // canCopy=false: the app has no Accessibility permission, so ⌘C cannot work; do not wait for it.
                    _ = Task.Run(() => manager.ExecutePresetAsync(new Preset
                    {
                        ProviderId = String.IsNullOrEmpty(Get("provider")) ? ProviderIds.Claude : Get("provider"),
                        AgentId = String.IsNullOrEmpty(Get("provider")) ? "" : null,   // the default agent
                        Input = Get("canCopy") == "false" ? InputModes.Popup : InputModes.Selection,
                        Flow = FlowModes.Edit,
                        Quick = true,
                    }));
                    return (200, "{\"ok\":true}");

                case ("POST", "new"):
                    // The default agent, unless an AI is named (older callers).
                    var created = manager.CreateSession(String.IsNullOrEmpty(Get("provider")) ? null : manager.AgentForProvider(Get("provider")), "");
                    created.StartDictation = Get("dictate") == "true";   // the dictation shortcut with no popup open
                    manager.OpenWindow(created);
                    return (200, "{\"ok\":true}");

                case ("POST", "history"):
                    manager.OpenHistory();
                    return (200, "{\"ok\":true}");

                case ("POST", "pip"):
                    // At start (only if "Keep Pip on screen" is on), or from the menu bar (show=true turns it on).
                    var shown = manager.ShowPip(Get("show") == "true");
                    return (200, shown ? "{\"ok\":true}" : "{\"ok\":false}");

                case ("POST", "actions"):
                    manager.OpenActions();
                    return (200, "{\"ok\":true}");

                case ("GET", "presets"):
                    return (200, manager.GetPresetsJson());

                case ("POST", "settings"):
                    manager.OpenSettings();
                    return (200, "{\"ok\":true}");

                case ("POST", "run"):
                    var ok = await manager.RunSavedPresetAsync(Get("presetId")).ConfigureAwait(false);
                    return ok ? (200, "{\"ok\":true}") : (404, "{\"ok\":false}");

                case ("GET", "status"):
                    var hotkey = QuickPromptSettings.IsValidHotkey(manager.Settings.Hotkey) ? manager.Settings.Hotkey : QuickPromptSettings.DefaultHotkey;
                    var dictationHotkey = QuickPromptSettings.IsValidHotkey(manager.Settings.DictationHotkey) ? manager.Settings.DictationHotkey : QuickPromptSettings.DefaultDictationHotkey;
                    return (200, JsonSerializer.Serialize(new { version = ChatManager.AppVersion, port = manager.Port, hotkey, dictationHotkey }));
            }

            return (404, "{\"error\":\"unknown\"}");
        }

        private static TaskCompletionSource<Boolean> NewTcs() =>
            new TaskCompletionSource<Boolean>(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
