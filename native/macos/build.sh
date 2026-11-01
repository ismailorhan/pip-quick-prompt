#!/usr/bin/env bash
# Builds the macOS popup helper (universal binary: Apple Silicon + Intel), ad-hoc signs it and
# copies it into the plugin package (src/QuickPrompt.Plugin/package/mac/QuickPromptPopup).
# Requires Xcode Command Line Tools: xcode-select --install
set -euo pipefail
cd "$(dirname "$0")"

if ! command -v swiftc >/dev/null 2>&1; then
  echo "swiftc not found. Install the Xcode Command Line Tools first: xcode-select --install" >&2
  exit 1
fi

mkdir -p build
outputs=()
for arch in ${ARCHS:-arm64 x86_64}; do
  echo "Compiling for $arch"
  swiftc -O -swift-version 5 \
    -target "$arch-apple-macos12.0" \
    -o "build/QuickPromptPopup-$arch" main.swift \
    -framework AppKit -framework WebKit \
    -Xlinker -sectcreate -Xlinker __TEXT -Xlinker __info_plist -Xlinker Info.plist
  outputs+=("build/QuickPromptPopup-$arch")
done

lipo -create -output build/QuickPromptPopup "${outputs[@]}"
rm -f "${outputs[@]}"
codesign --force --sign - build/QuickPromptPopup

target=../../src/QuickPrompt.Plugin/package/mac
mkdir -p "$target"
cp build/QuickPromptPopup "$target/QuickPromptPopup"
chmod +x "$target/QuickPromptPopup"
echo "Done: native/macos/build/QuickPromptPopup (copied to src/QuickPrompt.Plugin/package/mac/)"
