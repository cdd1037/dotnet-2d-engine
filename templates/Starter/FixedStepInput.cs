using GameAuthoringLab;

namespace Dotnet2DStarter;

// Copyable application policy, not an engine-owned loop. Poll once; drain steps;
// draw once. One-shot action edges survive zero-step frames and occur only once.
internal sealed class FixedStepInput
{
    private double accumulator;
    private ActionState pending;
    public double StepSeconds { get; }
    public double MaximumFrameSeconds { get; }
    public double DroppedSeconds { get; private set; }

    public FixedStepInput(double stepSeconds = 1d / 60, double maximumFrameSeconds = .1)
    {
        if (!double.IsFinite(stepSeconds) || stepSeconds < .000001 || stepSeconds > 1)
            throw new ArgumentOutOfRangeException(nameof(stepSeconds));
        if (!double.IsFinite(maximumFrameSeconds) || maximumFrameSeconds < stepSeconds || maximumFrameSeconds > 1)
            throw new ArgumentOutOfRangeException(nameof(maximumFrameSeconds));
        StepSeconds = stepSeconds;
        MaximumFrameSeconds = maximumFrameSeconds;
    }

    // Read only after draining TryTakeStep. Previous -> current presentation is
    // deliberately one fixed step behind simulation; never feed it into physics.
    public double InterpolationAlpha
    {
        get
        {
            if (accumulator + StepSeconds * 1e-9 >= StepSeconds)
                throw new InvalidOperationException("Drain fixed steps before sampling presentation.");
            return accumulator / StepSeconds;
        }
    }

    // suspended is an application choice: pause, focus loss, or no drawable area.
    // Reset also discards queued presses, so they cannot fire after resuming.
    // Returned real time is the capped frame delta, not a wall-clock timestamp.
    public TimingStep BeginFrame(double realSeconds, ActionState actions, bool suspended)
    {
        if (!double.IsFinite(realSeconds) || realSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(realSeconds));
        double accepted = Math.Min(realSeconds, MaximumFrameSeconds);
        DroppedSeconds += realSeconds - accepted;
        if (suspended) Reset();
        else
        {
            accumulator += accepted;
            pending = pending.Accumulate(actions);
        }
        return TimingStep.FromReal(accepted, suspended);
    }

    public bool TryTakeStep(out ActionState actions)
    {
        actions = default;
        // Relative tolerance absorbs repeated floating-point subtraction at the boundary.
        if (accumulator + StepSeconds * 1e-9 < StepSeconds) return false;
        accumulator = Math.Max(0, accumulator - StepSeconds);
        actions = pending;
        pending = pending.WithoutEdges();
        return true;
    }

    // Call on restart too. Game state and timers are reset separately by the game.
    public void Reset()
    {
        accumulator = 0;
        pending = default;
    }
}
