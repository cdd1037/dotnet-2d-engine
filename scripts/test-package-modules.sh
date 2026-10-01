#!/usr/bin/env bash
# One independent consumer for the existing optional modules. No downloads or publishing.
set -euo pipefail
cd "$(dirname "$0")/.."
root="$PWD"
if [[ -n "${DOTNET:-}" ]]; then dotnet="$DOTNET"
elif command -v dotnet >/dev/null 2>&1; then dotnet="$(command -v dotnet)"
else dotnet="$root/../android-trim-tools/dotnet/dotnet"; fi
feed="${PACKAGE_FEED:-$root/build-packages/feed}"
proof="${PACKAGE_MODULE_PROOF_ROOT:-/tmp/dotnet2d-module-proof}"
[[ ! -e "$proof" ]] || { echo 'Choose a fresh PACKAGE_MODULE_PROOF_ROOT.' >&2; exit 2; }
mkdir -p "$proof/feed" "$proof/logs" "$proof/prerequisites" "$proof/publish"
cp "$feed"/*.nupkg "$proof/feed/"
source_cache="${PACKAGE_SOURCE_CACHE:-$root/../android-trim-tools/nuget}"
for package in microsoft.net.illink.tasks microsoft.dotnet.ilcompiler runtime.linux-x64.microsoft.dotnet.ilcompiler microsoft.netcore.app.runtime.linux-x64 microsoft.aspnetcore.app.runtime.linux-x64 microsoft.netcore.app.runtime.nativeaot.linux-x64; do
 cp "$source_cache/$package/10.0.12/$package.10.0.12.nupkg" "$proof/feed/"
done
cp -a packaging/consumers/modules "$proof/consumer"
cp -a packaging/inspect "$proof/inspect"
cp assets/regions.bmp "$proof/consumer/assets/regions.bmp"
python3 - "$proof" <<'PY'
from pathlib import Path
import sys,wave,struct
p=Path(sys.argv[1])
with wave.open(str(p/'consumer/assets/pcm.wav'),'wb') as output:
    output.setparams((2,2,48000,2048,'NONE','not compressed'))
    output.writeframes(struct.pack('<hh',8192,-8192)*2048)
p.joinpath('NuGet.Config').write_text('<configuration><packageSources><clear/><add key="local" value="'+str(p/'feed')+'"/></packageSources></configuration>')
p.joinpath('prerequisites/inprocess-illink.targets').write_text('''<Project>
 <UsingTask TaskName="ComputeManagedAssemblies" AssemblyFile="$(NuGetPackageRoot)/microsoft.net.illink.tasks/10.0.12/tools/net/ILLink.Tasks.dll" TaskFactory="AssemblyTaskFactory" Override="true" />
 <UsingTask TaskName="ILLink" AssemblyFile="$(NuGetPackageRoot)/microsoft.net.illink.tasks/10.0.12/tools/net/ILLink.Tasks.dll" TaskFactory="AssemblyTaskFactory" Override="true" />
</Project>''')
PY
export DOTNET_CLI_HOME="$proof/prerequisites/dotnet-home" NUGET_PACKAGES="$proof/nuget-cache" NUGET_HTTP_CACHE_PATH="$proof/http-cache"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_GENERATE_ASPNET_CERTIFICATE=false DOTNET_SKIP_FIRST_TIME_EXPERIENCE=true
unset GAL_ASSET_ROOT LD_LIBRARY_PATH LD_PRELOAD LD_AUDIT
project="$proof/consumer/Sample.csproj"
"$dotnet" restore "$project" --configfile "$proof/NuGet.Config" > "$proof/logs/modules-restore.log" 2>&1
(cd "$proof"; "$dotnet" run --project "$project" -c Release --no-restore -p:UseSharedCompilation=false) > "$proof/logs/modules-jit.log" 2>&1
cp -a "$proof/consumer/bin/Release/net10.0" "$proof/publish/modules-fdd"
"$dotnet" restore "$project" --configfile "$proof/NuGet.Config" -r linux-x64 -p:PublishAot=true -p:RuntimeFrameworkVersion=10.0.12 > "$proof/logs/modules-aot-restore.log" 2>&1
LD_LIBRARY_PATH="$root/.tools/aot/usr/lib/x86_64-linux-gnu" "$dotnet" publish "$project" -c Release -r linux-x64 --no-restore -m:1 -nr:false -p:UseSharedCompilation=false -p:PublishAot=true -p:StripSymbols=true -p:IlcGenerateMapFile=true -p:RuntimeFrameworkVersion=10.0.12 -p:CppCompilerAndLinker="${AOT_CXX:-$root/.tools/aot/usr/bin/clang-19}" -p:CustomAfterMicrosoftCommonTargets="$proof/prerequisites/inprocess-illink.targets" -o "$proof/publish/modules-aot" > "$proof/logs/modules-aot-publish.log" 2>&1
(cd "$proof"; "$proof/publish/modules-aot/Sample.modules") > "$proof/logs/modules-aot.log" 2>&1
cp "$proof/consumer/obj/Release/net10.0/linux-x64/native/Sample.modules.map.xml" "$proof/logs/modules-aot-map.xml"

for negative in forged nulls immutable; do
 mkdir -p "$proof/$negative"
 cp packaging/consumers/modules/Sample.csproj "$proof/$negative/Sample.csproj"
 cp "packaging/negative/modules-$negative.cs" "$proof/$negative/Program.cs"
 "$dotnet" restore "$proof/$negative/Sample.csproj" --configfile "$proof/NuGet.Config" > "$proof/logs/$negative-restore.log" 2>&1
 if "$dotnet" build "$proof/$negative/Sample.csproj" --no-restore -p:UseSharedCompilation=false > "$proof/logs/$negative-build.log" 2>&1; then echo "Unexpected compile success: $negative" >&2; exit 1; fi
done
for owner in AudioSession AudioClip AudioVoice PhysicsWorld PhysicsBody PhysicsShape LoadedTileMap; do
 rg -q "CS1729.*$owner" "$proof/logs/forged-build.log"
done
rg -q 'CS8625' "$proof/logs/nulls-build.log"
rg -q 'CS0200' "$proof/logs/immutable-build.log"
rg -q 'CS8852' "$proof/logs/immutable-build.log"
rg -q 'CS0122.*PhysicsNative' "$proof/logs/immutable-build.log"
rg -q 'CS0122.*TileMapJsonContext' "$proof/logs/immutable-build.log"

# Recheck three minimal trimmed graphs after public visibility changes. Their
# full three-mode renderer matrix was already measured in the preceding batch.
for sample in empty sprite ui; do
 cp -a "packaging/consumers/$sample" "$proof/$sample"
 if [[ "$sample" == sprite ]]; then cp assets/regions.bmp "$proof/$sample/assets/regions.bmp"; fi
 project="$proof/$sample/Sample.csproj"
 "$dotnet" restore "$project" --configfile "$proof/NuGet.Config" -r linux-x64 -p:SelfContained=true -p:PublishTrimmed=true -p:RuntimeFrameworkVersion=10.0.12 > "$proof/logs/$sample-trim-restore.log" 2>&1
 "$dotnet" publish "$project" -c Release -r linux-x64 --self-contained true --no-restore -m:1 -nr:false -p:UseSharedCompilation=false -p:PublishTrimmed=true -p:RuntimeFrameworkVersion=10.0.12 -p:CustomAfterMicrosoftCommonTargets="$proof/prerequisites/inprocess-illink.targets" -o "$proof/publish/$sample-trim" > "$proof/logs/$sample-trim-publish.log" 2>&1
done
"$dotnet" restore "$proof/inspect/PackageInspect.csproj" --configfile "$proof/NuGet.Config" > "$proof/logs/inspect.log" 2>&1
"$dotnet" run --project "$proof/inspect/PackageInspect.csproj" -c Release --no-restore -- "$proof/publish/empty-trim/Dotnet2D.Engine.dll" "$proof/publish/sprite-trim/Dotnet2D.Engine.dll" "$proof/publish/ui-trim/Dotnet2D.Engine.dll" "$proof/publish/modules-fdd/Dotnet2D.Engine.dll" > "$proof/logs/managed-types.jsonl"
python3 scripts/measure-package-modules.py "$proof"
cat "$proof/logs/modules-jit.log" "$proof/logs/modules-aot.log"
echo "PACKAGE MODULE PROOF PASS $proof"
