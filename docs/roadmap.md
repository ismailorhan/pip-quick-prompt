# Roadmap

## Now (0.2)

- [x] Ask Claude / Ask ChatGPT action-editor presets on the Actions Ring
- [x] Selection capture that never sends a stale clipboard
- [x] macOS native popup with Liquid Glass (vibrancy fallback)
- [x] CI: tests, Windows build, macOS package, rolling "Latest build" release

## Next

- [ ] **Windows popup:** a native, borderless, topmost window at the cursor (WebView2 + Mica/Acrylic), matching the macOS behavior: no focus stealing, paste back into the previous app.
- [ ] Verify the Windows plugin on a real device: clipboard, Ctrl+C/V, CLI discovery (`claude.exe`, `codex.cmd`).
- [ ] Popup polish: dragging, keeping the position, light/dark accent per provider.
- [ ] Busy feedback for the no-popup behaviors (replace / copy).
- [ ] Settings UI inside the plugin (CLI paths, system prompt) instead of editing `settings.json`.

## Before the Logi Marketplace

- [ ] Decide on the license (`LoupedeckPackage.yaml` says MIT for now) and the author/publisher name
- [ ] Developer ID signing and notarization of the macOS popup
- [ ] Localization (`logiplugintool xliff`), with English as the base
- [ ] Marketplace approval guidelines review, screenshots, support page
