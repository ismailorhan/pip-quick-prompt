namespace QuickPrompt.Tests
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using QuickPrompt.Core;
    using Xunit;

    public sealed class WebSearchTests
    {
        [Fact]
        public void An_agent_keeps_its_web_search_choice_and_requests_carry_it()
        {
            var manager = new ChatManager(TempDir.Create(), new FakeHost(), new IAiProvider[] { new FakeProvider(ProviderIds.Claude) });
            using (manager)
            {
                var saved = manager.SaveAgent(new Dictionary<string, string>
                {
                    ["name"] = "Researcher", ["provider"] = "claude", ["connection"] = "api", ["webSearch"] = "true",
                });
                using var json = JsonDocument.Parse(saved);
                var agent = json.RootElement.GetProperty("agents").EnumerateArray().First(a => a.GetProperty("name").GetString() == "Researcher");
                Assert.True(agent.GetProperty("webSearch").GetBoolean());
                Assert.False(json.RootElement.GetProperty("agents").EnumerateArray().First(a => a.GetProperty("name").GetString() != "Researcher").GetProperty("webSearch").GetBoolean());
            }
        }

        [Fact]
        public void The_basic_search_tool_is_for_Haiku_and_queries_are_read_from_the_streamed_input()
        {
            Assert.True(AnthropicApiProvider.WebSearchBasic("claude-haiku-5-5"));
            Assert.False(AnthropicApiProvider.WebSearchBasic("claude-opus-5-5"));
            Assert.False(AnthropicApiProvider.WebSearchBasic("claude-sonnet-5-5"));
            Assert.Equal("yarın maç programı", AnthropicApiProvider.SearchQuery("{\"query\": \"yarın maç programı\"}"));
            Assert.Equal("", AnthropicApiProvider.SearchQuery("{\"query\": \"yar"));
        }

        [Fact]
        public void With_web_search_the_instruction_allows_searching_despite_the_no_tools_rule()
        {
            var plain = QuickPromptSettings.DefaultSystemPrompt;
            Assert.Equal(plain, AnthropicApiProvider.WithWebSearch(plain, false));
            var withSearch = AnthropicApiProvider.WithWebSearch(plain, true);
            Assert.StartsWith(plain, withSearch);
            Assert.Contains("search the web", withSearch);
            Assert.Contains("does not apply to web search", withSearch);
        }
    }
}
