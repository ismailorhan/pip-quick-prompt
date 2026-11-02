# Roadmap

## Done (0.3)

- [x] Mac menu bar app: background engine, shortcut (configurable), Open at Login, Check for Updates
- [x] Developer ID signing and notarization; DMG for first installs
- [x] Pip: floating icon with animation, toolbar, keep on screen / hide / never show, snap to an edge
- [x] Conversation as glass bubbles, stacked "latest only" view, slide in/out, glass density
- [x] Settings with sidebar sections (resizable); provider setup lists (plugins, skills, MCP servers)
- [x] Prompt templates: {$selected} / {$clipboard} / fields, on/off, export/import, own setup per template
- [x] Pictures: paste or drop, previews, clipboard screenshots; all four connections

## Earlier (0.2)

- [x] Ask Claude / Ask ChatGPT action-editor presets on the Actions Ring
- [x] Selection capture that never sends a stale clipboard
- [x] macOS native popup with Liquid Glass (vibrancy fallback)
- [x] CI: tests, Windows build, macOS package, rolling "Latest build" release
- [x] CLI or API per provider and per preset; Settings view with encrypted API keys
- [x] "Paste in popup" input
- [x] Quick Prompt app icon (default preset icon)
- [x] Conversation history: reopen and continue (CLI and API)

## Next

- [ ] Confirm on a Mac: the 0.3.38 changes listed in CLAUDE.md → Status.
- [ ] "Show latest only" sticky at the top of the conversation.
- [ ] Logi plugin: hand requests to the Mac app when it is running (one engine, one history).
- [ ] **Windows app:** tray app with the same engine (WebView2 + Mica).

- [ ] **Windows popup:** a native, borderless, topmost window at the cursor (WebView2 + Mica/Acrylic), matching the macOS behavior: no focus stealing, paste back into the previous app.
- [ ] Verify the Windows plugin on a real device: clipboard, Ctrl+C/V, CLI discovery (`claude.exe`, `codex.cmd`).
- [ ] Popup polish: dragging, keeping the position, light/dark accent per provider.
- [ ] Busy feedback for the no-popup behaviors (replace / copy).
- [ ] More settings in the Settings view (CLI paths, system prompt, effort) instead of editing `settings.json`.
- [ ] Verify the OpenAI default model name and the API connections with real keys.

## Before the Logi Marketplace

- [x] License: MIT (see `LICENSE`)
- [x] Developer ID signing and notarization of the Mac app (the plugin's popup helper is still ad hoc)
- [ ] Localization (`logiplugintool xliff`), with English as the base
- [ ] Marketplace approval guidelines review, screenshots, support page
