# Bounded diagnostics

These opt-in managed helpers use the existing renderer and native counters.
They add no native ABI, callback registry, reflection, background thread, editor,
GPU timestamps or automatic global instrumentation. The runtime package includes
the public APIs; applications that never call them do not acquire a static host
reference to these modules.

## Debug geometry

`DebugDrawBuffer(capacity, whiteTexture)` preallocates up to 65,536 line quads.
`whiteTexture` is a borrowed `TextureBinding` for a solid white texture or atlas
region. Keep its lease alive through submission, and use its owning engine.
Handle zero is useful for headless contract tests; the graphical fallback texture
is a soft round sprite and is unsuitable for solid debug lines. The sample reuses
the white texel at `(7,7,1,1)` in the existing `regions.bmp`.

Emission defaults off. Set `Enabled`, then call `Clear()` once per frame and
`TryAddLine` / `TryAddRectangle`. Coordinates and thickness use world units and
the same camera as sprites. Lines have centered, butt-ended strokes. Rectangle
edges are top/right/bottom/left in painter order, with ordinary alpha overlap at
corners. Zero-length lines or zero-area rectangles emit nothing. Finite positive
thickness, valid RGBA channels and representable float corners are required.

Capacity failure returns false and increments `DroppedPrimitiveCount`; a box
reserves all four quads or emits none. Invalid input throws before mutation.
Disabled calls skip validation and geometry work. Disabling discards queued
geometry; `Clear` also resets the per-frame drop counter. The buffer owns no native
resources and needs no disposal. Like sprite extraction, mutate it on one thread.

`RegionDraws` borrows the buffer until its next mutation. Submit it by itself with
`EngineHost.Draw`, or after scene geometry using
`EngineHost.DrawWithOverlay(camera, scene, overlay)`. The latter uses one frame,
the same camera, and no scissor. Both spans share the engine's total sprite budget;
an invalid second submission aborts the whole frame and permits a later draw.
This is an ordered geometry pass, not an independent screen-space UI layer.

## Structured log queue

`DiagnosticLog(capacity, includeTimestamp: false)` is a fixed FIFO with 1..65,536
entries and defaults to `MinimumLevel = Off`. Entries contain severity, nonzero
numeric event code, a 1..48 ASCII identifier category, up to 1,024 UTF-16 message
code units, caller-supplied frame number and a finite numeric value. This supports
tool output without parsing a formatted string. Event-code meanings belong to the
producer. `TryRead` drains copied values and releases retained string references.

The full queue drops new entries and increments `Dropped`, preserving existing
unread evidence. `Clear` releases queued strings and retains lifetime counters.
Filtered calls neither validate payloads nor read clocks. Reuse strings, or check
`IsEnabled` before interpolation: evaluating arguments is still the caller's work.
Timestamping is explicitly enabled at construction, reads only for accepted
entries, and uses raw `Stopwatch` ticks rather than wall-clock UTC. Counts saturate
at `ulong.MaxValue`. The queue and timing collector enforce creating-thread access
for mutations; neither is a multi-thread telemetry transport or a persistent sink.

## CPU timings

`CpuTimings` defaults disabled. Explicit `BeginFrame`, `Measure(CpuPhase)` and
`EndFrame` calls record CPU elapsed wall time for Input, Update, Extraction, Render
and Other. Each value-type scope should be used directly in `using`; boxing it as
`IDisposable` would allocate. The disabled path performs no timestamp reads.
Enabled scopes use `Stopwatch.GetTimestamp`, without per-frame managed allocation.

Phase scopes cannot overlap or nest. Repeated or stale copies of a disposed scope
cannot finish a newer phase, including after `Reset`. Finish the active scope
before ending a frame. On failure call `AbortFrame`, which discards the incomplete
phase/frame and counts the abort; already completed phase samples remain. Reset
is allowed only while idle. Frames and each phase expose count, last, minimum,
maximum, total and average seconds. No rolling history or percentile estimator is
implied. Callers select the measured boundary and sampling cadence.

These durations include JIT, allocation, waits, capture and presentation occurring
inside the scope. Render duration is not GPU execution time or measured latency.
Native `Stats` remains separate: cumulative frames, submitted quads and actual
issued world draw calls (plus the legacy tone counter). Native errors continue
through existing exception/error APIs; applications explicitly choose which ones
to enqueue, so logging does not silently suppress or replace failures.

## Fixture and validation

- `scripts/test.sh quick diagnostics`: log/clock/ownership/capacity/recovery and
  geometry checks, including warmed allocation measurements
- `--diagnostics-demo`: D toggles debug drawing, T toggles CPU timing, Escape exits
- `--diagnostics-scenario --frames 3`: enabled/disabled/re-enabled geometry,
  explicit phase timing and a structured JSON summary at exit. Headless mode
  checks counts only. `GAL_DIAGNOSTICS_CAPTURE_DIR` enables three graphical
  readbacks; `scripts/validate-diagnostics-pixels.py <directory>` validates them

The scenario's CPU numbers are observational output, not a benchmark. The graphics
fixture uses the existing software Vulkan setup and does not establish hardware
GPU or physical display performance.
