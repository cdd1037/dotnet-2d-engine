# Milestone small-consumer package sizes

Measured 2026-10-02 from checkout `cb7ee228972128db9bb0401959a3a014ed9ba30b`.
This bounded refresh publishes exactly **Sprite/UI × trimmed JIT/NativeAOT**.
It does not repeat the earlier empty/framework-dependent matrix or add features.
The unchanged small consumer sources are copied outside the checkout and use only
ordinary `PackageReference`s and a fresh, local-only NuGet cache.

## Whole output, same native profile

All figures are exact **uncompressed publish-directory bytes**, excluding `.pdb`
and `.dbg` files. Trimmed JIT includes the .NET runtime. AOT includes the native
executable. Both include assets and required package notices. These are not ZIP
sizes or complete-machine installation sizes.

| Consumer | Mode | Earlier `d07a6ab` | Current | Change |
| --- | --- | ---: | ---: | ---: |
| Sprite | Trimmed JIT | 34,127,881 | **34,173,681** | +45,800 |
| Sprite | NativeAOT | 12,515,604 | **12,566,876** | +51,272 |
| UI | Trimmed JIT | 34,562,636 | **34,661,684** | +99,048 |
| UI | NativeAOT | 13,045,585 | **13,225,433** | +179,848 |

The earlier figures remain documented in [NUGET_PROOF.md](NUGET_PROOF.md). Both
measurements use the **graphics/UI/audio/physics, SVG-OFF** native package and
retain its unstripped Release DSOs. The eight-file native payload is now
**9,584,856 bytes**, up **38,632** from 9,546,224. Every measured output contains
byte-identical copies of the current native package payload. Managed trimming
and NativeAOT do not remove unused code from those prebuilt shared libraries.

### Trimmed JIT breakdown

| Component | Sprite | UI |
| --- | ---: | ---: |
| App executable/host | 78,256 | 78,256 |
| App managed DLL | 6,144 | 9,216 |
| Trimmed engine DLL | 105,984 | 122,368 |
| Native engine/dependencies | 9,584,856 | 9,584,856 |
| Authored assets | 1,415 | 2,356 |
| Redistribution notices | 320,261 | 320,261 |
| Runtime/dependency metadata | 8,693 | 8,843 |
| .NET framework/other files | 24,068,072 | 24,535,528 |
| **Total excluding symbols** | **34,173,681** | **34,661,684** |
| Excluded sidecar symbols | 11,160 | 11,552 |

### NativeAOT breakdown

| Component | Sprite | UI |
| --- | ---: | ---: |
| App executable, including retained managed code | 2,660,344 | 3,317,960 |
| Separate managed DLLs | 0 | 0 |
| Native engine/dependencies | 9,584,856 | 9,584,856 |
| Authored assets | 1,415 | 2,356 |
| Redistribution notices | 320,261 | 320,261 |
| Other files | 0 | 0 |
| **Total excluding symbols** | **12,566,876** | **13,225,433** |
| Excluded sidecar symbols | 5,905,280 | 6,843,768 |

The full packaged engine DLL is 422,912 bytes. Its unused audio, physics,
frame-animation/timing, TileMap, camera-follow/bounds, world-clipping,
diagnostics/debug geometry, material and render-target families are absent from both trimmed metadata and
AOT symbol maps. Full-DLL positive controls first verify those types really exist
in the package. Sprite retains authored-scene code and drops UI bindings; UI
retains bindings. Sample-only mission/room/movement programs remain outside the
package. Native export controls still find audio, physics, clipping, materials,
render targets and rendering in the complete DSO.

## Optional SVG delta, separately measured

An optional source build, `build-svg/libgal.so`, was refreshed from the same
native sources using the same Release/compiler/linker/RPATH settings, with only
the SVG capability and its required inputs enabled. It is **5,034,544 bytes**,
versus **4,111,704 bytes** for the SVG-OFF packaged `libgal.so`: **+922,840 bytes**.
Its dynamic dependency list is unchanged; LunaSVG/PlutoVG are linked statically.
Both builds retain `$ORIGIN` `DT_RPATH`.

This is a **DSO delta**, not an SVG-enabled package or whole-application size.
The four published outputs above still contain the original SVG-OFF package.
An SVG distribution must also include its additional applicable notices and
caller-authored assets; those are not included in this DSO delta. No compressed
SVG package size is claimed.

## Source/artifact identity and verification

The cached managed package's NuGet repository stamp is `d0447aa`, while the native
package/manifest stamp is `a2afb88`. **Those stamps differ.** In particular, the
current managed physics sources differ from the managed package stamp. Rather
than treating that stamp as proof or silently repacking, this refresh forced a
complete managed rebuild from the current explicit runtime source list, preserving
the package's `SourceRevisionId` solely for metadata comparison. The rebuilt DLL
is byte-identical to the packaged DLL. Current native inputs equal `a2afb88`, and
all packaged DSO and static-input hashes agree with the native manifest. Exact
runtime source-file hashes are retained in the proof's `identity.json`.

| Artifact | SHA-256 |
| --- | --- |
| Managed `.nupkg` | `cbb0252bc746a1ffe2393ae445f2dab4300f2c0b8078cfc0c03c60493e532940` |
| Native `.nupkg` | `8ad9132d698d7c251c53bc824dbb16340d19e7468804e373054f79add2cfdbf1` |
| Full managed DLL, also forced-rebuild result | `e8dec0d8a915981ae81f900b5ac4d5d9b496db80bd36eeb41fd152eb4644f807` |
| Packaged SVG-OFF `libgal.so` | `4ec0956e7ee67931da919c9b263c7a4f7c6681624fbf06db44e4c69a9b4fe0d6` |
| Optional SVG-ON `libgal.so` | `c8949f796559aabecc10684fbff7876c3978734b81cb5f11777bbf53fb3639f9` |

All four publishes and executions passed, with no managed build/publish warning
diagnostics. UI screenshots are byte-identical between trimmed JIT and AOT.
Asset counts, byte-exact license copies, native hashes, package-only restore
graphs, full-DLL controls and trimmed/AOT roots all passed. SDK 10.0.401/runtime
10.0.12 and the existing clang 19 toolchain were reused; no dependencies were
downloaded or installed and nothing was published remotely.

The UI font remains external, selected through `GAL_UI_FONT`; **packaged font
bytes are zero**. This run used the existing Noto Sans CJK font and offscreen
software Vulkan. Host graphics drivers, glibc/C++ runtime requirements and the
Linux-x64-only support limits in [NATIVE_PACKAGE.md](NATIVE_PACKAGE.md) still apply.
No hardware graphics, physical audio or real OS IME acceptance is implied.

## Reproduce

With the existing verified feed and prerequisites:

```sh
python3 scripts/measure-milestone-packages.py \
  --proof /tmp/dotnet2d-small-milestone-new
```

The script refuses an existing proof directory and stops if package/source
identity fails. It performs exactly two trimmed and two AOT publishes. Optional
`--svg-library build-svg/libgal.so` additionally compares a previously refreshed
SVG build's size, configuration and dependencies without modifying the package
or those outputs. Use `--feed`, `--source-cache`, `--dotnet`, `AOT_CXX` and
`GAL_UI_FONT` to select existing prerequisites. The in-process ILLink shim is the
same build-host accommodation as the earlier proof.

Local evidence: `/tmp/dotnet2d-small-milestone-20261002/measurements.json`,
`identity.json`, per-file publish hashes, metadata/AOT maps, logs and UI captures.
Optional native configure/build logs are under ignored `evidence/milestone-sizes/`.
The report and script are tracked; generated artifacts are not.
