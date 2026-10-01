#!/usr/bin/env bash
# Explicit official tagged source setup. Library only; no samples/task-system dependencies.
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/box2d-env.sh
archive=".deps/downloads/box2d-v$BOX2D_VERSION.tar.gz"
mkdir -p .deps/downloads
if [[ ! -f "$archive" ]]; then
 curl -fL --retry 2 --connect-timeout 20 "https://codeload.github.com/erincatto/box2d/tar.gz/refs/tags/v$BOX2D_VERSION" -o "$archive.download"
 echo "$BOX2D_ARCHIVE_SHA256  $archive.download" | sha256sum --check
 mv "$archive.download" "$archive"
fi
echo "$BOX2D_ARCHIVE_SHA256  $archive" | sha256sum --check
if [[ ! -d ".deps/box2d-$BOX2D_VERSION" ]]; then tar -xzf "$archive" -C .deps --no-same-owner; fi
cm="${CMAKE:-$PWD/.tools/cmake-3.31.6-linux-x86_64/bin/cmake}"
"$cm" -S ".deps/box2d-$BOX2D_VERSION" -B ".deps/box2d-$BOX2D_VERSION-build" \
 -DCMAKE_BUILD_TYPE=Release -DCMAKE_POSITION_INDEPENDENT_CODE=ON -DBUILD_SHARED_LIBS=OFF \
 -DCMAKE_INSTALL_PREFIX="$BOX2D_PREFIX" -DBOX2D_SAMPLES=OFF -DBOX2D_UNIT_TESTS=OFF \
 -DBOX2D_BENCHMARKS=OFF -DBOX2D_DOCS=OFF -DBOX2D_PROFILE=OFF -DBOX2D_VALIDATE=ON -DBOX2D_AVX2=OFF
"$cm" --build ".deps/box2d-$BOX2D_VERSION-build" --parallel "${BUILD_JOBS:-4}"
"$cm" --install ".deps/box2d-$BOX2D_VERSION-build"
