# Explicit RGBA8 render targets and passes

For managed application code, prefer [typed sprite commands and pass factories](SAFE_AUTHORING_BOUNDARY.md). The low-level ABI contract below remains available.

This is a bounded extension of the Linux Vulkan renderer: reusable target textures,
an explicit ordered pass list, and basic post-processing through the existing
material API. The old `Draw` overloads continue to present one window frame. There
is no render graph, automatic dependency scheduler, depth/MSAA target, lighting,
arbitrary texture format or extra shader compiler dependency.

## Ownership and limits

`engine.RenderTargets.Create(width, height)` returns an owned `RenderTarget`.
Dimensions are 1..4096, with at most eight live targets and **64 MiB of paired RGBA8
texel storage** across the context. Each target uses **8 × width × height bytes**:
a private attachment and a public sampled image. This is logical texture storage;
driver allocation overhead, frame vertices and pipelines are additional. For
example, 4096×2048 consumes the full budget, so another target or a same-size resize
replacement requires releasing something or choosing smaller dimensions.

New targets contain transparent black, including when sampled before their first
pass. `Binding` borrows the sampled texture, and `Handle` is both the target and
sample identity. Keep the target alive through rendering. Texture dimensions and
the native texture count include targets, but ordinary texture release rejects a
target handle. The separate BMP cache retains its 256-entry quota.

Create/release occur on the engine's creating thread, outside legacy active frames.
Disposal is repeatable; engine-first destruction releases both images and makes
remaining targets unusable, while their later disposal stays safe. Released and
prior-context identities are rejected. Target store construction is internal so
callers cannot manufacture ownership. Target initialization pipelines are created
only when the first target is requested. Each explicitly acquired material owns
two bounded pipeline variants, for the window format and RGBA8 targets.

Resize explicitly: create a new target, switch the caller's references after it
succeeds, and dispose the old one. Failure retains the original. Contents are not
copied to a replacement. Window resizing does not silently resize offscreen assets.

## Frame/pass contract

Build a reusable `MaterialDraw[]` and `RenderPass[]`, then call
`EngineHost.RenderFrame(passes, draws, clips)`. A pass is created with target
handle, camera, first draw, draw count and straight RGBA clear color. Clear channels
must be finite in [0,1]. Ranges partition the shared draw array contiguously, in
order, with no omissions or duplicated draws. Empty passes are allowed and clear
their destination. Every frame has 1..16 passes, with **exactly the final pass
targeting the window (handle zero)**. The same offscreen target may be written in
different passes; a pass always clears rather than loading previous attachment
contents.

All draws together share the engine's existing maximum-sprite budget. Projection
and scissor use each destination's pixel dimensions. A target's camera never uses
window density or window dimensions. The optional clip array has the existing
zero/one/per-draw cardinality. Material/texture/parameter/clip batching is stable,
and adjacent runs never merge across pass boundaries. UI renders once after the
final window's world pass. The screenshot facility captures that final result.

The native entry point validates the complete pass list, ranges, cameras, clear
colors, handles, sprites, parameters and clips before executing any pass. Sampling
the current attachment's public target identity is rejected as feedback, even
though the implementation owns two images. Sampling another target or previous
frame contents is allowed. An invalid later pass cannot clear an earlier target.
The call is also rejected inside an existing legacy Begin/End frame.

The window is acquired before any pass runs. A minimized/non-drawable window or a
resize mismatch skips the entire frame, retaining offscreen contents. As with old
Draw calls, the accepted-frame counter can increase on a skipped frame; actual draw
calls remain zero. GPU/backend failure after execution begins is not an atomic
rollback of texture contents. Keep normal error handling at this boundary.

## Straight alpha and the resolve cost

The existing sprite/material contract emits straight-alpha color and uses
source-alpha blending. Rendering it over transparency accumulates premultiplied
RGB in the private attachment. Sampling those bytes as straight alpha would apply
alpha a second time and darken the image.

After each offscreen pass, a tiny internal full-screen resolve divides attachment
RGB by alpha into the public RGBA8 image, without blending. A zero alpha texel is
defined as transparent black. Every stored positive alpha is at least 1/255, so
division does not approach a floating-point singularity. Smaller authored alpha
can quantize to zero and loses its color. Both storage steps are 8-bit UNORM;
repeated passes incur ordinary quantization, especially at low alpha. This is not
HDR, linear-light color management or a precision-preserving compositing system.
Existing linear texture sampling still applies.

The pair plus **one additional GPU draw/pass** preserves the material profile and
supports subsequent RGB/alpha tint or desaturation without reserving hidden user
parameters. A blend-state flag alone would not correct arbitrary straight-alpha
fragment calculations or alpha tint. Resolve shaders are standard GLSL in
`shaders/resolve.vert` / `.frag`; `scripts/compile_shaders.py` produces the checked-in
text header. Generated standalone resolve SPIR-V files are ignored. The previous
sprite shader bytes are unchanged.

`Stats.Frames` counts accepted frames and `Stats.Sprites` counts submitted user
quads once. `Stats.DrawCalls` includes issued world runs **and target resolves**,
but excludes UI and texture-initialization clears. No GPU timing is measured.

## Reproduction

- `scripts/test.sh quick targets`: layout, ranges, ownership, budgets, feedback,
  lifetime, recovery and warmed allocation checks using virtual headless targets
- `--target-demo`: translucent direct comparison, target resampling, alpha tint,
  and desaturation; Escape exits
- `--target-graphics-test`: capture explicit passes, retained contents, failed
  plans, target-local camera/clip, translucent clear, initial contents, resize
  replacement and UI isolation. Set `GAL_TARGET_CAPTURE_DIR`, then run
  `python3 scripts/validate-target-pixels.py <directory>`

The software fixture compares overlapping half-alpha red/blue against direct
compositing, tints the first target through a second target, and checks 1/255,
near-zero and zero alpha. Headless tests do not allocate/execute GPU textures.
Windows, Metal, hardware GPU and physical display performance remain separate
acceptance gates.
