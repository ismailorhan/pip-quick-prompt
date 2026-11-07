// Quick open / close of the conversation with the real PopupController: where is the Pip the user sees, every frame?
import AppKit
import WebKit
let app = NSApplication.shared
app.setActivationPolicy(.accessory)
let popup = PopupController(url: URL(string: "http://127.0.0.1:\(ProcessInfo.processInfo.environment["PORT"] ?? "9981")/s/tok/")!)
popup.open(at: NSPoint(x: 1500, y: 300), bottomCenter: false)
func after(_ t: Double, _ f: @escaping () -> Void) { DispatchQueue.main.asyncAfter(deadline: .now() + t, execute: f) }
func js(_ s: String) { popup.webView.evaluateJavaScript(s, completionHandler: nil) }
var home: NSPoint? = nil, worst: CGFloat = 0, bad = 0, samples = 0, pictureSamples = 0
var log: [String] = []
var step = 0
let t = Timer(timeInterval: 1.0 / 60.0, repeats: true) { _ in
  if let w = popup.frozenWindow, (popup.webView?.alphaValue ?? 1) < 1 {   // the picture is what shows
    let c = NSPoint(x: w.frame.midX, y: w.frame.midY)
    pictureSamples += 1
    if let h = home { let d = hypot(c.x - h.x, c.y - h.y); worst = max(worst, d); if d > 3 { bad += 1; log.append(String(format: "picture %.0f pt off", d)) } }
    return
  }
  popup.webView.evaluateJavaScript("(() => { const r = document.getElementById('miniOpen').getBoundingClientRect(); return [r.left + r.width/2, r.top + r.height/2, r.width, document.getElementById('root').classList.contains('is-mini') ? 1 : 0, innerWidth, window.__dbg ? window.__dbg() : '']; })()") { v, _ in
    guard let arr = v as? [Any], arr.count == 6 else { return }
    let a = arr.prefix(5).compactMap { $0 as? Double }; let dbg = arr[5] as? String ?? ""
    guard a.count == 5, a[2] > 0 else { return }
    if popup.frozenWindow != nil && (popup.webView?.alphaValue ?? 1) < 1 { return }
    let f = popup.panel.frame
    let c = NSPoint(x: f.minX + a[0], y: f.maxY - a[1])
    samples += 1
    if home == nil { home = c; return }
    let d = hypot(c.x - home!.x, c.y - home!.y)
    worst = max(worst, d)
    if d > 3 { bad += 1; log.append(String(format: "step %d: Pip drawn %.0f pt off, mini=%.0f page w=%.0f panel w=%.0f frozen=%@", step, d, a[3], a[4], f.width, popup.frozenWindow != nil ? "y" : "n") + " moving=\(popup.moving) pending=\(popup.pendingLayout != nil) " + dbg) }
  }
}
after(3) {
  RunLoop.main.add(t, forMode: .common)
  func next() {
    if step >= 24 { after(1.5) {
      print("samples: \(samples) page + \(pictureSamples) picture; off by more than 3 pt: \(bad); worst \(String(format: "%.1f", worst)) pt")
      print(log.prefix(30).joined(separator: "\n")); exit(0) }; return }
    if step % 2 == 0 { js("window.__quickPromptMenu && window.__quickPromptMenu('open')") }
    else { js("document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))") }
    step += 1
    after(Double.random(in: 0.12...0.45), next)
  }
  next()
}
after(40) { print("timeout"); exit(1) }
app.run()
