# Public module boundary

The planned existing-feature exposure is now implemented as experimental public
APIs in the local `0.1.0-preview.1` package. No new native ABI, dependency, runtime
feature or SDK framework was added. Namespace remains `GameAuthoringLab`; this
prerelease makes no compatibility/stability promise.

## Ordinary consumer access

- `FrameClip`, `FramePlayer`, typed `Tween` factories/results, `TimingStep`,
  `EngineTimer`, `TimingScope` and their clock/playback/easing enums expose the
  existing explicit-clock animation/timing behavior. `TimingOperation` is a
  public common base with a private-protected constructor, so this release does
  not add arbitrary external playback implementations
- `EngineHost.OpenAudio`, `AudioGroup`, session/scope/clip/voice operations expose
  decoded clips, file streams, voices, gain groups and offline mixing. State is
  returned through immutable `AudioSessionState`/`AudioVoiceState` values
- `EngineHost.OpenPhysics`, settings/body/shape definitions, `PhysicsScale`,
  world/body/shape/scope operations, typed event copies and bounded queries expose
  the existing Box2D foundation. Public state, step, event and ray views omit
  ABI Size/Reserved fields. Ray Hit and removed-event flags are boolean
- `TileMapAsset`, validated source records, immutable grid/layer metadata,
  `LoadedTileMap`, placement/view/instance/collision types expose authored load,
  write, sprite extraction and optional static collision. The loaded asset's
  constructor is internal; source DTO edits cannot mutate the copied runtime grid

The earlier sprite/world/resources, typed UI, clipping, diagnostics, material and
render-target public surfaces remain available. This boundary does not introduce
new animation tracks, controllers, collision shapes, codecs or editor facilities.

## Ownership and retained data

Native-backed audio session/clip/voice and physics world/body/shape constructors
are internal. Callers obtain them from a live engine/session/world/body factory.
Opaque physics IDs are observable for query/event matching; they cannot create
another owning wrapper. Scope constructors reject null or closed owners. Existing
capacity, same-owner, creating-thread and disposal checks remain in force.

Public audio/physics snapshots are immutable copied values and remain readable
after the owner changes or closes. `PhysicsWorld.Events` returns a borrowed span
into a bounded 1,024-element view array; its lifetime ends at the next Step or
world/engine close. Copy individual records or the span to retain events. The
private ABI buffer and typed view buffer are allocated when opening the world;
warmed stepping/state reads allocate no managed bytes. Empty scopes still enforce
creating-thread disposal before attempting native work.

Raw P/Invoke, native ABI records, source-generation contexts, collision planning,
asset preflight helpers and engine-owned construction details stay internal.
Game/sample rules, the movement host and test probes are outside the library.
The original test host retains its named friend access. The independent consumer
has no friend access or source/project references; internal visibility is an API
boundary, not a security boundary against reflection or a deliberately renamed
friend assembly.

## Independent proof

With the already prepared .NET/native/AOT prerequisites:

```sh
bash scripts/pack-managed.sh
bash scripts/pack-native.sh
PACKAGE_MODULE_PROOF_ROOT=/tmp/dotnet2d-modules-new bash scripts/test-package-modules.sh
```

`DOTNET`, `PACKAGE_SOURCE_CACHE`, `PACKAGE_FEED` and `AOT_CXX` select existing local
prerequisites as in the earlier package proof. The script installs/downloads
nothing. It copies one ordinary SDK PackageReference consumer to a fresh directory
outside the checkout, with an isolated local feed/cache and no library lookup
back into the source tree. A tiny stereo WAV is generated there with Python's
standard library; only its source recipe is tracked. Existing atlas bytes and a
small authored map are copied as caller-owned assets before build evaluation.

The consumer passes **156 checks** in both ordinary JIT and fresh NativeAOT:
source-generated grid load/write and source/runtime isolation; a body resting on
its tile floor; copied typed sensor events, ray and AABB queries; 1,000 warmed
solver/state reads with zero managed allocations; frame IDs/tween and game versus
real-time clocks; actual decoded/streamed offline PCM with pause/gain controls;
null/missing-resource errors; copied values after disposal and complete cleanup.
It submits three **headless** sprite/map frames. This package check makes no new
rendered-pixel, physical sound, latency or device claim.

Compile-negative fixtures reject all seven native/loaded-owner constructions and
all seven unsupported null argument sites. They also reject mutable state writes
and access to raw physics interop/source-generation contexts. Full metadata is a
positive control; the combined AOT map retains the intended module roots and no
UI binding root. Fresh minimal empty/sprite/UI **trimmed publishes** still remove
these unused modules; their earlier full three-mode graphics matrix was not rerun.

## Measured output

Byte counts exclude symbols and separate code from dependencies and data:

| Combined consumer | App executable/entrypoint | Managed engine | Native payload | Authored assets | Notices | Output total |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Framework-dependent | 96,176 | 352,768 | 9,546,224 | 9,205 | 320,261 | 10,328,160 |
| NativeAOT | 2,871,624 | 0 | 9,546,224 | 9,205 | 320,261 | 12,747,314 |

The framework-dependent total also includes 3,526 bytes of runtime/dependency
metadata and requires an installed .NET runtime. The full managed engine grew
14,848 bytes from the preceding proof. Minimal sprite/UI trimmed managed sizes
remain 98,816/74,752 bytes; the empty trimmed output still has no engine assembly.
The native profile is byte-identical to the previous proof and remains present in
full wherever referenced. No prebuilt-native function trimming is claimed.

The source base is `89f680e` plus this public-boundary working tree; package assembly
hashes and output/native hashes identify the exact verified binaries. A final
README-only archive correction retains the same executed assembly bytes. The
ignored proof directory contains measurements, compiler-negative diagnostics,
source-generated/trim publish logs and AOT maps. Native artifacts and packages
remain outside Git. Modern Linux/glibc, real IME, hardware graphics/audio and
Windows/macOS acceptance remain separate gates. No public feed or release was
published.

## Focused follow-up feature proof

The subsequent capsule/exact-overlap batch has its own focused
`packaging/consumers/features` consumer and `scripts/test-package-features.sh`.
After rebuilding both packages, it passes **56 assertions in JIT and one fresh
NativeAOT run**, combining camera follow, animation markers/clip changes, atomic
TileMap editing and capsule/floor physics. Exact circle/rotated-box queries preserve
stable IDs, caller-span output, sensor/mask semantics and zero managed allocation
across 4,000 warmed calls. It uses a fresh external local-only package feed/cache
and caller-owned assets, with no friend identity or source/project references.

This later result verifies the new public APIs together; the preceding broad
module/negative-compile/minimal-trim matrix remains historical and was not repeated.
See [current evidence](validation.md#capsule-shapes-and-exact-overlaps-2026-10-02)
and [the physics contract](PHYSICS.md).
