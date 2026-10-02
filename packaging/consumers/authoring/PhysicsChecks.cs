using GameAuthoringLab;

internal static class PhysicsChecks
{
    internal static void Run(Checks check, EngineHost engine)
    {
        var circle = PhysicsShapeDefinition.Circle(radius: .5f);
        var box = PhysicsShapeDefinition.Box(halfWidth: 2, halfHeight: .1f);
        check.That(circle.Type == PhysicsShapeType.Circle && circle.A == .5f && circle.B == 0 &&
            box.Type == PhysicsShapeType.Box && box.A == 2 && box.B == .1f,
            "semantic factories preserve the original public descriptor and its defaults");
        var zeros = PhysicsShapeDefinition.Circle(.5f, density: 0, friction: 0, restitution: 0,
            category: 0, mask: 0, group: 0, sensor: true);
        check.That(zeros is { B: 0, Angle: 0, Density: 0, Friction: 0, Restitution: 0,
            Category: 0, Mask: 0, Group: 0, Sensor: true }, "explicit zeros and sensor survive circle authoring");
        var highBits = PhysicsShapeDefinition.Box(1, 2, category: 1UL << 63, mask: 1UL << 62, group: int.MinValue);
        check.That(highBits.Category == 1UL << 63 && highBits.Mask == 1UL << 62 && highBits.Group == int.MinValue,
            "box preserves full-width collision categories, masks and signed groups");
        check.Reject<ArgumentOutOfRangeException>(() => PhysicsShapeDefinition.Circle(0), "zero circle radius");
        check.Reject<ArgumentOutOfRangeException>(() => PhysicsShapeDefinition.Box(-1, 1), "negative box half width");
        check.Reject<ArgumentOutOfRangeException>(() => PhysicsShapeDefinition.Box(1, 0), "zero box half height");
        check.Reject<ArgumentOutOfRangeException>(() => PhysicsShapeDefinition.Box(1, 1, angle: float.NaN), "nonfinite radian angle");
        check.Reject<ArgumentOutOfRangeException>(() => PhysicsShapeDefinition.Box(1, 1, angle: 10001), "out-of-range radian angle");

        using (var physics = engine.OpenPhysics(new(0, 0, 1f / 60, 4)))
        {
            using var body = physics.CreateBody(new(PhysicsBodyType.Static, 2, 3));
            using var circleShape = body.AddShape(PhysicsShapeDefinition.Circle(.5f, offsetX: -2, offsetY: -3, sensor: true));
            using var boxShape = body.AddShape(PhysicsShapeDefinition.Box(2, .1f, angle: MathF.PI / 2));
            using var noCategory = body.AddShape(PhysicsShapeDefinition.Circle(1, offsetX: 10, category: 0));
            using var noMask = body.AddShape(PhysicsShapeDefinition.Box(1, 1, offsetX: 10, mask: 0));
            Span<ulong> hits = stackalloc ulong[4];
            check.That(physics.QueryCircle(.45f, 0, .02f, hits, sensors: PhysicsSensorQuery.Only) == 1 &&
                hits[0] == circleShape.Id, "meter radius/local offsets and sensor flag reach native geometry");
            check.That(physics.QueryCircle(0, 0, .02f, hits, sensors: PhysicsSensorQuery.Exclude) == 0,
                "sensor exclusion matches factory-authored shapes");
            check.That(physics.QueryCircle(2, 4.8f, .02f, hits) == 1 && hits[0] == boxShape.Id &&
                physics.QueryCircle(3.8f, 3, .02f, hits) == 0, "box uses half extents and radians without unit conversion");
            check.That(physics.QueryCircle(12, 3, .1f, hits) == 0, "zero category and zero mask remain effective native filters");
            uint shapes = physics.State.Shapes;
            check.Reject<ArgumentOutOfRangeException>(() => body.AddShape(circle with { Angle = .1f }),
                "with-edited circle angle is still validated by AddShape");
            check.Reject<ArgumentOutOfRangeException>(() => body.AddShape(box with { Friction = float.PositiveInfinity }),
                "with-edited box material is still validated by AddShape");
            check.That(physics.State.Shapes == shapes, "invalid descriptors leave native shape count unchanged");
            body.Dispose();
            check.That(physics.State.Bodies == 0 && physics.State.Shapes == 0, "factory shapes retain body-owned lifetime");
            check.Reject<ObjectDisposedException>(() => _ = circleShape.Id, "shape use after owning body disposal");
        }

        foreach (int group in new[] { 0, 7, -7 })
        {
            using var physics = engine.OpenPhysics();
            using var floor = physics.CreateBody(new(PhysicsBodyType.Static, 0, 3));
            using var ball = physics.CreateBody(new(PhysicsBodyType.Dynamic));
            ulong mask = group < 0 ? ulong.MaxValue : 0;
            floor.AddShape(PhysicsShapeDefinition.Box(10, .2f, mask: mask, group: group));
            ball.AddShape(PhysicsShapeDefinition.Circle(.2f, mask: mask, group: group));
            bool contact = false;
            for (int i = 0; i < 120; i++)
            {
                physics.Step();
                foreach (var e in physics.Events) contact |= e.Type == PhysicsEventType.ContactBegin;
            }
            check.That(group > 0 ? contact && ball.State.Y is > 2.5f and < 2.7f : !contact && ball.State.Y > 5,
                "zero group uses masks; matching positive/negative groups preserve collision overrides");
        }

        using (var physics = engine.OpenPhysics(new(0, 0, 1f / 60, 4)))
        {
            using var sensor = physics.CreateBody(new(PhysicsBodyType.Static, 0, 3));
            sensor.AddShape(PhysicsShapeDefinition.Circle(1, density: 0, sensor: true));
            using var visitor = physics.CreateBody(new(PhysicsBodyType.Dynamic, Vy: 4));
            visitor.AddShape(PhysicsShapeDefinition.Box(.2f, .2f));
            bool began = false, ended = false;
            for (int i = 0; i < 100; i++)
            {
                physics.Step();
                foreach (var e in physics.Events)
                {
                    began |= e.Type == PhysicsEventType.SensorBegin;
                    ended |= e.Type == PhysicsEventType.SensorEnd;
                }
            }
            check.That(began && ended && visitor.State.Vy == 4, "factory sensor emits begin/end without collision response");
        }
    }
}
