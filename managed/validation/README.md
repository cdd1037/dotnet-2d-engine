# Managed validation snapshots

Validated 2026-10-01 on Linux x64 with .NET SDK 10.0.401/runtime 10.0.12. Commands run from the repository root. Paths below describe this workspace, not prerequisites for other machines.

## Current world-model JIT checks

`world-self-test.log`: **547 assertions passed**, including the original ABI suite,
stable IDs, stale/foreign reference rejection, distinct transform/owner/scene graphs,
pickup/drop, persistent room transitions, mixed-graph teardown, numeric failure
atomicity and recovery, behavior mutation, extraction, and demo equivalence.

Both allocation probes report **0 managed bytes** over 1,000 frames after 128 warmup
frames: the original direct-array loop and the new ordinary-object
`World.Update` → extraction → native draw loop, each with 259 sprites per frame.

`world-headless-120.log`: **120 frames, 31,080 sprites, zero GPU draws/audio plays**.
The Release JIT build passed with zero warnings/errors using `--no-restore`, the
existing SDK/cache, and no new downloads. These headless tests exercise the managed
world and native validation boundary, not graphics or audio hardware.

The sections below retain the **original foundation-stage evidence**. In particular,
the blocked AOT attempt records the environment before later integration work; see
the current [repository validation report](../../docs/validation.md) for the final
NativeAOT and graphical results.

## Offline AOT compatibility analyzer

```sh
mkdir -p managed/.offline-source
DOTNET_CLI_TELEMETRY_OPTOUT=1 \
DOTNET_CLI_HOME="$PWD/managed/.dotnet-home" \
NUGET_PACKAGES=/workspace/scratch/196948ae630a/android-trim-tools/nuget \
/workspace/scratch/196948ae630a/android-trim-tools/dotnet/dotnet \
  build managed/GameAuthoringLab.csproj -c Release \
  -p:IsAotCompatible=true -p:NuGetAudit=false \
  -p:RestoreSources="$PWD/managed/.offline-source"
```

`aot-analyzer.log`: passed, zero warnings/errors. The empty restore source prevents network package retrieval; the SDK and ILLink analyzer package already existed locally. No new tools or packages were downloaded.

## ABI and smoke

```sh
LD_LIBRARY_PATH="$PWD/build-headless" \
/workspace/scratch/196948ae630a/android-trim-tools/dotnet/dotnet \
  managed/bin/Release/net10.0/GameAuthoringLab.dll --self-test
LD_LIBRARY_PATH="$PWD/build-headless" \
/workspace/scratch/196948ae630a/android-trim-tools/dotnet/dotnet \
  managed/bin/Release/net10.0/GameAuthoringLab.dll --headless --frames 120
```

`self-test.log`: 192 assertions passed, including zero managed allocations over 1,000 warmed-up frames with 259 sprites/frame.

`headless-120.log`: 120 frames, 31,080 validated sprites, zero GPU draw calls, zero audio plays. This is CPU-only validation, not a graphics/audio integration result.

## Original NativeAOT attempt (historical)

```sh
DOTNET_CLI_TELEMETRY_OPTOUT=1 \
DOTNET_CLI_HOME="$PWD/managed/.dotnet-home" \
NUGET_PACKAGES=/workspace/scratch/196948ae630a/android-trim-tools/nuget \
/workspace/scratch/196948ae630a/android-trim-tools/dotnet/dotnet \
  publish managed/GameAuthoringLab.csproj -c Release -r linux-x64 \
  -p:PublishAot=true --no-restore -o managed/bin/nativeaot
```

`nativeaot-not-restored.log`: blocked with NETSDK1047 because the assets file has no `net10.0/linux-x64` target; exit 1. SDK/cache inventory also showed no NativeAOT ILCompiler package. No NativeAOT binary was produced or executed. This does not establish whether a full NativeAOT build would pass after the required toolchain restore.
