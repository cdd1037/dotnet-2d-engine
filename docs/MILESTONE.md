# .NET 2D Engine — current milestone

**2026-10-03：开发保持冻结。** [最新路线评估](ROADMAP.md#evaluation-2026-10-03)建议近期低维护成本游戏生产优先 Godot + 薄 C# 作者层，同时保留独立引擎方向。生成契约／诊断已有有限验证，整游戏速度、token 和维护优势没有测量结论。用户随后明确下一方向回到 Godot，并要求保留阶段成果；具体魔改任务尚未指定，本引擎没有恢复开发，也没有启动新的 UI 比较实验。下文保留历史里程碑。

## 2026-10-03 阶段成果与归档边界

- **已交付基础：** 独立 .NET 10 宿主、窄 C ABI／SDL3 GPU 层、显式 World／owner／资源寿命、输入视口、精灵与图集、材质／RenderTarget、动画／时钟、TileMap 和可选音频／Box2D。能力与验收门槛见[闭环清单](ROADMAP_CLOSURE.md)，不等于全平台或所有游戏能力完成
- **作者入口已收敛：** [生成式 UI 契约](GENERATED_UI_CONTRACTS.md)、自动命令 ID、类型化绘图／资源句柄与输入 token、`StageAsset(initialModel)` 原子初始化和 Circle／Box 语义工厂；旧公开入口已移除或转为内部回归覆盖，见[当前公开 API](PUBLIC_API_CONSOLIDATION.md)
- **已有工程验证：** RELAY 完整可玩流程使用外部普通 PackageReference；独立消费者有 JIT、trim、NativeAOT 证明，CPU 合同和部分软件 Vulkan／云端显示输入、像素验证均按对应报告保留。它们证明限定环境下的机制与集成，不证明 AI 更快、token 更少或完整游戏总成本更低
- **尚未验收：** 真实中文 IME、目标硬件／高 DPI／物理音频、Windows／macOS 原生后端与包、干净机器分发、稳定 SDK／公开 NuGet 发布；当前字体、资源转换、合成、生命周期和平台集成仍有维护责任
- **继续冻结／暂缓：** 不自动补齐新游戏暴露的缺口，不扩展异步／热重载／prefab、高级特效、编辑器、移动／Web，也不整体迁移已交付复刻游戏；一个真实复杂 UI 屏幕比较仍是[待批准方案](ROADMAP.md#evaluation-2026-10-03)
- **后续方向：** 用户已明确准备回到 Godot。优先在 Godot 外围评估薄 C# 作者层；具体魔改／底层导出问题另行定义，归档不触发实现

本次归档保留源码历史、文档和重建脚本；离线消费所需本地包、必要已生成 shader／样例资源单独说明来源。工具链、依赖源码缓存、重复验证副本不代表新增交付能力。本次没有重跑重型引擎测试或产生新的平台验收结论。

Current full-game experiment: [RELAY with public PackageReferences](RELAY_REFERENCE.md).
The historical milestones below retain their original validation and scope.

Status: experimental independent engine, not a production release. The repository
name is `dotnet-2d-engine`. Source directories, `GameAuthoringLab`
managed names and the `gal` native ABI remain unchanged to avoid a disruptive
rename during integration.

## Current closure (2026-10-02)

The approved basic feature wave is closed; further feature implementation pauses
here. Recent delivered work includes static raster/UI SVG, camera follow, bounded
frame-entry events, transactional runtime TileMap cell/collision updates, capsule
shapes and exact circle/rotated-box queries. See the [current roadmap](ROADMAP.md)
and [closure audit](ROADMAP_CLOSURE.md) for what remains gated.

The [existing author fixture](AUTHOR_SMOKE.md) combines those features in a small
three-state rendered scenario. The [small-consumer package refresh](MILESTONE_PACKAGE_SIZES.md)
separates full uncompressed trimmed-JIT/AOT output, managed trimming, unchanged
native-profile content and optional SVG overhead. SVG remains off in the default
native package. Earlier follow-on sections below are historical descriptions;
current verification/provenance is linked above and in [validation](validation.md).

## What is here

- `engine/`: reusable .NET 10 runtime project and explicit linked source list
- `managed/`: runtime source files plus the separate demo/test executable, gameplay
  and game progress persistence
- `packaging/`: independent PackageReference fixtures and native package preparation
- `native/`: C++17 platform/rendering implementation and a narrow versioned C ABI
- `assets/`: generated BMP sample art, authored JSON scene and optional RML/RCSS UI
- `shaders/`: standard shader sources and offline-compiled SPIR-V
- `tests/`: native contracts and C ABI consumer
- `scripts/`: build, asset generation and validation entry points
- `docs/`: architecture, decisions, reproducible commands and milestone findings

Downloaded tools/dependencies, binaries, caches and raw captures/logs remain local
and are ignored by Git. Existing local evidence is retained, not deleted. No full
Chinese font is bundled. Generated shader blobs and small sample art are deliberate
runtime inputs, not accidental build outputs.

## Settled for this milestone

The host is C#/.NET with a C++ SDL3 GPU backend. Stable world identity, transform
parentage, lifetime ownership and scene membership are separate concepts.
Lightweight components do not imply a full ECS. Behavior attachments have explicit
ownership and cleanup; sprite extraction uses stable merge sorting.

Official System.Text.Json source generation handles save snapshots. Flat authored
scene JSON has a separate loader and validation contract; it is not a save-file
schema or a prefab inheritance system. Files and CLI operations are canonical for
AI authoring and future visual tools.

Optional RmlUi is an experiment behind `GAL_ENABLE_RMLUI`; selecting the final UI
framework remains open, including Myra, Gum and an engine-owned alternative.
See [architecture](architecture.md), [world model](../managed/WORLD.md),
[authored scenes](AUTHORED_SCENES.md), [ownership and sorting](LIFECYCLE_SORTING.md),
[two-room gameplay](TWO_ROOM.md) and [combined gameplay/UI](GAMEPLAY_UI_LIFECYCLE.md).

## Build and verification

Start with the headless commands in the [README](../README.md). Headless contract
checks do not establish graphics, audio or input correctness. The optional graphics
and UI scripts require the explicitly materialized dependencies described in
[dependencies](dependencies.md) and [UI prototype](UI_PROTOTYPE.md); they are not a
one-command fresh-machine installer.

Current final regression results and executable measurements are maintained in
[validation](validation.md). Earlier reports retain stage-specific numbers and
must not be interpreted as the final aggregate executable. NativeAOT executable
size excludes native shared libraries, assets, fonts and other shipping files.
Local experimental packages were rebuilt for the documented proofs; no public package release is implied.

## Known limits and next decisions

- Linux x64 software Vulkan is the validated rendering environment; physical GPU,
  Windows/D3D12 and macOS/Metal are not validated
- A targeted displayed-window Reset-after-scroll check now passes with explicit
  desktop pointer motion; the earlier miss was not reproduced. Real IME,
  accessibility and target-device input still require validation
- Mobile, Web and editor are deferred; the bounded Box2D core is implemented,
  while richer controllers and specialized physics/gameplay systems are not
- UI selection and deployment footprint remain open; full Chinese font packaging
  must be decided separately
- Revisit native-layer validation and platform coverage after the engine model
  stabilizes

The source milestones and local package proof remain experimental. They do not
establish a public package release or additional platform support. See the
[current closure audit](ROADMAP_CLOSURE.md) before treating any historical
follow-on section below as an outstanding task.

## Proportionate test policy

Keep the development loop small. A local change should compile and run its related
focused checks first. Run the complete JIT suite once per feature batch. NativeAOT
and graphics checks are targeted work when interop, serialization, trimming,
rendering or publishing changes; run the necessary aggregate once before a
milestone commit. Reuse verified final-tree results instead of rerunning the same
expensive suite merely to organize documentation.

Large differential/fuzz corpora and binary-size ablation experiments are opt-in
research. They are not the default development or CI loop. A headless pass and a
software-rendered pass must always remain clearly distinguished.

## Phase-1 playable follow-on

The separate `--game-demo` now delivers a repeatable 90-second two-room mission
with authored mission data, explicit RmlUi game screens, pause/focus/input gates,
win/loss, transactional checkpoint replacement and restart cleanup. Start with
[RELAY controls and run instructions](PLAYABLE_MISSION.md); the original room and
settings probes remain available. Final phase-1 counts are recorded in
[validation](validation.md). This does not select RmlUi permanently or broaden the
project into a generic binding, physics, ECS or editor framework.

## Resource follow-on

[Resource roots and texture leases](RESOURCES.md) now provide shared authoring path
validation and a context-owned synchronous BMP cache with explicit per-world
leases. Stable authored keys remain independent of file mappings. Failed candidate
loads keep the prior usable resource set; repeated synchronization remains
allocation-free. Final focused/full-JIT verification is recorded in
[validation](validation.md). Asynchronous loading and general asset packaging remain separate capability
batches; atlas UVs are covered by the later region follow-on below.

## Input and viewport follow-on

The [additive input/viewport contract](INPUT_VIEWPORT.md) now separates logical
window coordinates from framebuffer pixels, preserves event-derived short taps,
routes UI-owned inputs and supports copied/remappable managed bindings. The
interactive mission uses it while preserving fixed-step, focus and neutral gates.
Fresh JIT/AOT and queued SDL/Rml checks are recorded in [validation](validation.md).
World scissor and sprite-frame animation remain rendering work;
RmlUi's existing list scissor is not a world clipping API.

## SDL dependency follow-on

The [isolated SDL 3.4.16 upgrade](SDL_UPGRADE.md) is verified against native,
JIT/AOT, software-rendered and focused displayed-window checks. The older install
and pre-upgrade native libraries remain locally available for rollback. Runtime
version checking prevents accidental use of the retained older SDL. Image
libraries/formats are unchanged; audio integration remains a separate next batch.

## Texture-region follow-on

The [additive region draw contract](TEXTURE_REGIONS.md) now supports validated
shared-texture cells, per-sprite X/Y flips and strict authored v2 metadata. Resource
IDs remain independent of atlas locations and snapshots carry no texture handles
or packed coordinates. Stable ordering and existing full-texture pixels are
preserved. Native, fresh JIT/AOT and software pixels pass as recorded in
[validation](validation.md). The approved minimal SDL_mixer integration can proceed
on the verified SDL 3.4.16 baseline. World clipping and sprite-frame animation stay
as focused rendering follow-ons.

## Audio follow-on

The optional [SDL_mixer module](AUDIO.md) now provides bounded WAV/Vorbis clips,
reusable voices, seekable music streams, master/music/SFX gains and explicit
scene/context cleanup. Offline checks verify actual PCM; dummy-device and displayed
controls are labeled separately from physical speaker/latency acceptance. Generated
audio fixtures stay out of Git and have a verified source generator. The native
module is CMake opt-in and opens no mixer/device until requested. Final verification
and consistent-build footprint comparisons are recorded in [validation](validation.md).

## Physics follow-on

The optional [Box2D foundation](PHYSICS.md) provides fixed-step static/dynamic/
kinematic bodies, circle/box/capsule shapes, filters/sensors, copied begin/end events,
bounded closest-ray/broad-phase AABB and exact circle/rotated-box queries. Units and transform authority are
explicit; scene ownership uses existing teardown hooks. The strict source-generated
fixture and tests do not establish a full character controller, rich authoring
workflow or cross-platform determinism guarantee.

## Animation and timing follow-on

Managed [frame players, typed tweens and polling timers](ANIMATION_TIMING.md) now
share explicit game/real clock steps and bounded lifetime ownership. Atlas frame
keys remain independent of physical packing; optional retained keys prevent lease
churn. The sample and pixel checks cover pause, cancellation, restart and one-shot
retention. This managed-only batch leaves native ABI and serialization roots
unchanged; its current evidence is JIT/native contracts/software pixels and a
displayed cloud-window check, not a fresh AOT milestone.

## TileMap follow-on

The managed [basic TileMap](TILEMAP.md) adds strict source-generated orthogonal
grids, stable atlas IDs, layer order, visibility culling, fixed placement and
optional merged static box collision. It reuses existing native drawing/physics
ABIs and explicit scene cleanup. Frame capacity, physics capacity and unit limits
remain separate checked contracts; richer terrain/controller/editor features
remain outside this slice.

## UI text-input follow-on

The [UI ownership/text bridge](UI_TEXT_INPUT.md) now preserves selected text through composition cancellation, maps candidate geometry to window coordinates, isolates staged focus and retires contexts safely. Scripted composition is distinguished from real OS IME acceptance. The subsequent [typed-binding/list API](UI_BINDINGS.md) adds explicit model projections, stable row IDs and generation/revision-guarded copied actions.

## Typed UI binding follow-on

The reusable [bounded binding profile](UI_BINDINGS.md) supports text, text input,
checkboxes, integer ranges, buttons and dynamically replaced lists. Inventory and
roster fixtures use different IDs/models without new native profile branches.
Unchanged model batches skip interop; invalid projections and snapshots preserve
the live document. UI ownership and composition lifetimes remain explicit.

## Local package follow-on

The [NuGet proof](NUGET_PROOF.md) separates the runtime assembly from demo/test
code and validates independent local-feed PackageReference consumers. Trimming
removes unused managed modules; the deliberately full native profile stays intact.
The measured outputs distinguish app code, native dependencies, assets and .NET
runtime files. This remains a prerelease Linux x64 proof with no remote publishing.

## World clipping follow-on

[World scissor rectangles](WORLD_CLIPPING.md) now provide explicit framebuffer
clipping for region draws, including per-draw or broadcast state and safe coordinate
helpers. Pixel tests distinguish world clipping from tile culling and UI scissor;
legacy entry points remain unclipped and ordering stays stable.
