namespace QuickPrompt.Core
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;

    /// <summary>A preset offered under "Predefined actions" in the floating icon's toolbar.</summary>
    public sealed class SavedPreset
    {
        public String Id { get; set; } = "";

        public String Title { get; set; } = "";

        public String Instruction { get; set; } = "";

        public String ProviderId { get; set; } = ProviderIds.Claude;

        public String Connection { get; set; } = ConnectionModes.Default;

        public String Model { get; set; } = "";

        public DateTime LastUsedUtc { get; set; }

        public Boolean BuiltIn { get; set; }
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
            BuiltIn("Summarize", "Summarize the following in 3 short bullet points:\n\n{text}"),
            BuiltIn("Translate", "Translate the following to English (to Turkish if it is already English). Reply with the translation only:\n\n{text}"),
            BuiltIn("Fix grammar", "Fix spelling and grammar. Reply with the corrected text only:\n\n{text}"),
            BuiltIn("Explain", "Explain the following simply and briefly:\n\n{text}"),
        };

        private readonly String _path;
        private readonly Object _lock = new Object();

        public PresetCatalog(String path)
        {
            this._path = path;
        }

        /// <summary>Remembered presets (most recently used first), then the built-ins not already covered.</summary>
        public IReadOnlyList<SavedPreset> List()
        {
            lock (this._lock)
            {
                var remembered = this.ReadLocked();
                var titles = new HashSet<String>(remembered.Select(p => p.Title), StringComparer.OrdinalIgnoreCase);
                return remembered.Concat(BuiltIns.Where(b => !titles.Contains(b.Title))).ToList();
            }
        }

        public SavedPreset Find(String id) => this.List().FirstOrDefault(p => p.Id == id);

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
                var list = this.ReadLocked().Where(p => p.Id != saved.Id).ToList();
                list.Insert(0, saved);
                this.WriteLocked(list.Take(MaxRemembered).ToList());
            }
        }

        private List<SavedPreset> ReadLocked()
        {
            try
            {
                if (File.Exists(this._path))
                {
                    return JsonSerializer.Deserialize<List<SavedPreset>>(File.ReadAllText(this._path), JsonOptions) ?? new List<SavedPreset>();
                }
            }
            catch (Exception)
            {
                // Corrupt file: start over.
            }

            return new List<SavedPreset>();
        }

        private void WriteLocked(List<SavedPreset> list)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(this._path));
                File.WriteAllText(this._path, JsonSerializer.Serialize(list, JsonOptions));
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
