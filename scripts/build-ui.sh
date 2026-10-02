#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/sdl-env.sh
source scripts/mixer-env.sh
source scripts/box2d-env.sh
svg_args=(-DGAL_ENABLE_SVG="${GAL_WITH_SVG:-OFF}")
if [[ "${GAL_WITH_SVG:-OFF}" == ON ]]; then svg_args+=(-Dlunasvg_DIR="$PWD/.deps/svg-install/lib/cmake/lunasvg" -Dplutovg_DIR="$PWD/.deps/svg-install/lib/cmake/plutovg"); fi
physics_args=(-DGAL_ENABLE_PHYSICS="${GAL_WITH_PHYSICS:-OFF}")
if [[ "${GAL_WITH_PHYSICS:-OFF}" == ON ]]; then physics_args+=(-Dbox2d_DIR="$BOX2D_PREFIX/lib/cmake/box2d"); fi
mixer_args=(-DGAL_ENABLE_MIXER="${GAL_WITH_MIXER:-OFF}")
if [[ "${GAL_WITH_MIXER:-OFF}" == ON ]]; then mixer_args+=(-DSDL3_mixer_DIR="$SDL3_MIXER_PREFIX/lib/cmake/SDL3_mixer"); fi
cm="$PWD/.tools/cmake-3.31.6-linux-x86_64/bin/cmake"
root="$PWD"
"$cm" -S . -B build-ui -DCMAKE_BUILD_TYPE=Release -DGAL_ENABLE_RMLUI=ON "${svg_args[@]}" "${mixer_args[@]}" "${physics_args[@]}" \
 -DSDL3_DIR="$SDL3_PREFIX/lib/cmake/SDL3" \
 -DSDL3_image_DIR="${SDL3_IMAGE_PREFIX:-$root/.deps/ui-install}/lib/cmake/SDL3_image" \
 -DFREETYPE_INCLUDE_DIR_freetype2="$root/.deps/sysroot/usr/include/freetype2" \
 -DFREETYPE_INCLUDE_DIR_ft2build="$root/.deps/sysroot/usr/include/freetype2" \
 -DFREETYPE_LIBRARY_RELEASE=/usr/lib/x86_64-linux-gnu/libfreetype.so.6
"$cm" --build build-ui --parallel 4
"$cm" -S . -B build -DCMAKE_BUILD_TYPE=Release "${mixer_args[@]}" "${physics_args[@]}" -DSDL3_DIR="$SDL3_PREFIX/lib/cmake/SDL3" -DSDL3_image_DIR="${SDL3_IMAGE_PREFIX:-$PWD/.deps/ui-install}/lib/cmake/SDL3_image"
"$cm" --build build --parallel 4
bash scripts/build-headless.sh
