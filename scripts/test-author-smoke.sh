#!/usr/bin/env bash
# One existing PackageReference fixture + explicit optional SVG source runtime.
# No AOT republish, matrix, downloads, install or remote package publication.
set -euo pipefail
cd "$(dirname "$0")/.."
root="$PWD"
if [[ -n "${DOTNET:-}" ]]; then dotnet="$DOTNET"
elif command -v dotnet >/dev/null 2>&1; then dotnet="$(command -v dotnet)"
else dotnet="$root/../android-trim-tools/dotnet/dotnet"; fi
feed="${PACKAGE_FEED:-$root/build-packages/feed}"
svg="${AUTHOR_SVG_NATIVE_DIR:-$root/build-svg}"
proof="${AUTHOR_SMOKE_ROOT:-/tmp/dotnet2d-author-smoke}"
proof="$(python3 - "$proof" "$root" <<'PY'
from pathlib import Path
import sys
p, root = (Path(x).resolve() for x in sys.argv[1:])
if p.exists() or p == root or root in p.parents:
    raise SystemExit('AUTHOR_SMOKE_ROOT must be a fresh directory outside the checkout.')
print(p)
PY
)"
[[ -x "$dotnet" && -f "$svg/libgal.so" ]] || { echo 'Prepare existing SDK and optional SVG native build first.' >&2; exit 2; }
dotnet="$(realpath "$dotnet")"
svg="$(realpath "$svg")"
for flag in RMLUI SVG PHYSICS; do grep -q "^GAL_ENABLE_${flag}:BOOL=ON$" "$svg/CMakeCache.txt"; done
grep -q '^GAL_HEADLESS_ONLY:BOOL=OFF$' "$svg/CMakeCache.txt"
mkdir -p "$proof/feed" "$proof/logs" "$proof/prerequisites" "$proof/publish"
cp "$feed/Dotnet2D.Engine.0.1.0-preview.1.nupkg" "$feed/Dotnet2D.Native.Linux.x64.0.1.0-preview.1.nupkg" "$proof/feed/"
cp -a packaging/consumers/features "$proof/consumer"
cp -a .deps/graphics-sysroot "$proof/prerequisites/graphics"
python3 - "$proof" <<'PY'
from pathlib import Path
from xml.sax.saxutils import escape
import json, struct, sys
p=Path(sys.argv[1]); pixels=bytes([0,0,255,255,0,0,0,0])
header=b'BM'+struct.pack('<IHHI',54+len(pixels),0,0,54)+struct.pack('<IiiHHIIiiII',40,2,1,1,24,0,len(pixels),2835,2835,0,0)
(p/'consumer/assets/palette.bmp').write_bytes(header+pixels)
(p/'global.json').write_text(json.dumps({'sdk':{'version':'10.0.401','rollForward':'disable'}}))
(p/'NuGet.Config').write_text('<configuration><packageSources><clear/><add key="local" value="'+escape(str(p/'feed'),{'"':'&quot;'})+'"/></packageSources></configuration>')
PY
export DOTNET_CLI_HOME="$proof/prerequisites/dotnet-home" NUGET_PACKAGES="$proof/nuget-cache" NUGET_HTTP_CACHE_PATH="$proof/http-cache"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 DOTNET_GENERATE_ASPNET_CERTIFICATE=false
unset GAL_ASSET_ROOT LD_LIBRARY_PATH LD_PRELOAD LD_AUDIT
cd "$proof"
"$dotnet" restore consumer/Sample.csproj --configfile NuGet.Config > logs/restore.log 2>&1
"$dotnet" build consumer/Sample.csproj -c Release --no-restore -p:UseSharedCompilation=false > logs/build.log 2>&1
cp -a consumer/bin/Release/net10.0 publish/default-package-fdd
"$dotnet" publish/default-package-fdd/Sample.features.dll > logs/default-headless.log 2>&1
# This overlay is deliberately not represented as the unchanged SVG-off package.
cp -a publish/default-package-fdd publish/optional-svg-source-profile
cp "$svg/libgal.so" publish/optional-svg-source-profile/runtimes/linux-x64/native/libgal.so
cp "$svg/CMakeCache.txt" logs/svg-cmake-cache.txt
mkdir -p publish/optional-svg-source-profile/licenses/OptionalSvg
cp "$root"/docs/{LUNASVG-LICENSE.txt,PLUTOVG-LICENSE.txt,PLUTOVG-EMBEDDED-NOTICES.txt,PLUTOVG-FREETYPE-LICENSE.txt} publish/optional-svg-source-profile/licenses/OptionalSvg/
sha256sum feed/*.nupkg consumer/Program.cs consumer/AuthorSmoke.cs consumer/assets/* publish/*/runtimes/linux-x64/native/libgal.so > logs/inputs.sha256
export SDL_VIDEODRIVER=offscreen SDL_AUDIODRIVER=dummy
export VK_ICD_FILENAMES="$proof/prerequisites/graphics/usr/share/vulkan/icd.d/lvp_icd.json"
export LD_LIBRARY_PATH="$proof/prerequisites/graphics/usr/lib/x86_64-linux-gnu"
export MESA_SHADER_CACHE_DIR="$proof/prerequisites/mesa-cache" GAL_AUTHOR_CAPTURE_DIR="$proof/captures"
"$dotnet" publish/optional-svg-source-profile/Sample.features.dll --author-smoke > logs/author-smoke.log 2>&1
python3 - "$proof" <<'PY'
from pathlib import Path
import hashlib, json, re, sys
p=Path(sys.argv[1]); log=(p/'logs/author-smoke.log').read_text(); normal=(p/'logs/default-headless.log').read_text()
assert re.search(r'PACKAGE FEATURES PASS assertions=56 frames=3 .*query-bytes=0 bodies=0 textures=0',normal)
match=re.search(r'AUTHOR SMOKE PASS assertions=(\d+) phases=closed/open/restored .* raster=verified svg=verified',log)
assert match and re.search(r'PACKAGE FEATURES PASS assertions=\d+ frames=7 .*query-bytes=0 bodies=0 textures=0',log)
captures=sorted((p/'captures').glob('*.bmp')); assert len(captures)==3
for file in ('build.log','restore.log'):
    assert not re.search(r'\bwarning [A-Z]+\d+|\berror [A-Z]+\d+', (p/'logs'/file).read_text())
(p/'results.json').write_text(json.dumps({'mode':'JIT/software Vulkan','native_profile':'explicit SVG-on source overlay; default package remains SVG-off','author_assertions':int(match[1]),'default_headless_assertions':56,'frames_in_graphical_run':7,'aot_publishes':0,'captures':[{'name':f.name,'bytes':f.stat().st_size,'sha256':hashlib.sha256(f.read_bytes()).hexdigest()} for f in captures]},indent=2)+'\n')
PY
cat logs/default-headless.log logs/author-smoke.log
echo "AUTHOR SMOKE PROOF PASS $proof"
