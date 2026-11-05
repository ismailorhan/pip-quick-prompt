namespace QuickPrompt.Core
{
    using System;

    /// <summary>
    /// The popup page (no external resources). In the macOS panel (QuickPromptPopup) the page is transparent and
    /// every ".glass" shape is drawn natively (Liquid Glass on macOS 26+, vibrancy before): the page reports
    /// the shapes' rectangles and its height. Elsewhere the glass is imitated with CSS.
    /// </summary>
    public static class ChatPage
    {
        public const String Html = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Quick Prompt</title>
<style>
:root {
  --text: #1d1d1f; --muted: rgba(0,0,0,.5); --faint: rgba(0,0,0,.06); --chip: rgba(255,255,255,.45);
  --glass: rgba(255,255,255,.5); --edge: rgba(255,255,255,.65); --hi: rgba(255,255,255,.7);
  --error: #c62828; --accent: #d97757; --accent-text: #fff; --link: #1d5fd6;
  --page: linear-gradient(140deg, #cdbfcc 0%, #b3b8cc 45%, #d8c3ad 100%);
  --tint-rgb: 246, 246, 249;   /* Glass setting: a tint over the native glass, denser = easier to read */
}
:root[data-provider="codex"] { --accent: #10a37f; }
@media (prefers-color-scheme: dark) {
  :root { --text: #f5f5f7; --muted: rgba(255,255,255,.55); --faint: rgba(255,255,255,.08); --chip: rgba(255,255,255,.12);
          --glass: rgba(44,44,48,.55); --edge: rgba(255,255,255,.16); --hi: rgba(255,255,255,.14);
          --error: #ff8a80; --link: #7ab0ff; --page: linear-gradient(140deg, #2b2733 0%, #1d2230 50%, #2d2622 100%);
          --tint-rgb: 30, 30, 36; }
}
* { box-sizing: border-box; }
html, body { margin: 0; color: var(--text); font: 14px/1.5 -apple-system, BlinkMacSystemFont, "SF Pro Text", "Segoe UI", Roboto, sans-serif; -webkit-font-smoothing: antialiased; }
body { background: var(--page); min-height: 100vh; }
html.native, html.native body { background: transparent; min-height: 0; }

#root { display: flex; flex-direction: row; align-items: flex-start; gap: 10px; padding: 10px; }
/* The icon sits beside the conversation, on the side away from the nearer screen edge (set natively). */
#root.side-right { flex-direction: row-reverse; }
#main { display: flex; flex-direction: column; gap: 10px; flex: 1; min-width: 0; }
#root.is-mini #main { display: none; }
#root:not(.is-mini) .minibar { display: none; }
#root:not(.is-mini) .plist, #root:not(.is-mini) .bubble { display: none; }
/* Beside the conversation the icon sits at the bottom, next to the input; the conversation grows upward. */
#root:not(.is-mini) { align-items: flex-end; }
/* No room above Pip for the conversation: it lines up with Pip's top and grows downward. */
#root:not(.is-mini).grow-down { align-items: flex-start; }
/* The conversation slides out of the icon and back into it. */
#root { --reveal-dx: -26px; } #root.side-right { --reveal-dx: 26px; }
#main.reveal-pending { opacity: 0; }
#main.reveal-in { animation: qp-reveal-in .3s cubic-bezier(.2, .8, .2, 1) both; }
#main.reveal-out { animation: qp-reveal-out .22s ease-in both; }
@keyframes qp-reveal-in { from { opacity: 0; transform: translateX(var(--reveal-dx)); } to { opacity: 1; transform: none; } }
@keyframes qp-reveal-out { to { opacity: 0; transform: translateX(var(--reveal-dx)); } }
@media (prefers-reduced-motion: reduce) { #main.reveal-in, #main.reveal-out { animation-duration: .01s; } }

/* Glass shapes: CSS imitation; the native panel draws real glass behind transparent shapes. */
.glass {
  background: var(--glass);
  -webkit-backdrop-filter: blur(28px) saturate(1.7); backdrop-filter: blur(28px) saturate(1.7);
  border: 1px solid var(--edge);
  box-shadow: 0 10px 34px rgba(0,0,0,.16), inset 0 1px 0 var(--hi);
}
html.native .glass { background: rgba(var(--tint-rgb), var(--glass-tint, .3)); -webkit-backdrop-filter: none; backdrop-filter: none; box-shadow: none; border-color: transparent; }
html.native.vibrancy .glass { border-color: var(--edge); }

/* Conversation: a header pill and one glass bubble per message, newest at the bottom */
.card { display: flex; flex-direction: column; gap: 8px; }
.head { border-radius: 999px; padding: 4px 10px 4px 14px; }
.card > .notice { border-radius: 16px; padding: 8px 14px; margin: 0; }
.msg { border-radius: 20px; padding: 9px 14px 11px; max-width: 100%; }
.msg.user { align-self: flex-end; max-width: 82%; padding: 7px 13px 8px; font-size: 13px; }
.msg.user .q { color: var(--text); margin: 0; -webkit-line-clamp: 4; font-size: 13px; }
.msg.user .ctxinfo { color: var(--muted); font-size: 11.5px; }
.msg.bot { align-self: stretch; }
.mh { display: flex; align-items: center; gap: 6px; font-size: 12px; font-weight: 600; color: var(--muted); margin-bottom: 3px; }
.mh .ok { width: 15px; height: 15px; border-radius: 50%; background: #2fbf71; color: #fff; display: grid; place-items: center; font-size: 9.5px; flex: none; }
.mh .ok.err { background: #e5533d; }
.mh .spin { margin-right: 0; }
.mh .spk { margin-left: auto; width: 24px; height: 20px; padding: 0; border: 0; border-radius: 6px; display: grid; place-items: center; background: transparent; color: var(--muted); opacity: .6; cursor: pointer; }
.mh .spk:hover { opacity: 1; background: var(--faint); }
.mh .spk.on { opacity: 1; color: var(--accent); }
.mh .spk.on .w1 { animation: qp-wave 1.1s ease-in-out infinite; }
.mh .spk.on .w2 { animation: qp-wave 1.1s ease-in-out .25s infinite; }
@keyframes qp-wave { 0%, 100% { opacity: .25; } 50% { opacity: 1; } }
.vlist { display: grid; grid-template-columns: max-content 1fr auto; gap: 6px 10px; align-items: center; margin-top: 8px; font-size: 12.5px; }
.vlist select { font: inherit; font-size: 12.5px; color: var(--text); background: var(--faint); border: 0; border-radius: 8px; padding: 4px 8px; min-width: 0; }
.vlist .none { color: var(--muted); }
.vlist button { font-size: 12px; padding: 3px 10px; }
.mh .live { background: linear-gradient(90deg, var(--muted) 0%, var(--text) 50%, var(--muted) 100%); background-size: 200% 100%; -webkit-background-clip: text; background-clip: text; color: transparent; animation: qp-shimmer 1.6s linear infinite; }
@keyframes qp-shimmer { to { background-position: -200% 0; } }
.msg.bot.thinking { align-self: flex-start; border-radius: 999px; padding: 6px 14px; }
.msg.bot.thinking .mh { margin: 0; }
.msg.bot.thinking .a { display: none; }
.msg .actions { margin-top: 8px; }
.meta { display: flex; align-items: center; gap: 7px; font-size: 11.5px; color: var(--muted); min-height: 22px; }
.dot { width: 7px; height: 7px; border-radius: 50%; background: var(--accent); flex: none; }
.meta .title { font-weight: 600; color: var(--text); opacity: .8; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.meta .status { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.spin { width: 9px; height: 9px; border: 2px solid var(--faint); border-top-color: var(--accent); border-radius: 50%; display: inline-block; vertical-align: -1px; margin-right: 5px; animation: spin .8s linear infinite; }
@keyframes spin { to { transform: rotate(360deg); } }
.notice { font-size: 12.5px; color: var(--accent); margin: 2px 0 4px; }

/* The conversation never scrolls as a whole (each bubble has native glass behind it, which cannot follow
   a scroll smoothly): the newest bubbles that fit are shown, older ones are a click away, and an answer
   too long for the popup scrolls inside its own bubble. */
.thread { overflow-y: auto; max-height: var(--thread-max, 380px); scrollbar-width: none; display: flex; flex-direction: column; gap: 8px; }
.thread::-webkit-scrollbar { display: none; }
/* Stacked view: the earlier messages as the edges of cards behind the latest one */
.stackhead { display: flex; flex-direction: column; align-items: center; gap: 0; cursor: pointer; margin-bottom: -9px; }
.stackhead.open { margin-bottom: 0; }
.stacklabel { font-size: 11.5px; color: var(--muted); padding: 2px 12px 6px; border-radius: 999px; }
.stackhead:hover .stacklabel, .stackhead:focus-visible .stacklabel { color: var(--text); }
/* Each card behind shows a strip of its top edge above the next one. */
.stackcard { height: 22px; border-radius: 14px; margin-bottom: -13px; }
.stackcard.s2 { width: 88%; opacity: .5; }
.stackcard.s1 { width: 94%; opacity: .75; }
.msg.bot.top { position: relative; }
.mq { font-size: 12.5px; font-weight: 600; color: var(--text); margin-bottom: 2px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
html:not(.native) .stackcard { background: var(--glass); }
/* While the conversation scrolls, the bubbles draw their own background (the native glass cannot
   follow a scroll smoothly); the glass comes back as soon as it stops. */
html.native .thread.scrolling .msg { background: rgba(var(--tint-rgb), max(.62, var(--glass-tint, .3))); border-color: var(--edge); transition: none; }
/* When it stops, that background fades out over the glass coming back (removing it at once left a frame with neither). */
html.native .thread .msg { transition: background-color .25s ease, border-color .25s ease; }
.a.scroll { overflow-y: auto; scrollbar-width: thin; padding-right: 4px; }
.pager { align-self: center; font-size: 11.5px; padding: 3px 12px; border-radius: 999px; color: var(--muted); }
.thread:empty { display: none; }
.q { color: var(--muted); font-size: 12px; margin: 10px 0 3px; display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden; }
.q:first-child { margin-top: 2px; }
.tok { background: var(--accent); color: var(--accent-text); border-radius: 5px; padding: 0 5px; font-size: 11px; }
.a { overflow-wrap: anywhere; }
.a.error { color: var(--error); white-space: pre-wrap; }
.a p { margin: 0 0 .55em; } .a p:last-child { margin-bottom: 0; }
.a h1, .a h2, .a h3, .a h4 { font-size: 1em; margin: .7em 0 .3em; }
.a ul, .a ol { margin: 0 0 .55em; padding-left: 1.3em; } .a li { margin: .1em 0; }
.a code { background: var(--faint); border-radius: 5px; padding: .05em .3em; font: 12.5px/1.45 ui-monospace, SFMono-Regular, Menlo, Consolas, monospace; }
.a pre { position: relative; background: var(--faint); border-radius: 12px; padding: 9px 11px; overflow-x: auto; margin: 0 0 .55em; }
.a pre code { background: none; padding: 0; white-space: pre; }
.a pre button { position: absolute; top: 5px; right: 5px; font-size: 10.5px; padding: 1px 7px; }
.a blockquote { margin: 0 0 .55em; padding-left: 9px; border-left: 3px solid var(--faint); color: var(--muted); }
.a table { border-collapse: collapse; margin: 0 0 .55em; display: block; overflow-x: auto; font-size: 12.5px; }
.a th, .a td { border: 1px solid var(--faint); padding: 3px 7px; text-align: left; vertical-align: top; }
.a hr { border: 0; border-top: 1px solid var(--faint); margin: .6em 0; }
.a a { color: var(--link); }
.cursor { display: inline-block; width: 6px; height: 1em; background: var(--accent); vertical-align: -2px; margin-left: 2px; border-radius: 2px; animation: blink 1s steps(2) infinite; }
@keyframes blink { 50% { opacity: 0; } }

button { font: inherit; color: var(--text); background: var(--chip); border: 0; border-radius: 999px; padding: 4px 12px; font-size: 12px; cursor: pointer; }
button:hover { filter: brightness(1.06); }
button:focus-visible, textarea:focus-visible { outline: 2px solid var(--accent); outline-offset: 1px; }
.actions { display: flex; gap: 6px; margin-top: 10px; }
kbd { font: 11px -apple-system, sans-serif; color: var(--muted); margin-left: 5px; }
button.x { background: transparent; color: var(--muted); padding: 0 4px; font-size: 13px; line-height: 1; display: inline-grid; place-items: center; }
button.x:hover { color: var(--text); }

/* Dictation */
.round.mic.on { background: #e5484d; color: #fff; box-shadow: 0 0 0 calc(var(--lvl, 0) * 9px) rgba(229, 72, 77, .28); transition: box-shadow .08s linear; }
.round.mic.on:hover { background: #d93d42; color: #fff; }
.send.dictstop { background: #e5484d; color: #fff; opacity: 1; }
/* Dictating: a mirrored spectrum of red rays around Pip, moving with the voice (drawn on #pipWave). The panel
   gets room for it; the anchors keep Pip itself where it is. */
#root.dictating { padding: 30px; }
#pipWave { position: fixed; pointer-events: none; z-index: 5; }
.round.mic.wait { animation: qp-mic-wait 1s ease-in-out infinite; }
@keyframes qp-mic-wait { 50% { opacity: .35; } }
.bar textarea.dictating { color: var(--muted); }
.speechbox { border-radius: 18px; padding: 10px 14px; font-size: 13px; }
.speechbox .sbt { line-height: 1.4; }
.speechbox .sbbar { height: 5px; border-radius: 3px; background: var(--faint); margin-top: 8px; overflow: hidden; }
.speechbox .sbbar i { display: block; height: 100%; width: 0; background: var(--accent); transition: width .25s; }
.speechbox .sba { display: flex; gap: 8px; margin-top: 8px; }
.speechbox .sba:empty { display: none; }
.speechbox .sba button { font-size: 12.5px; padding: 5px 12px; border-radius: 999px; }
.speechbox .sba button.primary { background: var(--accent); color: var(--accent-text); }

.langs { display: flex; flex-wrap: wrap; gap: 6px; margin-top: 8px; align-items: center; }
.langs .lang { display: inline-flex; align-items: center; gap: 4px; font-size: 12.5px; padding: 4px 6px 4px 11px; border-radius: 999px; background: var(--chip); }
.langs .lang button { background: transparent; color: var(--muted); width: 20px; height: 20px; padding: 0; border-radius: 50%; font-size: 12px; line-height: 1; }
.langs .lang button:hover { color: var(--text); background: var(--faint); }
.langs select { font: inherit; font-size: 12.5px; color: var(--text); background: var(--faint); border: 0; border-radius: 999px; padding: 4px 10px; }
.spane #speechHints { width: 100%; margin-top: 8px; border: 1px solid var(--faint); border-radius: 10px; padding: 8px 10px; font-size: 13px; min-height: 52px; max-height: 120px; background: var(--chip); }

/* Optional pasted text */
.ctx { border-radius: 18px; padding: 8px 10px; }
.ctx textarea { min-height: 60px; max-height: 150px; font-size: 13px; resize: vertical; }

/* Input capsule */
.bar { display: flex; align-items: flex-end; gap: 8px; border-radius: 28px; padding: 7px; min-height: 56px; }
.round { flex: none; width: 40px; height: 40px; border-radius: 50%; padding: 0; display: grid; place-items: center; font-size: 22px; font-weight: 300; line-height: 1; }
.send { flex: none; width: 40px; height: 40px; border-radius: 50%; padding: 0; display: grid; place-items: center; background: var(--accent); color: var(--accent-text); transition: opacity .15s; }
.send svg { width: 18px; height: 18px; }
.round.gear { background: transparent; color: var(--muted); font-size: 17px; }
.round.gear:hover { color: var(--text); background: var(--faint); }
.send.idle { opacity: .45; }
textarea { flex: 1; width: 100%; resize: none; font: inherit; font-size: 15px; color: var(--text); background: transparent; border: 0; outline: none; padding: 9px 2px; max-height: 140px; min-height: 40px; }
.bar textarea { max-height: var(--prompt-max, 140px); }
.bar textarea::placeholder { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
textarea::placeholder { color: var(--muted); }
.bar textarea:focus-visible, .ctx textarea:focus-visible { outline: none; }
.round[hidden] + textarea { padding-left: 12px; }

/* Settings view (long: scrolls inside a screen-high limit, set from the screen size at start) */
/* Settings: a sidebar of sections (like a desktop app's settings) and the chosen section on the right. */
.settings { border-radius: 22px; padding: 0; display: flex; flex-direction: column; overflow: hidden; }
.shead { display: flex; align-items: center; gap: 8px; padding: 12px 14px 10px 18px; border-bottom: 1px solid var(--faint); }
.shead .title { font-weight: 600; font-size: 14px; }
.shead .status { flex: 1; font-size: 11.5px; color: var(--muted); }
.sbody { display: grid; grid-template-columns: 186px 1fr; height: min(var(--settings-h, 500px), calc(var(--view-max, 760px) - 120px)); min-height: 320px; }
/* Resize grip: in the top corner away from Pip (the panel grows from Pip's side). */
.settings { position: relative; }
.sgrip { position: absolute; top: 0; right: 0; width: 18px; height: 18px; cursor: nesw-resize; z-index: 2; }
.sgrip::before { content: ""; position: absolute; top: 5px; right: 5px; width: 7px; height: 7px; border-top: 2px solid var(--muted); border-right: 2px solid var(--muted); border-radius: 0 3px 0 0; opacity: .55; }
#root.side-right .sgrip { right: auto; left: 0; cursor: nwse-resize; }
#root.side-right .sgrip::before { right: auto; left: 5px; border-right: 0; border-left: 2px solid var(--muted); border-radius: 3px 0 0 0; }
.snav { padding: 10px 8px; border-right: 1px solid var(--faint); overflow-y: auto; display: flex; flex-direction: column; gap: 1px; }
.snav-g { font-size: 11px; color: var(--muted); padding: 10px 10px 4px; }
.snav-g:first-child { padding-top: 2px; }
.snav button { display: flex; align-items: center; gap: 9px; background: transparent; border-radius: 9px; padding: 6px 10px; font-size: 13px; text-align: left; color: var(--text); }
.snav button svg { color: var(--muted); flex: none; }
.snav button:hover { background: var(--faint); }
.snav button.on { background: var(--chip); font-weight: 600; }
.snav button.on svg { color: var(--text); }
.pdot { width: 9px; height: 9px; border-radius: 50%; margin: 0 3px; flex: none; }
.spanes { overflow-y: auto; scrollbar-width: thin; padding: 6px 22px 18px; }
.spane h2 { font-size: 15px; margin: 10px 0 4px; }
.sline { display: flex; align-items: center; justify-content: space-between; gap: 18px; padding: 13px 0; border-bottom: 1px solid var(--faint); }
.sline > div:first-child { min-width: 0; }
.sl-t { font-size: 13.5px; font-weight: 500; }
.sl-d { font-size: 12px; color: var(--muted); margin-top: 3px; line-height: 1.45; }
.sl-d a { color: var(--link); }
.spane .api-only, .spane .cli-only { padding: 4px 0 8px; border-bottom: 1px solid var(--faint); }
.spane .cli-only .srow.sub { margin-top: 10px; }
.stemplates { margin-top: 8px; }
.stemplates .hlist { max-height: none; }
.stemplates #actBack { display: none; }
.hotkey { font: 600 13px -apple-system, BlinkMacSystemFont, sans-serif; letter-spacing: .04em; min-width: 120px; padding: 6px 14px; border-radius: 9px; background: var(--faint); }
.hotkey.recording { box-shadow: inset 0 0 0 1.5px var(--accent); color: var(--muted); }
.kbgroup { font-size: 12px; color: var(--muted); margin: 16px 0 6px; }
.kbgroup:first-of-type { margin-top: 4px; }
.kbrow { display: grid; grid-template-columns: 150px 1fr; gap: 12px; align-items: baseline; padding: 6px 0; font-size: 13px; border-top: 1px solid var(--faint); }
.kbrow .keys { color: var(--muted); white-space: nowrap; }
.kbrow kbd { font: 500 12px -apple-system, BlinkMacSystemFont, sans-serif; color: var(--text); padding: 2px 7px; border-radius: 6px; background: var(--faint); }
.sfoot { display: flex; align-items: center; gap: 8px; padding: 10px 14px 12px 18px; border-top: 1px solid var(--faint); }
.sfoot .status { font-size: 11.5px; color: var(--muted); }
.sfoot-hint { flex: 1; font-size: 11.5px; color: var(--muted); text-align: right; padding-right: 4px; }
#actionsView { max-height: var(--view-max, 760px); overflow-y: auto; scrollbar-width: thin; }
.sgroup { padding: 10px 0 4px; border-top: 1px solid var(--faint); margin-top: 8px; }
.meta + .sgroup { border-top: 0; margin-top: 2px; }
.srow { display: flex; align-items: center; gap: 10px; }
.sname { font-weight: 600; flex: 1; }
.seg { display: inline-flex; background: var(--faint); border-radius: 999px; padding: 2px; }
.seg button { background: transparent; padding: 3px 14px; font-size: 12px; color: var(--muted); }
.seg button.on { background: var(--chip); color: var(--text); font-weight: 600; box-shadow: 0 1px 2px rgba(0,0,0,.12); }
.slider { display: flex; align-items: center; gap: 8px; width: 220px; }
.slider .sz { color: var(--muted); font-weight: 600; line-height: 1; }
.slider .sz.small { font-size: 10px; } .slider .sz.big { font-size: 16px; }
input[type=range] { -webkit-appearance: none; appearance: none; flex: 1; height: 22px; background: transparent; margin: 0; cursor: pointer; }
input[type=range]::-webkit-slider-runnable-track { height: 4px; border-radius: 2px; background: linear-gradient(to right, var(--accent) 0 var(--fill, 30%), var(--faint) var(--fill, 30%) 100%); }
input[type=range]::-webkit-slider-thumb { -webkit-appearance: none; width: 18px; height: 18px; border-radius: 50%; margin-top: -7px; background: #fff; box-shadow: 0 1px 4px rgba(0,0,0,.35); }
input[type=range]::-moz-range-track { height: 4px; border-radius: 2px; background: var(--faint); }
input[type=range]::-moz-range-progress { height: 4px; border-radius: 2px; background: var(--accent); }
input[type=range]::-moz-range-thumb { width: 18px; height: 18px; border: 0; border-radius: 50%; background: #fff; box-shadow: 0 1px 4px rgba(0,0,0,.35); }
input[type=range]:focus-visible { outline: none; }
input[type=range]:focus-visible::-webkit-slider-thumb { box-shadow: 0 0 0 3px var(--accent); }
.shint { font-size: 11.5px; color: var(--muted); margin: 5px 0 2px; }
.sfield { display: block; margin-top: 8px; font-size: 11.5px; color: var(--muted); }
.sfield input, .sfield select { display: block; width: 100%; margin-top: 3px; font: inherit; font-size: 13px; color: var(--text); background: var(--faint); border: 0; border-radius: 9px; padding: 7px 10px; outline: none; }
.sfield input:focus-visible, .sfield select:focus-visible { box-shadow: inset 0 0 0 1.5px var(--accent); }
.skey { display: flex; gap: 6px; align-items: flex-end; }
.skey .sfield { flex: 1; }
.skey button { margin-bottom: 1px; }
button.primary { background: var(--accent); color: var(--accent-text); font-weight: 600; }

/* Pictures: waiting ones above the input, sent ones in the bubble; click for a large preview */
.attach { border-radius: 18px; padding: 8px; }
.alist { display: flex; flex-wrap: wrap; gap: 8px; }
.athumb { position: relative; width: 64px; height: 64px; border-radius: 10px; overflow: hidden; background: var(--faint); cursor: zoom-in; }
.athumb img { width: 100%; height: 100%; object-fit: cover; display: block; }
.athumb button { position: absolute; top: 3px; right: 3px; width: 18px; height: 18px; padding: 0; border-radius: 50%; font-size: 10px; line-height: 18px; background: rgba(0,0,0,.6); color: #fff; }
.mimgs { display: flex; flex-wrap: wrap; gap: 6px; justify-content: flex-end; margin-top: 4px; }
.mimgs img { width: 88px; height: 64px; object-fit: cover; border-radius: 9px; cursor: zoom-in; display: block; }
.msg.user .q + .mimgs, .mimgs:first-child { margin-top: 2px; }
.lightbox { position: relative; border-radius: 18px; padding: 10px; display: grid; place-items: center; }
.lightbox img { max-width: 100%; max-height: calc(var(--view-max, 760px) - 60px); border-radius: 10px; display: block; cursor: zoom-out; }
.lightbox .lclose { position: absolute; top: 14px; right: 14px; width: 26px; height: 26px; border-radius: 50%; background: rgba(0,0,0,.55); color: #fff; }
html.dropping #main { outline: 2px dashed var(--accent); outline-offset: -4px; border-radius: 22px; }
html.dropping #root.is-mini #miniOpen { outline: 2px dashed var(--accent); outline-offset: 3px; border-radius: 50%; }
/* The agent picker: a small line under the question, right-aligned (like the model line under a chat box). */
/* The agent picker: a small glass pill under the question bar, on the right (the bar keeps its size). */
/* Out of the column's flow, so Pip beside the column still lines up with the question bar; the root keeps room for it. */
#main { position: relative; }
.barfoot { position: absolute; left: 0; right: 0; top: 100%; margin-top: 5px; display: flex; justify-content: flex-end; padding-right: 14px; }
#root.has-foot:not(.is-mini) { padding-bottom: 39px; }
.barfoot[hidden] { display: none; }
.agentpick { display: inline-flex; align-items: center; gap: 6px; height: 24px; max-width: 70%; padding: 0 9px 0 10px; border-radius: 12px; color: var(--muted); font-size: 12px; font-weight: 500; }
.agentpick:hover, .agentpick.hov { color: var(--text); }
.agentpick .aname, .agentpick .amodel { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.agentpick .amodel { font-weight: 400; opacity: .75; }
.agentpick .amodel:empty { display: none; }
.agentpick .dot { width: 6px; height: 6px; }
.agentpick svg { flex: none; opacity: .7; }
.agentpick .dot, .aglist .dot, .afagent .dot { background: var(--agent, var(--accent)); }
.swatches { display: flex; gap: 7px; }
.swatches button { width: 20px; height: 20px; padding: 0; border-radius: 50%; background: var(--c); box-shadow: inset 0 0 0 1px rgba(0,0,0,.12); }
.swatches button.on { box-shadow: 0 0 0 2px rgb(var(--tint-rgb)), 0 0 0 4px var(--c); }
.agform .frow .sfield[hidden] { display: none; }
.agform select, .agform input[type=text] { width: 100%; }
.aglist .hitem { align-items: center; }
.aglist .dot { margin-top: 0; width: 10px; height: 10px; }
.aglist .defbadge { flex: none; font-size: 11px; font-weight: 600; color: var(--accent); padding: 2px 8px; border-radius: 999px; background: var(--faint); }
.aglist .mkdef { flex: none; font-size: 11.5px; padding: 3px 10px; }
.addmenu { position: absolute; z-index: 30; min-width: 170px; padding: 4px; border-radius: 12px; background: rgb(var(--tint-rgb)); border: 1px solid var(--edge); box-shadow: 0 8px 24px rgba(0,0,0,.3); }
.addmenu button { display: block; width: 100%; text-align: left; background: transparent; border-radius: 8px; padding: 7px 10px; font-size: 13px; color: var(--text); }
.addmenu button:hover { background: var(--faint); }

/* Fields of a prompt template */
.fields { border-radius: 20px; padding: 9px 14px 12px; }
.ftitle { font-size: 12px; font-weight: 600; color: var(--muted); margin-bottom: 2px; }
.flist { display: grid; grid-template-columns: repeat(auto-fill, minmax(180px, 1fr)); gap: 0 10px; }
.flist .sfield { margin-top: 6px; }
.flist .sfield.missing input, .flist .sfield.missing textarea { box-shadow: inset 0 0 0 1.5px var(--error); }
/* A free-text field takes the whole row and grows with what is typed or pasted (then scrolls). */
.flist .sfield.wide { grid-column: 1 / -1; }
.flist textarea { display: block; width: 100%; margin-top: 3px; font: inherit; font-size: 13px; line-height: 1.4; color: var(--text); background: var(--faint); border: 0; border-radius: 9px; padding: 7px 10px; outline: none; resize: none; min-height: 0; max-height: var(--field-max, 220px); overflow-y: auto; }
.flist textarea:focus-visible { box-shadow: inset 0 0 0 1.5px var(--accent); }
.aform { padding: 0 6px; }
.params { margin-top: 6px; padding: 8px 10px; border-radius: 10px; background: var(--faint); font-size: 12px; }
.params .ptitle2 { font-weight: 600; color: var(--muted); font-size: 11px; text-transform: uppercase; letter-spacing: .04em; margin-bottom: 4px; }
.params .prow { display: grid; grid-template-columns: 112px 1fr; gap: 8px; padding: 2px 0; color: var(--muted); }
.params code { font: 12px ui-monospace, SFMono-Regular, Menlo, monospace; color: var(--text); }
.params .pnote { margin-top: 5px; color: var(--muted); }
.aform textarea { display: block; width: 100%; margin-top: 3px; font: 13px/1.45 ui-monospace, SFMono-Regular, Menlo, monospace; color: var(--text); background: var(--faint); border: 0; border-radius: 9px; padding: 7px 10px; outline: none; resize: vertical; min-height: 96px; max-height: 260px; }
.aform textarea:focus-visible { box-shadow: inset 0 0 0 1.5px var(--accent); }
.frow { display: flex; gap: 10px; } .frow .sfield { flex: 1; }
.aitem .hq { font-weight: 600; }
.aitem .run { flex: none; align-self: center; font-size: 11.5px; padding: 3px 10px; }
.aitem.disabled .hbody, .aitem.disabled .dot { opacity: .45; }
.aitem .pick { flex: none; align-self: center; margin: 0 2px 0 0; accent-color: var(--accent); }
/* On / off switch */
.tgl { flex: none; align-self: center; width: 34px; height: 20px; padding: 0; border-radius: 999px; background: var(--faint); position: relative; margin-right: 6px; }
.tgl::after { content: ""; position: absolute; top: 2px; left: 2px; width: 16px; height: 16px; border-radius: 50%; background: #fff; box-shadow: 0 1px 2px rgba(0,0,0,.3); transition: transform .15s; }
.tgl.on { background: var(--accent); }
.tgl.on::after { transform: translateX(14px); }
.exportbar { display: flex; align-items: center; gap: 8px; margin-top: 10px; padding: 0 6px; }
.exportbar .ecount { font-size: 12px; color: var(--muted); }
.pedit .pname { color: var(--muted); font-size: 12px; }

.srow.sub { margin-top: 10px; } .srow.sub .sname { font-weight: 500; font-size: 13px; }
.setup { margin-top: 6px; padding: 6px 10px 8px; background: var(--faint); border-radius: 12px; }
.sgrp { padding: 4px 0 6px; }
.sgrp + .sgrp { border-top: 1px solid var(--faint); margin-top: 4px; padding-top: 8px; }
.ghead { font-weight: 600; }
.ghead .gcount { margin-left: auto; }
.gitems { padding-left: 24px; }
.setup .stitle { font-size: 11px; font-weight: 600; color: var(--muted); text-transform: uppercase; letter-spacing: .04em; margin: 6px 0 2px; }
.chk { display: flex; align-items: center; gap: 8px; font-size: 13px; padding: 3px 0; cursor: pointer; }
.chk input { accent-color: var(--accent); margin: 0; }
.chk em { font-style: normal; font-size: 11px; color: var(--muted); }
.chk.off { opacity: .55; cursor: default; }
.setup a { color: var(--link); }

/* History view */
.history { border-radius: 22px; padding: 10px 10px 12px; }

/* Dragging (native popup) */
html.native [data-drag] { cursor: grab; }
html.native [data-drag] button, html.native [data-drag] textarea, html.native [data-drag] input, html.native [data-drag] select { cursor: auto; }
.pinned { color: var(--accent) !important; }

/* Collapsed (pinned): a free-floating mark, a small glass toolbar, and a notification bubble. No window. */
#root.is-mini { display: inline-flex; }
.mini { zoom: var(--mini-zoom, 1); display: flex; flex-direction: column; align-items: center; gap: 6px; }
.mascot { width: 64px; height: 64px; filter: drop-shadow(0 6px 12px rgba(0,0,0,.4)) !important; padding: 0; background: transparent; border-radius: 50%; display: grid; place-items: center; cursor: grab; filter: drop-shadow(0 6px 10px rgba(0,0,0,.35)); }
.mascot:hover { background: transparent; transform: translateY(-1px); }
.mascot svg { overflow: visible; }
.mascot .rays, .mascot .star { transform-origin: 12px 12px; transform-box: view-box; }
.mascot.busy .rays { animation: qp-spin 3s linear infinite; }
.mascot.busy .star { animation: qp-beat 1.1s ease-in-out infinite; }
.mascot.wiggle svg { animation: qp-wiggle .6s ease-in-out; }
@keyframes qp-spin { to { transform: rotate(360deg); } }
@keyframes qp-beat { 0%, 100% { transform: scale(1); } 14% { transform: scale(1.18); } 28% { transform: scale(.96); } 42% { transform: scale(1.12); } 70% { transform: scale(1); } }
@keyframes qp-wiggle { 0%, 100% { transform: rotate(0); } 20% { transform: rotate(-9deg) scale(1.04); } 40% { transform: rotate(8deg) scale(1.06); } 60% { transform: rotate(-5deg) scale(1.03); } 80% { transform: rotate(3deg); } }
@media (prefers-reduced-motion: reduce) { .mascot .rays, .mascot.busy .star, .mascot.wiggle svg { animation: none !important; } }
.minibar { display: flex; align-items: center; border-radius: 999px; padding: 3px; }
.minibar button { background: transparent; color: var(--text); width: 38px; height: 32px; padding: 0; border-radius: 999px; display: grid; place-items: center; }
.minibar button svg { width: 19px; height: 19px; }
/* Copy / Paste under an answer: small icons */
.iconbtn { width: 28px; height: 26px; padding: 0; border-radius: 8px; display: inline-grid; place-items: center; background: transparent; color: var(--muted); }
.iconbtn:hover { background: var(--faint); color: var(--text); }
.msg .actions { gap: 2px; margin-top: 6px; }
.minibar button:hover, .minibar button.hov { background: rgba(128,128,128,.24); }
.minibar .sep { width: 1px; height: 16px; background: var(--faint); }
/* Tucked, the toolbar keeps its size (so the panel never resizes on hover); only its glass shrinks to a
   thin line (the page reports the line's rect) and its buttons hide. */
.minibar { overflow: hidden; }
.minibar.tucked { cursor: pointer; }
.minibar.tucked > * { visibility: hidden; }
/* Opening and tucking: the native glass grows from the line (and shrinks back), the buttons fade. */
@keyframes qp-bar-in { from { opacity: 0; transform: scale(.85); } to { opacity: 1; transform: none; } }
@keyframes qp-bar-out { to { opacity: 0; transform: scale(.85); } }
.minibar.opening > * { animation: qp-bar-in .22s cubic-bezier(.2, .8, .2, 1) .07s both; }
.minibar.closing > * { animation: qp-bar-out .14s ease-in both; }
@media (prefers-reduced-motion: reduce) { .minibar.opening > *, .minibar.closing > * { animation-duration: .01s; } }
/* Near the screen's bottom the toolbar opens above Pip; near a side it lines up with Pip on that side. */
.mini.tb-above { flex-direction: column-reverse; }
.mini.tb-start { align-items: flex-start; }
.mini.tb-end { align-items: flex-end; }
/* Near the bottom the templates list and the answer bubble beside Pip line up with its bottom (they open upward). */
#root.is-mini.pip-bottom { align-items: flex-end; }
#root.pip-bottom .plist { margin-top: 0; margin-bottom: 6px; }
#root.pip-bottom .bubble { margin-top: 0; margin-bottom: 10px; }
.plist { width: 250px; border-radius: 18px; padding: 8px 6px; margin-top: 6px; }
.plist .ptitle { font-size: 11.5px; font-weight: 600; color: var(--muted); padding: 2px 10px 6px; }
.pitem { display: flex; align-items: center; gap: 9px; width: 100%; background: transparent; border-radius: 12px; padding: 7px 10px; text-align: left; }
.pitem:hover, .pitem.hov, .pitem:focus-visible { background: var(--faint); }
.pitem .dot { flex: none; }
.pitem[data-provider="codex"] .dot { background: #10a37f; }
.pitem[data-provider="claude"] .dot { background: #d97757; }
.pitem .pname { flex: 1; min-width: 0; font-size: 13px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.pitem .pmeta { font-size: 11px; color: var(--muted); white-space: nowrap; }
.bubble { max-width: 300px; border-radius: 18px; padding: 9px 14px 10px; text-align: left; margin-top: 10px; cursor: pointer; }
.bubble { position: relative; }
.bclose { position: absolute; top: -6px; left: -6px; width: 20px; height: 20px; padding: 0; border-radius: 50%; font-size: 10px; line-height: 20px;
  background: rgba(40,40,46,.92); color: #fff; box-shadow: 0 1px 4px rgba(0,0,0,.35); opacity: 0; transition: opacity .12s; }
#root.side-right .bclose { left: auto; right: -6px; }
.bubble:hover .bclose, #root.hovering .bclose, .bclose:focus-visible { opacity: 1; }
.bubble .bt { font-weight: 600; font-size: 13px; display: flex; align-items: center; gap: 6px; }
.bubble .bt .ok { width: 16px; height: 16px; border-radius: 50%; background: #2fbf71; color: #fff; display: grid; place-items: center; font-size: 10px; }
.bubble .bt .ok.err { background: #e5533d; }
.bubble .bb { font-size: 12.5px; color: var(--muted); margin-top: 2px; display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden; }
.history .meta { padding: 0 6px; }
.hlist { max-height: 380px; overflow-y: auto; margin-top: 6px; scrollbar-width: thin; }
.hitem { display: flex; gap: 10px; align-items: flex-start; width: 100%; text-align: left; background: transparent; border-radius: 14px; padding: 8px 10px; }
.hitem:hover, .hitem:focus-visible { background: var(--faint); }
.hitem .dot { margin-top: 6px; }
.hitem[data-provider="codex"] .dot { background: #10a37f; }
.hitem[data-provider="claude"] .dot { background: #d97757; }
.hbody { flex: 1; min-width: 0; }
.hq { font-size: 13px; color: var(--text); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.ha { font-size: 12px; color: var(--muted); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; margin-top: 1px; }
.hmeta { font-size: 11px; color: var(--muted); white-space: nowrap; margin-top: 2px; }
.hempty { color: var(--muted); font-size: 13px; padding: 14px 8px; }
.history .actions { padding: 0 6px; }
.toast { position: fixed; bottom: 76px; left: 50%; transform: translateX(-50%); background: var(--text); color: var(--glass); padding: 4px 12px; border-radius: 999px; font-size: 12px; opacity: 0; transition: opacity .15s; pointer-events: none; }
.toast.show { opacity: .92; }
[hidden] { display: none !important; }
</style>
</head>
<body>
<div id="root">
  <div class="mini" id="mini" hidden>
    <button type="button" class="mascot" id="miniOpen" title="Pip: click to open or tuck away, drag to move, ⌥-click or middle click to send it to an edge or the next corner"><svg viewBox="0 0 24 24" width="56" height="56">
      <defs>
        <linearGradient id="qpMini" gradientUnits="userSpaceOnUse" x1="5" y1="5" x2="19" y2="19"><stop offset="0.08" stop-color="#F08A5D"/><stop offset="0.42" stop-color="#1FB890"/><stop offset="0.7" stop-color="#4C8DF6"/><stop offset="0.95" stop-color="#A56BF2"/></linearGradient>
        <radialGradient id="qpDisc" cx="0.4" cy="0.3" r="0.85"><stop offset="0" stop-color="#3d3852"/><stop offset="1" stop-color="#121018"/></radialGradient>
        <linearGradient id="qpGloss" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#fff" stop-opacity=".32"/><stop offset="1" stop-color="#fff" stop-opacity="0"/></linearGradient>
      </defs>
      <circle cx="12" cy="12" r="11.2" fill="url(#qpDisc)" stroke="rgba(255,255,255,.28)" stroke-width=".5"/>
      <g class="links" stroke="url(#qpMini)" stroke-linecap="round" fill="none"></g>
      <g class="rays" stroke="url(#qpMini)" stroke-width="1.15" stroke-linecap="round">
        <g class="piece" data-cx="16.8" data-cy="7.2"><path d="M16.1 7.9L17.5 6.5"/></g>
        <g class="piece" data-cx="16.8" data-cy="16.8"><path d="M16.1 16.1L17.5 17.5"/></g>
        <g class="piece" data-cx="7.2" data-cy="16.8"><path d="M7.9 16.1L6.5 17.5"/></g>
        <g class="piece" data-cx="7.2" data-cy="7.2"><path d="M7.9 7.9L6.5 6.5"/></g>
      </g>
      <g class="signals"></g>
      <g class="core"><g class="star"><path d="M12 4.6L13.85 10.15L19.4 12L13.85 13.85L12 19.4L10.15 13.85L4.6 12L10.15 10.15Z" fill="url(#qpMini)"/></g></g>
      <ellipse cx="12" cy="6.2" rx="7.2" ry="3.4" fill="url(#qpGloss)" pointer-events="none"/>
    </svg></button>
    <div class="minibar glass" id="minibar">
      <button type="button" id="miniNew" title="New chat"><svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M12 20h8M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4z"/></svg></button>
      <span class="sep"></span>
      <button type="button" id="miniRecent" title="Recent chats"><svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M3.5 12a8.5 8.5 0 1 0 2.5-6M3.5 4.5V8H7M12 7.5V12l3 2"/></svg></button>
      <span class="sep"></span>
      <button type="button" id="miniActions" title="Prompt templates"><svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M10 6h10M10 12h10M10 18h10M5 4.5l.6 1.4 1.4.6-1.4.6L5 8.5l-.6-1.4L3 6.5l1.4-.6zM5 15.5l.6 1.4 1.4.6-1.4.6-.6 1.4-.6-1.4L3 17.5l1.4-.6z"/></svg></button>
    </div>
  </div>
  <div id="main">
  <div class="card" id="card" hidden>
    <div class="meta head glass" data-drag hidden>
      <span class="dot"></span>
      <span id="who"></span>
      <span class="title" id="title"></span>
      <span class="status" id="status"></span>
      <button type="button" class="x" id="pinBtn" title="Pin: Esc and ✕ keep it as Pip" hidden><svg viewBox="0 0 24 24" width="13" height="13" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M9 4h6l-1 6 3 3H7l3-3-1-6z M12 16v5"/></svg></button>
      <button type="button" class="x" id="metaHistory" title="Recent conversations"><svg viewBox="0 0 24 24" width="13" height="13" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M3.5 12a8.5 8.5 0 1 0 2.5-6M3.5 4.5V8H7M12 7.5V12l3 2"/></svg></button>
      <button type="button" class="x" id="gear" title="Settings"><svg viewBox="0 0 24 24" width="13" height="13" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"><path d="M12 9a3 3 0 1 0 0 6 3 3 0 0 0 0-6z M19.4 13.5l1.6 1.2-2 3.4-1.9-.7a7.5 7.5 0 0 1-2 1.2l-.3 2h-3.9l-.3-2a7.5 7.5 0 0 1-2-1.2l-1.9.7-2-3.4 1.6-1.2a7.6 7.6 0 0 1 0-3l-1.6-1.2 2-3.4 1.9.7a7.5 7.5 0 0 1 2-1.2l.3-2h3.9l.3 2a7.5 7.5 0 0 1 2 1.2l1.9-.7 2 3.4-1.6 1.2a7.6 7.6 0 0 1 0 3z"/></svg></button>
      <button type="button" class="x" id="close" title="Close (Esc)">✕</button>
    </div>
    <div class="notice glass" id="notice" hidden></div>
    <div class="thread" id="thread"></div>
    <div class="actions" id="errActions" hidden><button type="button" id="errSettings">Open settings</button></div>
    <div class="actions" id="actions" hidden>
      <button type="button" class="iconbtn" id="copy" title="Copy the answer" aria-label="Copy"><svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><rect x="8" y="8" width="12" height="12" rx="2.5"/><path d="M16 8V6.5A2.5 2.5 0 0 0 13.5 4h-7A2.5 2.5 0 0 0 4 6.5v7A2.5 2.5 0 0 0 6.5 16H8"/></svg></button>
      <button type="button" class="iconbtn" id="paste" title="Paste the answer into the app you were using (⌘↵)" aria-label="Paste"><svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M9 4.5h6M9 4.5a1.5 1.5 0 0 0-1.5 1.5v.5h9V6A1.5 1.5 0 0 0 15 4.5M7.5 6H6.5A2.5 2.5 0 0 0 4 8.5v10A2.5 2.5 0 0 0 6.5 21h11a2.5 2.5 0 0 0 2.5-2.5v-10A2.5 2.5 0 0 0 17.5 6h-1M12 10v7M9 14l3 3 3-3"/></svg><kbd id="pasteKey" hidden>⌘↵</kbd></button>
    </div>
  </div>
  <div class="settings glass" id="settings" hidden>
    <div class="sgrip" id="setGrip" title="Drag to resize"></div>
    <div class="shead" data-drag>
      <span class="title">Settings</span>
      <span class="status" id="setStatus"></span>
      <button type="button" class="x" id="setClose" title="Close">✕</button>
    </div>
    <div class="sbody">
      <nav class="snav" aria-label="Settings sections">
        <div class="snav-g">Preferences</div>
        <button type="button" data-pane="general"><svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"><path d="M12 9a3 3 0 1 0 0 6 3 3 0 0 0 0-6z M19.4 13.5l1.6 1.2-2 3.4-1.9-.7a7.5 7.5 0 0 1-2 1.2l-.3 2h-3.9l-.3-2a7.5 7.5 0 0 1-2-1.2l-1.9.7-2-3.4 1.6-1.2a7.6 7.6 0 0 1 0-3l-1.6-1.2 2-3.4 1.9.7a7.5 7.5 0 0 1 2-1.2l.3-2h3.9l.3 2a7.5 7.5 0 0 1 2 1.2l1.9-.7 2 3.4-1.6 1.2a7.6 7.6 0 0 1 0 3z"/></svg>General</button>
        <button type="button" data-pane="appearance"><svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"><path d="M12 3a9 9 0 1 0 0 18c1 0 1.6-.8 1.6-1.6 0-.5-.2-.9-.5-1.2-.3-.3-.5-.7-.5-1.2 0-.9.7-1.6 1.6-1.6H16a5 5 0 0 0 5-5c0-4.1-4-7.4-9-7.4z M7.5 12h0 M10 7.5h0 M14.5 7.5h0"/></svg>Appearance</button>
        <button type="button" data-pane="shortcuts"><svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"><path d="M4 6.5h16a1 1 0 0 1 1 1v9a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1v-9a1 1 0 0 1 1-1z M7 10h0 M10.5 10h0 M14 10h0 M17.5 10h0 M8 14h8"/></svg>Shortcuts</button>
        <button type="button" data-pane="speech"><svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"><path d="M12 3a3 3 0 0 0-3 3v6a3 3 0 0 0 6 0V6a3 3 0 0 0-3-3z M5.5 11a6.5 6.5 0 0 0 13 0 M12 17.5V21 M8.5 21h7"/></svg>Dictation</button>
        <button type="button" data-pane="voice"><svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"><path d="M4 9.5v5h3.5L12 18.5v-13L7.5 9.5z M15.5 9a4 4 0 0 1 0 6 M18 6.5a7.5 7.5 0 0 1 0 11"/></svg>Read Aloud</button>
        <div class="snav-g">AI</div>
        <button type="button" data-pane="agents"><svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"><path d="M12 3.5l1.6 4.4 4.4 1.6-4.4 1.6L12 15.5l-1.6-4.4L6 9.5l4.4-1.6z M5 17.5c1.6-1.4 4-2.2 7-2.2s5.4.8 7 2.2 M8.5 21h7"/></svg>Agents</button>
        <button type="button" data-pane="keys"><svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"><path d="M14.5 9.5a4.5 4.5 0 1 1-9 0 4.5 4.5 0 0 1 9 0z M13.2 12.7L20 19.5 M17 16.5l2-2 M18.8 18.3l1.7-1.7"/></svg>API keys</button>
        <div class="snav-g">Customize</div>
        <button type="button" data-pane="templates"><svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"><path d="M10 6h10M10 12h10M10 18h10M5 4.5l.6 1.4 1.4.6-1.4.6L5 8.5l-.6-1.4L3 6.5l1.4-.6zM5 15.5l.6 1.4 1.4.6-1.4.6-.6 1.4-.6-1.4L3 17.5l1.4-.6z"/></svg>Prompt templates</button>
        <div class="snav-g">Quick Prompt</div>
        <button type="button" data-pane="about"><svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"><path d="M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18z M12 11v5 M12 8h0"/></svg>About</button>
      </nav>
      <div class="spanes">
        <section class="spane" data-pane="general">
          <h2>General</h2>
          <div class="sline">
            <div><div class="sl-t">Conversation history</div><div class="sl-d">Keeps your last 50 conversations on this computer so you can reopen and continue them (Recent Chats). Turning it off deletes them.</div></div>
            <div class="seg" data-name="saveHistory"><button type="button" data-v="true">On</button><button type="button" data-v="false">Off</button></div>
          </div>
          <div class="sline">
            <div><div class="sl-t">Keep Pip on screen</div><div class="sl-d">Pip, the little floating icon, stays above your apps. Closing a conversation tucks it into Pip instead of closing it, and Pip shows when Quick Prompt starts. Off: popups close completely.</div></div>
            <div class="seg" data-name="keepPip"><button type="button" data-v="true">On</button><button type="button" data-v="false">Off</button></div>
          </div>
          <div class="sline">
            <div><div class="sl-t">Shortcut</div><div class="sl-d">Opens Quick Prompt with the selected text, in any app. Click, then press the new keys (with ⌘, ⌃ or ⌥). The menu bar icon has the rest.</div></div>
            <button type="button" class="hotkey" id="hotkeyBtn" title="Click, then press the keys">⌥⇧Space</button>
          </div>
          <div class="sline">
            <div><div class="sl-t">Dictation shortcut</div><div class="sl-d">Opens the chat and starts listening, in any app; press it again to stop. Click, then press the new keys.</div></div>
            <button type="button" class="hotkey" id="dictHotkeyBtn" title="Click, then press the keys">⌥⇧M</button>
          </div>
          <div class="sline">
            <div><div class="sl-t">Conversation shortcut</div><div class="sl-d">Talk with Pip: it listens, sends what you said, reads the answer aloud and listens again. Press it again to send (or, while Pip speaks, to cut in); with nothing said, it ends the conversation.</div></div>
            <button type="button" class="hotkey" id="convHotkeyBtn" title="Click, then press the keys">⌥⇧K</button>
          </div>
        </section>
        <section class="spane" data-pane="shortcuts">
          <h2>Shortcuts</h2>
          <div class="kbgroup">Anywhere</div>
          <div class="kbrow"><span class="keys"><kbd id="kbGlobal">⌥⇧Space</kbd></span><span>Ask about the selected text (change it in General)</span></div>
          <div class="kbrow"><span class="keys"><kbd id="kbDictate">⌥⇧M</kbd></span><span>Open the chat and dictate; again: stop (change it in General)</span></div>
          <div class="kbrow"><span class="keys"><kbd id="kbConverse">⌥⇧K</kbd></span><span>Conversation: start; again: send, or cut in while Pip speaks; with nothing said: end</span></div>
          <div class="kbrow"><span class="keys"><kbd>⌘,</kbd></span><span>Settings, from the menu bar menu</span></div>
          <div class="kbrow"><span class="keys"><kbd>⌘Q</kbd></span><span>Quit Quick Prompt, from the menu bar menu</span></div>
          <div class="kbgroup">In a conversation</div>
          <div class="kbrow"><span class="keys"><kbd>↵</kbd></span><span>Send</span></div>
          <div class="kbrow"><span class="keys"><kbd>⇧↵</kbd></span><span>New line</span></div>
          <div class="kbrow"><span class="keys"><kbd>⌘↵</kbd></span><span>With the box empty: paste the answer into the app you were using</span></div>
          <div class="kbrow"><span class="keys"><kbd>esc</kbd></span><span>Close, or tuck into Pip</span></div>
          <div class="kbrow"><span class="keys"><kbd>↵</kbd> <kbd>⌘↵</kbd></span><span>In a template's fields: next field, send</span></div>
          <div class="kbgroup">Dictation</div>
          <div class="kbrow"><span class="keys">Microphone</span><span>Start; click it or ■ to stop (the text stays in the box)</span></div>
          <div class="kbrow"><span class="keys"><kbd>↵</kbd></span><span>While dictating: stop and show the text; ↵ again sends</span></div>
          <div class="kbrow"><span class="keys"><kbd>esc</kbd></span><span>While dictating: cancel</span></div>
          <div class="kbgroup">Conversation</div>
          <div class="kbrow"><span class="keys">Hold the microphone</span><span>Start a conversation</span></div>
          <div class="kbrow"><span class="keys">Speak over Pip</span><span>Pip stops reading and listens</span></div>
          <div class="kbrow"><span class="keys"><kbd>↵</kbd> · click Pip</span><span>Send what you said (or cut in while Pip speaks)</span></div>
          <div class="kbrow"><span class="keys"><kbd>esc</kbd></span><span>End the conversation</span></div>
          <div class="kbgroup">Pip</div>
          <div class="kbrow"><span class="keys">Click</span><span>Open, or tuck away</span></div>
          <div class="kbrow"><span class="keys">Drag</span><span>Move it; a quick throw sends it to the corner that way</span></div>
          <div class="kbrow"><span class="keys">Middle click · <kbd>⌥</kbd> click</span><span>To the nearest edge; on an edge, on to the next corner clockwise</span></div>
          <div class="kbrow"><span class="keys">Right click</span><span>Menu: new chat, recent chats, templates, settings</span></div>
        </section>
        <section class="spane" data-pane="speech">
          <h2>Dictation</h2>
          <div class="shint">Speak instead of typing: click the microphone in the question bar. It runs on this Mac only (Whisper); what you say never leaves it.</div>
          <div class="sline" style="display:block">
            <div class="sl-t">Languages you speak</div><div class="sl-d">Each recording picks one of these from its first seconds; with one, it is always that one. Keep the list short: similar languages (Turkish and Azerbaijani, Spanish and Portuguese) get mixed up. English words inside another language come out fine.</div>
            <div class="langs" id="speechLangs"></div>
          </div>
          <div class="sline" style="display:block">
            <div class="sl-t">Words to expect</div><div class="sl-d">Names and terms you often say, so they come out spelled right. Separate them with commas.</div>
            <textarea id="speechHints" rows="2" spellcheck="false" placeholder="SAP Business One, Service Layer, ProcessForce, CompuTec"></textarea>
          </div>
          <div class="sline">
            <div><div class="sl-t">Speech model</div><div class="sl-d" id="spModel">Checking…</div></div>
            <button type="button" id="spModelBtn" hidden>Download</button>
          </div>
        </section>
        <section class="spane" data-pane="voice">
          <h2>Read Aloud</h2>
          <div class="shint">Answers can be read to you: click the speaker on an answer, or have every answer read as it comes. The voices run on this Mac only (EMA Lightning for Turkish, Kokoro for English); nothing you read leaves it.</div>
          <div class="sline">
            <div><div class="sl-t">Voices</div><div class="sl-d" id="vModel">Checking…</div></div>
            <button type="button" id="vModelBtn" hidden>Download</button>
          </div>
          <div class="sline">
            <div><div class="sl-t">Read answers aloud</div><div class="sl-d">Every new answer is read while it is written. Off: only when you click the speaker on an answer.</div></div>
            <div class="seg" data-name="readAloud"><button type="button" data-v="true">On</button><button type="button" data-v="false">Off</button></div>
          </div>
          <div class="sline" style="display:block">
            <div class="sl-t">Voice for each language</div><div class="sl-d">Turkish and English for now; answers in other languages are not read yet.</div>
            <div class="vlist" id="readVoices"></div>
          </div>
          <div class="sline">
            <div><div class="sl-t">In a conversation, send</div><div class="sl-d">When you pause for a moment, or only when you say so (the shortcut, ↵, or a click on Pip), so a pause to think never sends it.</div></div>
            <div class="seg" data-name="conversationEnd"><button type="button" data-v="silence">On a pause</button><button type="button" data-v="manual">When I say</button></div>
          </div>
          <div class="sline">
            <div style="margin-right:16px"><div class="sl-t">Speed</div></div>
            <div class="slider"><span class="sz small" title="Slower">Slow</span><input type="range" id="readRate" min="50" max="200" step="5" aria-label="Reading speed"><span class="sz small" title="Faster">Fast</span></div>
          </div>
          <div class="shint">In Turkish answers, English words are read the way they are said: “Service Layer” as “sörvis leyır”, “API” as “ey pi ay”. Code, tables and links are skipped.</div>
        </section>
        <section class="spane" data-pane="appearance">
          <h2>Appearance</h2>
          <div class="sline">
            <div><div class="sl-t">Chat size</div><div class="sl-d">The width and height of the conversation.</div></div>
            <div class="slider"><span class="sz small">A</span><input type="range" id="sizeSlider" min="0" max="100" step="1" aria-label="Chat size"><span class="sz big">A</span></div>
          </div>
          <div class="sline">
            <div><div class="sl-t">Pip size</div><div class="sl-d">Pip, the little floating icon, and its toolbar.</div></div>
            <div class="slider"><span class="sz small">A</span><input type="range" id="iconSlider" min="0" max="100" step="1" aria-label="Pip size"><span class="sz big">A</span></div>
          </div>
          <div class="sline">
            <div><div class="sl-t">Glass</div><div class="sl-d">From clear to dense, for easier reading. You see it as you move the slider.</div></div>
            <div class="slider"><span class="sz small" title="Clear">◌</span><input type="range" id="glassSlider" min="0" max="100" step="1" aria-label="Glass density"><span class="sz big" title="Dense">●</span></div>
          </div>
        </section>
        <section class="spane" data-pane="agents">
          <h2>Agents</h2>
          <div class="sl-d">An agent is an AI set up your way: which AI, how it connects, the model, and which of your plugins, skills and MCP servers it may use. New chats, Pip and the shortcuts use the default agent; a template or the chat can pick another.</div>
          <div class="hlist aglist" id="agList"></div>
          <div class="actions" id="agButtons"><button type="button" class="primary" id="agNew">New agent</button></div>
          <div class="aform agform" id="agForm" hidden>
            <label class="sfield">Name<input type="text" id="agName" spellcheck="false" maxlength="40" placeholder="e.g. Claude Fast"></label>
            <div class="srow sub"><span class="sname">Colour</span><div class="swatches" id="agColors"></div></div>
            <div class="srow sub"><span class="sname">AI</span>
              <div class="seg" data-name="agProvider"><button type="button" data-v="claude">Claude</button><button type="button" data-v="codex">ChatGPT</button></div></div>
            <div class="srow sub"><span class="sname">Connection</span>
              <div class="seg" data-name="agConnection"><button type="button" data-v="cli">CLI</button><button type="button" data-v="api">API</button></div></div>
            <div class="shint" id="agConnHint"></div>
            <div class="frow">
              <label class="sfield" id="agModelClaudeWrap">Model<select id="agModelClaude">
                <option value="">Default</option>
                <option value="claude-opus-5-5">Claude Opus 5.5</option>
                <option value="claude-sonnet-5-5">Claude Sonnet 5.5</option>
                <option value="claude-haiku-5-5">Claude Haiku 5.5 — fastest</option>
              </select></label>
              <label class="sfield" id="agModelGptWrap">Model<input type="text" id="agModelGpt" spellcheck="false"></label>
              <label class="sfield" id="agEffortWrap">Effort<select id="agEffort">
                <option value="low">Low — fastest</option><option value="medium">Medium</option><option value="high">High</option><option value="xhigh">Extra high</option><option value="max">Max</option>
              </select></label>
            </div>
            <div class="cli-only" data-for="agConnection">
              <div class="srow sub"><span class="sname">Plugins, skills and MCP servers</span>
                <div class="seg" data-name="agSetupOn"><button type="button" data-v="off">Off</button><button type="button" data-v="on">On</button></div></div>
              <div class="shint" id="agSetupHint"></div>
              <div class="setup" id="agSetupBox" data-setup="claude" data-for="agSetupOn" hidden></div>
            </div>
            <label class="sfield">Instruction (optional)<textarea id="agInstruction" rows="3" spellcheck="false" placeholder="e.g. Answer in Turkish. Keep it short."></textarea></label>
            <div class="srow sub"><span class="sname">Default agent</span>
              <div class="seg" data-name="agDefault"><button type="button" data-v="true">Yes</button><button type="button" data-v="false">No</button></div></div>
            <div class="shint">New chats, Pip and the shortcuts use the default agent.</div>
            <div class="actions"><button type="button" class="primary" id="agSave">Save</button><button type="button" id="agCancel">Cancel</button><span style="flex:1"></span><button type="button" id="agDelete">Delete</button></div>
          </div>
        </section>
        <section class="spane" data-pane="keys">
          <h2>API keys</h2>
          <div class="sl-d">Agents that connect through the API use these keys (billed per use to your account). Agents that connect through the CLI use your Claude or ChatGPT plan instead. The keys stay in your Keychain.</div>
          <div class="skey"><label class="sfield">Anthropic API key (Claude)<input type="password" id="claudeKey" autocomplete="off" spellcheck="false" placeholder="sk-ant-…"></label><button type="button" data-clear="claudeKey" hidden>Remove</button></div>
          <div class="skey"><label class="sfield">OpenAI API key (ChatGPT)<input type="password" id="chatgptKey" autocomplete="off" spellcheck="false" placeholder="sk-…"></label><button type="button" data-clear="chatgptKey" hidden>Remove</button></div>
        </section>
        <section class="spane" data-pane="templates">
          <h2>Prompt templates</h2>
          <div class="sl-d">Your own prompts, with fields to fill in. Run them from Pip or the menu bar. Each one runs with an agent of your choice.</div>
          <div class="stemplates" id="setTemplates"></div>
        </section>
        <section class="spane" data-pane="about">
          <h2>About</h2>
          <div class="sline"><div><div class="sl-t">Quick Prompt</div><div class="sl-d" id="aboutVersion"></div></div></div>
          <div class="sline"><div><div class="sl-t">Your keys</div><div class="sl-d">API keys are stored encrypted on this computer and are only sent to Anthropic or OpenAI. A prompt template can override CLI or API for itself.</div></div></div>
          <div class="sline"><div><div class="sl-t">Source and updates</div><div class="sl-d">github.com/ismailorhan/pip-quick-prompt, under Releases → Latest build</div></div></div>
          <div class="sline"><div><div class="sl-t">Voices and pronunciation data</div><div class="sl-d">Read aloud uses EMA Lightning (Turkish) and Kokoro-82M (English), both Apache 2.0, on ONNX Runtime (MIT); normalizer-tr and misaki's lexicons (Apache 2.0); the CMU Pronouncing Dictionary (© 1993-2015 Carnegie Mellon University, BSD licence) and Zemberek's Turkish dictionaries (Apache 2.0). Full notices: THIRD-PARTY-NOTICES.md in the app.</div></div></div>
        </section>
      </div>
    </div>
    <div class="sfoot">
      <span class="status" id="setVersion"></span>
      <span class="sfoot-hint">Changes apply to the next request.</span>
      <button type="button" id="setBack">Cancel</button>
      <button type="button" class="primary" id="setSave">Save</button>
    </div>
  </div>
  <div class="history glass" id="historyView" hidden>
    <div class="meta" data-drag>
      <span class="dot"></span>
      <span class="title">Recent conversations</span>
      <span class="status"></span>
      <button type="button" class="x" id="histClose" title="Close">✕</button>
    </div>
    <div class="hlist" id="hlist"></div>
    <div class="actions"><button type="button" id="histBack">Back</button><button type="button" id="histClear">Clear history</button></div>
  </div>
  <div class="history glass" id="actionsView" hidden>
    <div class="meta" data-drag>
      <span class="title" id="actTitle">Prompt templates</span>
      <span class="status"></span>
      <button type="button" class="x" id="actClose" title="Close">✕</button>
    </div>
    <div class="hlist" id="actList"></div>
    <div class="actions" id="actButtons"><button type="button" class="primary" id="actNew">New template</button><button type="button" id="actImport">Import…</button><button type="button" id="actExport">Export…</button><span style="flex:1"></span><button type="button" id="actBack">Back</button>
      <input type="file" id="importFile" accept=".json,application/json" hidden></div>
    <div class="exportbar" id="exportBar" hidden>
      <label class="chk"><input type="checkbox" id="exportAll"><span>Select all</span></label>
      <span class="ecount" id="exportCount"></span>
      <span style="flex:1"></span>
      <button type="button" id="exportCancel">Cancel</button>
      <button type="button" class="primary" id="exportGo">Export</button>
    </div>
    <div class="aform" id="actForm" hidden>
      <label class="sfield">Title<input type="text" id="afTitle" spellcheck="false" placeholder="e.g. Reply to email"></label>
      <label class="sfield">Prompt<textarea id="afPrompt" rows="5" spellcheck="false" placeholder="Write a {tone:friendly|formal|short} reply to this email:&#10;&#10;{$selected}"></textarea></label>
      <div class="params">
        <div class="ptitle2">Parameters</div>
        <div class="prow"><code>{$selected}</code><span>What is selected in the app you are in.</span></div>
        <div class="prow"><code>{$clipboard}</code><span>What is on the clipboard.</span></div>
        <div class="prow"><code>{name}</code><span>A field you fill in each time, e.g. <code>{language}</code>.</span></div>
        <div class="prow"><code>{name:a|b|c}</code><span>A list to pick from, e.g. <code>{tone:friendly|formal|short}</code>; the first is the default.</span></div>
        <div class="pnote">Parameters with <b>$</b> are filled in by Quick Prompt. Without <code>{$selected}</code> or <code>{$clipboard}</code>, you type or paste the text in the popup.</div>
      </div>
      <label class="sfield">Agent<select id="afAgent"></select></label>
      <div class="shint">Which AI, model, plugins, skills and MCP servers it uses. Set agents up in Settings → Agents.</div>
      <div class="srow sub"><span class="sname">Copy the first answer</span>
        <div class="seg" data-name="afAutoCopy"><button type="button" data-v="true">On</button><button type="button" data-v="false">Off</button></div></div>
      <div class="shint">On: the first answer goes onto the clipboard as soon as it is complete, ready to paste.</div>
      <div class="actions"><button type="button" class="primary" id="afSave">Save</button><button type="button" id="afCancel">Cancel</button><span style="flex:1"></span><button type="button" id="afDelete">Delete</button></div>
    </div>
  </div>
  <div class="fields glass" id="fieldsBox" hidden><div class="ftitle" id="fieldsTitle"></div><div class="flist" id="fieldList"></div></div>
  <div class="attach glass" id="attachBox" hidden><div class="alist" id="attachList"></div></div>
  <div class="lightbox glass" id="lightbox" hidden><img id="lightImg" alt=""><button type="button" class="x lclose" id="lightClose" title="Close (Esc)">✕</button></div>
  <div class="speechbox glass" id="speechBox" hidden><div class="sbt" id="sbText"></div><div class="sbbar" id="sbBar" hidden><i id="sbFill"></i></div><div class="sba" id="sbActions"></div></div>
  <div class="ctx glass" id="ctxWrap" hidden><textarea id="ctx" rows="3" placeholder="Paste the text here…"></textarea></div>
  <div class="bar glass" id="bar" data-drag>
    <button type="button" class="round" id="ctxBtn" title="Add text or an attachment">+</button><input type="file" id="attachFile" multiple hidden>
    <textarea id="prompt" rows="1" placeholder="Ask…"></textarea>
    <button type="button" class="round gear" id="barPin" title="Pin: Esc and ✕ keep it as Pip" hidden><svg viewBox="0 0 24 24" width="17" height="17" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M9 4h6l-1 6 3 3H7l3-3-1-6z M12 16v5"/></svg></button>
    <button type="button" class="round gear" id="barHistory" title="Recent conversations"><svg viewBox="0 0 24 24" width="17" height="17" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M3.5 12a8.5 8.5 0 1 0 2.5-6M3.5 4.5V8H7M12 7.5V12l3 2"/></svg></button>
    <button type="button" class="round gear mic" id="barMic" title="Dictate: speak, then click again (↵ sends, esc cancels)" hidden><svg viewBox="0 0 24 24" width="17" height="17" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M12 3a3 3 0 0 0-3 3v6a3 3 0 0 0 6 0V6a3 3 0 0 0-3-3z M5.5 11a6.5 6.5 0 0 0 13 0 M12 17.5V21 M8.5 21h7"/></svg></button>
    <button type="button" class="send idle" id="send" title="Send (Enter)"></button>
  </div>
  <div class="barfoot" id="barFoot" hidden><button type="button" class="agentpick glass" id="barAgent" title="Agent: click to switch"><span class="dot"></span><span class="aname"></span><span class="amodel"></span><svg viewBox="0 0 24 24" width="11" height="11" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M7 10l5 5 5-5"/></svg></button></div>
  </div>
  <div class="plist glass" id="presetList" hidden><div class="ptitle">Prompt templates</div><div id="plistItems"></div><button type="button" class="pitem pedit" id="plistEdit"><span class="pname">Edit templates…</span></button></div>
  <div class="bubble glass" id="bubble" hidden><button type="button" class="bclose" id="bubbleClose" title="Dismiss">✕</button><div class="bt"><span class="ok" id="bubbleIcon">✓</span><span id="bubbleTitle"></span></div><div class="bb" id="bubbleText"></div></div>
</div>
<div class="toast" id="toast"></div>
<canvas id="pipWave" hidden></canvas>
<script>
(() => {
  "use strict";
  const base = location.pathname.endsWith("/") ? location.pathname : location.pathname + "/";
  const isTab = new URLSearchParams(location.search).get("tab") === "1";
  const bridge = window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.quickPrompt;
  const nativeInfo = window.__quickPromptNative || {};
  const isMac = /Mac/.test(navigator.platform);
  if (bridge) {
    document.documentElement.classList.add("native");
    if (nativeInfo.glass !== "liquid") document.documentElement.classList.add("vibrancy");
  }
  const $ = id => document.getElementById(id);
  // The panel cannot be taller than ~85% of the screen; long views scroll inside that.
  // The question grows with what is typed, up to about half the screen (then it scrolls).
  const promptMax = Math.max(140, Math.round((screen.availHeight || 900) * 0.45));
  document.documentElement.style.setProperty("--prompt-max", promptMax + "px");
  const fieldMax = Math.max(120, Math.round((screen.availHeight || 900) * 0.35));
  document.documentElement.style.setProperty("--field-max", fieldMax + "px");
  document.documentElement.style.setProperty("--view-max", Math.max(360, Math.round((screen.availHeight || 900) * 0.8) - 20) + "px");
  const els = {
    root: $("root"), card: $("card"), who: $("who"), title: $("title"), status: $("status"), notice: $("notice"),
    ctxWrap: $("ctxWrap"), ctx: $("ctx"), thread: $("thread"), actions: $("actions"),
    prompt: $("prompt"), send: $("send"), ctxBtn: $("ctxBtn"), toast: $("toast"), bar: $("bar"), errActions: $("errActions"),
  };
  const mod = isMac ? "⌘" : "Ctrl+";
  $("pasteKey").textContent = mod + "↵";
  const ICON_SEND = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="M12 19V5M5.5 11.5L12 5l6.5 6.5"/></svg>';
  const ICON_STOP = '<svg viewBox="0 0 24 24"><rect x="7" y="7" width="10" height="10" rx="2" fill="currentColor"/></svg>';
  els.send.innerHTML = ICON_SEND;

  let state = null, version = 0, draftApplied = false, lastKey = "", stick = true, dictationAsked = false;

  const esc = s => String(s).replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));

  function toast(text) {
    els.toast.textContent = text;
    els.toast.classList.add("show");
    clearTimeout(toast.t);
    toast.t = setTimeout(() => els.toast.classList.remove("show"), 1200);
  }

  function closeWindow() {
    if (bridge) bridge.postMessage({ type: "close" }); else window.close();
  }

  function post(action, body) {
    return fetch(base + action, { method: "POST", headers: { "Content-Type": "application/json", "X-QuickPrompt": "1" }, body: JSON.stringify(body || {}) })
      .then(res => { if (res.status === 404) sessionEnded(); return res; });
  }

  // The engine no longer knows this popup (it restarted, or forgot the session). With Pip on screen the app
  // opens a fresh Pip in its place; otherwise (and in the Logi popup helper) the message stays.
  let ended = false;
  function sessionEnded() {
    if (ended) return;
    ended = true;
    if (bridge && (startedAsPip || pinned)) { bridge.postMessage({ type: "sessionEnded" }); return; }
    els.card.hidden = false;
    if (!sizeKnown) applySize(30);
    els.thread.innerHTML = '<div class="msg bot glass"><div class="a error">This popup session has ended. Press Esc and try again.</div></div>';
    els.send.disabled = true;
    queueLayout();
  }

  async function copyText(text) {
    try { await navigator.clipboard.writeText(text); } catch (e) { await post("copy", { text }); }
    toast("Copied");
  }

  function lastAnswer() {
    if (!state) return null;
    const done = state.messages.filter(m => m.role === "assistant" && m.state === "done");
    return done.length ? done[done.length - 1].text : null;
  }

  function pasteAndClose() {
    const text = lastAnswer();
    if (text == null || !state.appWindow) return;
    // With "Keep Pip on screen" the conversation tucks into Pip after pasting; otherwise the popup closes.
    post("paste", { text }).finally(() => { if (bridge && pinned) collapse(); else closeWindow(); });
  }

  // ---------- native layout: panel height + glass shapes ----------
  // Popup size (Settings slider, 0 = smallest … 100 = largest): panel width and answer height.
  let lastStateScale = null, lastStateIcon = null;
  let scale = -1, size = { w: 520, h: 380 }, sizeKnown = false, collapsed = false, pinned = false, startedAsPip = false;
  function applySize(value) {
    const v = Math.max(0, Math.min(100, Number.isFinite(+value) ? +value : 30));
    scale = v;
    size = { w: Math.round(400 + v * 4.2), h: Math.round(220 + v * 4) };
    sizeKnown = true;
    document.documentElement.style.setProperty("--thread-max", size.h + "px");
    queueLayout();
  }

  // Icon size (Settings): zoom of the floating icon and its toolbar, 0.7× … 1.5×.
  // Glass density (Settings): the tint over the native glass, 0 = clear … 100 = nearly solid.
  function applyGlass(value) {
    const v = Math.max(0, Math.min(100, Number.isFinite(+value) ? +value : 35));
    document.documentElement.style.setProperty("--glass-tint", (v / 100 * 0.88).toFixed(3));
  }
  applyGlass(35);
  let lastStateGlass = null, savedGlass = 35;
  let lastAutoCopied = null;

  function applyIconScale(value) {
    const v = Math.max(0, Math.min(100, Number.isFinite(+value) ? +value : 38));
    document.documentElement.style.setProperty("--mini-zoom", (0.7 + v * 0.008).toFixed(3));
    queueLayout();
  }

  let layoutQueued = false, lastLayout = "", layoutAfterReveal = false;
  const pipNudge = { x: 0, y: 0 };
  let thawNext = false;   // after this layout: show the web view again once it is drawn at the new size
  let barOpening = false;   // the toolbar is opening: the next layout lets its glass grow from the line
  let revealing = false, revealNext = null;
  function queueLayout() {
    if (!bridge || layoutQueued || !sizeKnown) return;
    if (revealing) { layoutAfterReveal = true; return; }   // positions are mid-slide; measure when done
    layoutQueued = true;
    requestAnimationFrame(() => {
      layoutQueued = false;
      if (revealing) { layoutAfterReveal = true; return; }   // the slide started meanwhile
      updateGrowDirection();
      const view = els.thread.getBoundingClientRect();
      const scrolling = els.thread.classList.contains("scrolling");
      const main = $("main");
      const rects = [...document.querySelectorAll(".glass")]
        .filter(el => !el.hidden && el.offsetParent !== null && !(scrolling && els.thread.contains(el)))
        .map(el => {
          let r = el.getBoundingClientRect();
          // Pip's toolbar tucked: its glass is just the thin line.
          if (el.id === "minibar" && el.classList.contains("tucked")) { const l = barLine(r); r = { left: l.x, top: l.y, width: l.w, height: l.h, bottom: l.y + l.h }; }
          let top = r.top, bottom = r.bottom;
          // Bubbles scroll inside the thread: only the visible part gets native glass.
          if (els.thread.contains(el)) { top = Math.max(top, view.top); bottom = Math.min(bottom, view.bottom); }
          // m: part of the conversation, which slides; Pip's own glass stays still.
          const rect = { x: r.left, y: top, w: r.width, h: Math.max(0, bottom - top), r: Math.min(parseFloat(getComputedStyle(el).borderTopLeftRadius) || 0, r.height / 2), m: main.contains(el) };
          // The toolbar opening: its glass grows out of the line it was.
          if (el.id === "minibar") rect.bar = true;
          if (el.id === "minibar" && barOpening && !el.classList.contains("tucked")) rect.from = barLine(r);
          return rect;
        })
        .filter(r => r.h > 1);
      const box = els.root.getBoundingClientRect();
      const height = Math.ceil(box.height);
      const side = mini.hidden ? 0 : Math.ceil(mini.getBoundingClientRect().width) + 10;
      const width = collapsed ? Math.ceil(box.width) : (previewOpen ? previewSize.w : settingsOpen ? settingsSize.w : actionsOpen ? 520 : size.w) + side;
      // Where the icon's middle will be in a panel of this width, so the panel can keep the icon still
      // on screen while things around it change (toolbar opening, conversation opening on either side).
      const icon = mini.hidden ? null : $("miniOpen").getBoundingClientRect();
      const anchorY = icon ? icon.top + icon.height / 2 - box.top : null;
      const anchorX = !icon ? null
        // Pip alone: the page sits at the panel's left, whose width is rounded up; measure from the left then.
        : els.root.classList.contains("side-right") && !els.root.classList.contains("is-mini") ? width - (box.right - (icon.left + icon.width / 2))
        : icon.left + icon.width / 2 - box.left;
      // Pip's centre on a whole point of the panel: the panel itself sits on whole points, so a fraction here
      // (from the icon's zoom) would be rounded differently for each panel size and Pip would shift a little
      // whenever the conversation opens or closes. A small translate of Pip's column takes the fraction away.
      if (icon) {
        const dx = Math.round(anchorX) - anchorX, dy = Math.round(anchorY) - anchorY;
        if (Math.abs(dx) > 0.01 || Math.abs(dy) > 0.01) {
          const zoom = parseFloat(getComputedStyle(mini).zoom) || 1;
          pipNudge.x += dx; pipNudge.y += dy;
          mini.style.transform = "translate(" + (pipNudge.x / zoom).toFixed(3) + "px," + (pipNudge.y / zoom).toFixed(3) + "px)";
          queueLayout();
          return;
        }
      }
      // pending: the conversation is about to slide in, so its glass waits hidden for the slide.
      const pending = main.classList.contains("reveal-pending");
      const key = JSON.stringify([width, height, rects, anchorX, anchorY, pending]);
      if (key === lastLayout) return;
      lastLayout = key;
      // Where the panel takes clicks while Pip is on its own (elsewhere they go to the app behind).
      const hits = collapsed && icon ? [...rects.map(r => ({ x: r.x, y: r.y, w: r.w, h: r.h })), { x: icon.left, y: icon.top, w: icon.width, h: icon.height }] : null;
      bridge.postMessage({ type: "layout", width, height, rects, anchorX, anchorY, pending, iconR: icon ? icon.width / 2 : null, hits });
      barOpening = false;
      if (revealNext) startRevealWhenSized(revealNext, width);
      if (thawNext) { thawNext = false; whenSized(width, thawPip); }
    });
  }
  // Beside Pip the conversation ends at Pip's bottom and grows upward; when the room above Pip runs out,
  // it lines up with Pip's top and grows downward instead (Pip itself never moves for it).
  function updateGrowDirection() {
    if (collapsed || mini.hidden || !pipEdges || pipEdges.top == null) { els.root.classList.remove("grow-down"); return; }
    const icon = $("miniOpen").getBoundingClientRect();
    const need = $("main").getBoundingClientRect().height - icon.height / 2 + 18;
    const down = els.root.classList.contains("grow-down");
    els.root.classList.toggle("grow-down", down ? need > pipEdges.top - 40 : need > pipEdges.top);
  }
  const threadObserver = window.ResizeObserver ? new ResizeObserver(() => queueLayout()) : null;
  if (bridge && window.ResizeObserver) {
    const ro = new ResizeObserver(queueLayout);
    ro.observe(els.root);
    document.querySelectorAll(".glass").forEach(el => ro.observe(el));
  }
  window.addEventListener("resize", queueLayout);

  // ---------- markdown (small, safe: everything is escaped first) ----------
  function inline(text) {
    const codes = [];
    let s = esc(text).replace(/`([^`]+)`/g, (_, c) => { codes.push(c); return "\u0000" + (codes.length - 1) + "\u0000"; });
    s = s.replace(/\*\*([^*]+)\*\*/g, "<strong>$1</strong>")
         .replace(/__([^_]+)__/g, "<strong>$1</strong>")
         .replace(/(^|[^*\w])\*([^*\s][^*]*?)\*(?!\*)/g, "$1<em>$2</em>")
         .replace(/~~([^~]+)~~/g, "<del>$1</del>")
         .replace(/\[([^\]]+)\]\((https?:\/\/[^\s)]+)\)/g, '<a href="$2" target="_blank" rel="noopener noreferrer">$1</a>');
    return s.replace(/\u0000(\d+)\u0000/g, (_, i) => "<code>" + codes[+i] + "</code>");
  }

  const reList = /^(\s*)([-*+]|\d+[.)])\s+(.*)$/;
  const reFence = /^\s*(```|~~~)/;
  const reHeading = /^(#{1,6})\s+(.*)$/;
  const reHr = /^\s*([-*_])(\s*\1){2,}\s*$/;
  const reTableSep = /^\s*\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?\s*$/;

  function splitRow(line) {
    let t = line.trim();
    if (t.startsWith("|")) t = t.slice(1);
    if (t.endsWith("|")) t = t.slice(0, -1);
    return t.split("|").map(c => c.trim());
  }

  function isBlockStart(line, next) {
    return reFence.test(line) || reHeading.test(line) || reHr.test(line) || reList.test(line) || /^\s*>/.test(line)
      || (line.includes("|") && next !== undefined && reTableSep.test(next));
  }

  function md(src) {
    const lines = String(src).replace(/\r/g, "").split("\n");
    const out = [];
    let i = 0;
    while (i < lines.length) {
      const line = lines[i];
      if (!line.trim()) { i++; continue; }

      const fence = line.match(reFence);
      if (fence) {
        const buf = [];
        i++;
        while (i < lines.length && !lines[i].trim().startsWith(fence[1])) { buf.push(lines[i]); i++; }
        i++;
        out.push('<pre><button type="button" data-copy>Copy</button><code>' + esc(buf.join("\n")) + "</code></pre>");
        continue;
      }

      const h = line.match(reHeading);
      if (h) { const n = Math.min(h[1].length + 1, 4); out.push(`<h${n}>${inline(h[2])}</h${n}>`); i++; continue; }

      if (reHr.test(line)) { out.push("<hr>"); i++; continue; }

      if (line.includes("|") && i + 1 < lines.length && reTableSep.test(lines[i + 1])) {
        const head = splitRow(line);
        i += 2;
        const rows = [];
        while (i < lines.length && lines[i].includes("|") && lines[i].trim()) { rows.push(splitRow(lines[i])); i++; }
        out.push("<table><thead><tr>" + head.map(c => "<th>" + inline(c) + "</th>").join("") + "</tr></thead><tbody>"
          + rows.map(r => "<tr>" + r.map(c => "<td>" + inline(c) + "</td>").join("") + "</tr>").join("") + "</tbody></table>");
        continue;
      }

      if (/^\s*>/.test(line)) {
        const buf = [];
        while (i < lines.length && /^\s*>/.test(lines[i])) { buf.push(lines[i].replace(/^\s*>\s?/, "")); i++; }
        out.push("<blockquote>" + md(buf.join("\n")) + "</blockquote>");
        continue;
      }

      if (reList.test(line)) {
        const ordered = /\d/.test(line.match(reList)[2]);
        const items = [];
        while (i < lines.length) {
          const m = lines[i].match(reList);
          if (m) { items.push({ indent: m[1].length, text: m[3] }); i++; continue; }
          if (lines[i].trim() && /^\s{2,}/.test(lines[i]) && items.length) { items[items.length - 1].text += " " + lines[i].trim(); i++; continue; }
          break;
        }
        const tag = ordered ? "ol" : "ul";
        out.push(`<${tag}>` + items.map(it => `<li style="margin-left:${Math.min(it.indent, 12) * 0.6}em">${inline(it.text)}</li>`).join("") + `</${tag}>`);
        continue;
      }

      const buf = [line];
      i++;
      while (i < lines.length && lines[i].trim() && !isBlockStart(lines[i], lines[i + 1])) { buf.push(lines[i]); i++; }
      out.push("<p>" + buf.map(inline).join("<br>") + "</p>");
    }
    return out.join("");
  }

  // ---------- settings ----------
  const set = { view: $("settings"), status: $("setStatus"), claudeKey: $("claudeKey"), chatgptKey: $("chatgptKey") };
  const segValues = {};
  const keyRemoval = {};
  let settingsOpen = false, settingsOnly = false;
  let actionsOpen = false, actionsOnly = false;

  function setSeg(name, value) {
    segValues[name] = value;
    document.querySelectorAll('.seg[data-name="' + name + '"] button').forEach(b => b.classList.toggle("on", b.dataset.v === value));
    const fields = document.querySelector('.api-only[data-for="' + name + '"]');
    if (fields) fields.hidden = value !== "api";
    const cli = document.querySelector('.cli-only[data-for="' + name + '"]');
    if (cli) cli.hidden = value !== "cli";
    const setup = document.querySelector('.setup[data-for="' + name + '"]');
    if (setup) { setup.hidden = value !== "on"; if (value === "on") loadSetup(); }
    if (name === "agProvider" || name === "agConnection") refreshAgentForm();
    queueLayout();
  }
  document.querySelectorAll(".seg button").forEach(b => b.addEventListener("click", () => setSeg(b.parentElement.dataset.name, b.dataset.v)));

  function showKeyHint(input, hint) {
    input.value = "";
    keyRemoval[input.id] = false;
    input.placeholder = hint ? "Saved (" + hint + ") — type to replace" : (input.id === "claudeKey" ? "sk-ant-…" : "sk-…");
    document.querySelector('[data-clear="' + input.id + '"]').hidden = !hint;
  }
  document.querySelectorAll("[data-clear]").forEach(b => b.addEventListener("click", () => {
    const input = $(b.dataset.clear);
    keyRemoval[input.id] = true;
    input.value = "";
    input.placeholder = "Will be removed when you save";
    b.hidden = true;
  }));

  // The slider resizes the popup live; Cancel goes back to the saved size.
  const sizeSlider = $("sizeSlider");
  let savedScale = 30;
  const iconSlider = $("iconSlider");
  function setSlider(value) {
    savedScale = Number.isFinite(+value) ? +value : 30;
    sizeSlider.value = savedScale;
    sizeSlider.style.setProperty("--fill", savedScale + "%");
  }
  function setIconSlider(value) {
    iconSlider.value = Number.isFinite(+value) ? +value : 38;
    iconSlider.style.setProperty("--fill", iconSlider.value + "%");
  }
  const glassSlider = $("glassSlider");
  function setGlassSlider(value) {
    glassSlider.value = Number.isFinite(+value) ? +value : 35;
    glassSlider.style.setProperty("--fill", glassSlider.value + "%");
  }
  glassSlider.addEventListener("input", () => applyGlass(glassSlider.value));   // live preview
  [sizeSlider, iconSlider, glassSlider].forEach(sl => sl.addEventListener("input", () => sl.style.setProperty("--fill", sl.value + "%")));

  // ---------- Shortcut recorder: click, then press the keys ----------
  let hotkeyValue = "alt+shift+Space", recording = false;
  const hotkeyBtn = $("hotkeyBtn");
  const MOD_SYMBOL = { ctrl: "⌃", alt: "⌥", shift: "⇧", cmd: "⌘" };
  function keyLabel(code) {
    if (/^Key[A-Z]$/.test(code)) return code.slice(3);
    if (/^Digit[0-9]$/.test(code)) return code.slice(5);
    return ({ Space: "Space", Enter: "↩", Tab: "⇥", Backquote: "`", Minus: "-", Equal: "=", BracketLeft: "[", BracketRight: "]",
      Backslash: "\\", Semicolon: ";", Quote: "'", Comma: ",", Period: ".", Slash: "/", ArrowUp: "↑", ArrowDown: "↓",
      ArrowLeft: "←", ArrowRight: "→" })[code] || code;
  }
  function hotkeyText(spec) {
    const parts = spec.split("+"), code = parts.pop();
    return ["ctrl", "alt", "shift", "cmd"].filter(m => parts.includes(m)).map(m => MOD_SYMBOL[m]).join("") + keyLabel(code);
  }
  // A shortcut button: click, then press the keys. onSet gets the spec ("alt+shift+KeyM").
  function shortcutPicker(btn, onSet) {
    let current = "", picking = false;
    const show = spec => { current = spec; btn.textContent = hotkeyText(spec); btn.classList.remove("recording"); picking = false; onSet(spec); };
    btn.addEventListener("click", () => { picking = true; btn.textContent = "Press keys…"; btn.classList.add("recording"); });
    btn.addEventListener("blur", () => { if (picking) show(current); });
    btn.addEventListener("keydown", e => {
      if (!picking) return;
      e.preventDefault(); e.stopPropagation();
      if (e.key === "Escape") { show(current); return; }
      if (["Shift", "Control", "Alt", "Meta", "CapsLock", "Fn"].includes(e.key)) return;   // wait for the actual key
      const supported = /^(Key[A-Z]|Digit[0-9]|F([1-9]|1[0-9])|Space|Enter|Tab|Backquote|Minus|Equal|BracketLeft|BracketRight|Backslash|Semicolon|Quote|Comma|Period|Slash|Arrow(Up|Down|Left|Right))$/;
      if (!(e.metaKey || e.ctrlKey || e.altKey)) { btn.textContent = "Add ⌘, ⌃ or ⌥"; return; }
      if (!supported.test(e.code)) { btn.textContent = "Try another key"; return; }
      const mods = [e.metaKey && "cmd", e.ctrlKey && "ctrl", e.altKey && "alt", e.shiftKey && "shift"].filter(Boolean);
      show(mods.join("+") + "+" + e.code);
    });
    return show;
  }
  const setHotkey = shortcutPicker(hotkeyBtn, spec => { hotkeyValue = spec; $("kbGlobal").textContent = hotkeyText(spec); });
  let dictHotkeyValue = "alt+shift+KeyM";
  const setDictHotkey = shortcutPicker($("dictHotkeyBtn"), spec => { dictHotkeyValue = spec; $("kbDictate").textContent = hotkeyText(spec); });
  let convHotkeyValue = "alt+shift+KeyK";
  const setConvHotkey = shortcutPicker($("convHotkeyBtn"), spec => { convHotkeyValue = spec; $("kbConverse").textContent = hotkeyText(spec); });

  // ---------- Settings size: drag the grip; remembered on this computer ----------
  var settingsSize = { w: 760, h: 500 };
  try { const saved = JSON.parse(localStorage.getItem("qp.settingsSize") || "null"); if (saved && saved.w && saved.h) settingsSize = saved; } catch (e) {}
  function applySettingsSize() {
    settingsSize.w = Math.round(Math.max(620, Math.min(1200, settingsSize.w)));
    settingsSize.h = Math.round(Math.max(320, Math.min(1000, settingsSize.h)));
    document.documentElement.style.setProperty("--settings-h", settingsSize.h + "px");
    queueLayout();
  }
  applySettingsSize();
  $("setGrip").addEventListener("mousedown", e => {
    if (e.button !== 0) return;
    e.preventDefault(); e.stopPropagation();
    const start = { x: e.screenX, y: e.screenY, w: settingsSize.w, h: settingsSize.h };
    // The panel stays put on Pip's side and grows on the other: drag away from Pip to make it bigger.
    const dir = els.root.classList.contains("side-right") ? -1 : 1;
    const move = ev => {
      settingsSize.w = start.w + dir * (ev.screenX - start.x);
      settingsSize.h = start.h - (ev.screenY - start.y);
      applySettingsSize();
    };
    const up = () => {
      window.removeEventListener("mousemove", move, true);
      window.removeEventListener("mouseup", up, true);
      try { localStorage.setItem("qp.settingsSize", JSON.stringify(settingsSize)); } catch (e) {}
    };
    window.addEventListener("mousemove", move, true);
    window.addEventListener("mouseup", up, true);
  });

  // Sections: the sidebar picks one; the last one used is remembered on this computer.
  let currentPane = "general";
  try { currentPane = localStorage.getItem("qp.settingsPane") || "general"; } catch (e) {}
  function showPane(name) {
    if (!set.view.querySelector('.spane[data-pane="' + name + '"]')) name = "general";
    currentPane = name;
    try { localStorage.setItem("qp.settingsPane", name); } catch (e) {}
    set.view.querySelectorAll(".spane").forEach(p => { p.hidden = p.dataset.pane !== name; });
    set.view.querySelectorAll(".snav button").forEach(b => b.classList.toggle("on", b.dataset.pane === name));
    if (name === "templates") { mountActions($("setTemplates")); showActionList(); loadActions(); }
    if (name === "agents") { showAgentList(); loadAgents(); }
    const panes = set.view.querySelector(".spanes");
    if (panes) panes.scrollTop = 0;
    queueLayout();
  }
  set.view.querySelectorAll(".snav button").forEach(b => b.addEventListener("click", () => showPane(b.dataset.pane)));

  async function openSettings(only, pane) {
    settingsOnly = !!only;
    settingsOpen = true;
    els.card.hidden = true; els.bar.hidden = true; els.ctxWrap.hidden = true;
    $("fieldsBox").hidden = true; hist.view.hidden = true; act.view.hidden = true;
    historyOpen = false; actionsOpen = false;
    set.view.hidden = false;
    set.status.textContent = "";
    showPane(pane || currentPane);
    try {
      const s = await (await fetch(base + "settings", { cache: "no-store" })).json();
      $("setVersion").textContent = s.version ? "v" + s.version : "";
      $("aboutVersion").textContent = s.version ? "Version " + s.version : "";
      setSeg("saveHistory", s.saveHistory ? "true" : "false");
      setSeg("keepPip", s.keepPip === false ? "false" : "true");
      speechLangs = parseLangs(s.speechLanguages); renderLangs();
      $("speechHints").value = s.speechHints || "";
      setSeg("readAloud", s.readAloud ? "true" : "false");
      readVoiceChoice = parseVoices(s.readVoices); loadVoice().then(renderVoiceSettings); renderVoiceSettings();
      $("readRate").value = s.readRate || 100;
      speechPost("probe");
      setHotkey(s.hotkey || "alt+shift+Space");
      setDictHotkey(s.dictationHotkey || "alt+shift+KeyM");
      setConvHotkey(s.conversationHotkey || "alt+shift+KeyK");
      setSeg("conversationEnd", s.conversationEnd === "manual" ? "manual" : "silence");
      setSlider(s.popupScale);
      setIconSlider(s.iconScale);
      savedGlass = s.glassTint; setGlassSlider(s.glassTint);
      showKeyHint(set.claudeKey, s.claude.keyHint);
      showKeyHint(set.chatgptKey, s.chatgpt.keyHint);
      setupData = null;
    } catch (e) {
      set.status.textContent = "Could not load settings";
    }
    queueLayout();
  }

  // ---------- "Use my setup": plugins, MCP servers, skills of the user's CLIs ----------
  let setupData = null, setupLoading = null;
  function loadSetup(refresh) {
    if (setupData && !refresh) { renderSetup(); return; }
    if (setupLoading) return;
    document.querySelectorAll(".setup").forEach(el => { el.innerHTML = '<div class="shint">Reading your setup…</div>'; });
    setupLoading = fetch(base + "setup" + (refresh ? "?refresh=1" : ""), { cache: "no-store" })
      .then(r => r.json()).then(d => { setupData = d; }).catch(() => { setupData = { error: true }; })
      .finally(() => { setupLoading = null; renderSetup(); });
  }
  function check(kind, id, label, on, disabled, note) {
    return '<label class="chk' + (disabled ? ' off' : '') + '"><input type="checkbox" data-kind="' + kind + '" data-id="' + esc(id) + '"'
      + (on ? ' checked' : '') + (disabled ? ' disabled' : '') + '><span>' + esc(label) + '</span>' + (note ? '<em>' + esc(note) + '</em>' : '') + '</label>';
  }
  // An action's own choice (el.choice) overrides what Settings has ticked.
  function withChoice(d, c) {
    if (!d || !c) return d;
    const list = (a, b) => a || b || [];
    const offPlugins = list(c.disabledPlugins, c.DisabledPlugins), offServers = list(c.disabledServers, c.DisabledServers), offSkills = list(c.disabledSkills, c.DisabledSkills);
    const flag = (a, b, fallback) => typeof a === "boolean" ? a : typeof b === "boolean" ? b : fallback;
    return Object.assign({}, d, {
      skills: flag(c.skills, c.Skills, true), web: flag(c.web, c.Web, true),
      plugins: (d.plugins || []).map(p => Object.assign({}, p, { on: p.cliEnabled && !offPlugins.includes(p.id) })),
      servers: (d.servers || []).map(x => Object.assign({}, x, { on: !offServers.includes(x.name) })),
      skillList: (d.skillList || []).map(k => Object.assign({}, k, { on: !offSkills.includes(k.name) })),
    });
  }
  // Three lists, one under the other (Plugins, Skills, MCP servers), each with a header checkbox that
  // switches the whole list on or off; then web search for Claude.
  function group(kind, title, itemsHtml, count, empty) {
    return '<div class="sgrp" data-group="' + kind + '">'
      + '<label class="chk ghead"><input type="checkbox" data-group-toggle="' + kind + '"' + (kind === "skill" ? ' data-kind="skills" data-id="skills"' : '') + '>'
      + '<span>' + title + '</span><em class="gcount"></em></label>'
      + '<div class="gitems">' + (count ? itemsHtml : '<div class="shint">' + empty + '</div>') + '</div></div>';
  }
  function renderSetup() {
    document.querySelectorAll(".setup").forEach(el => {
      const d = withChoice(setupData && setupData[el.dataset.setup], el.choice);
      if (!d) { el.innerHTML = '<div class="shint">Could not read your setup.</div>'; return; }
      if (!d.installed) { el.innerHTML = '<div class="shint">The CLI was not found on this computer.</div>'; return; }
      const claude = el.dataset.setup === "claude";
      const skillList = d.skillList || [];
      let html = "";
      if (claude) {
        html += group("plugin", "Plugins",
          (d.plugins || []).map(p => check("plugin", p.id, p.id.replace(/@.*$/, ""), p.on, !p.cliEnabled, p.cliEnabled ? "" : "off in Claude Code")).join(""),
          (d.plugins || []).length, "No plugins installed.");
      }
      html += group("skill", "Skills",
        skillList.map(k => check("skill", k.name, k.name, k.on && d.skills !== false, false, k.plugin ? "from " + k.plugin.replace(/@.*$/, "") : "")).join(""),
        skillList.length, claude ? "No skills of your own; Claude Code's built-in skills follow this switch." : "No skills found.");
      html += group("server", "MCP servers",
        d.servers.map(x => check("server", x.name, x.name, x.on)).join(""),
        d.servers.length, "No MCP servers in your " + (claude ? "user configuration (~/.claude.json)" : "~/.codex/config.toml") + ".");
      if (claude) {
        html += '<div class="sgrp"><label class="chk ghead"><input type="checkbox" data-kind="web" data-id="web"' + (d.web ? " checked" : "") + '><span>Web search and fetch</span></label></div>';
      }
      html += '<div class="shint">' + (d.error ? esc(d.error) + " " : "") + '<a href="#" data-refresh>Refresh</a></div>';
      el.innerHTML = html;
      el.querySelectorAll(".sgrp[data-group]").forEach(g => {
        // Skills with no list of their own: the header alone says whether skills are used.
        if (g.dataset.group === "skill" && !g.querySelector('input[data-kind="skill"]')) g.querySelector(".ghead input").checked = d.skills !== false;
        syncGroup(g);
      });
      syncPluginSkills(el);
    });
    queueLayout();
  }
  // Header checkbox: on when every item is on, a dash when some are.
  function syncGroup(g) {
    const items = [...g.querySelectorAll(".gitems input[type=checkbox]")].filter(b => !b.disabled);
    const head = g.querySelector(".ghead input");
    const count = g.querySelector(".gcount");
    if (!items.length) { if (count) count.textContent = ""; return; }
    const on = items.filter(b => b.checked).length;
    head.checked = on > 0;
    head.indeterminate = on > 0 && on < items.length;
    if (count) count.textContent = on + " of " + items.length;
  }
  // A plugin's skills go with it.
  function syncPluginSkills(box) {
    const list = ((setupData && setupData[box.dataset.setup]) || {}).skillList || [];
    box.querySelectorAll('input[data-kind="skill"]').forEach(sk => {
      const k = list.find(x => x.name === sk.dataset.id);
      if (!k || !k.plugin) return;
      const plugin = box.querySelector('input[data-kind="plugin"][data-id="' + CSS.escape(k.plugin) + '"]');
      const off = plugin && !plugin.checked;
      sk.disabled = !!off;
      sk.parentElement.classList.toggle("off", !!off);
    });
    box.querySelectorAll(".sgrp[data-group]").forEach(syncGroup);
  }
  document.addEventListener("change", e => {
    const t = e.target;
    if (!(t instanceof HTMLInputElement)) return;
    const box = t.closest(".setup");
    if (!box) return;
    if (t.dataset.groupToggle) {
      // Header: the whole list on or off.
      t.closest(".sgrp").querySelectorAll(".gitems input[type=checkbox]").forEach(b => { if (!b.disabled) b.checked = t.checked; });
    }
    syncPluginSkills(box);
    queueLayout();
  });
  document.addEventListener("click", e => {
    const a = e.target.closest("[data-refresh]");
    if (a) { e.preventDefault(); loadSetup(true); }
  });
  // Reads the ticks of one setup box (settings or an action) as a choice.
  function choiceFrom(el, on) {
    const name = el.dataset.setup;
    if (!setupData || !setupData[name] || !setupData[name].installed || !el.querySelector("input")) return JSON.stringify(Object.assign({}, el.choice || {}, { enabled: on }));
    const box = (kind, id) => el.querySelector('input[data-kind="' + kind + '"][data-id="' + CSS.escape(id) + '"]');
    const d = setupData[name];
    const off = (kind, ids) => ids.filter(id => { const b = box(kind, id); return b && !b.disabled && !b.checked; });
    const skills = box("skills", "skills"), web = box("web", "web");
    if (skills && skills.indeterminate) skills.checked = true;
    return JSON.stringify({
      enabled: on,
      disabledPlugins: off("plugin", (d.plugins || []).map(p => p.id)),
      disabledServers: off("server", d.servers.map(s => s.name)),
      disabledSkills: off("skill", (d.skillList || []).map(k => k.name)),
      skills: skills ? skills.checked : d.skills,
      web: web ? web.checked : d.web,
    });
  }

  // ---------- Agents (Settings → Agents): saved on their own, like templates ----------
  const ag = { list: $("agList"), form: $("agForm"), buttons: $("agButtons"), data: null, editing: null, color: "" };
  const MODEL_NAMES = { "claude-opus-5-5": "Opus 5.5", "claude-sonnet-5-5": "Sonnet 5.5", "claude-haiku-5-5": "Haiku 5.5" };
  async function fetchAgents() {
    try { ag.data = await (await fetch(base + "agents", { cache: "no-store" })).json(); } catch (e) { ag.data = ag.data || null; }
    return ag.data;
  }
  function agentSummary(a) {
    const parts = [a.provider === "codex" ? "ChatGPT" : "Claude", a.connection === "api" ? "API" : "CLI"];
    parts.push(a.model ? (MODEL_NAMES[a.model] || a.model) : "default model");
    if (a.connection === "cli") parts.push(a.setup && a.setup.enabled ? "plugins, skills, MCP" : "plain answers");
    if (a.instruction) parts.push("own instruction");
    return parts.join(" · ");
  }
  function renderAgents() {
    const d = ag.data;
    ag.list.innerHTML = d && d.agents.length ? d.agents.map(a =>
      '<div class="hitem aitem" data-agent="' + esc(a.id) + '" role="button" tabindex="0" style="--agent:' + esc(a.color) + '">'
      + '<span class="dot"></span><div class="hbody"><div class="hq">' + esc(a.name) + '</div>'
      + '<div class="hmeta">' + esc(agentSummary(a)) + '</div></div>'
      + (a.isDefault ? '<span class="defbadge">Default</span>' : '<button type="button" class="mkdef" data-mkdef="' + esc(a.id) + '">Make default</button>')
      + '</div>').join("") : '<div class="hempty">' + (d ? "No agents." : "Could not load agents.") + '</div>';
    queueLayout();
  }
  async function loadAgents() {
    ag.list.innerHTML = '<div class="hempty">Loading…</div>';
    await fetchAgents();
    renderAgents();
  }
  function showAgentList() {
    ag.form.hidden = true; ag.list.hidden = false; ag.buttons.hidden = false;
    queueLayout();
  }
  function setAgentColor(c) {
    ag.color = c;
    $("agColors").querySelectorAll("button").forEach(b => b.classList.toggle("on", b.dataset.c === c));
  }
  function editAgent(a) {
    ag.editing = a || null;
    const palette = (ag.data && ag.data.palette) || ["#D97757", "#10A37F", "#4C8DF6", "#A56BF2"];
    $("agColors").innerHTML = palette.map(c => '<button type="button" data-c="' + esc(c) + '" style="--c:' + esc(c) + '" aria-label="Colour ' + esc(c) + '"></button>').join("");
    $("agName").value = a ? a.name : "";
    const provider = a ? a.provider : "claude";
    setAgentColor(a ? a.color : (provider === "codex" ? "#10A37F" : palette[(ag.data ? ag.data.agents.length : 0) % palette.length]));
    const box = $("agSetupBox");
    box.dataset.setup = provider;
    box.choice = a ? a.setup : null;
    setSeg("agProvider", provider);
    setSeg("agConnection", a ? a.connection : "cli");
    const sel = $("agModelClaude");
    if (a && a.model && provider === "claude" && ![...sel.options].some(o => o.value === a.model)) sel.add(new Option(a.model, a.model));
    sel.value = a && provider === "claude" ? a.model : "";
    $("agModelGpt").value = a && provider === "codex" ? a.model : "";
    $("agEffort").value = a && a.effort ? a.effort : "low";
    setSeg("agSetupOn", a && a.setup && a.setup.enabled ? "on" : "off");
    $("agInstruction").value = a ? a.instruction : "";
    setSeg("agDefault", a && a.isDefault ? "true" : "false");
    document.querySelector('.seg[data-name="agDefault"]').style.pointerEvents = a && a.isDefault ? "none" : "";
    $("agDelete").hidden = !a || a.isDefault;
    ag.list.hidden = true; ag.buttons.hidden = true; ag.form.hidden = false;
    refreshAgentForm();
    setTimeout(() => $("agName").focus(), 0);
  }
  // What the form offers follows the AI and the connection.
  function refreshAgentForm() {
    const provider = segValues.agProvider || "claude", conn = segValues.agConnection || "cli", box = $("agSetupBox");
    if (box.dataset.setup !== provider) { box.choice = null; box.dataset.setup = provider; if (segValues.agSetupOn === "on") loadSetup(); }
    $("agModelClaudeWrap").hidden = provider !== "claude";
    $("agModelGptWrap").hidden = provider !== "codex";
    $("agEffortWrap").hidden = !(provider === "claude" && conn === "api");
    $("agModelClaude").options[0].text = conn === "api" ? "Default (Opus 5.5)" : "Claude Code's default";
    $("agModelGpt").placeholder = conn === "api" ? "gpt-5-mini" : "Codex's default";
    $("agConnHint").textContent = conn === "api"
      ? "Uses your " + (provider === "codex" ? "OpenAI" : "Anthropic") + " API key (Settings → API keys), billed per use. Faster; no plugins, skills or MCP servers."
      : "Uses your " + (provider === "codex" ? "ChatGPT plan through the Codex CLI" : "Claude plan through Claude Code") + ".";
    $("agSetupHint").textContent = provider === "codex"
      ? "Off: plain answers, faster. On: only what you tick below. Codex stays read-only."
      : "Off: plain answers, fastest. On: only what you tick below. It can look things up and use those tools, but cannot run commands or change files.";
    queueLayout();
  }
  $("agColors").addEventListener("click", e => { const b = e.target.closest("button[data-c]"); if (b) setAgentColor(b.dataset.c); });
  $("agNew").addEventListener("click", () => editAgent(null));
  $("agCancel").addEventListener("click", showAgentList);
  ag.list.addEventListener("click", async e => {
    const mk = e.target.closest("[data-mkdef]");
    if (mk) {
      e.stopPropagation();
      const res = await post("defaultagent", { id: mk.dataset.mkdef });
      if (res.ok) { ag.data = await res.json(); renderAgents(); toast("Default agent changed"); }
      return;
    }
    const row = e.target.closest("[data-agent]");
    if (row && ag.data) editAgent(ag.data.agents.find(a => a.id === row.dataset.agent));
  });
  ag.list.addEventListener("keydown", e => {
    const row = e.target.closest("[data-agent]");
    if (row && e.key === "Enter" && e.target === row && ag.data) editAgent(ag.data.agents.find(a => a.id === row.dataset.agent));
  });
  $("agSave").addEventListener("click", async () => {
    const provider = segValues.agProvider || "claude", conn = segValues.agConnection || "cli";
    const body = {
      id: ag.editing ? ag.editing.id : "",
      name: $("agName").value.trim(), color: ag.color, provider, connection: conn,
      model: provider === "claude" ? $("agModelClaude").value : $("agModelGpt").value.trim(),
      effort: provider === "claude" && conn === "api" ? $("agEffort").value : (ag.editing ? ag.editing.effort : ""),
      instruction: $("agInstruction").value.trim(),
      setup: choiceFrom($("agSetupBox"), segValues.agSetupOn === "on"),
      makeDefault: segValues.agDefault === "true" ? "true" : "false",
    };
    if (!body.name) { $("agName").focus(); toast("Give it a name"); return; }
    const res = await post("saveagent", body);
    const data = res.ok ? await res.json() : { error: "Could not save" };
    if (data.error) { toast(data.error); return; }
    ag.data = data;
    renderAgents(); showAgentList(); toast("Saved");
  });
  $("agDelete").addEventListener("click", async () => {
    if (!ag.editing) return;
    const res = await post("deleteagent", { id: ag.editing.id });
    const data = res.ok ? await res.json() : { error: "Could not delete" };
    if (data.error) { toast(data.error); return; }
    ag.data = data;
    renderAgents(); showAgentList(); toast("Deleted");
  });

  function closeSettings() {
    if (scale !== savedScale) applySize(savedScale);
    applyGlass(savedGlass);   // undo a preview that was not saved
    if (settingsOnly) { closeWindow(); return; }
    settingsOpen = false;
    set.view.hidden = true; els.bar.hidden = false;
    lastKey = "";
    if (state) render();
    queueLayout();
    els.prompt.focus();
  }

  async function saveSettings() {
    const keyValue = input => keyRemoval[input.id] ? "-" : input.value.trim();
    const body = {
      claudeKey: keyValue(set.claudeKey), chatgptKey: keyValue(set.chatgptKey),
      saveHistory: segValues.saveHistory,
      keepPip: segValues.keepPip,
      speechLanguages: speechLangs.join(","),
      speechHints: $("speechHints").value.trim(),
      readAloud: segValues.readAloud,
      readVoices: Object.entries(readVoiceChoice).filter(([, v]) => v).map(([k, v]) => k + "=" + v).join(";"),
      readRate: String($("readRate").value),
      hotkey: hotkeyValue,
      dictationHotkey: dictHotkeyValue,
      conversationHotkey: convHotkeyValue,
      conversationEnd: segValues.conversationEnd || "silence",
      popupScale: String(sizeSlider.value),
      iconScale: String(iconSlider.value),
      glassTint: String(glassSlider.value),
    };
    const res = await post("settings", body);
    if (!res.ok) { set.status.textContent = "Could not save"; return; }
    try { const saved = await res.json(); savedScale = saved.popupScale; applySize(savedScale); applyIconScale(saved.iconScale); savedGlass = saved.glassTint; applyGlass(savedGlass); } catch (e) {}
    setPinned(segValues.keepPip !== "false");
    toast("Saved");
    closeSettings();
  }

  $("gear").addEventListener("click", () => openSettings(false));
  $("errSettings").addEventListener("click", () => openSettings(false, state && state.connection === "api" ? "keys" : "agents"));
  $("setSave").addEventListener("click", saveSettings);
  $("setBack").addEventListener("click", closeSettings);
  $("setClose").addEventListener("click", () => settingsOnly ? closeWindow() : dismiss());

  // ---------- pin, collapse to an icon, drag (native popup only) ----------
  const mini = $("mini");
  // "Keep Pip on screen" (Settings → General): closing the conversation leaves Pip; Pip stays above other apps.
  function setPinned(value) {
    pinned = value;
    if (bridge) bridge.postMessage({ type: "pin", value: pinned });
  }

  const bubble = $("bubble");
  let lastBusy = false;

  function collapse() {
    // A dictation goes on with only Pip on screen (Pip shows it is listening); opening the chat shows the text.
    const dictating = !!window.__quickPromptSpeech && speech.state !== "idle";
    if (!collapsed && bridge && !collapse.now) {
      freezePip(() => { collapse.now = true; try { thawNext = true; collapse(); } finally { collapse.now = false; } });
      return;
    }
    collapsed = true;
    setTimeout(() => globe.start(), 0);
    bubble.hidden = true;
    $("presetList").hidden = true;
    if (bridge) bridge.postMessage({ type: "mode", value: "mini" });
    [els.card, els.bar, els.ctxWrap, set.view, $("historyView")].forEach(el => el.hidden = true);
    settingsOpen = false; historyOpen = false;
    els.root.classList.add("is-mini");
    mini.hidden = false;
    if (actionsOpen) { actionsOpen = false; act.view.hidden = true; }
    lastLayout = "";
    queueLayout();
    $("minibar").classList.remove("tucked");
    clearTimeout(window.__qpTuck);
    window.__qpTuck = setTimeout(() => { if (collapsed && $("presetList").hidden) tuckBar(); }, 1500);
    // Closing the chat starts over: the next open is a plain, empty conversation (no template, its AI or its
    // fields). It is in Recent Chats. A conversation still answering is kept, for Pip's bubble.
    if (!dictating && state && !state.busy && (state.messages.length || state.title || state.presetPrompt || state.provider !== "claude")) {
      stick = true;
      post("reset");
      els.prompt.value = ""; els.ctx.value = ""; showCtx(false); pending = []; renderAttachments(); autoGrow();
    }
  }

  // Before a change that resizes the panel: the native side puts a picture of Pip over the web view (its
  // old frames would make Pip jump sideways) and calls back; only then does the page change (next), and
  // once it is drawn at the new size the picture goes (thawPip). The picture is taken while the page is
  // still unchanged, so the change waits for it.
  let freezePending = false;
  function freezePip(next) {
    if (!bridge || mini.hidden) { next(); return; }
    if (freezePending) return;   // a change is already on its way
    freezePending = true;
    let done = false;
    const go = () => {
      if (done) return;
      done = true;
      freezePending = false;
      window.__quickPromptFrozen = null;
      next();
    };
    window.__quickPromptFrozen = go;
    setTimeout(go, 150);   // never wait longer than this
    const r = $("miniOpen").getBoundingClientRect();
    bridge.postMessage({ type: "freeze", icon: { x: r.left, y: r.top, w: r.width, h: r.height } });
  }
  function thawPip() { if (bridge) bridge.postMessage({ type: "thaw" }); }

  function expand() {
    if (collapsed && bridge && !expand.now) { freezePip(() => { expand.now = true; try { expand(); } finally { expand.now = false; } }); return; }
    collapsed = false;
    mini.hidden = !bridge;   // in the native popup the icon stays beside the conversation
    setTimeout(() => globe.start(), 0);
    bubble.hidden = true;
    $("presetList").hidden = true;
    if (bridge) bridge.postMessage({ type: "mode", value: "full" });
    els.root.classList.remove("is-mini");
    els.bar.hidden = false;
    lastKey = "";
    lastLayout = "";
    if (bridge) {
      // Slide out of the icon once the panel has its new size; until then the conversation stays hidden.
      revealNext = "in";
      $("main").classList.add("reveal-pending");
      clearTimeout(expand.safety);
      expand.safety = setTimeout(() => {
        if (!$("main").classList.contains("reveal-pending")) return;
        $("main").classList.remove("reveal-pending");
        revealNext = null; lastLayout = ""; queueLayout();
      }, 700);
    }
    if (state) render();
    queueLayout();
    setTimeout(() => els.prompt.focus(), 0);
  }

  // The panel resizes after the layout message; start the slide only once the page has its new width,
  // so nothing reflows mid-slide.
  function startRevealWhenSized(dir, width) {
    revealNext = null;
    whenSized(width, () => startReveal(dir));
  }
  // Runs fn once the page has the new width and has drawn two frames at it.
  function whenSized(width, fn) {
    const go = () => requestAnimationFrame(() => requestAnimationFrame(fn));
    if (Math.abs(window.innerWidth - width) < 2) { go(); return; }
    let started = false;
    const once = () => {
      if (started) return;
      started = true;
      window.removeEventListener("resize", once);
      go();
    };
    window.addEventListener("resize", once);
    setTimeout(once, 160);   // the panel may be clamped to the screen and never reach that width
  }

  // The slide: the page moves #main with CSS, the native panel moves the glass behind it the same way.
  const REVEAL_DX = 26, REVEAL_IN = 0.3, REVEAL_OUT = 0.22;
  function startReveal(dir) {
    revealNext = null;
    const main = $("main");
    const dx = els.root.classList.contains("side-right") ? REVEAL_DX : -REVEAL_DX;
    main.classList.remove("reveal-in", "reveal-out", "reveal-pending");
    void main.offsetWidth;
    main.classList.add(dir === "in" ? "reveal-in" : "reveal-out");
    if (dir === "in") thawPip();
    revealing = true;
    if (bridge) bridge.postMessage({ type: "reveal", dir, dx, duration: dir === "in" ? REVEAL_IN : REVEAL_OUT });
    clearTimeout(startReveal.t);
    startReveal.t = setTimeout(() => {
      revealing = false;
      if (dir === "in") main.classList.remove("reveal-in");
      if (layoutAfterReveal) { layoutAfterReveal = false; lastLayout = ""; queueLayout(); }
    }, (dir === "in" ? REVEAL_IN : REVEAL_OUT) * 1000 + 20);
  }

  /// Tucks the conversation back into the icon with the slide.
  function collapseAnimated() {
    if (collapsed) return;
    if (!bridge) { collapse(); return; }
    startReveal("out");
    setTimeout(() => { $("main").classList.remove("reveal-out"); collapse(); }, REVEAL_OUT * 1000 - 10);
  }

  /// Esc / ✕: with "Keep Pip on screen" the conversation tucks into Pip, otherwise the popup closes.
  function dismiss() {
    if (bridge && pinned) collapseAnimated(); else closeWindow();
  }


  bubble.addEventListener("click", expand);
  $("bubbleClose").addEventListener("click", e => {
    e.stopPropagation();
    bubble.hidden = true;
    lastLayout = "";
    queueLayout();
  });

  // Toolbar under the icon: tucks into a thin line after a few seconds; hover or click brings it back.
  const minibar = $("minibar");
  const presetList = $("presetList");
  let tuckTimer = null;
  function scheduleTuck() {
    clearTimeout(tuckTimer);
    tuckTimer = setTimeout(() => { if (collapsed && presetList.hidden) tuckBar(); }, 1500);
  }
  // Where the thin line sits for a toolbar rect (page pixels): under it, or over it when it opens above,
  // lined up like the toolbar.
  function barLine(r) {
    const z = parseFloat(getComputedStyle(mini).zoom) || 1, w = 34 * z, h = 6 * z;
    const x = mini.classList.contains("tb-start") ? r.left : mini.classList.contains("tb-end") ? r.right - w : r.left + (r.width - w) / 2;
    const y = mini.classList.contains("tb-above") ? r.bottom - h : r.top;
    return { x, y, w, h };
  }
  // The toolbar's glass shrinks into the line while its buttons fade, then the panel gets its new size.
  function tuckBar() {
    clearTimeout(tuckBar.t);
    if (minibar.classList.contains("tucked") || minibar.classList.contains("closing")) return;
    if (!bridge) { minibar.classList.add("tucked"); queueLayout(); return; }
    minibar.classList.remove("opening");
    minibar.classList.add("closing");
    bridge.postMessage({ type: "barOut", to: barLine(minibar.getBoundingClientRect()), duration: 0.16 });
    tuckBar.t = setTimeout(() => {
      minibar.classList.remove("closing");
      minibar.classList.add("tucked");
      lastLayout = ""; queueLayout();
    }, 170);
  }
  function untuck() {
    clearTimeout(tuckTimer);
    clearTimeout(window.__qpTuck);   // the tuck after collapsing must not fire under the mouse either
    if (minibar.classList.contains("closing")) {
      // Back while it was tucking: open again where it is.
      clearTimeout(tuckBar.t);
      minibar.classList.remove("closing");
      barOpening = true; lastLayout = ""; queueLayout();
      return;
    }
    if (!minibar.classList.contains("tucked")) return;
    minibar.classList.remove("tucked");
    minibar.classList.remove("opening"); void minibar.offsetWidth; minibar.classList.add("opening");
    clearTimeout(untuck.t);
    untuck.t = setTimeout(() => minibar.classList.remove("opening"), 320);
    barOpening = true;
    queueLayout();
  }
  mini.addEventListener("mouseenter", untuck);
  mini.addEventListener("mouseleave", scheduleTuck);
  minibar.addEventListener("click", e => {
    if (minibar.classList.contains("tucked")) { e.stopPropagation(); untuck(); scheduleTuck(); }
  }, true);

  $("miniRecent").addEventListener("click", () => { expand(); openHistory(false); });

  async function togglePresets() {
    // The list resizes the panel: Pip's picture covers the web view meanwhile (see freezePip).
    if (!presetList.hidden) { freezePip(() => { thawNext = true; presetList.hidden = true; lastLayout = ""; queueLayout(); scheduleTuck(); }); return; }
    bubble.hidden = true;
    let items = [];
    try {
      const res = await fetch(base + "presets", { cache: "no-store" });
      if (res.status === 404) { sessionEnded(); return; }
      items = await res.json();
    } catch (e) {}
    $("plistItems").innerHTML = items.length ? items.map(p =>
      '<button type="button" class="pitem" data-id="' + esc(p.id) + '" data-provider="' + esc(p.provider) + '">'
      + '<span class="dot"' + (p.color ? ' style="background:' + esc(p.color) + '"' : '') + '></span><span class="pname">' + esc(p.title) + '</span>'
      + '<span class="pmeta">' + esc(p.providerName) + '</span></button>').join("")
      : '<div class="hempty">No templates yet.</div>';
    freezePip(() => {
      thawNext = true;
      presetList.hidden = false;
      clearTimeout(tuckTimer);
      lastLayout = ""; queueLayout();
    });
  }
  $("miniActions").addEventListener("click", togglePresets);
  $("plistEdit").addEventListener("click", () => { presetList.hidden = true; expand(); openActions(false); });
  $("plistItems").addEventListener("click", async e => {
    const item = e.target.closest(".pitem");
    if (!item) return;
    const res = await post("usepreset", { id: item.dataset.id });
    if (res.ok) { runTemplateView(); } else { toast("Still answering"); }
  });
  // A template picked from Pip: the popup opens on it (its fields and the question bar), not on the Settings,
  // template list or history view that was open last time.
  function runTemplateView() {
    if (actionsOpen) closeActions();
    if (settingsOpen) closeSettings();
    if (historyOpen) closeHistory();
    stick = true;
    expand();
  }

  // Right click on the icon: native menu (Open, Settings, Close).
  // Right click (on the icon or anywhere that is not text): the native menu with everything that used
  // to sit in the popup's top row.
  // The templates submenu, as in the menu bar menu: menu ids "tpl-<n>" map to template ids.
  let menuTemplates = [], menuAgents = [];
  function menuItems() {
    return [
      ...(collapsed ? [{ id: "open", title: "Open" }] : []),
      ...(menuAgents.length > 1
        ? [{ title: "New Chat", items: menuAgents.map(a => ({ id: "newagent-" + a.id, title: a.name + (a.isDefault ? " (default)" : "") })) }]
        : [{ id: "new", title: "New Chat" }]),
      ...(speech.available ? [{ id: "converse", title: convo.on ? "End Conversation" : "Start Conversation" }] : []),
      { id: "recent", title: "Recent Chats" },
      { title: "Prompt Templates", items: [
        ...menuTemplates.map((p, i) => ({ id: "tpl-" + i, title: p.title })),
        ...(menuTemplates.length ? [{ separator: true }] : []),
        { id: "actions", title: "Edit Templates…" },
      ] },
      { separator: true },
      { id: "settings", title: "Settings…" },
      { separator: true },
      { id: "hide", title: "Hide Pip" },
      { id: "never", title: "Never Show Pip" },
    ];
  }
  document.addEventListener("contextmenu", async e => {
    if (!bridge) return;
    if (e.target.closest("textarea, input, select")) return;            // keep the text field's own menu
    const sel = window.getSelection();
    if (sel && !sel.isCollapsed && e.target.closest(".thread")) return;  // and Copy for selected answer text
    e.preventDefault();
    try {
      const res = await fetch(base + "presets", { cache: "no-store" });
      if (res.status === 404) { sessionEnded(); return; }
      menuTemplates = await res.json();
      const d = await fetchAgents();
      menuAgents = d ? d.agents : [];
    } catch (err) {}
    bridge.postMessage({ type: "menu", items: menuItems() });
  });
  window.__quickPromptMenu = async id => {
    if (id.startsWith("add-")) { runAddItem(id); return; }
    if (id.startsWith("agent-")) { switchAgent(id.slice(6)); return; }
    if (id === "agents-edit") { openSettings(false, "agents"); return; }
    if (id.startsWith("newagent-")) {
      const res = await post("reset", { agent: id.slice(9) });
      if (res.ok) { stick = true; expand(); } else if (res.status !== 404) toast("Still answering");
      return;
    }
    if (id.startsWith("tpl-")) {
      const preset = menuTemplates[+id.slice(4)];
      if (!preset) return;
      const res = await post("usepreset", { id: preset.id });
      if (res.ok) runTemplateView(); else if (res.status !== 404) toast("Still answering");
      return;
    }
    if (id === "converse") { convo.on ? endConversation() : startConversation(); return; }
    if (id === "open") expand();
    else if (id === "new") { const res = await post("reset"); if (res.ok) { stick = true; expand(); } else toast("Still answering"); }
    else if (id === "recent") { expand(); openHistory(false); }
    else if (id === "settings") { expand(); openSettings(false); }
    else if (id === "actions") { expand(); openActions(false); }
    else if (id === "hide") closeWindow();
    else if (id === "never") {
      // Turns "Keep Pip on screen" off; the menu bar's Show Pip turns it back on.
      try { await post("settings", { keepPip: "false" }); } catch (e) {}
      closeWindow();
    }
  };
  // Which side of the conversation the icon sits on (the native panel decides from the screen position).
  window.__quickPromptSide = side => {
    els.root.classList.toggle("side-right", side === "right");
    lastLayout = "";
    queueLayout();
  };
  if (nativeInfo.side) els.root.classList.toggle("side-right", nativeInfo.side === "right");

  // Room around Pip's middle (screen points, from the native panel): where the toolbar goes.
  // Pip on its own: the native panel reports the pointer (page points), since the web view gets no mouse moves.
  let hovered = null;
  window.__quickPromptPointer = (x, y) => {
    const el = x == null ? null : document.elementFromPoint(x, y);
    const target = el && el.closest(".minibar:not(.tucked) button, .pitem");
    if (target === hovered) return;
    if (hovered) hovered.classList.remove("hov");
    hovered = target;
    if (hovered) hovered.classList.add("hov");
  };
  let toolbarSize = null, pipEdges = null;
  window.__quickPromptEdges = edges => {
    pipEdges = edges;
    const bar = $("minibar"), icon = $("miniOpen").getBoundingClientRect();
    if (bar.offsetWidth) toolbarSize = { w: bar.getBoundingClientRect().width, h: bar.getBoundingClientRect().height };
    const zoom = parseFloat(getComputedStyle(mini).zoom) || 1;
    const size = toolbarSize || { w: 124 * zoom, h: 38 * zoom };
    const half = size.w / 2 + 6;
    const start = edges.left < half, end = !start && edges.right < half;
    const above = edges.bottom < icon.height / 2 + 6 * zoom + size.h + 6;
    const changed = mini.classList.contains("tb-start") !== start || mini.classList.contains("tb-end") !== end
      || mini.classList.contains("tb-above") !== above;
    mini.classList.toggle("tb-start", start);
    mini.classList.toggle("tb-end", end);
    mini.classList.toggle("tb-above", above);
    els.root.classList.toggle("pip-bottom", above);
    if (changed) { lastLayout = ""; queueLayout(); }
  };
  if (bridge) { mini.hidden = false; setTimeout(() => globe.start(), 0); }
  $("miniNew").addEventListener("click", async () => {
    const res = await post("reset");
    if (res.ok) { stick = true; expand(); } else { toast("Still answering"); }
  });

  /// While collapsed: pulse while answering, then show a bubble with the start of the answer.
  // ---------- the floating icon: loose thoughts that become one idea ----------
  // Asleep, each ray drifts in its own quarter between the star's arms, faint links form between rays that
  // come close and small signals run along them (the ray they reach swells for a moment); the star stays
  // in the middle, a little smaller, breathing. On hover (or while answering) the rays settle into the
  // logo and the star flashes once; while the mouse stays, the rays bounce like springs every 1-2 s.
  const globe = (() => {
    const button = $("miniOpen");
    const NS = "http://www.w3.org/2000/svg";
    const reduce = window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    const tune = { gather: 0.9, drift: 2, link: 0.45, star: 0.86, signals: 1, spring: 0.35 };
    const rays = [...button.querySelectorAll(".piece")].map(el => ({
      el, path: el.querySelector("path"), cx: +el.dataset.cx, cy: +el.dataset.cy,
      x: 0, y: 0, r: 0, vx: 0, vy: 0, vr: 0, glow: 0, ex: 0, ey: 0,
    }));
    const core = button.querySelector(".core");
    const linkGroup = button.querySelector(".links"), signalGroup = button.querySelector(".signals");
    const pairs = [];
    for (let i = 0; i < rays.length; i++) {
      for (let j = i + 1; j < rays.length; j++) pairs.push([rays[i], rays[j]]);
      pairs.push([rays[i], null]);   // to the star
    }
    const lines = pairs.map(() => { const l = document.createElementNS(NS, "line"); l.setAttribute("stroke-width", ".16"); linkGroup.appendChild(l); return l; });
    const signals = [];
    let hover = false, busy = false, awake = false, wake = 0, starScale = tune.star;
    let springT = -1, sprung = false, springWait = 0, flashT = -1, flashed = false, nextSignal = 0.4;
    let running = false, last = 0, t = 0, sleepTimer = 0;

    for (const p of rays) {
      const a = Math.random() * Math.PI * 2;
      p.x = Math.cos(a) * 1.2; p.y = Math.sin(a) * 1.2; p.r = (Math.random() - .5) * 120;
      p.vx = (Math.random() - .5) * 1.4; p.vy = (Math.random() - .5) * 1.4; p.vr = (Math.random() - .5) * 40;
    }

    function spawnSignal(strength) {
      const candidates = pairs.filter((_, i) => strength[i] > 0.25);
      if (!candidates.length) return;
      const pick = candidates[Math.floor(Math.random() * candidates.length)];
      const forward = Math.random() < 0.5 || !pick[1];
      const halo = document.createElementNS(NS, "circle"), dot = document.createElementNS(NS, "circle");
      halo.setAttribute("r", ".8"); halo.setAttribute("fill", "#dfe7ff");
      dot.setAttribute("r", ".32"); dot.setAttribute("fill", "#f2f5ff");
      signalGroup.append(halo, dot);
      signals.push({ from: forward ? pick[0] : pick[1], to: forward ? pick[1] : pick[0], t: 0, dur: 0.45 + Math.random() * 0.35, dot, halo });
    }

    // In its quarter (u, v = distances from the two axes) a ray stays outside the star's edge, which runs
    // from an arm tip (0, 7.4) to the inner corner (1.85, 1.85): u >= (7.4 - v) / 3 and v >= (7.4 - u) / 3.
    function keepInQuarter(p) {
      const sx = Math.sign(p.cx - 12), sy = Math.sign(p.cy - 12), m = 1.05;
      let u = (p.cx + p.x - 12) * sx, v = (p.cy + p.y - 12) * sy, vu = p.vx * sx, vv = p.vy * sy;
      const tip = 7.4 * starScale;
      const minU = Math.max(m, (tip - v) / 3 + m), minV = Math.max(m, (tip - u) / 3 + m);
      if (u < minU) { u = minU; if (vu < 0) vu = -vu; }
      if (v < minV) { v = minV; if (vv < 0) vv = -vv; }
      const room = 8.6, d = Math.hypot(u, v);
      if (d > room) { u *= room / d; v *= room / d; const dot = (vu * u + vv * v) / room; if (dot > 0) { vu -= 2 * dot * u / room; vv -= 2 * dot * v / room; } }
      p.x = u * sx + 12 - p.cx; p.y = v * sy + 12 - p.cy; p.vx = vu * sx; p.vy = vv * sy;
    }

    function setAwake(value) {
      if (value === awake) return;
      awake = value;
      if (!awake) for (const p of rays) { const a = Math.random() * Math.PI * 2; p.vx = Math.cos(a) * .9; p.vy = Math.sin(a) * .9; p.vr = (Math.random() - .5) * 40; }
      start();
    }
    function update() {
      clearTimeout(sleepTimer);
      if (hover || busy) setAwake(true);
      else sleepTimer = setTimeout(() => setAwake(false), 500);
    }

    function frame(now) {
      if (mini.hidden || document.hidden) { running = false; return; }
      requestAnimationFrame(frame);
      if (last && now - last < 30) return;          // ~30 fps is plenty for this and saves energy
      const dt = Math.min(0.06, (now - (last || now)) / 1000);
      last = now; t += dt;
      const settled = awake || reduce;
      wake += ((settled ? 1 : 0) - wake) * Math.min(1, dt * (3.2 / tune.gather));
      const k = Math.min(1, dt * (3.6 / tune.gather));
      for (const p of rays) {
        if (settled) { p.x -= p.x * k; p.y -= p.y * k; p.r -= p.r * k; continue; }
        const s = tune.drift;
        p.vx += (Math.random() - .5) * dt * 1.6 * s; p.vy += (Math.random() - .5) * dt * 1.6 * s;
        const speed = Math.hypot(p.vx, p.vy), max = 1.1 * s;
        if (speed > max) { p.vx *= max / speed; p.vy *= max / speed; }
        p.x += p.vx * dt * s; p.y += p.vy * dt * s; p.r += p.vr * dt * s;
        keepInQuarter(p);
      }

      // The spring bounce: first when the rays arrive, then every 1-2 s while the mouse stays on it.
      if (!awake || !hover || reduce) { springT = -1; sprung = false; }
      else if (springT < 0 && !sprung && wake > 0.9) { springT = 0; sprung = true; }
      if (springT >= 0) { springT += dt; if (springT > 1.1) { springT = -1; springWait = 1 + Math.random(); } }
      else if (sprung) { springWait -= dt; if (springWait <= 0) springT = 0; }

      rays.forEach((p, i) => {
        let jx = 0, jy = 0, jr = 0;
        if (springT >= 0) {
          const tt = Math.max(0, springT - i * 0.05);
          const a = tune.spring * Math.exp(-tt * 3.2) * Math.sin(tt * Math.PI * 2 * 5.5);
          jx = Math.sign(p.cx - 12) / Math.SQRT2 * a; jy = Math.sign(p.cy - 12) / Math.SQRT2 * a; jr = a * 10;
        }
        p.ex = p.cx + p.x + jx; p.ey = p.cy + p.y + jy;
        p.el.setAttribute("transform", `translate(${(p.x + jx).toFixed(3)} ${(p.y + jy).toFixed(3)}) rotate(${(p.r + jr).toFixed(2)} ${p.cx} ${p.cy})`);
        p.glow = Math.max(0, p.glow - dt * 2.2);
        p.path.setAttribute("stroke-width", (1.15 + p.glow * 0.45).toFixed(3));
      });

      // The star never turns or moves: a little smaller and breathing while asleep, one flash on waking.
      if (!awake) flashed = false;
      if (awake && !flashed && wake > 0.85) { flashed = true; flashT = 0; }
      let bump = 0;
      if (flashT >= 0) { flashT += dt * 2.5; bump = flashT < 1 ? Math.sin(flashT * Math.PI) * 0.08 : 0; if (flashT >= 1) flashT = -1; }
      const sleepScale = tune.star + Math.sin(t * 1.6) * 0.015;
      starScale = sleepScale + (1 - sleepScale) * wake + bump;
      core.setAttribute("transform", `translate(12 12) scale(${starScale.toFixed(4)}) translate(-12 -12)`);

      // Links: faint, only between rays that are close, gone once the logo is together.
      const fade = tune.link * (1 - wake), strength = [];
      pairs.forEach(([a, b], i) => {
        const x2 = b ? b.ex : 12, y2 = b ? b.ey : 12;
        const near = Math.max(0, Math.min(1, 1.6 - Math.hypot(a.ex - x2, a.ey - y2) / 7));
        strength[i] = near;
        const lit = signals.some(sg => (sg.from === a && sg.to === b) || (sg.from === b && sg.to === a)) ? 1.8 : 1;
        const l = lines[i];
        l.setAttribute("x1", a.ex.toFixed(2)); l.setAttribute("y1", a.ey.toFixed(2));
        l.setAttribute("x2", x2.toFixed(2)); l.setAttribute("y2", y2.toFixed(2));
        l.setAttribute("stroke-opacity", Math.min(1, fade * near * (b ? 1 : .6) * lit).toFixed(3));
      });

      // Signals run along the links while asleep; the ray they reach swells for a moment.
      if (!awake && !reduce) {
        nextSignal -= dt * tune.signals;
        if (nextSignal <= 0) { spawnSignal(strength); nextSignal = 0.35 + Math.random() * 0.7; }
      }
      for (let i = signals.length - 1; i >= 0; i--) {
        const sg = signals[i];
        sg.t += dt / sg.dur;
        const fx = sg.from ? sg.from.ex : 12, fy = sg.from ? sg.from.ey : 12, tx = sg.to ? sg.to.ex : 12, ty = sg.to ? sg.to.ey : 12;
        const q = Math.min(1, sg.t), e = q * q * (3 - 2 * q), o = (1 - wake) * Math.sin(q * Math.PI);
        for (const c of [sg.dot, sg.halo]) { c.setAttribute("cx", (fx + (tx - fx) * e).toFixed(2)); c.setAttribute("cy", (fy + (ty - fy) * e).toFixed(2)); }
        sg.dot.setAttribute("opacity", o.toFixed(3)); sg.halo.setAttribute("opacity", (o * .25).toFixed(3));
        if (sg.t >= 1) { if (sg.to) sg.to.glow = 1; sg.dot.remove(); sg.halo.remove(); signals.splice(i, 1); }
      }
    }
    function start() {
      if (running || mini.hidden) return;
      running = true; last = 0;
      requestAnimationFrame(frame);
    }
    button.addEventListener("mouseenter", () => { hover = true; update(); });
    button.addEventListener("mouseleave", () => { hover = false; update(); });
    button.addEventListener("focus", () => { hover = true; update(); });
    button.addEventListener("blur", () => { hover = false; update(); });
    document.addEventListener("visibilitychange", start);
    return {
      setBusy(value) { busy = value; update(); },
      setHover(value) { hover = value; update(); },
      start,
    };
  })();

  // The native panel reports hover itself (works while another app is in front).
  window.__quickPromptHover = inside => {
    if (!collapsed) return;
    globe.setHover(inside);
    els.root.classList.toggle("hovering", inside);   // shows the bubble's ✕ while another app is in front
    if (inside) untuck(); else scheduleTuck();
  };

  function updateMini(s) {
    globe.setBusy(!!s.busy);
    $("miniOpen").classList.toggle("busy", !!s.busy);
    if (lastBusy && !s.busy) {
      const last = s.messages[s.messages.length - 1];
      if (last && last.role === "assistant") {
        const failed = last.state === "error";
        $("bubbleIcon").textContent = failed ? "!" : "✓";
        $("bubbleIcon").classList.toggle("err", failed);
        $("bubbleTitle").textContent = (s.title || s.providerName) + (failed ? " — failed" : "");
        $("bubbleText").textContent = last.text.replace(/\s+/g, " ").slice(0, 220);
        bubble.hidden = false;
        lastLayout = "";
        queueLayout();
      }
    }
    lastBusy = !!s.busy;
  }

  // Drag: mouse down on a [data-drag] area (not on its controls) lets the panel follow the mouse.
  let downAt = null;
  document.addEventListener("mousedown", e => {
    downAt = { x: e.screenX, y: e.screenY };
    if (!bridge || e.button !== 0) return;
    const onMini = e.target.closest("#miniOpen");
    if (onMini && e.altKey) return;   // ⌥-click on Pip moves it (see below), no drag
    const area = e.target.closest("[data-drag]");
    if (!onMini && (!area || e.target.closest("button, textarea, input, select, a, .seg"))) return;
    if (!onMini) e.preventDefault();
    bridge.postMessage({ type: "drag" });
  });
  // Middle click (the wheel) or ⌥-click (for a trackpad) on Pip: it glides to the nearest screen edge,
  // and from an edge on to the next corner clockwise.
  $("miniOpen").addEventListener("mousedown", e => {
    if (e.button !== 1 || !bridge) return;
    e.preventDefault();
    bridge.postMessage({ type: "snap" });
  });
  $("miniOpen").addEventListener("auxclick", e => { if (e.button === 1) e.preventDefault(); });

  // A click on the icon opens it again; a drag (the icon moved under the mouse) does not.
  $("miniOpen").addEventListener("click", e => {
    if (e.altKey && bridge) { bridge.postMessage({ type: "snap" }); return; }
    if (downAt && Math.abs(e.screenX - downAt.x) + Math.abs(e.screenY - downAt.y) > 4) return;
    // In a conversation a click on Pip is "done": it sends what was said, or cuts in while Pip speaks.
    if (convo.on && collapsed && (speech.state === "listening" || reader.index >= 0)) { speech.state === "listening" ? sendTurn() : bargeIn(); return; }
    if (collapsed) expand(); else collapseAnimated();   // the icon beside the conversation tucks it away
  });

  // ---------- history ----------
  const hist = { view: $("historyView"), list: $("hlist") };
  let historyOpen = false, historyOnly = false;

  function ago(iso) {
    const s = Math.max(0, (Date.now() - new Date(iso).getTime()) / 1000);
    if (s < 60) return "just now";
    if (s < 3600) return Math.floor(s / 60) + " min ago";
    if (s < 86400) return Math.floor(s / 3600) + " h ago";
    if (s < 7 * 86400) return Math.floor(s / 86400) + " d ago";
    return new Date(iso).toLocaleDateString();
  }

  function renderHistory(data) {
    if (!data.enabled) {
      hist.list.innerHTML = '<div class="hempty">History is off. Turn it on in Settings (⚙).</div>';
    } else if (!data.items.length) {
      hist.list.innerHTML = '<div class="hempty">No conversations yet.</div>';
    } else {
      hist.list.innerHTML = data.items.map(it =>
        '<button type="button" class="hitem" data-id="' + esc(it.id) + '" data-provider="' + esc(it.provider) + '">'
        + '<span class="dot"></span><span class="hbody">'
        + '<div class="hq">' + esc(it.title ? it.title + " · " + it.question : it.question) + '</div>'
        + '<div class="ha">' + esc(it.answer) + '</div>'
        + '<div class="hmeta">' + esc(it.providerName) + (it.connection === "api" ? " · API" : "") + " · " + ago(it.updated)
        + (it.turns > 1 ? " · " + it.turns + " messages" : "") + '</div>'
        + '</span></button>').join("");
    }
    $("histClear").hidden = !data.enabled || !data.items.length;
    queueLayout();
  }

  async function openHistory(only) {
    historyOnly = !!only;
    historyOpen = true;
    els.card.hidden = true; els.bar.hidden = true; els.ctxWrap.hidden = true; set.view.hidden = true;
    hist.view.hidden = false;
    hist.list.innerHTML = '<div class="hempty">Loading…</div>';
    try {
      renderHistory(await (await fetch(base + "history", { cache: "no-store" })).json());
    } catch (e) {
      hist.list.innerHTML = '<div class="hempty">Could not load history.</div>';
    }
    queueLayout();
  }

  // ---------- Prompt templates editor ----------
  const act = { view: $("actionsView"), list: $("actList"), form: $("actForm"), buttons: $("actButtons"), items: [], editing: null };
  const INPUT_NAMES = { selection: "Uses the selection", clipboard: "Uses the clipboard", popup: "Typed in the popup", none: "No text" };
  function renderActions() {
    const exporting = !!act.exporting;
    act.list.innerHTML = act.items.length ? act.items.map(a =>
      '<div class="hitem aitem' + (a.enabled === false ? ' disabled' : '') + '" data-id="' + esc(a.id) + '" data-provider="' + esc(a.provider) + '" role="button" tabindex="0">'
      + (exporting ? '<input type="checkbox" class="pick" data-pick="' + esc(a.id) + '"' + (act.picked.has(a.id) ? ' checked' : '') + ' aria-label="Export ' + esc(a.title) + '">' : '')
      + '<span class="dot" style="background:' + esc(a.agentColor || "") + '"></span><div class="hbody"><div class="hq">' + esc(a.title) + '</div>'
      + '<div class="ha">' + esc(a.instruction.replace(/\s+/g, " ")) + '</div>'
      + '<div class="hmeta">' + esc(a.agentName || "") + (a.agent ? '' : ' (default)') + ' · ' + INPUT_NAMES[a.input]
      + (a.fields ? ' · ' + a.fields + (a.fields === 1 ? ' field' : ' fields') : '')
      + (a.builtIn ? ' · built-in' : '') + '</div></div>'
      + (exporting ? '' : '<button type="button" class="tgl' + (a.enabled === false ? '' : ' on') + '" data-toggle="' + esc(a.id) + '" role="switch" aria-checked="' + (a.enabled !== false) + '" title="' + (a.enabled === false ? 'Off: not offered in Pip or the menu bar' : 'On') + '"></button>'
        + '<button type="button" class="run" data-run="' + esc(a.id) + '">Run</button>')
      + '</div>').join("")
      : '<div class="hempty">No templates yet.</div>';
    $("exportBar").hidden = !exporting;
    act.buttons.hidden = exporting || !act.form.hidden;
    if (exporting) {
      const all = $("exportAll");
      all.checked = act.picked.size > 0 && act.picked.size === act.items.length;
      all.indeterminate = act.picked.size > 0 && act.picked.size < act.items.length;
      $("exportCount").textContent = act.picked.size ? act.picked.size + " selected" : "Choose templates";
      $("exportGo").disabled = act.picked.size === 0;
    }
    queueLayout();
  }
  async function openActions(only) {
    actionsOnly = !!only;
    actionsOpen = true;
    els.card.hidden = true; els.bar.hidden = true; els.ctxWrap.hidden = true; $("fieldsBox").hidden = true;
    set.view.hidden = true; hist.view.hidden = true; settingsOpen = false; historyOpen = false;
    act.view.hidden = false;
    mountActions(act.view);
    showActionList();
    await loadActions();
  }
  // The list and editor live in the Prompt templates view or inside Settings → Prompt templates.
  function mountActions(target) {
    if (act.list.parentElement !== target) target.append(act.list, act.buttons, $("exportBar"), act.form);
  }
  async function loadActions() {
    act.list.innerHTML = '<div class="hempty">Loading…</div>';
    try { act.items = await (await fetch(base + "actions", { cache: "no-store" })).json(); } catch (e) { act.items = []; }
    renderActions();
  }
  function closeActions() {
    if (actionsOnly) { closeWindow(); return; }
    actionsOpen = false;
    act.view.hidden = true; els.bar.hidden = false;
    lastKey = ""; fieldsKey = "";
    if (state) render();
    queueLayout();
    els.prompt.focus();
  }
  function showActionList() {
    act.form.hidden = true; act.list.hidden = false; act.buttons.hidden = false;
    $("actTitle").textContent = "Prompt templates";
    queueLayout();
  }
  function editAction(a) {
    act.editing = a || null;
    $("actTitle").textContent = a ? (a.builtIn ? "Edit " + a.title + " (saved as your copy)" : "Edit template") : "New template";
    $("afTitle").value = a ? a.title : "";
    $("afPrompt").value = a ? a.instruction : "";
    fillAgentPicker(a ? a.agent || "" : "");
    setSeg("afAutoCopy", a && a.autoCopy ? "true" : "false");
    $("afDelete").hidden = !a || a.builtIn;
    act.list.hidden = true; act.buttons.hidden = true; act.form.hidden = false;
    queueLayout();
    setTimeout(() => (a ? $("afPrompt") : $("afTitle")).focus(), 0);
  }
  // The template's agent: "" = the default agent (whichever it is when the template runs).
  async function fillAgentPicker(value) {
    const sel = $("afAgent");
    const d = await fetchAgents();
    const list = d ? d.agents : [];
    const main = list.find(a => a.isDefault);
    sel.innerHTML = '<option value="">Default agent' + (main ? ' (' + esc(main.name) + ')' : '') + '</option>'
      + list.map(a => '<option value="' + esc(a.id) + '">' + esc(a.name) + '</option>').join("");
    sel.value = list.some(a => a.id === value) ? value : "";
  }

  async function runAction(id) {
    const res = await post("usepreset", { id });
    if (!res.ok) { toast("Still answering"); return; }
    if (settingsOpen) { settingsOnly = false; closeSettings(); return; }
    actionsOnly = false;
    closeActions();
  }
  act.list.addEventListener("click", async e => {
    if (act.exporting) {
      // Choosing what to export: a click anywhere on the row ticks it.
      const row = e.target.closest(".aitem");
      if (!row) return;
      const id = row.dataset.id;
      if (act.picked.has(id)) act.picked.delete(id); else act.picked.add(id);
      renderActions();
      return;
    }
    const toggle = e.target.closest("[data-toggle]");
    if (toggle) {
      const on = !toggle.classList.contains("on");
      const res = await post("toggleaction", { id: toggle.dataset.toggle, enabled: on ? "true" : "false" });
      if (res.ok) { act.items = await res.json(); renderActions(); }
      return;
    }
    const run = e.target.closest("[data-run]");
    if (run) { runAction(run.dataset.run); return; }
    const item = e.target.closest(".aitem");
    if (item) editAction(act.items.find(a => a.id === item.dataset.id));
  });

  // ---------- export and import ----------
  act.picked = new Set();
  $("actExport").addEventListener("click", () => { act.exporting = true; act.picked = new Set(act.items.map(a => a.id)); renderActions(); });
  $("exportCancel").addEventListener("click", () => { act.exporting = false; renderActions(); });
  $("exportAll").addEventListener("change", e => {
    act.picked = e.target.checked ? new Set(act.items.map(a => a.id)) : new Set();
    renderActions();
  });
  $("exportGo").addEventListener("click", async () => {
    const res = await post("exportactions", { ids: [...act.picked].join(",") });
    if (!res.ok) { toast("Could not export"); return; }
    const content = await res.text();
    const name = act.picked.size === 1 ? (act.items.find(a => act.picked.has(a.id)) || {}).title + ".json" : "Quick Prompt templates.json";
    if (bridge) {
      bridge.postMessage({ type: "saveFile", name: name.replace(/[\\/:]/g, "-"), content });   // the native Save dialog
    } else {
      const a = document.createElement("a");
      a.href = URL.createObjectURL(new Blob([content], { type: "application/json" }));
      a.download = name;
      a.click();
      setTimeout(() => URL.revokeObjectURL(a.href), 1000);
    }
    act.exporting = false;
    renderActions();
  });
  window.__quickPromptSaved = ok => toast(ok ? "Exported" : "Not exported");
  $("actImport").addEventListener("click", () => $("importFile").click());
  $("importFile").addEventListener("change", async e => {
    const file = e.target.files && e.target.files[0];
    e.target.value = "";
    if (!file) return;
    const res = await post("importactions", { json: await file.text() });
    const data = res.ok ? await res.json() : { error: "Could not import" };
    if (data.error) { toast(data.error); return; }
    act.items = data.actions;
    renderActions();
    toast("Imported " + data.added + (data.skipped ? " · " + data.skipped + " already there" : ""));
  });
  act.list.addEventListener("keydown", e => {
    const item = e.target.closest(".aitem");
    if (item && e.key === "Enter" && e.target === item) editAction(act.items.find(a => a.id === item.dataset.id));
  });
  $("actNew").addEventListener("click", () => editAction(null));
  $("actBack").addEventListener("click", closeActions);
  $("actClose").addEventListener("click", () => actionsOnly ? closeWindow() : dismiss());
  $("afCancel").addEventListener("click", showActionList);
  $("afSave").addEventListener("click", async () => {
    const body = {
      id: act.editing ? act.editing.id : "",
      enabled: act.editing && act.editing.enabled === false ? "false" : "true",
      title: $("afTitle").value.trim(), instruction: $("afPrompt").value,
      agent: $("afAgent").value,
      autoCopy: segValues.afAutoCopy === "true" ? "true" : "false",
    };
    if (!body.title) { $("afTitle").focus(); toast("Give it a title"); return; }
    if (!body.instruction.trim()) { $("afPrompt").focus(); toast("Write the prompt"); return; }
    const res = await post("saveaction", body);
    if (!res.ok) { toast("Could not save"); return; }
    act.items = await res.json();
    renderActions();
    showActionList();
    toast("Saved");
  });
  $("afDelete").addEventListener("click", async () => {
    if (!act.editing) return;
    const res = await post("deleteaction", { id: act.editing.id });
    if (res.ok) { act.items = await res.json(); renderActions(); showActionList(); toast("Deleted"); }
  });


  function closeHistory() {
    if (historyOnly) { closeWindow(); return; }
    historyOpen = false;
    hist.view.hidden = true; els.bar.hidden = false;
    lastKey = "";
    if (state) render();
    queueLayout();
    els.prompt.focus();
  }

  hist.list.addEventListener("click", async e => {
    const item = e.target.closest(".hitem");
    if (!item) return;
    const res = await post("resume", { id: item.dataset.id });
    if (!res.ok) { toast("Could not open it"); return; }
    historyOnly = false;
    stick = true;
    closeHistory();
  });
  $("barHistory").addEventListener("click", () => openHistory(false));
  $("metaHistory").addEventListener("click", () => openHistory(false));
  $("histBack").addEventListener("click", closeHistory);
  $("histClose").addEventListener("click", () => historyOnly ? closeWindow() : dismiss());
  $("histClear").addEventListener("click", async () => {
    renderHistory(await (await post("clearhistory")).json());
    toast("History cleared");
  });

  // ---------- rendering ----------
  function render() {
    const s = state;
    document.documentElement.dataset.provider = s.provider;
    const agentName = s.agent && s.agent.name ? s.agent.name : s.providerName;
    els.who.textContent = agentName;
    renderAgentChip(s);
    // Apply sizes from the server only when they change there (Save applies its own values right away).
    if (!sizeKnown || (s.popupScale !== lastStateScale && !settingsOpen)) applySize(s.popupScale);
    if (s.iconScale !== lastStateIcon) applyIconScale(s.iconScale);
    if (s.glassTint !== lastStateGlass && !settingsOpen) { applyGlass(s.glassTint); savedGlass = s.glassTint; }
    lastStateGlass = s.glassTint;
    // A template with "Copy the first answer" put it on the clipboard.
    if (lastAutoCopied != null && (s.autoCopied || 0) > lastAutoCopied) toast("Answer copied");
    lastAutoCopied = s.autoCopied || 0;
    lastStateScale = s.popupScale; lastStateIcon = s.iconScale;
    if (!!s.keepPip !== pinned) setPinned(!!s.keepPip);
    if (s.startCollapsed && !startedAsPip && bridge) {
      // Shown as Pip on its own (at start / Show Pip): go to Pip's usual place and tuck in.
      startedAsPip = true; draftApplied = true;
      bridge.postMessage({ type: "home" });
      collapse();
      updateMini(s);
      // The dictation shortcut with no popup open: Pip appears and listens.
      if (s.startDictation && !dictationAsked) { dictationAsked = true; setTimeout(() => window.__quickPromptDictate(), 0); }
      if (s.startConversation && !conversationAsked) { conversationAsked = true; setTimeout(() => window.__quickPromptConverse(), 0); }
      return;
    }
    if (collapsed) { updateMini(s); return; }
    lastBusy = !!s.busy;
    if (s.showSettings && !settingsOpen && !draftApplied) { draftApplied = true; openSettings(true); return; }
    if (s.showHistory && !historyOpen && !draftApplied) { draftApplied = true; openHistory(true); return; }
    if (s.showActions && !actionsOpen && !draftApplied) { draftApplied = true; openActions(true); return; }
    if (settingsOpen || historyOpen || actionsOpen) return;
    els.title.textContent = s.title || "";
    document.title = (s.title ? s.title + " — " : "") + s.providerName;
    els.status.textContent = "";
    const dictatingNow = !!window.__quickPromptSpeech && speech.state !== "idle";
    if (!dictatingNow) {
      els.send.innerHTML = s.busy ? ICON_STOP : ICON_SEND;
      els.send.title = s.busy ? "Stop" : "Send (Enter)";
    }
    els.notice.hidden = !s.notice;
    els.notice.textContent = s.notice || "";
    els.card.hidden = !(s.messages.length || s.notice || s.busy);
    updateBarButtons();
    $("barPin").hidden = true;
    $("pinBtn").hidden = true;
    const last = s.messages[s.messages.length - 1];
    els.errActions.hidden = !(last && last.state === "error" && !s.busy);

    if (s.startDictation && !dictationAsked) { dictationAsked = true; setTimeout(() => window.__quickPromptDictate(), 0); }
    if (s.startConversation && !conversationAsked) { conversationAsked = true; setTimeout(() => window.__quickPromptConverse(), 0); }
    if (!draftApplied) {
      draftApplied = true;
      const d = s.draft || {};
      if (d.context || s.notice) { els.ctx.value = d.context || ""; showCtx(true); }
      if (d.instruction) els.prompt.value = d.instruction;
      autoGrow();
      setTimeout(() => {
        const target = s.notice && !els.ctx.value ? els.ctx : els.prompt;
        target.focus();
        target.setSelectionRange(target.value.length, target.value.length);
      }, 0);
    }

    buildFields(s);
    renderAttachments();
    const hasAnswer = lastAnswer() != null && !s.busy;
    els.actions.hidden = !hasAnswer;
    $("paste").hidden = !s.appWindow;
    els.prompt.placeholder = s.messages.length && !fieldsActive()
      ? "Follow up…"
      : fieldsActive()
        ? (/\{(\$selected|text|selection|input)\}/i.test(s.presetPrompt || "") && !(s.draft && s.draft.context) ? "Paste the text — Enter runs" : "Add a note (optional) — Enter runs")
      : s.pasteMode
        ? "Paste text" + (s.title ? " · " + s.title : "") + " — Enter sends"
        : ("Ask " + agentName + (s.title ? " · " + s.title : "") + "…");
    els.ctxBtn.hidden = !!s.pasteMode && !s.messages.length;
    updateSendState();

    const key = JSON.stringify(s.messages) + s.busy + (s.status || "") + (stacked ? "S" : "A");
    if (key !== lastKey) {
      lastKey = key;
      // Keep the Copy / Paste row alive across re-renders (it moves into the last answer bubble).
      els.card.appendChild(els.actions);
      els.card.appendChild(els.errActions);
      els.thread.innerHTML = s.messages.map((m, i) => {
        if (m.role === "user") {
          const q = m.text
            ? esc(m.text).replace(/\{(\$selected|text|selection|input)\}/gi, '<span class="tok">selected</span>').replace(/\{\$clipboard\}/gi, '<span class="tok">clipboard</span>')
            : '<span class="tok">selected text</span>';
          const ctx = m.context ? `<div class="ctxinfo" title="${esc(m.context.slice(0, 600))}">+ ${m.context.length} characters</div>` : "";
          const pics = (m.images || []).filter(Boolean).map((t, k) => `<img src="${esc(t)}" data-m="${i}" data-i="${k}" alt="Picture ${k + 1}">`).join("");
          return `<div class="msg user glass" data-drag><div class="q">${q}</div>${ctx}${pics ? `<div class="mimgs">${pics}</div>` : ""}</div>`;
        }
        const last = i === s.messages.length - 1;
        if (m.state === "error") {
          return `<div class="msg bot glass"><div class="mh" data-drag><span class="ok err">!</span>Something went wrong</div><div class="a error">${esc(m.text)}</div></div>`;
        }
        if (m.state === "streaming") {
          const label = m.text ? "Writing…" : (last && s.status ? s.status : "Thinking…");
          return `<div class="msg bot glass${m.text ? "" : " thinking"}"><div class="mh" data-drag><span class="spin"></span><span class="live">${esc(label)}</span>${m.text ? speakerButton(i) : ""}</div>`
            + `<div class="a">${m.text ? md(m.text) + '<span class="cursor"></span>' : ""}</div></div>`;
        }
        return `<div class="msg bot glass"><div class="mh" data-drag><span class="ok">✓</span>Answered${speakerButton(i)}</div><div class="a">${md(m.text || "")}</div></div>`;
      }).join("");
      applyStack(s.messages);
      const lastBubble = els.thread.lastElementChild;
      if (lastBubble && lastBubble.classList.contains("bot")) {
        lastBubble.appendChild(els.actions);
        lastBubble.appendChild(els.errActions);
      }
      reader.paint();
      if (bridge && window.ResizeObserver) els.thread.querySelectorAll(".glass").forEach(el => threadObserver.observe(el));
      if (stick) { autoScrollAt = performance.now(); els.thread.scrollTop = els.thread.scrollHeight; }
    }
    queueLayout();
  }

  // ---------- fields of a prompt template ({name} / {name:a|b}) ----------
  let fieldsKey = "";
  const fieldList = $("fieldList");
  function fieldsActive() { return !$("fieldsBox").hidden; }
  function label(name) { return name.charAt(0).toUpperCase() + name.slice(1).replace(/[_-]+/g, " "); }
  function buildFields(s) {
    const fields = s.fields || [];
    const key = JSON.stringify(fields) + "|" + (s.presetPrompt || "");
    if (key === fieldsKey) return;
    fieldsKey = key;
    $("fieldsBox").hidden = !fields.length;
    $("fieldsTitle").textContent = (s.title || "Template") + " · fill in";
    fieldList.innerHTML = fields.map(f => {
      const control = f.options && f.options.length
        ? '<select data-field="' + esc(f.name) + '">' + f.options.map(o => '<option>' + esc(o) + '</option>').join("") + '</select>'
        : '<textarea rows="1" data-field="' + esc(f.name) + '" spellcheck="false" autocomplete="off"></textarea>';
      return '<label class="sfield' + (f.options && f.options.length ? '' : ' wide') + '">' + esc(label(f.name)) + control + '</label>';
    }).join("");
    if (fields.length) setTimeout(() => { const first = fieldList.querySelector("[data-field]"); if (first) first.focus(); }, 30);
    updateSendState();
    queueLayout();
  }
  function collectFields() {
    const values = {}; let missing = null;
    fieldList.querySelectorAll("[data-field]").forEach(el => {
      values[el.dataset.field] = el.value;
      const empty = el.tagName === "TEXTAREA" && !el.value.trim();
      el.parentElement.classList.toggle("missing", false);
      if (empty && !missing) missing = el;
    });
    return { values, missing };
  }
  function growField(el) {
    if (el.tagName !== "TEXTAREA") return;
    el.style.height = "auto";
    el.style.height = Math.min(el.scrollHeight, fieldMax) + "px";
    queueLayout();
  }
  fieldList.addEventListener("input", e => { e.target.parentElement.classList.remove("missing"); growField(e.target); updateSendState(); });
  fieldList.addEventListener("keydown", e => {
    if (e.key !== "Enter" || e.isComposing || e.shiftKey) return;
    e.preventDefault(); e.stopPropagation();
    const all = [...fieldList.querySelectorAll("[data-field]")];
    const next = all[all.indexOf(e.target) + 1];
    if (next && !(e.metaKey || e.ctrlKey)) next.focus(); else send();
  });

  function updateSendState() {
    if (window.__quickPromptSpeech && speech.state !== "idle") { els.send.classList.remove("idle"); return; }   // the stop button
    if (fieldsActive()) {
      els.send.classList.toggle("idle", !!state && !state.busy && !!collectFields().missing);
      return;
    }
    const empty = !els.prompt.value.trim() && (els.ctxWrap.hidden || !els.ctx.value.trim())
      && !pending.length && !((state && state.draft && state.draft.images) || []).length;
    els.send.classList.toggle("idle", !!state && !state.busy && empty);
  }

  // ---------- stacked view: only the latest question and answer; earlier ones sit behind as a deck ----------
  let stacked = true;
  function applyStack(messages) {
    const bubbles = [...els.thread.querySelectorAll(":scope > .msg")];
    let lastUser = -1;
    messages.forEach((m, i) => { if (m.role === "user") lastUser = i; });
    const earlier = Math.max(0, lastUser);
    if (earlier === 0) return;
    if (stacked) {
      // The latest answer is the top card, titled with its question; the earlier ones peek out behind it.
      const answer = bubbles[lastUser + 1];
      bubbles.forEach((b, i) => { b.hidden = i < earlier || (i === lastUser && !!answer); });
      if (answer) {
        const q = bubbles[lastUser].querySelector(".q");
        const title = document.createElement("div");
        title.className = "mq";
        title.textContent = q ? q.textContent : "";
        answer.insertBefore(title, answer.firstChild);
        answer.classList.add("top");
      }
      const head = document.createElement("div");
      head.className = "stackhead";
      head.setAttribute("role", "button");
      head.tabIndex = 0;
      head.title = "Show the whole conversation";
      head.innerHTML = '<span class="stacklabel">' + earlier + (earlier === 1 ? " earlier message" : " earlier messages") + ' · Show all</span>'
        + '<div class="stackcard glass s2"></div><div class="stackcard glass s1"></div>';
      els.thread.insertBefore(head, answer || bubbles[lastUser]);
    } else {
      const head = document.createElement("div");
      head.className = "stackhead open";
      head.setAttribute("role", "button");
      head.tabIndex = 0;
      head.innerHTML = '<span class="stacklabel">Show latest only</span>';
      els.thread.insertBefore(head, bubbles[0]);
    }
  }
  els.thread.addEventListener("click", e => {
    const head = e.target.closest(".stackhead");
    if (!head) return;
    stacked = !stacked;
    stick = true;
    lastKey = "";
    if (state) render();
  });
  els.thread.addEventListener("keydown", e => {
    if ((e.key === "Enter" || e.key === " ") && e.target.closest && e.target.closest(".stackhead")) { e.preventDefault(); e.target.click(); }
  });

  // Scrolling: the bubbles switch to their own background until it stops, then the glass returns.
  // Only the user's own scrolling: following the answer to the bottom (on open, while it streams) keeps the
  // glass, or every open would flash the bubbles' flat background for a moment.
  let scrollIdle = 0, autoScrollAt = -1e9;
  els.thread.addEventListener("scroll", () => {
    const t = els.thread;
    stick = t.scrollHeight - t.scrollTop - t.clientHeight < 30;
    if (performance.now() - autoScrollAt < 150) return;
    if (!t.classList.contains("scrolling")) { t.classList.add("scrolling"); lastLayout = ""; queueLayout(); }
    clearTimeout(scrollIdle);
    scrollIdle = setTimeout(() => { t.classList.remove("scrolling"); lastLayout = ""; queueLayout(); }, 160);
  });
  els.thread.addEventListener("click", e => {
    const t = e.target;
    if (t instanceof HTMLElement && t.hasAttribute("data-copy")) copyText(t.parentElement.querySelector("code").textContent);
    if (t instanceof HTMLImageElement && t.dataset.m != null) openPreview(base + "image?m=" + t.dataset.m + "&i=" + t.dataset.i, t.src);
  });

  // ---------- pictures: paste (⌘V) or drop them; they go with the next message ----------
  let pending = [];   // { mediaType, data, thumb } made here
  const attachBox = $("attachBox"), attachList = $("attachList");
  function renderAttachments() {
    const waiting = (state && state.draft && state.draft.images) || [];   // from the clipboard / selection
    const html = waiting.map((t, k) => `<div class="athumb" data-draft="${k}"><img src="${esc(t)}" alt=""><button type="button" data-drop-draft="${k}" title="Remove">✕</button></div>`)
      .concat(pending.map((p, k) => `<div class="athumb" data-pending="${k}"><img src="${esc(p.thumb)}" alt=""><button type="button" data-drop="${k}" title="Remove">✕</button></div>`));
    attachList.innerHTML = html.join("");
    attachBox.hidden = html.length === 0 || collapsed;
    updateSendState();
    queueLayout();
  }
  attachList.addEventListener("click", async e => {
    const t = e.target;
    if (!(t instanceof HTMLElement)) return;
    if (t.dataset.drop != null) { pending.splice(+t.dataset.drop, 1); renderAttachments(); return; }
    if (t.dataset.dropDraft != null) { await post("dropimage", { i: t.dataset.dropDraft }); return; }
    const box = t.closest(".athumb");
    if (!box) return;
    if (box.dataset.pending != null && pending[+box.dataset.pending].mediaType === "application/pdf") return;   // the card says it all
    if (box.dataset.pending != null) openPreview("data:" + pending[+box.dataset.pending].mediaType + ";base64," + pending[+box.dataset.pending].data);
    else openPreview(base + "image?m=-1&i=" + box.dataset.draft, box.querySelector("img").src);
  });

  // Scales a picture down for the AI (longest side 1568 px, what the models use) and makes a small preview.
  function loadImage(file) {
    return new Promise((resolve, reject) => {
      const url = URL.createObjectURL(file);
      const img = new Image();
      img.onload = () => { URL.revokeObjectURL(url); resolve(img); };
      img.onerror = () => { URL.revokeObjectURL(url); reject(new Error("unreadable")); };
      img.src = url;
    });
  }
  function draw(img, max, type, quality) {
    const k = Math.min(1, max / Math.max(img.naturalWidth, img.naturalHeight));
    const c = document.createElement("canvas");
    c.width = Math.max(1, Math.round(img.naturalWidth * k));
    c.height = Math.max(1, Math.round(img.naturalHeight * k));
    const g = c.getContext("2d");
    if (type === "image/jpeg") { g.fillStyle = "#fff"; g.fillRect(0, 0, c.width, c.height); }
    g.drawImage(img, 0, 0, c.width, c.height);
    return c.toDataURL(type, quality);
  }
  async function addImageFile(file) {
    if (!file || !/^image\//.test(file.type)) return;
    if (pending.length >= 8) { toast("Up to 8 pictures and PDFs"); return; }
    try {
      const img = await loadImage(file);
      let url = draw(img, 1568, "image/png");
      if (url.length > 2000000) url = draw(img, 1568, "image/jpeg", 0.88);   // large photos: JPEG
      const comma = url.indexOf(",");
      pending.push({ mediaType: url.slice(5, url.indexOf(";")), data: url.slice(comma + 1), thumb: draw(img, 320, "image/jpeg", 0.8) });
      renderAttachments();
      els.prompt.focus();
    } catch (err) {
      toast("Could not read that picture");
    }
  }
  // PDFs go to the AI whole (Claude and the OpenAI API read them); the preview is a small card with the name.
  function pdfThumb(name) {
    const c = document.createElement("canvas"); c.width = 240; c.height = 240;
    const g = c.getContext("2d");
    g.fillStyle = "#2a2833"; g.fillRect(0, 0, 240, 240);
    g.fillStyle = "#f2f0f5"; g.beginPath(); g.moveTo(78, 22); g.lineTo(144, 22); g.lineTo(170, 48); g.lineTo(170, 140); g.lineTo(78, 140); g.closePath(); g.fill();
    g.fillStyle = "#c9c6d1"; g.beginPath(); g.moveTo(144, 22); g.lineTo(170, 48); g.lineTo(144, 48); g.closePath(); g.fill();
    g.fillStyle = "#E5484D"; g.fillRect(62, 92, 70, 32);
    g.fillStyle = "#fff"; g.font = "bold 22px -apple-system, Helvetica, Arial"; g.textAlign = "center"; g.textBaseline = "middle"; g.fillText("PDF", 97, 109);
    // The name in up to two lines, cut in the middle of the second.
    g.fillStyle = "#f2f0f5"; g.font = "600 25px -apple-system, Helvetica, Arial";
    const text = (name || "document.pdf").replace(/\.pdf$/i, "");
    let a = text; while (a.length > 1 && g.measureText(a).width > 220) a = a.slice(0, -1);
    let b = text.slice(a.length);
    if (b && g.measureText(b).width > 220) { while (b.length > 1 && g.measureText(b + "…").width > 220) b = b.slice(0, -1); b += "…"; }
    g.fillText(a, 120, b ? 176 : 190);
    if (b) g.fillText(b, 120, 208);
    return c.toDataURL("image/jpeg", 0.85);
  }
  const isPdf = f => f.type === "application/pdf" || /\.pdf$/i.test(f.name || "");
  async function addPdfFile(f) {
    if (pending.length >= 8) { toast("Up to 8 pictures and PDFs"); return; }
    if (f.size > 18 * 1024 * 1024) { toast("PDFs up to 18 MB"); return; }
    try {
      const url = await new Promise((resolve, reject) => { const r = new FileReader(); r.onload = () => resolve(String(r.result)); r.onerror = reject; r.readAsDataURL(f); });
      pending.push({ mediaType: "application/pdf", name: f.name || "document.pdf", data: url.slice(url.indexOf(",") + 1), thumb: pdfThumb(f.name) });
      renderAttachments();
      els.prompt.focus();
    } catch (err) {
      toast("Could not read " + (f.name || "that PDF"));
    }
  }
  document.addEventListener("paste", e => {
    if (collapsed || settingsOpen || historyOpen || actionsOpen) return;
    const items = [...(e.clipboardData ? e.clipboardData.items : [])];
    // Text wins when the clipboard has both (e.g. spreadsheet cells also come as a picture).
    if (items.some(it => it.kind === "string" && it.type === "text/plain")) return;
    const files = items.filter(it => it.kind === "file" && (/^image\//.test(it.type) || it.type === "application/pdf")).map(it => it.getAsFile());
    if (!files.length) return;
    e.preventDefault();
    files.forEach(f => isPdf(f) ? addPdfFile(f) : addImageFile(f));
  });
  // ---------- dictation (native popup: the app runs Whisper on this Mac) ----------
  // States: idle → starting (model loading) → listening (text streams into the box) → finishing (the final pass).
  const speech = { available: false, model: "missing", size: 0, received: 0, state: "idle", base: "", sendAfter: false, wantStart: false };
  const micBtn = $("barMic"), speechBox = $("speechBox");
  function speechPost(action, extra) { if (bridge) bridge.postMessage(Object.assign({ type: "speech", action }, extra || {})); }
  const mb = n => Math.round(n / 1000000) + " MB";
  // The languages Whisper knows (code: name). Settings keeps a short list of the ones the user speaks.
  const WHISPER_LANGS = Object.fromEntries("en:English,zh:Chinese,de:German,es:Spanish,ru:Russian,ko:Korean,fr:French,ja:Japanese,pt:Portuguese,tr:Turkish,pl:Polish,ca:Catalan,nl:Dutch,ar:Arabic,sv:Swedish,it:Italian,id:Indonesian,hi:Hindi,fi:Finnish,vi:Vietnamese,he:Hebrew,uk:Ukrainian,el:Greek,ms:Malay,cs:Czech,ro:Romanian,da:Danish,hu:Hungarian,ta:Tamil,no:Norwegian,th:Thai,ur:Urdu,hr:Croatian,bg:Bulgarian,lt:Lithuanian,la:Latin,mi:Maori,ml:Malayalam,cy:Welsh,sk:Slovak,te:Telugu,fa:Persian,lv:Latvian,bn:Bengali,sr:Serbian,az:Azerbaijani,sl:Slovenian,kn:Kannada,et:Estonian,mk:Macedonian,br:Breton,eu:Basque,is:Icelandic,hy:Armenian,ne:Nepali,mn:Mongolian,bs:Bosnian,kk:Kazakh,sq:Albanian,sw:Swahili,gl:Galician,mr:Marathi,pa:Punjabi,si:Sinhala,km:Khmer,sn:Shona,yo:Yoruba,so:Somali,af:Afrikaans,oc:Occitan,ka:Georgian,be:Belarusian,tg:Tajik,sd:Sindhi,gu:Gujarati,am:Amharic,yi:Yiddish,lo:Lao,uz:Uzbek,fo:Faroese,ht:Haitian Creole,ps:Pashto,tk:Turkmen,nn:Nynorsk,mt:Maltese,sa:Sanskrit,lb:Luxembourgish,my:Myanmar,bo:Tibetan,tl:Tagalog,mg:Malagasy,as:Assamese,tt:Tatar,haw:Hawaiian,ln:Lingala,ha:Hausa,ba:Bashkir,jw:Javanese,su:Sundanese,yue:Cantonese".split(",").map(x => x.split(":")));
  // Not chosen yet: English, and the Mac's language, or else its region's (an English Mac in Turkey: Turkish).
  const REGION_LANG = { TR: "tr", DE: "de", AT: "de", CH: "de", FR: "fr", BE: "fr", ES: "es", MX: "es", AR: "es", CO: "es", IT: "it",
    NL: "nl", PT: "pt", BR: "pt", RU: "ru", PL: "pl", JP: "ja", CN: "zh", TW: "zh", HK: "yue", KR: "ko", SA: "ar", AE: "ar", EG: "ar",
    GR: "el", SE: "sv", NO: "no", DK: "da", FI: "fi", CZ: "cs", HU: "hu", RO: "ro", UA: "uk", IL: "he", IN: "hi", ID: "id", VN: "vi",
    TH: "th", AZ: "az", IR: "fa", BG: "bg", HR: "hr", RS: "sr", SK: "sk", SI: "sl", MY: "ms", PK: "ur", KZ: "kk" };
  function defaultLangs() {
    const sys = (navigator.languages || [navigator.language || "en"]).map(l => (l || "").toLowerCase().split("-")[0]).find(c => c !== "en" && WHISPER_LANGS[c])
      || REGION_LANG[String(nativeInfo.region || "").toUpperCase()];
    return sys && sys !== "en" ? ["en", sys] : ["en"];
  }
  function parseLangs(value) {
    const list = String(value || "").split(",").filter(c => WHISPER_LANGS[c]);
    return list.length ? list : defaultLangs();
  }
  let speechLangs = defaultLangs();
  function renderLangs() {
    const box = $("speechLangs");
    box.textContent = "";
    speechLangs.forEach(code => {
      const chip = document.createElement("span");
      chip.className = "lang";
      chip.textContent = WHISPER_LANGS[code];
      if (speechLangs.length > 1) {
        const x = document.createElement("button");
        x.type = "button"; x.textContent = "✕"; x.title = "Remove";
        x.addEventListener("click", () => { speechLangs = speechLangs.filter(c => c !== code); renderLangs(); });
        chip.appendChild(x);
      }
      box.appendChild(chip);
    });
    if (speechLangs.length < 8) {
      const add = document.createElement("select");
      add.appendChild(new Option("Add a language…", ""));
      Object.entries(WHISPER_LANGS).filter(([c]) => !speechLangs.includes(c)).sort((a, b) => a[1].localeCompare(b[1]))
        .forEach(([c, n]) => add.appendChild(new Option(n, c)));
      add.addEventListener("change", () => { if (add.value) { speechLangs.push(add.value); renderLangs(); } });
      box.appendChild(add);
    }
    queueLayout();
  }
  // Pip while dictating: 36 rays around it, mirrored left and right, red fading to coral at the tips, turning
  // slowly. Their lengths follow the microphone's level (the helper sends ~12 a second); between those the
  // rays ease, so the motion stays smooth.
  const pipWave = (() => {
    const canvas = $("pipWave"), ctx = canvas.getContext("2d");
    const N = 36, bins = new Array(N).fill(0);
    let running = false, shown = 0, peak = 0.12, t = 0, raf = 0;
    function draw() {
      if (!running) return;
      raf = requestAnimationFrame(draw);
      const icon = $("miniOpen").getBoundingClientRect();
      if (!icon.width || mini.hidden) { canvas.hidden = true; return; }
      t += 1 / 60;
      // Automatic gain: relative to the loudest of the last few seconds, so a quiet voice moves the rays too.
      // Speaking (conversation): the rays move on their own, in the speaking colours.
      const level = api.tone === "speak" ? 0.45 + 0.3 * Math.abs(Math.sin(t * 7.3) * Math.cos(t * 2.9)) : api.level;
      peak = Math.max(level, peak * 0.996, 0.12);
      const target = level < 0.04 ? 0 : Math.min(1, level / peak);
      shown += (target - shown) * 0.25;
      // The rays start on the disc's rim (the button is larger than the disc it shows), reaching as far as before.
      const r = icon.width / 2, disc = $("miniOpen").querySelector("circle").getBoundingClientRect().width / 2 || r;
      const lw = Math.max(2, r * 0.1), inner = disc + lw / 2 + 0.5, reach = r * 0.85 + (r + 6 - inner), size = Math.ceil((inner + reach + 4) * 2);
      const dpr = window.devicePixelRatio || 1;
      if (canvas.width !== size * dpr) { canvas.width = canvas.height = size * dpr; canvas.style.width = canvas.style.height = size + "px"; }
      canvas.style.left = (icon.left + icon.width / 2 - size / 2) + "px";
      canvas.style.top = (icon.top + icon.height / 2 - size / 2) + "px";
      canvas.hidden = false;
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
      ctx.clearRect(0, 0, size, size);
      const c = size / 2, half = N / 2, turn = t * 0.25;
      for (let i = 0; i < half; i++) {
        // A spectrum-like spread from one level: neighbouring rays move together, each at its own pace.
        const v = shown * (0.4 + 0.6 * Math.abs(Math.sin(t * 6 + i * 0.9) * Math.cos(t * 3.3 + i * 0.37)));
        bins[i] += (v - bins[i]) * 0.3;
      }
      ctx.lineCap = "round";
      ctx.lineWidth = lw;
      for (let i = 0; i < N; i++) {
        const v = bins[i < half ? i : N - 1 - i];
        const a = i / N * Math.PI * 2 - Math.PI / 2 + turn, len = 3 + v * reach;
        const x0 = c + Math.cos(a) * inner, y0 = c + Math.sin(a) * inner, x1 = c + Math.cos(a) * (inner + len), y1 = c + Math.sin(a) * (inner + len);
        const g = ctx.createLinearGradient(x0, y0, x1, y1);
        g.addColorStop(0, api.tone === "speak" ? "rgba(110, 120, 255, .95)" : "rgba(229, 72, 77, .95)");
        g.addColorStop(1, api.tone === "speak" ? "rgba(190, 140, 255, 0)" : "rgba(240, 153, 123, 0)");
        ctx.strokeStyle = g;
        ctx.beginPath(); ctx.moveTo(x0, y0); ctx.lineTo(x1, y1); ctx.stroke();
      }
    }
    const api = {
      level: 0,
      tone: "listen",
      start() { if (running) return; running = true; api.level = 0; shown = 0; peak = 0.12; bins.fill(0); raf = requestAnimationFrame(draw); },
      stop() { running = false; cancelAnimationFrame(raf); canvas.hidden = true; },
    };
    return api;
  })();
  function showSpeechBox(text, opts) {
    opts = opts || {};
    $("sbText").textContent = text;
    $("sbBar").hidden = opts.progress == null;
    if (opts.progress != null) $("sbFill").style.width = Math.round(opts.progress * 100) + "%";
    const actions = $("sbActions");
    actions.textContent = "";
    (opts.buttons || []).forEach(b => {
      const el = document.createElement("button");
      el.type = "button";
      el.textContent = b.label;
      if (b.primary) el.className = "primary";
      el.addEventListener("click", b.run);
      actions.appendChild(el);
    });
    speechBox.hidden = false;
    queueLayout();
  }
  function hideSpeechBox() { if (!speechBox.hidden) { speechBox.hidden = true; queueLayout(); } }
  function showModelOffer() {
    showSpeechBox("Dictation needs a speech model (" + mb(speech.size) + ", downloaded once). It runs on this Mac only.", { buttons: [
      { label: "Download", primary: true, run: () => { speech.wantStart = true; speechPost("download"); } },
      { label: "Not now", run: () => { speech.wantStart = false; hideSpeechBox(); } },
    ] });
  }
  function renderSpeechSettings() {
    const line = $("spModel"), btn = $("spModelBtn");
    if (!speech.available) { line.textContent = "Dictation needs a Mac with Apple silicon."; btn.hidden = true; return; }
    btn.hidden = false;
    if (speech.model === "ready") { line.textContent = "Whisper large-v3-turbo · on this Mac · " + mb(speech.size); btn.textContent = "Delete"; btn.dataset.do = "deleteModel"; }
    else if (speech.model === "downloading") { line.textContent = "Downloading… " + mb(speech.received) + " of " + mb(speech.size); btn.textContent = "Cancel"; btn.dataset.do = "cancelDownload"; }
    else { line.textContent = "Not downloaded yet (" + mb(speech.size) + ")."; btn.textContent = "Download"; btn.dataset.do = "download"; }
  }
  $("spModelBtn").addEventListener("click", e => speechPost(e.currentTarget.dataset.do));
  function setSpeechState(next) {
    speech.state = next;
    micBtn.classList.toggle("on", next === "listening");
    micBtn.classList.toggle("wait", next === "starting" || next === "finishing");
    els.prompt.readOnly = next !== "idle";
    els.prompt.classList.toggle("dictating", next === "starting" || next === "finishing");
    micBtn.title = next === "listening" ? "Stop (↵ stops and sends, esc cancels)" : "Dictate: speak, then click again (↵ sends, esc cancels)";
    if (next === "idle") micBtn.style.removeProperty("--lvl");
    const recording = next !== "idle";
    els.send.innerHTML = recording ? ICON_STOP : (state && state.busy ? ICON_STOP : ICON_SEND);
    els.send.title = recording ? "Stop recording (↵)" : (state && state.busy ? "Stop" : "Send (Enter)");
    els.send.classList.toggle("dictstop", recording);
    if (recording) els.send.classList.remove("idle"); else updateSendState();
    els.root.classList.toggle("dictating", next === "listening" || next === "starting" || pipVoice);
    if (next === "listening" || next === "starting") { pipWave.tone = "listen"; pipWave.start(); } else if (!pipVoice) pipWave.stop();
    // While recording, the red stop button says it; the microphone steps aside.
    micBtn.hidden = !speech.available || recording;
  }
  function setDictated(text) {
    const base = speech.base.replace(/\s+$/, "");
    els.prompt.value = base && text ? base + " " + text : (base || text);
    autoGrow();
    els.prompt.scrollTop = els.prompt.scrollHeight;
  }
  function startDictation() {
    if (!speech.available || speech.state !== "idle") return;
    if (speech.model !== "ready") { speech.model === "downloading" ? renderSpeechBoxProgress() : showModelOffer(); return; }
    hideSpeechBox();
    reader.stop();   // the microphone would hear the voice
    speech.base = els.prompt.value;
    speech.sendAfter = false;
    setSpeechState("starting");
    speechPost("start", { languages: parseLangs(state && state.speechLanguages), prompt: (state && state.speechHints) || "", conversation: convo.on });
  }
  function stopDictation(thenSend) {
    if (speech.state === "listening" || speech.state === "starting") {
      speech.sendAfter = !!thenSend;
      setSpeechState("finishing");
      speechPost("stop");
    }
  }
  function cancelDictation() {
    if (speech.state === "idle") return;
    speechPost("cancel");
    els.prompt.value = speech.base;
    autoGrow();
    setSpeechState("idle");
  }
  function renderSpeechBoxProgress() {
    const p = speech.size ? speech.received / speech.size : 0;
    showSpeechBox("Downloading the speech model… " + mb(speech.received) + " of " + mb(speech.size), { progress: p,
      buttons: [{ label: "Cancel", run: () => { speech.wantStart = false; speechPost("cancelDownload"); } }] });
  }
  window.__quickPromptSpeech = e => {
    switch (e.type) {
      case "status":
        speech.available = !!e.available;
        speech.known = true;
        speech.model = e.model;
        speech.size = e.size || speech.size;
        speech.received = e.received || 0;
        micBtn.hidden = !speech.available || speech.state !== "idle";
        if (speech.startWhenKnown) { const what = speech.startWhenKnown; speech.startWhenKnown = false; setTimeout(what === "converse" ? startConversation : dictateNow, 0); }
        renderSpeechSettings();
        if (speech.model === "downloading" && speech.wantStart) renderSpeechBoxProgress();
        else if (speech.model === "ready" && speech.wantStart) { speech.wantStart = false; hideSpeechBox(); startDictation(); }
        else if (speech.model !== "downloading" && !speechBox.hidden && $("sbBar").hidden === false) hideSpeechBox();
        queueLayout();
        break;
      case "loading":
      case "starting":
        if (speech.state !== "idle") setSpeechState("starting");
        break;
      case "listening":
        if (speech.state === "starting") setSpeechState("listening");
        break;
      case "level":
        if (speech.state === "listening") { micBtn.style.setProperty("--lvl", Math.min(1, e.value).toFixed(2)); pipWave.level = Math.min(1, e.value); convo.onLevel(e.value); }
        break;
      case "partial":
        if (speech.state === "listening") { setDictated(e.text || ""); convo.onPartial(e.text || ""); }
        break;
      case "played":
        { const done = pendingPlays.get(e.id); pendingPlays.delete(e.id); if (done) done(); }
        break;
      case "barge":
        // Conversation: the user spoke over Pip. The helper stopped the answer and listens from just before.
        if (!convo.on) break;
        reader.autoFrom = Math.max(reader.autoFrom == null ? -1 : reader.autoFrom, reader.index);
        reader.stop(true);
        convo.heard = true; convo.activeAt = performance.now();
        speech.base = els.prompt.value; speech.sendAfter = false;
        setSpeechState("starting");
        break;
      case "final": {
        if (speech.state === "idle") break;
        setDictated(e.text || "");
        const thenSend = speech.sendAfter;
        setSpeechState("idle");
        els.prompt.focus();
        els.prompt.setSelectionRange(els.prompt.value.length, els.prompt.value.length);
        if (thenSend && els.prompt.value.trim()) send();
        else if (convo.on) { convo.waiting = false; setTimeout(listenTurn, 300); }   // nothing said: listen again
        break;
      }
      case "cancelled":
        if (speech.state !== "idle") { els.prompt.value = speech.base; autoGrow(); setSpeechState("idle"); }
        break;
      case "error":
        if (speech.state !== "idle") { setSpeechState("idle"); }
        speech.wantStart = false;
        if (collapsed) expand();   // listening on Pip alone: the message needs the chat
        if (e.code === "permission") showSpeechBox("Quick Prompt may not use the microphone. Allow it in System Settings → Privacy & Security → Microphone.",
          { buttons: [{ label: "Open Settings", primary: true, run: () => { speechPost("openPrivacy"); hideSpeechBox(); } }, { label: "Close", run: hideSpeechBox }] });
        else showSpeechBox(e.message || "Dictation failed.", { buttons: [{ label: "Close", run: hideSpeechBox }] });
        break;
    }
  };
  // A click dictates; holding the microphone starts a conversation.
  let micHold = 0, micHeld = false;
  micBtn.addEventListener("pointerdown", () => { micHeld = false; clearTimeout(micHold); micHold = setTimeout(() => { micHeld = true; startConversation(); }, 550); });
  ["pointerup", "pointerleave"].forEach(t => micBtn.addEventListener(t, () => clearTimeout(micHold)));
  micBtn.addEventListener("click", () => { if (micHeld) { micHeld = false; return; } speech.state === "idle" ? startDictation() : stopDictation(false); });
  // The dictation shortcut (from the app). With only Pip on screen, Pip listens on its own, like an assistant:
  // no chat opens (clicking Pip opens it, the recording goes on). Pressed again, it stops and the chat opens
  // with the text. With the chat open, it starts there. Returns "quiet" when the panel must not take the keys.
  window.__quickPromptDictate = () => {
    if (speech.state !== "idle") { if (collapsed) expand(); stopDictation(false); return "open"; }
    if (!speech.known) { speech.startWhenKnown = true; return collapsed ? "quiet" : "open"; }
    return dictateNow();
  };
  function dictateNow() {
    if (!speech.available) { if (collapsed) expand(); return "open"; }
    // Nothing to say on Pip (model download, errors): those need the chat.
    if (collapsed && speech.model !== "ready") expand();
    startDictation();
    return collapsed ? "quiet" : "open";
  }
  // While dictating, Enter stops and sends and Esc cancels (before the box's own keys).
  document.addEventListener("keydown", e => {
    if (speech.state === "idle") return;
    if (e.key === "Escape") { e.preventDefault(); e.stopImmediatePropagation(); cancelDictation(); }
    else if (e.key === "Enter" && !e.shiftKey) { e.preventDefault(); e.stopImmediatePropagation(); if (speech.state !== "finishing") convo.on ? sendTurn() : stopDictation(false); }
  }, true);
  speechPost("probe");

  // Text files go into the text box under the question, each under its name.
  const TEXT_EXT = /\.(txt|md|markdown|csv|tsv|json|xml|ya?ml|toml|ini|log|html?|css|js|mjs|ts|tsx|jsx|cs|swift|py|rb|go|rs|java|kt|c|h|cpp|hpp|m|sh|zsh|sql|ps1|bat|vb|php|lua|r|tex|rtf|srt|vtt)$/i;
  function isTextFile(f) {
    return /^text\//.test(f.type) || /json|xml|javascript|yaml|x-sh|sql/.test(f.type) || TEXT_EXT.test(f.name || "");
  }
  async function addTextFile(f) {
    if (f.size > 512 * 1024) { toast("Text files up to 512 KB"); return; }
    try {
      const text = await f.text();
      const part = "--- " + f.name + " ---\n" + text.replace(/\s+$/, "");
      els.ctx.value = els.ctx.value.trim() ? els.ctx.value.replace(/\s+$/, "") + "\n\n" + part : part;
      showCtx(true);
      updateSendState();
    } catch (err) {
      toast("Could not read " + f.name);
    }
  }
  async function addFiles(files) {
    let skipped = 0;
    for (const f of files) {
      if (/^image\//.test(f.type)) await addImageFile(f);
      else if (isPdf(f)) await addPdfFile(f);
      else if (isTextFile(f)) await addTextFile(f);
      else skipped++;
    }
    if (skipped) toast("Only pictures, PDFs and text files can be added");
    if (!collapsed) els.prompt.focus();
  }
  const hasFiles = e => e.dataTransfer && [...e.dataTransfer.items].some(it => it.kind === "file");
  document.addEventListener("dragover", e => {
    if (!hasFiles(e)) return;
    // Always taken, or the web view would open the file itself.
    e.preventDefault();
    if (settingsOpen || historyOpen || actionsOpen) { e.dataTransfer.dropEffect = "none"; return; }
    document.documentElement.classList.add("dropping");
  });
  document.addEventListener("dragleave", e => { if (!e.relatedTarget) document.documentElement.classList.remove("dropping"); });
  document.addEventListener("drop", async e => {
    document.documentElement.classList.remove("dropping");
    if (!hasFiles(e)) return;
    e.preventDefault();
    if (settingsOpen || historyOpen || actionsOpen) return;
    const files = [...e.dataTransfer.files];
    // Dropped on Pip: the conversation opens with them.
    if (collapsed) expand();
    await addFiles(files);
  });

  // ---------- large preview ----------
  let previewOpen = false, previewSize = { w: 600, h: 400 };
  const lightbox = $("lightbox"), lightImg = $("lightImg");
  function openPreview(src, fallback) {
    previewOpen = true;
    lightImg.onerror = () => { if (fallback && lightImg.src !== fallback) lightImg.src = fallback; };
    lightImg.onload = () => {
      const maxW = Math.min(1100, Math.round(screen.availWidth * 0.8)), maxH = Math.max(240, Math.round((screen.availHeight || 900) * 0.8) - 60);
      const k = Math.min(1, maxW / lightImg.naturalWidth, maxH / lightImg.naturalHeight);
      previewSize = { w: Math.max(360, Math.round(lightImg.naturalWidth * k) + 20), h: Math.round(lightImg.naturalHeight * k) + 20 };
      lastLayout = "";
      queueLayout();
    };
    lightImg.src = src;
    [els.card, els.bar, els.ctxWrap, attachBox, $("fieldsBox")].forEach(el => { el.dataset.wasHidden = el.hidden ? "1" : ""; el.hidden = true; });
    lightbox.hidden = false;
    queueLayout();
  }
  function closePreview() {
    if (!previewOpen) return;
    previewOpen = false;
    lightbox.hidden = true;
    lightImg.removeAttribute("src");
    [els.card, els.bar, els.ctxWrap, attachBox, $("fieldsBox")].forEach(el => { el.hidden = el.dataset.wasHidden === "1"; });
    lastLayout = "";
    queueLayout();
  }
  lightbox.addEventListener("click", closePreview);

  function showCtx(show) {
    els.ctxWrap.hidden = !show;
    queueLayout();
  }

  function autoGrow() {
    els.prompt.style.height = "auto";
    els.prompt.style.height = Math.min(els.prompt.scrollHeight, promptMax) + "px";
    // A scroll bar only once it is as tall as it gets (else WebKit flashes one whenever the box resizes).
    els.prompt.style.overflowY = els.prompt.scrollHeight > promptMax ? "auto" : "hidden";
    updateBarButtons();
    updateSendState();
    queueLayout();
  }

  async function send() {
    if (!state) return;
    // Dictating: the send button is the stop button; the text stays for a look, the next send sends it.
    if (window.__quickPromptSpeech && speech.state !== "idle") { stopDictation(false); return; }
    if (state.busy) { post("cancel"); return; }
    const instruction = els.prompt.value.trim();
    const context = els.ctxWrap.hidden ? "" : els.ctx.value;
    const fields = fieldsActive() ? collectFields() : null;
    if (fields && fields.missing) {
      fields.missing.parentElement.classList.add("missing");
      fields.missing.focus();
      return;
    }
    const waitingPictures = ((state.draft && state.draft.images) || []).length;
    if (!instruction && !context.trim() && !fields && !pending.length && !waitingPictures) { els.prompt.focus(); return; }
    els.send.disabled = true;
    const res = await post("send", { instruction, context, fields: fields ? JSON.stringify(fields.values) : "",
      images: pending.length ? JSON.stringify(pending) : "" });
    els.send.disabled = false;
    if (res.ok) {
      reader.stop();
      els.prompt.value = ""; els.ctx.value = ""; showCtx(false); pending = []; renderAttachments(); autoGrow(); stick = true;
    } else {
      toast(fields ? "Paste the text first" : "Could not send");
      if (fields) els.prompt.focus();
    }
  }

  $("close").addEventListener("click", dismiss);
  $("copy").addEventListener("click", () => { const t = lastAnswer(); if (t != null) copyText(t); });
  $("paste").addEventListener("click", pasteAndClose);
  els.send.addEventListener("click", send);
  // The history button gives its room to the question while something is typed.
  function updateBarButtons() {
    $("barHistory").hidden = !els.card.hidden || !!els.prompt.value;
  }
  // ---------- the chat's agent chip ----------
  function renderAgentChip(s) {
    const foot = $("barFoot"), chip = $("barAgent");
    if (!s.agent) { if (!foot.hidden) { foot.hidden = true; $("root").classList.remove("has-foot"); queueLayout(); } return; }
    chip.style.setProperty("--agent", s.agent.color || "");
    chip.querySelector(".aname").textContent = s.agent.name;
    // The model, unless the agent's name already says it (or it uses the CLI's own default).
    const model = s.model ? (MODEL_NAMES[s.model] || s.model) : "";
    chip.querySelector(".amodel").textContent = model && !s.agent.name.toLowerCase().includes(model.toLowerCase().split(" ")[0]) ? model : "";
    chip.title = "Agent: " + s.agent.name + " — click to switch";
    if (foot.hidden) { foot.hidden = false; $("root").classList.add("has-foot"); queueLayout(); }
  }
  async function switchAgent(id) {
    if (state && state.agent && state.agent.id === id) return;
    const res = await post("agent", { id });
    if (res.ok) { const a = (menuAgents.find(x => x.id === id) || {}).name; if (a) toast(state && state.messages.length ? "Next message goes to " + a : a); }
    else if (res.status !== 404) toast("Still answering");
  }
  function agentMenuItems() {
    const current = state && state.agent ? state.agent.id : "";
    return [
      ...menuAgents.map(a => ({ id: "agent-" + a.id, title: a.name, checked: a.id === current })),
      { separator: true },
      { id: "agents-edit", title: "Edit Agents…" },
    ];
  }
  $("barAgent").addEventListener("click", async e => {
    e.stopPropagation();
    const d = await fetchAgents();
    menuAgents = d ? d.agents : [];
    if (bridge) { bridge.postMessage({ type: "menu", items: agentMenuItems() }); return; }
    showPageMenu($("barAgent"), agentMenuItems(), id => window.__quickPromptMenu(id));
  });
  function toggleText() {
    const show = els.ctxWrap.hidden;
    showCtx(show);
    if (show) els.ctx.focus(); else { els.ctx.value = ""; els.prompt.focus(); }
    updateSendState();
  }
  function addMenuItems() {
    return [
      { id: "add-text", title: els.ctxWrap.hidden ? "Add Text" : "Remove Text" },
      { id: "add-file", title: "Add Attachment…" },
    ];
  }
  function runAddItem(id) {
    if (id === "add-text") toggleText();
    else if (id === "add-file") $("attachFile").click();
  }
  // In the native popup the + opens a native menu; in a browser tab a small menu of the page.
  let addMenu = null;
  function closeAddMenu() { if (addMenu) { addMenu.remove(); addMenu = null; } }
  // A small menu of the page above a button (browser tabs; the native popup uses a native menu).
  function showPageMenu(anchor, items, pick) {
    if (addMenu) { closeAddMenu(); return; }
    addMenu = document.createElement("div");
    addMenu.className = "addmenu";
    items.filter(it => it.id).forEach(it => {
      const b = document.createElement("button");
      b.type = "button";
      b.textContent = (it.checked ? "✓ " : "") + it.title;
      b.addEventListener("click", () => { closeAddMenu(); pick(it.id); });
      addMenu.appendChild(b);
    });
    const r = anchor.getBoundingClientRect();
    addMenu.style.left = Math.round(r.left + window.scrollX) + "px";
    document.body.appendChild(addMenu);
    const above = r.top - addMenu.offsetHeight - 6;
    addMenu.style.top = Math.round((above >= 0 ? above : r.bottom + 6) + window.scrollY) + "px";   // no room above: below
  }
  els.ctxBtn.addEventListener("click", e => {
    e.stopPropagation();
    if (bridge) { bridge.postMessage({ type: "menu", items: addMenuItems() }); return; }
    showPageMenu(els.ctxBtn, addMenuItems(), runAddItem);
  });
  document.addEventListener("click", e => { if (addMenu && !addMenu.contains(e.target)) closeAddMenu(); });
  $("attachFile").addEventListener("change", e => {
    addFiles([...e.target.files]);
    e.target.value = "";
  });
  els.prompt.addEventListener("input", autoGrow);
  els.ctx.addEventListener("input", () => { updateSendState(); queueLayout(); });
  document.addEventListener("keydown", e => {
    if (e.key === "Escape") {
      e.preventDefault();
      if (collapsed) return;
      if (previewOpen) { closePreview(); return; }
      if (actionsOpen) { if (!$("actForm").hidden) showActionList(); else if (actionsOnly) closeWindow(); else closeActions(); }
      else if (settingsOpen && !settingsOnly) closeSettings();
      else if (historyOpen && !historyOnly) closeHistory();
      else if (settingsOnly || historyOnly) closeWindow();
      else dismiss();
      return;
    }
    if (historyOpen || actionsOpen) return;
    if (settingsOpen) { if (e.key === "Enter" && e.target.tagName === "INPUT" && !e.target.closest("#setTemplates, #agForm")) { e.preventDefault(); saveSettings(); } return; }
    if (e.key !== "Enter" || e.isComposing) return;
    if (e.metaKey || e.ctrlKey) {
      e.preventDefault();
      if (lastAnswer() != null && !state.busy && !els.prompt.value.trim()) pasteAndClose(); else send();
      return;
    }
    if (!e.shiftKey && e.target === els.prompt) { e.preventDefault(); send(); }
  });
  window.addEventListener("pagehide", () => { try { navigator.sendBeacon(base + "closed"); } catch (e) {} });

  // ---------- read aloud: local voices in the engine (EMA Lightning for Turkish, Kokoro for English) ----------
  // Nothing leaves the computer: the engine makes each sentence's audio (QuickPrompt.Voice) and the page plays
  // them one after another, the next one already being made while one plays. An answer is read while it
  // streams; code blocks, tables and link addresses are left out. Turkish goes through autolex first, which
  // respells English words for the Turkish voice ("Service Layer" -> "Sörvis Leyır").
  const reader = { index: -1, done: 0, prefix: "", lang: "", queue: [], current: null, complete: false,
                   autoFrom: null, lex: null, lexLoading: null, run: 0 };
  let voice = { available: false, state: "missing", size: 0, received: 0, voices: { en: [] } };
  let audioCtx = null;
  const SPEAKER_ICON = '<svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M4 9.5v5h3.5L12 18.5v-13L7.5 9.5z"/><path class="w1" d="M15.5 9a4 4 0 0 1 0 6"/><path class="w2" d="M18 6.5a7.5 7.5 0 0 1 0 11"/></svg>';
  function speakerButton(i) {
    return voice.available ? `<button type="button" class="spk${reader.index === i ? " on" : ""}" data-read="${i}" title="${reader.index === i ? "Stop reading" : "Read aloud"}" aria-label="Read aloud">${SPEAKER_ICON}</button>` : "";
  }
  async function loadVoice(action) {
    try {
      const res = action ? await post("voice", { action }) : await fetch(base + "voice", { cache: "no-store" });
      voice = await res.json();
    } catch (e) {}
    return voice;
  }
  function parseVoices(value) {
    const map = {};
    String(value || "").split(";").forEach(p => { const k = p.indexOf("="); if (k > 0) map[p.slice(0, k)] = p.slice(k + 1); });
    return map;
  }
  // The answer's language: Turkish letters or words, else English words. Other languages have no voice yet.
  function guessLang(text) {
    const t = " " + text.toLowerCase().slice(0, 600) + " ";
    const count = rx => (t.match(rx) || []).length;
    const tr = count(/[ğış]/g) * 2 + count(/[\s,]+(ve|bir|bu|için|ile|ama|çok|olarak|var|yok|daha|gibi|değil|sonra|şimdi)[\s,.]/g);
    const en = count(/[\s,]+(the|and|is|are|of|to|for|with|that|this|you|it|can|will)[\s,.]/g);
    if (tr > en) return "tr";
    if (en > 0) return "en";
    return /[a-z]/.test(t) && !/[äßàâéèêëîïôûùÿœñ¿¡ąćęłńóśźżåøæ]/.test(t) ? "en" : "";
  }
  // Markdown -> what is worth hearing.
  function speakable(src) {
    return src
      .replace(/```[\s\S]*?(```|$)/g, "\n")
      .replace(/^\s*\|.*$/gm, "")
      .replace(/!\[[^\]]*\]\([^)]*\)/g, "")
      .replace(/\[([^\]]+)\]\([^)]*\)/g, "$1")
      .replace(/<https?:[^>]+>|https?:\/\/\S+|www\.\S+/g, "")
      .replace(/`([^`\n]+)`/g, (m, c) => /^[\p{L}\p{N} .'’-]{1,30}$/u.test(c) ? c : "")
      .replace(/<[^>\n]+>/g, "")
      .replace(/^\s{0,3}#{1,6}\s+/gm, "")
      .replace(/^\s*(?:[-*+•]|\d+[.)])\s+/gm, "")
      .replace(/^\s*>\s?/gm, "")
      .replace(/^\s*[-*_]{3,}\s*$/gm, "")
      .replace(/(\*\*|__|~~|\*)(?=\S)([^\n]+?)\1/g, "$2")
      .replace(/[ \t]{2,}/g, " ");
  }
  function sentences(text) {
    return text.split(/(?<=[.!?…])\s+|\n+/).map(x => x.trim()).filter(x => /[\p{L}\p{N}]/u.test(x));
  }
  // How much of the answer from `from` on can be read now: up to the last finished sentence or line,
  // and never into a code block still open.
  function readyPart(src, from, complete) {
    if (complete) return src.length - from;
    const rest = src.slice(from);
    let cut = 0, m;
    const rx = /[.!?…:;]["'’”)\]]*\s|\n/g;
    while ((m = rx.exec(rest))) cut = m.index + m[0].length;
    if (cut && ((src.slice(0, from + cut).match(/```/g) || []).length % 2)) cut = Math.max(0, src.lastIndexOf("```", from + cut) - from);
    return cut;
  }
  function loadLex() {
    if (!reader.lexLoading) {
      reader.lexLoading = new Promise((ok, fail) => {
        const tag = document.createElement("script");
        tag.src = base + "autolex.js";
        tag.onload = () => fetch(base + "autolex-tr.json").then(r => r.json()).then(t => { reader.lex = new window.AutoLex(t); ok(); }).catch(fail);
        tag.onerror = fail;
        document.head.appendChild(tag);
        setTimeout(fail, 5000);   // never wait longer than this for it
      }).catch(() => { if (!reader.lex) reader.lex = false; });   // without it, read the Turkish as written
    }
    return reader.lexLoading;
  }
  // One sentence's audio from the engine, decoded; null when it has none.
  async function fetchAudio(item, choice, rate) {
    let text = item.text;
    if (item.lang === "tr") {
      if (reader.lex === null) await loadLex();
      if (reader.lex) { try { text = reader.lex.apply(text); } catch (e) {} }
    }
    const res = await post("speak", { lang: item.lang, text, voice: (choice || {})[item.lang] || "", speed: String(Math.min(2, Math.max(0.5, (rate || 100) / 100))) });
    if (!res.ok) return null;
    const bytes = await res.arrayBuffer();
    // In a conversation the helper plays it, through the same engine as the microphone (echo cancellation).
    if (item.native) return { bytes };
    audioCtx = audioCtx || new (window.AudioContext || window.webkitAudioContext)();
    return { buffer: await audioCtx.decodeAudioData(bytes) };
  }
  const pendingPlays = new Map();
  let playSeq = 0, pipVoice = false;
  function toBase64(bytes) {
    const u8 = new Uint8Array(bytes);
    let out = "";
    for (let i = 0; i < u8.length; i += 0x8000) out += String.fromCharCode.apply(null, u8.subarray(i, i + 0x8000));
    return btoa(out);
  }
  // Pip shows it speaks (conversation): rays in the speaking colours.
  function pipSpeaking(on) {
    pipVoice = on;
    els.root.classList.toggle("dictating", on || speech.state === "listening" || speech.state === "starting");
    if (on) { pipWave.tone = "speak"; pipWave.start(); }
    else if (speech.state === "idle") pipWave.stop();
    queueLayout();
  }
  function prepare(item) {
    if (!item.audio) { item.native = convo.on && !!bridge && speech.available; item.audio = fetchAudio(item, parseVoices(state && state.readVoices), state && state.readRate).catch(() => null); }
    return item.audio;
  }
  async function pump() {
    if (reader.current) return;
    if (!reader.queue.length) { if (reader.complete && reader.index >= 0) finish(); return; }
    const run = reader.run, item = reader.queue[0];
    reader.current = item;
    reader.queue.slice(0, 2).forEach(prepare);
    const buffer = await prepare(item);
    if (run !== reader.run) return;   // stopped meanwhile
    reader.queue.shift();
    if (reader.queue[0]) prepare(reader.queue[0]);
    if (!buffer) { reader.current = null; pump(); return; }
    if (buffer.bytes) {
      const id = ++playSeq;
      item.playId = id;
      pendingPlays.set(id, () => { if (reader.current === item && run === reader.run) { reader.current = null; pump(); } });
      speechPost("play", { id, wav: toBase64(buffer.bytes) });
      pipSpeaking(true);
      return;
    }
    if (audioCtx.state === "suspended") { try { await audioCtx.resume(); } catch (e) {} }
    const source = audioCtx.createBufferSource();
    source.buffer = buffer.buffer;
    source.connect(audioCtx.destination);
    item.source = source;
    source.onended = () => { if (reader.current === item && run === reader.run) { reader.current = null; pump(); } };
    source.start();
  }
  reader.update = function (s) {
    if (!s || !voice.available) return;
    const msgs = s.messages || [];
    if (reader.autoFrom === null) {
      // Answers already there when the window opens are not read; one still being written is.
      reader.autoFrom = -1;
      msgs.forEach((m, i) => { if (m.role !== "user" && m.state !== "streaming") reader.autoFrom = i; });
    }
    const lastI = msgs.length - 1, last = msgs[lastI];
    if ((s.readAloud || convo.on) && voice.state === "ready" && last && last.role !== "user" && last.state !== "error" && lastI > reader.autoFrom && (last.text || last.state !== "streaming")) {
      reader.autoFrom = lastI;
      start(lastI);
      return;
    }
    // A conversation waiting for an answer that failed: listen again.
    if (convo.on && convo.waiting && !s.busy && reader.index < 0 && last && last.role !== "user" && last.state === "error") { convo.waiting = false; setTimeout(listenTurn, 300); }
    if (reader.index < 0) return;
    if (convo.on) convo.waiting = false;
    const m = msgs[reader.index], text = (m && m.text) || "";
    if (!m || m.role === "user" || m.state === "error" || !text.startsWith(reader.prefix)) { reader.stop(); return; }
    reader.complete = m.state !== "streaming";
    const n = readyPart(text, reader.done, reader.complete);
    if (n > 0) {
      if (!reader.lang) reader.lang = guessLang(text);
      if (reader.lang) sentences(speakable(text.slice(reader.done, reader.done + n))).forEach(t => reader.queue.push({ text: t, lang: reader.lang }));
      reader.done += n;
      reader.prefix = text.slice(0, reader.done);
      if (!reader.lang && reader.complete) { toast("No voice for this language yet"); reader.stop(); return; }
    }
    pump();
  };
  function start(i) {
    reader.stop();
    if (voice.state !== "ready") { offerVoices(); return; }
    Object.assign(reader, { index: i, done: 0, prefix: "", lang: "", queue: [], complete: false });
    reader.paint();
    reader.update(state);
  }
  function finish() {
    reader.index = -1; reader.paint(); pipSpeaking(false);
    if (convo.on) setTimeout(listenTurn, 300);   // the answer is read: Pip listens again
  }
  reader.stop = function (helperStopped) {
    reader.run++;
    const playing = reader.current && reader.current.source, native = reader.current && reader.current.playId;
    Object.assign(reader, { index: -1, queue: [], current: null });
    if (playing) { try { playing.stop(); } catch (e) {} }
    if ((native || pendingPlays.size) && !helperStopped) speechPost("hush");
    pendingPlays.clear();
    if (pipVoice) pipSpeaking(false);
    reader.paint();
  };
  reader.paint = function () {
    els.thread.querySelectorAll("[data-read]").forEach(b => {
      const on = +b.dataset.read === reader.index;
      b.classList.toggle("on", on);
      b.title = on ? "Stop reading" : "Read aloud";
    });
  };
  els.thread.addEventListener("click", e => {
    const b = e.target.closest && e.target.closest("[data-read]");
    if (!b) return;
    e.stopPropagation();
    if (reader.index === +b.dataset.read) { reader.autoFrom = Math.max(reader.autoFrom, reader.index); reader.stop(); }
    else start(+b.dataset.read);
  });
  // The voices are downloaded once; until then the speaker offers it.
  function offerVoices() {
    if (!voice.available) return;
    if (voice.state === "downloading") { renderVoiceProgress(); return; }
    showSpeechBox("Reading aloud needs the voices (" + mb(voice.size) + ", downloaded once): EMA Lightning for Turkish, Kokoro for English. They run on this Mac only.", { buttons: [
      { label: "Download", primary: true, run: async () => { await loadVoice("download"); watchVoiceDownload(); } },
      { label: "Not now", run: hideSpeechBox },
    ] });
  }
  function renderVoiceProgress() {
    const p = voice.size ? voice.received / voice.size : 0;
    showSpeechBox("Downloading the voices… " + mb(voice.received) + " of " + mb(voice.size), { progress: p,
      buttons: [{ label: "Cancel", run: async () => { await loadVoice("cancel"); hideSpeechBox(); } }] });
  }
  let voiceWatch = 0;
  function watchVoiceDownload() {
    clearTimeout(voiceWatch);
    if (voice.state === "downloading") {
      if (!settingsOpen) renderVoiceProgress();
      renderVoiceSettings();
      voiceWatch = setTimeout(async () => { await loadVoice(); watchVoiceDownload(); }, 700);
      return;
    }
    renderVoiceSettings();
    if (voice.state === "ready") { hideSpeechBox(); toast("Voices ready"); lastKey = ""; if (state) render(); }
    else if (voice.error) showSpeechBox(voice.error, { buttons: [{ label: "Close", run: hideSpeechBox }] });
  }
  loadVoice().then(v => {
    if (!v.available) return;
    lastKey = ""; if (state) render();   // the speakers appear
    if (v.state === "downloading") watchVoiceDownload();
    // Turkish answers read on their own: have autolex ready before the first one.
    if (state && state.readAloud && v.state === "ready" && parseLangs(state.speechLanguages).includes("tr")) loadLex();
  });

  // Settings → Read Aloud: the voices' download, an English voice, a test for each language.
  let readVoiceChoice = {};
  const SAMPLE = { tr: "Merhaba, cevapları ben okuyacağım. Service Layer bağlantısı hazır.", en: "Hello, I will read the answers to you." };
  function renderVoiceSettings() {
    const line = $("vModel"), btn = $("vModelBtn");
    if (!line) return;
    btn.hidden = false;
    if (!voice.available) { line.textContent = voice.state === "unsupported" ? "Needs a Mac with Apple silicon." : "Not available here."; btn.hidden = true; }
    else if (voice.state === "ready") { line.textContent = "EMA Lightning and Kokoro · on this Mac · " + mb(voice.size); btn.hidden = true; }
    else if (voice.state === "downloading") { line.textContent = "Downloading… " + mb(voice.received) + " of " + mb(voice.size); btn.textContent = "Cancel"; btn.dataset.do = "cancel"; }
    else { line.textContent = "Not downloaded yet · " + mb(voice.size); btn.textContent = "Download"; btn.dataset.do = "download"; }
    renderReadVoices();
  }
  $("vModelBtn").addEventListener("click", async e => { await loadVoice(e.currentTarget.dataset.do); watchVoiceDownload(); });
  function renderReadVoices() {
    const box = $("readVoices");
    if (!box) return;
    box.textContent = "";
    const rows = [["tr", "Turkish", [["", "EMA Lightning"]]], ["en", "English", (voice.voices && voice.voices.en || []).map(v => [v.id, v.name])]];
    rows.forEach(([code, label, options]) => {
      const name = document.createElement("span");
      name.textContent = label;
      box.appendChild(name);
      const sel = document.createElement("select");
      options.forEach(([id, title]) => sel.appendChild(new Option(title, id)));
      sel.value = options.some(([id]) => id === readVoiceChoice[code]) ? readVoiceChoice[code] : options.length ? options[0][0] : "";
      sel.disabled = options.length < 2;
      sel.addEventListener("change", () => { readVoiceChoice[code] = sel.value; });
      box.appendChild(sel);
      const test = document.createElement("button");
      test.type = "button"; test.textContent = "Test";
      test.disabled = voice.state !== "ready";
      test.addEventListener("click", async () => {
        reader.stop();
        test.disabled = true;
        const got = await fetchAudio({ text: SAMPLE[code], lang: code }, readVoiceChoice, +$("readRate").value).catch(() => null);
        test.disabled = false;
        if (!got) { toast("Could not read it"); return; }
        const source = audioCtx.createBufferSource();
        source.buffer = got.buffer; source.connect(audioCtx.destination); source.start();
      });
      box.appendChild(test);
    });
    queueLayout();
  }

  // ---------- conversation mode: listen, send, read the answer aloud, listen again ----------
  // A turn ends on a pause (Settings → Read Aloud) or when the user says so: the shortcut, ↵, a click on Pip.
  // While Pip speaks, the helper plays the answer with echo cancellation and watches the microphone: speaking
  // over Pip stops it and the turn starts there ("barge"); the shortcut or a click does the same by hand.
  const convo = { on: false, heard: false, activeAt: 0, partial: "", waiting: false };
  let conversationAsked = false;
  const convoEnd = () => (state && state.conversationEnd === "manual" ? "manual" : "silence");
  convo.onLevel = v => { if (convo.on && v > 0.35) { convo.heard = true; convo.activeAt = performance.now(); } };
  convo.onPartial = t => {
    if (!convo.on || t === convo.partial) return;
    convo.partial = t;
    if (t) { convo.heard = true; convo.activeAt = Math.max(convo.activeAt, performance.now() - 500); }
  };
  function startConversation() {
    if (convo.on) return;
    if (!speech.available) { if (collapsed) expand(); toast("Conversation needs dictation, on a Mac with Apple silicon"); return; }
    if (speech.model !== "ready") { if (collapsed) expand(); speech.model === "downloading" ? renderSpeechBoxProgress() : showModelOffer(); return; }
    if (voice.state !== "ready") { if (collapsed) expand(); offerVoices(); return; }
    convo.on = true; convo.waiting = false;
    els.root.classList.add("conversing");
    reader.stop();
    toast(convoEnd() === "manual" ? "Talk to Pip, then press ↵ or click Pip" : "Talk to Pip; a pause sends it");
    listenTurn();
  }
  function listenTurn() {
    if (!convo.on || speech.state !== "idle" || reader.index >= 0) return;
    convo.heard = false; convo.activeAt = 0; convo.partial = "";
    startDictation();
  }
  function sendTurn() {
    if (speech.state !== "listening" && speech.state !== "starting") return;
    convo.waiting = true;
    stopDictation(true);
  }
  function bargeIn() {
    reader.autoFrom = Math.max(reader.autoFrom == null ? -1 : reader.autoFrom, reader.index);
    reader.stop();
    setTimeout(listenTurn, 150);
  }
  function endConversation() {
    if (!convo.on) return;
    convo.on = false; convo.waiting = false;
    els.root.classList.remove("conversing");
    if (speech.state !== "idle") cancelDictation();
    reader.stop();
    toast("Conversation ended");
  }
  // On a pause: 1.5 s without sound or new words after something was said.
  setInterval(() => {
    if (!convo.on || convoEnd() !== "silence" || speech.state !== "listening" || !convo.heard) return;
    if (performance.now() - convo.activeAt > 1500 && els.prompt.value.trim()) sendTurn();
  }, 200);
  // Esc ends the conversation (before the dictation's own Esc, which only cancels the recording).
  window.addEventListener("keydown", e => {
    if (e.key === "Escape" && convo.on) { e.preventDefault(); e.stopImmediatePropagation(); endConversation(); }
  }, true);
  // The conversation shortcut (from the app). Returns "quiet" when the panel must not take the keys (Pip alone).
  window.__quickPromptConverse = () => {
    if (!speech.known) { speech.startWhenKnown = "converse"; return collapsed ? "quiet" : "open"; }
    if (!convo.on) startConversation();
    else if (speech.state === "listening" || speech.state === "starting") { if (els.prompt.value.trim()) sendTurn(); else endConversation(); }
    else if (reader.index >= 0) bargeIn();
    return collapsed ? "quiet" : "open";
  };

  // ---------- long-poll ----------
  async function poll() {
    while (true) {
      try {
        const res = await fetch(base + "state?v=" + version + (isTab ? "&tab=1" : ""), { cache: "no-store" });
        if (res.status === 404) { sessionEnded(); return; }
        state = await res.json();
        version = state.v;
        render();
        reader.update(state);
      } catch (e) {
        els.card.hidden = false;
        if (!sizeKnown) applySize(30);
        els.status.textContent = "No connection — is the plugin running?";
        queueLayout();
        await new Promise(r => setTimeout(r, 1500));
      }
    }
  }
  queueLayout();
  poll();
})();
</script>
</body>
</html>
""";
    }
}
