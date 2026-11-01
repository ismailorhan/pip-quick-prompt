namespace QuickPrompt.Core
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    public static class ProcessHelpers
    {
        public static ProcessStartInfo CreateStartInfo(String executable, IReadOnlyList<String> args, String workingDirectory, String pathVariable)
        {
            ProcessStartInfo psi;
            var ext = Path.GetExtension(executable);

            if (OperatingSystem.IsWindows() && (ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase) || ext.Equals(".bat", StringComparison.OrdinalIgnoreCase)))
            {
                // npm shims are batch files and must go through cmd.exe. ArgumentList quoting
                // does not match cmd's rules, so build the command line by hand.
                var comSpec = Environment.GetEnvironmentVariable("ComSpec");
                psi = new ProcessStartInfo(String.IsNullOrEmpty(comSpec) ? "cmd.exe" : comSpec)
                {
                    Arguments = "/d /s /c \"" + QuoteForCmd(executable) + " " + String.Join(" ", args.Select(QuoteForCmd)) + "\"",
                };
            }
            else
            {
                psi = new ProcessStartInfo(executable);
                foreach (var arg in args)
                {
                    psi.ArgumentList.Add(arg);
                }
            }

            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardInput = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            psi.StandardInputEncoding = new UTF8Encoding(false);
            if (!String.IsNullOrEmpty(workingDirectory))
            {
                Directory.CreateDirectory(workingDirectory);
                psi.WorkingDirectory = workingDirectory;
            }

            if (!String.IsNullOrEmpty(pathVariable))
            {
                var exeDir = Path.GetDirectoryName(executable);
                psi.Environment["PATH"] = String.IsNullOrEmpty(exeDir) ? pathVariable : exeDir + Path.PathSeparator + pathVariable;
            }

            // Plain output, no colors or interactive prompts.
            psi.Environment["NO_COLOR"] = "1";
            psi.Environment["CI"] = "1";
            return psi;
        }

        public static String QuoteForCmd(String arg)
        {
            if (arg.Length == 0)
            {
                return "\"\"";
            }

            if (arg.IndexOfAny(new[] { ' ', '\t', '&', '|', '<', '>', '^', '(', ')', '"', ',', ';', '=' }) < 0)
            {
                return arg;
            }

            return "\"" + arg.Replace("\"", "\"\"") + "\"";
        }

        public static void KillTree(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception)
            {
                // Already gone.
            }
        }

        /// <summary>Reads a stream to the end, keeping only the last few KB (for error messages).</summary>
        public static async Task<String> ReadTailAsync(StreamReader reader, Int32 maxChars = 4000)
        {
            var sb = new StringBuilder();
            var buffer = new Char[2048];
            try
            {
                Int32 read;
                while ((read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                {
                    sb.Append(buffer, 0, read);
                    if (sb.Length > maxChars * 2)
                    {
                        sb.Remove(0, sb.Length - maxChars);
                    }
                }
            }
            catch (Exception)
            {
                // Process killed.
            }

            var text = sb.ToString();
            return text.Length > maxChars ? text.Substring(text.Length - maxChars) : text;
        }

        /// <summary>Last meaningful lines of stderr, without ANSI codes and log timestamps noise.</summary>
        public static String Summarize(String stderr, Int32 maxLines = 6)
        {
            if (String.IsNullOrWhiteSpace(stderr))
            {
                return "";
            }

            var clean = System.Text.RegularExpressions.Regex.Replace(stderr, @"\x1B\[[0-9;?]*[A-Za-z]", "");
            var lines = clean.Split('\n')
                .Select(l => l.TrimEnd('\r').Trim())
                .Where(l => l.Length > 0)
                .ToArray();
            return String.Join("\n", lines.Skip(Math.Max(0, lines.Length - maxLines)));
        }
    }
}
