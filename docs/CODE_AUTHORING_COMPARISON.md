# Current pure-code authoring comparison

2026-10-02 · .NET2D `a49d2ff` · official Godot **4.6.3 .NET**

**2026-10-03 补充：** 本文保留当时三个切片的测量边界；当前公开 API 已完成[生成契约](GENERATED_UI_CONTRACTS.md)与[接口收敛](PUBLIC_API_CONSOLIDATION.md)，不能把旧手工 schema／数字命令接线当作当前缺点。机制改进也不等于整游戏效率已获验证。[最新路线评估](ROADMAP.md#evaluation-2026-10-03)建议近期采用 Godot + 薄 C# 作者层、保持本引擎冻结并保留其独立产品价值；用户随后明确准备回到 Godot，但尚未指定具体魔改任务，也未批准恢复本引擎开发。

## Conclusion

The generic UI bridge, entity cleanup/C# factories and explicit game-loop recipe
remove concrete earlier limits. Rich nested cards no longer need a text-row
workaround; repeated instances can own actual native resources; the loop has a
checked starting policy. They do **not** establish generally less application
wiring than Godot.

Both engines implement the three bounded game slices. Godot still supplies more
ordinary action-game machinery through character, node, scene-tree and interpolation
APIs. .NET2D makes clocks, ownership and native boundaries easier to trace directly,
but the author also maintains more of those connections. This is source/execution
evidence, not measured human productivity, AI tokens/speed or a universal winner.
Visual-editor/tooling differences were explicitly excluded.

A later [four-attempt AI maintenance experiment](AI_AUTHORING_EXPERIMENT.md)
records first-candidate correctness, observed task windows and build/setup friction.
Both engines passed both tasks; its small sample and confounds do not support a
general AI productivity ranking. This is separate from the source totals below.

## Matched tasks and findings

| Slice | What was actually exercised | Source-level result |
|---|---|---|
| Nested inventory | Four cards in two sections, nested stats, Inspect/Use, exact 64-bit keys, stock, pause, save/load, reverse/shrink/restart | RML expresses the repeated structure directly; explicit schema, command/revision and Apply/Draw contracts remain. Godot uses code-built Controls, typed handles and explicit refresh/reorder |
| Platformer | Same level/capsule/movement, shared 100 ms coyote/buffer rule, pause/restart, authoritative motion and display interpolation | .NET2D owns the loop/helper, ground rays, units, pose copy and display history. Godot uses CharacterBody2D, physics callbacks, Camera2D and native interpolation |
| Top-down | Two independent room/enemy/weapon factory graphs, key/door/timed attack, rollback and 12 teardown cycles | OnDestroy safely owns actual bodies, FramePlayers and subscriptions. Both use ordinary typed factories; Godot child ownership removes more native teardown wiring, while external C# events still need cleanup |

Equivalent behavior here does not mean identical physics or pixels. Platformer
jump rise was 127.000 versus 132.001 px against the same 128 px target/tolerance;
solvers/ground classification differ. Top-down uses different query/animation
scheduling; Godot also retains a small HUD and blade rotation track. Slopes,
moving platforms, high-speed edge cases and visual parity are not established.

The existing rules were retained where useful. Rich UI adds requirements; platformer
scene values and top-down tile/scene content deliberately move into C# composition.
These rewrites are disclosed and cannot be credited as line savings from the three
runtime/recipe stages. Pure-code scope permits text scenes/data; it does not require
converting all data to C#.

## Honest source cost

Nonblank physical lines include braces/comments/imports. Each standalone side
includes shared rules/data/config once, all factories/copied helpers, schema,
markup/style, bootstrap/config and asset bytes. Generated engine/SDK code and test
harnesses are separate; fewer lines/files is not an ease-of-use measurement.

| Complete current authored slice | .NET2D lines | Godot lines | .NET2D bytes | Godot bytes |
|---|---:|---:|---:|---:|
| Nested UI | 376 | 345 | 18,932 | 18,538 |
| Platformer | 354 | 226 | 14,707 | 8,466 |
| Top-down | 472 | 406 | 20,931 | 17,905 |

- UI schema/commands: 44 lines; Godot UI helpers: 36, retained in its 180-line adapter
- FixedStepInput: 65 lines per copied gameplay project, counted in both totals
- Top-down Godot includes an optional 17-line launcher (core runtime/config: 389),
  12 HUD lines and five blade-track lines; none was subtracted to improve its score
- Binary assets: platformer white bitmap 58 bytes; top-down white PNG 68 bytes
- UI tests: 29 shared + 128 .NET + 106 Godot lines, plus separately itemized scripts;
  platformer verification: 570 authored lines + 29 generated-host lines;
  top-down including tests/tools: 846 .NET / 731 Godot lines

Detailed shared/schema/helper/factory/data/config/test categories, file hashes and
bytes are retained in the companion study and the machine-readable verification
record. No helper code was moved out of the denominator.

## Checks and limitations

| Final focused execution | .NET2D | Godot | Actual .NET rendered evidence |
|---|---|---|---|
| UI | 14 shared + 26 native | Same shared rules + 39 native | Two captures, 14 pixel checks |
| Platformer | 18 native + 14 extracted-host | 24 native | Three captures, nine pixel checks |
| Top-down | 49 native/host + nine SDL-input | 79 native | One capture, seven pixel checks |

All supported final commands were independently rerun successfully. Final builds
had zero warnings/errors; consumed managed/native library hashes match the verified
packages. The original **110 source/data files and original report are unchanged**.
Assertion totals reflect different test organization, not engine quality scores.

UI exercised actual SDL pointer hit tests and Godot viewport mouse input (29
sequences, plus three labeled direct-signal probes). Platformer's extracted host
uses actual production statements/packaged physics with synthetic time/input;
top-down additionally injects SDL events through PollInput and the ordinary host.
Those are not physical-keyboard or OS focus/minimize acceptance.

Godot headless native execution passed. In the tested executor, rendered capture
could not start an X11/Wayland display because AF_UNIX listening sockets were
denied. This local evidence gap is not a claim that Godot graphics is generally
unavailable. Final reruns did not retry/bypass that restriction. No manual game-feel,
physical-GPU/device, Godot pixels, temporal smoothness, fresh NativeAOT or
performance comparison is claimed.

## Concrete debugging lessons

- A misspelled nested RML field builds as C#, then native rendering reports the
  resolved data path and C# callsite, without the original RML line. The analogous
  misspelled Godot C# model field fails compilation at the source location
- RML disabled attributes blocked clicks but the ordinary button's `:disabled`
  appearance needed an explicit bound class in this fixture
- Draw counts missed an app bug: texture zero drew soft-circle fallbacks, not
  rectangles. Explicit white-texture ownership plus pixel corner checks fixed it
- Typed handles still require live-instance checks: Godot input after external
  room deletion was corrected and tested; .NET checks its owned root's IsAlive

These were fixture-level corrections, not engine changes. Explicit local source
helps inspect mistakes, but runtime/render tests remain necessary on both sides.
The current author guide plus focused UI/composition/loop contracts form a clearer
entry path; no AI context-size or success-rate improvement was measured.

## Next bounded work

1. For action gameplay, extend the passing floor-edge case into one character-motion
   contract with one slope and one moving platform. Measure repeated ground/synchronization work
   before deciding whether a narrow helper belongs in examples or the runtime
2. For UI feedback, specify and reproduce nested-field/command diagnostics with
   file/line/path. Source-location preservation is a concrete improvement candidate
3. Require a second genuinely different panel or transferred gameplay resource
   before adding UI components, serialized scene inheritance or another framework

No engine runtime was changed, no remote push was made, and no additional feature
is authorized by this report alone.

## Reproduction and provenance

The companion directory is `../authoring-current-comparison/` relative to this
checkout; it contains full `REPORT.md`, `METHOD.md`, applications, scripts, source
manifests and logs. Run each genre's `run-checks.sh` (`topdown/run_checks.sh`), plus
`platformer/run-rendered-checks.sh --ours-only` on this prepared executor. Its
source/evidence ZIP excludes engines, SDKs, dependency caches and fonts. This is
not a self-contained engine distribution. Archive/hash details and exact evidence
are in [the verification record](verification/code-authoring-comparison.json).

Use the consolidated local `../game-loop-package-feed`: managed package SHA-256
`d301bff503cc05d216deae1f1a4e0e4a9cd9df7795ba7f594cc062ceb6c19358`, native
`a5b203f64856ed92e98b1a53b3d1544054831caeeed2fe6696e9a2ca2479040c`.
The older main-feed native package lacks generic UI exports. Prepared prerequisites
were .NET SDK 10.0.401, Linux x64, official Godot 4.6.3 .NET, SDL/Mesa software
rendering and an explicitly selected compatible font; no native rebuild was needed.

Current engine contracts: [author guide](AUTHOR_GUIDE.md), [UI](UI_MODELS.md),
[composition](CSHARP_COMPOSITION.md), [loop](GAME_LOOP_RECIPE.md).
Official Godot interpretation references:
[CharacterBody2D](https://docs.godotengine.org/en/4.6/classes/class_characterbody2d.html),
[pause modes](https://docs.godotengine.org/en/4.6/tutorials/scripting/pausing_games.html),
[C# signals](https://docs.godotengine.org/en/4.6/tutorials/scripting/c_sharp/c_sharp_signals.html).
