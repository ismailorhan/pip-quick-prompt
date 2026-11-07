import AppKit
import WebKit

/// The template ring's window: borderless, transparent, over the whole screen Pip is on, above the Dock and the
/// menu bar. It takes the keyboard without making Quick Prompt the active app, so the app you were in keeps its
/// selection; the window is gone again before ⌘C is pressed in that app.
final class RingPanel: NSPanel {
    override var canBecomeKey: Bool { true }
    override func constrainFrameRect(_ frameRect: NSRect, to screen: NSScreen?) -> NSRect { frameRect }
}

/// The template ring (the shortcut, default ⌥⇧Z): Pip casts the prompt templates out round itself as a
/// constellation, on the side of the screen with more room; ↑ ↓ or a number choose, Return runs the template on
/// the selection and pastes the answer, Esc (or the shortcut again, or a click elsewhere) pulls them back.
/// The page below draws it all (tools/pip-ring-lab.html, look C, ×1.2); this class only shows and hides it.
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
            "templates": templates.map { ["id": $0["id"] as? String ?? "", "title": $0["title"] as? String ?? "", "color": $0["color"] as? String ?? ""] },
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
.hintbar { position: fixed; left: 0; top: 0; font-size: 11px; color: rgba(255,255,255,.75); white-space: nowrap; opacity: 0; pointer-events: none;
  padding: 4px 8px; border-radius: 8px; background: rgba(30,28,42,.82); }
.hintbar kbd { font: 10.5px ui-monospace, Menlo, monospace; padding: 1px 5px; border-radius: 5px; background: rgba(255,255,255,.1);
  border: 1px solid rgba(255,255,255,.16); color: rgba(255,255,255,.85); }
</style>
</head>
<body>
<canvas id="fx"></canvas>
<div id="ghost"></div>
<div class="hintbar" id="hint"><kbd>↑</kbd><kbd>↓</kbd> choose · <kbd>↵</kbd> run and paste · <kbd>esc</kbd></div>
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

function build(list) {
  pills.forEach(p => p.remove());
  T = list; N = list.length; MID = (N - 1) / 2;
  pills = list.map((tp, i) => {
    const el = document.createElement("div");
    el.className = "pill"; el.style.setProperty("--c", hexA(tp.color, 1));
    const n = document.createElement("span"); n.className = "n" + (i < 9 ? "" : " none"); n.textContent = i < 9 ? String(i + 1) : "";
    const t = document.createElement("span"); t.className = "t"; t.textContent = tp.title;
    const d = document.createElement("span"); d.className = "dot"; d.style.background = hexA(tp.color, 1);
    el.append(n, t, d);
    el.addEventListener("mouseenter", () => { if (open && dir > 0 && fireAt < 0) select(i); });
    el.addEventListener("click", ev => { ev.stopPropagation(); if (open && dir > 0) { select(i); fire(); } });
    document.body.appendChild(el);
    return el;
  });
}

/* The list: pills on a circle round Pip, on the side with more room, moved up or down to stay on screen; the circle
   grows when it has to reach far (a corner gives a quarter circle round Pip). */
function layout() {
  side = pip.x > area.x + area.w / 2 ? -1 : 1;
  const usable = area.h - 80;
  gap = N > 1 ? Math.min(GAP, usable / (N - 1)) : 0;
  const total = (N - 1) * gap;
  const top = clamp(pip.y - total / 2, area.y + 40, Math.max(area.y + 40, area.y + area.h - 36 - total));
  const ys = T.map((_, i) => top + i * gap);
  const far = Math.max(0, ...ys.map(y => Math.abs(y - pip.y)));
  const R = Math.max(150, far * 1.04);
  widths = pills.map(p => p.offsetWidth || 150);
  nodes = ys.map((y, i) => {
    const dy = y - pip.y, dx = Math.sqrt(Math.max(R * R - dy * dy, (R * 0.32) ** 2));
    let x = pip.x + side * dx;
    if (side > 0) x = Math.min(x, area.x + area.w - 16 - widths[i]); else x = Math.max(x, area.x + 16 + widths[i]);
    return { x, y };
  });
}
const starX = i => nodes[i].x - side * (i % 2 ? 22 : 9);
function starPos(i, t) {
  const n = { x: starX(i), y: nodes[i].y }, dx = n.x - pip.x, dy = n.y - pip.y, dist = Math.hypot(dx, dy) || 1;
  const nx = -dy / dist, ny = dx / dist, swirl = dist * 0.42 * side;
  const cx = pip.x + dx * 0.5 + nx * swirl, cy = pip.y + dy * 0.5 + ny * swirl, u = 1 - t;
  return { x: u * u * pip.x + 2 * u * t * cx + t * t * n.x, y: u * u * pip.y + 2 * u * t * cy + t * t * n.y };
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

  // Casting: a ring leaves Pip.
  if (ms < 360 && dir > 0) {
    const t = ms / 360; ctx.save(); ctx.globalAlpha = (1 - t) * 0.8; ctx.strokeStyle = "#fff"; ctx.lineWidth = 1.5;
    ctx.beginPath(); ctx.arc(pip.x, pip.y, PIP_R + t * 40, 0, 7); ctx.stroke(); ctx.restore();
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
  const landed = delay(0) + 380, lp = clamp((ms - landed + 60) / 260);
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
      ctx.beginPath(); ctx.moveTo(pip.x, pip.y); ctx.quadraticCurveTo(lerp(pip.x, sx, 0.5), lerp(pip.y, sy, 0.2), sx, sy); ctx.stroke();
    }
    ctx.restore();
  }
  // The stars: flying out with tails, a burst as each lands; the chosen one big and turning.
  for (let i = 0; i < N; i++) {
    const d = delay(i), raw = clamp((ms - d) / 380); if (raw <= 0) continue;
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
    if (burst < 1 && dir > 0) { ctx.save(); ctx.globalAlpha = (1 - burst) * 0.7; ctx.strokeStyle = "#fff"; ctx.lineWidth = 1; ctx.beginPath(); ctx.arc(x, y, 3 + burst * 16, 0, 7); ctx.stroke(); ctx.restore(); }
    const r = isSel ? 8 : 4.2 + (1 - burst) * 5;
    sparkle(x, y, r, isSel ? now * 0.0015 : Math.PI / 4 * (i % 2), isSel ? hexA(T[i].color, 1) : "#fff", fade * (isSel ? 1 : 0.85 + 0.15 * Math.sin(now * 0.004 + i * 2)));
    if (isSel) sparkle(x, y, 3, -now * 0.002, "#fff", fade);
  }
  // A comet from the old choice to the new one.
  const ct = (now - selAt) / 200;
  if (ct < 1 && firing < 0 && prevSel !== sel && ms >= OPEN_MS) {
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
    const d = delay(i), pop = clamp((ms - d - 330) / 170), el = pills[i], n = nodes[i];
    let a = pop, s = 0.85 + 0.15 * outBack(pop, 2.2), ox = -side * (1 - pop) * 14;
    if (firing >= 0) {
      if (i === sel) { const g = clamp(firing / 300); a = 1 - g; s = 1 - 0.35 * g; ox = (pip.x - n.x) * outCubic(g) * 0.55; }
      else a *= 1 - clamp(firing / 240);
    }
    const left = side > 0 ? n.x + 6 : n.x - 6 - widths[i];
    el.style.opacity = a.toFixed(3);
    el.style.transformOrigin = side > 0 ? "left center" : "right center";
    el.style.transform = `translate(${(left + ox).toFixed(1)}px, ${(n.y - 15).toFixed(1)}px) scale(${(s * (i === sel ? 1.05 : 1)).toFixed(3)})`;
    el.style.pointerEvents = pop > 0.6 && firing < 0 && dir > 0 ? "auto" : "none";
    el.classList.toggle("sel", i === sel);
  }
  const last = nodes[N - 1], hp = clamp((ms - OPEN_MS + 120) / 200) * (firing >= 0 || dir < 0 ? 0 : 1);
  const below = last.y + 40 < area.y + area.h, anchor = below ? last : nodes[0];
  hint.style.opacity = (hp * 0.95).toFixed(2);
  hint.style.transform = `translate(${side > 0 ? anchor.x + 10 : anchor.x - 10 - hint.offsetWidth}px, ${below ? anchor.y + 22 : anchor.y - 44}px)`;
}

function select(i) { if (!N) return; i = (i + N) % N; if (i === sel) return; prevSel = sel; sel = i; selAt = now; }
function fire() {
  if (fireAt >= 0 || dir <= 0 || e < OPEN_MS * 0.6) return;
  fireAt = now;
  setTimeout(() => { flashAt = now; }, 280);
  setTimeout(() => { finish = { type: "run", id: T[sel].id }; reset(); }, 520);
}
function reset() {
  open = false; dir = 0; e = 0; fireAt = -1;
  pills.forEach(p => { p.style.opacity = 0; p.style.pointerEvents = "none"; });
  hint.style.opacity = 0; ghostEl.style.opacity = 0;
}

/* Called by the app. */
window.ringOpen = data => {
  sizeCanvas();
  pip = data.pip; area = data.area; ghost = !!data.ghost;
  ghostEl.style.left = pip.x + "px"; ghostEl.style.top = pip.y + "px";
  const list = (data.templates || []).filter(t => t && t.id);
  const fit = Math.max(1, Math.floor((area.h - 80) / 26) + 1);
  build(list.slice(0, fit));
  if (!N) { post({ type: "closed" }); return; }
  layout();
  finish = null; fireAt = -1; flashAt = -1e9;
  sel = prevSel = Math.min(Math.round(MID - 0.01), N - 1);   // the middle one, nearest Pip
  selAt = -1e9; open = true; dir = 1; e = 0;
};
window.ringClose = () => { if (!open) { post({ type: "closed" }); return; } if (fireAt < 0) dir = -1; };

document.addEventListener("keydown", ev => {
  if (!open || dir <= 0 || fireAt >= 0) { if (ev.key === "Escape") ringClose(); return; }
  if (ev.key === "ArrowDown" || ev.key === "ArrowRight" || (ev.key === "Tab" && !ev.shiftKey)) { ev.preventDefault(); select(sel + 1); }
  else if (ev.key === "ArrowUp" || ev.key === "ArrowLeft" || (ev.key === "Tab" && ev.shiftKey)) { ev.preventDefault(); select(sel - 1); }
  else if (ev.key === "Home") select(0);
  else if (ev.key === "End") select(N - 1);
  else if (ev.key === "Enter") { ev.preventDefault(); fire(); }
  else if (ev.key === "Escape") { ev.preventDefault(); ringClose(); }
  else if (/^[1-9]$/.test(ev.key) && +ev.key <= N) { select(+ev.key - 1); fire(); }
});
document.addEventListener("mousedown", ev => { if (!ev.target.closest(".pill")) ringClose(); });

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
