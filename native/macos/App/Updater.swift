// In-app updates from the rolling "Latest build" release on GitHub.
//
// The release carries QuickPrompt-mac.json ({ "version", "url", "sha256" }) next to the zip. To update we
// download the zip, check its SHA-256, unzip it, check that the new app is signed by the same team as this
// one (Developer ID), then quit, swap the app on disk and open the new one (a small shell script waits
// for this process to end). Files downloaded this way get no quarantine flag, so macOS opens the new
// version without asking, and the permissions (Accessibility) stay, since the signing team is the same.

import AppKit
import CryptoKit

final class Updater {
    static let feedURL = URL(string: "https://github.com/ismailorhan/pip-quick-prompt/releases/download/latest/QuickPrompt-mac.json")!

    struct Release {
        let version: String
        let url: URL
        let sha256: String
    }

    /// Set when a check found a newer version (the menu then offers to install it).
    private(set) var available: Release?
    var onChange: (() -> Void)?
    private var busy = false
    private var timer: Timer?

    var currentVersion: String {
        Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "0"
    }

    /// Quiet checks: shortly after start, then once a day.
    func startAutomaticChecks() {
        DispatchQueue.main.asyncAfter(deadline: .now() + 20) { [weak self] in self?.check(interactive: false) }
        let timer = Timer(timeInterval: 24 * 60 * 60, repeats: true) { [weak self] _ in self?.check(interactive: false) }
        RunLoop.main.add(timer, forMode: .common)
        self.timer = timer
    }

    /// interactive: the user chose "Check for Updates…" — always answer, and offer to install.
    func check(interactive: Bool) {
        guard !busy else { return }
        busy = true
        var request = URLRequest(url: Updater.feedURL, cachePolicy: .reloadIgnoringLocalCacheData, timeoutInterval: 20)
        request.setValue("Quick Prompt/\(currentVersion)", forHTTPHeaderField: "User-Agent")
        URLSession.shared.dataTask(with: request) { [weak self] data, response, error in
            DispatchQueue.main.async {
                guard let self = self else { return }
                self.busy = false
                let status = (response as? HTTPURLResponse)?.statusCode ?? 0
                guard error == nil, status == 200, let data = data,
                      let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
                      let version = json["version"] as? String,
                      let urlText = json["url"] as? String, let url = URL(string: urlText), url.scheme == "https",
                      let sha = json["sha256"] as? String else {
                    if interactive {
                        self.alert("Could not check for updates", error?.localizedDescription ?? "The update information could not be read (HTTP \(status)). Try again later.")
                    }
                    return
                }
                if Updater.isNewer(version, than: self.currentVersion) {
                    self.available = Release(version: version, url: url, sha256: sha.lowercased())
                    self.onChange?()
                    if interactive { self.offerInstall() }
                } else {
                    self.available = nil
                    self.onChange?()
                    if interactive {
                        self.alert("Quick Prompt is up to date", "You have the latest version, \(self.currentVersion).", style: .informational)
                    }
                }
            }
        }.resume()
    }

    func offerInstall() {
        guard let release = available else { return }
        NSApp.activate(ignoringOtherApps: true)
        let alert = NSAlert()
        alert.messageText = "Quick Prompt \(release.version) is available"
        alert.informativeText = "You have \(currentVersion). Install it now? Quick Prompt restarts when it is done."
        alert.addButton(withTitle: "Install and Restart")
        alert.addButton(withTitle: "Later")
        if alert.runModal() == .alertFirstButtonReturn {
            install(release)
        }
    }

    // MARK: install

    private func install(_ release: Release) {
        guard !busy else { return }
        busy = true
        URLSession.shared.downloadTask(with: release.url) { [weak self] location, response, error in
            guard let self = self else { return }
            func fail(_ message: String) {
                DispatchQueue.main.async {
                    self.busy = false
                    self.alert("The update could not be installed", message)
                }
            }
            guard let location = location, error == nil, (response as? HTTPURLResponse)?.statusCode == 200 else {
                fail(error?.localizedDescription ?? "The download failed. Try again later.")
                return
            }
            do {
                let work = FileManager.default.temporaryDirectory.appendingPathComponent("QuickPromptUpdate-\(UUID().uuidString)")
                try FileManager.default.createDirectory(at: work, withIntermediateDirectories: true)
                let zip = work.appendingPathComponent("QuickPrompt-mac.zip")
                try FileManager.default.moveItem(at: location, to: zip)

                let digest = SHA256.hash(data: try Data(contentsOf: zip)).map { String(format: "%02x", $0) }.joined()
                guard digest == release.sha256 else {
                    fail("The download is damaged (checksum mismatch). Try again later.")
                    return
                }
                guard Updater.run("/usr/bin/ditto", ["-x", "-k", zip.path, work.path]).status == 0 else {
                    fail("The download could not be unpacked.")
                    return
                }
                let newApp = work.appendingPathComponent("Quick Prompt.app")
                guard FileManager.default.fileExists(atPath: newApp.path) else {
                    fail("The download does not contain Quick Prompt.app.")
                    return
                }
                if let problem = Updater.signatureProblem(newApp: newApp, current: Bundle.main.bundleURL) {
                    fail(problem)
                    return
                }
                DispatchQueue.main.async { self.swapAndRelaunch(newApp: newApp) }
            } catch {
                fail(error.localizedDescription)
            }
        }.resume()
    }

    /// Quits, then (from a detached shell script) replaces the app on disk and opens the new one.
    private func swapAndRelaunch(newApp: URL) {
        let destination = Bundle.main.bundleURL.path
        let script = """
        while kill -0 "$1" 2>/dev/null; do sleep 0.2; done
        if mv "$3" "$3.updating-old"; then
          if mv "$2" "$3"; then rm -rf "$3.updating-old"; else mv "$3.updating-old" "$3"; fi
        fi
        open "$3"
        """
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/bin/sh")
        process.arguments = ["-c", script, "quickprompt-update", String(ProcessInfo.processInfo.processIdentifier), newApp.path, destination]
        do {
            try process.run()
        } catch {
            busy = false
            alert("The update could not be installed", error.localizedDescription)
            return
        }
        NSApp.terminate(nil)
    }

    // MARK: helpers

    /// "0.3.31" vs "0.3.9": compares number by number.
    static func isNewer(_ candidate: String, than current: String) -> Bool {
        let a = candidate.split(separator: ".").map { Int($0) ?? 0 }
        let b = current.split(separator: ".").map { Int($0) ?? 0 }
        for i in 0..<max(a.count, b.count) {
            let x = i < a.count ? a[i] : 0, y = i < b.count ? b[i] : 0
            if x != y { return x > y }
        }
        return false
    }

    /// The new app must have a valid signature, and, when this app is signed by a team, the same team.
    static func signatureProblem(newApp: URL, current: URL) -> String? {
        guard run("/usr/bin/codesign", ["--verify", "--deep", "--strict", newApp.path]).status == 0 else {
            return "The new version's signature is not valid."
        }
        let mine = teamIdentifier(current)
        if let mine = mine, teamIdentifier(newApp) != mine {
            return "The new version is not signed by the same developer."
        }
        return nil
    }

    private static func teamIdentifier(_ app: URL) -> String? {
        let output = run("/usr/bin/codesign", ["-dv", "--verbose=2", app.path]).output
        for line in output.split(separator: "\n") where line.hasPrefix("TeamIdentifier=") {
            let value = String(line.dropFirst("TeamIdentifier=".count))
            return value == "not set" ? nil : value
        }
        return nil
    }

    @discardableResult
    static func run(_ tool: String, _ arguments: [String]) -> (status: Int32, output: String) {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: tool)
        process.arguments = arguments
        let pipe = Pipe()
        process.standardOutput = pipe
        process.standardError = pipe
        do {
            try process.run()
        } catch {
            return (-1, "")
        }
        let data = pipe.fileHandleForReading.readDataToEndOfFile()
        process.waitUntilExit()
        return (process.terminationStatus, String(data: data, encoding: .utf8) ?? "")
    }

    private func alert(_ title: String, _ message: String, style: NSAlert.Style = .warning) {
        NSApp.activate(ignoringOtherApps: true)
        let alert = NSAlert()
        alert.messageText = title
        alert.informativeText = message
        alert.alertStyle = style
        alert.runModal()
    }
}
