# Test tiers

Run commands from the repository root. Use the smallest tier that covers the
change, and preserve the distinction between contract, rendering and device tests.
Every entry point prints elapsed wall time and its exit status.

| When | Command | Coverage / prerequisites |
|---|---|---|
| Small core edit (default) | `scripts/test.sh` | Native headless contracts, Release compile, three CPU-only frames |
| Authored scene edit | `scripts/test.sh quick scene` | Default plus sample scene validation |
| UI authoring edit | `scripts/test.sh quick ui` | Default plus RML/RCSS preflight, no renderer |
| Input/viewport edit | `scripts/test.sh quick input` | Default plus v2 layout, synthetic DPI, remapping, focus and fixed-step edge checks |
| Resource paths/cache edit | `scripts/test.sh quick resources` | Default plus focused CPU resource identity/lifetime/diagnostic checks |
| Playable mission edit | `scripts/test.sh quick game` | Default plus focused CPU mission lifecycle/save checks |
| Animation/tween/timer edit | `scripts/test.sh quick animation` | Default plus timing, lifetime, atlas residency and warmed-allocation checks |
| TileMap data/culling edit | `scripts/test.sh quick tilemap` | Default plus strict sourcegen data, chunk order/culling, lifetime, CPU collision plans and warmed allocations |
| Feature batch | `scripts/test.sh jit` | Native contracts, compile, complete JIT self-test |
| Interop/trimming/serialization/publish change | `AOT_APP=/absolute/path/to/fresh/app scripts/test.sh aot` | Native contracts and full self-test of the explicitly selected NativeAOT binary |
| Renderer change | `scripts/test.sh graphics` | Existing software Vulkan pixel checks, requires prepared SDL/ICD environment |
| UI rendering/integration change | `scripts/test.sh ui` | Existing JIT/AOT UI validation, self-tests, scripted UI and pixel checks |

The quick tiers are smoke/preflight checks, not full focused unit suites. Existing
assertions remain intact. Tests are embedded in the prototype executable; no late
framework migration or test assembly refactor is included in this milestone.

## Setup and freshness

Install the SDK and native compiler described in [dependencies](dependencies.md).
Set `DOTNET` if the SDK is not on PATH. Perform `dotnet restore
managed/GameAuthoringLab.csproj` explicitly once with your approved package source.
Quick/JIT use `--no-restore` and never install dependencies automatically. The
optional local sibling SDK fallback is a workspace convenience, not a dependency
included in a checkout.

The AOT tier deliberately does not republish. Publish the current tree first with
an appropriate NativeAOT toolchain, for example:

```sh
dotnet publish managed/GameAuthoringLab.csproj -c Release -r linux-x64 \
  -p:PublishAot=true -p:StripSymbols=true -o build-aot
AOT_APP="$PWD/build-aot/GameAuthoringLab" scripts/test.sh aot
```

Publishing may restore required runtime/compiler packs; it is an explicit special
step. An old binary passing tests is not evidence for current sources. UI scripts
also assume current JIT and `build-aot` outputs and prepared optional native UI
dependencies; see [UI prototype](UI_PROTOTYPE.md). They use software Vulkan and
scripted input, not physical GPU or real IME acceptance.

## Milestones and research

Run the necessary aggregate once against the final source tree before committing
a milestone. Reuse that evidence for documentation-only cleanup. Large XML
validation differential/fuzz corpora, size ablations and deep benchmarks remain
opt-in research, outside default development and CI commands. No automatic CI
schedule is introduced.

Raw evidence is generated locally and ignored by Git. Keep concise results,
versions, caveats and source identity in [validation](validation.md); historical
reports may refer to local evidence that is not shipped in a fresh checkout.

## Entry-point smoke validation (2026-10-01)

`scripts/test.sh quick ui` passed on the current Linux cloud runner in **13 s**
wall time (Release build reported **2.13 s**, zero warnings/errors). Native
contracts passed, three CPU frames processed 777 sprites with no GPU draws/audio,
and sample UI preflight passed. This is one measured invocation, not a benchmark
or guarantee. Existing final aggregate evidence is reused rather than duplicated.

UI text-input changes can run `--ui-owner-test` on the headless build and `--ui-text-test` with the prepared RmlUi build/font. The latter queues SDL composition packets; it does not drive a real OS input method. See [text bridge](UI_TEXT_INPUT.md).

## Typed bindings and lists

`bash scripts/test.sh quick bindings` exercises CPU schema/value bounds and owner
contracts. The full JIT tier includes these checks. With the optional UI build,
source `scripts/ui-env.sh` and run the host with `--binding-native-test`;
`--binding-demo` is the interactive inventory consumer. A native/AOT boundary pass
uses the same `--binding-native-test` on the freshly published executable.

The focused native fixture covers stable row add/remove/reorder, disabled actions,
copy-only typed edits, duplicate/malformed payload rollback, stale generations and
revisions, queue overflow, repeated list replacement, engine-first cleanup and
two-field composition lifetime. `GAL_BOUND_UI_CAPTURE_DIR` selects the three
readback destinations. Real desktop CUA acceptance and synthetic queued text are
recorded separately in [validation](validation.md).

## Independent package consumers

`pack-managed.sh`, `pack-native.sh` and `test-packages.sh` are an explicit optional
local-feed tier. They require existing SDK/native/AOT dependencies and never
download or publish packages. The proof copies ordinary SDK consumers outside the
checkout, restores a fresh cache from a local-only feed, then verifies normal
run/build, trimmed JIT and AOT. See [reproduction and measurements](NUGET_PROOF.md).
Do not rerun the full package matrix for every edit; use it at a packaging/API
boundary or when a concrete dependency/trim regression warrants it.

## World clipping

Use `scripts/test.sh quick clipping` for the focused managed/native CPU boundary.
The optional UI graphics build runs `--clip-graphics-test`, followed by
`scripts/validate-clipping-pixels.py` against `GAL_CLIP_CAPTURE_DIR`. The test records
actual atlas/alpha/edge/tilemap/UI/resize pixels; the headless tier does not. A fresh
AOT host can run those same flags at the additive ABI boundary.

## Bounded diagnostics

`scripts/test.sh quick diagnostics` checks the managed log/timing/geometry and
scene-overlay recovery contracts. With the existing optional graphics build,
run `--diagnostics-scenario --frames 3` and set `GAL_DIAGNOSTICS_CAPTURE_DIR`.
Then run `scripts/validate-diagnostics-pixels.py` on that directory to check solid
edges, diagonal geometry, unchanged scene interior and disabled/re-enabled output.
This managed-only slice adds no ABI or serialization roots; it does not require a
fresh aggregate AOT publication. See [semantics and limits](DIAGNOSTICS.md).

## Materials

Use `scripts/test.sh quick materials` for the source/cache/native-handle boundary,
and `scripts/test-material-builder.py` for offline GLSL preparation. The existing
UI graphics build supports `--material-graphics-test`; set
`GAL_MATERIAL_CAPTURE_DIR` and run `scripts/validate-material-pixels.py` against
the directory. At this additive ABI/source-generation boundary, run a fresh AOT
host with the same focused graphics checks and compare readbacks. Headless tests
do not claim shader compilation or execution. See [contract](MATERIALS.md).
