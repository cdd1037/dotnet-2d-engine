# Linux x64 native NuGet proof

Run `bash scripts/pack-native.sh` with the existing pinned dependencies and .NET 10
SDK materialized. `DOTNET` can select an installed SDK. The script makes no network
requests, installs nothing, and publishes nothing. Its restore configuration has
no package sources; the packaging project has no package dependencies.

The output defaults to
`build-packages/feed/Dotnet2D.Native.Linux.x64.0.1.0-preview.1.nupkg`.
An optional first argument or `PACKAGE_FEED` selects another local feed directory.
`PACKAGE_VERSION` changes the local proof version. It does not publish a release.

The script builds only `gal` in a separate `build-package` directory, with UI,
audio and physics enabled. It does not change `build-ui`, dependency installations,
upstream sources or the shared CMake configuration. It supplies
`CMAKE_BUILD_WITH_INSTALL_RPATH=ON`, `CMAKE_INSTALL_RPATH=$ORIGIN`, and
`--disable-new-dtags` at configuration time. The resulting old ELF `DT_RPATH`
locates both direct and transitive sibling libraries. A `DT_RUNPATH` on `libgal`
alone would not locate FreeType's children.

The eight assets are under `runtimes/linux-x64/native/`: `libgal.so`,
`libSDL3.so.0`, `libfreetype.so.6`, `libz.so.1`, `libbz2.so.1.0`,
`libpng16.so.16`, `libbrotlidec.so.1` and `libbrotlicommon.so.1`.
RmlUi 6.3, SDL_image 3.2.4, SDL_mixer 3.2.4 and Box2D 3.1.1 are linked statically
into the engine. One full native profile keeps this integration proof bounded;
feature/profile packages are deferred until their savings justify additional
runtime selection and validation. No static archive is a packaged runtime asset. A `buildTransitive` target copies
the complete license bundle to `licenses/Dotnet2D.Native.Linux.x64/LICENSE.txt` in
build/publish output, including AOT; it does not leave required notices stranded
in the NuGet cache.

`manifest.json` records exact versions, source paths, source pins, static-library
hashes, DSO SHA-256/byte counts, ELF dependencies and minimum required symbol
versions. Original project notices, complete available upstream notices, the
Debian runtime copyright files and complete embedded legal comment blocks from
compiled dependency inputs are retained. Source-package inventories may describe
build/contrib files not shipped in these runtime libraries. FreeType uses FTL,
HIDAPI uses its BSD alternative, and stb uses its MIT alternative.

The package's README documents the baseline: glibc with GLIBC_2.38, a C++ runtime
with GLIBCXX_3.4.29/CXXABI_1.3.15, libgcc, libm and loader remain host supplied.
Graphics/audio drivers, X11/ALSA system libraries and external fonts are excluded.
The current binary is a modern glibc Linux x64 proof; the RID does not establish
general distribution, musl, hardware graphics or cross-platform support.

After packing, verification extracts the actual `.nupkg` into a temporary directory
outside the checkout, clears `LD_LIBRARY_PATH`, `LD_PRELOAD` and `LD_AUDIT`, checks
the packaged hashes and relative RPATH, and runs `ldd -r` and an actual native load.
Every nonbaseline dependency must resolve to the extracted directory. The temporary
extraction is removed; `build-packages/native-verification.json` retains the result
and package hash. This verifies loader closure; managed behavior is verified by the
separate consumer integration tests.

Verified on 2026-10-01 from source base `d07a6ab`: the eight native DSOs total
9,546,224 bytes. The produced package is 4,300,911 bytes, including the manifest,
README, complete license bundle and 23 provenance-bearing notice files. The
isolated extraction passed dependency resolution, eager relocation checks and a
native load. Exact archive bytes/hash are recorded per run in the verification
report; rebuilding can change NuGet ZIP metadata even when the native inputs do
not change.

The refreshed independent consumers preserve byte-identical native payloads in
framework-dependent, trimmed JIT and AOT outputs. Export checks confirm audio,
physics, world clipping, materials and render-target entry points remain present.
This confirms the complete prebuilt profile is retained; it does not establish
hardware behavior or native function trimming.
