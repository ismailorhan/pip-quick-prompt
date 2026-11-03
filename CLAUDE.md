# Quick Prompt: project memory

Read this first. It is the working memory of the project: what it is, how we work, where things are,
and what is open. Keep it current when something important changes.

## What it is

Quick Prompt asks Claude or ChatGPT about whatever you are looking at (the selection, the clipboard, a
pasted screenshot) and shows the streamed answer in a small floating glass popup, without bringing the
Claude or ChatGPT apps to the front. It started as a Logi Actions Ring plugin and became a macOS menu bar
app; the plugin still exists.

- **Mac app** (`Quick Prompt.app`): menu bar item (✨), a global shortcut (default ⌥⇧Space, configurable),
  and **Pip**, the floating icon that stays on screen. Runs in the background; only *Quit Quick Prompt*
  in the menu bar menu quits it.
- **Logi plugin** (`QuickPrompt.lplug4`): Ask Claude / Ask ChatGPT action-editor presets for the Actions
  Ring. Uses its own popup helper process.
- **Windows**: not started for the app (the plugin builds on Windows; the popup there is a browser window).

## How we work (agreed with the owner)

- The owner speaks **Turkish** in chat. **Everything in the repository is English**: code, comments, UI
  strings, docs, commit messages.
- **Do not build or release unless the owner says "build" / "build al".** Work on the **`dev`** branch
  (pushing it builds nothing). "Build" means: fast-forward `main` to `dev` and push `main`; CI then builds,
  signs, notarizes and publishes the rolling **Latest build** release.
- The owner tests on their Mac by updating from the menu (*Check for Updates…*) or the DMG. They prefer
  not to run commands; give one-line commands only when unavoidable.
- Show UI changes with screenshots before building (Playwright + Chromium render the page with mocked
  state; see `docs/development.md`).
- Swift code cannot be compiled outside macOS: it is only checked by the CI macOS job. Keep Swift changes
  small and careful, and read the CI log after a build.
- Commit messages end with the attribution lines the session asks for.

## Architecture (short)

```
Quick Prompt.app (Swift, native/macos/App)            Logi Options+ → QuickPrompt.Plugin (.NET)
  menu bar, shortcut, Pip/popups (PopupController)       └─ QuickPromptPopup helper (Swift, native/macos/Popup)
  presses ⌘C/⌘V, reads nothing itself                    
  └─ starts QuickPrompt.Agent (.NET, self-contained)  ── both host QuickPrompt.Core:
       token-protected /api/* + long-poll /api/events      ChatManager, ChatSession, providers, history,
                                                           prompt templates, local page server (UiServer)
Popup page: one HTML/JS page (src/QuickPrompt.Core/ChatPage.cs) in a WKWebView; native glass views
(NSGlassEffectView / vibrancy) sit behind the page's ".glass" shapes, positioned from rects the page reports.
```

- Providers: Claude CLI (`claude -p … stream-json`), Codex CLI (`codex exec --json`), Anthropic API
  (official C# SDK, beta messages, server-side fallback), OpenAI Chat Completions. CLI or API per provider
  and per template. Pictures work with all four.
- "Use my plugins, skills and MCP servers": the CLIs can use the owner's Claude Code / Codex setup, chosen
  per item in Settings and per template; never shell or file-writing tools.
- Prompt templates (`actions.json` + built-ins): parameters `{$selected}`, `{$clipboard}` (system, `$`),
  `{name}` and `{name:a|b}` (fields asked in a form). On/off, export/import (`.json`).
- Data: `~/Library/Application Support/Quick Prompt` (settings.json, history/, actions.json, presets.json);
  API keys in the macOS Keychain (service "Quick Prompt").
- Engine log: `~/Library/Logs/Quick Prompt/agent.log` (the agent's stderr, written by the app; over 1 MB it
  moves to `agent.old.log`). Look here first when the owner reports something odd.
- Swift can be type-checked on the owner's Mac (Xcode is installed):
  `cd native/macos && swiftc -typecheck -swift-version 5 -target arm64-apple-macos12.0 App/main.swift App/Updater.swift Shared/PopupController.swift`.

Details: `docs/architecture.md`, `docs/development.md`, `README.md`.

## Where things are

| Path | What |
|---|---|
| `src/QuickPrompt.Core` | Engine. `ChatPage.cs` is the whole popup UI (HTML/CSS/JS in one string). |
| `src/QuickPrompt.Agent` | Engine process for the Mac app (`AgentHost`: pbcopy/pbpaste, Keychain, clipboard pictures via AppleScript + sips). |
| `src/QuickPrompt.Plugin` | Logi plugin. |
| `src/QuickPrompt.DevHost` | Runs Core without Options+ or the app. |
| `native/macos/Shared/PopupController.swift` | The floating panel: glass, drag/throw, Pip anchoring, Dock avoidance, menus, save/open dialogs. |
| `native/macos/App/` | Menu bar app (`main.swift`), in-app updater (`Updater.swift`), entitlements, Info.plist. |
| `native/macos/build-app.sh` | Builds the app, signs (Developer ID when secrets are set), notarizes, makes the DMG and `QuickPrompt-mac.json`. |
| `.github/workflows/build.yml` | CI: Linux tests; macOS package + app + DMG; publish the Latest build release. |
| `tests/QuickPrompt.Tests` | xUnit (≈100 tests), incl. a `node --check` of the page script. |
| `tools/icon-lab.html` | Stand-alone page used to tune Pip's animation. |

## Release and signing

- Repository: `ismailorhan/pip-quick-prompt` (renamed from `quick-prompt-for-logi-action-rings` on 3 Nov 2026;
  GitHub redirects the old name, but code and docs use the new one).
- Version `0.3.<CI run number>`, stamped by CI.
- GitHub secrets (set by the owner): `MACOS_CERT_P12`, `MACOS_CERT_PASSWORD`, `APPLE_TEAM_ID` (S7J34LZJ27),
  `ASC_KEY_P8`, `ASC_KEY_ID`, `ASC_ISSUER_ID`. Developer ID Application certificate valid until 2 Feb 2027.
- Latest build assets: `QuickPrompt.dmg` (first install), `QuickPrompt-mac.zip` + `QuickPrompt-mac.json`
  (in-app updates: SHA-256 and same-team signature are checked), `QuickPrompt.lplug4`.

## Status (v0.3.41, 3 Nov 2026)

Done and released: menu bar app; Pip (snow-globe/neural animation, toolbar, keep on screen, hide / never
show, middle click snaps to the nearest edge, opens popups beside it, remembers its place); conversation as
glass bubbles with a stacked "latest only" view; slide in/out; glass density; Settings with sidebar sections
(resizable); configurable shortcut; prompt templates (parameters, fields, on/off, export/import, own
plugins/MCP/skills); pictures (paste, drop, previews, `{$clipboard}` screenshots); history; Developer ID
signing + notarization; DMG; Check for Updates.

Not yet confirmed on the owner's Mac (built, untested): faster shortcut, popup placement at Pip, icon
Copy/Paste, Pip after Paste, middle-click snap, stacked view, bubble ✕, first-click on Pip, the own
Accessibility dialog with "Reset and Open Settings", template export/import dialogs, pictures from the
clipboard.

Built as v0.3.41 (3 Nov 2026), not yet confirmed on the owner's Mac: Pip left on screen overnight showed "This popup session has ended"
in the morning, because sessions idle for 3 h were forgotten even while Pip's page was polling. Now a
session a window is polling is kept; if one is lost anyway (long sleep), Pip asks the app to open a fresh
Pip in its place. The engine log is now written to a file (see above). The same lost session made Pip's
template list say "No templates yet" (the built-ins are always there) and New Chat say "Still answering";
any 404 now goes the same way. Also: Pip no longer jumps sideways when its toolbar opens next to
the Dock (the hover resize skips Dock avoidance); the conversation slides in only after the panel has its
new size, and only the conversation's glass slides (26 px, 0.3 s in / 0.22 s out); middle click on an edge
goes clockwise to the next corner; ⌥-click on Pip does the same (trackpads have no middle button; the
Windows app should use Alt-click). Pip's toolbar opens above Pip near the bottom of the screen (or the
Dock's top) and lines up with Pip's left or right side near that edge (`reportEdges` → `__quickPromptEdges`).
Pip's right-click menu has a templates submenu. Settings has a Shortcuts page (keep it current when adding
one). Open: in headless Chromium the page draws nothing for ~0.75 s after
collapsing (no long script task, so rendering); not known whether WKWebView does the same.

Known issue being fixed: the Accessibility prompt kept appearing because the TCC entry belonged to an older,
ad-hoc signed copy. Fix: `tccutil reset Accessibility app.quickprompt.mac` (or the app's Reset button),
then switch Quick Prompt on again. Signing is stable now, so it should hold across updates.

## Open items / ideas

- Make "Show latest only" sticky at the top of the conversation (offered, not done).
- Logi plugin: hand requests to the Mac app when it is running (one engine, one history).
- Windows app: tray app with the same engine (WebView2 + Mica). Pip there: Alt-click = middle click (the page already treats altKey as ⌥); the Shortcuts page then needs Windows key names.
- Verify the OpenAI default model name (`gpt-5-mini`) and the Codex CLI with a real login.
- Logi Marketplace: localization, review, screenshots, support page.
- Mac App Store is not planned (the app needs Accessibility and runs CLIs).
