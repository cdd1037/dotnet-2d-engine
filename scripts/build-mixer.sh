#!/usr/bin/env bash
# Explicit official-source dependency setup; no system installation or codec downloads.
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/sdl-env.sh
source scripts/mixer-env.sh
archive=".deps/downloads/SDL3_mixer-$SDL3_MIXER_VERSION.tar.gz"
mkdir -p .deps/downloads
if [[ ! -f "$archive" ]]; then
 curl -fL --retry 2 --connect-timeout 20 "https://github.com/libsdl-org/SDL_mixer/releases/download/release-$SDL3_MIXER_VERSION/SDL3_mixer-$SDL3_MIXER_VERSION.tar.gz" -o "$archive.download"
 echo "$SDL3_MIXER_ARCHIVE_SHA256  $archive.download" | sha256sum --check
 mv "$archive.download" "$archive"
fi
echo "$SDL3_MIXER_ARCHIVE_SHA256  $archive" | sha256sum --check
if [[ ! -d ".deps/SDL3_mixer-$SDL3_MIXER_VERSION" ]]; then
 tar --extract --gzip --file "$archive" --directory .deps --no-same-owner
fi
cm="${CMAKE:-$PWD/.tools/cmake-3.31.6-linux-x86_64/bin/cmake}"
"$cm" -S ".deps/SDL3_mixer-$SDL3_MIXER_VERSION" -B ".deps/mixer-$SDL3_MIXER_VERSION-build" \
 -DCMAKE_BUILD_TYPE=Release -DCMAKE_INSTALL_PREFIX="$SDL3_MIXER_PREFIX" \
 -DSDL3_DIR="$SDL3_PREFIX/lib/cmake/SDL3" -DBUILD_SHARED_LIBS=OFF -DCMAKE_POSITION_INDEPENDENT_CODE=ON \
 -DSDLMIXER_TESTS=OFF -DSDLMIXER_EXAMPLES=OFF -DSDLMIXER_INSTALL=ON -DSDLMIXER_INSTALL_CPACK=OFF \
 -DSDLMIXER_VENDORED=OFF -DSDLMIXER_STRICT=ON -DSDLMIXER_DEPS_SHARED=OFF \
 -DSDLMIXER_WAVE=ON -DSDLMIXER_VORBIS_STB=ON -DSDLMIXER_VORBIS_VORBISFILE=OFF -DSDLMIXER_VORBIS_TREMOR=OFF \
 -DSDLMIXER_AIFF=OFF -DSDLMIXER_VOC=OFF -DSDLMIXER_AU=OFF -DSDLMIXER_FLAC=OFF \
 -DSDLMIXER_GME=OFF -DSDLMIXER_MOD=OFF -DSDLMIXER_MP3=OFF -DSDLMIXER_MIDI=OFF \
 -DSDLMIXER_OPUS=OFF -DSDLMIXER_WAVPACK=OFF
"$cm" --build ".deps/mixer-$SDL3_MIXER_VERSION-build" --parallel "${BUILD_JOBS:-4}"
"$cm" --install ".deps/mixer-$SDL3_MIXER_VERSION-build"
