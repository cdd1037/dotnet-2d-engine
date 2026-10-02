#!/usr/bin/env bash
# One copied CPU-only public consumer, JIT and a fresh NativeAOT publish.
# No native library, graphics setup, dependency download or remote package publication.
set -euo pipefail
cd "$(dirname "$0")/.."
root="$PWD"
dotnet="${DOTNET:-$root/../android-trim-tools/dotnet/dotnet}"
feed="${PACKAGE_FEED:-$root/build-packages/feed}"
cache="${PACKAGE_SOURCE_CACHE:-$root/../android-trim-tools/nuget}"
proof="${PACKAGE_COMPOSITION_PROOF_ROOT:-/tmp/dotnet2d-composition-proof}"
[[ ! -e "$proof" ]] || { echo 'Choose a fresh PACKAGE_COMPOSITION_PROOF_ROOT.' >&2; exit 2; }
[[ -f "$feed/Dotnet2D.Engine.0.1.0-preview.1.nupkg" ]] || { echo 'Run scripts/pack-managed.sh against the current source first.' >&2; exit 2; }
mkdir -p "$proof/feed" "$proof/logs" "$proof/prerequisites" "$proof/publish"
cp "$feed/Dotnet2D.Engine.0.1.0-preview.1.nupkg" "$proof/feed/"
for package in microsoft.net.illink.tasks microsoft.dotnet.ilcompiler runtime.linux-x64.microsoft.dotnet.ilcompiler microsoft.netcore.app.runtime.linux-x64 microsoft.aspnetcore.app.runtime.linux-x64 microsoft.netcore.app.runtime.nativeaot.linux-x64; do
 path="$cache/$package/10.0.12/$package.10.0.12.nupkg"
 [[ -f "$path" ]] || { echo "Missing existing tool/runtime pack: $path" >&2; exit 2; }
 cp "$path" "$proof/feed/"
done
mkdir -p "$proof/consumer"
cp packaging/consumers/composition/*.cs packaging/consumers/composition/Sample.csproj "$proof/consumer/"
python3 - "$proof" <<'PY'
from pathlib import Path
import sys
from xml.sax.saxutils import escape
p = Path(sys.argv[1])
p.joinpath('NuGet.Config').write_text('<configuration><packageSources><clear/><add key="local" value="'+escape(str(p/'feed'), {'"':'&quot;'})+'"/></packageSources></configuration>')
p.joinpath('prerequisites/inprocess-illink.targets').write_text('''<Project>
 <UsingTask TaskName="ComputeManagedAssemblies" AssemblyFile="$(NuGetPackageRoot)/microsoft.net.illink.tasks/10.0.12/tools/net/ILLink.Tasks.dll" TaskFactory="AssemblyTaskFactory" Override="true" />
 <UsingTask TaskName="ILLink" AssemblyFile="$(NuGetPackageRoot)/microsoft.net.illink.tasks/10.0.12/tools/net/ILLink.Tasks.dll" TaskFactory="AssemblyTaskFactory" Override="true" />
</Project>''')
PY
export DOTNET_CLI_HOME="$proof/prerequisites/dotnet-home" NUGET_PACKAGES="$proof/nuget-cache" NUGET_HTTP_CACHE_PATH="$proof/http-cache"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_GENERATE_ASPNET_CERTIFICATE=false DOTNET_SKIP_FIRST_TIME_EXPERIENCE=true
unset GAL_ASSET_ROOT LD_LIBRARY_PATH LD_PRELOAD LD_AUDIT
project="$proof/consumer/Sample.csproj"
"$dotnet" --info > "$proof/logs/dotnet-info.log"
"$dotnet" restore "$project" --configfile "$proof/NuGet.Config" > "$proof/logs/jit-restore.log" 2>&1
"$dotnet" publish "$project" -c Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false -o "$proof/publish/jit" > "$proof/logs/jit-publish.log" 2>&1
(cd "$proof"; "$dotnet" "$proof/publish/jit/Sample.composition.dll") > "$proof/logs/jit-run.log" 2>&1
"$dotnet" restore "$project" --configfile "$proof/NuGet.Config" -r linux-x64 -p:PublishAot=true -p:RuntimeFrameworkVersion=10.0.12 > "$proof/logs/aot-restore.log" 2>&1
LD_LIBRARY_PATH="$root/.tools/aot/usr/lib/x86_64-linux-gnu" "$dotnet" publish "$project" -c Release -r linux-x64 --no-restore -m:1 -nr:false -p:UseSharedCompilation=false -p:PublishAot=true -p:StripSymbols=true -p:RuntimeFrameworkVersion=10.0.12 -p:CppCompilerAndLinker="${AOT_CXX:-$root/.tools/aot/usr/bin/clang-19}" -p:CustomAfterMicrosoftCommonTargets="$proof/prerequisites/inprocess-illink.targets" -o "$proof/publish/aot" > "$proof/logs/aot-publish.log" 2>&1
(cd "$proof"; "$proof/publish/aot/Sample.composition") > "$proof/logs/aot-run.log" 2>&1
python3 - "$root" "$proof" <<'PY'
from pathlib import Path
import hashlib, json, subprocess, sys, zipfile
root, proof = map(Path, sys.argv[1:])
digest = lambda data: hashlib.sha256(data).hexdigest()
package = proof/'feed/Dotnet2D.Engine.0.1.0-preview.1.nupkg'
with zipfile.ZipFile(package) as z:
    dll = z.read('lib/net10.0/Dotnet2D.Engine.dll')
assert dll == (root/'engine/bin/Release/net10.0/Dotnet2D.Engine.dll').read_bytes(), 'Package does not contain the current built engine'
assert dll == (proof/'publish/jit/Dotnet2D.Engine.dll').read_bytes(), 'Consumer did not use the isolated package'
for filename in ('Composition.cs', 'CompositionChecks.cs', 'Program.cs', 'Sample.csproj'):
    assert (root/'packaging/consumers/composition'/filename).read_bytes() == (proof/'consumer'/filename).read_bytes(), filename
assert '<ProjectReference' not in (proof/'consumer/Sample.csproj').read_text()
assert '<Compile ' not in (proof/'consumer/Sample.csproj').read_text()
assert not list((proof/'publish').rglob('libgal*')), 'CPU consumer unexpectedly shipped native engine'
jit = (proof/'logs/jit-run.log').read_text().strip()
aot = (proof/'logs/aot-run.log').read_text().strip()
assert jit == aot and jit.startswith('PACKAGE COMPOSITION PASS'), 'JIT/AOT contract results differ'
result = {'source_commit': subprocess.check_output(['git','rev-parse','HEAD'], cwd=root, text=True).strip(),
          'package_sha256': digest(package.read_bytes()), 'engine_dll_sha256': digest(dll),
          'source_sha256': {p: digest((root/p).read_bytes()) for p in ('managed/World.cs', 'managed/BehaviorLifetime.cs', 'engine/RuntimeSources.props')},
          'consumer_sha256': {p.name: digest(p.read_bytes()) for p in (proof/'consumer').glob('*.cs')},
          'result': jit, 'native_engine_included': False,
          'aot_executable_bytes': (proof/'publish/aot/Sample.composition').stat().st_size}
(proof/'results.json').write_text(json.dumps(result, indent=2)+'\n')
print(jit)
print('PACKAGE COMPOSITION PROOF PASS', proof)
PY
