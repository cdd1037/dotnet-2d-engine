using System.Numerics;
using GameAuthoringLab;
internal static class MaterialAsTexture
{
    internal static object MustNotCompile(MaterialHandle value) => new SpriteCommand(Transform2D.Identity, Vector2.One, Vector4.One, value);
}
