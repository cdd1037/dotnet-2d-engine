# Optional RmlUi experiment

Status: bounded prototype verified in software Vulkan; see results and limits below.

This page records the original settings profile and its stage-specific evidence.
The current public [bound-UI profile](UI_BINDINGS.md) separately supports typed
models, lists, [raster images](UI_IMAGES.md) and optional [SVG](UI_SVG.md).
Current aggregate and displayed-window results are in [validation](validation.md).

This reversible experiment uses upstream RmlUi 6.3 (commit
ba95ffe8bfb6370efb2cdcca927eaad4710c5413), with a separate premultiplied-alpha
SDL_GPU pipeline after world sprites. RML/RCSS are RmlUi formats, not browser
HTML/CSS. Myra and an engine-owned implementation remain alternatives.

The game world, scene snapshot JSON and business logic do not depend on RmlUi.
A build flag keeps the dependency optional. No managed DOM wrapper, embedded
JavaScript or reflection-based model binding is planned. The original settings adapter uses one small typed model; the later
[generic binding profile](UI_BINDINGS.md) adds explicit projections and dynamic lists.
Native events are copied into bounded polling queues.
Native callbacks never re-enter the non-reentrant public C ABI.

Initial authoring profile: a settings panel, text input, volume controls,
apply/reset actions and a rectangular scroll list. Chinese/English text uses an
explicit Chinese-capable font. Rounded button backgrounds are supported; rounded
or transformed scroll clipping, filters, custom effects and arbitrary UI images
are outside this profile. UI resources belong to the native context and are
released before its GPU device.

Reloads are staged; diagnostics must not silently replace the last valid document.
Generation numbers invalidate queued actions from old documents. Strict managed
preflight reports file/line/field/cause for the allowed authoring profile; upstream
warnings remain separately identified raw upstream diagnostics. This is not a
claim that a small validator understands all RML or RCSS.

Tests will distinguish scripted input and software-Vulkan pixel readback from
real keyboard/mouse, operating-system IME, candidate placement, accessibility,
physical GPUs and other platforms. The latter remain unverified in this runner.

Future editors can edit the same source resources and invoke the same validation;
no separate hidden editor state is introduced. No authored scene JSON expansion,
compression, binary cache, or generic UI framework is part of this experiment.

## Verified result (2026-10-01)

The optional experiment now builds and passes on Debian 13 x64. Linux SDL_GPU
Vulkan rasterization was exercised through SDL's offscreen video driver and Mesa
lavapipe (CPU software rendering). The image is an actual GPU-API readback of the
world sprite pass plus the separate premultiplied UI pass.

- JIT and NativeAOT: 1,402 regression assertions each, including 203 strict UI
  authoring checks. The existing three zero-managed-allocation world loops remain
  unchanged; no zero-allocation claim is made for the UI toolkit.
- Each host: 23 scripted UI assertions, 13 frames. These cover UTF-8 model/action
  copying, committed-text input, pointer hit-testing, actual text focus,
  rectangular scrolling, queue overflow, stale generation rejection, malformed
  native load retention, wrong thread/frame-state rejection, and close/reopen.
- Both initial and changed/scrolled screenshots are pixel-identical between JIT
  and AOT. 6,418 pixels change inside the scroll viewport; the strip below its
  clipping boundary and the world outside the panel stay unchanged.
- Three native CTest suites pass. Existing sprite texture/alpha/camera/resize
  tests and the full two-room JIT/AOT save/restart scenario also pass again.
- The displayed-window path, physical keyboard/mouse, real IME preedit/candidate
  placement, multiple DPI settings, grapheme/emoji editing, accessibility and
  non-Linux/physical-GPU drivers remain unverified.

Evidence: `evidence/ui/all-tests.log`, `pixels.log`, `ctest-ui.log`,
`two-room-regression.log`, `graphics-regression.log`, `aot-settings.png`.

## Narrow contract and limits

`native/include/gal_ui.h` adds optional calls without changing ABI v1 structures.
The settings model is one typed batch (name, volume, status); the authored list is
static in this probe. Native event records copy model values into a 64-record
queue; overflow is counted, not silently dropped. Native record generations are
monotonic across close/reopen. There are no managed callbacks into the guarded C
ABI. The core library exports only `gal_*` on the CMake Linux builds.

`UiAuthoring.ValidateFiles` returns immutable source snapshots. `UiSession` loads
those exact validated strings from an owned temporary directory, then removes it
after synchronous parsing. This is safe only for the current resource-restricted
profile (one linked stylesheet, no UI images/imports/templates). A wider resource
profile needs a corresponding resource lifetime and resolution policy.

Staging uses a second Rml context and same-format offscreen color target. The
candidate is updated and rendered before publication; warnings retain the current
document and diagnostic. Queued events are cleared only on accepted publication.
The public upstream stylesheet-cache clear prevents unique snapshot paths from
accumulating cached source styles on reload. RmlUi owns its GPU geometry/font
atlas resources; Shutdown occurs before the host GPU device is destroyed.

Managed preflight is the strict authoring entry point. Raw native open also checks
required elements and upstream diagnostics, but is not a full hostile-document
sandbox. Native model bounds and UTF-8 controls are independently checked. Status
text is RML-escaped; values are not interpreted as markup. The state diagnostic is
bounded to 512 bytes (last diagnostic), not a complete structured upstream log.

### Dependencies and reproduction

No automatic network fetch occurs during normal engine builds or execution.
Approved dependencies were installed/extracted locally:

- RmlUi 6.3, immutable commit `ba95ffe8bfb6370efb2cdcca927eaad4710c5413`,
  `.deps/RmlUi-ba95ffe8bfb6370efb2cdcca927eaad4710c5413`; MIT
- SDL_image 3.2.4, official release archive; static `.deps/ui-install`; zlib
  license, bundled stb PNG/JPEG decode enabled, optional AVIF/TIFF/WebP disabled
- FreeType development headers 2.13.3+dfsg-1+deb13u1 from signed Debian package
  metadata, `.deps/sysroot/usr/include/freetype2`; existing system
  `/usr/lib/x86_64-linux-gnu/libfreetype.so.6` reused
- Existing `/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc`, SC face index2,
  registered as `Noto Sans CJK SC`; SIL OFL. No font download or HarfBuzz install
- Existing SDL3 3.2.28, CMake 3.31.6, .NET10 and approved AOT toolchain reused

Archive/font hashes: `evidence/ui/dependency-sha256.txt`; notices:
`evidence/ui/licenses/`. These optional system font/runtime prerequisites are not
silently bundled into the earlier two-room runnable ZIP. An alternate
`GAL_UI_FONT` must be a matching collection with SC face at index2 in this probe;
a generic arbitrary font path is not yet supported.

With the local dependency builds present:

```sh
bash scripts/build-ui.sh
DOTNET_CLI_HOME=$PWD/.tools/dotnet-home ../android-trim-tools/dotnet/dotnet build managed/GameAuthoringLab.csproj -c Release --no-restore
bash evidence/aot/reproduce.sh
bash scripts/test-ui.sh
```

For physical-display testing, do not source `scripts/ui-env.sh`; supply platform
SDL/Vulkan libraries and run `--ui-demo`. In this runner `scripts/ui-env.sh`
explicitly selects software/offscreen testing. `--validate-ui assets/ui/settings.rml`
needs no graphics context. `--ui-demo` and `--ui-scenario` need `build-ui/libgal.so`.
The default `build/libgal.so` and standalone headless build remain UI-free and
return deterministic unsupported errors for optional UI calls.

The first stylesheet deliberately uses explicit display rules: RmlUi does not
provide browser default block styling for every HTML-looking tag. Rectangular
scroll clipping is tested; rounded button backgrounds do not imply rounded child
clipping. Chinese wrapping uses code-point break-all, not full CJK punctuation
layout.

## Future editor choices

The user deferred Blazor evaluation. Blazor, an engine-self-hosted editor, and a
hybrid remain open alternatives. Any future editor should use the same canonical
scene/resource data and validation/operation APIs as AI authoring. This trial
does not select the future editor implementation or expand game snapshot JSON.

`bash scripts/bootstrap-ui-deps.sh` is the explicit opt-in download/build recipe
for those pinned official UI dependencies. It is never invoked automatically by
the normal build. Existing SDL3/CMake, system FreeType runtime, font, and compiler
must already be present.

Measured uncompressed files in this build: NativeAOT executable 8,572,160 bytes;
UI native library 3,431,584 bytes; ordinary UI-free native library 56,816 bytes;
SDL3 3,566,664 bytes; FreeType 845,736 bytes; font collection 19,484,784 bytes.
The managed executable currently includes strict XML authoring validation even
when native UI is disabled; this noticeably increases its AOT size relative to
the earlier 3.15 MB two-room executable. No optimization/size claim is made.
The earlier UI runtime ZIP included the font. The active size-evaluation package
is now `game-authoring-lab-ui-linux-x64-nofont.zip`; it omits all font bytes and
requires explicit external `GAL_UI_FONT` for rendered UI. Prior archives and the
original system font are retained unchanged. No ZIP was uploaded.


## Font-excluded size evaluation (2026-10-01)

Packaging-only revision requested by the user; no UI/runtime implementation or
rounded-clipping changes. Current ZIP is **7,188,451 bytes** (7.188 MB decimal),
with **20,700,906 bytes** of uncompressed files. Font payload is **0 bytes**.
The earlier font-inclusive ZIP was 22,706,084 bytes.

| Included component | Uncompressed bytes | Compressed payload bytes |
|---|---:|---:|
| NativeAOT executable (host, demo, tests, validation) | 8,572,160 | 3,928,869 |
| libgal.so (engine + static RmlUi + static SDL_image) | 3,431,584 | 1,402,465 |
| SDL3 | 3,566,664 | 1,374,413 |
| FreeType | 845,736 | 422,378 |
| BMP assets and RML/RCSS | 4,234,144 | 42,544 |
| Launcher, readme and license notices | 50,618 | 14,666 |

ZIP container overhead is 3,116 bytes. RmlUi and SDL_image are linked into
libgal.so; their archive sizes are not added a second time or misrepresented as
separate runtime dependencies. System glibc/libstdc++, FreeType transitive
libraries and Vulkan/display drivers remain external. No separate .NET runtime
is needed. This is a prototype measurement, not a minimum stripped release build.

The extracted ZIP passed 1,402 self-test assertions with no font or display.
With `GAL_UI_FONT=/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc` explicitly
supplied, its bundled native libraries passed all 23 scripted UI assertions and
rendered pixels identical to the reference screenshot under software Vulkan.
The font is still required for CJK display; this is intentionally not a complete,
self-contained game distribution. SC face-index2 remains the current restriction.

Exact package breakdown/hash: `evidence/ui/nofont-package-sizes.json`.
Checks: `evidence/ui/nofont-selftest.log`, `nofont-scenario.log`.

## Selected interim XML validation design (2026-10-01)

JSON persistence retains its existing System.Text.Json source-generated contexts.
The UI preflight now uses `XmlReader` to populate a small bounded, read-only
profile model (`UiXmlModel.cs`) instead of `XDocument`. Framework XML parsing
still checks well-formedness, character/entity normalization and namespaces;
existing RML/RCSS validation and source-position diagnostics are preserved.
This is not a custom XML tokenizer or a constant-memory parser: bounded elements,
attributes and text are retained for the profile checks.

The per-document settings remain `DtdProcessing.Prohibit`, `XmlResolver = null`,
65,536-character and 1,024-entity-character limits, with maximum depth 16 and
256 elements. The project retains
`XmlResolverIsNetworkingEnabledByDefault=false`; this trims the unused default
network resolver but does not replace explicit per-document safety settings.

This is the selected temporary design. Once the engine is stable, consider moving
validation closer to native/RmlUi parsing and diagnostics, only after equivalent
strict acceptance/rejection, helpful source errors, resource bounds and lifecycle
behavior have been demonstrated. That migration is not part of this change.

Known inherited limitation: binding-like text split across XML comments, for
example `{<!-- split -->{x}<!-- split -->}`, passes the existing text-node-local
binding check. This revision deliberately preserves that behavior; tightening it
requires a separate contract change and regression coverage.

The archived font-excluded ZIP measurements above are historical and the ZIP has
not been rebuilt. Current host size and merged regression evidence are recorded
in [XML trimming report](XML_TRIMMING.md); do not substitute the new host size into an
old package's claimed total.

## Text-input and ownership follow-on

The [text-input bridge and owner contract](UI_TEXT_INPUT.md) now cover bounded SDL composition events, cancellation/focus lifecycle, candidate-coordinate conversion and exclusive managed UI ownership. Real OS Chinese-IME acceptance remains unverified; generic bindings and dynamic list mutation are a separate next batch.

## Typed model/list follow-on

[UI_BINDINGS.md](UI_BINDINGS.md) describes the schema-driven profile, stable row
identity, revision guards and lifecycle. It reuses the strict authoring tokenizer
and session ownership; inventory and roster are consumers rather than native
special cases. The original settings list remains authored/static.
