using System.Numerics;
using Dotnet2DStarter;
using GameAuthoringLab;

// Application-owned frame wiring shared by the live loop and scripted checks.
// No callback scheduler, world binding, or engine-level lifecycle API is introduced.
internal sealed class LoopUiHost(EngineHost engine, StarterGame game,
    UiModelSession<StarterGame> ui, ulong texture)
{
    public const uint Toggle = 1, Restart = 2;
    private readonly FixedStepInput fixedInput = new(StarterGame.StepSeconds);
    private readonly InputActionMap actions = new(
        InputBinding.Key(StarterGame.Left, PhysicalKey.A), InputBinding.Key(StarterGame.Left, PhysicalKey.Left),
        InputBinding.Key(StarterGame.Right, PhysicalKey.D), InputBinding.Key(StarterGame.Right, PhysicalKey.Right),
        InputBinding.Key(StarterGame.Up, PhysicalKey.W), InputBinding.Key(StarterGame.Up, PhysicalKey.Up),
        InputBinding.Key(StarterGame.Down, PhysicalKey.S), InputBinding.Key(StarterGame.Down, PhysicalKey.Down),
        InputBinding.Key(StarterGame.Pulse, PhysicalKey.Space),
        InputBinding.Key(StarterGame.Pause, PhysicalKey.Escape, allowUiConsumed: true),
        InputBinding.Key(StarterGame.RestartAction, PhysicalKey.T));
    private readonly SpriteDraw[] draws = new SpriteDraw[1];
    public int Commands { get; private set; }
    public int Restarts { get; private set; }
    public int Draws { get; private set; }
    public Vector2 DrawPosition { get; private set; }
    public double Alpha => fixedInput.InterpolationAlpha;
    public UiCommandEvent LastCommand { get; private set; }

    public bool Frame(double elapsed)
    {
        var input = engine.PollInput();
        if (input.Quit != 0) return false;
        var action = actions.Update(input);
        if ((action.Pressed & StarterGame.Pause) != 0) game.Paused = !game.Paused;
        bool restart = (action.Pressed & StarterGame.RestartAction) != 0;
        // Drain the whole current revision before Apply. UI keeps working while paused.
        for (var command = ui.Poll(); !command.IsEmpty; command = ui.Poll())
        {
            if (!ui.IsCurrent(command)) continue;
            if (command.CommandId == Toggle) game.Paused = !game.Paused;
            else if (command.CommandId == Restart) restart = true;
            LastCommand = command;
            Commands++;
        }
        if (restart) { game.Restart(); fixedInput.Reset(); Restarts++; }
        bool suspended = game.Paused || restart || !input.Focused || !input.Drawable;
        fixedInput.BeginFrame(elapsed, action, suspended);
        if (suspended) game.SnapPresentation();
        while (fixedInput.TryTakeStep(out var stepInput)) game.Tick(stepInput);
        ui.Apply(game); // One projection after command and simulation drains, even paused.
        if (input.Drawable)
        {
            DrawPosition = game.PresentationPosition(fixedInput.InterpolationAlpha);
            draws[0] = new SpriteDraw {
                M11 = 1, M22 = 1, X = DrawPosition.X - 16, Y = DrawPosition.Y - 16,
                Width = 32, Height = 32, Texture = texture,
                R = game.Paused ? .4f : 1, G = game.Pulses % 2 == 0 ? .8f : .2f, B = .3f, A = 1
            };
            engine.Draw(new Camera { Zoom = 1 }, draws); // Outside the simulation gate.
            Draws++;
        }
        return true;
    }
}
