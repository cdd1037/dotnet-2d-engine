# UI ownership and text-input bridge

This subbatch makes the existing RmlUi/SDL composition connection explicit and
bounded. It is not a new IME, candidate renderer, word segmenter, UI framework or
font system. Generic typed bindings and dynamic lists remain the next UI batch.

## Audit result

The original integration already registered RmlUi's `TextInputMethodEditor_SDL`,
forwarded `SDL_EVENT_TEXT_EDITING`, and passed committed text to RmlUi. Chinese
font rendering and a scripted committed string had been tested, but composition
lifecycle and candidate geometry had not.

Concrete gaps addressed here:

- Our standalone SDL initialization omitted the inline-composition capability hint
  used by RmlUi's own SDL GPU backend
- Rml's caret is in framebuffer pixels, while SDL's candidate anchor is in logical
  window coordinates. Passing it through unchanged was incorrect at non-1× density
- Focus/minimize changes did not explicitly cancel provisional composition or stop
  the SDL text-input session
- Upstream's editor object keeps composition offsets across context changes; a
  fresh editor is now used for each active context/boundary
- A composition temporarily replacing a selected range needs the original text
  and selection restored on cancellation, before ordinary committed insertion
- Candidate geometry could remain stale when programmatic text replacement changed
  glyph widths without changing the input box's size
- A staged native document with autofocus could capture the global text handler
  before publication, damaging the retained live document if staging later failed
- Managed settings/game session wrappers could replace or close each other's UI,
  and late disposal after engine destruction was not supported

## Ownership and document publication

`UiSession` and `GameUiSession` now derive from `UiSessionOwner`. An engine permits
one live managed UI owner, including a not-yet-loaded owner. Duplicate same-profile
or cross-profile construction rejects before native mutation. Dispose an unused or
failed owner to release its reservation. Failed first native opening destroys its
candidate UI; a failed reload preserves the existing document.

Disposal closes a successfully opened native UI, releases its owner reservation,
and is idempotent on the creating thread. Engine destruction invalidates the owner;
late disposal is safe. An obsolete disposed wrapper cannot release or close a newer
owner. Frame/thread guards still apply. Raw native callers are responsible for
coordinating their own use of the single context UI; managed ownership is not a
new native capability/permission boundary.

Staged documents use `FocusFlag::None`, so preview/update/render cannot claim the
live text handler. Successful publication retires the old composition/context,
focuses the new document explicitly and advances the existing generation. The
closed managed authoring profiles continue to reject autofocus. The staging rule
also protects raw native loads that contain it. Successful publication and close
retire already-queued SDL text/editing/candidate packets for this window, so they
cannot enter a newly focused document. Pointer/key packets and other windows are
preserved; ordinary focus changes do not flush future packets from the same poll.

## Composition and input policy

The engine-owned adapter wraps the **unchanged upstream SDL editor** for preedit
range updates. Before first preedit it snapshots the focused value and selected
character range. It validates each editing event, then lets upstream render the
provisional text and selection. Empty editing packets restore the original range;
subsequent `SDL_EVENT_TEXT_INPUT` performs normal Rml insertion exactly once.
Direct committed input without a preceding empty-edit packet is handled as well.

Blur, Escape during composition, window focus loss, minimization, explicit model
replacement, document replacement and close cancel provisional text. Cancellation
restores the original selected text rather than deleting it. Destruction callbacks
never access a widget that is already being torn down. Focus restoration retains
an existing field focus but does not restore the cancelled preedit; window restore
alone does not manufacture keyboard focus.

While composing, key-down/up events are consumed before ordinary Rml editing and
gameplay routing. Escape cancels; the OS owns candidate navigation. Raw physical
input remains inspectable through the existing explicit raw-input opt-in. The
existing neutral/held-key suppression is retained. Text events received while the
window is unfocused or minimized are ignored.

Accepted preedit is at most 127 UTF-8 bytes and 64 Unicode scalars, with valid SDL
selection bounds. Committed packets are at most 255 UTF-8 bytes. Invalid UTF-8,
controls, oversized packets and invalid ranges preserve the last accepted preedit
and increment a diagnostic counter. Provisional text may temporarily exceed the
field's committed maximum; normal Rml insertion enforces the authored maximum on
commit. The current settings model retains its 32-scalar/127-byte payload
contract; edited values that exceed the copied action buffer produce the existing
explicit UI overflow diagnostic. These limits are adapter policy, not claims of universal IME/grapheme support.

## Candidate anchor

`UiSystem` converts the Rml caret/line height using separate horizontal and vertical
window/framebuffer ratios, floors the origin, rounds height upward and clamps to
the valid window. It sends a one-window-pixel-wide caret area, not a whole control
rectangle. Zero/invalid viewport geometry stops text input and records a diagnostic.

Caret changes, resized/repositioned text bounds, viewport/density changes and explicit
model replacement refresh the anchor after Rml layout. Unchanged areas are not
resent every frame, and the SDL text-input session is not restarted each frame.
This updates SDL's requested anchor; the operating system decides how its actual
candidate window is positioned.

UI-enabled builds default `SDL_IME_IMPLEMENTED_UI` to `composition` before video
initialization, matching upstream. Explicit host/environment overrides are retained.
The engine does not advertise or implement candidate-list rendering; an override
claiming that capability is outside this bridge. No OS input-method setting or
package was changed.

References: [SDL candidate-area coordinates](https://wiki.libsdl.org/SDL3/SDL_SetTextInputArea),
[SDL IME capability hint](https://wiki.libsdl.org/SDL3/SDL_HINT_IME_IMPLEMENTED_UI),
and the pinned [RmlUi SDL GPU initialization](https://github.com/mikke89/RmlUi/blob/ba95ffe8bfb6370efb2cdcca927eaad4710c5413/Backends/RmlUi_Backend_SDL_GPU.cpp)
and [SDL platform/text editor adapter](https://github.com/mikke89/RmlUi/blob/ba95ffe8bfb6370efb2cdcca927eaad4710c5413/Backends/RmlUi_Platform_SDL.cpp).

## Diagnostics and verification

Additive `gal_ui_get_text_state` returns an 832-byte, version-1 snapshot; existing
ABI-v1 structs remain unchanged. `UiSessionOwner.TextState` exposes the same data:
active context/composition/SDL session/window flags, scalar selection, logical
candidate rectangle, framebuffer caret, preedit length, rejected-event count, and
bounded value/diagnostic text. The value may contain provisional text and is not a
committed application model. Existing copied action queues remain the application
boundary. Test-only commands queue SDL editing/commit events; they are explicitly
not evidence of an operating-system IME producing those events.

```sh
scripts/test.sh jit
# Prepared optional RmlUi build and existing external font:
source scripts/ui-env.sh
GAL_UI_TEXT_CAPTURE_DIR="$PWD/evidence/ui-text/visual-jit" \
  dotnet managed/bin/Release/net10.0/GameAuthoringLab.dll --ui-text-test
```

`--ui-owner-test` runs CPU ownership/layout checks without a UI build. The native
geometry test also requires no SDL/display and covers 1×/2×/nonuniform dimensions,
out-of-window caret clamping and invalid sizes. The focused renderer test covers
preedit replacement/cancel/commit, selected-range restoration, field limits,
malformed events, Escape routing, focus/minimize/restore, same-bounds text updates,
failed reload retention, staging autofocus, new contexts and engine-first disposal.

The boundary includes fresh JIT/AOT and existing UI/input regressions, recorded in
[validation](validation.md). Screenshot inspection confirms that queued Chinese
preedit and selection render through the existing font. **Real Chinese OS IME,
candidate selection/placement, high-DPI hardware and cross-platform acceptance remain
unverified.** No new font, input-method package or OS configuration was installed.
