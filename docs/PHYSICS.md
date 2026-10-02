# Box2D foundation

The opt-in physics module provides one **Box2D 3.1.1** world per engine context,
static/dynamic/kinematic bodies, circle/box/capsule shapes, filters, sensors, fixed stepping,
center force/impulse, copied contact events and bounded queries. It is independent
of SDL and can run in a headless native build. Rendering and game rules stay in
managed consumers; this is not a character controller or full physics editor.

## Verified dependency

- [Official release v3.1.1](https://github.com/erincatto/box2d/releases/tag/v3.1.1),
  published 2025-06-04
- Official tag commit `8c661469c9507d3ad6fbd2fea3f1aa71669c2fe3`
- Source archive SHA-256
  `fb6ef914b50f4312d7d921a600eabc12318bb3c55a0b8c0b90608fa4488ef2e4`
- The release has no separately attached archive/checksum asset. After the official
  codeload download, **all 233 archive file blobs** were verified against that
  commit's GitHub Git tree. The resulting archive hash is pinned for reproduction;
  this is not a claim of an independently published upstream SHA-256 signature
- Full [MIT notice](BOX2D-LICENSE.txt) retained; source unmodified

`scripts/build-box2d.sh` builds only the Release PIC static C17 library. Samples,
unit-test runners, benchmarks, documentation, Tracy and AVX2 are disabled. It does
not fetch enkiTS, GLFW, ImGui or other sample dependencies. Upstream validation is
enabled in the recorded build. No extra software installation was required. Only
Linux x64 is validated here; upstream platform support is not engine acceptance.

`GAL_ENABLE_PHYSICS` defaults OFF in CMake. The SDL/UI helper scripts likewise
require `GAL_WITH_PHYSICS=ON`; simply referencing/creating an `EngineHost` does not
open a physics world. Module-disabled builds retain explicit unavailable stubs.

## Units, stepping and transform authority

`PhysicsWorld` is opened explicitly with immutable `PhysicsSettings`. `Step()`
advances **exactly one configured fixed step**, with no real-time delta argument.
Defaults are 1/60 second, four substeps and gravity `(0, 9.8)`. The allowed step
range is 1/240..1/15 second, with 1..8 substeps. Box2D is configured with zero worker
threads and no task callbacks; there is no managed callback during simulation.

World positions and shape lengths are meters; velocity is meters/second; angles
and angular velocity are radians and radians/second; density is kg/m². Center
impulses use kg·m/s and center forces use newtons. `PhysicsScale` explicitly converts
pixels/meters using a validated pixels-per-meter value. It does not invert axes.
The fixture chooses x right/y down to match the renderer, positive-y gravity, and
50 pixels/meter. A consumer may choose another convention but must convert it
consistently. Do not pass pixel-sized geometry directly to the solver.

Physics is authoritative for dynamic body poses. The caller reads body state after
stepping and constructs its render transform. Kinematic bodies move by prescribed
velocity; static bodies cannot be given nonzero velocity. `Teleport` is an explicit
pose change and wakes the body so sleeping contacts are reevaluated. It is not a
continuous movement/character-slide operation. Forces apply to the next step;
impulses affect velocity immediately and wake dynamic bodies.

There is no automatic bidirectional entity binding, parent-scale propagation,
render interpolation or ownership inference. The demo turns body-center poses
into top-left affine sprite draws explicitly. It does not write render poses back
into the solver every frame. Box2D's angle-to-rotation function uses approximate
trigonometry; the returned angle describes the actual normalized rotation, rather
than promising exact identity with the authored scalar angle. The adapter projects
rotation with `atan2` for rendering. This reinforces why read-back should not be
fed into repeated teleports as a synchronization loop.

The demo's real-time catch-up cap and focus pause are sample policies. No
cross-platform, cross-compiler or cross-version deterministic simulation guarantee
is made. Repeating a fixed-input fixture in the same native binary is tested, as is
JIT/AOT consumption of that same binary; neither establishes broader determinism.

## Handles and scene lifetime

Native opaque nonzero IDs wrap, rather than expose, Box2D IDs. They are checked
against the live world registry and never reused within the process. Bodies own
their shapes; destroying a body destroys those native shapes. Disposed or stale
body/shape wrappers cannot issue new operations. A `PhysicsScope` owns bodies and
can register `Dispose` with an existing `BehaviorLifetime.OnDetach` to follow
scene/entity lifetime. Independently owned persistent bodies survive that scope's
unload. The engine does not assume that every body belongs to the active scene.

Closing the world destroys its bodies/shapes. Engine destruction closes the world
before its other native backends. Outstanding managed wrappers become invalid;
late same-thread disposal is safe and cannot release resources in a reopened world
or new context. No GC finalizer destroys native physics state. All ABI calls use
the creating thread, outside a sprite begin/end frame.

Limits are **256 bodies**, **512 shape identities**, and **1,024 copied events per
step**. A deleted shape keeps a short-lived identity record until the next step's
end events have been copied. Such retired records count against 512; a create/delete
burst must step before reusing those slots, or dispose the whole world. This avoids
reporting a reused Box2D slot as the wrong object's end event.

## Contacts, sensors and queries

All adapter shapes enable contact and sensor event collection. Shapes may be
sensors, which report overlap without a collision response. Sensors are discrete;
fast objects can cross a thin sensor between steps. Bullet/continuous collision is
not a universal sensor or dynamic-body tunneling solution. Sensor density still
contributes mass when nonzero, following Box2D semantics.

Shape filters use 64-bit category/mask values and a signed group. Matching nonzero
groups override masks: positive forces eligibility, negative suppresses it. Default
category is 1 and mask accepts all. Material density/friction/restitution are
explicit. No custom filter callback, collision material registry, joints, arbitrary
polygons, chain terrain, one-way platforms or slope-following controller is added.

`Step()` copies contact begin/end and sensor begin/end into engine-owned records,
then into the reusable managed event array. `PhysicsWorld.Events` is valid until
the next step; process it after every fixed step, rather than only after a multi-step
render frame, and copy records if retaining them longer. It contains stable engine
body/shape IDs, not live pointers. Sensor event A is the sensor and B the visitor.
Deletion-end events retain removed IDs and flag the removed side; do not treat
those IDs as still-operable bodies. There are no reentrant managed callbacks.

`PhysicsStepResult.Dropped > 0` explicitly reports truncated/unmapped events. **The step
has already completed; do not retry it.** The caller must handle that diagnostic.
The sample treats overflow as a visible error. Events are grouped by event kind;
no chronological ordering or cross-platform ordering is promised. Low-level event
and query copies reject insufficient output capacity without partially writing the
array and return the required count.

`RayCast` returns the closest filtered shape/body, point, normal and fraction;
it intentionally **ignores initial overlap**, matching the pinned upstream
convenience API. A zero-length ray is rejected. `QueryAabb` is explicitly a
**broad-phase AABB query**, not an exact shape-overlap/picking test. It returns
at most 512 IDs, sorted by the engine handle, with no managed callback. Query
category/mask filtering is separate from collision group overrides.

`QueryCircle` and `QueryBox` use **narrow-phase shape overlap** against circles,
rotated boxes and capsules. The circle takes a world-space center and radius; the
box takes a world-space center, positive half width/height and a rotation in
radians. Unlike `QueryAabb`, these remove broad-phase false positives using the
pinned `b2World_OverlapShape` distance test. Box2D’s narrow-phase distance test allows separations up to
**.0005 meters**, so “exact” here means its narrow-phase geometry,
not infinite-precision mathematical intersection or a guaranteed grazing-edge
decision. There is no shape casting or continuous sweep.

Both methods write only into the caller’s `Span<ulong>`, returning the number of
sorted stable shape IDs. Capacity is 0..512; a too-small span throws
`ArgumentException` with the required count and leaves all elements unchanged.
An empty span succeeds for no hits, otherwise reports capacity failure. Unused
elements remain untouched. Native calls also return the required count on
capacity failure. No managed callbacks or successful-query allocations are used.

`PhysicsSensorQuery.Include` (default), `Exclude` and `Only` explicitly control
sensor participation in the new queries. All modes still apply reciprocal
filtering: `(query.Category & shape.Mask) != 0` and
`(query.Mask & shape.Category) != 0`; shape collision groups do not override it.
Queries inspect current geometry immediately, including before the first step
and after teleport or TileMap collision replacement. They do not advance the
world, emit events or require the queried body to be awake. Ray/AABB behavior
and the original shape-definition ABI are unchanged.

`PhysicsBody.AddCapsule(PhysicsCapsuleDefinition)` supplies **two body-local
segment endpoints and a radius in meters**. The endpoints are the centers of
the semicircular ends, not the outer tips; total end-to-end length is segment
length plus twice the radius. Body translation/rotation applies normally, with
no second capsule-local angle or automatic pixel conversion. For example,
`new PhysicsCapsuleDefinition(0, scale.ToMeters(-25), 0, scale.ToMeters(25),
scale.ToMeters(10))` at 50 pixels/meter has endpoints `(0,-.5)`/`(0,.5)`
and radius `.2` meters. It is created by `b2CreateCapsuleShape`, shares ordinary
shape material/filter/sensor semantics, stable IDs and body-owned lifetime, and
participates in collision response, copied events, rays and all existing queries.
The version-1 authored JSON fixture remains circle/box-only; this batch adds a
programmatic capsule definition, not a new serialization schema.

## Numeric validation and authoring

Definitions reject unknown versions/flags, nonfinite values and invalid extents
before touching Box2D. Initial/teleport positions are bounded to ±10,000 meters;
velocity components to ±1,000 and angular speed to ±100; gravity components to
±1,000. Shape half extents/radius are .001..100 meters, local offsets ±100, density
0..10,000, friction 0..10 and restitution 0..1. Capsule endpoint components are
finite within ±100 meters and endpoint separation must be at least **.01 meters**
(inclusive at the float representation of `.01f`). This excludes upstream’s tiny-
segment fallback to a circle. Capsule radius is .001..100 meters. Exact-query
centers are within ±10,000 meters, dimensions .001..100 and box angle ±10,000
radians; unknown sensor modes and nonfinite inputs fail before the query.
These are input guards, not promises
of stable accuracy at every extreme. Prefer ordinary game-scale bodies around
0.1..10 meters and a modest world extent. Solver speed clamping and ordinary
floating-point/numerical limitations still apply as simulation evolves.

`assets/basics.physics.json` is a small separate version-1 fixture with static,
dynamic and kinematic bodies, circles/boxes and a sensor. Its official
`System.Text.Json` source-generated context keeps strict unknown/duplicate member
and required-field validation. Sources are bounded to 64 KiB before allocation,
with 1..64 uniquely named bodies, and use the shared asset-root resolver. Validation
is completed before the demo creates bodies. This fixture is not a serialized live
Box2D world or a replacement for the existing world/game save schema.

## Reproduction

```sh
bash scripts/build-box2d.sh                 # explicit pinned dependency setup
scripts/test.sh quick physics              # layouts/units/sourcegen, no Box2D required
bash scripts/test-physics.sh                # real solver, no SDL/GPU required
scripts/test.sh jit
GAL_WITH_MIXER=ON GAL_WITH_PHYSICS=ON bash scripts/build-ui.sh
source scripts/ui-env.sh
/path/to/dotnet managed/bin/Release/net10.0/GameAuthoringLab.dll --physics-scenario
# Choose displayed SDL drivers instead of ui-env.sh for interactive controls:
/path/to/dotnet managed/bin/Release/net10.0/GameAuthoringLab.dll --physics-demo
```

For an already published AOT app, `PHYSICS_APP=/absolute/path/to/app bash
scripts/test-physics.sh` exercises the same real solver boundary without republishing.
Interactive controls: E impulses the first dynamic body, T teleports it back and
zeros velocity, Space pauses/resumes, Escape exits. The moving kinematic slab is a
body-type example; it does not implement character riding rules. The automated
fixture runs 180 fixed steps and reports contacts, sensors, poses and final cleanup.


## Public package boundary

`EngineHost.OpenPhysics` and the existing settings/definitions, scale, world,
body/shape and scope operations are experimental public APIs. World/body/shape
constructors remain internal; scopes require a live world. Raw ABI records and
P/Invoke remain internal. Public state/step/ray results are immutable copied
values, with boolean ray `Hit` and typed `PhysicsEventType`. Event views expose
`RemovedA`/`RemovedB` instead of a native flags field. Opaque IDs can be compared
with query/event results; they cannot manufacture a new owning wrapper.

`PhysicsWorld.Events` borrows a reusable 1,024-entry typed array until the next
step or close. Retain individual value copies (or explicitly copy the span) for
longer use. A second 1,024-entry native buffer remains private for ABI transfer;
the typed copy introduces bounded setup storage and no per-step allocation.
Snapshots already copied by the caller survive disposal. Accessing live state
through a closed owner still fails. Settings and LastStep also check world access.
Public query and body-command preflight reports invalid finite/range arguments
with managed argument exceptions before native mutation; solver/native operation
errors retain the existing InvalidOperationException diagnostics.


## Additive capsule/overlap ABI

The original `gal_shape_def` remains 72 bytes with its original circle/box
interpretation. New exports `gal_physics_create_capsule_v1` and
`gal_physics_query_overlap_v1` accept independent version-1 records:
`gal_capsule_def_v1` (72 bytes, category offset 48) and
`gal_physics_overlap_query_v1` (56 bytes, category offset 40). Exact size, version,
flags and reserved fields are checked. No Box2D structures or callbacks cross
the boundary. The same context/thread/frame checks apply; stale/foreign body
IDs cannot create capsules. Disabled builds export both functions and report
that the module is unavailable. Managed wrappers reject closed/disposed owners
before access, including after a new physics world replaces the old one.
