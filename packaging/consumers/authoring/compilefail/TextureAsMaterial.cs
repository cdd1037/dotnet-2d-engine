using System.Numerics;
using GameAuthoringLab;
internal static class TextureAsMaterial
{
    internal static object MustNotCompile(TextureHandle value) => new SpriteCommand(Transform2D.Identity, Vector2.One, Vector4.One) { Material = value };
}
