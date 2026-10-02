# Basic orthogonal TileMap

A source-generated, bounded grid asset and an explicit placed instance reuse the
existing atlas, draw, resource and optional Box2D contracts. Cells are not entities.
No native ABI or existing scene/save version changes are needed.

## Source versus runtime data

`TileMapAsset.LoadAsset(root, "basics.tilemap.json")` loads `gal-tilemap` version 1.
The source contains grid dimensions, positive pixel tile sizes, resources, tile
IDs and dense cell layers. `TileMapAsset.Write` validates the same DTO and emits
canonical source-generated JSON. Runtime handles, occupancy chunks and generated
physics rectangles are never serialized.

- `resources`: the existing key/path/optional integer-texel region format. Paths
  are relative to the tilemap source's directory within its `AssetRoot`, exactly
  like authored scenes. Traversal, symlinks and BMP/region bounds keep the existing
  resource contract. Every declared resource is validated at load
- `tiles`: unique numeric `id` in 1..1024, registered `assetKey`, optional `solid`
  (default false). Reordering this palette does not change cell meaning. Resource
  keys remain independent of atlas filenames/coordinates
- `layers`: unique name, required signed integer `order`, optional `opacity`
  (default 1), and exactly width×height row-major integer `cells`. Zero means empty;
  other values must be registered tile IDs
- Optional `flips` is a JSON **integer array**, one entry per cell: X=1, Y=2, both=3.
  It is omitted/null for all-zero flags. Empty cells must have zero flags. No
  rotated/trimmed atlas packing or diagonal/transposed tile bit is interpreted

Limits: 2 MiB UTF-8 source, dimensions 1..256 on each axis, 1..8 layers, at most
131,072 total cell slots across layers, 1..256 tile definitions/resources, tile
pixel width/height 1..4096, names/resource keys up to 128 characters. File size is
checked before buffer allocation; text APIs also check UTF-8 byte size. Unknown,
duplicate, missing required, null-invalid and unsupported-version data produce
`TileMapException` diagnostics with source, code and JSON path. JSON parser errors
also include line/byte-column information.

The returned `LoadedTileMap.Source` is an authoring DTO. `Map` privately copies
cells/flags/palette/layer metadata, and `Catalog` copies resource mappings. Editing
that DTO cannot mutate a live instance. Revalidate and create a candidate instance
to adopt structural changes. Opt-in runtime cell edits are described below; they
do not rewrite the source DTO or JSON. There is no automatic reload or save
integration. Existing game progress saves are unchanged.

## Placement, extraction and lifetime

```csharp
var asset = TileMapAsset.LoadAsset(assets, "level.tilemap.json");
var placed = new TileMapInstance(engine, asset, new TileMapPlacement(64, 64, 1));
world.AttachBehavior(anchor, behavior, lifetime => lifetime.OnDetach(placed.Dispose));

// Draw ordinary entities and map cells in one native frame:
world.ExtractSprites(batch);
placed.AppendSprites(batch, TileView.FromCamera(camera, input.Viewport));
engine.Draw(camera, batch.RegionDraws);
```

`TileMapPlacement` is immutable: translation in world pixels and positive uniform
scale in [.01,100]. Origins must be finite and within ±10,000,000 pixels. A bare
`default(TileMapPlacement)` has scale zero and is invalid; use `new(0,0)` for unit
scale. The scene anchor above owns cleanup only: changing its transform does not
move the map. Rotation, shear, nonuniform scale, parent transforms and implicit
entity synchronization are outside this initial orthogonal contract. Floating
render coordinates retain normal large-coordinate precision limits.

The ordinary read-only constructor owns a `TextureBank` retaining only resource
keys used by its initial cells, including temporarily invisible cells. The
explicit editable factory retains every palette texture instead. Aliases/maps
share the engine's cache.
Constructor resource failure rolls back candidate leases. Residency does not
change while panning. Instance `Dispose` releases optional collision first, then
texture leases; it is idempotent and safe after engine/world closure on the owning
thread. Attach it with `BehaviorLifetime.OnDetach` for scene ownership, or dispose
it explicitly. Do not retain/draw an unloaded scene's instance without its owner.
All native-facing instance work obeys the engine's creating-thread contract.

`ExtractSprites` starts an empty map-only batch. `AppendSprites` keeps existing
entity/map draws, appends the map, then uses the same stable sort:

1. Ascending numeric layer order
2. For ties, existing batch draws first, then map layers in source-array order
3. Within a map layer, global row-major cell order

Calling `World.ExtractSprites` after appending would reset the batch; start with
world extraction and append maps afterward. Map extraction temporarily uses its
own catalog resolver and restores the caller's resolver. It never texture-sorts
transparent draws. Existing retained sprite/region/flip behavior is reused.

Each immutable layer has 16×16 occupancy chunks. Culling walks visible global rows,
then chunk columns, skips empty chunks, and visits cells in that row. It deliberately
does not emit chunk-major order. Count-before-append checks the **configured whole
frame** sprite budget, including existing draws. Overflow is rejected before
appending, never silently truncated. The existing native limit remains 65,536
sprites; choosing a larger source grid does not bypass it. An extraction failure
after mutation clears the incomplete batch; never submit a rejected extraction.
Warmed extraction/append/sort allocate no managed memory; first-time buffer growth,
asset load, cell-edit transactions and collision planning are setup allocations.

`TileView` is a half-open world-pixel visibility rectangle. Camera X/Y is the
upper-left; view extent uses framebuffer dimensions divided by zoom, not logical
window size. Double floor/ceil arithmetic is clamped before integer conversion,
including negative/far cameras. An inactive/minimized viewport produces an empty
view. Invalid camera/rect values reject explicitly. `TryWorldToCell` similarly uses
half-open bounds and returns false outside the map. A partially visible tile stays
a complete quad: this is visibility culling, **not world scissoring**. The actual
framebuffer clips the rendered quad.

## Opt-in runtime cell edits

```csharp
var editable = TileMapInstance.CreateEditable(engine, asset, new(64, 64));
TileMap previous = editable.Map; // immutable snapshot, safe to retain
editable.SetCells([
    new TileCellEdit(LayerIndex: 0, X: 15, Y: 4, TileId: 2, Flip: 1),
    new TileCellEdit(LayerIndex: 0, X: 16, Y: 4, TileId: 0)
]);
```

The existing constructor remains read-only and `SetCells` rejects on it.
`CreateEditable` explicitly retains **all resource keys referenced by the fixed
palette**, including tiles absent from the initial grid. Missing, invalid or
unloadable unused palette textures therefore fail during editable construction,
with candidate leases rolled back. Existing read-only instances still retain
only used keys. Editing never acquires textures, opens files or expands the
palette; all palette leases last until instance disposal. Unreferenced resource
records outside the palette are not retained.

`SetCells(ReadOnlySpan<TileCellEdit>)` synchronously accepts at most
`TileMapInstance.MaximumCellEdits` (**4,096**) replacements. `LayerIndex` addresses
the instance's sorted `Map.Layers`, not the original DTO array: numeric order,
then source order for ties. Coordinates are zero-based grid cells. IDs must be
zero (clear) or an existing palette ID; `Flip` is 0..3 and must be zero for a
cleared cell. Repeating a target layer/X/Y, even with the same value, is rejected.
Invalid layer/coordinates/ID/flags, an oversized batch, or collision failure
reject the entire batch. There is no clipping, partial success or last-write-wins.

Dimensions, tile sizes, layer names/order/opacity, palette and placement stay
fixed. A successful edit publishes a new immutable `Map` snapshot: touched layers
are copied and their 16×16 occupancy chunks recomputed; untouched layers and the
immutable palette are shared. The loaded asset, other instances, old snapshots
and authored arrays remain unchanged. `Map.AssetKeys` reflects keys used by that
snapshot, while the instance's residency still includes the entire palette.
Empty batches and edits that already match leave the snapshot unchanged. Edits
obey the engine's creating-thread/lifetime checks, and are bounded setup work,
not a zero-allocation per-frame operation. Extraction after editing retains the
existing warmed zero-allocation contract.

If collision is attached, an edit that changes the **union of solid cells across
all layers** first plans and allocates a complete replacement with the original
scale/friction/category/mask. Only then are the old owned bodies released and
the visual snapshot committed. All old bodies and shapes count during candidate
preflight: enough capacity for **old + new** bodies and **live + retired + new**
shape identities is required, even if the final replacement alone would fit.
Rectangle and physics-unit bounds still apply. Validation, capacity and candidate
native-creation rejection keep the old visual and collision state usable. As with
other physics operations, recovery from process-level out-of-memory failure is
not guaranteed. Allocation rollback may retire candidate shape IDs;
retired counts are not promised to remain unchanged after native creation starts.
No hidden physics step is used to reclaim identities or advance gameplay.

The originally returned `TileMapCollision` wrapper remains valid after successful
edits: its current rectangles and `TryGetRectangle` lookup change to the new
shapes. Replaced shape IDs stop resolving through it immediately; a previously
borrowed rectangle span remains a snapshot of the old plan. Previously copied
physics events may still contain old identities, and replacement may produce
normal contact-end/begin events on the next explicit step. Callers must not treat
an old hit or contact as a current collision lookup. Edits with an unchanged solid
union (flips, non-solid visuals, solid-to-solid changes, or transferring a solid
cell between layers in one batch) preserve shape IDs and consume no collision
headroom. Dispose the collision explicitly if it is no longer wanted; later edits
then affect visuals only until it is attached again.

This API does not resize grids, edit palettes/layers, regenerate terrain, animate
tiles, navigate, serialize runtime snapshots or introduce asynchronous work.

## Optional static collision

```csharp
using var physics = engine.OpenPhysics(); // requires the opt-in Box2D build
var collision = placed.AttachCollision(physics, new PhysicsScale(32));
// The host explicitly advances physics; the map never calls Step implicitly.
var result = physics.Step();
var hit = physics.RayCast(xMeters, yMeters, dxMeters, dyMeters);
if (hit.Hit && collision.TryGetRectangle(hit.Shape, out var cells))
{
    // cells is the generating rectangle in grid coordinates
}
```

The internal `TileCollisionPlan.Create` helper is a CPU-only preflight used by
the public instance's `AttachCollision`. It unions whole solid cells
across all layers, including opacity-zero layers; visual flips do not change a full
box. Deterministic greedy row-first rectangles cover the union exactly once. The
merge is not promised to minimize rectangle count. All generated shapes share the
requested friction/category/mask; arbitrary tile shapes/materials/sensors are not
part of this slice.

At most **128 rectangles** may be attached, one static body/box per rectangle.
A source may render correctly yet exceed this independent physics limit (e.g. a
large checkerboard). Centers and half-extents are explicitly converted from the
same placed pixel geometry to meters using `PhysicsScale`; positive Y keeps its
meaning. The plan validates every body/shape before native creation. Existing
physics limits still apply: body positions ±10,000 m, box half-extents .001..100 m.
Very large merged rectangles are rejected with their cell bounds; they are not
silently subdivided or distorted. Change the pixel/meter scale or solid layout.

Attachment requires the instance's engine/physics context, rejects a second live
attachment, and preflights capacity against other bodies and **live plus retired**
shape identities. The world caps remain 256 bodies/512 shape identities. Candidate
native failure disposes created bodies; rollback/disposal retires shape identities
until the next caller-requested `Step`. No implicit step is used to make room,
since that would advance unrelated gameplay. Capacity tests verify rejection before
mutation; the native allocation-failure rollback branch is not fault-injected here.

The instance owns its collision object. Early collision disposal is allowed; a
later attachment may need an explicit step to reclaim retired slots. Unloading the
owning scene releases both collision and textures while independent bodies survive.
Placement is immutable; editable cell transactions keep generated collision
coherent with the committed solid union.
Ray/AABB queries retain the physics module's existing closest-ray/broad-phase
semantics. This does not add navigation, terrain rules, one-way platforms, character
controllers, tile animation or external editor/import formats.

## Reproduction

```sh
scripts/test.sh quick tilemap
scripts/test.sh jit
# Existing prepared Box2D-only headless native build:
LD_LIBRARY_PATH="$PWD/build-physics" dotnet managed/bin/Release/net10.0/GameAuthoringLab.dll --tilemap-physics-test
# Existing prepared SDL software-Vulkan environment:
source scripts/ui-env.sh
GAL_TILEMAP_CAPTURE_DIR="$PWD/evidence/tilemap/visual-jit" \
  dotnet managed/bin/Release/net10.0/GameAuthoringLab.dll --tilemap-scenario --frames 5
GAL_TILEMAP_CAPTURE_DIR="$PWD/evidence/tilemap/physics-visual-jit" \
  dotnet managed/bin/Release/net10.0/GameAuthoringLab.dll --tilemap-physics-scenario --frames 180
python3 scripts/validate-tilemap-pixels.py evidence/tilemap/visual-jit evidence/tilemap/physics-visual-jit
GAL_TILEMAP_EDIT_CAPTURE_DIR="$PWD/evidence/tilemap-edit/visual-jit" \
  dotnet managed/bin/Release/net10.0/GameAuthoringLab.dll --tilemap-edit-graphics-test
python3 scripts/validate-tilemap-edit-pixels.py evidence/tilemap-edit/visual-jit
```

Use an installed `dotnet` or the explicit SDK path from the other test commands.
`--tilemap-demo` needs rendering only; `--tilemap-physics-demo` adds optional Box2D.
Arrows pan, wheel zooms, T resets the camera, E resets the optional dynamic ball,
and Escape exits. The same scenarios run with `--headless`, where texture-cache
entries are validated placeholders. This sample is a grid/camera/physics fixture,
not a character controller. Source data reuses existing `regions.bmp`; no new
binary asset, codec, font or external dependency is added.

The original TileMap batch added a source-generation root and included a fresh
NativeAOT publication. Runtime cell editing is pure managed code over existing
JSON/interop contracts and adds no new serialization root; its focused JIT,
physics and pixel evidence is recorded separately in [validation](validation.md).
