# Roadmap

## Now (0.2)

- [x] Ask Claude / Ask ChatGPT action-editor presets on the Actions Ring
- [x] Selection capture that never sends a stale clipboard
- [x] macOS native popup with Liquid Glass (vibrancy fallback)
- [x] CI: tests, Windows build, macOS package, rolling "Latest build" release
- [x] CLI or API per provider and per preset; Settings view with encrypted API keys
- [x] "Paste in popup" input
- [x] Quick Prompt app icon (default preset icon)
- [x] Conversation history: reopen and continue (CLI and API)

## Next

- [ ] **Windows popup:** a native, borderless, topmost window at the cursor (WebView2 + Mica/Acrylic), matching the macOS behavior: no focus stealing, paste back into the previous app.
- [ ] Verify the Windows plugin on a real device: clipboard, Ctrl+C/V, CLI discovery (`claude.exe`, `codex.cmd`).
- [ ] Popup polish: dragging, keeping the position, light/dark accent per provider.
- [ ] Busy feedback for the no-popup behaviors (replace / copy).
- [ ] More settings in the Settings view (CLI paths, system prompt, effort) instead of editing `settings.json`.
- [ ] Verify the OpenAI default model name and the API connections with real keys.

## Before the Logi Marketplace

- [x] License: MIT (see `LICENSE`)
- [ ] Developer ID signing and notarization of the macOS popup
- [ ] Localization (`logiplugintool xliff`), with English as the base
- [ ] Marketplace approval guidelines review, screenshots, support page
