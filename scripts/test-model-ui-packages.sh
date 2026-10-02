#!/usr/bin/env bash
# Independent local-feed consumers. No restore/publish output enters the checkout.
set -euo pipefail
cd "$(dirname "$0")/.."
root="$PWD"
dotnet="${DOTNET:-$root/../android-trim-tools/dotnet/dotnet}"
feed="${PACKAGE_FEED:-$root/build-packages/feed}"
proof="${PACKAGE_MODEL_UI_PROOF_ROOT:-/tmp/dotnet2d-model-ui-proof}"
cache="${PACKAGE_SOURCE_CACHE:-$root/../android-trim-tools/nuget}"
[[ ! -e "$proof" ]] || { echo 'Choose a fresh PACKAGE_MODEL_UI_PROOF_ROOT.' >&2; exit 2; }
mkdir -p "$proof/feed" "$proof/logs" "$proof/prerequisites" "$proof/publish" "$proof/captures"
cp "$feed"/Dotnet2D.Engine.*.nupkg "$feed"/Dotnet2D.Native.Linux.x64.*.nupkg "$proof/feed/"
for package in microsoft.net.illink.tasks microsoft.dotnet.ilcompiler runtime.linux-x64.microsoft.dotnet.ilcompiler microsoft.netcore.app.runtime.linux-x64 microsoft.aspnetcore.app.runtime.linux-x64 microsoft.netcore.app.runtime.nativeaot.linux-x64; do
 cp "$cache/$package/10.0.12/$package.10.0.12.nupkg" "$proof/feed/"
done
cp -a packaging/consumers/model-ui "$proof/consumer"
cp -a packaging/consumers/model-ui/benchmark "$proof/benchmark"
cp managed/UiModelExamples.cs "$proof/consumer/UiModelExamples.cs"
mkdir -p "$proof/consumer/assets/ui"
cp assets/ui/model-{inventory,dialogue,settings}.{rml,rcss} assets/ui/model-{blade,lantern}.bmp "$proof/consumer/assets/ui/"
python3 - "$proof" <<'PY'
from pathlib import Path
import sys
p=Path(sys.argv[1])
p.joinpath('NuGet.Config').write_text('<configuration><packageSources><clear/><add key="local" value="'+str(p/'feed')+'"/></packageSources></configuration>')
p.joinpath('prerequisites/inprocess-illink.targets').write_text('''<Project>
 <UsingTask TaskName="ComputeManagedAssemblies" AssemblyFile="$(NuGetPackageRoot)/microsoft.net.illink.tasks/10.0.12/tools/net/ILLink.Tasks.dll" TaskFactory="AssemblyTaskFactory" Override="true" />
 <UsingTask TaskName="ILLink" AssemblyFile="$(NuGetPackageRoot)/microsoft.net.illink.tasks/10.0.12/tools/net/ILLink.Tasks.dll" TaskFactory="AssemblyTaskFactory" Override="true" />
</Project>''')
PY
export DOTNET_CLI_HOME="$proof/prerequisites/dotnet-home" NUGET_PACKAGES="$proof/nuget-cache" NUGET_HTTP_CACHE_PATH="$proof/http-cache"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_GENERATE_ASPNET_CERTIFICATE=false DOTNET_SKIP_FIRST_TIME_EXPERIENCE=true
unset GAL_ASSET_ROOT LD_LIBRARY_PATH LD_PRELOAD LD_AUDIT
for sample in benchmark consumer; do
 project="$proof/$sample/Sample.csproj"
 "$dotnet" restore "$project" --configfile "$proof/NuGet.Config" > "$proof/logs/$sample-jit-restore.log" 2>&1
 "$dotnet" publish "$project" -c Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false -o "$proof/publish/$sample-jit" > "$proof/logs/$sample-jit-publish.log" 2>&1
 "$dotnet" restore "$project" --configfile "$proof/NuGet.Config" -r linux-x64 -p:PublishAot=true -p:RuntimeFrameworkVersion=10.0.12 > "$proof/logs/$sample-aot-restore.log" 2>&1
 LD_LIBRARY_PATH="$root/.tools/aot/usr/lib/x86_64-linux-gnu" "$dotnet" publish "$project" -c Release -r linux-x64 --no-restore -m:1 -nr:false -p:UseSharedCompilation=false -p:PublishAot=true -p:StripSymbols=true -p:IlcGenerateMapFile=true -p:RuntimeFrameworkVersion=10.0.12 -p:CppCompilerAndLinker="${AOT_CXX:-$root/.tools/aot/usr/bin/clang-19}" -p:CustomAfterMicrosoftCommonTargets="$proof/prerequisites/inprocess-illink.targets" -o "$proof/publish/$sample-aot" > "$proof/logs/$sample-aot-publish.log" 2>&1
 done
export SDL_VIDEODRIVER=offscreen SDL_AUDIODRIVER=dummy DOTNET_TieredCompilation=0
export VK_ICD_FILENAMES="${VK_ICD_FILENAMES:-$root/.deps/graphics-sysroot/usr/share/vulkan/icd.d/lvp_icd.json}"
export LD_LIBRARY_PATH="$root/.deps/graphics-sysroot/usr/lib/x86_64-linux-gnu"
export GAL_UI_FONT="${GAL_UI_FONT:-/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc}"
for mode in jit aot; do
 export GAL_MODEL_UI_CAPTURE_DIR="$proof/captures/$mode" MESA_SHADER_CACHE_DIR="$proof/mesa-cache-rich-$mode"
 if [[ "$mode" == jit ]]; then command=("$dotnet" "$proof/publish/consumer-jit/Sample.model-ui.dll");else command=("$proof/publish/consumer-aot/Sample.model-ui");fi
 (cd "$proof"; "${command[@]}") > "$proof/logs/consumer-$mode-run.log" 2>&1
 grep 'PACKAGE MODEL UI PASS' "$proof/logs/consumer-$mode-run.log"
 python3 "$root/scripts/validate-model-ui-pixels.py" > "$proof/logs/consumer-$mode-pixels.log"
 cat "$proof/logs/consumer-$mode-pixels.log"
done
unset GAL_MODEL_UI_CAPTURE_DIR
python3 "$root/packaging/consumers/model-ui/measure.py" "$root" "$proof" "$dotnet"
echo "PACKAGE MODEL UI PROOF PASS $proof"
