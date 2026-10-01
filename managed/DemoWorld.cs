namespace GameAuthoringLab;

internal static class DemoWorld
{
    public const int SpriteCount = 259;

    public static World Create()
    {
        var world = new World();
        Scene room = world.CreateScene("Sprite demo");
        Panel("Red panel", -175, -90, new Sprite2D(220, 180, 0.95f, 0.16f, 0.28f, 0.62f));
        Panel("Green panel", -90, -35, new Sprite2D(220, 180, 0.12f, 0.80f, 0.67f, 0.62f));
        Panel("Blue panel", 0, -90, new Sprite2D(220, 180, 0.24f, 0.39f, 1, 0.62f));
        for (int i = 0; i < SpriteCount - 3; i++)
        {
            Entity entity = world.Create($"Animated sprite {i}", room);
            entity.Behavior = new AnimatedSprite(i);
        }
        world.Update(0);
        return world;

        void Panel(string name, float x, float y, Sprite2D sprite)
        {
            Entity entity = world.Create(name, room, new Transform2D(x, y));
            entity.Sprite = sprite;
        }
    }

    private sealed class AnimatedSprite(int index) : IBehavior
    {
        private double _elapsed;

        public void Update(Entity entity, float deltaSeconds)
        {
            _elapsed += deltaSeconds;
            float phase = (float)_elapsed * 1.2f + index * 0.19f;
            entity.LocalTransform = new Transform2D(
                (index % 16 - 7.5f) * 46 + MathF.Sin(phase) * 12,
                (index / 16 - 7.5f) * 25 + MathF.Cos(phase) * 12);
            entity.Sprite = new Sprite2D(12 + 5 * (1 + MathF.Sin(phase)), 16,
                0.3f + 0.7f * ((index % 5) / 4f), 0.4f + 0.6f * ((index % 7) / 6f),
                0.9f, 0.35f + 0.6f * (0.5f + 0.5f * MathF.Sin(phase)));
        }
    }
}
