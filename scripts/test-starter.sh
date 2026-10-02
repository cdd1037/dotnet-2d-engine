#!/usr/bin/env bash
# Independent JIT public-package proof. No installs, native rebuild or AOT publish.
set -euo pipefail
cd "$(dirname "$0")/.."
root="$PWD"
if [[ -n "${DOTNET:-}" ]]; then dotnet="$DOTNET"
elif command -v dotnet >/dev/null 2>&1; then dotnet="$(command -v dotnet)"
else echo 'Set DOTNET to an existing .NET 10 SDK executable.' >&2; exit 2; fi
[[ -x "$dotnet" ]] || { echo 'DOTNET must be executable.' >&2; exit 2; }
dotnet="$(realpath "$dotnet")"
feed="$(realpath "${PACKAGE_FEED:-$root/build-packages/feed}")"
if [[ -n "${STARTER_PROOF_ROOT:-}" ]]; then
    proof="$(python3 - "$STARTER_PROOF_ROOT" "$root" <<'PY'
from pathlib import Path
import sys
p, root = map(lambda x: Path(x).resolve(), sys.argv[1:])
if p.exists() or p == root or root in p.parents:
    raise SystemExit('STARTER_PROOF_ROOT must be a fresh directory outside the checkout.')
print(p)
PY
)"
    mkdir -p "$proof"
else proof="$(mktemp -d "${TMPDIR:-/tmp}/dotnet2d-starter-XXXXXX")"; fi
python3 - "$proof" "$root" <<'PYROOT'
from pathlib import Path
import sys
p, root = map(lambda x: Path(x).resolve(), sys.argv[1:])
if p == root or root in p.parents:
    raise SystemExit('Proof must be outside the checkout; select another TMPDIR or STARTER_PROOF_ROOT.')
PYROOT
for package in Dotnet2D.Engine Dotnet2D.Native.Linux.x64; do
    [[ -f "$feed/$package.0.1.0-preview.1.nupkg" ]] || { echo "Missing local package: $package" >&2; exit 2; }
done
cp -a templates/Starter "$proof/app"
# Copy source only, even if an author previously built the canonical directory.
rm -rf "$proof/app/bin" "$proof/app/obj" "$proof/app/packages"
mkdir -p "$proof/app/packages" "$proof/logs" "$proof/unrelated-working-directory"
cp "$feed"/Dotnet2D.{Engine,Native.Linux.x64}.0.1.0-preview.1.nupkg "$proof/app/packages/"
export DOTNET_CLI_HOME="$proof/dotnet-home" NUGET_PACKAGES="$proof/nuget-cache" NUGET_HTTP_CACHE_PATH="$proof/http-cache"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 DOTNET_GENERATE_ASPNET_CERTIFICATE=false
unset LD_LIBRARY_PATH LD_PRELOAD LD_AUDIT GAL_ASSET_ROOT
cd "$proof"
"$dotnet" --version > logs/sdk-version.txt
"$dotnet" restore app/Starter.csproj --configfile app/NuGet.Config > logs/restore.log 2>&1
"$dotnet" build app/Starter.csproj -c Release --no-restore -p:UseSharedCompilation=false > logs/build.log 2>&1
output="$proof/app/bin/Release/net10.0"
app="$output/Starter.dll"
# Run from an unrelated current directory to verify AppContext-based asset paths.
cd "$proof/unrelated-working-directory"
"$dotnet" "$app" --self-test > "$proof/logs/self-test.log" 2>&1
"$dotnet" "$app" --headless > "$proof/logs/headless.log" 2>&1
"$dotnet" "$app" --headless --physics > "$proof/logs/physics.log" 2>&1
"$dotnet" "$app" --headless --frames 3 --assets "$output/assets" > "$proof/logs/explicit-assets.log" 2>&1
expect_error() {
    local label="$1" expected="$2"; shift 2
    local status=0
    "$dotnet" "$@" > "$proof/logs/$label.log" 2>&1 || status=$?
    [[ "$status" == 2 ]] || { echo "$label: expected exit 2, got $status" >&2; exit 1; }
    grep -q "$expected" "$proof/logs/$label.log"
}
expect_error invalid-option 'positive integer' "$app" --frames 0
expect_error missing-assets 'ASSET_MISSING.*white.png' "$app" --headless --assets "$proof/no-such-assets"
cp -a "$output" "$proof/missing-native"
rm -rf "$proof/missing-native/runtimes"
expect_error missing-native 'STARTER ERROR:.*shared library.*gal' "$proof/missing-native/Starter.dll" --headless
# Check the exact copied host statements, including suspension and presentation.
python3 "$root/scripts/test-loop-recipe.py" --starter-proof "$proof" --dotnet "$dotnet"
# Optional reuse of an already prepared software Vulkan system environment.
# This does not replace libgal or any packaged native library.
if [[ -n "${STARTER_GRAPHICS_SYSROOT:-}" ]]; then
    sysroot="$(realpath "$STARTER_GRAPHICS_SYSROOT")"
    [[ -f "$sysroot/usr/share/vulkan/icd.d/lvp_icd.json" ]] || { echo 'Missing prepared lavapipe ICD.' >&2; exit 2; }
    SDL_VIDEODRIVER=offscreen SDL_AUDIODRIVER=dummy \
    VK_ICD_FILENAMES="$sysroot/usr/share/vulkan/icd.d/lvp_icd.json" \
    LD_LIBRARY_PATH="$sysroot/usr/lib/x86_64-linux-gnu" \
    MESA_SHADER_CACHE_DIR="$proof/mesa-cache" \
        "$dotnet" "$app" --physics --frames 30 > "$proof/logs/graphics.log" 2>&1
fi
python3 - "$proof" "$root" <<'PY'
from pathlib import Path
import hashlib, json, re, sys, zipfile, xml.etree.ElementTree as ET
p, root = map(Path, sys.argv[1:]); output=p/'app/bin/Release/net10.0'
project=ET.parse(p/'app/Starter.csproj')
assert not project.findall('.//ProjectReference') and not project.findall('.//Compile')
assert all((v.text or '') != 'GameAuthoringLab' for v in project.findall('.//AssemblyName'))
assert {r.attrib['Include'] for r in project.findall('.//PackageReference')} == {'Dotnet2D.Engine','Dotnet2D.Native.Linux.x64'}
assets=json.loads((p/'app/obj/project.assets.json').read_text())
assert all(v['type']=='package' for v in assets['libraries'].values())
for log in ('restore', 'build'):
    assert not re.search(r'\b(?:warning|error) [A-Z]+\d+', (p/f'logs/{log}.log').read_text())
checks=(p/'logs/self-test.log').read_text(); match=re.search(r'STARTER CHECKS PASS assertions=(\d+)',checks); assert match
for log in ('headless', 'physics'):
    assert 'STARTER PASS frames=120 steps=120 pulses=0' in (p/f'logs/{log}.log').read_text()
assert 'STARTER PASS frames=3 steps=3' in (p/'logs/explicit-assets.log').read_text()
assert (output/'assets/white.png').read_bytes()==(root/'templates/Starter/assets/white.png').read_bytes()
for name in ('Dotnet2D.Engine','Dotnet2D.Native.Linux.x64'):
    assert (output/f'licenses/{name}/LICENSE.txt').stat().st_size>100
packages=sorted((p/'app/packages').glob('*.nupkg'))
with zipfile.ZipFile(next(f for f in packages if '.Engine.' in f.name)) as z:
    assert z.read('lib/net10.0/Dotnet2D.Engine.dll')==(output/'Dotnet2D.Engine.dll').read_bytes()
with zipfile.ZipFile(next(f for f in packages if '.Native.' in f.name)) as z:
    native=[n for n in z.namelist() if n.startswith('runtimes/linux-x64/native/') and not n.endswith('/')]
    assert len(native)==8
    for n in native: assert z.read(n)==(output/n).read_bytes()
if (p/'logs/graphics.log').exists():
    graphics=(p/'logs/graphics.log').read_text()
    assert 'Starter: vulkan;' in graphics and 'STARTER PASS frames=30 ' in graphics
    assert re.search(r'native-frames=30 draw-calls=([1-9][0-9]*)',graphics)
inputs={str(f.relative_to(root)):hashlib.sha256(f.read_bytes()).hexdigest() for f in sorted((root/'templates/Starter').rglob('*')) if f.is_file() and not any(s in ('bin','obj','packages') for s in f.relative_to(root/'templates/Starter').parts)}
result={'mode':'independent JIT PackageReference consumer','sdk':(p/'logs/sdk-version.txt').read_text().strip(),'assertions':int(match[1]),'host_policy_checks':json.loads((p/'loop-policy-results.json').read_text())['checks'],'headless_runs':{'plain':120,'physics':120,'explicit_asset_root':3},'negative_exits':{'invalid_option':2,'missing_assets':2,'missing_native':2},'native_libraries_verified':len(native),'graphics_30_frame_smoke':(p/'logs/graphics.log').exists(),'pixel_or_physical_input_acceptance':False,'aot_publishes':0,'packages':{f.name:hashlib.sha256(f.read_bytes()).hexdigest() for f in packages},'template_inputs':inputs}
(p/'results.json').write_text(json.dumps(result,indent=2)+'\n')
PY
cat "$proof/logs/self-test.log" "$proof/logs/headless.log" "$proof/logs/physics.log"
echo "STARTER PROOF PASS $proof"
