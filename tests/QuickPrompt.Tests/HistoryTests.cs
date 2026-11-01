namespace QuickPrompt.Tests
{
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;

    using QuickPrompt.Core;

    using Xunit;

    public class HistoryTests : IDisposable
    {
        private readonly string _dir = TempDir.Create();
        private readonly FakeHost _host = new FakeHost();
        private readonly FakeProvider _claudeCli = new FakeProvider(ProviderIds.Claude);
        private readonly FakeProvider _claudeApi = new FakeProvider(ProviderIds.Claude);
        private readonly ChatManager _manager;

        public HistoryTests()
        {
            this._claudeApi.Answer = r => "API: " + r.Prompt;
            this._manager = new ChatManager(this._dir, this._host,
                new IAiProvider[] { this._claudeCli, new FakeProvider(ProviderIds.Codex) },
                new IAiProvider[] { this._claudeApi, new FakeProvider(ProviderIds.Codex) });
            this._manager.Launcher.OverrideOpen = _ => { };
            this._manager.Start();
        }

        public void Dispose() => this._manager.Dispose();

        private async Task<ChatSession> AskAsync(string question, string connection = ConnectionModes.Default)
        {
            var session = this._manager.CreateSession(ProviderIds.Claude, "Ask", "", connection);
            Assert.True(session.StartTurn(question, ""));
            await session.CurrentTurn;
            return session;
        }

        private JsonElement History() => JsonDocument.Parse(this._manager.GetHistoryJson()).RootElement;

        [Fact]
        public async Task Finished_turns_are_saved_newest_first()
        {
            await this.AskAsync("first question");
            await Task.Delay(20);
            await this.AskAsync("second question");

            var items = History().GetProperty("items").EnumerateArray().ToList();
            Assert.Equal(2, items.Count);
            Assert.Equal("second question", items[0].GetProperty("question").GetString());
            Assert.StartsWith("ANSWER: second question", items[0].GetProperty("answer").GetString());
            Assert.Equal("Claude", items[0].GetProperty("providerName").GetString());
        }

        [Fact]
        public async Task A_closed_conversation_can_be_resumed_and_continued_with_its_connection()
        {
            // Asked over the API, then the popup is closed.
            var original = await this.AskAsync("what is ERP?", ConnectionModes.Api);
            var id = History().GetProperty("items")[0].GetProperty("id").GetString();

            // Later: a new popup (default connection is CLI) opens the saved conversation.
            var popup = this._manager.CreateSession(ProviderIds.Claude, "", "");
            Assert.Same(this._claudeCli, popup.Provider);
            Assert.True(this._manager.ResumeConversation(popup, id));
            Assert.Same(this._claudeApi, popup.Provider);
            Assert.Equal(original.HistoryId, popup.HistoryId);

            // The follow-up carries the earlier exchange and goes to the same connection.
            Assert.True(popup.StartTurn("and SAP Business One?", ""));
            await popup.CurrentTurn;
            var prompt = this._claudeApi.Requests.Last().Prompt.Replace("\r", "");
            Assert.Contains("<user>\nwhat is ERP?\n</user>", prompt);
            Assert.Contains("API: what is ERP?", prompt);
            Assert.EndsWith("and SAP Business One?", prompt);

            // APIs also get the conversation as separate messages.
            var request = this._claudeApi.Requests.Last();
            Assert.Equal(new[] { "user", "assistant" }, request.History.Select(t => t.Key));
            Assert.Equal("what is ERP?", request.History[0].Value);
            Assert.Equal("and SAP Business One?", request.Message);

            // Still one conversation in the history, now with two questions.
            var items = History().GetProperty("items").EnumerateArray().ToList();
            Assert.Single(items);
            Assert.Equal(2, items[0].GetProperty("turns").GetInt32());
        }

        [Fact]
        public async Task Turning_history_off_deletes_it_and_stops_saving()
        {
            await this.AskAsync("keep me?");
            this._manager.SaveSettings(new Dictionary<string, string> { ["saveHistory"] = "false" });
            Assert.False(History().GetProperty("enabled").GetBoolean());
            Assert.Empty(Directory.GetFiles(Path.Combine(this._dir, "history"), "*.json"));

            await this.AskAsync("not saved");
            Assert.Empty(Directory.GetFiles(Path.Combine(this._dir, "history"), "*.json"));
        }

        [Fact]
        public void Store_rejects_bad_ids_and_prunes()
        {
            var store = new ConversationStore(Path.Combine(this._dir, "store"));
            Assert.Null(store.Load("../../etc/passwd"));
            for (var i = 0; i < ConversationStore.MaxConversations + 5; i++)
            {
                store.Save(new ConversationRecord
                {
                    Id = Guid.NewGuid().ToString("N"),
                    UpdatedUtc = DateTime.UtcNow.AddMinutes(i),
                    Messages = { new ChatMessage { Role = "user", Text = "q" + i } },
                });
            }

            var all = store.List();
            Assert.Equal(ConversationStore.MaxConversations, all.Count);
            Assert.Equal("q" + (ConversationStore.MaxConversations + 4), all[0].Messages[0].Text);
        }

        [Fact]
        public async Task History_endpoints()
        {
            await this.AskAsync("over http");
            var popup = this._manager.CreateSession(ProviderIds.Claude, "", "");
            var baseUrl = this._manager.GetSessionUrl(popup, true);
            using var http = new HttpClient();

            var list = JsonDocument.Parse(await http.GetStringAsync(baseUrl + "history")).RootElement;
            var id = list.GetProperty("items")[0].GetProperty("id").GetString();

            var resume = new HttpRequestMessage(HttpMethod.Post, baseUrl + "resume") { Content = new StringContent("{\"id\":\"" + id + "\"}", Encoding.UTF8, "application/json") };
            resume.Headers.Add("X-QuickPrompt", "1");
            Assert.Equal(HttpStatusCode.OK, (await http.SendAsync(resume)).StatusCode);
            var state = JsonDocument.Parse(await http.GetStringAsync(baseUrl + "state?v=0")).RootElement;
            Assert.Equal("over http", state.GetProperty("messages")[0].GetProperty("text").GetString());

            var clear = new HttpRequestMessage(HttpMethod.Post, baseUrl + "clearhistory") { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
            clear.Headers.Add("X-QuickPrompt", "1");
            var cleared = JsonDocument.Parse(await (await http.SendAsync(clear)).Content.ReadAsStringAsync()).RootElement;
            Assert.Empty(cleared.GetProperty("items").EnumerateArray());
        }
    }
}
