// Quick Prompt engine, started by the desktop app.
//
//   QP_CONTROL_TOKEN  (required) token the app sends in X-QuickPrompt-Control
//   QP_DATA_DIR       data folder (default: ~/Library/Application Support/Quick Prompt)
//
// Prints "READY <port>" on stdout when listening. Exits when stdin closes (the app quit or died).
using QuickPrompt.Agent;
using QuickPrompt.Core;

var token = Environment.GetEnvironmentVariable("QP_CONTROL_TOKEN");
if (string.IsNullOrWhiteSpace(token))
{
    Console.Error.WriteLine("QP_CONTROL_TOKEN is required.");
    return 2;
}

var dataDir = Environment.GetEnvironmentVariable("QP_DATA_DIR");
if (string.IsNullOrWhiteSpace(dataDir))
{
    dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Quick Prompt");
}

var control = new AppControl(token);
var host = new AgentHost(control);
using var voices = new QuickPrompt.Voice.LocalVoices(dataDir, (message, ex) => host.Log(message, ex));
using var manager = new ChatManager(dataDir, host) { Control = control, ReadAloud = voices };
control.Manager = manager;

// The app shows popups itself: hand it the URL instead of starting a window.
manager.Launcher.OverrideOpen = url => control.Publish(new { type = "open", url });
manager.Start();

Console.Out.WriteLine($"READY {manager.Port}");
Console.Out.Flush();

// Lifetime: the app keeps our stdin open; EOF means it is gone.
await Task.Run(() =>
{
    try
    {
        while (Console.In.Read() >= 0)
        {
        }
    }
    catch (IOException)
    {
    }
});
return 0;
