#!/usr/bin/env bash
# Explicit opt-in download/extraction from the official tagged release; no build/install.
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/sdl-env.sh
archive=".deps/downloads/SDL3-$SDL3_VERSION.tar.gz"
mkdir -p .deps/downloads
if [[ ! -f "$archive" ]]; then
 curl -fL --retry 2 --connect-timeout 20 "https://github.com/libsdl-org/SDL/releases/download/release-$SDL3_VERSION/SDL3-$SDL3_VERSION.tar.gz" -o "$archive.download"
 echo "$SDL3_ARCHIVE_SHA256  $archive.download" | sha256sum --check
 mv "$archive.download" "$archive"
fi
echo "$SDL3_ARCHIVE_SHA256  $archive" | sha256sum --check
if [[ ! -d ".deps/SDL3-$SDL3_VERSION" ]]; then
 tar --extract --gzip --file "$archive" --directory .deps --no-same-owner
fi
printf 'Verified SDL %s sources at .deps/SDL3-%s\n' "$SDL3_VERSION" "$SDL3_VERSION"
