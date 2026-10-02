# Managed drawing and input entry

These additive experimental APIs avoid native ABI headers and interchangeable
resource integers in ordinary application code. The original `SpriteDraw`,
`SpriteDrawV2`, `MaterialDraw`, `RenderPass`, `InputSnapshot`, `InputBinding` and
mask-based `ActionState` APIs remain available and ABI-compatible for existing
applications and low-level tests. No renderer or native input routing changed.

## Draw ordinary descriptions

```csharp
using var texture = engine.Textures.Acquire(assets, "player.png");
SpriteCommand[] draws = new SpriteCommand[1]; // Reuse across frames.
draws[0] = new(new Transform2D(x, y), new Vector2(32, 32), Vector4.One, texture.Texture)
{
    Region = new TextureRegion(0, 0, 16, 16), FlipX = facingLeft
};
engine.Draw(camera, draws);
```

A `SpriteCommand` carries an existing `Transform2D`, finite nonnegative size, RGBA
tint in [0,1], optional region/flips and optional material/parameters. ABI size,
version, reserved fields and numeric resource IDs stay behind the conversion.
Use `Material = materialLease.Material` and `Parameters = new(...)` for a custom
material. `default(TextureHandle)` means an untextured solid; a region requires a
texture. `default(MaterialHandle)` selects the built-in material and requires zero
parameters. A default `SpriteCommand` is invalid because its transform has zero
scales. Zero sprite extents are valid, matching the existing sprite/native contract.
Camera, clip and shader semantics remain unchanged.

`TextureHandle`, `MaterialHandle` and `RenderTargetHandle` are distinct borrowed
values produced by the **existing owner factories**. They cannot be constructed
from arbitrary numbers or converted between resource kinds. Copying them does not
acquire a lease or keep the native resource alive. Disposing the exact originating
lease invalidates its copied handles even if another lease retains the same cache
entry. Context destruction also invalidates them; submitting to another engine is
rejected, including headless textures whose low-level ID is zero. Owners must still
be disposed explicitly on the creating thread.

For offscreen rendering, sample `target.Texture` and attach `target.Target`:

```csharp
FramePass[] passes = [
    FramePass.ToTarget(target.Target, camera, 0, 1),
    FramePass.Window(camera, 1, 1)
];
SpriteCommand[] draws = [scene, new(new(0, 0), new(320, 180), Vector4.One, target.Texture)];
engine.RenderFrame(passes, draws);
```

A default target is invalid. The window has an explicit factory, never an
interchangeable zero handle. Existing 1..16 pass, final-window, range partition,
clear, feedback and clip validation still applies. Empty passes/command spans are
valid; empty offscreen passes clear their target. Target attachment and sampling
views intentionally refer to the same owner, so read/write feedback remains an
error. Complete native plan validation precedes execution of any pass.

The host converts all managed commands and resolves borrowed resources before
opening a frame. Reused conversion arrays grow only to the requested command count
(up to `MaximumSprites`); pass storage has 16 entries. There is no per-sprite native
call or giant stack allocation. Native submit still validates projection and draws,
and the existing abort path recovers rejected ordinary frames. After warming to the
largest submitted size, conversion/submission allocates no managed memory.

For an extracted world, call `engine.Draw(camera, batch)` directly. To append managed
sprites without exposing/copying the batch ABI array, use
`engine.DrawWithOverlay(camera, batch, overlay)`. This reuses the existing batch and
stable order. Its legacy resource resolver continues to use its existing ownership
and native validation; it is not a second resource system. Compatible adjacent
scene/overlay runs can still batch together.

## Poll copied views and name actions

```csharp
var actions = new InputActionMap();
var moveLeft = actions.AddAction(InputControl.Key(PhysicalKey.A), InputControl.Key(PhysicalKey.Left));
var pause = actions.AddAction(InputControl.Key(PhysicalKey.Escape, allowUiConsumed: true));
// Once per outer frame:
var input = engine.PollInputFrame();
var state = actions.Update(input);
if (input.Quit) return;
if (state.IsPressed(pause)) paused = !paused;
float direction = state.IsDown(moveLeft) ? -1 : 0;
```

`InputFrame` is a copied, read-only value with no public ABI fields or fixed buffers.
`Game` is the UI-filtered primitive view; `Raw` is an explicitly named raw view.
Both expose physical key/button down/pressed/released and `Wheel`. Pointer,
viewport, focus/drawable flags and the consumption summary are frame metadata.
Read `input.Game.KeyPressed(...)` for gameplay. Reading `Raw` does not grant an
action binding permission to bypass UI consumption: the binding must explicitly
set `allowUiConsumed: true`. Text/IME input stays with UI, not physical-key actions.

Tokens are assigned automatically within their map, at most 32 actions and 128
bindings. Multiple controls per token keep the existing alternative-control
semantics. A default token is invalid; tokens cannot be queried against another
map's state. Empty default `ActionState` is useful idle input and returns false for
a valid token. Rebinding an action retains its token identity and deliberately
uses the existing map-wide raw-neutral gate; focus loss still releases held actions
and waits for neutral. Previously copied states remain snapshots of their original
map. The legacy explicit-mask API remains available; avoid mixing it into new
application code.

`pending = pending.Accumulate(next)` keeps the latest held actions and coalesces
pending pressed/released edges. After one fixed step, use
`pending = pending.WithoutEdges()`. This preserves token identity, makes edges
survive zero-step frames and prevents replay on later catch-up steps. Reset pending
to default on application pause/restart as before. These helpers do not create a
new loop or ordered event queue. `actions.CreateState(down: [...], pressed: [...],
released: [...])` creates validated typed synthetic states for pure-rule tests.

The starter uses these drawing/input entries and keeps its visible fixed-step
application policy. RELAY uses the copied input frame and managed overlay entry;
its established game-owned rule masks and historical low-level fixtures remain.


## Stage a complete UI and name physics geometry

First load and replacement now use the same explicit deferred operation:

```csharp
ui.StageAsset(assets, "ui/inventory.rml", initialModel);
engine.Draw(camera, scene); // The normal application frame validates and publishes it.
```

This copies and validates the source **and initial model** in an isolated native
candidate. It does not draw or introduce a loop. `Status.Pending` distinguishes a
candidate from the still-live document; inspect status after the normal frame for
acceptance or a deferred diagnostic. The old model, commands and renderer resources
survive candidate rejection. See [the precise publication contract](UI_MODELS.md#initialization-and-replacement).
Generated and handwritten schemas use the same entry; the recommended public card
consumer now needs one stage plus one normal draw. The additive native staging
entry requires the matching current native runtime; previous ABI records retain
their layouts.

Physics callers can describe geometry without generic `Type/A/B` fields:

```csharp
body.AddShape(PhysicsShapeDefinition.Circle(radius: .5f));
ground.AddShape(PhysicsShapeDefinition.Box(halfWidth: 10, halfHeight: .5f));
```

Factories validate immediately, preserve meter/radian units and the existing
material/filter defaults, and return the existing descriptor. Circle fixes its
unused B/angle to zero; Box names both **half** extents. Explicit zero category or
mask, signed groups and sensors keep their existing semantics. See [physics](PHYSICS.md).

## Cost and verification boundary

The stages 3–4 baseline prepared managed package grew from **229,744 B** at `598a8b1` to
**236,424 B**; its runtime DLL grows from **497,152 B** to **512,512 B**. The
native package is reused without rebuilding or changing its renderer/input ABI.
The draw adapter adds a linear conversion/validation pass and reusable native
storage (136 bytes per peak submitted sprite and 16 × 56 bytes for pass storage).
Zero warmed allocations do not imply zero CPU overhead; callers that deliberately
need the original ABI-shaped path retain it. Batch/world extraction is reused.

Source checks cover copied views, key/button alternatives, mixed filtered/raw
bindings, focus/rebind neutral gates, prior-state identity, default/foreign tokens,
failed changes, one-shot pending edges, borrowed lifetime, replacement contexts,
empty passes, feedback rejection and batching. The native runtime currently allows
only one live context, so replacement-context tests first dispose the old engine.
The stages 3–4 independent public consumer passed 59 checks in both JIT and NativeAOT,
forbids unsafe blocks and compiles eight misuse fixtures to confirm resource kinds and numeric IDs cannot cross the safe surface.

Reproduce with the prepared SDK/native package (no implicit native rebuild):

```sh
source .tools/recovery-env.sh
# Pack the changed managed runtime into the same prepared local feed.
PACKAGE_FEED="$PWD/build-packages/authoring-feed" bash scripts/pack-managed.sh
PACKAGE_FEED="$PWD/build-packages/authoring-feed" \
  PACKAGE_AUTHORING_PROOF_ROOT=/path/outside/checkout/fresh-authoring-proof \
  bash scripts/test-authoring-packages.sh
# Existing maintained-entry checks:
PACKAGE_FEED="$PWD/build-packages/authoring-feed" bash scripts/test-starter.sh
PACKAGE_FEED="$PWD/build-packages/authoring-feed" bash scripts/test-loop-ui.sh
PACKAGE_FEED="$PWD/build-packages/authoring-feed" bash scripts/test-relay-packages.sh
```

The UI/RELAY checks also require the documented licensed font and prepared graphics
prerequisites. Use a filesystem with room for temporary runtime packs; the proof
path is configurable. These checks establish Linux package/JIT/AOT and software
rendering behavior, not hardware input, GPU, audio, high-DPI device or OS IME
acceptance. No public feed publication or stable API/platform promise is added.

## Initialization and shape-factory verification (2026-10-02)

The stages 5–6 source and package checks passed with SDK 10.0.401/runtime 10.0.12:

- Final Release build: zero warnings/errors; eight native headless CTests and
  **11,951** aggregate managed assertions passed
- Generic UI graphics case: **159** assertions, including **62** initialized-stage
  checks. Those cover copied initial data, first-load failures/retry, empty arrays,
  successive pending candidates, invalid projections and image decoding, live
  and queued command retention, retired generations, disposal/reentrancy,
  unchanged zero-allocation applies, source cleanup and bounded image residency
- Candidate render rollback was checked with a one-shot fault in the existing
  probe-only native API, plus equality of actual before/after GPU-readback pixels.
  Real truncated-image decoding failure exercises native resource rejection
  independently; this does not claim real GPU/device failure recovery
- Physics: **156** contract assertions and **403** native simulation assertions,
  including factory defaults, bounds, byte-equivalent ABI records, offsets,
  rotated boxes, sensors, zero filters and collision groups
- Fresh external authoring PackageReference consumer: **80** assertions in both
  JIT and NativeAOT, eight expected misuse compilation failures, and zero warm
  input/draw allocations
- Fresh external generated-UI PackageReference consumer: JIT and NativeAOT pass
  the existing pointer-routing, disabled-command, save/load, reorder/shrink and
  pixel checks; **14** generated-contract checks include one-draw initialization,
  failed replacement and fresh-owner stale-packet rejection. All **12** real
  edit/rebuild cases and the packaged-runtime provenance checks pass

The final proof directories were outside the checkout, with isolated package
caches; temporary files were placed on workspace storage after `/tmp` filled
on an earlier incomplete attempt. The corrected final proofs both pass against
matching managed/native packages. No package identity or version scheme changed.
Native packaging reused the already rebuilt runtime without recompiling it.
A focused read-only review found and prompted correction of a fresh-owner stale
packet check; the added source and public JIT/AOT regressions pass.

Graphical evidence is offscreen software Vulkan on Linux x64, including an
inspected initial-card capture. Physical GPU, real OS IME and other-platform
acceptance remain separate gates. There is no new component runtime, controller,
render loop, binding language or automatic physics/render synchronization.
