# UI generator driver regressions

This ordinary .NET 10 console test project uses the public engine attributes and
Roslyn's generator driver. It does not load a native UI library, evaluate generated
assemblies, substitute test-only runtime attributes, or require a test-runner
package.

From the repository root, with .NET 10 available:

```sh
dotnet build tests/ui-generator/UiGenerator.Tests.csproj -c Release -m:1 --disable-build-servers
dotnet run --project tests/ui-generator/UiGenerator.Tests.csproj -c Release --no-build
```

In the recovery workspace, first source `.tools/recovery-env.sh`.

Coverage includes successful generated compilation and emission, declared private
entry points, direct typed command registrations, static field getters, exact names
and migration aliases, original RML/C# diagnostic provenance, empty nested
collection schemas, invalid controller/command/model declarations, document input
matching and non-RML filtering, native reserved command names, bounded expanded
model graphs, native-owned expression proof limits, and incremental driver reuse.
Assertions inspect symbols and syntax rather than snapshotting complete generated
text. Incremental tests do compare unchanged generated registrations across input
changes, and inspect Roslyn's named tracked steps.
