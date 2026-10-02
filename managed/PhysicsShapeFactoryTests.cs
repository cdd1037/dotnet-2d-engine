namespace GameAuthoringLab;

internal static unsafe class PhysicsShapeFactoryTests
{
    public static int RunContracts()
    {
        int n = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception("PHYSICS SHAPE FACTORY: " + label); n++; }
        void Reject(Action action)
        {
            try { action(); }
            catch (ArgumentOutOfRangeException) { n++; return; }
            throw new Exception("PHYSICS SHAPE FACTORY invalid input accepted");
        }

        var circle = PhysicsShapeDefinition.Circle(radius: .5f);
        var box = PhysicsShapeDefinition.Box(halfWidth: 2, halfHeight: .25f);
        Check(circle == new PhysicsShapeDefinition(PhysicsShapeType.Circle, .5f), "circle preserves descriptor defaults");
        Check(box == new PhysicsShapeDefinition(PhysicsShapeType.Box, 2, .25f), "box preserves descriptor defaults and half extents");
        Check(circle.B == 0 && circle.Angle == 0, "circle has no unused dimension or local angle");

        var authoredCircle = PhysicsShapeDefinition.Circle(.25f, offsetX: -2, offsetY: 3,
            density: 4, friction: .2f, restitution: .8f, category: 1UL << 63, mask: 1UL << 62, group: -7, sensor: true);
        var authoredBox = PhysicsShapeDefinition.Box(.25f, .5f, offsetX: -2, offsetY: 3, angle: -.75f,
            density: 4, friction: .2f, restitution: .8f, category: 1UL << 63, mask: 1UL << 62, group: 7, sensor: true);
        Check(authoredCircle == new PhysicsShapeDefinition(PhysicsShapeType.Circle, .25f, 0, -2, 3, 0,
            4, .2f, .8f, 1UL << 63, 1UL << 62, -7, true), "circle options retain their units, 64-bit filters and signed group");
        Check(authoredBox == new PhysicsShapeDefinition(PhysicsShapeType.Box, .25f, .5f, -2, 3, -.75f,
            4, .2f, .8f, 1UL << 63, 1UL << 62, 7, true), "box options retain local angle, material, filters and sensor");

        foreach (var definition in new[] { circle, box, authoredCircle, authoredBox,
            PhysicsShapeDefinition.Circle(.001f, density: 0, friction: 0, restitution: 0, category: 0, mask: 0, group: int.MinValue),
            PhysicsShapeDefinition.Box(100, .001f, -100, 100, -10000, 10000, 10, 1, 0, 0, int.MaxValue, true),
            PhysicsShapeDefinition.Box(.001f, 100, 100, -100, 10000, 0, 0, 0, 0, 0, 0),
            PhysicsShapeDefinition.Circle(100, -100, 100, 10000, 10, 1, ulong.MaxValue, ulong.MaxValue, int.MaxValue, true) })
        {
            var native = definition.NativeValue();
            Check(native.Size == sizeof(PhysicsShapeDef) && native.Version == 1 && native.Type == definition.Type &&
                native.Flags == (definition.Sensor ? 1u : 0u) && native.Reserved == 0,
                "unchanged ABI header and only the existing sensor flag");
            Check(native.A == definition.A && native.B == definition.B && native.OffsetX == definition.OffsetX &&
                native.OffsetY == definition.OffsetY && native.Angle == definition.Angle && native.Density == definition.Density &&
                native.Friction == definition.Friction && native.Restitution == definition.Restitution &&
                native.Category == definition.Category && native.Mask == definition.Mask && native.Group == definition.Group,
                "native descriptor preserves every scalar, zero filter and group bit");
        }

        var zeros = PhysicsShapeDefinition.Box(1, 1, density: 0, friction: 0, restitution: 0, category: 0, mask: 0, group: 0);
        Check(zeros.Density == 0 && zeros.Friction == 0 && zeros.Restitution == 0 && zeros.Category == 0 &&
            zeros.Mask == 0 && zeros.Group == 0 && !zeros.Sensor, "explicit zeros are values, not requests for defaults");

        foreach (float invalid in new[] { 0f, -.1f, .0009f, 100.01f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Reject(() => PhysicsShapeDefinition.Circle(invalid));
            Reject(() => PhysicsShapeDefinition.Box(invalid, 1));
            Reject(() => PhysicsShapeDefinition.Box(1, invalid));
        }
        foreach (float invalid in new[] { -100.01f, 100.01f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Reject(() => PhysicsShapeDefinition.Circle(1, offsetX: invalid));
            Reject(() => PhysicsShapeDefinition.Circle(1, offsetY: invalid));
            Reject(() => PhysicsShapeDefinition.Box(1, 1, offsetX: invalid));
            Reject(() => PhysicsShapeDefinition.Box(1, 1, offsetY: invalid));
        }
        foreach (float invalid in new[] { -10000.1f, 10000.1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            Reject(() => PhysicsShapeDefinition.Box(1, 1, angle: invalid));
        foreach (float invalid in new[] { -.1f, 10000.1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Reject(() => PhysicsShapeDefinition.Circle(1, density: invalid));
            Reject(() => PhysicsShapeDefinition.Box(1, 1, density: invalid));
        }
        foreach (float invalid in new[] { -.1f, 10.1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Reject(() => PhysicsShapeDefinition.Circle(1, friction: invalid));
            Reject(() => PhysicsShapeDefinition.Box(1, 1, friction: invalid));
        }
        foreach (float invalid in new[] { -.1f, 1.1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Reject(() => PhysicsShapeDefinition.Circle(1, restitution: invalid));
            Reject(() => PhysicsShapeDefinition.Box(1, 1, restitution: invalid));
        }
        Reject(() => (circle with { B = 1 }).Validate());
        Reject(() => (circle with { Angle = .1f }).Validate());
        Reject(() => (circle with { Angle = float.NaN }).Validate());
        Reject(() => (box with { Type = (PhysicsShapeType)2 }).Validate());

        var scale = new PhysicsScale(50);
        var converted = PhysicsShapeDefinition.Box(scale.ToMeters(25), scale.ToMeters(10),
            offsetX: scale.ToMeters(-50), offsetY: scale.ToMeters(100), angle: MathF.PI / 2);
        Check(converted.A == .5f && converted.B == .2f && converted.OffsetX == -1 && converted.OffsetY == 2 &&
            converted.Angle == MathF.PI / 2, "meters/radians pass through without scaling, axis inversion or normalization");
        Console.WriteLine($"PHYSICS SHAPE FACTORY CONTRACT PASS assertions={n}; descriptor defaults, native mapping and numeric guards");
        return n;
    }

    public static int RunSimulation()
    {
        int n = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception("PHYSICS SHAPE FACTORY SIM: " + label); n++; }
        using var engine = EngineHost.Create(headless: true, maxSprites: 16);
        using (var physics = engine.OpenPhysics(new(0, 0, 1f / 60, 4)))
        {
            using var body = physics.CreateBody(new(PhysicsBodyType.Static, 2, 3));
            using var circle = body.AddShape(PhysicsShapeDefinition.Circle(.5f, offsetX: -2, offsetY: -3, sensor: true));
            using var box = body.AddShape(PhysicsShapeDefinition.Box(2, .1f, angle: MathF.PI / 2));
            ulong[] hits = new ulong[2];
            Check(physics.QueryCircle(.45f, 0, .02f, hits, sensors: PhysicsSensorQuery.Only) == 1 && hits[0] == circle.Id,
                "circle radius, local offset and sensor reach the native solver");
            Check(physics.QueryCircle(0, 0, .02f, hits, sensors: PhysicsSensorQuery.Exclude) == 0,
                "factory sensor has no ordinary solid query result");
            Check(physics.QueryCircle(2, 4.8f, .02f, hits) == 1 && hits[0] == box.Id &&
                physics.QueryCircle(3.8f, 3, .02f, hits) == 0, "box takes half extents and a radian local rotation");
            uint shapes = physics.State.Shapes;
            try { body.AddShape(PhysicsShapeDefinition.Circle(1) with { Angle = 1 }); }
            catch (ArgumentOutOfRangeException) { n++; }
            Check(physics.State.Shapes == shapes, "with-edited definitions still validate before native creation");
        }
        foreach (int group in new[] { 0, 7, -7 })
        {
            using var physics = engine.OpenPhysics();
            using var floor = physics.CreateBody(new(PhysicsBodyType.Static, 0, 3));
            using var ball = physics.CreateBody(new(PhysicsBodyType.Dynamic));
            ulong mask = group < 0 ? ulong.MaxValue : 0;
            floor.AddShape(PhysicsShapeDefinition.Box(10, .2f, mask: mask, group: group));
            ball.AddShape(PhysicsShapeDefinition.Circle(.2f, mask: mask, group: group));
            for (int i = 0; i < 120; i++) physics.Step();
            Check(group > 0 ? ball.State.Y is > 2.5f and < 2.7f : ball.State.Y > 5,
                "zero masks, positive group overrides and negative group suppression retain solver semantics");
        }
        Console.WriteLine($"PHYSICS SHAPE FACTORY SIMULATION PASS assertions={n}; real native geometry, sensor and collision filters");
        return n;
    }
}
