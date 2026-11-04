namespace QuickPrompt.Core
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;

    /// <summary>A preset offered under "Prompt templates" in the floating icon's toolbar.</summary>
    public sealed class SavedPreset
    {
        public String Id { get; set; } = "";

        public String Title { get; set; } = "";

        public String Instruction { get; set; } = "";

        public String ProviderId { get; set; } = ProviderIds.Claude;

        public String Connection { get; set; } = ConnectionModes.Default;

        public String Model { get; set; } = "";

        /// <summary>Where the text comes from when run from the menu bar (InputModes).</summary>
        public String Input { get; set; } = InputModes.Selection;

        public DateTime LastUsedUtc { get; set; }

        public Boolean BuiltIn { get; set; }

        /// <summary>Made or edited in the Prompt templates editor (not remembered from the ring).</summary>
        public Boolean Custom { get; set; }

        /// <summary>Put the first answer on the clipboard as soon as it is complete.</summary>
        public Boolean AutoCopy { get; set; }

        /// <summary>Switched off: kept in the editor but not offered in Pip's list or the menu bar.</summary>
        public Boolean Enabled { get; set; } = true;

        /// <summary>CLI setup for this action: SetupModes (default = as in Settings).</summary>
        public String Setup { get; set; } = SetupModes.Default;

        /// <summary>The action's own plugins / MCP servers / skills, when Setup is "custom".</summary>
        public CliSetupChoice SetupChoice { get; set; }

        /// <summary>The setup a run of this action uses; null = as in Settings.</summary>
        public CliSetupChoice SetupFor() => SetupModes.Normalize(this.Setup) switch
        {
            SetupModes.Off => new CliSetupChoice { Enabled = false },
            SetupModes.Custom => Clone(this.SetupChoice ?? new CliSetupChoice(), enabled: true),
            _ => null,
        };

        private static CliSetupChoice Clone(CliSetupChoice c, Boolean enabled) => new CliSetupChoice
        {
            Enabled = enabled,
            Skills = c.Skills,
            Web = c.Web,
            DisabledPlugins = new List<String>(c.DisabledPlugins ?? new List<String>()),
            DisabledServers = new List<String>(c.DisabledServers ?? new List<String>()),
            DisabledSkills = new List<String>(c.DisabledSkills ?? new List<String>()),
        };
    }

    /// <summary>How an action picks its CLI setup.</summary>
    public static class SetupModes
    {
        public const String Default = "default";

        public const String Off = "off";

        public const String Custom = "custom";

        public static String Normalize(String value) => value == Off || value == Custom ? value : Default;
    }

    /// <summary>
    /// Options+ does not let a plugin list the presets configured on the ring, so every preset that is
    /// used is remembered here (presets.json, this computer only), next to a few built-in actions.
    /// </summary>
    public sealed class PresetCatalog
    {
        public const Int32 MaxRemembered = 30;

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };

        private static readonly SavedPreset[] BuiltIns =
        {
            BuiltIn("Summarize", "Summarize the following in 3 short bullet points:\n\n{$selected}"),
            BuiltIn("Translate", "Translate the following to English (to Turkish if it is already English). Reply with the translation only:\n\n{$selected}"),
            BuiltIn("Fix grammar", "Fix spelling and grammar. Reply with the corrected text only:\n\n{$selected}"),
            BuiltIn("Explain", "Explain the following simply and briefly:\n\n{$selected}"),
        };

        private readonly String _path;
        private readonly String _customPath;
        private readonly Object _lock = new Object();

        public PresetCatalog(String path)
        {
            this._path = path;
            this._customPath = Path.Combine(Path.GetDirectoryName(path) ?? "", "actions.json");
        }

        /// <summary>Your actions, then presets used from the ring (most recent first), then the built-ins not already covered.</summary>
        public IReadOnlyList<SavedPreset> List()
        {
            lock (this._lock)
            {
                var custom = this.ReadLocked(this._customPath);
                var titles = new HashSet<String>(custom.Select(p => p.Title), StringComparer.OrdinalIgnoreCase);
                var remembered = this.ReadLocked(this._path).Where(p => !titles.Contains(p.Title)).ToList();
                titles.UnionWith(remembered.Select(p => p.Title));
                return custom.Concat(remembered).Concat(BuiltIns.Where(b => !titles.Contains(b.Title)))
                    .Where(p => p.Enabled)
                    .ToList();
            }
        }

        /// <summary>Switches a template on or off (a built-in becomes your copy, so it can remember that).</summary>
        public void SetEnabled(String id, Boolean enabled)
        {
            var current = this.ListEditable().FirstOrDefault(p => p.Id == id);
            if (current == null)
            {
                return;
            }

            if (current.BuiltIn)
            {
                this.Save(new SavedPreset
                {
                    Title = current.Title, Instruction = current.Instruction, ProviderId = current.ProviderId,
                    Connection = current.Connection, Model = current.Model, Enabled = enabled,
                });
                return;
            }

            lock (this._lock)
            {
                var list = this.ReadLocked(this._customPath);
                var found = list.FirstOrDefault(p => p.Id == id);
                if (found != null)
                {
                    found.Enabled = enabled;
                    this.WriteLocked(this._customPath, list);
                }
            }
        }

        /// <summary>
        /// Adds templates from an export. The same title with the same prompt is skipped; the same title with
        /// another prompt is added with " (2)" (nothing is overwritten). Returns (added, skipped).
        /// </summary>
        public (Int32 Added, Int32 Skipped) Import(IEnumerable<SavedPreset> templates)
        {
            var added = 0;
            var skipped = 0;
            foreach (var template in templates)
            {
                if (template == null || String.IsNullOrWhiteSpace(template.Title) || String.IsNullOrWhiteSpace(template.Instruction))
                {
                    skipped++;
                    continue;
                }

                var existing = this.ListEditable();
                if (existing.Any(p => String.Equals(p.Title, template.Title.Trim(), StringComparison.OrdinalIgnoreCase) && p.Instruction == template.Instruction))
                {
                    skipped++;
                    continue;
                }

                var title = template.Title.Trim();
                for (var n = 2; existing.Any(p => String.Equals(p.Title, title, StringComparison.OrdinalIgnoreCase)); n++)
                {
                    title = $"{template.Title.Trim()} ({n})";
                }

                template.Id = "";
                template.Title = title;
                this.Save(template);
                added++;
            }

            return (added, skipped);
        }

        /// <summary>What the editor shows: your actions, then the built-ins you have not replaced.</summary>
        public IReadOnlyList<SavedPreset> ListEditable()
        {
            lock (this._lock)
            {
                var custom = this.ReadLocked(this._customPath);
                var titles = new HashSet<String>(custom.Select(p => p.Title), StringComparer.OrdinalIgnoreCase);
                return custom.Concat(BuiltIns.Where(b => !titles.Contains(b.Title))).ToList();
            }
        }

        /// <summary>Adds or updates an action. Editing a built-in saves your own copy in its place.</summary>
        public SavedPreset Save(SavedPreset action)
        {
            if (action == null || String.IsNullOrWhiteSpace(action.Title) || String.IsNullOrWhiteSpace(action.Instruction))
            {
                throw new ArgumentException("A template needs a title and a prompt.");
            }

            lock (this._lock)
            {
                var list = this.ReadLocked(this._customPath);
                var saved = new SavedPreset
                {
                    Id = String.IsNullOrEmpty(action.Id) || action.Id.StartsWith("builtin-", StringComparison.Ordinal)
                        ? "a" + Guid.NewGuid().ToString("N").Substring(0, 12)
                        : action.Id,
                    Title = action.Title.Trim(),
                    Instruction = action.Instruction,
                    ProviderId = action.ProviderId == ProviderIds.Codex ? ProviderIds.Codex : ProviderIds.Claude,
                    Connection = ConnectionModes.Normalize(action.Connection, ConnectionModes.Default),
                    Model = (action.Model ?? "").Trim(),
                    Input = InputModes.Normalize(action.Input),
                    Setup = SetupModes.Normalize(action.Setup),
                    SetupChoice = action.SetupChoice,
                    Custom = true,
                    Enabled = action.Enabled,
                    AutoCopy = action.AutoCopy,
                    LastUsedUtc = DateTime.UtcNow,
                };
                var index = list.FindIndex(p => p.Id == saved.Id);
                if (index >= 0)
                {
                    list[index] = saved;
                }
                else
                {
                    list.Add(saved);
                }

                this.WriteLocked(this._customPath, list);
                return saved;
            }
        }

        public void Delete(String id)
        {
            lock (this._lock)
            {
                var list = this.ReadLocked(this._customPath);
                if (list.RemoveAll(p => p.Id == id) > 0)
                {
                    this.WriteLocked(this._customPath, list);
                }
            }
        }

        /// <summary>Any template by id, switched off or not (Run in the editor works for both).</summary>
        public SavedPreset Find(String id) =>
            this.List().FirstOrDefault(p => p.Id == id) ?? this.ListEditable().FirstOrDefault(p => p.Id == id);

        /// <summary>Records a preset that was just run from the ring.</summary>
        public void Remember(Preset preset)
        {
            if (preset == null || preset.Input == InputModes.None && String.IsNullOrWhiteSpace(preset.Instruction))
            {
                return;
            }

            if (String.IsNullOrWhiteSpace(preset.Title) && String.IsNullOrWhiteSpace(preset.Instruction))
            {
                return;
            }

            var saved = new SavedPreset
            {
                Title = String.IsNullOrWhiteSpace(preset.Title) ? Shorten(preset.Instruction, 28) : preset.Title.Trim(),
                Instruction = preset.Instruction ?? "",
                ProviderId = preset.ProviderId,
                Connection = ConnectionModes.Normalize(preset.Connection, ConnectionModes.Default),
                Model = preset.Model ?? "",
                LastUsedUtc = DateTime.UtcNow,
            };
            saved.Id = IdFor(saved);

            lock (this._lock)
            {
                var list = this.ReadLocked(this._path).Where(p => p.Id != saved.Id).ToList();
                list.Insert(0, saved);
                this.WriteLocked(this._path, list.Take(MaxRemembered).ToList());
            }
        }

        private List<SavedPreset> ReadLocked(String path)
        {
            try
            {
                if (File.Exists(path))
                {
                    return JsonSerializer.Deserialize<List<SavedPreset>>(File.ReadAllText(path), JsonOptions) ?? new List<SavedPreset>();
                }
            }
            catch (Exception)
            {
                // Corrupt file: start over.
            }

            return new List<SavedPreset>();
        }

        private void WriteLocked(String path, List<SavedPreset> list)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonSerializer.Serialize(list, JsonOptions));
            }
            catch (Exception)
            {
                // Remembering presets is a convenience; never fail the request over it.
            }
        }

        private static SavedPreset BuiltIn(String title, String instruction)
        {
            var preset = new SavedPreset { Title = title, Instruction = instruction, BuiltIn = true };
            preset.Id = "builtin-" + IdFor(preset);
            return preset;
        }

        private static String IdFor(SavedPreset preset)
        {
            var key = $"{preset.ProviderId}|{preset.Connection}|{preset.Model}|{preset.Title}|{preset.Instruction}";
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
            return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
        }

        private static String Shorten(String text, Int32 max)
        {
            var oneLine = (text ?? "").Replace('\n', ' ').Trim();
            return oneLine.Length <= max ? oneLine : oneLine.Substring(0, max - 1) + "…";
        }
    }
}
