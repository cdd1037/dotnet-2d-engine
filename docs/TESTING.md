# Test tiers

Run commands from the repository root. Use the smallest tier that covers the
change, and preserve the distinction between contract, rendering and device tests.
Every entry point prints elapsed wall time and its exit status.

| When | Command | Coverage / prerequisites |
|---|---|---|
| Small core edit (default) | `scripts/test.sh` | Native headless contracts, Release compile, three CPU-only frames |
| Authored scene edit | `scripts/test.sh quick scene` | Default plus sample scene validation |
| C# composition/entity ownership | `scripts/test.sh quick composition` | Destruction registration, nested typed factories, rollback, repeated instances/subscriptions; CPU only |
| UI authoring edit | `scripts/test.sh quick ui` | Default plus RML/RCSS preflight, no renderer |
| Input/viewport edit | `scripts/test.sh quick input` | Default plus v2 layout, synthetic DPI, remapping, focus and fixed-step edge checks |
| Image format/UI resource edit | `scripts/test.sh quick images` | Bounded raster metadata, static UI image grammar, snapshot/budget and headless ownership contracts; see [integration](UI_IMAGES.md) |
| Static SVG authoring edit | `scripts/test.sh quick svg` | Strict SVG source/reference limits, bound profile and snapshots; [optional graphics integration](UI_SVG.md) |
| Resource paths/cache edit | `scripts/test.sh quick resources` | Default plus focused CPU resource identity/lifetime/diagnostic checks |
| Playable mission edit | `scripts/test.sh quick game` | Default plus focused CPU mission lifecycle/save checks |
| Animation/tween/timer edit | `scripts/test.sh quick animation` | Default plus timing, bounded frame events/clip switching, lifetime, atlas residency and warmed-allocation checks |
| Camera follow edit | `scripts/test.sh quick camera` | Default plus pure managed framing, half-life/clock, zoom/bounds and numeric/allocation contracts |
| Tile movement sample edit | `scripts/test.sh quick movement` | Input/clock boundaries; follow with `--movement-physics-test` on the Box2D build and the [rendered scenario](TILE_MOVEMENT.md) when movement changes |
| TileMap data/culling edit | `scripts/test.sh quick tilemap` | Default plus strict sourcegen data, atomic cell edits/residency, chunk order/culling, lifetime, CPU collision plans and warmed allocations |
| Generic UI models | `scripts/test.sh quick model-ui` | Scalar/record/array schemas, ABI layouts, authoring/resource bounds, re-entry and thread contracts; renderer checks below |
| Copied public-package starter | `DOTNET=/path/to/dotnet bash scripts/test-starter.sh` | Fresh external consumer/cache, fixed-step input/reset, real Box2D, owner cleanup, copied assets and startup failures; optional prepared software Vulkan smoke; no AOT |
| Combined feature-authoring integration | `bash scripts/test-author-smoke.sh` | Existing PackageReference fixture; one default headless JIT run plus three actual-pixel phases with the explicitly prepared SVG-on source runtime; no AOT republish |
| Feature batch | `scripts/test.sh jit` | Native contracts, compile, complete JIT self-test |
| Interop/trimming/serialization/publish change | `AOT_APP=/absolute/path/to/fresh/app scripts/test.sh aot` | Native contracts and full self-test of the explicitly selected NativeAOT binary |
| Renderer change | `scripts/test.sh graphics` | Existing software Vulkan pixel checks, requires prepared SDL/ICD environment |
| UI rendering/integration change | `scripts/test.sh ui` | Existing JIT/AOT UI validation, self-tests, scripted UI and pixel checks |

The quick tiers are smoke/preflight checks, not full focused unit suites. Existing
assertions remain intact. Tests are embedded in the prototype executable; no late
framework migration or test assembly refactor is included in this milestone.

## Setup and freshness

Install the SDK and native compiler described in [dependencies](dependencies.md).
Set `DOTNET` if the SDK is not on PATH. Perform `dotnet restore
managed/GameAuthoringLab.csproj` explicitly once with your approved package source.
Quick/JIT use `--no-restore` and never install dependencies automatically. The
optional local sibling SDK fallback is a workspace convenience, not a dependency
included in a checkout.

The CPU audio contract creates its own tiny WAV in a temporary directory. It does
not require generated `assets/audio` files, ffmpeg or an enabled mixer. The optional
`--audio-offline-test` integration still requires its prepared PCM/cue/Ogg fixtures
and the mixer-enabled native build described in [Audio](AUDIO.md).

The AOT tier deliberately does not republish. Publish the current tree first with
an appropriate NativeAOT toolchain, for example:

```sh
dotnet publish managed/GameAuthoringLab.csproj -c Release -r linux-x64 \
  -p:PublishAot=true -p:StripSymbols=true -o build-aot
AOT_APP="$PWD/build-aot/GameAuthoringLab" scripts/test.sh aot
```

Publishing may restore required runtime/compiler packs; it is an explicit special
step. An old binary passing tests is not evidence for current sources. UI scripts
also assume current JIT and `build-aot` outputs and prepared optional native UI
dependencies; see [UI prototype](UI_PROTOTYPE.md). They use software Vulkan and
scripted input, not physical GPU or real IME acceptance.

## Incremental headless builds

`scripts/build-headless.sh` configures the existing CMake headless graph, builds
only stale objects/links, and runs all eight native CTest contracts on every call
(including bounded image-metadata and SVG-source contracts).
Quick/JIT/AOT entry points keep using it. An unchanged rerun or a C#-only edit
therefore runs the contracts without recompiling native code. Shared-library and
injected-backend tests remain separate, including their distinct `gal.cpp` objects.
`build-headless/libgal.so` and the existing five executable paths remain unchanged;
`gal_image_metadata_tests` is an additional small CPU-only target.

This Linux script needs CMake/CTest >=3.20 and Make in addition to GCC/G++ (or the
selected `CC`/`CXX`). It uses CMake from PATH, or the already-installed local
`.tools/cmake-3.31.6-linux-x86_64`; it never downloads tools. Set `CMAKE` to another
CMake executable and optionally `CTEST` to its companion. `BUILD_JOBS` defaults to
2. Native builds default to Release; `CMAKE_BUILD_TYPE`, `CFLAGS`, `CXXFLAGS`, and
`LDFLAGS` may be set explicitly. The profile always disables SDL/RmlUi/mixer/Box2D
and preserves strict warnings for the library and all eight tests.

Configuration is reapplied each time, so flag/build-type changes invalidate the
appropriate objects or links. On a `CC`/`CXX` executable-path change, the script
retains the previous CMake cache as `build-headless/CMakeCache.txt.previous` before
reconfiguring. Objects and binaries are not cleaned. This avoids CMake's implicit
compiler-change reset losing the headless profile. Updating a compiler in place
at the same path is not detected automatically; move `build-headless` aside
before rebuilding in that case. Do not run simultaneous builds with different configurations in this tree.

The CMake Linux export map now also applies to this entry point: public `gal_*`
C names are retained with `GAL_1` symbol versioning, and incidental C++ exports
are hidden. Editing the export map invalidates the library link. Contract checks
use always-on checks, including in Release builds.

Run the opt-in workflow regression when changing native build orchestration:

```sh
python3 scripts/test-headless-build.py
```

It copies sources into a temporary directory, logs compiler invocations, and
checks unchanged/C#-only runs, native source/header/export-map invalidation,
changed flags, compiler/configuration switches and returning to defaults. It
reruns the same eight contracts each time, without editing the working source tree
or building optional native dependencies.

On the 2026-10-02 Linux runner (GCC 14.2, CMake 3.31.6, two jobs), the isolated
first build took **4.653 s**, unchanged rerun **0.099 s**, and C#-only rerun
**0.097 s**. Both reruns invoked the compiler **zero** times and passed all five
contracts. Source and header changes rebuilt exactly their dependent targets;
flag, build-type and compiler-path changes were also checked. These are measured
native build/test costs, not a full managed, graphics, AOT or package benchmark.

## Milestones and research

Run the necessary aggregate once against the final source tree before committing
a milestone. Reuse that evidence for documentation-only cleanup. Large XML
validation differential/fuzz corpora, size ablations and deep benchmarks remain
opt-in research, outside default development and CI commands. No automatic CI
schedule is introduced.

The retained `tests/xml-differential/Compare.csproj` explicitly links the asset-root
types used by the current validator. It remains an opt-in historical comparison,
including its mutation corpus and allocation measurement, rather than part of the
daily test tier. A JIT build can use `-p:PublishAot=false` when only checking that
the research harness still compiles; that does not establish a fresh AOT result.

Raw evidence is generated locally and ignored by Git. Keep concise results,
versions, caveats and source identity in [validation](validation.md); historical
reports may refer to local evidence that is not shipped in a fresh checkout.

## Entry-point smoke validation (2026-10-01)

`scripts/test.sh quick ui` passed on the current Linux cloud runner in **13 s**
wall time (Release build reported **2.13 s**, zero warnings/errors). Native
contracts passed, three CPU frames processed 777 sprites with no GPU draws/audio,
and sample UI preflight passed. This is one measured invocation, not a benchmark
or guarantee. Existing final aggregate evidence is reused rather than duplicated.

UI text-input changes can run `--ui-owner-test` on the headless build and `--ui-text-test` with the prepared RmlUi build/font. The latter queues SDL composition packets; it does not drive a real OS input method. See [text bridge](UI_TEXT_INPUT.md).

## Typed bindings and lists

`bash scripts/test.sh quick bindings` exercises CPU schema/value bounds and owner
contracts. The full JIT tier includes these checks. With the optional UI build,
source `scripts/ui-env.sh` and run the host with `--binding-native-test`;
`--binding-demo` is the interactive inventory consumer. A native/AOT boundary pass
uses the same `--binding-native-test` on the freshly published executable.

The focused native fixture covers stable row add/remove/reorder, disabled actions,
copy-only typed edits, duplicate/malformed payload rollback, stale generations and
revisions, queue overflow, repeated list replacement, engine-first cleanup and
two-field composition lifetime. `GAL_BOUND_UI_CAPTURE_DIR` selects the three
readback destinations. Real desktop CUA acceptance and synthetic queued text are
recorded separately in [validation](validation.md).

## Independent package consumers

`bash scripts/test-composition-packages.sh` is the focused managed-only public
composition boundary. Rebuild the managed package first; choose a fresh
`PACKAGE_COMPOSITION_PROOF_ROOT`. It copies the ordinary consumer outside the
checkout, restores from a fresh local-only feed/cache, runs JIT and one fresh
NativeAOT publish, verifies package/DLL/source identity, and uses no native library.
See [composition](CSHARP_COMPOSITION.md). This does not rerun the renderer/package
matrix or establish native resource/device behavior.

`pack-managed.sh`, `pack-native.sh` and `test-packages.sh` are an explicit optional
local-feed tier. They require existing SDK/native/AOT dependencies and never
download or publish packages. The proof copies ordinary SDK consumers outside the
checkout, restores a fresh cache from a local-only feed, then verifies normal
run/build, trimmed JIT and AOT. See [reproduction and measurements](NUGET_PROOF.md).
Do not rerun the full package matrix for every edit; use it at a packaging/API
boundary or when a concrete dependency/trim regression warrants it.

## World clipping

Use `scripts/test.sh quick clipping` for the focused managed/native CPU boundary.
The optional UI graphics build runs `--clip-graphics-test`, followed by
`scripts/validate-clipping-pixels.py` against `GAL_CLIP_CAPTURE_DIR`. The test records
actual atlas/alpha/edge/tilemap/UI/resize pixels; the headless tier does not. A fresh
AOT host can run those same flags at the additive ABI boundary.

## Bounded diagnostics

`scripts/test.sh quick diagnostics` checks the managed log/timing/geometry and
scene-overlay recovery contracts. With the existing optional graphics build,
run `--diagnostics-scenario --frames 3` and set `GAL_DIAGNOSTICS_CAPTURE_DIR`.
Then run `scripts/validate-diagnostics-pixels.py` on that directory to check solid
edges, diagonal geometry, unchanged scene interior and disabled/re-enabled output.
This managed-only slice adds no ABI or serialization roots; it does not require a
fresh aggregate AOT publication. See [semantics and limits](DIAGNOSTICS.md).

## Materials

Use `scripts/test.sh quick materials` for the source/cache/native-handle boundary,
and `scripts/test-material-builder.py` for offline GLSL preparation. The existing
UI graphics build supports `--material-graphics-test`; set
`GAL_MATERIAL_CAPTURE_DIR` and run `scripts/validate-material-pixels.py` against
the directory. At this additive ABI/source-generation boundary, run a fresh AOT
host with the same focused graphics checks and compare readbacks. Headless tests
do not claim shader compilation or execution. See [contract](MATERIALS.md).

## Explicit render targets

`scripts/test.sh quick targets` validates headless target/pass ownership, budgets,
partitioning and recovery. Run `--target-graphics-test` with
`GAL_TARGET_CAPTURE_DIR`, then `scripts/validate-target-pixels.py` for actual
translucent compositing, repeated sampling/alpha tint, zero/near-zero alpha,
clear/resize/clip behavior and final-window UI. A fresh AOT run and readback
comparison belong at this additive ABI boundary. See [limits and costs](RENDER_TARGETS.md).


## Public package module boundary

After rebuilding the two local packages, run
`PACKAGE_MODULE_PROOF_ROOT=/tmp/dotnet2d-modules-new bash scripts/test-package-modules.sh`.
It copies an ordinary SDK consumer outside the source tree, then runs existing
animation/timing, audio offline PCM, Box2D and TileMap through public APIs in JIT
and one fresh AOT publish. Seven forged-constructor and seven unsupported-null
compile sites must fail, while copied state views remain immutable and interop
helpers remain inaccessible. Three fresh minimal trimmed publishes inspect unused
module removal; this does not repeat every graphics/device matrix. See the
[public boundary](PACKAGE_API_NEXT.md) for exact prerequisites, limits and sizes.

## UI monotonic-clock regression

With the existing optional RmlUi build and prepared SDL/software Vulkan dependencies,
run this explicitly selected integration test from the repository root:

```sh
cmake -S . -B build-ui # refresh targets using the existing UI dependency cache
cmake --build build-ui --target gal_ui_clock_tests
source scripts/ui-env.sh
build-ui/gal_ui_clock_tests "$PWD/assets/ui/settings.rml"
```

Use the CMake executable used to configure `build-ui` if it is not on PATH. The
font defaults to `/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc`; set
`GAL_UI_FONT` to another compatible SC collection when needed. The test queues SDL
mouse input into a real RmlUi text field, waits 1.1 seconds without rendering and
checks that the next click remains a single click. An immediate following click
must still select the word. It fails with the old render-count clock and passes
with the inherited SDL performance-counter clock. This is software-rendered input
integration, not a physical device or real OS IME test. Its optional target is
excluded from the default build and CTest so CPU-only contracts stay independent
of fonts, assets and graphics setup.

## Runtime TileMap edits

The existing `quick tilemap` focus includes opt-in cell-edit validation, immutable
snapshot isolation, full-palette residency, chunk refresh and cleanup. Run
`--tilemap-physics-test` against the Box2D native build for atomic collision
replacement, multilayer union, body/live-plus-retired shape headroom and explicit
step/retry behavior. With the prepared software Vulkan build,
`--tilemap-edit-graphics-test` writes three captures to
`GAL_TILEMAP_EDIT_CAPTURE_DIR`; check them with
`scripts/validate-tilemap-edit-pixels.py`. The captures cover add/remove/replace,
X/Y/XY flips, 15/16 chunk boundaries, unchanged sibling instances and exact restore.
This managed-only API adds neither ABI nor serialization roots; one final JIT
aggregate is sufficient for the batch, with no repeated full AOT/package matrix.


## Capsule and exact-overlap boundary

`quick physics` includes the additive C/C# record layouts and capsule numeric
contracts. `scripts/test-physics.sh` also exercises the real solver's exact circle/
rotated-box overlaps, capsule response and contact/sensor events, caller-span
capacity, filtering and ownership. Its native `physics_contract` contract runs
in both enabled and disabled builds, including malformed ABI and context/thread/
frame guards. Follow with `--tilemap-physics-test` and `--movement-physics-test`
on that same Box2D build for existing collision consumers.

For the final combined public-API proof, rebuild the local engine/native packages,
then run `PACKAGE_FEATURE_PROOF_ROOT=/tmp/dotnet2d-features-new bash
scripts/test-package-features.sh`. It uses one ordinary external PackageReference
consumer for camera follow, animation events/clip selection, transactional tile
edits and capsule/exact queries, in JIT and one fresh NativeAOT publish. It does
not repeat the earlier negative-compile, minimal-trim or graphics/device matrices.

## Author-entry starter

[The current author guide](AUTHOR_GUIDE.md) leads to `templates/Starter`, an ordinary
SDK project using only the two public preview packages. The independent
`scripts/test-starter.sh` proof needs an existing .NET 10 SDK and prepared local
feed (default `build-packages/feed`, override `PACKAGE_FEED`). It copies the
starter to a fresh directory outside the checkout, restores a fresh package cache
with a local-only config, and checks JIT lifecycle/timing/input/reset, actual
Box2D motion, PNG metadata/managed lease ownership, asset copying and diagnostics.
It verifies the consumed managed DLL and eight native libraries against the actual
packages, plus redistributed license files. It does not pack/rebuild the runtime.

Optionally set `STARTER_GRAPHICS_SYSROOT` to an already prepared software Vulkan
sysroot containing `usr/share/vulkan/icd.d/lvp_icd.json` and its system libraries.
This adds 30 offscreen rendered frames using the unchanged native package. No
engine-library overlay or native rebuild is performed; this smoke checks successful
render submissions, not pixels or human keyboard/device acceptance. The default
path remains independent of graphics drivers and fonts.

`STARTER_PROOF_ROOT` may select a fresh evidence directory outside the checkout;
otherwise the script creates one under the system temporary directory. Results
include exact template/package hashes. The script exits on any failed check.

The optional `scripts/compare-starter-wiring.py` requires the separately prepared
three-genre comparison inputs and never edits them. It adapts isolated copies of
two hosts, records all source/helper line counts, builds/runs their existing native
checks and validates extracted host-policy statements with synthetic input. See
[the measured result and caveats](AUTHOR_ENTRY_COMPARISON.md).

## Generic UI data/event bridge

`bash scripts/test.sh quick model-ui` is the focused CPU tier. The full JIT
aggregate includes it. With the prepared offscreen graphics dependencies:

```sh
source scripts/ui-env.sh
../android-trim-tools/dotnet/dotnet managed/bin/Release/net10.0/GameAuthoringLab.dll --model-ui-native-test
python3 scripts/validate-model-ui-pixels.py
```

Use your installed .NET executable instead of the workspace sibling when needed.
The native suite exercises inventory card actions and images, dialogue choices,
nested settings arrays, exact 64-bit keys, pointer gestures across reorders,
failed projections/native batches/reloads, retired event generations, composition
cancellation, queue overflow and disposal. It captures three actual 960×540
software-rendered frames; `GAL_MODEL_UI_CAPTURE_DIR` changes the destination for
both the executable and pixel checker. `--model-ui-demo --frames 9` separately
checks public sample navigation through all three documents.

The isolated `scripts/test-model-ui-packages.sh` is an opt-in PackageReference
JIT/NativeAOT boundary and measurement tier, not part of each edit's loop. It needs
already-built local packages and installed compiler/runtime packs; it does not
publish remotely or install dependencies. See [generic model results](UI_MODELS.md).
