import AppKit
import WebKit

/// The template ring's window: borderless, transparent, over the whole screen Pip is on, above the Dock and the
/// menu bar. It takes the keyboard without making Quick Prompt the active app, so the app you were in keeps its
/// selection; the window is gone again before ⌘C is pressed in that app.
final class RingPanel: NSPanel {
    override var canBecomeKey: Bool { true }
    override func constrainFrameRect(_ frameRect: NSRect, to screen: NSScreen?) -> NSRect { frameRect }
    /// A key nobody handled ends here, and NSWindow beeps for it; the ring's keys are all its own, so stay quiet.
    override func noResponder(for eventSelector: Selector) {
        if eventSelector == #selector(NSResponder.keyDown(with:)) { return }
        super.noResponder(for: eventSelector)
    }
}

/// The template ring (the shortcut, default ⌥⇧Z): Pip casts the prompt templates out round itself as a
/// constellation, on the side of the screen with more room; ↑ ↓ or a number choose, Return runs the template on
/// the selection and pastes the answer, Esc (or the shortcut again, or a click elsewhere) pulls them back.
/// The page below draws it all (look C of tools/pip-ring-lab.html, ×1.2; groups from tools/pip-ring-groups-lab.html, copied
/// in by tools/ring-page-sync.py: edit the lab, then run that); this class only shows and hides it.
final class RingOverlay: NSObject, WKScriptMessageHandler, NSWindowDelegate {
    static let shared = RingOverlay()

    /// Runs the chosen template (its id) once the window is gone.
    var onRun: ((String) -> Void)?

    private var panel: RingPanel?
    private var webView: WKWebView?
    private var loaded = false
    private var pendingCall: String?
    private var closeFallback: DispatchWorkItem?

    var isOpen: Bool { panel?.isVisible == true }

    /// point: Pip's middle on screen; ghost: Pip is not on screen, so the page draws one there.
    func open(at point: NSPoint, ghost: Bool, templates: [[String: Any]]) {
        let screen = NSScreen.screens.first(where: { NSMouseInRect(point, $0.frame, false) }) ?? NSScreen.main ?? NSScreen.screens[0]
        let panel = self.panel ?? makePanel()
        let frame = screen.frame, visible = screen.visibleFrame
        let payload: [String: Any] = [
            "pip": ["x": point.x - frame.minX, "y": frame.maxY - point.y],
            "area": ["x": visible.minX - frame.minX, "y": frame.maxY - visible.maxY, "w": visible.width, "h": visible.height],
            "ghost": ghost,
            "templates": templates.map { ["id": $0["id"] as? String ?? "", "title": $0["title"] as? String ?? "",
                                          "color": $0["color"] as? String ?? "", "group": $0["group"] as? String ?? ""] },
        ]
        guard let data = try? JSONSerialization.data(withJSONObject: payload),
              let json = String(data: data, encoding: .utf8) else { return }
        closeFallback?.cancel()
        panel.setFrame(frame, display: false)
        panel.makeKeyAndOrderFront(nil)
        panel.makeFirstResponder(webView)
        let call = "ringOpen(\(json))"
        if loaded {
            webView?.evaluateJavaScript(call)
        } else {
            pendingCall = call
        }
    }

    /// Pulls the templates back into Pip (animated), then hides the window.
    func close() {
        guard isOpen else { return }
        webView?.evaluateJavaScript("ringClose()")
        let fallback = DispatchWorkItem { [weak self] in self?.hide() }
        closeFallback = fallback
        DispatchQueue.main.asyncAfter(deadline: .now() + 1.2, execute: fallback)
    }

    private func hide() {
        closeFallback?.cancel()
        closeFallback = nil
        panel?.orderOut(nil)
    }

    private func makePanel() -> RingPanel {
        let panel = RingPanel(contentRect: NSRect(x: 0, y: 0, width: 100, height: 100),
                              styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = false
        panel.level = .popUpMenu
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .transient, .ignoresCycle]
        panel.hidesOnDeactivate = false
        panel.isReleasedWhenClosed = false
        panel.delegate = self

        let config = WKWebViewConfiguration()
        config.userContentController.add(self, name: "ring")
        let web = WKWebView(frame: panel.contentView?.bounds ?? .zero, configuration: config)
        web.setValue(false, forKey: "drawsBackground")
        web.autoresizingMask = [.width, .height]
        panel.contentView = web
        web.loadHTMLString(ringPageHTML, baseURL: nil)
        self.panel = panel
        self.webView = web
        return panel
    }

    func userContentController(_ userContentController: WKUserContentController, didReceive message: WKScriptMessage) {
        guard let body = message.body as? [String: Any] else { return }
        switch body["type"] as? String {
        case "ready":
            loaded = true
            if let call = pendingCall {
                pendingCall = nil
                webView?.evaluateJavaScript(call)
            }
        case "run":
            // The window goes first, so ⌘C and ⌘V reach the app that has the selection.
            hide()
            if let id = body["id"] as? String {
                DispatchQueue.main.asyncAfter(deadline: .now() + 0.08) { [weak self] in self?.onRun?(id) }
            }
        case "closed":
            hide()
        default:
            break
        }
    }

    /// Another app or window took the keyboard: pull the ring back.
    func windowDidResignKey(_ notification: Notification) {
        if isOpen { close() }
    }
}

/// The ring's page: a canvas for the threads, stars and Pip's flash, and the template pills.
let ringPageHTML = #"""
<!doctype html>
<html>
<head>
<meta charset="utf-8">
<style>
html, body { margin: 0; height: 100%; background: transparent; overflow: hidden; cursor: default;
  font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif; -webkit-user-select: none; user-select: none; }
#fx { position: fixed; left: 0; top: 0; pointer-events: none; }
#ghost { position: fixed; width: 48px; height: 48px; margin: -24px 0 0 -24px; opacity: 0; pointer-events: none; }
#ghost svg { width: 48px; height: 48px; overflow: visible; filter: drop-shadow(0 6px 10px rgba(0,0,0,.45)); display: block; }
.pill { position: fixed; left: 0; top: 0; height: 30px; display: flex; align-items: center; gap: 8px; padding: 0 12px 0 6px; border-radius: 10px;
  background: rgba(30,28,42,.9); border: 1px solid rgba(255,255,255,.16); color: #f3f2f8; font-size: 13px; white-space: nowrap;
  opacity: 0; pointer-events: none; box-shadow: 0 6px 18px rgba(0,0,0,.35); max-width: 420px; }
.pill .t { overflow: hidden; text-overflow: ellipsis; }
.pill .n { font: 11px ui-monospace, Menlo, monospace; min-width: 18px; height: 18px; border-radius: 5px; display: grid; place-items: center;
  background: rgba(255,255,255,.12); color: rgba(255,255,255,.8); flex: none; }
.pill .n.none { visibility: hidden; }
.pill .dot { width: 7px; height: 7px; border-radius: 50%; margin-left: 2px; flex: none; }
.pill.sel { background: rgba(44,40,66,.95); box-shadow: 0 0 0 1.5px var(--c), 0 0 22px color-mix(in srgb, var(--c) 55%, transparent), 0 6px 18px rgba(0,0,0,.35); }
.pill.sel .n { background: var(--c); color: #fff; }
.pill .more { font-size: 11px; color: rgba(255,255,255,.55); margin-left: 2px; }
.pill .chev { font-size: 15px; line-height: 1; color: rgba(255,255,255,.6); margin-left: -2px; }
.pill.group { font-weight: 600; }
.pill.sel .chev { color: #fff; }
.hintbar { position: fixed; left: 0; top: 0; font-size: 11px; color: rgba(255,255,255,.75); white-space: nowrap; opacity: 0; pointer-events: none;
  padding: 4px 8px; border-radius: 8px; background: rgba(30,28,42,.82); }
.hintbar kbd { font: 10.5px ui-monospace, Menlo, monospace; padding: 1px 5px; border-radius: 5px; background: rgba(255,255,255,.1);
  border: 1px solid rgba(255,255,255,.16); color: rgba(255,255,255,.85); }
</style>
</head>
<body>
<canvas id="fx"></canvas>
<div id="ghost"></div>
<div class="hintbar" id="hint"></div>
<script>
"use strict";
const $ = id => document.getElementById(id);
const post = m => { try { window.webkit.messageHandlers.ring.postMessage(m); } catch (e) {} };
const SPEED = 1.2, OPEN_MS = 860, PIP_R = 25, GAP = 38;
const BRAND = ["#F08A5D", "#1FB890", "#4C8DF6", "#A56BF2"];
const clamp = (v, a = 0, b = 1) => Math.max(a, Math.min(b, v));
const lerp = (a, b, t) => a + (b - a) * t;
const outCubic = t => 1 - Math.pow(1 - t, 3);
const outBack = (t, s = 1.7) => 1 + (s + 1) * Math.pow(t - 1, 3) + s * Math.pow(t - 1, 2);
function hexA(h, a) {
  if (!/^#[0-9a-f]{6}$/i.test(h || "")) h = "#4C8DF6";
  const n = parseInt(h.slice(1), 16); return `rgba(${n >> 16},${(n >> 8) & 255},${n & 255},${a})`;
}

/* Pip, for when it is not on screen (the real one sits under this window otherwise). */
const STAR = [[12,4.6],[13.85,10.15],[19.4,12],[13.85,13.85],[12,19.4],[10.15,13.85],[4.6,12],[10.15,10.15]];
const CORNERS = [[16.8,7.2],[16.8,16.8],[7.2,16.8],[7.2,7.2]];
$("ghost").innerHTML = `<svg viewBox="0 0 24 24"><defs>
  <linearGradient id="m" gradientUnits="userSpaceOnUse" x1="5" y1="5" x2="19" y2="19"><stop offset="0.08" stop-color="#F08A5D"/><stop offset="0.42" stop-color="#1FB890"/><stop offset="0.7" stop-color="#4C8DF6"/><stop offset="0.95" stop-color="#A56BF2"/></linearGradient>
  <radialGradient id="d" cx="0.4" cy="0.3" r="0.85"><stop offset="0" stop-color="#3d3852"/><stop offset="1" stop-color="#121018"/></radialGradient>
  <linearGradient id="g" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#fff" stop-opacity=".32"/><stop offset="1" stop-color="#fff" stop-opacity="0"/></linearGradient></defs>
  <circle cx="12" cy="12" r="11.2" fill="url(#d)" stroke="rgba(255,255,255,.28)" stroke-width=".5"/>
  <g stroke="url(#m)" stroke-linecap="round" stroke-width="1.15">${CORNERS.map(([x, y]) => {
    const a = Math.atan2(y - 12, x - 12) + Math.PI / 2, hx = Math.cos(a) * 0.99, hy = Math.sin(a) * 0.99;
    return `<line x1="${x - hx}" y1="${y - hy}" x2="${x + hx}" y2="${y + hy}"/>`; }).join("")}</g>
  <polygon points="${STAR.map(p => p.join(",")).join(" ")}" fill="url(#m)" stroke="url(#m)" stroke-width=".5" stroke-linejoin="round"/>
  <ellipse cx="12" cy="6.2" rx="7.2" ry="3.4" fill="url(#g)"/></svg>`;

const cv = $("fx"), ctx = cv.getContext("2d"), hint = $("hint"), ghostEl = $("ghost");
let W = innerWidth, H = innerHeight, dpr = 1;
function sizeCanvas() {
  W = innerWidth; H = innerHeight; dpr = Math.min(2, window.devicePixelRatio || 1);
  cv.width = W * dpr; cv.height = H * dpr; cv.style.width = W + "px"; cv.style.height = H + "px";
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
}

let T = [], N = 0, MID = 0, pills = [], nodes = [], widths = [], side = 1;
let pip = { x: 0, y: 0 }, area = { x: 0, y: 0, w: 0, h: 0 }, ghost = false;
let open = false, dir = 0, e = 0, sel = 0, prevSel = 0, selAt = -1e9, fireAt = -1, flashAt = -1e9, now = 0, finish = null, gap = GAP;
/* Groups: the first ring shows the groups (each a star with its templates circling it as satellites) and the templates
   that have no group. A group's number opens it in place: nothing flies. The other entries fade where they are, the
   group's star and pill stay put (the pill becomes the header), and its templates unfold out of the pill's end, each drawn
   by a thread in the group's colour (about 0.25 s in all). So every template has a fixed two-key code (group, then
   template) that the hand learns. After the first step into a group the ring stays "calm": no more flights. */
let TOP = [], level = null, org = { x: 0, y: 0 }, hub = null, trans = null, skipFly = -1, fastUntil = 0, calm = false, header = null;
const TRANS_MS = 140, FAST_MS = 450, STEP = 22, DRAW_MS = 150;

function build(list, keepOld) {
  if (!keepOld) pills.forEach(p => p.remove());
  T = list; N = list.length; MID = (N - 1) / 2;
  pills = list.map((tp, i) => {
    const el = document.createElement("div");
    el.className = "pill" + (tp.items ? " group" : ""); el.style.setProperty("--c", hexA(tp.color, 1));
    const n = document.createElement("span"); n.className = "n" + (i < 9 ? "" : " none"); n.textContent = i < 9 ? String(i + 1) : "";
    const t = document.createElement("span"); t.className = "t"; t.textContent = tp.title;
    el.append(n, t);
    if (tp.items) {
      const m = document.createElement("span"); m.className = "more"; m.textContent = String(tp.items.length);
      const c = document.createElement("span"); c.className = "chev"; c.textContent = side > 0 ? "›" : "‹";
      el.append(m, c);
    } else {
      const d = document.createElement("span"); d.className = "dot"; d.style.background = hexA(tp.color, 1);
      el.append(d);
    }
    el.addEventListener("mouseenter", () => { if (open && dir > 0 && fireAt < 0 && !trans) select(i); });
    el.addEventListener("click", ev => { ev.stopPropagation(); if (open && dir > 0 && !trans) { select(i); choose(); } });
    document.body.appendChild(el);
    return el;
  });
  const fwd = side > 0 ? "→" : "←", back = side > 0 ? "←" : "→";
  hint.innerHTML = level
    ? `<kbd>1</kbd>–<kbd>9</kbd> run · <kbd>↑</kbd><kbd>↓</kbd> <kbd>↵</kbd> · <kbd>${back}</kbd> back · <kbd>esc</kbd>`
    : `<kbd>1</kbd>–<kbd>9</kbd> · <kbd>↑</kbd><kbd>↓</kbd> choose · <kbd>${fwd}</kbd> open · <kbd>↵</kbd> run · <kbd>esc</kbd>`;
}

/* The first ring: templates in their order, a group standing where its first template is (its templates together in it).
   A group takes the next of Pip's colours. */
function topList(templates) {
  const out = [], byName = new Map();
  for (const t of templates) {
    const name = (t.group || "").trim();
    if (!name) { out.push(t); continue; }
    const key = name.toLowerCase();
    if (!byName.has(key)) { const g = { title: name, color: BRAND[byName.size % BRAND.length], items: [] }; byName.set(key, g); out.push(g); }
    byName.get(key).items.push(t);
  }
  return out;
}
function snapshot() { return nodes.map((n, j) => ({ x: starX(j), y: n.y, c: T[j].items ? hexA(T[j].color, 1) : "#fff" })); }
function settle() { if (trans) { trans.pills.forEach(p => p.remove()); trans = null; } }
function enterGroup(i) {
  const g = T[i]; if (!g || !g.items || fireAt >= 0) return;
  settle();
  const from = snapshot();
  header = pills[i]; header.classList.add("sel"); header.style.pointerEvents = "auto";
  header.querySelector(".chev").textContent = side > 0 ? "‹" : "›";
  trans = { at: now, back: false, from, keep: i, pills: pills.filter(p => p !== header), color: g.color };
  hub = { x: starX(i), y: nodes[i].y };
  const end = side > 0 ? nodes[i].x + 6 + widths[i] : nodes[i].x - 6 - widths[i];
  org = { x: end + side * 8, y: nodes[i].y };
  level = g; calm = true;
  build(g.items, true); layout();
  sel = prevSel = Math.min(Math.round(MID - 0.01), N - 1); selAt = -1e9; skipFly = -1;
  e = 0; dir = 1; fastUntil = now + FAST_MS;
}
function goBack() {
  if (!level || fireAt >= 0) return;
  settle();
  const g = level, from = snapshot();
  trans = { at: now, back: true, from, keep: -1, pills: pills.slice(), color: g.color };
  if (header) { header.remove(); header = null; }
  level = null; org = { ...pip }; hub = null;
  build(TOP, true); layout();
  const idx = TOP.indexOf(g);
  sel = prevSel = idx; selAt = -1e9; skipFly = idx; e = 0; dir = 1;
}
function choose() { if (T[sel] && T[sel].items) enterGroup(sel); else fire(); }

/* The list: pills on a circle round Pip, on the side with more room, moved up or down to stay on screen; the circle
   grows when it has to reach far (a corner gives a quarter circle round Pip). */
function layout() {
  const usable = area.h - 80;
  gap = N > 1 ? Math.min(GAP, usable / (N - 1)) : 0;
  const total = (N - 1) * gap;
  const top = clamp(org.y - total / 2, area.y + 40, Math.max(area.y + 40, area.y + area.h - 36 - total));
  const ys = T.map((_, i) => top + i * gap);
  const far = Math.max(0, ...ys.map(y => Math.abs(y - org.y)));
  const R = Math.max(level ? 120 : 150, far * 1.04);
  widths = pills.map(p => p.offsetWidth || 150);
  nodes = ys.map((y, i) => {
    const dy = y - org.y, dx = Math.sqrt(Math.max(R * R - dy * dy, (R * 0.32) ** 2));
    let x = org.x + side * dx;
    if (side > 0) x = Math.min(x, area.x + area.w - 16 - widths[i]); else x = Math.max(x, area.x + 16 + widths[i]);
    return { x, y };
  });
}
const starX = i => nodes[i].x - side * (i % 2 ? 22 : 9);
function starPos(i, t) {
  const o = org, n = { x: starX(i), y: nodes[i].y }, dx = n.x - o.x, dy = n.y - o.y, dist = Math.hypot(dx, dy) || 1;
  const nx = -dy / dist, ny = dx / dist, swirl = dist * 0.42 * side;
  const cx = o.x + dx * 0.5 + nx * swirl, cy = o.y + dy * 0.5 + ny * swirl, u = 1 - t;
  return { x: u * u * o.x + 2 * u * t * cx + t * t * n.x, y: u * u * o.y + 2 * u * t * cy + t * t * n.y };
}
const delay = i => Math.min(Math.abs(i - MID) - 0.5, 5) * 50;

function glowDot(x, y, r, color, blur = 10) { ctx.save(); ctx.shadowColor = color; ctx.shadowBlur = blur; ctx.fillStyle = color; ctx.beginPath(); ctx.arc(x, y, Math.max(0, r), 0, Math.PI * 2); ctx.fill(); ctx.restore(); }
function sparkle(x, y, r, rot, color, alpha = 1) {
  ctx.save(); ctx.translate(x, y); ctx.rotate(rot); ctx.globalAlpha = alpha; ctx.fillStyle = color; ctx.shadowColor = color; ctx.shadowBlur = r * 1.6;
  ctx.beginPath();
  for (let k = 0; k < 8; k++) { const rr = k % 2 ? r * 0.22 : r, a = k * Math.PI / 4; ctx[k ? "lineTo" : "moveTo"](Math.cos(a) * rr, Math.sin(a) * rr); }
  ctx.closePath(); ctx.fill(); ctx.restore();
}

const dust = Array.from({ length: 40 }, (_, k) => ({ a: Math.random() * 7, r: Math.random(), s: 0.3 + Math.random(), k }));

function draw() {
  ctx.clearRect(0, 0, W, H);
  if (!open) return;
  const ms = e, firing = fireAt >= 0 ? now - fireAt : -1, fadeAll = firing >= 0 ? 1 - clamp(firing / 240) : 1;
  ghostEl.style.opacity = ghost ? clamp(ms / 150).toFixed(2) : "0";

  // Casting: a ring leaves Pip (the first time only).
  if (!calm && ms < 360 && dir > 0) {
    const t = ms / 360; ctx.save(); ctx.globalAlpha = (1 - t) * 0.8; ctx.strokeStyle = "#fff"; ctx.lineWidth = 1.5;
    ctx.beginPath(); ctx.arc(pip.x, pip.y, PIP_R + t * 40, 0, 7); ctx.stroke(); ctx.restore();
  }
  // Stepping in or out: what was there fades where it is.
  if (trans) {
    const tt = clamp((now - trans.at) / TRANS_MS);
    trans.from.forEach((p, j) => { if (j !== trans.keep) { ctx.globalAlpha = 1 - tt; glowDot(p.x, p.y, 2.4, p.c, 6); } });
    ctx.globalAlpha = 1;
    trans.pills.forEach(p => { p.style.opacity = (1 - tt).toFixed(3); p.style.pointerEvents = "none"; });
    if (tt >= 1) { trans.pills.forEach(p => p.remove()); trans = null; }
  }
  // Inside a group: Pip's thread to the group's star, which stays where it was; its pill is the header.
  if (level) {
    const col = level.color, ha = dir < 0 ? clamp(e / 200) : 1;
    ctx.save(); ctx.globalAlpha = ha * 0.8; ctx.setLineDash([2, 5]); ctx.lineDashOffset = -now * 0.015;
    ctx.strokeStyle = hexA(col, 1); ctx.lineWidth = 1.1;
    ctx.beginPath(); ctx.moveTo(pip.x + side * PIP_R, pip.y); ctx.quadraticCurveTo(lerp(pip.x, hub.x, 0.5), lerp(pip.y, hub.y, 0.15), hub.x, hub.y); ctx.stroke(); ctx.restore();
    sparkle(hub.x, hub.y, 8, now * 0.0008, hexA(col, 1), ha);
    sparkle(hub.x, hub.y, 2.8, -now * 0.0015, "#fff", ha);
    if (header) header.style.opacity = ha.toFixed(2);
  }
  // Dust round the list.
  const top = nodes[0].y, bot = nodes[N - 1].y, cxm = nodes.reduce((s, n) => s + n.x, 0) / N;
  ctx.save(); ctx.globalAlpha = clamp(ms / 500) * 0.6 * fadeAll; ctx.fillStyle = "rgba(255,255,255,.7)";
  for (const d of dust) {
    const x = cxm + Math.cos(d.a + now * 0.00012 * d.s) * (60 + d.r * 120) * 0.8, y = lerp(top - 20, bot + 20, (d.k / dust.length + now * 0.00002 * d.s) % 1);
    ctx.beginPath(); ctx.arc(x, y, 0.6 + d.r * 0.7, 0, 7); ctx.fill();
  }
  ctx.restore();
  // Constellation lines, once all have landed; a dotted line from Pip to the chosen star.
  if (level) {       // the threads from the header to each template; the chosen one bright
    ctx.save(); ctx.globalAlpha = fadeAll;
    for (let i = 0; i < N; i++) {
      const raw = clamp((ms - i * STEP) / DRAW_MS); if (raw <= 0) continue;
      const k = outCubic(raw), sx = starX(i), sy = nodes[i].y, x = lerp(org.x, sx, k), y = lerp(org.y, sy, k);
      const on = i === sel && firing < 0;
      ctx.strokeStyle = hexA(level.color, on ? 0.9 : 0.32); ctx.lineWidth = on ? 1.4 : 1;
      ctx.beginPath(); ctx.moveTo(org.x, org.y); ctx.quadraticCurveTo(lerp(org.x, x, 0.55), org.y, x, y); ctx.stroke();
      if (raw < 1) glowDot(x, y, 2, hexA(level.color, 1), 8);
    }
    ctx.restore();
  }
  const landed = calm ? -260 : delay(0) + 380, lp = level ? 0 : clamp((ms - landed + 60) / 260);
  if (lp > 0) {
    ctx.save(); ctx.strokeStyle = "rgba(200,210,255,.4)"; ctx.lineWidth = 0.9; ctx.globalAlpha = fadeAll;
    const segs = N - 1, shown = lp * segs;
    ctx.beginPath();
    for (let j = 0; j < segs && j < shown; j++) {
      const f = clamp(shown - j), ax = starX(j), bx = starX(j + 1);
      ctx.moveTo(ax, nodes[j].y); ctx.lineTo(lerp(ax, bx, f), lerp(nodes[j].y, nodes[j + 1].y, f));
    }
    ctx.stroke();
    if (firing < 0) {
      const sx = starX(sel), sy = nodes[sel].y;
      ctx.setLineDash([2, 5]); ctx.lineDashOffset = -now * 0.03; ctx.strokeStyle = hexA(T[sel].color, 0.7); ctx.lineWidth = 1.1;
      ctx.beginPath(); ctx.moveTo(org.x, org.y); ctx.quadraticCurveTo(lerp(org.x, sx, 0.5), lerp(org.y, sy, 0.2), sx, sy); ctx.stroke();
    }
    ctx.restore();
  }
  // The stars: flying out with tails, a burst as each lands; the chosen one big and turning.
  for (let i = 0; i < N; i++) {
    const d = calm ? (level ? i * STEP + DRAW_MS - 380 : -380) : delay(i), raw = calm ? (ms >= d + 380 ? 1 : 0) : clamp((ms - d) / 380);
    if (raw <= 0) continue;
    const t = outCubic(raw), isSel = i === sel, fade = firing < 0 ? 1 : isSel ? 1 - clamp(firing / 300) : fadeAll;
    if (raw < 1) {
      for (let k = 7; k >= 0; k--) {
        const p = starPos(i, Math.max(0, t - k * 0.045));
        ctx.globalAlpha = (1 - k / 8) * 0.8; glowDot(p.x, p.y, 2.2 * (1 - k / 9), BRAND[i % 4], 8);
      }
      ctx.globalAlpha = 1;
      continue;
    }
    const burst = clamp((ms - d - 380) / 220), x = starX(i), y = nodes[i].y;
    if (burst < 1 && dir > 0 && !calm) { ctx.save(); ctx.globalAlpha = (1 - burst) * 0.7; ctx.strokeStyle = "#fff"; ctx.lineWidth = 1; ctx.beginPath(); ctx.arc(x, y, 3 + burst * 16, 0, 7); ctx.stroke(); ctx.restore(); }
    const grp = !!T[i].items, grow = calm ? 0.55 + 0.45 * outCubic(clamp((ms - d - 380) / 120)) : 1;
    const r = (isSel ? (grp ? 9.5 : 8) : (grp ? 6.5 : 4.2) + (calm ? 0 : (1 - burst) * 5)) * grow;
    sparkle(x, y, r, isSel ? now * 0.0015 : Math.PI / 4 * (i % 2), isSel || grp ? hexA(T[i].color, 1) : "#fff", fade * (isSel ? 1 : 0.85 + 0.15 * Math.sin(now * 0.004 + i * 2)));
    if (isSel) sparkle(x, y, 3, -now * 0.002, "#fff", fade);
    if (grp) {       // a group's templates circle its star as satellites
      const k = Math.min(6, T[i].items.length);
      for (let s = 0; s < k; s++) {
        const a = now * 0.0011 * (isSel ? 1.8 : 1) + s * Math.PI * 2 / k, rr = (isSel ? 17 : 13) * (calm ? 1 : outCubic(burst));
        ctx.globalAlpha = fade * 0.9; glowDot(x + Math.cos(a) * rr, y + Math.sin(a) * rr * 0.75, 1.4, "#fff", 5);
      }
      ctx.globalAlpha = 1;
    }
  }
  // A comet from the old choice to the new one.
  const ct = (now - selAt) / 200;
  if (ct < 1 && firing < 0 && prevSel !== sel && ms >= (calm ? 200 : OPEN_MS) && !level) {
    const ax = starX(prevSel), bx = starX(sel), ay = nodes[prevSel].y, by = nodes[sel].y;
    for (let k = 6; k >= 0; k--) { const t = outCubic(clamp(ct - k * 0.06)); ctx.globalAlpha = 1 - k / 7; glowDot(lerp(ax, bx, t), lerp(ay, by, t), 2.4 * (1 - k / 8), "#fff", 10); }
    ctx.globalAlpha = 1;
  }
  // Return: a bright pulse runs from the chosen star into Pip, then Pip flashes.
  if (firing >= 0) {
    const t = clamp(firing / 300);
    if (t < 1) {
      for (let k = 5; k >= 0; k--) {
        const tt = clamp(1 - outCubic(t) + k * 0.035);
        ctx.globalAlpha = 1 - k / 6; glowDot(lerp(pip.x, starX(sel), tt), lerp(pip.y, nodes[sel].y, tt), 3.4 - k * 0.4, "#fff", 14);
      }
      ctx.globalAlpha = 1;
    }
  }
  const fl = (now - flashAt) / 260;
  if (fl >= 0 && fl < 1) {
    ctx.save();
    const g = ctx.createRadialGradient(pip.x, pip.y, 0, pip.x, pip.y, PIP_R + 30 * fl);
    g.addColorStop(0, `rgba(255,255,255,${0.75 * (1 - fl)})`); g.addColorStop(0.55, hexA(T[sel].color, 0.45 * (1 - fl))); g.addColorStop(1, "rgba(255,255,255,0)");
    ctx.fillStyle = g; ctx.beginPath(); ctx.arc(pip.x, pip.y, PIP_R + 30 * fl, 0, 7); ctx.fill();
    ctx.restore();
    sparkle(pip.x, pip.y, 14 * (1 - fl) + 4, fl * 2, "#fff", 1 - fl);
  }
  // Pills and the hint.
  for (let i = 0; i < N; i++) {
    const d = delay(i), el = pills[i], n = nodes[i];
    const pop = !calm ? clamp((ms - d - 330) / 170) : level ? clamp((ms - i * STEP - 90) / 130) : (i === skipFly ? 1 : clamp(ms / TRANS_MS));
    let a = pop, s = calm ? 1 : 0.85 + 0.15 * outBack(pop, 2.2), ox = -side * (1 - pop) * (calm ? 5 : 14);
    if (firing >= 0) {
      if (i === sel) { const g = clamp(firing / 300); a = 1 - g; s = 1 - 0.35 * g; ox = (pip.x - n.x) * outCubic(g) * 0.55; }
      else a *= 1 - clamp(firing / 240);
    }
    const left = side > 0 ? n.x + 6 : n.x - 6 - widths[i];
    el.style.opacity = a.toFixed(3);
    el.style.transformOrigin = side > 0 ? "left center" : "right center";
    el.style.transform = `translate(${(left + ox).toFixed(1)}px, ${(n.y - 15).toFixed(1)}px) scale(${(s * (i === sel ? 1.05 : 1)).toFixed(3)})`;
    el.style.pointerEvents = pop > 0.6 && firing < 0 && dir > 0 && !trans ? "auto" : "none";
    el.classList.toggle("sel", i === sel);
  }
  const last = nodes[N - 1], hp = clamp(calm ? (ms - 200) / 150 : (ms - OPEN_MS + 120) / 200) * (firing >= 0 || dir < 0 ? 0 : 1);
  const below = last.y + 40 < area.y + area.h, anchor = below ? last : nodes[0];
  hint.style.opacity = (hp * 0.95).toFixed(2);
  hint.style.transform = `translate(${side > 0 ? anchor.x + 10 : anchor.x - 10 - hint.offsetWidth}px, ${below ? anchor.y + 22 : anchor.y - 44}px)`;
}

function select(i) { if (!N) return; i = (i + N) % N; if (i === sel) return; prevSel = sel; sel = i; selAt = now; }
function fire() {
  if (fireAt >= 0 || dir <= 0 || (e < (calm ? 120 : OPEN_MS * 0.6) && now > fastUntil)) return;   // a quick second key runs at once
  fireAt = now;
  setTimeout(() => { flashAt = now; }, 280);
  setTimeout(() => { finish = { type: "run", id: T[sel].id }; reset(); }, 520);
}
function reset() {
  open = false; dir = 0; e = 0; fireAt = -1;
  pills.forEach(p => { p.style.opacity = 0; p.style.pointerEvents = "none"; });
  hint.style.opacity = 0; ghostEl.style.opacity = 0;
  if (trans) trans.pills.forEach(p => p.remove());
  if (header) { header.remove(); header = null; }
  trans = null; level = null; hub = null; skipFly = -1; calm = false;
}

/* Called by the app. */
window.ringOpen = data => {
  sizeCanvas();
  pip = data.pip; area = data.area; ghost = !!data.ghost;
  ghostEl.style.left = pip.x + "px"; ghostEl.style.top = pip.y + "px";
  const list = (data.templates || []).filter(t => t && t.id);
  const fit = Math.max(1, Math.floor((area.h - 80) / 26) + 1);
  reset(); side = pip.x > area.x + area.w / 2 ? -1 : 1; org = { ...pip };
  TOP = topList(list).slice(0, fit);
  build(TOP);
  if (!N) { post({ type: "closed" }); return; }
  layout();
  finish = null; fireAt = -1; flashAt = -1e9;
  sel = prevSel = Math.min(Math.round(MID - 0.01), N - 1);   // the middle one, nearest Pip
  selAt = -1e9; open = true; dir = 1; e = 0; fastUntil = now + FAST_MS;
};
window.ringClose = () => { if (!open) { post({ type: "closed" }); return; } if (fireAt < 0) dir = -1; };

document.addEventListener("keydown", ev => {
  // Every key is the ring's: one the page leaves unhandled goes back to macOS, which beeps.
  if (!ev.metaKey) ev.preventDefault();
  if (!open || dir <= 0 || fireAt >= 0) { if (ev.key === "Escape") ringClose(); return; }
  const fwd = side > 0 ? "ArrowRight" : "ArrowLeft", back = side > 0 ? "ArrowLeft" : "ArrowRight";
  if (ev.key === "ArrowDown" || (ev.key === "Tab" && !ev.shiftKey)) { ev.preventDefault(); select(sel + 1); }
  else if (ev.key === "ArrowUp" || (ev.key === "Tab" && ev.shiftKey)) { ev.preventDefault(); select(sel - 1); }
  else if (ev.key === fwd) { ev.preventDefault(); if (T[sel].items) enterGroup(sel); }
  else if (ev.key === back || ev.key === "Backspace") { ev.preventDefault(); goBack(); }
  else if (ev.key === "Home") select(0);
  else if (ev.key === "End") select(N - 1);
  else if (ev.key === "Enter") { ev.preventDefault(); choose(); }
  else if (ev.key === "Escape") { ev.preventDefault(); if (level) goBack(); else ringClose(); }
  else if (/^[1-9]$/.test(ev.key) && +ev.key <= N && !trans) { select(+ev.key - 1); choose(); }
  else if (/^[1-9]$/.test(ev.key) && trans && +ev.key <= N) { select(+ev.key - 1); choose(); }
});
document.addEventListener("mousedown", ev => {
  if (header && ev.target.closest(".pill") === header) { ev.preventDefault(); goBack(); return; }
  if (!ev.target.closest(".pill")) ringClose();
});

let lastTs = performance.now();
function frame(ts) {
  const dt = Math.min(50, ts - lastTs); lastTs = ts; now += dt;
  if (finish && !open) {          // one cleared frame is drawn before the app hides the window
    ctx.clearRect(0, 0, W, H);
    const m = finish; finish = null; requestAnimationFrame(() => post(m));
  }
  if (dir > 0) e = Math.min(OPEN_MS, e + dt * SPEED);
  else if (dir < 0) { e -= dt * SPEED * 1.8; if (e <= 0) { reset(); finish = { type: "closed" }; } }
  if (open) draw();
  requestAnimationFrame(frame);
}
sizeCanvas();
requestAnimationFrame(frame);
post({ type: "ready" });

</script>
</body>
</html>
"""#
