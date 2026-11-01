namespace Loupedeck.QuickPromptPlugin
{
    using System;

    using QuickPrompt.Core;

    /// <summary>Opens the popup on the Settings view: API keys, CLI/API per provider, models.</summary>
    public sealed class SettingsCommand : PluginDynamicCommand
    {
        public SettingsCommand()
            : base("Quick Prompt Settings", "API keys, CLI or API per provider, default models", "Quick Prompt")
        {
        }

        protected override void RunCommand(String actionParameter) => (this.Plugin as QuickPromptPlugin)?.Chat?.OpenSettings();

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            try
            {
                return BitmapImage.FromSvg(IconCatalog.BuildSvg("settings", "slate", "slate", "Settings", 80, 80));
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "Could not create the icon");
                return null;
            }
        }
    }
}
