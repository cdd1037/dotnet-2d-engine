# Dependency and platform checklist

## Already present and reused

- GNU g++ 14 / make: native contract library/tests
- .NET SDK 10.0.401 and runtime 10.0.12: `../android-trim-tools/dotnet/dotnet`
- System libshaderc.so.1: offline GLSL compilation only; compiled SPIR-V is committed in source snapshot
- Cached ILLink.Tasks: offline AOT-compatibility analyzer only

## Approved and installed locally (2026-10-01)

- SDL3 **3.2.28**, official release: https://github.com/libsdl-org/SDL/releases/tag/release-3.2.28 . Source tarball: https://www.libsdl.org/release/SDL3-3.2.28.tar.gz . Build/install locally under this project's `.deps`, not system-wide
- Kitware CMake **3.31.6** Linux x86-64 archive, official release: https://github.com/Kitware/CMake/releases/tag/v3.31.6 . Extract under this project's `.tools`

User authorized these build dependencies. SDL and CMake archives were verified against official SHA-256 release metadata. SDL X11/ALSA development prerequisites were fetched through signed Debian trixie package metadata and extracted into `.deps/sysroot`; hashes and versions are in `evidence/debian-packages-sha256.txt` and `evidence/sdl-prerequisites-download.log`. No system-wide install. `scripts/configure-local-sdl.sh` reproduces configuration/build from the materialized packages.

## Later validation requirements (separate from dependency approval above)

- Linux graphical: working X11 or Wayland session, Vulkan ICD and compatible device. The initial cloud execution environment has no display, /dev/dri or ICD, so a successful SDL compile would still not establish visual correctness
- Software Vulkan/Xvfb may validate the render pipeline in CI if separately available/authorized; label that as software, not physical GPU verification
- Linux NativeAOT: Microsoft runtime/ILCompiler packs matching the SDK plus an appropriate native compiler/linker toolchain. Official build packs/toolchain downloads are now approved; NativeAOT results are recorded separately. JIT success/analyzers are not NativeAOT execution
- Windows x64: Visual C++/SDK, Windows SDL3, offline DXIL shaders and D3D12 device. HLSL source exists but current backend selects Vulkan/SPIR-V explicitly. Official optional DXIL compiler source: https://github.com/microsoft/DirectXShaderCompiler/releases
- Metal: future implementation and validation, no support claim

## Optional UI dependency pins and notices

The opt-in `scripts/bootstrap-ui-deps.sh` records exact download hashes for:

- RmlUi 6.3, commit `ba95ffe8bfb6370efb2cdcca927eaad4710c5413` (MIT),
  [retained notice](RMLUI-LICENSE.txt)
- SDL3_image 3.2.4 (zlib), [retained notice](SDL3-IMAGE-LICENSE.txt)
- Debian FreeType development package 2.13.3+dfsg-1+deb13u1; the current
  prototype links the system FreeType library, not a vendored copy

SDL3's [zlib notice](SDL3-LICENSE.txt) is retained. Upstream dependencies and
build tools are not vendored in this source milestone. Before distributing a
binary package, audit all included transitive libraries and font notices; these
source notices are not a complete binary-distribution license bundle. A license
for this project's own source has not yet been chosen.

Verified foundation archive SHA-256 values:

```text
1330671214d146f8aeb1ed399fc3e081873cdb38b5189d1f8bb6ab15bbc04211  SDL3-3.2.28.tar.gz
5a1133ff103c71eb5120e2cc3de922733e7d8a26a98ae716397e8676adb367bf  cmake-3.31.6-linux-x86_64.tar.gz
```

References to `evidence/` in historical reports point to local generated logs,
not files promised in a fresh checkout. Portable source, scripts and concise
reports are tracked; tool caches and bulky captures are intentionally excluded.
