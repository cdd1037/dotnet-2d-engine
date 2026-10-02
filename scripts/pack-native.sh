#!/usr/bin/env bash
# Local-only package build. Never downloads, installs, or publishes packages.
set -euo pipefail
cd "$(dirname "$0")/.."
root="$PWD"
[[ "$(uname -s)" == Linux && "$(uname -m)" == x86_64 ]] || { echo 'This package proof requires Linux x64.' >&2; exit 2; }
if [[ -n "${DOTNET:-}" ]]; then dotnet="$DOTNET"
elif command -v dotnet >/dev/null 2>&1; then dotnet="$(command -v dotnet)"
elif [[ -x ../android-trim-tools/dotnet/dotnet ]]; then dotnet="$root/../android-trim-tools/dotnet/dotnet"
else echo 'Set DOTNET to an installed .NET 10 SDK executable.' >&2; exit 2; fi
cm="$root/.tools/cmake-3.31.6-linux-x86_64/bin/cmake"
[[ -x "$cm" ]] || { echo 'The existing pinned CMake tool is required.' >&2; exit 2; }
version="${PACKAGE_VERSION:-0.1.0-preview.1}"
feed="${1:-${PACKAGE_FEED:-$root/build-packages/feed}}"
mkdir -p "$feed" "$root/build-packages/native-stage" "$root/build-packages/native-obj"
feed="$(cd "$feed" && pwd)"
stage="$root/build-packages/native-stage"
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-$root/managed/.dotnet-home}"
export NUGET_HTTP_CACHE_PATH="${NUGET_HTTP_CACHE_PATH:-$root/../android-trim-tools/nuget-http-cache}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 DOTNET_GENERATE_ASPNET_CERTIFICATE=false

# Keep the existing interactive/test builds and all dependency sources untouched.
"$cm" -S "$root" -B "$root/build-package" -DCMAKE_BUILD_TYPE=Release \
 -DGAL_HEADLESS_ONLY=OFF -DGAL_ENABLE_RMLUI=ON -DGAL_ENABLE_SVG=OFF -DGAL_ENABLE_MIXER=ON -DGAL_ENABLE_PHYSICS=ON \
 -DSDL3_DIR="$root/.deps/sdl-3.4.16-install/lib/cmake/SDL3" \
 -DSDL3_image_DIR="$root/.deps/ui-install/lib/cmake/SDL3_image" \
 -DSDL3_mixer_DIR="$root/.deps/mixer-3.2.4-install/lib/cmake/SDL3_mixer" \
 -Dbox2d_DIR="$root/.deps/box2d-3.1.1-install/lib/cmake/box2d" \
 -DGAL_RMLUI_SOURCE="$root/.deps/RmlUi-ba95ffe8bfb6370efb2cdcca927eaad4710c5413" \
 -DGAL_RMLUI_CORE="$root/.deps/rmlui-build/librmlui.a" \
 -DFREETYPE_INCLUDE_DIR_freetype2="$root/.deps/sysroot/usr/include/freetype2" \
 -DFREETYPE_INCLUDE_DIR_ft2build="$root/.deps/sysroot/usr/include/freetype2" \
 -DFREETYPE_LIBRARY_RELEASE=/usr/lib/x86_64-linux-gnu/libfreetype.so.6 \
 -DCMAKE_BUILD_WITH_INSTALL_RPATH=ON '-DCMAKE_INSTALL_RPATH=$ORIGIN' \
 -DCMAKE_INSTALL_RPATH_USE_LINK_PATH=OFF -DCMAKE_SHARED_LINKER_FLAGS=-Wl,--disable-new-dtags
"$cm" --build "$root/build-package" --target gal --parallel "${BUILD_JOBS:-4}"
python3 packaging/native/package_native.py stage "$root" "$stage" "$version"
project="$root/packaging/native/Dotnet2D.Native.Linux.x64.csproj"
args=(-p:NativePackageStage="$stage" -p:PackageVersion="$version" -p:BaseIntermediateOutputPath="$root/build-packages/native-obj/" -p:MSBuildProjectExtensionsPath="$root/build-packages/native-obj/")
"$dotnet" restore "$project" --configfile "$root/packaging/native/NuGet.Config" "${args[@]}"
"$dotnet" pack "$project" --no-build --no-restore -c Release -o "$feed" "${args[@]}"
python3 packaging/native/package_native.py verify "$feed/Dotnet2D.Native.Linux.x64.$version.nupkg" "$root/build-packages/native-verification.json"
