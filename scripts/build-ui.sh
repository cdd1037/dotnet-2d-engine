#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
cm="$PWD/.tools/cmake-3.31.6-linux-x86_64/bin/cmake"
root="$PWD"
"$cm" -S . -B build-ui -DCMAKE_BUILD_TYPE=Release -DGAL_ENABLE_RMLUI=ON \
 -DSDL3_DIR="$root/.deps/sdl-install/lib/cmake/SDL3" \
 -DSDL3_image_DIR="$root/.deps/ui-install/lib/cmake/SDL3_image" \
 -DFREETYPE_INCLUDE_DIR_freetype2="$root/.deps/sysroot/usr/include/freetype2" \
 -DFREETYPE_INCLUDE_DIR_ft2build="$root/.deps/sysroot/usr/include/freetype2" \
 -DFREETYPE_LIBRARY_RELEASE=/usr/lib/x86_64-linux-gnu/libfreetype.so.6
"$cm" --build build-ui --parallel 4
"$cm" --build build --parallel 4
bash scripts/build-headless.sh
