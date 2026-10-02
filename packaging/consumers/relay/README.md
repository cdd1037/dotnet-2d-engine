# RELAY / Archive Rescue: public-package experiment

An ordinary .NET 10 development example using the public engine APIs. Copy this
whole directory anywhere: it contains its app code and assets and uses two
standard PackageReferences. It has no source/project references back to the
engine checkout. This is an experiment, not a packaged release.

The unchanged mission: carry the amber cell from Garden Workshop through the
east door and press E at the upper-right relay before 90 simulation seconds
expire. Title, play, pause, win/loss, save/load and restart are application code.

## Build and run

Prerequisites: .NET SDK 10.0.401 (validated runtime 10.0.12), the prepared local
Dotnet2D.Engine and Dotnet2D.Native.Linux.x64 development packages, Linux x64
host graphics/Vulkan prerequisites, and a licensed Noto Sans CJK SC font.
Fonts are not bundled. Native platform requirements are documented in the
native package README. The generic UI bridge must be present in those packages.

```sh
export GAL_UI_FONT=/absolute/path/to/NotoSansCJK-Regular.ttc
dotnet restore Sample.csproj --source /absolute/path/to/local/feed
dotnet build Sample.csproj -c Release --no-restore
dotnet bin/Release/net10.0/Relay.dll --save-file /absolute/path/to/relay-save.json
```

For an offline restore, the normal NuGet cache/local sources must also contain
Microsoft.NET.ILLink.Tasks 10.0.12, used by AOT compatibility analyzers. The source checkout's `scripts/run-game.sh`
is a convenience restore/build/run entry.

Assets resolve beside the executable, independently of working directory and
`GAL_ASSET_ROOT`. `EngineAsset` copies the mission, seven lossless PNG versions of
the existing procedural art, and RML/RCSS into build/publish output. MIT license
applies to this app code/art.

## Controls

- WASD/arrows move; E picks up/delivers, F drops, T uses a nearby door
- Escape pauses/resumes; Space starts/resumes/restarts on the applicable screen
- F5 saves during play/pause; F9 loads into pause; T from pause/results returns to title
- Mouse buttons offer Start, Pause, Resume, Save, Load, Restart and Menu
- Release held gameplay keys after start/resume/load/restart before moving
- Close the window to quit; `--frames N` is an optional finite smoke run

The timer follows bounded 60 Hz simulation ticks. Pause/focus loss/dropped backlog
consume no mission time. Final-tick delivery wins a tie. A versioned checkpoint
must match the authored mission. Bad save/mission/asset preparation retains the
current game. Saves use same-directory temporary replacement; this is not a
power-loss/fsync guarantee.

## Development checks

`--rules` runs 932 CPU assertions without opening native graphics. `--check` adds
118 scripted SDL/Rml assertions and framebuffer captures. It uses a unique
temporary checkpoint, never `--save-file`; set `RELAY_CAPTURE_DIR` to choose the
capture folder. Tests temporarily corrupt and restore the copied output
mission/art to check failure retention, so use a writable disposable build.

From the checkout, `scripts/test-relay-packages.sh` runs the copied application
outside the repository in JIT. Its optional `full` mode repeats the functional
checks under trimmed JIT and NativeAOT. These are opt-in development checks;
ordinary play needs no extra verification workflow. Physical input/GPU/audio/IME
and minimization acceptance remain separate.

## Source map

- `Program.cs`, `RelayHost.cs`: initialization, frame order, command eligibility, failures and ownership
- `MissionGame.cs`, `RoomGame.cs`: original mission rules, fixed-step/neutral policy and replacement
- `ScenePersistence.cs`, `SceneRecords.cs`, `RelayValidation.cs`: app-owned source-generated save schema
- `RelayInput.cs`, `RelayAssets.cs`: input actions and resource mappings
- `RelayView.cs`, `assets/ui/`: generic model projections, typed commands and RML/RCSS
- `RuleChecks.cs`, `ScenarioChecks.cs`, `SdlInput.cs`: development acceptance checks

The old fixed `GameUiSession` mission stays in the repository as a historical
probe. It is not referenced by this app or promoted into the engine package.
