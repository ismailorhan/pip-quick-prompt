namespace QuickPrompt.Tests
{
    using System.Diagnostics;
    using System.Text.RegularExpressions;

    using QuickPrompt.Core;

    using Xunit;

    public class ChatPageTests
    {
        [Fact]
        public void Page_has_no_external_resources()
        {
            Assert.DoesNotMatch(new Regex(@"(src|href)\s*=\s*""https?://", RegexOptions.IgnoreCase), ChatPage.Html);
            Assert.Contains("<title>Quick Prompt</title>", ChatPage.Html);
        }

        /// <summary>Catches JavaScript syntax errors (e.g. a stray quote in a UI string). Needs Node.js; skipped without it.</summary>
        [Fact]
        public void Page_script_is_valid_javascript()
        {
            var match = Regex.Match(ChatPage.Html, @"<script>(.*?)</script>", RegexOptions.Singleline);
            Assert.True(match.Success);

            var file = Path.Combine(Path.GetTempPath(), $"quick-prompt-page-{Guid.NewGuid():N}.js");
            File.WriteAllText(file, match.Groups[1].Value);
            try
            {
                Process node;
                try
                {
                    node = Process.Start(new ProcessStartInfo("node") { ArgumentList = { "--check", file }, RedirectStandardError = true, UseShellExecute = false });
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    return; // Node.js not installed.
                }

                var stderr = node.StandardError.ReadToEnd();
                node.WaitForExit();
                Assert.True(node.ExitCode == 0, "JavaScript syntax error:\n" + stderr);
            }
            finally
            {
                File.Delete(file);
            }
        }
    }
}
