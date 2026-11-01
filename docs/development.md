# Development

## Requirements

- .NET 10 SDK
- macOS popup: Xcode Command Line Tools (`xcode-select --install`)
- To run the plugin: Logi Options+ with an Actions Ring–capable device

The Logitech `PluginApi` comes from the `LogiPluginApi` NuGet package, so building does not need Options+. `logiplugintool` is pinned as a local .NET tool (`.config/dotnet-tools.json`).

## Common tasks

```bash
dotnet build QuickPrompt.slnx                    # Debug build; with Options+ installed, links and reloads the plugin
dotnet test tests/QuickPrompt.Tests              # unit tests
QUICKPROMPT_IT=1 dotnet test tests/QuickPrompt.Tests   # also calls the real Claude CLI
scripts/package.sh                               # macOS/Linux: (popup +) Release build → dist/QuickPrompt.lplug4
scripts/package.sh --install                     # …and open it in Options+ (macOS)
.\scripts\package.ps1 -Install                   # Windows
```

A Debug build of `src/QuickPrompt.Plugin` writes `QuickPrompt.link` into the Logi Plugin Service `Plugins` folder and asks the service to reload the plugin. Uninstall the packaged version first to avoid having two copies. Remove the link with `dotnet msbuild src/QuickPrompt.Plugin -t:UnlinkPlugin`.

## DevHost (no device needed)

```bash
dotnet run --project src/QuickPrompt.DevHost -- --provider claude --flow send --prompt "Summarize: {text}" --text "…"
dotnet run --project src/QuickPrompt.DevHost -- --provider codex --flow edit
dotnet run --project src/QuickPrompt.DevHost -- --icons icons.html
```

On macOS, DevHost uses `native/macos/build/QuickPromptPopup` if it has been built (`bash native/macos/build.sh`); otherwise it opens a browser window. `--no-open` only prints the popup URL.

## CI and releases

`.github/workflows/build.yml`:

| Job | Runner | |
|---|---|---|
| test | Ubuntu | unit tests |
| windows | Windows | builds everything and runs the tests |
| package | macOS | builds the Swift popup (universal binary, ad-hoc signed) and the plugin, packs and verifies `QuickPrompt.lplug4`, uploads it as an artifact |
| publish | Ubuntu | on every push to `main`: updates the **Latest build** pre-release (tag `latest`). On `v*` tags: creates a versioned release. |

Nobody has to run anything locally to try a change: push to `main`, then download `QuickPrompt.lplug4` from the *Latest build* release.

## Conventions

- Everything in the repository is in English: code, comments, UI strings and docs.
- Core must not reference the Logitech SDK. Platform-specific behavior goes through `IHostBridge` (plugin) or `WindowLauncher` (popup).
- The popup page has no external resources (CSP `default-src 'none'`).
