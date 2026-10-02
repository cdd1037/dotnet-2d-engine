using GameAuthoringLab;

// Caller-owned fixed-step recipe: carry edges across polls with no step, then
// expose each coalesced edge once while held state survives catch-up steps.
internal sealed class FixedStepInput
{
    private ActionState _pending;
    internal void Poll(ActionState state) => _pending = _pending.Accumulate(state);
    internal ActionState Step()
    {
        ActionState current = _pending;
        _pending = _pending.WithoutEdges();
        return current;
    }
}
