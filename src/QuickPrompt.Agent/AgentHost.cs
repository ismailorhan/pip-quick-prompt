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

        /// <summary>
        /// The clipboard picture (PNG, or TIFF converted) via AppleScript, scaled with sips to at most 1568 px
        /// (what the AIs use anyway), JPEG when that is still large, plus a 320 px preview.
        /// </summary>
        public ImageAttachment GetClipboardImage()
        {
            var dir = Path.Combine(Path.GetTempPath(), "quickprompt-clip-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(dir);
                var png = Path.Combine(dir, "clip.png");
                var tiff = Path.Combine(dir, "clip.tiff");
                string Save(string kind, string file) => Run("/usr/bin/osascript", "-", string.Join("\n", new[]
                {
                    "try",
                    "  set d to (the clipboard as «class " + kind + "»)",
                    "on error",
                    "  return \"none\"",
                    "end try",
                    "set f to open for access (POSIX file " + Quote(file) + ") with write permission",
                    "set eof f to 0",
                    "write d to f",
                    "close access f",
                    "return \"ok\"",
                })).Output.Trim();

                if (Save("PNGf", png) != "ok")
                {
                    if (Save("TIFF", tiff) != "ok")
                    {
                        return null;
                    }

                    Run("/usr/bin/sips", "-s format png " + Quote(tiff) + " --out " + Quote(png), null);
                }

                if (!File.Exists(png) || new FileInfo(png).Length == 0)
                {
                    return null;
                }

                Run("/usr/bin/sips", "-Z 1568 " + Quote(png), null);
                var mediaType = "image/png";
                var full = png;
                if (new FileInfo(png).Length > 1_500_000)
                {
                    full = Path.Combine(dir, "clip.jpg");
                    Run("/usr/bin/sips", "-s format jpeg -s formatOptions 85 " + Quote(png) + " --out " + Quote(full), null);
                    mediaType = "image/jpeg";
                }

                var thumb = Path.Combine(dir, "thumb.jpg");
                Run("/usr/bin/sips", "-Z 320 -s format jpeg -s formatOptions 80 " + Quote(png) + " --out " + Quote(thumb), null);
                return new ImageAttachment
                {
                    MediaType = mediaType,
                    Data = Convert.ToBase64String(File.ReadAllBytes(full)),
                    Thumb = File.Exists(thumb) ? "data:image/jpeg;base64," + Convert.ToBase64String(File.ReadAllBytes(thumb)) : "",
                };
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (Exception) { }
            }
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
