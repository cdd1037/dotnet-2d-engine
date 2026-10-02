# Starter wiring: two real host adaptations

Measured 2026-10-02 against the existing sibling `authoring-comparison` fixtures.
The bounded result is **a reusable, checked input/clock policy**, not fewer total
application lines or a measured development-speed improvement.

## Scope and counts

`scripts/compare-starter-wiring.py` copies the actual platformer and top-down
projects into a new external directory, then changes only each copied
`ours/Program.cs` and adds the starter's `FixedStepInput.cs` byte-for-byte. Game
rules, physics, assets, presentation classes and ordinary PackageReferences remain
unchanged. The original fixtures and Godot projects are not edited or executed.

Counts are physical nonblank lines, including braces, comments and imports, with
normal formatting. They cover the whole host file, including startup/test routing
and cleanup; they are not semantic statement counts.

| File | Before | After | Change |
|---|---:|---:|---:|
| Platformer `ours/Program.cs` | 57 | 53 | -4 |
| Top-down `ours/Program.cs` | 56 | 59 | +3 |
| `FixedStepInput.cs`, per copied helper | 0 | 54 | +54 |
| Card UI `ours/Program.cs`, inspected only | 20 | 20 | 0 |

The two adapted hosts total 112 lines versus 113 originally. Counting the reusable
helper once makes 166; the two isolated projects physically each carry a copy, so
their host-plus-helper total is 220. Supporting game code and assets are unchanged
and excluded on both sides. The adaptation/count/test script and generated test
harness are separate tooling, not hidden application-code savings.

## What moved, and what changed

- The accumulator, `.1`-second frame cap, pending one-shot storage, edge consumption
  and clearing move into the helper. They were not deleted. The helper also adds
  finite-delta validation, dropped-time accounting, released-edge retention and
  a relative floating-point boundary tolerance. Multiple presses before a tick
  coalesce into one action bit; this is not an ordered input-event queue
- The host still explicitly polls once, updates action mappings, chooses pause and
  restart behavior, calls `BeginFrame`, drains `TryTakeStep`, calls game-specific
  simulation, draws once when drawable, and owns cleanup. No hidden engine loop,
  automatic world update, or engine-level callback contract is introduced
- Platformer keeps A/D/arrows, Space jump, Escape pause, T restart, its original
  float `Simulation.StepSeconds`, native simulation and presentation. Its existing
  pause/focus/no-draw suspension is retained. Restart now clears old queued jumps
  and accumulated time, and deliberately consumes no gameplay input on that frame
- Top-down keeps WASD, E interact, Space attack, Escape pause, T restart and its
  original `1d / 60` simulation step. Explicit `InputActionMap` bindings replace
  direct key reads. This adds the map's existing focus-regain neutral-control gate
- Top-down's old outer loop kept accumulating/draining while paused; `Arena.Step`
  itself returned early. The new host suspends and clears pending input on pause,
  focus loss, nondrawable frames and restart. It skips drawing when nondrawable
  and gives its camera zero gameplay time while suspended. These are intentional
  lifecycle fixes, not a behavior-identical extraction or helper-only LOC saving
- Both hosts update their wall-clock sample while suspended, avoiding resume-time
  catch-up. Restart clears the queue and discards same-frame action edges. Game
  restart semantics remain their own: platformer preserves pause; top-down creates
  a fresh arena. The helper does not reset game state or game-owned timers

The card UI host uses a bound UI action queue and event-driven game rules. It has
no fixed-step accumulator or gameplay edge buffer to replace. Adding this helper
would add wiring without a demonstrated benefit, so it is left alone. This batch
does not shorten its UI markup, list binding, refresh, revision or save/load work.

## Verification and its limits

Both unmodified snapshots and both adapted copies built Release with **zero
warnings and zero errors**, using .NET SDK **10.0.401** / runtime **10.0.12** on
Debian 13 Linux x64. Package sources were inspected and restricted to the prepared
local `build-packages/feed`; inherited native-loader and asset-root overrides were
cleared before execution. No package was rebuilt, installed globally or replaced
with an engine-source ProjectReference.

- Platformer native contracts: **13/13** on both baseline and iterated scenes,
  before and after
- Top-down native contracts: **14/14**, before and after
- Separate deterministic host-policy harness: **47 checks passed** across both
  adapters. It extracts the actual adapted host policy statements and bindings,
  uses the unchanged helper and package input API, and substitutes recording
  simulation/draw objects. Checks cover zero-step queued taps, catch-up one-shot
  consumption, held movement, all direction bindings, distinct E/Space actions,
  pause/resume, restart plus simultaneous input, focus loss/neutral recovery,
  no-draw suppression, camera suspension and long-frame capping
- The script verified **110 original source/data files** unchanged during the run
  and compared both helper copies byte-for-byte with the template

The native `--test` branches bypass the interactive hosts. They validate the
unchanged game/native contracts, **not** real keyboard events, window focus,
minimize/restore, visual presentation or the outer loop's wall-clock sampling.
The synthetic harness exercises extracted policy, not an SDL event loop. No
interactive/manual or graphics acceptance, fresh NativeAOT publish, Godot rerun,
performance comparison or human authoring-efficiency experiment is claimed.

## Reproduce

Prepare the external fixtures, local SDK and local preview feed first; this is not
a self-contained downloaded SDK/runtime distribution. From the repository root:

```sh
python3 scripts/compare-starter-wiring.py \
  --source ../authoring-comparison \
  --output ../authoring-entry-comparison-repro \
  --feed "$PWD/build-packages/feed" \
  --dotnet "$PWD/../android-trim-tools/dotnet/dotnet" \
  --run
```

Adjust paths for the machine. The output must not exist and must be outside both
the fixture source and repository. Omit `--run` to generate/count without building.
The script preserves before/after source snapshots, unchanged project files,
helper copies, a generated host-policy test, exact build/test commands and SDK/feed
logs, and `results.json` with line/byte counts, hashes and exit codes. Failure is
nonzero with the specific log path. No Godot executable is invoked.

The recorded run lives outside the tracked repository at sibling
`authoring-entry-comparison-verified/`; raw logs are local evidence, not shipped files.
Identity for this run:

- Helper SHA-256: `57aadd655355a58ae3efb53e024ec2e0c0b298f8b3f433f1d0e656fd82615505`
- Platformer before host: `5ac1c6bb745f38abc6de4a0b11b7c0ebc4f0862e182d2e7cd8c7eab81bf0271e`
- Top-down before host: `2bd2157a92dd48a91ff17b6930ea1015fec54892e366073547553955024a700d`
- `Dotnet2D.Engine` **0.1.0-preview.1** package SHA-256: `cbb0252bc746a1ffe2393ae445f2dab4300f2c0b8078cfc0c03c60493e532940`
- `Dotnet2D.Native.Linux.x64` **0.1.0-preview.1** package SHA-256: `8ad9132d698d7c251c53bc824dbb16340d19e7468804e373054f79add2cfdbf1`
