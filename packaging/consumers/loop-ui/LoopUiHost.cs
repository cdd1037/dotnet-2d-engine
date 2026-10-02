using System.Numerics;
using Dotnet2DStarter;
using GameAuthoringLab;

// Application-owned frame wiring shared by the live loop and scripted checks.
// No callback scheduler, world binding, or engine-level lifecycle API is introduced.
internal sealed class LoopUiHost(EngineHost engine, StarterGame game,
    UiModelSession<StarterGame> ui, TextureHandle texture, StarterInput controls)
{
    private bool restartRequested;
    public void Toggle() => game.Paused = !game.Paused;
    public void Restart() => restartRequested = true;
    private readonly FixedStepInput fixedInput = new(StarterGame.StepSeconds);
    private readonly InputActionMap actions = controls.Map;
    private readonly SpriteCommand[] draws = new SpriteCommand[1];
    public int Commands { get; private set; }
    public int Restarts { get; private set; }
    public int Draws { get; private set; }
    public Vector2 DrawPosition { get; private set; }
    public double Alpha => fixedInput.InterpolationAlpha;
    public UiCommandEvent LastCommand { get; private set; }

    public bool Frame(double elapsed)
    {
        var input = engine.PollInputFrame();
        if (input.Quit) return false;
        var action = actions.Update(input);
        if (action.IsPressed(controls.Pause)) game.Paused = !game.Paused;
        bool restart = action.IsPressed(controls.Restart);
        // Drain the whole current revision before Apply. UI keeps working while paused.
        for (var command = ui.Poll(); !command.IsEmpty; command = ui.Poll())
        {
            if (!ui.Dispatch(command)) continue;
            LastCommand = command;
            Commands++;
        }
        restart |= restartRequested; restartRequested = false;
        if (restart) { game.Restart(); fixedInput.Reset(); Restarts++; }
        bool suspended = game.Paused || restart || !input.Focused || !input.Drawable;
        fixedInput.BeginFrame(elapsed, action, suspended);
        if (suspended) game.SnapPresentation();
        while (fixedInput.TryTakeStep(out var stepInput)) game.Tick(stepInput);
        ui.Apply(game); // One projection after command and simulation drains, even paused.
        if (input.Drawable)
        {
            DrawPosition = game.PresentationPosition(fixedInput.InterpolationAlpha);
            draws[0] = new(new(DrawPosition.X - 16, DrawPosition.Y - 16), new(32, 32),
                new(game.Paused ? .4f : 1, game.Pulses % 2 == 0 ? .8f : .2f, .3f, 1), texture);
            engine.Draw(new Camera { Zoom = 1 }, draws); // Outside the simulation gate.
            Draws++;
        }
        return true;
    }
}
