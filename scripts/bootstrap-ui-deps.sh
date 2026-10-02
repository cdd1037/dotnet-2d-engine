#!/usr/bin/env bash
# Explicit, opt-in official dependency download/build; not run by ordinary builds.
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/sdl-env.sh
cm="$PWD/.tools/cmake-3.31.6-linux-x86_64/bin/cmake"
bash scripts/bootstrap-image-deps.sh
mkdir -p .deps/ui-downloads evidence/ui
fetch(){ local url="$1" output="$2" hash="$3"; if [[ ! -f "$output" ]];then curl -fL --max-time 180 "$url" -o "$output";fi;echo "$hash  $output"|sha256sum -c -; }
fetch https://github.com/mikke89/RmlUi/archive/ba95ffe8bfb6370efb2cdcca927eaad4710c5413.tar.gz .deps/ui-downloads/rmlui-6.3.tar.gz 1541ef5577115e9368f8ed389b29f0925ef6572f326a33d378ea16c3cfa2cde8
fetch 'https://deb.debian.org/debian/pool/main/f/freetype/libfreetype-dev_2.13.3%2bdfsg-1%2bdeb13u1_amd64.deb' .deps/ui-downloads/libfreetype-dev.deb f58107859d9fa44206e64cf35bdbd4febdec66a33e456ce2e5e3712f01b6c895
[[ -d .deps/RmlUi-ba95ffe8bfb6370efb2cdcca927eaad4710c5413 ]]||tar -xf .deps/ui-downloads/rmlui-6.3.tar.gz -C .deps
dpkg-deb -x .deps/ui-downloads/libfreetype-dev.deb .deps/sysroot
"$cm" -S .deps/RmlUi-ba95ffe8bfb6370efb2cdcca927eaad4710c5413 -B .deps/rmlui-build -DCMAKE_BUILD_TYPE=Release -DBUILD_SHARED_LIBS=OFF -DCMAKE_POSITION_INDEPENDENT_CODE=ON -DFREETYPE_INCLUDE_DIR_freetype2="$PWD/.deps/sysroot/usr/include/freetype2" -DFREETYPE_INCLUDE_DIR_ft2build="$PWD/.deps/sysroot/usr/include/freetype2" -DFREETYPE_LIBRARY_RELEASE=/usr/lib/x86_64-linux-gnu/libfreetype.so.6 -DRMLUI_SAMPLES=OFF -DRMLUI_PRECOMPILED_HEADERS=ON
"$cm" --build .deps/rmlui-build --target rmlui_core --parallel 4
