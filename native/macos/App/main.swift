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
    private var loginItem: NSMenuItem?

    func applicationDidFinishLaunching(_ notification: Notification) {
        setUpStatusItem()
        registerHotKey()
        startAgent()
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
        menu.addItem(NSMenuItem(title: "New Chat", action: #selector(newChat), keyEquivalent: ""))
        menu.addItem(NSMenuItem(title: "Recent Chats", action: #selector(recentChats), keyEquivalent: ""))
        menu.addItem(.separator())
        menu.addItem(NSMenuItem(title: "Settings…", action: #selector(openSettings), keyEquivalent: ","))
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

    func menuWillOpen(_ menu: NSMenu) {
        if #available(macOS 13.0, *) {
            loginItem?.state = SMAppService.mainApp.status == .enabled ? .on : .off
        }
    }

    @objc private func askAboutSelection() {
        // The menu has just closed; give the previous app a moment to get its focus back before ⌘C.
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.15) { [weak self] in self?.send("hotkey") }
    }

    @objc private func newChat() { send("new") }

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

    @objc private func quit() {
        NSApp.terminate(nil)
    }

    // MARK: global shortcut (⌥⇧Space)

    private func registerHotKey() {
        var eventType = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        InstallEventHandler(GetApplicationEventTarget(), { _, _, userData -> OSStatus in
            guard let userData = userData else { return noErr }
            let me = Unmanaged<AppDelegate>.fromOpaque(userData).takeUnretainedValue()
            DispatchQueue.main.async { me.send("hotkey") }
            return noErr
        }, 1, &eventType, Unmanaged.passUnretained(self).toOpaque(), nil)

        let id = EventHotKeyID(signature: OSType(0x5150_524D), id: 1) // "QPRM"
        let status = RegisterEventHotKey(UInt32(kVK_Space), UInt32(optionKey | shiftKey), id, GetApplicationEventTarget(), 0, &hotKeyRef)
        if status != noErr {
            NSLog("Quick Prompt: could not register ⌥⇧Space (\(status)); another app may be using it.")
        }
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
        default:
            break
        }
    }

    // MARK: popups

    private func openPopup(_ url: URL) {
        // One popup at a time; pinned ones (and floating icons) stay.
        for popup in popups where !popup.pinned {
            popup.close()
        }
        let popup = PopupController(url: url)
        popup.onClosed = { [weak self] closed in
            self?.popups.removeAll { $0 === closed }
        }
        popups.append(popup)
        popup.open()
    }

    // MARK: ⌘C / ⌘V in the front app

    private func pressKeys(_ action: String) {
        let keyCode: CGKeyCode
        switch action {
        case "copy": keyCode = CGKeyCode(kVK_ANSI_C)
        case "paste": keyCode = CGKeyCode(kVK_ANSI_V)
        default: return
        }

        // Sending keystrokes needs the Accessibility permission; ask once (macOS shows its own dialog).
        let options = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary
        guard AXIsProcessTrustedWithOptions(options) else { return }

        let source = CGEventSource(stateID: .combinedSessionState)
        // Explicit flags: ⌥⇧ may still be held from the shortcut, but the app must see a plain ⌘C / ⌘V.
        let down = CGEvent(keyboardEventSource: source, virtualKey: keyCode, keyDown: true)
        let up = CGEvent(keyboardEventSource: source, virtualKey: keyCode, keyDown: false)
        down?.flags = .maskCommand
        up?.flags = .maskCommand
        down?.post(tap: .cghidEventTap)
        up?.post(tap: .cghidEventTap)
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
