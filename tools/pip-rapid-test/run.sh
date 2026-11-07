#!/bin/bash
# Quick open / close of the conversation, 24 times at random 0.12-0.45 s intervals, with the real PopupController and a
# mock engine. Prints how far the Pip on screen (the page's, or the picture covering it) ever was from where it belongs.
# Pass: "off by more than 3 pt: 0". Run it a few times (the timings are random).
set -euo pipefail
cd "$(dirname "$0")"
build="$(mktemp -d)"
trap 'kill "${server:-}" 2>/dev/null || true; rm -rf "$build"' EXIT
sed -e 's/^    private func /    func /' -e 's/^    private var /    var /' -e 's/^    private(set) var /    var /' \
  ../../native/macos/Shared/PopupController.swift > "$build/PopupController.swift"
swiftc -swift-version 5 -o "$build/rapid" main.swift "$build/PopupController.swift" -framework AppKit -framework WebKit 2>&1 | grep error: || true
python3 -I server.py 9981 & server=$!
sleep 1
PORT=9981 "$build/rapid"
