namespace QuickPrompt.Tests
{
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;

    using QuickPrompt.Core;

    using Xunit;

    public class FlowTests : IDisposable
    {
        private readonly string _dir = TempDir.Create();
        private readonly FakeHost _host = new FakeHost();
        private readonly FakeProvider _claude = new FakeProvider(ProviderIds.Claude);
        private readonly FakeProvider _codex = new FakeProvider(ProviderIds.Codex);
        private readonly List<string> _opened = new List<string>();
        private readonly ChatManager _manager;

        public FlowTests()
        {
            this._manager = new ChatManager(this._dir, this._host, new IAiProvider[] { this._claude, this._codex });
            this._manager.Launcher.OverrideOpen = url => { lock (this._opened) { this._opened.Add(url); } };
            this._manager.CopyDelayMs = 10;
            this._manager.Start();
        }

        public void Dispose()
        {
            this._manager.Dispose();
            try
            {
                Directory.Delete(this._dir, true);
            }
            catch (IOException)
            {
            }
        }

        [Fact]
        public void Settings_file_is_created_with_defaults()
        {
            Assert.True(File.Exists(this._manager.SettingsPath));
            Assert.Contains("SystemPrompt", File.ReadAllText(this._manager.SettingsPath));
        }

        [Fact]
        public async Task Send_flow_uses_selection_and_opens_window()
        {
            this._host.Clipboard = "old clipboard";
            this._host.Selection = "selected text";

            await this._manager.ExecutePresetAsync(new Preset { ProviderId = ProviderIds.Codex, Title = "Summary", Instruction = "Summarize: {text}", Flow = FlowModes.Send });

            var url = Assert.Single(this._opened);
            Assert.StartsWith($"http://127.0.0.1:{this._manager.Port}/s/", url);
            var token = url.Split('/')[4];
            Assert.True(this._manager.TryGetSession(token, out var session));
            await session.CurrentTurn;

            Assert.Equal("Summarize: selected text", this._codex.Requests.Single().Prompt);
            Assert.Contains("Do not use tools", this._codex.Requests.Single().SystemPrompt);
            Assert.Equal("ANSWER: Summarize: selected text", session.LastAnswer);
            Assert.Equal("old clipboard", this._host.Clipboard);   // restored after reading the selection
            Assert.Empty(this._claude.Requests);
        }

        [Fact]
        public async Task No_selection_never_sends_stale_clipboard()
        {
            // The reported bug: the copy did not happen and the old clipboard ("Send now") was sent.
            this._host.Clipboard = "Send now";
            this._host.Selection = null;
            await this._manager.ExecutePresetAsync(new Preset { Instruction = "Translate: {text}", Flow = FlowModes.Send });

            var session = this.SingleSession();
            Assert.Empty(this._claude.Requests);
            Assert.False(session.IsBusy);
            Assert.Equal("Translate: {text}", session.DraftInstruction);
            Assert.Equal("", session.DraftContext);
            Assert.Contains("Could not read", session.Notice);
            Assert.Equal("Send now", this._host.Clipboard);   // clipboard left as it was
        }

        [Fact]
        public async Task Selection_equal_to_clipboard_is_still_detected()
        {
            this._host.Clipboard = "same text";
            this._host.Selection = "same text";
            await this._manager.ExecutePresetAsync(new Preset { Instruction = "Translate: {text}", Flow = FlowModes.Send });
            await this.SingleSession().CurrentTurn;
            Assert.Equal("Translate: same text", this._claude.Requests.Single().Prompt);
            Assert.Equal("same text", this._host.Clipboard);
        }

        [Fact]
        public async Task Late_copy_is_caught_by_the_retry()
        {
            this._host.Clipboard = "old";
            this._host.Selection = "late copy";
            this._host.IgnoreCopies = 1;   // first Cmd+C lost (focus not back yet)
            await this._manager.ExecutePresetAsync(new Preset { Instruction = "{text}", Flow = FlowModes.Copy });
            Assert.Equal(2, this._host.CopyCount);
            Assert.Equal("ANSWER: late copy", this._host.Clipboard);
        }

        [Fact]
        public async Task Clipboard_input_mode_uses_clipboard()
        {
            this._host.Clipboard = "from clipboard";
            await this._manager.ExecutePresetAsync(new Preset { Input = InputModes.Clipboard, Instruction = "Translate", Flow = FlowModes.Send });
            await this.SingleSession().CurrentTurn;
            Assert.Equal("Translate\n\n<text>\nfrom clipboard\n</text>", this._claude.Requests.Single().Prompt);
        }

        [Fact]
        public async Task Paste_in_popup_applies_the_preset_prompt_to_the_pasted_text()
        {
            this._host.Clipboard = "clipboard is ignored";
            this._host.Selection = "selection is ignored";
            await this._manager.ExecutePresetAsync(new Preset { Title = "Translate", Instruction = "Translate to English: {text}", Input = InputModes.Popup, Flow = FlowModes.Replace });

            // No copy, no request yet: just an empty popup waiting for text.
            Assert.Equal(0, this._host.CopyCount);
            Assert.Empty(this._claude.Requests);
            var session = this.SingleSession();
            using (var doc = JsonDocument.Parse(session.ToJson(true)))
            {
                Assert.True(doc.RootElement.GetProperty("pasteMode").GetBoolean());
                Assert.Equal("", doc.RootElement.GetProperty("draft").GetProperty("instruction").GetString());
            }

            // The user pastes text into the capsule and presses Enter.
            using var http = new HttpClient();
            var res = await this.PostAsync(http, this._manager.GetSessionUrl(session, true) + "send", "{\"instruction\":\"Yarın toplantı var\",\"context\":\"\"}");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            await session.CurrentTurn;
            Assert.Equal("Translate to English: Yarın toplantı var", this._claude.Requests.Single().Prompt);

            // Follow-ups are ordinary messages again.
            Assert.True(session.StartTurnFromPopup("Shorter", ""));
            await session.CurrentTurn;
            Assert.EndsWith("Shorter", this._claude.Requests.Last().Prompt);
            using var after = JsonDocument.Parse(session.ToJson(true));
            Assert.False(after.RootElement.GetProperty("pasteMode").GetBoolean());
        }

        [Fact]
        public void Paste_in_popup_ignores_an_empty_message()
        {
            var session = this._manager.CreateSession(ProviderIds.Claude, "", "");
            session.PresetInstruction = "Summarize: {text}";
            Assert.False(session.StartTurnFromPopup("   ", ""));
            Assert.Empty(this._claude.Requests);
        }

        [Fact]
        public async Task Edit_flow_only_prefills()
        {
            this._host.Selection = "some text";
            await this._manager.ExecutePresetAsync(new Preset { Instruction = "Explain", Flow = FlowModes.Edit });
            var session = this.SingleSession();
            Assert.Equal("Explain", session.DraftInstruction);
            Assert.Equal("some text", session.DraftContext);
            Assert.False(session.IsBusy);
            Assert.Empty(this._claude.Requests);
        }

        [Fact]
        public async Task Empty_prompt_and_no_text_opens_editor_instead_of_sending()
        {
            await this._manager.ExecutePresetAsync(new Preset { Input = InputModes.None, Instruction = "  ", Flow = FlowModes.Replace });
            Assert.Single(this._opened);
            Assert.Empty(this._claude.Requests);
            Assert.Empty(this._host.Pasted);
        }

        [Fact]
        public async Task Replace_flow_pastes_answer_and_restores_clipboard()
        {
            this._host.Clipboard = "previous";
            this._host.Selection = "teh qiuck fox";
            this._claude.Answer = _ => "the quick fox";

            await this._manager.ExecutePresetAsync(new Preset { Instruction = "Fix spelling: {text}", Flow = FlowModes.Replace });

            Assert.Equal(new[] { "the quick fox" }, this._host.Pasted);
            Assert.Equal("previous", this._host.Clipboard);
            Assert.Empty(this._opened);
        }

        [Fact]
        public async Task Copy_flow_puts_answer_on_clipboard()
        {
            this._host.Selection = "x";
            await this._manager.ExecutePresetAsync(new Preset { Instruction = "{text}", Flow = FlowModes.Copy });
            Assert.Equal("ANSWER: x", this._host.Clipboard);
            Assert.Contains(this._host.Notifications, n => n.Contains("clipboard"));
            Assert.Empty(this._opened);
        }

        [Fact]
        public async Task Background_error_opens_window_with_the_error()
        {
            this._claude.FailWith = "Not logged in · Please run /login";
            this._host.Selection = "x";
            await this._manager.ExecutePresetAsync(new Preset { Instruction = "{text}", Flow = FlowModes.Copy });
            var session = this.SingleSession();
            using var doc = JsonDocument.Parse(session.ToJson(true));
            var last = doc.RootElement.GetProperty("messages").EnumerateArray().Last();
            Assert.Equal("error", last.GetProperty("state").GetString());
            Assert.Contains("/login", last.GetProperty("text").GetString());
            Assert.Empty(this._host.Pasted);
        }

        [Fact]
        public async Task Http_api_end_to_end()
        {
            var session = this._manager.CreateSession(ProviderIds.Claude, "Test", "");
            session.DraftInstruction = "draft";
            var baseUrl = this._manager.GetSessionUrl(session, true);
            using var http = new HttpClient();

            var page = await http.GetStringAsync(baseUrl);
            Assert.Contains("<title>Quick Prompt</title>", page);

            var state = JsonDocument.Parse(await http.GetStringAsync(baseUrl + "state?v=0")).RootElement;
            Assert.Equal("draft", state.GetProperty("draft").GetProperty("instruction").GetString());
            Assert.True(state.GetProperty("appWindow").GetBoolean());
            var v = state.GetProperty("v").GetInt64();

            // No custom header: rejected (blocks cross-site form posts).
            var bare = await http.PostAsync(baseUrl + "send", new StringContent("{\"instruction\":\"x\"}", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.BadRequest, bare.StatusCode);

            var ok = await this.PostAsync(http, baseUrl + "send", "{\"instruction\":\"hello\",\"context\":\"\"}");
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            var busy = await this.PostAsync(http, baseUrl + "send", "{\"instruction\":\"second\"}");
            Assert.Equal(HttpStatusCode.Conflict, busy.StatusCode);

            // Long-poll until done.
            var deadline = DateTime.UtcNow.AddSeconds(10);
            JsonElement snap;
            do
            {
                snap = JsonDocument.Parse(await http.GetStringAsync(baseUrl + "state?v=" + v)).RootElement;
                v = snap.GetProperty("v").GetInt64();
            }
            while (snap.GetProperty("busy").GetBoolean() && DateTime.UtcNow < deadline);

            var messages = snap.GetProperty("messages").EnumerateArray().ToList();
            Assert.Equal(2, messages.Count);
            Assert.Equal("ANSWER: hello", messages[1].GetProperty("text").GetString());
            Assert.Equal("done", messages[1].GetProperty("state").GetString());

            // Follow-up carries the history.
            await this.PostAsync(http, baseUrl + "send", "{\"instruction\":\"go on\"}");
            await session.CurrentTurn;
            var followUp = this._claude.Requests.Last().Prompt;
            Assert.Contains("<user>\nhello\n</user>", followUp.Replace("\r", ""));
            Assert.Contains("ANSWER: hello", followUp);
            Assert.EndsWith("go on", followUp);

            // Unknown token and foreign Host header.
            var missing = await http.GetAsync($"http://127.0.0.1:{this._manager.Port}/s/yok/state");
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            var foreign = new HttpRequestMessage(HttpMethod.Get, baseUrl);
            foreign.Headers.Host = "evil.example:80";
            Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(foreign)).StatusCode);
        }

        [Fact]
        public async Task Cancel_stops_a_running_turn()
        {
            this._claude.DelayMs = 200;
            var session = this._manager.CreateSession(ProviderIds.Claude, "", "");
            Assert.True(session.StartTurn("a long question that should get a long answer", ""));
            await Task.Delay(300);
            session.Cancel();
            await Assert.ThrowsAsync<AiTurnException>(() => session.CurrentTurn);
            Assert.False(session.IsBusy);
            using var doc = JsonDocument.Parse(session.ToJson(true));
            Assert.Contains("Cancel", doc.RootElement.GetProperty("messages")[1].GetProperty("text").GetString());
        }

        [Fact]
        public async Task Paste_endpoint_pastes_after_delay()
        {
            var session = this._manager.CreateSession(ProviderIds.Claude, "", "");
            using var http = new HttpClient();
            var res = await this.PostAsync(http, this._manager.GetSessionUrl(session, true) + "paste", "{\"text\":\"paste me\"}");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            // The paste waits ~0.5 s for focus to return; allow slow CI machines plenty of time.
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (this._host.Pasted.Count == 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }

            Assert.Equal(new[] { "paste me" }, this._host.Pasted);
        }

        private ChatSession SingleSession()
        {
            var token = Assert.Single(this._opened).Split('/')[4];
            Assert.True(this._manager.TryGetSession(token, out var session));
            return session;
        }

        private Task<HttpResponseMessage> PostAsync(HttpClient http, string url, string json)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            request.Headers.Add("X-QuickPrompt", "1");
            return http.SendAsync(request);
        }
    }

    /// <summary>Runs the real Claude Code CLI. Enabled with QUICKPROMPT_IT=1.</summary>
    public class ClaudeCliIntegrationTests
    {
        [Fact]
        public async Task Real_claude_cli_answers()
        {
            if (Environment.GetEnvironmentVariable("QUICKPROMPT_IT") != "1")
            {
                return;
            }

            var dir = TempDir.Create();
            var provider = new ClaudeCliProvider(() => new QuickPromptSettings(), dir);
            Assert.NotNull(provider.LocateExecutable());

            var events = new List<AiEvent>();
            await foreach (var ev in provider.RunAsync(new AiRequest { Prompt = "Reply with just 'OK'.", SystemPrompt = QuickPromptSettings.DefaultSystemPrompt }, CancellationToken.None))
            {
                events.Add(ev);
            }

            Assert.Contains(events, e => e.Kind == AiEventKind.Delta);
            var final = events.Last();
            Assert.Equal(AiEventKind.Final, final.Kind);
            Assert.Contains("OK", final.Text);
        }
    }
}
