# Phase 1: RELAY / Archive Rescue (historical fixed-profile probe)

The current maintained full-game entry is the [independent public-package RELAY
app](RELAY_REFERENCE.md). This page preserves the original phase-1 architecture
and evidence. Its old executable remains runnable with `scripts/run-legacy-game.sh`;
`scripts/run-game.sh` now selects the public-package application.

A repeatable small game built on the existing two-room sample. Carry the amber
power cell from the Garden Workshop through the east door to the glowing relay
in the upper-right Field Archive. Press **E** near the relay while carrying the
cell before the 90-second simulation timer expires.

## Run and controls

Build managed Release and the optional native UI build using the prerequisites in
[UI_PROTOTYPE.md](UI_PROTOTYPE.md). No new dependency is introduced. From the repo
root, `scripts/run-legacy-game.sh` runs the prepared original managed/native builds and assets;
it does not run an older packaged ZIP. A displayed window runs until closed.

- WASD / arrows: move; E: pick up or deliver; F: drop; T near a door: change rooms
- Escape: pause / resume; Space: start / resume / restart on the relevant screen
- F5: save during play or pause; F9: load a checkpoint (enters pause)
- T from pause or a result: title; window close: quit
- Mouse buttons: explicit Start, Pause, Resume, Save, Load, Restart, Title commands
- Release held mapped keys after start, resume, reload or restart before moving

The pause menu exposes Restart. Winning and losing have separate result screens
and restart/title choices. The loss condition is timeout; this slice does not add
combat, health, enemies, new physics, dynamic inventory or a general UI framework.
The timer advances with actual bounded fixed simulation ticks, not wall-clock time;
pause, lost focus and dropped catch-up backlog do not consume mission time.
A final-tick delivery wins a tie with the timeout.

`--save-file PATH` chooses a checkpoint, default `relay-save.json`. Writes validate
before a unique same-directory temporary file replaces the destination. A failed
validation leaves the old file; this is not a power-loss/fsync guarantee. No
checkpoint is silently overwritten on start/restart. Original `--room-demo`
world-only saves remain a separate format and mode.

## Authored inputs and lifetime

`assets/relay.mission.json` is a small strict v1 mission definition: identity, title,
objective, duration and a bounded delivery target in the clear archive zone.
It is separate from the existing flat authored-scene document and runtime world
snapshot. Room collision geometry remains the existing explicit two-room sample.
A general prefab/level metadata system has not been introduced.

Restart reads a fresh mission candidate and creates a fresh room World. Both room
resource sets are preflighted and candidate texture preparation must succeed before
the live run is replaced. Bad JSON, incompatible saves and failed candidate asset
preparation retain the last usable run; diagnostics identify source file/field.
Source edits do not mutate the live definition. Existing save rules must exactly
match the current authored mission to avoid silently changing a checkpoint's timer
or objective. A relay save wraps the existing source-generated runtime world save,
plus the explicit mission and remaining ticks. Neither UI nor runtime behaviors
are serialized.

A replaced run unloads room scenes and destroys persistent entities, invoking the
existing explicit owned-behavior cleanup. The host owns the UI context and texture
bank. Room changes synchronize the bank; restarts do not create another native
context. Screen boundaries discard pending simulation input, clear stale UI
commands through generations, and require neutral gameplay input. Native SDL focus
loss suppresses gameplay keys and the host pauses; focus gain never auto-resumes.

Retirement attempts the remaining scene/entity cleanup even when one registered
callback throws, then reports the failures together. A cleanup callback error after
replacement does not undo the newly committed run. If numeric teardown preflight
leaves part of the old run alive, the mission retains that one pending owner and
retries it before another replacement or during disposal; it does not accumulate
an unbounded list of abandoned runs.

Disposal closes mission operations before callbacks run, so a callback cannot
reopen the mission and loading a checkpoint after disposal is rejected. A later
`Dispose` can retry incomplete teardown after the caller repairs invalid transforms
through retained entity references. Already retired callbacks do not run again,
and a recursive `Dispose` during cleanup is a no-op. Callback failures themselves
are reported after commit; they are not retried as if the callback had never run.
Preparation and retirement callbacks cannot start or load another replacement.
Disposal during preparation is allowed; the uncommitted candidate is cleaned up
instead of reopening the closed mission when the preparation callback returns.

The game UI has its own [bounded RML/RCSS profile](GAME_UI_PROFILE.md) and explicit C# model/commands.
There are no hidden settings controls, settings-action aliases or generic DOM
binding. Settings experiments remain unchanged. The mission marker uses existing
sprite rendering. No external art or font download is performed.

## Verification commands

```sh
scripts/test.sh quick game                 # compile + CPU mission regression
scripts/test.sh jit                        # complete feature-batch JIT suite
# --game-ui-self-test separately checks the native game UI adapter
source scripts/ui-env.sh                   # prepared software Vulkan, offscreen
GAL_GAME_CAPTURE="$PWD/evidence/mission/jit" \
  ../android-trim-tools/dotnet/dotnet managed/bin/Release/net10.0/GameAuthoringLab.dll \
  --game-scenario --save-file evidence/mission/scenario-save.json
```

Use your installed `dotnet` path instead of the workspace convenience above.
`--game-self-test` runs focused CPU checks without a renderer. `--game-scenario`
requires the real optional UI renderer and uses actual Rml event listeners with
synthetic gameplay input, captures title/pause/archive/win/loss, repeatedly restarts,
rejects stale UI commands and verifies final world/texture/context cleanup. Its
default save is `relay-scenario-save.json`, separate from the interactive checkpoint.

Software Vulkan checks are not physical-GPU, speaker, real keyboard timing or IME
acceptance. The settings prototype's intermittent first Reset after scrolling
remains separate outstanding phase-2 work. No package/ZIP, remote or release is
created by this phase. Final counts and exact command evidence belong in
[validation.md](validation.md).

The title currently keeps the frozen last-room background and its time readout as
an attract/recap view. Start always creates a new 90-second run; the title does not
resume the old timer. Refining that readout is a small later UI polish item.
