# Fixed mission UI profile

Current API note: this fixed-profile/target-binding implementation is retained for
internal regression coverage. Public applications use [generic UI models](UI_MODELS.md),
typed handlers, and `StageAsset` initialization. Historical measurements below
apply to the implementation/revision stated, not the current public API.

This is a separate closed RmlUi profile for the phase-one playable mission. It
uses `assets/ui/game.rml` and `game.rcss`; no settings inputs, Apply/Reset aliases,
hidden dummy controls, arbitrary bindings, or phase-two UI framework were added.

## Host contract

`GameUiSession` owns the one native UI context for the host. Load validates and
stages immutable RML/RCSS copies. Render once to commit, then read State.Generation.
Set returns the current generation, which the host must retain:

```csharp
using var ui = new GameUiSession(engine);
ui.Load(GameUiSession.SourcePath);
Render();
uint generation = ui.State.Generation;
generation = ui.Set(generation, screen, title, objective, status,
                    secondsLeft, canSave, canLoad);
```

Screens are Title=0, Playing=1, Paused=2, Won=3, Lost=4, matching MissionScreen.
Actions are Start=10, Resume=11, Save=12, Load=13, Restart=14, Menu=15, Pause=16.
Poll returns an explicit `GameUiAction` with Generation and Action; None=0 means
empty. Capture and scripted Command are diagnostics, not OS input verification.

- Title displays Start and optionally Load
- Playing displays HUD objective/status/time and Pause
- Paused displays Resume, optional Save/Load, Restart and Menu
- Won/Lost display the host's distinct result title/status plus Restart and Menu

Real commands remain real buttons in the document; only commands relevant to the
current screen are shown. Native listener dispatch and scripted commands both
reject unavailable actions. Settings and game poll/model APIs reject the wrong
profile rather than interpreting each other's fields.

Generation advances and queued actions/overflow reset on screen or availability
flags changes. Same-screen text/timer changes retain generation. Hosts must drop
old-generation events and process at most one transition command per input/render
boundary. Reload/close/reopen also retires the old document and queued listeners.
A single native profile/context is active; do not overlap owning UI sessions.

## Bounded data and validation

The additive C ABI uses size-checked `gal_game_ui_model` (664 bytes) and
`gal_game_ui_action` (16 bytes). Model title is at most127 UTF-8 bytes; objective
and status at most255 each. Seconds is0..9999, screens0..4, availability flags0..3,
and reserved fields must be zero. Text is strict UTF-8 without control characters,
validated by managed and native boundaries, and escaped before insertion into Rml.
The action queue holds64 entries with explicit overflow count.

GameUiAuthoring uses the existing bounded framework XML tokenization and restricted
RCSS tokenizer, with a distinct fixed-ID/tag/parent schema and selector list.
Only the sibling game.rcss resource is allowed. DTD/external entities, scripts,
namespace tricks, event attributes, unknown nodes/IDs, arbitrary resource URLs,
inline markup within text controls, and binding expressions are rejected. Parsed
content is staged from exactly the validated strings. Settings validation remains
its own unchanged profile.

## Input ownership and focus

This game UI contains no text field, so KeyboardFocus is false. MissionHost owns
modal simulation/input gating, preserving its Space/Escape/F5/F9/T shortcuts.
The settings profile retains text-focus filtering separately.

Input.Keys gains the additive FocusLost bit2048 without changing struct layout.
SDL window focus-lost latches it until focus-gained. While latched, gameplay keys
and wheel are suppressed; the host auto-pauses and requires neutral input before
resume. The latch starts clear and is event-driven so offscreen testing is not
mistaken for an actual desktop focus-loss event. It does not claim verification
of physical OS event timing or startup-without-focus behavior.

## Focused checks

`GameUiTests.RunAuthoring` validates the real profile plus malformed/unsupported
mutations and ABI sizes. `RunNative` owns its own graphics context and verifies
actual Rml pointer hit-testing, all explicit actions, availability rejection,
bounded queue/overflow, generation retirement, timer updates, UTF-8/numeric bounds,
profile separation, close/reopen, and synthetic SDL focus events. Probe-only native
commands100/101 enqueue focus loss/gain;200+action uses Rml pointer hit-testing.
They are not game commands or a general input-injection API.

Focused logs and a visually inspected title capture are in `evidence/mission-ui/`.
The gameplay lead owns the final aggregate JIT/NativeAOT regression. Physical
keyboard/mouse, real IME, physical GPU, and arbitrary small-window layouts remain
outside the focused adapter evidence.
