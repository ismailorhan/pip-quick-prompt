namespace QuickPrompt.Tests
{
    using System.Net;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Text;
    using System.Text.Json;

    using QuickPrompt.Core;

    using Xunit;

    /// <summary>Tiny HTTP server on 127.0.0.1 that records one request and replies with a canned response.</summary>
    internal sealed class FakeHttpServer : IDisposable
    {
        private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);
        private readonly int _status;
        private readonly string _contentType;
        private readonly string _body;

        public FakeHttpServer(int status, string contentType, string body)
        {
            this._status = status;
            this._contentType = contentType;
            this._body = body;
            this._listener.Start();
            _ = Task.Run(this.ServeOnceAsync);
        }

        public string BaseUrl => $"http://127.0.0.1:{((IPEndPoint)this._listener.LocalEndpoint).Port}";

        public TaskCompletionSource<(string Head, string Body)> Request { get; } = new();

        private async Task ServeOnceAsync()
        {
            using var client = await this._listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            var buffer = new MemoryStream();
            var chunk = new byte[8192];
            string head = null;
            var contentLength = 0;
            while (true)
            {
                var read = await stream.ReadAsync(chunk);
                if (read == 0)
                {
                    break;
                }

                buffer.Write(chunk, 0, read);
                var text = Encoding.UTF8.GetString(buffer.ToArray());
                var end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                if (end < 0)
                {
                    continue;
                }

                head ??= text.Substring(0, end);
                foreach (var line in head.Split("\r\n"))
                {
                    if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    {
                        contentLength = int.Parse(line.Substring(15).Trim());
                    }
                }

                var bodyBytes = buffer.Length - (Encoding.UTF8.GetByteCount(text.Substring(0, end)) + 4);
                if (bodyBytes >= contentLength)
                {
                    var body = Encoding.UTF8.GetString(buffer.ToArray(), (int)buffer.Length - (int)bodyBytes, (int)bodyBytes);
                    this.Request.TrySetResult((head, body));
                    break;
                }
            }

            var payload = Encoding.UTF8.GetBytes(this._body);
            var response = $"HTTP/1.1 {this._status} X\r\nContent-Type: {this._contentType}\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(response));
            await stream.WriteAsync(payload);
            await stream.FlushAsync();
        }

        public void Dispose() => this._listener.Stop();
    }

    public class ApiProviderTests
    {
        private static readonly Func<QuickPromptSettings> Settings = () => new QuickPromptSettings();

        private static async Task<List<AiEvent>> RunAsync(IAiProvider provider, string prompt = "hi", string model = "")
        {
            var events = new List<AiEvent>();
            await foreach (var ev in provider.RunAsync(new AiRequest { Prompt = prompt, Model = model, SystemPrompt = "be brief" }, CancellationToken.None))
            {
                events.Add(ev);
            }

            return events;
        }

        private static string Sse(params (string Name, object Data)[] events) =>
            string.Concat(events.Select(e => $"event: {e.Name}\ndata: {JsonSerializer.Serialize(e.Data)}\n\n"));

        [Fact]
        public async Task Anthropic_streams_text_and_sends_key_model_fallbacks_and_effort()
        {
            var sse = Sse(
                ("message_start", new { type = "message_start", message = new { id = "m", type = "message", role = "assistant", model = "claude-haiku-5-5", content = Array.Empty<object>(), stop_reason = (string)null, stop_sequence = (string)null, usage = new { input_tokens = 1, output_tokens = 0 } } }),
                ("content_block_start", new { type = "content_block_start", index = 0, content_block = new { type = "text", text = "" } }),
                ("content_block_delta", new { type = "content_block_delta", index = 0, delta = new { type = "text_delta", text = "Hel" } }),
                ("content_block_delta", new { type = "content_block_delta", index = 0, delta = new { type = "text_delta", text = "lo" } }),
                ("content_block_stop", new { type = "content_block_stop", index = 0 }),
                ("message_delta", new { type = "message_delta", delta = new { stop_reason = "end_turn", stop_sequence = (string)null }, usage = new { output_tokens = 2 } }),
                ("message_stop", new { type = "message_stop" }));
            using var server = new FakeHttpServer(200, "text/event-stream", sse);
            var provider = new AnthropicApiProvider(Settings, () => "sk-ant-test", server.BaseUrl);

            var events = await RunAsync(provider, model: "haiku");

            Assert.Equal(new[] { "Hel", "lo" }, events.Where(e => e.Kind == AiEventKind.Delta).Select(e => e.Text));
            Assert.Equal("Hello", events.Last().Text);
            Assert.Equal(AiEventKind.Final, events.Last().Kind);

            var (head, body) = await server.Request.Task;
            // The C# SDK sends the API key as "Authorization: Bearer" (documented on ClientOptions.ApiKey).
            Assert.Contains("Authorization: Bearer sk-ant-test", head, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("server-side-fallback-2026-07-01", head);
            using var json = JsonDocument.Parse(body);
            Assert.Equal("claude-haiku-5-5", json.RootElement.GetProperty("model").GetString());
            Assert.Equal("default", json.RootElement.GetProperty("fallbacks").GetString());
            Assert.Equal("low", json.RootElement.GetProperty("output_config").GetProperty("effort").GetString());
            Assert.Equal("be brief", json.RootElement.GetProperty("system").GetString());
            Assert.True(json.RootElement.GetProperty("stream").GetBoolean());
            Assert.Equal("ephemeral", json.RootElement.GetProperty("cache_control").GetProperty("type").GetString());
        }

        private static AiRequest FollowUp() => new AiRequest
        {
            Prompt = "(stitched prompt for the CLI)",
            History = new List<KeyValuePair<string, string>>
            {
                new("user", "Translate: merhaba"),
                new("assistant", "hello"),
            },
            Message = "and more formal?",
            SystemPrompt = "be brief",
        };

        private static async Task<List<AiEvent>> RunAsync(IAiProvider provider, AiRequest request)
        {
            var events = new List<AiEvent>();
            await foreach (var ev in provider.RunAsync(request, CancellationToken.None))
            {
                events.Add(ev);
            }

            return events;
        }

        [Fact]
        public async Task Anthropic_follow_up_sends_the_conversation_as_messages()
        {
            var sse = Sse(
                ("message_start", new { type = "message_start", message = new { id = "m", type = "message", role = "assistant", model = "x", content = Array.Empty<object>(), stop_reason = (string)null, stop_sequence = (string)null, usage = new { input_tokens = 1, output_tokens = 0 } } }),
                ("content_block_delta", new { type = "content_block_delta", index = 0, delta = new { type = "text_delta", text = "Good day" } }),
                ("message_delta", new { type = "message_delta", delta = new { stop_reason = "end_turn", stop_sequence = (string)null }, usage = new { output_tokens = 2 } }),
                ("message_stop", new { type = "message_stop" }));
            using var server = new FakeHttpServer(200, "text/event-stream", sse);
            var events = await RunAsync(new AnthropicApiProvider(Settings, () => "k", server.BaseUrl), FollowUp());
            Assert.Equal("Good day", events.Last().Text);

            var (_, body) = await server.Request.Task;
            var messages = JsonDocument.Parse(body).RootElement.GetProperty("messages").EnumerateArray().ToList();
            Assert.Equal(new[] { "user", "assistant", "user" }, messages.Select(m => m.GetProperty("role").GetString()));
            Assert.Equal("Translate: merhaba", messages[0].GetProperty("content").GetString());
            Assert.Equal("hello", messages[1].GetProperty("content").GetString());
            Assert.Equal("and more formal?", messages[2].GetProperty("content").GetString());
            Assert.DoesNotContain("stitched", body);
        }

        [Fact]
        public async Task OpenAi_follow_up_sends_the_conversation_as_messages()
        {
            using var server = new FakeHttpServer(200, "text/event-stream", "data: {\"choices\":[{\"delta\":{\"content\":\"ok\"}}]}\n\ndata: [DONE]\n\n");
            await RunAsync(new OpenAiApiProvider(Settings, () => "k", server.BaseUrl), FollowUp());

            var (_, body) = await server.Request.Task;
            var messages = JsonDocument.Parse(body).RootElement.GetProperty("messages").EnumerateArray().ToList();
            Assert.Equal(new[] { "system", "user", "assistant", "user" }, messages.Select(m => m.GetProperty("role").GetString()));
            Assert.Equal("and more formal?", messages[3].GetProperty("content").GetString());
        }

        private static ImageAttachment Pic(string data) => new ImageAttachment { MediaType = "image/png", Data = data, Thumb = "data:image/jpeg;base64,AA==" };

        private static AiRequest WithPictures()
        {
            var request = FollowUp();
            request.HistoryImages = new IReadOnlyList<ImageAttachment>[] { new[] { Pic("AAAA") }, Array.Empty<ImageAttachment>() };
            request.Images = new[] { Pic("BBBB") };
            return request;
        }

        [Fact]
        public async Task Anthropic_sends_pictures_as_image_blocks_before_the_text()
        {
            var sse = Sse(
                ("message_start", new { type = "message_start", message = new { id = "m", type = "message", role = "assistant", model = "x", content = Array.Empty<object>(), stop_reason = (string)null, stop_sequence = (string)null, usage = new { input_tokens = 1, output_tokens = 0 } } }),
                ("message_stop", new { type = "message_stop" }));
            using var server = new FakeHttpServer(200, "text/event-stream", sse);
            await RunAsync(new AnthropicApiProvider(Settings, () => "k", server.BaseUrl), WithPictures());

            var (_, body) = await server.Request.Task;
            var messages = JsonDocument.Parse(body).RootElement.GetProperty("messages").EnumerateArray().ToList();
            var first = messages[0].GetProperty("content");
            Assert.Equal("image", first[0].GetProperty("type").GetString());
            Assert.Equal("AAAA", first[0].GetProperty("source").GetProperty("data").GetString());
            Assert.Equal("image/png", first[0].GetProperty("source").GetProperty("media_type").GetString());
            Assert.Equal("text", first[1].GetProperty("type").GetString());
            var last = messages[2].GetProperty("content");
            Assert.Equal("BBBB", last[0].GetProperty("source").GetProperty("data").GetString());
            Assert.Equal("and more formal?", last[1].GetProperty("text").GetString());
        }

        [Fact]
        public async Task OpenAi_sends_pictures_as_data_urls()
        {
            using var server = new FakeHttpServer(200, "text/event-stream", "data: {\"choices\":[{\"delta\":{\"content\":\"ok\"}}]}\n\ndata: [DONE]\n\n");
            await RunAsync(new OpenAiApiProvider(Settings, () => "k", server.BaseUrl), WithPictures());

            var (_, body) = await server.Request.Task;
            var messages = JsonDocument.Parse(body).RootElement.GetProperty("messages").EnumerateArray().ToList();
            var last = messages[3].GetProperty("content");
            Assert.Equal("image_url", last[0].GetProperty("type").GetString());
            Assert.Equal("data:image/png;base64,BBBB", last[0].GetProperty("image_url").GetProperty("url").GetString());
            Assert.Equal("and more formal?", last[1].GetProperty("text").GetString());
            Assert.Equal("assistant", messages[2].GetProperty("role").GetString());
            Assert.Equal("hello", messages[2].GetProperty("content").GetString());
        }

        [Fact]
        public void Clis_take_pictures_on_stdin_or_as_files()
        {
            var line = ClaudeCliProvider.BuildImageMessage("What is this?", new[] { Pic("CCCC") });
            using var doc = JsonDocument.Parse(line);
            var content = doc.RootElement.GetProperty("message").GetProperty("content");
            Assert.Equal("user", doc.RootElement.GetProperty("type").GetString());
            Assert.Equal("CCCC", content[0].GetProperty("source").GetProperty("data").GetString());
            Assert.Equal("What is this?", content[1].GetProperty("text").GetString());

            var args = CodexCliProvider.BuildArguments(new AiRequest(), "/tmp/ws", "/tmp/ws/last.txt", null, new[] { "/tmp/ws/a.png" }).ToList();
            Assert.Contains("--image=/tmp/ws/a.png", args);
            Assert.Equal("-", args.Last());
        }

        [Fact]
        public async Task Anthropic_rejected_key_is_a_readable_error()
        {
            using var server = new FakeHttpServer(401, "application/json", "{\"type\":\"error\",\"error\":{\"type\":\"authentication_error\",\"message\":\"invalid x-api-key\"}}");
            var events = await RunAsync(new AnthropicApiProvider(Settings, () => "bad", server.BaseUrl));
            Assert.Equal(AiEventKind.Error, events.Last().Kind);
            Assert.Contains("key was rejected", events.Last().Text);
        }

        [Fact]
        public async Task Missing_keys_point_to_settings()
        {
            var claude = await RunAsync(new AnthropicApiProvider(Settings, () => null));
            var openai = await RunAsync(new OpenAiApiProvider(Settings, () => " "));
            Assert.Contains("Settings", claude.Single().Text);
            Assert.Contains("Settings", openai.Single().Text);
        }

        [Theory]
        [InlineData("haiku", "claude-haiku-5-5")]
        [InlineData("Sonnet", "claude-sonnet-5-5")]
        [InlineData("", "claude-opus-5-5")]
        [InlineData("claude-opus-5", "claude-opus-5")]
        public void Model_aliases(string requested, string expected) =>
            Assert.Equal(expected, AnthropicApiProvider.ResolveModel(requested, "claude-opus-5-5"));

        [Fact]
        public async Task OpenAi_streams_text_and_sends_bearer_key()
        {
            var sse = "data: {\"choices\":[{\"delta\":{\"role\":\"assistant\"}}]}\n\n"
                + "data: {\"choices\":[{\"delta\":{\"content\":\"Mer\"}}]}\n\n"
                + "data: {\"choices\":[{\"delta\":{\"content\":\"haba\"},\"finish_reason\":null}]}\n\n"
                + "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\n\n"
                + "data: [DONE]\n\n";
            using var server = new FakeHttpServer(200, "text/event-stream", sse);
            var provider = new OpenAiApiProvider(Settings, () => "sk-test", server.BaseUrl);

            var events = await RunAsync(provider);

            Assert.Equal("Merhaba", events.Last().Text);
            Assert.Equal(AiEventKind.Final, events.Last().Kind);
            var (head, body) = await server.Request.Task;
            Assert.StartsWith("POST /v1/chat/completions", head);
            Assert.Contains("Authorization: Bearer sk-test", head, StringComparison.OrdinalIgnoreCase);
            using var json = JsonDocument.Parse(body);
            Assert.Equal("gpt-5-mini", json.RootElement.GetProperty("model").GetString());
            Assert.Equal("system", json.RootElement.GetProperty("messages")[0].GetProperty("role").GetString());
        }

        [Fact]
        public async Task OpenAi_errors_are_readable()
        {
            using var server = new FakeHttpServer(429, "application/json", "{\"error\":{\"message\":\"You exceeded your current quota\"}}");
            var events = await RunAsync(new OpenAiApiProvider(Settings, () => "sk", server.BaseUrl));
            Assert.Equal(AiEventKind.Error, events.Last().Kind);
            Assert.Contains("quota", events.Last().Text);
        }

        [Fact]
        public void OpenAi_stream_error_line_ends_the_stream_once()
        {
            var parser = new OpenAiStreamParser();
            var ev = parser.Feed("data: {\"error\":{\"message\":\"boom\"}}").Single();
            Assert.Equal(AiEventKind.Error, ev.Kind);
            Assert.Empty(parser.Complete(false));
        }
    }

    public class SettingsAndConnectionTests : IDisposable
    {
        private readonly string _dir = TempDir.Create();
        private readonly FakeHost _host = new FakeHost();
        private readonly FakeProvider _cli = new FakeProvider(ProviderIds.Claude);
        private readonly FakeProvider _api = new FakeProvider(ProviderIds.Claude);
        private readonly ChatManager _manager;

        public SettingsAndConnectionTests()
        {
            this._api.Answer = r => "API: " + r.Prompt;
            this._manager = new ChatManager(this._dir, this._host,
                new IAiProvider[] { this._cli, new FakeProvider(ProviderIds.Codex) },
                new IAiProvider[] { this._api, new FakeProvider(ProviderIds.Codex) });
            this._manager.Launcher.OverrideOpen = _ => { };
            this._manager.CopyDelayMs = 10;
            this._manager.Start();
        }

        public void Dispose() => this._manager.Dispose();

        [Fact]
        public async Task Connection_comes_from_the_default_agent_unless_the_preset_overrides_it()
        {
            Assert.Same(this._cli, this._manager.GetProvider(ProviderIds.Claude, ConnectionModes.Default));
            Assert.Same(this._api, this._manager.GetProvider(ProviderIds.Claude, ConnectionModes.Api));

            this._manager.SaveAgent(new Dictionary<string, string> { ["id"] = "claude", ["name"] = "Claude", ["provider"] = "claude", ["connection"] = "api" });
            Assert.Same(this._api, this._manager.GetProvider(ProviderIds.Claude, ConnectionModes.Default));
            Assert.Same(this._cli, this._manager.GetProvider(ProviderIds.Claude, ConnectionModes.Cli));

            this._host.Selection = "x";
            await this._manager.ExecutePresetAsync(new Preset { Instruction = "{text}", Flow = FlowModes.Copy });
            Assert.Equal("API: x", this._host.Clipboard);
        }

        [Fact]
        public async Task Settings_endpoint_never_returns_keys_and_saves_them_as_secrets()
        {
            var session = this._manager.CreateSession(ProviderIds.Claude, "", "");
            var url = this._manager.GetSessionUrl(session, true) + "settings";
            using var http = new HttpClient();

            var save = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent("{\"claudeConnection\":\"api\",\"claudeKey\":\"sk-ant-secret-1234\",\"claudeModel\":\"claude-haiku-5-5\",\"chatgptModel\":\"gpt-x\"}", Encoding.UTF8, "application/json"),
            };
            save.Headers.Add("X-QuickPrompt", "1");
            var saved = await (await http.SendAsync(save)).Content.ReadAsStringAsync();

            Assert.Equal("sk-ant-secret-1234", this._host.GetSecret(AnthropicApiProvider.SecretName));
            Assert.DoesNotContain("secret", saved);
            var got = JsonDocument.Parse(await http.GetStringAsync(url)).RootElement;
            Assert.Equal("api", got.GetProperty("claude").GetProperty("connection").GetString());
            Assert.Equal("••••1234", got.GetProperty("claude").GetProperty("keyHint").GetString());
            Assert.Equal("claude-haiku-5-5", got.GetProperty("claude").GetProperty("model").GetString());
            Assert.Equal("gpt-x", got.GetProperty("chatgpt").GetProperty("model").GetString());
            Assert.Contains("\"ClaudeConnection\": \"api\"", File.ReadAllText(this._manager.SettingsPath));
            Assert.DoesNotContain("sk-ant", File.ReadAllText(this._manager.SettingsPath));

            // Empty keeps the key, "-" removes it.
            this._manager.SaveSettings(new Dictionary<string, string> { ["claudeKey"] = "" });
            Assert.NotNull(this._host.GetSecret(AnthropicApiProvider.SecretName));
            this._manager.SaveSettings(new Dictionary<string, string> { ["claudeKey"] = "-" });
            Assert.Null(this._host.GetSecret(AnthropicApiProvider.SecretName));
        }

        [Fact]
        public void Popup_scale_is_saved_clamped_and_reported()
        {
            var session = this._manager.CreateSession(ProviderIds.Claude, "", "");
            Assert.Equal(QuickPromptSettings.DefaultPopupScale, JsonDocument.Parse(session.ToJson(true)).RootElement.GetProperty("popupScale").GetInt32());

            this._manager.SaveSettings(new Dictionary<string, string> { ["popupScale"] = "72" });
            Assert.Equal(72, JsonDocument.Parse(session.ToJson(true)).RootElement.GetProperty("popupScale").GetInt32());
            Assert.Equal(72, JsonDocument.Parse(this._manager.GetSettingsJson()).RootElement.GetProperty("popupScale").GetInt32());

            this._manager.SaveSettings(new Dictionary<string, string> { ["popupScale"] = "250" });
            Assert.Equal(100, this._manager.Settings.PopupScale);
        }

        [Fact]
        public void Older_popup_size_setting_is_carried_over()
        {
            File.WriteAllText(this._manager.SettingsPath, "{\"PopupSize\": \"large\", \"ClaudeConnection\": \"api\"}");
            File.SetLastWriteTimeUtc(this._manager.SettingsPath, DateTime.UtcNow.AddSeconds(5));
            Assert.Equal(85, this._manager.Settings.PopupScale);
            Assert.Equal("api", this._manager.Settings.ClaudeConnection);
        }

        [Fact]
        public void Session_reports_its_connection()
        {
            var session = this._manager.CreateSession(ProviderIds.Claude, "", "", ConnectionModes.Api);
            using var doc = JsonDocument.Parse(session.ToJson(true));
            Assert.Equal("api", doc.RootElement.GetProperty("connection").GetString());
        }
    }
}
