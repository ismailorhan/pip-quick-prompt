namespace Loupedeck.QuickPromptPlugin
{
    using System;
    using System.Threading.Tasks;

    using QuickPrompt.Core;

    /// <summary>
    /// One configurable preset. Each time the user drops this action on the Actions Ring,
    /// Options+ opens the Action Editor, so every slot gets its own title, prompt, icon and behaviour.
    /// </summary>
    public abstract class AskPresetCommandBase : ActionEditorCommand
    {
        internal const String ControlTitle = "title";
        internal const String ControlPrompt = "prompt";
        internal const String ControlInput = "input";
        internal const String ControlFlow = "flow";
        internal const String ControlIcon = "icon";
        internal const String ControlColor = "color";
        internal const String ControlModel = "model";

        private readonly String _providerId;
        private readonly String _defaultColor;
        private readonly String _defaultDisplayName;

        protected AskPresetCommandBase(String providerId, String displayName, String description, String defaultColor, String modelHint)
        {
            this._providerId = providerId;
            this._defaultColor = defaultColor;
            this._defaultDisplayName = displayName;

            this.DisplayName = displayName;
            this.GroupName = "Quick Prompt";
            this.Description = description;

            this.ActionEditor.AddControlEx(
                new ActionEditorTextbox(ControlTitle, "Title", "Short name shown on the ring and in lists")
                    .SetPlaceholder("e.g. Summarize")
                    .SetMaxLength(40));

            this.ActionEditor.AddControlEx(
                new ActionEditorTextArea(ControlPrompt, "Prompt", "{text} is replaced with the selected text. Without it, the text is appended.")
                    .SetPlaceholder("e.g. Summarize the following in 3 bullet points:\n\n{text}")
                    .SetMaxLength(8000));

            this.ActionEditor.AddControlEx(new ActionEditorListbox(ControlInput, "Input", "Which text goes into the prompt"));
            this.ActionEditor.AddControlEx(new ActionEditorListbox(ControlFlow, "Behavior", "Where the answer goes"));
            this.ActionEditor.AddControlEx(new ActionEditorListbox(ControlIcon, "Icon"));
            this.ActionEditor.AddControlEx(new ActionEditorListbox(ControlColor, "Color"));
            this.ActionEditor.AddControlEx(
                new ActionEditorTextbox(ControlModel, "Model (optional)", "Leave empty to use the CLI's default model")
                    .SetPlaceholder(modelHint));

            this.ActionEditor.ListboxItemsRequested += this.OnListboxItemsRequested;
            this.ActionEditor.ControlValueChanged += this.OnControlValueChanged;
        }

        protected override Boolean RunCommand(ActionEditorActionParameters actionParameters)
        {
            var chat = (this.Plugin as QuickPromptPlugin)?.Chat;
            if (chat == null)
            {
                PluginLog.Warning("Quick Prompt is not ready yet");
                return false;
            }

            var preset = new Preset
            {
                ProviderId = this._providerId,
                Title = actionParameters.GetString(ControlTitle, ""),
                Instruction = actionParameters.GetString(ControlPrompt, ""),
                Input = actionParameters.GetString(ControlInput, InputModes.Selection),
                Flow = actionParameters.GetString(ControlFlow, FlowModes.Send),
                Model = actionParameters.GetString(ControlModel, ""),
            };

            // Never block the plugin service: the CLI call can take many seconds.
            _ = Task.Run(() => chat.ExecutePresetAsync(preset));
            return true;
        }

        protected override String GetCommandDisplayName(ActionEditorActionParameters actionParameters)
        {
            var title = actionParameters.GetString(ControlTitle, "");
            return String.IsNullOrWhiteSpace(title) ? this._defaultDisplayName : title.Trim();
        }

        protected override BitmapImage GetCommandImage(ActionEditorActionParameters actionParameters, Int32 imageWidth, Int32 imageHeight)
        {
            try
            {
                var svg = IconCatalog.BuildSvg(
                    actionParameters.GetString(ControlIcon, "sparkle"),
                    actionParameters.GetString(ControlColor, this._defaultColor),
                    this._defaultColor,
                    actionParameters.GetString(ControlTitle, this._defaultDisplayName),
                    imageWidth,
                    imageHeight);
                return BitmapImage.FromSvg(svg);
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "Could not create the icon");
                return null;
            }
        }

        private void OnListboxItemsRequested(Object sender, ActionEditorListboxItemsRequestedEventArgs e)
        {
            if (e.ControlName.EqualsNoCase(ControlInput))
            {
                e.AddItem(InputModes.Selection, "Selected text", "Copies the selection with Ctrl/Cmd+C");
                e.AddItem(InputModes.Clipboard, "Clipboard text", "Uses the clipboard, leaves the selection alone");
                e.AddItem(InputModes.None, "No input", "Only the prompt is sent");
                SelectDefault(e, InputModes.Selection);
            }
            else if (e.ControlName.EqualsNoCase(ControlFlow))
            {
                e.AddItem(FlowModes.Send, "Send now, show in popup", "A small popup opens and the answer streams in");
                e.AddItem(FlowModes.Edit, "Edit in popup first", "Prompt and text open in the popup; you send");
                e.AddItem(FlowModes.Replace, "Replace selection (no popup)", "e.g. fix spelling, translate");
                e.AddItem(FlowModes.Copy, "Copy to clipboard (no popup)", "The answer is copied when ready");
                SelectDefault(e, FlowModes.Send);
            }
            else if (e.ControlName.EqualsNoCase(ControlIcon))
            {
                foreach (var icon in IconCatalog.Icons)
                {
                    e.AddItem(icon.Id, icon.Name, null);
                }

                SelectDefault(e, "sparkle");
            }
            else if (e.ControlName.EqualsNoCase(ControlColor))
            {
                foreach (var color in IconCatalog.Colors)
                {
                    e.AddItem(color.Id, color.Name, null);
                }

                SelectDefault(e, this._defaultColor);
            }
        }

        private static void SelectDefault(ActionEditorListboxItemsRequestedEventArgs e, String defaultName)
        {
            var current = e.ActionEditorState.GetControlValue(e.ControlName);
            e.SetSelectedItemName(String.IsNullOrEmpty(current) ? defaultName : current);
        }

        private void OnControlValueChanged(Object sender, ActionEditorControlValueChangedEventArgs e)
        {
            if (e.ControlName.EqualsNoCase(ControlTitle))
            {
                var title = e.ActionEditorState.GetControlValue(ControlTitle);
                e.ActionEditorState.SetDisplayName(String.IsNullOrWhiteSpace(title) ? this._defaultDisplayName : title.Trim());
            }
        }
    }

    public sealed class AskClaudeCommand : AskPresetCommandBase
    {
        public AskClaudeCommand()
            : base(
                ProviderIds.Claude,
                "Ask Claude",
                "Sends the selected text and your prompt to Claude through the Claude Code CLI, in the background.",
                "orange",
                "e.g. sonnet, opus, haiku")
        {
        }
    }

    public sealed class AskChatGptCommand : AskPresetCommandBase
    {
        public AskChatGptCommand()
            : base(
                ProviderIds.Codex,
                "Ask ChatGPT",
                "Sends the selected text and your prompt to your ChatGPT account through the Codex CLI, in the background.",
                "green",
                "Empty = Codex default")
        {
        }
    }
}
