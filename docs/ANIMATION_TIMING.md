# Frame animation, tween values and timers

This is an opt-in **managed polling API**. There is no global scheduler, entity
registry, wall-clock read, background thread, reflection, property-name binding or
completion callback. The sample host chooses when to advance and when to apply
results. Native draw ABI, scene/save versions and JSON source-generation roots are
unchanged.

## Small API surface

- `FrameClip(ReadOnlySpan<string>, frameSeconds)` copies 1–4096 logical resource
  keys into immutable, fixed-rate metadata. Keys identify catalog entries, not
  texture handles, atlas coordinates or filenames
- `FramePlayer(clip, loop, domain)` exposes `AssetKey`, `FrameIndex` and cycle-local
  `ElapsedSeconds`. Looping wraps; one-shot playback holds its last frame
- `Tween.Float`, `Tween.Vector` and `Tween.Color` create typed `Tween<float>`,
  `Tween<Vector2>` and `Tween<Vector4>` values. Color means straight RGBA in [0,1],
  interpolated numerically, not linear-light or premultiplied color
- Easing is `Linear`, quadratic `EaseIn`, quadratic `EaseOut`, or `SmoothStep`.
  Numeric/vector endpoints must be finite. Double intermediates avoid overflowing
  a float subtraction between large opposite-sign endpoints. Completion assigns
  the exact requested endpoint. Rotation/angle wrapping is caller policy
- `EngineTimer(seconds, repeating, domain)` exposes `TicksDue` for the most recent
  advance and `RemainingSeconds`. Missed repeat ticks are coalesced into one
  `ulong` count; processing each missed tick is a caller decision, never an
  unbounded engine callback loop
- `TimingStep(realSeconds, gameSeconds)` carries independently supplied deltas;
  `TimingStep.FromReal(seconds, paused, timeScale)` is a convenience for deriving
  game time. `ClockDomain.Game` is the default; `RealTime` selects the other delta
- `TimingScope.Own(operation)` provides bounded lifetime ownership only. Advance
  each operation explicitly; the scope neither advances nor schedules it

All types currently have the repository's internal visibility, like the rest of
the prototype API. The later packaging proof decides exported assembly boundaries.

```csharp
var clip = new FrameClip(["walk-0", "walk-1", "walk-2"], 0.125);
var scope = new TimingScope();
world.AttachBehavior(actor, behavior, lifetime => lifetime.OnDetach(scope.Dispose));
var frames = scope.Own(new FramePlayer(clip));
var movement = scope.Own(Tween.Vector(new(0, 0), new(80, 0), 1, TweenEase.SmoothStep));
var cooldown = scope.Own(new EngineTimer(0.5));

// In the host's explicit update phase, after attachment has completed:
var step = TimingStep.FromReal(deltaSeconds, paused: gamePaused);
frames.Advance(step);
movement.Advance(step);
cooldown.Advance(step);
actor.Sprite = actor.Sprite!.Value with { AssetKey = frames.AssetKey };
actor.LocalTransform = actor.LocalTransform with { X = movement.Value.X, Y = movement.Value.Y };
if (cooldown.TicksDue != 0) { /* ordinary game logic */ }
```

A scope may accept new work after attachment registration has sealed. Register its
cleanup once, before setup that could fail. Scope disposal works during attachment
rollback, behavior replacement, entity destruction and scene unload because it
never reads or writes an entity. A scope does not infer a scene or follow an
unrelated entity automatically.

## Time, pause and edge semantics

- Each selected delta is finite and in [0,86400] seconds. Invalid/oversized input is
  rejected before playback mutation; it is not silently clamped or split
- Time scale is finite and in [0,16]. The scaled game delta must also fit the delta
  bound. The host may instead provide a fixed simulation delta and independent
  real delta explicitly. There is no implicit connection to `World.Update` or the
  Box2D fixed step
- Positive durations are at least one microsecond and at most one day; a clip's
  total duration must also fit one day. These bounds keep arithmetic finite and
  timer counts representable, including 86,400,000,000 due ticks in one advance
- A zero-duration one-shot timer or tween is permitted. Its initial value/state is
  available immediately, but completion occurs on the next **positive selected
  delta**. It cannot fire merely because a game is paused. Zero-duration repeating
  timers and frame durations are rejected
- A zero step makes no time progress. Every `Advance`, including a zero or paused
  advance, clears `TicksDue` and `CompletedThisAdvance` before considering time.
  Read those transient results after every advance, including every fixed step
- Local `Pause` freezes either domain. `Resume` retains phase. A game-clock pause
  does not freeze a real-clock object unless the host explicitly supplies zero
  real time too. “RealTime” means a caller-supplied unscaled delta, not a hidden
  wall-clock timer that continues while the application isn't updating
- `Cancel` preserves the current value and stops advancement; it does not snap to
  the endpoint. `Restart` resets phase/value/results and runs from the beginning,
  including after cancellation or completion. Local pause is cleared by restart
- `Dispose` is idempotent on the creating thread, cancels unfinished work and
  prohibits later mutation/restart/advance. Last values and state remain readable.
  Already-completed operations retain `Completed` state. No finalizers or callbacks
- Mutable operations/scopes belong to the creating thread; immutable clips can be
  shared. Concurrent reads while another thread mutates are not supported
- A scope owns at most 256 operations (a smaller capacity is optional); each object
  may have only one scope owner. Completed/cancelled objects remain owned because
  they can restart. Dispose an item to release its slot; disposed slots are reclaimed
  when adding new work. Scope disposal releases all retained items

Frame selection and repeated-timer catch-up use constant-time arithmetic rather
than iterating per skipped frame/tick. Seconds are IEEE double values: exact input
sequences are repeatable in these tests, but arbitrary delta partitioning is not
bit-identical. For example, `.3 / .1` is slightly below 3; a repeating `.1` timer
advanced by `.3` reports 2 ticks and a tiny time remaining, while three `.1` steps
report 3. The next positive step catches up. No undocumented epsilon is applied.
Use a consistent clock/fixed-step sequence where boundary reproducibility matters;
this is not a cross-platform deterministic timing guarantee.

## Texture residency while animating

Use the optional `TextureBank(engine, catalog, retainedKeys)` constructor with
`clip.AssetKeys` (or a combined set for several clips). It copies up to 4096 keys
at setup and validates registration. `Sync` then validates files/regions and
acquires candidate leases transactionally, including those keys even if they are
not the current sprite frame. All aliases of one atlas still share one native
texture. Keys remain resident until bank disposal, even when an entity disappears.

Without retention, existing `Sync` still follows only current world usage and
may release/reacquire leases during frame swaps. Retention is explicit rather than
keeping every catalog texture resident. It does not bypass the cache's 256 distinct
texture limit or resource errors. Playback itself neither validates nor loads
resources; use the bank before applying a new scene as usual.

Warmed timing advancement, property application and retained-key bank
synchronization/extraction allocate zero managed bytes in the focused test. Setup,
object creation, input failures, disposal, arbitrary caller work and unpinned
resource changes are outside that claim.

## Sample and verification

```sh
scripts/test.sh quick animation
scripts/test.sh jit
# Existing prepared SDL build; no UI framework is required by the sample:
source scripts/sdl-env.sh
LD_LIBRARY_PATH="$PWD/build:$LD_LIBRARY_PATH" \
  dotnet managed/bin/Release/net10.0/GameAuthoringLab.dll --animation-demo
# Prepared cloud software Vulkan environment:
source scripts/ui-env.sh
GAL_ANIMATION_CAPTURE_DIR="$PWD/evidence/animation/visual" \
  dotnet managed/bin/Release/net10.0/GameAuthoringLab.dll --animation-scenario --frames 16
python3 scripts/validate-animation-pixels.py
```

Use an installed `dotnet` or the same explicit SDK path as other test commands.
The sample uses existing `regions.scene.json` resource metadata and `regions.bmp`;
no new images, fonts, codecs, JSON schema or authoring dependency is added.

Top moving sprite: game-clock frames, vector movement and scalar rotation. Upper
right: one-shot game-clock frames. Lower flipped sprite: real-clock frames and
RGBA tween. Lower lamps: game-clock and real-clock repeat timers. Space pauses only
game time; F cancels all; T restarts; Escape exits. Focus loss/minimization freezes
both clocks in this sample and does not later catch up hidden wall time. The scripted
scenario waits for drawable frames, then tests pause/cancel/restart with explicit
quarter-second steps and repeatable captures. It also runs headlessly, where
texture-cache entries are validated placeholders rather than GPU uploads.

This batch intentionally contains no property tracks, timeline events, animation
state machine, blend tree, skeletal animation, async scheduling, animation snapshot
or automatic serialization of arbitrary runtime jobs.
