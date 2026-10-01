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
