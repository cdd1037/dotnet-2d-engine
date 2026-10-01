# .NET 2D Engine — foundation milestone

Status: experimental independent engine, not a production release. The repository
name is `dotnet-2d-engine`. Source directories, `GameAuthoringLab`
managed names and the `gal` native ABI remain unchanged to avoid a disruptive
rename during integration.

## What is here

- `engine/`: reusable .NET 10 runtime project and explicit linked source list
- `managed/`: runtime source files plus the separate demo/test executable, gameplay
  and game progress persistence
- `packaging/`: independent PackageReference fixtures and native package preparation
- `native/`: C++17 platform/rendering implementation and a narrow versioned C ABI
- `assets/`: generated BMP sample art, authored JSON scene and optional RML/RCSS UI
- `shaders/`: standard shader sources and offline-compiled SPIR-V
- `tests/`: native contracts and C ABI consumer
- `scripts/`: build, asset generation and validation entry points
- `docs/`: architecture, decisions, reproducible commands and milestone findings

Downloaded tools/dependencies, binaries, caches and raw captures/logs remain local
and are ignored by Git. Existing local evidence is retained, not deleted. No full
Chinese font is bundled. Generated shader blobs and small sample art are deliberate
runtime inputs, not accidental build outputs.

## Settled for this milestone

The host is C#/.NET with a C++ SDL3 GPU backend. Stable world identity, transform
parentage, lifetime ownership and scene membership are separate concepts.
Lightweight components do not imply a full ECS. Behavior attachments have explicit
ownership and cleanup; sprite extraction uses stable merge sorting.

Official System.Text.Json source generation handles save snapshots. Flat authored
scene JSON has a separate loader and validation contract; it is not a save-file
schema or a prefab inheritance system. Files and CLI operations are canonical for
AI authoring and future visual tools.

Optional RmlUi is an experiment behind `GAL_ENABLE_RMLUI`; selecting the final UI
framework remains open, including Myra, Gum and an engine-owned alternative.
See [architecture](architecture.md), [world model](../managed/WORLD.md),
[authored scenes](AUTHORED_SCENES.md), [ownership and sorting](LIFECYCLE_SORTING.md),
[two-room gameplay](TWO_ROOM.md) and [combined gameplay/UI](GAMEPLAY_UI_LIFECYCLE.md).

## Build and verification

Start with the headless commands in the [README](../README.md). Headless contract
checks do not establish graphics, audio or input correctness. The optional graphics
and UI scripts require the explicitly materialized dependencies described in
[dependencies](dependencies.md) and [UI prototype](UI_PROTOTYPE.md); they are not a
one-command fresh-machine installer.

Current final regression results and executable measurements are maintained in
[validation](validation.md). Earlier reports retain stage-specific numbers and
must not be interpreted as the final aggregate executable. NativeAOT executable
size excludes native shared libraries, assets, fonts and other shipping files.
No existing package archive is replaced by this source milestone.

## Known limits and next decisions

- Linux x64 software Vulkan is the validated rendering environment; physical GPU,
  Windows/D3D12 and macOS/Metal are not validated
- Displayed-window UI was tried, but the first Reset after scrolling occasionally
  missed; real IME, accessibility and target-device input still require validation
- Mobile, Web and editor are deferred; the bounded Box2D core is implemented,
  while richer controllers and specialized physics/gameplay systems are not
- UI selection and deployment footprint remain open; full Chinese font packaging
  must be decided separately
- Revisit native-layer validation and platform coverage after the engine model
  stabilizes

The source milestones and local package proof remain experimental. They do not
establish a public package release or additional platform support. See the
[current closure audit](ROADMAP_CLOSURE.md) before treating any historical
follow-on section below as an outstanding task.

## Proportionate test policy

Keep the development loop small. A local change should compile and run its related
focused checks first. Run the complete JIT suite once per feature batch. NativeAOT
and graphics checks are targeted work when interop, serialization, trimming,
rendering or publishing changes; run the necessary aggregate once before a
milestone commit. Reuse verified final-tree results instead of rerunning the same
expensive suite merely to organize documentation.

Large differential/fuzz corpora and binary-size ablation experiments are opt-in
research. They are not the default development or CI loop. A headless pass and a
software-rendered pass must always remain clearly distinguished.

## Phase-1 playable follow-on

The separate `--game-demo` now delivers a repeatable 90-second two-room mission
with authored mission data, explicit RmlUi game screens, pause/focus/input gates,
win/loss, transactional checkpoint replacement and restart cleanup. Start with
[RELAY controls and run instructions](PLAYABLE_MISSION.md); the original room and
settings probes remain available. Final phase-1 counts are recorded in
[validation](validation.md). This does not select RmlUi permanently or broaden the
project into a generic binding, physics, ECS or editor framework.

## Resource follow-on

[Resource roots and texture leases](RESOURCES.md) now provide shared authoring path
validation and a context-owned synchronous BMP cache with explicit per-world
leases. Stable authored keys remain independent of file mappings. Failed candidate
loads keep the prior usable resource set; repeated synchronization remains
allocation-free. Final focused/full-JIT verification is recorded in
[validation](validation.md). Asynchronous loading and general asset packaging remain separate capability
batches; atlas UVs are covered by the later region follow-on below.

## Input and viewport follow-on

The [additive input/viewport contract](INPUT_VIEWPORT.md) now separates logical
window coordinates from framebuffer pixels, preserves event-derived short taps,
routes UI-owned inputs and supports copied/remappable managed bindings. The
interactive mission uses it while preserving fixed-step, focus and neutral gates.
Fresh JIT/AOT and queued SDL/Rml checks are recorded in [validation](validation.md).
World scissor and sprite-frame animation remain rendering work;
RmlUi's existing list scissor is not a world clipping API.

## SDL dependency follow-on

The [isolated SDL 3.4.16 upgrade](SDL_UPGRADE.md) is verified against native,
JIT/AOT, software-rendered and focused displayed-window checks. The older install
and pre-upgrade native libraries remain locally available for rollback. Runtime
version checking prevents accidental use of the retained older SDL. Image
libraries/formats are unchanged; audio integration remains a separate next batch.

## Texture-region follow-on

The [additive region draw contract](TEXTURE_REGIONS.md) now supports validated
shared-texture cells, per-sprite X/Y flips and strict authored v2 metadata. Resource
IDs remain independent of atlas locations and snapshots carry no texture handles
or packed coordinates. Stable ordering and existing full-texture pixels are
preserved. Native, fresh JIT/AOT and software pixels pass as recorded in
[validation](validation.md). The approved minimal SDL_mixer integration can proceed
on the verified SDL 3.4.16 baseline. World clipping and sprite-frame animation stay
as focused rendering follow-ons.

## Audio follow-on

The optional [SDL_mixer module](AUDIO.md) now provides bounded WAV/Vorbis clips,
reusable voices, seekable music streams, master/music/SFX gains and explicit
scene/context cleanup. Offline checks verify actual PCM; dummy-device and displayed
controls are labeled separately from physical speaker/latency acceptance. Generated
audio fixtures stay out of Git and have a verified source generator. The native
module is CMake opt-in and opens no mixer/device until requested. Final verification
and consistent-build footprint comparisons are recorded in [validation](validation.md).

## Physics follow-on

The optional [Box2D foundation](PHYSICS.md) provides fixed-step static/dynamic/
kinematic bodies, circle/box shapes, filters/sensors, copied begin/end events and
bounded closest-ray/broad-phase AABB queries. Units and transform authority are
explicit; scene ownership uses existing teardown hooks. The strict source-generated
fixture and tests do not establish a full character controller, rich authoring
workflow or cross-platform determinism guarantee.

## Animation and timing follow-on

Managed [frame players, typed tweens and polling timers](ANIMATION_TIMING.md) now
share explicit game/real clock steps and bounded lifetime ownership. Atlas frame
keys remain independent of physical packing; optional retained keys prevent lease
churn. The sample and pixel checks cover pause, cancellation, restart and one-shot
retention. This managed-only batch leaves native ABI and serialization roots
unchanged; its current evidence is JIT/native contracts/software pixels and a
displayed cloud-window check, not a fresh AOT milestone.

## TileMap follow-on

The managed [basic TileMap](TILEMAP.md) adds strict source-generated orthogonal
grids, stable atlas IDs, layer order, visibility culling, fixed placement and
optional merged static box collision. It reuses existing native drawing/physics
ABIs and explicit scene cleanup. Frame capacity, physics capacity and unit limits
remain separate checked contracts; richer terrain/controller/editor features
remain outside this slice.

## UI text-input follow-on

The [UI ownership/text bridge](UI_TEXT_INPUT.md) now preserves selected text through composition cancellation, maps candidate geometry to window coordinates, isolates staged focus and retires contexts safely. Scripted composition is distinguished from real OS IME acceptance. The subsequent [typed-binding/list API](UI_BINDINGS.md) adds explicit model projections, stable row IDs and generation/revision-guarded copied actions.

## Typed UI binding follow-on

The reusable [bounded binding profile](UI_BINDINGS.md) supports text, text input,
checkboxes, integer ranges, buttons and dynamically replaced lists. Inventory and
roster fixtures use different IDs/models without new native profile branches.
Unchanged model batches skip interop; invalid projections and snapshots preserve
the live document. UI ownership and composition lifetimes remain explicit.

## Local package follow-on

The [NuGet proof](NUGET_PROOF.md) separates the runtime assembly from demo/test
code and validates independent local-feed PackageReference consumers. Trimming
removes unused managed modules; the deliberately full native profile stays intact.
The measured outputs distinguish app code, native dependencies, assets and .NET
runtime files. This remains a prerelease Linux x64 proof with no remote publishing.

## World clipping follow-on

[World scissor rectangles](WORLD_CLIPPING.md) now provide explicit framebuffer
clipping for region draws, including per-draw or broadcast state and safe coordinate
helpers. Pixel tests distinguish world clipping from tile culling and UI scissor;
legacy entry points remain unclipped and ordering stays stable.
