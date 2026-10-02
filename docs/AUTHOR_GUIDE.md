# Current author guide

**Start here when making a game.** This page describes the current public author
path, rather than the order historical experiments were built. Use the checked-in
[ordinary C# starter](../templates/Starter/README.md), then choose only the modules
your game needs. Focused links below specify the detailed bounds and contracts.

The current runtime is experimental .NET 10 / `0.1.0-preview.1`, namespace
`GameAuthoringLab`. The API is not stable. The distributable binary proof is Linux
x64 only, with explicit system prerequisites. Existing source support and past
AOT/device experiments do not establish support for every target platform.

## 1. Begin from a real package consumer

Copy `templates/Starter` outside the repository. Follow its README to supply the
two local packages and explicitly restore/build. The project references no sample
executable, source files, friend assembly or private interop. You do not need the
prototype's enormous `Program.cs` flag switch to make your game.

From a prepared source checkout, the smallest repeatable validation is:

```sh
# DOTNET may point at your existing .NET 10 SDK; no dependency installs occur.
DOTNET=/path/to/dotnet bash scripts/test-starter.sh
```

The script copies the starter outside the repository and restores both packages
into a fresh isolated cache. The checked-in template has no absolute machine paths;
the script's temporary evidence paths are local execution outputs only. To build
packages yourself, see [managed package proof](NUGET_PROOF.md) and
[native package proof](NATIVE_PACKAGE.md). Package creation is an explicit setup
step, not a per-edit rebuild of SDL, UI or physics.

The default graphics native package includes SDL_image raster support, RmlUi,
SDL_mixer and Box2D. Its SVG option is **OFF**. Keep its bundled libraries and
license files together. Its exact glibc/C++ requirements, Vulkan/display and audio
prerequisites are in [the native package README](../packaging/native/README.md).
A `linux-x64` RID does not mean this binary runs on every Linux distribution.

## 2. Own a small explicit main loop

The starter keeps the author-visible order in `Program.cs`:

1. Resolve assets, then create `EngineHost`
2. Create resource leases and game-owned optional modules
3. Poll input once, map actions once, process outer-frame pause/restart
4. Supply elapsed time to `FixedStepInput.BeginFrame`, drain `TryTakeStep`
5. Apply rules/input, advance physics once, consume events, synchronize presentation
6. Draw when drawable; dispose game/module owners, leases, then engine

`FixedStepInput` is copyable application code, **not a new runtime base class**. It
only reuses the accumulator/edge logic repeated by the platform and top-down
prototypes. No hidden callback order, reflection, automatic behavior discovery or
service container was added. See [measured comparison](AUTHOR_ENTRY_COMPARISON.md)
for exactly what moved and what remains author work.

Define action bits and bindings in game code. Use `InputActionMap`, not the sample's
`CreateSample()` defaults. Gameplay controls respect UI consumption unless an
individual binding explicitly opts out. Input edges coalesce, survive zero-step
frames and apply to only the first catch-up step. Raw physical scancodes are not
text input; use the UI composition bridge for text. See [input](INPUT_VIEWPORT.md).

The default policy pauses gameplay on Escape, focus loss or no drawable area,
clears pending edges/debt and does not catch up after resuming. T resets the game
and loop while keeping pause. The loop caps each frame to 0.1 seconds, records
excess time and drains all accepted steps. These are visible application decisions;
edit them if minimized background simulation or a different pause policy is needed.

Use a consistent fixed step for rules, physics and fixed gameplay animation. Use
`TimingStep` to explicitly select game vs real-time advancement for UI/timers.
Do not advance the same animation with both frame and fixed time. The starter's
real-time delta is also capped; supply uncapped time explicitly when required.
[Animation/timers](ANIMATION_TIMING.md) explains pause, expiry and marker bounds.

Headless mode is a deterministic finite CLI clock. It does not test live input,
raster decode, actual pixels, GPU/device behavior, audio hardware or real IME.

## 3. Choose state and presentation deliberately

- **Pure rules first:** ordinary C# types can hold health, items, turn state or
  movement policy and be tested without constructing an engine
- **Simple sprites:** submit explicit `SpriteDraw` arrays, as in the starter
- **Related entities/scenes:** create a `World`, then explicit `Scene`/`Entity`
  objects, or load a strict `AuthoredScene`. Extract through `SpriteBatch`
- **Content reuse:** ordinary C# factories are valid. There is no automatic prefab
  inheritance, arbitrary serialized component registry or behavior discovery

`World` deliberately separates transform parent, lifetime owner and scene
membership. One entity has one behavior slot; compose a behavior explicitly if
needed. Direct `entity.Behavior = value` is a **non-owning** reference to the newly
assigned value. Use `world.AttachBehavior` and its `OnDetach` registration for
owned subscriptions/resources. Replacing/clearing an attached behavior retires
its registered cleanup; it does not magically dispose every `IDisposable` assigned
through the property. Same-instance property assignment is a no-op. Cleanup
failures are aggregated after the world transaction commits; don't replay it.
See [lifetime/sorting](LIFECYCLE_SORTING.md) and [World](../managed/WORLD.md).

Authored scene JSON is immutable author input, with strict versions/unknown-field
checks and stable IDs. It is **not a player-progress save format**. Sample
`ScenePersistence`, room/mission saves and sample UI profiles are not public package
APIs. Own a versioned game save model and explicit validation/migration/restart
policy; do not serialize native handles. [Scene authoring](AUTHORED_SCENES.md) and
[resource mapping](RESOURCES.md) show the current source contract.

## 4. Resource identity, units and disposal

Capture one explicit `AssetRoot`. The starter chooses the built app's `assets/`
and supports `--assets PATH`; it is independent of the current directory and does
not silently read `GAL_ASSET_ROOT`. Use `EngineAsset` items to copy selected files
to build/publish output. Authored-scene resource paths are relative to the scene
file; logical keys are case-sensitive. Avoid traversal, URIs and descendant links.

Raster resources support BMP, PNG and JPEG. Use `ReadImageInfo` for general image
preflight; `ReadBitmapInfo` remains a deliberately BMP-only compatibility method.
Validation checks bounded metadata; native decoding is authoritative. Use
`TextureCache.Acquire` leases directly or `TextureBank` with a catalog and sprite
batch. A live lease retains its snapshot after a file edit; release/reacquire for
an explicit reload. There is no implicit file watcher, async load or atlas packer.
See [images](UI_IMAGES.md), [regions](TEXTURE_REGIONS.md) and [resources](RESOURCES.md).

Use `using`/`try-finally` around owners on the creating thread. Reverse declaration
order should release a game/session, its resource leases, then `EngineHost`.
Factories own native wrappers: open physics/audio via the engine. Avoid arbitrary
handle construction, cross-engine resource reuse and disposing resources in a
borrowed span's lifetime. Constructor failure paths must release already-created
owners. The starter demonstrates rollback after physics-body setup failure.

Physics uses meters/seconds/radians; rendering and camera units use framebuffer
pixels. `--physics` shows one authoritative physics pose copied after `Step`, using
`PhysicsScale` at the boundary. If projecting onto an entity, map center-based body
coordinates to your sprite's local origin explicitly and write local transforms
only when appropriate for its transform parent. No implicit entity-body binding
exists. Never overwrite both poses in competing systems each frame.

`PhysicsWorld.Step()` performs one configured fixed step. Consume `Events` before
the next step/close, or copy events you need to retain. A nonzero `Dropped` count
means the step already completed; report lost events rather than retrying physics.
Exact overlap queries/capsules are available, but grounded/sliding/slope/moving-
platform rules are still game code. [Physics](PHYSICS.md) states current limits;
[camera follow](CAMERA_FOLLOW.md) handles explicit framing; [tile maps](TILEMAP.md)
cover synchronized cell/collision edits. The starter does not introduce a generic
character controller or encode game-specific rules in the runtime.

## 5. Add UI only when it helps the game

Use public `BoundUiSession<T>` with explicit delegates and a model projection.
`GameUiSession`/settings profiles belong to examples, not a reusable package SDK.
RML/RCSS is a bounded RmlUi profile, not browser HTML/CSS. The public
`session.LoadAsset(assets, path)` validates and stages the source internally;
`BoundUiAuthoring` is not public. Render once to publish the accepted document,
then call `session.Apply(model)` explicitly. `session.Poll()` returns a copied
`UiBindingAction`, not a borrowed span. Check `session.IsCurrent(action)` before
applying an action to game state: this validates its generation, revision, target
and enabled state after changes. Drain or bound the queue deliberately, update the
model, and apply its next projection. [Bindings/lists](UI_BINDINGS.md)
and [text input](UI_TEXT_INPUT.md) describe current contracts.

The current bound profile permits at most 32 targets and 64 total list rows per
document. `UiListRow` has stable ID, text, enabled and selected state. It is useful
for menus and bounded inventories; it is not an arbitrary rich card/slot template,
drag/drop framework, virtualized list or automatic focus-navigation system.
Static raster UI images are separate from rich dynamic row structure. Optional
[SVG](UI_SVG.md) needs an SVG-on native build and remains off in the default package.

**Fonts are not supplied by the repository or NuGet packages.** When adding UI,
require an explicitly selected, readable `GAL_UI_FONT` before opening the session:

```csharp
string? font = Environment.GetEnvironmentVariable("GAL_UI_FONT");
if (string.IsNullOrWhiteSpace(font) || !File.Exists(font))
    throw new InvalidOperationException("Set GAL_UI_FONT to a readable compatible font you are licensed to use.");
```

The existence check is only setup feedback; verify compatible face/glyph coverage
in actual UI. Do not assume a developer machine's Noto path exists on the user's
machine. Copy a font into your own distribution only when its license permits it,
and retain its notices. No font is installed or bundled by this starter.

## 6. Diagnose and validate at the right cost

Missing assets report root, logical name and stable error code; strict authored
JSON also reports field/path/line context. Native-load errors require checking the
selected package and system prerequisites, not copying random shared libraries.
The starter README keeps focused startup checks and commands next to the app.
Use `DiagnosticLog`, `CpuTimings` and debug geometry explicitly when useful; they
are not a full editor/inspector/profiler, and render CPU time is not GPU time.
[Diagnostics](DIAGNOSTICS.md) has bounds and overlays.

Daily iteration: build JIT, run pure rules, starter self-checks and the relevant
focused contract test. Rendered changes need a real rendered check. ABI,
serialization, trimming roots or package-boundary changes justify a fresh trimmed
or NativeAOT publish; a documentation/template-only batch does not need the full
native/graphics/AOT matrix again. AOT suitability is not the same as a fresh AOT
execution result. Record which path you actually ran. [Test tiers](TESTING.md)
separates quick, integration, rendering, device and release evidence.

The starter and guide remove a repeated starting decision, not the remaining
content-authoring tools gap. Layout/level editing, rich UI and robust character
motion still deserve requirements from a real game before new general APIs.
