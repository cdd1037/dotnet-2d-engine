namespace GameAuthoringLab;

public enum PhysicsBodyType : uint { Static, Kinematic, Dynamic }
public enum PhysicsShapeType : uint { Circle, Box }
public enum PhysicsEventType : uint { ContactBegin = 1, ContactEnd, SensorBegin, SensorEnd }

/// <summary>A copied world snapshot. Retired shape identities count against capacity until the next step.</summary>
public readonly record struct PhysicsStateView(uint Bodies, uint Shapes, uint RetiredShapes, uint Steps, uint Events, uint Dropped)
{
    internal static PhysicsStateView FromNative(in PhysicsState value) =>
        new(value.Bodies, value.Shapes, value.RetiredShapes, value.Steps, value.Events, value.Dropped);
}

/// <summary>A copied body pose and velocity in meters, seconds and radians.</summary>
public readonly record struct PhysicsBodyStateView(float X, float Y, float Angle, float Vx, float Vy, float AngularVelocity, PhysicsBodyType Type, bool Awake)
{
    internal static PhysicsBodyStateView FromNative(in PhysicsBodyState value) =>
        new(value.X, value.Y, value.Angle, value.Vx, value.Vy, value.AngularVelocity, value.Type, value.Awake);
}

/// <summary>One completed fixed step. A nonzero Dropped count reports lost events; do not repeat the completed step.</summary>
public readonly record struct PhysicsStepResult(uint Events, uint Dropped, uint Index)
{
    internal static PhysicsStepResult FromNative(in PhysicsStep value) => new(value.Events, value.Dropped, value.Index);
}

/// <summary>A copied contact or sensor event. For sensors, A is the sensor and B the visitor; removed IDs are no longer live handles.</summary>
public readonly record struct PhysicsEventView(PhysicsEventType Type, ulong ShapeA, ulong ShapeB, ulong BodyA, ulong BodyB, bool RemovedA, bool RemovedB)
{
    internal static PhysicsEventView FromNative(in PhysicsEvent value) =>
        new((PhysicsEventType)value.Type, value.ShapeA, value.ShapeB, value.BodyA, value.BodyB, (value.Flags & 1) != 0, (value.Flags & 2) != 0);
}

/// <summary>A copied closest-ray result. When Hit is false, the remaining fields are zero; initial overlap is ignored.</summary>
public readonly record struct PhysicsRayHitView(bool Hit, ulong Shape, ulong Body, float X, float Y, float NormalX, float NormalY, float Fraction)
{
    internal static PhysicsRayHitView FromNative(in PhysicsRayHit value) =>
        new(value.Hit != 0, value.Shape, value.Body, value.X, value.Y, value.NormalX, value.NormalY, value.Fraction);
}
