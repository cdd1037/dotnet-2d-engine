# .NET 2D Engine — foundation milestone

Status: experimental independent engine, not a production release. The proposed
repository name is `dotnet-2d-engine`. Source directories, `GameAuthoringLab`
managed names and the `gal` native ABI remain unchanged to avoid a disruptive
rename during integration.

## What is here

- `managed/`: .NET 10 executable, ordinary-object World, components, authored
  scene loading, gameplay, persistence, UI preflight and regression tests
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
- Mobile and Web are deferred; no editor or general physics system is delivered
- UI selection and deployment footprint remain open; full Chinese font packaging
  must be decided separately
- Revisit native-layer validation and platform coverage after the engine model
  stabilizes

This milestone is local Git history only. Remote creation, upload, release
packaging and repository publication are separate actions.

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
[validation](validation.md). Asynchronous loading, atlas UVs and general asset
packaging remain separate capability batches.

## Input and viewport follow-on

The [additive input/viewport contract](INPUT_VIEWPORT.md) now separates logical
window coordinates from framebuffer pixels, preserves event-derived short taps,
routes UI-owned inputs and supports copied/remappable managed bindings. The
interactive mission uses it while preserving fixed-step, focus and neutral gates.
Fresh JIT/AOT and queued SDL/Rml checks are recorded in [validation](validation.md).
Texture regions, world scissor and sprite-frame animation remain rendering work;
RmlUi's existing list scissor is not a world clipping API.

## SDL dependency follow-on

The [isolated SDL 3.4.16 upgrade](SDL_UPGRADE.md) is verified against native,
JIT/AOT, software-rendered and focused displayed-window checks. The older install
and pre-upgrade native libraries remain locally available for rollback. Runtime
version checking prevents accidental use of the retained older SDL. Image
libraries/formats are unchanged; audio integration remains a separate next batch.
