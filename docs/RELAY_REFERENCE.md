# RELAY public-package experiment

2026-10-02. The existing **RELAY / Archive Rescue** game now runs as an ordinary
[PackageReference application](../packaging/consumers/relay/README.md). It is a
development experiment for testing the public authoring APIs, not a release or
production reference standard. The [small starter](../templates/Starter/README.md)
remains the minimal example.

## Run

With the existing .NET 10 SDK, prepared local Engine/native packages, Linux x64
graphics prerequisites and a licensed Noto Sans CJK SC font:

```sh
export DOTNET=/path/to/dotnet
export GAL_UI_FONT=/path/to/NotoSansCJK-Regular.ttc
# Defaults to the prepared build-packages/relay-feed directory.
export PACKAGE_FEED=/path/to/local/feed
bash scripts/run-game.sh --save-file /path/to/relay-save.json
# CPU-only checks do not need a font or graphics:
bash scripts/run-game.sh --rules
```

The launcher performs ordinary restore/build/run of the sample project. It does
not rebuild native code, install prerequisites or publish packages. Version
management is deferred while this remains an experiment.

The entire `packaging/consumers/relay` directory can also be copied outside the
checkout and built with normal `dotnet restore` and `dotnet build`; it contains
all app code/assets and references no engine source or sample executable. Assets
resolve beside the executable. A checkpoint defaults to the invoking directory.
The old fixed-profile in-repository probe remains under
`scripts/run-legacy-game.sh` for historical regression work.

## What is preserved

The mission objective, 90-second simulation timer, movement speed, collision
geometry, pickup/drop/doors and save schema are unchanged. Original procedural
room art is bundled as lossless PNGs, and the mission JSON is unchanged. The
same title/play/pause/win/loss/save/load/restart screens now use the public generic
UI model and typed commands. A fixed action strip keeps buttons steady when a
notice wraps.

Rules, screen eligibility, save source-generation contexts, resource mappings and
input bits remain app code. Saves restore relationships through public
`World.Reparent`/`SetOwner`; no old sample internals were exposed as engine APIs.
RELAY processes one UI command per frame and discards duplicate gestures as an
application policy. The loop still exposes input, commands, simulation, UI and
render ordering explicitly.

Invalid saves, missions or candidate assets retain the usable live game. A
post-commit cleanup failure is reported as such rather than claiming rollback.
World/UI/texture disposal remains explicit. No new engine API or native change
was needed for this migration.

## Functional checks

```sh
# Focused copied-app JIT check (default):
RELAY_PROOF_ROOT=/tmp/my-fresh-relay-check bash scripts/test-relay-packages.sh
# Optional existing trim/AOT boundary check, when those tool packs are prepared:
RELAY_PROOF_ROOT=/tmp/my-fresh-relay-full bash scripts/test-relay-packages.sh full
```

The app has 932 CPU rule/save/lifecycle assertions and 118 real SDL/Rml scenario
assertions. The scenario covers all screens, save/load, first-loss Menu,
pickup/drop/delivery, focus/neutral boundaries, stale/double-click rejection,
failed replacement retention, twelve restart cycles and final cleanup. It uses
a temporary checkpoint and restores any output assets temporarily corrupted by
the failure tests. Six captured screens also have pixel checks. These checks are
for development, not part of the ordinary run workflow.

The migration's full check passed in JIT, trimmed self-contained JIT and NativeAOT,
with identical captures and 22 pixel assertions. The in-repository aggregate
passed 11,726 assertions. Those results record the migration check. Day-to-day changes use the focused JIT
check; full publication modes are optional.

This is Linux x64/software Vulkan evidence. Physical GPU/input/audio, real IME,
minimization and wider platform/distribution acceptance remain unverified. No
public NuGet publication, release pipeline or versioning overhaul is introduced.

The managed-boundary iteration uses `PollInputFrame` and
`DrawWithOverlay(camera, batch, SpriteCommand[])` in `RelayHost`. Its game-owned
legacy rule masks, input routes, ownership and screen policy are unchanged. New
projects should follow the [typed starter entry](SAFE_AUTHORING_BOUNDARY.md).
