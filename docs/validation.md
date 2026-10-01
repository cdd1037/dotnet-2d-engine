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
