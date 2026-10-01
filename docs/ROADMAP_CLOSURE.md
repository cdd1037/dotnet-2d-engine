# Original roadmap closure audit

Audit of the implemented source through the local package proof, 2026-10-01.
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
| World rectangular clipping | TileMap visibility culling and RmlUi's own scissor exist; `gal.h` has no world clip contract and `DrawRun` keys only by texture | Explicit framebuffer clip rectangles, stable draw order across clip changes, bounds/resize/empty behavior and two consumers |
| Materials/shader workflow | One fixed Vulkan/SPIR-V sprite pipeline; built-in GLSL is compiled by `compile_shaders.py`; HLSL is an unwired future path | A bounded material contract using existing standard GLSL and existing tooling, then a separate measured render-target/basic post-process slice |
| Public render targets/basic post-processing | Diagnostic screenshot capture owns a private offscreen texture | Reusable target ownership and explicit pass use; screenshot internals do not count as this API |
| Debug drawing/logging/timing | Error strings, module state, cumulative frame/draw/sprite/tone counters and test Stopwatches | Small debug lines/rectangles, structured diagnostics and opt-in CPU frame-stage timing; counts alone are not a profiler |
| Basic tile movement acceptance | TileMap collision generation, Box2D bodies/queries and a falling-ball fixture work; RoomGame movement is sample logic | A simple tile-based character game fixture exercising the shared physics/input primitives, without inventing a new engine controller framework |
| Reusable composition/resources | Synchronous BMP cache and flat authored scene loads are repeatable and isolated | Keep current limitations explicit; add only a demonstrated original composition need, with stable IDs and ownership. Async/hot-reload/prefab expansion is deferred |

The next compact batch is **world scissor/clipping**. It should define framebuffer
coordinates separately from world/camera conversion, preserve transparent ordering,
validate an entire submitted batch before mutation, and test region sprites plus a
tilemap/second consumer at resized viewports. No new dependency is required.

A separate diagnostics batch can follow. Do not combine clipping, timing, material
pipelines and render targets into one redesign. Material work should reuse standard
GLSL and the existing compiler path; no new language or large SDK/compiler install
is implied by this audit.

## Acceptance and portability gates

- **UI:** the historical first Reset after scrolling was not conclusively closed.
  Later list/scroll and explicit desktop-pointer checks passed, but they are not a
  reproduction of that exact sequence. Window-bound synthetic clicking can leave
  SDL's pointer motion state stale; distinguish that tooling behavior in the next
  targeted Reset check. Do not silently declare the original issue fixed
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
**near-completion** comparison should follow explicit delivered/deferred status
for the remaining core batches and concrete results or named blockers for desktop
acceptance. It must not turn newly discovered engine gaps into automatic scope.

## Evidence pointers

- [Current roadmap](ROADMAP.md), [milestone](MILESTONE.md), [validation](validation.md)
- `native/include/gal.h`, `native/src/backend.h` and `backend_sdl.cpp`: current
  draw/run/pipeline/capture contracts
- `managed/TextureCache.cs`, `AuthoredScene.cs`, `TileMapDemo.cs` and `RoomGame.cs`:
  synchronous resources, scene loading and movement fixtures
- [UI bridge](UI_TEXT_INPUT.md), [typed bindings](UI_BINDINGS.md),
  [local package proof](NUGET_PROOF.md), [native baseline](NATIVE_PACKAGE.md)
