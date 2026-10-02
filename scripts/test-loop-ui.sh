#!/usr/bin/env bash
# Optional real pause-menu proof. Reuses existing packages and software Vulkan;
# no native rebuild, package repack, AOT publish or dependency installation.
set -euo pipefail
cd "$(dirname "$0")/.."
root="$PWD"
dotnet="${DOTNET:-$root/../android-trim-tools/dotnet/dotnet}"
[[ -x "$dotnet" ]] || { echo 'Set DOTNET to an existing .NET 10 SDK.' >&2; exit 2; }
dotnet="$(realpath "$dotnet")"
: "${GAL_UI_FONT:?Set GAL_UI_FONT to a readable compatible font, e.g. licensed Noto Sans CJK SC.}"
[[ -r "$GAL_UI_FONT" ]] || { echo 'GAL_UI_FONT is not readable.' >&2; exit 2; }
export GAL_UI_FONT="$(realpath "$GAL_UI_FONT")"
feed="$(realpath "${PACKAGE_FEED:-$root/build-packages/feed}")"
sysroot="$(realpath "${LOOP_GRAPHICS_SYSROOT:-$root/.deps/graphics-sysroot}")"
[[ -f "$sysroot/usr/share/vulkan/icd.d/lvp_icd.json" ]] || { echo 'Select an existing prepared graphics sysroot.' >&2; exit 2; }
python3 -c 'from PIL import Image' # Explicit prerequisite; never install implicitly.
if [[ -n "${LOOP_UI_PROOF_ROOT:-}" ]]; then
 proof="$(realpath -m "$LOOP_UI_PROOF_ROOT")"
 [[ ! -e "$proof" ]] || { echo 'LOOP_UI_PROOF_ROOT must not exist.' >&2; exit 2; }
else proof="$(mktemp -d "${TMPDIR:-/tmp}/dotnet2d-loop-ui-XXXXXX")"; fi
python3 - "$proof" "$root" <<'PY'
from pathlib import Path
import sys
p, root = (Path(s).resolve() for s in sys.argv[1:])
if p == root or root in p.parents:
    raise SystemExit('Select an external LOOP_UI_PROOF_ROOT.')
PY
mkdir -p "$proof/feed" "$proof/logs" "$proof/captures" "$proof/unrelated-working-directory"
cp "$feed"/Dotnet2D.{Engine,Native.Linux.x64}.0.1.0-preview.1.nupkg "$proof/feed/"
python3 - "$proof/feed/Dotnet2D.Native.Linux.x64.0.1.0-preview.1.nupkg" <<'PY_NATIVE'
import sys, zipfile
with zipfile.ZipFile(sys.argv[1]) as package:
    library=package.read('runtimes/linux-x64/native/libgal.so')
    if b'gal_ui_model_open\0' not in library:
        raise SystemExit('Selected native package predates the generic UI bridge. Set PACKAGE_FEED to a matched prepared feed; no automatic native rebuild is performed.')
PY_NATIVE
cp -a packaging/consumers/loop-ui "$proof/app"
rm -rf "$proof/app/bin" "$proof/app/obj"
cp templates/Starter/{StarterGame,StarterInput,FixedStepInput}.cs "$proof/app/"
cp templates/Starter/assets/white.png "$proof/app/assets/"
cp templates/Starter/LICENSE.txt "$proof/app/assets/LICENSE.txt"
python3 - "$proof" <<'PY'
from pathlib import Path
from xml.sax.saxutils import quoteattr
import sys
p=Path(sys.argv[1])
(p/'NuGet.Config').write_text('<configuration><packageSources><clear/><add key="local" value='+quoteattr(str(p/'feed'))+'/></packageSources></configuration>')
PY
export DOTNET_CLI_HOME="$proof/dotnet-home" NUGET_PACKAGES="$proof/nuget-cache" NUGET_HTTP_CACHE_PATH="$proof/http-cache"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_GENERATE_ASPNET_CERTIFICATE=false DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
unset GAL_ASSET_ROOT LD_LIBRARY_PATH LD_PRELOAD LD_AUDIT
"$dotnet" --version > "$proof/logs/sdk-version.txt"
"$dotnet" restore "$proof/app/Sample.csproj" --configfile "$proof/NuGet.Config" > "$proof/logs/restore.log" 2>&1
"$dotnet" build "$proof/app/Sample.csproj" -c Release --no-restore -p:UseSharedCompilation=false > "$proof/logs/build.log" 2>&1
export SDL_VIDEODRIVER=offscreen SDL_AUDIODRIVER=dummy
export VK_ICD_FILENAMES="$sysroot/usr/share/vulkan/icd.d/lvp_icd.json"
export LD_LIBRARY_PATH="$sysroot/usr/lib/x86_64-linux-gnu" MESA_SHADER_CACHE_DIR="$proof/mesa-cache"
export GAL_LOOP_CAPTURE_DIR="$proof/captures"
(cd "$proof/unrelated-working-directory"; "$dotnet" "$proof/app/bin/Release/net10.0/Sample.loop-ui.dll" --check) > "$proof/logs/run.log" 2>&1
python3 scripts/validate-loop-ui-pixels.py > "$proof/logs/pixels.log"
python3 - "$proof" "$root" <<'PY'
from pathlib import Path
import hashlib, json, re, sys, zipfile, xml.etree.ElementTree as ET
p, root = map(Path, sys.argv[1:]); output=p/'app/bin/Release/net10.0'
def sha(f):return hashlib.sha256(f.read_bytes()).hexdigest()
project=ET.parse(p/'app/Sample.csproj')
assert not project.findall('.//ProjectReference') and not project.findall('.//Compile')
assert {r.attrib['Include'] for r in project.findall('.//PackageReference')} == {'Dotnet2D.Engine','Dotnet2D.Native.Linux.x64'}
for log in ('restore','build'):
    assert not re.search(r'\b(?:warning|error) [A-Z]+\d+', (p/f'logs/{log}.log').read_text())
for package, member in [('Dotnet2D.Engine','lib/net10.0/Dotnet2D.Engine.dll'),('Dotnet2D.Native.Linux.x64',None)]:
    with zipfile.ZipFile(p/'feed'/f'{package}.0.1.0-preview.1.nupkg') as z:
        if member: assert z.read(member)==(output/Path(member).name).read_bytes()
        else:
            paths=[n for n in z.namelist() if n.startswith('runtimes/linux-x64/native/') and not n.endswith('/')]
            assert len(paths)==8
            for n in paths:assert z.read(n)==(output/n).read_bytes()
for name in ('StarterGame.cs','StarterInput.cs','FixedStepInput.cs'):
    assert (p/'app'/name).read_bytes()==(root/'templates/Starter'/name).read_bytes()
for f in (root/'packaging/consumers/loop-ui').rglob('*'):
    if f.is_file() and f.suffix in ('.cs','.csproj','.rml','.rcss'):
        assert f.read_bytes()==(p/'app'/f.relative_to(root/'packaging/consumers/loop-ui')).read_bytes()
inputs={str(f.relative_to(root)):sha(f) for f in (root/'packaging/consumers/loop-ui').rglob('*') if f.is_file() and not {'bin','obj'} & set(f.parts)}
inputs.update({f'templates/Starter/{name}':sha(root/'templates/Starter'/name) for name in ('StarterGame.cs','StarterInput.cs','FixedStepInput.cs','assets/white.png','LICENSE.txt')})
run=(p/'logs/run.log').read_text(); checks=re.search(r'LOOP UI CHECKS PASS assertions=(\d+)',run)
assert checks and 'LOOP UI OWNERSHIP PASS reopened-empty-world=true' in run
pixels=re.search(r'LOOP UI PIXELS PASS assertions=(\d+)',(p/'logs/pixels.log').read_text()); assert pixels
result={'scripted_assertions':int(checks[1]),'pixel_assertions':int(pixels[1]),'mode':'independent JIT package consumer, scripted SDL + software Vulkan readback','sdk':(p/'logs/sdk-version.txt').read_text().strip(),
        'sources':inputs,'packages':{f.name:sha(f) for f in (p/'feed').glob('*.nupkg')},
        'physical_input_or_hardware_gpu_acceptance':False,'aot_publishes':0,'native_rebuilds':0}
(p/'results.json').write_text(json.dumps(result,indent=2)+'\n')
PY
cat "$proof/logs/run.log" "$proof/logs/pixels.log"
echo "LOOP UI PROOF PASS $proof"
