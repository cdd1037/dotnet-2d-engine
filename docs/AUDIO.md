# Bounded audio with SDL_mixer

This optional module uses **SDL_mixer 3.2.4** on the verified SDL **3.4.16** baseline.
It provides decoded clips, reusable voices, seekable file streams, three gain groups
and explicit main-thread lifetimes. It does not add a managed realtime callback,
background asset loader, audio ECS or platform-support promise.

## Dependency and selected features

The official tagged source archive is pinned in `scripts/mixer-env.sh`:

- [Official 3.2.4 release](https://github.com/libsdl-org/SDL_mixer/releases/tag/release-3.2.4)
- Revision `release-3.2.4-0-g72a81869`
- `SDL3_mixer-3.2.4.tar.gz`, 5,161,139 bytes
- SHA-256 `182a07c745375e113dc740d43964ff21b0be29f29f59876c4dbc4db3d32f6901`,
  verified against the official release asset digest

`build-mixer.sh` explicitly builds a Release PIC static library with WAVE and the
bundled **stb_vorbis 1.22** decoder. It disables external Vorbis/Tremor, Opus, FLAC,
MP3, MIDI, module music, game-music-emu, WavPack, AIFF, VOC and AU, plus examples,
upstream tests and vendored dependency fetching. The internal raw PCM path remains
available for decoded clips. No external codec library or encoder is linked into
the engine. SDL_mixer's source is unmodified.

Retained full notices: [SDL_mixer zlib](SDL3-MIXER-LICENSE.txt) and
[stb_vorbis MIT/public-domain alternatives](STB-VORBIS-LICENSE.txt). We use the MIT
alternative for stb_vorbis and retain the full upstream dual-license notice.
These join the existing SDL/runtime/platform notices; they are not a complete
binary-package license audit. Upstream source portability does not establish
Windows/macOS/mobile support for this engine. This slice is verified on Linux x64.
The bundled stb decoder has its upstream limitations, including no chained Vorbis
streams or old floor-0 Vorbis; this is not an all-Ogg-format reader.

## Ownership and behavior

`EngineHost.OpenAudio(offline: false)` lazily opens one `AudioSession`. Device mode
uses SDL's default playback device and lets its mixer thread pull audio. Offline
mode opens no audio device: `Mix(Span<float>)` generates real interleaved float32
stereo at 48,000 Hz. Device mixers cannot be read through `Mix`. Device format may
differ from the requested stereo/48 kHz hint; `State` reports the actual mix format.
CMake and the SDL/UI build helpers default to mixer-disabled; select
`GAL_WITH_MIXER=ON` explicitly when using those helpers. The headless-only native
build has an explicit unavailable-module stub. A graphics
capable native build may create a `GAL_HEADLESS` context then open just this audio
module, as the device-free and dummy-device checks do.

- `LoadClip(root, logicalPath)` synchronously decodes into retained float PCM
- `CreateVoice(clip, Music/Sfx)` creates a reusable, initially stopped track
- `OpenStream(root, logicalPath, Music/Sfx)` gives one voice its own seekable file
  descriptor and decoder. It does not preload the whole encoded file or decoded PCM
- `Play(loops)` restarts from the beginning. `0` plays once, positive values add
  repeats, and `-1` loops indefinitely. Pause/resume preserves playback position;
  stop is immediate. There are no fades or callback completion events in this slice
- Voice gain and group gain are finite `[0,1]`. Effective gain is
  `voice × Music/SFX × Master`. Changing one category leaves the other unchanged.
  Muting does not stop or pause a track. Group updates lock the native mixer so an
  audio buffer cannot observe half of a multi-voice category change
- Native `Playing` excludes paused tracks; `Paused` is separately observable.
  Position is source sample frames, not latency or wall-clock time. Decoded clips
  use 48 kHz frames; streams keep their original source sample rate

Clips are reusable PCM resources, not an implicit path cache: load once and share
one clip across voices. Releasing a clip's caller ownership prevents new voices,
but existing attached voices keep its PCM alive and can replay it. The retained
clip and its memory budget disappear only after the last voice releases it.
Releasing a voice stops it, destroys its track, then closes its file stream.

`AudioScope` is an explicit scene/example resource owner: dispose voices before
clips. Register `scope.Dispose` with an existing `BehaviorLifetime.OnDetach` when
scene/entity teardown should release it. There is no inference that every voice
belongs to the active scene. Tests unload three owned scenes while an independent
persistent voice keeps playing, then verify the scene sounds no longer mix.

Closing a session stops/releases all voices, closes streams, releases clips, then
destroys the mixer/device. Engine destruction performs this before its SDL graphics
backend teardown. Outstanding managed objects become invalid; late same-thread
`Dispose` is harmless and cannot release resources into a newly opened session or
context. Handles are monotonic nonzero IDs, validated against their owning live
registry; stale/foreign handles cannot be reused. No finalizer destroys objects on
a GC thread. All public operations use the creating thread, outside sprite frames.

The prior `gal_play_tone` probe remains available for ABI compatibility and its
legacy counter. `--audio-demo` disables that automatic legacy stream and uses the
new mixer only. `gal_stats.audio_plays` still counts the legacy tone, not new voices;
use the audio state API for resident voice/clip counts.

## Bounds, paths and failure rules

The managed adapter uses the same captured `AssetRoot.Resolve` path and descendant
link rejection as textures/UI; accepted logical file extensions are `.wav` and
`.ogg`. Native opening additionally checks WAV/Ogg signatures and a **64 MiB**
encoded-file ceiling. Each stream owns an independent cursor. The file must remain
unchanged for its lifetime; a path rename after opening is checked on Linux, but
cross-platform rename semantics are not promised. Synchronous file I/O/decoding on
the mixer thread can stall, so this is not a latency guarantee or network streamer.
Root validation is not a race-proof sandbox against concurrent file replacement.

- At most **64 resident clips** and **32 voices**, including stopped/paused voices
- Mono/stereo input at **8,000..192,000 Hz**; clips normalize to float stereo/48 kHz
- Incremental decode caps each clip at **16 MiB PCM**, and retained clips together
  at **64 MiB**. Candidate decode/copy buffers add temporary memory; these numbers
  are retained PCM limits, not peak process RSS or decoder-internal allocation limits
- Offline calls require **1..16,384 stereo frames** (2..32,768 floats)
- Loop count is `-1..1,000,000`; non-play commands reject a nonzero loop argument

Clip decoding ignores embedded loop metadata. File streams preserve finite file
metadata loops, but reject explicitly infinite-duration metadata; use the public
voice loop setting for intentional indefinite playback. Malformed/unsupported
files, source limits and invalid format return explicit errors. A failed candidate
load/open does not replace live voices or clips. Assets are trusted authoring inputs,
not an adversarial media-decoder security boundary. Finite sample values are checked
when loading PCM clips; there is no engine limiter or clipping-avoidance mixer, and
multiple overlapping voices can exceed unit float amplitude.

Initial decode/open and control failures are reported synchronously. A later device
or streaming failure can stop a track; this API does not yet distinguish every
asynchronous failure from normal completion or expose a callback/error event queue.
No DSP filters, 3D spatialization, device selection/hotplug UI, capture, microphone,
stream prefetch service, sample-accurate music scheduler or measured end-to-end
latency is claimed.

## Source-only fixtures and reproduction

Original generated audio is excluded from Git, in line with the source-only
publication choice. Regenerate it once before tests/demo:

```sh
# Uses already installed Python 3 and ffmpeg with libvorbis; does not install them.
python3 scripts/generate-audio-fixtures.py
# Explicit dependency setup; official pinned download + local build/install only.
bash scripts/build-mixer.sh
GAL_WITH_MIXER=ON bash scripts/build-ui.sh
scripts/test.sh quick audio
scripts/test.sh jit
source scripts/ui-env.sh
/path/to/dotnet managed/bin/Release/net10.0/GameAuthoringLab.dll --audio-offline-test
/path/to/dotnet managed/bin/Release/net10.0/GameAuthoringLab.dll --audio-device-test
/path/to/dotnet managed/bin/Release/net10.0/GameAuthoringLab.dll --audio-demo --frames 120
```

The encoder is a fixture-generation tool, not a runtime dependency. The generated
WAVs contain exact test PCM and an original short two-note cue; the Ogg contains
an original synthetic six-note loop. Ogg container bytes can vary with encoder
version/serial metadata; decoder assertions are on PCM behavior, not file identity.
Copies go into managed build/publish assets only when these generated files exist.
There is no implicit ffmpeg launch during a normal build.

For a displayed desktop, select an appropriate SDL video/audio driver instead of
`ui-env.sh`'s software/dummy settings, then run `--audio-demo`. It reuses the atlas
scene with bottom state lamps. E replays SFX; Space pauses/resumes music; F stops
music; T restarts looping music; left/right adjust master gain; Escape exits.
Focus loss/nondrawable state pauses active voices and only resumes those that were
playing before suspension. Console text documents the controls. Physical speaker
output and hardware latency still require separate acceptance.
