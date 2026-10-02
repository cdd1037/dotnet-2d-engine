# Generic UI bridge verification

## Scope and source

This verifies the first richer-UI architecture step: C# scalar/record/array
projections, copied native models and typed event packets, authored with ordinary
RmlUi 6.3 RML/RCSS. Vue-style authoring, component compilation, props/slots and
keyed component lifecycle are deferred. The legacy BoundUi mapping remains
separate; it shares the renderer, staged resources and session ownership.

- Baseline: `8e59754`
- Generic bridge and three fixtures: `855789f`
- Draft/focus continuity refinement: `3e3edbe`
- Target: Linux x64, .NET SDK 10.0.401 / runtime and NativeAOT packs 10.0.12
- UI: SDL 3.4.16, pinned RmlUi 6.3, offscreen SDL surface and Mesa software Vulkan

The narrow [RmlUi compatibility hooks](UI_MODELS.md#pinned-rmlui-ordering-and-draft-hooks)
are generated from a hash-checked original file. The dependency checkout and
static archive remain unchanged. Same-depth structural ordering was reproduced
as an old-fails/new-passes inventory shrink case. Generic attribute draft caching
is context-scoped; legacy contexts retain upstream attribute behavior.

## Acceptance checks

- Release managed build: zero warnings/errors
- Eight native CTest contracts, including new C ABI layout assertions
- Complete JIT self-test: **11,612 assertions**; 29 generic CPU contracts
- Generic native/UI integration: **87 assertions**
- Existing BoundUi native regression: **60 assertions**
- Existing text-input/IME scripted regression: **41 assertions**
- Public three-document demo: **9 frames**, inventory → dialogue → settings
- Three source-run 960×540 captures: visually inspected; **21 pixel assertions**

The generic suite covers exact `ulong.MaxValue` and values above 2^53; copied
text/bool/number/key arguments; nested cards and independent actions; disabled
choices; nested settings arrays; removal/reorder/clear; malformed snapshots;
failed reload retention; native source-file access denial; revision/generation
retirement; overflow; wrong-thread/re-entry; repeated replacement and disposal.

Editing regressions cover repeated SDL character commits without refocusing,
burst text packets drained before one Apply, unchanged-field draft/caret/preedit
preservation, authoritative external value replacement, and retirement of focus
on key/shape change or hidden/disabled ancestors. A nested keyed child schema does
not prove identity for an unkeyed outer row. These are injected SDL events and
software-rendered readbacks, not real OS IME or physical-device acceptance.

## Independent package proof and measurements

The final refined proof passed both JIT and NativeAOT, with **37 public checks per
mode**, all three fixtures, and **42 pixel assertions** across six captures. The
new continuous-typing regression fails against the preserved `855789f` package
and passes against `3e3edbe`. Twelve minimal measurement processes and two separate
loaded-library identity checks also passed.
The proof copies ordinary SDK consumers outside the checkout, restores from a
fresh local-only feed/cache, and verifies the actual loaded package `libgal.so`.
It uses no ProjectReference, reflection access or private engine context. Public
SDL event injection exercises native commands and ordinary typing in JIT and
NativeAOT, while the three unrelated fixtures use the same packaged bridge.

The size/timing comparison uses equivalent minimal text views, the same full
native configuration (RmlUi/mixer/physics ON, SVG OFF), and existing official
compiler/runtime packs. Publish sizes exclude symbols. Framework-dependent JIT
excludes its installed runtime; AOT includes required runtime code, so compare
before/after within each mode.

### Matched distribution sizes

All values are bytes, excluding symbols.

| Payload | Baseline `8e59754` | Refined `3e3edbe` | Change |
|---|---:|---:|---:|
| `libgal.so` | 4,111,704 | 4,172,424 | +60,720 |
| Eight native DSOs | 9,584,856 | 9,645,576 | +60,720 |
| Managed Engine DLL | 422,912 | 466,944 | +44,032 |
| Engine nupkg | 167,722 | 186,005 | +18,283 |
| Native nupkg | 4,317,459 | 4,346,832 | +29,373 |
| Minimal JIT distribution | 10,419,382 | 10,525,184 | +105,802 |
| Minimal AOT distribution | 13,219,156 | 13,255,246 | +36,090 |
| Minimal AOT executable | 3,313,696 | 3,289,040 | −24,656 |

The native bridge's library growth is **1.48%**; the minimal AOT distribution
increases **0.27%**. These are consumer/profile-specific totals, not universal
feature costs. The generic benchmark includes 1,024 extra bytes of JIT application
IL for crossing counters and 26 extra authored asset bytes. AOT trims the chosen
API path, so replacing the legacy consumer can reduce executable bytes while the
native library grows. Compressed packages also contain path-dependent provenance.

### Timings and crossings

Apply measurements use 2,000 warmup and 20,000 timed calls per process. Warm results
are medians of five shader-warm processes; tiered compilation is disabled in both
JIT consumers.

| Measurement | Baseline JIT | Generic JIT | Baseline AOT | Generic AOT |
|---|---:|---:|---:|---:|
| Shader-cold first-content, ms | 553.511 | 337.046 | 238.721 | 237.684 |
| Shader-warm first-content, ms | 167.751 | 196.845 | 74.949 | 79.950 |
| Warm unchanged Apply, µs | 0.402 | 0.174 | 0.381 | 0.235 |
| Warm changed Apply, µs | 1.905 | 0.466 | 1.700 | 0.501 |

Both measured loops allocate **zero calling-thread managed bytes** in both modes.
The generic counters report 20,000 state calls/zero mutations for unchanged loops,
and 40,000 total calls/20,000 mutations for changed loops. Native allocation costs
remain unmeasured.

The more comparable JIT **Apply + Draw** test alternates six baseline/generic
process pairs, excludes the first pair, and uses 10 warmup + 100 measured frames
per process. Median was **783.583 → 999.083 µs/frame**, about **0.216 ms slower** for
the generic path in this sample. Warm-process values ranged 690.733–1,486.704 µs
for baseline and 797.541–1,863.439 µs for generic. The shared software-Vulkan host
varied substantially; this is not evidence for an overall rendering speedup.
The lower Apply-only time reflects different placement of work and must not be
presented as a completed-frame improvement.

Final verification hashes:

- Native nupkg: `a5b203f64856ed92e98b1a53b3d1544054831caeeed2fe6696e9a2ca2479040c`
- `libgal.so`: `8c7e3c97d8e2bb467f4877cfaa27e7ebcb755983557e6d1a26230dae5ec0c9c3`
- Unmodified original RmlUi archive: `02487161629a0ca58555c86a7d55dde5f539d3182a5b18aec9f69ba4aa19e0cd`

All four package-consumer publishes carried exactly the package's eight native
DSOs. Both JIT Engine assemblies matched their package, and loaded library paths
resolved inside the copied publish directories.

Important interpretation:

- An unchanged generic Apply performs one native state query and no mutation
- A changed generic Apply performs two crossings: state query + copied snapshot
- Generic Apply defers Rml expression/layout work to Draw; old BoundUi eagerly
  mutates text DOM. An Apply-only difference is not a rendering speedup
- Measured allocation loops cover managed bytes on the calling thread only;
  native allocations, user delegate allocations and arbitrary larger models are
  not zero-allocation claims
- First-content time ends when Draw returns, not hardware monitor presentation
- Shader-cold means a fresh Mesa shader cache, not flushed operating-system caches
- Same-work Apply+Draw samples on a shared software-Vulkan runner have substantial
  variability; they are exploratory evidence, not a performance guarantee

## Reproduction

After explicit dependency setup, use the focused and aggregate tiers in
[TESTING.md](TESTING.md). A graphical source run is:

```sh
source scripts/ui-env.sh
$DOTNET managed/bin/Release/net10.0/GameAuthoringLab.dll --model-ui-native-test
python3 scripts/validate-model-ui-pixels.py
```

Set `DOTNET` to the installed .NET 10 executable. The default captures are in
`evidence/ui-model/visual`; `GAL_MODEL_UI_CAPTURE_DIR` selects another directory.
Inventory and settings intentionally use clipped scroll containers.

For the optional independent package boundary, first build both local packages
with `scripts/pack-managed.sh` and `scripts/pack-native.sh`, then run
`scripts/test-model-ui-packages.sh` with a fresh `PACKAGE_MODEL_UI_PROOF_ROOT` and
the existing tool/runtime cache. The script neither installs dependencies nor
publishes remotely. Raw proof logs, package hashes, timings and captures are
local generated evidence, not source/runtime dependencies.

## Remaining limits

No physical GPU, platform/device matrix, real IME candidate window, accessibility
or other-platform acceptance is claimed. Data-for remains positional; data-if
hides instead of unmounting. Focus retirement on structural change and pointer
revision invalidation are deliberately conservative. There is no virtualized
list, component lifecycle, props/slots runtime or automatic focus navigation.
The exercised controls are text inputs, range/checkbox inputs, buttons, images
and nested authored layouts, not an exhaustive validation of every RmlUi control.
The explicit schema/resource bounds remain in [UI_MODELS.md](UI_MODELS.md).
