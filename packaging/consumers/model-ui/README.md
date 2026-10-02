# Independent generic UI package consumer

Run from a built checkout using only existing SDK/AOT packs and the local feed:

```sh
PACKAGE_MODEL_UI_PROOF_ROOT=/tmp/model-ui-proof-unique \
  bash scripts/test-model-ui-packages.sh
```

The harness refuses an existing proof directory. It copies both consumers into
that directory, creates an empty NuGet cache and local-only feed, and publishes
framework-dependent JIT and NativeAOT consumers. No project references, internal
runtime sources, reflection access, or friend assemblies are used. The rich
consumer copies `managed/UiModelExamples.cs` as application sample source and all
six RML/RCSS and two BMP fixture files. It executes all three examples, then
checks public exact-key, reorder, removal, reload and typed handler contracts.
Independently declared public SDL 3.4.16 event functions inject real mouse input
without exposing EngineHost internals. Both modes verify copied exact-key click
packets, application dispatch, stale revisions, a pointer-down/reorder/up rejection
and a fresh click after reorder. Four consecutive public SDL text commits verify
continued typing without refocus, and an unaccepted draft survives an unrelated
scalar update before more text is accepted. UTF-8 event storage stays allocated
until public EngineHost.PollInput consumes the event. Additional constructed packets cover
dialogue text and nested Boolean handlers. The harness checks all six captures
with the fixture pixel validator.

The minimal benchmark compares equivalent one-text-field UI with the historical
BoundUi benchmark: two 7-byte strings, 2,000 warmup calls and 20,000 measured
unchanged/changed calls. It reports actual per-session native crossing counters:
unchanged Apply crosses once for state, changed Apply twice for state + snapshot.
The timed loops contain no Draw/layout/render calls. Generic Apply validates and
copies CPU data and marks the model dirty; RmlUi binding evaluation/layout occurs
on Draw. BoundUi performs a direct text-element update in Apply, so the changed
Apply timings do not measure equivalent completed DOM work.

First-content ends at Draw return, not presentation or GPU completion. A fresh
Mesa cache makes the first process shader-cold; OS caches are not flushed. Five
subsequent processes give the shader-warm median. Calling-thread allocations do
not include native allocations or total process memory. FDD totals exclude the
installed .NET runtime; AOT carries the required runtime code.

`measurements.json` retains each run, counters, sizes, package/file SHA-256 hashes,
loaded-native path checks, package/publish payload identity and the tool versions.
Only driver prerequisites enter LD_LIBRARY_PATH. libgal must load from each copied
publish directory, never a source/build directory. License/provenance contents are
included in distribution size and can vary with recorded snapshot paths.

A separate same-frame-work JIT comparison can be reproduced without modifying
the baseline snapshot:

```sh
python3 packaging/consumers/model-ui/measure-frames.py \
  "$PWD" ../ui-bridge-baseline /tmp/model-ui-frames-unique \
  ../android-trim-tools/dotnet/dotnet
```

It builds copied package-only baseline/current consumers, then alternates six
process runs of each (first excluded), with 10 warmup + 100 measured Apply+Draw
frames per process. This includes deferred model evaluation and frame submission,
but still is not a GPU-completion or physical display latency measurement.
