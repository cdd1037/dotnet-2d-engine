# .NET 2D Engine — next stages

Planning document created after foundation checkpoint `cf3ebe6`. These are
proposed delivery stages, not claims of implemented support or approval to install,
publish or deploy anything. Use small playable examples to validate general-purpose engine capabilities;
one sample is a starting point, not the engine feature ceiling.

## Current progress and immediate order

Phase 1's playable loop is committed locally as `105eb06`. Continued incremental
engine implementation is authorized (2026-10-01); external publication and new
dependency selections remain separate decisions. The resource batch adds
shared root/path validation, stable key mappings and explicit context-owned BMP
texture leases; see [resource foundation](RESOURCES.md).

The input/viewport batch now adds versioned raw/routed edges, coordinate helpers
and managed action bindings; see [input contract](INPUT_VIEWPORT.md). Rendering
now includes [texture regions/atlas UVs](TEXTURE_REGIONS.md) and managed
[frame animation, tweening and timers](ANIMATION_TIMING.md), plus
[world rectangular clipping](WORLD_CLIPPING.md) and bounded
[sprite materials/offline GLSL](MATERIALS.md). Box2D and SDL_mixer
evaluation/integration are authorized after version, license, footprint and platform
validation. The separate [SDL 3.4.16 upgrade](SDL_UPGRADE.md) is now validated.
The minimal [SDL_mixer audio module](AUDIO.md) now covers clips, streams, gains and
explicit lifetimes. The [Box2D foundation](PHYSICS.md) now adds bounded bodies,
shapes, events and queries with explicit units/ownership. The managed
[basic TileMap](TILEMAP.md) adds bounded authored grids, chunk-assisted visibility
culling and optional generated static collision.
The [UI text-input/ownership follow-on](UI_TEXT_INPUT.md) now validates the
composition bridge and candidate-coordinate contract synthetically. The
[typed UI binding/list profile](UI_BINDINGS.md) adds explicit C# projections and
bounded stable-ID list mutations. Real desktop IME acceptance remains outstanding.
The advanced-effects group remains deferred.
The delivery stages below remain useful acceptance targets, not a replacement for
this engine-capability order.

## 1. Turn the sample into a repeatable small game

Goal: extend the current two-room/pickup/save/menu slice into a representative
title → start → play → pause → win/lose → save → reload → restart loop. Move suitable authored content
out of hard-coded sample logic without prematurely designing a full prefab system.

Exit: the same authored content loads twice without shared mutable runtime state;
scene transitions, ownership cleanup, save/reload and restart are reliable; an
invalid resource reports its source location and leaves the last usable state.
Use focused tests during iteration and one full JIT pass at the feature boundary.

## 2. Make the UI useful beyond the settings probe

Goal: small explicit C# model/event contracts for changing text, dynamic lists,
inventory selection, focus and gameplay-driven UI, with controlled teardown/reload.
Keep AI data edits testable through file/field/source-position diagnostics. Reproduce and
resolve the intermittent first Reset after scrolling before calling input robust.

Exit: the playable slice drives UI state and list changes without native callback
reentrancy, stale events or leaked attachments. Validate text entry/IME on a real
target desktop. Compare RmlUi, Myra, Gum and an engine-owned option against this
actual use case before making a permanent framework choice.

## 3. Consolidate assets, builds and distribution

Goal: predictable resource paths, asset validation and repeatable build/publish
commands using explicit tool versions, lightweight defaults and only the minimal
real audio/input needs demonstrated by the game. Separate reusable runtime concerns from
test/probe payloads when justified by real shipping needs, not cosmetic size goals.

Exit: a clean machine with documented prerequisites builds the sample; a package
runs without this workspace's sibling directories; dependency/font notices and
missing-resource diagnostics are complete. Measure the entire distribution,
including native libraries/assets/fonts, independently of the host executable.
Keep large differential/size experiments opt-in.

## 4. Validate desktop platforms incrementally

Goal: validate Windows first, then macOS incrementally, beyond cloud software
Vulkan. macOS requires its own Metal/shader and native packaging work. Choose
and document the Windows graphics backend and shader workflow rather than
assuming the existing HLSL file establishes D3D12 support.

Exit: gameplay/UI/save/reload, resize, focus loss, minimize/restore, repeated input,
audio and error recovery pass on physical hardware. Record exact drivers/platforms
and distinguish functional checks from performance measurements. Revisit native
validation boundaries once the managed contracts are stable.

## 5. Evaluate mobile; keep Web and editor gated

Android/iOS begin later with a small feasibility slice covering application lifecycle,
touch, graphics backend, text/IME and AOT/package restrictions; it is not a desktop
port support claim. Decide supported devices and acceptance criteria from results.

Web remains a separate feasibility decision pending the SDL/browser graphics
backend and .NET runtime/toolchain path. Do not promise that desktop NativeAOT
transfers directly to Web.

A visual editor comes later, using the same canonical IDs, resources, schemas and
validated operations as files/CLI. Blazor, engine-self-hosted and hybrid approaches
remain open. Define versioned metadata and lossless round-tripping before an
editor adds a second authoring surface. A Godot fork is outside this project.

## Engine capability track (alongside the stages above)

The stages above describe integration, authoring and delivery. They do not replace
building reusable 2D engine functionality. Phase 1 is complete and focused resource/input/rendering
batches are being advanced incrementally. The following remains a prioritized
roadmap, not a dependency selection or instruction to implement everything immediately.

### A. Resources, scenes, rendering and animation

- General resource cache and explicit lifetime, scene transitions and reusable
  entity compositions with stable identity. Async loading, hot reload and prefab
  overrides were explicitly deferred in the later scope decision; this older
  capability list does not reactivate them.
- Sprite atlases, UV regions, cameras, clipping and stable draw order; materials,
  standard shader authoring/build workflow, render targets and basic post-processing
- Sprite-frame animation and tweening, with explicit update/pause/lifetime rules

Existing sprites, camera, transforms, sorting and flat scene JSON are foundations,
not evidence that the complete capabilities above exist. Exit: at least two small
examples with different content reuse the same APIs without sample-role hardcoding;
resource failure/reload and animation teardown remain predictable.

### B. World construction, physics, input and sound

- Tilemaps and level data, including tile collision generation
- Collision queries, triggers, character movement and rigid bodies; evaluate a
  mature 2D physics library before implementing a general solver ourselves
- Input actions, remapping and focus/capture; touch and gamepad support later,
  aligned with the platform schedule (not added to phase 1)
- Real sound effects and streamed music, voice lifetime and volume groups
- Reusable timers, save integration, debug drawing, logging and basic profiling.
  The current [bounded diagnostics](DIAGNOSTICS.md) slice supplies line/rectangle
  geometry, a structured log FIFO and opt-in CPU frame/phase timings

The earlier AABB/legacy-tone samples remain separate from the now implemented
bounded Box2D and SDL_mixer foundations. Those modules do not themselves satisfy
the movement-example and actual-device acceptance below.
Exit: a tile-based movement example and a different interaction/physics example
exercise shared APIs, and input/audio work on an actual target device. Resource
and update ownership must remain explicit across pause, scene changes and restart.

### C. Effects and specialized capabilities — deferred

User decision (2026-10-01): defer this entire enhancement group. Initial scope
targets ordinary top-down, side-scrolling action and platform games; mass-unit RTS
and large simulation/management games are not initial priorities. Do not add ECS
or parallel scheduling solely for those deferred workloads.

Later, evaluate particles, 2D lights/shadows, navigation/pathfinding and skeletal animation
against concrete game needs. Prefer suitable maintained dependencies where they
reduce total maintenance. Do not promise complete Godot feature parity or create
large abstract frameworks before the required semantics are understood.

### Ordering and integration

After the playable phase-1 loop, progress resource/scene and render/animation work
alongside UI/data authoring. Then add tile/world/physics and input/audio capabilities,
while improving project/export tooling. Desktop validation should accompany each
relevant capability rather than wait until every feature exists. Advanced effects,
mobile, Web and editor work retain their separate gates. Use multiple small examples
to avoid designing the entire engine around one game.

## Working constraints across all stages

- Scope small edits to quick compile and related CPU checks
- Full JIT once per feature batch; AOT/graphics when the affected boundary needs it
- One necessary final aggregate per milestone; preserve evidence provenance
- No remote publication, releases or new platform-support claims by implication
- No full ECS, generic physics framework or editor expansion without a concrete
  need demonstrated by representative game examples

## Local NuGet packaging proof

The [local-feed proof](NUGET_PROOF.md) separates the managed runtime from the demo/
test host and validates independent empty/sprite/UI PackageReference consumers in
framework-dependent, trimmed JIT and AOT modes. It uses ordinary NuGet/MSBuild.
Native profiles, assets, notices and loader dependencies are measured separately;
prebuilt native functions are not removed by managed trimming.

This completes the bounded local proof, not distribution/platform acceptance.
Some implemented module APIs remain internal; broader public surfaces need their
own consumer tests. Clean-machine setup, Windows/macOS native packages and real
device acceptance remain open. Public NuGet publication and a custom SDK are not
part of this proof. Source generation remains explicit; no blanket assembly roots
or trim/AOT warning suppression were introduced. See the [closure audit](ROADMAP_CLOSURE.md)
for remaining functional work and named validation limits.

## Deferred comparison

Near completion of the currently agreed roadmap, perform a comprehensive
Godot/Unity 2D capability comparison. Newly discussed advanced gaps are deferred
and are not added to this implementation scope now.
