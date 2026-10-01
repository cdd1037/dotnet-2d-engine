# Typed UI bindings and dynamic lists

This optional RmlUi profile adds explicit C# projections for ordinary models. The
settings and mission adapters retain their existing contracts. It is not a managed
DOM, reflection mapper, CLR serializer, template language or widget framework.

```csharp
var bindings = new UiBindings<Inventory>()
    .Text("heading", m => m.Title)
    .TextInput("name", m => m.Name, action: 101)
    .Boolean("enabled", m => m.Enabled, action: 102)
    .Number("quantity", m => m.Quantity, action: 103)
    .Action("refresh", action: 104, enabled: m => m.Enabled)
    .List("items", m => m.Items, action: 105);
using var ui = new BoundUiSession<Inventory>(engine, bindings);
ui.LoadAsset(assets, "ui/inventory.rml");
// The next ordinary draw validates/renders the staged document and publishes it.
engine.Draw(camera, sprites);
ui.Apply(model); // all projections/validation precede one native batch
// Poll copied actions; the application chooses whether to accept edits into its model.
var action = ui.Poll();
if (!action.IsEmpty && ui.IsCurrent(action)) { /* update model explicitly */ }
```

`--binding-demo` is a complete consumer:
text, checkbox and range actions update its C# model, a row click removes that
stable ID, and Refresh adds a new ID. The separate roster fixture uses different
IDs and a different model type through the same API. These engine-facing classes
currently follow the prototype's internal class visibility; the separate packaging
proof will establish a reusable assembly/public surface.

## Values and bounds

A session freezes 1..32 target registrations. IDs contain 1..47 ASCII bytes,
matching `[a-z][a-z0-9-]*`. Every authored interactive element is registered.
Targets cannot nest. Supported kinds are:

- Text: plain `p`, `h1`, `h2` or `div`, up to 255 UTF-8 bytes/255 Unicode scalars
- TextInput: `input type="text"`, up to 255 bytes and at most 64 scalars; a smaller
  authored `maxlength` is enforced before native mutation
- Boolean: `input type="checkbox"`; separate optional enabled projection
- Number: `input type="range"`, integer 0..100, authored min 0/max 100/step 1
- Action: plain `button`, nonzero application action ID and optional enabled projection
- List: initially empty `div`, nonzero action ID and at most 64 copied rows across
  the entire document; optional enabled projection

`UiListRow` has a nonzero `ulong` ID, plain text (255-byte/scalar bound), enabled and
selected flags. IDs are unique within a list; different lists may reuse a row ID.
Rows can be added, removed, cleared or reordered by replacing/mutating the model
list and calling Apply. No index becomes application identity. Selected is a
visual model flag; the application owns selection policy. A list's parent element
survives replacement, retaining its scroll container; RmlUi clamps scroll after
layout. Lists are fully materialized, bounded, and not virtualized.

Strings are copied as text nodes or input values. `<`, `&` and quotes in model or
row strings do not become markup. A user draft exceeding the copied action byte limit is retained in the widget,
but its action is dropped with an explicit target/limit diagnostic and overflow
count; it never becomes a truncated model update. Invalid UTF-16/UTF-8, controls, duplicate IDs,
unknown flags, non-finite/out-of-range native numbers, oversized snapshots and
invalid payload combinations produce explicit errors.

## Dirty batches, actions and lifecycle

Call Apply after model changes or once per update. It projects a complete bounded
snapshot and compares it with the last accepted snapshot. An unchanged snapshot
skips native mutation and revision advancement. Several model edits therefore
cross the ABI in one call. The warmed unchanged inventory path allocates zero
managed bytes in the checked 1,000-iteration fixture; this is not a zero-allocation
claim for changed lists, arbitrary application delegates, rendering or RmlUi.

Native staging validates all targets/rows and constructs changed text/list nodes
before touching live DOM. Invalid batches and failed projections preserve the
last published model, revision and queued actions. Changed snapshots increment a
single document-wide revision and clear pending actions, even if only one target
changes. This deliberately conservative rule is easy to audit. Allocation failure
inside upstream live property setters is not a strong transactional rollback
promise; handle an out-of-memory error as a failed UI session.

`Poll` copies an action with document generation, model revision, target index,
action ID, row ID and the relevant typed value. An empty queue has action ID 0.
`IsCurrent` checks generation/revision, target/action/kind, membership and enabled
state against the **last applied** UI snapshot. Application-side mutations do not
change that snapshot until Apply succeeds. Previously polled actions must still
be checked before use. Removed/reinserted IDs, reordered/disabled rows, changed
models, reload and close cannot revive an old revision. A 64-action queue reports
overflow through common UiState; it does not silently pretend every action arrived.

User input is a draft until the application accepts its action. Reapplying an
unchanged model does not reset a draft or move the caret. An unrelated changed
text target preserves the current input's composition. Replacing the composing
field cancels preedit before setting its new value. The existing text bridge,
window/framebuffer candidate conversion and routed input rules still apply.

A session is the EngineHost's exclusive UI owner, on its owning thread and outside
an active sprite frame. Load stages exact validated source snapshots; Apply waits
until a draw publishes them. A failed reload retains the live document. Accepted
publication changes generation and clears retired events/text. Disposal detaches
native listeners before context destruction and releases managed projection
references. Engine-first destruction invalidates wrappers, and an obsolete wrapper
cannot close a newer owner. No managed callbacks run from native input or rendering.

## Authoring and ABI

`BoundUiAuthoring` reuses the existing bounded XmlReader and finite RCSS parser,
with registered IDs, static layout IDs/classes and scoped range/list generated
selectors. Each RML file links only its exact same-basename sibling RCSS. The
profile permits no scripts, inline handlers/styles, data bindings, interpolation,
external resources, imports, UI images or namespaces. Generated `bound-row` and
`bound-items` classes belong to native code. Limits remain 64 KiB per source file,
256 elements/depth 16, 128 rules and 32 declarations per rule. Strict snapshots are
staged from an owned temporary directory and removed after synchronous parsing.

`gal_bound_ui_open/apply/poll` are additive calls. The snapshot is size-tagged,
version 1; original ABI1 structs and existing profiles remain unchanged. Target,
value, row, snapshot and action records are 64, 288, 272, 24 and 304 bytes. Arrays and
strings are copied synchronously; pointers are not retained. Registration and
snapshot state are bounded per document. Test commands are explicitly probes,
not alternate production event injection APIs.

Verification is summarized in [validation](validation.md). Scripted SDL composition
and Rml pointer hits do not establish real OS Chinese IME/candidate-window,
accessibility or physical-GPU acceptance. The existing external font remains a
separate prerequisite and is excluded from Git.
