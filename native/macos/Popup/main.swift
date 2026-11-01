// QuickPromptPopup: the standalone popup used by the Logi plugin (one process per popup).
// The plugin starts it with the page URL (http://127.0.0.1:<port>/s/<token>/).
// The Quick Prompt app does not use this; it hosts PopupController itself.
//
// Build: ../build.sh

import AppKit

let newPopupNotification = Notification.Name("app.quickprompt.popup.opened")

final class HelperDelegate: NSObject, NSApplicationDelegate {
    private let popup: PopupController

    init(url: URL) {
        popup = PopupController(url: url)
        super.init()
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        // One popup at a time: tell older ones to close (pinned ones stay).
        let me = String(ProcessInfo.processInfo.processIdentifier)
        DistributedNotificationCenter.default().addObserver(forName: newPopupNotification, object: nil, queue: .main) { [weak self] note in
            if let sender = note.object as? String, sender != me, self?.popup.pinned == false {
                self?.popup.close()
            }
        }
        DistributedNotificationCenter.default().postNotificationName(newPopupNotification, object: me, userInfo: nil, deliverImmediately: true)

        popup.onClosed = { _ in NSApp.terminate(nil) }
        popup.open()
    }
}

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
let delegate = HelperDelegate(url: url)
app.delegate = delegate
app.run()
