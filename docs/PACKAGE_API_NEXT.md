# Remaining public module boundary

The current package is a working local distribution proof, not a complete public
engine SDK. Four implemented module families still lack ordinary consumer entry
points: animation/timing, audio, physics and TileMap. Tests inside the repository
have friend-assembly access and therefore do not establish external usability.
The fresh empty/sprite/UI matrix proves delivery/trimming for its explicit roots.

A bounded next batch can expose existing behavior without adding features or a
native ABI. The effort is a moderate managed API cleanup plus one external sample,
not a new subsystem. Keep the version prerelease and make no stability promise.

1. Expose `FrameClip`, `FramePlayer`, typed `Tween` factories/results, `TimingStep`,
   timer/scope types and their small clock/state/easing enums. Keep internal
   ownership fields and factory-only generic constructors internal; decide the
   playback base's extension boundary deliberately
2. Expose `EngineHost.OpenAudio`, audio group/session/scope/clip/voice operations.
   Keep native config structs and P/Invoke internal. Replace raw ABI state return
   values with small immutable public views. Convert clip/voice primary constructors
   to explicit internal constructors before making their classes public, so a
   caller cannot manufacture ownership from arbitrary IDs
3. Expose `EngineHost.OpenPhysics`, validated settings/body/shape definitions,
   unit conversion, world/body/shape/scope operations, copied events and queries.
   Keep interop buffers internal and provide typed immutable result views. Apply
   the same constructor restriction to native-backed body/shape wrappers. Preserve
   existing caps, creating-thread ownership and explicit stepping
4. Expose the bounded authored TileMap loader/records and immutable map/placement/
   view/instance/collision results required for an ordinary consumer to load, draw
   and attach static collision. Keep source-generated context, collision planning
   helpers and native construction details internal

Use one independent ordinary SDK `PackageReference` consumer, copied outside the
checkout, to exercise all four families: advance an atlas clip/tween/timer, load a
small authored grid, step a body onto its collision and query it, and mix a tiny
generated WAV through an offline voice. Verify pause/dispose/cleanup and a missing
asset error. This establishes API reachability and composition, not every feature
boundary again. No physical audibility claim follows from PCM mixing.

Run that combined consumer in JIT and one fresh trimmed NativeAOT build. Retain
the existing module-specific unit/solver/PCM suites and check the three minimal
consumer roots still trim unused modules after visibility changes. A separate
three-mode matrix for every type is unnecessary. Record the combined consumer's
root-dependent size separately from the existing empty/sprite/UI rows.

The existing native payload can be reused if its bytes/ABI do not change. No new
dependency, public feed, release pipeline, package credential, runtime framework
or feature-specific native profile is implied by this work.
