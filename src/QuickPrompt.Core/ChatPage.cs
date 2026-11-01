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
/* The conversation slides out of the icon and back into it. */
#root { --reveal-dx: -18px; } #root.side-right { --reveal-dx: 18px; }
#main.reveal-in { animation: qp-reveal-in .24s cubic-bezier(.2, .8, .2, 1) both; }
#main.reveal-out { animation: qp-reveal-out .2s ease-in both; }
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
/* While the conversation scrolls, the bubbles draw their own background (the native glass cannot
   follow a scroll smoothly); the glass comes back as soon as it stops. */
html.native .thread.scrolling .msg { background: rgba(var(--tint-rgb), max(.62, var(--glass-tint, .3))); border-color: var(--edge); }
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
textarea::placeholder { color: var(--muted); }
.bar textarea:focus-visible, .ctx textarea:focus-visible { outline: none; }
.round[hidden] + textarea { padding-left: 12px; }

/* Settings view (long: scrolls inside a screen-high limit, set from the screen size at start) */
/* Settings: a sidebar of sections (like a desktop app's settings) and the chosen section on the right. */
.settings { border-radius: 22px; padding: 0; display: flex; flex-direction: column; overflow: hidden; }
.shead { display: flex; align-items: center; gap: 8px; padding: 12px 14px 10px 18px; border-bottom: 1px solid var(--faint); }
.shead .title { font-weight: 600; font-size: 14px; }
.shead .status { flex: 1; font-size: 11.5px; color: var(--muted); }
.sbody { display: grid; grid-template-columns: 186px 1fr; height: min(500px, calc(var(--view-max, 760px) - 120px)); min-height: 320px; }
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

/* Fields of a prompt template */
.fields { border-radius: 20px; padding: 9px 14px 12px; }
.ftitle { font-size: 12px; font-weight: 600; color: var(--muted); margin-bottom: 2px; }
.flist { display: grid; grid-template-columns: repeat(auto-fill, minmax(180px, 1fr)); gap: 0 10px; }
.flist .sfield { margin-top: 6px; }
.flist .sfield.missing input { box-shadow: inset 0 0 0 1.5px var(--error); }
.aform { padding: 0 6px; }
.aform textarea { display: block; width: 100%; margin-top: 3px; font: 13px/1.45 ui-monospace, SFMono-Regular, Menlo, monospace; color: var(--text); background: var(--faint); border: 0; border-radius: 9px; padding: 7px 10px; outline: none; resize: vertical; min-height: 96px; max-height: 260px; }
.aform textarea:focus-visible { box-shadow: inset 0 0 0 1.5px var(--accent); }
.frow { display: flex; gap: 10px; } .frow .sfield { flex: 1; }
.aitem .hq { font-weight: 600; }
.aitem .run { flex: none; align-self: center; font-size: 11.5px; padding: 3px 10px; }
.pedit .pname { color: var(--muted); font-size: 12px; }

.srow.sub { margin-top: 10px; } .srow.sub .sname { font-weight: 500; font-size: 13px; }
.setup { margin-top: 6px; padding: 6px 10px 8px; background: var(--faint); border-radius: 12px; }
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
.minibar button { background: transparent; color: var(--text); width: 32px; height: 28px; padding: 0; border-radius: 999px; display: grid; place-items: center; }
.minibar button:hover { background: var(--faint); }
.minibar .sep { width: 1px; height: 16px; background: var(--faint); }
.minibar { transition: padding .25s ease, width .25s ease, height .25s ease; overflow: hidden; }
.minibar.tucked { padding: 0; width: 34px; height: 6px; margin-top: -4px; cursor: pointer; }
.minibar.tucked > * { display: none; }
.plist { width: 250px; border-radius: 18px; padding: 8px 6px; margin-top: 6px; }
.plist .ptitle { font-size: 11.5px; font-weight: 600; color: var(--muted); padding: 2px 10px 6px; }
.pitem { display: flex; align-items: center; gap: 9px; width: 100%; background: transparent; border-radius: 12px; padding: 7px 10px; text-align: left; }
.pitem:hover, .pitem:focus-visible { background: var(--faint); }
.pitem .dot { flex: none; }
.pitem[data-provider="codex"] .dot { background: #10a37f; }
.pitem[data-provider="claude"] .dot { background: #d97757; }
.pitem .pname { flex: 1; min-width: 0; font-size: 13px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.pitem .pmeta { font-size: 11px; color: var(--muted); white-space: nowrap; }
.bubble { max-width: 300px; border-radius: 18px; padding: 9px 14px 10px; text-align: left; margin-top: 10px; cursor: pointer; }
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
    <button type="button" class="mascot" id="miniOpen" title="Open (drag to move)"><svg viewBox="0 0 24 24" width="56" height="56">
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
      <button type="button" class="x" id="pinBtn" title="Pin: Esc and ✕ keep it as a small icon" hidden><svg viewBox="0 0 24 24" width="13" height="13" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M9 4h6l-1 6 3 3H7l3-3-1-6z M12 16v5"/></svg></button>
      <button type="button" class="x" id="metaHistory" title="Recent conversations"><svg viewBox="0 0 24 24" width="13" height="13" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M3.5 12a8.5 8.5 0 1 0 2.5-6M3.5 4.5V8H7M12 7.5V12l3 2"/></svg></button>
      <button type="button" class="x" id="gear" title="Settings"><svg viewBox="0 0 24 24" width="13" height="13" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"><path d="M12 9a3 3 0 1 0 0 6 3 3 0 0 0 0-6z M19.4 13.5l1.6 1.2-2 3.4-1.9-.7a7.5 7.5 0 0 1-2 1.2l-.3 2h-3.9l-.3-2a7.5 7.5 0 0 1-2-1.2l-1.9.7-2-3.4 1.6-1.2a7.6 7.6 0 0 1 0-3l-1.6-1.2 2-3.4 1.9.7a7.5 7.5 0 0 1 2-1.2l.3-2h3.9l.3 2a7.5 7.5 0 0 1 2 1.2l1.9-.7 2 3.4-1.6 1.2a7.6 7.6 0 0 1 0 3z"/></svg></button>
      <button type="button" class="x" id="close" title="Close (Esc)">✕</button>
    </div>
    <div class="notice glass" id="notice" hidden></div>
    <div class="thread" id="thread"></div>
    <div class="actions" id="errActions" hidden><button type="button" id="errSettings">Open settings</button></div>
    <div class="actions" id="actions" hidden>
      <button type="button" id="copy">Copy</button>
      <button type="button" id="paste" title="Closes the popup and pastes the answer into the app you were using">Paste<kbd id="pasteKey">⌘↵</kbd></button>
    </div>
  </div>
  <div class="settings glass" id="settings" hidden>
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
        <div class="snav-g">AI</div>
        <button type="button" data-pane="claude"><svg class="plogo" viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="#D97757" stroke-width="1.7" stroke-linejoin="round" aria-hidden="true"><path d="M10 3.5l1.6 4.4 4.4 1.6-4.4 1.6L10 15.5l-1.6-4.4L4 9.5l4.4-1.6z M17.5 13.5l.8 2.2 2.2.8-2.2.8-.8 2.2-.8-2.2-2.2-.8 2.2-.8z"/></svg>Claude</button>
        <button type="button" data-pane="chatgpt"><svg class="plogo" viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="#10A37F" stroke-width="1.7" stroke-linejoin="round" aria-hidden="true"><path d="M10 3.5l1.6 4.4 4.4 1.6-4.4 1.6L10 15.5l-1.6-4.4L4 9.5l4.4-1.6z M17.5 13.5l.8 2.2 2.2.8-2.2.8-.8 2.2-.8-2.2-2.2-.8 2.2-.8z"/></svg>ChatGPT</button>
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
            <div><div class="sl-t">Ask about the selection</div><div class="sl-d">⌥⇧Space in any app opens Quick Prompt with the selected text. The menu bar icon (✨) has New Chat, Recent Chats, Prompt Templates and Settings.</div></div>
          </div>
        </section>
        <section class="spane" data-pane="appearance">
          <h2>Appearance</h2>
          <div class="sline">
            <div><div class="sl-t">Chat size</div><div class="sl-d">The width and height of the conversation.</div></div>
            <div class="slider"><span class="sz small">A</span><input type="range" id="sizeSlider" min="0" max="100" step="1" aria-label="Chat size"><span class="sz big">A</span></div>
          </div>
          <div class="sline">
            <div><div class="sl-t">Icon size</div><div class="sl-d">The floating icon and its toolbar.</div></div>
            <div class="slider"><span class="sz small">A</span><input type="range" id="iconSlider" min="0" max="100" step="1" aria-label="Icon size"><span class="sz big">A</span></div>
          </div>
          <div class="sline">
            <div><div class="sl-t">Glass</div><div class="sl-d">From clear to dense, for easier reading. You see it as you move the slider.</div></div>
            <div class="slider"><span class="sz small" title="Clear">◌</span><input type="range" id="glassSlider" min="0" max="100" step="1" aria-label="Glass density"><span class="sz big" title="Dense">●</span></div>
          </div>
        </section>
        <section class="spane" data-pane="claude">
          <h2>Claude</h2>
          <div class="sline">
            <div><div class="sl-t">Connection</div><div class="sl-d">CLI uses your Claude plan through Claude Code. API is faster and billed per use to your API account.</div></div>
            <div class="seg" data-name="claudeConnection"><button type="button" data-v="cli">CLI</button><button type="button" data-v="api">API</button></div>
          </div>
      <div class="api-only" data-for="claudeConnection">
      <div class="skey"><label class="sfield">Anthropic API key<input type="password" id="claudeKey" autocomplete="off" spellcheck="false" placeholder="sk-ant-…"></label><button type="button" data-clear="claudeKey" hidden>Remove</button></div>
      <label class="sfield">API model<select id="claudeModel">
        <option value="claude-opus-5-5">Claude Opus 5.5</option>
        <option value="claude-sonnet-5-5">Claude Sonnet 5.5</option>
        <option value="claude-haiku-5-5">Claude Haiku 5.5 — fastest</option>
      </select></label>
      </div>
      <div class="cli-only" data-for="claudeConnection">
        <div class="srow sub"><span class="sname">Use my Claude Code setup</span>
          <div class="seg" data-name="claudeSetupOn"><button type="button" data-v="off">Off</button><button type="button" data-v="on">On</button></div></div>
        <div class="shint">Off: plain answers, fastest. On: the plugins, skills and MCP servers you tick can be used (slower). It can look things up and use the servers' tools, but cannot run commands or change files.</div>
        <div class="setup" data-setup="claude" data-for="claudeSetupOn" hidden></div>
      </div>
        </section>
        <section class="spane" data-pane="chatgpt">
          <h2>ChatGPT</h2>
          <div class="sline">
            <div><div class="sl-t">Connection</div><div class="sl-d">CLI uses your ChatGPT plan through the Codex CLI. API is faster and billed per use to your OpenAI account.</div></div>
            <div class="seg" data-name="chatgptConnection"><button type="button" data-v="cli">CLI</button><button type="button" data-v="api">API</button></div>
          </div>
      <div class="api-only" data-for="chatgptConnection">
      <div class="skey"><label class="sfield">OpenAI API key<input type="password" id="chatgptKey" autocomplete="off" spellcheck="false" placeholder="sk-…"></label><button type="button" data-clear="chatgptKey" hidden>Remove</button></div>
      <label class="sfield">API model<input type="text" id="chatgptModel" spellcheck="false" placeholder="gpt-5-mini"></label>
      </div>
      <div class="cli-only" data-for="chatgptConnection">
        <div class="srow sub"><span class="sname">Use my Codex setup</span>
          <div class="seg" data-name="chatgptSetupOn"><button type="button" data-v="off">Off</button><button type="button" data-v="on">On</button></div></div>
        <div class="shint">Off: none of your Codex MCP servers start (faster). On: the ones you tick are available. Codex stays read-only.</div>
        <div class="setup" data-setup="chatgpt" data-for="chatgptSetupOn" hidden></div>
      </div>
        </section>
        <section class="spane" data-pane="templates">
          <h2>Prompt templates</h2>
          <div class="sl-d">Your own prompts, with fields to fill in. Run them from the floating icon or the menu bar. Each one can choose its own plugins, MCP servers and skills.</div>
          <div class="stemplates" id="setTemplates"></div>
        </section>
        <section class="spane" data-pane="about">
          <h2>About</h2>
          <div class="sline"><div><div class="sl-t">Quick Prompt</div><div class="sl-d" id="aboutVersion"></div></div></div>
          <div class="sline"><div><div class="sl-t">Your keys</div><div class="sl-d">API keys are stored encrypted on this computer and are only sent to Anthropic or OpenAI. A prompt template can override CLI or API for itself.</div></div></div>
          <div class="sline"><div><div class="sl-t">Source and updates</div><div class="sl-d">github.com/ismailorhan/quick-prompt-for-logi-action-rings, under Releases → Latest build</div></div></div>
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
    <div class="actions" id="actButtons"><button type="button" class="primary" id="actNew">New template</button><button type="button" id="actBack">Back</button></div>
    <div class="aform" id="actForm" hidden>
      <label class="sfield">Title<input type="text" id="afTitle" spellcheck="false" placeholder="e.g. Reply to email"></label>
      <label class="sfield">Prompt<textarea id="afPrompt" rows="5" spellcheck="false" placeholder="Write a {tone:friendly|formal|short} reply to this email:&#10;&#10;{text}"></textarea></label>
      <div class="shint"><b>{text}</b> is the selected text. Add a field to fill in with <b>{name}</b>, or a list with <b>{tone:friendly|formal|short}</b>. Fields are asked each time the template runs.</div>
      <div class="srow" style="margin-top:10px"><span class="sname">AI</span>
        <div class="seg" data-name="afProvider"><button type="button" data-v="claude">Claude</button><button type="button" data-v="codex">ChatGPT</button></div></div>
      <div class="frow">
        <label class="sfield">Text from<select id="afInput"><option value="selection">Selected text</option><option value="clipboard">Clipboard</option><option value="popup">Paste in popup</option><option value="none">Nothing</option></select></label>
        <label class="sfield">Connection<select id="afConnection"><option value="default">As in Settings</option><option value="cli">CLI</option><option value="api">API</option></select></label>
      </div>
      <label class="sfield">Model (optional)<input type="text" id="afModel" spellcheck="false" placeholder="Default"></label>
      <div class="srow sub"><span class="sname">Plugins, MCP servers and skills</span>
        <div class="seg" data-name="afSetup"><button type="button" data-v="default">As in Settings</button><button type="button" data-v="off">None</button><button type="button" data-v="custom">Choose</button></div></div>
      <div class="shint" id="afSetupHint"></div>
      <div class="setup" id="afSetupBox" data-setup="claude" hidden></div>
      <div class="actions"><button type="button" class="primary" id="afSave">Save</button><button type="button" id="afCancel">Cancel</button><span style="flex:1"></span><button type="button" id="afDelete">Delete</button></div>
    </div>
  </div>
  <div class="fields glass" id="fieldsBox" hidden><div class="ftitle" id="fieldsTitle"></div><div class="flist" id="fieldList"></div></div>
  <div class="ctx glass" id="ctxWrap" hidden><textarea id="ctx" rows="3" placeholder="Paste the text here…"></textarea></div>
  <div class="bar glass" id="bar" data-drag>
    <button type="button" class="round" id="ctxBtn" title="Add text">+</button>
    <textarea id="prompt" rows="1" placeholder="Ask…"></textarea>
    <button type="button" class="round gear" id="barPin" title="Pin: Esc and ✕ keep it as a small icon" hidden><svg viewBox="0 0 24 24" width="17" height="17" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M9 4h6l-1 6 3 3H7l3-3-1-6z M12 16v5"/></svg></button>
    <button type="button" class="round gear" id="barHistory" title="Recent conversations"><svg viewBox="0 0 24 24" width="17" height="17" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M3.5 12a8.5 8.5 0 1 0 2.5-6M3.5 4.5V8H7M12 7.5V12l3 2"/></svg></button>
    <button type="button" class="round gear" id="barGear" title="Settings"><svg viewBox="0 0 24 24" width="17" height="17" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"><path d="M12 9a3 3 0 1 0 0 6 3 3 0 0 0 0-6z M19.4 13.5l1.6 1.2-2 3.4-1.9-.7a7.5 7.5 0 0 1-2 1.2l-.3 2h-3.9l-.3-2a7.5 7.5 0 0 1-2-1.2l-1.9.7-2-3.4 1.6-1.2a7.6 7.6 0 0 1 0-3l-1.6-1.2 2-3.4 1.9.7a7.5 7.5 0 0 1 2-1.2l.3-2h3.9l.3 2a7.5 7.5 0 0 1 2 1.2l1.9-.7 2 3.4-1.6 1.2a7.6 7.6 0 0 1 0 3z"/></svg></button>
    <button type="button" class="send idle" id="send" title="Send (Enter)"></button>
  </div>
  </div>
  <div class="plist glass" id="presetList" hidden><div class="ptitle">Prompt templates</div><div id="plistItems"></div><button type="button" class="pitem pedit" id="plistEdit"><span class="pname">Edit templates…</span></button></div>
  <div class="bubble glass" id="bubble" hidden><div class="bt"><span class="ok" id="bubbleIcon">✓</span><span id="bubbleTitle"></span></div><div class="bb" id="bubbleText"></div></div>
</div>
<div class="toast" id="toast"></div>
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

  let state = null, version = 0, draftApplied = false, lastKey = "", stick = true;

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
    return fetch(base + action, { method: "POST", headers: { "Content-Type": "application/json", "X-QuickPrompt": "1" }, body: JSON.stringify(body || {}) });
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
    post("paste", { text }).finally(closeWindow);
  }

  // ---------- native layout: panel height + glass shapes ----------
  // Popup size (Settings slider, 0 = smallest … 100 = largest): panel width and answer height.
  let lastStateScale = null, lastStateIcon = null;
  let scale = -1, size = { w: 520, h: 380 }, sizeKnown = false, collapsed = false, pinned = false;
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

  function applyIconScale(value) {
    const v = Math.max(0, Math.min(100, Number.isFinite(+value) ? +value : 38));
    document.documentElement.style.setProperty("--mini-zoom", (0.7 + v * 0.008).toFixed(3));
    queueLayout();
  }

  let layoutQueued = false, lastLayout = "", layoutAfterReveal = false;
  let revealing = false, revealNext = null;
  function queueLayout() {
    if (!bridge || layoutQueued || !sizeKnown) return;
    if (revealing) { layoutAfterReveal = true; return; }   // positions are mid-slide; measure when done
    layoutQueued = true;
    requestAnimationFrame(() => {
      layoutQueued = false;
      const view = els.thread.getBoundingClientRect();
      const scrolling = els.thread.classList.contains("scrolling");
      const rects = [...document.querySelectorAll(".glass")]
        .filter(el => !el.hidden && el.offsetParent !== null && !(scrolling && els.thread.contains(el)))
        .map(el => {
          const r = el.getBoundingClientRect();
          let top = r.top, bottom = r.bottom;
          // Bubbles scroll inside the thread: only the visible part gets native glass.
          if (els.thread.contains(el)) { top = Math.max(top, view.top); bottom = Math.min(bottom, view.bottom); }
          return { x: r.left, y: top, w: r.width, h: Math.max(0, bottom - top), r: Math.min(parseFloat(getComputedStyle(el).borderTopLeftRadius) || 0, r.height / 2) };
        })
        .filter(r => r.h > 1);
      const box = els.root.getBoundingClientRect();
      const height = Math.ceil(box.height);
      const side = mini.hidden ? 0 : Math.ceil(mini.getBoundingClientRect().width) + 10;
      const width = collapsed ? Math.ceil(box.width) : (settingsOpen ? 760 : actionsOpen ? 520 : size.w) + side;
      // Where the icon's middle will be in a panel of this width, so the panel can keep the icon still
      // on screen while things around it change (toolbar opening, conversation opening on either side).
      const icon = mini.hidden ? null : $("miniOpen").getBoundingClientRect();
      const anchorY = icon ? icon.top + icon.height / 2 - box.top : null;
      const anchorX = !icon ? null
        : els.root.classList.contains("side-right") ? width - (box.right - (icon.left + icon.width / 2))
        : icon.left + icon.width / 2 - box.left;
      const key = JSON.stringify([width, height, rects, anchorX, anchorY]);
      if (key === lastLayout) return;
      lastLayout = key;
      bridge.postMessage({ type: "layout", width, height, rects, anchorX, anchorY });
      if (revealNext) startReveal(revealNext);
    });
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
  const set = { view: $("settings"), status: $("setStatus"), claudeKey: $("claudeKey"), chatgptKey: $("chatgptKey"),
                claudeModel: $("claudeModel"), chatgptModel: $("chatgptModel") };
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
    if (name === "afSetup" || name === "afProvider") refreshActionSetup();
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
      setSeg("claudeConnection", s.claude.connection);
      setSeg("chatgptConnection", s.chatgpt.connection);
      setSeg("saveHistory", s.saveHistory ? "true" : "false");
      setSlider(s.popupScale);
      setIconSlider(s.iconScale);
      savedGlass = s.glassTint; setGlassSlider(s.glassTint);
      showKeyHint(set.claudeKey, s.claude.keyHint);
      showKeyHint(set.chatgptKey, s.chatgpt.keyHint);
      if (![...set.claudeModel.options].some(o => o.value === s.claude.model)) {
        set.claudeModel.add(new Option(s.claude.model, s.claude.model));
      }
      set.claudeModel.value = s.claude.model;
      set.chatgptModel.value = s.chatgpt.model;
      setupData = null;
      setSeg("claudeSetupOn", s.claude.setup ? "on" : "off");
      setSeg("chatgptSetupOn", s.chatgpt.setup ? "on" : "off");
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
  function renderSetup() {
    document.querySelectorAll(".setup").forEach(el => {
      const d = withChoice(setupData && setupData[el.dataset.setup], el.choice);
      if (!d) { el.innerHTML = '<div class="shint">Could not read your setup.</div>'; return; }
      if (!d.installed) { el.innerHTML = '<div class="shint">The CLI was not found on this computer.</div>'; return; }
      let html = "";
      if (el.dataset.setup === "claude") {
        html += '<div class="stitle">Plugins</div>' + (d.plugins.length
          ? d.plugins.map(p => check("plugin", p.id, p.id.replace(/@.*$/, ""), p.on, !p.cliEnabled, p.cliEnabled ? "" : "off in Claude Code")).join("")
          : '<div class="shint">No plugins installed.</div>');
      }
      html += '<div class="stitle">MCP servers</div>' + (d.servers.length
        ? d.servers.map(s => check("server", s.name, s.name, s.on)).join("")
        : '<div class="shint">No MCP servers in your ' + (el.dataset.setup === "claude" ? "user configuration (~/.claude.json)" : "~/.codex/config.toml") + '.</div>');
      const skillList = (d.skillList || []);
      if (el.dataset.setup === "claude") {
        html += '<div class="stitle">Skills</div>' + check("skills", "skills", "Use skills", d.skills, false, "built-in Claude Code skills follow this switch");
      } else {
        html += '<div class="stitle">Skills</div>';
      }
      html += '<div class="skills"' + (el.dataset.setup === "claude" && !d.skills ? ' hidden' : '') + '>' + (skillList.length
        ? skillList.map(k => check("skill", k.name, k.name, k.on, false, k.plugin ? "from " + k.plugin.replace(/@.*$/, "") : "")).join("")
        : '<div class="shint">No skills found.</div>') + '</div>';
      if (el.dataset.setup === "claude") {
        html += '<div class="stitle">Other</div>' + check("web", "web", "Web search and fetch", d.web);
      }
      html += '<div class="shint">' + (d.error ? esc(d.error) + " " : "") + '<a href="#" data-refresh>Refresh</a></div>';
      el.innerHTML = html;
    });
    queueLayout();
  }
  document.addEventListener("change", e => {
    const t = e.target;
    if (!(t instanceof HTMLInputElement) || !t.dataset.kind) return;
    const box = t.closest(".setup");
    if (t.dataset.kind === "skills") { box.querySelector(".skills").hidden = !t.checked; queueLayout(); }
    if (t.dataset.kind === "plugin") {
      // A plugin's skills go with it.
      box.querySelectorAll('input[data-kind="skill"]').forEach(s => {
        const k = ((setupData[box.dataset.setup] || {}).skillList || []).find(x => x.name === s.dataset.id);
        if (k && k.plugin === t.dataset.id) { s.disabled = !t.checked; s.parentElement.classList.toggle("off", !t.checked); }
      });
    }
  });
  document.addEventListener("click", e => {
    const a = e.target.closest("[data-refresh]");
    if (a) { e.preventDefault(); loadSetup(true); }
  });
  function setupChoice(name) {
    return choiceFrom(document.querySelector('.setup[data-for][data-setup="' + name + '"]'), segValues[name + "SetupOn"] === "on");
  }
  // Reads the ticks of one setup box (settings or an action) as a choice.
  function choiceFrom(el, on) {
    const name = el.dataset.setup;
    if (!setupData || !setupData[name] || !setupData[name].installed || !el.querySelector("input")) return JSON.stringify(Object.assign({}, el.choice || {}, { enabled: on }));
    const box = (kind, id) => el.querySelector('input[data-kind="' + kind + '"][data-id="' + CSS.escape(id) + '"]');
    const d = setupData[name];
    const off = (kind, ids) => ids.filter(id => { const b = box(kind, id); return b && !b.disabled && !b.checked; });
    const skills = box("skills", "skills"), web = box("web", "web");
    return JSON.stringify({
      enabled: on,
      disabledPlugins: off("plugin", (d.plugins || []).map(p => p.id)),
      disabledServers: off("server", d.servers.map(s => s.name)),
      disabledSkills: off("skill", (d.skillList || []).map(k => k.name)),
      skills: skills ? skills.checked : d.skills,
      web: web ? web.checked : d.web,
    });
  }

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
      claudeConnection: segValues.claudeConnection, chatgptConnection: segValues.chatgptConnection,
      claudeModel: set.claudeModel.value, chatgptModel: set.chatgptModel.value.trim(),
      claudeKey: keyValue(set.claudeKey), chatgptKey: keyValue(set.chatgptKey),
      saveHistory: segValues.saveHistory,
      popupScale: String(sizeSlider.value),
      iconScale: String(iconSlider.value),
      glassTint: String(glassSlider.value),
      claudeSetup: setupChoice("claude"), chatgptSetup: setupChoice("chatgpt"),
    };
    const res = await post("settings", body);
    if (!res.ok) { set.status.textContent = "Could not save"; return; }
    try { const saved = await res.json(); savedScale = saved.popupScale; applySize(savedScale); applyIconScale(saved.iconScale); savedGlass = saved.glassTint; applyGlass(savedGlass); } catch (e) {}
    toast("Saved");
    closeSettings();
  }

  $("gear").addEventListener("click", () => openSettings(false));
  $("barGear").addEventListener("click", () => openSettings(false));
  $("errSettings").addEventListener("click", () => openSettings(false, state && state.provider === "codex" ? "chatgpt" : "claude"));
  $("setSave").addEventListener("click", saveSettings);
  $("setBack").addEventListener("click", closeSettings);
  $("setClose").addEventListener("click", () => settingsOnly ? closeWindow() : dismiss());

  // ---------- pin, collapse to an icon, drag (native popup only) ----------
  const mini = $("mini");
  function setPinned(value) {
    pinned = value;
    ["pinBtn", "barPin"].forEach(id => $(id).classList.toggle("pinned", pinned));
    if (bridge) bridge.postMessage({ type: "pin", value: pinned });
    toast(pinned ? "Pinned: Esc keeps it as an icon" : "Unpinned");
  }

  const bubble = $("bubble");
  let lastBusy = false;

  function collapse() {
    collapsed = true;
    setTimeout(() => globe.start(), 0);
    bubble.hidden = true;
    $("presetList").hidden = true;
    if (bridge) bridge.postMessage({ type: "mode", value: "mini" });
    [els.card, els.bar, els.ctxWrap, set.view, $("historyView")].forEach(el => el.hidden = true);
    settingsOpen = false; historyOpen = false;
    els.root.classList.add("is-mini");
    mini.hidden = false;
    lastLayout = "";
    queueLayout();
    $("minibar").classList.remove("tucked");
    clearTimeout(window.__qpTuck);
    window.__qpTuck = setTimeout(() => { if (collapsed && $("presetList").hidden) { $("minibar").classList.add("tucked"); queueLayout(); } }, 3000);
  }

  function expand() {
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
    revealNext = bridge ? "in" : null;   // slide out of the icon once the panel has its new size
    if (state) render();
    queueLayout();
    setTimeout(() => els.prompt.focus(), 0);
  }

  // The slide: the page moves #main with CSS, the native panel moves the glass behind it the same way.
  function startReveal(dir) {
    revealNext = null;
    const main = $("main");
    const dx = els.root.classList.contains("side-right") ? 18 : -18;
    main.classList.remove("reveal-in", "reveal-out");
    void main.offsetWidth;
    main.classList.add(dir === "in" ? "reveal-in" : "reveal-out");
    revealing = true;
    if (bridge) bridge.postMessage({ type: "reveal", dir, dx, duration: dir === "in" ? 0.24 : 0.2 });
    clearTimeout(startReveal.t);
    startReveal.t = setTimeout(() => {
      revealing = false;
      if (dir === "in") main.classList.remove("reveal-in");
      if (layoutAfterReveal) { layoutAfterReveal = false; lastLayout = ""; queueLayout(); }
    }, dir === "in" ? 260 : 220);
  }

  /// Tucks the conversation back into the icon with the slide.
  function collapseAnimated() {
    if (collapsed) return;
    if (!bridge) { collapse(); return; }
    startReveal("out");
    setTimeout(() => { $("main").classList.remove("reveal-out"); collapse(); }, 210);
  }

  /// Esc / ✕: a pinned popup shrinks to an icon, otherwise it closes.
  function dismiss() {
    if (bridge && pinned) collapseAnimated(); else closeWindow();
  }

  $("pinBtn").addEventListener("click", () => setPinned(!pinned));
  $("barPin").addEventListener("click", () => setPinned(!pinned));
  bubble.addEventListener("click", expand);

  // Toolbar under the icon: tucks into a thin line after a few seconds; hover or click brings it back.
  const minibar = $("minibar");
  const presetList = $("presetList");
  let tuckTimer = null;
  function scheduleTuck() {
    clearTimeout(tuckTimer);
    tuckTimer = setTimeout(() => {
      if (collapsed && presetList.hidden) { minibar.classList.add("tucked"); queueLayout(); }
    }, 3000);
  }
  function untuck() {
    clearTimeout(tuckTimer);
    if (minibar.classList.contains("tucked")) { minibar.classList.remove("tucked"); queueLayout(); }
  }
  mini.addEventListener("mouseenter", untuck);
  mini.addEventListener("mouseleave", scheduleTuck);
  minibar.addEventListener("click", e => {
    if (minibar.classList.contains("tucked")) { e.stopPropagation(); untuck(); scheduleTuck(); }
  }, true);

  $("miniRecent").addEventListener("click", () => { expand(); openHistory(false); });

  async function togglePresets() {
    if (!presetList.hidden) { presetList.hidden = true; queueLayout(); scheduleTuck(); return; }
    bubble.hidden = true;
    let items = [];
    try { items = await (await fetch(base + "presets", { cache: "no-store" })).json(); } catch (e) {}
    $("plistItems").innerHTML = items.length ? items.map(p =>
      '<button type="button" class="pitem" data-id="' + esc(p.id) + '" data-provider="' + esc(p.provider) + '">'
      + '<span class="dot"></span><span class="pname">' + esc(p.title) + '</span>'
      + '<span class="pmeta">' + esc(p.providerName) + (p.connection === "api" ? " · API" : "") + '</span></button>').join("")
      : '<div class="hempty">No templates yet.</div>';
    presetList.hidden = false;
    clearTimeout(tuckTimer);
    queueLayout();
  }
  $("miniActions").addEventListener("click", togglePresets);
  $("plistEdit").addEventListener("click", () => { presetList.hidden = true; expand(); openActions(false); });
  $("plistItems").addEventListener("click", async e => {
    const item = e.target.closest(".pitem");
    if (!item) return;
    const res = await post("usepreset", { id: item.dataset.id });
    if (res.ok) { stick = true; expand(); } else { toast("Still answering"); }
  });

  // Right click on the icon: native menu (Open, Settings, Close).
  // Right click (on the icon or anywhere that is not text): the native menu with everything that used
  // to sit in the popup's top row.
  function menuItems() {
    return [
      ...(collapsed ? [{ id: "open", title: "Open" }] : []),
      { id: "new", title: "New Chat" },
      { id: "recent", title: "Recent Chats" },
      { id: "actions", title: "Prompt Templates…" },
      { separator: true },
      { id: "pin", title: "Pin", checked: pinned },
      { id: "settings", title: "Settings…" },
      { separator: true },
      { id: "close", title: "Close" },
    ];
  }
  document.addEventListener("contextmenu", e => {
    if (!bridge) return;
    if (e.target.closest("textarea, input, select")) return;            // keep the text field's own menu
    const sel = window.getSelection();
    if (sel && !sel.isCollapsed && e.target.closest(".thread")) return;  // and Copy for selected answer text
    e.preventDefault();
    bridge.postMessage({ type: "menu", items: menuItems() });
  });
  window.__quickPromptMenu = async id => {
    if (id === "open") expand();
    else if (id === "new") { const res = await post("new"); if (res.ok) { stick = true; expand(); } else toast("Still answering"); }
    else if (id === "recent") { expand(); openHistory(false); }
    else if (id === "settings") { expand(); openSettings(false); }
    else if (id === "actions") { expand(); openActions(false); }
    else if (id === "pin") setPinned(!pinned);
    else if (id === "close") closeWindow();
  };
  // Which side of the conversation the icon sits on (the native panel decides from the screen position).
  window.__quickPromptSide = side => {
    els.root.classList.toggle("side-right", side === "right");
    lastLayout = "";
    queueLayout();
  };
  if (nativeInfo.side) els.root.classList.toggle("side-right", nativeInfo.side === "right");
  if (bridge) { mini.hidden = false; setTimeout(() => globe.start(), 0); }
  $("miniNew").addEventListener("click", async () => {
    const res = await post("new");
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
    const area = e.target.closest("[data-drag]");
    if (!onMini && (!area || e.target.closest("button, textarea, input, select, a, .seg"))) return;
    if (!onMini) e.preventDefault();
    bridge.postMessage({ type: "drag" });
  });
  // A click on the icon opens it again; a drag (the icon moved under the mouse) does not.
  $("miniOpen").addEventListener("click", e => {
    if (downAt && Math.abs(e.screenX - downAt.x) + Math.abs(e.screenY - downAt.y) > 4) return;
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
  const INPUT_NAMES = { selection: "Selected text", clipboard: "Clipboard", popup: "Paste in popup", none: "No text" };
  function renderActions() {
    act.list.innerHTML = act.items.length ? act.items.map(a =>
      '<div class="hitem aitem" data-id="' + esc(a.id) + '" data-provider="' + esc(a.provider) + '" role="button" tabindex="0">'
      + '<span class="dot"></span><div class="hbody"><div class="hq">' + esc(a.title) + '</div>'
      + '<div class="ha">' + esc(a.instruction.replace(/\s+/g, " ")) + '</div>'
      + '<div class="hmeta">' + (a.provider === "codex" ? "ChatGPT" : "Claude") + ' · ' + INPUT_NAMES[a.input]
      + (a.fields ? ' · ' + a.fields + (a.fields === 1 ? ' field' : ' fields') : '')
      + (a.setup === "custom" ? ' · own plugins/MCP/skills' : a.setup === "off" ? ' · no plugins/MCP/skills' : '')
      + (a.builtIn ? ' · built-in' : '') + '</div></div>'
      + '<button type="button" class="run" data-run="' + esc(a.id) + '">Run</button></div>').join("")
      : '<div class="hempty">No templates yet.</div>';
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
    if (act.list.parentElement !== target) target.append(act.list, act.buttons, act.form);
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
    setSeg("afProvider", a ? a.provider : "claude");
    $("afInput").value = a ? a.input : "selection";
    $("afConnection").value = a ? a.connection : "default";
    $("afModel").value = a ? a.model : "";
    $("afSetupBox").choice = a && a.setupChoice ? a.setupChoice : null;
    setSeg("afSetup", a ? a.setup || "default" : "default");
    $("afDelete").hidden = !a || a.builtIn;
    act.list.hidden = true; act.buttons.hidden = true; act.form.hidden = false;
    queueLayout();
    setTimeout(() => (a ? $("afPrompt") : $("afTitle")).focus(), 0);
  }
  // The action's own setup: only for CLI connections; the list follows the action's AI.
  function refreshActionSetup() {
    const box = $("afSetupBox"), mode = segValues.afSetup || "default", provider = segValues.afProvider || "claude";
    if (!box) return;
    if (box.dataset.setup !== provider && box.querySelector("input")) box.choice = null;   // another CLI: start from Settings
    box.dataset.setup = provider;
    box.hidden = mode !== "custom";
    $("afSetupHint").textContent = mode === "default" ? "Uses what Settings → Use my setup has ticked."
      : mode === "off" ? "Plain answer: no plugins, MCP servers or skills (fastest)."
      : "Only for this template. Used when it runs through the CLI.";
    if (mode === "custom") loadSetup(); else queueLayout();
  }

  async function runAction(id) {
    const res = await post("usepreset", { id });
    if (!res.ok) { toast("Still answering"); return; }
    if (settingsOpen) { settingsOnly = false; closeSettings(); return; }
    actionsOnly = false;
    closeActions();
  }
  act.list.addEventListener("click", e => {
    const run = e.target.closest("[data-run]");
    if (run) { runAction(run.dataset.run); return; }
    const item = e.target.closest(".aitem");
    if (item) editAction(act.items.find(a => a.id === item.dataset.id));
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
      title: $("afTitle").value.trim(), instruction: $("afPrompt").value,
      provider: segValues.afProvider || "claude", input: $("afInput").value,
      connection: $("afConnection").value, model: $("afModel").value.trim(),
      setup: segValues.afSetup || "default",
      setupChoice: segValues.afSetup === "custom" ? choiceFrom($("afSetupBox"), true) : "",
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
    els.who.textContent = s.providerName + (s.connection === "api" ? " · API" : "") + (s.model ? " · " + s.model : "");
    // Apply sizes from the server only when they change there (Save applies its own values right away).
    if (!sizeKnown || (s.popupScale !== lastStateScale && !settingsOpen)) applySize(s.popupScale);
    if (s.iconScale !== lastStateIcon) applyIconScale(s.iconScale);
    if (s.glassTint !== lastStateGlass && !settingsOpen) { applyGlass(s.glassTint); savedGlass = s.glassTint; }
    lastStateGlass = s.glassTint;
    lastStateScale = s.popupScale; lastStateIcon = s.iconScale;
    if (collapsed) { updateMini(s); return; }
    lastBusy = !!s.busy;
    if (s.showSettings && !settingsOpen && !draftApplied) { draftApplied = true; openSettings(true); return; }
    if (s.showHistory && !historyOpen && !draftApplied) { draftApplied = true; openHistory(true); return; }
    if (s.showActions && !actionsOpen && !draftApplied) { draftApplied = true; openActions(true); return; }
    if (settingsOpen || historyOpen || actionsOpen) return;
    els.title.textContent = s.title || "";
    document.title = (s.title ? s.title + " — " : "") + s.providerName;
    els.status.textContent = "";
    els.send.innerHTML = s.busy ? ICON_STOP : ICON_SEND;
    els.send.title = s.busy ? "Stop" : "Send (Enter)";
    els.notice.hidden = !s.notice;
    els.notice.textContent = s.notice || "";
    els.card.hidden = !(s.messages.length || s.notice || s.busy);
    $("barGear").hidden = !els.card.hidden;
    $("barHistory").hidden = !els.card.hidden;
    $("barPin").hidden = !bridge || !els.card.hidden;
    $("pinBtn").hidden = !bridge;
    const last = s.messages[s.messages.length - 1];
    els.errActions.hidden = !(last && last.state === "error" && !s.busy);

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
    const hasAnswer = lastAnswer() != null && !s.busy;
    els.actions.hidden = !hasAnswer;
    $("paste").hidden = !s.appWindow;
    els.prompt.placeholder = s.messages.length && !fieldsActive()
      ? "Follow up…"
      : fieldsActive()
        ? (/\{(text|selection|input)\}/i.test(s.presetPrompt || "") && !(s.draft && s.draft.context) ? "Paste the text — Enter runs" : "Add a note (optional) — Enter runs")
      : s.pasteMode
        ? "Paste text" + (s.title ? " · " + s.title : "") + " — Enter sends"
        : ("Ask " + s.providerName + (s.title ? " · " + s.title : "") + "…");
    els.ctxBtn.hidden = !!s.pasteMode && !s.messages.length;
    updateSendState();

    const key = JSON.stringify(s.messages) + s.busy + (s.status || "");
    if (key !== lastKey) {
      lastKey = key;
      // Keep the Copy / Paste row alive across re-renders (it moves into the last answer bubble).
      els.card.appendChild(els.actions);
      els.card.appendChild(els.errActions);
      els.thread.innerHTML = s.messages.map((m, i) => {
        if (m.role === "user") {
          const q = m.text
            ? esc(m.text).replace(/\{(text|selection|input)\}/gi, '<span class="tok">selected text</span>')
            : '<span class="tok">selected text</span>';
          const ctx = m.context ? `<div class="ctxinfo" title="${esc(m.context.slice(0, 600))}">+ ${m.context.length} characters</div>` : "";
          return `<div class="msg user glass" data-drag><div class="q">${q}</div>${ctx}</div>`;
        }
        const last = i === s.messages.length - 1;
        if (m.state === "error") {
          return `<div class="msg bot glass"><div class="mh" data-drag><span class="ok err">!</span>Something went wrong</div><div class="a error">${esc(m.text)}</div></div>`;
        }
        if (m.state === "streaming") {
          const label = m.text ? "Writing…" : (last && s.status ? s.status : "Thinking…");
          return `<div class="msg bot glass${m.text ? "" : " thinking"}"><div class="mh" data-drag><span class="spin"></span><span class="live">${esc(label)}</span></div>`
            + `<div class="a">${m.text ? md(m.text) + '<span class="cursor"></span>' : ""}</div></div>`;
        }
        return `<div class="msg bot glass"><div class="mh" data-drag><span class="ok">✓</span>Answered</div><div class="a">${md(m.text || "")}</div></div>`;
      }).join("");
      const lastBubble = els.thread.lastElementChild;
      if (lastBubble && lastBubble.classList.contains("bot")) {
        lastBubble.appendChild(els.actions);
        lastBubble.appendChild(els.errActions);
      }
      if (bridge && window.ResizeObserver) els.thread.querySelectorAll(".glass").forEach(el => threadObserver.observe(el));
      if (stick) els.thread.scrollTop = els.thread.scrollHeight;
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
        : '<input type="text" data-field="' + esc(f.name) + '" spellcheck="false" autocomplete="off">';
      return '<label class="sfield">' + esc(label(f.name)) + control + '</label>';
    }).join("");
    if (fields.length) setTimeout(() => { const first = fieldList.querySelector("[data-field]"); if (first) first.focus(); }, 30);
    updateSendState();
    queueLayout();
  }
  function collectFields() {
    const values = {}; let missing = null;
    fieldList.querySelectorAll("[data-field]").forEach(el => {
      values[el.dataset.field] = el.value;
      const empty = el.tagName === "INPUT" && !el.value.trim();
      el.parentElement.classList.toggle("missing", false);
      if (empty && !missing) missing = el;
    });
    return { values, missing };
  }
  fieldList.addEventListener("input", e => { e.target.parentElement.classList.remove("missing"); updateSendState(); });
  fieldList.addEventListener("keydown", e => {
    if (e.key !== "Enter" || e.isComposing || e.shiftKey) return;
    e.preventDefault(); e.stopPropagation();
    const all = [...fieldList.querySelectorAll("[data-field]")];
    const next = all[all.indexOf(e.target) + 1];
    if (next && !(e.metaKey || e.ctrlKey)) next.focus(); else send();
  });

  function updateSendState() {
    if (fieldsActive()) {
      els.send.classList.toggle("idle", !!state && !state.busy && !!collectFields().missing);
      return;
    }
    const empty = !els.prompt.value.trim() && (els.ctxWrap.hidden || !els.ctx.value.trim());
    els.send.classList.toggle("idle", !!state && !state.busy && empty);
  }

  // Scrolling: the bubbles switch to their own background until it stops, then the glass returns.
  let scrollIdle = 0;
  els.thread.addEventListener("scroll", () => {
    const t = els.thread;
    stick = t.scrollHeight - t.scrollTop - t.clientHeight < 30;
    if (!t.classList.contains("scrolling")) { t.classList.add("scrolling"); lastLayout = ""; queueLayout(); }
    clearTimeout(scrollIdle);
    scrollIdle = setTimeout(() => { t.classList.remove("scrolling"); lastLayout = ""; queueLayout(); }, 160);
  });
  els.thread.addEventListener("click", e => {
    const t = e.target;
    if (t instanceof HTMLElement && t.hasAttribute("data-copy")) copyText(t.parentElement.querySelector("code").textContent);
  });

  function showCtx(show) {
    els.ctxWrap.hidden = !show;
    els.ctxBtn.textContent = show ? "−" : "+";
    els.ctxBtn.title = show ? "Remove text" : "Add text";
    queueLayout();
  }

  function autoGrow() {
    els.prompt.style.height = "auto";
    els.prompt.style.height = Math.min(els.prompt.scrollHeight, 140) + "px";
    updateSendState();
    queueLayout();
  }

  async function send() {
    if (!state) return;
    if (state.busy) { post("cancel"); return; }
    const instruction = els.prompt.value.trim();
    const context = els.ctxWrap.hidden ? "" : els.ctx.value;
    const fields = fieldsActive() ? collectFields() : null;
    if (fields && fields.missing) {
      fields.missing.parentElement.classList.add("missing");
      fields.missing.focus();
      return;
    }
    if (!instruction && !context.trim() && !fields) { els.prompt.focus(); return; }
    els.send.disabled = true;
    const res = await post("send", { instruction, context, fields: fields ? JSON.stringify(fields.values) : "" });
    els.send.disabled = false;
    if (res.ok) {
      els.prompt.value = ""; els.ctx.value = ""; showCtx(false); autoGrow(); stick = true;
    } else {
      toast(fields ? "Paste the text first" : "Could not send");
      if (fields) els.prompt.focus();
    }
  }

  $("close").addEventListener("click", dismiss);
  $("copy").addEventListener("click", () => { const t = lastAnswer(); if (t != null) copyText(t); });
  $("paste").addEventListener("click", pasteAndClose);
  els.send.addEventListener("click", send);
  els.ctxBtn.addEventListener("click", () => {
    const show = els.ctxWrap.hidden;
    showCtx(show);
    if (show) els.ctx.focus(); else els.ctx.value = "";
    updateSendState();
  });
  els.prompt.addEventListener("input", autoGrow);
  els.ctx.addEventListener("input", () => { updateSendState(); queueLayout(); });
  document.addEventListener("keydown", e => {
    if (e.key === "Escape") {
      e.preventDefault();
      if (collapsed) return;
      if (actionsOpen) { if (!$("actForm").hidden) showActionList(); else if (actionsOnly) closeWindow(); else closeActions(); }
      else if (settingsOpen && !settingsOnly) closeSettings();
      else if (historyOpen && !historyOnly) closeHistory();
      else if (settingsOnly || historyOnly) closeWindow();
      else dismiss();
      return;
    }
    if (historyOpen || actionsOpen) return;
    if (settingsOpen) { if (e.key === "Enter" && e.target.tagName === "INPUT" && !e.target.closest("#setTemplates")) { e.preventDefault(); saveSettings(); } return; }
    if (e.key !== "Enter" || e.isComposing) return;
    if (e.metaKey || e.ctrlKey) {
      e.preventDefault();
      if (lastAnswer() != null && !state.busy && !els.prompt.value.trim()) pasteAndClose(); else send();
      return;
    }
    if (!e.shiftKey && e.target === els.prompt) { e.preventDefault(); send(); }
  });
  window.addEventListener("pagehide", () => { try { navigator.sendBeacon(base + "closed"); } catch (e) {} });

  // ---------- long-poll ----------
  async function poll() {
    while (true) {
      try {
        const res = await fetch(base + "state?v=" + version + (isTab ? "&tab=1" : ""), { cache: "no-store" });
        if (res.status === 404) {
          els.card.hidden = false;
          if (!sizeKnown) applySize(30);
          els.thread.innerHTML = '<div class="msg bot glass"><div class="a error">This popup session has ended. Press Esc and try again.</div></div>';
          els.send.disabled = true;
          queueLayout();
          return;
        }
        state = await res.json();
        version = state.v;
        render();
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
