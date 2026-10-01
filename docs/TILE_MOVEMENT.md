# Tile movement acceptance fixture

`--movement-demo` is a small game built from the existing input, TileMap, Box2D,
sprite and typed UI APIs. `TileMovementGame.cs`, its host and tests compile only
into the demo executable. No controller API, native ABI or serializer root was
added to the runtime package.

The source-generated `movement.tilemap.json` is a 24x12 grid placed at (64,64)
pixels. Its existing atlas resource IDs produce a red floor and two blue walls.
TileMap owns the generated static collision and texture leases. The fixture owns
one fixed-rotation dynamic box and a static goal sensor through `PhysicsScope`.
The solver owns the player pose; rendering explicitly converts meters to pixels
at 32 pixels/meter. Positive Y is down, gravity is 18 m/s², movement is 4 m/s,
and a grounded jump sets vertical velocity to -7 m/s. Assigning horizontal speed
preserves vertical velocity. These are sample game rules.

Controls: A/D or left/right move, Space jumps, E pauses/resumes, T restarts,
Escape exits. The displayed status reports pose, grounding, goal enter/exit
counts and restarts. A terrain-filtered downward ray with an upward-facing normal
provides the simple grounding check. Player and sensor categories are excluded.
This flat-floor example makes no slope, platform, ledge or complete character
controller claim. The goal is a discrete overlap sensor, not a swept trigger.

`TileMovementClock` consumes `InputActionMap` results and advances at 60 Hz.
Render frames accumulate a jump edge until the next fixed tick, then consume it
once even if airborne. Holding Space does not repeatedly jump. Opposing directions
cancel. A frame contributes at most 100 ms, hence at most six catch-up ticks;
excess wall time is intentionally discarded. Negative/nonfinite deltas fail.
Focus loss, nondrawable/minimized state, pause and restart clear pending actions
and accumulated time. Resume requires raw bound keys to become neutral, including
keys already consumed by UI. A neutral poll itself advances no old time.

Events are inspected after every physics step. Overflow is a failure, never a
quietly missing goal transition. Restart disposes the old map and body scope,
performs one explicit reclamation step, then creates fresh resources. Goal state,
velocity and spawn reset; old body identities are not reused. Final teardown
checks zero live bodies/shapes, retired shapes and cached textures.

## Validation

With existing prerequisites:

```sh
bash scripts/test.sh quick movement
source scripts/ui-env.sh
../android-trim-tools/dotnet/dotnet managed/bin/Release/net10.0/GameAuthoringLab.dll --movement-physics-test
GAL_MOVEMENT_CAPTURE_DIR="$PWD/evidence/movement/scripted" ../android-trim-tools/dotnet/dotnet managed/bin/Release/net10.0/GameAuthoringLab.dll --movement-scenario
python3 scripts/validate-movement-pixels.py evidence/movement/scripted
```

The mock/headless contract tier has 27 input/clock checks, including quick taps,
pause/focus/minimize/restart boundaries and zero warmed clock allocations. The
real Box2D tier has 62 checks for standing, jump/landing, ignored airborne jump,
both wall limits, trigger enter/exit, filtered queries and 12 full scene restarts.
The 600-frame rendered scenario adds eight pose/state checkpoints, including a
quick jump through the complete input-to-solver path. Five actual software Vulkan
readbacks pass 30 floor/wall/player-position checks.

Displayed cloud X11 keyboard checks also passed: movement across the goal,
visible jump/landing, pause while holding movement, holding against the right
wall, focus loss during a jump with an unchanged airborne pose across more than
30 seconds, focus return, restart and return to floor standing. The game was
closed afterward. This used the cloud software renderer, not hardware GPU,
physical audio or a Windows/macOS environment. The UI uses the existing external
font; no font was added. No new AOT run is required for this sample-only batch.
