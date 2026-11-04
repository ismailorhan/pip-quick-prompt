#!/bin/bash
# Builds whisper.cpp (pinned) as static arm64 libraries with Metal (its shaders embedded), for the speech helper.
# Output: native/macos/build/whisper/{include,lib}. Re-running reuses the build.
set -euo pipefail
cd "$(dirname "$0")/.."
version="v1.9.5"
src="build/whisper.cpp"
out="build/whisper"
if [[ -f "$out/lib/libwhisper.a" && "$(cat "$out/VERSION" 2>/dev/null)" == "$version" ]]; then
  echo "whisper.cpp $version already built"
  exit 0
fi
rm -rf "$src" "$out"
git clone --quiet --depth 1 --branch "$version" https://github.com/ggml-org/whisper.cpp "$src"
cmake -S "$src" -B "$src/build" -DCMAKE_BUILD_TYPE=Release -DBUILD_SHARED_LIBS=OFF \
  -DCMAKE_OSX_ARCHITECTURES=arm64 -DCMAKE_OSX_DEPLOYMENT_TARGET=12.0 \
  -DGGML_METAL=ON -DGGML_METAL_EMBED_LIBRARY=ON -DGGML_BLAS=OFF -DGGML_NATIVE=OFF \
  -DWHISPER_BUILD_EXAMPLES=OFF -DWHISPER_BUILD_TESTS=OFF -DWHISPER_BUILD_SERVER=OFF >/dev/null
cmake --build "$src/build" --config Release -j "$(sysctl -n hw.ncpu)" >/dev/null
mkdir -p "$out/lib" "$out/include"
find "$src/build" -name "*.a" -exec cp {} "$out/lib/" \;
cp "$src/include/whisper.h" "$src"/ggml/include/*.h "$out/include/"
# Lets Swift import it as a module: import Whisper.
cat > "$out/include/module.modulemap" <<'MAP'
module Whisper {
  header "whisper.h"
  export *
}
MAP
echo "$version" > "$out/VERSION"
ls "$out/lib"
