#!/usr/bin/env bash
# RELAY development experiment outside checkout. JIT by default; full is opt-in.
set -euo pipefail
cd "$(dirname "$0")/.."
root="$PWD"
tier="${1:-jit}"
[[ "$tier" == jit || "$tier" == full ]] || { echo 'Usage: test-relay-packages.sh [jit|full]' >&2; exit 2; }
modes=(jit)
[[ "$tier" != full ]] || modes+=(trim aot)
dotnet="${DOTNET:-$(command -v dotnet || true)}"
[[ -x "$dotnet" ]] || { echo 'Set DOTNET to an existing .NET 10 SDK.' >&2; exit 2; }
dotnet="$(realpath "$dotnet")"
feed="$(realpath "${PACKAGE_FEED:-$root/build-packages/relay-feed}")"
: "${GAL_UI_FONT:?Set GAL_UI_FONT to a readable licensed Noto Sans CJK SC font.}"
[[ -r "$GAL_UI_FONT" ]] || { echo 'GAL_UI_FONT is not readable.' >&2; exit 2; }
font="$(realpath "$GAL_UI_FONT")"
cache="${PACKAGE_SOURCE_CACHE:-$root/.tools/nuget}"
proof="$(realpath -m "${RELAY_PROOF_ROOT:-${TMPDIR:-/tmp}/dotnet2d-relay-proof}")"
[[ ! -e "$proof" ]] || { echo 'Choose a fresh RELAY_PROOF_ROOT.' >&2; exit 2; }
python3 - "$proof" "$root" <<'PY'
from pathlib import Path
import sys
p, root=map(Path,sys.argv[1:]); assert p!=root and root not in p.parents, 'RELAY_PROOF_ROOT must be outside checkout'
PY
mkdir -p "$proof/feed" "$proof/logs" "$proof/prerequisites" "$proof/publish" "$proof/captures" "$proof/unrelated-cwd"
cp "$feed"/Dotnet2D.{Engine,Native.Linux.x64}.0.1.0-preview.1.nupkg "$proof/feed/"
packages=(microsoft.net.illink.tasks)
[[ "$tier" != full ]] || packages+=(microsoft.dotnet.ilcompiler runtime.linux-x64.microsoft.dotnet.ilcompiler microsoft.netcore.app.runtime.linux-x64 microsoft.aspnetcore.app.runtime.linux-x64 microsoft.netcore.app.runtime.nativeaot.linux-x64)
for package in "${packages[@]}"; do
 path="$cache/$package/10.0.12/$package.10.0.12.nupkg"
 [[ -f "$path" ]] || { echo "Missing existing tool/runtime pack: $path" >&2; exit 2; }
 cp "$path" "$proof/feed/"
done
cp -a packaging/consumers/relay "$proof/app"
rm -rf "$proof/app/bin" "$proof/app/obj"
# Copy the existing host graphics prerequisite, not an engine/native source tree.
# Dereference the top-level convenience link only; retain normal internal SONAME links.
cp -a "$(realpath "${RELAY_GRAPHICS_SYSROOT:-$root/.deps/graphics-sysroot}")" "$proof/prerequisites/graphics"
# This is a local verification copy of an installed font, never a redistributable asset.
cp "$font" "$proof/prerequisites/$(basename "$font")"
export GAL_UI_FONT="$proof/prerequisites/$(basename "$font")"
python3 - "$proof" <<'PY'
from pathlib import Path
from xml.sax.saxutils import quoteattr
import sys
p=Path(sys.argv[1])
(p/'NuGet.Config').write_text('<configuration><packageSources><clear/><add key="local" value='+quoteattr(str(p/'feed'))+'/></packageSources></configuration>')
(p/'prerequisites/inprocess-illink.targets').write_text('''<Project>
 <UsingTask TaskName="ComputeManagedAssemblies" AssemblyFile="$(NuGetPackageRoot)/microsoft.net.illink.tasks/10.0.12/tools/net/ILLink.Tasks.dll" TaskFactory="AssemblyTaskFactory" Override="true" />
 <UsingTask TaskName="ILLink" AssemblyFile="$(NuGetPackageRoot)/microsoft.net.illink.tasks/10.0.12/tools/net/ILLink.Tasks.dll" TaskFactory="AssemblyTaskFactory" Override="true" />
</Project>''')
PY
export DOTNET_CLI_HOME="$proof/prerequisites/dotnet-home" NUGET_PACKAGES="$proof/nuget-cache" NUGET_HTTP_CACHE_PATH="$proof/http-cache" NUGET_SCRATCH="$proof/nuget-scratch"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_GENERATE_ASPNET_CERTIFICATE=false DOTNET_SKIP_FIRST_TIME_EXPERIENCE=true
unset GAL_ASSET_ROOT LD_LIBRARY_PATH LD_PRELOAD LD_AUDIT
project="$proof/app/Sample.csproj"
"$dotnet" --info > "$proof/logs/dotnet-info.log"
"$dotnet" restore "$project" --configfile "$proof/NuGet.Config" > "$proof/logs/jit-restore.log" 2>&1
"$dotnet" build "$project" -c Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false > "$proof/logs/jit-build.log" 2>&1
cp -a "$proof/app/bin/Release/net10.0" "$proof/publish/jit"
for mode in "${modes[@]:1}"; do
 args=(-r linux-x64 -p:RuntimeFrameworkVersion=10.0.12)
 if [[ "$mode" == trim ]]; then args+=(-p:PublishTrimmed=true -p:SelfContained=true)
 else args+=(-p:PublishAot=true -p:StripSymbols=true -p:IlcGenerateMapFile=true -p:CppCompilerAndLinker="${AOT_CXX:-$root/.tools/aot/usr/bin/clang-19}"); fi
 "$dotnet" restore "$project" --configfile "$proof/NuGet.Config" "${args[@]}" > "$proof/logs/$mode-restore.log" 2>&1
 LD_LIBRARY_PATH="${AOT_LIBRARY_PATH:-$root/.tools/aot/usr/lib/x86_64-linux-gnu}" "$dotnet" publish "$project" -c Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false "${args[@]}" -p:CustomAfterMicrosoftCommonTargets="$proof/prerequisites/inprocess-illink.targets" -o "$proof/publish/$mode" > "$proof/logs/$mode-publish.log" 2>&1
 done
[[ "$tier" != full ]] || cp "$proof/app/obj/Release/net10.0/linux-x64/native/Relay.map.xml" "$proof/logs/aot-map.xml"
export SDL_VIDEODRIVER=offscreen SDL_AUDIODRIVER=dummy
export VK_ICD_FILENAMES="$proof/prerequisites/graphics/usr/share/vulkan/icd.d/lvp_icd.json"
export LD_LIBRARY_PATH="$proof/prerequisites/graphics/usr/lib/x86_64-linux-gnu"
for mode in "${modes[@]}"; do
 export RELAY_CAPTURE_DIR="$proof/captures/$mode" MESA_SHADER_CACHE_DIR="$proof/mesa-cache-$mode"
 if [[ "$mode" == jit ]]; then command=("$dotnet" "$proof/publish/$mode/Relay.dll"); else command=("$proof/publish/$mode/Relay"); fi
 (cd "$proof/unrelated-cwd"; "${command[@]}" --check) > "$proof/logs/$mode-run.log" 2>&1
 grep 'RELAY .*PASS' "$proof/logs/$mode-run.log"
 # Startup diagnostics run against the actual published app in each mode.
 if (cd "$proof/unrelated-cwd"; GAL_UI_FONT=/nonexistent/relay-font "${command[@]}" --frames 1) > "$proof/logs/$mode-no-font.log" 2>&1; then echo 'Missing font unexpectedly accepted' >&2; exit 1; fi
 grep -q 'RELAY font: set GAL_UI_FONT' "$proof/logs/$mode-no-font.log"
 mv "$proof/publish/$mode/assets/room-b.png" "$proof/publish/$mode/assets/room-b.png.held"
 if (cd "$proof/unrelated-cwd"; "${command[@]}" --frames 1) > "$proof/logs/$mode-no-asset.log" 2>&1; then echo 'Missing asset unexpectedly accepted' >&2; exit 1; fi
 mv "$proof/publish/$mode/assets/room-b.png.held" "$proof/publish/$mode/assets/room-b.png"
 grep -q "room-b.png" "$proof/logs/$mode-no-asset.log"
done
python3 scripts/verify-relay-proof.py "$proof"
echo "RELAY PACKAGE PROOF PASS $proof"
