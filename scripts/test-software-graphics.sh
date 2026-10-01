#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/sdl-env.sh
export SDL_VIDEODRIVER=offscreen SDL_AUDIODRIVER=dummy
export VK_ICD_FILENAMES="$PWD/.deps/graphics-sysroot/usr/share/vulkan/icd.d/lvp_icd.json"
export LD_LIBRARY_PATH="$PWD/build:$SDL3_PREFIX/lib:$PWD/.deps/graphics-sysroot/usr/lib/x86_64-linux-gnu${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
export MESA_SHADER_CACHE_DIR="$PWD/.tools/mesa-cache"
python3 scripts/validate-software-graphics.py
