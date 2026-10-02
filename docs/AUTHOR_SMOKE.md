# Combined feature-authoring smoke

The existing independent `packaging/consumers/features` fixture now has an opt-in
`--author-smoke` mode. It is a small deterministic authoring example, not another
game framework or new engine feature. It uses the same caller-owned JSON map and
two-pixel atlas, plus three tiny authored RML/RCSS/SVG files.

## One coherent scene

Three polled frame-entry markers select **closed → open → restored**:

1. `FramePlayer` selects the actor's atlas region and emits the caller's marker
2. The caller applies a bounded `SetCells` edit; an immediate exact circle query
   agrees with the updated floor and existing `TileMapCollision` owner
3. A real dynamic capsule falls through the opened floor and stands on restoration
4. `CameraFollow` tracks the resulting body position using the actual viewport
5. The same marker updates typed UI text; caller atlas art and an SVG badge render
   in the document-owned overlay
6. GPU readback checks the changed floor, retained neighbor, animated actor,
   camera coordinates and both UI image kinds

Game-clock pause is also checked between phases: it changes neither the camera
nor the frame-event side effects. The existing consumer retains its independent
capsule/exact-query, atomic edit/snapshot, camera/bounds, animation/overflow and
zero-allocation checks. Owners release all bodies, shapes and world textures.

## Reproduce with existing prerequisites

After preparing the existing local packages and the optional native build:

```sh
GAL_WITH_SVG=ON GAL_WITH_MIXER=ON GAL_WITH_PHYSICS=ON bash scripts/build-ui.sh
AUTHOR_SVG_NATIVE_DIR="$PWD/build-ui" \
AUTHOR_SMOKE_ROOT=/tmp/dotnet2d-author-smoke-new \
  bash scripts/test-author-smoke.sh
```

The runner defaults `AUTHOR_SVG_NATIVE_DIR` to the retained `build-svg` directory
and requires SVG, RmlUi and physics enabled. It does not rebuild native code itself.
`DOTNET` and `PACKAGE_FEED` select prepared local inputs; a fresh output directory
outside the checkout prevents accidentally mixing results. The .NET 10.0.401 SDK,
local package feed, software-Vulkan driver and separately installed CJK font must
already exist. `GAL_UI_FONT` can select that external font; no font is bundled.
The script downloads, installs and publishes nothing remotely.

## Profile and evidence boundaries

The default `pack-native.sh` package remains **SVG OFF**. The script first runs the
original **56-assertion headless JIT** consumer using that exact packaged runtime.
It then copies that FDD output into `optional-svg-source-profile` and explicitly
replaces only `libgal.so` with the prepared SVG-on source build. The optional
license notices are copied as well. Its name, logs, SHA-256 input manifest and
results identify this overlay; it is not presented as a new SVG native package or
as evidence that the unchanged default package supports SVG.

On 2026-10-02, the graphical run passed **71 additional integration assertions**,
**127 total**, with seven submitted frames including setup and **three 960×540
readbacks**. All three captures were visually inspected. Exact pixel checks cover
the world/SVG; the magnified two-pixel raster allows four intensity levels of
linear-filter rounding at its sample centers. The unchanged default run still
passes **56 assertions**, three headless frames and **4,000 warmed queries with
zero managed allocation**. Both runs end with zero bodies and textures. Release
compilation has zero warnings/errors; no engine integration bug was exposed.

Evidence is retained in `/tmp/dotnet2d-author-smoke-final-20261002`, including
`results.json`, `logs/inputs.sha256`, build/run logs and the three captures. The
source tree is `cb7ee22` plus this fixture/docs closure. The optional source
`libgal.so` SHA-256 is
`c8949f796559aabecc10684fbff7876c3978734b81cb5f11777bbf53fb3639f9`.
The [size refresh](MILESTONE_PACKAGE_SIZES.md) gives complete package-byte and
source equivalence provenance and the corresponding SVG overhead.

This new integration mode is **JIT/software Vulkan**. It intentionally performs
**zero new AOT publishes**; the earlier 56-assertion recent-feature JIT/AOT proof
is separate, as are the original SVG-specific AOT/pixel tests. No combined-mode
AOT, physical GPU, actual OS IME, physical audio or additional-platform acceptance
is claimed. The [current closure audit](ROADMAP_CLOSURE.md) preserves those gates.
