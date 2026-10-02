# Typed-command Discard replay

This maintained package consumer replays the bounded inventory Discard task from
`ai-authoring-efficiency`, using its accepted game rules and authored cards. The
historical baseline, attempt, evaluator and measurements were read without edits.
This is an API migration and edit-surface comparison, not another AI trial.

The application declares each command name, argument codec and managed handler
once in `CardUi.cs`. Inspect is 1, Use 2, Pause 3, Restart 4, Save 5, Load 6,
Reverse 7, Remove empty 8, and Discard 9, matching the accepted historical task.
`UiArgs.Key` delivers an exact `ulong`, never a floating-point number. There is
no application command enum or dispatch switch. `InventorySchema.cs` describes
the view separately; game rules do not depend on the engine.

The loop explicitly polls input, drains UI events through `Dispatch`, applies the
resulting view once, and draws. Dispatch validates the current packet immediately
before invoking its handler. It does not imply that a game action succeeded:
`CardGame.Discard` still guards pause, visibility and positive stock. Native code
does not call managed handlers. Disposing the session releases its handler
captures; the temporary registration builder is not retained by the application.

Card spacing belongs to `.actions button`, rather than named Use/Discard styles.
All three actions retain both the native disabled attribute and disabled visual
class. No engine-specific Discard widget or per-widget rule was introduced.

## Run as an independent package consumer

Prerequisites: an existing .NET 10 SDK, a matched local feed containing the new
typed-command `Dotnet2D.Engine` and compatible UI-enabled
`Dotnet2D.Native.Linux.x64` packages, a supported Linux SDL/Vulkan environment,
and a readable licensed font matching the RCSS family (`Noto Sans CJK SC`).
No engine sources or ProjectReferences are used. Both packages keep the existing
`0.1.0-preview.1` development version, so use a fresh NuGet cache for rebuilt
packages; the version string alone does not verify package contents.

From the source checkout, with `DOTNET`, `PACKAGE_FEED` and `GAL_UI_FONT` set:

```sh
proof=$(mktemp -d /tmp/dotnet2d-ui-ergonomics-XXXXXX)
cp -a packaging/consumers/ui-ergonomics "$proof/app"
export NUGET_PACKAGES="$proof/nuget-cache"
export DOTNET_CLI_HOME="$proof/dotnet-home"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export GAL_UI_ERGONOMICS_OUTPUT="$proof/captures"
"$DOTNET" restore "$proof/app/Sample.csproj" --source "$PACKAGE_FEED"
"$DOTNET" build "$proof/app/Sample.csproj" -c Release --no-restore -m:1 \
  -p:UseSharedCompilation=false
mkdir "$proof/unrelated-working-directory"
(cd "$proof/unrelated-working-directory"; \
  "$DOTNET" "$proof/app/bin/Release/net10.0/Sample.ui-ergonomics.dll" --check)
```

The check is finite. It writes BMP captures, a test-only save and loaded-library
evidence beneath `GAL_UI_ERGONOMICS_OUTPUT`; without that variable it selects a
fresh temporary directory. `--check` does not read or overwrite a live save.
For the normal interactive inventory, omit `--check`; `CARD_SAVE_PATH` selects
the save destination (default `cards-save.json` in the working directory).

For an already-prepared software Vulkan fixture, set `SDL_VIDEODRIVER=offscreen`,
`SDL_AUDIODRIVER=dummy`, `VK_ICD_FILENAMES` to its `lvp_icd.json`, and
`LD_LIBRARY_PATH` to its matching graphics-library directory before execution.
Keep the package's native libraries and licenses together. No dependencies or
fonts are downloaded or installed by this consumer.

An independent NativeAOT proof can publish this same project with
`-r linux-x64 -p:PublishAot=true`, using a prepared source/cache containing the
SDK's matching AOT runtime/compiler packages, then run the produced
`Sample.ui-ergonomics --check` from an unrelated directory. The JIT and AOT
executions must both use the final matched engine/native package bytes. This
README specifies the runnable checks; validation results belong to the enclosing
change's verification report.

## Exact acceptance coverage

`checks/RulesChecks.cs` retains all 14 original shared-rules assertions.
`checks/Smoke.cs` retains all 26 original native regression assertions, with
only capture/save-path plumbing adapted for the self-contained consumer.
`checks/DiscardChecks.cs` retains the accepted Discard behavioral checks and adds
native command routing and explicit-drain checks:

- Discard returns true only for an unpaused, visible item with positive stock,
  decrements exactly one, and leaves every other persisted property unchanged
- Zero/unknown, empty/depleted, paused and removed IDs are non-mutating false
  results, including a removed item whose loaded count is positive
- `CanDiscard` mirrors the rule; selection and visibility survive successful
  discards; save/load retains counts and pause; Restart restores initial counts
- Model rules exercise `ulong.MaxValue`; real SDL pointer events route exact keys
  9007199254740993 through 9007199254740996, including reorder and array shrink
- Native clicks hit both columns and rows, verify distinct session-assigned command IDs, and preserve
  Inspect, Use, toolbar, invalid-load atomicity, reload and empty-list behavior
- Depleted and paused Discard buttons emit no native command; captured native
  pixels are the enabled `(122,211,181)` and disabled `(41,62,77)` fills
- Three queued Discards drain the same revision, invoke three handlers, stop at
  zero stock through the rule guard, and produce exactly one changed snapshot
- Two queued Pause commands both execute before the final unchanged projection;
  no native Apply is needed; updated/reloaded documents retire earlier packets
- Native binding diagnostics and overflow remain empty/zero

`checks/DiagnosticChecks.cs` creates a temporary ordinary RML/RCSS fixture after
disposing the inventory's session. Through public package APIs it checks:

- Valid empty nested loops; schema-detectable wrong nested fields, Text passed to
  a Key command and wrong command arity even with no instantiated inner rows
- Exact diagnostic code, authored RML file/line/field and cause, with a separate
  auto-captured C# command declaration for argument errors
- Failed reloads retain the live document, revision and last applied snapshot
- Invalid nested text and number projections report `UI_MODEL_VALUE`, full array
  index breadcrumbs, actual C# declaration locations and original inner errors,
  while explicitly avoiding an invented runtime data-source location
- Failed projections preserve the native snapshot; temporary assets are removed
- `/proc/self/maps` contains exactly one `libgal.so`, beneath this JIT/AOT
  application's `AppContext.BaseDirectory`, recorded in `package-native-path.txt`

These are public SDL 3.4.16 event-queue tests and native rendered pixel readbacks.
They do not claim physical pointer hardware, a hardware GPU, other platforms,
runtime performance, or broad authoring-productivity results.

## Bounded edit-surface comparison

The historical addition touched six production files: domain/view/command enum,
schema plus registration, dispatch switch, RML, and per-button RCSS. With this
maintained starting pattern, replaying the same feature touches four: the domain
method and projected availability, its schema field, one typed `On` declaration,
and the appended RML button. The session assigns the command ID automatically.
Tests still need updating in either workflow. These are source edit locations,
not measured editing turns, elapsed time, token use, or a speedup ratio.

Physical line/UTF-8 byte counts, including blank lines and comments:

| Consumer scope | Pre-Discard baseline | Accepted Discard | Maintained replay |
|---|---:|---:|---:|
| `CardUi` + `InventorySchema` | 144 / 5,418 | 149 / 5,664 | 116 / 4,685 |
| Rules + those two files + entry point + RML + RCSS | 329 / 16,101 | 341 / 16,802 | 319 / 16,416 |

These consumer-only counts exclude tests, catalog JSON, project/config files,
documentation and engine helpers. They are not total implementation-cost claims:
the shared typed-command and diagnostic helpers must be counted separately in
the engine change's ledger. Program growth includes portable check/artifact
setup and the diagnostic fixture. The baseline predates Discard and is shown for
provenance; the accepted Discard column is the behavior-equivalent comparison.

Recovery note: the source was initially copied and checked against the historical
files. A subsequent execution-environment replacement removed those local inputs.
This consumer was reconstructed from source text retained in tool results; all
unchanged reconstructed files match their previously recorded line/byte counts.
Those counts do not constitute a cryptographic identity check. The historical
counts above were measured before replacement; original-input byte equality has
not been re-established after recovery. The historical directory was not rebuilt.
