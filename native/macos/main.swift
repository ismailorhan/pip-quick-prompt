// QuickPromptPopup: a small floating panel at the mouse cursor that shows the Quick Prompt page.
//
// The plugin starts it with the page URL (http://127.0.0.1:<port>/s/<token>/).
// The panel is transparent and non-activating: the app you were working in stays frontmost,
// so "Paste" pastes straight back into it. The page reports its height and the rectangles
// of its ".glass" shapes; behind each one we place a native Liquid Glass view
// (NSGlassEffectView, macOS 26+) or a vibrancy view on older systems.
//
// Build: ./build.sh  (needs Xcode Command Line Tools: xcode-select --install)

import AppKit
import QuartzCore
import WebKit

let defaultPopupWidth: CGFloat = 520
// Looked up at runtime so the code also builds with older SDKs.
let glassViewClass: NSView.Type? = NSClassFromString("NSGlassEffectView") as? NSView.Type
let newPopupNotification = Notification.Name("app.quickprompt.popup.opened")

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

final class AppDelegate: NSObject, NSApplicationDelegate, NSWindowDelegate, WKNavigationDelegate, WKUIDelegate, WKScriptMessageHandler {
    private let url: URL
    private var panel: PopupPanel!
    private var webView: WKWebView!
    private var container: NSView!
    private var glassViews: [NSView] = []
    private var topLeft = NSPoint.zero
    private var visibleArea = NSRect.zero
    private var shown = false
    private var closing = false
    private var popupWidth: CGFloat = defaultPopupWidth
    /// Pinned popups survive new popups (and collapse to an icon instead of closing; the page handles that).
    private var pinned = false
    private var dragTimer: Timer?
    private var dragStart = NSPoint.zero
    private var dragOrigin = NSPoint.zero
    private var dragSamples: [(time: TimeInterval, point: NSPoint)] = []
    /// The page shows either the full popup or the floating icon ("mini"); the icon keeps its own place.
    private var isMini = false
    private var miniTopLeft: NSPoint?

    init(url: URL) {
        self.url = url
        super.init()
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        // One popup at a time: tell older ones to close.
        let me = String(ProcessInfo.processInfo.processIdentifier)
        DistributedNotificationCenter.default().addObserver(forName: newPopupNotification, object: nil, queue: .main) { [weak self] note in
            if let sender = note.object as? String, sender != me, self?.pinned == false {
                self?.close()
            }
        }
        DistributedNotificationCenter.default().postNotificationName(newPopupNotification, object: me, userInfo: nil, deliverImmediately: true)

        // Place the panel just below and to the right of the cursor, inside the visible screen area.
        let mouse = NSEvent.mouseLocation
        let screen = NSScreen.screens.first(where: { NSMouseInRect(mouse, $0.frame, false) }) ?? NSScreen.main ?? NSScreen.screens[0]
        visibleArea = AppDelegate.usableFrame(of: screen)
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
        panel.hasShadow = true
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
            source: "window.__quickPromptNative = { glass: '\(glassKind)' };",
            injectionTime: .atDocumentStart,
            forMainFrameOnly: true))
        webView = WKWebView(frame: container.bounds, configuration: config)
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

    private func close() {
        guard !closing else { return }
        closing = true
        panel?.orderOut(nil)
        NSApp.terminate(nil)
    }

    /// Resizes the panel keeping its top-left corner (the page reports its own size), inside the screen.
    private func setSize(width requestedWidth: CGFloat?, height requested: CGFloat) {
        if let w = requestedWidth {
            popupWidth = min(max(w.rounded(.up), 56), visibleArea.width - 16)
        }
        let height = min(max(requested.rounded(.up), 40), visibleArea.height * 0.85)
        // The floating icon may sit right in a corner; the full popup keeps a small gap.
        let edge: CGFloat = isMini ? 0 : 8
        var top = min(topLeft.y, visibleArea.maxY - edge)
        if top - height < visibleArea.minY + edge {
            top = min(visibleArea.minY + edge + height, visibleArea.maxY - edge)
        }
        let x = min(max(topLeft.x, visibleArea.minX + edge), visibleArea.maxX - popupWidth - edge)
        topLeft = NSPoint(x: x, y: top)
        panel.setFrame(NSRect(x: x, y: top - height, width: popupWidth, height: height), display: true, animate: false)
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

    /// The screen area the popup may use: the visible frame, minus where an auto-hidden Dock appears
    /// when it slides in (macOS counts that area as free, so the icon would sit under the Dock).
    static func usableFrame(of screen: NSScreen) -> NSRect {
        var area = screen.visibleFrame
        let dock = UserDefaults(suiteName: "com.apple.dock")
        guard dock?.bool(forKey: "autohide") == true else { return area }
        let tile = CGFloat(dock?.double(forKey: "tilesize") ?? 0)
        let reserve = (tile > 0 ? tile : 48) + 22
        switch dock?.string(forKey: "orientation") ?? "bottom" {
        case "left":
            area.origin.x += reserve
            area.size.width -= reserve
        case "right":
            area.size.width -= reserve
        default:
            area.origin.y += reserve
            area.size.height -= reserve
        }
        return area
    }

    private func finishDrag(_ samples: [(time: TimeInterval, point: NSPoint)]) {
        let screen = AppDelegate.usableFrame(of: panel.screen ?? NSScreen.main ?? NSScreen.screens[0])
        visibleArea = screen
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

        // Always end fully on screen (and off the Dock).
        target.origin.x = min(max(target.origin.x, screen.minX), screen.maxX - frame.width)
        target.origin.y = min(max(target.origin.y, screen.minY), screen.maxY - frame.height)

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
            effect.maskImage = AppDelegate.roundedMask(radius: radius)
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
                setSize(width: (body["width"] as? Double).map { CGFloat($0) }, height: CGFloat(value))
            }
            if let rects = body["rects"] as? [[String: Any]] {
                layoutGlass(rects)
            }
            show()
        case "close":
            close()
        case "drag":
            startDrag()
        case "mode":
            let mini = (body["value"] as? String) == "mini"
            if mini && !isMini {
                // Back to where the icon was left (not where the popup happened to be).
                if let saved = miniTopLeft { topLeft = saved }
            } else if !mini && isMini {
                // The popup opens from the icon's place; remember it for the way back.
                miniTopLeft = topLeft
            }
            isMini = mini
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
        let html = "<body style='font:13px -apple-system;margin:16px;color:#c00;background:transparent'>Could not connect to the Quick Prompt plugin: \(error.localizedDescription)<br>Press Esc to close.</body>"
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

    func webViewDidClose(_ webView: WKWebView) {
        close()
    }

    func windowDidMove(_ notification: Notification) {
        topLeft = NSPoint(x: panel.frame.minX, y: panel.frame.maxY)
        if isMini {
            miniTopLeft = topLeft
        }
        if let screen = panel.screen {
            visibleArea = AppDelegate.usableFrame(of: screen)
        }
    }
}

// MARK: entry point

let arguments = CommandLine.arguments
guard arguments.count > 1,
      let url = URL(string: arguments[1]),
      url.scheme == "http",
      url.host == "127.0.0.1" || url.host == "localhost" else {
    FileHandle.standardError.write("Usage: QuickPromptPopup http://127.0.0.1:<port>/s/<token>/\n".data(using: .utf8)!)
    exit(2)
}

let app = NSApplication.shared
app.setActivationPolicy(.accessory)
let delegate = AppDelegate(url: url)
app.delegate = delegate
app.run()
