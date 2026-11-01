# Architecture

```
Actions Ring button (Options+ action editor preset)
  └─ QuickPrompt.Plugin  (Logi Actions SDK, runs inside Logi Plugin Service)
       │  HostBridge: clipboard, ⌘C/⌘V or Ctrl+C/V, notifications
       └─ QuickPrompt.Core (no Logitech dependency)
            ├─ ChatManager      runs a preset: read selection → start session → open popup / paste / copy
            ├─ ChatSession      one popup = one conversation (history, streaming state, long-poll versioning)
            ├─ ClaudeCliProvider  claude -p --output-format stream-json --tools "" …   (prompt on stdin)
            ├─ CodexCliProvider   codex exec --sandbox read-only --json --ephemeral -  (prompt on stdin)
            ├─ AnthropicApiProvider  Anthropic C# SDK, streamed, server-side refusal fallbacks
            ├─ OpenAiApiProvider     Chat Completions over HttpClient, streamed (SSE)
            ├─ CliLocator       finds the CLIs (Homebrew, npm, nvm, ~/.local/bin, login-shell PATH)
            ├─ UiServer         HTTP on 127.0.0.1, random port, per-session token
            ├─ ChatPage         the popup page (HTML/CSS/JS, no external resources)
            └─ WindowLauncher   macOS: native popup · otherwise: Edge/Chrome --app window
                 └─ native/macos/QuickPromptPopup (Swift: NSPanel + WKWebView + NSGlassEffectView)
```

## Projects

| Path | What |
|---|---|
| `src/QuickPrompt.Core` | Everything that does not need the Logitech SDK. Unit-tested. |
| `src/QuickPrompt.Plugin` | The Logi plugin: `QuickPromptPlugin`, the two action-editor commands, `HostBridge`, package files. |
| `src/QuickPrompt.DevHost` | Console app that runs Core and the popup without Options+. |
| `native/macos` | Swift popup helper (`QuickPromptPopup`), built into the package. |
| `tests/QuickPrompt.Tests` | xUnit tests for Core. |

## Reading the selection

The plugin presses ⌘C/Ctrl+C and watches the clipboard:

1. If the clipboard holds text, it is first replaced with a unique marker, so even copying the same text is detected.
2. It waits 250 ms for the ring to close and focus to return to the app, sends the shortcut, and polls for up to 1.5 s, retrying the shortcut once after 600 ms.
3. It restores the previous clipboard text.
4. If nothing was copied, the old clipboard is **not** used. The popup opens with an empty text box and a notice.

## CLI calls

- The prompt goes in on stdin, which avoids command-line quoting and length limits.
- Claude: `--tools ""` (no tools), `--strict-mcp-config` (no MCP servers), `--no-session-persistence`, and `--append-system-prompt` for the instructions. Output is `stream-json` with partial messages, so the answer streams token by token.
- Codex: `--sandbox read-only`, `--ephemeral`, `--json`, and `-o <file>` as a fallback for the final message. There is no system-prompt flag, so the instructions are put in front of the prompt.
- Calls are stateless. Follow-up questions carry the earlier turns inside the prompt.
- Each CLI runs in an empty `workspace/` folder in the plugin data directory, so no project context is picked up.

## CLI or API

`ChatManager.GetProvider(id, connection)` picks the provider: the preset's **Connection** (`cli` / `api`), or for `default` the provider's connection in Settings (`ClaudeConnection`, `ChatGptConnection`). API keys go through `IHostBridge.GetSecret/SetSecret`. The plugin stores them in Logi Plugin Service's encrypted plugin settings (local only), never in `settings.json`. The popup's Settings view reads `GET /s/<token>/settings`, which returns only a masked key hint, and writes `POST /s/<token>/settings`.

- **Anthropic:** official C# SDK (`Anthropic` NuGet), beta Messages endpoint with `fallbacks: "default"` (`server-side-fallback-2026-07-01`), `output_config.effort` (default `low` for quick answers), automatic prompt caching (top-level `cache_control`), streamed. `haiku`/`sonnet`/`opus` map to the current model ids.
- **OpenAI:** `POST /v1/chat/completions` with `stream: true`, parsed by `OpenAiStreamParser`.

How a follow-up is sent differs by connection (`AiRequest`):

- **CLI:** one prompt (`AiRequest.Prompt`) with the earlier turns written out, because each call is a fresh process.
- **API:** the conversation as separate messages (`AiRequest.History` + `AiRequest.Message`): user / assistant / … / user. Assistant turns carry only their text; thinking is never stored, so no thinking blocks are replayed and the history is append-only. That suits preserved thinking and keeps the prompt cache valid across follow-ups.

## History

Calls are stateless for both CLI and API: a follow-up carries the earlier turns in its prompt. So the conversation lives entirely in `ChatSession`. After every finished turn, `ChatManager` writes it to `history/<id>.json` (`ConversationStore`, last 50, plugin data folder). `POST /s/<token>/resume` loads a saved conversation into the current popup, with its original provider and connection, and the next message simply continues it. Turning history off in Settings deletes the folder's contents.

## Action images

`package/actionicons/<action class>.svg` gives each action a static image. Logi Plugin Service checks this folder first, and the Actions Ring did not show `GetCommandImage` results for action-editor presets. Regenerate the files with `dotnet run --project src/QuickPrompt.DevHost -- --action-icons src/QuickPrompt.Plugin/package/actionicons`.

## Popup

- `UiServer` listens on `127.0.0.1` only. Every URL contains a random 144-bit session token. The `Host` header is checked against DNS rebinding, and state-changing requests need an `X-QuickPrompt` header to block cross-site posts. The page has a strict CSP and no external resources.
- The page long-polls `/s/<token>/state?v=N`. Endpoints: `send`, `cancel`, `copy`, `paste`, `closed`.
- **macOS:** `QuickPromptPopup` is a borderless, *non-activating* `NSPanel` at the mouse cursor, so the user's app stays frontmost and **Paste** goes straight into it. The page is transparent. It reports its height and the rectangles of its `.glass` elements through `window.webkit.messageHandlers.quickPrompt`, and the panel puts an `NSGlassEffectView` behind each one (looked up at runtime; `NSVisualEffectView` before macOS 26). One popup at a time: a new one tells older ones to close through a distributed notification, except pinned ones. The page also reports its width (Settings → Popup size), asks the panel to follow the mouse (`drag`, a 120 Hz loop until the button is released, so it works while the web view tracks the mouse), and tells it whether it is pinned (`pin`). A pinned popup collapses to a 56 px icon on Esc / ✕ instead of closing; the page keeps the conversation and restores it on click.
- **Other platforms:** the same page in an Edge/Chrome `--app` window with a separate profile, where CSS imitates the glass. The default browser is the last resort.
