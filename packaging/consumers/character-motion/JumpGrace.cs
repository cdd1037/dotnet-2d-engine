namespace CharacterMotionRecipe;

// Game-owned 100 ms coyote time and jump buffer. The host passes an edge, not held input.
internal sealed class JumpGrace(float window)
{
    private float coyote, buffered;
    public bool Step(bool supported, bool pressed, float delta)
    {
        coyote = supported ? window : Math.Max(0, coyote - delta);
        buffered = pressed ? window : Math.Max(0, buffered - delta);
        if (coyote <= 0 || buffered <= 0) return false;
        Reset();
        return true;
    }
    public void Reset() => coyote = buffered = 0;
}
