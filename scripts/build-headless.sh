#!/usr/bin/env bash
# Incremental CPU contracts; no downloads or optional native dependencies.
set -euo pipefail
cd "$(dirname "$0")/.."
if [[ -n "${CMAKE:-}" ]]; then cmake="$(command -v "$CMAKE")"
elif command -v cmake >/dev/null 2>&1; then cmake="$(command -v cmake)"
else cmake="$PWD/.tools/cmake-3.31.6-linux-x86_64/bin/cmake"; fi
[[ -x "$cmake" ]] || { echo 'Install CMake >=3.20 or set CMAKE to its executable' >&2; exit 2; }
ctest="${CTEST:-$(dirname "$cmake")/ctest}"
cc="$(command -v "${CC:-gcc}")"
cxx="$(command -v "${CXX:-g++}")"
cache=build-headless/CMakeCache.txt
# CMake's automatic compiler-change reset drops command-line options, including
# GAL_HEADLESS_ONLY. Retire only its cache first, never objects or binaries.
if [[ -f "$cache" ]] && { [[ "$(sed -n 's/^CMAKE_C_COMPILER:[^=]*=//p' "$cache")" != "$cc" ]] ||
                         [[ "$(sed -n 's/^CMAKE_CXX_COMPILER:[^=]*=//p' "$cache")" != "$cxx" ]]; }; then
 mv "$cache" "$cache.previous"
fi
"$cmake" -S . -B build-headless -G 'Unix Makefiles' \
 -DCMAKE_C_COMPILER:FILEPATH="$cc" -DCMAKE_CXX_COMPILER:FILEPATH="$cxx" \
 -DCMAKE_BUILD_TYPE="${CMAKE_BUILD_TYPE:-Release}" -DCMAKE_BUILD_RPATH_USE_ORIGIN=ON \
 -DCMAKE_C_FLAGS="${CFLAGS:-}" -DCMAKE_CXX_FLAGS="${CXXFLAGS:-}" \
 -DCMAKE_EXE_LINKER_FLAGS="${LDFLAGS:-}" -DCMAKE_SHARED_LINKER_FLAGS="${LDFLAGS:-}" \
 -DCMAKE_C_STANDARD=11 -DCMAKE_C_STANDARD_REQUIRED=ON -DCMAKE_C_EXTENSIONS=OFF \
 -DCMAKE_CXX_STANDARD=17 -DCMAKE_CXX_STANDARD_REQUIRED=ON -DCMAKE_CXX_EXTENSIONS=OFF \
 -DGAL_HEADLESS_ONLY=ON -DGAL_ENABLE_RMLUI=OFF -DGAL_ENABLE_MIXER=OFF -DGAL_ENABLE_PHYSICS=OFF
"$cmake" --build build-headless --parallel "${BUILD_JOBS:-2}"
"$ctest" --test-dir build-headless --output-on-failure
