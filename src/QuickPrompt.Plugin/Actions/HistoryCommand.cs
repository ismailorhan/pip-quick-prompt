namespace Loupedeck.QuickPromptPlugin
{
    using System;

    using QuickPrompt.Core;

    /// <summary>Opens the popup on the list of recent conversations, to reopen and continue one.</summary>
    public sealed class HistoryCommand : PluginDynamicCommand
    {
        public HistoryCommand()
            : base("Quick Prompt History", "Reopen a recent conversation and continue it", "Quick Prompt")
        {
        }

        protected override void RunCommand(String actionParameter) => (this.Plugin as QuickPromptPlugin)?.Chat?.OpenHistory();

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            try
            {
                return BitmapImage.FromSvg(IconCatalog.BuildSvg("history", "slate", "slate", "History", 80, 80));
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "Could not create the icon");
                return null;
            }
        }
    }
}
