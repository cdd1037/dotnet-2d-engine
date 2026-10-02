# Minimal object world and explicit scene saves

`World.cs` is an ordinary C# object model. An `Entity` holds its own transform,
optional `Sprite2D`, and optional `IBehavior`. There is no Godot dependency,
compatibility layer, component registry, reflection-based object construction,
ECS scheduler, or scene DSL.

## Three independent relationships

- **Transform parent:** positions, rotates, scales, and shears the child. Editing
  this relation never implicitly changes ownership or scene membership
- **Lifetime owner:** explicit `World.Destroy(owner)` destroys recursively owned
  objects. An object may be owned without following that owner's transform
- **Scene membership:** supplies the room's unload roots. Unloading a room removes
  its members and follows ownership into other nonpersistent scenes. Membership
  in `PersistentScene` stops this ownership traversal

Each relation has at most one target. Transform and ownership cycles are rejected
independently. Cross-links involving the two different graphs are allowed.

`Create(name, scene)` creates a root without a parent or owner. Omit `scene` for
persistent world scope. `CreateChild(parent, name)` conveniently initializes all
three relations: transform parent, owner, and the parent's scene. Subsequent edits
are independent. Foreign worlds, destroyed entities, unloaded scenes, invalid
numeric data, empty names, and duplicate persistent IDs are rejected.

## Runtime identity versus saved identity

`EntityId` is positive and process-unique. Creation, pickup, drop, reparenting, and
room transitions retain the same object and runtime ID. Destruction invalidates
lookup. Runtime IDs are never reused, even across worlds. They are **not save keys**.

Each entity and scene also has an immutable, nonempty `Guid PersistentId`. New
objects receive fresh GUIDs. Authoring/import code may supply one explicitly:

```csharp
Scene room = world.CreateScene("Room A", persistentId: authoredRoomGuid);
Entity item = world.Create("Cell", room, persistentId: authoredItemGuid);
Entity sameItem = world.GetPersistent(item.PersistentId);
Scene sameRoom = world.GetScene(room.PersistentId);
```

GUIDs must be unique across both entities and scenes in a world. Serialization
retains these GUIDs exactly. Loading creates a distinct world with fresh objects
and fresh runtime IDs; relations and game-state references bind through GUIDs.
Loading the same save twice therefore shares saved identity, never runtime objects.
Persistent IDs are not names, hierarchy paths, array offsets, or content hashes.
`GetPersistent` and `GetScene` reject destroyed/unloaded objects.

## Pickup, drop, persistence, and room unload

```csharp
var world = new World();
Scene room = world.CreateScene("Room A");
Entity player = world.Create("Player", room, new Transform2D(100, 80));
Entity item = world.Create("Item", room, new Transform2D(150, 90));
item.Sprite = new Sprite2D(12, 8, AssetKey: "cell.bmp", Layer: 10);

world.MakePersistent(player);
world.PickUp(item, player); // Same object/IDs and world affine transform.
world.Drop(item, room);    // No lifetime owner; belongs to the room again.
world.PickUp(item, player);
world.UnloadScene(room);   // Player and held item remain alive.

Scene nextRoom = world.CreateScene("Room B");
world.Drop(item, nextRoom);
world.UnloadScene(nextRoom); // Dropped item dies; persistent player stays alive.
```

- `Reparent(entity, parent, keepWorldTransform: true)` only changes transform
  parenting. Pass `false` to retain local coordinates instead
- `SetOwner(entity, owner)` only changes lifetime ownership
- `MoveToScene(entity, scene, includeOwned: false)` only changes membership; opt in
  to move the recursively owned subtree
- `MakePersistent(entity)` moves its owned subtree into `PersistentScene`. External
  transform parents and owners are detached. Full world transforms are preserved;
  merely transform-parented neighbors are not retained
- `PickUp(item, carrier)` attaches both parent and owner, moves the owned subtree
  into the carrier's scene, and preserves its world transform. Author an explicit
  local transform afterward to snap it to a hand/socket
- `Drop(item, scene, transformParent: null)` clears lifetime ownership, moves the
  owned subtree to the destination scene, and preserves its world transform
- `Destroy(entity)` follows all lifetime-owned descendants, including persistent
  ones. Persistence protects against room unload, not explicit destruction
- `UnloadScene(scene)` destroys room members and nonpersistent owned descendants.
  Surviving external relations detach world-stably. The unloaded scene rejects
  new objects and cannot be resolved by GUID. Persistent scope cannot unload

Moving an object between scenes does not sever its ownership. Use the explicit
persistence operation when appropriate. Pickup/drop validate both graphs before
mutation. Teardown computes all survivor transforms before deleting anything,
including alternating chains of surviving and dying objects. Numeric failures
leave relations and room loading state unchanged so callers may repair and retry.

Room gameplay may intentionally retain a dropped item in hidden persistent scope
while its room is absent, then restore scene membership when that room is loaded.
That is an explicit game policy, separate from `UnloadScene`'s destruction rules.

## Rotation and full affine transforms

`Transform2D(X, Y, ScaleX = 1, ScaleY = 1, Rotation = 0, Shear = 0)` preserves the
previous constructor. Rotation is in **radians**. Scale must remain positive;
negative scales/reflections and zero scales are deliberately unsupported. All
fields must be finite. Use `Transform2D.Identity`, not the invalid zero-filled
`default` struct.

The basis is `rotation * [ScaleX Shear; 0 ScaleY]`. Shear is an explicit linear
coefficient, not an angle. `GetBasis(out m11, out m12, out m21, out m22)` provides:

```text
x' = m11*x + m21*y + X
y' = m12*x + m22*y + Y
```

A rotated child under nonuniform parent scaling generally acquires shear. The
world composes complete affine matrices, then decomposes them into positive
scales, radians, and shear. Reparenting in keep-world mode computes the inverse
parent affine transform; detachment, pickup/drop, persistence, and room teardown
retain the resulting shear rather than silently approximating it with rotation
and scale alone. The sprite origin is still its local top-left; there is no pivot
component yet.

The whole parent chain accumulates in double precision before narrowing to floats.
This preserves the earlier compensating-large-scales regression. Keep-world
operations preserve representable affine geometry within floating-point rounding,
not bit-identical parameters or unlimited precision. Severely ill-conditioned,
singular, overflowing, or underflowing transforms fail deterministically. Local
setters validate local data only; descendants can still become nonrepresentable.
Repair local data or explicitly detach keep-local before retrying a failed teardown.

## Sprites and behavior/render loop

`Sprite2D` stores finite nonnegative width/height, RGBA in [0, 1], an optional
`AssetKey`, integer `Layer`, and optional `FlipX`/`FlipY`. A null sprite means no visual. A null asset key
uses the engine's built-in circle; a nonnull key must be nonempty and resolves
through the caller's texture registry. Saves contain stable asset keys, never
native texture handles or machine-dependent paths. Higher layers render later;
equal layers retain creation order. No texture lookup/loading occurs in the world
model itself.

`World.Update(dt)` invokes behaviors in creation order with finite nonnegative dt.
New entities start on the next tick, destroyed entries are skipped, and compaction
waits until the pass ends. Exceptions propagate but recover the update guard and
pending compaction. Recursive updates are rejected. There are no inferred
start/destroy callbacks, subscription discovery or `IDisposable` scans.
Use explicit `World.AttachBehavior(entity, behavior, attach)` and register cleanup
with `BehaviorLifetime.OnDetach` to bind subscriptions/resources to the attachment.
Plain `Behavior` assignment creates no ownership. Clearing or replacing a previously
owned attachment does retire its registered cleanup; assigning the identical
behavior instance through the property is a no-op. Destruction and unload also
retire registered cleanup once; callbacks
run after commit with mutation blocked and failures aggregated. See
[ownership and sorting](../docs/LIFECYCLE_SORTING.md) for exact setup/rollback/exception/order semantics.

`World.OnDestroy(entity, cleanup)` separately registers entity-owned cleanup that
survives behavior replacement. After committed destruction and all existing
behavior cleanup, these callbacks run lifetime descendants before owners, LIFO
within each entity; sibling/unrelated ordering is unspecified. Every registration
is attempted once under the same mutation guard, with failures aggregated. There
is no cancellation token, implicit disposal discovery or callback serialization.
See [C# composition](../docs/CSHARP_COMPOSITION.md) for typed factories, construction
rollback, shared-resource ownership and the full registration contract.

```csharp
var batch = new SpriteBatch(capacity: 1024) { TextureResolver = ResolveTexture };
world.Update(deltaSeconds);
world.ExtractSprites(batch);
engine.Draw(camera, batch.Draws);
```

The batch reuses its arrays and stable-merge-sorts layers, retaining a linear
already-ordered fast path. Unordered batches lazily allocate reusable scratch
arrays on first use/capacity growth; warmed sorting remains allocation-free. Its legacy `Sprites` view
remains for axis-aligned ABI regressions; `Draws` carries the complete affine basis
and texture handles. Use `Draws` to render rotation, shear, and multiple textures.
If extraction fails, `Count` resets to zero. Submitted spans are borrowed until the
next extraction. No per-property/per-entity native interop is added.

`World.Entities` exposes a cached read-only live view for lookup/rebinding and indexed
iteration. The collection cannot be changed through mutable collection interfaces;
create and destroy members through `World`, and edit each entity through its explicit
properties. Reading the view does not copy the list or allocate a new wrapper.
Filter `IsAlive` if enumerating during an update. `LoadedScenes` exposes
currently loaded scene objects. The world remains single-threaded. Setup, graph
edits, save/load, texture loading, capacity growth, and exceptional paths allocate;
the established warmed behavior/extract/draw loop still tests zero managed bytes.

## Versioned JSON save/load

`ScenePersistence` is the demo/test executable's internal runtime-save example,
not a public `Dotnet2D.Engine` API. Independent games own their progress format;
the public [authored-scene loader](../docs/AUTHORED_SCENES.md) loads source defaults
instead. The example uses .NET's source-generated `System.Text.Json` metadata, not
runtime reflection. The serializer has no added packages and works with reflection
serialization disabled. The base version-1 schema contains:

- Scenes: GUID, name, persistent-scope flag
- Entities in creation/draw order: GUID, name, scene GUID, separate optional parent
  and owner GUIDs, all six local transform values, and optional complete sprite
- Optional `GameSaveState`: player, active scene, held item, and item GUIDs;
  room index, item room index (`-1` while held), transition count, pickup count

The example also accepts version 2 and writes it when a sprite uses a flip;
version 1 rejects nondefault flips. Neither version serializes behavior or native resources.

```csharp
string json = ScenePersistence.Save(world, new GameSaveState
{
    PlayerId = player.PersistentId,
    ActiveSceneId = room.PersistentId,
    ItemId = item.PersistentId,
    HeldItemId = item.PersistentId,
    ItemRoomIndex = -1,
    RoomIndex = 0,
    TransitionCount = 0,
    PickupCount = 1
});

LoadedScene candidate = ScenePersistence.Load(json, key => assetCatalog.Contains(key));
// Validate game-specific room/item rules and explicitly rebind behavior here.
world = candidate.World;
```

`Load` returns a separate candidate only after structural, reference, graph,
resource, transform, and visual numeric validation. It never mutates an existing
world. Keep the current world/state until candidate validation and game-specific
rebinding succeed, then swap both together. The asset predicate must be a read-only
existence check; loading/unloading native resources belongs to the caller.

Malformed JSON, duplicate JSON properties, unknown/missing schema fields, unsupported
versions, null entries, empty/duplicate GUIDs, missing references, separate graph
cycles, invalid numeric values, missing assets, nonrepresentable world transforms,
and overflowing transformed sprite corners all fail. State references are checked;
a saved held item must have the saved player as both parent and owner. Each distinct
external asset is checked once. Input is bounded to 16 Mi characters, 100,000
entities, 10,000 scenes, and 512 entities per relation chain. The depth bound also
limits worst-case transform evaluation for untrusted save input. Runtime ID values consumed by a rejected candidate are
never reused. `SceneFormatException` describes invalid load data. Malformed JSON diagnostics
include the field path, 1-based line and byte column, and original parser cause;
the host adds the save filename when reading a file.

The serializer saves currently loaded scene data, not unloaded room templates or
an automatic streaming cache. Unloaded room definitions remain authored C# data.
Behaviors, delegates, CLR type names, arbitrary code, native resources, and animation
clocks are intentionally absent. Gameplay explicitly rebinds behavior and validates
its own invariants after loading. There is no migration framework yet; unsupported
format versions fail instead of being guessed. Disk persistence is the host's
responsibility; JSON construction/loading itself performs no filesystem operations.

## Boundaries and verification

This is an inspectable semantic baseline, not a scalability claim. Ownership uses
explicit graph scans and may be quadratic. World transforms are recomputed on
extraction. There is no ECS, generic serializer/plugin registry, reflection-based
behavior resurrection, automatic hot reload or editor integration. Optional
[physics](../docs/PHYSICS.md) and [typed UI](../docs/UI_BINDINGS.md) are separate
modules; `World` does not implicitly step physics or own a UI session.

`WorldSelfTests.Run()` retains the original lifecycle/allocation regressions.
`WorldPersistenceTests.Run()` adds independent affine point evaluation across 100
seeded hierarchies, shear-preserving reparent/detach, stable versus runtime identity,
complete save-load-save round-trips, shared resource resolution, post-load lifecycle,
and invalid-save rejection while the current world/state remains unchanged.
