# Reusable C# entity and room compositions

Use ordinary C# factories that return **typed game objects containing direct
entity references**. Pass immutable parameters and explicit dependencies. Create
new mutable state for every call. The runnable [composition consumer](../packaging/consumers/composition/)
does this with weapons nested in enemies nested in rooms.

There is no engine enemy type, factory registry, component lookup, service locator,
reflection-based creation, generic scene base class or prefab inheritance system.
`EnemySpec`, `EnemyInstance`, `WeaponFactory` and `RoomFactory` in the consumer are
**application examples, not engine APIs**. The sole added runtime operation is
`World.OnDestroy(Entity, Action)`, described below. All entity/scene construction,
identity, transforms, ownership and unloading reuse the existing World APIs.

## Start with parameters and typed references

The consumer's call site is ordinary code:

```csharp
var world = new World();
var pulses = new PulseSource();
var clip = new FrameClip(["enemy-a", "enemy-b"], .25);
var scout = new EnemySpec("enemy", new Transform2D(10, 20),
    HitPoints: 3, Speed: 8, Size: 16, Animation: clip);
var guard = scout with { HitPoints = 7, Speed = 2, Size = 24 };
using var room = RoomFactory.Create(world, "yard", new Transform2D(100, 50),
    [scout, guard], pulses);

EnemyInstance enemy = room.Enemies[0];
enemy.State.Damage(1);
enemy.Visual.Sprite = enemy.Visual.Sprite!.Value with { R = .5f };
enemy.Weapon.Dispose();
```

`EnemyInstance` is a sealed game class with explicit `Root`, `Visual`, `Weapon`,
`State`, `InitialBrain` and `Animation` references. It does not discover objects
by name, hierarchy path, cast or registry. `InitialBrain` is the initially created
brain, not an automatically synchronized view of the replaceable behavior slot.
The example keeps game state separate from the current behavior implementation.

Each factory call creates new entities, state, behaviors and animation players.
The immutable `FrameClip` may be shared; playback position may not. Parameters are
explicit values, not a mutable global configuration. `with` creates another
parameter record; it does not clone a running instance or a native resource.

Entity names can repeat. Each creation receives fresh runtime and persistent IDs.
Runtime IDs remain process-unique; GUIDs identify an instance for persistence, not
the shared source definition. Repeating the same factory never reuses entity IDs.
Do not copy an existing entity's GUID into a repeated instance.

## Compose with existing graphs

`world.CreateChild(parent, name, localTransform)` sets three initial defaults:
transform parent, lifetime owner and parent's scene membership. The sample uses it
for room → enemy → weapon → blade. Local placements compose through that graph.
`world.CreateScene` creates an unload boundary; a room root is an ordinary entity
inside that scene, not a different scene type.

Those relationships can diverge after creation:

- `Reparent` changes only transform parentage; its default preserves world pose
- `SetOwner` changes only lifetime ownership
- `MoveToScene` changes membership, optionally including owned descendants
- `MakePersistent` transfers an owned subtree to persistent scope and detaches
  external relations; it is an explicit game decision
- `Destroy(root)` destroys the current lifetime-owned subtree, across scenes
- `UnloadScene(scene)` destroys that scene's members and nonpersistent owned
  descendants; existing persistent members survive under the established rules

These remain the [World contracts](../managed/WORLD.md). A typed instance is a
convenient view of created objects, not another ownership graph. If a game transfers
a weapon to another owner, the original typed `Weapon` reference still identifies
that same object. Update game state explicitly; no reference rewriting is inferred.

The enemy/weapon wrappers' `Dispose` checks `Root.IsAlive` before `World.Destroy`;
the room wrapper checks `Scene.IsLoaded` before `UnloadScene`. This permits late or
repeated wrapper disposal after external world teardown. Raw `World.Destroy` and
`UnloadScene` still reject an already destroyed/unloaded target. World itself has
no `Dispose`; the host explicitly ends its owned roots/scenes.

## Two different cleanup lifetimes

Use `AttachBehavior(..., lifetime => lifetime.OnDetach(...))` when a subscription
or resource belongs to **that behavior attachment**. Replacing or clearing the
behavior retires its registered cleanup. The existing behavior API is unchanged.

Use `world.OnDestroy(entity, cleanup)` for a resource owned by **that entity
instance**, which must survive AI replacement:

```csharp
var animation = new FramePlayer(clip);
try { world.OnDestroy(root, animation.Dispose); }
catch { animation.Dispose(); throw; } // Registration did not acquire ownership.

Action handler = state.OnPulse;
world.OnDestroy(root, () => pulses.Pulse -= handler);
pulses.Pulse += handler;
```

Register undo before fallible subscription setup. Register acquired-resource
cleanup immediately; if registration itself fails, the factory still owns that
resource and must dispose it. The registration does not run the callback now.

The contract is deliberately small:

1. A registration lasts until entity destruction, independent of behavior changes,
   transform reparenting, scene moves and ownership transfers. There is no removal
   operation or cancellation token. For shorter lifetimes, keep explicit ownership
   in game code or use the existing behavior-attachment scope
2. Destruction first preflights surviving transforms. If preflight fails, nothing
   is destroyed and no cleanup runs; repair the graph/data before retrying
3. Once preflight succeeds, all doomed entities/lookup entries and scene unloading
   commit before any callback. All behavior cleanup runs first, in its existing
   world creation order
4. Entity destruction callbacks then run lifetime descendants before owners,
   following the full pre-commit ownership graph. Within each entity, callbacks
   run in reverse registration order. Sibling/unrelated order is unspecified.
   Transform parentage does not choose this order
5. Every registration is attempted once, even if others fail. Registering the same
   action twice requests two invocations. Errors from both cleanup phases are
   aggregated after commit. A failure does not undo destruction; do not replay the
   world operation. Repair failed external cleanup separately if needed
6. Same-world creation, graph/property mutation, cleanup registration, Update and
   sprite extraction are blocked during callbacks. Use captured publisher/handler/
   resource references: dead entity guarded properties are unavailable. Identity,
   `IsAlive`, and read-only lookup can observe the committed boundary

Only explicitly registered cleanup is owned. Neither `IDisposable` on a behavior
nor a game instance is discovered automatically. Cleanup delegates are not saved
in JSON or restored from a save. Native resource thread/frame constraints still
apply; this registration does not make disposal during an active draw frame safe.

Keep `EngineHost`, shared physics/audio worlds, catalogs, shared definitions and
context caches under their existing external owners. Register only an instance's
own player/body/lease/subscription, never a shared handle by convenience. If an
instance will survive its original owner, ensure its required resources also have
independent lifetimes before transferring it. A persistent entity does not keep
an unrelated ancestor-owned resource alive automatically.

## Failure rollback is explicit and bounded

The consumer factories create a fresh root or scene, build within that boundary,
then return typed references. Their ordinary `try/catch` destroys that fresh root
or unloads that fresh scene if later construction fails. Nested construction
failure therefore removes earlier successful nested instances too.

If cleanup also throws, the factory preserves both the construction error and
cleanup aggregate. It checks `IsAlive`/`IsLoaded` because teardown can throw after
it already committed. Cleanup callbacks must not perform additional World edits.

This is **not a transaction around arbitrary C#**. Do not transfer newly created
objects outside the construction boundary, attach existing objects to it, modify
unrelated world state, or publish unfinished instances before success. Arbitrary
external side effects and mutations of existing entities are not rolled back.
An unregistered acquisition is still the factory's responsibility. A transform
preflight failure can prevent cleanup; both failures are surfaced rather than
claiming atomic success. The examples keep all partially built entities owned by
their fresh root/scene until successful return.

## JSON remains a separate path

`AuthoredScene.Load` imports a validated flat source document into a **new World**,
preserving its declared GUIDs. It is not an in-world instantiator and does not
rebind arbitrary behaviors. Keep using it where that source format fits.

This stage adds no static-template format, GUID-remapping loader, JSON factory
metadata, nested override language, scene inheritance, hot reload or save schema.
C# records and factory parameters are the current reusable authoring path.

## Run and verify

From a prepared checkout:

```sh
bash scripts/test.sh quick composition
# One public PackageReference boundary, after source checks:
bash scripts/pack-managed.sh
PACKAGE_COMPOSITION_PROOF_ROOT=/tmp/my-composition-proof \
    bash scripts/test-composition-packages.sh
```

The source test uses the exact consumer factory/check files plus focused lifecycle
tests. The package script copies ordinary sources outside the checkout, uses only
a fresh local package feed/cache, runs JIT and a freshly published NativeAOT binary,
and checks DLL/package/source hashes. It does not reference engine source or the
test executable, install dependencies, build native code, or publish remotely.

Coverage includes distinct repeated instances, parameter/state/animation
independence, nested transforms, behavior replacement, partial factory failures,
real `FramePlayer` disposal, subscription removal, explicit persistence and 24
room unload/recreate cycles. Lifecycle tests cover callback ordering, reentrancy,
exception aggregation, transform-preflight retry and deep iterative ownership.
The final recorded results belong in [validation](validation.md).

This is CPU authoring/lifetime/AOT evidence. Sprite extraction uses zero placeholder
texture handles and never submits them. No native body/texture, graphics, audio,
device, performance, development-speed or Godot-comparison result is implied.
