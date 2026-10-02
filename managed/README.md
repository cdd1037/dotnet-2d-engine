# Managed host

.NET 10 owns the event/update/render loop. `Native.cs` mirrors `native/include/gal.h` with source-generated `LibraryImport` stubs and explicit C calling convention. There are no external NuGet dependencies, reflection-based discovery, reverse-P/Invoke callbacks, or per-sprite interop calls. `EngineHost` requires explicit same-thread disposal; it intentionally does not use a finalizer to destroy the thread-owned context.

Build from the repository root:

```sh
dotnet build managed/GameAuthoringLab.csproj -c Release
LD_LIBRARY_PATH="$PWD/build-headless${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}" \
  dotnet managed/bin/Release/net10.0/GameAuthoringLab.dll --self-test
LD_LIBRARY_PATH="$PWD/build-headless${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}" \
  dotnet managed/bin/Release/net10.0/GameAuthoringLab.dll --headless --frames 120
```

On Windows, place `gal.dll` beside `GameAuthoringLab.exe` or add its directory to `PATH`. On Linux use `libgal.so` beside the app or `LD_LIBRARY_PATH`. Without `--headless`, the native platform graphics/window backend must be available. Arrow keys pan, mouse wheel zooms around the viewport center, Space plays one tone per press, and Escape exits. `--frames N` can also bound the graphical run. Headless defaults to 120 frames and performs only CPU batch validation, with zero rendering/audio counters.

The demo uses 259 ordinary C# entities with `Transform2D`, optional `Sprite2D`, and optional behavior. It extracts them into one reusable sprite array and submits one batch each frame. It includes overlapping alpha panels and animated translucent sprites; no textures/files are loaded by the managed host. See [WORLD.md](WORLD.md) for the independent transform-parent, lifetime-owner, and scene-membership semantics, including pickup/drop and persistent room transitions.

The regression suite warms up 128 headless frames, then measures managed allocations on the calling thread over 1,000 update/poll/draw iterations using `GC.GetAllocatedBytesForCurrentThread`. It tests both the original direct-array loop and the ordinary-object behavior/extraction/draw loop and requires zero bytes in each. Setup, structural world edits, capacity growth, diagnostics, exceptional paths, and native allocations are excluded.

## NativeAOT

The same code uses only AOT-compatible, statically declared interop. On a machine with the .NET 10 NativeAOT packs, compiler, and linker installed:

```sh
dotnet publish managed/GameAuthoringLab.csproj -c Release -r linux-x64 \
  -p:PublishAot=true -p:StripSymbols=true -o build/aot
LD_LIBRARY_PATH="$PWD/build" ./build/aot/GameAuthoringLab --self-test
```

Use `win-x64` on Windows with the Windows native library and Visual C++ toolchain. NativeAOT publication may need SDK workload/runtime packages that are not installed in an otherwise working JIT environment. An unexecuted AOT build is not evidence of AOT validation.

The original offline analyzer-only checks are retained under `validation/`. Current platform and NativeAOT integration status belongs to the repository's [validation report](../docs/validation.md); an analyzer build alone is not NativeAOT execution evidence.

## Test coverage

`--self-test` exercises ABI version/struct layouts, invalid configs and pointers, one-context enforcement, invalid camera/sprite data, begin/submit/end/abort state, empty and split batches, capacity overflow, atomic rejection, batch-abort recovery, cumulative stats, error reporting, same-thread requirements, disposal, and 32 destroy/recreate cycles. Stale native handles are checked only before a new allocation, since raw pointer handles cannot guarantee ABA detection after reuse. It also checks CLI parsing, camera/animation logic, stable managed entity IDs, separate transform/owner/scene graphs, lifetime teardown, pickup/drop and persistent room transitions, behavior mutation, numeric recovery, extraction, and both allocation probes. The managed world IDs do reject stale/cross-world references independently of that native raw-pointer limitation. It does **not** validate a GPU, real keyboard/mouse input, resize behavior, alpha pixels, or audible output; those need a graphical run.

## Animation and timing

`FrameClip`/`FramePlayer`, typed `Tween` factories and `EngineTimer` are polling primitives with explicit `TimingStep` game/real deltas. Frame markers expose bounded, borrowed events with due/dropped counts; explicit `Play` switches clips without resetting repeated selection. `TimingScope` owns cancellation; it does not schedule callbacks. See [contracts and sample](../docs/ANIMATION_TIMING.md).

`TileMapAsset` separates authored DTOs from immutable grid data; `TileMapInstance` owns placement, texture residency and optional generated collision. Its append path composes with ordinary entity sprites. `CreateEditable` explicitly pins palette textures and enables atomic bounded `SetCells` transactions with coherent generated collision. See [TileMap](../docs/TILEMAP.md).
