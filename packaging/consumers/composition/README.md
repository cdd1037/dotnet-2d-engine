# C# composition consumer

Copy this directory outside the checkout, supply a local package source containing
the current `Dotnet2D.Engine` package, and use ordinary .NET 10 commands:

```sh
dotnet restore Sample.csproj --source /absolute/path/to/local-feed
dotnet run --project Sample.csproj -c Release --no-restore
```

`Composition.cs` is application code: immutable parameter records, typed instance
references, nested weapon/enemy/room factories and explicit rollback. It is not
compiled into the engine package. `CompositionChecks.cs` is the executable proof;
`Program.cs` simply runs it. No native package or window is required.

The only new engine API is `World.OnDestroy(entity, cleanup)`. Existing entity,
scene, ownership, behavior, transform and animation APIs do the remaining work.
See the [author-facing guide](../../../docs/CSHARP_COMPOSITION.md) for ownership
rules, failure limits and why replacing a behavior is a different lifetime.

The CPU extraction check uses placeholder zero texture handles that are never
submitted to a renderer. This consumer does not load asset files or validate
pixels, physics, audio, input or real devices.

From a prepared checkout, `bash scripts/test-composition-packages.sh` copies this
consumer to a fresh external directory, restores only from an isolated local
feed/cache, runs JIT and one fresh NativeAOT publish, and checks package/source
identity. Rebuild the managed package first with `bash scripts/pack-managed.sh`.
The script uses already installed .NET 10.0.12 runtime/AOT packs and the prepared
Clang toolchain, following the existing package proofs; it downloads nothing and
does not rebuild or require any native engine dependency.
