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

- The owner speaks **Turkish** in chat. **Every reply to the owner is in Turkish**, including short
  progress notes while working, unless they ask for another language at that moment.
  **Everything in the repository is English**: code, comments, UI strings, docs, commit messages.
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
- Agents (`settings.json` → `Agents`, `DefaultAgentId`; `Agent.cs`): a named AI setup = AI, connection (CLI/API),
  model, API effort, plugins/skills/MCP choice, own instruction. The default agent serves new chats, Pip and the
  shortcuts; templates store `AgentId` ("" = default agent; null only in files from before agents, migrated once at
  start); the chat's agent chip switches the agent for the next message. Ring presets (provider + overrides) resolve
  to their AI's agent with the overrides (`ResolveAgent`). API keys stay per provider (Settings → API keys).
- Prompt templates (`actions.json` + built-ins): parameters `{$selected}`, `{$clipboard}` (system, `$`),
  `{name}` and `{name:a|b}` (fields asked in a form). On/off, export/import (`.json`).
- Data: `~/Library/Application Support/Quick Prompt` (settings.json, history/, actions.json, presets.json);
  API keys in the macOS Keychain (service "Quick Prompt").
- Engine log: `~/Library/Logs/Quick Prompt/agent.log` (the agent's stderr, written by the app; over 1 MB it
  moves to `agent.old.log`). Look here first when the owner reports something odd.
- Swift can be type-checked on the owner's Mac (Xcode is installed):
  `cd native/macos && swiftc -typecheck -swift-version 5 -target arm64-apple-macos12.0 App/main.swift App/Updater.swift App/Speech.swift App/Ring.swift Shared/PopupController.swift`.

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
| `native/macos/Speech/` | Dictation helper `QuickPromptSpeech` (Swift + whisper.cpp, arm64): mic → live text → final pass; `build-whisper.sh` builds whisper.cpp v1.9.5 static. `App/Speech.swift` runs it and downloads the model. |
| `native/macos/build-app.sh` | Builds the app, signs (Developer ID when secrets are set), notarizes, makes the DMG and `QuickPrompt-mac.json`. |
| `.github/workflows/build.yml` | CI: Linux tests; macOS package + app + DMG; publish the Latest build release. |
| `tests/QuickPrompt.Tests` | xUnit (≈100 tests), incl. a `node --check` of the page script. |
| `tools/icon-lab.html` | Stand-alone page used to tune Pip's animation. |
| `tools/pip-motion-test/` | Native test of how Pip moves (real PopupController, mock engine); `run.sh`. |

## Release and signing

- Repository: `ismailorhan/pip-quick-prompt` (renamed from `quick-prompt-for-logi-action-rings` on 3 Nov 2026;
  GitHub redirects the old name, but code and docs use the new one).
- Version `0.3.<CI run number>`, stamped by CI.
- GitHub secrets (set by the owner): `MACOS_CERT_P12`, `MACOS_CERT_PASSWORD`, `APPLE_TEAM_ID` (S7J34LZJ27),
  `ASC_KEY_P8`, `ASC_KEY_ID`, `ASC_ISSUER_ID`. Developer ID Application certificate valid until 2 Feb 2027.
- Latest build assets: `QuickPrompt.dmg` (first install), `QuickPrompt-mac.zip` + `QuickPrompt-mac.json`
  (in-app updates: SHA-256 and same-team signature are checked), `QuickPrompt.lplug4`.

## Status (v0.3.67, 9 Nov 2026)

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

Built (v0.3.41 / v0.3.42, 3-4 Nov 2026), not yet confirmed on the owner's Mac: Pip left on screen overnight showed "This popup session has ended"
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

v0.3.42 (measured with the real PopupController in a test app, see `docs/development.md` → not yet written
there; the method is in Hindsight): Pip no longer drifts on hover (its place is the truth, the panel follows),
corners place Pip itself, the toolbar keeps its size and morphs its glass (empty parts let clicks through),
and a snapshot of Pip covers the web view while the panel resizes (no sideways jump).
v0.3.43 (4 Nov 2026), not yet confirmed on the owner's Mac: Pip stays under the Dock (level just below it) and a Dock that does not
hide is the screen's edge; near the bottom the templates list and bubble open upward; the open/close blink is
gone (freeze now waits: the page asks, the native side takes the snapshot of the unchanged page, shows it in a
separate still window, hides the web view, then answers `__quickPromptFrozen`; WKWebView snapshots taken while
the page is changing come back wrong, and CGWindowListCreateImage was not used because of the screen-recording
permission).
In v0.3.44: Pip's motion rewritten around one rule (Pip's centre is the truth; every move picks a
target, keeps Pip whole on screen and goes through `glide(to:)`; layouts wait while a glide or drag runs;
`windowDidMove` ignores our own moves; the panel overrides `constrainFrameRect` so macOS no longer pushes it
down from the top; no Dock avoidance, the Dock is above Pip). Verified with `tools/pip-motion-test/run.sh`
(all steps pass on both screens). Run that test after any change to how Pip moves.
v0.3.44 (4 Nov 2026), not yet confirmed on the owner's Mac: the motion rewrite above, and the question bar has no Settings button; the history button hides while text is
typed; the question grows up to ~45% of the screen, upward from Pip's bottom, or downward from Pip's top when
the room above runs out (`grow-down`, from the `top` the edge report now carries); + opens a menu (Add Text,
Add Attachment…); pictures and text files (into the text box, under their names) can be added from the menu
or dropped on the popup or on Pip (Pip opens with them).
v0.3.45: Pip's toolbar tucks 1.5 s after the mouse leaves (was 3 s).
On `dev` (not built): dictation. Mic button in the question bar; live text while speaking (turbo, greedy, every
~0.4 s, commits at pauses), then a final pass over the whole recording (beam search + silero VAD, ~1 s for 30 s).
Model ggml-large-v3-turbo-q5_0 (574 MB) + silero (0.9 MB) downloaded once into Application Support/Quick
Prompt/models (size + SHA-256 checked). Languages: Settings → Dictation lists the ones you speak (up to 8; default English + the Mac's language, else its
region's: an English Mac in Turkey gets Turkish); each recording picks among them after 3 s of speech (Whisper's
open detection picked Russian). Settings → Dictation: language, words to expect, model. Test without a
mic: QP_SPEECH_FILE=<16 kHz wav> (and QP_SPEECH_HELPER for the app side). Template text fields are full-width
textareas that grow. Pip's toolbar highlights under the pointer (the panel gets no mouse moves while Pip is alone).
Also on `dev`: templates have "Copy the first answer" (autoCopy; the first complete answer goes onto the
clipboard, the page toasts "Answer copied"); a template picked from Pip's list or right-click menu opens on its
fields and the question bar, closing any Settings / template list / history view left open.
Also on `dev`: Pip no longer shifts by a fraction of a point on open/close (the page keeps Pip's centre on a whole
point with a translate of its column; the native side rounds the panel itself; measured: identical in all states).
Closing the chat (collapse) resets the popup to a plain Claude conversation (no template, its AI or fields; it stays
in Recent Chats; kept while answering); New Chat does the same. Opening no longer flashes the bubbles' flat
background (scrolling to the end ourselves no longer counts as the user scrolling).
v0.3.46 (5 Nov 2026) has everything above marked "on `dev`": dictation, template autoCopy, Pip hover highlight,
no sub-point shift, reset on close, no open flash. Checked in the release: helper signed with audio-input,
notarized, NSMicrophoneUsageDescription present. Not yet confirmed on the owner's Mac.
On `dev` after v0.3.46: while dictating, the send button is a stop button (■) and Enter only stops (the final
text shows; Enter again sends) — sending during a recording used to leave the mic on and re-add the text. A
dictation shortcut (Settings → General, default ⌥⇧M; Carbon hot key id 2; `dictationHotkey`) opens the chat and
starts listening, again stops; with no popup it opens a new chat with `startDictation`. Recording goes on when the
chat tucks into Pip; Pip shows a mirrored spectrum of red rays (option A1, picked by the owner) drawn on #pipWave
from the mic loudness (dB scale −50…−10, with automatic gain in the page); #root.dictating pads 30 px so the rays
fit, the anchors keep Pip still. The mic button hides while recording.
v0.3.47 (5 Nov 2026), built, not yet confirmed on the owner's Mac: Pip's dictation rays start on the disc's rim (no gap). **Agents**
(the owner's design): Settings → Agents (list, Make default, editor: name, colour, AI, CLI/API, model, effort for the
Claude API, plugins/skills/MCP, instruction) and Settings → API keys replace the Claude / ChatGPT panes; the template
editor picks an agent; the question bar has an agent chip (a native menu; just its dot while typing); Pip's right-click
New Chat lists the agents when there are several. Older settings become agents "Claude" (default) and "ChatGPT";
older templates point at the matching agent or a new one ("Claude Haiku 5.5 · API"). Exports are version 2 (they carry
the template's agent). Checked with the real engine (DevHost) and Playwright; not yet on the owner's Mac.
On `dev` after v0.3.47: the agent picker is a small glass pill under the question bar (outside it, the bar keeps its
size). PDFs can be added (drop on Pip or the popup, paste, Add Attachment; up to ~18 MB; a card with the name as the
preview): Claude CLI (stream-json document block, verified with the real CLI) and API, and the OpenAI API (file part)
read them; the Codex CLI answers that it cannot. The dictation shortcut with only Pip on screen listens on Pip alone
(no chat, the panel does not take the keys; `__quickPromptDictate` returns "quiet"); clicking Pip opens the chat and
the recording goes on; pressing it again stops and opens the chat with the text. With no popup, it opens Pip listening.
v0.3.48 (6 Nov 2026), built and notarized, not yet confirmed on the owner's Mac; it also has everything "On `dev` after v0.3.47" above. **Read aloud** (step 1 of voice; plan in Hindsight "Quick Prompt: read-aloud and conversation mode"). A speaker
button on every answer, Settings → Read Aloud (read every answer as it comes, a voice per Dictation language with Test, speed);
system voices through speechSynthesis in the page, local voices only (works in WKWebView; WebView2 later), read sentence by sentence
while streaming, code blocks, tables and link addresses left out; stops on send and when dictation starts, goes on when tucked into Pip.
Turkish goes through **autolex** first (`src/QuickPrompt.Core/Voice/autolex.js` + `autolex-tr.json.gz`, served at `/s/{token}/autolex.js`
and `autolex-tr.json`; CSP allows `script-src 'self'`): English words respelled for the Turkish voice ("Service Layer" → "Sörvis Leyır",
API → "ey pi ay"); built by `tools/autolex` from CMUdict (BSD-2) + Zemberek (Apache-2.0), notices in `THIRD-PARTY-NOTICES.md` (copied into the
app) and About. Rebuild: `sh tools/autolex/fetch_data.sh && python3 -I tools/autolex/build_tables.py`; the exceptions list
`tools/autolex/exceptions.tr.json` (abbreviations, product names). Origin: Forganise branch `claude/tts-watch` (owner approved the readings by
ear). Checked with a fake speech engine in Playwright; not yet on the owner's Mac. Next: conversation mode (send on silence or on a manual
"done", the owner's choice; its own shortcut and a long press on the mic; barge-in needs echo cancellation).
v0.3.50 (6 Nov 2026), built and notarized (the arm64 engine carries libonnxruntime and libqp_normalizer, signed), not yet
confirmed on the owner's Mac: **local voices replace the system voices** (the owner: no system voices;
EMA for Turkish and the English model we discussed). `src/QuickPrompt.Voice` (ONNX Runtime 1.31.1, Apple silicon only: no
macOS x64 library) makes each sentence's WAV in the engine (`POST /s/{token}/speak {lang,text,voice,speed}`, `GET|POST voice`
for state and the one-time download); the page plays them with Web Audio, the next one made while one plays
(`mediaTypesRequiringUserActionForPlayback = []`). Turkish: autolex in the page, then normalizer-tr 0.8 (`native/normalizer`,
a Rust cdylib with a C ABI, built by `build-app.sh` with cargo), then a C# port of EMA Lightning 1.1.2's frontend, chunker and
engine over our ONNX export (`tools/voice/export_ema.py`; checked against PyTorch: audio within 2e-4; 7 s of speech in ~160 ms).
English: Kokoro-82M fp32 (owner's pick over int8; first sentence 0.4–0.75 s) with an eSpeak-free G2P (misaki lexicons + CMUdict, `tools/voice/build_en_lexicon.py`; inflections,
compounds, letters; numbers as words); voices Heart, Bella, Michael (US), Emma, George (UK). Models (~363 MB) download once into
`models/voice` (size + SHA-256, `VoiceModels.cs`): Kokoro from Hugging Face at a fixed revision, EMA from this repo's
`voice-models-1` release (pre-release, created 6 Nov 2026 with the owner's OK: the four files + EMA's LICENSE; verified: a fresh
download of all ~130 MB took ~12 s, checks passed, both languages spoke). Other languages are not read. Licences in THIRD-PARTY-NOTICES.md and About. Also: Pip lines up with the bar again (the agent
pill is out of the column's flow), scrolling fades the bubbles' own background over the returning glass. 
**Conversation mode** (same commit range): listen → send → read the answer aloud → listen again, in the same chat (messages and
history as usual). Entry: its own shortcut (Settings → General, default ⌥⇧K, Carbon hot key id 3, `conversationHotkey`;
with no popup it opens Pip listening, `startConversation`), holding the microphone, Pip's menu (Start/End Conversation).
A turn ends on a 1.5 s pause or only "when I say" (Settings → Read Aloud, `ConversationEnd` silence|manual): the shortcut, ↵,
or a click on Pip. Cutting in: the helper plays the answer itself (`play`/`hush`/`played`, base64 WAV) through the same
AVAudioEngine as the microphone with voice processing on (echo cancellation; other audio ducked least on macOS 14+), and while
it plays the silero VAD watches the mic: 8 frames (~0.25 s) above 0.6, not in the first 0.4 s, stop the answer and the turn
starts from 0.8 s before (`barge`); the shortcut or a click on Pip cut in by hand. Esc ends it. While Pip speaks its rays turn
blue-violet. Checked: helper compiles and links, page flow with a mocked helper and the real voices (Playwright); **not checked:
the echo cancellation and barge-in with a real microphone and speakers** (owner's Mac).
v0.3.52 (6 Nov 2026), built and notarized, not yet confirmed: v0.3.51 had no error but the conversation mic gave nothing (rays
still, no text; dictation fine). Likely cause: the converter was built for the mic format read before the connections, which voice
processing changes, so every buffer failed silently. Now the tap takes the buffers' own format (converter rebuilt when it changes),
the format is read after the connections and logged, and 1.5 s of digital silence from the voice-processing mic restarts it without.
The helper's stderr goes to `~/Library/Logs/Quick Prompt/speech.log` (look there first for dictation and conversation problems).
v0.3.53 (6 Nov 2026), built, not yet confirmed: speech.log from v0.3.52 showed the voice-processing mic as 9 channels at 48 kHz
(plain mic: 3 or 1); some sessions gave only zeros (fallback ran), others gave sound but no text. The converter's mix-down averaged
the voice away: now the 9 channels are summed into one. A turn ends when the helper's VAD hears 1.2 s of no speech after some
(`pause` event; fallback in the page: 2.5 s with no new words). Each recording logs length, peak, speech seconds and echo
cancellation on/off to speech.log.
v0.3.54 (6 Nov 2026), built, not yet confirmed: in v0.3.53 the mic worked but Pip cut its own answer off ("thinks I'm speaking"): summing the 9
voice-processing channels brought the raw mic, so Pip's own voice, back. Now channel 0 alone (the echo-cancelled one) is used
while it carries sound, else the sum; barge-in needs 0.5 s of clear speech (16 frames each > 0.5, average > 0.75, not in the
first 0.6 s); speech.log gets each channel's level ("Channels (dBFS): …, channel 0 while Pip spoke …") and "Barge-in after …".
v0.3.55 (7 Nov 2026), built, not yet confirmed: the listening rays reach 14 px past the disc (were ~37), 8 px while Pip speaks; #root.dictating pads 18 px.
**Talking star** (owner's pick in `tools/pip-voice-lab.html`): while Pip speaks in a conversation the star turns once, fills out
(inner corners 2.6 → 3.9, rounded edges), gets eyes (blinking) and a mouth; its lower arm drops like a jaw and its side arms lift
with the voice; the four pieces fly out round its sides and circle it. The mouth and the speaking rays follow the loudness of the
sentence being played (`voiceEnvelope`, 50 values a second from the WAV, timed from `play`). In `globe` (`setTalking`).
Also in v0.3.55: v0.3.54's speech.log showed all 9 voice-processing channels equal (so channel 0 alone is right) and Pip cutting
itself off with the English voice ("Barge-in after 1.3 s", channel 0 at −48 dBFS: echo left after cancellation, which the voice
detector took for speech; the owner speaks at −20…−35). Barge-in now also needs the last 0.5 s above −40 dBFS.
Also in v0.3.55: a conversation started with Pip alone shows no toast (it was cut off) and tucks Pip's toolbar; in a conversation
the toolbar's first button is a red stop that ends it (New Chat otherwise).
v0.3.56 (7 Nov 2026), built, not yet confirmed: the talking star shows for the whole conversation (from its start; the mouth moves only while Pip
speaks); rays show only while someone speaks (none when quiet). The first conversation after the helper started got a whole
turn at −80 dBFS (voice processing started "dead"; the digital-zero check missed it): a start with no peak above 0.003 in
1.5 s now restarts voice processing once, then falls back to the plain mic.
v0.3.57 (7 Nov 2026), built, not yet confirmed (with the next three notes): v0.3.56's log: the turn after an answer heard nothing (voice processing came back silent twice, then the
plain mic; the turn was lost). Each turn used to stop the engine and start a new one (to play, then to listen). Now a
conversation keeps one engine (microphone) running between turns; `release` (page: endConversation) or two idle minutes stop it.
#root.conversing keeps the 18 px room for the rays for the whole conversation, so Pip no longer moves at each listen/speak switch.
**Conversation listens continuously** (owner: "kökten çöz, sürekli dinlemeli"; in v0.3.57): the helper runs the turns itself. The
page sends `start` once (conversation, `autoEnd` = "a pause sends it"); the helper keeps one engine, watches with the voice
detector between turns (onset: 8 frames avg > 0.6 and > −42 dBFS, not within 0.5 s after an answer; over an answer the stricter
barge rule), sends `listening`, partials, then on a 1.2 s pause ends the turn itself (`final` with `conversation: true`) and watches
again. The page sends each such final (over an answer still being written: cancel, then send), plays answers, no longer calls
listenTurn after answers. Manual end: ↵ / Pip click → `stop`. `release` or ten idle minutes (`ended`) stop it; the app's 10-minute
helper quit ignores conversation finals. Checked: the real helper on a WAV with two utterances (both sent by themselves, TR + EN);
the page flow with a mocked helper (Playwright). Not yet with a real microphone.
Also in v0.3.57: Pip moved and scroll bars showed while the rays' room was on (owner's video): the page's ResizeObserver watched
#root's content box, which padding does not change, so the panel never grew. Now it observes the border box; `html.native`
never scrolls (overflow clip); the room is one class (`roomy`, `updateRoom()`), switched under a picture of Pip (freezePip) with
Pip alone and thawed after the panel's resize. Measured with the real PopupController (a conversation start/speak/end test,
like tools/pip-motion-test): Pip drawn 0.0 pt from its place, the page never larger than the panel; pip-motion-test all pass.
On `dev` after v0.3.57: **web search for API agents** (owner: an option, off unless switched on): Agent `WebSearch` (Settings →
Agents, Claude + API: "Web search" Off/On; exported with templates). The Anthropic provider adds the server-side tool
(`web_search_20260209`, `web_search_20250305` for Haiku and older models; MaxUses 5, which keeps the server loop short of pause_turn),
streams "Searching the web: <query>" / "Reading what it found…" as the status (the live label), and lists the sources as links
under the answer. Pip wears a turning globe badge while it searches (`.mascot.searching`). The Claude CLI already had it
("Web search and fetch" in the plugins/skills choice). OpenAI API: not yet. Not tried against the live API (compiled against SDK 13.3.0).
v0.3.58 (7 Nov 2026), built, not yet confirmed: has the web search above. Scrolling no longer changes the bubbles' colour: while
the user scrolls, one still pane of glass sits behind the visible conversation (the per-bubble glass moves a frame late) and the
bubbles keep their own tint; each bubble's glass returns when it stops. Side effect: the gaps between bubbles look frosted while scrolling.
v0.3.59 (7 Nov 2026), built, not yet confirmed: the Settings footer's Save also saves the agent open in the editor. **Settings in a window of their own**
(owner): from Pip (gear, menu, Edit Agents…) the page posts `opensettings`; the engine opens `?window=settings[&pane=…]`, which
the app opens as a standalone PopupController (no Pip, no glass, opaque page with a shadow, centred, beside Pip's popup; a second
one replaces the first). Its Chat width / Pip size / Glass sliders post `preview` (not saved): every session's state carries the
tried values (`lookPreview`), Pip's popup opens its chat while the width is tried and tucks it back after; Cancel/✕/closing the
window clears it, Save makes it permanent. Chat size is now **Chat width** (380 + v·7 px, up to 1080) and the chat's outer edge
can be dragged (saved at once); the conversation's height is no longer fixed: it grows as tall as the screen allows on its side
of Pip (`updateThreadMax`). A chat opened but left empty tucks back into Pip when another app or window takes the keyboard
(`windowDidResignKey` → `__quickPromptBlur`).
v0.3.60 (7 Nov 2026), built, not yet confirmed: in v0.3.59 the Settings window never appeared (kept hidden until centred, but a
hidden web view runs no requestAnimationFrame, so the page never sent its layout); now it is on screen with alpha 0 until centred.
v0.3.61 (7 Nov 2026), built, not yet confirmed: slimmer question bar (46 px, 32 px buttons, 14 px text; Pip's middle on the bar's
middle via a negative margin on .mini); the agent pill hides with the bar (none under the Settings window); Edit Templates (Pip's menu,
its template list, the menu bar's Prompt Templates) opens the Settings window on Prompt templates, and Run there opens the template
at Pip (`runtemplate` → RunSavedPresetAsync).
v0.3.62 (7 Nov 2026), built, not yet confirmed: web search actually searches (the default system prompt's "do not use tools,
answer only from the text" made Haiku refuse; `WithWebSearch` adds a note allowing it). A conversation answer was not read aloud in
v0.3.61 (cause unknown): agent.log now gets "Page: Read aloud: …" lines (answer read, no audio, voices not ready) and speech.log
"Playing an answer…" / "Play: skipped…". Look there first when reading aloud fails.
v0.3.63 (7 Nov 2026), built, not yet confirmed (with the notes up to the CI note): **Settings save as you change them** (owner, like Claude's settings): no Save/Cancel in the footer
("Changes are saved as you make them." / "Saved"); `persistSettings` after a debounced change (only once loaded; unchanged bodies
are not sent); API keys on blur/Enter, Remove at once. The agent and template editors save themselves too (debounced) with
"‹ Agents" / "‹ Templates" back links and a red Delete; new ones get an id made by the page (agents `g`+10 hex, accepted by SaveAgent;
templates `a`+12 hex; editing a built-in makes your copy at the first change); a template needs a title and a prompt before it saves.
Also in v0.3.63: no answer bubble beside Pip in a conversation; read aloud skips the Sources list and decides the language
from what the answer has said (waits while unclear; ç/ö/ü count as Turkish; the question decides at the end); editors have a
breadcrumb ("← Agents / name"). **Quick open / close no longer throws Pip to the panel's far edge** (owner's video: ~0.15 s jumps of ~390 pt).
Causes, each measured with `tools/pip-rapid-test/run.sh` (real PopupController, 24 random open/close; about half the runs
failed): Esc's slide-out timer collapsed the chat even after it was opened again; a slide-in that waited for the panel's size
started after the chat was tucked again (thawed Pip's picture, held the tucked layout back); freezes had no ids (an earlier
change's thaw or 0.8 s fallback removed a later picture; a late snapshot showed a stale Pip); a freeze asked for while one was
pending was dropped. Now freezes/thaws carry ids, changes under one picture share it and it goes with the last thaw, the
tuck timer is cancelled by opening, and collapse ends a pending slide. Run pip-rapid-test (and pip-motion-test) after changes
to open/close or freeze.
v0.3.64 (7 Nov 2026), built, not yet confirmed: **the conversation microphone comes on at once** (owner: the mic icon showed ~2 s late). Measured with
the helper: `setVoiceProcessingEnabled` alone takes ~2.2 s (plain mic 0.1 s; starting a stopped voice-processing engine again
0.25 s), and setting it up while another engine records stops that one's buffers. Now a conversation starts on the plain mic,
voice processing is set up after the first turn (while the answer is written) and swapped in (not while an answer plays); at
the end the engine is stopped and kept for the next conversation; dictation drops it. The dead-start check is 0.8 s. The model
loads in the background (the mic no longer waits for it). speech.log lines carry times, "Audio engine … running X s after the
start", "Microphone gave sound X s after the start", "Voice processing takes over …". A conversation answer (web search) was
not read: agent.log showed the page no longer in a conversation; it now logs "Conversation started" / "Conversation ended:
<why>". Helper compiled locally (no mic permission for the shell, so timings only).
v0.3.65 (8 Nov 2026), built, not yet confirmed: **template ring** (owner's idea; look C "Constellation" at ×1.2 picked in
`tools/pip-ring-lab.html`). A shortcut (Settings → General, default ⌥⇧Z, Carbon hot key id 4, `ringHotkey`) makes Pip cast the
enabled templates out round itself as stars with pills, towards the side of the screen with more room (where Pip is, else where it
was last left, else the bottom middle, with a drawn Pip); ↑↓ / Tab / 1–9 choose, Return runs the template on the selection and
pastes the answer in its place (`POST /api/run` with `flow=replace` → FlowModes.Replace; fields or no selection open the popup),
Esc / the shortcut again / a click elsewhere pull it back. `native/macos/App/Ring.swift`: a borderless non-activating panel over the
whole screen at popUpMenu level (takes the keys without activating Quick Prompt, so the other app keeps its selection; hidden before
⌘C), the page is a string in that file (`ringPageHTML`, posts ready/run/closed to the `ring` handler). Checked: the page with a mocked
app in Chromium (messages and timing), Swift type-check, tests. Not checked: the real window and keys on the owner's Mac.
v0.3.66 (8 Nov 2026), built, not yet confirmed: **template groups** (owner's design, 8 Nov 2026). A template may have a Group (a name; optional,
`SavedPreset.Group`, in /presets, /actions, saveaction, export/import). The ring's first level keeps the template order with each
group standing where its first template is (groups and plain templates mixed); a group is a star in Pip's next colour with its
templates circling it, a count and a chevron. Its number opens it in place (owner: little motion, not slow): the others fade where
they are, its pill stays as the header, its templates unfold from the pill's end along threads in the group's colour (~0.3 s);
back with Esc / ⌫ / the arrow away from the list or a click on the header. Codes are fixed (group, then template: ⇧⌥Z 3 2); a
quick second key runs at once. The page is edited in `tools/pip-ring-groups-lab.html` and copied into Ring.swift with
`python3 -I tools/ring-page-sync.py`. Settings: a Group field (suggests existing groups) and the list under group headers.
Checked: tests, Swift type-check, the app's page in Chromium (messages), Settings with the real engine (DevHost).
v0.3.67 (9 Nov 2026), built, not yet confirmed: the ring no longer beeps on its keys (the page left the number keys
unhandled, WKWebView handed them back to macOS; now every non-⌘ key is preventDefault-ed and RingPanel's `noResponder(for:)`
stays quiet for keyDown).
Next idea (owner): Action Templates (command / key-press steps, e.g. Snagit captures).
CI note: the macOS job can sit queued and get cancelled ("not acquired by Runner … capacity"); re-run the whole run (`gh run rerun <id>`; `--failed` is refused after a cancel). On 4 Nov 2026 it took three attempts.

## Open items / ideas

- Make "Show latest only" sticky at the top of the conversation (offered, not done).
- Logi plugin: hand requests to the Mac app when it is running (one engine, one history).
- Windows app: tray app with the same engine (WebView2 + Mica). Pip there: Alt-click = middle click (the page already treats altKey as ⌥); the Shortcuts page then needs Windows key names.
- Verify the OpenAI default model name (`gpt-5-mini`) and the Codex CLI with a real login.
- Logi Marketplace: localization, review, screenshots, support page.
- Mac App Store is not planned (the app needs Accessibility and runs CLIs).
