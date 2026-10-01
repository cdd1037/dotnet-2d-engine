#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
: "${SDL3_PREFIX:?Set SDL3_PREFIX to your SDL 3.4.16 installation}"
cmake -S . -B build -DCMAKE_BUILD_TYPE=Release -DSDL3_DIR="$SDL3_PREFIX/lib/cmake/SDL3"
cmake --build build --parallel "${BUILD_JOBS:-2}"
ctest --test-dir build --output-on-failure
