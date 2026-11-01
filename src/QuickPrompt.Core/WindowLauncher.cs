namespace QuickPrompt.Core
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;

    /// <summary>
    /// Opens the response page as a small app-style window (no tabs or address bar) using
    /// Edge/Chrome/Brave with a separate profile. Falls back to the default browser.
    /// </summary>
    public sealed class WindowLauncher
    {
        private readonly Func<QuickPromptSettings> _settings;
        private readonly String _profileDirectory;
        private readonly IHostBridge _host;

        public WindowLauncher(Func<QuickPromptSettings> settings, String profileDirectory, IHostBridge host)
        {
            this._settings = settings;
            this._profileDirectory = profileDirectory;
            this._host = host;
        }

        /// <summary>Path of the QuickPromptPopup helper shipped with the plugin (macOS). Settings.PopupPath overrides it.</summary>
        public String DefaultPopupPath { get; set; }

        /// <summary>For tests: replaces the actual process start.</summary>
        public Action<String> OverrideOpen { get; set; }

        /// <param name="urlFor">Builds the URL; the argument says whether it opens as an app window.</param>
        public void Open(Func<Boolean, String> urlFor)
        {
            if (this.OverrideOpen != null)
            {
                this.OverrideOpen(urlFor(true));
                return;
            }

            var settings = this._settings();

            // macOS: small native floating panel at the mouse cursor.
            if (OperatingSystem.IsMacOS() && this.TryOpenPopup(settings, urlFor(true)))
            {
                return;
            }

            var browser = this.FindChromium(settings.BrowserPath);
            if (browser != null)
            {
                try
                {
                    Directory.CreateDirectory(this._profileDirectory);
                    var psi = new ProcessStartInfo(browser) { UseShellExecute = false };
                    psi.ArgumentList.Add("--app=" + urlFor(true));
                    psi.ArgumentList.Add("--user-data-dir=" + this._profileDirectory);
                    psi.ArgumentList.Add($"--window-size={settings.WindowWidth},{settings.WindowHeight}");
                    psi.ArgumentList.Add("--no-first-run");
                    psi.ArgumentList.Add("--no-default-browser-check");
                    psi.ArgumentList.Add("--disable-features=Translate,TranslateUI");
                    Process.Start(psi)?.Dispose();
                    return;
                }
                catch (Exception ex)
                {
                    this._host.Log($"Could not open a browser window ({browser}); trying the default browser", ex);
                }
            }

            OpenDefault(urlFor(false));
        }

        public String FindPopup(QuickPromptSettings settings)
        {
            var path = !String.IsNullOrWhiteSpace(settings.PopupPath) ? CliLocator.ExpandHome(settings.PopupPath.Trim()) : this.DefaultPopupPath;
            return !String.IsNullOrEmpty(path) && File.Exists(path) ? path : null;
        }

        private Boolean TryOpenPopup(QuickPromptSettings settings, String url)
        {
            var popup = this.FindPopup(settings);
            if (popup == null)
            {
                return false;
            }

            try
            {
                if (!OperatingSystem.IsWindows())
                {
                    // Zip packaging can drop the executable bit.
                    var mode = File.GetUnixFileMode(popup);
                    var exec = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
                    if ((mode & UnixFileMode.UserExecute) == 0)
                    {
                        File.SetUnixFileMode(popup, mode | exec);
                    }
                }

                if (OperatingSystem.IsMacOS())
                {
                    RemoveQuarantine(popup);
                }

                var psi = new ProcessStartInfo(popup) { UseShellExecute = false };
                psi.ArgumentList.Add(url);
                Process.Start(psi)?.Dispose();
                return true;
            }
            catch (Exception ex)
            {
                this._host.Log($"Could not start the popup ({popup}); trying a browser window", ex);
                return false;
            }
        }

        /// <summary>
        /// A package downloaded from the internet can carry the quarantine flag, which stops an
        /// ad-hoc signed helper from starting. Best effort; failures are ignored.
        /// </summary>
        private static void RemoveQuarantine(String path)
        {
            try
            {
                var psi = new ProcessStartInfo("/usr/bin/xattr") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
                psi.ArgumentList.Add("-d");
                psi.ArgumentList.Add("com.apple.quarantine");
                psi.ArgumentList.Add(path);
                using var process = Process.Start(psi);
                process?.WaitForExit(2000);
            }
            catch (Exception)
            {
                // Not quarantined, or xattr unavailable.
            }
        }

        private static void OpenDefault(String url)
        {
            if (OperatingSystem.IsMacOS())
            {
                var psi = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
                psi.ArgumentList.Add(url);
                Process.Start(psi)?.Dispose();
            }
            else if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
            }
            else
            {
                var psi = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
                psi.ArgumentList.Add(url);
                Process.Start(psi)?.Dispose();
            }
        }

        public String FindChromium(String overridePath)
        {
            if (!String.IsNullOrWhiteSpace(overridePath))
            {
                var expanded = CliLocator.ExpandHome(overridePath.Trim());
                return File.Exists(expanded) ? expanded : null;
            }

            foreach (var candidate in Candidates())
            {
                try
                {
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch (Exception)
                {
                    // Ignore.
                }
            }

            return null;
        }

        private static IEnumerable<String> Candidates()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (OperatingSystem.IsWindows())
            {
                var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

                // Edge ships with Windows, so it is the most reliable choice.
                yield return Path.Combine(pf86, "Microsoft", "Edge", "Application", "msedge.exe");
                yield return Path.Combine(pf, "Microsoft", "Edge", "Application", "msedge.exe");
                yield return Path.Combine(pf, "Google", "Chrome", "Application", "chrome.exe");
                yield return Path.Combine(pf86, "Google", "Chrome", "Application", "chrome.exe");
                yield return Path.Combine(local, "Google", "Chrome", "Application", "chrome.exe");
                yield return Path.Combine(pf, "BraveSoftware", "Brave-Browser", "Application", "brave.exe");
                yield return Path.Combine(local, "BraveSoftware", "Brave-Browser", "Application", "brave.exe");
            }
            else if (OperatingSystem.IsMacOS())
            {
                foreach (var root in new[] { "/Applications", Path.Combine(home, "Applications") })
                {
                    yield return Path.Combine(root, "Google Chrome.app", "Contents", "MacOS", "Google Chrome");
                    yield return Path.Combine(root, "Microsoft Edge.app", "Contents", "MacOS", "Microsoft Edge");
                    yield return Path.Combine(root, "Brave Browser.app", "Contents", "MacOS", "Brave Browser");
                    yield return Path.Combine(root, "Chromium.app", "Contents", "MacOS", "Chromium");
                }
            }
            else
            {
                yield return "/usr/bin/google-chrome";
                yield return "/usr/bin/chromium";
                yield return "/usr/bin/chromium-browser";
                yield return "/usr/bin/microsoft-edge";
            }
        }
    }
}
