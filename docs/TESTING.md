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
