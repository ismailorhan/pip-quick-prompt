namespace QuickPrompt.Core
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Text.RegularExpressions;

    /// <summary>A saved conversation (one popup's messages), so it can be reopened and continued later.</summary>
    public sealed class ConversationRecord
    {
        public String Id { get; set; } = "";

        public String ProviderId { get; set; } = ProviderIds.Claude;

        public String Connection { get; set; } = ConnectionModes.Cli;

        public String Model { get; set; } = "";

        public String Title { get; set; } = "";

        public DateTime CreatedUtc { get; set; }

        public DateTime UpdatedUtc { get; set; }

        public List<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
    }

    /// <summary>
    /// Conversation history on disk: one JSON file per conversation in the plugin data folder
    /// (this computer only). Calls to the CLIs and APIs are stateless, so continuing a conversation
    /// only needs these messages; it works the same for CLI and API connections.
    /// </summary>
    public sealed class ConversationStore
    {
        public const Int32 MaxConversations = 50;

        private static readonly Regex IdPattern = new Regex("^[0-9a-f]{32}$", RegexOptions.CultureInvariant);
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = false };

        private readonly String _directory;
        private readonly Object _lock = new Object();

        public ConversationStore(String directory)
        {
            this._directory = directory;
        }

        public static Boolean IsValidId(String id) => id != null && IdPattern.IsMatch(id);

        public void Save(ConversationRecord record)
        {
            if (!IsValidId(record.Id) || record.Messages.Count == 0)
            {
                return;
            }

            lock (this._lock)
            {
                Directory.CreateDirectory(this._directory);
                var path = this.PathFor(record.Id);
                var temp = path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(record, JsonOptions));
                File.Move(temp, path, overwrite: true);
                this.PruneLocked();
            }
        }

        public ConversationRecord Load(String id)
        {
            if (!IsValidId(id))
            {
                return null;
            }

            lock (this._lock)
            {
                return ReadFile(this.PathFor(id));
            }
        }

        /// <summary>Newest first.</summary>
        public IReadOnlyList<ConversationRecord> List(Int32 max = MaxConversations)
        {
            lock (this._lock)
            {
                return this.ReadAllLocked().Take(max).ToList();
            }
        }

        public void Delete(String id)
        {
            if (!IsValidId(id))
            {
                return;
            }

            lock (this._lock)
            {
                TryDelete(this.PathFor(id));
            }
        }

        public void Clear()
        {
            lock (this._lock)
            {
                if (!Directory.Exists(this._directory))
                {
                    return;
                }

                foreach (var file in Directory.GetFiles(this._directory, "*.json"))
                {
                    TryDelete(file);
                }
            }
        }

        private String PathFor(String id) => Path.Combine(this._directory, id + ".json");

        private List<ConversationRecord> ReadAllLocked()
        {
            if (!Directory.Exists(this._directory))
            {
                return new List<ConversationRecord>();
            }

            return Directory.GetFiles(this._directory, "*.json")
                .Select(ReadFile)
                .Where(r => r != null)
                .OrderByDescending(r => r.UpdatedUtc)
                .ToList();
        }

        private void PruneLocked()
        {
            foreach (var old in this.ReadAllLocked().Skip(MaxConversations))
            {
                TryDelete(this.PathFor(old.Id));
            }
        }

        private static ConversationRecord ReadFile(String path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                var record = JsonSerializer.Deserialize<ConversationRecord>(File.ReadAllText(path), JsonOptions);
                return record != null && IsValidId(record.Id) ? record : null;
            }
            catch (Exception)
            {
                return null;   // Corrupt or half-written file: skip it.
            }
        }

        private static void TryDelete(String path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception)
            {
                // Ignore.
            }
        }
    }
}
