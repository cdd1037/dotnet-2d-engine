#!/usr/bin/env bash
# Safe public-authoring boundary: ordinary PackageReference JIT, negative
# compilation, and one NativeAOT publish using prepared local packages/toolchains.
set -euo pipefail
cd "$(dirname "$0")/.."
root="$PWD"
[[ "$(uname -s)" == Linux && "$(uname -m)" == x86_64 ]] || { echo 'This package proof requires Linux x64.' >&2; exit 2; }
if [[ -n "${DOTNET:-}" ]]; then dotnet="$DOTNET"
elif command -v dotnet >/dev/null 2>&1; then dotnet="$(command -v dotnet)"
else dotnet="$root/.tools/dotnet/dotnet"; fi
feed="${PACKAGE_FEED:-$root/build-packages/feed}"
source_cache="${PACKAGE_SOURCE_CACHE:-$root/.tools/nuget}"
compiler="${AOT_CXX:-$root/.tools/aot/usr/bin/clang-19}"
proof="${PACKAGE_AUTHORING_PROOF_ROOT:-/tmp/dotnet2d-authoring-proof}"
proof="$(python3 - "$proof" "$root" <<'PY'
from pathlib import Path
import sys
proof, root = (Path(x).resolve() for x in sys.argv[1:])
if proof == root or root in proof.parents:
    raise SystemExit('PACKAGE_AUTHORING_PROOF_ROOT must be outside the checkout.')
if proof.exists():
    raise SystemExit('Choose a fresh PACKAGE_AUTHORING_PROOF_ROOT.')
print(proof)
PY
)"
[[ -x "$dotnet" && -x "$compiler" ]] || { echo 'Set DOTNET and AOT_CXX to existing local SDK/compiler executables.' >&2; exit 2; }
dotnet="$(realpath "$dotnet")"
compiler="$(realpath "$compiler")"
mkdir -p "$proof/feed" "$proof/logs" "$proof/prerequisites" "$proof/publish" "$proof/consumer"
trap 'echo "Authoring proof logs: $proof/logs"' EXIT
cp "$feed/Dotnet2D.Engine.0.1.0-preview.1.nupkg" "$feed/Dotnet2D.Native.Linux.x64.0.1.0-preview.1.nupkg" "$proof/feed/"
for package in microsoft.net.illink.tasks microsoft.dotnet.ilcompiler runtime.linux-x64.microsoft.dotnet.ilcompiler microsoft.netcore.app.runtime.linux-x64 microsoft.aspnetcore.app.runtime.linux-x64 microsoft.netcore.app.runtime.nativeaot.linux-x64; do
    cp "$source_cache/$package/10.0.12/$package.10.0.12.nupkg" "$proof/feed/"
done
tar -C packaging/consumers/authoring --exclude=bin --exclude=obj -cf - . | tar -C "$proof/consumer" -xf -
python3 - "$proof" <<'PY'
from pathlib import Path
from xml.sax.saxutils import escape
import json, sys
p = Path(sys.argv[1])
p.joinpath('global.json').write_text(json.dumps({'sdk': {'version': '10.0.401', 'rollForward': 'disable'}}))
p.joinpath('NuGet.Config').write_text('<configuration><packageSources><clear/><add key="local" value="' + escape(str(p / 'feed'), {'"': '&quot;'}) + '"/></packageSources></configuration>')
p.joinpath('prerequisites/inprocess-illink.targets').write_text('''<Project>
 <UsingTask TaskName="ComputeManagedAssemblies" AssemblyFile="$(NuGetPackageRoot)/microsoft.net.illink.tasks/10.0.12/tools/net/ILLink.Tasks.dll" TaskFactory="AssemblyTaskFactory" Override="true" />
 <UsingTask TaskName="ILLink" AssemblyFile="$(NuGetPackageRoot)/microsoft.net.illink.tasks/10.0.12/tools/net/ILLink.Tasks.dll" TaskFactory="AssemblyTaskFactory" Override="true" />
</Project>''')
PY
export DOTNET_CLI_HOME="$proof/prerequisites/dotnet-home" NUGET_PACKAGES="$proof/nuget-cache" NUGET_HTTP_CACHE_PATH="$proof/http-cache"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_GENERATE_ASPNET_CERTIFICATE=false DOTNET_SKIP_FIRST_TIME_EXPERIENCE=true
export DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=true
unset GAL_ASSET_ROOT LD_LIBRARY_PATH LD_PRELOAD LD_AUDIT
# Fresh cache and all commands run outside the checkout against PackageReference.
cd "$proof"
"$dotnet" --info > logs/dotnet-info.log
project="$proof/consumer/Sample.csproj"
"$dotnet" restore "$project" --configfile "$proof/NuGet.Config" > logs/authoring-restore.log 2>&1
"$dotnet" run --project "$project" -c Release --no-restore -p:UseSharedCompilation=false > logs/authoring-jit.log 2>&1
cp -a consumer/bin/Release/net10.0 publish/authoring-fdd
negative="$proof/consumer/compilefail/WrongKinds.csproj"
"$dotnet" restore "$negative" --configfile "$proof/NuGet.Config" > logs/compilefail-restore.log 2>&1
if "$dotnet" build "$negative" -c Release --no-restore -p:UseSharedCompilation=false > logs/compilefail.log 2>&1; then
    echo 'Wrong resource kinds unexpectedly compiled.' >&2; exit 1
fi
attribute_negative="$proof/consumer/compilefail/NoNumericAttributes.csproj"
"$dotnet" restore "$attribute_negative" --configfile "$proof/NuGet.Config" > logs/attribute-restore.log 2>&1
if "$dotnet" build "$attribute_negative" -c Release --no-restore -p:UseSharedCompilation=false > logs/attribute-compilefail.log 2>&1; then
    echo 'Explicit numeric UI attribute unexpectedly compiled.' >&2; exit 1
fi
python3 - "$proof" <<'PY'
from pathlib import Path
import json, re, sys
p = Path(sys.argv[1])
log = p.joinpath('logs/compilefail.log').read_text() + p.joinpath('logs/attribute-compilefail.log').read_text()
expected = {'TextureAsMaterial': 'CS0029', 'MaterialAsTexture': 'CS1503', 'TextureAsTarget': 'CS1503',
            'TargetAsTexture': 'CS1503', 'MaterialAsTarget': 'CS1503', 'TargetAsMaterial': 'CS0029',
            'NumericResourceIds': 'CS0029', 'NumericAction': 'CS0029',
            'RawSprite': 'CS0122',
            'RawAffine': 'CS0122',
            'RawRegion': 'CS0122',
            'RawMaterial': 'CS0122',
            'RawPass': 'CS0122',
            'RawInput': 'CS0122',
            'RawBinding': 'CS0122',
            'LegacyUi': 'CS0122',
            'NumericState': 'CS1729',
            'NumericShape': 'CS1729',
            'RawShapeExtent': 'CS0200',
            'NumericTexture': 'CS1503',
            'NumericCommand': 'CS1503',
            'NumericAttribute': 'CS0246',
            'RawLease': 'CS1061',
            'RawBatch': 'CS1061',
            'SourceOnlyUi': 'CS1061',
            'RawPoll': 'CS1061',
            }
for name, code in expected.items():
    if not re.search(r'\b' + name + r'\.cs\(\d+,\d+\): error ' + code + r'\b', log):
        raise SystemExit('Missing intended compile-time rejection: ' + name + ' ' + code)
p.joinpath('logs/compilefail-results.json').write_text(json.dumps(expected, indent=2) + '\n')
print('PACKAGE AUTHORING COMPILEFAIL PASS cases=' + str(len(expected)))
PY
"$dotnet" restore "$project" --configfile "$proof/NuGet.Config" -r linux-x64 -p:PublishAot=true -p:RuntimeFrameworkVersion=10.0.12 > logs/authoring-aot-restore.log 2>&1
LD_LIBRARY_PATH="$root/.tools/aot/usr/lib/x86_64-linux-gnu" "$dotnet" publish "$project" -c Release -r linux-x64 --no-restore -m:1 -nr:false -p:UseSharedCompilation=false -p:PublishAot=true -p:StripSymbols=true -p:IlcGenerateMapFile=true -p:RuntimeFrameworkVersion=10.0.12 -p:CppCompilerAndLinker="$compiler" -p:CustomAfterMicrosoftCommonTargets="$proof/prerequisites/inprocess-illink.targets" -o "$proof/publish/authoring-aot" > logs/authoring-aot-publish.log 2>&1
"$proof/publish/authoring-aot/Sample.authoring" > logs/authoring-aot.log 2>&1
cp consumer/obj/Release/net10.0/linux-x64/native/Sample.authoring.map.xml logs/authoring-aot-map.xml
python3 - "$proof" <<'PY'
from pathlib import Path
import json, re, sys
p = Path(sys.argv[1])
result = {'sdk': '10.0.401', 'runtime': '10.0.12', 'native_aot_publishes': 1,
          'compile_failures': json.loads(p.joinpath('logs/compilefail-results.json').read_text()), 'modes': {}}
for mode, directory in [('jit', 'authoring-fdd'), ('aot', 'authoring-aot')]:
    log = p.joinpath('logs/authoring-' + mode + '.log').read_text()
    match = re.search(r'^PACKAGE AUTHORING PASS assertions=(\d+) input-bytes=0 draw-bytes=0 textures=0 materials=0 targets=0$', log, re.M)
    if not match:
        raise SystemExit('Missing successful authoring proof: ' + mode)
    files = [f for f in p.joinpath('publish', directory).rglob('*') if f.is_file()]
    result['modes'][mode] = {'assertions': int(match[1]), 'output_bytes': sum(f.stat().st_size for f in files),
                             'files': len(files), 'input_managed_bytes': 0, 'draw_managed_bytes': 0}
p.joinpath('results.json').write_text(json.dumps(result, indent=2) + '\n')
PY
cat logs/authoring-jit.log logs/authoring-aot.log
echo "PACKAGE AUTHORING PROOF PASS $proof"
