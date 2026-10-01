#!/usr/bin/env bash
# Source from project root. CPU lavapipe, no displayed window or real input.
source scripts/sdl-env.sh
export SDL_VIDEODRIVER=offscreen SDL_AUDIODRIVER=dummy
export VK_ICD_FILENAMES=$PWD/.deps/graphics-sysroot/usr/share/vulkan/icd.d/lvp_icd.json
export LD_LIBRARY_PATH=${GAL_NATIVE_DIR:-$PWD/build-ui}:$SDL3_PREFIX/lib:$PWD/.deps/graphics-sysroot/usr/lib/x86_64-linux-gnu
export MESA_SHADER_CACHE_DIR=$PWD/.tools/mesa-cache
