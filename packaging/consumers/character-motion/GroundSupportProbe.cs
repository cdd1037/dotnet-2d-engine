using System.Numerics;
using GameAuthoringLab;

namespace CharacterMotionRecipe;

// Game-local meter-space query. Never steps, owns or mutates physics bodies.
internal readonly record struct GroundSupport(bool Supported, ulong Body, Vector2 Normal, Vector2 Velocity);
internal static class GroundSupportProbe
{
    private static readonly float[] Offsets = [-.6f, 0, .6f];

    public static GroundSupport Read(PhysicsWorld world, PhysicsBodyStateView player,
        float radius, float halfHeight, float reach, Func<ulong, Vector2> supportVelocity, ulong retainedSupport)
    {
        foreach (float offset in Offsets)
        {
            var hit = world.RayCast(player.X + radius * offset, player.Y, 0, halfHeight + reach, 2, 1);
            if (!hit.Hit || hit.NormalY >= -.7f) continue; // This game's actor=2 / terrain=1 filters.
            Vector2 normal = new(hit.NormalX, hit.NormalY), velocity = supportVelocity(hit.Body);
            // Uphill/rising travel is not a jump: compare separation along the normal.
            // Retain a known rider across reversal, but only while a ray still hits it.
            // The caller MUST clear retainedSupport on jump, teleport or external launch.
            if (hit.Body != retainedSupport && Vector2.Dot(new Vector2(player.Vx, player.Vy) - velocity, normal) > .1f)
                continue;
            return new(true, hit.Body, normal, velocity);
        }
        return default;
    }
}
