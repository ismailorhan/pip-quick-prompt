namespace Loupedeck.QuickPromptPlugin
{
    using System;

    using QuickPrompt.Core;

    // Universal plugin: works in every app, talks to the Claude Code and Codex CLIs in the background.
    public partial class QuickPromptPlugin : Plugin
    {
        public override Boolean UsesApplicationApiOnly => true;

        public override Boolean HasNoApplication => true;

        public QuickPromptPlugin()
        {
            PluginLog.Init(this.Log);
            PluginResources.Init(this.Assembly);
        }

        internal ChatManager Chat { get; private set; }

        public override void Load()
        {
            try
            {
                this.Chat = new ChatManager(this.GetPluginDataDirectory(), new HostBridge(this));

                // Package layout: <root>/bin/QuickPromptPlugin.dll and <root>/mac/QuickPromptPopup.
                var binDir = System.IO.Path.GetDirectoryName(this.AssemblyFilePath) ?? "";
                this.Chat.Launcher.DefaultPopupPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(binDir, "..", "mac", "QuickPromptPopup"));
                this.Chat.Start();
                PluginLog.Info($"Quick Prompt ready. Local window server: 127.0.0.1:{this.Chat.Port}, settings: {this.Chat.SettingsPath}");
                this.OnPluginStatusChanged(Loupedeck.PluginStatus.Normal, null);
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Quick Prompt failed to start");
                this.OnPluginStatusChanged(Loupedeck.PluginStatus.Error, "Quick Prompt failed to start: " + ex.Message);
            }
        }

        public override void Unload()
        {
            this.Chat?.Dispose();
            this.Chat = null;
        }
    }
}
