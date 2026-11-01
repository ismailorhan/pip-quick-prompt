namespace QuickPrompt.Core
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;

    /// <summary>
    /// Finds CLI executables. Logi Plugin Service is started by the OS, not from a terminal,
    /// so its PATH usually lacks Homebrew, npm, nvm and ~/.local/bin. We add those ourselves
    /// and, on macOS/Linux, also borrow the PATH of the user's login shell.
    /// </summary>
    public static class CliLocator
    {
        private static readonly Object Gate = new Object();
        private static String _cachedPath;

        public static Boolean IsWindows => OperatingSystem.IsWindows();

        public static String Find(String name, String overridePath)
        {
            if (!String.IsNullOrWhiteSpace(overridePath))
            {
                var expanded = ExpandHome(overridePath.Trim());
                return File.Exists(expanded) ? expanded : null;
            }

            foreach (var dir in GetSearchDirectories())
            {
                foreach (var candidate in CandidateFileNames(name))
                {
                    try
                    {
                        var full = Path.Combine(dir, candidate);
                        if (File.Exists(full))
                        {
                            return full;
                        }
                    }
                    catch (ArgumentException)
                    {
                        // Invalid characters in a PATH entry; skip it.
                    }
                }
            }

            return null;
        }

        /// <summary>PATH to hand to child processes (npm shims need to find node).</summary>
        public static String GetEnrichedPath()
        {
            lock (Gate)
            {
                if (_cachedPath != null)
                {
                    return _cachedPath;
                }

                var dirs = GetSearchDirectories();
                _cachedPath = String.Join(Path.PathSeparator, dirs);
                return _cachedPath;
            }
        }

        public static void ResetCache()
        {
            lock (Gate)
            {
                _cachedPath = null;
                _searchDirs = null;
            }
        }

        private static List<String> _searchDirs;

        private static List<String> GetSearchDirectories()
        {
            lock (Gate)
            {
                if (_searchDirs != null)
                {
                    return _searchDirs;
                }

                var result = new List<String>();
                var seen = new HashSet<String>(IsWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

                void Add(String dir)
                {
                    if (String.IsNullOrWhiteSpace(dir))
                    {
                        return;
                    }

                    dir = dir.Trim().Trim('"');
                    if (seen.Add(dir))
                    {
                        result.Add(dir);
                    }
                }

                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

                if (IsWindows)
                {
                    var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                    Add(Path.Combine(home, ".local", "bin"));          // Claude Code native installer
                    Add(Path.Combine(appData, "npm"));                 // npm -g
                    Add(Path.Combine(localAppData, "pnpm"));
                    Add(Path.Combine(home, ".bun", "bin"));
                    Add(Path.Combine(localAppData, "Volta", "bin"));
                    Add(Path.Combine(programFiles, "nodejs"));
                    SplitPath(Environment.GetEnvironmentVariable("PATH")).ForEach(Add);
                }
                else
                {
                    Add(Path.Combine(home, ".local", "bin"));          // Claude Code native installer
                    Add(Path.Combine(home, ".claude", "local"));
                    Add("/opt/homebrew/bin");
                    Add("/usr/local/bin");
                    Add(Path.Combine(home, ".npm-global", "bin"));
                    Add(Path.Combine(home, ".volta", "bin"));
                    Add(Path.Combine(home, ".bun", "bin"));
                    Add(Path.Combine(home, "Library", "pnpm"));
                    Add(Path.Combine(home, ".local", "share", "pnpm"));
                    foreach (var nvmBin in NvmBinDirectories(home))
                    {
                        Add(nvmBin);
                    }

                    SplitPath(Environment.GetEnvironmentVariable("PATH")).ForEach(Add);
                    SplitPath(ReadLoginShellPath()).ForEach(Add);
                    Add("/usr/bin");
                    Add("/bin");
                    Add("/usr/sbin");
                    Add("/sbin");
                }

                _searchDirs = result;
                return result;
            }
        }

        private static IEnumerable<String> CandidateFileNames(String name)
        {
            if (!IsWindows)
            {
                yield return name;
                yield break;
            }

            // Prefer real executables over npm .cmd shims.
            yield return name + ".exe";
            yield return name + ".cmd";
            yield return name + ".bat";
        }

        private static IEnumerable<String> NvmBinDirectories(String home)
        {
            var root = Path.Combine(home, ".nvm", "versions", "node");
            if (!Directory.Exists(root))
            {
                return Array.Empty<String>();
            }

            try
            {
                // Newest version first.
                return Directory.GetDirectories(root)
                    .OrderByDescending(d => d, StringComparer.Ordinal)
                    .Select(d => Path.Combine(d, "bin"))
                    .ToArray();
            }
            catch (Exception)
            {
                return Array.Empty<String>();
            }
        }

        private static List<String> SplitPath(String path) =>
            String.IsNullOrEmpty(path)
                ? new List<String>()
                : path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).ToList();

        private static String ReadLoginShellPath()
        {
            const String Marker = "__ASKAI_PATH__";
            var shell = Environment.GetEnvironmentVariable("SHELL");
            if (String.IsNullOrEmpty(shell) || !File.Exists(shell))
            {
                shell = File.Exists("/bin/zsh") ? "/bin/zsh" : "/bin/bash";
            }

            try
            {
                var psi = new ProcessStartInfo(shell)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                // -i loads .zshrc/.bashrc too (where nvm usually lives); markers survive any noise those files print.
                psi.ArgumentList.Add("-ilc");
                psi.ArgumentList.Add($"printf '%s%s%s' '{Marker}' \"$PATH\" '{Marker}'");

                using var process = Process.Start(psi);
                process.StandardInput.Close();
                var readTask = process.StandardOutput.ReadToEndAsync();
                _ = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(5000))
                {
                    ProcessHelpers.KillTree(process);
                    return null;
                }

                var output = readTask.GetAwaiter().GetResult();
                var start = output.IndexOf(Marker, StringComparison.Ordinal);
                var end = output.LastIndexOf(Marker, StringComparison.Ordinal);
                if (start >= 0 && end > start)
                {
                    return output.Substring(start + Marker.Length, end - start - Marker.Length);
                }
            }
            catch (Exception)
            {
                // Best effort only.
            }

            return null;
        }

        public static String ExpandHome(String path)
        {
            if (path.StartsWith("~/", StringComparison.Ordinal) || path == "~")
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                return Path.Combine(home, path.Length > 2 ? path.Substring(2) : "");
            }

            return Environment.ExpandEnvironmentVariables(path);
        }
    }
}
