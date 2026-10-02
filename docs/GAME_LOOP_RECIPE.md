# Explicit fixed-step game loop

The [starter](../templates/Starter/Program.cs) is the copyable recipe. The
[optional pause-menu consumer](../packaging/consumers/loop-ui/) adds real generic
UI without changing the engine. All calls and owners remain ordinary C#; there is
no base game, scheduler, service locator, entity/body binder or new runtime API.

## One order to copy

1. Poll native input once, then update your `InputActionMap` once
2. Sample elapsed time and update the stopwatch baseline, including paused frames
3. Drain UI commands on the outer frame. Check `IsCurrent` immediately before
   dispatch; handle keyboard/UI pause and restart requests before simulation
4. Reset game state on restart. Choose suspension: paused, restarted, unfocused,
   or nondrawable. `BeginFrame(elapsed, actions, suspended)` clears debt and input
   when suspended; the explicit restart `Reset()` also documents that boundary
5. Collapse presentation history on suspension, then drain `TryTakeStep`
6. In **each** fixed step: apply gameplay input, call `physics.Step()` once,
   inspect/copy its events, copy one authoritative body state, update game-owned
   fixed timers/animation/behaviors in your chosen order
7. After draining, project the current UI model once. Advance real-time menu work
   once with `frame.RealSeconds`; draw once when drawable, including while paused
8. Dispose UI/game/resource owners before the engine, on their creating thread

The starter has no UI/font dependency. Its comments show where optional UI belongs;
the pause-menu consumer is a concrete runnable version. UI polling and drawing
must not sit inside `while (TryTakeStep(...))` or `if (!paused)`. Otherwise a paused
menu, or a high-refresh frame with no physics step, cannot respond normally.

RML nodes and `World` entities remain separate trees. Native polling routes input
through RmlUi first; `InputActionMap` uses the gameplay-filtered snapshot by
default, including pointer-button bindings. A consumed pointer-down remains
blocked from gameplay until release, even when dragged out of the UI. Raw input
and `allowUiConsumed: true` are explicit bypasses, used here only for Escape.
Showing an RML panel alone does not pause gameplay: the host's suspension choice
does. Pointer coordinates are SDL window units; use `input.Viewport` conversion
methods before game-world picking. There is no automatic World input dispatcher,
modal stack or RML-to-entity coordinate/lifetime binding.

`World.Update(0)` **still invokes behaviors**. For suspended gameplay, skip the
call; zero delta is not a side-effect barrier. For an entity-driven game, call
`World.Update((float)fixedInput.StepSeconds)` at one explicit location per step.
Choose whether those behaviors issue commands before physics or consume results
after physics. A body must have only one authoritative pose writer. UI needs no
world update in order to receive native input and render.

## Clocks and action edges

`FixedStepInput` is application-owned source, not part of `Dotnet2D.Engine`.
It caps each frame at 0.1 seconds, accumulates accepted time, and records discarded
hitch time in `DroppedSeconds`. `Reset()` clears the accumulator and pending
input, not that cumulative diagnostic. The caller must drain the steps.

- A tap survives zero-step frames. Pressed/released edges coalesce by action bit
  and reach only the first catch-up step; held state reaches every step
- A press and release between steps may arrive together with `Down == 0`
- Pause, focus loss, no drawable area and restart discard queued gameplay input
- Restart plus a gameplay press discards both old and same-frame input and skips
  simulation for that frame. Pause is preserved by the starter's game reset
- `TimingStep.GameSeconds` returned by `BeginFrame` is accepted frame time, **not**
  completed step count times step duration. Fixed gameplay timers use the fixed
  delta inside the step; never advance them from both clocks
- The real-time delta is capped too. Supply another explicit clock for a deadline
  that needs uncapped time. Neither clock promises replay/network determinism

For simulation-triggered death/scene change during a catch-up batch, decide
explicitly whether to finish that batch. A game that aborts it should signal the
host, reset pending steps, and snap presentation after resetting/replacing state.
Do not silently retry a completed step or continue using the old world.

## Physics and presentation

`StarterGame.Position` is the authoritative copied simulation position in pixels.
After input and one successful physics step, the sample reads `body.State` once,
converts its meter coordinates once, and shifts `(previous, current)` for every
step. Several catch-up steps leave the **last two** positions, not the outer
frame's first and last positions. Rules, collision queries and fixed-step camera
logic read authoritative state, never an interpolated display position.

Only rendering calls `PresentationPosition(fixedInput.InterpolationAlpha)`.
The helper rejects sampling while a full step remains undrained. Once drained,
alpha is the residual time divided by the step duration, in `[0, 1)`.

- This is interpolation between previous and current states, with an intentional
  one-fixed-step presentation delay. At an exact step boundary alpha is zero
- Zero-step frames change only the display fraction, not simulation
- Pause/focus loss/minimize calls `SnapPresentation()` so alpha zero does not
  rewind the sprite. That collapsed history also survives a zero-step resume
- Restart/teleport must set **both** positions to the new authoritative pose.
  Never interpolate across a teleport or retain an old instance's history
- For lowest-latency, noninterpolated drawing, render `game.Position` directly.
  This simpler policy is still valid and remains in the compared games

The starter has fixed rotation, so this recipe intentionally interpolates only
translation. Rotation, discontinuous animation frames, contacts, attacks and
queries are not interpolated. Camera and player should use a deliberate common
presentation policy to avoid their moving at different apparent rates.

A body center is not a sprite's top-left corner. The starter draws a 32×32 sprite
at `center - (16,16)`. For an unparented `World` entity, write that display origin
into `LocalTransform` immediately before extraction; keep simulation position in
the game object. Do not use that shortcut for a transformed parent: either keep
physics-backed visuals unparented, or explicitly convert the world pose into the
parent's local space. Preserve scale/rotation/shear instead of replacing them
accidentally. No implicit transform conversion or binding was introduced.

A nonzero `PhysicsStepResult.Dropped` means physics has **already advanced**.
Report lost events; never retry the completed step. Event spans are borrowed until
the next step/disposal, so handle each batch before continuing the drain.

## Restart ownership

There are two legitimate recipes in the real comparison:

- Platformer/starter reset existing bodies: teleport, clear velocities, reset
  game timers/counters as intended, clear input debt, and snap display history
- Top-down recreates its arena: dispose all old owners first, create the new
  instance, then reset input/history. The engine permits one physics owner, so
  constructing a second world before retiring the old one fails

Keep UI owners outside a replaced gameplay instance if the menu should survive.
After replacement, retire old UI command identities too: project a fresh
per-instance key or reload the document/session. An identical `Apply` deduplicates
and **does not** create a new revision. Never retain closures or entity/body
handles from the retired instance. The starter's in-place reset deliberately
keeps the same game instance. Make new
construction exception-safe; if it fails after disposal, fail closed rather than
running the disposed old object. [C# composition](CSHARP_COMPOSITION.md) covers
constructor rollback, entity lifetime registration and explicit cleanup.

## What became simpler, and what did not

Inspection of the original platformer/top-down and their verified starter
adaptations found no further shared orchestration abstraction worth introducing.
The existing helper already owns the repeated accumulator and edge logic. Hiding
pause/reset/physics/UI behind callbacks would move code and obscure game-specific
ordering. This stage standardizes the **recipe and its tested invariants**, rather
than claiming fewer total application lines or measured authoring-speed gains.

Nonblank physical lines (including comments/imports/braces), before this stage at
`86cca04` and after:

| Starter source | Before | After | Change |
|---|---:|---:|---:|
| `Program.cs` | 79 | 83 | +4 |
| `FixedStepInput.cs`, entire helper | 54 | 65 | +11 |
| `StarterGame.cs` | 63 | 78 | +15 |
| `StarterOptions.cs`, unchanged | 31 | 31 | 0 |
| All production C#, including helper | 227 | 257 | +30 |
| `StarterChecks.cs`, separate tests | 77 | 106 | +29 |

The added code is validation and optional presentation policy, not erased wiring.
The independent extracted-host harness and optional UI sample/test code are
additional artifacts, not savings concealed outside this table. The optional
sample's complete counts and verification are recorded in [validation](validation.md).

Re-running the existing comparison keeps platformer/top-down host counts at
53/59 (originally 57/56). With the now 65-line helper counted once, that is
177 versus 113 original host lines; with two physically copied helpers it is
242. Their game/presentation files remain unchanged. The original report and its
historical 54-line helper result remain valid for that earlier snapshot; this
stage does not rewrite original fixtures or old evidence. Card UI has no
fixed-step loop to simplify and remains unchanged.

## Verify proportionately

```sh
DOTNET=/path/to/dotnet bash scripts/test-starter.sh
# Optional rendered/menu proof, with existing local package feed and compatible font:
DOTNET=/path/to/dotnet PACKAGE_FEED=/path/to/matched-local-feed \
  GAL_UI_FONT=/path/to/NotoSansCJK-Regular.ttc bash scripts/test-loop-ui.sh
```

The UI proof needs a native package containing the generic UI bridge, as well as
the current managed package; an older native package with the same preview
version is not sufficient. The script rejects that mismatch before building.

The starter script copies the current template outside the repository, uses a
fresh local-only package cache, and checks both its normal self-tests and an
extracted-source host harness. The harness executes the actual frame statements
with real packaged physics and synthetic inputs; menu real-time work is a
recording stand-in there. The optional UI proof checks real RmlUi commands from
queued SDL events and software-rendered pixels. Neither is physical-input,
OS-focus/minimize, hardware-GPU, game-feel or general character-controller
acceptance. No native operations, public runtime boundary or serializer roots
changed; there is no fresh AOT publish or native dependency rebuild in this stage.
