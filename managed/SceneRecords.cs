using System.Text.Json.Serialization;
namespace GameAuthoringLab;

public sealed class EntityRecord
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required Guid SceneId { get; init; }
    public required Guid? ParentId { get; init; }
    public required Guid? OwnerId { get; init; }
    public required TransformRecord Transform { get; init; }
    public required SpriteRecord? Sprite { get; init; }
}

public sealed class TransformRecord
{
    public required float X { get; init; }
    public required float Y { get; init; }
    public required float ScaleX { get; init; }
    public required float ScaleY { get; init; }
    public required float Rotation { get; init; }
    public required float Shear { get; init; }
    internal Transform2D ToTransform() => new(X, Y, ScaleX, ScaleY, Rotation, Shear);
    internal static TransformRecord From(Transform2D value) => new()
    { X = value.X, Y = value.Y, ScaleX = value.ScaleX, ScaleY = value.ScaleY, Rotation = value.Rotation, Shear = value.Shear };
}

public sealed class SpriteRecord
{
    public required float Width { get; init; }
    public required float Height { get; init; }
    public required float R { get; init; }
    public required float G { get; init; }
    public required float B { get; init; }
    public required float A { get; init; }
    public required string? AssetKey { get; init; }
    public required int Layer { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool FlipX { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool FlipY { get; init; }
    internal Sprite2D ToSprite() => new(Width, Height, R, G, B, A, AssetKey, Layer, FlipX, FlipY);
    internal static SpriteRecord From(Sprite2D value) => new()
    { Width = value.Width, Height = value.Height, R = value.R, G = value.G, B = value.B, A = value.A, AssetKey = value.AssetKey, Layer = value.Layer, FlipX = value.FlipX, FlipY = value.FlipY };
}
