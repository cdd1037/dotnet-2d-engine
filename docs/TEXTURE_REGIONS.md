# Texture regions and sprite flips

This compact rendering slice adds shared-atlas regions without changing image
codecs, shader formats or the existing ABI-1 context and 56-byte `gal_draw`.
`assets/regions.scene.json` is a second general-purpose authored example; its
438-byte, 16×8 BMP is an original deterministic test pattern, not a bundled art pack.

## Identity, ownership and validation

`AssetCatalog` accepts immutable `TextureAsset(Path, Region?)` descriptors as well
as its old key-to-path constructor. `TextureRegion(X, Y, Width, Height)` uses integer
texels with a top-left origin. Width/height must be positive and the entire rectangle
must fit the texture; overflow-safe bounds checks reject negative/outside values.
No rectangle means the whole texture. Entity `AssetKey` remains independent of
physical filenames and atlas coordinates. Remapping a key to another cell does not
change an entity ID or its saved asset key.

All regions/aliases of the same physical path share the context-owned `TextureCache`
entry and lifetime. A lease now retains the texture dimensions along with its
handle. Real uploads use authoritative decoded dimensions from
`gal_texture_get_info`; headless uses BMP preflight dimensions. An existing live
entry's dimensions are a retained snapshot, just like its pixels, even if the source
file changes. `TextureBank.Sync` validates candidate regions against that snapshot,
then checks newly acquired decoded dimensions before committing additions. Failure
retains the previous usable leases. `ASSET_REGION` and authored
`$.resources[index].region` diagnostics identify rejected bounds.

`TextureBank.ResolveRegion` returns a borrowed typed texture plus region. Set it as
`SpriteBatch.RegionResolver`, extract, then call `engine.Draw(camera, batch)`. A
custom resolver returns `new TextureBinding(lease.Texture, region)`; target sampling
uses `target.Binding`. Extraction validates dimensions and submission revalidates
the exact owner and engine before opening the frame. Raw resolver IDs and batch
ABI arrays are internal.

`Sprite2D.FlipX`/`FlipY` reverse sampling only. They do not move vertices, change the
pivot, mutate transforms, affect collision geometry or reorder sprites. Geometric
rotation, scale and shear remain the existing affine transform. Stable layer sort
keeps geometry, sampling and flags together; equal layers retain entity order.
Adjacent draws using the same underlying texture still merge, even across different
regions/flips. Texture sorting never replaces alpha order.

## Native v2 boundary and sampling

`gal_submit_draws_v2` accepts 88-byte `gal_draw_v2` records:

- `size`, `version = 2`, then the unchanged affine draw at offset 8
- Integer source rectangle at offset 64
- X/Y flip bits (`1` / `2`), and a zero reserved field

All-zero source fields explicitly select legacy full-texture UVs `[0,1]`. Otherwise
the rectangle must fit a live, context-owned texture; the built-in texture cannot
be assigned a source rectangle. Unknown version/size/bits, stale handles and invalid
geometry/color/bounds reject the **whole submit call before appending anything**.
Prior successful submissions in that frame remain intact. Old and new draw calls
may be mixed in one frame and share adjacent-texture batching. No struct packing
or ABI-version-1 behavior changed. Texture dimension queries are size-tagged and
reject stale/foreign handles without overwriting the caller's prior output.

Regions interpolate between the first and last texel centers:
`(x + 0.5)/textureWidth` through `(x + width - 0.5)/textureWidth`, likewise for y.
The existing linear-clamp sampler and no-mipmap textures are retained. This prevents
neighbor-cell bleed, including one-texel-wide/high regions, without padding or new
shader attributes. It slightly changes the sampling scale compared with texel-edge
UVs; it is **not** a pixel-perfect nearest-neighbor mode or a general mipmapped atlas
solution. Full-texture records keep the old edge UVs and existing sample pixels.
There is no rotated/trimmed packer metadata, nine-slice, automatic packing or
atlas generation. A 90-degree entity rotation in the fixture is geometry rotation,
not rotated atlas storage.

Headless bank synchronization validates source bounds and ownership. Its texture
handles remain zero, so extracted headless submissions omit the source rectangle
and exercise geometry/flags only; they do not decode/sample pixels. Native region
sampling is verified separately using real SDL uploads and software Vulkan.

## JSON versions and snapshots

The strict official JSON source-generated serializers remain in use. No reflection
reconstruction or polymorphic type names were introduced.

- Authored versions 1 and 2 load. Paths keep their original scene-relative meaning
- Version 2 optionally adds `resources[].region` with required integer `x`, `y`,
  `width`, `height`, and optional entity sprite `flipX` / `flipY` booleans
- Version 1 rejects non-null regions and nondefault flips; it is never silently
  reinterpreted. Defaults are omitted when writing, preserving ordinary v1 files
- Runtime saves accept versions 1 and 2. Saves without flips still emit version 1;
  any nondefault flip emits version 2. Old engines reject version 2 explicitly
- Saves contain entity asset IDs and flips, never native handles, physical paths or
  atlas coordinates. The caller supplies the catalog on restore. An atlas remap is
  therefore a resource change, not a world-save migration

World scissor/clipping, sprite-frame animation, nearest/pixel-art sampling and
packaged asset loading remain separate follow-on work. No complete renderer or
cross-platform rendering-support claim follows from this fixture.

## Reproduction

```sh
scripts/test.sh quick regions
scripts/test.sh jit
scripts/build-ui.sh
source scripts/ui-env.sh
/path/to/dotnet managed/bin/Release/net10.0/GameAuthoringLab.dll --region-graphics-test
python3 scripts/validate-region-pixels.py
# Interactively view the same source after choosing a displayed SDL environment:
/path/to/dotnet managed/bin/Release/net10.0/GameAuthoringLab.dll --authored-demo assets/regions.scene.json
```

The graphics fixture checks one shared upload for four IDs and one ordered draw
run for eight sprites. Its pixels cover adjacent-cell edges, both flips, rotation,
tint and same-layer alpha overlap. The Python check also resizes a real offscreen
surface and verifies v2 camera projection with a single-texel region. Native mock
checks cover malformed metadata, overflow, transactional rejection and old/new
coexistence. CPU checks include source/save roundtrips, stable sorting, remapping,
resource failure retention, cleanup and zero warmed frame allocations.
