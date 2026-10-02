#!/usr/bin/env bash
# Run the RELAY development experiment with ordinary PackageReferences.
set -euo pipefail
launch_dir="$PWD"
cd "$(dirname "$0")/.."
root="$PWD"
dotnet="${DOTNET:-$(command -v dotnet || true)}"
[[ -x "$dotnet" ]] || { echo 'Set DOTNET to an existing .NET 10 SDK.' >&2; exit 2; }
feed="${PACKAGE_FEED:-$root/build-packages/relay-feed}"
[[ -d "$feed" ]] || { echo 'Set PACKAGE_FEED to the prepared local Engine/native packages.' >&2; exit 2; }
if [[ " $* " != *' --rules '* ]]; then
 : "${GAL_UI_FONT:?Set GAL_UI_FONT to a readable licensed Noto Sans CJK SC font.}"
 [[ -r "$GAL_UI_FONT" ]] || { echo 'GAL_UI_FONT is not readable.' >&2; exit 2; }
 export GAL_UI_FONT="$(realpath "$GAL_UI_FONT")"
fi
project="$root/packaging/consumers/relay/Sample.csproj"
# Optional already-installed SDK tool packs support an offline local restore.
sources=(--source "$feed")
cache="${PACKAGE_SOURCE_CACHE:-$root/.tools/nuget}"
[[ ! -d "$cache" ]] || sources+=(--source "$cache")
"$dotnet" restore "$project" "${sources[@]}" --nologo
"$dotnet" build "$project" -c Release --no-restore --nologo -m:1 -nr:false -p:UseSharedCompilation=false
cd "$launch_dir"
"$dotnet" "$root/packaging/consumers/relay/bin/Release/net10.0/Relay.dll" "$@"
