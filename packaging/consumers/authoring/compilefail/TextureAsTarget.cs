using System.Numerics;
using GameAuthoringLab;
internal static class TextureAsTarget
{
    internal static object MustNotCompile(TextureHandle value) => FramePass.ToTarget(value, new Camera { Zoom = 1 }, 0, 0);
}
