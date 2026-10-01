#!/usr/bin/env bash
# Local-feed proof only. Does not install dependencies or publish packages remotely.
set -euo pipefail
cd "$(dirname "$0")/.."
root="$PWD"
if [[ -n "${DOTNET:-}" ]]; then dotnet="$DOTNET"
elif command -v dotnet >/dev/null 2>&1; then dotnet="$(command -v dotnet)"
else dotnet="$root/../android-trim-tools/dotnet/dotnet"; fi
source_cache="${PACKAGE_SOURCE_CACHE:-$root/../android-trim-tools/nuget}"
feed="${PACKAGE_FEED:-$root/build-packages/feed}"
proof="${PACKAGE_PROOF_ROOT:-${TMPDIR:-/tmp}/dotnet2d-package-proof}"
[[ -f "$feed/Dotnet2D.Engine.0.1.0-preview.1.nupkg" && -f "$feed/Dotnet2D.Native.Linux.x64.0.1.0-preview.1.nupkg" ]] || { echo 'Build both local packages first.' >&2; exit 2; }
[[ ! -e "$proof" ]] || { echo "Proof directory already exists; choose a new PACKAGE_PROOF_ROOT: $proof" >&2; exit 2; }
mkdir -p "$proof/feed" "$proof/consumers" "$proof/logs" "$proof/prerequisites" "$proof/publish"
cp "$feed"/*.nupkg "$proof/feed/"
# Existing official cache only: seed the host tool/runtime packs into the isolated local feed.
for package in microsoft.net.illink.tasks microsoft.dotnet.ilcompiler runtime.linux-x64.microsoft.dotnet.ilcompiler microsoft.netcore.app.runtime.linux-x64 microsoft.aspnetcore.app.runtime.linux-x64 microsoft.netcore.app.runtime.nativeaot.linux-x64; do
 path="$source_cache/$package/10.0.12/$package.10.0.12.nupkg"
 [[ -f "$path" ]] || { echo "Missing already-installed tool/runtime pack: $path" >&2; exit 2; }
 cp "$path" "$proof/feed/"
done
cp -a packaging/consumers/. "$proof/consumers/"
cp assets/regions.bmp "$proof/consumers/sprite/assets/regions.bmp"
cp -a packaging/inspect "$proof/inspect"
# The runner's software driver is a host prerequisite, not package content. Keep its
# source workspace out of runtime lookup so it cannot accidentally supply libgal/SDL.
if [[ -z "${PACKAGE_GRAPHICS_LIBDIR:-}" ]]; then
 cp -a .deps/graphics-sysroot "$proof/prerequisites/graphics"
 graphics="$proof/prerequisites/graphics/usr/lib/x86_64-linux-gnu"
 export VK_ICD_FILENAMES="$proof/prerequisites/graphics/usr/share/vulkan/icd.d/lvp_icd.json"
else graphics="$PACKAGE_GRAPHICS_LIBDIR"; fi
export SDL_VIDEODRIVER="${SDL_VIDEODRIVER:-offscreen}" SDL_AUDIODRIVER="${SDL_AUDIODRIVER:-dummy}"
export MESA_SHADER_CACHE_DIR="$proof/prerequisites/mesa-cache"
export LD_LIBRARY_PATH="$graphics"
export DOTNET_CLI_HOME="$proof/prerequisites/dotnet-home" NUGET_PACKAGES="$proof/nuget-cache" NUGET_HTTP_CACHE_PATH="$proof/http-cache"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_GENERATE_ASPNET_CERTIFICATE=false DOTNET_SKIP_FIRST_TIME_EXPERIENCE=true
unset GAL_ASSET_ROOT
python3 - "$proof" <<'PY'
from pathlib import Path
import sys
p=Path(sys.argv[1]);p.joinpath('NuGet.Config').write_text('<configuration><packageSources><clear/><add key="local-proof" value="'+str(p/'feed')+'"/></packageSources></configuration>')
# This process cannot use the SDK task-host's AF_UNIX sockets. The original
# Microsoft task assembly executes in-process; no SDK sources/binaries are patched.
p.joinpath('prerequisites/inprocess-illink.targets').write_text('''<Project>
  <UsingTask TaskName="ComputeManagedAssemblies" AssemblyFile="$(NuGetPackageRoot)/microsoft.net.illink.tasks/10.0.12/tools/net/ILLink.Tasks.dll" TaskFactory="AssemblyTaskFactory" Override="true" />
  <UsingTask TaskName="ILLink" AssemblyFile="$(NuGetPackageRoot)/microsoft.net.illink.tasks/10.0.12/tools/net/ILLink.Tasks.dll" TaskFactory="AssemblyTaskFactory" Override="true" />
</Project>''')
PY
# Every consumer is now a copied ordinary SDK project with PackageReferences only.
for sample in empty sprite ui; do
 project="$proof/consumers/$sample/Sample.csproj"
 "$dotnet" restore "$project" --configfile "$proof/NuGet.Config" > "$proof/logs/$sample-restore.log" 2>&1
 (cd "$proof"; "$dotnet" run --project "$project" -c Release --no-restore -p:UseSharedCompilation=false) > "$proof/logs/$sample-run.log" 2>&1
 if [[ "$sample" == ui ]]; then cp "$proof/package-ui.bmp" "$proof/logs/ui-fdd.bmp"; fi
 cp -a "$proof/consumers/$sample/bin/Release/net10.0" "$proof/publish/$sample-fdd"
done
for sample in empty sprite ui; do
 project="$proof/consumers/$sample/Sample.csproj"
 "$dotnet" restore "$project" --configfile "$proof/NuGet.Config" -r linux-x64 -p:SelfContained=true -p:PublishTrimmed=true -p:RuntimeFrameworkVersion=10.0.12 > "$proof/logs/$sample-trim-restore.log" 2>&1
 "$dotnet" publish "$project" -c Release -r linux-x64 --self-contained true --no-restore -m:1 -nr:false -p:UseSharedCompilation=false -p:PublishTrimmed=true -p:RuntimeFrameworkVersion=10.0.12 -p:CustomAfterMicrosoftCommonTargets="$proof/prerequisites/inprocess-illink.targets" -o "$proof/publish/$sample-trim" > "$proof/logs/$sample-trim-publish.log" 2>&1
 (cd "$proof"; "$proof/publish/$sample-trim/Sample.$sample") > "$proof/logs/$sample-trim-run.log" 2>&1
 if [[ "$sample" == ui ]]; then cp "$proof/package-ui.bmp" "$proof/logs/ui-trim.bmp"; fi
done
for sample in empty sprite ui; do
 project="$proof/consumers/$sample/Sample.csproj"
 "$dotnet" restore "$project" --configfile "$proof/NuGet.Config" -r linux-x64 -p:PublishAot=true -p:RuntimeFrameworkVersion=10.0.12 > "$proof/logs/$sample-aot-restore.log" 2>&1
 # Compiler/LLVM are build-host tools, explicitly passed; they do not enter the package.
 compiler="${AOT_CXX:-$root/.tools/aot/usr/bin/clang-19}"
 LD_LIBRARY_PATH="$root/.tools/aot/usr/lib/x86_64-linux-gnu:$graphics" "$dotnet" publish "$project" -c Release -r linux-x64 --no-restore -m:1 -nr:false -p:UseSharedCompilation=false -p:PublishAot=true -p:StripSymbols=true -p:IlcGenerateMapFile=true -p:RuntimeFrameworkVersion=10.0.12 -p:CppCompilerAndLinker="$compiler" -p:CustomAfterMicrosoftCommonTargets="$proof/prerequisites/inprocess-illink.targets" -o "$proof/publish/$sample-aot" > "$proof/logs/$sample-aot-publish.log" 2>&1
 (cd "$proof"; "$proof/publish/$sample-aot/Sample.$sample") > "$proof/logs/$sample-aot-run.log" 2>&1
 if [[ "$sample" == ui ]]; then cp "$proof/package-ui.bmp" "$proof/logs/ui-aot.bmp"; fi
 cp "$proof/consumers/$sample/obj/Release/net10.0/linux-x64/native/Sample.$sample.map.xml" "$proof/logs/$sample-aot-map.xml"
 echo "PACKAGE MODES PASS $sample"
done
# Public wrappers must not let consumers manufacture cache/lease ownership.
mkdir -p "$proof/negative-cache"
cp "$proof/consumers/empty/Sample.csproj" "$proof/negative-cache/Sample.csproj"
cat > "$proof/negative-cache/Program.cs" <<'CS'
using GameAuthoringLab;
using var engine=EngineHost.Create();
var cache=new TextureCache(engine);
var lease=new TextureLease(engine.Textures,"fake",0,default);
CS
"$dotnet" restore "$proof/negative-cache/Sample.csproj" --configfile "$proof/NuGet.Config" > "$proof/logs/negative-cache-restore.log" 2>&1
if "$dotnet" build "$proof/negative-cache/Sample.csproj" --no-restore -p:UseSharedCompilation=false > "$proof/logs/negative-cache-build.log" 2>&1; then
 echo 'Public cache/lease construction was unexpectedly accepted.' >&2; exit 1
fi
rg -q "CS1729.*TextureCache" "$proof/logs/negative-cache-build.log"
rg -q "CS1729.*TextureLease" "$proof/logs/negative-cache-build.log"
"$dotnet" restore "$proof/inspect/PackageInspect.csproj" --configfile "$proof/NuGet.Config" > "$proof/logs/inspect-build.log" 2>&1
"$dotnet" run --project "$proof/inspect/PackageInspect.csproj" -c Release --no-restore -- "$proof/publish/empty-trim/Dotnet2D.Engine.dll" "$proof/publish/sprite-trim/Dotnet2D.Engine.dll" "$proof/publish/ui-trim/Dotnet2D.Engine.dll" > "$proof/logs/managed-types.jsonl"
python3 "$root/scripts/measure-packages.py" "$proof"
echo "PACKAGE PROOF PASS $proof"
