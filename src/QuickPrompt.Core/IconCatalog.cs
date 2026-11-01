namespace QuickPrompt.Core
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Security;

    /// <summary>Button icons for presets: a colored rounded square with a white line glyph, built as SVG.</summary>
    public static class IconCatalog
    {
        public sealed class IconDef
        {
            public IconDef(String id, String name, String paths)
            {
                this.Id = id;
                this.Name = name;
                this.Paths = paths;
            }

            public String Id { get; }

            public String Name { get; }

            /// <summary>SVG path data on a 24x24 grid, drawn as strokes.</summary>
            public String Paths { get; }
        }

        public sealed class ColorDef
        {
            public ColorDef(String id, String name, String hex)
            {
                this.Id = id;
                this.Name = name;
                this.Hex = hex;
            }

            public String Id { get; }

            public String Name { get; }

            public String Hex { get; }
        }

        /// <summary>Shows the first letters of the preset title instead of a glyph.</summary>
        public const String TextIconId = "text";

        /// <summary>The Quick Prompt app icon (full color; ignores the color setting). Default for presets.</summary>
        public const String AppIconId = "app";

        /// <summary>
        /// The Quick Prompt mark: a four-point star with short diagonal rays on a dark tile, in a
        /// warm-orange → green → blue → violet gradient. Original artwork; no third-party logos.
        /// </summary>
        public static String AppIconSvg(Int32 width, Int32 height) =>
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 24 24\">"
            + "<defs>"
            + "<linearGradient id=\"qpTile\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"1\"><stop offset=\"0\" stop-color=\"#26222E\"/><stop offset=\"1\" stop-color=\"#111114\"/></linearGradient>"
            + "<linearGradient id=\"qpMark\" gradientUnits=\"userSpaceOnUse\" x1=\"5\" y1=\"5\" x2=\"19\" y2=\"19\">"
            + "<stop offset=\"0.08\" stop-color=\"#F08A5D\"/><stop offset=\"0.42\" stop-color=\"#1FB890\"/>"
            + "<stop offset=\"0.7\" stop-color=\"#4C8DF6\"/><stop offset=\"0.95\" stop-color=\"#A56BF2\"/></linearGradient>"
            + "</defs>"
            + "<rect width=\"24\" height=\"24\" rx=\"5.5\" fill=\"url(#qpTile)\"/>"
            + "<path d=\"M12 3.8L14.05 9.95L20.2 12L14.05 14.05L12 20.2L9.95 14.05L3.8 12L9.95 9.95Z\" fill=\"url(#qpMark)\"/>"
            + "<path d=\"M16.1 7.9L17.5 6.5M16.1 16.1L17.5 17.5M7.9 16.1L6.5 17.5M7.9 7.9L6.5 6.5\" stroke=\"url(#qpMark)\" stroke-width=\"1.25\" stroke-linecap=\"round\"/>"
            + "</svg>";

        public static readonly IReadOnlyList<IconDef> Icons = new[]
        {
            new IconDef(AppIconId, "Quick Prompt (default)", ""),
            new IconDef("sparkle", "Sparkle (general)", "M12 3.5l1.9 5.1 5.1 1.9-5.1 1.9L12 17.5l-1.9-5.1-5.1-1.9 5.1-1.9z M18.5 15.5l.7 1.8 1.8.7-1.8.7-.7 1.8-.7-1.8-1.8-.7 1.8-.7z"),
            new IconDef("chat", "Chat", "M4 5.5h16v10.5H10l-4.5 3.5V16H4z M8 9.5h8 M8 12.5h5"),
            new IconDef("question", "Question", "M12 3.5a8.5 8.5 0 1 0 0 17 8.5 8.5 0 0 0 0-17z M9.6 9.6a2.5 2.5 0 1 1 3.6 2.2c-.8.4-1.2 1-1.2 1.8v.4 M12 16.8v.2"),
            new IconDef("summary", "Summary", "M5 6h14 M5 10h14 M5 14h9 M5 18h6"),
            new IconDef("list", "Bullet list", "M10 6.5h10 M10 12h10 M10 17.5h10 M5 6.5h.5 M5 12h.5 M5 17.5h.5"),
            new IconDef("translate", "Translate", "M3.5 6h9 M8 4v2 M10.5 6c-.6 3.6-2.8 6.6-6 8.2 M6 9.2c1.2 2.2 3 3.9 5.2 4.8 M13.5 20l3.5-8.5 3.5 8.5 M14.6 17.4h4.8"),
            new IconDef("pen", "Rewrite", "M4.5 19.5h4l10-10-4-4-10 10z M13 7l4 4 M14 19.5h5.5"),
            new IconDef("check", "Fix", "M4.5 12.5l4.5 4.5L19.5 6.5"),
            new IconDef("mail", "Email", "M3.5 6h17v12h-17z M3.8 6.5L12 12.5l8.2-6"),
            new IconDef("code", "Code", "M8.5 7.5L4 12l4.5 4.5 M15.5 7.5L20 12l-4.5 4.5 M13.5 5l-3 14"),
            new IconDef("terminal", "Terminal", "M3.5 5h17v14h-17z M7 9.5l3 2.5-3 2.5 M12.5 15h4.5"),
            new IconDef("database", "SQL / data", "M5 6c0-1.4 3.1-2.5 7-2.5s7 1.1 7 2.5-3.1 2.5-7 2.5S5 7.4 5 6z M5 6v12c0 1.4 3.1 2.5 7 2.5s7-1.1 7-2.5V6 M5 12c0 1.4 3.1 2.5 7 2.5s7-1.1 7-2.5"),
            new IconDef("table", "Table", "M4 5h16v14H4z M4 10h16 M4 14.5h16 M10 5v14"),
            new IconDef("bulb", "Idea", "M9.5 18h5 M10.5 21h3 M12 3a6 6 0 0 0-3.6 10.8c.7.6 1.1 1.3 1.1 2.2h5c0-.9.4-1.6 1.1-2.2A6 6 0 0 0 12 3z"),
            new IconDef("search", "Search / explain", "M10.5 4a6.5 6.5 0 1 0 0 13 6.5 6.5 0 0 0 0-13z M15.3 15.3L20 20"),
            new IconDef("doc", "Document", "M6 3.5h8l4 4v13H6z M14 3.5v4h4 M9 12h6 M9 15.5h6"),
            new IconDef("calendar", "Meeting", "M4 6h16v14H4z M4 10h16 M8.5 3.5v4 M15.5 3.5v4"),
            new IconDef("smile", "Tone", "M12 3.5a8.5 8.5 0 1 0 0 17 8.5 8.5 0 0 0 0-17z M8.5 14c.9 1.3 2.1 2 3.5 2s2.6-.7 3.5-2 M9 9.5v.5 M15 9.5v.5"),
            new IconDef(TextIconId, "Title initials", ""),
            new IconDef("settings", "Settings", "M12 9a3 3 0 1 0 0 6 3 3 0 0 0 0-6z M19.4 13.5l1.6 1.2-2 3.4-1.9-.7a7.5 7.5 0 0 1-2 1.2l-.3 2h-3.9l-.3-2a7.5 7.5 0 0 1-2-1.2l-1.9.7-2-3.4 1.6-1.2a7.6 7.6 0 0 1 0-3l-1.6-1.2 2-3.4 1.9.7a7.5 7.5 0 0 1 2-1.2l.3-2h3.9l.3 2a7.5 7.5 0 0 1 2 1.2l1.9-.7 2 3.4-1.6 1.2a7.6 7.6 0 0 1 0 3z"),
        };

        public static readonly IReadOnlyList<ColorDef> Colors = new[]
        {
            new ColorDef("orange", "Orange", "#D97757"),
            new ColorDef("green", "Green", "#10A37F"),
            new ColorDef("blue", "Blue", "#2563EB"),
            new ColorDef("purple", "Purple", "#7C3AED"),
            new ColorDef("pink", "Pink", "#DB2777"),
            new ColorDef("red", "Red", "#DC2626"),
            new ColorDef("amber", "Amber", "#B7791F"),
            new ColorDef("teal", "Teal", "#0E7490"),
            new ColorDef("slate", "Gray", "#475569"),
            new ColorDef("black", "Black", "#111111"),
        };

        public static IconDef GetIcon(String id) =>
            Icons.FirstOrDefault(i => String.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Icons[0];

        public static ColorDef GetColor(String id, String fallbackId) =>
            Colors.FirstOrDefault(c => String.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? Colors.FirstOrDefault(c => c.Id == fallbackId)
            ?? Colors[0];

        public static String BuildSvg(String iconId, String colorId, String fallbackColorId, String title, Int32 width, Int32 height)
        {
            var icon = GetIcon(iconId);
            var color = GetColor(colorId, fallbackColorId);
            width = width > 0 ? width : 80;
            height = height > 0 ? height : 80;
            if (icon.Id == AppIconId)
            {
                return AppIconSvg(width, height);
            }

            String glyph;
            if (icon.Id == TextIconId)
            {
                var letters = Initials(title);
                var fontSize = letters.Length <= 1 ? 13 : letters.Length == 2 ? 10.5 : 8.2;
                glyph = $"<text x=\"12\" y=\"12\" dy=\".36em\" text-anchor=\"middle\" font-family=\"Segoe UI, Helvetica, Arial, sans-serif\" font-weight=\"700\" font-size=\"{fontSize.ToString(CultureInfo.InvariantCulture)}\" fill=\"#FFFFFF\">{SecurityElement.Escape(letters)}</text>";
            }
            else
            {
                glyph = $"<path d=\"{icon.Paths}\" fill=\"none\" stroke=\"#FFFFFF\" stroke-width=\"1.7\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>";
            }

            // Glyph drawn at 60% of the button, centered.
            return $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 24 24\">"
                + $"<rect x=\"0\" y=\"0\" width=\"24\" height=\"24\" rx=\"5\" fill=\"{color.Hex}\"/>"
                + $"<g transform=\"translate(4.8 4.8) scale(0.6)\">{glyph}</g>"
                + "</svg>";
        }

        /// <summary>"Write email" → "WE", "Summary" → "SUM", "SQL" → "SQL". Text after an apostrophe is ignored.</summary>
        public static String Initials(String title)
        {
            // "Mail'e" -> "Mail": suffixes after an apostrophe are not words.
            var words = (title ?? "")
                .Split(new[] { ' ', '-', '_', '/' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(w => w.Split('\'', '\u2019')[0])
                .Where(w => w.Length > 0 && Char.IsLetterOrDigit(w[0]))
                .ToArray();
            if (words.Length == 0)
            {
                return "AI";
            }

            var culture = CultureInfo.CurrentCulture;
            if (words.Length == 1)
            {
                var w = new String(words[0].Where(Char.IsLetterOrDigit).ToArray());
                return w.Substring(0, Math.Min(3, w.Length)).ToUpper(culture);
            }

            return new String(words.Take(3).Select(w => w[0]).ToArray()).ToUpper(culture);
        }
    }
}
