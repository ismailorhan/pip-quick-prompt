// Quick Prompt.app: the menu bar app.
//
// It lives in the menu bar (no Dock icon) and keeps running in the background. Closing a popup or the
// floating icon never quits it; only "Quit Quick Prompt" in the menu bar menu does.
//
// The engine (providers, sessions, history, the local page server) is the bundled .NET program
// QuickPrompt.Agent, started as a hidden child process. We pass it a random token, read "READY <port>"
// from its stdout and keep its stdin open (it exits when we go away). It asks us, through a long-poll on
// /api/events, to open popups (hosted here with PopupController) and to press ⌘C / ⌘V in the front app.
//
// Build: ../build-app.sh

import AppKit
import Carbon.HIToolbox
import ServiceManagement

let appName = "Quick Prompt"

final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private var statusItem: NSStatusItem!
    private var popups: [PopupController] = []

    // Engine
    private var agent: Process?
    private var agentInput: Pipe?
    private var agentOutputBuffer = ""
    private var port: Int?
    private let token = UUID().uuidString + UUID().uuidString
    private var lastEventId: Int64 = 0
    private var quitting = false
    private var agentStarts: [Date] = []
    private var pendingActions: [(String, [String: String])] = []

    private var hotKeyRef: EventHotKeyRef?
    private var askedForAccessibility = false
    private var loginItem: NSMenuItem?
    private let actionsMenu = NSMenu()
    private var actions: [[String: Any]] = []

    private let updater = Updater()
    private var updateItem: NSMenuItem?

    func applicationDidFinishLaunching(_ notification: Notification) {
        setUpStatusItem()
        registerHotKey()
        startAgent()
        updater.onChange = { [weak self] in self?.refreshUpdateItem() }
        updater.startAutomaticChecks()
    }

    func applicationWillTerminate(_ notification: Notification) {
        quitting = true
        stopAgent()
    }

    // MARK: menu bar

    private func setUpStatusItem() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        if let button = statusItem.button {
            let image = NSImage(systemSymbolName: "sparkles", accessibilityDescription: appName)
            image?.isTemplate = true
            button.image = image
            button.toolTip = appName
        }

        let menu = NSMenu()
        menu.delegate = self
        let ask = NSMenuItem(title: "Ask About Selection", action: #selector(askAboutSelection), keyEquivalent: " ")
        ask.keyEquivalentModifierMask = [.option, .shift]
        menu.addItem(ask)
        askItem = ask
        menu.addItem(NSMenuItem(title: "New Chat", action: #selector(newChat), keyEquivalent: ""))
        menu.addItem(NSMenuItem(title: "Recent Chats", action: #selector(recentChats), keyEquivalent: ""))
        menu.addItem(NSMenuItem(title: "Show Pip", action: #selector(showPip), keyEquivalent: ""))
        let actionsItem = NSMenuItem(title: "Prompt Templates", action: nil, keyEquivalent: "")
        actionsItem.submenu = actionsMenu
        menu.addItem(actionsItem)
        rebuildActionsMenu()
        menu.addItem(.separator())
        menu.addItem(NSMenuItem(title: "Settings…", action: #selector(openSettings), keyEquivalent: ","))
        let update = NSMenuItem(title: "Check for Updates…", action: #selector(checkForUpdates), keyEquivalent: "")
        menu.addItem(update)
        updateItem = update
        if #available(macOS 13.0, *) {
            let item = NSMenuItem(title: "Open at Login", action: #selector(toggleLoginItem), keyEquivalent: "")
            menu.addItem(item)
            loginItem = item
        }
        menu.addItem(.separator())
        menu.addItem(NSMenuItem(title: "Quit \(appName)", action: #selector(quit), keyEquivalent: "q"))
        for item in menu.items where item.action != nil {
            item.target = self
        }
        statusItem.menu = menu
    }

    /// The actions list: from the engine (refreshed each time the menu opens, so it is current next time).
    private func rebuildActionsMenu() {
        actionsMenu.removeAllItems()
        for (index, action) in actions.enumerated() {
            let title = action["title"] as? String ?? "Action"
            let provider = (action["provider"] as? String) == "codex" ? "ChatGPT" : "Claude"
            let item = NSMenuItem(title: title, action: #selector(runAction(_:)), keyEquivalent: "")
            item.toolTip = provider
            item.tag = index
            item.target = self
            actionsMenu.addItem(item)
        }
        if !actions.isEmpty {
            actionsMenu.addItem(.separator())
        }
        let edit = NSMenuItem(title: "Edit Templates…", action: #selector(editActions), keyEquivalent: "")
        edit.target = self
        actionsMenu.addItem(edit)
    }

    private func refreshActions() {
        guard let request = request("presets", method: "GET") else { return }
        URLSession.shared.dataTask(with: request) { [weak self] data, _, _ in
            guard let data = data, let list = try? JSONSerialization.jsonObject(with: data) as? [[String: Any]] else { return }
            DispatchQueue.main.async {
                self?.actions = list
                self?.rebuildActionsMenu()
            }
        }.resume()
    }

    @objc private func runAction(_ sender: NSMenuItem) {
        guard actions.indices.contains(sender.tag), let id = actions[sender.tag]["id"] as? String else { return }
        // Let the previous app get its focus back first: the action may read its selection.
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.15) { [weak self] in self?.send("run", ["presetId": id]) }
    }

    @objc private func editActions() { send("actions") }

    func menuWillOpen(_ menu: NSMenu) {
        refreshActions()
        if #available(macOS 13.0, *) {
            loginItem?.state = SMAppService.mainApp.status == .enabled ? .on : .off
        }
    }

    @objc private func askAboutSelection() {
        // The menu has just closed; give the previous app a moment to get its focus back before ⌘C.
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.15) { [weak self] in self?.runShortcut() }
    }

    /// The shortcut: tell the engine whether ⌘C can work at all, so it never waits for a copy that cannot happen.
    func runShortcut() {
        let canCopy = AXIsProcessTrusted()
        send("hotkey", ["canCopy": canCopy ? "true" : "false"])
        if !canCopy && !askedForAccessibility {
            askedForAccessibility = true
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.8) { [weak self] in self?.explainAccessibility() }
        }
    }

    @objc private func newChat() { send("new") }

    /// Brings Pip back (and turns "Keep Pip on screen" on again after "Never Show Pip").
    @objc private func showPip() { send("pip", ["show": "true"]) }

    @objc private func recentChats() { send("history") }

    @objc private func openSettings() { send("settings") }

    @objc private func toggleLoginItem() {
        guard #available(macOS 13.0, *) else { return }
        do {
            if SMAppService.mainApp.status == .enabled {
                try SMAppService.mainApp.unregister()
            } else {
                try SMAppService.mainApp.register()
            }
        } catch {
            showAlert("Could not change Open at Login", error.localizedDescription)
        }
    }

    @objc private func checkForUpdates() {
        if updater.available != nil {
            updater.offerInstall()
        } else {
            updater.check(interactive: true)
        }
    }

    /// When a quiet check found a newer version, the menu says so.
    private func refreshUpdateItem() {
        if let release = updater.available {
            updateItem?.title = "Install Update \(release.version)…"
            statusItem.button?.toolTip = "\(appName): version \(release.version) is available"
        } else {
            updateItem?.title = "Check for Updates…"
            statusItem.button?.toolTip = appName
        }
    }

    @objc private func quit() {
        NSApp.terminate(nil)
    }

    // MARK: global shortcut (⌥⇧Space)

    private var hotKeyHandlerInstalled = false
    private var askItem: NSMenuItem?

    /// Registers the global shortcut. spec: Settings' format, e.g. "alt+shift+Space" (modifiers + a key code).
    private func registerHotKey(_ spec: String = "alt+shift+Space") {
        if !hotKeyHandlerInstalled {
            hotKeyHandlerInstalled = true
            var eventType = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
            InstallEventHandler(GetApplicationEventTarget(), { _, _, userData -> OSStatus in
                guard let userData = userData else { return noErr }
                let me = Unmanaged<AppDelegate>.fromOpaque(userData).takeUnretainedValue()
                DispatchQueue.main.async { me.runShortcut() }
                return noErr
            }, 1, &eventType, Unmanaged.passUnretained(self).toOpaque(), nil)
        }
        if let old = hotKeyRef {
            UnregisterEventHotKey(old)
            hotKeyRef = nil
        }
        guard let parsed = AppDelegate.parseHotkey(spec) else {
            NSLog("Quick Prompt: unknown shortcut \(spec)")
            return
        }
        let id = EventHotKeyID(signature: OSType(0x5150_524D), id: 1) // "QPRM"
        let (keyCode, carbonModifiers, menuKey, menuModifiers) = parsed
        let status = RegisterEventHotKey(keyCode, carbonModifiers, id, GetApplicationEventTarget(), 0, &hotKeyRef)
        if status != noErr {
            NSLog("Quick Prompt: could not register the shortcut \(spec) (\(status)); another app may be using it.")
        }
        // Show it in the menu next to Ask About Selection.
        askItem?.keyEquivalent = menuKey
        askItem?.keyEquivalentModifierMask = menuModifiers
    }

    private static let keyCodes: [String: Int] = [
        "KeyA": kVK_ANSI_A, "KeyB": kVK_ANSI_B, "KeyC": kVK_ANSI_C, "KeyD": kVK_ANSI_D, "KeyE": kVK_ANSI_E,
        "KeyF": kVK_ANSI_F, "KeyG": kVK_ANSI_G, "KeyH": kVK_ANSI_H, "KeyI": kVK_ANSI_I, "KeyJ": kVK_ANSI_J,
        "KeyK": kVK_ANSI_K, "KeyL": kVK_ANSI_L, "KeyM": kVK_ANSI_M, "KeyN": kVK_ANSI_N, "KeyO": kVK_ANSI_O,
        "KeyP": kVK_ANSI_P, "KeyQ": kVK_ANSI_Q, "KeyR": kVK_ANSI_R, "KeyS": kVK_ANSI_S, "KeyT": kVK_ANSI_T,
        "KeyU": kVK_ANSI_U, "KeyV": kVK_ANSI_V, "KeyW": kVK_ANSI_W, "KeyX": kVK_ANSI_X, "KeyY": kVK_ANSI_Y,
        "KeyZ": kVK_ANSI_Z,
        "Digit0": kVK_ANSI_0, "Digit1": kVK_ANSI_1, "Digit2": kVK_ANSI_2, "Digit3": kVK_ANSI_3, "Digit4": kVK_ANSI_4,
        "Digit5": kVK_ANSI_5, "Digit6": kVK_ANSI_6, "Digit7": kVK_ANSI_7, "Digit8": kVK_ANSI_8, "Digit9": kVK_ANSI_9,
        "Space": kVK_Space, "Enter": kVK_Return, "Tab": kVK_Tab, "Backquote": kVK_ANSI_Grave,
        "Minus": kVK_ANSI_Minus, "Equal": kVK_ANSI_Equal, "BracketLeft": kVK_ANSI_LeftBracket,
        "BracketRight": kVK_ANSI_RightBracket, "Backslash": kVK_ANSI_Backslash, "Semicolon": kVK_ANSI_Semicolon,
        "Quote": kVK_ANSI_Quote, "Comma": kVK_ANSI_Comma, "Period": kVK_ANSI_Period, "Slash": kVK_ANSI_Slash,
        "ArrowUp": kVK_UpArrow, "ArrowDown": kVK_DownArrow, "ArrowLeft": kVK_LeftArrow, "ArrowRight": kVK_RightArrow,
        "F1": kVK_F1, "F2": kVK_F2, "F3": kVK_F3, "F4": kVK_F4, "F5": kVK_F5, "F6": kVK_F6, "F7": kVK_F7,
        "F8": kVK_F8, "F9": kVK_F9, "F10": kVK_F10, "F11": kVK_F11, "F12": kVK_F12, "F13": kVK_F13,
        "F14": kVK_F14, "F15": kVK_F15, "F16": kVK_F16, "F17": kVK_F17, "F18": kVK_F18, "F19": kVK_F19,
    ]

    /// "alt+shift+Space" → (key code, Carbon modifiers, menu key equivalent, menu modifiers).
    static func parseHotkey(_ spec: String) -> (UInt32, UInt32, String, NSEvent.ModifierFlags)? {
        var parts = spec.split(separator: "+").map(String.init)
        guard let code = parts.popLast(), let keyCode = keyCodes[code] else { return nil }
        var carbon = 0
        var flags: NSEvent.ModifierFlags = []
        for part in parts {
            switch part {
            case "cmd": carbon |= cmdKey; flags.insert(.command)
            case "ctrl": carbon |= controlKey; flags.insert(.control)
            case "alt": carbon |= optionKey; flags.insert(.option)
            case "shift": carbon |= shiftKey; flags.insert(.shift)
            default: return nil
            }
        }
        guard carbon & (cmdKey | controlKey | optionKey) != 0 else { return nil }
        var menuKey = ""
        if code.hasPrefix("Key") { menuKey = String(code.dropFirst(3)).lowercased() }
        else if code.hasPrefix("Digit") { menuKey = String(code.dropFirst(5)) }
        else if code == "Space" { menuKey = " " }
        return (UInt32(keyCode), UInt32(carbon), menuKey, menuKey.isEmpty ? [] : flags)
    }

    /// Asks the engine for the shortcut in Settings (at start).
    private func loadHotkey() {
        guard let request = request("status", method: "GET") else { return }
        URLSession.shared.dataTask(with: request) { [weak self] data, _, _ in
            guard let data = data,
                  let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
                  let spec = json["hotkey"] as? String else { return }
            DispatchQueue.main.async { self?.registerHotKey(spec) }
        }.resume()
    }

    // MARK: engine process

    private func agentExecutable() -> URL? {
        guard let resources = Bundle.main.resourceURL else { return nil }
        #if arch(arm64)
        let preferred = ["arm64", "x64"]
        #else
        let preferred = ["x64", "arm64"]
        #endif
        for arch in preferred {
            let candidate = resources.appendingPathComponent("agent/\(arch)/QuickPrompt.Agent")
            if FileManager.default.isExecutableFile(atPath: candidate.path) {
                return candidate
            }
        }
        return nil
    }

    private func startAgent() {
        guard !quitting else { return }
        guard let executable = agentExecutable() else {
            showAlert("Quick Prompt is incomplete", "The engine (QuickPrompt.Agent) is missing from the app. Please download Quick Prompt again.")
            return
        }

        // Do not spin if the engine keeps crashing.
        agentStarts = agentStarts.filter { $0.timeIntervalSinceNow > -60 } + [Date()]
        if agentStarts.count > 5 {
            showAlert("Quick Prompt engine keeps stopping", "Quit Quick Prompt and open it again. If it happens again, please report it.")
            return
        }

        let process = Process()
        process.executableURL = executable
        var environment = ProcessInfo.processInfo.environment
        environment["QP_CONTROL_TOKEN"] = token
        environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
        process.environment = environment

        let input = Pipe()
        let output = Pipe()
        process.standardInput = input
        process.standardOutput = output
        process.standardError = FileHandle.standardError

        agentOutputBuffer = ""
        output.fileHandleForReading.readabilityHandler = { [weak self] handle in
            let data = handle.availableData
            guard !data.isEmpty, let text = String(data: data, encoding: .utf8) else {
                handle.readabilityHandler = nil
                return
            }
            DispatchQueue.main.async { self?.agentPrinted(text) }
        }
        process.terminationHandler = { [weak self] finished in
            DispatchQueue.main.async { self?.agentStopped(finished) }
        }

        do {
            try process.run()
            agent = process
            agentInput = input
        } catch {
            showAlert("Could not start the Quick Prompt engine", error.localizedDescription)
        }
    }

    private func agentPrinted(_ text: String) {
        agentOutputBuffer += text
        while let newline = agentOutputBuffer.firstIndex(of: "\n") {
            let line = agentOutputBuffer[..<newline].trimmingCharacters(in: .whitespacesAndNewlines)
            agentOutputBuffer.removeSubrange(...newline)
            if line.hasPrefix("READY "), let value = Int(line.dropFirst(6)) {
                port = value
                lastEventId = 0
                pollEvents(port: value)
                refreshActions()
                send("pip")   // shows Pip when "Keep Pip on screen" is on
                loadHotkey()
                let queued = pendingActions
                pendingActions.removeAll()
                for (action, body) in queued {
                    send(action, body)
                }
            }
        }
    }

    private func agentStopped(_ process: Process) {
        guard process === agent else { return }
        agent = nil
        agentInput = nil
        port = nil
        if !quitting {
            NSLog("Quick Prompt: engine exited (\(process.terminationStatus)); restarting.")
            DispatchQueue.main.asyncAfter(deadline: .now() + 1) { [weak self] in self?.startAgent() }
        }
    }

    private func stopAgent() {
        // Closing stdin tells the engine to exit; terminate in case it does not.
        try? agentInput?.fileHandleForWriting.close()
        if let process = agent, process.isRunning {
            process.terminate()
        }
        agent = nil
        agentInput = nil
    }

    // MARK: talking to the engine

    private func request(_ path: String, method: String, body: [String: String]? = nil, timeout: TimeInterval = 10) -> URLRequest? {
        guard let port = port, let url = URL(string: "http://127.0.0.1:\(port)/api/\(path)") else { return nil }
        var request = URLRequest(url: url, cachePolicy: .reloadIgnoringLocalCacheData, timeoutInterval: timeout)
        request.httpMethod = method
        request.setValue(token, forHTTPHeaderField: "X-QuickPrompt-Control")
        request.setValue("1", forHTTPHeaderField: "X-QuickPrompt")
        if let body = body {
            request.setValue("application/json", forHTTPHeaderField: "Content-Type")
            request.httpBody = try? JSONSerialization.data(withJSONObject: body)
        }
        return request
    }

    private func send(_ action: String, _ body: [String: String] = [:]) {
        guard let request = request(action, method: "POST", body: body) else {
            // The engine is still starting: run it as soon as it is ready.
            pendingActions.append((action, body))
            return
        }
        URLSession.shared.dataTask(with: request) { _, _, error in
            if let error = error {
                NSLog("Quick Prompt: /api/\(action) failed: \(error.localizedDescription)")
            }
        }.resume()
    }

    private func pollEvents(port expectedPort: Int) {
        guard port == expectedPort, !quitting,
              let request = request("events?since=\(lastEventId)", method: "GET", timeout: 40) else { return }
        URLSession.shared.dataTask(with: request) { [weak self] data, _, error in
            DispatchQueue.main.async {
                guard let self = self else { return }
                var delay: TimeInterval = 0
                if error != nil {
                    delay = 1
                } else if let data = data,
                          let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any] {
                    if let last = (json["last"] as? NSNumber)?.int64Value {
                        self.lastEventId = last
                    }
                    for event in json["events"] as? [[String: Any]] ?? [] {
                        self.handle(event)
                    }
                } else {
                    delay = 1
                }
                DispatchQueue.main.asyncAfter(deadline: .now() + delay) { self.pollEvents(port: expectedPort) }
            }
        }.resume()
    }

    private func handle(_ event: [String: Any]) {
        switch event["type"] as? String {
        case "open":
            if let text = event["url"] as? String, let url = URL(string: text) {
                openPopup(url)
            }
        case "keys":
            pressKeys(event["action"] as? String ?? "")
        case "hotkey":
            // Changed in Settings: use it right away.
            if let spec = event["value"] as? String {
                registerHotKey(spec)
            }
        default:
            break
        }
    }

    // MARK: popups

    private func openPopup(_ url: URL) {
        // One popup (and so one Pip) at a time: the new one takes the place of the old, right where Pip is;
        // with no Pip on screen, where Pip was last left, else at the bottom middle of the screen.
        let anchor = popups.last?.iconPoint ?? PopupController.savedPipPoint()
        for popup in popups {
            popup.close()
        }
        let popup = PopupController(url: url)
        popup.onClosed = { [weak self] closed in
            self?.popups.removeAll { $0 === closed }
        }
        popups.append(popup)
        popup.open(at: anchor, bottomCenter: true)
    }

    // MARK: ⌘C / ⌘V in the front app

    private func pressKeys(_ action: String) {
        let keyCode: CGKeyCode
        switch action {
        case "copy": keyCode = CGKeyCode(kVK_ANSI_C)
        case "paste": keyCode = CGKeyCode(kVK_ANSI_V)
        default: return
        }

        // Sending keystrokes needs the Accessibility permission. macOS's own request shows at most once per
        // start; after that a missing permission is only logged (no dialog on every ⌘C / ⌘V).
        guard AXIsProcessTrusted() else {
            if !askedForAccessibility {
                askedForAccessibility = true
                DispatchQueue.main.async { [weak self] in self?.explainAccessibility() }
            } else {
                NSLog("Quick Prompt: no Accessibility permission, so ⌘C / ⌘V cannot be sent.")
            }
            return
        }

        let source = CGEventSource(stateID: .combinedSessionState)
        // Explicit flags: ⌥⇧ may still be held from the shortcut, but the app must see a plain ⌘C / ⌘V.
        let down = CGEvent(keyboardEventSource: source, virtualKey: keyCode, keyDown: true)
        let up = CGEvent(keyboardEventSource: source, virtualKey: keyCode, keyDown: false)
        down?.flags = .maskCommand
        up?.flags = .maskCommand
        down?.post(tap: .cghidEventTap)
        up?.post(tap: .cghidEventTap)
    }

    /// Our own explanation instead of macOS's prompt: a switch that looks on in System Settings may belong
    /// to an older copy (another signature), so offer to clear that entry before turning it on again.
    private func explainAccessibility() {
        NSApp.activate(ignoringOtherApps: true)
        let alert = NSAlert()
        alert.messageText = "Quick Prompt needs Accessibility permission"
        alert.informativeText = """
        It presses ⌘C and ⌘V for you, to read the selection and paste answers.

        If Quick Prompt already looks switched on in Privacy & Security → Accessibility, that entry belongs to an \
        older copy. Reset clears it; then switch Quick Prompt on in the list that opens, and start Quick Prompt again.
        """
        alert.addButton(withTitle: "Reset and Open Settings")
        alert.addButton(withTitle: "Open Settings")
        alert.addButton(withTitle: "Not Now")
        let answer = alert.runModal()
        if answer == .alertThirdButtonReturn {
            return
        }
        if answer == .alertFirstButtonReturn, let bundleID = Bundle.main.bundleIdentifier {
            let reset = Process()
            reset.executableURL = URL(fileURLWithPath: "/usr/bin/tccutil")
            reset.arguments = ["reset", "Accessibility", bundleID]
            try? reset.run()
            reset.waitUntilExit()
        }
        if answer == .alertFirstButtonReturn {
            // After the reset this puts Quick Prompt back in the list (switched off), to switch on.
            let options = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary
            _ = AXIsProcessTrustedWithOptions(options)
        }
        if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility") {
            NSWorkspace.shared.open(url)
        }
    }

    private func showAlert(_ title: String, _ message: String) {
        let alert = NSAlert()
        alert.messageText = title
        alert.informativeText = message
        alert.alertStyle = .warning
        NSApp.activate(ignoringOtherApps: true)
        alert.runModal()
    }
}

let app = NSApplication.shared
app.setActivationPolicy(.accessory)
let delegate = AppDelegate()
app.delegate = delegate
app.run()
