namespace Loupedeck.QuickPromptPlugin
{
    using System;
    using System.Threading;

    using QuickPrompt.Core;

    public partial class QuickPromptPlugin
    {
        /// <summary>Clipboard, keystrokes and notifications through Logi Plugin Service.</summary>
        /// <remarks>Nested so it can use the plugin's protected NativeMethods / NativeGui.</remarks>
        private sealed class HostBridge : IHostBridge
        {
            private readonly QuickPromptPlugin _plugin;

            public HostBridge(QuickPromptPlugin plugin)
            {
                this._plugin = plugin;
            }

            private static ModifierKey CopyPasteModifier => OperatingSystem.IsMacOS() ? ModifierKey.Command : ModifierKey.Control;

            public String GetClipboardText() => this.OnMainThreadIfNeeded(() => this._plugin.NativeMethods.ClipboardGetText()) ?? "";

            public void SetClipboardText(String text) =>
                this.OnMainThreadIfNeeded(() =>
                {
                    this._plugin.NativeMethods.ClipboardSetText(text ?? "");
                    return true;
                });

            public void SendCopyShortcut() => this._plugin.NativeMethods.SendKeyboardShortcut(VirtualKeyCode.KeyC, CopyPasteModifier);

            public void SendPasteShortcut() => this._plugin.NativeMethods.SendKeyboardShortcut(VirtualKeyCode.KeyV, CopyPasteModifier);

            public void Notify(String title, String message, Boolean isError)
            {
                try
                {
                    this._plugin.NativeGui.ShowBalloonTip(message, title, isError ? BalloonTipIcon.Error : BalloonTipIcon.Info);
                }
                catch (Exception ex)
                {
                    PluginLog.Verbose(ex, "Could not show a notification");
                }

                if (isError)
                {
                    this._plugin.OnPluginStatusChanged(Loupedeck.PluginStatus.Warning, message);
                }
            }

            public void Log(String message, Exception exception = null)
            {
                if (exception == null)
                {
                    PluginLog.Info(message);
                }
                else
                {
                    PluginLog.Warning(exception, message);
                }
            }

            // Clipboard access may require the UI thread on some systems: try directly first, then marshal.
            private T OnMainThreadIfNeeded<T>(Func<T> action)
            {
                try
                {
                    return action();
                }
                catch (Exception first)
                {
                    PluginLog.Verbose(first, "Retrying clipboard access on the main thread");
                    var result = default(T);
                    Exception error = null;
                    var done = new ManualResetEventSlim(false); // not disposed: the callback may run after a timeout
                    this._plugin.NativeGui.BeginExecuteOnMainThread(() =>
                    {
                        try
                        {
                            result = action();
                        }
                        catch (Exception ex)
                        {
                            error = ex;
                        }
                        finally
                        {
                            done.Set();
                        }
                    });

                    if (!done.Wait(TimeSpan.FromSeconds(2)) || error != null)
                    {
                        throw error ?? first;
                    }

                    return result;
                }
            }
        }
    }
}
