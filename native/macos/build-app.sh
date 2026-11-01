#!/usr/bin/env bash
# Builds "Quick Prompt.app", the menu bar app (universal: Apple Silicon + Intel), into native/macos/build/
# and zips it to dist/QuickPrompt-mac.zip. The .NET engine (QuickPrompt.Agent) is published self-contained
# for both architectures into Contents/Resources/agent/{arm64,x64}; the app picks the right one at start.
# Requires Xcode Command Line Tools and the .NET SDK. QP_VERSION sets the version (default 0.3.0).
set -euo pipefail
cd "$(dirname "$0")"
root=../..
version="${QP_VERSION:-0.3.0}"

app="build/Quick Prompt.app"
rm -rf "$app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources/agent"

echo "Compiling the app"
outputs=()
for arch in arm64 x86_64; do
  swiftc -O -swift-version 5 \
    -target "$arch-apple-macos12.0" \
    -o "build/QuickPromptApp-$arch" App/main.swift Shared/PopupController.swift \
    -framework AppKit -framework WebKit -framework Carbon -framework ServiceManagement
  outputs+=("build/QuickPromptApp-$arch")
done
lipo -create -output "$app/Contents/MacOS/Quick Prompt" "${outputs[@]}"
rm -f "${outputs[@]}"

sed "s/__VERSION__/$version/g" App/Info.plist > "$app/Contents/Info.plist"

echo "Making the icon"
iconset=build/AppIcon.iconset
rm -rf "$iconset" && mkdir -p "$iconset"
source_png="$root/src/QuickPrompt.Plugin/package/metadata/Icon256x256.png"
for size in 16 32 128 256; do
  sips -z $size $size "$source_png" --out "$iconset/icon_${size}x${size}.png" >/dev/null
  double=$((size * 2))
  sips -z $double $double "$source_png" --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$iconset" -o "$app/Contents/Resources/AppIcon.icns"

echo "Publishing the engine"
for rid in osx-arm64 osx-x64; do
  dotnet publish "$root/src/QuickPrompt.Agent/QuickPrompt.Agent.csproj" -c Release -r "$rid" --self-contained true \
    -p:PublishSingleFile=false -p:Version="$version" -o "$app/Contents/Resources/agent/${rid#osx-}" --nologo -v quiet
done

echo "Signing (ad hoc)"
find "$app/Contents/Resources/agent" -type f \( -name "*.dylib" -o -name "QuickPrompt.Agent" -o -name "createdump" \) -exec codesign --force --sign - {} \;
codesign --force --sign - "$app"

mkdir -p "$root/dist"
rm -f "$root/dist/QuickPrompt-mac.zip"
ditto -c -k --keepParent "$app" "$root/dist/QuickPrompt-mac.zip"
echo "Done: native/macos/$app and dist/QuickPrompt-mac.zip ($version)"
