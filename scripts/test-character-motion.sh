#!/usr/bin/env bash
# Focused headless native-physics recipe check. Existing packages only; no engine rebuild or AOT.
set -euo pipefail
started=$SECONDS
cd "$(dirname "$0")/.."
root="$PWD"
dotnet="${DOTNET:-$root/../android-trim-tools/dotnet/dotnet}"
[[ -x "$dotnet" ]] || { echo 'Set DOTNET to an existing .NET 10 SDK.' >&2; exit 2; }
dotnet="$(realpath "$dotnet")"
feed="$(realpath "${PACKAGE_FEED:-$root/build-packages/feed}")"
for package in Dotnet2D.Engine Dotnet2D.Native.Linux.x64; do
    [[ -f "$feed/$package.0.1.0-preview.1.nupkg" ]] || { echo "Missing prepared package: $package" >&2; exit 2; }
done
if [[ -n "${CHARACTER_MOTION_PROOF_ROOT:-}" ]]; then
    proof="$(realpath -m "$CHARACTER_MOTION_PROOF_ROOT")"
    [[ ! -e "$proof" ]] || { echo 'CHARACTER_MOTION_PROOF_ROOT must not exist.' >&2; exit 2; }
else proof="$(mktemp -d "${TMPDIR:-/tmp}/dotnet2d-character-motion-XXXXXX")"; fi
python3 - "$proof" "$root" <<'PY'
from pathlib import Path
import sys
p, root = map(lambda s: Path(s).resolve(), sys.argv[1:])
if p == root or root in p.parents:
    raise SystemExit('Select an external CHARACTER_MOTION_PROOF_ROOT.')
PY
mkdir -p "$proof/logs" "$proof/unrelated-working-directory"
trap 'status=$?; echo "CHARACTER MOTION PROOF FAILED: $proof/logs" >&2; tail -n 12 "$proof"/logs/*.log >&2; exit "$status"' ERR
cp -a packaging/consumers/character-motion "$proof/app"
rm -rf "$proof/app/bin" "$proof/app/obj" "$proof/app/packages"
mkdir -p "$proof/app/packages"
cp "$feed"/Dotnet2D.{Engine,Native.Linux.x64}.0.1.0-preview.1.nupkg "$proof/app/packages/"
export DOTNET_CLI_HOME="$proof/dotnet-home" NUGET_PACKAGES="$proof/nuget-cache" NUGET_HTTP_CACHE_PATH="$proof/http-cache"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_GENERATE_ASPNET_CERTIFICATE=false DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
unset LD_LIBRARY_PATH LD_PRELOAD LD_AUDIT GAL_ASSET_ROOT
"$dotnet" --version > "$proof/logs/sdk-version.txt"
"$dotnet" restore "$proof/app/Sample.csproj" --configfile "$proof/app/NuGet.Config" > "$proof/logs/restore.log" 2>&1
"$dotnet" build "$proof/app/Sample.csproj" -c Release --no-restore -p:UseSharedCompilation=false > "$proof/logs/build.log" 2>&1
output="$proof/app/bin/Release/net10.0"
app="$output/Sample.character-motion.dll"
(cd "$proof/unrelated-working-directory"; "$dotnet" "$app" --check) > "$proof/logs/checks.log" 2>&1
(cd "$proof/unrelated-working-directory"; "$dotnet" "$app") > "$proof/logs/demo.log" 2>&1
python3 - "$proof" "$root" <<'PY'
from pathlib import Path
import hashlib, json, re, sys, zipfile, xml.etree.ElementTree as ET
p, root = map(Path, sys.argv[1:]); output=p/'app/bin/Release/net10.0'
project=ET.parse(p/'app/Sample.csproj')
assert not project.findall('.//ProjectReference') and not project.findall('.//Compile')
assert {r.attrib['Include'] for r in project.findall('.//PackageReference')} == {'Dotnet2D.Engine','Dotnet2D.Native.Linux.x64'}
assets=json.loads((p/'app/obj/project.assets.json').read_text())
assert all(v['type']=='package' for v in assets['libraries'].values())
for name in ('restore','build'):
    assert not re.search(r'\b(?:warning|error) [A-Z]+\d+', (p/f'logs/{name}.log').read_text())
checks=(p/'logs/checks.log').read_text()
match=re.search(r'CHARACTER MOTION CHECKS PASS assertions=(\d+)', checks); assert match
demo=(p/'logs/demo.log').read_text(); assert 'CHARACTER MOTION DEMO PASS steps=390' in demo
for log in (checks, demo): assert 'CHARACTER MOTION OWNERSHIP PASS reopened-empty-world=true' in log
packages=sorted((p/'app/packages').glob('*.nupkg'))
with zipfile.ZipFile(next(f for f in packages if '.Engine.' in f.name)) as z:
    assert z.read('lib/net10.0/Dotnet2D.Engine.dll')==(output/'Dotnet2D.Engine.dll').read_bytes()
    assert z.read('LICENSE')==(output/'licenses/Dotnet2D.Engine/LICENSE.txt').read_bytes()
with zipfile.ZipFile(next(f for f in packages if '.Native.' in f.name)) as z:
    native=[n for n in z.namelist() if n.startswith('runtimes/linux-x64/native/') and not n.endswith('/')]
    assert native
    for n in native: assert z.read(n)==(output/n).read_bytes()
    assert z.read('LICENSE.txt')==(output/'licenses/Dotnet2D.Native.Linux.x64/LICENSE.txt').read_bytes()
for name in ('Dotnet2D.Engine','Dotnet2D.Native.Linux.x64'):
    assert (output/f'licenses/{name}/LICENSE.txt').stat().st_size>100
source=root/'packaging/consumers/character-motion'
inputs={}
for f in sorted(source.rglob('*')):
    if not f.is_file() or {'bin','obj','packages'} & set(f.relative_to(source).parts): continue
    assert f.read_bytes()==(p/'app'/f.relative_to(source)).read_bytes()
    inputs[str(f.relative_to(root))]=hashlib.sha256(f.read_bytes()).hexdigest()
result={'mode':'independent JIT PackageReference consumer, headless native Box2D',
        'assertions':int(match[1]),'sdk':(p/'logs/sdk-version.txt').read_text().strip(),
        'native_libraries_verified':len(native),'sources':inputs,
        'packages':{f.name:hashlib.sha256(f.read_bytes()).hexdigest() for f in packages},
        'native_rebuilds':0,'aot_publishes':0,'rendered_or_device_acceptance':False}
(p/'results.json').write_text(json.dumps(result,indent=2)+'\n')
PY
cat "$proof/logs/checks.log" "$proof/logs/demo.log"
echo "CHARACTER MOTION PROOF PASS $proof elapsed=$((SECONDS-started))s"
