# Optional pause-menu loop

A small ordinary C# package consumer: the starter's existing game/physics, plus
an explicit RmlUi pause/resume/restart panel. UI commands are polled every outer
frame, gameplay runs only in drained fixed steps, and UI projection/drawing remain
active while paused. This is application code, not an engine lifecycle framework.

From the source checkout, use the focused proof (existing packages/tools only):

```sh
DOTNET=/path/to/dotnet PACKAGE_FEED=/path/to/matched-local-feed \
  GAL_UI_FONT=/path/to/NotoSansCJK-Regular.ttc bash scripts/test-loop-ui.sh
```

It copies this consumer outside the repository, then copies the current
`templates/Starter/StarterGame.cs`, `FixedStepInput.cs`, white PNG and its MIT
license. These are application-owned example files, not linked engine source.
Both `Dotnet2D.Engine` and `Dotnet2D.Native.Linux.x64` are ordinary PackageReferences.
The native package must contain the generic UI bridge; matching preview version
strings alone do not establish matching package contents.

The script builds/runs JIT with the already-prepared software Vulkan sysroot,
queues public SDL events, checks commands/ownership, and validates three actual
pixel readbacks. No physical input, device/GPU performance, real IME or fresh AOT
acceptance is claimed. `ScriptedChecks.cs` contains the fixture-only SDL ABI and
finite test clock; the live path does not use those helpers.

For a live run, copy the consumer and those starter files, provide your own local
NuGet.Config with the two packages, restore explicitly, then run without --check:

```sh
dotnet restore Sample.csproj --configfile /path/to/NuGet.Config
dotnet build Sample.csproj -c Release --no-restore
GAL_UI_FONT=/path/to/NotoSansCJK-Regular.ttc \
  dotnet bin/Release/net10.0/Sample.loop-ui.dll
```

Use a supported display/Vulkan environment. Fonts are never bundled or installed;
change the RCSS font family if you choose another licensed compatible font. Keep
all native package libraries and output license files together. WASD/arrows move,
Space changes color, Escape/Pause toggles pause, and T/Restart resets while keeping
the pause choice. Closing the window releases UI, game, texture and engine owners.

The two authored trees stay separate: RML describes the menu; game code describes
physics and scene presentation. RmlUi filters consumed input before the normal
`InputActionMap` bindings, while explicit pause suspension prevents all gameplay
steps. Escape deliberately opts out of UI consumption. There is no automatic
World-entity input dispatch or assumption that displaying a panel pauses a game.

See the [loop recipe](../../../docs/GAME_LOOP_RECIPE.md) for clocks, interpolation,
input priority, coordinate conversion, reset ownership and full source counts.
