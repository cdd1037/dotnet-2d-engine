# Explicit camera follow

`CameraFollow` is a pure managed helper for following a world point, optional
half-life smoothing, and rectangular world bounds. The caller keeps the returned
`Camera`; there is no controller object, entity reference, scheduler or hidden
clock. The existing three-float camera ABI is unchanged: `X` and `Y` are the
visible world's top-left and `Zoom` is preserved.

```csharp
using System.Numerics;
using GameAuthoringLab;

Camera camera = new() { Zoom = 1 };
CameraFollowOptions follow = new(
    WorldOffset: new Vector2(0, -40),
    HalfLifeSeconds: .12,
    Bounds: new CameraBounds(-256, 0, 4096, 2048));

// Once per chosen update, after resolving the current player's world point:
camera = CameraFollow.Update(camera, playerWorldPoint, input.Viewport,
    TimingStep.FromReal(frameSeconds, paused: gamePaused), follow);

// On an explicit teleport/target replacement, if immediate reframing is wanted:
camera = CameraFollow.Snap(camera, playerWorldPoint, input.Viewport, follow);
```

The example's `playerWorldPoint`, `input`, `frameSeconds` and `gamePaused` are
caller-owned values. Pass a fresh point from a live target. Destroying/unloading
an entity cannot leave a retained target inside this helper. Zoom changes remain
the caller's responsibility and take effect in the next positioning calculation.

## Position and time contract

- Desired center is `targetWorldPoint + WorldOffset`, in world pixels. Visible
  width/height use **framebuffer** pixels divided by current zoom, independently
  of logical-window DPI. The desired top-left subtracts half of those dimensions
- `HalfLifeSeconds` is finite and nonnegative. Positive values close half the
  remaining distance per half-life using exponential smoothing. This is
  frame-partition invariant for a fixed desired point up to float rounding when
  output bounds do not intervene; moving targets and changing constraints are
  naturally sampling-dependent
- Defaults, including `default(CameraFollowOptions)`, select game time, zero
  offset, no bounds and zero half-life. Zero half-life moves immediately only
  when the selected delta is positive
- `ClockDomain.Game` uses `TimingStep.GameSeconds`; opt into
  `ClockDomain.RealTime` to continue following during a game pause. Time scaling
  is supplied through `TimingStep`, not applied again by this helper
- Zero selected time preserves the exact current camera, even outside bounds or
  after a resize. Inactive/invalid `Viewport` values also preserve it. Input and
  option validation still runs before either no-op
- `Snap` bypasses time and smoothing for initialization or teleporting. It still
  validates all options, applies the same bounds and preserves invalid-view state

## Bounds and numeric safety

`CameraBounds(X, Y, Width, Height)` is an optional world-pixel rectangle with
finite double coordinates, nonnegative extents and finite right/bottom edges.
Negative origins and zero-size axes are valid. Each axis is handled separately:

1. When the world is at least as large as the visible extent, constrain the
   top-left so the complete view stays in the world
2. When the world is smaller than the view, center it on that axis instead of
   pinning the view to one edge
3. Constrain the desired position before smoothing, then constrain the output.
   A positive-time update therefore corrects a current position outside newly
   supplied bounds immediately. This correction can break partition invariance;
   the bounds take priority. Zero-time updates never make that correction

Current position, target and offset must be finite; zoom must be in `[.01, 100]`,
matching existing viewport/culling helpers. Intermediate position math uses
doubles to avoid float subtraction/addition overflow. Exponential evaluation
handles very small positive deltas and half-lives without losing otherwise
representable movement or overflowing its ratio.

Results must fit finite float camera coordinates. Fitting axes round inward at
bounds after float conversion. An interval with no representable float top-left
(for example, an exact-fit world starting at double `0.1`) rejects explicitly.
These invalid numbers, options or unrepresentable results raise
`ArgumentOutOfRangeException`; they are never silently repaired into a plausible
camera. Small-world centering uses the nearest representable float position.

## Verification and scope

Run `scripts/test.sh quick camera`, or the already-built managed host with
`--camera-self-test` for the pure CPU suite. The complete JIT self-test includes
the same contracts. The initial isolated Release run passed **137 assertions**,
including fixed-target partitions, DPI/zoom changes, negative coordinates,
large/small/empty bounds, exact-fit and float-rounding edges, snapped state,
paused/scaled/real time, invalid inputs, extreme finite intermediates, tiny
positive deltas, long-step residuals and zero warmed allocation in 2,000 bounded
update/snap pairs.

This batch adds no native entry point, rendering feature or serialization root.
The focused checks are managed geometry/time contracts, not a rendered camera
scenario or new hardware/AOT claim. Dead zones, look-ahead, rotation, shake,
parallax and multiple cameras remain outside this helper.
