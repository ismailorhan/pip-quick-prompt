namespace QuickPrompt.Agent
{
    using System.Diagnostics;
    using System.Text;

    using QuickPrompt.Core;

    /// <summary>
    /// Host services for the engine on macOS. The clipboard goes through pbcopy / pbpaste; key presses
    /// (⌘C / ⌘V) and notifications are done by the app, which has the Accessibility permission;
    /// API keys live in the login Keychain (security tool, fed through stdin so they never appear
    /// on a command line).
    /// </summary>
    public sealed class AgentHost : IHostBridge
    {
        private const string KeychainService = "Quick Prompt";

        private readonly AppControl _control;

        public AgentHost(AppControl control)
        {
            this._control = control;
        }

        public string GetClipboardText() => Run("/usr/bin/pbpaste", "", null).Output;

        public void SetClipboardText(string text) => Run("/usr/bin/pbcopy", "", text ?? "");

        public void SendCopyShortcut() => this._control.Publish(new { type = "keys", action = "copy" });

        public void SendPasteShortcut() => this._control.Publish(new { type = "keys", action = "paste" });

        public void Notify(string title, string message, bool isError) =>
            this._control.Publish(new { type = "notify", title, message, isError });

        public void Log(string message, Exception exception = null) =>
            Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}{(exception != null ? ": " + exception.Message : "")}");

        public string GetSecret(string name)
        {
            var result = Run("/usr/bin/security", $"find-generic-password -a {Quote(name)} -s {Quote(KeychainService)} -w", null);
            return result.ExitCode == 0 && result.Output.Trim().Length > 0 ? result.Output.Trim() : null;
        }

        public void SetSecret(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                Run("/usr/bin/security", $"delete-generic-password -a {Quote(name)} -s {Quote(KeychainService)}", null);
                return;
            }

            // "security -i" reads commands from stdin, so the key is not visible in the process list.
            var command = $"add-generic-password -U -a {Quote(name)} -s {Quote(KeychainService)} -w {Quote(value.Trim())}\n";
            Run("/usr/bin/security", "-i", command);
        }

        private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        private static (int ExitCode, string Output) Run(string file, string arguments, string stdin)
        {
            try
            {
                var psi = new ProcessStartInfo(file, arguments)
                {
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardInputEncoding = new UTF8Encoding(false),
                };
                psi.Environment["LANG"] = "en_US.UTF-8";
                using var process = Process.Start(psi);
                if (stdin != null)
                {
                    process.StandardInput.Write(stdin);
                }

                process.StandardInput.Close();
                var output = process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd();
                process.WaitForExit(5000);
                return (process.ExitCode, output);
            }
            catch (Exception)
            {
                return (-1, "");
            }
        }
    }
}
