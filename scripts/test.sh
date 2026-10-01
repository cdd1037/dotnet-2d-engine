#!/usr/bin/env bash
# Small default loop. No downloads, publishing, graphics or research by default.
set -euo pipefail
cd "$(dirname "$0")/.."
tier="${1:-quick}"
focus="${2:-core}"
case "$tier" in
 quick|jit) case "$focus" in core|scene|ui|game|resources) ;; *) echo 'Focus must be core, scene, ui, game or resources' >&2; exit 2;; esac;;
 aot|graphics|ui) ;;
 *) echo 'Usage: scripts/test.sh [quick [core|scene|ui|game|resources]|jit|aot|graphics|ui]' >&2; exit 2;;
esac
start=$SECONDS
trap 'code=$?; echo "TEST tier=$tier focus=$focus exit=$code elapsed=$((SECONDS-start))s" >&2' EXIT
resolve_dotnet() {
 if [[ -n "${DOTNET:-}" ]]; then dotnet="$DOTNET"
 elif command -v dotnet >/dev/null 2>&1; then dotnet="$(command -v dotnet)"
 elif [[ -x ../android-trim-tools/dotnet/dotnet ]]; then dotnet=../android-trim-tools/dotnet/dotnet
 else echo 'Set DOTNET to an installed .NET 10 SDK executable' >&2; exit 2; fi
 export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-$PWD/managed/.dotnet-home}"
 export DOTNET_CLI_TELEMETRY_OPTOUT=1
}
case "$tier" in
 quick|jit)
  resolve_dotnet
  bash scripts/build-headless.sh
  # Restore is an explicit setup step, never an implicit dependency download here.
  "$dotnet" build managed/GameAuthoringLab.csproj -c Release --no-restore --nologo
  export LD_LIBRARY_PATH="$PWD/build-headless${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
  app=("$dotnet" managed/bin/Release/net10.0/GameAuthoringLab.dll)
  if [[ "$tier" == jit ]]; then
   "${app[@]}" --self-test
  else
   "${app[@]}" --headless --frames 3
   case "$focus" in
    scene) "${app[@]}" --validate-scene assets/compositions.scene.json;;
    ui) "${app[@]}" --validate-ui assets/ui/settings.rml;;
    game) "${app[@]}" --game-self-test;;
    resources) "${app[@]}" --resource-self-test;;
   esac
  fi
  ;;
 aot)
  : "${AOT_APP:?Set AOT_APP to a freshly published NativeAOT executable; this tier does not publish}"
  [[ -x "$AOT_APP" ]] || { echo 'AOT_APP must be executable' >&2; exit 2; }
  bash scripts/build-headless.sh
  export LD_LIBRARY_PATH="$PWD/build-headless${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
  "$AOT_APP" --self-test
  ;;
 graphics) bash scripts/test-software-graphics.sh;;
 ui) bash scripts/test-ui.sh;;
esac
