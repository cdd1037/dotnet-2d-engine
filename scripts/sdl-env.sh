#!/usr/bin/env bash
# Source from the repository root. Pins only; never downloads or installs.
export SDL3_VERSION=3.4.16
export SDL3_ARCHIVE_SHA256=7322236cd12090c3eb40b9728be4d49c76f66ad17d04369584d4ecad5cf77c68
export SDL3_PREFIX="${SDL3_PREFIX:-$PWD/.deps/sdl-$SDL3_VERSION-install}"
