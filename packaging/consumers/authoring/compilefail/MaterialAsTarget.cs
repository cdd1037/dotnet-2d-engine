using System.Numerics;
using GameAuthoringLab;
internal static class MaterialAsTarget
{
    internal static object MustNotCompile(MaterialHandle value) => FramePass.ToTarget(value, new Camera { Zoom = 1 }, 0, 0);
}
