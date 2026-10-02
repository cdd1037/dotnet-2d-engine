using System.Runtime.InteropServices;

namespace GameAuthoringLab;

internal static unsafe class PhysicsExtensionTests
{
    private const ulong Sentinel = 0xFEEDCAFEDEADBEEFUL;

    public static int RunContracts()
    {
        int n = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception("PHYSICS EXTENSION: " + label); n++; }
        void Reject(Action action)
        {
            try { action(); }
            catch (ArgumentException) { n++; return; }
            throw new Exception("PHYSICS EXTENSION invalid capsule accepted");
        }

        Check(sizeof(PhysicsCapsuleDef) == 72 && sizeof(PhysicsOverlapQuery) == 56, "capsule/overlap ABI sizes");
        Check(Marshal.OffsetOf<PhysicsCapsuleDef>(nameof(PhysicsCapsuleDef.X1)).ToInt32() == 16 &&
              Marshal.OffsetOf<PhysicsCapsuleDef>(nameof(PhysicsCapsuleDef.Radius)).ToInt32() == 32 &&
              Marshal.OffsetOf<PhysicsCapsuleDef>(nameof(PhysicsCapsuleDef.Category)).ToInt32() == 48 &&
              Marshal.OffsetOf<PhysicsCapsuleDef>(nameof(PhysicsCapsuleDef.Group)).ToInt32() == 64 &&
              Marshal.OffsetOf<PhysicsCapsuleDef>(nameof(PhysicsCapsuleDef.Reserved2)).ToInt32() == 68,
              "capsule scalar and 64-bit field offsets");
        Check(Marshal.OffsetOf<PhysicsOverlapQuery>(nameof(PhysicsOverlapQuery.X)).ToInt32() == 16 &&
              Marshal.OffsetOf<PhysicsOverlapQuery>(nameof(PhysicsOverlapQuery.Angle)).ToInt32() == 32 &&
              Marshal.OffsetOf<PhysicsOverlapQuery>(nameof(PhysicsOverlapQuery.Category)).ToInt32() == 40 &&
              Marshal.OffsetOf<PhysicsOverlapQuery>(nameof(PhysicsOverlapQuery.Mask)).ToInt32() == 48,
              "overlap query scalar and 64-bit field offsets");
        Check(sizeof(PhysicsSensorQuery) == 4 && (uint)PhysicsSensorQuery.Include == 0 &&
              (uint)PhysicsSensorQuery.Exclude == 1 && (uint)PhysicsSensorQuery.Only == 2,
              "sensor query enum ABI");

        var capsule = new PhysicsCapsuleDefinition(-1, 0, 1, 0, .25f);
        capsule.Validate();
        Check(capsule.Density == 1 && capsule.Friction == .6f && capsule.Restitution == 0 &&
              capsule.Category == 1 && capsule.Mask == ulong.MaxValue && capsule.Group == 0 && !capsule.Sensor,
              "capsule defaults match other shapes");
        new PhysicsCapsuleDefinition(-100, -100, 100, 100, 100, 10000, 10, 1,
            Category: 0, Mask: 0, Group: int.MinValue, Sensor: true).Validate();
        new PhysicsCapsuleDefinition(0, 0, .01f, 0, .001f, 0, 0, 0, Group: int.MaxValue).Validate();
        new PhysicsCapsuleDefinition(0, 0, .008f, .008f, .1f).Validate();
        new PhysicsCapsuleDefinition(1, 0, -1, 0, .25f).Validate();
        Check(true, "valid inclusive limits, Euclidean endpoint separation and reversed endpoints");

        Reject(() => default(PhysicsCapsuleDefinition).Validate());
        Reject(() => (capsule with { X1 = float.NaN }).Validate());
        Reject(() => (capsule with { Y1 = float.PositiveInfinity }).Validate());
        Reject(() => (capsule with { X2 = float.NegativeInfinity }).Validate());
        Reject(() => (capsule with { Y2 = float.NaN }).Validate());
        Reject(() => (capsule with { X1 = -100.01f }).Validate());
        Reject(() => (capsule with { Y1 = 100.01f }).Validate());
        Reject(() => (capsule with { X2 = 100.01f }).Validate());
        Reject(() => (capsule with { Y2 = -100.01f }).Validate());
        Reject(() => new PhysicsCapsuleDefinition(1, 2, 1, 2, .25f).Validate());
        Reject(() => new PhysicsCapsuleDefinition(0, 0, .005f, 0, .25f).Validate());
        Reject(() => new PhysicsCapsuleDefinition(0, 0, .009999f, 0, .25f).Validate());
        Reject(() => new PhysicsCapsuleDefinition(0, 0, .006f, .006f, .25f).Validate());
        foreach (float radius in new[] { 0f, .0009f, 100.01f, float.NaN, float.PositiveInfinity })
            Reject(() => (capsule with { Radius = radius }).Validate());
        foreach (float density in new[] { -1f, 10000.1f, float.NaN, float.PositiveInfinity })
            Reject(() => (capsule with { Density = density }).Validate());
        foreach (float friction in new[] { -.1f, 10.1f, float.NaN, float.PositiveInfinity })
            Reject(() => (capsule with { Friction = friction }).Validate());
        foreach (float restitution in new[] { -.1f, 1.1f, float.NaN, float.PositiveInfinity })
            Reject(() => (capsule with { Restitution = restitution }).Validate());

        var scale = new PhysicsScale(50);
        var converted = new PhysicsCapsuleDefinition(scale.ToMeters(-50), scale.ToMeters(25),
            scale.ToMeters(50), scale.ToMeters(25), scale.ToMeters(10));
        converted.Validate();
        Check(converted.X1 == -1 && converted.Y1 == .5f && converted.X2 == 1 &&
              converted.Y2 == .5f && converted.Radius == .2f,
              "capsule endpoints and radius use explicit meter conversion without axis inversion");
        Console.WriteLine($"PHYSICS EXTENSION CONTRACT PASS assertions={n}; layouts and validation, no solver required");
        return n;
    }

    public static int RunSimulation()
    {
        int n = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception("PHYSICS EXTENSION SIM: " + label); n++; }
        T Reject<T>(Action action, string label) where T : Exception
        {
            try { action(); }
            catch (T e) { n++; return e; }
            throw new Exception("PHYSICS EXTENSION accepted: " + label);
        }

        using var engine = new EngineHost(true, 16);
        using (var physics = engine.OpenPhysics(new(0, 0, 1f / 60, 4)))
        {
            using var circleBody = physics.CreateBody(new(PhysicsBodyType.Static));
            using var circle = circleBody.AddShape(new(PhysicsShapeType.Circle, 1));
            using var boxBody = physics.CreateBody(new(PhysicsBodyType.Static, 6, 0, Angle: MathF.PI / 4));
            using var box = boxBody.AddShape(new(PhysicsShapeType.Box, 2, .15f));
            using var capsuleBody = physics.CreateBody(new(PhysicsBodyType.Static, 12, 0));
            using var capsule = capsuleBody.AddCapsule(new(-1, 0, 1, 0, .25f));
            ulong[] hits = new ulong[8];

            Check(physics.QueryCircle(0, 0, .05f, hits) == 1 && hits[0] == circle.Id,
                "circle query includes complete containment within a circle");
            Check(physics.QueryBox(0, 0, .05f, .05f, .4f, hits) == 1 && hits[0] == circle.Id,
                "rotated box query includes complete containment within a circle");
            Check(physics.QueryAabb(.75f, .75f, .85f, .85f, hits) == 1 && hits[0] == circle.Id &&
                  physics.QueryCircle(.8f, .8f, .05f, hits) == 0 &&
                  physics.QueryBox(.8f, .8f, .025f, .025f, MathF.PI / 4, hits) == 0,
                "exact queries reject circle AABB corner false positives");
            Check(physics.QueryBox(.9f, .9f, .5f, .02f, MathF.PI / 4, hits) == 1 && hits[0] == circle.Id &&
                  physics.QueryBox(.9f, .9f, .5f, .02f, -MathF.PI / 4, hits) == 0,
                "query box rotation changes geometric overlap against a circle");
            Check(physics.QueryCircle(7.2f, 1.2f, .06f, hits) == 1 && hits[0] == box.Id &&
                  physics.QueryBox(7.2f, 1.2f, .08f, .02f, MathF.PI / 4, hits) == 1 && hits[0] == box.Id,
                "circle and rotated box queries overlap a rotated thin box");
            Check(physics.QueryAabb(7.15f, -1.25f, 7.25f, -1.15f, hits) == 1 && hits[0] == box.Id &&
                  physics.QueryCircle(7.2f, -1.2f, .06f, hits) == 0 &&
                  physics.QueryBox(7.2f, -1.2f, .08f, .02f, MathF.PI / 4, hits) == 0,
                "exact queries reject rotated-box AABB corner false positives");
            Check(physics.QueryCircle(13.2f, 0, .04f, hits) == 1 && hits[0] == capsule.Id &&
                  physics.QueryBox(13.2f, 0, .04f, .03f, MathF.PI / 4, hits) == 1 && hits[0] == capsule.Id,
                "circle and rotated box queries include capsule rounded endpoints");
            Check(physics.QueryAabb(13.18f, .18f, 13.22f, .22f, hits) == 1 && hits[0] == capsule.Id &&
                  physics.QueryCircle(13.2f, .2f, .02f, hits) == 0 &&
                  physics.QueryBox(13.2f, .2f, .005f, .005f, MathF.PI / 4, hits) == 0,
                "exact queries reject capsule rounded-end AABB corner false positives");
            Check(physics.QueryCircle(6, 0, 8, hits) == 3 && hits[0] == circle.Id &&
                  hits[1] == box.Id && hits[2] == capsule.Id && hits[0] < hits[1] && hits[1] < hits[2],
                "circle overlap IDs are sorted independent of broad-phase traversal");
            Check(physics.QueryBox(6, 0, 8, 3, .17f, hits) == 3 && hits[0] == circle.Id &&
                  hits[1] == box.Id && hits[2] == capsule.Id,
                "rotated box overlap IDs use the same stable sorted identities");
            Check(physics.QueryCircle(50, 50, 1, Span<ulong>.Empty) == 0 &&
                  physics.QueryBox(50, 50, 1, 1, 0, Span<ulong>.Empty) == 0,
                "empty spans accept empty exact query results");

            ulong[] shortOutput = [Sentinel, Sentinel];
            var circleCapacity = Reject<ArgumentException>(() => physics.QueryCircle(6, 0, 8, shortOutput), "circle insufficient capacity");
            Check(shortOutput.All(value => value == Sentinel) && circleCapacity.Message.Contains("3"),
                "circle capacity failure reports required size and leaves every output unchanged");
            var boxCapacity = Reject<ArgumentException>(() => physics.QueryBox(6, 0, 8, 3, .17f, shortOutput), "box insufficient capacity");
            Check(shortOutput.All(value => value == Sentinel) && boxCapacity.Message.Contains("3"),
                "box capacity failure reports required size and leaves every output unchanged");
            Reject<ArgumentException>(() => physics.QueryCircle(0, 0, 1, Span<ulong>.Empty), "nonempty result into empty span");
            Array.Fill(hits, Sentinel);
            Check(physics.QueryCircle(6, 0, 8, hits.AsSpan(2, 4)) == 3 && hits[2] == circle.Id &&
                  hits[3] == box.Id && hits[4] == capsule.Id && hits[0] == Sentinel && hits[1] == Sentinel &&
                  hits[5] == Sentinel && hits[6] == Sentinel && hits[7] == Sentinel,
                "caller span slice writes only the returned prefix");
            ulong[] maximum = new ulong[512];
            Array.Fill(maximum, Sentinel);
            Check(physics.QueryBox(6, 0, 8, 3, .17f, maximum) == 3 && maximum[3] == Sentinel && maximum[511] == Sentinel,
                "maximum public capacity is accepted without writing unused storage");
            ulong[] oversized = new ulong[513];
            Array.Fill(oversized, Sentinel);
            Reject<ArgumentOutOfRangeException>(() => physics.QueryCircle(0, 0, 1, oversized), "circle capacity over 512");
            Reject<ArgumentOutOfRangeException>(() => physics.QueryBox(0, 0, 1, 1, 0, oversized), "box capacity over 512");
            Check(oversized.All(value => value == Sentinel), "oversized query rejection leaves output unchanged");

            Array.Fill(hits, Sentinel);
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 10000.1f, -10000.1f })
            {
                Reject<ArgumentOutOfRangeException>(() => physics.QueryCircle(invalid, 0, 1, hits), "circle X bounds");
                Reject<ArgumentOutOfRangeException>(() => physics.QueryCircle(0, invalid, 1, hits), "circle Y bounds");
                Reject<ArgumentOutOfRangeException>(() => physics.QueryBox(invalid, 0, 1, 1, 0, hits), "box X bounds");
                Reject<ArgumentOutOfRangeException>(() => physics.QueryBox(0, invalid, 1, 1, 0, hits), "box Y bounds");
                Reject<ArgumentOutOfRangeException>(() => physics.QueryBox(0, 0, 1, 1, invalid, hits), "box angle bounds");
            }
            foreach (float invalid in new[] { 0f, .0009f, 100.01f, float.NaN, float.PositiveInfinity })
            {
                Reject<ArgumentOutOfRangeException>(() => physics.QueryCircle(0, 0, invalid, hits), "circle radius bounds");
                Reject<ArgumentOutOfRangeException>(() => physics.QueryBox(0, 0, invalid, 1, 0, hits), "box half width bounds");
                Reject<ArgumentOutOfRangeException>(() => physics.QueryBox(0, 0, 1, invalid, 0, hits), "box half height bounds");
            }
            Reject<ArgumentOutOfRangeException>(() => physics.QueryCircle(0, 0, 1, hits, sensors: (PhysicsSensorQuery)3), "unknown circle sensor policy");
            Reject<ArgumentOutOfRangeException>(() => physics.QueryBox(0, 0, 1, 1, 0, hits, sensors: (PhysicsSensorQuery)uint.MaxValue), "unknown box sensor policy");
            Check(hits.All(value => value == Sentinel), "invalid query preflight never changes caller output");
            Check(physics.QueryCircle(10000, -10000, .001f, hits) == 0 &&
                  physics.QueryCircle(-10000, 10000, 100, hits) == 0 &&
                  physics.QueryBox(10000, -10000, .001f, 100, 10000, hits) == 0 &&
                  physics.QueryBox(-10000, 10000, 100, .001f, -10000, hits) == 0,
                "query position, size and angle bounds are inclusive");
            uint priorShapes = physics.State.Shapes;
            Reject<ArgumentException>(() => capsuleBody.AddCapsule(new(0, 0, 0, 0, .1f)), "degenerate capsule preflight");
            Check(physics.State.Shapes == priorShapes, "invalid capsule creation does not mutate the world");

            Span<ulong> warmedOutput = stackalloc ulong[8];
            for (int i = 0; i < 512; i++)
            {
                physics.QueryCircle(6, 0, 8, warmedOutput);
                physics.QueryBox(6, 0, 8, 3, .17f, warmedOutput, sensors: PhysicsSensorQuery.Exclude);
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            int total = 0;
            for (int i = 0; i < 1000; i++)
            {
                total += physics.QueryCircle(6, 0, 8, warmedOutput);
                total += physics.QueryBox(6, 0, 8, 3, .17f, warmedOutput, sensors: PhysicsSensorQuery.Exclude);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check(total == 6000 && allocated == 0, "warmed exact queries into caller-owned Span allocate zero managed bytes");
        }

        using (var physics = engine.OpenPhysics(new(0, 0, 1f / 60, 4)))
        {
            using var owner = physics.CreateBody(new(PhysicsBodyType.Static));
            using var solid = owner.AddCapsule(new(-.5f, 0, .5f, 0, .2f, Category: 2, Mask: 4, Group: 7));
            using var sensor = owner.AddCapsule(new(-.5f, 0, .5f, 0, .2f, Category: 8, Mask: 4, Group: -7, Sensor: true));
            using var masked = owner.AddShape(new(PhysicsShapeType.Box, .2f, .2f, Category: 2, Mask: 0, Group: 7));
            using var circleSensor = owner.AddShape(new(PhysicsShapeType.Circle, .2f, Category: 16, Mask: 4, Sensor: true));
            const ulong highBit = 1UL << 63;
            using var high = owner.AddCapsule(new(-.5f, 0, .5f, 0, .2f, Category: highBit, Mask: highBit));
            ulong[] hits = new ulong[8];
            for (int queryType = 0; queryType < 2; queryType++)
            {
                int Query(ulong category, ulong mask, PhysicsSensorQuery sensors = PhysicsSensorQuery.Include) =>
                    queryType == 0 ? physics.QueryCircle(0, 0, 1, hits, category, mask, sensors) :
                    physics.QueryBox(0, 0, 1, 1, .37f, hits, category, mask, sensors);
                Check(Query(4, ulong.MaxValue) == 3 && hits[0] == solid.Id && hits[1] == sensor.Id && hits[2] == circleSensor.Id,
                    "exact query includes solids and sensors by default and ignores collision groups");
                Check(Query(4, ulong.MaxValue, PhysicsSensorQuery.Exclude) == 1 && hits[0] == solid.Id,
                    "Exclude sensor policy keeps only eligible solid shapes");
                Check(Query(4, ulong.MaxValue, PhysicsSensorQuery.Only) == 2 && hits[0] == sensor.Id && hits[1] == circleSensor.Id,
                    "Only sensor policy keeps eligible capsule and circle sensors");
                Check(Query(4, 2) == 1 && hits[0] == solid.Id && Query(4, 8) == 1 && hits[0] == sensor.Id,
                    "query mask filters shape category");
                Check(Query(1, ulong.MaxValue) == 0 && Query(0, ulong.MaxValue) == 0,
                    "shape mask reciprocally filters query category");
                Check(Query(4, 0) == 0 && Query(ulong.MaxValue, 2) == 1 && hits[0] == solid.Id,
                    "positive collision groups never override either query mask");
                Check(Query(highBit, highBit) == 1 && hits[0] == high.Id,
                    "exact overlap filters preserve all 64 category and mask bits");
                Check(Query(highBit, highBit, PhysicsSensorQuery.Only) == 0,
                    "sensor policy and reciprocal filters both apply");
            }
        }

        using (var physics = engine.OpenPhysics(new(0, 0, 1f / 60, 4)))
        {
            var scale = new PhysicsScale(50);
            using var body = physics.CreateBody(new(PhysicsBodyType.Static, scale.ToMeters(500), scale.ToMeters(1000), Angle: MathF.PI / 2));
            using var capsule = body.AddCapsule(new(0, scale.ToMeters(50), 0, scale.ToMeters(150), scale.ToMeters(10)));
            Span<ulong> hits = stackalloc ulong[4];
            Check(physics.QueryCircle(8, 20, .05f, hits) == 1 && hits[0] == capsule.Id &&
                  physics.QueryCircle(7, 20, .05f, hits) == 1 && hits[0] == capsule.Id &&
                  physics.QueryBox(8, 20, .05f, .05f, .23f, hits) == 1 && hits[0] == capsule.Id,
                "body rotation transforms offset local capsule endpoints into world meters");
            Check(physics.QueryCircle(10, 20, .05f, hits) == 0 && physics.QueryCircle(10, 22, .05f, hits) == 0 &&
                  physics.QueryCircle(6.7f, 20, .05f, hits) == 0,
                "capsule geometry is neither centered automatically nor interpreted as world-space endpoints");
            var ray = physics.RayCast(5, 20, 6, 0);
            Check(ray.Hit && ray.Shape == capsule.Id && ray.Body == body.Id && Math.Abs(ray.X - 6.8f) < .03f && ray.NormalX < -.95f,
                "existing closest ray casts support transformed capsule geometry");
            Check(scale.ToPixels(body.State.X) == 500 && scale.ToPixels(body.State.Y) == 1000,
                "shape-local endpoints do not change the authored body pose");

            using var light = physics.CreateBody(new(PhysicsBodyType.Dynamic, 30, 0, FixedRotation: true));
            using var lightShape = light.AddCapsule(new(-.5f, 0, .5f, 0, .25f, Density: 1));
            using var heavy = physics.CreateBody(new(PhysicsBodyType.Dynamic, 35, 0, FixedRotation: true));
            using var heavyShape = heavy.AddCapsule(new(-.5f, 0, .5f, 0, .25f, Density: 2));
            light.ApplyImpulse(1, 0);
            heavy.ApplyImpulse(1, 0);
            Check(light.State.Vx > 0 && Math.Abs(light.State.Vx - 2 * heavy.State.Vx) < .001f,
                "capsule material density contributes to actual dynamic body mass");
        }

        using (var physics = engine.OpenPhysics())
        {
            using var floor = physics.CreateBody(new(PhysicsBodyType.Static, 0, 5));
            using var floorShape = floor.AddShape(new(PhysicsShapeType.Box, 3, .5f));
            using var falling = physics.CreateBody(new(PhysicsBodyType.Dynamic, 0, 0, FixedRotation: true));
            using var fallingShape = falling.AddCapsule(new(0, -.6f, 0, .6f, .25f));
            using var platform = physics.CreateBody(new(PhysicsBodyType.Static, 10, 5));
            using var platformShape = platform.AddCapsule(new(-2, 0, 2, 0, .5f));
            using var ball = physics.CreateBody(new(PhysicsBodyType.Dynamic, 10, 0));
            using var ballShape = ball.AddShape(new(PhysicsShapeType.Circle, .3f));
            bool capsuleBegan = false, circleBegan = false, dropped = false;
            for (int i = 0; i < 300; i++)
            {
                dropped |= physics.Step().Dropped != 0;
                foreach (var e in physics.Events)
                {
                    if (e.Type != PhysicsEventType.ContactBegin) continue;
                    capsuleBegan |= Pair(e.ShapeA, e.ShapeB, fallingShape.Id, floorShape.Id) && Pair(e.BodyA, e.BodyB, falling.Id, floor.Id);
                    circleBegan |= Pair(e.ShapeA, e.ShapeB, ballShape.Id, platformShape.Id) && Pair(e.BodyA, e.BodyB, ball.Id, platform.Id);
                }
            }
            Check(!dropped && capsuleBegan && falling.State.Y is > 3.60f and < 3.70f && Math.Abs(falling.State.Vy) < .01f,
                "dynamic capsule settles on a box with stable body/shape contact identities");
            Check(circleBegan && ball.State.Y is > 4.15f and < 4.25f && Math.Abs(ball.State.Vy) < .01f,
                "circle settles on a static capsule with contact begin events");
            Check(!falling.State.Awake, "resting dynamic capsule can sleep");
            falling.Teleport(0, -2);
            physics.Step();
            bool ended = false;
            foreach (var e in physics.Events)
                ended |= e.Type == PhysicsEventType.ContactEnd && Pair(e.ShapeA, e.ShapeB, fallingShape.Id, floorShape.Id);
            Check(ended, "teleporting the capsule away emits its contact end");
        }

        using (var physics = engine.OpenPhysics(new(0, 0, 1f / 60, 4)))
        {
            using var sensorBody = physics.CreateBody(new(PhysicsBodyType.Static, 0, 2));
            using var sensor = sensorBody.AddCapsule(new(-1, 0, 1, 0, .35f, Density: 0, Sensor: true));
            using var visitor = physics.CreateBody(new(PhysicsBodyType.Dynamic, 0, 0, Vy: 2, FixedRotation: true));
            using var visitorShape = visitor.AddCapsule(new(0, -.2f, 0, .2f, .1f));
            bool began = false, ended = false, wrongIdentity = false, dropped = false;
            for (int i = 0; i < 150; i++)
            {
                dropped |= physics.Step().Dropped != 0;
                foreach (var e in physics.Events)
                {
                    if (e.Type is not (PhysicsEventType.SensorBegin or PhysicsEventType.SensorEnd)) continue;
                    wrongIdentity |= e.ShapeA != sensor.Id || e.BodyA != sensorBody.Id || e.ShapeB != visitorShape.Id || e.BodyB != visitor.Id;
                    began |= e.Type == PhysicsEventType.SensorBegin;
                    ended |= e.Type == PhysicsEventType.SensorEnd;
                }
            }
            Check(!dropped && !wrongIdentity && began && ended && Math.Abs(visitor.State.Vy - 2) < .0001f && Math.Abs(visitor.State.Y - 5) < .005f,
                "capsule sensor crossing emits ordered sensor/visitor identities without collision response");
            visitor.Teleport(0, 2);
            visitor.SetVelocity(0, 0);
            physics.Step();
            ulong removedShape = visitorShape.Id, removedBody = visitor.Id;
            visitor.Dispose();
            physics.Step();
            bool removedEnd = false;
            foreach (var e in physics.Events)
                removedEnd |= e.Type == PhysicsEventType.SensorEnd && e.ShapeA == sensor.Id &&
                              e.ShapeB == removedShape && e.BodyB == removedBody && e.RemovedB;
            Check(removedEnd, "removed capsule visitor preserves retired identity in sensor end events");
        }

        ulong staleBody;
        PhysicsWorld oldWorld;
        PhysicsBody oldBody;
        PhysicsShape oldCapsule;
        using (var physics = engine.OpenPhysics(new(0, 0, 1f / 60, 4)))
        {
            oldWorld = physics;
            oldBody = physics.CreateBody(new(PhysicsBodyType.Static));
            oldCapsule = oldBody.AddCapsule(new(-1, 0, 1, 0, .25f));
            staleBody = oldBody.Id;
            ulong[] hits = [Sentinel, Sentinel];
            Exception? circleFailure = null, boxFailure = null, capsuleFailure = null;
            var thread = new Thread(() =>
            {
                try { physics.QueryCircle(0, 0, 2, hits); } catch (Exception e) { circleFailure = e; }
                try { physics.QueryBox(0, 0, 2, 2, 0, hits); } catch (Exception e) { boxFailure = e; }
                try { oldBody.AddCapsule(new(-1, 0, 1, 0, .25f)); } catch (Exception e) { capsuleFailure = e; }
            });
            thread.Start();
            thread.Join();
            Check(circleFailure is InvalidOperationException && boxFailure is InvalidOperationException && capsuleFailure is InvalidOperationException,
                "exact queries and capsule creation enforce the creating thread");
            Check(hits.All(value => value == Sentinel) && physics.State.Shapes == 1,
                "wrong-thread failures do not mutate output or shape ownership");
            oldCapsule.Dispose();
            Check(physics.QueryCircle(0, 0, 2, hits) == 0 && physics.QueryBox(0, 0, 2, 2, 0, hits) == 0,
                "disposed capsules disappear from both exact query kinds immediately");
            Reject<ObjectDisposedException>(() => _ = oldCapsule.Id, "disposed capsule handle access");
            oldCapsule = oldBody.AddCapsule(new(-1, 0, 1, 0, .25f));
            oldBody.Dispose();
            Reject<ObjectDisposedException>(() => oldBody.AddCapsule(new(-1, 0, 1, 0, .25f)), "capsule creation on disposed body");
            Reject<ObjectDisposedException>(() => _ = oldCapsule.Id, "body destruction invalidates its capsule wrapper");
            Check(physics.QueryCircle(0, 0, 2, hits) == 0, "destroyed body capsule cannot leak into query results");
        }
        using (var physics = engine.OpenPhysics(new(0, 0, 1f / 60, 4)))
        {
            using var owner = physics.CreateBody(new(PhysicsBodyType.Static));
            using var capsule = owner.AddCapsule(new(-1, 0, 1, 0, .25f));
            ulong[] hits = [Sentinel, Sentinel];
            Reject<ObjectDisposedException>(() => oldWorld.QueryCircle(0, 0, 2, hits), "circle query on old world after reopen");
            Reject<ObjectDisposedException>(() => oldWorld.QueryBox(0, 0, 2, 2, 0, hits), "box query on old world after reopen");
            Check(hits.All(value => value == Sentinel), "closed-world query errors leave output unchanged");
            var raw = new PhysicsCapsuleDef
            {
                Size = 72, Version = 1, X1 = -1, X2 = 1, Radius = .25f,
                Density = 1, Friction = .6f, Category = 1, Mask = ulong.MaxValue
            };
            ulong rejected = Sentinel;
            Check(PhysicsNative.CreateCapsule(engine.NativeContext, staleBody, &raw, &rejected) != 0 && rejected == 0,
                "raw capsule creation rejects stale body identity after world reopen");
            oldCapsule.Dispose();
            oldBody.Dispose();
            oldWorld.Dispose();
            Check(physics.State.Shapes == 1 && physics.QueryCircle(0, 0, 2, hits) == 1 && hits[0] == capsule.Id,
                "late old-wrapper disposal cannot release a new world's capsule");
        }
        using (var physics = engine.OpenPhysics())
        {
            var body = physics.CreateBody(new(PhysicsBodyType.Static));
            var capsule = body.AddCapsule(new(-1, 0, 1, 0, .25f));
            engine.Dispose();
            Reject<ObjectDisposedException>(() => physics.QueryCircle(0, 0, 2, new ulong[1]), "circle query after engine teardown");
            Reject<ObjectDisposedException>(() => physics.QueryBox(0, 0, 2, 2, 0, new ulong[1]), "box query after engine teardown");
            Reject<ObjectDisposedException>(() => body.AddCapsule(new(-1, 0, 1, 0, .25f)), "capsule creation after engine teardown");
            capsule.Dispose();
            body.Dispose();
            Check(true, "engine teardown permits safe late capsule/body disposal");
        }

        Console.WriteLine($"PHYSICS EXTENSION SIMULATION PASS assertions={n}; exact overlap, capsules, filters, allocation and lifetime");
        return n;
    }

    private static bool Pair(ulong a, ulong b, ulong expectedA, ulong expectedB) =>
        (a == expectedA && b == expectedB) || (a == expectedB && b == expectedA);
}
