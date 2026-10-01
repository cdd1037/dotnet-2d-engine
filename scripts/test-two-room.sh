#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export SDL_VIDEODRIVER=offscreen SDL_AUDIODRIVER=dummy
export VK_ICD_FILENAMES="$PWD/.deps/graphics-sysroot/usr/share/vulkan/icd.d/lvp_icd.json"
export LD_LIBRARY_PATH="$PWD/build:$PWD/.deps/sdl-install/lib:$PWD/.deps/graphics-sysroot/usr/lib/x86_64-linux-gnu${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
export MESA_SHADER_CACHE_DIR="$PWD/.tools/mesa-cache"
mkdir -p evidence/two-room
python3 scripts/validate-texture-api.py | tee evidence/two-room/texture-pixels.log
for mode in jit aot; do
 if [[ "$mode" == jit ]]; then app=("${DOTNET:-../android-trim-tools/dotnet/dotnet}" managed/bin/Release/net10.0/GameAuthoringLab.dll); else app=(build-aot/GameAuthoringLab); fi
 GAL_CAPTURE_BMP="$PWD/evidence/two-room/$mode-start.bmp" "${app[@]}" --scenario --save-file "evidence/two-room/$mode-save.json" | tee "evidence/two-room/$mode-scenario.log"
 "${app[@]}" --validate-save "evidence/two-room/$mode-save.json" | tee "evidence/two-room/$mode-validate.log"
 GAL_CAPTURE_BMP="$PWD/evidence/two-room/$mode-restored.bmp" "${app[@]}" --room-demo --load-file "evidence/two-room/$mode-save.json" --frames 120 | tee "evidence/two-room/$mode-restart.log"
done
python3 - <<'PY'
from pathlib import Path
from PIL import Image,ImageChops
p=Path('evidence/two-room')
for mode in ['jit','aot']:
 for phase in ['start','restored']:
  im=Image.open(p/f'{mode}-{phase}.bmp').convert('RGB');assert im.size==(960,540);im.save(p/f'{mode}-{phase}.png')
# Same explicit scripted state must rasterize identically across JIT/AOT; GUIDs are not visual state.
a=Image.open(p/'jit-restored.png');b=Image.open(p/'aot-restored.png');assert ImageChops.difference(a,b).getbbox() is None
assert a.getpixel((200,200))!=(Image.open(p/'aot-start.png').getpixel((200,200))) # distinct room asset
print('PASS JIT/AOT restored-room pixels identical; distinct room backgrounds; 960x540 actual GPU readbacks')
PY
