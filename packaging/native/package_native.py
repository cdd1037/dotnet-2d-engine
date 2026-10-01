#!/usr/bin/env python3
"""Stage the audited local native inputs and verify a packed NuGet extraction."""
import ctypes
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import zipfile


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def command(*args, **kwargs):
    return subprocess.check_output(args, text=True, **kwargs)


def describe(path, source):
    return {"source_path": str(source.resolve()), "bytes": path.stat().st_size,
            "sha256": digest(path)}


def system_versions():
    versions = {}
    for library, function, name in [
        ("libz.so.1", "zlibVersion", "zlib"),
        ("libbz2.so.1.0", "BZ2_bzlibVersion", "bzip2"),
        ("libpng16.so.16", "png_get_libpng_ver", "libpng"),
    ]:
        fn = getattr(ctypes.CDLL(library), function)
        fn.restype = ctypes.c_char_p
        versions[name] = (fn(None) if name == "libpng" else fn()).decode()
    ft = ctypes.CDLL("libfreetype.so.6")
    handle = ctypes.c_void_p()
    if ft.FT_Init_FreeType(ctypes.byref(handle)):
        raise RuntimeError("Cannot inspect the installed FreeType version")
    parts = [ctypes.c_int() for _ in range(3)]
    ft.FT_Library_Version(handle, *(ctypes.byref(p) for p in parts))
    ft.FT_Done_FreeType(handle)
    versions["FreeType"] = ".".join(str(p.value) for p in parts)
    fn = ctypes.CDLL("libbrotlidec.so.1").BrotliDecoderVersion
    fn.restype = ctypes.c_uint32
    n = fn()
    versions["Brotli"] = f"{n >> 24}.{(n >> 12) & 4095}.{n & 4095}"
    return versions


def stage(root, destination, version):
    root, destination = root.resolve(), destination.resolve()
    if destination != root / "build-packages/native-stage":
        raise RuntimeError("Staging is restricted to build-packages/native-stage")
    # This is this script's generated staging directory, never a dependency tree.
    if destination.exists():
        shutil.rmtree(destination)
    native = destination / "runtimes/linux-x64/native"
    licenses = destination / "licenses"
    native.mkdir(parents=True)
    licenses.mkdir()
    versions = system_versions()
    inputs = [
        ("libgal.so", root / "build-package/libgal.so", "Dotnet2D", version),
        ("libSDL3.so.0", root / ".deps/sdl-3.4.16-install/lib/libSDL3.so.0", "SDL3", "3.4.16"),
        ("libfreetype.so.6", Path("/usr/lib/x86_64-linux-gnu/libfreetype.so.6"), "FreeType", versions["FreeType"]),
        ("libz.so.1", Path("/usr/lib/x86_64-linux-gnu/libz.so.1"), "zlib", versions["zlib"]),
        ("libbz2.so.1.0", Path("/usr/lib/x86_64-linux-gnu/libbz2.so.1.0"), "bzip2", versions["bzip2"]),
        ("libpng16.so.16", Path("/usr/lib/x86_64-linux-gnu/libpng16.so.16"), "libpng", versions["libpng"]),
        ("libbrotlidec.so.1", Path("/usr/lib/x86_64-linux-gnu/libbrotlidec.so.1"), "Brotli", versions["Brotli"]),
        ("libbrotlicommon.so.1", Path("/usr/lib/x86_64-linux-gnu/libbrotlicommon.so.1"), "Brotli", versions["Brotli"]),
    ]
    files = []
    required_versions = {}
    for name, source, component, upstream_version in inputs:
        target = native / name
        shutil.copyfile(source, target)
        metadata = describe(target, source)
        metadata.update({"path": str(target.relative_to(destination)), "component": component,
                         "version": upstream_version,
                         "needed": re.findall(r"\(NEEDED\).*?\[(.*?)\]", command("readelf", "-d", str(target)))})
        files.append(metadata)
        elf_versions = command("readelf", "--version-info", str(target))
        for family in ["GLIBC", "GLIBCXX", "CXXABI", "GCC"]:
            found = re.findall(r"\b" + family + r"_([0-9.]+)", elf_versions)
            if found:
                required_versions[family] = max(found + [required_versions.get(family, "0")],
                                                key=lambda s: tuple(map(int, s.split("."))))

    notices = []

    def copy_notice(source, name):
        target = licenses / name
        shutil.copyfile(source, target)
        notices.append({"path": str(target.relative_to(destination)), **describe(target, source)})

    copy_notice(root / "LICENSE", "project-MIT.txt")
    for name in ["SDL3", "SDL3-IMAGE", "SDL3-MIXER", "STB-VORBIS", "BOX2D", "RMLUI"]:
        copy_notice(root / f"docs/{name}-LICENSE.txt", name + ".txt")
    rml = root / ".deps/RmlUi-ba95ffe8bfb6370efb2cdcca927eaad4710c5413"
    sdl = root / ".deps/SDL3-3.4.16"
    copy_notice(rml / "Include/RmlUi/Core/Containers/LICENSE.txt", "RmlUi-containers-MIT.txt")
    copy_notice(sdl / "src/hidapi/LICENSE-bsd.txt", "HIDAPI-BSD-selected.txt")
    copy_notice(sdl / "src/video/yuv2rgb/LICENSE", "yuv2rgb-BSD.txt")
    for package in ["libfreetype6", "libpng16-16t64", "libbz2-1.0", "libbrotli1", "zlib1g"]:
        copy_notice(Path("/usr/share/doc") / package / "copyright", package + "-copyright.txt")
    # Debian source-package notices reference these texts for build/contrib files.
    # Retaining them does not select GPL for FreeType (FTL) or HIDAPI (BSD).
    for name in ["Apache-2.0", "GPL-2", "GPL-3"]:
        copy_notice(Path("/usr/share/common-licenses") / name, name + ".txt")

    # Dependency files identify actual compiled sources and headers, including
    # SDL's embedded X11/XDG, qsort, fdlibm, Khronos, stb and image-format notices.
    comment_pattern = re.compile(r"/\*.*?\*/|(?m:^[ \t]*//[^\n]*(?:\n[ \t]*//[^\n]*)*)", re.S)
    component_sources = [
        ("SDL", sdl, root / ".deps/sdl-3.4.16-build"),
        ("SDL-image", root / ".deps/SDL3_image-3.2.4", root / ".deps/sdlimage-build"),
        ("RmlUi", rml, root / ".deps/rmlui-build"),
        ("SDL-mixer", root / ".deps/SDL3_mixer-3.2.4", root / ".deps/mixer-3.2.4-build"),
        ("Box2D", root / ".deps/box2d-3.1.1", root / ".deps/box2d-3.1.1-build"),
    ]
    for name, source_root, build_root in component_sources:
        sources = set()
        # Include precompiled-header dependency files and the engine's RmlUi
        # backend compilation inputs as well as ordinary object inputs.
        for depfile in [*build_root.rglob("*.d"), *(root / "build-package").rglob("*.d")]:
            for raw in re.findall(r"/[^\s\\]+", depfile.read_text()):
                source = Path(raw).resolve()
                if source.is_file() and source.is_relative_to(source_root):
                    sources.add(source)
        if not sources:
            raise RuntimeError(f"No compiled dependency inputs available for {name}; cannot collect notices")
        blocks = {}
        for source in sorted(sources):
            for block in comment_pattern.findall(source.read_text(errors="strict")):
                if re.search(r"copyright|permission (?:is |to )|public.domain|SPDX-License-Identifier", block, re.I):
                    blocks.setdefault(block, []).append(str(source.relative_to(root)))
        target = licenses / (name + "-compiled-source-notices.txt")
        text = "Complete legal comment blocks from compiled dependency source/header inputs.\n\n"
        for block, paths in blocks.items():
            text += "Sources: " + ", ".join(paths) + "\n" + block + "\n\n"
        target.write_text(text)
        notices.append({"path": str(target.relative_to(destination)), "bytes": target.stat().st_size,
                        "sha256": digest(target), "source_paths": [str(p) for p in sorted(sources)],
                        "unique_notice_blocks": len(blocks)})

    archive_specs = [
        ("SDL3", "3.4.16", ".deps/SDL3-3.4.16", "7322236cd12090c3eb40b9728be4d49c76f66ad17d04369584d4ecad5cf77c68"),
        ("RmlUi", "6.3 / ba95ffe8bfb6370efb2cdcca927eaad4710c5413", str(rml.relative_to(root)), "1541ef5577115e9368f8ed389b29f0925ef6572f326a33d378ea16c3cfa2cde8"),
        ("SDL_image", "3.2.4", ".deps/SDL3_image-3.2.4", "a725bd6d04261fdda0dd8d950659e1dc15a8065d025275ef460d32ae7dcfc182"),
        ("SDL_mixer", "3.2.4", ".deps/SDL3_mixer-3.2.4", "182a07c745375e113dc740d43964ff21b0be29f29f59876c4dbc4db3d32f6901"),
        ("Box2D", "3.1.1 / 8c661469c9507d3ad6fbd2fea3f1aa71669c2fe3", ".deps/box2d-3.1.1", "fb6ef914b50f4312d7d921a600eabc12318bb3c55a0b8c0b90608fa4488ef2e4"),
    ]
    static_specs = [("RmlUi", ".deps/rmlui-build/librmlui.a"),
                    ("SDL_image", ".deps/ui-install/lib/libSDL3_image.a"),
                    ("SDL_mixer", ".deps/mixer-3.2.4-install/lib/libSDL3_mixer.a"),
                    ("Box2D", ".deps/box2d-3.1.1-install/lib/libbox2d.a")]
    manifest = {
        "schema": 1, "package_id": "Dotnet2D.Native.Linux.x64", "package_version": version,
        "rid": "linux-x64", "profile": "graphics-ui-audio-physics", "font_bytes": 0,
        "repository_commit": command("git", "-C", str(root), "rev-parse", "HEAD").strip(),
        "build": {"type": "Release", "directory": str(root / "build-package"),
                  "rpath": "$ORIGIN", "elf_tag": "DT_RPATH", "new_dtags": False,
                  "license_collection": "Explicit upstream notices plus complete legal comment blocks from compiled dependency inputs"},
        "minimum_symbol_versions": required_versions,
        "native_payload_bytes": sum(f["bytes"] for f in files), "files": files,
        "static_link_inputs": [{"component": name, **describe(root / p, root / p)} for name, p in static_specs],
        "upstream_source_pins": [{"component": n, "version": v, "source_path": str(root / p),
                                  "pinned_archive_sha256": h} for n, v, p, h in archive_specs],
        "notices": notices,
        "excluded": ["fonts", "game assets", "libc", "libm", "libstdc++", "libgcc_s", "ELF loader", "Vulkan/graphics drivers", "X11/ALSA system libraries", "build tools"],
    }
    (destination / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
    shutil.copyfile(root / "packaging/native/README.md", destination / "README.md")
    preface = ("Dotnet2D native package license bundle\n\n"
               "Original engine: MIT. Dependencies retain their individual terms.\n"
               "HIDAPI: BSD alternative selected. stb: MIT alternative selected. FreeType: FTL.\n"
               "This software is based in part on the work of the FreeType Team.\n"
               "Portions are copyright 1996-2024 The FreeType Project (https://www.freetype.org).\n"
               "Debian source-package inventories also describe build/contrib files not shipped here.\n"
               "Their GPL references do not relicense the runtime libraries.\n\n")
    bundle = preface
    for notice in notices:
        bundle += "=" * 72 + "\n" + notice["path"] + "\n" + "=" * 72 + "\n"
        bundle += (destination / notice["path"]).read_text() + "\n\n"
    (destination / "LICENSE.txt").write_text(bundle)
    print(f"Staged {len(files)} DSOs, {manifest['native_payload_bytes']:,} bytes; {len(notices)} notice files")


def verify(package, report):
    clean_env = os.environ.copy()
    for variable in ["LD_LIBRARY_PATH", "LD_PRELOAD", "LD_AUDIT"]:
        clean_env.pop(variable, None)
    with tempfile.TemporaryDirectory(prefix="dotnet2d-native-proof-") as directory:
        extracted = Path(directory)
        with zipfile.ZipFile(package) as archive:
            archive.extractall(extracted)
        manifest = json.loads((extracted / "manifest.json").read_text())
        native = extracted / "runtimes/linux-x64/native"
        expected = {Path(f["path"]).name for f in manifest["files"]}
        assert len(expected) == 8 and {p.name for p in native.iterdir()} == expected
        for entry in manifest["files"]:
            local = extracted / entry["path"]
            assert local.stat().st_size == entry["bytes"] and digest(local) == entry["sha256"]
        for entry in manifest["notices"]:
            local = extracted / entry["path"]
            assert local.stat().st_size == entry["bytes"] and digest(local) == entry["sha256"]
        tags = command("readelf", "-d", str(native / "libgal.so"), env=clean_env)
        assert re.findall(r"\(RPATH\).*?\[(.*?)\]", tags) == ["$ORIGIN"], tags
        assert "(RUNPATH)" not in tags, tags
        for library in native.iterdir():
            dynamic = command("readelf", "-d", str(library), env=clean_env)
            for path in re.findall(r"\((?:RPATH|RUNPATH)\).*?\[(.*?)\]", dynamic):
                assert path == "$ORIGIN", (library, path)
        baseline = {"libc.so.6", "libm.so.6", "libstdc++.so.6", "libgcc_s.so.1"}
        resolution = command("ldd", "-r", str(native / "libgal.so"), env=clean_env)
        assert "not found" not in resolution and "undefined symbol" not in resolution, resolution
        resolved = {}
        for name, target in re.findall(r"^\s*(\S+)\s+=>\s+(\S+)", resolution, re.M):
            if name in expected:
                assert Path(target).parent == native, (name, target)
                resolved[name] = target
            else:
                assert name in baseline, ("Unexpected dependency", name, target)
        assert set(resolved) == expected - {"libgal.so"}, resolved
        command(sys.executable, "-c", "import ctypes,sys; ctypes.CDLL(sys.argv[1])", str(native / "libgal.so"), env=clean_env)
        result = {"package": str(package.resolve()), "package_bytes": package.stat().st_size,
                  "package_sha256": digest(package), "native_payload_bytes": manifest["native_payload_bytes"],
                  "verified": True, "extraction_outside_repository": True, "extraction_retained": False,
                  "ld_library_path": "unset", "rpath": "$ORIGIN (DT_RPATH)",
                  "all_seven_nonbaseline_dependencies_resolved_from_extraction": True,
                  "eager_relocations_and_native_load": "passed", "ldd_output": resolution}
        report.write_text(json.dumps(result, indent=2) + "\n")
        print(json.dumps({k: v for k, v in result.items() if k != "ldd_output"}, indent=2))


if __name__ == "__main__":
    if sys.argv[1] == "stage":
        stage(Path(sys.argv[2]), Path(sys.argv[3]), sys.argv[4])
    elif sys.argv[1] == "verify":
        verify(Path(sys.argv[2]), Path(sys.argv[3]))
    else:
        raise SystemExit("Expected stage or verify")
