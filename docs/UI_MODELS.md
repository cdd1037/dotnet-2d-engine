# Generic C# UI models and ordinary RML

Use `UiModelSession<T>` for new UI: project ordinary C# data through an explicit
schema, author nested views in RmlUi 6.3 RML/RCSS, and poll typed commands back into
your game. C# remains authoritative. There is no managed DOM, reflection mapper,
runtime JavaScript, runtime code generation or engine-owned game-rule layer.

`BoundUiSession<T>` remains available as a separate legacy target/list mapping
implementation. It does **not** forward to this model bridge. Both paths share
native rendering, source staging, image resources and exclusive UI ownership;
their authoring, update and focus contracts differ. See [legacy bindings](UI_BINDINGS.md).

## Describe data, then author the view

`UiRecord<T>` registers named fields with explicit `Func<T, U>` readers. Use
`Text`, `Boolean`, `Number`, `Key`, `Record`, `Array`, or the general `Field`
method with `UiData<U>`. `UiArray<T>` can contain scalars, records or further
arrays. Reuse record descriptions for repeated structures. The root must be a
nonempty record; it is exposed as `state` inside `data-model="model"`.

The session freezes the schema, projection delegates and command registrations
at construction. Later edits to those builders do not change the session.
Delegates execute synchronously in managed code during `Apply`; keep them pure,
bounded and free of session re-entry. This is an explicit AOT-safe projection,
not automatic discovery of CLR properties. The native side receives copied
schema/value/command records and never calls managed delegates.

For example, these ordinary model types describe a small inventory card:

```csharp
sealed class CardCopy { public string Title = "Lantern", Badge = "TOOL"; }
sealed class Item
{
    public ulong Id = 9_007_199_254_740_993UL;
    public CardCopy Card = new();
    public bool Equipped;
}
sealed class Kit
{
    public string Title = "Trail kit";
    public List<Item> Items = [new()];
}
```

Register only the data and commands the view needs. Here `engine`, `assets` and
`camera` are already owned by the application's main loop:

```csharp
var card = new UiRecord<CardCopy>()
    .Text("title", static x => x.Title).Text("badge", static x => x.Badge);
var item = new UiRecord<Item>()
    .Key("id", static x => x.Id).Record("card", static x => x.Card, card)
    .Boolean("equipped", static x => x.Equipped);
var schema = new UiRecord<Kit>()
    .Text("title", static x => x.Title)
    .Array("items", static x => x.Items, item, maximum: 32);
var commands = new UiCommands().Add("equip", 1, UiValueKind.Key);
var model = new Kit();
using var ui = new UiModelSession<Kit>(engine, schema, commands);
ui.LoadAsset(assets, "ui/kit.rml");
engine.Draw(camera, ReadOnlySpan<SpriteDraw>.Empty); // publish accepted source
ui.Apply(model);
engine.Draw(camera, ReadOnlySpan<SpriteDraw>.Empty); // synchronize model/view

// In each outer update, after the ordinary engine.PollInput():
for (var command = ui.Poll(); !command.IsEmpty; command = ui.Poll())
{
    if (command.CommandId != 1 || !ui.IsCurrent(command)) continue;
    var selected = model.Items.Find(x => x.Id == command[0].Key);
    if (selected is null) continue;
    selected.Equipped = !selected.Equipped; // this policy belongs to the game
    ui.Apply(model);
}
// The next normal Draw synchronizes the accepted snapshot before more commands.
```

`assets/ui/kit.rml` is an ordinary authored subtree, not a generated row widget:

```xml
<rml>
  <head><title>Trail kit</title><link type="text/rcss" href="kit.rcss" /></head>
  <body data-model="model">
    <h1>{{state.title}}</h1>
    <div class="cards">
      <div class="card" data-for="item : state.items"
           data-class-equipped="item.equipped">
        <h2>{{item.card.title}}</h2><span class="badge">{{item.card.badge}}</span>
        <p data-if="item.equipped">In your kit</p>
        <button data-event-click="equip(item.id)">
          {{item.equipped ? 'Unequip' : 'Equip'}}
        </button>
      </div>
    </div>
  </body>
</rml>
```

The same-basename `kit.rcss` can use normal RmlUi selectors and layout:

```css
body { font-family: Noto Sans CJK SC; font-size: 18px; color: #ffffff; }
.cards { display: flex; flex-direction: column; }
.card { padding: 12px; margin: 8px; background-color: #18202b; }
.card.equipped { background-color: #274b42; }
.badge { color: #ffd080; }
```

RML/RCSS are RmlUi formats, not browser HTML/CSS. Native RmlUi handles nesting,
`{{...}}` text expressions, `data-for`, `data-if`, `data-class-*`, `data-attr-*`,
`data-attrif-*` and supported expression composition. Nested loops can read nested
arrays; arrays also expose `.size`. The bridge does not recreate RmlUi's grammar
or impose the legacy finite widget/property allowlist. RmlUi warnings during
parse, update or render are errors in this integration, so test the real rendered
document, including populated arrays and conditional states.

## Commands, keys and drafts

`UiCommands.Add(name, id, argumentKinds...)` declares one nonzero application ID
and zero to four scalar arguments. `data-event-<event>` must invoke exactly one
registered command, such as `choose(choice.id, choice.text)`. Assignment and
statement sequencing are rejected. Command arity and types are checked natively
when the event fires; an invalid payload is dropped with a diagnostic, not coerced
into a different command. `Poll()` returns a copied `UiCommandEvent`; command ID 0
means empty. Read `command[i].Text`, `.Boolean`, `.Number` or `.Key` according to
the registered kind.

Use `Key` for identity, including IDs above the exact integer range of `double`.
Each key is a nonzero `ulong`, unique across the **entire snapshot**, including
different/nested arrays. Native RmlUi receives a canonical decimal **string** and
commands parse it back to the exact `uint64`. Pass the bound key directly, as in
`equip(item.id)`; do not use arithmetic or numeric literals for keys. A key command
argument must be present in the current applied snapshot. Keys preserve application
identity; they do not enable a keyed DOM diff.

Inputs are drafts until game code accepts the command. Author read bindings and
typed commands explicitly:

```xml
<input type="text" maxlength="48" data-attr-value="state.name"
       data-event-change="rename(ev.value)" />
<input type="range" min="0" max="100" step="1" data-attr-value="state.volume"
       data-event-change="volume(ev.value * 1)" />
<input type="checkbox" data-attrif-checked="state.hints"
       data-event-change="hints(!state.hints)" />
```

Register `rename` with `Text`, `volume` with `Number` and `hints` with `Boolean`.
The range expression converts the event's text value to a number; game code must
still validate its own range and rules. `data-value`, `data-checked` and `data-rml`
are intentionally unsupported: there is no implicit two-way model mutation or
markup injection. Model text remains plain text, including `<`, `&` and quotes.

## Snapshots, events and lifecycle

`Apply(model)` projects and validates a complete bounded snapshot. Equal snapshots
return `false` without native mutation or revision advancement. A changed snapshot
is copied and accepted as one native batch, advances the document revision and
clears queued old commands. Validation/projection failures preserve the accepted
model, revision and queued commands. This is not a rollback promise for an
out-of-memory condition or a later RmlUi update/render failure.

Accepted values become visible through RmlUi's next normal update/render. Commands
are suppressed until that synchronization succeeds. Call `IsCurrent(command)`
**immediately before applying game behavior**, even for an event already polled:
it checks generation, revision, registered argument types and key membership in
the last applied snapshot. Changing a C# object without a successful `Apply` does
not update that snapshot. The application still owns game-rule validation.

RmlUi `data-for` reuses elements by position. Reorder, removal and replacement may
retarget an existing element to a different item. Every changed snapshot therefore
conservatively cancels current focus and text preedit, even for an unrelated field
change. An active pointer gesture is invalidated across **all** command callbacks,
including mouseup, until a fresh interaction resets it. An unchanged `Apply` does
not cancel focus or reset a draft. This deliberately trades editing continuity for
safe identity routing; do not assume the legacy profile's narrower focus policy.

`data-if` hides an element; it does not unmount a component. Hidden or disabled
elements/ancestors cannot emit accepted commands. There is no Vue-style keyed
diff, component instance lifecycle or component runtime. A Vue-style compiler,
props and slots are deferred. These templates are authored RML composition today.

A session is its `EngineHost`'s single UI owner. Use it on the creating thread,
outside active sprite frames, and dispose it before the engine. Engine-first
destruction invalidates the wrapper; an obsolete wrapper cannot close a newer
owner. Reload stages exact copied sources and publishes only after strict native
parse/update/render validation. A failed candidate retains the live document;
accepted publication changes generation and retires old events and text state.
Retained native state is one bounded current model plus bounded staging/event
storage, not a history of snapshots. No managed model pointers or callbacks are
retained by native UI; managed projection references are released on disposal.

### Pinned RmlUi ordering fix

The UI build applies a narrow RmlUi 6.3 compatibility fix using
`scripts/patches/patch-rmlui-datafor.py`. It verifies the original
`DataViewDefault.cpp` SHA-256, writes a generated copy, and changes only the
`DataViewFor` constructor's update bias from `DataView(element, 0)` to
`DataView(element, -1000)`. CMake compiles that copy into `gal`; neither the
dependency checkout nor its static archive is edited. Python 3 is required for
this guarded build step. A changed upstream source hash fails closed and requires
review before upgrading the dependency.

Without this ordering change, a shrinking loop's same-depth root bindings can
evaluate a removed index before the loop removes that root; this reproduced as a
`state.items[2].equipped` warning in an offscreen render. Structural loop updates
now precede those same-depth views. RmlUi's ancestor-depth ordering is preserved
(2,000 spacing with allowed bias −1,000..999). This does not introduce keyed diffing
or change `data-if` semantics.

## Bounds and resource boundary

- Names: `[a-z][a-z0-9_]{0,46}`; duplicate record fields and command names/IDs fail
- Schema: at most 128 nodes, depth 16 including the root and array element nodes
- Arrays: registered maximum 1..64, default 64; a value may be empty
- Snapshot: at most 2,048 values, including record and array container nodes
- Text: at most 255 UTF-8 bytes and 255 Unicode scalars; invalid UTF-16/UTF-8,
  control characters and null values fail
- Numbers: finite `double` within ±(2^53−1); use `Key` for exact 64-bit identity
- Commands: at most 32 registrations, four scalar arguments each, 64 queued events;
  dropped payloads/overflow are visible through `Status.Diagnostic`/`Overflow`
- Source: 1..64 KiB per RML/RCSS file; at most 256 authored XML elements, depth 16
- Images: at most 32 unique document images, 16 MiB encoded/64 MiB decoded RGBA
  aggregate budgets, plus the format-specific [image](UI_IMAGES.md)/[SVG](UI_SVG.md) limits

The source XML requires one `rml` root, then `head` and `body`, with exactly one
same-basename sibling RCSS link. Authored IDs must be unique; loop clones are
positional views, so use keys for commands rather than repeated DOM IDs. Native
RmlUi owns normal layout/style parsing. Generic RCSS does not use the legacy
128-rule/32-declaration finite style parser.

This is a trusted project-authoring boundary, **not a hostile-filesystem sandbox**.
Scripts, inline event handlers, templates, imports, new font loading, navigation,
namespaces and indirect resource-loading features are rejected. Inline style
attributes may contain resource-checked declarations; inline stylesheets are not
supported. The native `UiFileGate` restricts generic RmlUi source reads to the exact
copied RML/RCSS snapshots. Images use the same native image renderer and finite
validated manifest as existing UI, not unrestricted file access.

Static `src`, `image()`/`svg()` and `fill-image` references are collected into that
manifest. Dynamic `data-attr-src` requires a nonempty explicit list passed to
`LoadAsset(assets, path, declaredImages)`; list every possible image using paths
relative to the RML file. Only validated manifest images may load. Dynamic styles
cannot introduce resource declarations or CSS variables. SVG requires the optional
SVG-enabled build; it remains off in the default native package. The host-selected
font is a separate prerequisite, not a template resource. Follow the
[author guide's font setup](AUTHOR_GUIDE.md#5-add-ui-only-when-it-helps-the-game).

Managed source errors report file, line/column, field, stable error code and cause
through `UiAuthoringException`. Native parsing, binding, update and render errors
report strict diagnostics; check both thrown errors and `Status`. No generation
or revision counter wraps silently; exhaustion requires the appropriate reload
or session lifecycle recovery.

## Examples and validation

`managed/UiModelExamples.cs` belongs to the sample executable, not the runtime
package. Its inventory cards, nested dialogue and grouped settings demonstrate
unrelated C# models using the same bridge; their equipment/dialogue/preferences
rules are application examples. Templates live in `assets/ui/model-*.rml` and
their sibling RCSS files. `--model-ui-demo` cycles with F5 or the footer button;
Escape exits. `--frames N` makes it finite. `GAL_MODEL_UI_CAPTURE_DIR` selects an
optional directory for bitmap captures.

After the existing .NET/native setup, run the sample executable with:

```sh
# CPU contracts with a matching headless library:
LD_LIBRARY_PATH="$PWD/build-headless" dotnet managed/bin/Release/net10.0/GameAuthoringLab.dll --model-ui-self-test
# UI-enabled native library, compatible GAL_UI_FONT and a working display/backend:
LD_LIBRARY_PATH="$PWD/build-ui" dotnet managed/bin/Release/net10.0/GameAuthoringLab.dll --model-ui-native-test
LD_LIBRARY_PATH="$PWD/build-ui" dotnet managed/bin/Release/net10.0/GameAuthoringLab.dll --model-ui-demo
```

Add your SDL dependency-library paths where required. These commands describe the
entry points; they are not a claim that this checkout has completed every gate.

- Managed contract and native integration results: **pending final validation**
- Three-example screenshot review: **pending final visual evidence**
- Trimmed/package/NativeAOT validation of the new bridge: **pending final validation**

Record completed runs and their environment in [validation](validation.md).
Scripted pointer/text probes and software Vulkan do not establish physical-GPU,
real OS IME/candidate-window, accessibility or other-platform acceptance. No
virtualized lists, drag/drop framework or automatic focus-navigation system is
provided by this bridge.
