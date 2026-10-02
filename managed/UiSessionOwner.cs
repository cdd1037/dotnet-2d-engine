namespace GameAuthoringLab;

// One managed owner for the context's optional UI. No abandoned wrapper can close a newer owner.
public abstract unsafe class UiSessionOwner : IDisposable , IEngineOwned
{
    protected EngineHost Engine { get; }
    private bool _nativeOpened;
    public bool IsDisposed { get; private set; }
    private protected UiSessionOwner(EngineHost engine) { ArgumentNullException.ThrowIfNull(engine); Engine=engine; engine.AcquireUi(this); }
    protected nint Context { get { CheckAccess(); return Engine.NativeContext; } }
    protected void CheckAccess() { Engine.AssertThread(); ObjectDisposedException.ThrowIf(IsDisposed,this); Engine.AssertAlive(); }
    internal UiTextState TextState { get { var state=new UiTextState{Size=(uint)sizeof(UiTextState),Version=1};Native.Check(UiNative.TextState(Context,&state),"UI text state");return state; } }
    protected void MarkNativeOpened() => _nativeOpened=true;
    void IEngineOwned.EngineDestroyed()=>EngineDestroyed();
    internal void EngineDestroyed() { IsDisposed=true; OnClosed(); }
    protected virtual void BeforeClose() { }
    protected virtual void OnClosed() { }
    public void Dispose()
    {
        Engine.AssertThread(); if(IsDisposed)return; BeforeClose();
        if(_nativeOpened)Native.Check(UiNative.Close(Context),"UI close");
        IsDisposed=true; Engine.ReleaseUi(this); OnClosed();
    }
}
