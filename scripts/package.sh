#!/usr/bin/env bash
# Builds the plugin in Release and packs dist/QuickPrompt.lplug4.
# On macOS it first builds the native popup (native/macos/build.sh) so the package contains it.
#   scripts/package.sh            build + pack
#   scripts/package.sh --install  build + pack + open the package in Logi Options+ (macOS)
set -euo pipefail
cd "$(dirname "$0")/.."

if [[ "$(uname)" == "Darwin" ]]; then
  echo "== macOS popup =="
  bash native/macos/build.sh
fi

# QP_VERSION (set by CI, e.g. 0.3.42) stamps the assemblies and the package manifest,
# so every build shows its own version in Options+.
version_args=()
if [[ -n "${QP_VERSION:-}" ]]; then
  version_args=("-p:Version=$QP_VERSION")
fi

echo "== Plugin =="
dotnet tool restore >/dev/null
dotnet build src/QuickPrompt.Plugin -c Release -p:SkipPluginLink=true -nologo -v q "${version_args[@]}"
if [[ -n "${QP_VERSION:-}" ]]; then
  perl -pi -e "s/^version: .*/version: $QP_VERSION/" bin/plugin/Release/metadata/LoupedeckPackage.yaml
  echo "Version: $QP_VERSION"
fi

echo "== Package =="
mkdir -p dist
rm -f dist/QuickPrompt.lplug4
dotnet logiplugintool pack bin/plugin/Release dist/QuickPrompt.lplug4
dotnet logiplugintool verify dist/QuickPrompt.lplug4
echo "Package: dist/QuickPrompt.lplug4"

if [[ "${1:-}" == "--install" && "$(uname)" == "Darwin" ]]; then
  open dist/QuickPrompt.lplug4
fi
