#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
: "${SDL3_PREFIX:?Set SDL3_PREFIX to your SDL 3.2.28 installation}"
cmake -S . -B build -DCMAKE_BUILD_TYPE=Release -DCMAKE_PREFIX_PATH="$SDL3_PREFIX"
cmake --build build --parallel "${BUILD_JOBS:-2}"
ctest --test-dir build --output-on-failure
