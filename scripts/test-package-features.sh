#!/usr/bin/env bash
# Focused recent-feature proof: ordinary PackageReference JIT plus one fresh AOT.
# Requires freshly rebuilt local engine/native packages. No downloads or publishing.
set -euo pipefail
cd "$(dirname "$0")/.."
root="$PWD"
[[ "$(uname -s)" == Linux && "$(uname -m)" == x86_64 ]] || { echo 'This package proof requires Linux x64.' >&2; exit 2; }
if [[ -n "${DOTNET:-}" ]]; then dotnet="$DOTNET"
elif command -v dotnet >/dev/null 2>&1; then dotnet="$(command -v dotnet)"
else dotnet="$root/../android-trim-tools/dotnet/dotnet"; fi
feed="${PACKAGE_FEED:-$root/build-packages/feed}"
source_cache="${PACKAGE_SOURCE_CACHE:-$root/../android-trim-tools/nuget}"
compiler="${AOT_CXX:-$root/.tools/aot/usr/bin/clang-19}"
proof="${PACKAGE_FEATURE_PROOF_ROOT:-/tmp/dotnet2d-feature-proof}"
proof="$(python3 - "$proof" "$root" <<'PY'
from pathlib import Path
import sys
proof, root = (Path(x).resolve() for x in sys.argv[1:])
if proof == root or root in proof.parents:
    raise SystemExit('PACKAGE_FEATURE_PROOF_ROOT must be outside the checkout.')
if proof.exists():
    raise SystemExit('Choose a fresh PACKAGE_FEATURE_PROOF_ROOT.')
print(proof)
PY
)"
[[ -x "$dotnet" && -x "$compiler" ]] || { echo 'Set DOTNET and AOT_CXX to existing local SDK/compiler executables.' >&2; exit 2; }
dotnet="$(realpath "$dotnet")"
compiler="$(realpath "$compiler")"
mkdir -p "$proof/feed" "$proof/logs" "$proof/prerequisites" "$proof/publish"
cp "$feed/Dotnet2D.Engine.0.1.0-preview.1.nupkg" "$feed/Dotnet2D.Native.Linux.x64.0.1.0-preview.1.nupkg" "$proof/feed/"
for package in microsoft.net.illink.tasks microsoft.dotnet.ilcompiler runtime.linux-x64.microsoft.dotnet.ilcompiler microsoft.netcore.app.runtime.linux-x64 microsoft.aspnetcore.app.runtime.linux-x64 microsoft.netcore.app.runtime.nativeaot.linux-x64; do
    cp "$source_cache/$package/10.0.12/$package.10.0.12.nupkg" "$proof/feed/"
done
cp -a packaging/consumers/features "$proof/consumer"
python3 - "$proof" <<'PY'
from pathlib import Path
from xml.sax.saxutils import escape
import json, struct, sys
p = Path(sys.argv[1])
# A caller-owned 2x1, 24-bit BMP with row padding: red then blue. No repo atlas input.
pixels = bytes([0, 0, 255, 255, 0, 0, 0, 0])
header = b'BM' + struct.pack('<IHHI', 54 + len(pixels), 0, 0, 54)
header += struct.pack('<IiiHHIIiiII', 40, 2, 1, 1, 24, 0, len(pixels), 2835, 2835, 0, 0)
(p / 'consumer/assets/palette.bmp').write_bytes(header + pixels)
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
# Every build/run starts outside the checkout; global.json pins the prepared SDK.
cd "$proof"
"$dotnet" --info > logs/dotnet-info.log
sha256sum feed/*.nupkg consumer/Program.cs consumer/Sample.csproj consumer/assets/* > logs/inputs.sha256
project="$proof/consumer/Sample.csproj"
"$dotnet" restore "$project" --configfile "$proof/NuGet.Config" > logs/features-restore.log 2>&1
"$dotnet" run --project "$project" -c Release --no-restore -p:UseSharedCompilation=false > logs/features-jit.log 2>&1
cp -a consumer/bin/Release/net10.0 publish/features-fdd
"$dotnet" restore "$project" --configfile "$proof/NuGet.Config" -r linux-x64 -p:PublishAot=true -p:RuntimeFrameworkVersion=10.0.12 > logs/features-aot-restore.log 2>&1
LD_LIBRARY_PATH="$root/.tools/aot/usr/lib/x86_64-linux-gnu" "$dotnet" publish "$project" -c Release -r linux-x64 --no-restore -m:1 -nr:false -p:UseSharedCompilation=false -p:PublishAot=true -p:StripSymbols=true -p:IlcGenerateMapFile=true -p:RuntimeFrameworkVersion=10.0.12 -p:CppCompilerAndLinker="$compiler" -p:CustomAfterMicrosoftCommonTargets="$proof/prerequisites/inprocess-illink.targets" -o "$proof/publish/features-aot" > logs/features-aot-publish.log 2>&1
"$proof/publish/features-aot/Sample.features" > logs/features-aot.log 2>&1
cp consumer/obj/Release/net10.0/linux-x64/native/Sample.features.map.xml logs/features-aot-map.xml
python3 - "$proof" <<'PY'
from pathlib import Path
import json, re, sys
p = Path(sys.argv[1])
result = {'sdk': '10.0.401', 'runtime': '10.0.12', 'native_aot_publishes': 1, 'modes': {}}
for mode, directory in [('jit', 'features-fdd'), ('aot', 'features-aot')]:
    log = p.joinpath('logs/features-' + mode + '.log').read_text()
    match = re.search(r'^PACKAGE FEATURES PASS assertions=(\d+).*query-bytes=0 bodies=0 textures=0$', log, re.M)
    if not match:
        raise SystemExit('Missing successful feature proof: ' + mode)
    files = [f for f in p.joinpath('publish', directory).rglob('*') if f.is_file()]
    result['modes'][mode] = {'assertions': int(match[1]), 'output_bytes': sum(f.stat().st_size for f in files), 'files': len(files), 'query_managed_bytes': 0}
p.joinpath('results.json').write_text(json.dumps(result, indent=2) + '\n')
PY
cat logs/features-jit.log logs/features-aot.log
echo "PACKAGE FEATURES PROOF PASS $proof"
