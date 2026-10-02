#!/usr/bin/env bash
# Explicit optional setup; ordinary builds neither download nor enable SVG.
set -euo pipefail
cd "$(dirname "$0")/.."
cm="${CMAKE:-$PWD/.tools/cmake-3.31.6-linux-x86_64/bin/cmake}"
archive=.deps/ui-downloads/lunasvg-3.5.0.tar.gz
mkdir -p .deps/ui-downloads
if [[ ! -f "$archive" ]]; then
 curl -fL --max-time 180 https://github.com/sammycage/lunasvg/releases/download/v3.5.0/lunasvg-3.5.0.tar.gz -o "$archive"
fi
echo "1abf1472ee6c4d19797916e8cc3c2e4b628e0d81178ffac60bdb0d457e32c690  $archive" | sha256sum -c -
[[ -d .deps/lunasvg-3.5.0 ]] || tar -xf "$archive" -C .deps
"$cm" -S .deps/lunasvg-3.5.0 -B .deps/lunasvg-build -DCMAKE_BUILD_TYPE=Release \
 -DCMAKE_INSTALL_PREFIX="$PWD/.deps/svg-install" -DCMAKE_POSITION_INDEPENDENT_CODE=ON \
 -DBUILD_SHARED_LIBS=OFF -DLUNASVG_BUILD_EXAMPLES=OFF -DLUNASVG_DISABLE_LOAD_SYSTEM_FONTS=ON
"$cm" --build .deps/lunasvg-build --parallel "${BUILD_JOBS:-4}"
"$cm" --install .deps/lunasvg-build
