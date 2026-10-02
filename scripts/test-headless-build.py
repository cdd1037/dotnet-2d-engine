#!/usr/bin/env python3
"""Opt-in incremental-build regression in a temporary source copy (Linux)."""

import os
from pathlib import Path
import shlex
import shutil
import subprocess
import tempfile
import time


ROOT = Path(__file__).resolve().parents[1]
TESTS = ("native_contract", "backend_contract", "c_abi_consumer",
         "input_state_contract", "text_input_geometry")


def main():
    cmake = shutil.which(os.environ.get("CMAKE", "cmake"))
    if not cmake:
        cmake = str(ROOT / ".tools/cmake-3.31.6-linux-x86_64/bin/cmake")
    compilers = {"gcc": shutil.which("gcc"), "g++": shutil.which("g++")}
    assert all(compilers.values()), "This regression requires GCC and G++"
    with tempfile.TemporaryDirectory(prefix="gal-headless-build-") as temporary:
        source = Path(temporary)
        shutil.copyfile(ROOT / "CMakeLists.txt", source / "CMakeLists.txt")
        for directory in ("native", "tests"):
            shutil.copytree(ROOT / directory, source / directory)
        for filename in ("scripts/build-headless.sh", "managed/Program.cs"):
            target = source / filename
            target.parent.mkdir(exist_ok=True)
            shutil.copyfile(ROOT / filename, target)
        wrappers = source / "compiler-bin"
        wrappers.mkdir()
        log = source / "compiler.log"
        for name, compiler in compilers.items():
            for suffix in ("", "-alternate"):
                wrapper = wrappers / (name + suffix)
                wrapper.write_text("#!/usr/bin/env bash\n"
                                   "printf -v args '%q ' \"$@\"\n"
                                   "printf '%s\\n' \"$args\" >> \"$GAL_COMPILER_LOG\"\n"
                                   f"exec {shlex.quote(compiler)} \"$@\"\n")
                wrapper.chmod(0o755)
        env = dict(os.environ, CMAKE=str(Path(cmake).resolve()),
                   PATH=str(wrappers) + os.pathsep + os.environ["PATH"],
                   GAL_COMPILER_LOG=str(log))
        for name in ("CC", "CXX", "CFLAGS", "CXXFLAGS", "LDFLAGS", "CMAKE_BUILD_TYPE"):
            env.pop(name, None)

        def run(label, **overrides):
            before = log.read_text().splitlines() if log.exists() else []
            started = time.monotonic()
            result = subprocess.run(["bash", "scripts/build-headless.sh"], cwd=source,
                                    env=dict(env, **overrides), text=True,
                                    stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
            (source / (label + ".log")).write_text(result.stdout)
            assert result.returncode == 0, result.stdout
            assert "100% tests passed, 0 tests failed out of 5" in result.stdout, result.stdout
            assert all(name in result.stdout for name in TESTS), result.stdout
            calls = [shlex.split(line) for line in log.read_text().splitlines()[len(before):]]
            compiled = [args[args.index("-c") + 1] for args in calls if "-c" in args]
            print(f"{label}: {time.monotonic() - started:.3f}s, "
                  f"{len(calls)} compiler invocations, {len(compiled)} compilations", flush=True)
            return calls, compiled

        def outputs():
            return {path: path.stat().st_mtime_ns
                    for path in (source / "build-headless").rglob("*")
                    if path.is_file() and (path.suffix in (".o", ".so")
                                           or path.name.startswith("gal_"))}

        calls, _ = run("first-build")
        project_calls = [args for args in calls if "-c" in args and
                         any(str(source / directory) in args[args.index("-c") + 1]
                             for directory in ("native", "tests"))]
        assert len(project_calls) == 12, project_calls
        for args in project_calls:
            assert all(flag in args for flag in ("-Wall", "-Wextra", "-Werror")), args
            standard = "-std=c11" if args[args.index("-c") + 1].endswith(".c") else "-std=c++17"
            assert standard in args, args
        snapshot = outputs()
        calls, _ = run("unchanged")
        assert not calls and outputs() == snapshot, ("Unchanged build invoked a compiler or replaced output", calls)

        with (source / "managed/Program.cs").open("a") as managed:
            managed.write("\n// Isolated managed-only edit for the native build regression.\n")
        calls, _ = run("managed-only")
        assert not calls and outputs() == snapshot, "A C# edit rebuilt native outputs"

        with (source / "native/src/gal.cpp").open("a") as native:
            native.write('\nstatic_assert(sizeof(uint32_t) == 4, "incremental source probe");\n')
        _, compiled = run("native-source")
        assert compiled == [str(source / "native/src/gal.cpp")] * 2, compiled

        with (source / "native/src/text_input_geometry.h").open("a") as header:
            header.write('\nstatic_assert(sizeof(TextInputRect) == 4 * sizeof(int), "header probe");\n')
        _, compiled = run("native-header")
        assert compiled == [str(source / "tests/text_input_geometry_tests.cpp")], compiled

        # The ABI export map is a link input, not a compile input.
        with (source / "native/gal.exports").open("a") as exports:
            exports.write("\n/* Isolated export-map dependency probe. */\n")
        library = source / "build-headless/libgal.so"
        before = library.stat().st_mtime_ns
        _, compiled = run("export-map")
        assert not compiled and library.stat().st_mtime_ns != before

        flags = {"CXXFLAGS": "-DGAL_INCREMENTAL_FLAGS_PROBE=1"}
        _, compiled = run("changed-flags", **flags)
        assert len(compiled) == 11 and all(path.endswith(".cpp") for path in compiled), compiled
        calls, _ = run("unchanged-flags", **flags)
        assert not calls

        debug = dict(flags, CMAKE_BUILD_TYPE="Debug")
        _, compiled = run("changed-config", **debug)
        assert len(compiled) == 12, compiled
        calls, _ = run("unchanged-config", **debug)
        assert not calls

        alternate = dict(flags, CC=str(wrappers / "gcc-alternate"),
                         CXX=str(wrappers / "g++-alternate"), CMAKE_BUILD_TYPE="Debug")
        run("changed-compilers-config", **alternate)
        cache = source / "build-headless/CMakeCache.txt"
        assert cache.with_suffix(".txt.previous").exists(), "Compiler switch did not retain old cache"
        contents = cache.read_text()
        assert "GAL_HEADLESS_ONLY:BOOL=ON" in contents and "CMAKE_BUILD_TYPE:STRING=Debug" in contents
        assert any(line.startswith("CMAKE_CXX_COMPILER:") and line.endswith("=" + alternate["CXX"])
                   for line in contents.splitlines())
        calls, _ = run("unchanged-compilers-config", **alternate)
        assert not calls
        run("restore-defaults")
        calls, _ = run("unchanged-defaults")
        assert not calls
        print("PASS all five contracts run on every invocation; source checkout was untouched")


if __name__ == "__main__":
    main()
