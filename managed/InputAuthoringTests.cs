using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace GameAuthoringLab;

internal static class InputAuthoringTests
{
    public static int Run()
    {
        int count = 0;
        void Check(bool condition, string label) { count++; if (!condition) throw new InvalidOperationException("TYPED INPUT: " + label); }
        void Reject(Action action, string label)
        {
            try { action(); }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException) { count++; return; }
            throw new InvalidOperationException("TYPED INPUT accepted: " + label);
        }

        var source = Snapshot();
        source.Quit = 7; source.Flags |= InputFlags.FocusChanged;
        source.MouseX = 17; source.MouseY = 29;
        source.WheelX = 3; source.WheelY = -4; source.GameWheelX = 1; source.GameWheelY = -2;
        source.Consumed = InputConsumption.Keyboard | InputConsumption.Pointer | InputConsumption.Wheel;
        SetKey(ref source, PhysicalKey.E, true, true, false, consumed: true);
        SetKey(ref source, (PhysicalKey)511, false, true, true);
        SetKey(ref source, (PhysicalKey)1, true, false, false);
        SetButton(ref source, PointerButton.Left, true, true, false, consumed: true);
        SetButton(ref source, (PointerButton)32, false, true, true);
        var frame = new InputFrame(source);
        var savedGame = frame.Game; var savedRaw = frame.Raw;
        source = default;
        Check(frame.Quit && frame.Focused && frame.FocusChanged && frame.Drawable, "frame copies flags into safe boolean properties");
        Check(frame.Viewport == new Viewport(960, 540, 1920, 1080) && frame.Pointer == new Vector2(17, 29), "copied dimensions and pointer preserve coordinate contract");
        Check(frame.Consumed == (InputConsumption.Keyboard | InputConsumption.Pointer | InputConsumption.Wheel), "consumption categories remain observable");
        Check(savedRaw.KeyDown(PhysicalKey.E) && savedRaw.KeyPressed(PhysicalKey.E) && !savedGame.KeyDown(PhysicalKey.E) && !savedGame.KeyPressed(PhysicalKey.E), "raw key access is explicit and filtered input stays blocked");
        Check(savedGame.KeyDown((PhysicalKey)1) && savedGame.KeyPressed((PhysicalKey)511) && savedGame.KeyReleased((PhysicalKey)511) && !savedGame.KeyDown((PhysicalKey)511), "safe views retain all key words and short taps after source replacement");
        Check(savedRaw.ButtonDown(PointerButton.Left) && savedRaw.ButtonPressed(PointerButton.Left) && !savedGame.ButtonDown(PointerButton.Left), "copied pointer routing remains explicit");
        Check(savedGame.ButtonPressed((PointerButton)32) && savedGame.ButtonReleased((PointerButton)32) && !savedGame.ButtonDown((PointerButton)32), "safe views retain highest pointer button and both edges");
        Check(savedRaw.Wheel == new Vector2(3, -4) && savedGame.Wheel == new Vector2(1, -2), "raw and filtered wheel totals remain separate");
        Check(!default(InputFrame).Quit && !default(InputFrame).Drawable && !default(InputFrame).Game.KeyDown(PhysicalKey.E), "default frame is empty and nondrawable");
        Check(!RuntimeHelpers.IsReferenceOrContainsReferences<InputFrame>() && !RuntimeHelpers.IsReferenceOrContainsReferences<InputView>(), "safe primitive views are self-contained value copies");
        Check(typeof(InputFrame).GetFields(BindingFlags.Public | BindingFlags.Instance).Length == 0 && typeof(InputView).GetFields(BindingFlags.Public | BindingFlags.Instance).Length == 0, "authoring views expose no ABI fields or fixed buffers");
        Reject(() => savedGame.KeyDown((PhysicalKey)0), "key zero");
        Reject(() => savedRaw.KeyPressed((PhysicalKey)512), "key beyond snapshot");
        Reject(() => savedGame.ButtonReleased((PointerButton)33), "button beyond snapshot");
        Reject(() => InputControl.Key((PhysicalKey)0), "invalid key control");
        Reject(() => InputControl.Button((PointerButton)0), "invalid pointer control");
        InputFrame polled;
        using (var engine = new EngineHost(true, 4))
        {
            polled = engine.PollInputFrame();
            Check(polled.Focused && polled.Drawable && polled.Viewport.IsValid && !polled.Game.KeyPressed(PhysicalKey.E), "headless poll exposes safe copied frame");
            engine.PollInputFrame();
        }
        Check(polled.Viewport.WindowWidth == 960 && !polled.Raw.KeyDown(PhysicalKey.E), "safe frame survives later polls and engine disposal");

        var map = new InputActionMap();
        var controls = new[] { InputControl.Key(PhysicalKey.A), InputControl.Key(PhysicalKey.Left), InputControl.Button(PointerButton.Left) };
        var move = map.AddAction(controls);
        var accept = map.AddAction(InputControl.Key(PhysicalKey.E));
        var menu = map.AddAction(InputControl.Key(PhysicalKey.Escape, allowUiConsumed: true));
        controls[0] = InputControl.Key(PhysicalKey.F);
        var both = Snapshot();
        SetKey(ref both, PhysicalKey.A, true, true, false);
        SetKey(ref both, PhysicalKey.Left, true, true, false);
        var state = map.Update(new InputFrame(both));
        Check(state.IsDown(move) && state.IsPressed(move) && !state.IsReleased(move) && !state.IsDown(accept), "typed alternatives allocate independent action identities and copy controls");
        SetKey(ref both, PhysicalKey.A, true, false, false);
        SetKey(ref both, PhysicalKey.Left, false, true, true);
        state = map.Update(new InputFrame(both));
        Check(state.IsDown(move) && !state.IsPressed(move) && !state.IsReleased(move), "alternate tap cannot retrigger a continuously held typed action");
        SetButton(ref both, PointerButton.Left, true, true, false);
        map.Update(new InputFrame(both));
        SetKey(ref both, PhysicalKey.A, false, false, true);
        SetKey(ref both, PhysicalKey.Left, false, false, false);
        SetButton(ref both, PointerButton.Left, true, false, false);
        state = map.Update(new InputFrame(both));
        Check(state.IsDown(move) && !state.IsReleased(move), "held button alternative survives a key release");
        SetButton(ref both, PointerButton.Left, false, false, true);
        state = map.Update(new InputFrame(both));
        Check(!state.IsDown(move) && state.IsReleased(move), "last alternative release ends typed action");

        var routed = Snapshot();
        SetKey(ref routed, PhysicalKey.E, true, true, false, consumed: true);
        SetKey(ref routed, PhysicalKey.Escape, false, true, true, consumed: true);
        state = map.Update(new InputFrame(routed));
        Check(!state.IsDown(accept) && !state.IsPressed(accept) && state.IsPressed(menu) && state.IsReleased(menu), "mixed filtered and explicit raw actions use the same snapshot");
        var mixedMap = new InputActionMap();
        var mixed = mixedMap.AddAction(InputControl.Key(PhysicalKey.E), InputControl.Button(PointerButton.Right, allowUiConsumed: true));
        SetButton(ref routed, PointerButton.Right, false, true, true, consumed: true);
        Check(mixedMap.Update(new InputFrame(routed)).IsPressed(mixed), "raw opt-in applies to the individual alternative only");

        var foreignMap = new InputActionMap();
        var foreign = foreignMap.AddAction(InputControl.Key(PhysicalKey.E));
        Check(!default(ActionState).IsDown(move) && !default(ActionState).IsPressed(move) && !default(ActionState).IsReleased(move), "default pending state is safe with a valid token");
        Reject(() => default(ActionState).IsDown(default), "default token on default state");
        Reject(() => state.IsDown(default), "default token on owned state");
        Reject(() => state.IsDown(foreign), "foreign down token");
        Reject(() => state.IsPressed(foreign), "foreign pressed token");
        Reject(() => state.IsReleased(foreign), "foreign released token");
        Reject(() => map.CreateState().IsDown(foreign), "foreign token on owned empty state");
        Reject(() => new ActionState(1, 0, 0).IsDown(move), "nonempty mask state cannot infer token ownership");
        Reject(() => map.Rebind(foreign, InputControl.Key(PhysicalKey.F)), "foreign rebind token");
        Reject(() => map.Rebind(default(InputAction), InputControl.Key(PhysicalKey.F)), "default rebind token");
        Reject(() => map.CreateState(down: [foreign]), "foreign synthetic down token");
        Reject(() => map.CreateState(pressed: [default]), "default synthetic pressed token");
        Reject(() => map.CreateState(released: [foreign]), "foreign synthetic released token");
        var synthetic = map.CreateState(down: [move, accept, move], pressed: [accept], released: [menu]);
        Check(synthetic.IsDown(move) && synthetic.IsDown(accept) && synthetic.IsPressed(accept) && synthetic.IsReleased(menu), "synthetic state accepts typed sets and coalesces duplicates");
        Check(synthetic == new ActionState(synthetic.Down, synthetic.Pressed, synthetic.Released) && synthetic.GetHashCode() == new ActionState(synthetic.Down, synthetic.Pressed, synthetic.Released).GetHashCode(), "legacy state value equality and hash stay mask-based");

        var prior = map.CreateState(down: [move], pressed: [move]);
        map.Rebind(move, InputControl.Key(PhysicalKey.F));
        Check(prior.IsDown(move) && prior.IsPressed(move), "prior copied states keep their identity after action rebind");
        var f = Snapshot(); SetKey(ref f, PhysicalKey.F, true, true, false, consumed: true);
        Check(!map.Update(new InputFrame(f)).IsDown(move), "raw consumed hold keeps the rebind neutral gate closed");
        SetKey(ref f, PhysicalKey.F, false, true, true, consumed: true);
        Check(!map.Update(new InputFrame(f)).IsPressed(move), "raw consumed transient tap also keeps neutral gate closed");
        map.Update(new InputFrame(Snapshot()));
        f = Snapshot(); SetKey(ref f, PhysicalKey.F, true, true, false); SetKey(ref f, PhysicalKey.E, true, true, false);
        state = map.Update(new InputFrame(f));
        Check(state.IsDown(move) && state.IsPressed(move) && state.IsPressed(accept), "rebind keeps other actions and activates after neutral");
        var unfocused = Snapshot(); unfocused.Flags &= ~InputFlags.Focused;
        state = map.Update(new InputFrame(unfocused));
        Check(state.IsReleased(move) && state.IsReleased(accept) && !state.IsDown(move), "focus loss releases typed actions");
        Reject(() => state.IsReleased(foreign), "focus result retains owner");
        state = map.Update(new InputFrame(f));
        Check(!state.IsDown(move) && !state.IsPressed(accept), "focus recovery waits for raw neutral");
        Reject(() => state.IsDown(foreign), "neutral-gated result retains owner");
        map.Update(new InputFrame(Snapshot()));
        state = map.Update(new InputFrame(f));
        Check(state.IsPressed(move), "focus recovery accepts a fresh action after neutral");

        var failedControls = new[] { InputControl.Key(PhysicalKey.A), default(InputControl) };
        Reject(() => map.Rebind(move, failedControls), "invalid rebind control");
        SetKey(ref f, PhysicalKey.F, true, false, false); SetKey(ref f, PhysicalKey.E, true, false, false);
        state = map.Update(new InputFrame(f));
        Check(state.IsDown(move) && !state.IsPressed(move) && state.IsDown(accept), "failed validation preserves bindings, held history and gate atomically");
        Reject(() => map.Rebind(move, new InputControl[129]), "oversized rebind");
        Check(map.Update(new InputFrame(f)).IsDown(move), "oversized rebind leaves held history usable");
        map.Rebind(move);
        map.Update(new InputFrame(Snapshot()));
        Check(!map.Update(new InputFrame(f)).IsDown(move), "empty action rebind disables its controls");
        Check(prior.IsDown(move), "unbinding does not invalidate prior state token");

        var combined = new InputActionMap(InputBinding.Key(1, PhysicalKey.A), InputBinding.Key(8, PhysicalKey.D));
        var allocated = combined.AddAction(InputControl.Key(PhysicalKey.E));
        Check(combined.CreateState(down: [allocated]).Down == 2, "automatic bit allocation skips legacy bindings");
        var beforeLegacyRebind = combined.CreateState(down: [allocated], pressed: [allocated]);
        combined.Rebind(InputBinding.Key(16, PhysicalKey.F));
        Check(beforeLegacyRebind.IsDown(allocated) && beforeLegacyRebind.IsPressed(allocated), "legacy whole-map rebind preserves prior state identity");
        Check(!combined.Update(new InputFrame(Snapshot())).IsDown(allocated), "legacy rebind keeps old token valid with its controls absent");
        var nextAllocated = combined.AddAction();
        Check(nextAllocated != allocated && combined.CreateState(down: [nextAllocated]).Down == 1, "whole-map legacy rebind does not recycle typed token bits");
        var full = new InputActionMap(); var tokens = new InputAction[32];
        for (int i = 0; i < tokens.Length; i++) tokens[i] = full.AddAction();
        Check(full.CreateState(down: tokens).Down == uint.MaxValue, "all 32 action bits can be allocated safely");
        Reject(() => full.AddAction(InputControl.Key(PhysicalKey.E)), "33rd action");
        full.Rebind(tokens[31], InputControl.Key(PhysicalKey.E)); full.Update(new InputFrame(Snapshot()));
        var eTap = Snapshot(); SetKey(ref eTap, PhysicalKey.E, false, true, true);
        Check(full.Update(new InputFrame(eTap)).IsPressed(tokens[31]), "highest-bit token can bind and query normally");

        var bounded = new InputActionMap();
        var manyControls = Enumerable.Repeat(InputControl.Key(PhysicalKey.E), 127).ToArray();
        var many = bounded.AddAction(manyControls); var last = bounded.AddAction(InputControl.Key(PhysicalKey.F));
        var allHeld = Snapshot(); SetKey(ref allHeld, PhysicalKey.E, true, true, false); SetKey(ref allHeld, PhysicalKey.F, true, true, false);
        bounded.Update(new InputFrame(allHeld));
        Reject(() => bounded.AddAction(InputControl.Key(PhysicalKey.A)), "129th binding through add");
        Reject(() => bounded.Rebind(last, InputControl.Key(PhysicalKey.A), InputControl.Key(PhysicalKey.D)), "129th binding through action rebind");
        SetKey(ref allHeld, PhysicalKey.E, true, false, false); SetKey(ref allHeld, PhysicalKey.F, true, false, false);
        var retained = bounded.Update(new InputFrame(allHeld));
        Check(retained.IsDown(many) && retained.IsDown(last) && !retained.IsPressed(last), "failed capacity changes preserve live state atomically");
        bounded.Rebind(many, InputControl.Key(PhysicalKey.E));
        var afterFailure = bounded.AddAction();
        Check(bounded.CreateState(down: [afterFailure]).Down == 4, "failed add does not consume an action bit");
        Reject(() => bounded.AddAction(default(InputControl)), "default input control");
        Check(bounded.CreateState(down: [bounded.AddAction()]).Down == 8, "failed validation does not consume an action bit");
        Reject(() => bounded.AddAction(null!), "null control array");
        Reject(() => bounded.Rebind(last, null!), "null rebind control array");

        ActionState pending = default;
        pending = pending.Accumulate(map.CreateState(pressed: [accept], released: [accept]));
        pending = pending.Accumulate(map.CreateState());
        Check(pending.IsPressed(accept) && pending.IsReleased(accept) && !pending.IsDown(accept), "zero-step outer frames retain quick tap edges");
        var firstStep = pending; pending = pending.WithoutEdges();
        Check(firstStep.IsPressed(accept) && !pending.IsPressed(accept) && !pending.IsReleased(accept), "first fixed step consumes pending edges exactly once");
        pending = pending.Accumulate(map.CreateState(down: [accept], pressed: [accept]));
        firstStep = pending; pending = pending.WithoutEdges();
        Check(firstStep.IsPressed(accept) && pending.IsDown(accept) && !pending.IsPressed(accept), "catch-up fixed steps keep held input without replaying edges");
        pending = pending.Accumulate(map.CreateState(released: [accept]));
        Check(!pending.IsDown(accept) && pending.IsReleased(accept), "accumulation uses the latest held state");
        Reject(() => pending.WithoutEdges().IsDown(foreign), "edge consumption preserves owner");
        Reject(() => pending.Accumulate(foreignMap.CreateState()), "accumulation rejects foreign empty state");
        Reject(() => map.CreateState().Accumulate(foreignMap.CreateState(down: [foreign])), "empty owned state cannot adopt foreign map");
        Reject(() => pending.Accumulate(new ActionState(1, 0, 0)), "accumulation rejects unowned nonempty next state");
        Reject(() => new ActionState(1, 0, 0).Accumulate(pending), "accumulation rejects unowned nonempty previous state");
        Check(new ActionState(1, 1, 0).Accumulate(new ActionState(0, 0, 1)) == new ActionState(0, 1, 1), "helpers remain useful for existing mask-only states");
        Check(pending.Accumulate(default).IsReleased(accept), "an unowned default does not erase map identity or queued edges");
        Check(!default(ActionState).WithoutEdges().IsDown(accept), "clearing a default pending state remains safe");

        var allocationMap = new InputActionMap();
        var allocationAction = allocationMap.AddAction(InputControl.Key(PhysicalKey.E));
        ActionState allocationPending = default; int observed = 0;
        void Exercise()
        {
            var input = new InputFrame(eTap);
            allocationPending = allocationPending.Accumulate(allocationMap.Update(input));
            if (input.Game.KeyPressed(PhysicalKey.E) && input.Raw.KeyReleased(PhysicalKey.E) && allocationPending.IsPressed(allocationAction)) observed++;
            allocationPending = allocationPending.WithoutEdges();
        }
        for (int i = 0; i < 256; i++) Exercise();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) Exercise();
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocatedBytes == 0 && observed == 1256, "warmed copied views, typed mapping, queries and pending helpers allocate zero bytes");

        Console.WriteLine($"TYPED INPUT SELF-TEST PASS assertions={count}; copied views, tokens, routing, atomic rebinds and queued-once edges");
        return count;
    }

    private static InputSnapshot Snapshot() => new()
    {
        Flags = InputFlags.Focused | InputFlags.Drawable,
        WindowWidth = 960, WindowHeight = 540, PixelWidth = 1920, PixelHeight = 1080
    };

    private static unsafe void SetKey(ref InputSnapshot snapshot, PhysicalKey key, bool down, bool pressed, bool released, bool consumed = false)
    {
        int word = (int)key / 64; ulong bit = 1ul << ((int)key % 64);
        fixed (ulong* d = snapshot.KeysDown, p = snapshot.KeysPressed, r = snapshot.KeysReleased,
            gd = snapshot.GameKeysDown, gp = snapshot.GameKeysPressed, gr = snapshot.GameKeysReleased)
        {
            d[word] = down ? d[word] | bit : d[word] & ~bit; p[word] = pressed ? p[word] | bit : p[word] & ~bit; r[word] = released ? r[word] | bit : r[word] & ~bit;
            gd[word] = down && !consumed ? gd[word] | bit : gd[word] & ~bit; gp[word] = pressed && !consumed ? gp[word] | bit : gp[word] & ~bit; gr[word] = released && !consumed ? gr[word] | bit : gr[word] & ~bit;
        }
    }

    private static void SetButton(ref InputSnapshot snapshot, PointerButton button, bool down, bool pressed, bool released, bool consumed = false)
    {
        uint bit = 1u << ((int)button - 1);
        snapshot.ButtonsDown = down ? snapshot.ButtonsDown | bit : snapshot.ButtonsDown & ~bit;
        snapshot.ButtonsPressed = pressed ? snapshot.ButtonsPressed | bit : snapshot.ButtonsPressed & ~bit;
        snapshot.ButtonsReleased = released ? snapshot.ButtonsReleased | bit : snapshot.ButtonsReleased & ~bit;
        snapshot.GameButtonsDown = down && !consumed ? snapshot.GameButtonsDown | bit : snapshot.GameButtonsDown & ~bit;
        snapshot.GameButtonsPressed = pressed && !consumed ? snapshot.GameButtonsPressed | bit : snapshot.GameButtonsPressed & ~bit;
        snapshot.GameButtonsReleased = released && !consumed ? snapshot.GameButtonsReleased | bit : snapshot.GameButtonsReleased & ~bit;
    }
}
