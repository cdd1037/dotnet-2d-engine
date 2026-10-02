#!/usr/bin/env bash
# Focused independent PackageReference proof; no broad timing benchmark or native rebuild.
set -euo pipefail
cd "$(dirname "$0")/.."
root="$PWD"
dotnet="${DOTNET:-$root/.tools/dotnet/dotnet}"
feed="${PACKAGE_FEED:-$root/build-packages/feed}"
proof="${PACKAGE_UI_ERGONOMICS_PROOF_ROOT:-/tmp/dotnet2d-ui-ergonomics-proof}"
cache="${PACKAGE_SOURCE_CACHE:-$root/.tools/nuget}"
[[ ! -e "$proof" ]] || { echo 'Choose a fresh PACKAGE_UI_ERGONOMICS_PROOF_ROOT.' >&2; exit 2; }
mkdir -p "$proof/feed" "$proof/logs" "$proof/prerequisites" "$proof/publish" "$proof/captures"
cp "$feed"/Dotnet2D.Engine.*.nupkg "$feed"/Dotnet2D.Native.Linux.x64.*.nupkg "$proof/feed/"
for package in microsoft.net.illink.tasks microsoft.dotnet.ilcompiler runtime.linux-x64.microsoft.dotnet.ilcompiler microsoft.netcore.app.runtime.linux-x64 microsoft.aspnetcore.app.runtime.linux-x64 microsoft.netcore.app.runtime.nativeaot.linux-x64; do
 cp "$cache/$package/10.0.12/$package.10.0.12.nupkg" "$proof/feed/"
done
cp -a packaging/consumers/ui-ergonomics "$proof/consumer"
rm -rf "$proof/consumer/bin" "$proof/consumer/obj"
python3 - "$proof" <<'PY'
from pathlib import Path
from xml.sax.saxutils import escape
import sys
p=Path(sys.argv[1])
p.joinpath('NuGet.Config').write_text('<configuration><packageSources><clear/><add key="local" value="'+escape(str(p/'feed'), {'"': '&quot;'})+'"/></packageSources></configuration>')
p.joinpath('prerequisites/inprocess-illink.targets').write_text('''<Project>
 <UsingTask TaskName="ComputeManagedAssemblies" AssemblyFile="$(NuGetPackageRoot)/microsoft.net.illink.tasks/10.0.12/tools/net/ILLink.Tasks.dll" TaskFactory="AssemblyTaskFactory" Override="true" />
 <UsingTask TaskName="ILLink" AssemblyFile="$(NuGetPackageRoot)/microsoft.net.illink.tasks/10.0.12/tools/net/ILLink.Tasks.dll" TaskFactory="AssemblyTaskFactory" Override="true" />
</Project>''')
PY
export DOTNET_CLI_HOME="$proof/prerequisites/dotnet-home" NUGET_PACKAGES="$proof/nuget-cache" NUGET_HTTP_CACHE_PATH="$proof/http-cache"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_GENERATE_ASPNET_CERTIFICATE=false DOTNET_SKIP_FIRST_TIME_EXPERIENCE=true
unset GAL_ASSET_ROOT LD_LIBRARY_PATH LD_PRELOAD LD_AUDIT
project="$proof/consumer/Sample.csproj"
"$dotnet" restore "$project" --configfile "$proof/NuGet.Config" > "$proof/logs/jit-restore.log" 2>&1
"$dotnet" publish "$project" -c Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false -o "$proof/publish/jit" > "$proof/logs/jit-publish.log" 2>&1
python3 "$root/scripts/test-ui-contract-edits.py" "$proof/consumer" "$proof/logs/edits"
"$dotnet" restore "$project" --configfile "$proof/NuGet.Config" -r linux-x64 -p:PublishAot=true -p:RuntimeFrameworkVersion=10.0.12 > "$proof/logs/aot-restore.log" 2>&1
LD_LIBRARY_PATH="${AOT_LIBRARY_PATH:-$root/.tools/aot/usr/lib/x86_64-linux-gnu}" "$dotnet" publish "$project" -c Release -r linux-x64 --no-restore -m:1 -nr:false -p:UseSharedCompilation=false -p:PublishAot=true -p:StripSymbols=true -p:RuntimeFrameworkVersion=10.0.12 -p:CppCompilerAndLinker="${AOT_CXX:-$root/.tools/aot/usr/bin/clang-19}" -p:CustomAfterMicrosoftCommonTargets="$proof/prerequisites/inprocess-illink.targets" -o "$proof/publish/aot" > "$proof/logs/aot-publish.log" 2>&1
export SDL_VIDEODRIVER=offscreen SDL_AUDIODRIVER=dummy
export VK_ICD_FILENAMES="${VK_ICD_FILENAMES:-$root/.deps/graphics-sysroot/usr/share/vulkan/icd.d/lvp_icd.json}"
export LD_LIBRARY_PATH="${GRAPHICS_LIBRARY_PATH:-$root/.deps/graphics-sysroot/usr/lib/x86_64-linux-gnu}"
export GAL_UI_FONT="${GAL_UI_FONT:-/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc}"
for mode in jit aot; do
 export GAL_UI_ERGONOMICS_OUTPUT="$proof/captures/$mode" MESA_SHADER_CACHE_DIR="$proof/mesa-cache-$mode"
 if [[ "$mode" == jit ]]; then command=("$dotnet" "$proof/publish/jit/Sample.ui-ergonomics.dll"); else command=("$proof/publish/aot/Sample.ui-ergonomics"); fi
 (cd "$proof"; "${command[@]}" --check) > "$proof/logs/$mode-run.log" 2>&1
 grep -E 'PASS|NATIVE_LOADED' "$proof/logs/$mode-run.log"
done
python3 "$root/scripts/verify-ui-ergonomics-package.py" "$proof"
echo "PACKAGE UI ERGONOMICS PROOF PASS $proof"
