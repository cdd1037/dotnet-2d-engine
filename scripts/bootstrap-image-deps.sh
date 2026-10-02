#!/usr/bin/env bash
# Explicit, opt-in official SDL_image build. No UI/font dependency or implicit download.
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/sdl-env.sh
cm="${CMAKE:-$PWD/.tools/cmake-3.31.6-linux-x86_64/bin/cmake}"
mkdir -p .deps/ui-downloads
fetch(){ local url="$1" output="$2" hash="$3"; if [[ ! -f "$output" ]];then curl -fL --max-time 180 "$url" -o "$output";fi;echo "$hash  $output"|sha256sum -c -; }
fetch https://github.com/libsdl-org/SDL_image/releases/download/release-3.2.4/SDL3_image-3.2.4.tar.gz .deps/ui-downloads/SDL3_image-3.2.4.tar.gz a725bd6d04261fdda0dd8d950659e1dc15a8065d025275ef460d32ae7dcfc182
[[ -d .deps/SDL3_image-3.2.4 ]]||tar -xf .deps/ui-downloads/SDL3_image-3.2.4.tar.gz -C .deps
"$cm" -S .deps/SDL3_image-3.2.4 -B .deps/sdlimage-build -DCMAKE_BUILD_TYPE=Release -DBUILD_SHARED_LIBS=OFF -DSDL3_DIR="$SDL3_PREFIX/lib/cmake/SDL3" -DSDLIMAGE_AVIF=OFF -DSDLIMAGE_TIF=OFF -DSDLIMAGE_WEBP=OFF -DSDLIMAGE_JXL=OFF -DSDLIMAGE_SAMPLES=OFF -DSDLIMAGE_TESTS=OFF -DSDLIMAGE_INSTALL=ON -DSDLIMAGE_BACKEND_STB=ON -DCMAKE_INSTALL_PREFIX="${SDL3_IMAGE_PREFIX:-$PWD/.deps/ui-install}"
"$cm" --build .deps/sdlimage-build --parallel "${BUILD_JOBS:-2}"
"$cm" --install .deps/sdlimage-build
