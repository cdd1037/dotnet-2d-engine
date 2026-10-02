# C#-declared UI contracts

The recommended author path makes named C# methods and opted-in DTO members the
source of UI command and model registration. A small incremental Roslyn generator
emits ordinary `UiCommands`/`UiRecord` calls and checks associated RML during build.
Game rules, model projection timing, publication, queue draining and disposal stay
explicit. This is not a Vue compiler, component runtime, reflection mapper or an
assembly-execution build step.

## Authoring

```csharp
[UiModel]
public sealed record ItemView(ulong Id, string Title, int Count);

[UiModel]
public sealed record InventoryView(
    [property: UiField(Maximum = 8)] ItemView[] Items);

[UiContract(typeof(InventoryView), "assets/ui/inventory.rml")]
public sealed partial class InventoryUi
{
    private readonly UiModelSession<InventoryView> ui;
    public InventoryUi(EngineHost engine)
        => ui = new(engine, CreateUiSchema(), CreateUiCommands());

    [UiCommand]
    private void Discard(ulong id)
    {
        // Apply ordinary C# game rules here. A current packet is not rule authority.
    }
}
```

```xml
<!-- Sample.csproj: ordinary NuGet and compiler inputs -->
<PackageReference Include="Dotnet2D.Engine" Version="0.1.0-preview.1" />
<PackageReference Include="Dotnet2D.Native.Linux.x64" Version="0.1.0-preview.1" />
<AdditionalFiles Include="assets/ui/inventory.rml" />
<EngineAsset Include="assets/**/*" />
```

```xml
<div data-for="item : state.Items">
  <button data-event-click="Discard(item.Id)">{{item.Title}}: {{item.Count}}</button>
</div>
```

The containing controller is partial; handlers are ordinary implemented methods.
The generator adds private `CreateUiCommands()` and private static
`CreateUiSchema()` helpers. It does not create a session or load/apply/render for
the application. Use `ui.StageAsset(assets, path, initialModel)` and the next
ordinary engine draw to publish the copied source and model together. Check
`Status.Pending`, `Loaded` and `Diagnostic` afterward; no hidden frame is drawn.
Explicitly drain/dispatch the current queue before applying a changed model.
See [initialization and replacement](UI_MODELS.md#initialization-and-replacement).
The document path in the attribute is relative to the project and must resolve to
exactly one `AdditionalFiles` item; it is not an `AssetRoot`-relative runtime path.

Names are exact, case-sensitive C# symbol names: `Discard` stays `Discard`, and
`Title` stays `Title`. There is no hidden snake_case conversion. The existing manual
API also now accepts `On(nameof(Discard), UiArgs.Key, Discard)`. Explicit
`[UiCommand(Name = "old_name")]` and `[UiField(Name = "old_field")]` aliases allow
an intentional migration without forcing a simultaneous authored-file rename.

`[UiModel]` opts a DTO into its public readable instance properties and fields,
including straightforward inherited members. Supported scalar model types are
`string`, `bool`, `double`, `float`, integral types through `uint`, and `ulong`.
`ulong` is always an exact Key; the other supported numeric types are Number.
Nested opted-in classes/structs and one-dimensional arrays, `List<T>` and
`IReadOnlyList<T>` are supported. Collection elements are `string`, `bool`, `double`,
`ulong`, or opted-in DTOs. Use an explicit `[UiField(Maximum = n)]` for a smaller
collection bound; the default is 64. The ordinary schema limits still apply:
128 expanded nodes, depth 16 including elements, arrays of 1–64 items, and 2,048
runtime snapshot values.

Commands support zero to four by-value parameters: `string`, `bool`, `double`,
`ulong`. For example, an `int` model property is safely projected as Number, while
an `int` handler parameter is not an implicit lossy double-to-int conversion.
Perform the intended conversion inside a `double` handler. Async, generic,
static, ref/out, unsupported or unimplemented commands produce declaration-site
errors. Recursive models, inaccessible/unsupported members, hidden inherited
members, nullable value types, nullable collection elements and unsupported
collection shapes also get explicit
diagnostics. Nullable references can compile; actual null values still fail the
existing projection validation. Getter bodies are never executed or analyzed by
the generator. Computed views, custom aliases or other advanced representations
can continue using handwritten `UiRecord` projections and typed `On` registrations.

## What is removed

In the maintained [Discard consumer](../packaging/consumers/ui-ergonomics/), the
separate `InventorySchema.cs` mapping is gone. Properties no longer repeat their
names and kinds in a schema file. Each command's method supplies its name,
parameter types and handler; there is no independent numeric ID or registration
entry. RML still refers to the exposed names, but a rename mismatch fails at build
rather than remaining an invisible edit dependency. Bounds and intentional aliases
remain explicit facts because they cannot be inferred from a CLR type.

This reduces repeated facts and unchecked rename dependencies. It is not a claim
that all application code is shorter, that game rules are generated, or that
arbitrary runtime registration can be statically reconstructed.

## IDs and compatibility

No-ID `On` overloads cover every supported arity. A session freezes registrations,
reserves all explicit IDs, then assigns the smallest unused positive values in
ordinal name order. Explicit declarations may appear before or after automatic
ones. Reordering registrations does not change the mapping; changing the set may.
Automatic IDs are private to that frozen contract: do not serialize them or use
them as game-action identities.

Existing `Add(name, id, ...)`, explicit-ID `On`, manual schemas, `Poll`, `IsCurrent`
and `Dispatch` remain available. `[UiCommand(Id = ...)]` is an advanced explicit
ID option. Lowercase names keep their meaning. PascalCase requires the current
native naming gate; managed and native ABI struct layouts are unchanged. Runtime
strict loading and signature/key/generation/revision checks are retained even for
built documents, since packaged files and runtime values can change later.

## Diagnostics and honest proof limits

- `DUI001`: controller shape or generated member-name collision
- `DUI002`: invalid/duplicate/reserved command name, ID or method signature
- `DUI003`: unsupported, cyclic or oversized model shape
- `DUI004`: associated RML missing, unreadable or ambiguous in AdditionalFiles
- `DUI005`: original RML line/column, field path and available C# declaration for a
  provable contract mismatch

XML tokenization uses the framework `XmlReader`, with the same bounded,
DTD-disabled source-aware representation as runtime validation. A shared lexical
path checker proves command names/arity, direct path/literal scalar kinds,
record/array members, loop aliases and simple scoped aliases. Empty lists do not
hide bad template paths because the schema is checked without model values.

This is deliberately not a second RmlUi expression parser. Dynamic index results,
operators/ternaries, transforms, event values, current array bounds, actual key
membership and runtime numeric/text validity remain native/runtime-owned. Unknown
`data-*` metadata, literal textarea content and escaped-only interpolation keep
their existing behavior. CSS, resource manifests, widget layout and complete RML
expression grammar still go through runtime/native loading. A successful build
is not a guarantee that every future snapshot or replaced asset is valid.

Generated calls retain original declaration file/line, so projection exceptions
point to the DTO property and full field/index breadcrumb rather than a `.g.cs`
file. No runtime data-source origin is invented. Ordinary methods still propagate
exceptions to application policy; code generation does not turn an invoked handler
into a successful game action.

## Build and package integration

The netstandard2.0 analyzer is bundled under `analyzers/dotnet/cs` in the existing
Engine package. It uses the normal Roslyn analyzer contract and standard
AdditionalFiles/CompilerVisibleProperty inputs. The package carries no Roslyn
runtime dependency, MSBuild task that executes game code, special package lock,
or runtime compiler. Generated applications are ordinary AOT-safe C#.

The generator build uses the official `Microsoft.CodeAnalysis.CSharp` 4.14.0
NuGet dependency, private to tooling. Run the normal source restore once (for
example `dotnet restore managed/GameAuthoringLab.csproj` with the official NuGet
source available); subsequent prepared local-feed package scripts can work from
the restored cache. The tested application SDK is .NET 10.0.401. Source-checkout
projects that use generated contracts need an explicit analyzer ProjectReference;
a runtime ProjectReference alone does not transitively supply analyzers.

Descriptors are symbol-free equatable values. The incremental pipeline caches
unchanged declarations and documents, and unrelated AdditionalFiles do not alter
selected contracts. Generated text and IDs are deterministic for unchanged inputs.
Source locations necessarily follow the authored paths supplied by the compiler;
byte-identical output across different checkout paths is not claimed.

## Verification

See [test tiers](TESTING.md). The source generator driver suite is
`tests/ui-generator`; it compiles and emits generated consumer code, verifies
original-source diagnostics and checks retained-driver incremental caching.
`scripts/test-ui-contract-edits.py` performs real edit/rebuild tests in a disposable,
already-restored package consumer. The existing
`scripts/test-ui-ergonomics-packages.sh` now runs that proof plus JIT/NativeAOT
native UI checks using the generated Discard registration and model contract.
It never installs a custom SDK or rebuilds native dependencies during the test.

### Verified 2026-10-02

Linux x64, .NET SDK 10.0.401/runtime 10.0.12, SDL 3.4.16, pinned RmlUi 6.3,
offscreen SDL/software Vulkan and Noto Sans CJK SC:

- Release source/generator builds: 0 warnings, 0 errors
- Full source headless aggregate: **11,738 assertions**
- Generic native UI: **97 assertions**; legacy BoundUi: **60 assertions**;
  existing three-model image checks: **21 assertions**
- Source-shared parser/preflight extraction: **305** isolated differential and
  regression assertions, including exact old diagnostic/source-position parity
- Roslyn driver: **28 regression groups**, including generated compilation/PE
  emission, negative declarations, exact source locations, bounded expanded
  schemas, unchanged caching and unrelated semantic edits
- Independent copied PackageReference consumer: **12 real build cases** including
  method/property renames, wrong case, missing inputs, Key/Number and arity
  mismatches, bad nested loops, and coordinated rename recovery
- That consumer published and ran in **JIT and NativeAOT**, **170 assertions each**:
  14 shared rules + 26 existing native regressions + 93 Discard checks +
  29 runtime diagnostic checks + 8 generated-registration checks. The printed
  39 Discard-rule subtotal is already part of 93
- Generated runtime checks cover exact `ulong.MaxValue` routing, Number decoding,
  stale revisions, original property declaration lines, nested projection errors,
  empty collections and controller-capture release after disposal
- Analyzer is present in the Engine package and absent from both application
  publish outputs; no Microsoft.CodeAnalysis runtime assemblies are copied

One measured edited-consumer build took **2.404 s**; its immediately unchanged
build took **0.648 s**. Failed/successful edit builds in the same pass took
**2.126–2.429 s**. These include the whole small consumer compiler/MSBuild cost;
they are not an isolated generator benchmark or an authoring-speed comparison.
The driver checks caching independently of MSBuild's whole-compile skip.

The build-only analyzer is **69,120 B**. The local Engine nupkg grew from
**195,720 B** at the preceding RELAY package to **229,744 B**; its runtime DLL grew
from **488,960 B** to **497,152 B**. This includes automatic IDs, attributes and
shared validation refactoring; it is not attributed solely to source generation.
The tested full consumer NativeAOT executable is **4,764,336 B**; that figure
excludes its separate debug file, native libraries, assets and notices and is not
a before/after size delta. The native naming gate was rebuilt without changing ABI layouts. No new native
dependency, package versioning scheme or runtime package check was introduced.

JIT/AOT Discard captures were visually inspected and keep both action columns,
disabled styling and selected-state behavior. These software-rendered/scripted
checks do not establish hardware input/GPU behavior, real OS IME, other platforms,
full expression proof, general UI productivity or runtime performance.
