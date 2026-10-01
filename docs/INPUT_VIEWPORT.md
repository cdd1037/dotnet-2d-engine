# Input and viewport contract

This batch adds `gal_poll_v2` / `EngineHost.PollInput()` while preserving the
32-byte `gal_input`, `gal_poll`, ABI-1 context creation and existing consumers.
The new 472-byte snapshot starts with `size` and `version = 2`; a nonzero reserved
field, wrong size/version, active frame or wrong thread is rejected. Poll **one**
version once per tick: both consume the same event stream, so calling both is not
an independent observation. Backend errors do not partially overwrite a v2 caller's
last usable snapshot.

## Coordinates and drawable state

The v2 snapshot separates logical `WindowWidth/WindowHeight` from framebuffer
`PixelWidth/PixelHeight`. Mouse coordinates use SDL logical window units. Camera
projection uses framebuffer pixels: `(world - camera.Position) * camera.Zoom`.
The managed immutable `Viewport` provides `TryWindowToPixels`,
`TryPixelsToWindow`, `TryWindowToWorld` and `TryWorldToWindow`. These handle
independent x/y density ratios and use double intermediates. They return false
and a zero output for nonfinite/overflowing coordinates, invalid camera zoom or
zero/negative/out-of-range/nondrawable dimensions. They transform positions; they
do not clip points, provide picking, scale the game world or apply letterboxing.

`Drawable` is false while minimized or when either coordinate space has invalid
sizes. Raw dimensions remain visible; v2 does not pretend a zero viewport is
1 pixel. The renderer retains the last valid projection and skips nondrawable
frames. Initial projection uses actual framebuffer size. If the swapchain size
changes after polling, rendering that stale projection is skipped; the next poll
updates the dimensions. Resize does not silently stretch incorrectly projected
vertices. The frame submission counter can advance for a skipped frame, while
sprite draw-call count does not.

The legacy v1 adapter keeps its old compatibility behavior: window-unit mouse
coordinates alongside pixel dimensions, positive-clamped sizes and the small
fixed sample action mask. New code should use v2 for coordinate conversions.
No high-DPI/device support claim follows from the synthetic 1x, 2x and nonuniform
math tests. The existing SDL window flags are unchanged. The fixed-size sample
world/UI still needs a separate small-window layout/presentation policy.

## Physical inputs and UI ownership

Raw down/pressed/released arrays contain 512 SDL3 physical-scancode bits, indexed
by scancode rather than translated character. Named `PhysicalKey` values cover the
sample controls; bindings may use any valid index 1..511. Pointer button 1..32 maps
to bit 0..31. Key/button events are accumulated for this window; unrelated window
events are ignored. A press followed by release between polls sets both edge bits,
with down false. Auto-repeat does not create a new press. These are bounded boolean
summaries, not an ordered event queue or a count of multiple taps in one poll.

`game_*` / the default managed queries provide routed state. The native RmlUi
adapter now preserves its handled/propagating result. A consumed press cannot become
a gameplay hold when the UI later loses focus or closes: it stays blocked until
release. A release still ends a prior gameplay-owned hold. An unrelated unconsumed
key is not suppressed merely because another key was consumed. Text-field focus
captures keyboard state and suppresses gameplay wheel input as before. Raw inputs
remain observable for deliberate escape/menu shortcuts.

`Consumed` exposes keyboard/pointer/wheel/text categories handled during a poll;
keyboard also indicates active text-field capture. Wheel totals expose both raw
and unconsumed values. Flipped SDL wheel direction is normalized for the snapshot.
The RmlUi adapter still owns its own scrolling behavior. UI document hit-testing
controls consumption; this does not invent a separate global modal stack or promise
that transparent document backgrounds pass pointers through.

Focus loss clears physical/routed held state, reports releases, discards pending
presses/wheel and stays unfocused until a gain event. Held-key repeats after gain
cannot resurrect gameplay input; release and a new press are required. This state
is event-derived, not a background global keyboard monitor. Text composition,
character entry and existing IME handling stay with the UI; they are not key actions.

## Managed action mapping

`InputActionMap` copies a bounded array of `InputBinding` records. Action IDs are
caller-chosen single bits, with any number of key/button alternatives within 128
bindings. `Update(snapshot)` returns `ActionState(Down, Pressed, Released)`; systems
read the result directly without registering callbacks on every entity.

Alternative controls combine into one held action. A continuously held alternative
prevents another binding's tap/release from spuriously ending or retriggering it.
A release/repress completed between polls can expose both action edges. Since the
snapshot is unordered, simultaneous handoffs between different alternative controls
without a continuously held binding can also expose both edges; precise gestures
or replay would need a separate ordered event stream. Rebinding is validated before
replacement and waits for all bound raw controls to become neutral. Focus loss uses
the same neutral gate. A warmed mapping update allocates no managed memory.

`AllowUiConsumed` is an explicit per-binding opt-in to raw input; the sample uses
it only for Escape. `CreateSample()` defines WASD/arrows, E/F/T, Space/Escape and
F5/F9 in managed code. Those are sample controls, not native v2 game rules. Runtime
programmatic rebinding exists; a settings editor or serialized binding format does
not. There is no gamepad, touch, mouse gesture or mobile expansion in this batch.

The mission's interactive host now uses this v2 map. Room/mission updates accept
explicit pressed bits so short interactions survive until the next fixed tick;
legacy held-mask overloads remain. Pause, focus, screen-generation and neutral
release gates still apply. Nondrawable/focus-lost mission windows pause and discard
pending UI commands rather than advancing the timer invisibly.

## Verification

```sh
scripts/test.sh quick input
scripts/test.sh jit
source scripts/ui-env.sh
/path/to/dotnet managed/bin/Release/net10.0/GameAuthoringLab.dll --input-graphics-test
```

The default native tier includes a standalone event-accumulator test; C11 and
managed checks verify the v2 layout. Tests cover quick taps/represses, repeats,
per-control consumption, focus/neutral gates, remapping, raw escape opt-in,
allocation-free mapping, high-DPI coordinate math and fixed-step edge retention.
Native mock failures verify v2 output retention and last-valid projection after a
zero viewport. The optional graphics probe queues actual SDL events through the
backend and RmlUi for repeated consumed clicks, text-focus key ownership, wheel,
focus, resize, synthetic minimize/restore, stale-projection skipping and UI close.
These deterministic events are synthetic; a displayed desktop check is recorded
separately in [validation](validation.md). NativeAOT is republished at this ABI
boundary; earlier AOT binaries are not evidence for this version.

Next rendering work remains texture-region/atlas metadata and UV submission,
world clipping/scissor and sprite-frame animation. Existing RmlUi scissor is UI
clipping only. Those capabilities, general input event queues, device-specific DPI
acceptance and responsive sample layout are not included here.
