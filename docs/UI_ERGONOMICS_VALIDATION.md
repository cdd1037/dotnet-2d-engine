# Typed UI commands and actionable diagnostics

This report records the earlier hand-registration baseline. The current
[generated UI contract](GENERATED_UI_CONTRACTS.md) supersedes its generator
deferral and manual ID/schema authoring recommendations; historical measurements
below are unchanged.

This bounded refinement keeps ordinary C# models, explicit schemas and ordinary
RmlUi authoring. It does not introduce a generator, reflection mapper, JavaScript,
Vue compiler, component/controller runtime or general lifecycle helper.

## What changed

- `UiCommands.On` declares name, stable wire ID, zero-to-four scalar codecs and
  managed handler together. No matching application dispatch switch is required
- `UiModelSession.Dispatch` checks generation, revision, signature and key
  membership immediately before invoking each handler. Polling and the
  drain-before-Apply ordering remain explicit application code
- `UiArgs.Key` delivers an exact `ulong`. Native code never calls managed handlers;
  the session releases its handler/projection captures on disposal
- Schema fields and typed commands retain their C# caller file/line. Authored RML
  errors retain the RML use location and optionally a separate `Declaration`
- Projection failures carry full field/index breadcrumbs and their original
  exception, with the schema declaration as the location. A runtime data-source
  file/line is not invented
- Load-time schema checks find resolvable field/loop-alias mistakes even when
  arrays are empty, and check command arity and proven direct-path/literal kinds
- The maintained [Discard consumer](../packaging/consumers/ui-ergonomics/README.md)
  uses one typed registration per action and shared `.actions button` spacing

Typed `On`/`Dispatch` is the public command path. Copied `Poll`/`IsCurrent` remain
available for inspection and stale-event checks. The former `BoundUiSession`
implementation is internal regression coverage. Legacy canonical-text Key arguments
remain accepted; new typed `On` definitions require directly bound identity fields
to be declared `Key`. Schema methods remain source-compatible but now include
optional caller-info parameters: rebuild consumers against this experimental
package; binary compatibility with already-compiled older callers is not claimed.
The local package development version remains `0.1.0-preview.1`; hashes and fresh
caches establish the tested identity.

## Proof limits and compatibility

The managed check is a lexical/schema preflight, not another RmlUi grammar or
expression evaluator. It proves whole scalar paths/literals, statically resolvable
record/array paths, `.size`, loop aliases and simple scoped aliases. It does not
prove dynamic index results, current array bounds, event values, transformation
outputs or general operator/ternary result types. Native strict staging and
runtime payload validation remain authoritative for those cases.

Independent review caught and corrected false positives for unknown `data-*`
metadata, legacy text keys, local alias initialization order, literal textarea
contents and escaped-only curly braces. Raw text interpolation presence is retained
from XmlReader's source positions because native RmlUi selects a text view before
entity decoding. Mixed raw/escaped text uses the decoded full segment; ambiguous
comment-split expressions remain native-owned. This deliberately permits unknown
cases instead of silently narrowing the expression language.

Validation errors still preserve the live document/snapshot/queue where the
existing contract promises it. Resource snapshots, staged publication, exclusive
context ownership, projection re-entry and disposal rules are unchanged. Arbitrary
handler exceptions propagate to game code; `Dispatch` does not silently recover,
apply a model or guarantee a successful game action.

## Final verification, 2026-10-02

Linux x64, .NET SDK 10.0.401/runtime 10.0.12, pinned SDL 3.4.16/RmlUi 6.3,
software Vulkan (lavapipe), offscreen SDL, Noto Sans CJK SC font:

- Release source build: **0 warnings, 0 errors**
- Full JIT aggregate: **11,726 assertions**, including **61** generic UI CPU
  contracts (**32** new ergonomics checks)
- Generic UI native suite: **97 assertions**, including continuous/burst text,
  draft/caret/preedit preservation, nested reorder/shrink, stale gestures/events,
  exact keys, source gate, failure retention, and **10** typed-dispatch checks
- Warmed unchanged snapshots still allocate **0 calling-thread managed bytes**
  and make no native mutation calls in the existing focused loop
- Legacy BoundUi native suite: **60 assertions**, unchanged-batch allocations 0
- Existing three-model software readback suite: **21 pixel assertions**
- Restored headless and UI native builds: **8/8 CTest contracts each**
- One independent copied PackageReference consumer was published and run in both
  **JIT and NativeAOT**, with fresh caches and no ProjectReference/runtime internals
  - **14** shared rule assertions and **26** existing native UI assertions
  - **93** Discard assertions in total: **39** rule plus **54** native/pixel checks
  - **29** public diagnostics/package-boundary assertions
  - Thus **162 assertions per mode**; the printed 39-rule subtotal is already
    included in the printed 93 Discard total, not counted twice
- Each mode loaded exactly one `libgal.so` from its own publish tree. All **8**
  native payload files matched the final native package, and the JIT engine
  assembly matched the final managed package
- Enabled/disabled Discard captures were visually inspected: the three card
  actions fit both columns, disabled styling and selected state remain distinct

The Discard replay checks paused/hidden/empty/unknown/removed IDs, stock-only
mutation, exact keys above 2^53 and `ulong.MaxValue`, saved state, restart,
reorder/shrink, disabled native clicks and pixels. Three queued Discards run before
one Apply, with the game guard stopping at zero; two queued pause toggles return
to the unchanged snapshot. Deliberately wrong fields, Key arguments, arity and
empty nested loops fail at authored source locations. Invalid nested text/number
values identify their C# declaration and complete index path.

The initial package verifier assumed JIT native files were flattened beside the
assembly. Both runs passed, but that file-location assertion failed because the
portable JIT publish uses `runtimes/linux-x64/native`. The verifier now follows the
actually loaded, already-validated in-publish path; it was rerun against those same
final outputs and all eight hashes matched. No republish or new benchmark was
needed for that harness correction.

Final identities and evidence hashes are in
[the machine-readable ledger](verification/ui-ergonomics.json). Raw logs/captures
are generated local evidence, not committed runtime dependencies.

## Source and edit-surface accounting

Physical lines and UTF-8 bytes include comments and blank lines. Runtime scope is
exactly `UiCommands`, `UiModel`, `UiModelSession`, `UiModelAuthoring`,
`UiModelPreflight`, `UiAuthoring` and `UiXmlModel`; the ledger lists each file.
The baseline is commit `5666c871b24244817ab4fdb0d9202f386a2a1c66`.

| Scope | Before | After | Difference |
|---|---:|---:|---:|
| Runtime helper/source scope | 1,133 lines / 79,373 bytes | 1,715 / 110,221 | +582 / +30,848 |
| Consumer adapter + schema | 149 / 5,664 | 116 / 4,685 | −33 / −979 |
| Consumer rules + adapter + schema + entry + RML + RCSS | 341 / 16,802 | 319 / 16,416 | −22 / −386 |
| Specified runtime scope + behavior-equivalent consumer | 1,474 / 96,175 | 2,034 / 126,637 | +560 / +30,462 |

The runtime grew to centralize typed dispatch and diagnostics. This is not a
whole-codebase size reduction. Consumer counts exclude tests, catalog JSON,
project/config files and documentation; runtime counts exclude tests, manifests,
other unchanged engine code and the package harness. None of those exclusions are
claimed as deleted code. The new test files and reproduction scripts remain in
the change for inspection.

For a future Discard-sized action following this pattern, edit four production
files: domain method/availability, schema field, one typed `On` declaration and
one RML button. The earlier accepted sample also changed a command enum/dispatch
switch and per-button CSS, across six files. This is an edit-location comparison,
not a fresh AI benchmark, measured turnaround or speed ratio.

During this task an execution-environment replacement removed the original local
checkout, dependencies and historical experiment files. The published source
baseline was cloned back; feature/consumer edits were reconstructed from retained
source text and all final tests above rerun. Native code was not changed; its
rebuild was recovery from lost artifacts. Historical consumer counts were measured
before replacement; original-input byte equality has not been re-established and
is not inferred from matching line/byte counts. No historical evidence record was
rewritten. The new maintained consumer and final source checks are self-contained.

## Reproduction

With the repository's documented SDK/native prerequisites prepared:

```sh
$DOTNET build managed/GameAuthoringLab.csproj -c Release -m:1 -p:UseSharedCompilation=false
LD_LIBRARY_PATH="$PWD/build-headless" $DOTNET managed/bin/Release/net10.0/GameAuthoringLab.dll --self-test
source scripts/ui-env.sh
$DOTNET managed/bin/Release/net10.0/GameAuthoringLab.dll --model-ui-native-test
$DOTNET managed/bin/Release/net10.0/GameAuthoringLab.dll --binding-native-test
python3 scripts/validate-model-ui-pixels.py
```

For the independent JIT/AOT proof, prepare the matched native package once, pack
the final managed source with `scripts/pack-managed.sh`, and run
`scripts/test-ui-ergonomics-packages.sh` with `DOTNET`, `PACKAGE_FEED`,
`PACKAGE_SOURCE_CACHE` and a fresh `PACKAGE_UI_ERGONOMICS_PROOF_ROOT`. The script
uses the existing AOT compiler/cache and graphics environment; it does not rebuild
native or download/install dependencies. `AOT_CXX`, `AOT_LIBRARY_PATH` and
`GRAPHICS_LIBRARY_PATH` can select those prepared paths. The separate
`verify-ui-ergonomics-package.py PROOF_ROOT` rechecks payload identity without
republishing.

No physical-GPU, physical-input, real OS IME candidate-window, cross-platform,
accessibility, generalized UI productivity or runtime-performance conclusion is
claimed. Generators, Vue-style components and broader lifecycle abstractions stay
deferred.
