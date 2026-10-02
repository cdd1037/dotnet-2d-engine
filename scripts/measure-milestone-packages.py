#!/usr/bin/env python3
"""Four local-only publishes: Sprite/UI x trimmed JIT/NativeAOT, with identity guards.

Uses existing SDK/tool/runtime packs. No downloads, installs, or remote publishing.
The optional SVG DSO is reported separately; it never replaces package payloads.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import xml.etree.ElementTree as ET
from zipfile import ZipFile


ROOT = Path(__file__).resolve().parent.parent
VERSION = "0.1.0-preview.1"
RUNTIME = "10.0.12"
PACKS = ("microsoft.net.illink.tasks", "microsoft.dotnet.ilcompiler",
         "runtime.linux-x64.microsoft.dotnet.ilcompiler", "microsoft.netcore.app.runtime.linux-x64",
         "microsoft.aspnetcore.app.runtime.linux-x64", "microsoft.netcore.app.runtime.nativeaot.linux-x64")
UNUSED = ("AudioSession", "PhysicsWorld", "PhysicsBody", "FrameClip", "FramePlayer", "Tween",
          "TimingScope", "EngineTimer", "TileMap", "TileMapInstance", "TileMapCollision",
          "FramebufferClip", "ClippingNative", "DiagnosticLog", "CpuTimings", "DebugDrawBuffer",
          "MaterialCache", "MaterialLease", "MaterialAsset", "MaterialJsonContext", "MaterialDraw",
          "MaterialParameters", "MaterialNative", "RenderTargetStore", "RenderTarget", "RenderPass", "TargetNative")
SAMPLE_ONLY = ("MissionGame", "RoomGame", "Program", "SelfTests", "TileMovementClock", "TileMovementLevel", "TileMovementDemo")
BASELINE = {"sprite-trim": 34127881, "sprite-aot": 12515604,
            "ui-trim": 34562636, "ui-aot": 13045585}


def sha(data):
    return hashlib.sha256(data).hexdigest()


def describe(path):
    return {"bytes": path.stat().st_size, "sha256": sha(path.read_bytes())}


def git(*args):
    return subprocess.check_output(["git", *args], cwd=ROOT, text=True).strip()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--proof", type=Path, required=True, help="Fresh directory outside the checkout")
    parser.add_argument("--feed", type=Path, default=ROOT / "build-packages/feed")
    parser.add_argument("--source-cache", type=Path, default=ROOT.parent / "android-trim-tools/nuget")
    parser.add_argument("--dotnet", type=Path, default=Path(os.environ.get("DOTNET", ROOT.parent / "android-trim-tools/dotnet/dotnet")))
    parser.add_argument("--svg-library", type=Path, help="Optional current SVG-on DSO; report separately")
    args = parser.parse_args()
    proof, feed, cache, dotnet = (x.resolve() for x in (args.proof, args.feed, args.source_cache, args.dotnet))
    assert not proof.exists() and proof != ROOT and ROOT not in proof.parents, "Use a fresh external proof directory"
    for name in ("feed", "logs", "prerequisites", "publish", "consumers", "full"):
        (proof / name).mkdir(parents=True, exist_ok=True)
    env = os.environ.copy()
    for key in ("GAL_ASSET_ROOT", "LD_LIBRARY_PATH", "LD_PRELOAD", "LD_AUDIT"):
        env.pop(key, None)
    env.update(DOTNET_CLI_HOME=str(proof / "prerequisites/dotnet-home"), NUGET_PACKAGES=str(proof / "nuget-cache"),
               NUGET_HTTP_CACHE_PATH=str(proof / "http-cache"), DOTNET_CLI_TELEMETRY_OPTOUT="1",
               DOTNET_GENERATE_ASPNET_CERTIFICATE="false", DOTNET_SKIP_FIRST_TIME_EXPERIENCE="true",
               DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE="true")

    def run(command, log, cwd=proof, extra_env=None):
        print("RUN", log, flush=True)
        with (proof / "logs" / log).open("w") as output:
            subprocess.run([str(x) for x in command], cwd=cwd, env=env | (extra_env or {}),
                           stdout=output, stderr=subprocess.STDOUT, check=True)

    packages = {}
    for package_id in ("Dotnet2D.Engine", "Dotnet2D.Native.Linux.x64"):
        path = feed / f"{package_id}.{VERSION}.nupkg"
        shutil.copy2(path, proof / "feed")
        with ZipFile(path) as package:
            spec = ET.fromstring(package.read(next(n for n in package.namelist() if n.endswith(".nuspec"))))
            packages[package_id] = describe(path) | {"repository_commit": spec.find(".//{*}repository").attrib["commit"]}
            if package_id == "Dotnet2D.Engine":
                assembly = package.read("lib/net10.0/Dotnet2D.Engine.dll")
                managed_notice = package.read("LICENSE")
                assert package.read("buildTransitive/Dotnet2D.Engine.targets") == (ROOT / "engine/Dotnet2D.Engine.targets").read_bytes()
            else:
                manifest = json.loads(package.read("manifest.json"))
                native = {Path(n).name: package.read(n) for n in package.namelist()
                          if n.startswith("runtimes/linux-x64/native/") and not n.endswith("/")}
                native_notice = package.read("LICENSE.txt")
    assert manifest["profile"] == "graphics-ui-audio-physics" and manifest["font_bytes"] == 0
    assert manifest["repository_commit"] == packages["Dotnet2D.Native.Linux.x64"]["repository_commit"]
    for record in manifest["files"] + manifest["static_link_inputs"]:
        assert describe(Path(record["source_path"])) == {key: record[key] for key in ("bytes", "sha256")}, record
    assert native["libgal.so"] == (ROOT / "build-package/libgal.so").read_bytes()
    native_inputs = git("ls-files", "native", "CMakeLists.txt", "scripts/patches/patch-rmlui-svg.py").splitlines()
    stamp = packages["Dotnet2D.Native.Linux.x64"]["repository_commit"]
    for path in native_inputs:
        assert (ROOT / path).read_bytes() == subprocess.check_output(["git", "show", f"{stamp}:{path}"], cwd=ROOT), path
    cmake_cache = (ROOT / "build-package/CMakeCache.txt").read_text()
    for option, value in {"GAL_HEADLESS_ONLY": "OFF", "GAL_ENABLE_SVG": "OFF", "GAL_ENABLE_RMLUI": "ON",
                          "GAL_ENABLE_MIXER": "ON", "GAL_ENABLE_PHYSICS": "ON"}.items():
        assert re.search(rf"^{option}:\w+={value}$", cmake_cache, re.M), option
    # The feed may have been packed from uncommitted runtime changes. Do not trust
    # the NuGet source stamp alone: rebuild all managed inputs with that metadata
    # stamp, then require exact DLL bytes. A different artifact stops the proof.
    stamp = packages["Dotnet2D.Engine"]["repository_commit"]
    run([dotnet, "build", ROOT / "engine/Dotnet2D.Engine.csproj", "-t:Rebuild", "-c", "Release", "--no-restore",
         "-m:1", "-nr:false", "-p:UseSharedCompilation=false", f"-p:SourceRevisionId={stamp}"],
        "managed-identity-build.log", ROOT, {"NUGET_PACKAGES": str(cache)})
    assert assembly == (ROOT / "engine/bin/Release/net10.0/Dotnet2D.Engine.dll").read_bytes(), "Rebuilt engine differs from packaged DLL; repack first"
    (proof / "full/Dotnet2D.Engine.dll").write_bytes(assembly)
    managed_inputs = ["engine/Dotnet2D.Engine.csproj", "engine/RuntimeSources.props"]
    managed_inputs += [str((ROOT / "engine" / n.attrib["Include"].replace("$(MSBuildThisFileDirectory)", "")).resolve().relative_to(ROOT))
                       for n in ET.parse(ROOT / "engine/RuntimeSources.props").iter("EngineRuntimeSource")]
    sources = {name: sha((ROOT / name).read_bytes()) for name in sorted(set(managed_inputs + native_inputs))}
    identity = {"checkout_commit": git("rev-parse", "HEAD"), "packages": packages,
                "engine_assembly": {"bytes": len(assembly), "sha256": sha(assembly)},
                "managed_identity": "Forced rebuild with package SourceRevisionId is byte-identical",
                "native_identity": "Source files equal native package commit; DSO/static inputs equal manifest hashes",
                "source_files_sha256": sources, "source_set_sha256": sha(json.dumps(sources, sort_keys=True).encode()),
                "native_files": {name: {"bytes": len(data), "sha256": sha(data)} for name, data in native.items()}}
    (proof / "identity.json").write_text(json.dumps(identity, indent=2) + "\n")
    for package in PACKS:
        shutil.copy2(cache / package / RUNTIME / f"{package}.{RUNTIME}.nupkg", proof / "feed")
    (proof / "global.json").write_text(json.dumps({"sdk": {"version": "10.0.401", "rollForward": "disable"}}))
    config = ET.Element("configuration")
    sources_node = ET.SubElement(config, "packageSources")
    ET.SubElement(sources_node, "clear")
    ET.SubElement(sources_node, "add", key="local", value=str(proof / "feed"))
    ET.ElementTree(config).write(proof / "NuGet.Config")
    shim = proof / "prerequisites/inprocess-illink.targets"
    shim.write_text('<Project>' + ''.join(f'<UsingTask TaskName="{task}" AssemblyFile="$(NuGetPackageRoot)/microsoft.net.illink.tasks/{RUNTIME}/tools/net/ILLink.Tasks.dll" TaskFactory="AssemblyTaskFactory" Override="true" />' for task in ("ComputeManagedAssemblies", "ILLink")) + '</Project>')
    shutil.copytree(ROOT / "packaging/inspect", proof / "inspect", ignore=shutil.ignore_patterns("bin", "obj"))
    shutil.copytree(ROOT / ".deps/graphics-sysroot", proof / "prerequisites/graphics", symlinks=True)
    graphics = proof / "prerequisites/graphics/usr/lib/x86_64-linux-gnu"
    runtime_env = {"LD_LIBRARY_PATH": str(graphics), "SDL_VIDEODRIVER": "offscreen", "SDL_AUDIODRIVER": "dummy",
                   "VK_ICD_FILENAMES": str(proof / "prerequisites/graphics/usr/share/vulkan/icd.d/lvp_icd.json"),
                   "MESA_SHADER_CACHE_DIR": str(proof / "prerequisites/mesa-cache"),
                   "GAL_UI_FONT": os.environ.get("GAL_UI_FONT", "/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc")}
    assert Path(runtime_env["GAL_UI_FONT"]).is_file(), "Provide an external GAL_UI_FONT"
    run([dotnet, "--info"], "dotnet-info.log")
    for sample in ("sprite", "ui"):
        directory = proof / "consumers" / sample
        shutil.copytree(ROOT / "packaging/consumers" / sample, directory, ignore=shutil.ignore_patterns("bin", "obj"))
        if sample == "sprite":
            shutil.copy2(ROOT / "assets/regions.bmp", directory / "assets/regions.bmp")
        project = directory / "Sample.csproj"
        assert "ProjectReference" not in project.read_text() and "Compile Include" not in project.read_text()
        for mode in ("trim", "aot"):
            label = f"{sample}-{mode}"
            properties = [f"-p:RuntimeFrameworkVersion={RUNTIME}"]
            properties += ["-p:SelfContained=true", "-p:PublishTrimmed=true"] if mode == "trim" else ["-p:PublishAot=true"]
            run([dotnet, "restore", project, "--configfile", proof / "NuGet.Config", "-r", "linux-x64", *properties], f"{label}-restore.log")
            assert not any(v.get("type") == "project" for v in json.loads((directory / "obj/project.assets.json").read_text())["libraries"].values())
            options = [] if mode == "trim" else ["-p:StripSymbols=true", "-p:IlcGenerateMapFile=true", f"-p:CppCompilerAndLinker={os.environ.get('AOT_CXX', ROOT / '.tools/aot/usr/bin/clang-19')}"]
            run([dotnet, "publish", project, "-c", "Release", "-r", "linux-x64", "--no-restore", "-m:1", "-nr:false",
                 "-p:UseSharedCompilation=false", *properties, *options, f"-p:CustomAfterMicrosoftCommonTargets={shim}",
                 "-o", proof / "publish" / label], f"{label}-publish.log", extra_env={"LD_LIBRARY_PATH": str(ROOT / ".tools/aot/usr/lib/x86_64-linux-gnu")})
            run([proof / "publish" / label / f"Sample.{sample}"], f"{label}-run.log", extra_env=runtime_env)
            assert f"PACKAGE {sample.upper()} PASS" in (proof / "logs" / f"{label}-run.log").read_text()
            if sample == "ui":
                shutil.copy2(proof / "package-ui.bmp", proof / "logs" / f"{label}.bmp")
            if mode == "aot":
                shutil.copy2(directory / "obj/Release/net10.0/linux-x64/native" / f"Sample.{sample}.map.xml", proof / "logs" / f"{sample}-aot-map.xml")
    run([dotnet, "restore", proof / "inspect/PackageInspect.csproj", "--configfile", proof / "NuGet.Config"], "inspect-restore.log")
    run([dotnet, "run", "--project", proof / "inspect/PackageInspect.csproj", "-c", "Release", "--no-restore", "-p:UseSharedCompilation=false", "--",
         proof / "publish/sprite-trim/Dotnet2D.Engine.dll", proof / "publish/ui-trim/Dotnet2D.Engine.dll", proof / "full/Dotnet2D.Engine.dll"], "managed-types.jsonl")
    records = [json.loads(line) for line in (proof / "logs/managed-types.jsonl").read_text().splitlines() if line.startswith("{")]
    assert len(records) == 3 and all(r["exists"] for r in records)
    for name in UNUSED:
        assert "GameAuthoringLab." + name in records[2]["types"], ("missing full positive control", name)
    for name in SAMPLE_ONLY:
        assert "GameAuthoringLab." + name not in records[2]["types"], ("sample-only code in package", name)
    aot_roots = {}
    for sample, record in zip(("sprite", "ui"), records):
        for name in UNUSED + SAMPLE_ONLY:
            assert not any(t == "GameAuthoringLab." + name or t.startswith("GameAuthoringLab." + name + "`") for t in record["types"]), (sample, name)
        expected = "AuthoredScene" if sample == "sprite" else "BoundUiSession"
        assert any(t.startswith("GameAuthoringLab." + expected) for t in record["types"]), (sample, "missing managed root", expected)
        text = (proof / "logs" / f"{sample}-aot-map.xml").read_text()
        aot_roots[sample] = {name: "GameAuthoringLab_" + name in text or "GameAuthoringLab." + name in text for name in UNUSED + SAMPLE_ONLY + ("BoundUiSession", "AuthoredScene")}
        assert not any(aot_roots[sample][name] for name in UNUSED + SAMPLE_ONLY), sample
        assert aot_roots[sample][expected], (sample, "missing AOT root", expected)
        if sample == "sprite":
            assert not any("BoundUi" in t or "UiAuthoring" in t for t in record["types"])
            assert not aot_roots[sample]["BoundUiSession"]
    rows = []
    for label in BASELINE:
        sample = label.split("-")[0]
        directory = proof / "publish" / label
        groups = {name: [] for name in ("executable", "app_managed", "engine_managed", "native", "assets", "notices", "metadata", "symbols", "framework_other")}
        files = sorted(p for p in directory.rglob("*") if p.is_file())
        for path in files:
            relative = path.relative_to(directory)
            if path.suffix in (".pdb", ".dbg"): group = "symbols"
            elif path.name == f"Sample.{sample}": group = "executable"
            elif path.name == f"Sample.{sample}.dll": group = "app_managed"
            elif path.name == "Dotnet2D.Engine.dll": group = "engine_managed"
            elif path.name in native: group = "native"
            elif "assets" in relative.parts: group = "assets"
            elif "licenses" in relative.parts: group = "notices"
            elif path.name.endswith((".deps.json", ".runtimeconfig.json")): group = "metadata"
            else: group = "framework_other"
            groups[group].append(path)
        assert len(groups["native"]) == len(native) == 8
        assert all(p.read_bytes() == native[p.name] for p in groups["native"])
        assert len(groups["assets"]) == 2 and len(groups["notices"]) == 2
        assert (directory / "licenses/Dotnet2D.Engine/LICENSE.txt").read_bytes() == managed_notice
        assert (directory / "licenses/Dotnet2D.Native.Linux.x64/LICENSE.txt").read_bytes() == native_notice
        row = {"output": label} | {key + "_bytes": sum(p.stat().st_size for p in values) for key, values in groups.items()}
        row["distribution_without_symbols_bytes"] = sum(p.stat().st_size for p in files) - row["symbols_bytes"]
        row["delta_from_d07a6ab_bytes"] = row["distribution_without_symbols_bytes"] - BASELINE[label]
        row["files"] = {str(p.relative_to(directory)): describe(p) for p in files}
        rows.append(row)
        print(json.dumps({k: v for k, v in row.items() if k != "files"}), flush=True)
    assert (proof / "logs/ui-trim.bmp").read_bytes() == (proof / "logs/ui-aot.bmp").read_bytes()
    exports = subprocess.check_output(["nm", "-D", "--defined-only", proof / "publish/sprite-aot/libgal.so"], text=True)
    expected_exports = ("gal_audio_open", "gal_physics_open", "gal_submit_draws_clipped_v1", "gal_material_create_v1", "gal_target_create_v1", "gal_render_frame_v1")
    assert all(any(line.split()[-1].split("@")[0] == name for line in exports.splitlines()) for name in expected_exports)
    # Cache entries were freshly extracted from the isolated feed; compare them
    # as well, guarding NuGet's fixed-version reuse behavior on later reruns.
    for package_id, expected in packages.items():
        cached = proof / "nuget-cache" / package_id.lower() / VERSION / f"{package_id.lower()}.{VERSION}.nupkg"
        assert sha(cached.read_bytes()) == expected["sha256"]
    optional = None
    if args.svg_library:
        svg = args.svg_library.resolve()
        svg_cache = (svg.parent / "CMakeCache.txt").read_text()
        for key in ("CMAKE_BUILD_TYPE", "CMAKE_CXX_FLAGS", "CMAKE_CXX_FLAGS_RELEASE", "CMAKE_SHARED_LINKER_FLAGS", "CMAKE_BUILD_WITH_INSTALL_RPATH", "CMAKE_INSTALL_RPATH", "GAL_HEADLESS_ONLY", "GAL_ENABLE_RMLUI", "GAL_ENABLE_MIXER", "GAL_ENABLE_PHYSICS"):
            extract = lambda text: re.search(rf"^{key}:\w+=(.*)$", text, re.M).group(1)
            assert extract(cmake_cache) == extract(svg_cache), ("SVG comparison flags differ", key)
        assert re.search(r"^GAL_ENABLE_SVG:\w+=ON$", svg_cache, re.M)
        dynamic = lambda p: subprocess.check_output(["readelf", "-d", p], text=True)
        needed = lambda p: sorted(re.findall(r"\(NEEDED\).*?\[(.*?)\]", dynamic(p)))
        assert needed(svg) == needed(ROOT / "build-package/libgal.so")
        assert "Library rpath: [$ORIGIN]" in dynamic(svg)
        optional = describe(svg) | {"path": str(svg), "base_libgal_bytes": len(native["libgal.so"]),
                                  "libgal_delta_bytes": svg.stat().st_size - len(native["libgal.so"]),
                                  "same_dynamic_dependencies": True, "packaged": False,
                                  "svg_static_library": describe(ROOT / ".deps/svg-install/lib/liblunasvg.a")}
    warnings = {p.name: [line for line in p.read_text(errors="replace").splitlines() if re.search(r"\bwarning\s+[A-Z]+\d+\s*:", line)]
                for p in (proof / "logs").glob("*.log")}
    warnings = {name: lines for name, lines in warnings.items() if lines}
    report = {"sdk": "10.0.401", "runtime": RUNTIME, "publish_count": {"trimmed_jit": 2, "native_aot": 2},
              "identity": identity, "outputs": rows, "managed_types": records, "aot_engine_roots": aot_roots,
              "full_native_profile_exports": list(expected_exports), "ui_captures_identical": True,
              "external_font": runtime_env["GAL_UI_FONT"], "packaged_font_bytes": 0,
              "optional_svg": optional, "warnings": warnings,
              "scope": "Entire uncompressed publish directory, excluding .pdb/.dbg; native DSOs are unstripped; no ZIP size claim"}
    assert sources == {name: sha((ROOT / name).read_bytes()) for name in sources}, "Runtime sources changed during proof"
    (proof / "measurements.json").write_text(json.dumps(report, indent=2) + "\n")
    print("MILESTONE SMALL PACKAGE PROOF PASS", proof)


if __name__ == "__main__":
    main()
