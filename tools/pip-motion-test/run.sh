#!/bin/bash
# Builds the real PopupController (its private members opened up for the test) with main.swift, starts the
# mock page server and runs the motion test. It moves a floating Pip around your screens for about a minute.
set -euo pipefail
cd "$(dirname "$0")"
build="$(mktemp -d)"
trap 'kill "${server:-}" 2>/dev/null || true; rm -rf "$build"' EXIT
sed -e 's/^    private func /    func /' -e 's/^    private var /    var /' -e 's/^    private(set) var /    var /' \
  ../../native/macos/Shared/PopupController.swift > "$build/PopupController.swift"
swiftc -swift-version 5 -o "$build/pip-motion" main.swift "$build/PopupController.swift" -framework AppKit -framework WebKit
port=9977
python3 -I server.py "$port" & server=$!
sleep 1
PORT="$port" "$build/pip-motion"
