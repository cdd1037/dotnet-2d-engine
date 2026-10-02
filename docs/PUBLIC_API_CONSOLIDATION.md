# Public authoring API consolidation

The six author-entry changes now define the public preview API. Maintained package
consumers and the starter use them directly. Superseded managed entry points are
internal or removed; there are no compatibility facades. The native C ABI, resource
ownership and renderer/input protocols are unchanged.

## Public paths

- Draw `SpriteCommand` descriptions, extracted `SpriteBatch` instances, or
  `DebugDrawBuffer` geometry. Use typed borrowed texture/material views and
  `FramePass.Window` / `FramePass.ToTarget` for explicit passes
- `TextureBinding` carries `TextureHandle` and an optional region. World/tile
  extraction still accepts `TextureBank.ResolveRegion` or a custom typed resolver
- `SpriteBatch.Commands` and `DebugDrawBuffer.Commands` are borrowed views for
  explicit pass/clip/material composition. Copy into reusable caller-owned storage
  when editing descriptions. Views expire on extraction/geometry mutation
- Poll `InputFrame` once per outer frame and use its Game/Raw views. Allocate
  `InputAction` tokens from a map, query typed `ActionState` values, and use
  `CreateState` for synthetic rule tests
- UI uses `UiModelSession<T>`, generated declarations or handwritten `UiRecord<T>`
  projections, typed `UiCommands.On`, `Poll`/`IsCurrent`/`Dispatch`, and
  `StageAsset(assets, path, initialModel)` followed by the normal application draw
- Construct circle/box geometry with `PhysicsShapeDefinition.Circle` / `Box`.
  Descriptor observations and material/filter/offset configuration remain public

`Camera`, `Stats`, clipping, material parameters, render targets, manual/computed
UI projections, copied event inspection, capsule/query APIs and the ordinary world,
tile, timing, audio and physics modules remain available. UI packet command IDs
are observable session-local details, never application-assigned action identities.

## Superseded surface removed from public access

- `Sprite`, `SpriteDraw`, `SpriteDrawV2`, `MaterialDraw`, `RenderPass`, and
  `EngineHost` overloads taking those records, including raw overlay/clip paths
- `SpriteBatch.Sprites`, `Draws`, `RegionDraws`, and numeric `TextureResolver`;
  `DebugDrawBuffer.RegionDraws`; numeric `TextureBank.Resolve`
- Numeric `TextureLease.Handle`, `MaterialLease.Handle`, `RenderTarget.Handle`,
  and the numeric `TextureBinding` constructor
- `InputSnapshot`, `InputBinding`, `InputFlags`, `EngineHost.PollInput`, explicit
  binding-array map construction/rebinding, snapshot-based Update, CreateSample,
  and ActionState's numeric constructor/masks/deconstruction
- `BoundUiSession<T>`, `UiBindings<T>`, `UiBindingKind`, `UiBindingTarget`,
  `UiBindingAction`, and `UiListRow`
- Source-only `UiModelSession<T>.LoadAsset`, numeric `UiCommands.Add` and `On`
  overloads, and `UiCommandAttribute.Id` / generated numeric-ID support
- The generic positional `PhysicsShapeDefinition` constructor and public
  modification of its Type/A/B geometry fields

Internal protocol fixtures retain access where they test malformed ABI records,
explicit packet IDs, rejected source-only staging or historical implementations.
They are not examples to copy into a package consumer. Internal visibility is an
API boundary, not a security boundary against deliberately forged friend assemblies.

## Ownership, precision and costs

Typed resource values remain borrowed: copying a command/view does not acquire a
lease. Extracted batches retain the exact originating leases as borrowed references
and validate them before opening a frame. Disposing that lease invalidates its
views even if another cache lease remains. Previous-context resources are rejected.
Empty disabled debug geometry references no texture. Sorting and tile appends keep
semantic commands, raw geometry and resource identity in the same stable order.

Debug semantic commands retain the generated affine basis exactly, including very
long vertical lines. Changing material/tint/texture preserves it; explicitly replacing
Transform replaces the basis. Reconstructing a command from its decomposed float
rotation can lose precision, so copy the command itself for material/pass composition.

Semantic command storage increases batch/debug capacity memory alongside the
internal native arrays. Arrays are reused; capacity growth/sorting setup can
allocate, while warmed extraction/submission and typed input remain allocation-free.
The drawing adapter still adds validation/conversion work. This is no claim of
zero CPU cost or unchanged memory usage.

## Verification scope

Focused source tests, the final aggregate, compile-negative public API fixtures,
fresh PackageReference JIT/NativeAOT authoring and generated-UI proofs, generator
regressions, and the maintained consumer checks are recorded in
[validation](validation.md). The source-linked XML differential harness also builds
with the shared parser/descriptors; its deep corpus is a separate opt-in test.

The native package is reused; no native rebuild or public package publication is
part of this consolidation. Linux software Vulkan checks do not establish physical
GPU/input/audio, real OS IME, or other-platform acceptance.
