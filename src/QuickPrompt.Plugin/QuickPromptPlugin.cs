namespace Loupedeck.QuickPromptPlugin
{
    using System;

    using QuickPrompt.Core;

    // Universal plugin: works in every app, talks to the Claude Code and Codex CLIs in the background.
    public partial class QuickPromptPlugin : Plugin
    {
        public override Boolean UsesApplicationApiOnly => true;

        public override Boolean HasNoApplication => true;

        // Options+ "Plugin settings" shows plugin preferences; the only kind is an account row with
        // sign-in / sign-out buttons. Each API key is shown as one: "Add key" opens the Settings popup,
        // "Remove key" deletes it, and a stored key appears as "••••1234".
        private readonly PluginPreferenceAccount _anthropicKey;
        private readonly PluginPreferenceAccount _openAiKey;

        public QuickPromptPlugin()
        {
            PluginLog.Init(this.Log);
            PluginResources.Init(this.Assembly);

            this._anthropicKey = new PluginPreferenceAccount("anthropic-api-key")
            {
                DisplayName = "Anthropic API key (Claude via API)",
                IsRequired = false,
                LoginUrlTitle = "Add key",
                LogoutUrlTitle = "Remove key",
            };
            this._openAiKey = new PluginPreferenceAccount("openai-api-key")
            {
                DisplayName = "OpenAI API key (ChatGPT via API)",
                IsRequired = false,
                LoginUrlTitle = "Add key",
                LogoutUrlTitle = "Remove key",
            };
            this.PluginPreferences.Add(this._anthropicKey);
            this.PluginPreferences.Add(this._openAiKey);
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

                this._anthropicKey.LoginRequested += this.OnAddKeyRequested;
                this._openAiKey.LoginRequested += this.OnAddKeyRequested;
                this._anthropicKey.LogoutRequested += this.OnRemoveAnthropicKey;
                this._openAiKey.LogoutRequested += this.OnRemoveOpenAiKey;
                this.Chat.SecretsChanged += this.SyncKeyPreferences;
                this.SyncKeyPreferences();
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
            this._anthropicKey.LoginRequested -= this.OnAddKeyRequested;
            this._openAiKey.LoginRequested -= this.OnAddKeyRequested;
            this._anthropicKey.LogoutRequested -= this.OnRemoveAnthropicKey;
            this._openAiKey.LogoutRequested -= this.OnRemoveOpenAiKey;
            if (this.Chat != null)
            {
                this.Chat.SecretsChanged -= this.SyncKeyPreferences;
            }

            this.Chat?.Dispose();
            this.Chat = null;
        }

        private void OnAddKeyRequested(Object sender, EventArgs e) => this.Chat?.OpenSettings();

        private void OnRemoveAnthropicKey(Object sender, EventArgs e) => this.Chat?.RemoveKey(AnthropicApiProvider.SecretName);

        private void OnRemoveOpenAiKey(Object sender, EventArgs e) => this.Chat?.RemoveKey(OpenAiApiProvider.SecretName);

        /// <summary>Shows each stored key as a signed-in account ("••••1234") in Options+ Plugin settings.</summary>
        private void SyncKeyPreferences()
        {
            Sync(this._anthropicKey, AnthropicApiProvider.SecretName);
            Sync(this._openAiKey, OpenAiApiProvider.SecretName);

            void Sync(PluginPreferenceAccount account, String secretName)
            {
                try
                {
                    var hint = this.Chat?.GetKeyHint(secretName) ?? "";
                    if (hint.Length > 0)
                    {
                        // The key itself stays in the encrypted secret store; the account only shows its hint.
                        account.ReportLogin(hint, "configured", null);
                    }
                    else
                    {
                        account.ReportLogout();
                    }
                }
                catch (Exception ex)
                {
                    PluginLog.Warning(ex, "Could not update Plugin settings");
                }
            }
        }
    }
}
