#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
root="$PWD"
if [[ -n "${DOTNET:-}" ]]; then dotnet="$DOTNET"
elif command -v dotnet >/dev/null 2>&1; then dotnet="$(command -v dotnet)"
else dotnet="$root/../android-trim-tools/dotnet/dotnet"; fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_GENERATE_ASPNET_CERTIFICATE=false DOTNET_SKIP_FIRST_TIME_EXPERIENCE=true
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-$root/build-packages/dotnet-home}"
export NUGET_HTTP_CACHE_PATH="${NUGET_HTTP_CACHE_PATH:-$root/build-packages/http-cache}"
if [[ -z "${NUGET_PACKAGES:-}" && -d "$root/../android-trim-tools/nuget" ]]; then export NUGET_PACKAGES="$root/../android-trim-tools/nuget"; fi
feed="${PACKAGE_FEED:-$root/build-packages/feed}"
mkdir -p "$feed"
"$dotnet" restore engine/Dotnet2D.Engine.csproj --source "$feed"
"$dotnet" pack engine/Dotnet2D.Engine.csproj -c Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false -o "$feed"
