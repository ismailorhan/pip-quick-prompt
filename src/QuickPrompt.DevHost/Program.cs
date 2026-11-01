// Quick Prompt DevHost: runs the popup and the CLI providers without Logi Options+.
//
//   dotnet run --project src/QuickPrompt.DevHost -- [--provider claude|codex] [--flow send|edit|replace|copy]
//       [--connection default|cli|api] [--settings] [--history] [--input clipboard|popup|none] [--prompt "..."] [--text "..."] [--title "..."] [--model "..."] [--no-open] [--data <dir>]
//   dotnet run --project src/QuickPrompt.DevHost -- --icons icons.html
//   dotnet run --project src/QuickPrompt.DevHost -- --app-icon icon.svg
//   dotnet run --project src/QuickPrompt.DevHost -- --action-icons src/QuickPrompt.Plugin/package/actionicons
//
// The "clipboard" is simply --text.
using QuickPrompt.Core;

var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
for (var i = 0; i < args.Length; i++)
{
    if (args[i].StartsWith("--") && i + 1 < args.Length && !args[i + 1].StartsWith("--"))
    {
        options[args[i][2..]] = args[++i];
    }
    else if (args[i].StartsWith("--"))
    {
        flags.Add(args[i][2..]);
    }
}

string Opt(string name, string fallback) => options.TryGetValue(name, out var v) ? v : fallback;

// --action-icons <dir> : writes the package's actionicons/*.svg (static action images) and exits.
if (options.TryGetValue("action-icons", out var actionIconDir))
{
    Directory.CreateDirectory(actionIconDir);
    var icons = new Dictionary<string, string>
    {
        ["Loupedeck.QuickPromptPlugin.AskClaudeCommand.svg"] = IconCatalog.AppIconSvg(80, 80),
        ["Loupedeck.QuickPromptPlugin.AskChatGptCommand.svg"] = IconCatalog.AppIconSvg(80, 80),
        ["Loupedeck.QuickPromptPlugin.SettingsCommand.svg"] = IconCatalog.BuildSvg("settings", "slate", "slate", "", 80, 80),
        ["Loupedeck.QuickPromptPlugin.HistoryCommand.svg"] = IconCatalog.BuildSvg("history", "slate", "slate", "", 80, 80),
    };
    foreach (var (name, svg) in icons)
    {
        File.WriteAllText(Path.Combine(actionIconDir, name), svg);
    }

    Console.WriteLine($"Action icons: {actionIconDir} ({icons.Count})");
    return;
}

// --app-icon icon.svg : writes the app icon (256 px) and exits.
if (options.TryGetValue("app-icon", out var appIconPath))
{
    File.WriteAllText(appIconPath, IconCatalog.AppIconSvg(256, 256));
    Console.WriteLine("App icon: " + appIconPath);
    return;
}

// --icons sheet.html : writes a preview of every icon/color combination and exits.
if (options.TryGetValue("icons", out var sheetPath))
{
    var sb = new System.Text.StringBuilder("<!doctype html><meta charset=utf-8><body style='font:12px sans-serif;background:#2b2b2b;color:#eee'><table>");
    foreach (var icon in IconCatalog.Icons)
    {
        sb.Append($"<tr><td style='padding-right:10px'>{icon.Id}</td>");
        foreach (var color in IconCatalog.Colors)
        {
            sb.Append("<td>").Append(IconCatalog.BuildSvg(icon.Id, color.Id, "orange", "Write email", 64, 64)).Append("</td>");
        }

        sb.Append("</tr>");
    }

    File.WriteAllText(sheetPath, sb.Append("</table>").ToString());
    Console.WriteLine("Icon preview: " + sheetPath);
    return;
}

var dataDir = Opt("data", Path.Combine(Path.GetTempPath(), "quick-prompt-devhost"));
var host = new ConsoleHost(dataDir) { Clipboard = Opt("text", "") };
using var manager = new ChatManager(dataDir, host);
if (flags.Contains("no-open"))
{
    manager.Launcher.OverrideOpen = url => Console.WriteLine("Popup URL: " + url);
}

// macOS: use the popup helper built by native/macos/build.sh, if present.
foreach (var candidate in new[]
{
    Path.Combine(Directory.GetCurrentDirectory(), "native", "macos", "build", "QuickPromptPopup"),
    Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "native", "macos", "build", "QuickPromptPopup")),
})
{
    if (File.Exists(candidate))
    {
        manager.Launcher.DefaultPopupPath = candidate;
        Console.WriteLine("Popup helper: " + candidate);
        break;
    }
}

manager.Start();
Console.WriteLine($"Settings: {manager.SettingsPath}");

var preset = new Preset
{
    ProviderId = Opt("provider", ProviderIds.Claude),
    Title = Opt("title", "Test"),
    Instruction = Opt("prompt", ""),
    Input = Opt("input", InputModes.Clipboard),
    Flow = Opt("flow", FlowModes.Edit),
    Model = Opt("model", ""),
    Connection = Opt("connection", ConnectionModes.Default),
};

if (flags.Contains("settings"))
{
    manager.OpenSettings();
}
else if (flags.Contains("history"))
{
    manager.OpenHistory();
}
else
{
    await manager.ExecutePresetAsync(preset);
}

Console.WriteLine("Press Ctrl+C to quit.");
await Task.Delay(Timeout.Infinite);

internal sealed class ConsoleHost : IHostBridge
{
    private readonly string _secretsFile;

    public ConsoleHost(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        this._secretsFile = Path.Combine(dataDir, "devhost-secrets.json");
    }

    // DevHost only: plain JSON file in the data folder (the plugin stores keys encrypted).
    public string GetSecret(string name) => this.ReadSecrets().TryGetValue(name, out var v) ? v : null;

    public void SetSecret(string name, string value)
    {
        var all = this.ReadSecrets();
        if (string.IsNullOrEmpty(value))
        {
            all.Remove(name);
        }
        else
        {
            all[name] = value;
        }

        File.WriteAllText(this._secretsFile, System.Text.Json.JsonSerializer.Serialize(all));
    }

    private Dictionary<string, string> ReadSecrets() =>
        File.Exists(this._secretsFile)
            ? System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(this._secretsFile)) ?? new()
            : new();

    public string Clipboard { get; set; } = "";

    public string GetClipboardText() => this.Clipboard;

    public void SetClipboardText(string text)
    {
        this.Clipboard = text;
        Console.WriteLine($"[clipboard] {Shorten(text)}");
    }

    public void SendCopyShortcut() => Console.WriteLine("[key] Ctrl/Cmd+C");

    public void SendPasteShortcut() => Console.WriteLine($"[key] Ctrl/Cmd+V → {Shorten(this.Clipboard)}");

    public void Notify(string title, string message, bool isError) => Console.WriteLine($"[bildirim] {title}: {message}");

    public void Log(string message, Exception exception = null) => Console.WriteLine($"[log] {message} {exception?.Message}");

    private static string Shorten(string s) => s.Length > 80 ? s[..80] + "…" : s;
}
