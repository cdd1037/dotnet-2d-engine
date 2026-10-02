# Bounded character-motion recipe

An independent, headless C# package consumer showing a dynamic capsule on one
20-degree slope and one translating platform. Copy this directory outside the
checkout; it needs no linked engine source, renderer, art or UI/font. The default
finite run prints uphill/rise/reversal positions; `--check` runs the focused native
physics fixture. It is application code, not a public engine controller.

## Run

Use an existing .NET 10 SDK and the two prepared `0.1.0-preview.1` packages.
The Linux x64 native package **must have Box2D enabled**; a matching version string
alone does not establish matching binary contents. See the native package's
[system prerequisites](../../native/README.md). No remote feed is used.

```sh
# From your copied directory, put both .nupkg files in ./packages first.
dotnet restore Sample.csproj --configfile NuGet.Config
dotnet build Sample.csproj -c Release --no-restore
dotnet bin/Release/net10.0/Sample.character-motion.dll
dotnet bin/Release/net10.0/Sample.character-motion.dll --check
```

From the repository, validate a fresh external copy/cache with existing packages:

```sh
DOTNET=/path/to/dotnet PACKAGE_FEED=/path/to/prepared-feed \
  bash scripts/test-character-motion.sh
```

`CHARACTER_MOTION_PROOF_ROOT` optionally selects a new external directory. The
script verifies copied source, consumed managed/native bytes and licenses against
the actual packages, then saves logs and package/source hashes. It never rebuilds
the engine, repacks dependencies or publishes AOT. On the prepared authoring runner,
the verified physics feed is `../game-loop-package-feed` relative to the repository;
the default `build-packages/feed` may contain a different preview profile.

## Copy the policy, not a promise

- `GroundSupportProbe.cs` reads three downward rays in meters. It returns support
  identity, normal and linear velocity; the first qualifying ray wins. Collision
  bits actor=2/terrain=1, normal cutoff -0.7 and separation threshold 0.1 m/s are
  visible game choices. A retained identity still requires a matching ray hit
- `CharacterMotion.cs` owns one step, gravity, tangent speed, platform carry and
  detach. The platform's requested velocity is set **before** the player's step.
  Only this one platform moves; the body-ID lookup returns zero for static terrain
- A same-body rider survives the tested abrupt reversal. Authored jumps clear the
  retained body. Teleport clears identity, velocity, jump grace and interpolation.
  If adapting the code to expose forces/impulses or external launches, explicitly
  invalidate support and grace before the launch; direct body mutation is not an
  offered path here
- Gravity is intentionally caller-owned (`GravityScale=0`). Airborne gravity is
  applied before the step, except the launch tick's jump velocity replaces it;
  supported motion replaces it with tangent speed plus platform velocity. This
  stabilizes the selected slope/ride but changes measured flat jump rise from the
  previous native-gravity recipe's 127 px to **132 px**, still inside its existing
  128±5 px contract. It is not a behavior-neutral drop-in replacement
- Airborne X velocity is authored again each tick. Support X velocity lasts only
  through the supported jump tick; supported jumps inherit current platform Y
  velocity. Coyote jumps do not inherit a remembered platform velocity. Choose a
  different leave/momentum policy deliberately if your game needs it
- `JumpGrace.cs` is a game-owned 100 ms coyote/buffer rule. Pass a pressed edge, not
  held input. `MotionChecks.cs` is separate fixture code, removable in your game

The existing starter and demos are unchanged. No gameplay tuning is silently
applied to them.

## Host and presentation

The default driver uses exactly one 1/60 s step per call. For a live game, keep the
[starter's fixed-step host](../../../templates/Starter/README.md): map input once,
drain its `FixedStepInput`, call `Tick(axis, jumpPressed)` once per drained step,
then draw with its interpolation alpha. A pause/focus-loss boundary must stop
draining steps, reset input edges/debt, and call `Suspend`; `SetPaused(true)` also
snaps history and clears jump grace. The recipe's pause freezes the whole physics
world, including the platform. It does not schedule the outer host for you.

Both bodies have one authoritative post-step read, `PhysicsScale` conversion to
pixels, explicit previous/current copies and checked presentation interpolation.
Use `PresentationPosition(alpha)` and `PresentationPlatformPosition(alpha)` only
for drawing; rules read current `Position`/native state. Never feed an interpolated
pose back into physics. Teleport/pause snap both histories; resume starts a new
pair. There is no new units or transform-binding abstraction.

## Bounds and verification

Checks cover flat landing/movement/jump and grace rules; slope idle, both tangent
directions and jumping; actual platform identity/height, rise, abrupt reversal,
descending support reacquisition after teleport and jumping; support invalidation,
pause, both interpolation histories and ownership cleanup. The test prints the
measured flat jump apex. Ballistic landing onto a moving platform is not tested.

No general floor snap, seams, walls/ceilings, one-way surfaces, rotating or deleted
supports, many-support lookup, collision-safe carry under obstacles, arbitrary
external impulses, or general platform-leave momentum is established. Three rays
do not replace swept collision or `MoveAndSlide`. The solver still resolves the
dynamic capsule. This is a narrow recipe to edit, not a compatibility guarantee.

Headless native checks and numeric interpolation do not establish rendered
smoothness, live input, game feel, device performance, AOT or other platforms.

Validated on 2026-10-02 with .NET SDK 10.0.401 and the prepared physics feed:
35 focused assertions, the 390-step finite driver, and empty-world reopen after
both runs passed. Restore/build reported zero warnings/errors; the consumed
managed DLL and all eight packaged native libraries matched their package bytes.
Measured jump rise was 132.000 px; rise/reversal/reacquisition platform relative drift
stayed below 0.005 px in this fixture. Source/package hashes and full logs are
generated by the script, not required files in a fresh checkout.
