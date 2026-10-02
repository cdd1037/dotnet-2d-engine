using System.Numerics;

namespace CharacterMotionRecipe;

// Separate executable fixture; the default recipe does not call this code.
internal static class MotionChecks
{
    public static void Run(CharacterMotion game)
    {
        int assertions = 0;
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException($"FAIL: {name}");
            Console.WriteLine($"PASS: {name}");
            assertions++;
        }
        void Tick(int count, float axis = 0) { for (int i = 0; i < count; i++) game.Tick(axis); }
        int GroundedTicks(int count, float axis = 0)
        {
            int grounded = 0;
            for (int i = 0; i < count; i++) { game.Tick(axis); if (game.Grounded) grounded++; }
            return grounded;
        }
        static bool Near(Vector2 a, Vector2 b, float tolerance) => Vector2.Distance(a, b) <= tolerance;

        game.Teleport(new(130, 320)); // Keep the flat jump clear of the slope's left corner.
        Tick(120);
        Check(game.Grounded && Math.Abs(game.Position.Y - 460) < 2, "flat capsule landing matches authored dimensions");
        var start = game.Position;
        Tick(10, 1); Tick(10, -1);
        Check(Near(game.Position, start, 1), "flat direction reversal returns to start");
        game.Tick(jumpPressed: true);
        Check(!game.Grounded && game.Velocity.Y < 0 && game.RetainedSupportBody == 0, "jump detaches retained support");
        float upward = game.Velocity.Y, top = game.Position.Y;
        game.Tick(jumpPressed: true);
        Check(game.Velocity.Y > upward, "airborne press cannot reset jump velocity");
        for (int i = 0; i < 100; i++) { Tick(1); top = Math.Min(top, game.Position.Y); }
        float rise = start.Y - top;
        Console.WriteLine($"METRIC flat-jump-rise={rise:F3}px; authored target={CharacterMotion.JumpHeight}px");
        Check(Math.Abs(rise - 132) < .1f && Math.Abs(rise - CharacterMotion.JumpHeight) < 5,
            "explicit manual-gravity policy yields 132px rise within 128+/-5 contract");
        Check(game.Grounded, "flat jump lands again");

        game.Teleport(new(1190, 440)); Tick(30);
        Check(game.Grounded, "coyote setup grounded at floor edge");
        int edgeTicks = 0;
        while (game.Grounded && edgeTicks++ < 30) Tick(1, 1);
        Check(!game.Grounded && edgeTicks < 30, "walks past floor edge");
        game.Tick(1, true);
        Check(game.Velocity.Y < -400, "coyote press just after leaving floor jumps");
        game.Teleport(new(1190, 440)); Tick(30);
        edgeTicks = 0;
        while (game.Grounded && edgeTicks++ < 30) Tick(1, 1);
        Tick(8, 1); game.Tick(1, true);
        Check(!game.Grounded && game.Velocity.Y >= 0, "expired coyote window cannot jump");
        game.Teleport(new(130, 455)); game.Tick(jumpPressed: true); Tick(5);
        Check(game.Velocity.Y < -400, "press shortly before landing is buffered");
        game.Teleport(new(130, 200)); game.Tick(jumpPressed: true); Tick(100);
        Check(game.Grounded && Math.Abs(game.Position.Y - 460) < 2, "expired buffer cannot jump on landing");

        game.Teleport(new(350, 170)); Tick(120);
        ulong slopeBody = game.Support.Body;
        Check(game.Grounded && Math.Abs(game.Position.Y - 208) < 5 && slopeBody != game.PlatformBodyId,
            "lands on the slope, away from the floor and platform");
        start = game.Position;
        int grounded = GroundedTicks(60);
        Check(Near(game.Position, start, 1) && grounded == 60, "idle slope holds within 1px, grounded 60/60");
        Vector2 travel = new(MathF.Cos(CharacterMotion.SlopeAngle), MathF.Sin(CharacterMotion.SlopeAngle));
        travel *= CharacterMotion.Speed * .5f;
        start = game.Position; grounded = GroundedTicks(30, 1);
        Check(Near(game.Position - start, travel, 2) && grounded == 30 && game.Support.Body == slopeBody,
            "uphill tangent speed, same support, grounded 30/30");
        game.Tick(jumpPressed: true);
        Check(game.Velocity.Y < -400 && !game.Grounded && game.RetainedSupportBody == 0, "uphill jump detaches");
        game.Teleport(new(350, 170)); Tick(120);
        start = game.Position; grounded = GroundedTicks(30, -1);
        Check(Near(game.Position - start, -travel, 2) && grounded == 30 && game.Support.Body == slopeBody,
            "downhill tangent speed, same support, grounded 30/30");

        Check(game.PlatformPosition == new Vector2(950, 300), "platform starts at authored pose");
        game.Teleport(new(950, 220)); Tick(120);
        Check(game.Support.Body == game.PlatformBodyId && Math.Abs(game.Position.Y - 270) < 2,
            "lands on actual platform, not remote floor");
        void Ride(string phase, Vector2 velocity)
        {
            game.SetPlatformVelocity(velocity);
            Vector2 relative = game.Position - game.PlatformPosition;
            int supported = 0;
            for (int i = 0; i < 60; i++)
            {
                Tick(1);
                if (game.Support.Body == game.PlatformBodyId) supported++;
            }
            Vector2 drift = game.Position - game.PlatformPosition - relative;
            Console.WriteLine($"METRIC {phase}-relative-drift={drift}");
            Check(Math.Abs(drift.X) < .1f && Math.Abs(drift.Y) < .1f && supported == 60,
                $"{phase}: same platform 60/60, relative drift below .1px");
        }
        Ride("rise", new(60, -30));
        Ride("abrupt-reversal", new(-60, 30));
        game.SetPlatformVelocity(new(60, -30)); Tick(2); game.Tick(jumpPressed: true);
        Check(game.Velocity.Y < -500 && !game.Grounded && game.RetainedSupportBody == 0,
            "rising platform jump inherits its vertical speed and detaches");
        game.SetPlatformVelocity(new(-60, 30));
        game.Teleport(game.PlatformPosition + new Vector2(0, -30)); Tick(3);
        Ride("descending-reacquisition", new(-60, 30));

        start = game.Position;
        Vector2 platformStart = game.PlatformPosition;
        uint steps = game.Steps;
        game.SetPaused(true); Tick(10, 1); game.Tick(jumpPressed: true);
        Check(game.Position == start && game.PlatformPosition == platformStart && game.Steps == steps,
            "pause freezes both bodies and physics step count");
        Check(game.PresentationPosition(0) == start && game.PresentationPlatformPosition(0) == platformStart,
            "pause snaps both histories");
        game.SetPaused(false); Tick(1);
        Check(game.PresentationPosition(0) == start && game.PresentationPosition(1) == game.Position
            && game.PresentationPlatformPosition(0) == platformStart && game.PresentationPlatformPosition(1) == game.PlatformPosition,
            "resume preserves previous/current endpoints for both bodies");
        Check(Near(game.PresentationPosition(.5), (start + game.Position) / 2, .001f)
            && Near(game.PresentationPlatformPosition(.5), (platformStart + game.PlatformPosition) / 2, .001f),
            "both render midpoints interpolate without moving physics");
        Check(game.Grounded && game.RetainedSupportBody == game.PlatformBodyId, "resume ignores paused jump press");
        game.Teleport(game.Position); // Same location: identity reset cannot hide behind rays missing an old support.
        Check(game.RetainedSupportBody == 0 && game.Velocity == Vector2.Zero, "same-position teleport clears retained identity and velocity");
        Check(game.PresentationPosition(0) == game.Position && game.PresentationPlatformPosition(0) == game.PlatformPosition,
            "teleport snaps both histories");
        game.Teleport(new(200, 100)); game.Tick(jumpPressed: true);
        Check(game.Velocity.Y >= 0, "teleport clears coyote grace before airborne press");
        foreach (double alpha in new[] { -.1, 1.1, double.NaN, double.PositiveInfinity })
        {
            bool playerRejects = false, platformRejects = false;
            try { game.PresentationPosition(alpha); } catch (ArgumentOutOfRangeException) { playerRejects = true; }
            try { game.PresentationPlatformPosition(alpha); } catch (ArgumentOutOfRangeException) { platformRejects = true; }
            Check(playerRejects && platformRejects, $"both presentation paths reject alpha={alpha}");
        }
        Console.WriteLine($"CHARACTER MOTION CHECKS PASS assertions={assertions}");
    }
}
