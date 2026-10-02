using System.Numerics;
using GameAuthoringLab;

internal static class InputChecks
{
    internal static void Run(Checks check, EngineHost engine)
    {
        var actions = new InputActionMap();
        InputAction jump = actions.AddAction(InputControl.Key(PhysicalKey.Space), InputControl.Button(PointerButton.Left));
        InputAction menu = actions.AddAction(InputControl.Key(PhysicalKey.Escape, allowUiConsumed: true));
        InputFrame frame = engine.PollInputFrame();
        InputView game = frame.Game, raw = frame.Raw;
        check.That(!frame.Quit && frame.Consumed == 0 && frame.Pointer == Vector2.Zero,
            "safe headless poll exposes neutral metadata without ABI headers");
        check.That(!game.KeyDown(PhysicalKey.Space) && !game.KeyPressed(PhysicalKey.Space) && !game.KeyReleased(PhysicalKey.Space)
            && !raw.KeyDown(PhysicalKey.Space) && !raw.KeyPressed(PhysicalKey.Space) && !raw.KeyReleased(PhysicalKey.Space),
            "copied game and raw keyboard views expose neutral controls");
        check.That(!game.ButtonDown(PointerButton.Left) && !game.ButtonPressed(PointerButton.Left) && !game.ButtonReleased(PointerButton.Left)
            && !raw.ButtonDown(PointerButton.Left) && !raw.ButtonPressed(PointerButton.Left) && !raw.ButtonReleased(PointerButton.Left)
            && game.Wheel == Vector2.Zero && raw.Wheel == Vector2.Zero, "safe pointer and wheel views");
        _ = frame.Focused; _ = frame.FocusChanged; _ = frame.Drawable; _ = frame.Viewport;
        ActionState polled = actions.Update(frame);
        check.That(!polled.IsDown(jump) && !polled.IsPressed(menu) && !polled.IsReleased(jump), "typed action map consumes safe input frame");
        _ = engine.PollInputFrame();
        check.That(game.Wheel == Vector2.Zero && !game.KeyDown(PhysicalKey.Space), "copied view survives subsequent polls");
        check.Reject<ArgumentOutOfRangeException>(() => game.KeyDown((PhysicalKey)0), "invalid primitive key");
        check.Reject<ArgumentOutOfRangeException>(() => raw.ButtonPressed((PointerButton)33), "invalid primitive button");
        check.Reject<ArgumentOutOfRangeException>(() => actions.AddAction(default(InputControl)), "default control cannot create an action");

        InputAction[] jumpOnly = [jump];
        ActionState held = actions.CreateState(down: jumpOnly, pressed: jumpOnly);
        jumpOnly[0] = menu;
        check.That(held.IsDown(jump) && held.IsPressed(jump) && !held.IsDown(menu), "synthetic typed state copies its caller input");
        ActionState edgesConsumed = held.WithoutEdges();
        check.That(edgesConsumed.IsDown(jump) && !edgesConsumed.IsPressed(jump) && !edgesConsumed.IsReleased(jump), "WithoutEdges retains typed held state");
        var pending = new FixedStepInput();
        pending.Poll(held);
        pending.Poll(actions.CreateState(released: [jump]));
        ActionState first = pending.Step(), second = pending.Step(), third = pending.Step();
        check.That(!first.IsDown(jump) && first.IsPressed(jump) && first.IsReleased(jump), "zero-step polls coalesce a tap into first fixed step");
        check.That(!second.IsPressed(jump) && !second.IsReleased(jump) && !third.IsPressed(jump), "catch-up steps do not repeat one-shot edges");
        pending.Poll(actions.CreateState(down: [menu], pressed: [menu]));
        first = pending.Step(); second = pending.Step();
        check.That(first.IsDown(menu) && first.IsPressed(menu) && second.IsDown(menu) && !second.IsPressed(menu), "held action survives catch-up after its edge is consumed");
        ActionState accumulated = held.Accumulate(actions.CreateState(down: [menu], released: [jump]));
        check.That(!accumulated.IsDown(jump) && accumulated.IsDown(menu) && accumulated.IsPressed(jump) && accumulated.IsReleased(jump),
            "accumulation retains pending edges with newest held state");

        var foreignMap = new InputActionMap();
        InputAction foreign = foreignMap.AddAction(InputControl.Key(PhysicalKey.Space));
        ActionState foreignState = foreignMap.CreateState(down: [foreign]);
        check.Reject<ArgumentException>(() => held.IsDown(default), "default action token query");
        check.Reject<ArgumentException>(() => held.IsPressed(foreign), "foreign action token query");
        check.Reject<ArgumentException>(() => actions.CreateState(down: [foreign]), "foreign synthetic token");
        check.Reject<ArgumentException>(() => actions.CreateState(pressed: [default]), "default synthetic token");
        check.Reject<ArgumentException>(() => actions.Rebind(foreign, InputControl.Key(PhysicalKey.W)), "foreign rebind token");
        check.Reject<ArgumentException>(() => actions.Rebind(default(InputAction), InputControl.Key(PhysicalKey.W)), "default rebind token");
        check.Reject<ArgumentException>(() => held.Accumulate(foreignState), "states from different maps cannot accumulate");
        check.Reject<ArgumentException>(() => edgesConsumed.IsReleased(foreign), "WithoutEdges preserves map identity");
        check.Reject<ArgumentException>(() => actions.CreateState().IsDown(foreign), "empty typed state retains map identity");
        actions.Rebind(jump, InputControl.Key(PhysicalKey.W));
        check.That(actions.CreateState(down: [jump]).IsDown(jump) && held.IsPressed(jump), "rebind preserves token and copied state identity");
        check.That(!actions.Update(frame).IsPressed(jump), "rebind neutral gate consumes a safe neutral poll");
        actions.Rebind(jump, []);
        check.That(actions.CreateState(pressed: [jump]).IsPressed(jump), "unbound action remains valid for typed simulation");
        var full = new InputActionMap();
        var allocated = new InputAction[32];
        for (int i = 0; i < allocated.Length; i++) allocated[i] = full.AddAction();
        check.Reject<InvalidOperationException>(() => full.AddAction(), "32-action limit is enforced without caller bit arithmetic");
        ActionState all = full.CreateState(down: allocated);
        check.That(allocated.Distinct().Count() == 32 && allocated.All(all.IsDown), "all 32 allocated tokens remain distinct and usable");
        check.That(!default(ActionState).IsDown(jump), "default pending state accepts a valid typed query as neutral");
    }

    internal static long MeasureWarm(EngineHost engine)
    {
        var actions = new InputActionMap();
        InputAction jump = actions.AddAction(InputControl.Key(PhysicalKey.Space));
        InputAction[] tokens = [jump];
        var pending = new FixedStepInput();
        bool observed = false;
        void Frame()
        {
            InputFrame frame = engine.PollInputFrame();
            InputView game = frame.Game, raw = frame.Raw;
            observed |= game.KeyDown(PhysicalKey.Space) || raw.ButtonPressed(PointerButton.Left)
                || game.Wheel != Vector2.Zero || raw.Wheel != Vector2.Zero;
            pending.Poll(actions.Update(frame));
            pending.Poll(actions.CreateState(down: tokens, pressed: tokens));
            ActionState first = pending.Step(), catchUp = pending.Step();
            observed |= first.IsDown(jump) && first.IsPressed(jump) && catchUp.IsDown(jump) && !catchUp.IsPressed(jump);
        }
        for (int i = 0; i < 256; i++) Frame();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1024; i++) Frame();
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        if (!observed) throw new InvalidOperationException("Warm typed input work was not observed.");
        return bytes;
    }
}
