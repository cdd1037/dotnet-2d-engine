# Ordinary C# starter

Copy this directory, then edit it like any other .NET console project. It consumes
only public `PackageReference` APIs, with no source links, project references,
friend assembly name, code generation or automatic component discovery. The
`GameAuthoringLab` namespace is the current experimental engine namespace.

This is an editable application skeleton, **not a new engine framework**.
`FixedStepInput.cs` is a small application-owned policy helper. The complete loop,
input mapping, game state, physics step, draw and disposal remain visible in C#.

## Prepare once

- Install an existing .NET 10 SDK (verified with 10.0.401)
- Obtain **both** locally built `0.1.0-preview.1` packages:
  `Dotnet2D.Engine` and `Dotnet2D.Native.Linux.x64`
- Put their `.nupkg` files in this directory's `packages/`, or edit the one source
  in `NuGet.Config` to your local feed
- Restore explicitly, then build/run without another restore

```sh
mkdir -p packages
# Copy both preview .nupkg files from your prepared local feed into packages/.
dotnet restore Starter.csproj --configfile NuGet.Config
dotnet build Starter.csproj -c Release --no-restore
dotnet run --project Starter.csproj -c Release --no-build -- --self-test
dotnet run --project Starter.csproj -c Release --no-build -- --headless
dotnet run --project Starter.csproj -c Release --no-build -- --headless --physics
# Requires a supported graphics/display environment:
dotnet run --project Starter.csproj -c Release --no-build
dotnet run --project Starter.csproj -c Release --no-build -- --physics
```

These are **local-feed preview** packages, not a promised public NuGet download or
stable SDK. The native binary is **Linux x64 only**, requiring GLIBC_2.38,
GLIBCXX_3.4.29, CXXABI_1.3.15 and the host ELF loader/libgcc/libm. It does not support
Windows, macOS, mobile or musl/Alpine. Graphics needs the host's Vulkan loader/ICD
and a supported display (this build supports X11/offscreen); device audio needs
ALSA. Consult the native package's included README/manifest for exact provenance.

Keep all eight `runtimes/linux-x64/native/` libraries together. Do not copy just
`libgal.so`, and do not remove output `licenses/` when redistributing the app.
The library's relative RPATH resolves its bundled dependencies; don't add a path
to some other engine build to make a normal package consumer work.

**No fonts are bundled or needed by this starter.** Before adding RmlUi, explicitly
set `GAL_UI_FONT` to a compatible font you installed and have permission to use.
For current CJK examples use a font with the required Chinese coverage. Verify
that file exists before opening the UI; do not depend on the engine's historical
Linux system-font fallback. The variable selects a file, not a download or font
license. Font compatibility/glyph coverage still require an actual UI check.

The default native package has **SVG OFF**. BMP/PNG/JPEG textures work through the
current raster API; UI SVG requires a separately built, explicitly selected SVG-on
profile. Source support is not proof that the default binary enabled that feature.

## Files you own

- `Program.cs`: entry point, explicit input/fixed loop, pause/restart, rendering
- `StarterGame.cs`: ordinary game state and optional physics-to-presentation example
- `FixedStepInput.cs`: bounded frame delta, accumulator and one-shot input queue
- `StarterChecks.cs`: dependency-free checks you can keep or move to a test project
- `StarterOptions.cs`: strict CLI parsing and finite headless mode
- `assets/white.png`: original 1×1 white placeholder, covered by the included MIT `LICENSE.txt`
- `Starter.csproj`: two package references and explicit `EngineAsset` copying

Arrows/WASD move. Space changes the square's color. Escape toggles pause. T resets
position, velocity, action count and simulation step count, preserving pause.
There is deliberately no automatic save. Author defaults and player progress are
different data; add a versioned save format when your game needs one.

## The frame contract

1. Poll once, map actions once. Gameplay bindings honor UI consumption by default;
   only Escape deliberately bypasses it here
2. Process pause/restart on the outer frame. Always update the stopwatch baseline
3. Begin a fixed-step frame, then **drain** `TryTakeStep` before beginning the next
4. Apply game input, advance exactly one physics step if selected, copy the pose
   from meters to pixels, then update game presentation state
5. Draw once when drawable; dispose all owners on the creating thread

The helper caps each accepted delta at 0.1 seconds and accumulates dropped time in
`DroppedSeconds`. That intentionally drops excessive hitch time rather than doing
unbounded catch-up. It is not an authoritative multiplayer/replay clock. Returned
`TimingStep.RealSeconds` is also capped. Supply another explicit clock if a menu,
network deadline or other real-time system needs uncapped elapsed wall time.

A press/release survives a frame with no fixed step. The first catch-up step gets
those edges; later steps get held input only. Multiple taps coalesce, matching
`ActionState`; this is not an ordered event queue. Pause, focus loss and no drawable
area clear simulation debt and pending edges. Restart also clears them and skips
simulation for that outer frame. Focus re-entry uses `InputActionMap`'s existing
neutral-control policy. These choices are application policy, so change them here
if your game should simulate while minimized or accept gameplay input while paused.

Headless mode uses a deterministic supplied delta and defaults to 120 frames; it
has no live keyboard/display and explicitly ignores their absence. `--frames N`
sets a positive frame limit; desktop has no limit unless selected. This test mode
checks native contracts and submission, not actual raster decoding/pixels or input.

`--physics` demonstrates a free-moving Box2D box with no gravity or terrain. The
physics world owns the pose; presentation reads it after `Step`. It is not a
platform character controller and provides no grounded/sliding/moving-platform
semantics. Keep simulation and presentation units separate. The example converts
at explicit boundaries with `PhysicsScale(64)` and uses the same fixed delta in
the helper and `PhysicsSettings`.

## Paths and troubleshooting

Assets are rooted at `AppContext.BaseDirectory/assets`, not the shell's current
working directory; `EngineAsset` copies them to build/publish output. `--assets
PATH` explicitly selects another root. This starter intentionally does not read
`GAL_ASSET_ROOT` implicitly. Logical resource names remain relative slash paths;
no traversal or descendant symlinks. `white.png` is preflighted before rendering.

- `NU1101`: check both exact-version packages and the local-only `NuGet.Config`;
  no remote package source or tool install is added automatically
- `ASSET_MISSING` / `ASSET_PATH`: read the reported root, code and logical path;
  check copied files or the explicit `--assets` argument
- Missing `gal`, wrong ELF class or missing symbol: check architecture, matching
  packages, preserved native siblings and native package system prerequisites
- Vulkan/display startup error: establish a working host graphics environment;
  try `--headless` to separate native/logic checks from rendering setup
- UI/font error after adding UI: verify `GAL_UI_FONT`, readable file, compatibility
  and glyph coverage; no third-party font is installed or distributed here

`--self-test` checks timing/edge/reset policy, strict options, native physics,
restart and owner cleanup using the packaged runtime. For an independent-public-
consumer proof, the source checkout provides `scripts/test-starter.sh`. Daily
iteration is JIT. Trimming/NativeAOT is a separate milestone validation; nothing
here invokes an AOT publish or installs a compiler implicitly.
