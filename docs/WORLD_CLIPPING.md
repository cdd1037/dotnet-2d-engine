# World rectangular clipping

World sprite submissions can now carry axis-aligned framebuffer scissor rectangles.
This clips rasterized pixels; it does not alter world geometry, collision, input
routing, TileMap visibility extraction or the camera itself. It is separate from
RmlUi's own rectangular scrolling/clip state.

```csharp
var clip = new FramebufferClip(64, 64, viewport.PixelWidth - 128, viewport.PixelHeight - 128);
engine.Draw(camera, batch, new[] { clip }); // one clip for the whole batch

// Or pass zero clips (all unclipped), one (broadcast), or one per final draw.
engine.Draw(camera, draws, clips);

if (FramebufferClip.TryFromWorld(viewport, camera, x, y, width, height, out var projected))
    engine.Draw(camera, draws, projected);
```

The simple example assumes a drawable viewport larger than 128 pixels per axis.
The TileMap consumer clamps its clip extents to zero for smaller windows. Use
`FramebufferClip.Disabled` for an explicit unclipped entry. The raw default struct
has no size/version header and is rejected; it is not silently interpreted as a
constructed rectangle. Negative extents throw; zero extents are valid empty clips.

## Coordinates and ordering

Direct rectangles use integer **framebuffer pixels**, top-left origin and half-open
bounds `[x,x+width) × [y,y+height)`. They stay fixed on the framebuffer when the camera
moves. Window mouse/layout units can differ on high-density displays: use
`TryFromWindow` to scale the two axes independently. `TryFromWorld` projects
axis-aligned world-pixel bounds through the current camera. It does not accept
physics meters implicitly or support a rotated/polygon clip.

Conversion helpers validate finite inputs, nonnegative extents, camera limits and
a drawable viewport. They round outward conservatively and clamp to that viewport;
zero-area input remains empty even at fractional positions. A false result must be
handled by the caller. This mathematical coverage is synthetic; it does not claim
physical high-DPI/multi-monitor validation.

Native submission accepts signed 32-bit origins and nonnegative signed 32-bit extents.
The backend adds endpoints in 64-bit arithmetic and intersects with the framebuffer
actually acquired for the frame. Entirely outside/empty rectangles submit no world
draw. The existing resize-between-poll-and-acquire guard still skips a frame whose
projection dimensions have become stale.

Clip records correspond to the **final draw order**. When using a per-draw list,
construct it after extraction/sorting or preserve the association explicitly in
application code. This batch does not add authored clip IDs, per-entity clip trees,
clip stacks or scene JSON fields. The single-rectangle overload is appropriate
for a camera/map viewport.

Adjacent draws combine only when both texture and requested clip state match.
Changing a clip splits a run without reordering transparent sprites. Equivalent
intersections need not merge: batching compares requested rectangles. Existing
sprite/affine/region entry points always submit canonical disabled clipping, so
mixing legacy and clipped commands in one frame restores the full viewport.
Clip pointers are borrowed only during the call; copied run state owns the values.

## Frame and ABI contract

The entire clip list, draw list, region bounds and remaining frame capacity are
validated before queued geometry/runs change. Managed validation catches obvious
count/default errors before beginning a frame; a native submit error aborts the
managed frame as usual. Invalid later records cannot leave a plausible partial
submission. Clips require no handles, allocation lifetime or disposal operation.

The full framebuffer is still cleared. SDL scissor is set explicitly for every
submitted world run. Empty intersections are skipped, and the following UI pass
has its own state; clipping the world empty does not hide the UI. `gal_stats.sprites`
counts submitted sprites, including clipped ones, while `draw_calls` counts world
runs actually issued. Culling is a separate optimization.

The additive `gal_submit_draws_clipped_v1` accepts unchanged `gal_draw_v2` data plus
`gal_clip_rect` values. The clip record is 32 bytes: size/version/flags/reserved,
then signed x/y/width/height. Version 1 and enabled bit 1 are the only admitted values;
unknown bits, reserved fields and nonzero disabled rectangles are rejected.
Clip count 0 disables clipping, count 1 broadcasts, otherwise it must equal draw
count. Supplied clip headers are validated even for an empty draw list. All ABI 1,
56-byte affine and 88-byte region records remain unchanged.

The implementation uses the existing SDL3 GPU
[scissor operation](https://wiki.libsdl.org/SDL3/SDL_SetGPUScissor). No new dependency,
shader language, compiler, material system or render-target API was added.

## Reproduce and evidence

- `bash scripts/test.sh quick clipping`: layout, safe coordinate conversion,
  invalid/minimized viewports, overflow, frame recovery and warmed allocation checks
- With the UI graphics build/environment, `--clip-graphics-test` then
  `python3 scripts/validate-clipping-pixels.py`: actual software-GPU readbacks
- `--tilemap-clip-demo` or `--tilemap-clip-scenario`: the existing tile consumer in
  a fixed 64-pixel framebuffer margin, with camera pan/zoom and resize
- `GAL_CLIP_CAPTURE_DIR` selects the readback directory for JIT/AOT comparison

Final JIT and fresh AOT each pass **10,328 CPU assertions** (including 35 clip checks),
**9 clip graphics assertions**, and **28 pixel assertions**. Native CTest is 5/5.
All **ten captures** are byte-identical between JIT/AOT: clip edges, stable alpha
order, rotated/flipped atlas sampling, legacy state reset, tile cropping, UI
isolation and resized bounds. Native mock tests also exercise invalid second
records, count/null/header checks, all legacy entry points, merge ordering and
full signed-integer endpoint cases. Warmed clipped submissions allocate zero
managed bytes across 1,000 calls.

The displayed cloud X11 TileMap was operated with keyboard/pointer input: camera
pan, wheel zoom, resize with preserved scissor margins and clean exit were observed.
That remains a software-rendered cloud-window check, not physical GPU or hardware
DPI acceptance. Generated captures/logs remain ignored under `evidence/clipping/`.
