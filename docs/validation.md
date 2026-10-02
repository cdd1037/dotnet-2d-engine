# Current two-room milestone (2026-10-01)

- JIT and NativeAOT: **1,199 assertions passed**, including the previous foundation suite
- Three warmed-up 1,000-frame loops (raw ABI, world extraction, and room simulation/resource sync/affine drawing): **0 managed allocations** in each measured loop
- Direct SDL_GPU/Vulkan native build and all3 CTest suites passed; updated affine/resource fault contracts passed ASan+UBSan with leak detection disabled
- Real BMP texture tests passed: alpha corners,90-degree rotation pixels, ordered multi-texture runs, missing/corrupt files, active-frame release rejection, stale/cross-context handles and cleanup
- Both JIT/AOT scripted game runs: **371 rendered frames /1,484 sprite draws**, movement/collision/pickup/transition/drop/save/reload;8 texture loads,4 transition/status releases, and final native live count0
- Both launched a fresh process, validated the JSON file and rendered120 restored-room frames; persistent GUIDs matched each saved file and runtime IDs were regenerated
- Restored-room JIT/AOT GPU readbacks are pixel-identical; final actual image `evidence/two-room/aot-restored.png` was visually inspected
- New save/load failure tests cover malformed/missing fields, versions, duplicate IDs, foreign/cyclic relations, missing assets, numerical overflow, relation-depth bounds, collider-incompatible player poses and malformed persistent UI/hidden-item state

Evidence: `evidence/two-room/all-e2e.log`, `jit-selftest.log`, `texture-pixels.log`, `native-ctest.log`, `native-asan.log`, and current `evidence/aot/`. Commands and scope: [two-room guide](TWO_ROOM.md).

The JSON currently stores a runtime world/game snapshot, not a complete arbitrary authored-level/editor format. Rooms/collision rules remain explicit C# in this slice. AI-first authoring uses standard formats and CLI validation. Future editors or derived compressed/binary formats are architectural possibilities, not implemented features.

Rendering remains Mesa CPU software Vulkan through SDL offscreen. Displayed windows, physical input, physical GPU performance, audible sound and Windows/Metal remain unverified. No production-engine edits or external repository pushes occurred.

---

# Earlier first-slice validation record (2026-10-01)

## Current verified results

- Linux x86-64: g++14, .NET SDK 10.0.401/runtime 10.0.12
- Pinned official SDL3 3.2.28 built locally with Vulkan GPU, X11 and ALSA; CMake 3.31.6
- Direct SDL_GPU backend compiles/links with warnings-as-errors; 3 CTest suites pass
- C11 ABI consumer, 100 recreate cycles, wrong-thread/state/capacity/float validation, injected backend failures, frame abort/recovery and transform math tests pass
- ASan + UBSan contract/fault-injection tests pass with leak detection disabled; LeakSanitizer itself fails under this runner's ptrace restriction, so leak detection is NOT a pass
- Managed .NET10 Release: no warnings/errors; 547 assertions covering the ABI and world model
- Both raw ABI loop and world-model update/extract/draw loop allocate 0 managed bytes over 1,000 warmed-up frames × 259 sprites; setup, exceptional paths and native allocations excluded
- 120-frame headless smoke: 31,080 submitted sprites, correctly zero draw/audio counts
- Linux NativeAOT publishes/runs successfully with official Microsoft 10.0.12 packs and locally extracted Debian Clang19; final source/output hashes and logs in `evidence/aot/`

## Actual software rendering

Mesa 25.0.7 lavapipe is explicitly selected via its ICD file. SDL's offscreen Vulkan surface works without a display server. This exercises the real SDL_GPU textured alpha pipeline, not a CPU rasterizer substitute or the headless-validation stub.

- 120 frames / 31,080 sprites / 120 real draw calls
- GPU texture readback: `evidence/graphics/demo.png`, visually inspected
- Pixel assertions pass for reversed alpha ordering, expected RGB blend results, transparent texture corners, camera pan/zoom, and offscreen surface resize to384×288
- One tone is accepted by SDL's dummy audio stream, with audio counter1; this is NOT audible-output validation
- Pixel test source and command: `scripts/validate-software-graphics.py`, `scripts/test-software-graphics.sh`
- Diagnostic capture uses `GAL_CAPTURE_BMP=/absolute/path.bmp`: first frame renders through the same pipeline to a color texture, blits to the swapchain, downloads behind a GPU fence, and saves BMP. It is opt-in and adds no readback work to normal frames

## Not verified

Physical GPU/driver compatibility or performance, a displayed interactive window, physical keyboard/mouse input, minimize/restore, audible speaker output, Windows/D3D12, or Metal. Camera math and SDL surface resize have pixel-level software tests; they do not imply interactive UI validation.

Xvfb was installed after explicit permission, but cannot establish local Unix sockets in this environment (`evidence/xvfb.log`). No alternate socket transport or security-setting workaround was used. SDL offscreen rendering is an independent supported no-display path.

## Build and dependency evidence

- `evidence/dependency-sha256.txt`, `sdl-official-digest-verification.log`: official archive hashes
- `evidence/sdl-prerequisites-download.log`, `debian-packages-sha256.txt`: signed Debian SDL build dependencies, local extraction
- `evidence/sdl-build.log`, `native-sdl-build-tests.log`, `native-final-ctest.log`: actual SDL/native build tests
- `evidence/native-exports.log`: only intended `gal_*` exports
- `evidence/backend-contract-asan.log`, `native-asan-no-leaks.log`: sanitizer tests
- `managed/validation/`: final JIT/world results and earlier offline AOT-analyzer evidence
- `evidence/aot/README.md`: NativeAOT reproduction, official package provenance, source/output hashes, and final results
- `evidence/graphics/`: real software Vulkan captures and pixel assertions

Earlier `--no-restore` AOT attempts and no-display startup failures remain in evidence as historical logs, superseded by the successful restored AOT and offscreen rendering runs. All third-party build/test packages were installed locally inside this project; the production Godot tree was not modified.

## Merged XML trimming verification (2026-10-01)

The selected bounded `XmlReader` profile model is now integrated. Fresh merged
JIT and NativeAOT each pass 8,574 self-test assertions, 23 UI scenario assertions
and 16 combined room/UI assertions. Final UI captures remain byte-identical to
the original validator. The previously executed 7,092-case JIT/AOT differential
checks (zero differences) are reused against verified identical validator source;
they were not redundantly rebuilt after the copy.

The full stripped NativeAOT host is **4,742,560 bytes**, compared with the
same-source XDocument baseline of 5,290,432 bytes. JSON source-generation remains
unchanged. The historical 7,188,451-byte nofont ZIP is not rebuilt by this change.
See [merged evidence](XML_TRIMMING.md) for hashes, logs,
reproduction, limits and exact distinctions between fresh and reused checks.

## Phase-1 playable mission (2026-10-01)

The local phase-1 working tree adds RELAY's complete title/start/play/pause/
win/lose/save/load/restart loop. See [mission instructions](PLAYABLE_MISSION.md)
and [bounded game UI contract](GAME_UI_PROFILE.md). This supersedes aggregate
counts above for this working tree; older evidence remains historical.

Final feature-boundary verification used existing .NET 10.0.401 / runtime 10.0.12,
SDL3 3.2.28, RmlUi 6.3 and Mesa lavapipe software Vulkan:

- Release build: zero warnings/errors; full JIT **8,771 assertions**
- Fresh NativeAOT publish: full **8,771 assertions**, including 185 focused mission
  checks and 12 game-profile authoring checks
- JIT and AOT each: **28** native game UI checks, **49** integrated game-loop
  checks, original **23** settings UI checks, original **16** combined room/UI checks
- Native optional-UI CTest: **3/3**
- Mission CPU checks include 25 repeated playthrough replacements with subscription
  cleanup, repeated room crossings, invalid/missing resources retaining the live
  run, malformed/incompatible saves, failed candidate preparation, timer/input
  boundaries and final-tick simulation limits
- Integrated UI checks include 12 repeated restart/menu cycles, bounded native
  texture counts, stale-generation rejection and final entity/texture/context cleanup
- Captured archive/win/loss/final-title pixels are byte-identical between JIT/AOT;
  settings captures are also identical. Initial title/pause captures intentionally
  differ in Load-button availability: the JIT scenario began with an existing
  checkpoint, while the AOT scenario began without one
- Final stripped aggregate NativeAOT executable: **4,867,040 bytes**. This includes
  the test/probe host, excludes native libraries/assets/fonts, and is not a shipping
  package measurement. No size-ablation or large differential corpus was rerun

Local logs/captures: `evidence/mission/`. Initial AOT `--no-restore` against the
ordinary JIT assets file failed before compilation because it lacked a linux-x64
restore target. The successful publish reused the already-restored isolated
`build-aot-artifacts`, existing clang 19 and existing in-process ILLink host override;
no packages, frameworks or toolchains were downloaded for this phase.

These graphics checks use real SDL GPU rendering and Rml listeners with synthetic
host input and synthetic SDL focus events. They do not establish physical-GPU
performance, physical keyboard timing, audio output or real IME behavior. Displayed
X11 desktop checks are recorded separately from this deterministic evidence. The
settings first-Reset-after-scroll issue remains outside this phase's claim.

### Displayed-window check, same phase-1 build

A separate CUA pass on the cloud X11 desktop with software Vulkan verified:
Title → Start → Escape pause → Save (Load becomes available) → Restart → Pause →
Load (90-second checkpoint restored) → Resume; then actual held-key movement,
E pickup, T door transition, E delivery with 6 seconds remaining, visible win,
Menu back to title, and window close. This exercises the displayed SDL window and
Rml controls; it is still a virtual desktop with CPU rendering and dummy audio,
not a physical GPU/input/audio/IME test.

The title deliberately retains the frozen last room, including its remaining-time
readout; Start always resets from the authored 90-second mission. Hiding or labeling
that title readout is a small outstanding UI polish item. Simultaneous restart
and pause commands in one input batch are deliberately discarded at the first
screen boundary; a fresh Escape or Pause click works after that boundary.

## Resource-root and texture-lifetime batch (2026-10-01)

Following phase-1 commit `105eb06`, the managed resource batch introduces the
[shared path/cache contract](RESOURCES.md). Final source Release build has zero
warnings/errors; full JIT passes **8,842 assertions**, including **71** focused
resource checks. Existing warmed direct/world/room frame loops still allocate
zero managed bytes. Native headless contracts pass unchanged.

Focused software-Vulkan checks with the existing SDL3/RmlUi build pass:

- **27** resource graphics assertions: shared uploads, lease release, wrong-thread
  rejection, native BMP failure after a candidate upload, complete candidate
  rollback, drawing the retained world and zero final native textures
- Three-frame authored composition demo through the same `TextureBank`
- **49** integrated mission and **23** settings UI scenario assertions
- Settings final capture and mission archive/won/lost/final-title captures are
  byte-identical to phase 1's JIT captures

The initial negative BMP fixture changed compression metadata that SDL accepted;
this was a fixture assumption, not a proven engine defect. The final fixture uses
unsupported 7-bit pixel depth: bounded managed header preflight accepts it, native
BMP decoding rejects it, and the test proves upload rollback and live-world
retention. An added source-ancestor check also verifies that explicit-root scene
file reads cannot skip the descendant-link policy.

Local logs/source hashes and captures: `evidence/resources/`. The last change after
the graphics/UI integration run only added the explicit-root scene-source guard
and its CPU test; texture-cache, UI and mission source hashes remained unchanged.
The final focused resource/scene graphics run is repeated against the final build.
No dependencies, native ABI, JSON schema/source-generation options or XmlReader
profile changed. NativeAOT was not republished in this batch; phase 1's AOT results
remain historical and are not a current-resource-batch AOT claim. No physical GPU,
real input/IME/audio or new-platform validation is claimed.

## Input/viewport ABI boundary (2026-10-01)

The additive [v2 input contract](INPUT_VIEWPORT.md) keeps ABI-1 creation and v1
poll consumers intact. Final Release/JIT and freshly published NativeAOT each pass
**8,894 assertions**, including **52** focused managed input/viewport checks and
**71** resource checks. All warmed allocation checks remain zero. Native CTest
passes **4/4**, including the new standalone input event accumulator and C11 v2
size/offset checks. Build/publish completed with zero warnings/errors using the
existing SDK/compiler packs and in-process ILLink configuration; no restore or
package installation occurred.

Against pinned SDL3 3.2.28, RmlUi 6.3 and Mesa software Vulkan, JIT/AOT each pass:

- **28** queued SDL input/Rml consumption checks, including down+up between polls,
  repeated click isolation, text-field capture until release, wheel consumption,
  focus loss/gain, actual SDL resize, synthetic minimize/restore and a resize
  between poll and draw that skips stale projection
- **27** real texture resource ownership/rollback checks
- **28** game UI, **49** integrated mission, **23** settings UI and **16** combined
  room/UI checks
- Existing alpha/order/camera/384x288 resize pixel checks and dummy tone acceptance

JIT/AOT settings and selected mission captures match exactly; archive/won/lost/
final-title mission captures also match phase 1. The final stripped aggregate AOT
executable is **4,990,752 bytes**, excluding native libraries/assets/fonts; this is
not a shipping-package comparison. Local logs, captures and final hashes are under
`evidence/input/`. Large differential and size-ablation research was not rerun.

A separate cloud X11 CUA pass used the displayed mission's new interactive v2 loop:
visible title → Start click → held D movement → short E pickup (CELL LINKED visible)
→ Escape pause with 71 seconds remaining. This validates displayed-window routing
through virtual desktop input with software Vulkan and dummy audio, not physical
hardware, audible output or real IME. A border-resize attempt brought another
window forward and did not produce a verified resized mission view; no displayed
resize/minimize acceptance is claimed. The test process was stopped from its
terminal and the returned shell prompt was verified. Deterministic resize and
minimize/restore coverage above remains distinct from that incomplete desktop check.

## Isolated SDL 3.4.16 upgrade (2026-10-01)

SDL 3.4.16's official source archive digest, zlib notice, new local build/prefix
and retained 3.2.28 rollback paths are recorded in [upgrade details](SDL_UPGRADE.md).
The selected runtime reports `SDL-release-3.4.16-0-gfa2c02bb6`; wrong library search
paths selecting 3.2.28 now fail explicitly before window creation. The new X11
extension prerequisites were verified against signed official Debian metadata and
extracted locally, with no system install. SDL3_image/RmlUi and accepted image
formats remain unchanged.

Final checks against the upgraded ordinary native builds:

- Native CTest **4/4**; strict native build succeeds
- JIT and the identity-verified existing NativeAOT binary each pass **8,894**
  complete CPU assertions, input graphics **28**, resource graphics **27**, game UI
  **28**, mission **49**, settings UI **23**, combined room/UI **16**
- Existing alpha/order/camera/384x288 resize pixel assertions and dummy tone pass
- Selected settings and mission captures are identical between JIT/AOT and the
  prior SDL 3.2.28 input milestone
- Managed sources, JIT output and the **4,990,752-byte** AOT host match the fresh
  input-boundary publish; no managed republish or size-ablation was needed

The graphics Python helper initially opened retained SDL 3.2.28 by absolute path
alongside the upgraded engine's SDL, so its resize lookup saw a separate empty
window list. This was corrected to use the selected SDL SONAME and assert the
minimum version. The final pixel test then passed. Native input/resource/UI
scenarios had already passed against the correct 3.4.16 runtime.

Displayed cloud-X11 CUA verification on 3.4.16: title → Start → held D movement →
short E pickup (CELL LINKED visible) → Escape pause → Resume → fresh Escape pause.
The process was then stopped and the terminal prompt verified. Immediate same-batch
resume/pause is intentionally discarded at the first screen boundary; a fresh
Escape works. Window-manager resize controls were not successfully exercised
through this CUA surface, so displayed resize/minimize, physical high-DPI devices,
GPU/audio hardware and real IME remain unverified. Deterministic SDL resize and
synthetic minimize/restore tests pass separately.

Local evidence: `evidence/sdl-3.4.16/`. Release SDL shared-library size is
**4,003,064 bytes** versus **3,566,664** previously, including newly enabled X11
extensions; this is not a controlled version-only or whole-distribution size claim.
No dependency archives, compiled libraries, fonts or raw evidence are committed.

## Texture-region milestone — 2026-10-01

Final source adds the backward-compatible v2 draw entry point, decoded texture
size queries, catalog regions, sprite flips and strict authored/save version rules.
The [contract and fixture guide](TEXTURE_REGIONS.md) describes sampling and limits.

- Native CTest **4/4**, including C11 `88`-byte draw/`16`-byte info layouts,
  malformed source bounds/flags/version rejection, mixed old/new draw batching,
  stale dimensions and no partial append when a later draw is invalid
- Full JIT and **freshly republished NativeAOT: 8,922 assertions each**, including
  **28** new region CPU checks; warmed extraction/sync/submit remains zero allocation
- JIT and AOT each: region graphics **29**, input graphics **28**, resource graphics
  **27**, mission **49**, combined room/UI **16**; authored atlas scene renders 3 frames
- Region pixel validator: **29** checks covering neighboring-cell edges, X/Y flips,
  rotation, tint, stable alpha order, a one-texel source and camera projection after
  actual offscreen surface resize; rendered fixture was also visually inspected
- Region JIT/AOT readbacks are identical. All six mission captures match JIT/AOT
  and the prior SDL 3.4.16 milestone. Existing legacy textured-alpha/order/camera,
  384×288 resize, texture lifetime and dummy-tone checks remain passing

The original 16×8 BMP fixture is **438 bytes**. Four logical resource IDs share one
upload, and eight ordered fixture sprites form one texture draw run. The stripped
Linux x64 AOT test host is **5,048,800 bytes** (aggregate runtime plus tests, excluding
native libraries/assets/fonts); this is not a whole-distribution size claim.
The publish uses the same existing .NET 10.0.12 packs/Clang setup and in-process
ILLink workaround recorded at the input milestone, without downloads.

Evidence and source/output hashes are local under `evidence/regions/`. Graphics
use offscreen SDL 3.4.16 with Mesa software Vulkan. No new physical GPU, high-DPI,
displayed resize, audio hardware or platform acceptance is claimed. World clipping,
frame animation, atlas packing and pixel-perfect/mipmapped filtering remain outside
this batch. Image codecs and UI preflight restrictions are unchanged.

## Minimal audio milestone — 2026-10-01

SDL_mixer 3.2.4 is hash-verified against its official release metadata, built
unmodified with only WAVE + bundled stb_vorbis (plus internal raw PCM), and linked
statically into the optional SDL build. Full notices and limits are described in
[the audio contract](AUDIO.md). No codec or system package installation was needed.

Final results:

- Native CTest **4/4**, including the additive audio C11 layouts
- Full JIT and **fresh NativeAOT: 8,932 CPU assertions each** (10 new managed
  contract checks; this aggregate does not claim to exercise an audio device)
- JIT/AOT each pass **61 actual offline PCM checks**: exact WAV samples, Ogg decode
  and file streaming, layered/independent gains, pause/resume/stop/restart, finite
  and indefinite looping, ownership retention, 32-voice/64-clip capacity, malformed
  input and encoded/decoded limits, failed-load retention, source-path confinement,
  stale/cross-context handles and teardown
- Three actual world scene unloads release only their audio scopes while a separate
  persistent voice continues mixing. Final retained clips/voices/PCM accounting is
  zero. Warmed mix/state queries allocate **zero managed bytes**
- Dummy-device JIT/AOT tests observe an asynchronously advancing track and verify
  playback controls and cleanup. The displayed demo runs on software Vulkan and
  dummy audio, then exits with clips=0/voices=0
- JIT/AOT region graphics **29**, input **28**, resources **27**, mission **49**
  pass; all six mission readbacks match both runtime modes and the pre-audio
  milestone. Existing alpha/camera/resize/legacy-tone software checks still pass
- The source-only fixture generator was executed in a separate reproduction
  directory; its WAVs match the existing test inputs exactly and its generated
  Vorbis passes all **61** offline assertions

Displayed cloud-X11 CUA checks exercised Space pause/resume, gain increase, F stop,
T restart and Escape exit. The paused/stopped visual colors were corrected and
rechecked in the final build; the terminal confirms zero final clips/voices.
The short E action was sent but its transient lamp was not separately captured;
SFX PCM/replay behavior is established by offline tests. This is virtual desktop
input and dummy audio, not verified speaker audibility, hardware latency, device
hotplug, IME or cross-platform acceptance.

Consistent Release Linux x64 unstripped ELF comparison, same final source and
non-UI configuration: mixer-disabled `libgal.so` **75,176 bytes**, mixer-enabled
**261,400 bytes**, increment **186,224 bytes**. This includes the adapter and
selected mixer/decoder code; it is not a whole-package size. The pre-batch non-UI
library was **74,136 bytes** and UI-enabled library **3,465,944 bytes**; after audio
they are **261,400** and **3,655,664 bytes**. The static mixer archive is **273,318
bytes**. SDL stays **4,003,064 bytes** separately. ELF dependencies show no extra
external codec/encoder runtime. Managed aggregate AOT host: **5,115,456 bytes**,
excluding native libraries, generated assets and fonts.

Audio WAV/Ogg binaries are ignored rather than committed. Build/publish copies
locally generated fixtures; generation is explicit and never launches an encoder
during an ordinary build. Evidence/source/output hashes are under local
`evidence/audio/`. The separate room-asset preparation commit remains independent.

## Box2D foundation milestone — 2026-10-01

The opt-in [physics contract](PHYSICS.md) uses unmodified Box2D 3.1.1, library-only.
The official tag commit was resolved and all **233 archive blobs** matched its Git
tree; archive SHA-256 is pinned. Full MIT notice retained. Samples/task systems/
profilers were disabled, and no extra dependency install was needed.

Final verification:

- Native CTest **4/4** in the real solver and combined graphics builds, including
  new C11 physics struct sizes/offsets
- Full JIT and **fresh NativeAOT: 8,948 CPU assertions each**, including **16**
  layout/unit/source-generation checks; that aggregate alone is not solver validation
- JIT and AOT each pass **284 real Box2D assertions** without SDL/GPU: falling and
  resting/sleeping bodies, wake-on-teleport contact ends, kinematic motion, force/
  impulse, fixed rotation, sensor passage and deletion ends, mask/group filters,
  dynamic-circle rebound, closest rays and initial-overlap policy, broad-phase
  output capacity, event overflow diagnostics, body/shape caps and slot reuse,
  ownership/thread/frame guards, scene unload and late disposal
- Warmed step/body-state/copied-event access allocates **zero managed bytes**
- Two independently loaded fixed-input fixtures repeat their same-binary result.
  The rendered **180-step** source fixture reports **3 contact begins**, **2 sensor
  begins**, and zero final bodies/shapes. Its final poses and initial readback match
  JIT/AOT using the same native library
- Combined-build regressions pass in JIT/AOT: actual audio PCM **61**, region graphics
  **29**, input **28**, resources **27**, mission **49**. All six mission captures
  match both modes and the prior audio milestone

A focused displayed cloud-X11 CUA pass observed the dynamic box resting on its
static floor, T teleport/reset, yellow pause state, upward E impulse/resume and
Escape exit; terminal output confirmed bodies=0/shapes=0. Rendering is still Mesa
software Vulkan, and this does not establish physical GPU/platform acceptance.
The native solver result likewise does not promise cross-platform determinism.

The tests exposed two integration details now explicit in the contract: teleports
wake sleeping bodies, and the pinned closest-ray convenience API ignores initial
overlap. Box2D's intentionally approximate angle-to-rotation math is preserved;
render pose angles are derived from its actual rotation rather than presumed to be
identical to the authored angle.

Consistent final-source Release headless builds: physics disabled **46,248 bytes**,
physics enabled **381,920 bytes**, increment **335,672 bytes**. The Box2D static
archive is **498,984 bytes**. Combined SDL/mixer/physics `libgal.so` is **605,984
bytes**, or **3,991,744 bytes** with the existing RmlUi module. The stripped aggregate
AOT test host is **5,261,152 bytes**, excluding native libraries/assets/fonts. These
are build-specific file measurements, not peak RSS, performance or package totals.

Evidence, source/output hashes and source-tree verification are local under
`evidence/physics/`. Only source/JSON/scripts/notices/reports are committed. The
agreed scope remains basic engine foundations; advanced controller/authoring gaps
and a broader Godot/Unity comparison are deferred as recorded in the roadmap.

## Managed animation/timing milestone (2026-10-01)

[Frame animation, tweens and timers](ANIMATION_TIMING.md) add three managed runtime
files (242 lines at this milestone) plus a small retained-key `TextureBank`
constructor. Their polling API has no native entry point, JSON schema/sourcegen
root, framework/dependency, property callback or global scheduling registry.

Final-source verification:

- Native headless contracts **4/4**, Release build with **0 warnings/errors**
- Full JIT **9,075 assertions**, including **127** new timing/animation assertions
- Boundaries: invalid/NaN/negative/oversized deltas and durations; explicit zero
  behavior; one-frame/one-shot/loop endpoints; one-day jumps; coalesced repeat counts
  up to 86.4 billion; float/vector/RGBA easing; separate clock domains and local
  pause; cancel/restart/dispose/thread/ownership/capacity rules
- Lifetime checks cover repeated scene unload, behavior replacement, failed
  attachment rollback and entity destruction without accessing invalid entities
- Warmed playback, tweens, timers, sprite property updates, retained atlas-key
  synchronization and extraction: **zero managed bytes** in 1,000 measured ticks
- The 16-step graphics fixture uses **4 logical-key leases, 1 native texture upload**,
  releases all textures, and passes **17 software pixel checks**. Paused game values
  remain identical while real-time tint advances; cancellation retains values and
  restart reproduces the earlier image byte-for-byte
- The same fixture runs headlessly with validated cache placeholders and no GPU
  uploads. Existing real resource graphics **27** and atlas graphics **29** pass

Displayed cloud-X11 CUA additionally observed game-clock pause while real-clock
frames/timer lamps continue, restart while paused, resume, repeated cancellation
and Escape exit. The terminal reported **4 key leases, 1 texture upload, 0 live
textures** after that displayed run. This is still Mesa software Vulkan, not a
physical-GPU/platform/performance validation. No IME/font changes were made.

Read-only review identified an interrupted scenario issue: non-drawable windows
could advance the same scripted frame repeatedly. The scenario now waits for a
drawable frame before advancing its script. Double-second boundary precision is
explicit and regression-tested: arbitrary decimal-delta partitioning need not
produce an identical tick count at the exact rounding boundary. No epsilon or
cross-platform deterministic-timing claim is hidden in the API.

No fresh AOT publication was performed for this managed-only batch. The prior
physics AOT evidence remains historical evidence for that source revision, not a
claim that this new animation implementation has already been run under AOT.
Generated screenshots/logs/hashes remain ignored under `evidence/animation/`.

## Basic TileMap milestone (2026-10-01)

The [orthogonal TileMap](TILEMAP.md) adds three managed data/runtime/collision
files (364 lines at this milestone), a separate strict JSON source-generation
root, a 28,355-byte source-only fixture and ordinary test/demo code. It reuses the
existing draw/physics ABIs and `regions.bmp`; no native source or dependency changes
were required. The small `SpriteBatch.Reserve` addition permits stable composition
with ordinary entity sprites, checked against the recorded engine frame capacity.

Final-source results:

- Native headless contracts **4/4**; Release compilation **0 warnings/errors**
- Full JIT and **fresh NativeAOT: 10,256 CPU assertions each**, including **1,181**
  TileMap assertions and the prior animation batch's **127** assertions
- Strict source/load/write/round-trip checks, numeric flip arrays, copied immutable
  runtime state, palette ID reorder, UTF-8/file/dimension/cell/resource bounds and
  precise diagnostics pass. A test exposed an omitted-opacity default being reset
  to zero on an init-only DTO sourcegen path; the optional DTO property now retains
  its initializer, and omitted opacity=1 is checked in JIT/AOT
- Forty varied view rectangles compare the chunk-assisted output with a simple
  global-row-major reference. Exact/subpixel/far/negative bounds, synthetic 2× DPI,
  inactive viewport, map/entity layer ties and frame-budget rejection pass
- Warmed map extraction and combined map/entity append/stable sorting allocate
  **zero managed bytes** over 1,000 measured iterations each
- JIT/AOT each pass **199 real Box2D assertions**. The fixture merges into **7**
  static rectangles; ray/AABB hits map back to cell rectangles, a dynamic circle
  settles on the floor, body/retired-shape budget failures preserve live state,
  and repeated scene unload releases map resources while an independent body
  survives. Retirement is reclaimed only by explicit steps
- The five-camera fixture and 180-step physics fixture each use **one native atlas
  upload** and end with zero live textures/bodies. **18 pixel checks** pass in each
  mode; all **7 captures are byte-identical between JIT and AOT** with the same
  native library. The circle's final Y is **14.60007 m** in both runs
- The fresh AOT host also passes existing region graphics **29** and resource
  graphics **27**. A headless TileMap scenario confirms 67 visible draws, one
  cache placeholder and **zero GPU uploads**

A displayed cloud-X11 CUA check observed resting/resetting the ball, arrow camera
pan, wheel zoom, camera reset, focus interruption/recovery, a window resize and
Escape exit. Terminal cleanup reported textures=0/bodies=0. These are displayed
software-Vulkan checks, not physical-GPU, high-DPI hardware, tiny-window usability
or cross-platform physics determinism acceptance. Minimized/invalid viewport math
is tested synthetically; this displayed check did not test actual minimization.

The freshly stripped aggregate AOT test host is **5,555,280 bytes**. It includes
both the animation and TileMap additions since the previous publication, along
with aggregate tests; this is not an isolated TileMap API or whole-package size.
Native libraries are unchanged. Local evidence and hashes are in
`evidence/tilemap/`; generated images/build outputs remain excluded from Git.

## UI composition/ownership subbatch (2026-10-01)

The [UI text-input bridge](UI_TEXT_INPUT.md) now wraps the unchanged pinned RmlUi
SDL editor with explicit lifecycle, bounds, candidate-coordinate conversion and
exclusive managed session ownership. UI-enabled SDL initialization advertises
inline composition as upstream's GPU backend does. Candidate-list rendering and
OS input-method setup are not implemented by this bridge.

Final-source verification:

- Native CTest **5/5**, including a new SDL-independent caret geometry test for
  2×/nonuniform density, clamping and invalid sizes; C11 verifies the additive
  **832-byte** text-state diagnostic struct and payload offset
- Full JIT and **fresh NativeAOT: 10,265 CPU assertions each**, including **9** UI
  owner/layout checks. Duplicate same/cross-profile sessions, wrong-thread close,
  failed opening, obsolete-wrapper disposal and engine-first disposal are covered
- JIT/AOT each pass **41 queued-SDL composition assertions** through real RmlUi:
  ASCII/CJK preedit replacement and scalar selection; ordinary/direct commit;
  original selected-text restoration on cancel; field-length enforcement;
  malformed/oversized/range rejection; consumed Escape; blur/focus/minimize/restore;
  candidate refresh after resize and same-bounds glyph-width changes; failed reload
  retention; staged autofocus isolation; new context state; and queued old text
  retirement on document replacement and close/reopen
- Both modes retain existing routed input **28**, settings UI **23**, mission UI
  **28**, and modal room/UI **16** checks. In particular, retiring text packets
  preserves the existing pointer/key routing behavior
- **Four text/settings captures are byte-identical between JIT and AOT**. The
  Chinese preedit/selection capture was inspected visually. Rendering remains
  offscreen Mesa software Vulkan with the existing external CJK font

Review and focused reproduction found three lifecycle/geometry defects beyond
initial wiring: pending autofocus could steal the live text context; same-size
text replacement could leave the candidate anchor stale; and text already in the
SDL queue could leak into a replacement document. Regression tests reproduce and
cover each corrected boundary. Text retirement filters only this window's queued
text/editing/candidate events, preserving pointer/key events and other windows.

No real OS Chinese IME, actual candidate window/selection, high-DPI hardware or
cross-platform acceptance was performed. Synthetic SDL editing packets are not a
substitute for those tests. No input-method package, font or OS setting was added
or changed. Generic typed binding and dynamic lists remain the next UI subbatch.

The stripped aggregate AOT test host is **5,575,888 bytes**; the combined optional
UI/audio/physics native library is **4,005,768 bytes** in this Release build. These
are aggregate file measurements, not an isolated API or total distribution size.
Logs, readbacks and output hashes remain ignored under `evidence/ui-text/`.

## Typed bindings and dynamic lists (2026-10-01)

The optional [binding profile](UI_BINDINGS.md) now supports six registered target
kinds, explicit C# model projections and stable-ID list replacement. Inventory and
roster fixtures share the same native path. It adds four C entry points and keeps
all prior ABI records unchanged. The managed API/session is 141 lines, the native
profile implementation/header 206 lines, and the reusable strict authoring profile
273 lines in this source snapshot; integration, tests and fixtures are additional.

Final-source checks:

- Full JIT and fresh NativeAOT: **10,293 CPU assertions each**, including 28 binding
  schema/value/ownership checks; native CTest **5/5**, including C11 layouts
- JIT/AOT each: **59 native binding assertions**, covering typed model/input values,
  escaped plain text, pointer-hit actions, disabled rows/buttons, add/remove/clear/
  reorder/reinsert, stale generation/revision rejection, queued and already-polled
  actions, duplicate/invalid batch retention, direct-C NaN/UTF8/flags/version/ID
  rejection, queue overflow and explicit over-byte-capacity draft diagnostics,
  repeated replacement, a different model/schema,
  constructor/session ownership, disposal and engine-first destruction
- The same fixture verifies failed projections, nested Apply rejection, smaller
  authored text limits, unrelated-text updates preserving active composition and
  replacing the composing target safely. A reviewed out-of-order native ID check
  was fixed before final validation; all registration IDs are validated before
  any DOM lookup uses them
- **Zero managed bytes** allocated across 1,000 warmed unchanged inventory Apply
  calls. Changed lists, caller delegates and the toolkit/render loop are outside
  that measurement
- Existing JIT/AOT UI regression suites remain green: composition 41, routed
  input 28, mission 28, settings 23 and modal room 16
- **Three captures are byte-identical between JIT and AOT**: initial inventory,
  mutated inventory and the separately registered roster. Disabled and selected
  row styling and literal `<north> & compass` rendering were visually inspected

A displayed cloud X11 window was also operated with desktop pointer movement and
keyboard input: row removal, repeated addition, checkbox changes, disabled button
behavior, ASCII text editing, range input, rectangular scrolling and clean exit
were verified. Window-bound synthetic clicking did not consistently update SDL's
pointer motion state, so this acceptance used the full desktop pointer API. This
is a cloud software-Vulkan window check, not a physical GPU/high-DPI or real OS
Chinese IME/candidate-window test. No font, OS setting or dependency was added.

The aggregate stripped AOT test host is **5,737,936 bytes**; the combined optional
UI/audio/physics native library is **4,041,568 bytes** in the same Release config.
These are aggregate files, not total package sizes or independently trimmed module
measurements. The local NuGet proof remains the place to compare consumer roots.
Ignored logs/readbacks are under `evidence/ui-binding/`.

## Local package boundary (2026-10-01)

The [local NuGet proof](NUGET_PROOF.md) separates the reusable runtime from the
aggregate demo/test host and passes independent PackageReference consumers for
empty, authored sprite and typed UI cases. All three pass normal SDK run/build,
trimmed self-contained JIT and NativeAOT; three UI captures are byte-identical.
Full JIT remains 10,293 assertions, typed UI 60, offline PCM 61 and Box2D 284. The
package proof uses no source/project reference to the checkout, no whole-assembly
root and no trim/AOT warning suppression.

Trimmed metadata and AOT maps establish actual managed module absence: empty
drops the engine assembly, sprite retains scene/resource code without UI/audio/
physics, and UI retains bindings without audio/physics/game rules. The full native
profile remains identical at 9,514,720 bytes across sprite/UI consumers. The linked
report separates application, managed engine, native, framework, assets and symbol
bytes and records the narrower Linux/toolchain/font limits.

Review corrected public cache/lease construction and generation/revision status
between reload publication and the first model Apply. External compilation now
rejects manufactured ownership; the independent UI consumer checks revision zero
at that boundary. Neither native ABI nor new runtime features were added.
No packages, binaries, fonts or generated reports enter source control; no remote
package publication occurred.

## Package notices and closure audit

A packaging follow-on copies the complete managed/native license texts into normal,
trimmed and AOT application outputs. All nine output sets retain byte-exact notices.
Runtime DLLs/native DSOs and AOT executables are unchanged; the UI framebuffer
remains identical across modes. The measured report now counts notice bytes
separately (1,065 for empty; 320,261 for sprite/UI). Source-controlled tests check
notice presence and content instead of only checking the NuGet archive.

The [closure audit](ROADMAP_CLOSURE.md) identifies remaining original renderer,
diagnostics, simple movement-example and desktop acceptance work. It does not
reactivate later-deferred async/hot-reload/prefab or expanded Godot feature scope.
World rectangular clipping is the next compact renderer batch; the full requested
Godot/Unity comparison remains a near-completion step rather than a claim that the
local package proof completes the roadmap.

## World scissor boundary (2026-10-01)

The [world clipping contract](WORLD_CLIPPING.md) adds a 32-byte size/version-tagged
rectangle and one submit entry point while preserving prior draw ABIs. Native
validation is atomic, painter order is retained across clip/texture runs, legacy
calls restore unclipped state and framebuffer intersections use 64-bit endpoints.

- Full JIT and fresh AOT: **10,328 CPU assertions each**, including 35 clip checks
- Native CTest **5/5**, including new C11 layout/export and mock-run cases
- JIT/AOT each: **9 graphics assertions + 28 pixel assertions**; all **10 readbacks
  are byte-identical** across modes
- Pixel coverage: half-open edges, alpha painter order, rotation/flip/atlas sampling,
  uniform/per-draw clipping, empty/offscreen regions, legacy reset, TileMap partial
  tiles, unaffected UI and actual offscreen-window resize
- Warmed clipped submissions allocate **0 managed bytes across 1,000 calls**
- Existing JIT region 29/input 28/UI 60 checks pass; AOT UI 60 and clipped TileMap
  scenario also pass. The extracted runtime/demo project boundary compiles with
  the SDK's separate per-project artifact layout for this fresh AOT publication

A displayed cloud X11 TileMap check verified camera pan, wheel zoom, resize with
fixed framebuffer margins and clean exit. No physical GPU/high-DPI claim follows.
The aggregate stripped AOT test host is **5,771,312 bytes**; combined optional
UI/audio/physics native library is **4,041,824 bytes** in this Release build. These
are aggregate files, not a repeated package-size matrix. Evidence is ignored under
`evidence/clipping/`. Remaining renderer/diagnostic/platform work is recorded in
the updated [closure audit](ROADMAP_CLOSURE.md).

## Bounded diagnostics and targeted Reset check (2026-10-01)

The [diagnostic helpers](DIAGNOSTICS.md) add fixed-capacity debug line/rectangle
geometry, a typed bounded log FIFO and explicitly enabled CPU frame/phase timing.
The default paths emit no geometry/log entries and take no timestamps. The sample
uses an existing white atlas texel and submits its scene/overlay in one frame.
No native ABI, serialization root or dependency was added.

- Focused checks: **45** log/timing/overlay assertions and **112** geometry
  assertions, including disabled paths, thread ownership, validation/capacity
  boundaries, timer abort/reset/copied scopes and frame recovery
- Full JIT: **10,485 CPU assertions**, build with **0 warnings/errors**; existing
  headless native contract checks also pass through the usual JIT test script
- Warmed log enqueue/drain and timing scopes/frames, plus debug buffer emission,
  each measured **0 managed bytes** across **1,000** iterations
- Headless and software Vulkan three-frame scenarios both report 3 frames and
  15 submitted quads; actual world draw calls are respectively 0 and 3
- **21 pixel assertions** cover four outline edges, butt-ended horizontal and
  diagonal lines, scene interior, disabled removal and byte-identical re-enabled
  output. Captures remain ignored under `evidence/diagnostics/`
- Read-only lifecycle/validation review found no actionable issues. This
  managed-only batch reuses the previously verified native ABI; a fresh AOT or
  full package-size matrix was not run

Separately, a displayed cloud X11 settings probe changed both player name and
volume, scrolled the list to the bottom, and restored both defaults with the first
Reset click. It repeated successfully with another draft after scrolling back to
the top. Each click used an explicit desktop pointer move. An AT-SPI text insertion
tool error occurred before the first draft; ordinary key events completed typing.
The game was closed after verification. The historical missed click was not
reproduced, so no speculative input fix was made. These two narrow cases do not
establish universal UI/input reliability or actual Chinese IME operation.

CPU duration output includes JIT/capture/presentation within the selected scopes;
it is not a performance benchmark, GPU timing or end-to-end latency measurement.
The next original renderer items are described in [the bounded design](RENDERER_NEXT.md).

## Sprite material boundary (2026-10-01)

The [material contract](MATERIALS.md) adds a 16-byte creation descriptor and
136-byte versioned draw, leaving earlier layouts and entry points unchanged.
The fixed sprite vertex path uses one sampler and 32 copied fragment parameter
bytes. Native context ownership is bounded to 64 pipelines with stale-handle,
thread/frame, all-or-nothing submission and failure cleanup checks.

- Full JIT and fresh AOT each pass **10,526 CPU assertions**, including **41**
  managed material/source/cache checks; builds report **0 warnings/errors**
- Native CTest **5/5**, with added C11 layouts/exports, raw validation/lifetime and
  injected-backend rollback/run-key/copied-parameter/default-restoration cases
- JIT and AOT each pass **8 graphics assertions + 31 pixel assertions**; all
  **five readbacks are byte-identical** across modes. Actual pixels cover default,
  tint, desaturation, parameter changes, alpha order, material scissor, copied
  constants after submit, legacy pipeline restoration and reacquisition
- Existing UI native **60** checks pass in both modes. JIT world-scissor regression
  **9 graphics + 28 pixel** checks pass through the factored default pipeline
- Warmed material submission allocates **0 managed bytes** across **1,000 calls**
- **Eight** offline builder tests cover deterministic/relocated source output,
  ordinary GLSL macro/conditional preprocessing, compiler diagnostics, fixed
  includes, source/compiled bounds and pre-publication failure retention
- Review found and corrected missing compiled-size enforcement and missing output
  copy for extra authored shaders. A third temporary named material's manifest and
  SPIR-V copied byte-exact through an ordinary build; temporary probe files were
  cleaned up. No further lifecycle/ABI/binding finding remained

The stripped aggregate AOT host is **5,919,616 bytes** and the combined optional
native UI/audio/physics library is **4,055,512 bytes** in this Release build.
These are individual build artifacts, not a new distribution-size matrix. Shader
SPIR-V and captures remain generated/ignored; source and digest manifests are
tracked. Runtime hash/profile/header checks are consistency validation of trusted
shader programs, not semantic reflection or a sandbox. Render targets and basic
post-processing remain the next separate original-scope batch.

## Explicit targets and basic post-processing (2026-10-01)

The [target/pass contract](RENDER_TARGETS.md) adds owned RGBA8 pairs and a bounded
ordered frame plan, with a 16-byte target descriptor and 56-byte pass descriptor.
The existing draw ABIs remain intact. All validation precedes execution; final
window UI and legacy one-pass rendering use the same backend path.

- Full JIT and fresh AOT each pass **10,567 CPU assertions**, including **41**
  managed target checks; builds report **0 warnings/errors**
- Native CTest **5/5**, including target/texture ownership, exact 64 MiB virtual
  budget, eight-target cap, independent BMP quota, pass bounds/partitioning,
  stale/context IDs, feedback rejection, mock allocation failure cleanup,
  target-local projection/clip/run boundaries, copied parameters and legacy recovery
- JIT and AOT each pass **7 target graphics assertions + 37 pixel assertions**;
  all **nine readbacks are byte-identical** across modes
- Pixels verify half-alpha red/blue overlap against direct drawing, resampling
  through a second target with RGB/alpha tint, desaturation, alpha 1/255 and
  near-zero/zero handling, retained contents after a rejected later pass,
  target-local camera/scissor, translucent clear, newly initialized transparent
  contents, explicit smaller replacement and final-window-only UI
- Warmed frame-plan calls allocate **0 managed bytes** across **1,000 iterations**
- Fresh AOT material **8 graphics + 31 pixel**, world scissor **9 graphics + 28
  pixel**, and UI **60** regression checks pass through the shared backend
- Read-only ownership/alpha/pass review found no actionable remaining defect.
  Lead review moved default target/resolve pipeline initialization to first target
  creation so a sprite-only host retains the prior startup path. A mock-test
  indentation warning was corrected before final verification

The transparent resolve explicitly costs two RGBA8 images and one extra GPU draw
per offscreen pass. Their combined texel budget is **64 MiB**; pipeline and driver
overhead are additional. Alpha zero produces transparent black, and every positive
stored alpha is at least 1/255. The tests establish the bounded UNORM behavior,
not HDR, linear-light correctness or precision preservation across arbitrary pass
chains. A blend flag alone would not maintain the current straight-alpha shader
profile for alpha tint and arbitrary fragment calculations.

The stripped aggregate AOT host is **5,961,088 bytes** and the combined optional
native library is **4,073,072 bytes** in this Release build. These individual files
are not a repeated package footprint matrix. Canonical resolve GLSL and its
generated text header are tracked; standalone resolve SPIR-V, builds and captures
remain ignored. Prior sprite shader bytes are unchanged. Basic renderer slices
are now delivered at the software Vulkan boundary; simple tile movement and the
named platform/UI/distribution acceptance gates remain in the closure audit.


## Tile movement acceptance (2026-10-01)

The [sample-only tile movement fixture](TILE_MOVEMENT.md) closes the original
flat-floor character example using the existing input, TileMap and Box2D APIs.
There is no runtime API/ABI change or new serialization context.

- Full JIT: **10,594 assertions**, including **27** clock/input checks; strict
  native mock/backend/C11/input/text-geometry checks remain passing
- Real Box2D movement: **62 assertions**, including both walls, jump/landing,
  sensor enter/exit, filtered queries and 12 complete resource-safe restarts
- Software Vulkan: **600 rendered frames**, eight pose/state checkpoints and
  five captures with **30 pixel checks**; final bodies/shapes/retired/textures zero
- Displayed cloud X11: actual held movement crosses the goal, jump is visible and
  lands, pause ignores a held movement key, right-wall collision holds, focus loss
  preserves an airborne pose across more than 30 seconds, focus return/restart
  recovers floor standing, and Escape closes the game

The existing external font and software Vulkan driver were reused. No new AOT
publish was run for this game-only batch; the preceding target ABI milestone
remains the last engine AOT evidence. This does not establish hardware GPU,
Windows/macOS, real IME or physical audio acceptance. Package measurements were still stale at this fixture commit; the separate
refresh below now covers the current runtime.


## Latest local package proof refresh (2026-10-01)

Fresh local packages from runtime/native source `d07a6ab` pass all nine independent
empty/sprite/UI combinations of framework-dependent, trimmed self-contained JIT
and NativeAOT. UI captures are byte-identical across modes; no compiler/publish
warnings were emitted. Required notices match package bytes in every output.

The full managed DLL is **337,920 bytes**. Unused new renderer/diagnostic and
existing optional module types are present in the untrimmed positive control and
absent from trimmed/AOT consumer roots. Empty AOT contains no engine assembly
nodes. Expected authored-scene/UI roots remain in their respective consumers.
The compiler also rejects direct construction of texture/material caches/leases
and render-target stores/wrappers.

The eight native DSOs total **9,546,224 bytes**, a **31,504-byte** increase over the
earlier proof. All nonempty outputs contain byte-identical copies and retain
audio/physics/clipping/material/target exports. Empty has no native package.
AOT executable bytes remain **1,147,432 / 2,647,704 / 3,176,744** for empty/sprite/UI;
complete AOT output totals are **1,148,497 / 12,515,604 / 13,045,585** including
notices, native dependencies and authored assets as applicable.

The final managed README correction changed only its archive entry; the executed
assembly was unchanged. Metadata/root/export checks were rerun after strengthening
the proof. An initial export assertion needed to normalize ELF symbol-version
suffixes (`@@GAL_1`); this was a measurement-script correction, not a missing API.
See [full measured categories](NUGET_PROOF.md) and [remaining public APIs](PACKAGE_API_NEXT.md).
The proof still requires the documented modern Linux baseline, driver and external
font. It does not test real Chinese IME, hardware GPU, physical audio or other OSes.

## Public optional-module package boundary (2026-10-01)

Existing animation/timing, audio, physics and TileMap operations are now public
experimental managed APIs. Native-backed constructors, interop buffers, planning
helpers and source-generated contexts remain internal. Audio/physics results use
immutable copied views; the bounded physics event span has explicit next-step/
close lifetime. No C++ source, native ABI, dependency or asset format changed.

- Full repository JIT remains **10,594 assertions**, with no build warnings
- Real Box2D **284**, offline PCM **61** and movement solver **62** checks pass
- One independent ordinary PackageReference consumer passes **156 checks** in
  both JIT and fresh NativeAOT, including actual solver/PCM work, public sourcegen
  tile load/write, animation clocks, ownership cleanup and copied-value lifetimes
- Its warmed 1,000 physics steps/state reads allocate zero managed bytes
- Compiler-negative cases reject **seven** forged owner constructions, **seven**
  unsupported null sites, mutable state writes and raw interop/sourcegen access
- Fresh minimal empty/sprite/UI trimmed publishes still remove unused modules;
  the combined AOT map positively roots the intended modules and no UI bindings
- The complete native profile is byte-identical to the preceding proof:
  **9,546,224 bytes**. Combined AOT executable: **2,871,624 bytes**; complete output
  including native files, caller assets and notices: **12,747,314 bytes**
- Full managed DLL: **352,768 bytes**. Minimal sprite/UI trimmed managed DLLs
  remain **98,816 / 74,752 bytes**, and empty retains no engine assembly

The combined consumer submits three headless frames. Its graphics/device paths
were not reclassified as displayed pixels or audible output. The earlier renderer
matrix was not repeated; existing feature evidence remains linked separately.
A read-only ownership/API review found no actionable defect. The final README-only
archive correction retained the exact executed assembly. Measurements, negative
compiler diagnostics and AOT maps are in the ignored external module-proof folder.
See [the public contract and categories](PACKAGE_API_NEXT.md). Real IME, hardware
GPU/audio, Windows/macOS and broader distribution acceptance remain open.

## Bounded common images and document-owned UI images (2026-10-02)

Feature source: `23c191e976d3073c6da1af09746e1a160c3b8640`. The subsequent documentation
commit does not change executed code. Linux x64, .NET SDK 10.0.401/runtime 10.0.12,
SDL 3.4.16, SDL_image 3.2.4 embedded STB and RmlUi 6.3; offscreen Vulkan/lavapipe
with the existing external Noto CJK font. Dependencies were reused, not rebuilt.
See [accepted formats, paths, budgets and ownership](UI_IMAGES.md).

- Final complete JIT: **11,053 assertions**, zero build warnings/errors, **3 s**
  for the incremental native contracts + managed build + aggregate invocation
- One fresh, isolated NativeAOT publication: zero warnings/errors; its complete
  CPU self-test also passes **11,053 assertions**
- Focused image authoring/metadata CPU coverage: **241 common-image +106 UI-image
  checks**, included in both aggregates rather than a separate full matrix
- JIT and that fresh AOT each pass **107** actual software-rendered image checks:
  intrinsic/explicit sizing, nested paths, PNG alpha/zero-alpha, JPEG/BMP,
  backgrounds, source deletion/edit retention, hidden/hover decode failures,
  failed first open and retry, pending replacement, 12 alternating reloads,
  world/UI isolation and normal/engine-first cleanup
- All **23 JIT/AOT BMP captures are byte-identical**. The initial image readback
  was also visually inspected; pixel assertions run inside the test itself
- Existing typed binding native regression: **60 checks**, including composition,
  repeated lists, generation/revision retirement and zero-allocation unchanged
  batches. Existing game UI native: **28 checks**. Native UI monotonic-clock
  regression also passes with the per-document renderer ownership
- Headless, SDL and UI native builds each pass **6/6 CTest** contracts, including
  new image metadata and C/native OpenImages boundary checks. Direct actual
  BMP/PNG/JPEG native GPU uploads end with zero world textures
- Native metadata helper passes ASan+UBSan. LeakSanitizer is unavailable under
  this runner's ptrace configuration, so no leak-sanitizer pass is claimed
- The isolated incremental-build workflow still passes: unchanged **0.105 s**
  and C#-only **0.114 s**, both with zero compiler invocations; all six contracts
  run. Its fresh headless build took **5.107 s**. These are one-run observations
- The opt-in XML differential project builds again with the extracted source-file
  helper and common-image metadata linked; its large research corpus was not run

Integration exposed and fixed a real URI-lifetime bug before acceptance: preloading
an already-absolute image URL through RmlUi's JoinPath twice removed its leading
slash. Manifest preloading now uses the same relative-source/document pair as
ordinary RML/RCSS resolution. Per-document render managers ensure upstream cache
keys and queued releases cannot accumulate across arbitrary reloads. Synchronous
reload may recreate pipelines and regenerate font resources; no reload-latency
benchmark or hard process-memory sandbox is claimed.

Logs, fresh output and captures are local ignored evidence under
`evidence/ui-images/`, `build-image-aot/` and `build-image-aot-artifacts/`.
Bootstrap script changes received shell syntax checks; no dependency download,
font installation or complete native rebuild was performed. The ordinary package
consumer matrix, Windows/macOS, hardware GPU/audio and real OS IME were not rerun.
SVG and WebP remain excluded; the existing NanoSVG backend was assessed as a
possible later restricted-icon path, not enabled by this batch.

## 2026-10-02 — optional bounded SVG checkpoint

`GAL_ENABLE_SVG=ON` adds the official pinned RmlUi SVG plugin with LunaSVG 3.5.0
and bundled PlutoVG 1.3.1. It is confined to file-backed bound-UI SVG elements and
decorators; world images and fixed UI profiles remain unchanged. See
[accepted source profile, budgets and build](UI_SVG.md).

Verified on the final SVG sources:

- Release compile: zero warnings/errors; full JIT aggregate **11,217 assertions**,
  including **164** managed SVG contracts
- Optional SVG native build and headless: **7/7 CTest** contracts; the new source
  validator is standalone and requires no SDL/LunaSVG
- Managed/native validator parity: **125** meaningful fixtures with zero
  accept/reject mismatches, including XML normalization, colors, path arities,
  references, expansion and cumulative transform bounds
- Native strict warning build, ASan/UBSan canonical contracts and **60,000**
  deterministic mutated-source cases pass. LeakSanitizer remains unavailable
  under ptrace; this is a robustness smoke, not exhaustive fuzzing
- SVG software Vulkan integration: **179 assertions** with actual pixel readback.
  Checks alpha/tint without double premultiplication, gradients, clipping, masks,
  internal use/currentColor, group opacity, non-square viewBox and aspect policy,
  snapshots surviving changed/deleted sources, hidden invalid sources, direct
  native validation bypass attempts, missing manifests, all three raster budgets,
  live-state preservation, repeated reloads and owner/engine-first cleanup
- A direct C ABI replacement reuses the exact same RML/SVG paths while the old
  document is still live. Old red pixels remain red after a source edit; the next
  blue snapshot publishes blue after another edit and deletion of the files.
  This exercises render-manager-scoped plugin cache identity independently of
  the managed generation-specific staging paths
- Software density **1×/1.5×/2×** and real SDL window resizes pass. No physical
  high-DPI display or platform-specific device acceptance is claimed
- SVG-disabled UI build: **12 assertions**, verifying the explicit disabled
  diagnostic, preserved live model/action/pixels and a successful raster retry
- Existing image integration **107**, bound UI **60**, game UI **28**, and UI clock
  regressions pass. The existing image snapshot/ownership rules are preserved
- The incremental headless workflow still passes all seven contracts on every
  invocation, including zero compiler calls for unchanged and managed-only work
- Invalid SVG-without-RmlUi configuration and a changed upstream patch-input
  digest fail closed. Shell syntax, Python syntax and diff-whitespace checks pass

Measured Linux binary cost against an otherwise equivalent SVG-disabled Release
build with mixer and physics enabled: stripped `libgal.so` grows from **3,474,496**
to **4,269,616 bytes**, an increase of **795,120 bytes (~776 KiB)**. Zlib level 9
compression grows by **356,423 bytes (~348 KiB)**. This includes the native source
validator and host hooks, beyond the earlier plugin-only assessment. It is not a
cross-platform package-size guarantee.

The source archive was reused from the prior verified official release and its
SHA-256 checked again. LunaSVG builds with system-font discovery disabled; full
LunaSVG/PlutoVG and embedded FreeType/stb notices are retained. No font install or
unrelated dependency rebuild was required. Native SVG code links only with the
opt-in flag; the existing package proof explicitly retains SVG-off behavior.

Local logs and captures are under `evidence/ui-svg/`; the SVG native build is
`build-svg/`. The canonical parser tests are `tests/svg_validation_tests.cpp`,
with an optional tracked mutation harness in `tests/svg_validation_mutation.cpp`.
This checkpoint covers JIT/native tests; no fresh SVG-enabled NativeAOT run is
claimed at this checkpoint. Physical GPU/IME/audio, the
package-consumer matrix and other operating systems were not rerun.

## Camera follow managed checkpoint (2026-10-02)

The additive [camera helper](CAMERA_FOLLOW.md) retains the existing camera ABI,
uses caller-supplied timing and has no native or serialization changes.

- Release build passes with **zero warnings/errors**
- `scripts/test.sh quick camera` passes in **2 s** on this runner: all **7/7**
  existing native contracts, three headless frames and **137 camera assertions**.
  The headless graph was already current; no native object was recompiled
- The same **137 assertions** pass in an isolated external managed executable
  referencing the rebuilt public `Dotnet2D.Engine.dll`, without linked runtime
  source, the friend assembly name or a native library. This is a public-assembly
  check, not a repeated NuGet package matrix
- Coverage includes fixed-target partitions, zoom/DPI/negative-world bounds,
  small and zero-size worlds, exact-fit/float edge rounding, snap/teleport,
  game pause/time scale/real time, invalid and extreme numbers, subnormal timing,
  long-step residuals and zero allocation in 2,000 warmed bounded update/snap pairs
- CLI/help registration, shell syntax and diff-whitespace checks pass

Focused testing found and fixed exact-fit interval cancellation and tiny-delta
smoothing cancellation before acceptance. Local logs are under
`evidence/camera-follow/`. This checkpoint does not claim a camera graphics
scenario, physical device result or a fresh NativeAOT publication; the combined
feature pass is recorded separately.

## 2026-10-02 — combined SVG and camera NativeAOT gate

After the SVG (`a1d0557`) and camera-follow (`1059ea6`) implementation commits,
the clean combined tree passed the final gate:

- Release/JIT and a **fresh NativeAOT** publication each pass **11,354 assertions**,
  including SVG **164** and camera-follow **137** contracts
- That AOT executable passes the optional SVG software-render suite **179**,
  raster-image suite **107**, typed binding suite **60**, game UI suite **28**, and
  SVG-disabled behavior suite **12**
- All **32 SVG** and **23 raster-image** JIT/AOT BMP captures are byte-identical
- SVG-enabled native and headless **7/7 CTest** remain green; the optional native
  UI clock test also passes. Enabled/disabled builds export identical public ABI
  symbol sets; this batch adds no native ABI record or entry point
- The AOT compiler emitted no trimming/AOT warnings. Publication uses isolated
  `build-svg-aot-artifacts/` intermediates and `build-svg-aot/` output, leaving the
  ordinary JIT and previous image-batch AOT outputs separate

The default MSBuild out-of-process ILLink task host hit this runner's existing
`MSB4216` limitation before native code generation. Reusing the already prepared
in-process official ILLink task override and clang-19 toolchain completed the
fresh publication without product-source changes. Runtime/compiler packages were
restored from the existing local cache and an offline source; no additional
package download or dependency rebuild was needed.

The successful publication was equivalent to:

```sh
export DOTNET_CLI_HOME="$PWD/managed/.dotnet-home"
export NUGET_PACKAGES="$PWD/../android-trim-tools/nuget"
LD_LIBRARY_PATH="$PWD/.tools/aot/usr/lib/x86_64-linux-gnu" \
  ../android-trim-tools/dotnet/dotnet publish managed/GameAuthoringLab.csproj \
  -c Release -r linux-x64 --no-restore -m:1 -nr:false \
  -p:UseSharedCompilation=false -p:PublishAot=true -p:StripSymbols=true \
  -p:RuntimeFrameworkVersion=10.0.12 \
  -p:CppCompilerAndLinker="$PWD/.tools/aot/usr/bin/clang-19" \
  -p:CustomAfterMicrosoftCommonTargets="$PWD/evidence/aot/inprocess-illink.targets" \
  -p:ArtifactsPath="$PWD/build-svg-aot-artifacts" -p:UseArtifactsOutput=true \
  -o "$PWD/build-svg-aot"
AOT_APP="$PWD/build-svg-aot/GameAuthoringLab" scripts/test.sh aot
GAL_NATIVE_DIR="$PWD/build-svg" source scripts/ui-env.sh
GAL_UI_SVG_CAPTURE_DIR="$PWD/evidence/ui-svg/aot" \
  build-svg-aot/GameAuthoringLab --svg-graphics-test
```

The runner-specific SDK/cache/compiler paths and ILLink host override above are
local setup, not new product dependencies or required settings on ordinary hosts.
Evidence and binary hashes are in `evidence/ui-svg/combined/`; raster AOT captures
are in `evidence/ui-images/svg-aot/`. `build-svg` enables SVG, mixer and physics;
`build-ui` was intentionally kept SVG-off for the opt-out checks. These results
continue to be software-rendered Linux evidence, not a cross-platform, hardware
high-DPI, physical audio/IME or package-consumer matrix.

## Bounded frame events and explicit clip selection (2026-10-02)

The next pure-managed slice adds copied `FrameMarker` metadata, bounded borrowed
event results with due/dropped counts, and `FramePlayer.Play` for explicit clip
selection. Same-clip selection preserves phase and state; overflow advances fully
without replaying dropped events. See [the full timing contract](ANIMATION_TIMING.md).

Verified against `70b7a81` plus this frame-event working tree:

- Release build: **zero warnings/errors**
- Focused animation suite: **218 assertions**, including **87 frame-event checks**
  for stable copied order, boundaries and loops, one-shot completion, zero/paused/
  scaled/real time, earliest-event overflow and counts-only mode, restart/cancel/
  disposal, repeated and switched `Play`, wrong-thread/null rejection, and explicit
  resource preparation without hidden loads
- Maximum legal catch-up counts **353,894,400,004,096** due markers with only the
  selected capacity materialized; decimal modulo remainder/count alignment and
  binary-exact split/lumped event streams are covered
- **Zero managed bytes** in 2,000 warmed marked-player updates that include clip
  switches, repeated selection, overflow and event reads. Existing retained-bank
  synchronization/extraction allocation checks also remain green
- The same **87** event contracts pass in an isolated ordinary managed executable
  referencing the rebuilt public engine DLL, without the friend assembly name,
  linked runtime source or native library. This is a public-assembly reachability
  check, not a repeated NuGet package matrix
- One final `scripts/test.sh jit` passes **11,445 assertions**, including the
  existing **137 camera** checks; native CTest remains **7/7**. Its incremental
  native graph was current and recompiled no native objects. Reported tier wall
  time was **2 s** on this runner; this is one invocation, not a benchmark

Local build, focused/public-consumer and final JIT logs plus source/binary hashes
are in `evidence/frame-events/`. This adds no native ABI, serialization root,
dependency, callback graph, queue, blending or property-track system. No fresh
NativeAOT, rendered animation scenario, hardware/device or package matrix was run
for this managed-only change; the preceding combined SVG/camera AOT evidence
remains historical evidence for that earlier tree.

## Transactional runtime TileMap cell editing (2026-10-02)

Verified against `4e0f7ff` plus this managed-only edit implementation. The new
`TileMapInstance.CreateEditable` factory retains the fixed palette's textures;
`SetCells` applies at most 4,096 distinct cell replacements atomically, publishing
immutable per-instance snapshots and synchronously replacing attached collision
when the multilayer solid union changes. Read-only construction keeps its prior
used-keys-only residency. See [the full contract](TILEMAP.md).

- Final Release/JIT: **11,546 assertions**, zero warnings/errors, existing native
  **7/7 CTest** passed. One final aggregate invocation reported **11 s** on this
  runner; the current native graph recompiled no native objects. This is one
  measured invocation, not a benchmark
- TileMap CPU: **1,282 assertions**, including **101 new edit checks**. Coverage
  includes sorted layer indexing, two editable instances and loaded-asset/snapshot
  isolation, shared untouched layers, copied caller edits, chunk empty/refill on
  both axes, row-major culling, all invalid batch fields, repeated targets,
  4,096/4,097 bounds, no-op identity, thread/lifetime guards and scene cleanup
- Distinct-file residency checks prove editable setup retains a previously unused
  palette texture, read-only setup does not, edits perform no loads/releases,
  resident data survives source deletion, nonpalette resources stay unretained,
  and failure to prepare an unused palette texture leaves existing leases usable
- Real Box2D TileMap suite: **243 assertions**, including **44 edit checks**.
  Immediate ray/AABB results and the original collision wrapper follow replacement;
  old IDs stop resolving, borrowed rectangle snapshots survive, unchanged solid
  unions preserve IDs, opacity-zero layers participate, and non-unit placement,
  meter scale and filtering remain coherent
- Body and live-plus-retired shape limits explicitly require **old + candidate**
  headroom. Failures preserve the old Map, queries and world state before native
  mutation. Tests cover retry after releasing body space or explicitly stepping,
  128/129 collision rectangles, oversized physics geometry, empty/refill, scene
  unload, and closed/reopened physics worlds. No implicit step occurs
- A separate ordinary executable references the rebuilt public
  `Dotnet2D.Engine.dll` and passes **16** editing/physics/ownership checks, without
  linked runtime source or the friend assembly identity. It is a public-DLL
  reachability test, not a repeated NuGet package matrix
- The new software-Vulkan edit fixture passes **341 managed assertions** and
  **54 actual-pixel assertions** across three readbacks: add/remove/replace,
  previously unused palette entries, X/Y/XY flips, both 15/16 chunk boundaries,
  unchanged sibling pixels and exact full-frame restore. All three were visually
  inspected
- Existing five-frame map and 180-step physics scenarios pass their **18 pixel
  checks**. Final ball Y is **14.60007 m**, the expected generated floor remains,
  and both scenarios finish with zero live textures/bodies. Initial/final physics
  readbacks were also visually inspected
- Warmed extraction after editing remains **zero managed bytes** across 1,000
  iterations; existing warmed allocation contracts remain green. Edit planning,
  immutable copies and collision construction intentionally allocate setup data

Logs, captures and source/binary hashes are under `evidence/tilemap-edit/`; the
initial focused CPU/physics logs are under `evidence/tilemap-editing/`. The external
consumer's first rerun lacked the runner's writable `DOTNET_CLI_HOME` and stopped
before compilation; repeating with the existing local CLI home passed.
Native allocation-failure rollback is not fault-injected; any shapes created
before such a failure can remain retired until the caller's next explicit step.
Process-level out-of-memory recovery is not guaranteed. The change adds no native
ABI, JSON schema/root, dependency, terrain/tileset tooling or save integration.
No fresh AOT, package matrix, hardware/device or cross-platform result is claimed;
the earlier SVG/camera AOT evidence remains historical for that earlier tree.
