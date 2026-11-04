// Shared by the Quick Prompt app and the standalone QuickPromptPopup helper (used by the Logi plugin):
// a small floating panel at the mouse cursor that shows a Quick Prompt page.
//
// The panel is transparent and non-activating: the app you were working in stays frontmost,
// so "Paste" pastes straight back into it. The page reports its size and the rectangles of its
// ".glass" shapes; behind each one we place a native Liquid Glass view (NSGlassEffectView,
// macOS 26+) or a vibrancy view on older systems. It also asks us to drag, pin, switch between
// the full popup and the floating icon, and show a context menu.

import AppKit
import ApplicationServices
import QuartzCore
import UniformTypeIdentifiers
import WebKit

let defaultPopupWidth: CGFloat = 520
// Looked up at runtime so the code also builds with older SDKs.
let glassViewClass: NSView.Type? = NSClassFromString("NSGlassEffectView") as? NSView.Type

final class PopupPanel: NSPanel {
    var onClose: (() -> Void)?

    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { true }

    // The controller keeps the panel on screen itself (Pip right in a corner leaves the panel's transparent
    // margin past the edge); macOS would push it down from under the menu bar, away from where Pip goes.
    override func constrainFrameRect(_ frameRect: NSRect, to screen: NSScreen?) -> NSRect { frameRect }

    // An accessory app has no Edit menu, so route the standard shortcuts ourselves.
    override func performKeyEquivalent(with event: NSEvent) -> Bool {
        let flags = event.modifierFlags.intersection(.deviceIndependentFlagsMask)
        let key = event.charactersIgnoringModifiers?.lowercased() ?? ""
        var action: Selector?
        if flags == .command {
            switch key {
            case "c": action = #selector(NSText.copy(_:))
            case "v": action = #selector(NSText.paste(_:))
            case "x": action = #selector(NSText.cut(_:))
            case "a": action = #selector(NSText.selectAll(_:))
            case "z": action = Selector(("undo:"))
            case "w":
                onClose?()
                return true
            default: break
            }
        } else if flags == [.command, .shift], key == "z" {
            action = Selector(("redo:"))
        }
        if let action = action, NSApp.sendAction(action, to: nil, from: self) {
            return true
        }
        return super.performKeyEquivalent(with: event)
    }

    override func cancelOperation(_ sender: Any?) {
        onClose?()
    }
}

/// The first click counts even while another app is in front: one click on Pip opens it (or starts a drag),
/// instead of the first click only bringing the panel forward.
final class ClickThroughWebView: WKWebView {
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
}

extension PopupController {
    /// Just under the Dock: above other apps and their floating windows, never over the Dock.
    static let belowDock = NSWindow.Level(rawValue: Int(CGWindowLevelForKey(.dockWindow)) - 1)
}

final class PopupController: NSObject, NSWindowDelegate, WKNavigationDelegate, WKUIDelegate, WKScriptMessageHandler {
    let url: URL
    /// Called once the popup has closed (the app forgets it; the helper quits).
    var onClosed: ((PopupController) -> Void)?
    /// Called when the engine no longer knows this popup's session (the app opens a fresh Pip).
    var onSessionEnded: (() -> Void)?
    private var panel: PopupPanel!
    private var webView: WKWebView!
    private var container: NSView!
    private var glassViews: [NSView] = []
    /// Per glass view: part of the conversation (slides with it), not Pip's own glass.
    private var glassInMain: [Bool] = []
    /// Which glass view is behind Pip's toolbar (it grows from / shrinks to the thin line).
    private var barGlassIndex: Int?
    private var topLeft = NSPoint.zero
    private var visibleArea = NSRect.zero
    private var shown = false
    private var closing = false
    private var popupWidth: CGFloat = defaultPopupWidth
    /// Pinned popups survive new popups (and collapse to an icon instead of closing; the page handles that).
    private(set) var pinned = false
    private var dragTimer: Timer?
    private var dragStart = NSPoint.zero
    private var dragOrigin = NSPoint.zero
    private var dragSamples: [(time: TimeInterval, point: NSPoint)] = []
    /// The page shows either the full popup or the floating icon ("mini"); the icon keeps its own place.
    private var isMini = false
    private var miniTopLeft: NSPoint?
    /// While the floating icon shows: is the mouse over it? Checked here because a panel of a background
    /// app gets no hover events from WebKit while another app is in front.
    private var hoverTimer: Timer?
    private var mouseInside = false
    /// The icon's middle: inside the page at the last layout, and on screen. The panel keeps the icon
    /// where it is while the conversation opens beside it, the toolbar opens under it, and so on.
    private var lastAnchorX: CGFloat?
    private var iconScreenX: CGFloat?
    private var lastAnchorY: CGFloat?
    private var iconScreenY: CGFloat?
    /// While Pip is on its own: the parts of the page that take clicks (page coordinates, top-left origin).
    private var hitRects: [NSRect]?
    /// Pip's radius in points (the page reports it with each layout).
    private var iconRadius: CGFloat = 28

    /// A glide is running (see glide): layouts wait for it, so nothing moves the panel mid-way.
    private var moving = false
    private var moveID = 0
    private var pendingLayout: [String: Any]?

    /// Which side of the conversation the icon sits on: away from the nearer screen edge.
    private var side = "left"

    init(url: URL) {
        self.url = url
        super.init()
    }

    /// Creates the panel near the mouse cursor and loads the page.
    /// Where Pip is on screen (its middle), once the page has laid itself out.
    var iconPoint: NSPoint? {
        guard let x = iconScreenX, let y = iconScreenY, lastAnchorX != nil else { return nil }
        return NSPoint(x: x, y: y)
    }

    /// Where Pip was last left (dragged or closed), if that is still on a screen.
    static func savedPipPoint() -> NSPoint? {
        guard let saved = UserDefaults.standard.array(forKey: "pipPosition") as? [Double], saved.count == 2 else { return nil }
        let point = NSPoint(x: saved[0], y: saved[1])
        return NSScreen.screens.contains(where: { NSMouseInRect(point, $0.frame, false) }) ? point : nil
    }

    /// anchor: where Pip goes (the conversation opens beside it); nil = at the mouse (the Logi popup helper),
    /// or, with bottomCenter, the middle of the bottom of the screen the mouse is on.
    func open(at anchor: NSPoint? = nil, bottomCenter: Bool = false) {
        let mouse = NSEvent.mouseLocation
        let reference = anchor ?? mouse
        let screen = NSScreen.screens.first(where: { NSMouseInRect(reference, $0.frame, false) }) ?? NSScreen.main ?? NSScreen.screens[0]
        updateArea(screen)
        var point = reference
        if anchor == nil && bottomCenter {
            point = NSPoint(x: visibleArea.midX, y: visibleArea.minY + 90)
        }
        side = point.x > visibleArea.midX ? "right" : "left"
        iconScreenX = point.x
        iconScreenY = point.y
        let initialHeight: CGFloat = 76
        let x = min(max(mouse.x - 24, visibleArea.minX + 8), visibleArea.maxX - defaultPopupWidth - 8)
        let top = min(max(mouse.y - 12, visibleArea.minY + initialHeight + 8), visibleArea.maxY - 8)
        topLeft = NSPoint(x: x, y: top)

        let frame = NSRect(x: topLeft.x, y: topLeft.y - initialHeight, width: defaultPopupWidth, height: initialHeight)
        panel = PopupPanel(contentRect: frame,
                           styleMask: [.borderless, .nonactivatingPanel, .fullSizeContentView],
                           backing: .buffered,
                           defer: false)
        panel.isFloatingPanel = true
        panel.level = .floating
        panel.hidesOnDeactivate = false
        panel.isOpaque = false
        panel.backgroundColor = .clear
        // No window shadow: macOS traces it around the transparent panel's shapes, which shows up as a dark
        // ring over light backgrounds (e.g. remote desktop windows). The glass and the page draw their own.
        panel.hasShadow = false
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        panel.delegate = self
        panel.onClose = { [weak self] in self?.close() }

        // Transparent container: glass views (behind) + the web page (on top).
        container = NSView(frame: NSRect(origin: .zero, size: frame.size))
        container.autoresizingMask = [.width, .height]

        let config = WKWebViewConfiguration()
        config.userContentController.add(self, name: "quickPrompt")
        let glassKind = glassViewClass != nil ? "liquid" : "vibrancy"
        config.userContentController.addUserScript(WKUserScript(
            source: "window.__quickPromptNative = { glass: '\(glassKind)', side: '\(side)' };",
            injectionTime: .atDocumentStart,
            forMainFrameOnly: true))
        webView = ClickThroughWebView(frame: container.bounds, configuration: config)
        webView.autoresizingMask = [.width, .height]
        webView.navigationDelegate = self
        webView.uiDelegate = self
        webView.setValue(false, forKey: "drawsBackground")
        webView.underPageBackgroundColor = .clear
        container.addSubview(webView)
        panel.contentView = container

        webView.load(URLRequest(url: url))

        // Show when the page is ready (looks instant), or after a short fallback delay.
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.6) { [weak self] in self?.show() }
    }

    private func show() {
        guard !shown, !closing else { return }
        shown = true
        panel.makeKeyAndOrderFront(nil)   // becomes key without activating: the previous app stays frontmost
        panel.makeFirstResponder(webView)
    }

    func close() {
        guard !closing else { return }
        closing = true
        // Remember where Pip was, for the next popup when Pip is not on screen.
        if let point = iconPoint {
            UserDefaults.standard.set([Double(point.x), Double(point.y)], forKey: "pipPosition")
        }
        dragTimer?.invalidate()
        hoverTimer?.invalidate()
        frozenWindow?.orderOut(nil)
        frozenWindow = nil
        panel?.orderOut(nil)
        webView?.configuration.userContentController.removeScriptMessageHandler(forName: "quickPrompt")
        webView?.stopLoading()
        onClosed?(self)
    }

    private func watchHover(_ on: Bool) {
        hoverTimer?.invalidate()
        hoverTimer = nil
        mouseInside = false
        panel?.ignoresMouseEvents = false
        guard on else { return }
        let timer = Timer(timeInterval: 0.08, target: self, selector: #selector(hoverTick(_:)), userInfo: nil, repeats: true)
        RunLoop.main.add(timer, forMode: .common)
        hoverTimer = timer
    }

    @objc private func hoverTick(_ timer: Timer) {
        guard let panel = panel, panel.isVisible, dragTimer == nil else { return }
        let mouse = NSEvent.mouseLocation
        let inside = NSMouseInRect(mouse, panel.frame, false)
        // Pip on its own keeps room for its toolbar: clicks on the empty part go to the app behind.
        if let hits = hitRects {
            let point = NSPoint(x: mouse.x - panel.frame.minX, y: panel.frame.maxY - mouse.y)   // page coordinates
            let onSomething = inside && hits.contains { $0.insetBy(dx: -3, dy: -3).contains(point) }
            if panel.ignoresMouseEvents == onSomething {
                panel.ignoresMouseEvents = !onSomething
            }
        }
        if inside != mouseInside {
            mouseInside = inside
            webView?.evaluateJavaScript("window.__quickPromptHover && window.__quickPromptHover(\(inside))", completionHandler: nil)
        }
    }

    /// Resizes the panel keeping its top-left corner (the page reports its own size), inside the screen.
    private func setSize(width requestedWidth: CGFloat?, height requested: CGFloat) {
        if let w = requestedWidth {
            popupWidth = min(max(w.rounded(.up), 56), visibleArea.width - 16)
        }
        let height = min(max(requested.rounded(.up), 40), visibleArea.height * 0.85)
        // The full popup stays on screen with a small gap. Pip on its own is never pushed: it was placed on
        // screen and off the Dock when dropped, and near an edge its toolbar opens inward (reportEdges), so
        // pushing the panel would only move Pip away from the edge while the toolbar opens or turns.
        let requested = NSRect(x: topLeft.x, y: topLeft.y - height, width: popupWidth, height: height)
        let frame = isMini ? requested : fit(requested, edge: 8)
        topLeft = NSPoint(x: frame.minX, y: frame.maxY)
        panel.setFrame(frame, display: true, animate: false)
        container.layoutSubtreeIfNeeded()
    }

    /// Moves the panel with the mouse until the button is released (started by the page on mouse down).
    /// A quick throw sends it to the screen corner in that direction; otherwise it stays where dropped.
    private func startDrag() {
        dragStart = NSEvent.mouseLocation
        dragOrigin = panel.frame.origin
        dragSamples = []
        dragTimer?.invalidate()
        let timer = Timer(timeInterval: 1.0 / 120.0, target: self, selector: #selector(dragTick(_:)), userInfo: nil, repeats: true)
        // .common so it also runs while the web view is tracking the mouse.
        RunLoop.main.add(timer, forMode: .common)
        dragTimer = timer
    }

    @objc private func dragTick(_ timer: Timer) {
        let now = NSEvent.mouseLocation
        let time = ProcessInfo.processInfo.systemUptime
        dragSamples.append((time, now))
        dragSamples.removeAll { time - $0.time > 0.08 }
        if NSEvent.pressedMouseButtons & 1 == 0 {
            timer.invalidate()
            dragTimer = nil
            finishDrag(dragSamples)
            return
        }
        panel.setFrameOrigin(NSPoint(x: dragOrigin.x + now.x - dragStart.x, y: dragOrigin.y + now.y - dragStart.y))
    }

    /// The screen area the popup and Pip may use (see placement).
    private func updateArea(_ screen: NSScreen) {
        visibleArea = PopupController.placement(on: screen).area
    }

    /// The Dock is always above the popup and Pip (see belowDock). A Dock that hides covers them only while it
    /// shows, so the whole screen below the menu bar is theirs; one that stays on screen is the edge.
    static func placement(on screen: NSScreen) -> (area: NSRect, dock: NSRect?) {
        let full = screen.frame
        let visible = screen.visibleFrame
        let autohide = UserDefaults(suiteName: "com.apple.dock")?.bool(forKey: "autohide") == true
        guard autohide else { return (visible, nil) }
        var area = full
        area.size.height = visible.maxY - full.minY   // below the menu bar
        return (area, nil)
    }

    /// Moves a frame inside the usable area (`edge` points in from its sides).
    private func fit(_ frame: NSRect, edge: CGFloat) -> NSRect {
        let area = visibleArea.insetBy(dx: edge, dy: edge)
        var f = frame
        f.origin.x = min(max(f.minX, area.minX), area.maxX - f.width)
        f.origin.y = min(max(f.minY, area.minY), area.maxY - f.height)
        return f
    }

    // MARK: moving Pip
    //
    // Pip's centre (iconScreenX/Y) is the one truth; the panel follows it through the page's anchors (where
    // Pip's centre is inside the page). Every move (drop, throw, middle click) picks a target, keeps it on
    // screen and goes through glide(to:). While a glide runs, layouts from the page wait (pendingLayout),
    // so nothing else moves the panel half-way; edges, side and the saved place update when it ends.

    /// The rect a move places: Pip's circle while it is on its own (so it can sit right in a corner, its
    /// toolbar opening inward), the whole popup while the conversation is open.
    private func moveSubject(atPanel frame: NSRect) -> NSRect {
        guard isMini, let ax = lastAnchorX, let ay = lastAnchorY else { return frame }
        let r = iconRadius
        return NSRect(x: frame.minX + ax - r, y: frame.maxY - ay - r, width: 2 * r, height: 2 * r)
    }

    /// The panel frame (of `base`'s size) that puts the move subject at `subject`.
    private func panelFrame(placing subject: NSRect, from base: NSRect) -> NSRect {
        let current = moveSubject(atPanel: base)
        return base.offsetBy(dx: subject.minX - current.minX, dy: subject.minY - current.minY)
    }

    /// Where the panel is headed: the end of a running glide, else where it is.
    private var restingFrame: NSRect {
        let f = panel.frame
        return NSRect(x: topLeft.x, y: topLeft.y - f.height, width: f.width, height: f.height)
    }

    private func finishDrag(_ samples: [(time: TimeInterval, point: NSPoint)]) {
        updateArea(panel.screen ?? NSScreen.main ?? NSScreen.screens[0])
        let screen = visibleArea
        let frame = panel.frame   // where the drag left it
        var subject = moveSubject(atPanel: frame)
        if let first = samples.first, let last = samples.last, last.time - first.time > 0.01 {
            let dt = CGFloat(last.time - first.time)
            let vx = (last.point.x - first.point.x) / dt
            let vy = (last.point.y - first.point.y) / dt
            let speed = hypot(vx, vy)
            if speed > 1400 {
                // Thrown: to the corner (or edge) in the direction of the throw.
                if abs(vx) > speed * 0.3 {
                    subject.origin.x = vx > 0 ? screen.maxX - subject.width : screen.minX
                }
                if abs(vy) > speed * 0.3 {
                    subject.origin.y = vy > 0 ? screen.maxY - subject.height : screen.minY
                }
            }
        }
        glide(to: panelFrame(placing: fit(subject, edge: 0), from: frame))
    }

    /// Middle click (or ⌥-click) on Pip. Away from the edges: glide to the nearest one. On an edge: go
    /// clockwise along it to the next corner (bottom → bottom left, left → top left, top → top right, right
    /// → bottom right), so clicking again goes round the corners. A click during a glide counts from its end.
    private func snapToNearestEdge() {
        updateArea(panel.screen ?? NSScreen.main ?? NSScreen.screens[0])
        let base = restingFrame
        let subject = fit(moveSubject(atPanel: base), edge: 0)
        let a = visibleArea
        let left = subject.minX <= a.minX + 1, right = subject.maxX >= a.maxX - 1
        let bottom = subject.minY <= a.minY + 1, top = subject.maxY >= a.maxY - 1
        let minX = a.minX, maxX = a.maxX - subject.width
        let minY = a.minY, maxY = a.maxY - subject.height
        var target = subject
        if left && !top {
            target.origin = NSPoint(x: minX, y: maxY)          // up the left edge to the top left
        } else if top && !right {
            target.origin = NSPoint(x: maxX, y: maxY)          // along the top to the top right
        } else if right && !bottom {
            target.origin = NSPoint(x: maxX, y: minY)          // down the right edge to the bottom right
        } else if bottom {
            target.origin = NSPoint(x: minX, y: minY)          // along the bottom to the bottom left
        } else {
            let distances: [(CGFloat, Int)] = [
                (subject.minX - a.minX, 0), (a.maxX - subject.maxX, 1),
                (subject.minY - a.minY, 2), (a.maxY - subject.maxY, 3),
            ]
            switch distances.min(by: { $0.0 < $1.0 })?.1 ?? 0 {
            case 0: target.origin.x = minX
            case 1: target.origin.x = maxX
            case 2: target.origin.y = minY
            default: target.origin.y = maxY
            }
        }
        glide(to: panelFrame(placing: target, from: base))
    }

    /// The one way the panel moves on its own: Pip's place is set to the end at once, the panel glides there.
    private func glide(to target: NSRect) {
        topLeft = NSPoint(x: target.minX, y: target.maxY)
        if isMini {
            miniTopLeft = topLeft
        }
        if let anchor = lastAnchorX {
            iconScreenX = target.minX + anchor
        }
        if let anchorY = lastAnchorY {
            iconScreenY = target.maxY - anchorY
        }
        // Remember where Pip was left, for the next start.
        if let x = iconScreenX, let y = iconScreenY {
            UserDefaults.standard.set([Double(x), Double(y)], forKey: "pipPosition")
        }
        moveID += 1
        let id = moveID
        let finish = { [weak self] in
            guard let self = self, id == self.moveID else { return }   // a newer glide took over
            self.moving = false
            // On the other half of the screen the conversation opens on Pip's other side; near an edge its
            // toolbar turns (the page answers with a layout, which keeps Pip still).
            self.updateSide()
            if let body = self.pendingLayout {
                self.pendingLayout = nil
                self.handle(body)
            }
        }
        let from = panel.frame
        guard abs(from.minX - target.minX) > 0.5 || abs(from.minY - target.minY) > 0.5 else {
            panel.setFrame(target, display: true)
            finish()
            return
        }
        moving = true
        NSAnimationContext.runAnimationGroup({ context in
            context.duration = 0.28
            context.timingFunction = CAMediaTimingFunction(name: .easeOut)
            self.panel.animator().setFrame(target, display: true)
        }, completionHandler: finish)
    }

    /// Tells the page how much room Pip's middle has to the left, right and below, so its toolbar opens above
    /// near the bottom and lines up with Pip near a side.
    private func reportEdges() {
        guard let x = iconScreenX, let y = iconScreenY else { return }
        let a = visibleArea
        let js = String(format: "window.__quickPromptEdges && window.__quickPromptEdges({left:%.0f,right:%.0f,bottom:%.0f,top:%.0f})",
                        x - a.minX, a.maxX - x, y - a.minY, a.maxY - y)
        webView?.evaluateJavaScript(js, completionHandler: nil)
    }

    /// Puts the icon on the side away from the nearer screen edge (the page lays itself out mirrored).
    private func updateSide() {
        reportEdges()
        let x = iconScreenX ?? panel.frame.midX
        let next = x > visibleArea.midX ? "right" : "left"
        guard next != side else { return }
        side = next
        webView?.evaluateJavaScript("window.__quickPromptSide && window.__quickPromptSide('\(next)')", completionHandler: nil)
    }

    // MARK: context menu (right click on the floating icon)

    private func showMenu(_ items: [[String: Any]]) {
        buildMenu(items).popUp(positioning: nil, at: NSEvent.mouseLocation, in: nil)
    }

    /// Items: {id, title, checked?}, {separator: true}, or {title, items: [...]} for a submenu.
    private func buildMenu(_ items: [[String: Any]]) -> NSMenu {
        let menu = NSMenu()
        menu.autoenablesItems = false
        for item in items {
            if (item["separator"] as? Bool) == true {
                menu.addItem(.separator())
                continue
            }
            guard let title = item["title"] as? String else { continue }
            if let children = item["items"] as? [[String: Any]] {
                let parent = NSMenuItem(title: title, action: nil, keyEquivalent: "")
                parent.submenu = buildMenu(children)
                menu.addItem(parent)
                continue
            }
            guard let id = item["id"] as? String else { continue }
            let menuItem = NSMenuItem(title: title, action: #selector(menuItemSelected(_:)), keyEquivalent: "")
            menuItem.target = self
            menuItem.representedObject = id
            if (item["checked"] as? Bool) == true {
                menuItem.state = .on
            }
            menu.addItem(menuItem)
        }
        return menu
    }

    @objc private func menuItemSelected(_ sender: NSMenuItem) {
        guard let id = sender.representedObject as? String, id.allSatisfy({ $0.isLetter || $0.isNumber || $0 == "-" }) else { return }
        webView.evaluateJavaScript("window.__quickPromptMenu && window.__quickPromptMenu('\(id)')", completionHandler: nil)
    }

    /// Places one glass view behind each rectangle reported by the page (CSS pixels, top-left origin).
    /// pending: the conversation is about to slide in; its glass waits hidden for the "reveal" message.
    private func layoutGlass(_ rects: [[String: Any]], pending: Bool) {
        let height = container.bounds.height
        while glassViews.count < rects.count {
            let view = makeGlassView()
            container.addSubview(view, positioned: .below, relativeTo: webView)
            glassViews.append(view)
        }
        glassInMain = rects.map { ($0["m"] as? Bool) ?? true }
        for (index, view) in glassViews.enumerated() {
            guard index < rects.count else {
                view.isHidden = true
                continue
            }
            let r = rects[index]
            let x = CGFloat(r["x"] as? Double ?? 0)
            let y = CGFloat(r["y"] as? Double ?? 0)
            let w = CGFloat(r["w"] as? Double ?? 0)
            let h = CGFloat(r["h"] as? Double ?? 0)
            let radius = CGFloat(r["r"] as? Double ?? 0)
            view.isHidden = false
            view.alphaValue = pending && glassInMain[index] ? 0 : 1
            let final = NSRect(x: x, y: height - y - h, width: w, height: h)
            if (r["bar"] as? Bool) == true {
                barGlassIndex = index
            }
            if let from = r["from"] as? [String: Any] {
                // Pip's toolbar opening: its glass grows out of the thin line.
                view.frame = pageRect(from, height: height)
                setCornerRadius(view, min(radius, min(w, h) / 2))
                NSAnimationContext.runAnimationGroup { context in
                    context.duration = 0.22
                    context.timingFunction = CAMediaTimingFunction(controlPoints: 0.2, 0.8, 0.2, 1)
                    view.animator().frame = final
                }
                continue
            }
            view.frame = final
            setCornerRadius(view, min(radius, min(w, h) / 2))
        }
        panel.invalidateShadow()
    }

    /// A rect from the page ({x, y, w, h}, CSS pixels from the top left) in the container's coordinates.
    private func pageRect(_ r: [String: Any], height: CGFloat) -> NSRect {
        let x = CGFloat(r["x"] as? Double ?? 0), y = CGFloat(r["y"] as? Double ?? 0)
        let w = CGFloat(r["w"] as? Double ?? 0), h = CGFloat(r["h"] as? Double ?? 0)
        return NSRect(x: x, y: height - y - h, width: w, height: h)
    }

    private func makeGlassView() -> NSView {
        if let glassClass = glassViewClass {
            return glassClass.init(frame: .zero)
        }
        let effect = NSVisualEffectView(frame: .zero)
        effect.material = .hudWindow
        effect.blendingMode = .behindWindow
        effect.state = .active
        return effect
    }

    private func setCornerRadius(_ view: NSView, _ radius: CGFloat) {
        if view.responds(to: NSSelectorFromString("setCornerRadius:")) {
            view.setValue(radius, forKey: "cornerRadius")
        } else if let effect = view as? NSVisualEffectView {
            effect.maskImage = PopupController.roundedMask(radius: radius)
        } else {
            view.wantsLayer = true
            view.layer?.cornerRadius = radius
            view.layer?.masksToBounds = true
        }
    }

    private static func roundedMask(radius: CGFloat) -> NSImage {
        let edge = max(radius * 2 + 1, 1)
        let image = NSImage(size: NSSize(width: edge, height: edge), flipped: false) { rect in
            NSColor.black.setFill()
            NSBezierPath(roundedRect: rect, xRadius: radius, yRadius: radius).fill()
            return true
        }
        image.capInsets = NSEdgeInsets(top: radius, left: radius, bottom: radius, right: radius)
        image.resizingMode = .stretch
        return image
    }

    // MARK: page → panel messages

    func userContentController(_ userContentController: WKUserContentController, didReceive message: WKScriptMessage) {
        guard let body = message.body as? [String: Any] else { return }
        handle(body)
    }

    // MARK: Pip's picture while the panel resizes
    //
    // After a resize WKWebView shows its old picture for a few frames, pinned to the panel's top left, so
    // when the conversation opens (the panel grows to Pip's left) or tucks away, Pip jumps sideways for a
    // moment. Before such a change the page asks for a "freeze" and waits: a snapshot of Pip (taken while
    // the page is still unchanged) goes into a small window of its own at Pip's place, the web view hides,
    // and the page is told to go on. "thaw" (sent once the page is drawn at its new size) brings the web
    // view back. The picture has its own window because one inside the panel would move with it: macOS
    // moves a window at once but its views only with the next frame.

    private var frozenWindow: NSWindow?

    private func freeze(_ rect: [String: Any]) {
        let goOn: () -> Void = { [weak self] in
            self?.webView?.evaluateJavaScript("window.__quickPromptFrozen && window.__quickPromptFrozen()", completionHandler: nil)
        }
        let r = NSRect(x: rect["x"] as? Double ?? 0, y: rect["y"] as? Double ?? 0, width: rect["w"] as? Double ?? 0, height: rect["h"] as? Double ?? 0)
        guard frozenWindow == nil, let webView = webView, r.width > 0, r.height > 0 else { goOn(); return }
        // With Pip's shadow, and on whole points so the picture sits exactly where Pip is.
        let screenRect = NSRect(x: panel.frame.minX + r.minX, y: panel.frame.maxY - r.maxY, width: r.width, height: r.height)
            .insetBy(dx: -12, dy: -12).integral
        let config = WKSnapshotConfiguration()
        config.rect = NSRect(x: screenRect.minX - panel.frame.minX, y: panel.frame.maxY - screenRect.maxY,
                             width: screenRect.width, height: screenRect.height)   // the web view's top-left coordinates
        webView.takeSnapshot(with: config) { [weak self] image, _ in
            guard let self = self else { return }
            guard let image = image, !self.closing else { goOn(); return }
            let window = NSWindow(contentRect: screenRect, styleMask: .borderless, backing: .buffered, defer: false)
            window.isOpaque = false
            window.backgroundColor = .clear
            window.hasShadow = false
            window.ignoresMouseEvents = true
            window.isReleasedWhenClosed = false
            window.level = self.panel.level
            window.collectionBehavior = self.panel.collectionBehavior
            let view = NSImageView(frame: NSRect(origin: .zero, size: screenRect.size))
            view.image = image
            view.imageScaling = .scaleAxesIndependently
            window.contentView = view
            window.order(.above, relativeTo: self.panel.windowNumber)
            self.frozenWindow = window
            webView.alphaValue = 0
            CATransaction.flush()
            // The page changes (and the panel resizes) once the hidden web view is on screen.
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.02) { goOn() }
            // Never stay frozen if "thaw" does not come.
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.8) { [weak self] in self?.thaw() }
        }
    }

    private func thaw() {
        guard let window = frozenWindow else { return }
        frozenWindow = nil
        webView?.alphaValue = 1
        CATransaction.flush()
        // The picture goes a frame later, once the web view shows Pip again (no gap between them).
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.03) { window.orderOut(nil) }
    }

    private func handle(_ body: [String: Any]) {
        guard let type = body["type"] as? String else { return }
        switch type {
        case "freeze":
            if let rect = body["icon"] as? [String: Any] {
                freeze(rect)
            }
        case "thaw":
            thaw()
        case "layout":
            // A glide or a drag is moving the panel: this layout waits for its end (the latest one wins).
            if moving || dragTimer != nil {
                pendingLayout = body
                return
            }
            if let value = body["height"] as? Double {
                // Keep the floating icon where it is when its column changes width.
                let anchor = (body["anchorX"] as? Double).map { CGFloat($0) }
                let anchorY = (body["anchorY"] as? Double).map { CGFloat($0) }
                if let anchor = anchor, let screenX = iconScreenX {
                    topLeft.x = screenX - anchor
                }
                // Vertically too: beside the conversation the icon sits at the bottom, so the panel grows upward.
                if let anchorY = anchorY, let screenY = iconScreenY {
                    topLeft.y = screenY + anchorY
                }
                lastAnchorX = anchor
                lastAnchorY = anchorY
                if let r = body["iconR"] as? Double, r > 0 {
                    iconRadius = CGFloat(r)
                }
                hitRects = (body["hits"] as? [[String: Any]])?.map {
                    NSRect(x: $0["x"] as? Double ?? 0, y: $0["y"] as? Double ?? 0, width: $0["w"] as? Double ?? 0, height: $0["h"] as? Double ?? 0)
                }
                if hitRects == nil {
                    panel.ignoresMouseEvents = false
                }
                let wanted = topLeft
                setSize(width: (body["width"] as? Double).map { CGFloat($0) }, height: CGFloat(value))
                // Pip's place is the truth; the panel only follows it. macOS rounds window positions to whole
                // points, so recomputing Pip's place from the panel after every resize would creep it left
                // (the toolbar's opening alone sends a dozen sizes). Only a panel kept on screen moves Pip.
                if let anchor = anchor, iconScreenX == nil || abs(panel.frame.minX - wanted.x) > 1 {
                    iconScreenX = panel.frame.minX + anchor
                }
                if let anchorY = anchorY, iconScreenY == nil || abs(panel.frame.maxY - wanted.y) > 1 {
                    iconScreenY = panel.frame.maxY - anchorY
                }
            }
            if let rects = body["rects"] as? [[String: Any]] {
                layoutGlass(rects, pending: (body["pending"] as? Bool) ?? false)
            }
            show()
        case "close":
            close()
        case "sessionEnded":
            onSessionEnded?()
        case "drag":
            startDrag()
        case "mode":
            // The icon stays where it is either way; the conversation opens on its free side.
            let mini = (body["value"] as? String) == "mini"
            isMini = mini
            watchHover(mini)
            updateSide()
        case "snap":
            snapToNearestEdge()
        case "saveFile":
            // Export: the standard Save dialog; the page gets told whether it was saved.
            guard let content = body["content"] as? String else { break }
            let name = (body["name"] as? String).flatMap { $0.isEmpty ? nil : $0 } ?? "Quick Prompt templates.json"
            let savePanel = NSSavePanel()
            savePanel.nameFieldStringValue = name
            savePanel.canCreateDirectories = true
            if #available(macOS 11.0, *) {
                savePanel.allowedContentTypes = [.json]
            }
            NSApp.activate(ignoringOtherApps: true)
            var saved = false
            if savePanel.runModal() == .OK, let target = savePanel.url {
                saved = (try? content.write(to: target, atomically: true, encoding: .utf8)) != nil
            }
            webView?.evaluateJavaScript("window.__quickPromptSaved && window.__quickPromptSaved(\(saved))", completionHandler: nil)
        case "home":
            // Pip on its own (at start / Show Pip): where it was last left, or near the bottom-right corner.
            updateArea(panel.screen ?? NSScreen.main ?? NSScreen.screens[0])
            if let saved = UserDefaults.standard.array(forKey: "pipPosition") as? [Double], saved.count == 2,
               let screen = NSScreen.screens.first(where: { NSMouseInRect(NSPoint(x: saved[0], y: saved[1]), $0.frame, false) }) {
                // Kept whole on that screen (older versions could leave Pip half over an edge).
                updateArea(screen)
                let r = iconRadius, a = visibleArea
                iconScreenX = min(max(CGFloat(saved[0]), a.minX + r), a.maxX - r)
                iconScreenY = min(max(CGFloat(saved[1]), a.minY + r), a.maxY - r)
            } else {
                iconScreenX = visibleArea.maxX - 70
                iconScreenY = visibleArea.minY + 150
            }
            updateSide()
        case "barOut":
            // Pip's toolbar tucking away: its glass shrinks into the thin line; the page resizes afterwards.
            if let to = body["to"] as? [String: Any], let index = barGlassIndex, index < glassViews.count {
                let view = glassViews[index]
                let target = pageRect(to, height: container.bounds.height)
                NSAnimationContext.runAnimationGroup { context in
                    context.duration = body["duration"] as? Double ?? 0.16
                    context.timingFunction = CAMediaTimingFunction(name: .easeIn)
                    view.animator().frame = target
                }
            }
        case "reveal":
            // The conversation slides out of (or back into) the icon: move the glass with it.
            let goingIn = (body["dir"] as? String) == "in"
            let dx = CGFloat(body["dx"] as? Double ?? 0)
            let duration = body["duration"] as? Double ?? 0.22
            for (index, view) in glassViews.enumerated() where !view.isHidden && index < glassInMain.count && glassInMain[index] {
                let final = view.frame.origin
                let offset = NSPoint(x: final.x + dx, y: final.y)
                if goingIn {
                    view.alphaValue = 0
                    view.setFrameOrigin(offset)
                }
                NSAnimationContext.runAnimationGroup { context in
                    context.duration = duration
                    context.timingFunction = goingIn
                        ? CAMediaTimingFunction(controlPoints: 0.2, 0.8, 0.2, 1)
                        : CAMediaTimingFunction(name: .easeIn)
                    view.animator().alphaValue = goingIn ? 1 : 0
                    view.animator().setFrameOrigin(goingIn ? final : offset)
                }
            }
        case "menu":
            if let items = body["items"] as? [[String: Any]] {
                showMenu(items)
            }
        case "pin":
            pinned = (body["value"] as? Bool) ?? false
            // Pinned: above other apps' floating windows too, on every Space and over full-screen apps, but
            // under the Dock (the Dock always stays on top).
            panel.level = pinned ? PopupController.belowDock : .floating
        default:
            break
        }
    }

    // MARK: web view

    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        show()
    }

    func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation!, withError error: Error) {
        let html = "<body style='font:13px -apple-system;margin:16px;color:#c00;background:transparent'>Could not connect to Quick Prompt: \(error.localizedDescription)<br>Press Esc to close.</body>"
        webView.loadHTMLString(html, baseURL: nil)
        show()
    }

    func webView(_ webView: WKWebView, decidePolicyFor navigationAction: WKNavigationAction, decisionHandler: @escaping (WKNavigationActionPolicy) -> Void) {
        // Links in answers open in the default browser, not inside the popup.
        if navigationAction.navigationType == .linkActivated, let target = navigationAction.request.url {
            NSWorkspace.shared.open(target)
            decisionHandler(.cancel)
            return
        }
        decisionHandler(.allow)
    }

    func webView(_ webView: WKWebView, createWebViewWith configuration: WKWebViewConfiguration, for navigationAction: WKNavigationAction, windowFeatures: WKWindowFeatures) -> WKWebView? {
        if let target = navigationAction.request.url {
            NSWorkspace.shared.open(target)
        }
        return nil
    }

    /// The page's process crashed or was stopped by macOS: load it again instead of leaving an empty panel.
    func webViewWebContentProcessDidTerminate(_ webView: WKWebView) {
        guard !closing else { return }
        webView.load(URLRequest(url: url))
    }

    /// <input type="file"> (Import, Add Attachment…): the standard Open dialog.
    func webView(_ webView: WKWebView, runOpenPanelWith parameters: WKOpenPanelParameters, initiatedByFrame frame: WKFrameInfo,
                 completionHandler: @escaping ([URL]?) -> Void) {
        let openPanel = NSOpenPanel()
        openPanel.canChooseFiles = true
        openPanel.canChooseDirectories = false
        openPanel.allowsMultipleSelection = parameters.allowsMultipleSelection
        NSApp.activate(ignoringOtherApps: true)
        completionHandler(openPanel.runModal() == .OK ? openPanel.urls : nil)
    }

    func webViewDidClose(_ webView: WKWebView) {
        close()
    }

    func windowDidMove(_ notification: Notification) {
        // Our own moves keep Pip's place themselves: a glide sets it at its start (this fires on every frame
        // of the glide), and a move to where we put the panel only differs by macOS's rounding. Anything else
        // (the screen arrangement changed, say) moves Pip with the panel.
        if moving || dragTimer != nil { return }
        if abs(panel.frame.minX - topLeft.x) < 1, abs(panel.frame.maxY - topLeft.y) < 1 { return }
        topLeft = NSPoint(x: panel.frame.minX, y: panel.frame.maxY)
        if let anchor = lastAnchorX {
            iconScreenX = panel.frame.minX + anchor
        }
        if let anchorY = lastAnchorY {
            iconScreenY = panel.frame.maxY - anchorY
        }
        if isMini {
            miniTopLeft = topLeft
        }
        // Not while dragging (the Dock lookup is not free); finishDrag refreshes it.
        if dragTimer == nil, let screen = panel.screen {
            updateArea(screen)
        }
    }
}
