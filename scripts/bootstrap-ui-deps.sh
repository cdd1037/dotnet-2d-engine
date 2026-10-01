#!/usr/bin/env bash
# Explicit, opt-in official dependency download/build; not run by ordinary builds.
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/sdl-env.sh
cm="$PWD/.tools/cmake-3.31.6-linux-x86_64/bin/cmake"
mkdir -p .deps/ui-downloads evidence/ui
fetch(){ local url="$1" output="$2" hash="$3"; if [[ ! -f "$output" ]];then curl -fL --max-time 180 "$url" -o "$output";fi;echo "$hash  $output"|sha256sum -c -; }
fetch https://github.com/mikke89/RmlUi/archive/ba95ffe8bfb6370efb2cdcca927eaad4710c5413.tar.gz .deps/ui-downloads/rmlui-6.3.tar.gz 1541ef5577115e9368f8ed389b29f0925ef6572f326a33d378ea16c3cfa2cde8
fetch https://github.com/libsdl-org/SDL_image/releases/download/release-3.2.4/SDL3_image-3.2.4.tar.gz .deps/ui-downloads/SDL3_image-3.2.4.tar.gz a725bd6d04261fdda0dd8d950659e1dc15a8065d025275ef460d32ae7dcfc182
fetch 'https://deb.debian.org/debian/pool/main/f/freetype/libfreetype-dev_2.13.3%2bdfsg-1%2bdeb13u1_amd64.deb' .deps/ui-downloads/libfreetype-dev.deb f58107859d9fa44206e64cf35bdbd4febdec66a33e456ce2e5e3712f01b6c895
[[ -d .deps/RmlUi-ba95ffe8bfb6370efb2cdcca927eaad4710c5413 ]]||tar -xf .deps/ui-downloads/rmlui-6.3.tar.gz -C .deps
[[ -d .deps/SDL3_image-3.2.4 ]]||tar -xf .deps/ui-downloads/SDL3_image-3.2.4.tar.gz -C .deps
dpkg-deb -x .deps/ui-downloads/libfreetype-dev.deb .deps/sysroot
"$cm" -S .deps/SDL3_image-3.2.4 -B .deps/sdlimage-build -DCMAKE_BUILD_TYPE=Release -DBUILD_SHARED_LIBS=OFF -DSDL3_DIR="$SDL3_PREFIX/lib/cmake/SDL3" -DSDLIMAGE_AVIF=OFF -DSDLIMAGE_TIF=OFF -DSDLIMAGE_WEBP=OFF -DSDLIMAGE_JXL=OFF -DSDLIMAGE_SAMPLES=OFF -DSDLIMAGE_TESTS=OFF -DSDLIMAGE_INSTALL=ON -DSDLIMAGE_BACKEND_STB=ON -DCMAKE_INSTALL_PREFIX="$PWD/.deps/ui-install"
"$cm" --build .deps/sdlimage-build --parallel 4
"$cm" --install .deps/sdlimage-build
"$cm" -S .deps/RmlUi-ba95ffe8bfb6370efb2cdcca927eaad4710c5413 -B .deps/rmlui-build -DCMAKE_BUILD_TYPE=Release -DBUILD_SHARED_LIBS=OFF -DCMAKE_POSITION_INDEPENDENT_CODE=ON -DFREETYPE_INCLUDE_DIR_freetype2="$PWD/.deps/sysroot/usr/include/freetype2" -DFREETYPE_INCLUDE_DIR_ft2build="$PWD/.deps/sysroot/usr/include/freetype2" -DFREETYPE_LIBRARY_RELEASE=/usr/lib/x86_64-linux-gnu/libfreetype.so.6 -DRMLUI_SAMPLES=OFF -DRMLUI_PRECOMPILED_HEADERS=ON
"$cm" --build .deps/rmlui-build --target rmlui_core --parallel 4
