namespace GameAuthoringLab;

// Only explicitly registered cleanup is owned. IDisposable on a behavior is never
// inferred. A scope is single-use and cannot accept registrations after setup.
internal sealed class BehaviorLifetime
{
    private readonly List<Action> _cleanup=[];
    private bool _sealed,_released;
    public void OnDetach(Action cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        if(_sealed)throw new InvalidOperationException("Behavior lifetime registration has ended.");
        _cleanup.Add(cleanup);
    }
    internal void Seal()=>_sealed=true;
    internal void Release(ref List<Exception>? failures)
    {
        if(_released)return;
        _sealed=_released=true;
        // Reverse registration order, like nested using statements. Always try all.
        for(int i=_cleanup.Count-1;i>=0;i--)
            try{_cleanup[i]();}catch(Exception error){(failures??=[]).Add(error);}
        _cleanup.Clear();
    }
}
