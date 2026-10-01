# Resource foundation

This slice provides a synchronous filesystem-backed resource root and an explicit
BMP texture lifetime. It is shared by the authored-scene sample, two-room/RELAY
hosts and the settings/game UI source preflight. No native ABI, JSON schema,
source-generation strategy or bounded XmlReader profile changed in the original resource slice.
The subsequent [texture-region slice](TEXTURE_REGIONS.md) extends catalog metadata,
texture dimensions and the authored/draw contracts while retaining these lifetimes.

## Identity and paths

`AssetRoot` captures one absolute root at construction. Precedence is an explicit
root, `GAL_ASSET_ROOT`, `assets` beside the executable when present, then `assets`
under the current working directory. A later working-directory/environment change
does not alter an existing root. Settings and game UI default paths use this same
policy. The external UI font still uses the existing explicit `GAL_UI_FONT`
configuration; fonts are not bundled or implicitly added to this resource catalog.

Logical paths use `/`, are relative, and reject empty/dot/traversal segments,
backslashes, drive/URI syntax, control characters, trailing segment dots/spaces,
and the reserved characters `<>"|?*`. They are bounded to 4096 UTF-16 code units.
`FilePath` only constructs a validated path; `Resolve` additionally requires a
file and rejects symbolic links/reparse points in each descendant component.
The chosen root and its ancestors are trusted. This is predictable authoring
validation, not a security sandbox against concurrent file replacement. Physical
filesystem case/alias behavior remains platform-specific; use exact spelling.

`AssetCatalog` copies a case-sensitive key → logical-path mapping. Keys are
independent of physical filenames and native texture handles. The seven room
sample keys are only a default catalog, not an engine-wide allowlist. Changing a
file mapping leaves `Sprite2D.AssetKey` and authored entity IDs unchanged.

A version-1 authored resource `path` remains relative to its scene file. A nested
scene `levels/example.scene.json` referencing `cell.bmp` maps to
`levels/cell.bmp` under the supplied root. `AuthoredScene.LoadAsset(root, path)`
uses the root namespace; standalone `LoadFile(path)` anchors a root at the scene's
directory. `Load`, `Write`, `SaveFile` and `LoadFile` also accept an explicit root.
There is no silent change to version-1 resource meaning or second scene schema.
`LoadedAuthoredScene.Catalog` can be passed straight to `TextureBank`.

`UiAuthoring.ValidateAsset` / `GameUiAuthoring.ValidateAsset` and the UI sessions'
`LoadAsset` methods accept the same root/logical-path pair. The existing closed
profiles still allow only sibling `settings.rcss` or `game.rcss`; they gain no
external images, scripts, arbitrary URLs or bindings. Native UI staging consumes
the exact validated strings, retaining its prior usable document on rejection.
RmlUi's internal font/UI GPU objects remain owned by its adapter, not the BMP cache.

## Texture ownership

Each `EngineHost` owns one `Textures` cache. `Acquire(root, logicalPath)` returns a
unique disposable `TextureLease`. An exact absolute source path identifies a cache
entry; two catalog aliases or overlapping worlds share its upload. There is no
process-global cache and no sharing across native contexts.

- A lease keeps its texture resident. The final release frees it immediately
- A live entry is a retained snapshot. Edits or deletion do not change existing
  leases or cause another upload; a later acquire after final release revalidates
  and reloads. There is no watcher, background task or automatic hot reload
- Use, acquire and release happen on the engine's creating thread, outside native
  begin/end frames. A disposed lease cannot expose its old handle
- Engine destruction releases native resources and invalidates outstanding leases.
  Late same-thread disposal of those leases is safe and cannot release a resource
  belonging to a newly created context
- Cache capacity is 256 distinct paths, matching the current native texture ceiling.
  Retaining an existing entry does not consume another slot. Direct low-level native
  texture loads also consume native capacity and can make an upload fail earlier.
  Candidate and retained old resources must fit together during transactional replacement;
  the cache never silently evicts a live lease
- Headless uses the same validation, key, capacity and ownership contracts with
  handle zero and no native uploads. It does not decode/render BMP pixels

`TextureBank.Sync(world)` owns one lease per used catalog key. It first validates
all uncached additions, then acquires candidate leases before releasing old ones.
Validation or native-upload failure releases only candidate additions and retains
the previous usable set. Synchronization of an unchanged world performs no file
I/O and allocates zero managed bytes after warmup. Empty worlds release their
bank's leases; another world still using them remains valid. Dispose is idempotent;
a disposed bank cannot be synchronized again. Do not retain raw texture handles
after their lease/bank is released or destroy/release them through the low-level
native API while the cache owns them.

Filesystem/preflight/upload failure retention does not promise recovery from
out-of-memory, wrong-thread use, direct native-handle tampering or arbitrary native
frame-state misuse. Release ownership is changed only after a successful native
release, so ordinary failed release can be retried.

## Diagnostics and checks

`AssetException` derives from `IOException` and exposes `Code`, `Root` and
`LogicalPath`: `ASSET_ROOT`, `ASSET_PATH`, `ASSET_KEY`, `ASSET_MISSING`, `ASSET_LINK`,
`ASSET_FILE`, `ASSET_BMP`, `ASSET_UPLOAD` and `ASSET_CAPACITY`. Scene adapters retain
`SCENE_RESOURCE` plus the exact JSON field; UI adapters retain `UI_FILE` plus the
resource diagnostic as the inner cause. BMP header/dimensions preflight is shared;
native decoding is authoritative, and an apparently plausible header can still
fail upload.

```sh
scripts/test.sh quick resources
scripts/test.sh jit
# After building the optional UI/SDL backend and preparing existing dependencies:
source scripts/ui-env.sh
/path/to/dotnet managed/bin/Release/net10.0/GameAuthoringLab.dll --resource-graphics-test
```

The resource CPU suite covers root selection, malformed paths, missing/corrupt
files, descendant links (Linux), nested scenes, stable-key remapping, both UI
profiles, shared/overlapping leases, failure retention, capacity, thread guards,
engine disposal and stale-context isolation. The graphics suite uses real BMP
uploads and checks native decoder rejection after a successful candidate upload,
rollback to the previous usable texture set, subsequent draw and zero final native
textures. It is a focused lifetime/integration check, not a pixel-quality, physical
GPU, latency or cross-platform acceptance test.

[Texture regions](TEXTURE_REGIONS.md) now map multiple IDs to one resident texture.
Async loading, general asset formats, package files, hot reload, automatic atlas packing,
GPU eviction and a global resource database remain separate work.
