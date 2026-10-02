using System.Numerics;
using GameAuthoringLab;

// This ordinary PackageReference caller has no engine source/friend access.
bool authorSmoke = args.Contains("--author-smoke", StringComparer.Ordinal);
int assertions = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException("PACKAGE FEATURES: " + label);
    assertions++;
}
void Reject<T>(Action action, string label) where T : Exception
{
    try { action(); }
    catch (T) { assertions++; return; }
    throw new InvalidOperationException("PACKAGE FEATURES accepted: " + label);
}
static bool Near(float actual, float expected, float tolerance = .0001f) => Math.Abs(actual - expected) <= tolerance;
static bool Sorted(ReadOnlySpan<ulong> values)
{
    for (int i = 1; i < values.Length; i++) if (values[i - 1] >= values[i]) return false;
    return true;
}

var assets = new AssetRoot(Path.Combine(AppContext.BaseDirectory, "assets"));
var loaded = TileMapAsset.LoadAsset(assets, "level.tilemap.json");
loaded.Source.Layers[0].Cells[0] = 2;
Check(loaded.Map.Layers[0].Cell(0) == 0, "authored caller DTO does not mutate loaded grid");
using var engine = EngineHost.Create(headless: !authorSmoke, maxSprites: 32);
using var physics = engine.OpenPhysics();
using var map = TileMapInstance.CreateEditable(engine, loaded, new(0, 0));
using var sibling = new TileMapInstance(engine, loaded, new(256, 0));
var scale = new PhysicsScale(32);
var collision = map.AttachCollision(physics, scale, category: 1);
Check(map.IsEditable && collision.Rectangles.Length == 1, "editable caller map has one merged floor");
using var bodies = new PhysicsScope(physics);
var actorBody = bodies.CreateBody(new(PhysicsBodyType.Dynamic, 2.5f, .75f, FixedRotation: true));
var actorCapsule = actorBody.AddCapsule(new(0, -.35f, 0, .35f, .25f, Category: 2, Mask: 1));
ulong actorShape = actorCapsule.Id;
uint shapeCount = physics.State.Shapes;
Reject<ArgumentOutOfRangeException>(() => actorBody.AddCapsule(new(0, 0, .009f, 0, .25f)), "too-short capsule");
Check(physics.State.Shapes == shapeCount, "invalid capsule leaves owning body unchanged");

// Far-away static geometry keeps the picking oracle separate from falling gameplay.
var circleBody = bodies.CreateBody(new(PhysicsBodyType.Static, 20, 0));
ulong circle = circleBody.AddShape(new(PhysicsShapeType.Circle, 1, Category: 8, Mask: 16, Group: 7)).Id;
var sensorBody = bodies.CreateBody(new(PhysicsBodyType.Static, 20, 0, Angle: MathF.PI / 2));
ulong sensor = sensorBody.AddCapsule(new(0, -.5f, 0, .5f, .3f, Category: 8, Mask: 16, Group: 7, Sensor: true)).Id;
var diagonalBody = bodies.CreateBody(new(PhysicsBodyType.Static, 24, 0));
ulong diagonal = diagonalBody.AddShape(new(PhysicsShapeType.Box, 1, .05f, Angle: MathF.PI / 4, Category: 8, Mask: 16)).Id;
Span<ulong> query = stackalloc ulong[16];
int count = physics.QueryCircle(20, 0, 1.1f, query);
Check(count == 2 && Sorted(query[..count]) && query[0] == circle && query[1] == sensor,
    "circle query defaults include sensors and return stable sorted IDs");
count = physics.QueryBox(20, 0, 1.1f, 1.1f, .37f, query, category: 16, mask: 8);
Check(count == 2 && Sorted(query[..count]) && query[0] == circle && query[1] == sensor,
    "rotated box query returns the same stable sorted IDs");
Check(physics.QueryCircle(20, 0, 1.1f, query, 16, 8, PhysicsSensorQuery.Exclude) == 1 && query[0] == circle,
    "circle sensor exclusion");
Check(physics.QueryBox(20, 0, 1.1f, 1.1f, 0, query, 16, 8, PhysicsSensorQuery.Only) == 1 && query[0] == sensor,
    "box sensors-only selection");
Check(physics.QueryCircle(20.75f, 0, .03f, query, 16, 8, PhysicsSensorQuery.Only) == 1 && query[0] == sensor,
    "capsule local endpoints follow body rotation");
Check(physics.QueryCircle(20, .75f, .03f, query, 16, 8, PhysicsSensorQuery.Only) == 0,
    "rotated capsule excludes its unrotated endpoint");
Check(physics.QueryCircle(20, 0, 1.1f, query, category: 1, mask: 8) == 0,
    "query category must match shape mask, even for positive collision groups");
Check(physics.QueryBox(20, 0, 1.1f, 1.1f, 0, query, category: 16, mask: 4) == 0,
    "query mask must match shape category");
Check(physics.QueryAabb(20.75f, .75f, 20.85f, .85f, query, 16, 8) == 1 && query[0] == circle,
    "broad-phase AABB deliberately includes a circle corner false positive");
Check(physics.QueryCircle(20.8f, .8f, .05f, query, 16, 8) == 0,
    "precise circle rejects that AABB corner");
Check(physics.QueryBox(20.8f, .8f, .05f, .05f, 0, query, 16, 8) == 0,
    "precise box rejects that AABB corner");
Check(physics.QueryAabb(24.55f, -.65f, 24.65f, -.55f, query, 16, 8) == 1 && query[0] == diagonal,
    "broad-phase AABB includes a rotated thin-box corner");
Check(physics.QueryBox(24.6f, -.6f, .05f, .05f, MathF.PI / 4, query, 16, 8) == 0,
    "precise rotated query rejects the thin-box false positive");
Check(physics.QueryBox(24.6f, .6f, .1f, .05f, MathF.PI / 4, query, 16, 8) == 1 && query[0] == diagonal,
    "precise rotated query finds actual diagonal geometry");
Check(physics.QueryCircle(21.10025f, 0, .1f, query, 16, 8) == 1 && query[0] == circle,
    "near-tangent query respects documented .0005 meter narrow-phase tolerance");
Check(physics.QueryCircle(21.102f, 0, .1f, query, 16, 8) == 0,
    "separation beyond narrow-phase tolerance is excluded");

ulong[] untouched = [123456789, 987654321];
Reject<ArgumentException>(() => physics.QueryCircle(20, 0, 1.1f, untouched.AsSpan(0, 1)), "circle output capacity");
Check(untouched[0] == 123456789 && untouched[1] == 987654321, "short circle span receives no partial write");
Reject<ArgumentException>(() => physics.QueryBox(20, 0, 1.1f, 1.1f, 0, untouched.AsSpan(0, 1)), "box output capacity");
Check(untouched[0] == 123456789 && untouched[1] == 987654321, "short box span receives no partial write");
Check(physics.QueryCircle(40, 40, .1f, Span<ulong>.Empty) == 0, "zero-capacity empty result succeeds");
Reject<ArgumentException>(() => physics.QueryCircle(20, 0, 1.1f, Span<ulong>.Empty), "zero-capacity nonempty result");
Reject<ArgumentOutOfRangeException>(() => physics.QueryBox(20, 0, 1, 1, 0, new ulong[513]), "capacity above 512");
Reject<ArgumentOutOfRangeException>(() => physics.QueryCircle(20, 0, float.NaN, untouched), "nonfinite query radius");
Reject<ArgumentOutOfRangeException>(() => physics.QueryBox(20, 0, 1, 1, 0, untouched, sensors: (PhysicsSensorQuery)3), "unknown sensor mode");

// Contact identity connects the capsule simulation to caller-owned map rectangles.
bool landed = false;
PhysicsEventView copiedContact = default;
uint dropped = 0;
for (int i = 0; i < 180; i++)
{
    dropped += physics.Step().Dropped;
    foreach (var contact in physics.Events)
        if (contact.Type == PhysicsEventType.ContactBegin &&
            ((contact.ShapeA == actorShape && collision.TryGetRectangle(contact.ShapeB, out _)) ||
             (contact.ShapeB == actorShape && collision.TryGetRectangle(contact.ShapeA, out _))))
        { landed = true; copiedContact = contact; }
}
Check(dropped == 0 && landed, "physical capsule contacts the generated floor with stable event IDs");
Check(Near(actorBody.State.Y, 3.4f, .04f) && Math.Abs(actorBody.State.Vy) < .05f, "capsule rests at segment-plus-radius height");
var floorHit = physics.RayCast(2.5f, 0, 0, 6, mask: 1);
Check(floorHit.Hit && collision.TryGetRectangle(floorHit.Shape, out var floor) && floor == new TileRectangle(0, 4, 6, 1),
    "public ray resolves initial generated floor rectangle");
var before = map.Map;
var beforeState = physics.State;
Reject<ArgumentException>(() => map.SetCells([new(0, 2, 4, 0), new(0, 1, 0, 999)]), "invalid edit after a valid floor removal");
Check(ReferenceEquals(before, map.Map) && physics.State == beforeState && collision.TryGetRectangle(floorHit.Shape, out _),
    "invalid batch atomically preserves snapshot and native collision");
TileCellEdit[] edits = [new(0, 2, 4, 0), new(0, 0, 0, 2, 3)];
map.SetCells(edits);
var opened = map.Map;
edits[0] = new(0, 3, 4, 0);
Check(opened.Layers[0].Cell(26) == 0 && opened.Layers[0].Cell(27) == 1 && opened.Layers[0].Flip(0) == 3,
    "successful edit snapshots caller array and flipped non-solid palette tile");
Check(before.Layers[0].Cell(26) == 1 && before.Layers[0].Cell(0) == 0 &&
    loaded.Map.Layers[0].Cell(26) == 1 && sibling.Map.Layers[0].Cell(26) == 1,
    "old snapshot, loaded asset and sibling remain unchanged");
Check(collision.Rectangles.Length == 2 && !collision.TryGetRectangle(floorHit.Shape, out _) && physics.State.Steps == beforeState.Steps,
    "same collision wrapper updates IDs and plan without a hidden physics step");
Check(physics.QueryCircle(2.5f, 4.2f, .1f, query, mask: 1) == 0 &&
    physics.QueryBox(3.5f, 4.2f, .1f, .1f, 0, query, mask: 1) == 1 && collision.TryGetRectangle(query[0], out _),
    "precise queries see the committed gap and retained adjacent floor immediately");
for (int i = 0; i < 60; i++) dropped += physics.Step().Dropped;
Check(dropped == 0 && actorBody.State.Y > 5, "capsule falls through the edited collision gap");
map.SetCells([new(0, 2, 4, 1)]);
Check(opened.Layers[0].Cell(26) == 0 && map.Map.Layers[0].Cell(26) == 1 && collision.Rectangles.Length == 1,
    "restoring floor publishes another independent coherent snapshot");
actorBody.Teleport(2.5f, .75f);
actorBody.SetVelocity(0, 0);
for (int i = 0; i < 180; i++) dropped += physics.Step().Dropped;
Check(dropped == 0 && Near(actorBody.State.Y, 3.4f, .04f), "capsule lands on restored map collision");

using var timing = new TimingScope();
var walk = new FrameClip(["red", "blue"], .125, [new(0, 10), new(1, 20)]);
var idle = new FrameClip(["red"], .5, [new(0, 99)]);
var animation = timing.Own(new FramePlayer(walk, eventCapacity: 2));
animation.Advance(TimingStep.FromReal(.125));
Check(animation.AssetKey == "blue" && animation.EventsDue == 2 && animation.Events[0].EventId == 10 && animation.Events[1].EventId == 20,
    "public frame-entry markers report initial and crossed entries");
FrameMarker retainedMarker = animation.Events[1];
animation.Play(walk);
Check(animation.ElapsedSeconds == .125 && animation.EventsDue == 2 && animation.FrameIndex == 1,
    "repeated same-clip selection preserves phase and latest events");
animation.Play(idle);
Check(animation.AssetKey == "red" && animation.ElapsedSeconds == 0 && animation.Events.IsEmpty,
    "clip switch immediately resets phase and results");
animation.Advance(TimingStep.FromReal(.1, paused: true));
Check(animation.Events.IsEmpty && animation.ElapsedSeconds == 0, "game pause retains pending initial clip entry");
animation.Advance(TimingStep.FromReal(.01));
Check(animation.Events.Length == 1 && animation.Events[0].EventId == 99 && retainedMarker.EventId == 20,
    "new clip emits its initial entry and caller marker copy stays independent");
animation.Play(walk);
animation.Advance(TimingStep.FromReal(.5));
Check(animation.EventsDue == 5 && animation.EventsDropped == 3 && animation.FrameIndex == 0 &&
    animation.Events[0].EventId == 10 && animation.Events[1].EventId == 20,
    "bounded overflow preserves earliest markers while completing full advancement");
animation.Advance(TimingStep.FromReal(.125));

var viewport = new Viewport(96, 96, 192, 192);
var camera = new Camera { Zoom = 2 };
var follow = new CameraFollowOptions(HalfLifeSeconds: .125, Bounds: new(0, 0, 192, 192));
var target = new Vector2(80, 96);
camera = CameraFollow.Update(camera, target, viewport, TimingStep.FromReal(.125), follow);
Check(Near(camera.X, 16) && Near(camera.Y, 24) && camera.Zoom == 2, "camera uses framebuffer/zoom and half-life smoothing");
var pausedCamera = CameraFollow.Update(camera, target, viewport, TimingStep.FromReal(.125, paused: true), follow);
Check(pausedCamera.X == camera.X && pausedCamera.Y == camera.Y, "game-clock camera respects pause");
var realCamera = CameraFollow.Update(camera, target, viewport, TimingStep.FromReal(.125, paused: true),
    follow with { ClockDomain = ClockDomain.RealTime });
Check(Near(realCamera.X, 24) && Near(realCamera.Y, 36), "real-time camera follows while game is paused");
var bounded = CameraFollow.Snap(camera, new(1000, 1000), viewport, follow);
Check(bounded.X == 96 && bounded.Y == 96 && bounded.Zoom == 2, "snap applies zoom-aware whole-view bounds");

using var textures = new TextureBank(engine, loaded.Catalog, walk.AssetKeys);
var world = new World();
var actor = world.Create("Caller capsule sprite");
actor.LocalTransform = new(scale.ToPixels(actorBody.State.X), scale.ToPixels(actorBody.State.Y));
actor.Sprite = new(16, 32, AssetKey: animation.AssetKey, Layer: 2);
textures.Sync(world);
var batch = new SpriteBatch(16) { RegionResolver = textures.ResolveRegion };
world.ExtractSprites(batch);
map.AppendSprites(batch, new(0, 0, 192, 192));
Check(batch.Count == 8 && engine.Textures.Count == 1, "marked animation and edited map share caller atlas residency");
camera = CameraFollow.Snap(camera, new(actor.LocalTransform.X, actor.LocalTransform.Y), viewport, follow);
for (int i = 0; i < 3; i++) engine.Draw(camera, batch.RegionDraws);
Check(engine.GetStats().Frames == 3, "combined public feature scene submits through packaged native library");

if (authorSmoke)
    assertions += AuthorSmoke.Run(engine, assets, physics, actorBody, map, collision, scale, world, actor, textures, batch);

// Measure only successful warmed public queries; setup, errors and edits allocate.
for (int i = 0; i < 256; i++)
{
    physics.QueryCircle(20, 0, 1.1f, query, 16, 8);
    physics.QueryBox(20, 0, 1.1f, 1.1f, .37f, query, 16, 8);
}
long allocationStart = GC.GetAllocatedBytesForCurrentThread();
int resultTotal = 0;
for (int i = 0; i < 2000; i++)
{
    resultTotal += physics.QueryCircle(20, 0, 1.1f, query, 16, 8);
    resultTotal += physics.QueryBox(20, 0, 1.1f, 1.1f, .37f, query, 16, 8);
}
long queryAllocation = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
Check(resultTotal == 8000 && queryAllocation == 0 && Sorted(query[..2]), "4000 warmed exact queries allocate zero managed bytes");

bodies.Dispose(); map.Dispose(); sibling.Dispose(); textures.Dispose(); timing.Dispose(); physics.Step();
Check(physics.State is { Bodies: 0, Shapes: 0, RetiredShapes: 0 } && engine.Textures.Count == 0,
    "all combined public feature owners release physics and texture resources");
Check(copiedContact.Type == PhysicsEventType.ContactBegin && retainedMarker.EventId == 20,
    "copied contact and animation events survive owner cleanup");
Console.WriteLine($"PACKAGE FEATURES PASS assertions={assertions} frames={engine.GetStats().Frames} capsule-contact=verified tile-edit=coherent exact-queries=verified query-bytes={queryAllocation} bodies=0 textures=0");
