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
}
:root[data-provider="codex"] { --accent: #10a37f; }
@media (prefers-color-scheme: dark) {
  :root { --text: #f5f5f7; --muted: rgba(255,255,255,.55); --faint: rgba(255,255,255,.08); --chip: rgba(255,255,255,.12);
          --glass: rgba(44,44,48,.55); --edge: rgba(255,255,255,.16); --hi: rgba(255,255,255,.14);
          --error: #ff8a80; --link: #7ab0ff; --page: linear-gradient(140deg, #2b2733 0%, #1d2230 50%, #2d2622 100%); }
}
* { box-sizing: border-box; }
html, body { margin: 0; color: var(--text); font: 14px/1.5 -apple-system, BlinkMacSystemFont, "SF Pro Text", "Segoe UI", Roboto, sans-serif; -webkit-font-smoothing: antialiased; }
body { background: var(--page); min-height: 100vh; }
html.native, html.native body { background: transparent; min-height: 0; }

#root { display: flex; flex-direction: column; gap: 10px; padding: 10px; }

/* Glass shapes: CSS imitation; the native panel draws real glass behind transparent shapes. */
.glass {
  background: var(--glass);
  -webkit-backdrop-filter: blur(28px) saturate(1.7); backdrop-filter: blur(28px) saturate(1.7);
  border: 1px solid var(--edge);
  box-shadow: 0 10px 34px rgba(0,0,0,.16), inset 0 1px 0 var(--hi);
}
html.native .glass { background: transparent; -webkit-backdrop-filter: none; backdrop-filter: none; box-shadow: none; border-color: transparent; }
html.native.vibrancy .glass { border-color: var(--edge); }

/* Answer card */
.card { border-radius: 22px; padding: 10px 14px 12px; }
.meta { display: flex; align-items: center; gap: 7px; font-size: 11.5px; color: var(--muted); min-height: 22px; }
.dot { width: 7px; height: 7px; border-radius: 50%; background: var(--accent); flex: none; }
.meta .title { font-weight: 600; color: var(--text); opacity: .8; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.meta .status { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.spin { width: 9px; height: 9px; border: 2px solid var(--faint); border-top-color: var(--accent); border-radius: 50%; display: inline-block; vertical-align: -1px; margin-right: 5px; animation: spin .8s linear infinite; }
@keyframes spin { to { transform: rotate(360deg); } }
.notice { font-size: 12.5px; color: var(--accent); margin: 2px 0 4px; }

.thread { overflow-y: auto; max-height: 360px; scrollbar-width: thin; margin-top: 2px; }
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
button.x { background: transparent; color: var(--muted); padding: 0 4px; font-size: 13px; line-height: 1; }
button.x:hover { color: var(--text); }

/* Optional pasted text */
.ctx { border-radius: 18px; padding: 8px 10px; }
.ctx textarea { min-height: 60px; max-height: 150px; font-size: 13px; resize: vertical; }

/* Input capsule */
.bar { display: flex; align-items: flex-end; gap: 8px; border-radius: 28px; padding: 7px; min-height: 56px; }
.round { flex: none; width: 40px; height: 40px; border-radius: 50%; padding: 0; display: grid; place-items: center; font-size: 22px; font-weight: 300; line-height: 1; }
.send { flex: none; width: 40px; height: 40px; border-radius: 50%; padding: 0; display: grid; place-items: center; background: var(--accent); color: var(--accent-text); transition: opacity .15s; }
.send svg { width: 18px; height: 18px; }
.send.idle { opacity: .45; }
textarea { flex: 1; width: 100%; resize: none; font: inherit; font-size: 15px; color: var(--text); background: transparent; border: 0; outline: none; padding: 9px 2px; max-height: 140px; min-height: 40px; }
textarea::placeholder { color: var(--muted); }
.bar textarea:focus-visible, .ctx textarea:focus-visible { outline: none; }
.round[hidden] + textarea { padding-left: 12px; }

/* Settings view */
.settings { border-radius: 22px; padding: 10px 16px 14px; }
.sgroup { padding: 10px 0 4px; border-top: 1px solid var(--faint); margin-top: 8px; }
.meta + .sgroup { border-top: 0; margin-top: 2px; }
.srow { display: flex; align-items: center; gap: 10px; }
.sname { font-weight: 600; flex: 1; }
.seg { display: inline-flex; background: var(--faint); border-radius: 999px; padding: 2px; }
.seg button { background: transparent; padding: 3px 14px; font-size: 12px; color: var(--muted); }
.seg button.on { background: var(--chip); color: var(--text); font-weight: 600; box-shadow: 0 1px 2px rgba(0,0,0,.12); }
.shint { font-size: 11.5px; color: var(--muted); margin: 5px 0 2px; }
.sfield { display: block; margin-top: 8px; font-size: 11.5px; color: var(--muted); }
.sfield input, .sfield select { display: block; width: 100%; margin-top: 3px; font: inherit; font-size: 13px; color: var(--text); background: var(--faint); border: 0; border-radius: 9px; padding: 7px 10px; outline: none; }
.sfield input:focus-visible, .sfield select:focus-visible { box-shadow: inset 0 0 0 1.5px var(--accent); }
.skey { display: flex; gap: 6px; align-items: flex-end; }
.skey .sfield { flex: 1; }
.skey button { margin-bottom: 1px; }
button.primary { background: var(--accent); color: var(--accent-text); font-weight: 600; }
.toast { position: fixed; bottom: 76px; left: 50%; transform: translateX(-50%); background: var(--text); color: var(--glass); padding: 4px 12px; border-radius: 999px; font-size: 12px; opacity: 0; transition: opacity .15s; pointer-events: none; }
.toast.show { opacity: .92; }
[hidden] { display: none !important; }
</style>
</head>
<body>
<div id="root">
  <div class="card glass" id="card" hidden>
    <div class="meta">
      <span class="dot"></span>
      <span id="who"></span>
      <span class="title" id="title"></span>
      <span class="status" id="status"></span>
      <button type="button" class="x" id="gear" title="Settings">⚙</button>
      <button type="button" class="x" id="close" title="Close (Esc)">✕</button>
    </div>
    <div class="notice" id="notice" hidden></div>
    <div class="thread" id="thread"></div>
    <div class="actions" id="errActions" hidden><button type="button" id="errSettings">Open settings</button></div>
    <div class="actions" id="actions" hidden>
      <button type="button" id="copy">Copy</button>
      <button type="button" id="paste" title="Closes the popup and pastes the answer into the app you were using">Paste<kbd id="pasteKey">⌘↵</kbd></button>
    </div>
  </div>
  <div class="settings glass" id="settings" hidden>
    <div class="meta">
      <span class="dot"></span>
      <span class="title">Settings</span>
      <span class="status" id="setStatus"></span>
      <button type="button" class="x" id="setClose" title="Close">✕</button>
    </div>
    <div class="sgroup">
      <div class="srow"><span class="sname">Claude</span>
        <div class="seg" data-name="claudeConnection"><button type="button" data-v="cli">CLI</button><button type="button" data-v="api">API</button></div></div>
      <div class="shint">CLI uses your Claude plan through Claude Code. API is faster and billed per use to your API account.</div>
      <div class="api-only" data-for="claudeConnection">
      <div class="skey"><label class="sfield">Anthropic API key<input type="password" id="claudeKey" autocomplete="off" spellcheck="false" placeholder="sk-ant-…"></label><button type="button" data-clear="claudeKey" hidden>Remove</button></div>
      <label class="sfield">API model<select id="claudeModel">
        <option value="claude-opus-5-5">Claude Opus 5.5</option>
        <option value="claude-sonnet-5-5">Claude Sonnet 5.5</option>
        <option value="claude-haiku-5-5">Claude Haiku 5.5 — fastest</option>
      </select></label>
      </div>
    </div>
    <div class="sgroup">
      <div class="srow"><span class="sname">ChatGPT</span>
        <div class="seg" data-name="chatgptConnection"><button type="button" data-v="cli">CLI</button><button type="button" data-v="api">API</button></div></div>
      <div class="shint">CLI uses your ChatGPT plan through the Codex CLI. API is faster and billed per use to your OpenAI account.</div>
      <div class="api-only" data-for="chatgptConnection">
      <div class="skey"><label class="sfield">OpenAI API key<input type="password" id="chatgptKey" autocomplete="off" spellcheck="false" placeholder="sk-…"></label><button type="button" data-clear="chatgptKey" hidden>Remove</button></div>
      <label class="sfield">API model<input type="text" id="chatgptModel" spellcheck="false" placeholder="gpt-5-mini"></label>
      </div>
    </div>
    <div class="shint">Keys are stored encrypted on this computer. A preset can override CLI/API in its Connection field. Changes apply to the next request.</div>
    <div class="actions"><button type="button" class="primary" id="setSave">Save</button><button type="button" id="setBack">Cancel</button></div>
  </div>
  <div class="ctx glass" id="ctxWrap" hidden><textarea id="ctx" rows="3" placeholder="Paste the text here…"></textarea></div>
  <div class="bar glass" id="bar">
    <button type="button" class="round" id="ctxBtn" title="Add text">+</button>
    <textarea id="prompt" rows="1" placeholder="Ask…"></textarea>
    <button type="button" class="send idle" id="send" title="Send (Enter)"></button>
  </div>
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
  let layoutQueued = false, lastLayout = "";
  function queueLayout() {
    if (!bridge || layoutQueued) return;
    layoutQueued = true;
    requestAnimationFrame(() => {
      layoutQueued = false;
      const rects = [...document.querySelectorAll(".glass")]
        .filter(el => !el.hidden && el.offsetParent !== null)
        .map(el => {
          const r = el.getBoundingClientRect();
          return { x: r.left, y: r.top, w: r.width, h: r.height, r: parseFloat(getComputedStyle(el).borderTopLeftRadius) || 0 };
        });
      const height = Math.ceil(els.root.getBoundingClientRect().height);
      const key = JSON.stringify([height, rects]);
      if (key === lastLayout) return;
      lastLayout = key;
      bridge.postMessage({ type: "layout", height, rects });
    });
  }
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

  function setSeg(name, value) {
    segValues[name] = value;
    document.querySelectorAll('.seg[data-name="' + name + '"] button').forEach(b => b.classList.toggle("on", b.dataset.v === value));
    const fields = document.querySelector('.api-only[data-for="' + name + '"]');
    if (fields) fields.hidden = value !== "api";
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

  async function openSettings(only) {
    settingsOnly = !!only;
    settingsOpen = true;
    els.card.hidden = true; els.bar.hidden = true; els.ctxWrap.hidden = true;
    set.view.hidden = false;
    set.status.textContent = "";
    try {
      const s = await (await fetch(base + "settings", { cache: "no-store" })).json();
      setSeg("claudeConnection", s.claude.connection);
      setSeg("chatgptConnection", s.chatgpt.connection);
      showKeyHint(set.claudeKey, s.claude.keyHint);
      showKeyHint(set.chatgptKey, s.chatgpt.keyHint);
      if (![...set.claudeModel.options].some(o => o.value === s.claude.model)) {
        set.claudeModel.add(new Option(s.claude.model, s.claude.model));
      }
      set.claudeModel.value = s.claude.model;
      set.chatgptModel.value = s.chatgpt.model;
    } catch (e) {
      set.status.textContent = "Could not load settings";
    }
    queueLayout();
  }

  function closeSettings() {
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
    };
    const res = await post("settings", body);
    if (!res.ok) { set.status.textContent = "Could not save"; return; }
    toast("Saved");
    closeSettings();
  }

  $("gear").addEventListener("click", () => openSettings(false));
  $("errSettings").addEventListener("click", () => openSettings(false));
  $("setSave").addEventListener("click", saveSettings);
  $("setBack").addEventListener("click", closeSettings);
  $("setClose").addEventListener("click", closeWindow);

  // ---------- rendering ----------
  function render() {
    const s = state;
    document.documentElement.dataset.provider = s.provider;
    els.who.textContent = s.providerName + (s.connection === "api" ? " · API" : "") + (s.model ? " · " + s.model : "");
    if (s.showSettings && !settingsOpen && !draftApplied) { draftApplied = true; openSettings(true); return; }
    if (settingsOpen) return;
    els.title.textContent = s.title || "";
    document.title = (s.title ? s.title + " — " : "") + s.providerName;
    els.status.innerHTML = s.busy ? '<span class="spin"></span>' + esc(s.status || "Working…") : "";
    els.send.innerHTML = s.busy ? ICON_STOP : ICON_SEND;
    els.send.title = s.busy ? "Stop" : "Send (Enter)";
    els.notice.hidden = !s.notice;
    els.notice.textContent = s.notice || "";
    els.card.hidden = !(s.messages.length || s.notice || s.busy);
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

    const hasAnswer = lastAnswer() != null && !s.busy;
    els.actions.hidden = !hasAnswer;
    $("paste").hidden = !s.appWindow;
    els.prompt.placeholder = s.messages.length
      ? "Follow up…"
      : s.pasteMode
        ? "Paste text" + (s.title ? " · " + s.title : "") + " — Enter sends"
        : ("Ask " + s.providerName + (s.title ? " · " + s.title : "") + "…");
    els.ctxBtn.hidden = !!s.pasteMode && !s.messages.length;
    updateSendState();

    const key = JSON.stringify(s.messages) + s.busy;
    if (key !== lastKey) {
      lastKey = key;
      els.thread.innerHTML = s.messages.map(m => {
        if (m.role === "user") {
          const q = m.text
            ? esc(m.text).replace(/\{(text|selection|input)\}/gi, '<span class="tok">selected text</span>')
            : '<span class="tok">selected text</span>';
          const ctx = m.context ? ` · <span title="${esc(m.context.slice(0, 600))}">${m.context.length} characters</span>` : "";
          return `<div class="q">${q}${ctx}</div>`;
        }
        if (m.state === "error") return `<div class="a error">${esc(m.text)}</div>`;
        return `<div class="a">${m.text ? md(m.text) : ""}${m.state === "streaming" ? '<span class="cursor"></span>' : ""}</div>`;
      }).join("");
      if (stick) els.thread.scrollTop = els.thread.scrollHeight;
    }
    queueLayout();
  }

  function updateSendState() {
    const empty = !els.prompt.value.trim() && (els.ctxWrap.hidden || !els.ctx.value.trim());
    els.send.classList.toggle("idle", !!state && !state.busy && empty);
  }

  els.thread.addEventListener("scroll", () => {
    const t = els.thread;
    stick = t.scrollHeight - t.scrollTop - t.clientHeight < 30;
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
    if (!instruction && !context.trim()) { els.prompt.focus(); return; }
    els.send.disabled = true;
    const res = await post("send", { instruction, context });
    els.send.disabled = false;
    if (res.ok) {
      els.prompt.value = ""; els.ctx.value = ""; showCtx(false); autoGrow(); stick = true;
    } else {
      toast("Could not send");
    }
  }

  $("close").addEventListener("click", closeWindow);
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
    if (e.key === "Escape") { e.preventDefault(); if (settingsOpen && !settingsOnly) closeSettings(); else closeWindow(); return; }
    if (settingsOpen) { if (e.key === "Enter" && e.target.tagName === "INPUT") { e.preventDefault(); saveSettings(); } return; }
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
          els.thread.innerHTML = '<div class="a error">This popup session has ended. Press Esc and try again.</div>';
          els.send.disabled = true;
          queueLayout();
          return;
        }
        state = await res.json();
        version = state.v;
        render();
      } catch (e) {
        els.card.hidden = false;
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
