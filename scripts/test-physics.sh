#!/usr/bin/env bash
# Real solver checks without an SDL/GPU dependency. Setup/build-box2d is explicit.
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/box2d-env.sh
cm="${CMAKE:-$PWD/.tools/cmake-3.31.6-linux-x86_64/bin/cmake}"
"$cm" -S . -B build-physics -DCMAKE_BUILD_TYPE=Release -DGAL_HEADLESS_ONLY=ON -DGAL_ENABLE_PHYSICS=ON \
 -Dbox2d_DIR="$BOX2D_PREFIX/lib/cmake/box2d"
"$cm" --build build-physics --parallel "${BUILD_JOBS:-4}"
"$cm" --build build-physics --target test
export LD_LIBRARY_PATH="$PWD/build-physics${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
if [[ -n "${PHYSICS_APP:-}" ]]; then "$PHYSICS_APP" --physics-self-test
else
 dotnet="${DOTNET:-../android-trim-tools/dotnet/dotnet}"
 export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-$PWD/managed/.dotnet-home}" DOTNET_CLI_TELEMETRY_OPTOUT=1
 "$dotnet" build managed/GameAuthoringLab.csproj -c Release --no-restore --nologo
 "$dotnet" managed/bin/Release/net10.0/GameAuthoringLab.dll --physics-self-test
fi
