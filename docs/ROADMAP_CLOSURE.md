# Original roadmap closure audit

Audit begun at the local package proof and updated after tile movement acceptance, 2026-10-01.
This is a capability/acceptance inventory, not a completion percentage or an
instruction to add every gap found in another engine.

## Delivered foundations

The project has a repeatable authored playable loop; stable identities and
explicit world/scene/behavior ownership; bounded resource leases and failure
retention; atlas regions/flips/stable ordering; frame animation/tweens/timers;
action mapping and coordinate contracts; optional bounded audio/Box2D/tilemap
modules; typed UI lists and a synthetic composition bridge; and independent
local NuGet consumers in ordinary JIT, trimmed JIT and NativeAOT modes.

These are implemented and exercised. Their documented bounds and software-versus-
physical validation distinctions remain part of the result. The package proof is
an integration milestone, not completion of the roadmap.

## Remaining original functional work

| Area | Current evidence | Smallest useful remaining work |
| --- | --- | --- |
| World rectangular clipping | Now implemented and validated separately from culling/UI; see [contract](WORLD_CLIPPING.md) | Hardware/DPI acceptance remains with the platform gates |
| Materials/shader workflow | Context-owned materials, fixed sprite vertex layout, 32-byte copied fragment parameters, standard GLSL/offline builder, source manifests and stable run keys; see [contract](MATERIALS.md) | Non-Vulkan shader tooling remains a platform gate |
| Public render targets/basic post-processing | Owned paired RGBA8 targets, bounded explicit passes, transparent alpha resolve, tint/desaturation and final-window UI; see [contract](RENDER_TARGETS.md) | Current basic software Vulkan slice delivered; no HDR, arbitrary formats, render graph or advanced effect claim |
| Debug drawing/logging/timing | Bounded line/rectangle buffer, typed log FIFO, opt-in explicit CPU frame/phase timing and native draw counters; see [contract](DIAGNOSTICS.md) | Current bounded slice is delivered; no general profiler, GPU timing or editor claim |
| Basic tile movement acceptance | [A controllable game fixture](TILE_MOVEMENT.md) now exercises flat-floor standing, both walls, jump/landing, triggers/queries, fixed-step input boundaries and repeatable restart; scripted and displayed cloud X11 checks pass | Current bounded example delivered; richer character helpers remain deferred |
| Reusable composition/resources | Synchronous BMP cache and flat authored scene loads are repeatable and isolated | Keep current limitations explicit; add only a demonstrated original composition need, with stable IDs and ownership. Async/hot-reload/prefab expansion is deferred |

The world scissor batch is now complete at its documented software-validation
boundary: framebuffer coordinates, window/world conversions, stable run ordering,
atomic validation, region/tilemap consumers and resized pixel checks are covered.
The bounded diagnostics slice is also complete at its documented CPU/software
rendering boundary. Sprite materials now extend that pipeline with bounded
ownership and offline shader preparation. Explicit paired targets and a basic
post-process fixture now exercise translucent composition and two-pass tint.
The simple tile-based movement acceptance example is now delivered. This audit
identifies no further missing feature in the agreed basic in-repository Linux capability slice;
the platform, IME, UI selection and distribution gates below stay open. This is
not a declaration that the whole cross-platform roadmap is complete.

Keep material pipelines and render targets in separate, reviewable batches rather
than one rendering redesign. Material work should reuse standard
GLSL and the existing compiler path; no new language or large SDK/compiler install
is implied by this audit.

## Acceptance and portability gates

- **Latest distribution proof:** fresh packages from `d07a6ab` pass independent
  empty/sprite/UI framework-dependent, trimmed JIT and AOT runs. Current renderer
  and diagnostic modules have positive untrimmed controls and unused-root removal
  checks; full native payload preservation is explicit. Broader usable package
  APIs are still a concrete remaining delivery item: animation/timing, audio,
  physics and TileMap currently remain internal. [A bounded exposure plan](PACKAGE_API_NEXT.md)
  can be completed autonomously with existing implementations and one combined
  external consumer; no new runtime feature or public package publication is implied
- **UI:** a targeted cloud X11 check changed both name and slider, scrolled to the
  bottom, then restored both defaults with one Reset click. A second changed draft
  after scrolling back to the top also reset with one click. This narrow sequence
  now passes; the historical miss was not reproduced and no speculative runtime
  fix was made. Explicit desktop pointer motion preceded each click, avoiding the
  previously observed window-bound tool's stale pointer state. This does not
  establish every control/platform/input-device combination
- **IME:** synthetic preedit/commit/cancellation, text-session ownership and candidate
  coordinate math pass. An actual OS Chinese IME, candidate window and selection
  remain unverified. No input-method package or OS configuration was added
- **Hardware:** cloud X11 pointer/keyboard checks and offscreen software Vulkan do
  not establish hardware GPU, high-DPI/multi-monitor, physical audio or latency
  acceptance. Focus/resize/minimize contracts have useful synthetic coverage,
  with narrower displayed checks documented per feature
- **Windows:** no physical Windows validation environment has been provisioned for
  this project. Backend/shader packaging remains work: supplied HLSL is not DXIL
  compilation or a tested D3D12 backend
- **macOS:** Metal/shader and native packaging work remain unvalidated; a Linux AOT
  publish is not a cross-OS build
- **Distribution:** the Linux native profile requires GLIBC_2.38 and the documented
  C++ ABI, plus host drivers and a separately licensed font. The local proof reuses
  prepared dependencies/toolchains, so it does not establish a fresh-machine setup
  flow. Several implemented feature APIs remain internal to the package prototype
- **UI choice:** RmlUi remains experimental. Comparing it with Myra, Gum and an
  engine-owned alternative is still a decision gate before permanent selection;
  it does not require implementing four UI systems

## Explicitly deferred expansion

The later scope decision takes precedence over earlier proposed bullets. Async
loading, hot reload, prefab overrides, richer animation tracks/transitions,
slopes/one-way/moving-platform helpers, custom shape/polygon APIs, rich camera/
parallax/multiview and expanded TileMap/editor workflows are not reactivated by
this audit. Basic movement acceptance above remains a game fixture using current
primitives, not an expanded character controller.

Particles, lights/shadows, navigation/pathfinding, skeletal animation, mass-unit
RTS/ECS/parallel scheduling, visual editor, mobile/Web and later touch/gamepad
acceptance remain gated or deferred as previously recorded. Basic post-processing
is an original renderer item; do not relabel it as deferred advanced effects.

A preliminary Godot/Unity2D inventory is useful now. The requested comprehensive
**near-completion** comparison is now reasonable alongside the remaining public package boundary:
the core batch status is explicit and desktop acceptance gaps have named blockers.
Read-only comparison and a UI candidate assessment can proceed autonomously;
permanent UI selection and actual Windows/macOS/IME/hardware acceptance still need
their respective decision or environment. The comparison must not turn newly
discovered engine gaps into automatic scope.

## Evidence pointers

- [Current roadmap](ROADMAP.md), [milestone](MILESTONE.md), [validation](validation.md)
- `native/include/gal.h`, `native/src/backend.h` and `backend_sdl.cpp`: current
  draw/run/pipeline/capture contracts
- `managed/TextureCache.cs`, `AuthoredScene.cs`, `TileMapDemo.cs` and `RoomGame.cs`:
  synchronous resources, scene loading and movement fixtures
- [UI bridge](UI_TEXT_INPUT.md), [typed bindings](UI_BINDINGS.md),
  [local package proof](NUGET_PROOF.md), [native baseline](NATIVE_PACKAGE.md),
  [diagnostics](DIAGNOSTICS.md), [remaining renderer design](RENDERER_NEXT.md)
