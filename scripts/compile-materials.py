#!/usr/bin/env python3
"""Compile complete GLSL material fragments offline with the installed libshaderc.

The sprite-fragment-v1 profile is an engine binding convention for trusted source,
not SPIR-V reflection, binary-layout verification, or a shader sandbox. Sources
are compiled unchanged; shaderc handles the ordinary GLSL include directive.
"""

import argparse
import ctypes as c
import ctypes.util
import hashlib
import json
import os
from pathlib import Path
import re
import struct
import sys
import tempfile


ROOT = Path(__file__).resolve().parents[1]
SOURCE_DIR = ROOT / "shaders" / "materials"
OUTPUT_DIR = ROOT / "assets" / "materials"
PROFILE = "sprite-fragment-v1"
HELPER_NAME = PROFILE + ".glsl"
MAX_SOURCE_BYTES = 64 * 1024
MAX_INCLUDE_DEPTH = 8
MAX_SPIRV_BYTES = 256 * 1024
PARAMETER_BYTES = 32


class MaterialBuildError(Exception):
    """An actionable authoring or compiler error."""


def read_source(path):
    with path.open("rb") as source_file:
        source = source_file.read(MAX_SOURCE_BYTES + 1)
    if len(source) > MAX_SOURCE_BYTES:
        raise MaterialBuildError(f"{path}: source exceeds {MAX_SOURCE_BYTES} bytes")
    if b"\0" in source:
        raise MaterialBuildError(f"{path}: NUL bytes are not supported in GLSL source")
    try:
        source.decode("utf-8")
    except UnicodeDecodeError as error:
        raise MaterialBuildError(f"{path}: source must be UTF-8: {error}") from error
    return source


def preflight(source, path):
    """Reject obvious interface changes; this is not a semantic GLSL validator.

    Resource/layout declarations and global stage interfaces belong in the
    fixed helper. Ordinary GLSL preprocessing is handled by shaderc, so macro
    expansion and conditional compilation exceed what these text checks prove.
    This profile requires trusted authors to preserve the binding convention.
    """
    # This comment-stripped copy is used only for checks, never for compilation.
    clean = re.sub(r"//[^\n]*|/\*[\s\S]*?\*/", lambda match: "\n" * match[0].count("\n"),
                   source.decode("utf-8"))
    include_count = 0
    lines = clean.splitlines(keepends=True)
    index = 0
    while index < len(lines):
        line = lines[index]
        directive = re.match(r"\s*#\s*(\w+)\b(.*)", line)
        if not directive:
            index += 1
            continue
        first_line = index
        # Skip complete logical directive lines during text-only checks. The
        # original macro definitions and continuations go unchanged to shaderc.
        while line.rstrip("\r\n").endswith("\\") and index + 1 < len(lines):
            index += 1
            line = line.rstrip("\r\n")[:-1] + lines[index]
        directive = re.match(r"\s*#\s*(\w+)\b(.*)", line, re.DOTALL)
        name, value = directive.groups()
        if name == "include":
            if value.strip() not in {f'"{HELPER_NAME}"', f"<{HELPER_NAME}>"}:
                raise MaterialBuildError(
                    f"{path}:{first_line + 1}: only #include \"{HELPER_NAME}\" is supported")
            include_count += 1
        for line_index in range(first_line, index + 1):
            lines[line_index] = "\n" if lines[line_index].endswith("\n") else ""
        index += 1
    if include_count != 1:
        raise MaterialBuildError(f"{path}: include \"{HELPER_NAME}\" exactly once")

    checked = "".join(lines)
    braces = parentheses = 0
    for token_match in re.finditer(r"[A-Za-z_][A-Za-z_0-9]*|[{}()]", checked):
        token = token_match[0]
        resource = (token in {"layout", "uniform", "buffer", "atomic_uint"}
                    or re.fullmatch(r"[iu]?(?:sampler|image)\w*", token))
        interface = token in {"in", "out", "inout"} and braces == 0 and parentheses == 0
        if resource or interface:
            line = checked.count("\n", 0, token_match.start()) + 1
            raise MaterialBuildError(
                f"{path}:{line}: {PROFILE} binding convention reserves resource, "
                f"layout, and global interface declarations for {HELPER_NAME} "
                f"(found {token!r})")
        if token == "{":
            braces += 1
        elif token == "}":
            braces -= 1
        elif token == "(":
            parentheses += 1
        elif token == ")":
            parentheses -= 1


def validate_compiled(blob, path):
    """Match the runtime's bounded SPIR-V header contract, without reflection."""
    if not 20 <= len(blob) <= MAX_SPIRV_BYTES or len(blob) % 4:
        raise MaterialBuildError(
            f"{path}: compiled SPIR-V must contain 20..{MAX_SPIRV_BYTES} bytes "
            f"in whole 4-byte words (received {len(blob)})")
    magic, version, _generator, bound, schema = struct.unpack_from("<5I", blob)
    if magic != 0x07230203 or version != 0x00010000 or schema != 0 or not 0 < bound <= 0x3FFFFF:
        raise MaterialBuildError(
            f"{path}: compiled output requires SPIR-V 1.0 magic/version, schema 0, "
            "and an ID bound of 1..0x3fffff")


class IncludeResult(c.Structure):
    _fields_ = [("source_name", c.c_void_p), ("source_name_length", c.c_size_t),
                ("content", c.c_void_p), ("content_length", c.c_size_t),
                ("user_data", c.c_void_p)]


ResolveInclude = c.CFUNCTYPE(c.c_void_p, c.c_void_p, c.c_char_p, c.c_int,
                            c.c_char_p, c.c_size_t)
ReleaseInclude = c.CFUNCTYPE(None, c.c_void_p, c.c_void_p)


class Compiler:
    def __init__(self):
        try:
            self.lib = c.CDLL(ctypes.util.find_library("shaderc") or "libshaderc.so.1")
        except OSError as error:
            raise MaterialBuildError(f"installed libshaderc is required (offline): {error}") from error
        signatures = {
            "compiler_initialize": (c.c_void_p, []),
            "compiler_release": (None, [c.c_void_p]),
            "compile_options_initialize": (c.c_void_p, []),
            "compile_options_release": (None, [c.c_void_p]),
            "compile_options_set_target_env": (None, [c.c_void_p, c.c_int, c.c_uint]),
            "compile_options_set_target_spirv": (None, [c.c_void_p, c.c_int]),
            "compile_options_set_include_callbacks":
                (None, [c.c_void_p, ResolveInclude, ReleaseInclude, c.c_void_p]),
            "compile_into_spv": (c.c_void_p, [c.c_void_p, c.c_char_p, c.c_size_t,
                                            c.c_int, c.c_char_p, c.c_char_p, c.c_void_p]),
            "result_get_compilation_status": (c.c_int, [c.c_void_p]),
            "result_get_error_message": (c.c_char_p, [c.c_void_p]),
            "result_get_length": (c.c_size_t, [c.c_void_p]),
            "result_get_bytes": (c.c_void_p, [c.c_void_p]),
            "result_release": (None, [c.c_void_p]),
        }
        for suffix, (return_type, argument_types) in signatures.items():
            function = getattr(self.lib, "shaderc_" + suffix)
            function.restype = return_type
            function.argtypes = argument_types

        self.helper = read_source(SOURCE_DIR / HELPER_NAME)
        self.includes = {}
        self.resolve_include = ResolveInclude(self._resolve_include)
        self.release_include = ReleaseInclude(self._release_include)
        self.compiler = self.lib.shaderc_compiler_initialize()
        self.options = self.lib.shaderc_compile_options_initialize()
        if not self.compiler or not self.options:
            self.close()
            raise MaterialBuildError("libshaderc could not initialize the compiler/options")
        # shaderc_target_env_vulkan = 0, Vulkan 1.0 = 1 << 22, SPIR-V 1.0 = 0x10000.
        self.lib.shaderc_compile_options_set_target_env(self.options, 0, 1 << 22)
        self.lib.shaderc_compile_options_set_target_spirv(self.options, 0x10000)
        self.lib.shaderc_compile_options_set_include_callbacks(
            self.options, self.resolve_include, self.release_include, None)

    def _resolve_include(self, user_data, requested, include_type, requesting, depth):
        if depth > MAX_INCLUDE_DEPTH:
            name, content = b"", f"include depth exceeds {MAX_INCLUDE_DEPTH}".encode()
        elif requested != HELPER_NAME.encode():
            name, content = b"", f"only the fixed include {HELPER_NAME} is supported".encode()
        else:
            name, content = HELPER_NAME.encode(), self.helper
        name_buffer = c.create_string_buffer(name)
        content_buffer = c.create_string_buffer(content)
        result = IncludeResult(c.cast(name_buffer, c.c_void_p), len(name),
                               c.cast(content_buffer, c.c_void_p), len(content), None)
        address = c.addressof(result)
        # shaderc owns neither the struct nor its strings; keep all alive until release.
        self.includes[address] = (result, name_buffer, content_buffer)
        return address

    def _release_include(self, user_data, result):
        self.includes.pop(result, None)

    def compile(self, path):
        source = read_source(path)
        preflight(source, path)
        # shaderc_fragment_shader = 1. Pass the original full GLSL source unchanged.
        # A logical filename keeps output independent of the checkout location.
        result = self.lib.shaderc_compile_into_spv(
            self.compiler, source, len(source), 1, os.fsencode(path.name), b"main", self.options)
        if not result:
            raise MaterialBuildError(f"{path}: libshaderc did not return a compilation result")
        try:
            if self.lib.shaderc_result_get_compilation_status(result):
                message = self.lib.shaderc_result_get_error_message(result)
                raise MaterialBuildError(message.decode("utf-8", errors="replace").rstrip())
            return c.string_at(self.lib.shaderc_result_get_bytes(result),
                               self.lib.shaderc_result_get_length(result))
        finally:
            self.lib.shaderc_result_release(result)

    def close(self):
        if self.options:
            self.lib.shaderc_compile_options_release(self.options)
            self.options = None
        if self.compiler:
            self.lib.shaderc_compiler_release(self.compiler)
            self.compiler = None
        self.includes.clear()


def publish(compiled, output_dir):
    """Stage everything first, replace each file atomically, publish manifests last."""
    output_dir.mkdir(parents=True, exist_ok=True)
    artifacts = []
    manifests = []
    for name, blob in compiled:
        filename = name + ".frag.spv"
        artifacts.append((output_dir / filename, blob))
        manifest = {"version": 1, "profile": PROFILE, "fragment": filename,
                    "sha256": hashlib.sha256(blob).hexdigest(), "parameterBytes": PARAMETER_BYTES}
        manifests.append((output_dir / (name + ".material.json"),
                          (json.dumps(manifest, indent=2) + "\n").encode("utf-8")))
    staged = []
    try:
        for destination, content in artifacts + manifests:
            descriptor, temporary = tempfile.mkstemp(prefix=".material-", dir=output_dir)
            temporary = Path(temporary)
            staged.append((temporary, destination))
            with os.fdopen(descriptor, "wb") as file:
                file.write(content)
                file.flush()
                os.fsync(file.fileno())
        for temporary, destination in staged:
            os.replace(temporary, destination)
    finally:
        for temporary, _ in staged:
            temporary.unlink(missing_ok=True)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("names", nargs="*", help="material names, without .frag (default: tint desaturate)")
    parser.add_argument("--source-dir", type=Path, default=SOURCE_DIR,
                        help="full fragment source directory; include still uses the fixed engine helper")
    parser.add_argument("--output-dir", type=Path, default=OUTPUT_DIR,
                        help="SPIR-V and sibling manifest directory")
    args = parser.parse_args(argv)
    names = args.names or ["tint", "desaturate"]
    if len(set(names)) != len(names) or any(not re.fullmatch(r"[A-Za-z][A-Za-z0-9_-]*", name) for name in names):
        parser.error("use distinct material names containing letters, digits, underscores or hyphens, starting with a letter")
    compiler = None
    try:
        compiler = Compiler()
        # No output is changed unless every requested source compiles successfully.
        compiled = []
        for name in names:
            path = args.source_dir / (name + ".frag")
            blob = compiler.compile(path)
            validate_compiled(blob, path)
            compiled.append((name, blob))
        publish(compiled, args.output_dir)
        for name, blob in compiled:
            print(f"{name}: {len(blob)} bytes SPIR-V 1.0 -> {args.output_dir / (name + '.material.json')}")
    except (MaterialBuildError, OSError, AttributeError) as error:
        print(f"material build failed: {error}", file=sys.stderr)
        return 1
    finally:
        if compiler is not None:
            compiler.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
