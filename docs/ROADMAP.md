# .NET 2D Engine — next stages

Planning document created after foundation checkpoint `cf3ebe6`. These are
proposed delivery stages, not claims of implemented support or approval to install,
publish or deploy anything. Use small playable examples to validate general-purpose engine capabilities;
one sample is a starting point, not the engine feature ceiling.

## Current milestone closure (2026-10-02)

The agreed basic Linux capability slice is delivered at its documented CPU and
software-Vulkan boundaries. Recent additions are [raster UI images](UI_IMAGES.md),
[optional static SVG](UI_SVG.md), [camera follow](CAMERA_FOLLOW.md),
[polled frame markers/clip selection](ANIMATION_TIMING.md),
[transactional runtime TileMap edits](TILEMAP.md), and
[capsule shapes/exact circle and rotated-box overlap](PHYSICS.md).
They extend the existing resource/input/atlas, clipping/material/target,
audio/physics, typed-binding and bounded-diagnostics foundations.

The existing independent feature fixture now has a small
[combined author smoke](AUTHOR_SMOKE.md): frame markers edit a floor, exact queries
and a real capsule observe the same collision change, the camera follows, and a
typed UI with raster/SVG artwork renders the result. Current
[Sprite/UI package sizes and trimming](MILESTONE_PACKAGE_SIZES.md) are measured
separately from that SVG-on source-profile smoke. The default native package
continues to disable SVG; optional SVG overhead is identified explicitly.

This closes the approved feature wave. Pause feature implementation here; do not
turn historical proposals or newly discovered gaps into automatic scope. The
remaining decisions are platform/device acceptance, real OS IME, permanent UI
choice and broader distribution, as enumerated in the [closure audit](ROADMAP_CLOSURE.md).
Advanced effects, async/hot-reload/prefab expansion, richer controllers, editor,
mobile and Web remain deferred. The stages below retain the longer-term goals and
acceptance criteria; they are not a new implementation queue.

### Subsequently approved authoring iteration

The later authoring sequence reopens these bounded tasks, in order:

1. Close the [generic UI model/event bridge](UI_MODEL_VALIDATION.md)
2. Prove [C# composition reuse](CSHARP_COMPOSITION.md) with parameterized typed
   factories, nested instances, rollback and aggregate cleanup. The reusable
   engine addition is destruction-only registration; factories remain game code
3. Use the [checked explicit loop recipe](GAME_LOOP_RECIPE.md) for input/pause,
   restart and physics-to-display wiring, preserving caller-visible timing,
   ownership and game rules; no new loop framework is justified by the examples
4. Compare pure-code authoring with Godot after those improvements; exclude
   editor and visual-tooling differences from that comparison

All four are implemented at their documented validation boundaries, including
the [pure-code comparison](CODE_AUTHORING_COMPARISON.md). The loop stage adds a tested
recipe and presentation history, not a measured LOC or authoring-speed reduction.
The later typed UI command/diagnostic iteration and the [public-package RELAY
experiment](RELAY_REFERENCE.md) are also delivered as bounded authoring stages.
RELAY retains its original game/save rules and runs the whole flow outside the
checkout with ordinary PackageReferences. It remains an experiment; additional
package identity/version-management work is deferred. None of
these stages authorizes broader deferred capabilities or public package
distribution, and the sequence does not revive prefab inheritance.

## Two complementary development tracks

Future work alternates between two evidence-driven tracks:

1. **Real projects reveal necessary engine capabilities.** Build and maintain
   complete games against the public package boundary. Use actual ownership,
   interaction, data and validation failures to identify the next useful engine
   capability; do not turn a one-off game's rules into a generic subsystem
2. **API, diagnostics and tooling reduce authoring friction.** Remove duplicated
   facts, hidden change dependencies and easy misuse. Evaluate clear contracts,
   local change reasoning and actionable errors, rather than file count, shortest
   code or moving all code into one file

A real project can expose a missing capability or a confusing authoring contract;
API/tooling work must then be checked in that project and independent consumers.
Neither track automatically takes priority forever or licenses unrelated feature
expansion. Preserve explicit timing, ownership, serialization and failure policy.

### Approved next author-API phase, after RELAY closure

This bounded sequence is approved for compatible, individually verified
batches. Completed author-API batches are marked below; unmarked items remain
next-stage work beyond the RELAY migration:

1. Automatically assign UI command IDs on the recommended author path; retain an
   advanced explicit-ID path and update recommended examples/docs. **Implemented:**
   typed `On` overloads reserve explicit IDs before deterministic session-local
   assignment; runtime/native tests cover mixed registrations and stale dispatch
2. Make C# declarations the single source for UI registration and build-time RML
   name/type/command contract checks, with source-aware diagnostics and runtime
   fallback for dynamic content. Constants alone do not solve this. Bounded
   generated registration/build checks are now in scope, superseding the earlier
   general generator deferral only for this contract. **Implemented:**
   [opted-in C# DTO/handler generation](GENERATED_UI_CONTRACTS.md), shared static
   preflight and source-aware build diagnostics; dynamic proof limits stay explicit
3. Provide safer public rendering/input surfaces and typed resource handles,
   keeping raw/native representations behind an explicit advanced boundary. **Implemented:**
   [managed sprite/pass descriptions, borrowed typed handles and copied input views](SAFE_AUTHORING_BOUNDARY.md);
   existing ABI-shaped APIs remain compatible
4. Prefer input action tokens over manually allocated action bits on the ordinary
   author path, while preserving the existing implementation's semantics. **Implemented:**
   map-allocated tokens, typed queries/rebind and identity-preserving edge helpers;
   starter and its pause-menu use them, with raw/UI/focus policy kept explicit
5. Add an explicit UI initialization/publication helper that preserves failed-load
   or failed-reload retention and caller-visible update/render ownership
6. Add semantic Circle/Box shape factories so authors do not rely on ambiguous
   parameter positions or shape-discriminator values

Keep existing consumers working during migration, use targeted negative tests,
and remeasure package/AOT boundaries when the affected contract demands it.
This approval does not include a Vue-style component runtime, editor, automatic
scene lifecycle or broader platform/distribution work.

The numbered stages below preserve the original proposals and exit criteria.
Use the current status above and linked validation reports when historical wording
describes work that has since landed; this list is not an automatic work queue.

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
building reusable 2D engine functionality. Phase 1 and the agreed focused resource/input/rendering
feature wave are complete at the documented validation boundaries. The following remains a prioritized
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
the actual-device acceptance below. The [tile movement fixture](TILE_MOVEMENT.md) now
passes scripted solver/rendering and displayed cloud X11 controls, including fixed-step
input boundaries, wall/floor collision, triggers and restart.
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
- No releases, new package-feed publication or new platform-support claims by implication
- No full ECS, generic physics framework or editor expansion without a concrete
  need demonstrated by representative game examples

## Local NuGet packaging proof

The [local-feed proof](NUGET_PROOF.md) separates the managed runtime from the demo/
test host and validates independent empty/sprite/UI PackageReference consumers in
framework-dependent, trimmed JIT and AOT modes. It uses ordinary NuGet/MSBuild.
Native profiles, assets, notices and loader dependencies are measured separately;
prebuilt native functions are not removed by managed trimming.

This completes the bounded local proof, not distribution/platform acceptance.
Raw interop/probes and selected implementation helpers remain internal.
Clean-machine setup, Windows/macOS native packages and real
device acceptance remain open. The [public optional-module boundary](PACKAGE_API_NEXT.md)
now makes existing animation/timing, audio, physics and TileMap usable by an
independent JIT/AOT consumer; this remains an experimental API. Public NuGet publication and a custom SDK are not
part of this proof. Source generation remains explicit; no blanket assembly roots
or trim/AOT warning suppression were introduced. See the [closure audit](ROADMAP_CLOSURE.md)
for remaining functional work and named validation limits.

## Deferred comparison

Near completion of the currently agreed roadmap, perform a comprehensive
Godot/Unity 2D capability comparison. Newly discussed advanced gaps are deferred
and are not added to this implementation scope now.
