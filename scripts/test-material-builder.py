#!/usr/bin/env python3
"""Offline builder regression checks; no renderer or downloaded dependencies."""

import hashlib
import importlib.util
import contextlib
import io
import json
from pathlib import Path
import shutil
import struct
import subprocess
import sys
import tempfile
import unittest
from unittest import mock


ROOT = Path(__file__).resolve().parents[1]
BUILDER = ROOT / "scripts" / "compile-materials.py"
SPEC = importlib.util.spec_from_file_location("material_builder", BUILDER)
builder = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(builder)


class MaterialBuilderTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory(prefix="material-builder-")
        self.addCleanup(self.directory.cleanup)
        self.base = Path(self.directory.name)
        self.sources = self.base / "sources"
        self.output = self.base / "materials"
        self.sources.mkdir()
        for name in ("tint", "desaturate"):
            shutil.copyfile(builder.SOURCE_DIR / (name + ".frag"), self.sources / (name + ".frag"))

    def run_builder(self, *names):
        return subprocess.run([sys.executable, str(BUILDER), "--source-dir", str(self.sources),
                               "--output-dir", str(self.output), *names],
                              capture_output=True, text=True, check=False)

    def snapshot(self):
        return {path.name: path.read_bytes() for path in self.output.iterdir()}

    def test_both_fragments_are_deterministic_with_matching_manifests(self):
        first = self.run_builder()
        self.assertEqual(first.returncode, 0, first.stderr)
        snapshot = self.snapshot()
        self.assertEqual(len(snapshot), 4)
        for name in ("tint", "desaturate"):
            binary = snapshot[name + ".frag.spv"]
            self.assertEqual(struct.unpack_from("<II", binary), (0x07230203, 0x00010000))
            manifest = json.loads(snapshot[name + ".material.json"])
            self.assertEqual(manifest, {"version": 1, "profile": "sprite-fragment-v1",
                                       "fragment": name + ".frag.spv",
                                       "sha256": hashlib.sha256(binary).hexdigest(),
                                       "parameterBytes": 32})
        # The same authored sources must stay byte-identical in a different directory.
        relocated = self.base / "relocated-sources"
        shutil.copytree(self.sources, relocated)
        self.sources = relocated
        second = self.run_builder()
        self.assertEqual(second.returncode, 0, second.stderr)
        self.assertEqual(self.snapshot(), snapshot)

    def test_failed_second_fragment_preserves_every_existing_output(self):
        initial = self.run_builder()
        self.assertEqual(initial.returncode, 0, initial.stderr)
        snapshot = self.snapshot()
        tint = self.sources / "tint.frag"
        tint.write_text(tint.read_text().replace("material.first;", "material.first * 0.5;"))
        desaturate = self.sources / "desaturate.frag"
        desaturate.write_text(desaturate.read_text().replace("sampled.a);", "sampled.a) BROKEN;"))
        failed = self.run_builder()
        self.assertNotEqual(failed.returncode, 0)
        self.assertIn("desaturate.frag:", failed.stderr)
        self.assertIn("error", failed.stderr.lower())
        self.assertEqual(self.snapshot(), snapshot)

    def test_extra_resources_are_rejected(self):
        source = (self.sources / "tint.frag").read_bytes()
        for declaration in (b"layout(set=2,binding=1) uniform sampler2D extra;",
                            b"uniform sampler2D extra;", b"out vec4 extra;"):
            with self.subTest(declaration=declaration):
                with self.assertRaisesRegex(builder.MaterialBuildError, "binding convention"):
                    builder.preflight(source + b"\n" + declaration, self.sources / "tint.frag")

    def test_rejected_compiled_output_preserves_every_existing_output(self):
        initial = self.run_builder()
        self.assertEqual(initial.returncode, 0, initial.stderr)
        snapshot = self.snapshot()
        changed_first = bytearray(snapshot["tint.frag.spv"])
        struct.pack_into("<I", changed_first, 8, 0x12345678)
        original = snapshot["desaturate.frag.spv"]
        oversized = original + bytes(builder.MAX_SPIRV_BYTES + 4 - len(original))
        bad_headers = []
        for offset, value in ((0, 0), (4, 0x00010300), (12, 0),
                              (12, 0x400000), (16, 1)):
            malformed = bytearray(original)
            struct.pack_into("<I", malformed, offset, value)
            bad_headers.append(bytes(malformed))
        for rejected in [oversized, original[:16], original + b"x", *bad_headers]:
            with self.subTest(size=len(rejected), header=rejected[:20]):
                compiler = mock.Mock()
                compiler.compile.side_effect = [bytes(changed_first), rejected]
                errors = io.StringIO()
                with mock.patch.object(builder, "Compiler", return_value=compiler), \
                        contextlib.redirect_stderr(errors):
                    status = builder.main(["--source-dir", str(self.sources),
                                           "--output-dir", str(self.output)])
                self.assertEqual(status, 1)
                self.assertIn("desaturate.frag", errors.getvalue())
                self.assertIn("compiled", errors.getvalue())
                self.assertEqual(self.snapshot(), snapshot)
                compiler.close.assert_called_once()

    def test_preflight_rejects_other_includes_and_oversized_sources(self):
        source = (self.sources / "tint.frag").read_bytes()
        bad = source.replace(builder.HELPER_NAME.encode(), b"../sprite.frag")
        with self.assertRaises(builder.MaterialBuildError):
            builder.preflight(bad, self.sources / "tint.frag")
        path = self.sources / "too-large.frag"
        path.write_bytes(b" " * (builder.MAX_SOURCE_BYTES + 1))
        with self.assertRaisesRegex(builder.MaterialBuildError, "exceeds 65536 bytes"):
            builder.read_source(path)

    def test_normal_glsl_function_and_comment_are_supported(self):
        tint = self.sources / "tint.frag"
        source = tint.read_text().replace("void main()", "void copyColor(in vec4 inputColor, out vec4 outputColor) {\n"
                                        "    outputColor = inputColor;\n}\n// uniform buffer layout are harmless in comments\n"
                                        "void main()")
        tint.write_text(source)
        result = self.run_builder("tint")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(set(self.snapshot()), {"tint.frag.spv", "tint.material.json"})

    def test_normal_glsl_preprocessing_is_supported(self):
        tint = self.sources / "tint.frag"
        source = tint.read_text().replace("void main()", "#define USE_TINT 1\n"
                                        "#define APPLY_TINT(value) \\\n    ((value) * material.first)\n"
                                        "#if USE_TINT\n#define BRIGHTNESS 0.5\n"
                                        "#else\n#define BRIGHTNESS 1.0\n#endif\n"
                                        "#line 20\nvoid main()")
        source = source.replace("texture(sprite_texture, uv) * color * material.first;",
                                "APPLY_TINT(texture(sprite_texture, uv) * color) * BRIGHTNESS;")
        tint.write_text(source)
        result = self.run_builder("tint")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(set(self.snapshot()), {"tint.frag.spv", "tint.material.json"})

    def test_include_callback_rejects_non_helper_and_excess_depth(self):
        compiler = builder.Compiler()
        self.addCleanup(compiler.close)
        for name, depth, expected in ((b"other.glsl", 1, b"only the fixed include"),
                                      (builder.HELPER_NAME.encode(), 9, b"include depth exceeds 8")):
            with self.subTest(name=name, depth=depth):
                address = compiler._resolve_include(None, name, 0, b"test.frag", depth)
                result = builder.IncludeResult.from_address(address)
                self.assertEqual(result.source_name_length, 0)
                self.assertIn(expected, builder.c.string_at(result.content, result.content_length))
                compiler._release_include(None, address)
        self.assertEqual(compiler.includes, {})


if __name__ == "__main__":
    unittest.main(verbosity=2)
