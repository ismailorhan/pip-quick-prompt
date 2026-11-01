# Quick Prompt

Ask Claude or ChatGPT about the text you have selected, from your Logitech **Actions Ring**, without leaving the app you are working in.

Press a ring button and the selection, together with that button's prompt, is sent in the background through the **Claude Code CLI** or the **Codex CLI** (signed in with your ChatGPT account). The answer streams into a small popup next to the mouse cursor. On macOS the popup is a native Liquid Glass panel. The Claude and ChatGPT apps never open, and your current app stays in front.

![Quick Prompt popup](docs/images/popup-dark.png)

## Features

- **Two actions:** *Ask Claude* and *Ask ChatGPT*. Add either one to the ring as many times as you like.
- **Each ring button is a preset**, with its own title, prompt, input source, behavior, icon, color and model, all set in the Options+ action editor.
- **Four behaviors:** send and show the answer in the popup, edit in the popup first, replace the selection (no popup), or copy to the clipboard (no popup).
- **Popup:** follow-up questions, **Copy**, and **Paste** (⌘↵ / Ctrl+Enter) into the app you came from.
- **No API keys:** it uses the subscriptions you already have through the official CLIs. Tools are disabled and the CLIs run read-only.

## Install

1. Download `QuickPrompt.lplug4` from the [latest release](../../releases/latest) (or the rolling **Latest build** pre-release).
2. Double-click it. Logi Options+ installs the plugin.
3. Make sure the CLIs you want are installed and signed in:
   - Claude: [Claude Code](https://code.claude.com/docs). Run `claude` once to sign in.
   - ChatGPT: `npm i -g @openai/codex`, then run `codex` once and sign in with your ChatGPT account.
4. In Options+, open **Actions Ring**, find the **Quick Prompt** group and drag *Ask Claude* or *Ask ChatGPT* onto a slot.

On macOS, Options+ needs *Accessibility* permission to send ⌘C/⌘V. It usually has it already.

## Presets

| Field | Meaning |
|---|---|
| Title | Short name shown on the ring |
| Prompt | `{text}` is replaced with the selected text. Without it, the text is appended. |
| Input | Selected text (copied with ⌘C/Ctrl+C), clipboard text, or none |
| Behavior | Send now · Edit in popup first · Replace selection · Copy to clipboard |
| Icon / Color | 18 line icons or the title's initials, in 10 colors. Options+'s own icon editor also works. |
| Model | Optional, e.g. `sonnet`, `haiku`. Empty uses the CLI default. |

If the selection cannot be read, the old clipboard is **never** sent. The popup opens with an empty text box instead.

Example presets:

| Title | Provider | Prompt | Behavior |
|---|---|---|---|
| Ask | Claude | *(empty)* | Edit in popup first |
| Summarize | Claude | `Summarize the following in 3 bullet points:\n\n{text}` | Send now |
| Fix | Claude | `Fix spelling and grammar. Reply with the corrected text only:\n\n{text}` | Replace selection |
| Translate | ChatGPT | `Translate to English (or to Turkish if it is English). Reply with the translation only:\n\n{text}` | Replace selection |

## Settings

`settings.json` is created in the plugin's data folder on first run; the plugin log prints its path. Changes apply without a restart.

| Key | Default | |
|---|---|---|
| `ClaudePath`, `CodexPath` | auto-detect | Full path if the CLI is not found |
| `PopupPath` | bundled | macOS popup helper |
| `BrowserPath` | auto-detect | Chromium-based browser for the fallback window |
| `SystemPrompt` | answer directly, no tools, in the request's language | Added to every request |
| `TimeoutSeconds` | 300 | |
| `WindowWidth`, `WindowHeight` | 520 × 560 | Browser fallback window only |
| `RestoreClipboard` | true | Restore the clipboard after reading the selection or pasting |

## Platforms

| | macOS | Windows |
|---|---|---|
| Plugin, presets, CLIs | ✅ | ✅ (not yet tested on a device) |
| Popup | Native Liquid Glass panel (vibrancy before macOS 26) | Small Edge/Chrome app window for now. A native popup is on the [roadmap](docs/roadmap.md). |

## Documentation

- [Architecture](docs/architecture.md)
- [Development](docs/development.md): building, testing, DevHost, CI
- [Roadmap](docs/roadmap.md)

## License

[MIT](LICENSE). Quick Prompt is not affiliated with Anthropic, OpenAI or Logitech. It only runs CLIs the user has installed.
