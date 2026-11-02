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
        private readonly Dictionary<String, IAiProvider> _providers;      // CLI connections
        private readonly Dictionary<String, IAiProvider> _apiProviders;   // API connections
        private readonly IHostBridge _host;
        private readonly String _dataDirectory;
        private readonly String _settingsPath;
        private readonly UiServer _server;
        private readonly ConversationStore _history;
        private readonly PresetCatalog _presets;
        private readonly Timer _cleanupTimer;
        private readonly SemaphoreSlim _clipboardGate = new SemaphoreSlim(1, 1);
        private QuickPromptSettings _settings;
        private DateTime _settingsReadUtc;

        /// <param name="providers">CLI providers (tests pass fakes here).</param>
        /// <param name="apiProviders">API providers (tests pass fakes here).</param>
        public ChatManager(String dataDirectory, IHostBridge host, IEnumerable<IAiProvider> providers = null, IEnumerable<IAiProvider> apiProviders = null)
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
            var apiList = apiProviders?.ToList() ?? new List<IAiProvider>
            {
                new AnthropicApiProvider(() => this.Settings, () => host.GetSecret(AnthropicApiProvider.SecretName)),
                new OpenAiApiProvider(() => this.Settings, () => host.GetSecret(OpenAiApiProvider.SecretName)),
            };
            this._apiProviders = apiList.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);

            this._server = new UiServer(this);
            this._history = new ConversationStore(Path.Combine(dataDirectory, "history"));
            this._presets = new PresetCatalog(Path.Combine(dataDirectory, "presets.json"));
            this.Launcher = new WindowLauncher(() => this.Settings, Path.Combine(dataDirectory, "browser-profile"), host);
            this._cleanupTimer = new Timer(_ => this.Cleanup(), null, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
        }

        public String SettingsPath => this._settingsPath;

        /// <summary>Set by the desktop app's engine (QuickPrompt.Agent) to enable the /api control endpoints.</summary>
        public AppControl Control { get; set; }

        /// <summary>Runs a saved preset ("Prompt templates") on the current selection.</summary>
        public async Task<Boolean> RunSavedPresetAsync(String presetId)
        {
            var saved = this._presets.Find(presetId);
            if (saved == null)
            {
                return false;
            }

            await this.ExecutePresetAsync(new Preset
            {
                ProviderId = saved.ProviderId,
                Title = saved.Title,
                Instruction = saved.Instruction,
                Connection = saved.Connection,
                Model = saved.Model,
                Setup = saved.SetupFor(),
                // Custom templates read what their system parameters ask for; older ones keep their setting.
                Input = saved.Custom ? PromptBuilder.InputFor(saved.Instruction) : InputModes.Normalize(saved.Input),
                Flow = FlowModes.Send,
            }).ConfigureAwait(false);
            return true;
        }

        /// <summary>Product version ("0.3.42"), from the assembly's informational version.</summary>
        public static String AppVersion
        {
            get
            {
                var info = typeof(ChatManager).Assembly
                    .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                    .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
                    .FirstOrDefault()?.InformationalVersion ?? "";
                var plus = info.IndexOf('+');
                return plus > 0 ? info.Substring(0, plus) : info;
            }
        }


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

        public IAiProvider GetProvider(String id) => this.GetProvider(id, ConnectionModes.Default);

        /// <summary>The provider for "claude"/"codex" over the given connection ("default" = from Settings).</summary>
        public IAiProvider GetProvider(String id, String connection)
        {
            var resolved = this.ResolveConnection(id, connection);
            var table = resolved == ConnectionModes.Api ? this._apiProviders : this._providers;
            return table.TryGetValue(id ?? "", out var provider) ? provider : table.Values.First();
        }

        public String ResolveConnection(String providerId, String connection)
        {
            var normalized = ConnectionModes.Normalize(connection, ConnectionModes.Default);
            if (normalized != ConnectionModes.Default)
            {
                return normalized;
            }

            var settings = this.Settings;
            return String.Equals(providerId, ProviderIds.Codex, StringComparison.OrdinalIgnoreCase) ? settings.ChatGptConnection : settings.ClaudeConnection;
        }

        public Boolean TryGetSession(String token, out ChatSession session) =>
            this._sessions.TryGetValue(token ?? "", out session);

        public ChatSession CreateSession(String providerId, String title, String model, String connection = ConnectionModes.Default)
        {
            var resolved = this.ResolveConnection(providerId, connection);
            var session = new ChatSession(this.GetProvider(providerId, resolved), () => this.Settings)
            {
                Title = title ?? "",
                Model = model ?? "",
                Connection = resolved,
            };
            session.TurnFinished += this.OnTurnFinished;
            this._sessions[session.Token] = session;
            return session;
        }

        public String GetSessionUrl(ChatSession session, Boolean appWindow) =>
            $"http://127.0.0.1:{this.Port}/s/{session.Token}/" + (appWindow ? "" : "?tab=1");

        public void OpenWindow(ChatSession session) =>
            this.Launcher.Open(appWindow => this.GetSessionUrl(session, appWindow));

        private void OnTurnFinished(ChatSession session)
        {
            if (!this.Settings.SaveHistory)
            {
                return;
            }

            try
            {
                this._history.Save(session.ToRecord());
            }
            catch (Exception ex)
            {
                this._host.Log("Could not save the conversation", ex);
            }
        }

        /// <summary>Opens the popup on the History view.</summary>
        public void OpenHistory()
        {
            var session = this.CreateSession(ProviderIds.Claude, "", "");
            session.ShowHistory = true;
            this.OpenWindow(session);
        }

        /// <summary>Recent conversations for the History view, newest first.</summary>
        public String GetHistoryJson()
        {
            var items = this.Settings.SaveHistory ? this._history.List() : new List<ConversationRecord>();
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                enabled = this.Settings.SaveHistory,
                items = items.Select(r =>
                {
                    var question = r.Messages.FirstOrDefault(m => m.Role == "user");
                    var answer = r.Messages.FirstOrDefault(m => m.Role == "assistant");
                    return new
                    {
                        id = r.Id,
                        title = r.Title ?? "",
                        provider = r.ProviderId,
                        providerName = this.GetProvider(r.ProviderId, r.Connection).DisplayName,
                        connection = r.Connection,
                        question = Shorten(question == null ? "" : PromptBuilder.Build(question.Text, question.Context), 160),
                        answer = Shorten(answer?.Text ?? "", 160),
                        turns = r.Messages.Count(m => m.Role == "user"),
                        updated = r.UpdatedUtc.ToString("o"),
                    };
                }).ToArray(),
            });
        }

        /// <summary>Presets for "Prompt templates": the ones used from the ring, then built-ins.</summary>
        public String GetPresetsJson() =>
            System.Text.Json.JsonSerializer.Serialize(this._presets.List().Select(p => new
            {
                id = p.Id,
                title = p.Title,
                provider = p.ProviderId,
                providerName = this.GetProvider(p.ProviderId, p.Connection).DisplayName,
                connection = this.ResolveConnection(p.ProviderId, p.Connection),
                builtIn = p.BuiltIn,
            }).ToArray());

        /// <summary>The Prompt templates editor: your templates and the built-ins.</summary>
        public String GetActionsJson() =>
            System.Text.Json.JsonSerializer.Serialize(this._presets.ListEditable().Select(p => new
            {
                id = p.Id,
                title = p.Title,
                instruction = p.Instruction,
                provider = p.ProviderId,
                connection = p.Connection,
                input = p.Custom || p.BuiltIn ? PromptBuilder.InputFor(p.Instruction) : InputModes.Normalize(p.Input),
                model = p.Model,
                builtIn = p.BuiltIn,
                fields = PromptBuilder.GetParameters(p.Instruction).Count,
                setup = SetupModes.Normalize(p.Setup),
                enabled = p.Enabled,
                setupChoice = p.SetupChoice == null ? null : new
                {
                    skills = p.SetupChoice.Skills,
                    web = p.SetupChoice.Web,
                    disabledPlugins = p.SetupChoice.DisabledPlugins,
                    disabledServers = p.SetupChoice.DisabledServers,
                    disabledSkills = p.SetupChoice.DisabledSkills,
                },
            }).ToArray());

        public String SaveAction(IReadOnlyDictionary<String, String> body)
        {
            String Get(String key) => body.TryGetValue(key, out var v) ? v ?? "" : "";
            this._presets.Save(new SavedPreset
            {
                Id = Get("id"),
                Title = Get("title"),
                Instruction = Get("instruction"),
                ProviderId = Get("provider"),
                Connection = Get("connection"),
                Input = PromptBuilder.InputFor(Get("instruction")),
                Model = Get("model"),
                Setup = SetupModes.Normalize(Get("setup")),
                Enabled = Get("enabled") != "false",
                SetupChoice = ParseSetupChoice(Get("setupChoice"), new CliSetupChoice { Enabled = true }),
            });
            return this.GetActionsJson();
        }

        public String SetActionEnabled(String id, Boolean enabled)
        {
            this._presets.SetEnabled(id, enabled);
            return this.GetActionsJson();
        }

        /// <summary>The chosen templates as a file to share: { "quickPromptTemplates": 1, "templates": [...] }.</summary>
        public String ExportActions(IEnumerable<String> ids)
        {
            var wanted = new HashSet<String>(ids ?? Array.Empty<String>());
            var templates = this._presets.ListEditable().Where(p => wanted.Contains(p.Id)).Select(p => new
            {
                title = p.Title,
                instruction = p.Instruction,
                provider = p.ProviderId,
                connection = p.Connection,
                model = p.Model,
                enabled = p.Enabled,
                setup = SetupModes.Normalize(p.Setup),
                setupChoice = p.SetupChoice == null ? null : new
                {
                    skills = p.SetupChoice.Skills,
                    web = p.SetupChoice.Web,
                    disabledPlugins = p.SetupChoice.DisabledPlugins,
                    disabledServers = p.SetupChoice.DisabledServers,
                    disabledSkills = p.SetupChoice.DisabledSkills,
                },
            }).ToArray();
            return System.Text.Json.JsonSerializer.Serialize(new { quickPromptTemplates = 1, exported = DateTime.UtcNow.ToString("o"), templates },
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }

        /// <summary>Adds the templates of an exported file. Returns { added, skipped, actions } or { error }.</summary>
        public String ImportActions(String json)
        {
            var templates = new List<SavedPreset>();
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json ?? "");
                if (!doc.RootElement.TryGetProperty("templates", out var list) || list.ValueKind != System.Text.Json.JsonValueKind.Array)
                {
                    return System.Text.Json.JsonSerializer.Serialize(new { error = "This is not a Quick Prompt templates file." });
                }

                foreach (var item in list.EnumerateArray())
                {
                    String Str(String name) => item.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : "";
                    templates.Add(new SavedPreset
                    {
                        Title = Str("title"),
                        Instruction = Str("instruction"),
                        ProviderId = Str("provider"),
                        Connection = Str("connection"),
                        Model = Str("model"),
                        Input = PromptBuilder.InputFor(Str("instruction")),
                        Setup = SetupModes.Normalize(Str("setup")),
                        SetupChoice = item.TryGetProperty("setupChoice", out var choice) && choice.ValueKind == System.Text.Json.JsonValueKind.Object
                            ? ParseSetupChoice(choice.GetRawText(), new CliSetupChoice { Enabled = true })
                            : null,
                        Enabled = !item.TryGetProperty("enabled", out var en) || en.ValueKind != System.Text.Json.JsonValueKind.False,
                    });
                }
            }
            catch (System.Text.Json.JsonException)
            {
                return System.Text.Json.JsonSerializer.Serialize(new { error = "This file could not be read." });
            }

            var (added, skipped) = this._presets.Import(templates);
            return "{\"added\":" + added + ",\"skipped\":" + skipped + ",\"actions\":" + this.GetActionsJson() + "}";
        }

        public String DeleteAction(String id)
        {
            this._presets.Delete(id);
            return this.GetActionsJson();
        }

        /// <summary>Shows Pip on its own (at start, or from the menu bar). show = also turn "Keep Pip on screen" on.</summary>
        public Boolean ShowPip(Boolean show)
        {
            if (show && !this.Settings.KeepPip)
            {
                this.Settings.KeepPip = true;
                this.Settings.Save(this._settingsPath);
                this._settingsReadUtc = SafeLastWrite(this._settingsPath);
            }

            if (!this.Settings.KeepPip)
            {
                return false;
            }

            var session = this.CreateSession(ProviderIds.Claude, "", "");
            session.StartCollapsed = true;
            this.OpenWindow(session);
            return true;
        }

        /// <summary>Opens the popup on the Prompt templates editor.</summary>
        public void OpenActions()
        {
            var session = this.CreateSession(ProviderIds.Claude, "", "");
            session.ShowActions = true;
            this.OpenWindow(session);
        }

        /// <summary>Turns the popup into the chosen preset, waiting for pasted text.</summary>
        public Boolean UsePreset(ChatSession session, String presetId)
        {
            var preset = this._presets.Find(presetId);
            if (preset == null)
            {
                return false;
            }

            var connection = this.ResolveConnection(preset.ProviderId, preset.Connection);
            var instruction = PromptBuilder.FillClipboard(preset.Instruction, this._host.GetClipboardText());
            if (!session.ApplyPreset(this.GetProvider(preset.ProviderId, connection), connection, preset.Title, preset.Model, instruction))
            {
                return false;
            }

            session.Setup = preset.SetupFor();
            return true;
        }

        /// <summary>Loads a saved conversation into the popup so it can be continued.</summary>
        public Boolean ResumeConversation(ChatSession session, String historyId)
        {
            var record = this._history.Load(historyId);
            return record != null && session.ResumeFrom(record, this.GetProvider(record.ProviderId, record.Connection));
        }

        public void DeleteConversation(String historyId) => this._history.Delete(historyId);

        public void ClearHistory() => this._history.Clear();

        private static String Shorten(String text, Int32 max)
        {
            var oneLine = System.Text.RegularExpressions.Regex.Replace(text ?? "", @"\s+", " ").Trim();
            return oneLine.Length <= max ? oneLine : oneLine.Substring(0, max - 1) + "…";
        }

        /// <summary>Opens the popup on the Settings view (keys, connections, models).</summary>
        public void OpenSettings()
        {
            var session = this.CreateSession(ProviderIds.Claude, "Settings", "");
            session.ShowSettings = true;
            this.OpenWindow(session);
        }

        /// <summary>Settings as shown in the popup. API keys are never returned, only whether one is set.</summary>
        public String GetSettingsJson()
        {
            var s = this.Settings;
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                version = AppVersion,
                saveHistory = s.SaveHistory,
                popupScale = s.PopupScale ?? QuickPromptSettings.DefaultPopupScale,
                iconScale = s.IconScale,
                glassTint = s.GlassTint,
                keepPip = s.KeepPip,
                hotkey = QuickPromptSettings.IsValidHotkey(s.Hotkey) ? s.Hotkey : QuickPromptSettings.DefaultHotkey,
                claude = new
                {
                    connection = s.ClaudeConnection,
                    model = s.ClaudeApiModel,
                    effort = s.ClaudeApiEffort,
                    keyHint = KeyHint(this._host.GetSecret(AnthropicApiProvider.SecretName)),
                    setup = s.ClaudeSetup?.Enabled == true,
                },
                chatgpt = new
                {
                    connection = s.ChatGptConnection,
                    model = s.OpenAiApiModel,
                    keyHint = KeyHint(this._host.GetSecret(OpenAiApiProvider.SecretName)),
                    setup = s.CodexSetup?.Enabled == true,
                },
            });
        }

        /// <summary>What the CLIs have installed and what Quick Prompt may use (Settings → "Use my setup").</summary>
        public String GetSetupJson(Boolean refresh)
        {
            var s = this.Settings;
            var claudeExe = CliLocator.Find("claude", s.ClaudePath);
            var codexExe = CliLocator.Find("codex", s.CodexPath);
            var claude = claudeExe != null ? CliSetup.DiscoverClaude(claudeExe, refresh) : new CliSetupInventory();
            var codex = CliSetup.DiscoverCodex();

            Object Describe(CliSetupChoice choice, CliSetupInventory inventory, Boolean installed) => new
            {
                installed,
                enabled = choice.Enabled,
                skills = choice.Skills,
                web = choice.Web,
                plugins = inventory.Plugins.Select(p => new { id = p.Id, cliEnabled = p.EnabledInCli, on = p.EnabledInCli && !choice.DisabledPlugins.Contains(p.Id) }).ToArray(),
                servers = inventory.Servers.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).Select(n => new { name = n, on = !choice.DisabledServers.Contains(n) }).ToArray(),
                skillList = inventory.Skills.Select(k => new { name = k.Name, plugin = k.Plugin, on = !choice.DisabledSkills.Contains(k.Name) }).ToArray(),
                error = inventory.Error,
            };

            return System.Text.Json.JsonSerializer.Serialize(new
            {
                claude = Describe(s.ClaudeSetup ??= new CliSetupChoice(), claude, claudeExe != null),
                chatgpt = Describe(s.CodexSetup ??= new CliSetupChoice(), codex, codexExe != null),
            });
        }

        private static CliSetupChoice ParseSetupChoice(String json, CliSetupChoice current)
        {
            if (String.IsNullOrWhiteSpace(json))
            {
                return current;
            }

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;
                Boolean Flag(String name, Boolean fallback) =>
                    root.TryGetProperty(name, out var v) && (v.ValueKind == System.Text.Json.JsonValueKind.True || v.ValueKind == System.Text.Json.JsonValueKind.False) ? v.GetBoolean() : fallback;
                // A list that was not sent (the setup was still loading) keeps its saved value.
                List<String> Names(String name, List<String> saved) =>
                    root.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Array
                        ? v.EnumerateArray().Where(e => e.ValueKind == System.Text.Json.JsonValueKind.String).Select(e => e.GetString()).Distinct().ToList()
                        : saved ?? new List<String>();
                return new CliSetupChoice
                {
                    Enabled = Flag("enabled", current.Enabled),
                    Skills = Flag("skills", current.Skills),
                    Web = Flag("web", current.Web),
                    DisabledPlugins = Names("disabledPlugins", current.DisabledPlugins),
                    DisabledServers = Names("disabledServers", current.DisabledServers),
                    DisabledSkills = Names("disabledSkills", current.DisabledSkills),
                };
            }
            catch (System.Text.Json.JsonException)
            {
                return current;
            }
        }

        /// <summary>
        /// Saves settings from the popup. Keys: an empty value keeps the stored key, "-" removes it.
        /// </summary>
        public void SaveSettings(IReadOnlyDictionary<String, String> values)
        {
            String Get(String name) => values.TryGetValue(name, out var v) ? (v ?? "").Trim() : null;

            var s = this.Settings;
            s.ClaudeConnection = ConnectionModes.Normalize(Get("claudeConnection"), s.ClaudeConnection);
            s.ChatGptConnection = ConnectionModes.Normalize(Get("chatgptConnection"), s.ChatGptConnection);
            if (!String.IsNullOrEmpty(Get("claudeModel")))
            {
                s.ClaudeApiModel = Get("claudeModel");
            }

            if (!String.IsNullOrEmpty(Get("claudeEffort")))
            {
                s.ClaudeApiEffort = Get("claudeEffort");
            }

            if (!String.IsNullOrEmpty(Get("chatgptModel")))
            {
                s.OpenAiApiModel = Get("chatgptModel");
            }

            s.ClaudeSetup = ParseSetupChoice(Get("claudeSetup"), s.ClaudeSetup ?? new CliSetupChoice());
            s.CodexSetup = ParseSetupChoice(Get("chatgptSetup"), s.CodexSetup ?? new CliSetupChoice());

            if (Int32.TryParse(Get("popupScale"), out var scale))
            {
                s.PopupScale = Math.Clamp(scale, 0, 100);
            }

            var hotkey = Get("hotkey");
            if (QuickPromptSettings.IsValidHotkey(hotkey) && hotkey != s.Hotkey)
            {
                s.Hotkey = hotkey;
                this.Control?.Publish(new { type = "hotkey", value = hotkey });   // the app registers it right away
            }

            if (Get("keepPip") is "true" or "false")
            {
                s.KeepPip = Get("keepPip") == "true";
            }

            if (Int32.TryParse(Get("glassTint"), out var glassTint))
            {
                s.GlassTint = Math.Clamp(glassTint, 0, 100);
            }

            if (Int32.TryParse(Get("iconScale"), out var iconScale))
            {
                s.IconScale = Math.Clamp(iconScale, 0, 100);
            }

            if (Get("saveHistory") is "true" or "false")
            {
                s.SaveHistory = Get("saveHistory") == "true";
                if (!s.SaveHistory)
                {
                    this._history.Clear();
                }
            }

            s.Save(this._settingsPath);
            this._settingsReadUtc = SafeLastWrite(this._settingsPath);

            SaveKey(AnthropicApiProvider.SecretName, Get("claudeKey"));
            SaveKey(OpenAiApiProvider.SecretName, Get("chatgptKey"));

            void SaveKey(String name, String value)
            {
                if (!String.IsNullOrEmpty(value))
                {
                    this._host.SetSecret(name, value == "-" ? null : value);
                }
            }
        }

        private static String KeyHint(String key) =>
            String.IsNullOrWhiteSpace(key) ? "" : "••••" + key.Trim().Substring(Math.Max(0, key.Trim().Length - 4));

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

        private ChatSession CreateSession(Preset preset)
        {
            var session = this.CreateSession(preset.ProviderId, preset.Title, preset.Model, preset.Connection);
            session.Setup = preset.Setup;
            return session;
        }

        private async Task ExecutePresetCoreAsync(Preset preset)
        {
            this._presets.Remember(preset);

            var instruction = preset.Instruction ?? "";
            var flow = preset.Flow ?? FlowModes.Send;

            // "Paste in popup": nothing to read; open an empty popup that applies the preset prompt on Enter.
            if (preset.Input == InputModes.Popup)
            {
                var pasteSession = this.CreateSession(preset);
                if (instruction.Trim().Length > 0)
                {
                    pasteSession.PresetInstruction = instruction;
                }

                this.OpenWindow(pasteSession);
                return;
            }

            // {$clipboard} may be a picture (a screenshot): read it before ⌘C replaces the clipboard.
            var images = new List<ImageAttachment>();
            var clipboardImage = PromptBuilder.HasClipboard(instruction) ? this.SafeGetClipboardImage() : null;

            var capture = await this.CaptureInputAsync(preset.Input, preset.Quick).ConfigureAwait(false);
            var context = capture.Text;

            // {$selected} may be a picture too (an image selected and copied in its app).
            if (capture.Image != null)
            {
                images.Add(capture.Image);
            }

            // {$clipboard}: the clipboard as it was before the selection was copied (or the clipboard itself).
            if (PromptBuilder.HasClipboard(instruction))
            {
                var clipboardText = preset.Input == InputModes.Clipboard ? context : capture.PreviousClipboard ?? "";
                if (clipboardImage != null && (capture.Image == null || clipboardImage.Data != capture.Image.Data))
                {
                    images.Add(clipboardImage);
                    if (clipboardText.Trim().Length == 0)
                    {
                        clipboardText = "(the attached image)";
                    }
                }

                instruction = PromptBuilder.FillClipboard(instruction, clipboardText);
                if (preset.Input == InputModes.Clipboard)
                {
                    context = "";
                }
            }

            // A prompt with fields ({name} or {name:a|b}): ask for them in the popup, with the captured text ready.
            if (PromptBuilder.GetParameters(instruction).Count > 0)
            {
                var form = this.CreateSession(preset);
                form.PresetInstruction = instruction;
                form.DraftContext = context;
                form.DraftImages = images;
                if (preset.Input == InputModes.Selection && !capture.Captured && capture.Image == null)
                {
                    form.Notice = "Could not read the selected text. Paste it below if the action needs it.";
                }

                this.OpenWindow(form);
                return;
            }

            var nothingToSend = instruction.Trim().Length == 0 && context.Trim().Length == 0 && images.Count == 0;

            // The preset needs the selection but none could be read: never send the stale clipboard.
            // Open the popup instead so the text can be pasted or typed.
            var selectionMissing = preset.Input == InputModes.Selection && !capture.Captured && capture.Image == null;

            if (flow == FlowModes.Edit || nothingToSend || selectionMissing)
            {
                var draft = this.CreateSession(preset);
                draft.DraftInstruction = instruction;
                draft.DraftContext = context;
                draft.DraftImages = images;
                if (selectionMissing && flow != FlowModes.Edit)
                {
                    draft.Notice = "Could not read the selected text. Paste it below and send.";
                }

                this.OpenWindow(draft);
                return;
            }

            if (flow == FlowModes.Send)
            {
                var session = this.CreateSession(preset);
                session.StartTurn(instruction, context, images);
                this.OpenWindow(session);
                return;
            }

            // Background flows: no window unless something goes wrong.
            var background = this.CreateSession(preset);
            var providerName = background.Provider.DisplayName;
            this._host.Notify(providerName, $"{(String.IsNullOrWhiteSpace(preset.Title) ? "Request" : preset.Title)} sent…", false);
            background.StartTurn(instruction, context, images);

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

            /// <summary>For selection mode: the picture Ctrl/Cmd+C copied (a selected image), if no text came.</summary>
            public ImageAttachment Image { get; set; }
        }

        /// <summary>
        /// For "selection" we press Ctrl/Cmd+C and watch the clipboard. When the clipboard already holds
        /// text we first replace it with a unique marker, so a copy of the very same text is still detected
        /// and a failed copy is never mistaken for a selection. The old text is put back afterwards.
        /// </summary>
        private ImageAttachment SafeGetClipboardImage()
        {
            try
            {
                var image = this._host.GetClipboardImage();
                return image != null && ImageAttachment.IsSupported(image.MediaType) && image.Data.Length > 0 ? image : null;
            }
            catch (Exception ex)
            {
                this._host.Log("Could not read the clipboard picture", ex);
                return null;
            }
        }

        public async Task<CaptureResult> CaptureInputAsync(String inputMode, Boolean quick = false)
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
                // With no text on the clipboard there is no marker: note any picture already there, so an old
                // picture is never taken for the selection. (Not for the quick shortcut: reading a picture takes a moment.)
                var imageBefore = marker.Length == 0 && !quick ? this.SafeGetClipboardImage() : null;
                if (marker.Length > 0)
                {
                    this._host.SetClipboardText(marker);
                }

                // Let the Actions Ring overlay close and focus return to the app.
                if (!quick)
                {
                    await Task.Delay(this.CopyDelayMs).ConfigureAwait(false);
                }
                this._host.SendCopyShortcut();

                var started = DateTime.UtcNow;
                var retried = false;
                var text = "";
                var maxWait = quick ? 700 : 1500;
                var retryAt = quick ? 300 : 600;
                var stillMarker = false;
                while ((DateTime.UtcNow - started).TotalMilliseconds < maxWait)
                {
                    await Task.Delay(40).ConfigureAwait(false);
                    var current = this.SafeGetClipboard();
                    if (current.Length > 0 && !String.Equals(current, marker, StringComparison.Ordinal))
                    {
                        text = current;
                        break;
                    }

                    // Focus may not have been back yet: press Ctrl/Cmd+C once more.
                    stillMarker = marker.Length > 0 && String.Equals(current, marker, StringComparison.Ordinal);
                    if (!retried && (DateTime.UtcNow - started).TotalMilliseconds > retryAt)
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

                ImageAttachment image = null;
                if (!captured)
                {
                    // A selected picture: ⌘C put an image (and no text) on the clipboard. Only worth reading when the
                    // clipboard did change (our marker is gone) or there was no marker to tell.
                    var mayHavePicture = marker.Length > 0 ? !stillMarker : !quick;
                    var after = mayHavePicture ? this.SafeGetClipboardImage() : null;
                    if (after != null && (imageBefore == null || after.Data != imageBefore.Data))
                    {
                        image = after;
                    }
                    else
                    {
                        this._host.Log("Could not read the selection (Ctrl/Cmd+C did not change the clipboard).");
                    }
                }

                return new CaptureResult { Text = text, PreviousClipboard = before, Captured = captured, Image = image };
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
