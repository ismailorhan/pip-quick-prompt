namespace QuickPrompt.Tests
{
    using System.Xml.Linq;

    using QuickPrompt.Core;

    using Xunit;

    public class PromptBuilderTests
    {
        [Fact]
        public void Token_is_replaced_with_text()
        {
            Assert.Equal("Summarize: hello world", PromptBuilder.Build("Summarize: {text}", "hello world"));
            Assert.Equal("Translate: hi", PromptBuilder.Build("Translate: {TEXT}", " hi "));
            Assert.Equal("a x b x", PromptBuilder.Build("a {selection} b {input}", "x"));
        }

        [Fact]
        public void Text_is_appended_when_there_is_no_token()
        {
            Assert.Equal("Summarize\n\n<text>\nabc\n</text>", PromptBuilder.Build("Summarize", "abc"));
            Assert.Equal("Summarize", PromptBuilder.Build("Summarize", "   "));
            Assert.Equal("abc", PromptBuilder.Build("", "abc"));
        }

        [Fact]
        public void History_is_included_for_follow_ups()
        {
            var history = new List<KeyValuePair<string, string>>
            {
                new("user", "question 1"),
                new("assistant", "answer 1"),
            };
            var prompt = PromptBuilder.BuildWithHistory(history, "question 2");
            Assert.Contains("<user>\nquestion 1\n</user>", prompt.Replace("\r", ""));
            Assert.Contains("<assistant>\nanswer 1\n</assistant>", prompt.Replace("\r", ""));
            Assert.EndsWith("question 2", prompt);
            Assert.Equal("tek", PromptBuilder.BuildWithHistory(new List<KeyValuePair<string, string>>(), "tek"));
        }
    }

    public class ClaudeParserTests
    {
        // Shapes recorded from `claude -p --output-format stream-json --verbose --include-partial-messages` (v2.1).
        private const string Init = "{\"type\":\"system\",\"subtype\":\"init\",\"tools\":[]}";
        private const string Delta1 = "{\"type\":\"stream_event\",\"event\":{\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"1\\n2\\n3\\n4\"}}}";
        private const string Delta2 = "{\"type\":\"stream_event\",\"event\":{\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"\\n5\"}}}";
        private const string Assistant = "{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"1\\n2\\n3\\n4\\n5\"}]}}";
        private const string Result = "{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false,\"result\":\"1\\n2\\n3\\n4\\n5\"}";

        [Fact]
        public void Streams_deltas_then_final()
        {
            var parser = new ClaudeStreamParser();
            var events = new[] { Init, Delta1, Delta2, Assistant, Result }.SelectMany(parser.Feed).ToList();
            events.AddRange(parser.Complete(0, "", false));

            Assert.Equal(new[] { AiEventKind.Delta, AiEventKind.Delta, AiEventKind.Final }, events.Select(e => e.Kind));
            Assert.Equal("1\n2\n3\n4", events[0].Text);
            Assert.Equal("1\n2\n3\n4\n5", events[2].Text);
        }

        [Fact]
        public void Whole_messages_work_without_partial_streaming()
        {
            var parser = new ClaudeStreamParser();
            var events = new[] { Init, Assistant, Result }.SelectMany(parser.Feed).ToList();
            Assert.Equal(AiEventKind.Snapshot, events[0].Kind);
            Assert.Equal(AiEventKind.Final, events[1].Kind);
        }

        [Fact]
        public void Error_result_becomes_error()
        {
            var parser = new ClaudeStreamParser();
            var events = parser.Feed("{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":true,\"result\":\"Not logged in · Please run /login\"}").ToList();
            Assert.Single(events);
            Assert.Equal(AiEventKind.Error, events[0].Kind);
            Assert.Contains("/login", events[0].Text);
            Assert.Empty(parser.Complete(1, "", false));
        }

        [Fact]
        public void Crash_without_result_reports_stderr()
        {
            var parser = new ClaudeStreamParser();
            Assert.Empty(parser.Feed("not json at all"));
            var events = parser.Complete(127, "\u001b[31mError: something broke\u001b[0m\n", false).ToList();
            Assert.Single(events);
            Assert.Equal(AiEventKind.Error, events[0].Kind);
            Assert.Contains("127", events[0].Text);
            Assert.Contains("Error: something broke", events[0].Text);
        }

        [Fact]
        public void Arguments_disable_tools_and_pass_model()
        {
            var args = ClaudeCliProvider.BuildArguments(new AiRequest { Model = "sonnet", SystemPrompt = "short" });
            Assert.Equal("", args[args.ToList().IndexOf("--tools") + 1]);
            Assert.Equal("sonnet", args[args.ToList().IndexOf("--model") + 1]);
            Assert.Equal("short", args[args.ToList().IndexOf("--append-system-prompt") + 1]);
            Assert.Contains("-p", args);
            Assert.DoesNotContain("--model", ClaudeCliProvider.BuildArguments(new AiRequest()));
        }
    }

    public class CodexParserTests
    {
        [Fact]
        public void Agent_message_then_turn_completed()
        {
            var parser = new CodexStreamParser();
            var lines = new[]
            {
                "{\"type\":\"thread.started\",\"thread_id\":\"t1\"}",
                "{\"type\":\"turn.started\"}",
                "{\"type\":\"item.completed\",\"item\":{\"id\":\"item_0\",\"type\":\"reasoning\",\"text\":\"...\"}}",
                "{\"type\":\"item.completed\",\"item\":{\"id\":\"item_1\",\"type\":\"agent_message\",\"text\":\"Hello!\"}}",
                "{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":10}}",
            };
            var events = lines.SelectMany(parser.Feed).ToList();
            events.AddRange(parser.Complete(0, "", false));

            Assert.Equal(AiEventKind.Status, events[0].Kind);
            Assert.Equal(AiEventKind.Snapshot, events[1].Kind);
            Assert.Equal("Hello!", events[1].Text);
            Assert.Equal(AiEventKind.Final, events.Last().Kind);
            Assert.Equal("Hello!", events.Last().Text);
        }

        [Fact]
        public void Turn_failed_is_an_error()
        {
            var parser = new CodexStreamParser();
            var events = parser.Feed("{\"type\":\"turn.failed\",\"error\":{\"message\":\"401 Unauthorized\"}}").ToList();
            Assert.Equal(AiEventKind.Error, events.Single().Kind);
            Assert.Equal("401 Unauthorized", events.Single().Text);
            Assert.Empty(parser.Complete(1, "", false));
        }

        [Fact]
        public void Reconnect_messages_are_status_and_last_one_explains_failure()
        {
            // Recorded: codex exec without network access.
            var parser = new CodexStreamParser();
            var events = new[]
            {
                "{\"type\":\"thread.started\",\"thread_id\":\"x\"}",
                "{\"type\":\"error\",\"message\":\"Reconnecting... 2/5 (stream disconnected before completion)\"}",
                "2026-10-31T21:07:16Z ERROR codex_api::endpoint::responses_websocket: failed to connect",
                "{\"type\":\"item.completed\",\"item\":{\"id\":\"item_0\",\"type\":\"error\",\"message\":\"Falling back from WebSockets to HTTPS transport.\"}}",
                "{\"type\":\"error\",\"message\":\"Reconnecting... waiting for network\"}",
            }.SelectMany(parser.Feed).ToList();
            Assert.All(events, e => Assert.Equal(AiEventKind.Status, e.Kind));

            var end = parser.Complete(1, "", false).Single();
            Assert.Equal(AiEventKind.Error, end.Kind);
            Assert.Contains("waiting for network", end.Text);
        }

        [Fact]
        public void Falls_back_to_last_message_file()
        {
            var parser = new CodexStreamParser(() => "answer from file\n");
            parser.Feed("{\"type\":\"turn.completed\"}").ToList();
            var end = parser.Complete(0, "", false).Single();
            Assert.Equal(AiEventKind.Final, end.Kind);
            Assert.Equal("answer from file", end.Text);
        }

        [Fact]
        public void Arguments_are_read_only_and_read_stdin()
        {
            var args = CodexCliProvider.BuildArguments(new AiRequest { Model = "m1" }, "/tmp/ws", "/tmp/ws/last.txt").ToList();
            Assert.Equal("exec", args[0]);
            Assert.Equal("read-only", args[args.IndexOf("--sandbox") + 1]);
            Assert.Equal("/tmp/ws/last.txt", args[args.IndexOf("-o") + 1]);
            Assert.Equal("m1", args[args.IndexOf("-m") + 1]);
            Assert.Equal("-", args.Last());
            Assert.StartsWith("SYS", CodexCliProvider.BuildStdin(new AiRequest { SystemPrompt = "SYS", Prompt = "P" }));
        }
    }

    public class IconAndQuotingTests
    {
        [Fact]
        public void Every_icon_and_color_builds_valid_svg()
        {
            foreach (var icon in IconCatalog.Icons)
            {
                foreach (var color in IconCatalog.Colors)
                {
                    var svg = IconCatalog.BuildSvg(icon.Id, color.Id, "orange", "Write <email> & send", 80, 80);
                    var doc = XDocument.Parse(svg);
                    Assert.Equal("svg", doc.Root.Name.LocalName);
                    Assert.Contains(color.Hex, svg);
                }
            }
        }

        [Fact]
        public void Unknown_ids_fall_back()
        {
            var svg = IconCatalog.BuildSvg("nope", "nope", "green", "x", 0, 0);
            Assert.Contains("#10A37F", svg);
            Assert.Contains("width=\"80\"", svg);
        }

        [Theory]
        [InlineData("Summary", "SUM")]
        [InlineData("Write email", "WE")]
        [InlineData("SQL explain", "SE")]
        [InlineData("", "AI")]
        [InlineData("Mail's draft", "MD")]
        public void Initials(string title, string expected) => Assert.Equal(expected, IconCatalog.Initials(title));

        [Fact]
        public void Cmd_quoting()
        {
            Assert.Equal("\"\"", ProcessHelpers.QuoteForCmd(""));
            Assert.Equal("-p", ProcessHelpers.QuoteForCmd("-p"));
            Assert.Equal("\"C:\\Program Files\\x\"", ProcessHelpers.QuoteForCmd("C:\\Program Files\\x"));
            Assert.Equal("\"a\"\"b\"", ProcessHelpers.QuoteForCmd("a\"b"));
        }
    }
}
