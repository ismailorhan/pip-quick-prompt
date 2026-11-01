namespace QuickPrompt.Core
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Text.RegularExpressions;

    public static class PromptBuilder
    {
        // Any of these in a preset prompt is replaced with the captured text.
        private static readonly Regex TokenRegex = new Regex(
            @"\{(text|selection|input)\}",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static Boolean HasToken(String instruction) =>
            !String.IsNullOrEmpty(instruction) && TokenRegex.IsMatch(instruction);

        /// <summary>
        /// Combines a preset instruction with the captured text.
        /// "{text}" (or {selection}/{input}) marks where the text goes; without a token the text is appended.
        /// </summary>
        public static String Build(String instruction, String context)
        {
            instruction = (instruction ?? "").Trim();
            context = (context ?? "").Trim();

            if (HasToken(instruction))
            {
                return TokenRegex.Replace(instruction, _ => context).Trim();
            }

            if (context.Length == 0)
            {
                return instruction;
            }

            if (instruction.Length == 0)
            {
                return context;
            }

            return $"{instruction}\n\n<text>\n{context}\n</text>";
        }

        /// <summary>
        /// CLI calls are stateless, so a follow-up carries the earlier turns in the prompt.
        /// </summary>
        public static String BuildWithHistory(IReadOnlyList<KeyValuePair<String, String>> history, String newPrompt)
        {
            if (history == null || history.Count == 0)
            {
                return newPrompt ?? "";
            }

            var sb = new StringBuilder();
            sb.AppendLine("Below is the earlier part of this conversation. Taking it into account, answer only the last message.");
            sb.AppendLine();
            foreach (var turn in history)
            {
                var tag = turn.Key == "assistant" ? "assistant" : "user";
                sb.Append('<').Append(tag).AppendLine(">");
                sb.AppendLine((turn.Value ?? "").Trim());
                sb.Append("</").Append(tag).AppendLine(">");
            }

            sb.AppendLine();
            sb.AppendLine("Last message:");
            sb.Append(newPrompt ?? "");
            return sb.ToString();
        }
    }
}
