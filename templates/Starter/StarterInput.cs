using GameAuthoringLab;
namespace Dotnet2DStarter;

// Game-owned action names. Tokens are allocated by this map; no bit IDs to maintain.
internal sealed class StarterInput
{
    public InputActionMap Map { get; } = new();
    public InputAction Left { get; }
    public InputAction Right { get; }
    public InputAction Up { get; }
    public InputAction Down { get; }
    public InputAction Pulse { get; }
    public InputAction Pause { get; }
    public InputAction Restart { get; }
    public StarterInput()
    {
        Left = Map.AddAction(InputControl.Key(PhysicalKey.A), InputControl.Key(PhysicalKey.Left));
        Right = Map.AddAction(InputControl.Key(PhysicalKey.D), InputControl.Key(PhysicalKey.Right));
        Up = Map.AddAction(InputControl.Key(PhysicalKey.W), InputControl.Key(PhysicalKey.Up));
        Down = Map.AddAction(InputControl.Key(PhysicalKey.S), InputControl.Key(PhysicalKey.Down));
        Pulse = Map.AddAction(InputControl.Key(PhysicalKey.Space));
        Pause = Map.AddAction(InputControl.Key(PhysicalKey.Escape, allowUiConsumed: true));
        Restart = Map.AddAction(InputControl.Key(PhysicalKey.T));
    }
}
