#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
: "${SDL3_PREFIX:?Set SDL3_PREFIX to your SDL 3.4.16 installation}"
source scripts/mixer-env.sh
mixer_args=(-DGAL_ENABLE_MIXER="${GAL_WITH_MIXER:-OFF}")
if [[ "${GAL_WITH_MIXER:-OFF}" == ON ]]; then mixer_args+=(-DSDL3_mixer_DIR="$SDL3_MIXER_PREFIX/lib/cmake/SDL3_mixer"); fi
cmake -S . -B build -DCMAKE_BUILD_TYPE=Release "${mixer_args[@]}" -DSDL3_DIR="$SDL3_PREFIX/lib/cmake/SDL3"
cmake --build build --parallel "${BUILD_JOBS:-2}"
ctest --test-dir build --output-on-failure
