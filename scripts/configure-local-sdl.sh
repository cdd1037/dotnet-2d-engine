#!/usr/bin/env bash
# Build already-materialized pinned SDL sources; this script never downloads packages.
set -euo pipefail
cd "$(dirname "$0")/.."
cmake_bin="${CMAKE_BIN:-$PWD/.tools/cmake-3.31.6-linux-x86_64/bin/cmake}"
prefix="$PWD/.deps/sysroot"
export PKG_CONFIG_SYSROOT_DIR="$prefix"
export PKG_CONFIG_PATH="$prefix/usr/lib/x86_64-linux-gnu/pkgconfig:$prefix/usr/share/pkgconfig"
"$cmake_bin" -S .deps/SDL3-3.2.28 -B .deps/sdl-build \
 -DCMAKE_BUILD_TYPE=Release -DCMAKE_INSTALL_PREFIX="$PWD/.deps/sdl-install" \
 -DCMAKE_PREFIX_PATH="$prefix/usr" -DCMAKE_INCLUDE_PATH="$prefix/usr/include" \
 -DCMAKE_LIBRARY_PATH="$prefix/usr/lib/x86_64-linux-gnu" -DCMAKE_C_FLAGS="-I$prefix/usr/include" \
 -DSDL_INSTALL=ON -DSDL_TEST_LIBRARY=OFF -DSDL_TESTS=OFF -DSDL_EXAMPLES=OFF \
 -DSDL_SHARED=ON -DSDL_STATIC=OFF
"$cmake_bin" --build .deps/sdl-build --parallel "${BUILD_JOBS:-4}"
"$cmake_bin" --install .deps/sdl-build
