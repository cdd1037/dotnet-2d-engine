using System.Numerics;
using GameAuthoringLab;
internal static class TargetAsMaterial
{
    internal static object MustNotCompile(RenderTargetHandle value) => new SpriteCommand(Transform2D.Identity, Vector2.One, Vector4.One) { Material = value };
}
