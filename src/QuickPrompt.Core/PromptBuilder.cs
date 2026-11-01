namespace QuickPrompt.Core
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;

    /// <summary>A field a preset prompt asks for: "{name}" (free text) or "{name:a|b|c}" (a list).</summary>
    public sealed class PromptParameter
    {
        public String Name { get; set; } = "";

        public IReadOnlyList<String> Options { get; set; } = Array.Empty<String>();
    }

    public static class PromptBuilder
    {
        // "{tone}" or "{tone:formal|friendly|short}". {text}, {selection} and {input} are the captured text instead.
        private static readonly Regex ParameterRegex = new Regex(
            @"\{([\p{L}\p{N}_][\p{L}\p{N}_ \-]{0,39})(?::([^{}\r\n]*))?\}",
            RegexOptions.CultureInvariant);

        // Any of these in a preset prompt is replaced with the captured text.
        private static readonly Regex TokenRegex = new Regex(
            @"\{(text|selection|input)\}",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static Boolean HasToken(String instruction) =>
            !String.IsNullOrEmpty(instruction) && TokenRegex.IsMatch(instruction);

        private static Boolean IsTextToken(String name) =>
            name.Equals("text", StringComparison.OrdinalIgnoreCase)
            || name.Equals("selection", StringComparison.OrdinalIgnoreCase)
            || name.Equals("input", StringComparison.OrdinalIgnoreCase);

        /// <summary>The fields a preset prompt asks for, in order, each name once.</summary>
        public static IReadOnlyList<PromptParameter> GetParameters(String instruction)
        {
            var result = new List<PromptParameter>();
            if (String.IsNullOrEmpty(instruction))
            {
                return result;
            }

            var seen = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in ParameterRegex.Matches(instruction))
            {
                var name = match.Groups[1].Value.Trim();
                if (name.Length == 0 || IsTextToken(name) || !seen.Add(name))
                {
                    continue;
                }

                var options = match.Groups[2].Success
                    ? match.Groups[2].Value.Split('|').Select(o => o.Trim()).Where(o => o.Length > 0).ToArray()
                    : Array.Empty<String>();
                result.Add(new PromptParameter { Name = name, Options = options });
            }

            return result;
        }

        /// <summary>Puts the filled-in values into the prompt (a list field left empty takes its first option).</summary>
        public static String FillParameters(String instruction, IReadOnlyDictionary<String, String> values)
        {
            if (String.IsNullOrEmpty(instruction))
            {
                return instruction ?? "";
            }

            var lookup = new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase);
            if (values != null)
            {
                foreach (var pair in values)
                {
                    lookup[pair.Key.Trim()] = pair.Value ?? "";
                }
            }

            return ParameterRegex.Replace(instruction, match =>
            {
                var name = match.Groups[1].Value.Trim();
                if (IsTextToken(name))
                {
                    return match.Value;
                }

                if (lookup.TryGetValue(name, out var value) && value.Trim().Length > 0)
                {
                    return value.Trim();
                }

                var first = match.Groups[2].Success ? match.Groups[2].Value.Split('|').Select(o => o.Trim()).FirstOrDefault(o => o.Length > 0) : null;
                return first ?? "";
            });
        }

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
