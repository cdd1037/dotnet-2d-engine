#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/ui-env.sh
mkdir -p evidence/ui
for mode in jit aot; do
 if [[ "$mode" == jit ]]; then app=("${DOTNET:-../android-trim-tools/dotnet/dotnet}" managed/bin/Release/net10.0/GameAuthoringLab.dll); else app=(build-aot/GameAuthoringLab); fi
 "${app[@]}" --validate-ui assets/ui/settings.rml | tee "evidence/ui/$mode-validate.log"
 "${app[@]}" --self-test | tee "evidence/ui/$mode-selftest.log"
 GAL_UI_CAPTURE="$PWD/evidence/ui/$mode-settings.bmp" "${app[@]}" --ui-scenario | tee "evidence/ui/$mode-scenario.log"
done
python3 scripts/validate-ui-pixels.py | tee evidence/ui/pixels.log
