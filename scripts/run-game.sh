#!/usr/bin/env bash
# Run the current managed build against the explicitly prepared optional native UI build.
# No downloads, copying old packages or implicit publish/build.
set -euo pipefail
cd "$(dirname "$0")/.."
if [[ -n "${DOTNET:-}" ]]; then dotnet="$DOTNET"
elif command -v dotnet >/dev/null 2>&1; then dotnet="$(command -v dotnet)"
elif [[ -x ../android-trim-tools/dotnet/dotnet ]]; then dotnet=../android-trim-tools/dotnet/dotnet
else echo 'Set DOTNET to an installed .NET 10 SDK executable' >&2; exit 2; fi
export LD_LIBRARY_PATH="$PWD/build-ui:$PWD/.deps/sdl-install/lib${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
export GAL_ASSET_ROOT="$PWD/assets"
export GAL_UI_FONT="${GAL_UI_FONT:-/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc}"
exec "$dotnet" managed/bin/Release/net10.0/GameAuthoringLab.dll --game-demo "$@"
