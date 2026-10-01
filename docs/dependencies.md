# Dependency and platform checklist

## Already present and reused

- GNU g++ 14 / make: native contract library/tests
- .NET SDK 10.0.401 and runtime 10.0.12: `../android-trim-tools/dotnet/dotnet`
- System libshaderc.so.1: offline GLSL compilation only; compiled SPIR-V is committed in source snapshot
- Cached ILLink.Tasks: offline AOT-compatibility analyzer only

## Approved and installed locally (2026-10-01)

Current SDL pin: **3.4.16**, official stable release published 2026-09-02:
https://github.com/libsdl-org/SDL/releases/tag/release-3.4.16 . Its isolated install
is `.deps/sdl-3.4.16-install`; the older source/install below is retained for rollback.
`scripts/sdl-env.sh` is the shared version/prefix default. `SDL3_PREFIX` can select
an explicit installation that meets the CMake version requirement. The explicit
`scripts/fetch-sdl.sh` verifies the pinned official release archive hash before
extraction; `scripts/configure-local-sdl.sh` only builds already-materialized source.
See [upgrade verification and limits](SDL_UPGRADE.md).

Foundation dependencies retained:

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

SDL3's [zlib notice](SDL3-LICENSE.txt) is retained and refreshed to the 3.4.16
archive's 2026 notice. Its zlib terms are unchanged. Upstream dependencies and
build tools are not vendored in this source milestone. Before distributing a
binary package, audit all included transitive libraries and font notices; these
source notices are not a complete binary-distribution license bundle. Original
project code/documentation are licensed under [MIT](../LICENSE);
[third-party notices](../THIRD_PARTY_NOTICES.md) remain separate.

Verified foundation archive SHA-256 values:

```text
1330671214d146f8aeb1ed399fc3e081873cdb38b5189d1f8bb6ab15bbc04211  SDL3-3.2.28.tar.gz
5a1133ff103c71eb5120e2cc3de922733e7d8a26a98ae716397e8676adb367bf  cmake-3.31.6-linux-x86_64.tar.gz
```

References to `evidence/` in historical reports point to local generated logs,
not files promised in a fresh checkout. Portable source, scripts and concise
reports are tracked; tool caches and bulky captures are intentionally excluded.

The SDL 3.4.16 build required X11 extension development packages that 3.2.28
previously omitted when unavailable. Only official Debian trixie packages were
downloaded, verified against the signed Packages index and extracted into the
local `.deps/sysroot`: libxcursor-dev + libxcursor1; libxi-dev + libxi6;
libxrandr-dev + libxrandr2; libxfixes-dev + libxfixes3; libxss-dev + libxss1;
libxtst-dev + libxtst6; and libxrender-dev + libxrender1. No system-wide
installation occurred. Ordinary Linux build setups may already provide these.
This local build supports X11/offscreen and Vulkan; missing Wayland/PipeWire/Pulse
development stacks were not added or claimed as tested platform support.

Current SDL source archive SHA-256, verified against the official release asset
metadata on 2026-10-01:

```text
7322236cd12090c3eb40b9728be4d49c76f66ad17d04369584d4ecad5cf77c68  SDL3-3.4.16.tar.gz
```

SDL3_image 3.2.4 and RmlUi 6.3 remain unchanged. Built-in SDL PNG functionality
does not replace the current image dependency or change accepted authoring formats
in this upgrade. SDL_mixer integration is a separate subsequent batch.

## Optional audio dependency

[SDL_mixer 3.2.4](AUDIO.md) is pinned, hash-verified and built locally as a static
PIC library with WAVE and bundled stb_vorbis only. `scripts/build-mixer.sh` is the
explicit setup entry point; normal builds do not download codecs. Full mixer and
stb notices are retained. Generated audio fixtures are source-only reproductions
from `scripts/generate-audio-fixtures.py`, using Python/ffmpeg for authoring only.

## Optional physics dependency

[Box2D 3.1.1](PHYSICS.md) is pinned to official commit
`8c661469c9507d3ad6fbd2fea3f1aa71669c2fe3`; the archive hash and all 233 source
file blobs were verified. `scripts/build-box2d.sh` builds only the static C17
library, with no samples/GLFW/enkiTS/ImGui downloads. The full MIT notice is
retained. Native physics and helper scripts require explicit opt-in.
