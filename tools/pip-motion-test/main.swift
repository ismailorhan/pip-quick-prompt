// Pip motion test: runs the real PopupController against a mock page server and checks every way Pip
// moves (drops, throws in eight directions, middle-click corner rounds, fast double clicks, hover) on every
// screen: Pip must end whole on screen, exactly in the expected corner or edge, with no back-and-forth.
// Run with ./run.sh (macOS with Xcode). Exit code 0 = all passed.
import AppKit
import WebKit
// Every way Pip moves, on both screens, checked: whole on screen, exact corners, no back-and-forth.
let app = NSApplication.shared
app.setActivationPolicy(.accessory)
let popup = PopupController(url: URL(string: "http://127.0.0.1:\(ProcessInfo.processInfo.environment["PORT"] ?? "9977")/s/tok/")!)
popup.open(at: nil, bottomCenter: true)
func after(_ t: Double, _ f: @escaping () -> Void) { DispatchQueue.main.asyncAfter(deadline: .now() + t, execute: f) }
func pip() -> NSPoint { let f = popup.panel.frame; return NSPoint(x: f.minX + (popup.lastAnchorX ?? 0), y: f.maxY - (popup.lastAnchorY ?? 0)) }
var fails = 0
func area(_ p: NSPoint) -> NSRect { let s = NSScreen.screens.first { NSMouseInRect(p, $0.frame, false) } ?? NSScreen.screens[0]; return PopupController.placement(on: s).area }
func check(_ label: String, expect: String?, path: [NSPoint], done: @escaping () -> Void) {
  // Let the page answer the edges (the toolbar turns), then judge where Pip is.
  after(0.25) {
    let p = pip(), a = area(p), r = popup.iconRadius
    var problems: [String] = []
    if p.x < a.minX + r - 1.5 || p.x > a.maxX - r + 1.5 || p.y < a.minY + r - 1.5 || p.y > a.maxY - r + 1.5 { problems.append("not whole on screen") }
    let atL = abs(p.x - (a.minX + r)) < 1.5, atR = abs(p.x - (a.maxX - r)) < 1.5, atB = abs(p.y - (a.minY + r)) < 1.5, atT = abs(p.y - (a.maxY - r)) < 1.5
    let at = [atL ? "L" : "", atR ? "R" : "", atB ? "B" : "", atT ? "T" : ""].joined()
    if let e = expect, Set(e) != Set(at) { problems.append("expected \(e), at '\(at)'") }
    var rev = 0
    for k in 0..<2 { var last: CGFloat = 0; for i in 1..<max(1, path.count) { let d = k == 0 ? path[i].x - path[i-1].x : path[i].y - path[i-1].y; if abs(d) > 1 { if last != 0 && (d > 0) != (last > 0) { rev += 1 }; last = d } } }
    if rev > 0 && !label.contains("off screen") { problems.append("\(rev) reversal(s)") }
    let p2 = pip()
    if abs(p2.x - (popup.iconScreenX ?? 0)) > 1 || abs(p2.y - (popup.iconScreenY ?? 0)) > 1 { problems.append("panel and Pip disagree") }
    if !problems.isEmpty { fails += 1 }
    print(String(format: "%@ %-34@ Pip %.0f,%.0f at '%@'  %@", problems.isEmpty ? "PASS" : "FAIL", label as NSString, p.x, p.y, at, problems.joined(separator: "; ")))
    done()
  }
}
func run(_ label: String, expect: String?, wait: Double = 0.45, _ action: @escaping () -> Void, done: @escaping () -> Void) {
  var path: [NSPoint] = []
  let timer = Timer(timeInterval: 1.0 / 120.0, repeats: true) { _ in path.append(pip()) }
  RunLoop.main.add(timer, forMode: .common)
  action()
  after(wait) { timer.invalidate(); check(label, expect: expect, path: path, done: done) }
}
func throwTo(_ vx: Double, _ vy: Double) {
  let t = ProcessInfo.processInfo.systemUptime, p = NSEvent.mouseLocation
  popup.finishDrag([(time: t - 0.05, point: p), (time: t, point: NSPoint(x: p.x + vx * 0.05, y: p.y + vy * 0.05))])
}
func drop(_ x: CGFloat, _ y: CGFloat) {
  let f = popup.panel.frame
  popup.panel.setFrameOrigin(NSPoint(x: x - (popup.lastAnchorX ?? 0), y: y + (popup.lastAnchorY ?? 0) - f.height))
  popup.finishDrag([])
}
func snap() { popup.webView.evaluateJavaScript("window.webkit.messageHandlers.quickPrompt.postMessage({type:'snap'})", completionHandler: nil) }

var steps: [(String, String?, Double, () -> Void)] = []
for (si, screen) in NSScreen.screens.enumerated() {
  let a = PopupController.placement(on: screen).area
  let tag = "s\(si)"
  let dirs: [(String, Double, Double, String)] = [("→", 3000, 0, "R"), ("←", -3000, 0, "L"), ("↑", 0, 3000, "T"), ("↓", 0, -3000, "B"),
    ("↗", 3000, 3000, "RT"), ("↖", -3000, 3000, "LT"), ("↘", 3000, -3000, "RB"), ("↙", -3000, -3000, "LB")]
  for (name, vx, vy, e) in dirs {
    steps.append(("\(tag) drop middle", "", 0.45, { drop(a.midX, a.midY) }))
    steps.append(("\(tag) throw \(name)", e, 0.45, { throwTo(vx, vy) }))
  }
  steps.append(("\(tag) drop bottom middle", "B", 0.45, { drop(a.midX, a.minY + 5) }))
  steps.append(("\(tag) snap → bottom left", "LB", 0.45, { snap() }))
  steps.append(("\(tag) snap → top left", "LT", 0.45, { snap() }))
  steps.append(("\(tag) snap → top right", "RT", 0.45, { snap() }))
  steps.append(("\(tag) snap → bottom right", "RB", 0.45, { snap() }))
  steps.append(("\(tag) snap → bottom left", "LB", 0.45, { snap() }))
  steps.append(("\(tag) 2 fast snaps → top right", "RT", 0.7, { snap(); after(0.08) { snap() } }))
  steps.append(("\(tag) drop near left, mid", nil, 0.45, { drop(a.minX + 120, a.midY + 37) }))
  steps.append(("\(tag) snap → left edge", "L", 0.45, { snap() }))
  steps.append(("\(tag) drop off screen", nil, 0.45, { drop(a.maxX + 40, a.minY - 30) }))
  steps.append(("\(tag) hover in/out (Pip still)", nil, 4.2, {
    popup.webView.evaluateJavaScript("__quickPromptHover(true)", completionHandler: nil)
    after(0.6) { popup.webView.evaluateJavaScript("__quickPromptHover(false)", completionHandler: nil) } }))
}
var i = 0
var before = NSPoint.zero
func next() {
  guard i < steps.count else { print(fails == 0 ? "ALL PASS" : "\(fails) FAILED"); exit(fails == 0 ? 0 : 2) }
  let (label, expect, wait, action) = steps[i]; i += 1
  before = pip()
  run(label, expect: expect, wait: wait, action) {
    if label.contains("hover") { let p = pip(); if abs(p.x - before.x) > 0.5 || abs(p.y - before.y) > 0.5 { fails += 1; print("FAIL hover moved Pip") } }
    after(0.1, next)
  }
}
after(3, next)
after(120) { print("timeout"); exit(1) }
app.run()
