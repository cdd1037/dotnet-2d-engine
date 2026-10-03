# .NET 2D Engine — evaluation and frozen roadmap

Planning document created after foundation checkpoint `cf3ebe6`. These are
proposed delivery stages, not claims of implemented support or approval to install,
publish or deploy anything. Use small playable examples to validate general-purpose engine capabilities;
one sample is a starting point, not the engine feature ceiling.

<a id="evaluation-2026-10-03"></a>

## 2026-10-03 路线评估：近期游戏生产与独立引擎目标分开

**当前建议：若近期目标是低维护成本地完成游戏，优先 Godot + 薄 C# 作者层；本仓库继续冻结开发，保留独立引擎方向。** 不为此深度重写 Godot，不把已交付的复刻游戏整体迁到自研引擎，也不把新发现的缺口变成自动开工清单。

本节以[公开冻结基线 f8e334d](https://github.com/cdd1037/dotnet-2d-engine/commit/f8e334d65ae2a67ab8e5ab3819026ee5b4f11326)为准，记录代码审计和有限探针支持的评估与建议。用户随后要求归档阶段成果，并明确准备回到 Godot 魔改；这是下一工作方向，具体实现任务尚未指定，也不等于放弃自研、恢复本引擎开发或启动下面的比较实验。后文已交付阶段是历史记录，未完成阶段继续受冻结和单独审批约束。

### 已证实与未证实的收益

- **已证实：** [C# 生成式 UI 契约](GENERATED_UI_CONTRACTS.md)移除了手工字段 schema、数字命令 ID 和分派的重复声明；生成诊断能定位 RML 与 C# 声明。当前公开包的窄探针中，错误字段得到 `DUI005` 和行列位置；非法文本得到 `UI_MODEL_VALUE`、模型路径和声明位置。[类型化公开边界](PUBLIC_API_CONSOLIDATION.md)也减少了资源句柄和输入映射的误用面。
- **尚未证实：** 完整游戏开发更快、AI 总 token／上下文更少、总维护成本更低。早先[三个代码切片](CODE_AUTHORING_COMPARISON.md)和[四次 AI 维护尝试](AI_AUTHORING_EXPERIMENT.md)都不足以推出普遍效率排名；旧实验早于当前生成式契约，不能用已移除的手工注册缺点描述当前版本，也不能把新增机制直接换算成生产效率收益。
- **普通 C# 核心可移植，但收益已能在 Godot 中获得：** 已交付复刻游戏的 17 个 Core 文件、4,584 个物理行不依赖 Godot，已原样编译进普通 .NET 控制台并通过小范围规则断言。文件数和行数只说明依赖边界，**不是工作量比例或节省时间**；玩法语义、剧情时序与回归判断不会因换引擎消失。
- **普通 C# 不自动等于 AOT 就绪：** 同一游戏的反射 JSON 序列化在关闭默认反射的窄 JIT 探针中失败。这不是完整 NativeAOT 发布失败实验，也未找齐全部问题；源生成 context、显式写入或已知类型方案仍需按真实数据审核。参见 [.NET JSON 源生成](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation)和 [NativeAOT 限制](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)。

本轮没有重做整款游戏、实现配对复杂界面、测量整游戏作者工时／token，或新增完整 AOT／目标设备发布验证。这里保留的是结论摘要，不随文发布私有 Flash 素材、剧情数据、游戏资源或探针输入。

### 当前实现的取舍，不是 SDL／RmlUi 的先天弱点

当前 [UI 模型边界](UI_MODELS.md#bounds-and-resource-boundary)把一个绑定文本值限制为 255 UTF-8 字节并拒绝换行等控制字符；[文档图片清单](UI_IMAGES.md)最多允许 32 个独立路径，隐藏、hover 和动态声明也计入。这些是本引擎的当前契约／预算策略，**不是 SDL 或 RmlUi 固有地不能处理长文本或更多图片**。

真实内容已经触达边界：长中文段落和换行正文不能直接投影为单个文本值；一个界面的 14 个图标 × 3 个状态需要 42 张独立图片。可选择分块／分页、图集、预合成或未来修改契约，但这些都有适配和回归成本；本次仅记录，不批准扩展。

生成器和绑定也没有消除以下长期责任：

- 字体注册、字重、中文排版与合法分发；SVG／音频转换、图集和缓存；受限 SVG 支持不等于所有源图像可直接加载
- 动态图像、遮罩、混合层级和 UI 合成；现有[材质](MATERIALS.md)与 [RenderTarget](RENDER_TARGETS.md)是真实能力，但不等于已具备同一游戏的全部图像处理工作流
- 显式排空命令、`Dispatch`／`Apply` 顺序、owner 销毁、旧战斗身份和旧输入失效、模态焦点恢复与底层时钟；generation／revision 防护不能替代游戏规则
- 窗口、用户路径、平台原生包、真实 IME、高 DPI、物理音频和目标硬件验收；这些仍由应用／引擎维护者承担，[现有验收门槛](ROADMAP_CLOSURE.md#acceptance-and-portability-gates)未被本次文档更新关闭

### Godot 的薄作者层可以先解决什么

这里的薄层是项目外置的 C# 库、构建检查和资源工具，不是 Godot 引擎 fork，也不是重新实现一套场景／UI 运行时。下列是后续可评估的方向，尚未承诺实现或证明净省工：

1. 类型化 Node／Resource 引用、有限源生成和文件／字段／行列诊断，优先避免重复事实和易碎字符串；利用已有 [C# 导出引用](https://docs.godotengine.org/en/stable/tutorials/scripting/c_sharp/c_sharp_exports.html)
2. 轻量 UI 状态投影／绑定，配合共享 [Theme](https://docs.godotengine.org/en/stable/tutorials/ui/gui_using_theme_editor.html) 和[场景实例复用](https://docs.godotengine.org/en/stable/tutorials/scripting/nodes_and_scene_instances.html)；不强制所有固定控件都由 C# 逐个创建，也不把场景数量当作效率指标
3. 明确生命周期、订阅清理、模态、输入和时钟策略；Godot 的 [C# 信号](https://docs.godotengine.org/en/stable/tutorials/scripting/c_sharp/c_sharp_signals.html)有捕获变量 lambda／自定义信号等需显式清理的边界，不能承诺自动消除旧回调问题
4. 资源校验、转换和缓存工具，结合已有 [CLI 导入与导出](https://docs.godotengine.org/en/stable/tutorials/editor/command_line_tutorial.html)，使构建失败可定位且可复现

真正涉及运行时、底层渲染、原生平台或导出工具链的缺口应另列、另验收；不能声称薄层能解决全部底层问题，也不应因此默认深改 Godot。以上官方文档只说明可利用的能力，不是本项目效率提升的测量证据。

### 什么目标仍可能值得做独立引擎

若产品目标本身是 .NET-first 的宿主、可控 NativeAOT／trim 边界、窄原生 ABI、小型分发包与可检查的作者契约，自研依然有独立价值。[现有包证明](NUGET_PROOF.md)和[包大小记录](MILESTONE_PACKAGE_SIZES.md)支持各自限定环境下的技术结果；它们不证明同等完整游戏比 Godot 更省作者时间。包大小须含原生库、字体和资源，平台集成与长期维护也必须计入。

因此，“保留自研作为产品方向”和“近期使用成熟引擎降低游戏生产维护负担”可以同时成立。是否恢复自研以及恢复哪一段，需要用户另行决定；本节不撤销项目，也不扩展当前范围。

### 若以后验证，只比较一个真实复杂 UI 屏幕

这是一项**待单独批准**的有界方案：选择同一个真实复杂界面，例如含长说明、14 个三态图标和保留底层状态的模态弹层的法术参考／修炼屏。先固定行为、素材范围和像素容差；Godot 侧允许合理的场景／Theme 复用，自研侧仅用当前公开包。不得为比较迁移整款游戏、继续追未确认的原作细节或把内部 API 当成作者能力。

记录四类结果：

- 修改面：完成同一状态／字段／交互修改要碰哪些独立文件、声明和规则，是否必须修改引擎
- 资源适配：长文本、字体、图片状态、SVG／音频和图集需要哪些转换与特例；把管线及缓存维护算入总账
- 生命周期与输入：关闭／重开、模态焦点恢复、底层时钟、过期输入与旧对象命令是否通过实际交互回归
- 实际导出：在约定目标上真正导出、启动、加载资源并重走该屏交互；区分 JIT、trim、AOT、软件渲染和目标设备证据，不能用编译成功代替导出验收

达到同一屏行为、文本可读、资源完整、旧输入无效和约定导出验收后即停止，报告结果；若关键能力要求解冻引擎或扩大平台范围，先记录阻塞并请求决定。之后如需判断作者成本，再对这一屏的同一小修改做配对试验，记录设置／调试／验证成本和可取得的 token／工时。一次屏幕实验仍不能推出整游戏或普遍维护优势。

## Current milestone closure (2026-10-02)

The agreed basic Linux capability slice is delivered at its documented CPU and
software-Vulkan boundaries. Recent additions are [raster UI images](UI_IMAGES.md),
[optional static SVG](UI_SVG.md), [camera follow](CAMERA_FOLLOW.md),
[polled frame markers/clip selection](ANIMATION_TIMING.md),
[transactional runtime TileMap edits](TILEMAP.md), and
[capsule shapes/exact circle and rotated-box overlap](PHYSICS.md).
They extend the existing resource/input/atlas, clipping/material/target,
audio/physics, typed-binding and bounded-diagnostics foundations.

The existing independent feature fixture now has a small
[combined author smoke](AUTHOR_SMOKE.md): frame markers edit a floor, exact queries
and a real capsule observe the same collision change, the camera follows, and a
typed UI with raster/SVG artwork renders the result. Current
[Sprite/UI package sizes and trimming](MILESTONE_PACKAGE_SIZES.md) are measured
separately from that SVG-on source-profile smoke. The default native package
continues to disable SVG; optional SVG overhead is identified explicitly.

This closes the approved feature wave. Pause feature implementation here; do not
turn historical proposals or newly discovered gaps into automatic scope. The
remaining decisions are platform/device acceptance, real OS IME, permanent UI
choice and broader distribution, as enumerated in the [closure audit](ROADMAP_CLOSURE.md).
Advanced effects, async/hot-reload/prefab expansion, richer controllers, editor,
mobile and Web remain deferred. The stages below retain the longer-term goals and
acceptance criteria; they are not a new implementation queue.

### Historical, completed authoring iteration

The later approved authoring sequence covered these bounded tasks, in order:

1. Close the [generic UI model/event bridge](UI_MODEL_VALIDATION.md)
2. Prove [C# composition reuse](CSHARP_COMPOSITION.md) with parameterized typed
   factories, nested instances, rollback and aggregate cleanup. The reusable
   engine addition is destruction-only registration; factories remain game code
3. Use the [checked explicit loop recipe](GAME_LOOP_RECIPE.md) for input/pause,
   restart and physics-to-display wiring, preserving caller-visible timing,
   ownership and game rules; no new loop framework is justified by the examples
4. Compare pure-code authoring with Godot after those improvements; exclude
   editor and visual-tooling differences from that comparison

All four are implemented at their documented validation boundaries, including
the [pure-code comparison](CODE_AUTHORING_COMPARISON.md). The loop stage adds a tested
recipe and presentation history, not a measured LOC or authoring-speed reduction.
The later typed UI command/diagnostic iteration and the [public-package RELAY
experiment](RELAY_REFERENCE.md) are also delivered as bounded authoring stages.
RELAY retains its original game/save rules and runs the whole flow outside the
checkout with ordinary PackageReferences. It remains an experiment; additional
package identity/version-management work is deferred. None of
these stages authorizes broader deferred capabilities or public package
distribution, and the sequence does not revive prefab inheritance.

## Two complementary development tracks (conditional on future approval)

If development is separately reopened, retain these two evidence-driven tracks.
Neither is an active work queue while the engine is frozen:

1. **Real projects reveal necessary engine capabilities.** Build and maintain
   complete games against the public package boundary. Use actual ownership,
   interaction, data and validation failures to identify the next useful engine
   capability; do not turn a one-off game's rules into a generic subsystem
2. **API, diagnostics and tooling reduce authoring friction.** Remove duplicated
   facts, hidden change dependencies and easy misuse. Evaluate clear contracts,
   local change reasoning and actionable errors, rather than file count, shortest
   code or moving all code into one file

A real project can expose a missing capability or a confusing authoring contract;
API/tooling work must then be checked in that project and independent consumers.
Neither track automatically takes priority forever or licenses unrelated feature
expansion. Preserve explicit timing, ownership, serialization and failure policy.

### Completed author-API phase after RELAY closure

The six individually verified author-API batches below are complete. A subsequent
approved consolidation removes superseded public entry points, while retaining
internal ABI regression coverage; see [the current boundary](SAFE_AUTHORING_BOUNDARY.md).

1. Automatically assign UI command IDs. **Implemented and consolidated:** typed
   `On` and generated command methods use deterministic session-local IDs. Explicit
   numeric registration is internal protocol coverage; stale dispatch remains checked.
2. Make C# declarations the single source for UI registration and build-time RML
   name/type/command contract checks, with source-aware diagnostics and runtime
   fallback for dynamic content. Constants alone do not solve this. Bounded
   generated registration/build checks are now in scope, superseding the earlier
   general generator deferral only for this contract. **Implemented:**
   [opted-in C# DTO/handler generation](GENERATED_UI_CONTRACTS.md), shared static
   preflight and source-aware build diagnostics; dynamic proof limits stay explicit
3. Provide safer public rendering/input surfaces and typed resource handles,
   keeping raw/native representations internal. **Implemented:**
   [managed sprite/pass descriptions, borrowed typed handles and copied input views](SAFE_AUTHORING_BOUNDARY.md);
   native layouts remain unchanged and ABI-shaped managed entries are internal
4. Prefer input action tokens over manually allocated action bits on the ordinary
   author path, while preserving the existing implementation's semantics. **Implemented:**
   map-allocated tokens, typed queries/rebind and identity-preserving edge helpers;
   starter and its pause-menu use them, with raw/UI/focus policy kept explicit
5. Add an explicit UI initialization/publication helper. **Implemented:**
   `StageAsset(initialModel)` preserves failed-load/reload retention and caller-visible
   update/render ownership; source-only public initialization is removed.
6. Add semantic Circle/Box shape factories. **Implemented:** public construction
   names dimensions; the generic geometry constructor is internal.

Maintained consumers were migrated and targeted negative tests were run;
package/AOT evidence remains limited to the linked contract-specific proofs.
That completed approval did not include a Vue-style component runtime, editor, automatic
scene lifecycle or broader platform/distribution work.

The numbered stages below preserve the original proposals and exit criteria.
Use the current status above and linked validation reports when historical wording
describes work that has since landed; this list is not an automatic work queue.

## 1. Turn the sample into a repeatable small game

Goal: extend the current two-room/pickup/save/menu slice into a representative
title → start → play → pause → win/lose → save → reload → restart loop. Move suitable authored content
out of hard-coded sample logic without prematurely designing a full prefab system.

Exit: the same authored content loads twice without shared mutable runtime state;
scene transitions, ownership cleanup, save/reload and restart are reliable; an
invalid resource reports its source location and leaves the last usable state.
Use focused tests during iteration and one full JIT pass at the feature boundary.

## 2. Make the UI useful beyond the settings probe

Goal: small explicit C# model/event contracts for changing text, dynamic lists,
inventory selection, focus and gameplay-driven UI, with controlled teardown/reload.
Keep AI data edits testable through file/field/source-position diagnostics. Reproduce and
resolve the intermittent first Reset after scrolling before calling input robust.

Exit: the playable slice drives UI state and list changes without native callback
reentrancy, stale events or leaked attachments. Validate text entry/IME on a real
target desktop. Compare RmlUi, Myra, Gum and an engine-owned option against this
actual use case before making a permanent framework choice.

## 3. Consolidate assets, builds and distribution

Goal: predictable resource paths, asset validation and repeatable build/publish
commands using explicit tool versions, lightweight defaults and only the minimal
real audio/input needs demonstrated by the game. Separate reusable runtime concerns from
test/probe payloads when justified by real shipping needs, not cosmetic size goals.

Exit: a clean machine with documented prerequisites builds the sample; a package
runs without this workspace's sibling directories; dependency/font notices and
missing-resource diagnostics are complete. Measure the entire distribution,
including native libraries/assets/fonts, independently of the host executable.
Keep large differential/size experiments opt-in.

## 4. Validate desktop platforms incrementally

Goal: validate Windows first, then macOS incrementally, beyond cloud software
Vulkan. macOS requires its own Metal/shader and native packaging work. Choose
and document the Windows graphics backend and shader workflow rather than
assuming the existing HLSL file establishes D3D12 support.

Exit: gameplay/UI/save/reload, resize, focus loss, minimize/restore, repeated input,
audio and error recovery pass on physical hardware. Record exact drivers/platforms
and distinguish functional checks from performance measurements. Revisit native
validation boundaries once the managed contracts are stable.

## 5. Evaluate mobile; keep Web and editor gated

Android/iOS begin later with a small feasibility slice covering application lifecycle,
touch, graphics backend, text/IME and AOT/package restrictions; it is not a desktop
port support claim. Decide supported devices and acceptance criteria from results.

Web remains a separate feasibility decision pending the SDL/browser graphics
backend and .NET runtime/toolchain path. Do not promise that desktop NativeAOT
transfers directly to Web.

A visual editor comes later, using the same canonical IDs, resources, schemas and
validated operations as files/CLI. Blazor, engine-self-hosted and hybrid approaches
remain open. Define versioned metadata and lossless round-tripping before an
editor adds a second authoring surface. A Godot fork is outside this project.

## Engine capability track (alongside the stages above)

The stages above describe integration, authoring and delivery. They do not replace
building reusable 2D engine functionality. Phase 1 and the agreed focused resource/input/rendering
feature wave are complete at the documented validation boundaries. The following remains a historical
capability roadmap, not a dependency selection or permission to resume implementation.

### A. Resources, scenes, rendering and animation

- General resource cache and explicit lifetime, scene transitions and reusable
  entity compositions with stable identity. Async loading, hot reload and prefab
  overrides were explicitly deferred in the later scope decision; this older
  capability list does not reactivate them.
- Sprite atlases, UV regions, cameras, clipping and stable draw order; materials,
  standard shader authoring/build workflow, render targets and basic post-processing
- Sprite-frame animation and tweening, with explicit update/pause/lifetime rules

Existing sprites, camera, transforms, sorting and flat scene JSON are foundations,
not evidence that the complete capabilities above exist. Exit: at least two small
examples with different content reuse the same APIs without sample-role hardcoding;
resource failure/reload and animation teardown remain predictable.

### B. World construction, physics, input and sound

- Tilemaps and level data, including tile collision generation
- Collision queries, triggers, character movement and rigid bodies; evaluate a
  mature 2D physics library before implementing a general solver ourselves
- Input actions, remapping and focus/capture; touch and gamepad support later,
  aligned with the platform schedule (not added to phase 1)
- Real sound effects and streamed music, voice lifetime and volume groups
- Reusable timers, save integration, debug drawing, logging and basic profiling.
  The current [bounded diagnostics](DIAGNOSTICS.md) slice supplies line/rectangle
  geometry, a structured log FIFO and opt-in CPU frame/phase timings

The earlier AABB/legacy-tone samples remain separate from the now implemented
bounded Box2D and SDL_mixer foundations. Those modules do not themselves satisfy
the actual-device acceptance below. The [tile movement fixture](TILE_MOVEMENT.md) now
passes scripted solver/rendering and displayed cloud X11 controls, including fixed-step
input boundaries, wall/floor collision, triggers and restart.
Exit: a tile-based movement example and a different interaction/physics example
exercise shared APIs, and input/audio work on an actual target device. Resource
and update ownership must remain explicit across pause, scene changes and restart.

### C. Effects and specialized capabilities — deferred

User decision (2026-10-01): defer this entire enhancement group. Initial scope
targets ordinary top-down, side-scrolling action and platform games; mass-unit RTS
and large simulation/management games are not initial priorities. Do not add ECS
or parallel scheduling solely for those deferred workloads.

Later, evaluate particles, 2D lights/shadows, navigation/pathfinding and skeletal animation
against concrete game needs. Prefer suitable maintained dependencies where they
reduce total maintenance. Do not promise complete Godot feature parity or create
large abstract frameworks before the required semantics are understood.

### Ordering and integration

After the playable phase-1 loop, progress resource/scene and render/animation work
alongside UI/data authoring. Then add tile/world/physics and input/audio capabilities,
while improving project/export tooling. Desktop validation should accompany each
relevant capability rather than wait until every feature exists. Advanced effects,
mobile, Web and editor work retain their separate gates. Use multiple small examples
to avoid designing the entire engine around one game.

## Working constraints across all stages

- Scope small edits to quick compile and related CPU checks
- Full JIT once per feature batch; AOT/graphics when the affected boundary needs it
- One necessary final aggregate per milestone; preserve evidence provenance
- No releases, new package-feed publication or new platform-support claims by implication
- No full ECS, generic physics framework or editor expansion without a concrete
  need demonstrated by representative game examples

## Local NuGet packaging proof

The [local-feed proof](NUGET_PROOF.md) separates the managed runtime from the demo/
test host and validates independent empty/sprite/UI PackageReference consumers in
framework-dependent, trimmed JIT and AOT modes. It uses ordinary NuGet/MSBuild.
Native profiles, assets, notices and loader dependencies are measured separately;
prebuilt native functions are not removed by managed trimming.

This completes the bounded local proof, not distribution/platform acceptance.
Raw interop/probes and selected implementation helpers remain internal.
Clean-machine setup, Windows/macOS native packages and real
device acceptance remain open. The [public optional-module boundary](PACKAGE_API_NEXT.md)
now makes existing animation/timing, audio, physics and TileMap usable by an
independent JIT/AOT consumer; this remains an experimental API. Public NuGet publication and a custom SDK are not
part of this proof. Source generation remains explicit; no blanket assembly roots
or trim/AOT warning suppression were introduced. See the [closure audit](ROADMAP_CLOSURE.md)
for remaining functional work and named validation limits.

## Deferred comparison

The earlier comprehensive Godot/Unity 2D capability comparison remains a deferred
proposal, not scheduled work. The 2026-10-03 evaluation above is the current
decision input; it does not reopen implementation or approve a new experiment.
