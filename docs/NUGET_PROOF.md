# Local NuGet and trimming proof

This is a local-feed experiment, version **0.1.0-preview.1**, using ordinary
`Microsoft.NET.Sdk` projects. It is not a public NuGet release, stable SDK/API,
Windows/macOS package or general Linux distribution support claim.

Two explicit package references separate responsibilities:

```xml
<PackageReference Include="Dotnet2D.Engine" Version="0.1.0-preview.1" />
<!-- Required only when the application uses the matching native runtime. -->
<PackageReference Include="Dotnet2D.Native.Linux.x64" Version="0.1.0-preview.1" />
```

The managed package contains the net10.0 runtime assembly, MIT license, README and
one small MSBuild target for opt-in `EngineAsset` items. The native package contains
one full-feature Linux x64 profile and its nonbaseline dynamic dependencies under
`runtimes/linux-x64/native/`, plus its source/version/hash manifest and notices.
The normal SDK/NuGet native asset selection handles copy/probing. There is no custom
SDK, runtime code generator, package installer or native download on first use.

## Source and API boundary

`engine/RuntimeSources.props` is the explicit reusable source list. The library
compiles those files; the original `managed/GameAuthoringLab.csproj` excludes them
and references the library. Demo programs, tests, mission/room rules, fixed profile
hosts, physics fixture, game progress schema and sample catalog defaults remain in
the executable. Shared authored entity records remain in the library with their
explicit JSON attributes. Source-generated contexts are referenced directly by
loaders; no reflection serializer or assembly-wide root was added.

Namespace remains `GameAuthoringLab`. The deliberate public proof surface covers
`EngineHost.Create`, camera/sprite draw data, input snapshots/action maps, ordinary
World/Entity/Scene behavior, explicit resource mappings/leases, authored-scene
loading and typed UI bindings. The factory disables legacy tone initialization.
Raw interop, UI probes and optional feature surfaces not exercised as package APIs
remain internal. This proof does not make every implemented prototype module a
stable public API. The test host has explicit friend access; independent consumers
do not. Internal visibility is an API boundary, not a security boundary.

`EngineHost` stores optional owner references through a tiny internal interface,
so its disposal path does not statically retain concrete UI/audio/physics/cache
implementations. Owners still invalidate their wrappers on the creating thread.
Texture caches and leases have internal constructors: callers obtain them through
`EngineHost.Textures` and `Acquire`. The public binding status uses the current
document generation and reports revision zero after publication until Apply.

The runtime enables `IsTrimmable` and `IsAotCompatible`; normal builds run their
analyzers. There are no trim/AOT warning suppressions or blanket assembly roots.
The consumer projects disable reflection JSON and the unused default networking
XML resolver. Independent trimmed and AOT executions verify those choices.

## Reproduce with existing prerequisites

```sh
bash scripts/pack-managed.sh
bash scripts/pack-native.sh
PACKAGE_PROOF_ROOT=/tmp/dotnet2d-proof-new bash scripts/test-packages.sh
```

Packages are generated under ignored `build-packages/feed/`; binaries/packages are
not source-controlled. `DOTNET` selects the installed .NET 10 SDK. The proof used
SDK 10.0.401/runtime 10.0.12 and an existing clang 19 toolchain. `PACKAGE_SOURCE_CACHE`
selects an existing NuGet cache containing the six official 10.0.12 ILLink,
ILCompiler and runtime packs; the script only copies these existing archives into
the isolated local feed. It downloads or installs nothing. `AOT_CXX` selects the
existing NativeAOT C++ compiler. The runner's in-process ILLink task shim uses the
original Microsoft task assembly to accommodate unavailable task-host Unix
sockets; it is a build-host option, not a package/consumer SDK modification.

The script copies three ordinary consumer projects to a fresh directory outside
the checkout. Their references are `PackageReference` only, with a fresh NuGet
cache and local-only feed. No project/source references point back to the engine.
Assets are copied into the fixture before MSBuild evaluation; the sprite fixture
uses the existing tiny atlas BMP, and no second bitmap copy is tracked in Git.
`EngineAsset Include="assets/**/*"` copies caller-owned relative asset paths into
both build and publish outputs. The runtime package selects no game assets.

For this runner, the existing offscreen software graphics driver is copied to an
external prerequisite directory. Runtime `LD_LIBRARY_PATH` points only there,
never to `build-ui`, the checkout's SDL installation or engine source folders.
The package's native library resolves its own eight-file closure with relative
RPATH. Host graphics/audio libraries and drivers remain separate prerequisites.
`PACKAGE_GRAPHICS_LIBDIR` can select an existing suitable driver directory instead.
The external CJK font is separately installed and selected with `GAL_UI_FONT`;
no font bytes are packaged. This is not a fully self-contained OS/driver/font image.

See [native packaging](NATIVE_PACKAGE.md) for the glibc/C++ baseline, loader checks,
complete selected notices and excluded host dependencies.

## Measured results

All three copied consumers passed `dotnet run`/build, trimmed self-contained JIT
publish/run, and fresh NativeAOT publish/run. Empty references only the managed
package and calls no engine API. Sprite loads the explicit source-generated scene,
resolves its atlas, submits three real frames and checks missing-resource errors.
UI drives typed rows, mutation/revision guards, reload and replacement ownership.
Its framebuffer captures are byte-identical across all three modes.

Bytes below exclude debug symbols. Framework-dependent output requires an already
installed .NET runtime, so it must not be compared to self-contained output as a
complete-machine installation size.

| Consumer | Framework-dependent output | Trimmed JIT output (includes .NET runtime) | AOT executable | Native engine/dependency files | Authored assets | AOT output total |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Empty | 381,958 | 23,791,557 | 1,147,432 | 0 | 0 | 1,147,432 |
| Sprite | 9,902,042 | 33,776,116 | 2,647,704 | 9,514,720 | 1,415 | 12,163,839 |
| UI | 9,906,555 | 34,210,871 | 3,176,744 | 9,514,720 | 2,356 | 12,693,820 |

The full managed runtime DLL is 297,472 bytes. It is absent from the empty trimmed
output, 98,816 bytes in the sprite output and 74,752 bytes in the UI output. Sprite
roots authored-scene JSON/World/resource code; UI instead roots XML/RML validation
and typed bindings, so these two consumers exercise different graphs. Trimmed
metadata and NativeAOT symbol maps confirm neither roots AudioSession,
PhysicsWorld, mission or room gameplay, and sprite does not root UI bindings.
The empty AOT graph contains no engine methods.

The native package was **4,286,695 compressed bytes**, with eight DSOs totaling
**9,514,720 bytes**. Every sprite/UI output contains byte-identical copies of those
DSOs. Their unused native functions are still present: managed trimming does not
rewrite a prebuilt shared library. Empty has no native payload because it does not
reference the native package. No native function-trimming claim follows from that
package choice. The managed archive was 122,410 bytes in this run; ZIP metadata and
assembly source-revision stamps may change on rebuild. Per-run hashes are retained
in the ignored measurements, rather than treating archive byte counts as ABI.

The machine-readable report separates app executable/managed entrypoint, managed
engine, native payload, assets, metadata, .NET framework/other files and debug
symbols. Framework-dependent files are snapshotted before RID-specific publishes;
otherwise nested build directories would falsely double-count native assets.

## Verification and remaining work

The split retains **10,293 full JIT assertions**, **60 typed UI native checks**,
**61 offline PCM checks** and **284 real Box2D checks**. The independent package
proof additionally checks copied assets, no project references, native hashes,
trimmed/AOT roots, three-mode UI capture equality and compiler rejection of forged
cache/lease construction. The native package's isolated extraction clears library
overrides, validates RPATH and all seven nonbaseline dependencies, performs eager
relocation checks and actually loads the DSO. No new native ABI was required.

The current proof is Linux x64/software Vulkan only. UI requires the existing
external font; real OS Chinese IME, hardware graphics and physical audio acceptance
remain separate. More public module APIs, feature-specific native packages,
cross-platform binaries, broader compatibility baselines and SDK conveniences
require their own measured work. No package was published remotely and no new
credentials, CI release or public feed were configured.

Evidence: ignored `evidence/nuget/`, `build-packages/native-verification.json`, and
the selected external proof directory's `measurements.json` and `logs/`.
Official guidance: [native NuGet assets](https://learn.microsoft.com/en-us/nuget/create-packages/native-files-in-net-packages)
and [trimmable libraries](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/prepare-libraries-for-trimming).
