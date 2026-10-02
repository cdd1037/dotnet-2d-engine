using GameAuthoringLab;

namespace Relay;

// Application action masks. Kept at the original values so the legacy scripted
// scenarios and saved-game rules have identical behavior, without Native access.
internal static class RelayInput
{
    public const uint Left = 1, Right = 2, Up = 4, Down = 8, Space = 16,
        Escape = 32, Interact = 64, Drop = 128, Transition = 256,
        Save = 512, Load = 1024, FocusLost = 2048;

    public static InputActionMap CreateActions() => new(
        InputBinding.Key(Left, PhysicalKey.Left), InputBinding.Key(Left, PhysicalKey.A),
        InputBinding.Key(Right, PhysicalKey.Right), InputBinding.Key(Right, PhysicalKey.D),
        InputBinding.Key(Up, PhysicalKey.Up), InputBinding.Key(Up, PhysicalKey.W),
        InputBinding.Key(Down, PhysicalKey.Down), InputBinding.Key(Down, PhysicalKey.S),
        InputBinding.Key(Space, PhysicalKey.Space), InputBinding.Key(Escape, PhysicalKey.Escape, true),
        InputBinding.Key(Interact, PhysicalKey.E), InputBinding.Key(Drop, PhysicalKey.F),
        InputBinding.Key(Transition, PhysicalKey.T), InputBinding.Key(Save, PhysicalKey.F5),
        InputBinding.Key(Load, PhysicalKey.F9));
}
