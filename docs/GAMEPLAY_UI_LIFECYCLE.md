# Gameplay/UI and lifecycle experiments

This page preserves the original experiment and its verification. The later
[owned-attachment API](LIFECYCLE_SORTING.md) implements the explicit cleanup
boundary proposed below; plain `Behavior` assignment remains non-owning.
For current input routing, see [input and viewport contracts](INPUT_VIEWPORT.md).

## Result and scope

A small combined host now runs the existing two-room game with the existing RmlUi
settings document. `--room-ui-demo` is the interactive experiment;
`--room-ui-scenario` is deterministic verification. The original `--room-demo`
and `--ui-demo` remain separate. No ECS, component registry, reflection factory,
new game serialization schema, editor, or third-party dependency was added.

Interactive controls: WASD/arrows move; E/F pick up/drop; T uses a nearby door.
Escape opens settings and Escape closes/resumes even with a text input focused.
Closing the window exits. After dismissal, release all mapped keys before gameplay
resumes. This intentionally conservative rule prevents a key held while typing
from becoming a movement or action on the first gameplay frame. The experiment
does not expose F5/F9 save/load; those remain in the original room demo.

## Findings

1. The old UI demo drew the room but never advanced it. A zero input snapshot did
   not prove that gameplay would remain still while typing. The new scenario
   proves movement first, focuses the actual Rml input, commits text through Rml,
   then supplies nonzero movement, pickup and transition keys for 120 paused
   ticks. Player position, game actions and behavior ticks must remain unchanged.
2. Native input filtering previously swallowed Escape along with gameplay keys
   when an input had focus. It now preserves Escape as a host/menu command;
   movement/actions and wheel remain suppressed while typing.
3. Pausing only input is insufficient: behavior updates, held-item rotation and
   simulation time also need a policy. The experiment pauses the simulation at
   the managed host. A small `ModalGameInput` gate clears pending fixed-step
   actions and fractional accumulation at both boundaries, then requires a
   neutral input frame. This prevents queued pre-pause pickup and post-pause
   catch-up bursts. The rendering/UI loop keeps running.
4. `World` correctly defers newly spawned entities to the next update, skips an
   enemy destroyed before its turn, preserves player/carried pickup on room
   unload, and destroys persistent owned pickups on explicit player destruction.
5. `World` clears a destroyed entity's Behavior but does **not** invoke IDisposable
   or detach an event handler held by an external publisher. An executable test
   demonstrates a callback still arriving after unload. Explicit host-owned
   disposal before unload or behavior replacement removes it. Twenty repeated
   scene cycles verify zero remaining subscribers/entities. This limitation is
   documented, not hidden behind a test that silently assumes automatic cleanup.
6. Closing settings destroys its native context and queued actions. Reopening
   uses a new generation, starts without old text focus/queued actions, and rejects
   stale-generation commands. The scene-switch scenario retires UI before
   unloading the old room, then opens a fresh UI without an old action leaking in.

## Original next-design findings

Keep ordinary entities and typed C# objects. A game can compose several small
behavior objects in an explicit order without adopting a general ECS. Before
shipping subscribed or resource-owning components, define one attachment owner
and symmetric attach/detach semantics (including replacement, scene unload,
explicit destruction and failed creation). A disposable host/scene scope is the
smallest sufficient first step. Do not add automatic disposal to `World.Destroy`
without specifying callback order, exception policy and reentrancy: current
teardown preflights transforms before mutation, and arbitrary callbacks could
break that guarantee.

Keep pause/modal routing as host policy, separate from Rml's focused element.
A future nonmodal UI needs explicit consumed-input/capture semantics; “any input
has focus” is only a prototype shortcut. A production key router should track
raw physical state separately from gameplay snapshots and suppress held actions
until release. This probe's all-mapped-keys neutral latch is intentionally coarse.

## Reproduction and evidence

Use the existing tools/dependencies. Build managed Release and the optional UI
native build as described in `UI_PROTOTYPE.md`. For software rendering:

```sh
source scripts/ui-env.sh
../android-trim-tools/dotnet/dotnet managed/bin/Release/net10.0/GameAuthoringLab.dll --self-test
../android-trim-tools/dotnet/dotnet managed/bin/Release/net10.0/GameAuthoringLab.dll --room-ui-scenario
build-aot/GameAuthoringLab --room-ui-scenario
```

Logs are in `evidence/gameplay-ui/`. The headless test count is 1,452 (50 new
lifecycle/modal assertions). The combined graphical scenario has 16 assertions.
The original 23-assertion UI scenario remains a separate regression.

Evidence is synthetic managed host input plus real Rml text/listener handling and
SDL software Vulkan rendering. It does not claim physical-keyboard event timing,
real CJK IME composition, displayed-window interaction, physical GPU performance,
Windows or Metal validation. No package/ZIP was regenerated or uploaded.

Verified on 2026-10-01: Release compilation has zero warnings/errors; JIT and
NativeAOT each pass all 1,452 self-test assertions, the original 23 UI assertions,
and the 16 combined room/UI assertions. Original JIT/AOT UI pixel captures remain
identical. Native UI-build CTest passes 3/3 targets. A three-frame bounded combined
interactive-host smoke run also exits cleanly (no physical interaction claimed).

Follow-on implementation: `LIFECYCLE_SORTING.md` defines and implements the explicit
owned-attachment option discussed above. Plain Behavior assignment remains non-owning.
