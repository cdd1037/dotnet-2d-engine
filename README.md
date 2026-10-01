# .NET 2D Engine

An independent, AI-authoring-first 2D engine prototype: .NET 10 main executable + narrow C ABI + C++ SDL3 GPU platform layer. This is not a Godot fork. Proposed repository name: `dotnet-2d-engine`; the existing `GameAuthoringLab` assembly, namespace and `gal` ABI names remain stable during this milestone.

Start with the [milestone overview](docs/MILESTONE.md) for project structure, current decisions, verification and remaining work. No remote repository is configured by this milestone.

## Authoring principle

This project is primarily designed for AI-driven authoring and iteration. Prefer standard GLSL/HLSL, C#, BMP and explicit versioned JSON over a new language dialect. Verbosity is acceptable when schemas, ownership and behavior are unambiguous. Keep the full workflow accessible through files and CLI commands, with reproducible tests and errors that identify the file, field and cause. Useful abstractions and visual tools remain welcome; essential state must not live only in an editor. A future visual editor is explicitly welcome: it must read/write the same canonical scene/resource formats, stable IDs and validated operations used by AI tools. Human visual edits and AI code/data edits should interoperate without a separate hidden source of truth. Versioned extension metadata and lossless round-tripping need an explicit design before such an editor is built; this milestone does not build one.

## Implemented

- Minimal ordinary-object C# World: stable entity IDs, separate transform parent/lifetime owner/scene, persistent player and pickup/drop semantics
- Main-thread lifecycle, error/status ABI, struct version/size checks, atomic batch rejection, explicit frame abort
- C# world loop and reusable 259-sprite batch, camera keyboard pan/wheel zoom, Space tone and Escape exit
- Direct SDL_GPU backend: Vulkan/SPIR-V, real BMP textures, affine/rotated sprites, ordered per-texture batches, upload cycling, input and audio
- Two-room sample: fixed-step AABB movement, world-stable pickup/drop, room cleanup, JSON save/validate/restart restore
- Offline compiled and embedded SPIR-V; no runtime shader compiler
- Explicit headless contract-validation mode; never substitutes for a graphics test

See the [two-room playable milestone](docs/TWO_ROOM.md) for controls, persistence, CLI scenarios and current limits.

## Resource foundation

[Shared asset roots and texture leases](docs/RESOURCES.md) give authored scenes and UI consistent logical-path validation, copied key mappings, context-owned BMP caching and transactional world resource replacement. `scripts/test.sh quick resources` runs the focused CPU contract checks.

## Input and viewport foundation

[Versioned input and viewport contracts](docs/INPUT_VIEWPORT.md) separate window units from framebuffer pixels, preserve short key/button edges, route RmlUi-consumed input and expose configurable managed action maps. The mission uses the new poll path; legacy callers remain compatible.

## Complete playable loop

`./scripts/run-game.sh` starts **RELAY / Archive Rescue** using the optional RmlUi build: title → start → two-room delivery mission → pause → win/lose → save/load → restart. See [controls, authored mission and verification](docs/PLAYABLE_MISSION.md). The original probes remain available.

## Quick contract test (Linux, no new dependencies)

Use `scripts/test.sh` for the default small CPU-only loop after initial restore.
See [test tiers](docs/TESTING.md) for focused preflight, full JIT and opt-in AOT/UI/graphics validation.

```sh
./scripts/build-headless.sh
# In a restricted shell, set DOTNET_CLI_HOME to a writable folder first.
dotnet build managed/GameAuthoringLab.csproj -c Release
LD_LIBRARY_PATH="$PWD/build-headless" dotnet managed/bin/Release/net10.0/GameAuthoringLab.dll --self-test
LD_LIBRARY_PATH="$PWD/build-headless" dotnet managed/bin/Release/net10.0/GameAuthoringLab.dll --headless --frames 120
```

Requires .NET SDK 10.0.401 (validated runtime 10.0.12). This workspace reused `../android-trim-tools/dotnet/dotnet`; that sibling directory is not part of this repository. Use your own SDK installation and set `DOTNET` for scripts that accept it.

## Graphical build

Install SDL3 3.4.16 from its official release and CMake >=3.20, then:

```sh
SDL3_PREFIX=/path/to/sdl3/install ./scripts/build-sdl.sh
LD_LIBRARY_PATH="$PWD/build:/path/to/sdl3/install/lib" dotnet managed/bin/Release/net10.0/GameAuthoringLab.dll --frames 600
```

The pinned SDL3 3.4.16 runtime and rollback details are in [the upgrade report](docs/SDL_UPGRADE.md). The real SDL_GPU backend compiles/links and native tests pass. Mesa software Vulkan renders through an SDL offscreen surface, with GPU readback and pixel assertions for alpha/camera/resize. Displayed-window UI checks have also been exercised separately, with an intermittent first Reset after scrolling still open; physical-GPU validation is not claimed. Full build/visual validation status is recorded in [validation](docs/validation.md). Vulkan is selected explicitly and startup failure is visible; there is no automatic software/backend fallback. Windows D3D12 and Metal remain unvalidated extension work (HLSL source provided but no DXIL wired yet).

See [world model](managed/WORLD.md), [dependency checklist](docs/dependencies.md), [architecture](docs/architecture.md), [managed usage](managed/README.md), and [validation](docs/validation.md). Existing compiled shader blobs can be rebuilt offline on a Linux machine with libshaderc installed using `python3 scripts/compile_shaders.py`.

## Optional UI experiment

RmlUi 6.3 settings prototype: `docs/UI_PROTOTYPE.md`. Build flag `GAL_ENABLE_RMLUI` is off by default. Bilingual text, scrolling, C# events/model updates and JIT/NativeAOT are tested with software Vulkan; real IME and other-platform acceptance remain open. RML/RCSS are RmlUi-specific formats. Other UI choices and a future Blazor/engine-self-hosted/hybrid editor remain alternatives.

Combined gameplay/menu and lifecycle experiments: [findings and run instructions](docs/GAMEPLAY_UI_LIFECYCLE.md).

Explicit attachment ownership and efficient stable sorting: [semantics and validation](docs/LIFECYCLE_SORTING.md).

## Small authored-scene slice

Flat authored JSON now has a separate validate/load/run path with stable GUIDs, explicit BMP resource mappings and file/JSON-path/entity diagnostics. The two-instance example preserves equal-layer array ordering. This does not generalize the two-room gameplay save or implement prefab inheritance. See `docs/AUTHORED_SCENES.md`.

### Texture regions

The additive [texture-region contract](docs/TEXTURE_REGIONS.md) supports shared
atlas cells, independent asset IDs and sprite flips. `assets/regions.scene.json`
is the small authored v2 fixture. Run `scripts/test.sh quick regions` for its
CPU/resource/schema checks; the linked guide covers GPU readback checks.

### Audio

The optional [SDL_mixer audio module](docs/AUDIO.md) provides WAV/Vorbis clips,
reusable voices, file-streamed music, gain groups and explicit scene cleanup.
Generate the excluded audio fixtures with `python3 scripts/generate-audio-fixtures.py`
before `scripts/test.sh quick audio` or the `--audio-demo` sample. Build native audio
explicitly with `GAL_WITH_MIXER=ON bash scripts/build-ui.sh` after the mixer setup.
The guide records
actual offline PCM checks separately from dummy-device and physical audio acceptance.

### Physics

The opt-in [Box2D foundation](docs/PHYSICS.md) provides fixed-step bodies/shapes,
filters, sensors, copied events and bounded ray/AABB queries. Start with
`scripts/test.sh quick physics`, then `bash scripts/test-physics.sh` for the real
solver after dependency setup. The source-generated `basics.physics.json` fixture
and `--physics-demo` show explicit meter/pixel conversion and scene cleanup.

Managed [frame animation, typed tweens and timers](docs/ANIMATION_TIMING.md) use explicit clocks and lifetime ownership. Try `--animation-demo` or run `scripts/test.sh quick animation`.

The [basic TileMap](docs/TILEMAP.md) loads bounded source-generated grids, culls atlas cells and optionally generates static Box2D collision. Try `--tilemap-demo` or `--tilemap-physics-demo`.
