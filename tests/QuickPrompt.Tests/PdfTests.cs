namespace QuickPrompt.Tests
{
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;
    using QuickPrompt.Core;
    using Xunit;

    public sealed class PdfTests
    {
        private static ImageAttachment Pdf() => new ImageAttachment { MediaType = ImageAttachment.Pdf, Name = "report.pdf", Data = "JVBERi0xLjMK", Thumb = "data:image/jpeg;base64,AA==" };

        [Fact]
        public void The_Claude_CLI_gets_a_PDF_as_a_document_block_with_its_name()
        {
            var line = ClaudeCliProvider.BuildImageMessage("Summarise", new[] { Pdf() });
            using var doc = JsonDocument.Parse(line);
            var first = doc.RootElement.GetProperty("message").GetProperty("content")[0];
            Assert.Equal("document", first.GetProperty("type").GetString());
            Assert.Equal("application/pdf", first.GetProperty("source").GetProperty("media_type").GetString());
            Assert.Equal("report.pdf", first.GetProperty("title").GetString());
        }

        [Fact]
        public async Task A_PDF_alone_is_sent_with_a_question_about_the_document()
        {
            var claude = new FakeProvider(ProviderIds.Claude);
            var codex = new FakeProvider(ProviderIds.Codex);
            using var manager = new ChatManager(TempDir.Create(), new FakeHost(), new IAiProvider[] { claude, codex });
            var session = manager.CreateSession(null, "");
            Assert.True(session.StartTurn("", "", new[] { Pdf() }));
            await session.CurrentTurn;
            var request = claude.Requests.Last();
            Assert.Equal("Look at this document.", request.Message);
            Assert.True(request.Images.Single().IsPdf);
        }
    }
}
