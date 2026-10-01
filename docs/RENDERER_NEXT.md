# Remaining original renderer scope

This records the bounded renderer sequence without expanding into advanced
effects. World scissor, atlas regions, bounded diagnostics and the first material
batch are implemented. Public render targets and a basic post-process example
still remain in the original scope.

## Delivered batch: materials and shader assets

The design below is now implemented at the documented Linux software Vulkan
boundary. See [actual material contract and limits](MATERIALS.md): fixed vertex
layout, one sampler, two copied fragment vectors, 64 owned pipelines, explicit
source preparation and manifest/hash consistency checks. It does not provide
SPIR-V reflection or a sandbox. The retained design notes describe this slice;
they are not a second material implementation request.

Keep `shaders/sprite.vert` / `.frag` as the canonical standard GLSL baseline.
`scripts/compile_shaders.py` already uses the installed libshaderc offline and
emits SPIR-V; extend that existing path to named input/output assets with useful
compiler diagnostics. Do not introduce a shader language or install a new SDK.
Compilation belongs in asset preparation, not the game loop. Preserve the built-in
material for old calls and handle zero.

A small first contract can retain the current sprite vertex layout and vertex
shader, one sampled texture, a fixed-size fragment parameter block, and ordinary
straight-alpha blending. A material manifest names a logical shader asset and its
explicit binding contract. Source generation and asset-root confinement should
follow existing resource patterns. SPIR-V and custom shaders are trusted authored
assets, not a sandbox for arbitrary untrusted programs. A failed shader/pipeline
load must preserve the previous live material and report the exact asset/stage.

Use bounded context-owned material handles with generation/owner checks, explicit
release outside active frames, and cleanup before context destruction. Add an
additive versioned draw entry point rather than altering existing record layouts.
Adjacent batching must include material, texture, clip and relevant parameter
values in its key, without reordering alpha draws. Validate a complete submission
before appending it. A default shader plus a simple tint/desaturation fragment
fixture is enough to test reuse, parameters, order and failed-load retention.
Arbitrary vertex layouts, reflection-generated setters, a node editor and rich
material graph are unnecessary here.

## Following batch: explicit render targets and one post-process

Begin with bounded 2D RGBA8 targets usable as color attachments and sampled
textures. The public owner exposes a borrowed texture binding and dimensions;
context/target disposal, failed allocation and explicit recreate-on-resize rules
must be clear. Reuse typed handles and generation checks. Reject sampling a target
while it is the current attachment, stale handles and cross-context resources.

Keep passes explicit: render a scene into a target, end that pass, draw the target
through the material to the window, then render UI once. Target camera projection
and clipping use that target's dimensions, independently of window/framebuffer
density. Preserve older one-call window rendering. Bound passes and total draws,
and define frame abort behavior before adding the ABI. Account for pipelines
matching target formats; the present swapchain format is not necessarily RGBA8.

A two-pass grayscale/tint effect with actual pixel checks closes the basic
post-process use case. Verify target contents, resize/recreate failure retention,
read/write feedback rejection, UI ordering and teardown. The private screenshot
capture texture in `backend_sdl.cpp` is useful implementation context but does not
currently provide this public ownership or pass API. GPU profiling, bloom chains,
lighting, arbitrary compute effects and a render-graph framework remain outside
this batch.

Each native boundary gets focused native/managed tests, one full JIT pass and a
fresh targeted AOT/graphics milestone. Shader/backend claims remain Linux Vulkan
until Windows DXIL/D3D12 and macOS Metal assets/backends are actually implemented
and validated. These renderer batches still do not close the simple tile movement
acceptance fixture, UI selection or the [platform gates](ROADMAP_CLOSURE.md).
