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
    -o "build/QuickPromptApp-$arch" App/main.swift App/Updater.swift Shared/PopupController.swift \
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

# Signing: with the Developer ID certificate when the CI secrets are there (then notarized by Apple, so
# macOS opens it without warnings and keeps its permissions across updates), otherwise ad hoc.
identity=""
if [[ -n "${MACOS_CERT_P12:-}" && -n "${MACOS_CERT_PASSWORD:-}" ]]; then
  echo "Importing the Developer ID certificate"
  tmp="${RUNNER_TEMP:-$(mktemp -d)}"
  keychain="$tmp/quickprompt.keychain-db"
  keychain_password="$(uuidgen)"
  security create-keychain -p "$keychain_password" "$keychain"
  security set-keychain-settings -lut 21600 "$keychain"
  security unlock-keychain -p "$keychain_password" "$keychain"
  printf '%s' "$MACOS_CERT_P12" | base64 --decode > "$tmp/cert.p12"
  security import "$tmp/cert.p12" -k "$keychain" -P "$MACOS_CERT_PASSWORD" -T /usr/bin/codesign >/dev/null
  rm -f "$tmp/cert.p12"
  security set-key-partition-list -S apple-tool:,apple:,codesign: -s -k "$keychain_password" "$keychain" >/dev/null
  security list-keychains -d user -s "$keychain" $(security list-keychains -d user | tr -d '"')
  identity="$(security find-identity -v -p codesigning "$keychain" | awk '/Developer ID Application/ { print $2; exit }')"
  if [[ -z "$identity" ]]; then
    echo "No Developer ID Application identity in the certificate; check MACOS_CERT_P12 / MACOS_CERT_PASSWORD" >&2
    exit 1
  fi
fi

sign() {   # sign <path> [entitlements]
  if [[ -n "$identity" ]]; then
    codesign --force --timestamp --options runtime ${2:+--entitlements "$2"} --sign "$identity" "$1"
  else
    codesign --force --sign - "$1"
  fi
}

echo "Signing ${identity:+(Developer ID)}${identity:-(ad hoc)}"
# Every Mach-O file of the engine (libraries first), then the app itself.
while IFS= read -r -d '' file; do
  if file -b "$file" | grep -q "Mach-O"; then
    case "$(basename "$file")" in
      QuickPrompt.Agent|createdump) sign "$file" App/Agent.entitlements ;;
      *) sign "$file" ;;
    esac
  fi
done < <(find "$app/Contents/Resources/agent" -type f -print0)
sign "$app"
codesign --verify --strict --verbose=2 "$app"

mkdir -p "$root/dist"
zip="$root/dist/QuickPrompt-mac.zip"
rm -f "$zip"
ditto -c -k --keepParent "$app" "$zip"

if [[ -n "$identity" && -n "${ASC_KEY_P8:-}" && -n "${ASC_KEY_ID:-}" && -n "${ASC_ISSUER_ID:-}" ]]; then
  echo "Notarizing (Apple checks the app; this takes a few minutes)"
  key="${RUNNER_TEMP:-/tmp}/AuthKey.p8"
  printf '%s\n' "$ASC_KEY_P8" > "$key"
  xcrun notarytool submit "$zip" --key "$key" --key-id "$ASC_KEY_ID" --issuer "$ASC_ISSUER_ID" --wait --timeout 30m \
    --output-format json | tee "${RUNNER_TEMP:-/tmp}/notary.json"
  status="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1])).get("status",""))' "${RUNNER_TEMP:-/tmp}/notary.json")"
  if [[ "$status" != "Accepted" ]]; then
    id="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1])).get("id",""))' "${RUNNER_TEMP:-/tmp}/notary.json")"
    [[ -n "$id" ]] && xcrun notarytool log "$id" --key "$key" --key-id "$ASC_KEY_ID" --issuer "$ASC_ISSUER_ID" || true
    rm -f "$key"
    echo "Notarization failed: $status" >&2
    exit 1
  fi
  # Kept for the disk image below; removed at the end.
  # Staple the ticket so the app opens without a network check, then zip it again.
  xcrun stapler staple "$app"
  rm -f "$zip"
  ditto -c -k --keepParent "$app" "$zip"
  spctl --assess --type execute --verbose=2 "$app" || true
fi

# The disk image for first installs: a small window with Quick Prompt on the left, an arrow, and the
# Applications folder on the right, to drag across (laid out with dmgbuild; plain image if that fails).
echo "Making the disk image"
dmg="$root/dist/QuickPrompt.dmg"
rm -f "$dmg"
if python3 -m venv build/dmgvenv >/dev/null 2>&1 \
  && build/dmgvenv/bin/pip install --quiet "dmgbuild>=1.6,<2" \
  && tiffutil -cathidpicheck dmg-background.png dmg-background@2x.png -out build/dmg-background.tiff >/dev/null \
  && build/dmgvenv/bin/python -m dmgbuild -s dmg-settings.py \
       -D app="$app" -D icon="$app/Contents/Resources/AppIcon.icns" -D background="build/dmg-background.tiff" \
       "Quick Prompt" "$dmg"; then
  echo "Disk image laid out"
else
  echo "dmgbuild did not work; making a plain disk image" >&2
  staging="build/dmg"
  rm -rf "$staging" && mkdir -p "$staging"
  ditto "$app" "$staging/Quick Prompt.app"
  ln -s /Applications "$staging/Applications"
  hdiutil create -volname "Quick Prompt" -srcfolder "$staging" -fs HFS+ -format UDZO -ov "$dmg" >/dev/null
  rm -rf "$staging"
fi
if [[ -n "$identity" ]]; then
  codesign --force --timestamp --sign "$identity" "$dmg"
  if [[ -n "${key:-}" && -f "${key:-}" ]]; then
    echo "Notarizing the disk image"
    xcrun notarytool submit "$dmg" --key "$key" --key-id "$ASC_KEY_ID" --issuer "$ASC_ISSUER_ID" --wait --timeout 30m
    xcrun stapler staple "$dmg"
    spctl --assess --type open --context context:primary-signature --verbose=2 "$dmg" || true
  fi
fi
rm -f "${key:-/nonexistent}"
# What the app's "Check for Updates…" reads (published next to the zip on the Latest build release).
sha="$(shasum -a 256 "$zip" | awk '{ print $1 }')"
printf '{ "version": "%s", "url": "https://github.com/ismailorhan/quick-prompt-for-logi-action-rings/releases/download/latest/QuickPrompt-mac.zip", "sha256": "%s" }\n' "$version" "$sha" > "$root/dist/QuickPrompt-mac.json"
echo "Done: native/macos/$app, dist/QuickPrompt-mac.zip and dist/QuickPrompt-mac.json ($version)"
