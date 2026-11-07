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
            this.MigrateTemplatesToAgents();
        }

        // ---------- agents ----------

        /// <summary>The agent for an AI named by older callers (the ring's Ask Claude / Ask ChatGPT): the default
        /// agent when it is that AI, else the first agent of that AI, else one made from the older settings.</summary>
        public Agent AgentForProvider(String providerId)
        {
            var s = this.Settings;
            var id = String.Equals(providerId, ProviderIds.Codex, StringComparison.OrdinalIgnoreCase) ? ProviderIds.Codex : ProviderIds.Claude;
            var main = s.DefaultAgent();
            return main.ProviderId == id ? main
                : s.Agents.FirstOrDefault(a => a.ProviderId == id) ?? QuickPromptSettings.LegacyAgents(s).First(a => a.ProviderId == id);
        }

        /// <summary>An AI with its own connection / model / setup (ring presets, older templates): its agent with those changed.</summary>
        public Agent ResolveAgent(String providerId, String connection, String model, CliSetupChoice setup)
        {
            var agent = this.AgentForProvider(providerId).Clone();
            var own = ConnectionModes.Normalize(connection, ConnectionModes.Default);
            if (own != ConnectionModes.Default && own != agent.Connection)
            {
                agent.Connection = own;
                agent.Model = "";   // the agent's model was for its own connection
            }

            if (!String.IsNullOrWhiteSpace(model))
            {
                agent.Model = model.Trim();
            }

            if (setup != null)
            {
                agent.Setup = Agent.CloneSetup(setup);
            }

            // Changed: no longer that agent (a conversation saved with it must not come back as the agent).
            if (!agent.SameSetupAs(this.AgentForProvider(providerId)))
            {
                agent.Id = "";
            }

            return agent;
        }

        private IAiProvider GetProvider(Agent agent)
        {
            var table = agent.Connection == ConnectionModes.Api ? this._apiProviders : this._providers;
            return table.TryGetValue(agent.ProviderId, out var provider) ? provider : table.Values.First();
        }

        /// <summary>The agent a template uses: its own (an agent id; empty = the default agent), or for a template
        /// from before agents, its AI with its own connection, model and setup.</summary>
        private Agent AgentFor(SavedPreset preset) =>
            preset.AgentId != null
                ? this.Settings.FindAgent(preset.AgentId)
                : this.ResolveAgent(preset.ProviderId, preset.Connection, preset.Model, preset.SetupFor());

        /// <summary>
        /// Templates from before agents carried their own AI, connection, model and setup: each now points at the
        /// agent with that setup (the default agent when it is that one), and an agent is made for any setup that
        /// has none. Runs once; templates that already have an agent are left alone.
        /// </summary>
        private void MigrateTemplatesToAgents()
        {
            try
            {
                var s = this.Settings;
                var changed = false;
                this._presets.UpdateCustom(list =>
                {
                    foreach (var template in list.Where(t => t.AgentId == null))
                    {
                        var wanted = this.ResolveAgent(template.ProviderId, template.Connection, template.Model, template.SetupFor());
                        template.AgentId = this.AgentIdFor(wanted, s, template.Title);
                        changed = true;
                    }

                    return changed;
                });

                if (changed)
                {
                    s.Save(this._settingsPath);
                    this._settingsReadUtc = SafeLastWrite(this._settingsPath);
                }
            }
            catch (Exception ex)
            {
                this._host.Log("Could not move the templates to agents", ex);
            }
        }

        /// <summary>The id of an agent with this setup ("" for the default agent), adding one if there is none.</summary>
        private String AgentIdFor(Agent wanted, QuickPromptSettings s, String hint)
        {
            if (wanted.SameSetupAs(s.DefaultAgent()))
            {
                return "";
            }

            var same = s.Agents.FirstOrDefault(a => a.SameSetupAs(wanted));
            if (same != null)
            {
                return same.Id;
            }

            var made = wanted.Clone();
            made.Id = Agent.NewId();
            // "Claude Haiku 5.5 · API", "ChatGPT gpt-5 · API", "Claude · Reply to email" (its own plugins and skills).
            var model = System.Text.RegularExpressions.Regex.Match(made.Model, @"^claude-([a-z]+)-(\d+)-(\d+)$");
            var name = (made.ProviderId == ProviderIds.Codex ? "ChatGPT" : "Claude")
                + (model.Success ? " " + Char.ToUpperInvariant(model.Groups[1].Value[0]) + model.Groups[1].Value.Substring(1) + " " + model.Groups[2].Value + "." + model.Groups[3].Value
                    : made.Model.Length > 0 ? " " + made.Model : "")
                + (made.Connection == ConnectionModes.Api ? " · API" : "")
                + (!made.Setup.Enabled ? "" : " · " + (String.IsNullOrWhiteSpace(hint) ? "tools" : hint.Trim()));
            made.Name = UniqueAgentName(s, name);
            made.Normalize();
            s.Agents.Add(made);
            return made.Id;
        }

        private static String UniqueAgentName(QuickPromptSettings s, String name)
        {
            name = (name ?? "").Trim();
            if (name.Length > 36)
            {
                name = name.Substring(0, 36).Trim();
            }

            var result = name;
            for (var n = 2; s.Agents.Any(a => String.Equals(a.Name, result, StringComparison.OrdinalIgnoreCase)); n++)
            {
                result = $"{name} ({n})";
            }

            return result;
        }

        private static Object DescribeSetup(CliSetupChoice c) => new
        {
            enabled = c?.Enabled == true,
            skills = c?.Skills ?? true,
            web = c?.Web ?? true,
            disabledPlugins = c?.DisabledPlugins ?? new List<String>(),
            disabledServers = c?.DisabledServers ?? new List<String>(),
            disabledSkills = c?.DisabledSkills ?? new List<String>(),
        };

        private static Object DescribeAgent(Agent a, String defaultId) => new
        {
            id = a.Id,
            name = a.Name,
            color = a.Color,
            provider = a.ProviderId,
            connection = a.Connection,
            model = a.Model,
            effort = a.Effort,
            webSearch = a.WebSearch,
            instruction = a.Instruction,
            setup = DescribeSetup(a.Setup),
            isDefault = a.Id == defaultId,
        };

        /// <summary>The agents for Settings, the template editor and the chat's agent chip.</summary>
        public String GetAgentsJson()
        {
            var s = this.Settings;
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                defaultId = s.DefaultAgent().Id,
                agents = s.Agents.Select(a => DescribeAgent(a, s.DefaultAgent().Id)).ToArray(),
                palette = Agent.Palette,
            });
        }

        /// <summary>Adds or updates an agent (id empty = new). Returns the agents, or { error }.</summary>
        public String SaveAgent(IReadOnlyDictionary<String, String> body)
        {
            String Get(String key) => body.TryGetValue(key, out var v) ? v ?? "" : "";
            var s = this.Settings;
            var existing = s.Agents.FirstOrDefault(a => a.Id == Get("id"));
            var name = Get("name").Trim();
            if (name.Length == 0)
            {
                return System.Text.Json.JsonSerializer.Serialize(new { error = "Give the agent a name." });
            }

            if (s.Agents.Any(a => a != existing && String.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return System.Text.Json.JsonSerializer.Serialize(new { error = "There is already an agent with that name." });
            }

            // A new agent may bring the id the page made for it (it saves as you type and must find it again).
            var wanted = Get("id");
            var agent = existing ?? new Agent { Id = System.Text.RegularExpressions.Regex.IsMatch(wanted, "^g[0-9a-f]{10}$") ? wanted : Agent.NewId() };
            agent.Name = name;
            agent.Color = Get("color");
            agent.ProviderId = Get("provider");
            agent.Connection = Get("connection");
            agent.Model = Get("model");
            agent.Effort = Get("effort");
            agent.WebSearch = Get("webSearch") == "true";
            agent.Instruction = Get("instruction");
            agent.Setup = ParseSetupChoice(Get("setup"), agent.Setup ?? new CliSetupChoice());
            agent.Normalize();
            if (existing == null)
            {
                s.Agents.Add(agent);
            }

            if (Get("makeDefault") == "true")
            {
                s.DefaultAgentId = agent.Id;
            }

            s.Save(this._settingsPath);
            this._settingsReadUtc = SafeLastWrite(this._settingsPath);
            return this.GetAgentsJson();
        }

        /// <summary>Removes an agent (not the default one, and never the last). Its templates use the default agent.</summary>
        public String DeleteAgent(String id)
        {
            var s = this.Settings;
            if (s.Agents.Count <= 1 || id == s.DefaultAgent().Id)
            {
                return System.Text.Json.JsonSerializer.Serialize(new { error = "Make another agent the default first." });
            }

            if (s.Agents.RemoveAll(a => a.Id == id) > 0)
            {
                s.Save(this._settingsPath);
                this._settingsReadUtc = SafeLastWrite(this._settingsPath);
                this._presets.UpdateCustom(list =>
                {
                    var used = list.Where(t => t.AgentId == id).ToList();
                    used.ForEach(t => t.AgentId = "");
                    return used.Count > 0;
                });
            }

            return this.GetAgentsJson();
        }

        public String SetDefaultAgent(String id)
        {
            var s = this.Settings;
            if (s.Agents.Any(a => a.Id == id))
            {
                s.DefaultAgentId = id;
                s.Save(this._settingsPath);
                this._settingsReadUtc = SafeLastWrite(this._settingsPath);
            }

            return this.GetAgentsJson();
        }

        /// <summary>The chat's agent chip: the next message goes to this agent, with the conversation so far.</summary>
        public Boolean SetSessionAgent(ChatSession session, String agentId)
        {
            var agent = this.Settings.Agents.FirstOrDefault(a => a.Id == agentId);
            return agent != null && session.UseAgent(this.GetProvider(agent), agent);
        }

        /// <summary>A popup with the given agent (null = the default agent).</summary>
        public ChatSession CreateSession(Agent agent, String title)
        {
            agent ??= this.Settings.DefaultAgent();
            var session = new ChatSession(this.GetProvider(agent), () => this.Settings)
            {
                Title = title ?? "",
                Preview = () => this._lookPreview,
            };
            session.UseAgent(this.GetProvider(agent), agent);
            session.TurnFinished += this.OnTurnFinished;
            session.AnswerToCopy += this.OnAnswerToCopy;
            this._sessions[session.Token] = session;
            return session;
        }

        public String SettingsPath => this._settingsPath;

        /// <summary>Set by the desktop app's engine (QuickPrompt.Agent) to enable the /api control endpoints.</summary>
        public AppControl Control { get; set; }

        /// <summary>Local voices for reading answers aloud; null where the host has none (the Logi plugin).</summary>
        public IReadAloud ReadAloud { get; set; }

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
                AgentId = saved.AgentId,
                AutoCopy = saved.AutoCopy,
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

            return this.AgentForProvider(providerId).Connection;
        }

        public Boolean TryGetSession(String token, out ChatSession session) =>
            this._sessions.TryGetValue(token ?? "", out session);

        /// <summary>A popup with an AI named the older way (its agent, with this connection and model).</summary>
        public ChatSession CreateSession(String providerId, String title, String model, String connection = ConnectionModes.Default) =>
            this.CreateSession(this.ResolveAgent(providerId, connection, model, null), title);

        public String GetSessionUrl(ChatSession session, Boolean appWindow) =>
            $"http://127.0.0.1:{this.Port}/s/{session.Token}/" + (appWindow ? "" : "?tab=1");

        public void OpenWindow(ChatSession session) =>
            this.Launcher.Open(appWindow => this.GetSessionUrl(session, appWindow));

        /// <summary>A template with "Copy the first answer" finished its first answer: onto the clipboard.</summary>
        private void OnAnswerToCopy(ChatSession session, String text)
        {
            try
            {
                this._host.SetClipboardText(text);
            }
            catch (Exception ex)
            {
                this._host.Log("Could not copy the answer", ex);
            }
        }

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
            var session = this.CreateSession(null, "");
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
                        providerName = this.Settings.Agents.FirstOrDefault(a => a.Id == r.AgentId)?.Name ?? this.GetProvider(r.ProviderId, r.Connection).DisplayName,
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
            System.Text.Json.JsonSerializer.Serialize(this._presets.List().Select(p =>
            {
                var agent = this.AgentFor(p);
                return new
                {
                    id = p.Id,
                    title = p.Title,
                    provider = agent.ProviderId,
                    providerName = agent.Name,
                    color = agent.Color,
                    connection = agent.Connection,
                    builtIn = p.BuiltIn,
                };
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
                agent = p.AgentId ?? "",
                agentName = this.AgentFor(p).Name,
                agentColor = this.AgentFor(p).Color,
                builtIn = p.BuiltIn,
                fields = PromptBuilder.GetParameters(p.Instruction).Count,
                setup = SetupModes.Normalize(p.Setup),
                enabled = p.Enabled,
                autoCopy = p.AutoCopy,
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
                AgentId = this.Settings.Agents.Any(a => a.Id == Get("agent")) ? Get("agent") : "",
                ProviderId = this.Settings.FindAgent(Get("agent")).ProviderId,
                Input = PromptBuilder.InputFor(Get("instruction")),
                Enabled = Get("enabled") != "false",
                AutoCopy = Get("autoCopy") == "true",
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
            // A template keeps its agent's setup (an agent with that name and setup is used or made on import);
            // a template that uses the default agent goes on using the default agent wherever it is imported.
            var templates = this._presets.ListEditable().Where(p => wanted.Contains(p.Id)).Select(p =>
            {
                var own = String.IsNullOrEmpty(p.AgentId) ? null : this.AgentFor(p);
                return new
                {
                    title = p.Title,
                    instruction = p.Instruction,
                    enabled = p.Enabled,
                    autoCopy = p.AutoCopy,
                    agent = own == null ? null : new
                    {
                        name = own.Name,
                        color = own.Color,
                        provider = own.ProviderId,
                        connection = own.Connection,
                        model = own.Model,
                        effort = own.Effort,
                        webSearch = own.WebSearch,
                        instruction = own.Instruction,
                        setup = DescribeSetup(own.Setup),
                    },
                };
            }).ToArray();
            return System.Text.Json.JsonSerializer.Serialize(new { quickPromptTemplates = 2, exported = DateTime.UtcNow.ToString("o"), templates },
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

                var s = this.Settings;
                var agentsBefore = s.Agents.Count;
                foreach (var item in list.EnumerateArray())
                {
                    String Str(System.Text.Json.JsonElement e, String name) => e.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : "";
                    String agentId;
                    if (item.TryGetProperty("agent", out var a) && a.ValueKind == System.Text.Json.JsonValueKind.Object)
                    {
                        // Its own agent: the one here with that name and setup, else one with that setup, else a new one.
                        var wanted = new Agent
                        {
                            Name = Str(a, "name"), Color = Str(a, "color"), ProviderId = Str(a, "provider"), Connection = Str(a, "connection"),
                            Model = Str(a, "model"), Effort = Str(a, "effort"), Instruction = Str(a, "instruction"),
                            WebSearch = a.TryGetProperty("webSearch", out var ws) && ws.ValueKind == System.Text.Json.JsonValueKind.True,
                            Setup = a.TryGetProperty("setup", out var setup) && setup.ValueKind == System.Text.Json.JsonValueKind.Object
                                ? ParseSetupChoice(setup.GetRawText(), new CliSetupChoice()) : new CliSetupChoice(),
                        };
                        wanted.Normalize();
                        var named = s.Agents.FirstOrDefault(x => String.Equals(x.Name, wanted.Name, StringComparison.OrdinalIgnoreCase) && x.SameSetupAs(wanted));
                        agentId = named != null ? named.Id : this.AgentIdFor(wanted, s, wanted.Name);
                        var made = s.Agents.FirstOrDefault(x => x.Id == agentId);
                        if (made != null && s.Agents.IndexOf(made) >= agentsBefore)
                        {
                            made.Name = "";
                            made.Name = UniqueAgentName(s, wanted.Name);
                            made.Color = wanted.Color;
                        }
                    }
                    else if (item.TryGetProperty("provider", out _))
                    {
                        // A file from before agents: its AI with its own connection, model and setup.
                        var choice = item.TryGetProperty("setupChoice", out var c) && c.ValueKind == System.Text.Json.JsonValueKind.Object
                            ? ParseSetupChoice(c.GetRawText(), new CliSetupChoice { Enabled = true }) : null;
                        var legacy = new SavedPreset { Setup = SetupModes.Normalize(Str(item, "setup")), SetupChoice = choice };
                        agentId = this.AgentIdFor(this.ResolveAgent(Str(item, "provider"), Str(item, "connection"), Str(item, "model"), legacy.SetupFor()), s, Str(item, "title"));
                    }
                    else
                    {
                        agentId = "";
                    }

                    templates.Add(new SavedPreset
                    {
                        Title = Str(item, "title"),
                        Instruction = Str(item, "instruction"),
                        AgentId = agentId,
                        ProviderId = s.FindAgent(agentId).ProviderId,
                        Input = PromptBuilder.InputFor(Str(item, "instruction")),
                        Enabled = !item.TryGetProperty("enabled", out var en) || en.ValueKind != System.Text.Json.JsonValueKind.False,
                        AutoCopy = item.TryGetProperty("autoCopy", out var ac) && ac.ValueKind == System.Text.Json.JsonValueKind.True,
                    });
                }

                if (s.Agents.Count != agentsBefore)
                {
                    s.Save(this._settingsPath);
                    this._settingsReadUtc = SafeLastWrite(this._settingsPath);
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

            var session = this.CreateSession(null, "");
            session.StartCollapsed = true;
            this.OpenWindow(session);
            return true;
        }

        /// <summary>Opens the popup on the Prompt templates editor.</summary>
        /// <summary>Prompt templates (menu bar): the Settings window on its Prompt templates section.</summary>
        public void OpenActions() => this.OpenSettings("templates");

        /// <summary>Turns the popup into the chosen preset, waiting for pasted text.</summary>
        public Boolean UsePreset(ChatSession session, String presetId)
        {
            var preset = this._presets.Find(presetId);
            if (preset == null)
            {
                return false;
            }

            var agent = this.AgentFor(preset);
            var instruction = PromptBuilder.FillClipboard(preset.Instruction, this._host.GetClipboardText());
            if (!session.ApplyPreset(this.GetProvider(agent), agent, preset.Title, instruction))
            {
                return false;
            }

            session.AutoCopyFirstAnswer = preset.AutoCopy;
            return true;
        }

        /// <summary>A plain, empty conversation with an agent (Pip's chat closed, or New Chat); empty id = the default agent.</summary>
        public Boolean ResetSession(ChatSession session, String agentId = "")
        {
            var agent = this.Settings.FindAgent(agentId);
            return session.Reset(this.GetProvider(agent), agent);
        }

        /// <summary>Loads a saved conversation into the popup so it can be continued.</summary>
        public Boolean ResumeConversation(ChatSession session, String historyId)
        {
            var record = this._history.Load(historyId);
            if (record == null)
            {
                return false;
            }

            // The agent it was held with, if it is still there; otherwise it goes on as it was saved.
            var agent = this.Settings.Agents.FirstOrDefault(a => a.Id == record.AgentId);
            return agent != null
                ? session.ResumeFrom(record, this.GetProvider(agent), agent) && session.UseAgent(this.GetProvider(agent), agent)
                : session.ResumeFrom(record, this.GetProvider(record.ProviderId, record.Connection));
        }

        public void DeleteConversation(String historyId) => this._history.Delete(historyId);

        public void ClearHistory() => this._history.Clear();

        private static String Shorten(String text, Int32 max)
        {
            var oneLine = System.Text.RegularExpressions.Regex.Replace(text ?? "", @"\s+", " ").Trim();
            return oneLine.Length <= max ? oneLine : oneLine.Substring(0, max - 1) + "…";
        }

        /// <summary>
        /// Opens Settings in a window of its own, beside Pip (not in Pip's popup), optionally on one pane.
        /// An earlier Settings window is replaced by the app.
        /// </summary>
        public void OpenSettings(String pane = null)
        {
            var session = this.CreateSession(null, "Settings");
            session.ShowSettings = true;
            session.IsSettingsWindow = true;
            var query = "window=settings" + (IsPaneName(pane) ? "&pane=" + pane : "");
            this.Launcher.Open(appWindow => this.GetSessionUrl(session, appWindow) + (appWindow ? "?" : "&") + query);
        }

        private static Boolean IsPaneName(String pane) =>
            !String.IsNullOrEmpty(pane) && pane.Length <= 20 && pane.All(c => c is >= 'a' and <= 'z');

        private volatile LookPreview _lookPreview;

        /// <summary>
        /// Sizes and glass moved in the Settings window: every open popup (Pip, the conversation) shows them at
        /// once, before Save. clear=true (Cancel, the window closed) puts the saved values back.
        /// </summary>
        public void PreviewLook(IReadOnlyDictionary<String, String> body)
        {
            String Get(String key) => body.TryGetValue(key, out var v) ? v ?? "" : "";
            Int32? Value(String key) => Int32.TryParse(Get(key), out var v) ? Math.Clamp(v, 0, 100) : null;
            var next = Get("clear") == "true" ? null : new LookPreview { PopupScale = Value("popupScale"), IconScale = Value("iconScale"), GlassTint = Value("glassTint") };
            if (next == null && this._lookPreview == null)
            {
                return;
            }

            this._lookPreview = next;
            this.RefreshSessions();
        }

        /// <summary>A diagnostic line from the popup page (why something was not read aloud, say), into the engine log.</summary>
        public void LogFromPage(String text)
        {
            var line = (text ?? "").Replace('\n', ' ').Replace('\r', ' ');
            this._host.Log("Page: " + (line.Length > 300 ? line.Substring(0, 300) : line));
        }

        /// <summary>Every open page fetches its state again (settings changed).</summary>
        private void RefreshSessions()
        {
            foreach (var session in this._sessions.Values)
            {
                session.Refresh();
            }
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
                speechLanguages = QuickPromptSettings.IsValidSpeechLanguages(s.SpeechLanguages) ? s.SpeechLanguages : "",
                speechHints = s.SpeechHints ?? "",
                readAloud = s.ReadAloud,
                readVoices = QuickPromptSettings.CleanReadVoices(s.ReadVoices),
                readRate = Math.Clamp(s.ReadRate, 50, 200),
                conversationEnd = s.ConversationEnd == "manual" ? "manual" : "silence",
                conversationHotkey = QuickPromptSettings.IsValidHotkey(s.ConversationHotkey) ? s.ConversationHotkey : QuickPromptSettings.DefaultConversationHotkey,
                hotkey = QuickPromptSettings.IsValidHotkey(s.Hotkey) ? s.Hotkey : QuickPromptSettings.DefaultHotkey,
                dictationHotkey = QuickPromptSettings.IsValidHotkey(s.DictationHotkey) ? s.DictationHotkey : QuickPromptSettings.DefaultDictationHotkey,
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

            var dictationHotkey = Get("dictationHotkey");
            if (QuickPromptSettings.IsValidHotkey(dictationHotkey) && dictationHotkey != s.DictationHotkey)
            {
                s.DictationHotkey = dictationHotkey;
                this.Control?.Publish(new { type = "dictationHotkey", value = dictationHotkey });
            }

            var conversationHotkey = Get("conversationHotkey");
            if (QuickPromptSettings.IsValidHotkey(conversationHotkey) && conversationHotkey != s.ConversationHotkey)
            {
                s.ConversationHotkey = conversationHotkey;
                this.Control?.Publish(new { type = "conversationHotkey", value = conversationHotkey });
            }

            if (Get("conversationEnd") is "silence" or "manual")
            {
                s.ConversationEnd = Get("conversationEnd");
            }

            if (Get("keepPip") is "true" or "false")
            {
                s.KeepPip = Get("keepPip") == "true";
            }

            if (QuickPromptSettings.IsValidSpeechLanguages(Get("speechLanguages")))
            {
                s.SpeechLanguages = Get("speechLanguages")!;
            }

            if (Get("speechHints") is { } speechHints)
            {
                s.SpeechHints = speechHints.Length > 600 ? speechHints.Substring(0, 600) : speechHints;
            }

            if (Get("readAloud") is "true" or "false")
            {
                s.ReadAloud = Get("readAloud") == "true";
            }

            if (Get("readVoices") is { } readVoices)
            {
                s.ReadVoices = QuickPromptSettings.CleanReadVoices(readVoices);
            }

            if (Int32.TryParse(Get("readRate"), out var readRate))
            {
                s.ReadRate = Math.Clamp(readRate, 50, 200);
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
            this._lookPreview = null;
            this.RefreshSessions();   // Pip and the conversation take the saved sizes now, not at their next change

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
            var agent = preset.AgentId != null
                ? this.Settings.FindAgent(preset.AgentId)
                : this.ResolveAgent(preset.ProviderId, preset.Connection, preset.Model, preset.Setup);
            var session = this.CreateSession(agent, preset.Title);
            session.AutoCopyFirstAnswer = preset.AutoCopy;
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

        private void Cleanup() => this.RemoveStaleSessions(DateTime.UtcNow);

        /// <summary>Forgets sessions that have been idle for three hours with no window looking at them.</summary>
        public void RemoveStaleSessions(DateTime nowUtc)
        {
            var cutoff = nowUtc.AddHours(-3);
            var removed = 0;
            foreach (var pair in this._sessions)
            {
                if (pair.Value.IsStale(cutoff) && this._sessions.TryRemove(pair.Key, out _))
                {
                    removed++;
                }
            }

            if (removed > 0)
            {
                this._host.Log($"Forgot {removed} idle session(s)");
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
