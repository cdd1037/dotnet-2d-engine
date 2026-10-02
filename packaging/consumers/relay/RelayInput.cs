using GameAuthoringLab;

namespace Relay;

// These masks belong to RELAY's pure simulation, never to the engine input protocol.
internal sealed class RelayInput
{
    public const uint Left = 1, Right = 2, Up = 4, Down = 8, Space = 16,
        Escape = 32, Interact = 64, Drop = 128, Transition = 256,
        Save = 512, Load = 1024, FocusLost = 2048;
    public InputActionMap Map { get; } = new();
    private readonly (uint Rule, InputAction Action)[] actions;
    public RelayInput()
    {
        actions = [
            (Left, Map.AddAction(InputControl.Key(PhysicalKey.Left), InputControl.Key(PhysicalKey.A))),
            (Right, Map.AddAction(InputControl.Key(PhysicalKey.Right), InputControl.Key(PhysicalKey.D))),
            (Up, Map.AddAction(InputControl.Key(PhysicalKey.Up), InputControl.Key(PhysicalKey.W))),
            (Down, Map.AddAction(InputControl.Key(PhysicalKey.Down), InputControl.Key(PhysicalKey.S))),
            (Space, Map.AddAction(InputControl.Key(PhysicalKey.Space))),
            (Escape, Map.AddAction(InputControl.Key(PhysicalKey.Escape, allowUiConsumed: true))),
            (Interact, Map.AddAction(InputControl.Key(PhysicalKey.E))),
            (Drop, Map.AddAction(InputControl.Key(PhysicalKey.F))),
            (Transition, Map.AddAction(InputControl.Key(PhysicalKey.T))),
            (Save, Map.AddAction(InputControl.Key(PhysicalKey.F5))),
            (Load, Map.AddAction(InputControl.Key(PhysicalKey.F9)))];
    }
    public (uint Down, uint Pressed) Update(InputFrame frame) => ToRules(Map.Update(frame));
    public (uint Down, uint Pressed) ToRules(ActionState state)
    {
        uint down = 0, pressed = 0;
        foreach (var item in actions)
        {
            if (state.IsDown(item.Action)) down |= item.Rule;
            if (state.IsPressed(item.Action)) pressed |= item.Rule;
        }
        return (down, pressed);
    }
    public ActionState TestState(uint down, uint pressed)
    {
        return Map.CreateState(actions.Where(x => (x.Rule & down) != 0).Select(x => x.Action).ToArray(),
            actions.Where(x => (x.Rule & pressed) != 0).Select(x => x.Action).ToArray());
    }
}
