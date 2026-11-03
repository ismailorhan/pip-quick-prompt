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
    private var topLeft = NSPoint.zero
    private var visibleArea = NSRect.zero
    /// Where the Dock is (or slides in when auto-hidden) on the panel's screen; the panel stays off it.
    private var dockArea: NSRect?
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
        panel?.orderOut(nil)
        webView?.configuration.userContentController.removeScriptMessageHandler(forName: "quickPrompt")
        webView?.stopLoading()
        onClosed?(self)
    }

    private func watchHover(_ on: Bool) {
        hoverTimer?.invalidate()
        hoverTimer = nil
        mouseInside = false
        guard on else { return }
        let timer = Timer(timeInterval: 0.08, target: self, selector: #selector(hoverTick(_:)), userInfo: nil, repeats: true)
        RunLoop.main.add(timer, forMode: .common)
        hoverTimer = timer
    }

    @objc private func hoverTick(_ timer: Timer) {
        guard let panel = panel, panel.isVisible, dragTimer == nil else { return }
        let inside = NSMouseInRect(NSEvent.mouseLocation, panel.frame, false)
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
        // The floating icon may sit right in a corner; the full popup keeps a small gap.
        let frame = fit(NSRect(x: topLeft.x, y: topLeft.y - height, width: popupWidth, height: height), edge: isMini ? 0 : 8)
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

    /// The screen area the popup may use: the whole screen below the menu bar. The Dock is handled
    /// separately (dockArea) so the icon can still go right into a corner beside it.
    private func updateArea(_ screen: NSScreen) {
        let (area, dock) = PopupController.placement(on: screen)
        visibleArea = area
        dockArea = dock
    }

    static func placement(on screen: NSScreen) -> (area: NSRect, dock: NSRect?) {
        let full = screen.frame
        let visible = screen.visibleFrame
        let defaults = UserDefaults(suiteName: "com.apple.dock")
        let orientation = defaults?.string(forKey: "orientation") ?? "bottom"
        let autohide = defaults?.bool(forKey: "autohide") == true
        var area = full
        area.size.height = visible.maxY - full.minY   // below the menu bar

        // The Dock's real size and place, read through Accessibility (the app has that permission).
        if let tiles = dockTilesFrame() {
            let pad: CGFloat = 10
            var dock: NSRect
            switch orientation {
            case "left":
                dock = NSRect(x: full.minX, y: tiles.minY - pad, width: tiles.width + 2 * pad, height: tiles.height + 2 * pad)
            case "right":
                dock = NSRect(x: full.maxX - tiles.width - 2 * pad, y: tiles.minY - pad, width: tiles.width + 2 * pad, height: tiles.height + 2 * pad)
            default:
                dock = NSRect(x: tiles.minX - pad, y: full.minY, width: tiles.width + 2 * pad, height: tiles.height + 2 * pad)
            }
            if !autohide {
                // Use what macOS reserves when that is larger (it includes the Dock's margins).
                switch orientation {
                case "left": dock.size.width = max(dock.width, visible.minX - full.minX)
                case "right": dock.origin.x = min(dock.minX, visible.maxX); dock.size.width = full.maxX - dock.minX
                default: dock.size.height = max(dock.height, visible.minY - full.minY)
                }
            }
            if dock.intersects(full) {
                return (area, dock)
            }
            return (area, nil)
        }

        // No Accessibility: keep clear of the whole Dock edge (as macOS does for windows).
        var dockEdge: NSRect?
        if autohide {
            let tile = CGFloat(defaults?.double(forKey: "tilesize") ?? 0)
            let reserve = (tile > 0 ? tile : 48) + 22
            switch orientation {
            case "left": dockEdge = NSRect(x: full.minX, y: full.minY, width: reserve, height: full.height)
            case "right": dockEdge = NSRect(x: full.maxX - reserve, y: full.minY, width: reserve, height: full.height)
            default: dockEdge = NSRect(x: full.minX, y: full.minY, width: full.width, height: reserve)
            }
        } else if visible.minY > full.minY {
            dockEdge = NSRect(x: full.minX, y: full.minY, width: full.width, height: visible.minY - full.minY)
        } else if visible.minX > full.minX {
            dockEdge = NSRect(x: full.minX, y: full.minY, width: visible.minX - full.minX, height: full.height)
        } else if visible.maxX < full.maxX {
            dockEdge = NSRect(x: visible.maxX, y: full.minY, width: full.maxX - visible.maxX, height: full.height)
        }
        return (area, dockEdge)
    }

    /// The frame of the Dock's icon row in Cocoa screen coordinates, or nil without Accessibility.
    static func dockTilesFrame() -> NSRect? {
        guard AXIsProcessTrusted(),
              let dockApp = NSRunningApplication.runningApplications(withBundleIdentifier: "com.apple.dock").first else { return nil }
        let app = AXUIElementCreateApplication(dockApp.processIdentifier)
        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(app, kAXChildrenAttribute as CFString, &value) == .success,
              let children = value as? [AXUIElement] else { return nil }
        for child in children {
            var role: CFTypeRef?
            guard AXUIElementCopyAttributeValue(child, kAXRoleAttribute as CFString, &role) == .success,
                  (role as? String) == (kAXListRole as String) else { continue }
            var positionValue: CFTypeRef?
            var sizeValue: CFTypeRef?
            guard AXUIElementCopyAttributeValue(child, kAXPositionAttribute as CFString, &positionValue) == .success,
                  AXUIElementCopyAttributeValue(child, kAXSizeAttribute as CFString, &sizeValue) == .success,
                  let positionRef = positionValue, let sizeRef = sizeValue,
                  CFGetTypeID(positionRef) == AXValueGetTypeID(), CFGetTypeID(sizeRef) == AXValueGetTypeID() else { continue }
            var position = CGPoint.zero
            var size = CGSize.zero
            AXValueGetValue(positionRef as! AXValue, .cgPoint, &position)
            AXValueGetValue(sizeRef as! AXValue, .cgSize, &size)
            guard size.width > 0, size.height > 0 else { continue }
            // Accessibility uses top-left coordinates from the primary screen.
            let primaryHeight = NSScreen.screens.first?.frame.height ?? 0
            return NSRect(x: position.x, y: primaryHeight - position.y - size.height, width: size.width, height: size.height)
        }
        return nil
    }

    /// Moves a frame inside the usable area and off the Dock.
    private func fit(_ frame: NSRect, edge: CGFloat) -> NSRect {
        let area = visibleArea.insetBy(dx: edge, dy: edge)
        var f = frame
        func clamp() {
            f.origin.x = min(max(f.minX, area.minX), area.maxX - f.width)
            f.origin.y = min(max(f.minY, area.minY), area.maxY - f.height)
        }
        clamp()
        if let dock = dockArea?.insetBy(dx: -edge, dy: -edge), f.intersects(dock) {
            // Step out of the Dock the shortest way that stays on screen.
            var options: [NSRect] = []
            var up = f; up.origin.y = dock.maxY; options.append(up)
            var left = f; left.origin.x = dock.minX - f.width; options.append(left)
            var right = f; right.origin.x = dock.maxX; options.append(right)
            var down = f; down.origin.y = dock.minY - f.height; options.append(down)
            let valid = options.filter { area.contains($0) }
            if let best = valid.min(by: { hypot($0.minX - f.minX, $0.minY - f.minY) < hypot($1.minX - f.minX, $1.minY - f.minY) }) {
                f = best
            } else {
                f.origin.y = dock.maxY
                clamp()
            }
        }
        return f
    }

    private func finishDrag(_ samples: [(time: TimeInterval, point: NSPoint)]) {
        updateArea(panel.screen ?? NSScreen.main ?? NSScreen.screens[0])
        let screen = visibleArea
        let frame = panel.frame
        let margin: CGFloat = 0
        var target = frame

        if let first = samples.first, let last = samples.last, last.time - first.time > 0.01 {
            let dt = CGFloat(last.time - first.time)
            let vx = (last.point.x - first.point.x) / dt
            let vy = (last.point.y - first.point.y) / dt
            let speed = hypot(vx, vy)
            if speed > 1400 {
                // Thrown: go to the corner (or edge) in the direction of the throw.
                if abs(vx) > speed * 0.3 {
                    target.origin.x = vx > 0 ? screen.maxX - frame.width - margin : screen.minX + margin
                }
                if abs(vy) > speed * 0.3 {
                    target.origin.y = vy > 0 ? screen.maxY - frame.height - margin : screen.minY + margin
                }
            }
        }

        // Always end fully on screen and off the Dock.
        target = fit(target, edge: 0)

        if target.origin != frame.origin {
            NSAnimationContext.runAnimationGroup({ context in
                context.duration = 0.28
                context.timingFunction = CAMediaTimingFunction(name: .easeOut)
                self.panel.animator().setFrame(target, display: true)
            }, completionHandler: { [weak self] in
                guard let self = self else { return }
                self.topLeft = NSPoint(x: self.panel.frame.minX, y: self.panel.frame.maxY)
            })
        }
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
        // Dropped on the other half of the screen: the conversation moves to the icon's other side.
        updateSide()
        // Remember where Pip was left, for the next start.
        if let x = iconScreenX, let y = iconScreenY {
            UserDefaults.standard.set([Double(x), Double(y)], forKey: "pipPosition")
        }
    }

    /// Middle click on Pip: glide to the nearest screen edge (left, right, top or bottom), staying off the Dock.
    private func snapToNearestEdge() {
        updateArea(panel.screen ?? NSScreen.main ?? NSScreen.screens[0])
        let frame = panel.frame
        var target = frame
        let distances: [(CGFloat, Int)] = [
            (frame.minX - visibleArea.minX, 0), (visibleArea.maxX - frame.maxX, 1),
            (frame.minY - visibleArea.minY, 2), (visibleArea.maxY - frame.maxY, 3),
        ]
        switch distances.min(by: { $0.0 < $1.0 })?.1 ?? 0 {
        case 0: target.origin.x = visibleArea.minX
        case 1: target.origin.x = visibleArea.maxX - frame.width
        case 2: target.origin.y = visibleArea.minY
        default: target.origin.y = visibleArea.maxY - frame.height
        }
        target = fit(target, edge: 0)
        NSAnimationContext.runAnimationGroup({ context in
            context.duration = 0.28
            context.timingFunction = CAMediaTimingFunction(name: .easeOut)
            self.panel.animator().setFrame(target, display: true)
        }, completionHandler: { [weak self] in
            guard let self = self else { return }
            self.topLeft = NSPoint(x: self.panel.frame.minX, y: self.panel.frame.maxY)
            if let anchor = self.lastAnchorX { self.iconScreenX = self.panel.frame.minX + anchor }
            if let anchorY = self.lastAnchorY { self.iconScreenY = self.panel.frame.maxY - anchorY }
            if self.isMini { self.miniTopLeft = self.topLeft }
            self.updateSide()
            if let point = self.iconPoint {
                UserDefaults.standard.set([Double(point.x), Double(point.y)], forKey: "pipPosition")
            }
        })
    }

    /// Puts the icon on the side away from the nearer screen edge (the page lays itself out mirrored).
    private func updateSide() {
        let x = iconScreenX ?? panel.frame.midX
        let next = x > visibleArea.midX ? "right" : "left"
        guard next != side else { return }
        side = next
        webView?.evaluateJavaScript("window.__quickPromptSide && window.__quickPromptSide('\(next)')", completionHandler: nil)
    }

    // MARK: context menu (right click on the floating icon)

    private func showMenu(_ items: [[String: Any]]) {
        let menu = NSMenu()
        menu.autoenablesItems = false
        for item in items {
            if (item["separator"] as? Bool) == true {
                menu.addItem(.separator())
                continue
            }
            guard let id = item["id"] as? String, let title = item["title"] as? String else { continue }
            let menuItem = NSMenuItem(title: title, action: #selector(menuItemSelected(_:)), keyEquivalent: "")
            menuItem.target = self
            menuItem.representedObject = id
            if (item["checked"] as? Bool) == true {
                menuItem.state = .on
            }
            menu.addItem(menuItem)
        }
        menu.popUp(positioning: nil, at: NSEvent.mouseLocation, in: nil)
    }

    @objc private func menuItemSelected(_ sender: NSMenuItem) {
        guard let id = sender.representedObject as? String, id.allSatisfy({ $0.isLetter || $0.isNumber || $0 == "-" }) else { return }
        webView.evaluateJavaScript("window.__quickPromptMenu && window.__quickPromptMenu('\(id)')", completionHandler: nil)
    }

    /// Places one glass view behind each rectangle reported by the page (CSS pixels, top-left origin).
    private func layoutGlass(_ rects: [[String: Any]]) {
        let height = container.bounds.height
        while glassViews.count < rects.count {
            let view = makeGlassView()
            container.addSubview(view, positioned: .below, relativeTo: webView)
            glassViews.append(view)
        }
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
            view.alphaValue = 1
            view.frame = NSRect(x: x, y: height - y - h, width: w, height: h)
            setCornerRadius(view, min(radius, min(w, h) / 2))
        }
        panel.invalidateShadow()
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
        guard let body = message.body as? [String: Any], let type = body["type"] as? String else { return }
        switch type {
        case "layout":
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
                setSize(width: (body["width"] as? Double).map { CGFloat($0) }, height: CGFloat(value))
                if let anchor = anchor {
                    iconScreenX = panel.frame.minX + anchor
                }
                if let anchorY = anchorY {
                    iconScreenY = panel.frame.maxY - anchorY
                }
            }
            if let rects = body["rects"] as? [[String: Any]] {
                layoutGlass(rects)
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
               NSScreen.screens.contains(where: { NSMouseInRect(NSPoint(x: saved[0], y: saved[1]), $0.frame, false) }) {
                iconScreenX = CGFloat(saved[0])
                iconScreenY = CGFloat(saved[1])
            } else {
                iconScreenX = visibleArea.maxX - 70
                iconScreenY = visibleArea.minY + 150
            }
            updateSide()
        case "reveal":
            // The conversation slides out of (or back into) the icon: move the glass with it.
            let goingIn = (body["dir"] as? String) == "in"
            let dx = CGFloat(body["dx"] as? Double ?? 0)
            let duration = body["duration"] as? Double ?? 0.22
            for view in glassViews where !view.isHidden {
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
            // Pinned: above other apps' floating windows too, on every Space and over full-screen apps.
            panel.level = pinned ? .statusBar : .floating
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

    /// <input type="file"> (Import): the standard Open dialog.
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
