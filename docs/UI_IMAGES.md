# Common images and document-owned UI resources

The world resource path now accepts BMP, PNG and JPEG (`.bmp`, `.png`, `.jpg`,
`.jpeg`) through the already pinned SDL_image 3.2.4 backend. `AssetRoot.ReadImageInfo`
and `ValidateImage` are additive APIs; the existing BMP-only methods keep their
format restriction. The existing `BitmapInfo` value type retains its name for
source compatibility and reports dimensions for all supported raster formats.
Catalogs, authored scenes, tile maps and texture leases use the common image path. The legacy low-level C function `gal_texture_load_bmp` retains its symbol name and now detects these three signatures regardless of filename; managed authoring additionally requires a matching supported extension.

No new decoder dependency was added. Every graphical native build now links the
existing SDL_image package; headless builds still need neither SDL nor SDL_image.
The pinned build uses embedded STB for PNG/JPEG. Its WebP backend is disabled, so
WebP remains rejected. GIF, animated PNG and arbitrary formats supported by
other SDL_image builds are deliberately excluded from this contract. World SVG is
also excluded; optional bound-UI SVG uses a separate [strict vector profile](UI_SVG.md).

## Static UI images

Public `UiModelSession<T>` accepts static images and a declared manifest for dynamic
image paths; use `StageAsset` with the initial model. See [generic image authoring](UI_MODELS.md).
The bounded target-binding rules below describe the internal regression profile,
not a second public UI API. Both implementations share resource ownership:

```xml
<img id="badge" src="images/badge.png" width="32" height="32" />
```

`src` is required; `id`, `class`, `width` and `height` are optional. Dimensions are
integer texels from 1 through 4096. Omit both for intrinsic sizing, or use ordinary
width/height RCSS. Images must be empty, cannot be registered binding targets, and
cannot be nested inside a text/action binding. For a button with an icon or a
panel background, use the bounded RmlUi image decorator:

```css
#panel { decorator: image(images/panel.jpg); }
#button:hover { decorator: image(images/hover.png); }
```

The grammar is exactly `image(path)` or `none`, with one image and no nested
functions, quoted URL syntax, multi-decorators, tiling options or arbitrary CSS
resource properties. Existing finite selectors and length/color properties still
apply. Settings and mission/game UI retain their separate fixed contracts and
continue to reject image elements and decorators. No new template language,
reflection binding or image-valued model projection was introduced.

## Paths, limits and snapshots

The RML and its exact same-basename sibling RCSS share one asset-root namespace.
Image paths are relative to their containing RML/RCSS directory, including nested
subdirectories. For example `ui/menu.rml` using `images/badge.png` resolves to
`ui/images/badge.png` under the supplied `AssetRoot`. UI image paths are 1..255
ASCII characters from letters, digits, `_`, `-`, `.`, `/`; empty/dot/traversal
segments and trailing dots are rejected. URI schemes, percent escapes, queries,
fragments, absolute paths, backslashes and external URLs are not accepted.
Descendant filesystem links are rejected by `AssetRoot` on snapshot read.
The root/ancestors are trusted; this is authoring validation, not a filesystem
sandbox against concurrent file replacement.

- Every encoded image: at most 16 MiB and dimensions 1..4096 on each axis
- Decoded RGBA output: at most 64 MiB per image
- Each UI document: at most 32 distinct image paths, 16 MiB encoded aggregate and
  64 MiB decoded RGBA aggregate; aliases by identical path count once
- World cache/native texture pool: at most 256 textures and 256 MiB resident RGBA;
  retained leases share their entry without spending the budget again
- Transactional replacement counts current and candidate resources together.
  UI may retain one current and one pending document (up to twice the per-document
  image budgets); it never evicts the current document to make a candidate fit

PNG supports legal 1/2/4/8-bit combinations, including transparency and interlace;
16-bit, animated and vendor-specific PNG are excluded. JPEG supports 8-bit
baseline/extended sequential/progressive frames with 1, 3 or 4 components.
Header/chunk/segment checks run before native decoding. Encoded bytes and final
RGBA output are bounded; upstream decoder temporary allocations are not a hard
allocator sandbox. The budgets describe encoded inputs and retained RGBA output, not peak staging, upload or process memory. EXIF orientation and color-profile transforms are not added by the engine. CRC and compressed-pixel integrity remain decoder-owned, so a
plausible header does not guarantee upload success. Headless validates metadata
and ownership only and never claims to decode or render pixels.

RML/RCSS and all image bytes are copied into owned snapshots. Native staging copies
and preloads the complete image manifest, including resources referenced only by
hidden elements or hover rules, before accepting a candidate. Later source edits
or deletion cannot alter the accepted document. A new `LoadAsset` takes a fresh
snapshot. No network fetch, watcher, automatic hot reload or implicit background
work occurs.

Native staging and rendering must both succeed before a new generation is
published. Failed validation, image decode or upload retains the live document,
model revision, actions and usable images. An attempted native replacement may
discard an older *pending* candidate; a managed preflight rejection never calls
native and leaves that pending candidate alone. Inspect `Status` after drawing to
observe publication or a render-stage diagnostic. Explicit successful reloads
retire the old generation as before.

World images and UI images have separate owners. World leases are shared by the
engine cache and released at final lease disposal. Each candidate/live document has its own RmlUi render manager, so retired texture-cache keys disappear with the document. Synchronous reload recreates renderer pipelines and may regenerate active font resources; there is no warm-reload latency claim. RmlUi owns its UI GPU images;
they are not exposed as world texture handles and do not appear in
`EngineHost.TextureCount`. Closing the UI or destroying the engine releases its
resources; repeated or late disposal cannot close a newer UI owner. Source staging
files are removed when their candidate/live document retires or the owner closes.

## Verification

Run `scripts/test.sh quick images` for bounded image metadata, paths, profiles,
resource snapshots and headless cache ownership. `scripts/test.sh jit` includes
these contracts with the existing aggregate. With the prepared optional UI backend:

```sh
source scripts/ui-env.sh
# Select the installed .NET SDK as in docs/TESTING.md.
dotnet managed/bin/Release/net10.0/GameAuthoringLab.dll --image-graphics-test
```

The focused integration reads actual software Vulkan pixels and checks PNG alpha,
JPEG/backgrounds, source deletion/edit retention, failed/hidden image reloads,
repeated replacement and owner cleanup. `GAL_UI_IMAGE_CAPTURE_DIR` selects the
readback directory. An additive C ABI, `gal_bound_ui_open_images`, copies a bounded
relative-path manifest synchronously; the old bound-open call still takes no
image manifest. This boundary requires one fresh NativeAOT publication, followed
by the same focused integration and CPU aggregate. No full native dependency
rebuild or platform/device matrix is necessary for each image edit.

## Optional SVG follow-on

The assessed NanoSVG path remains unexposed. Runtime UI vectors now have a separate
opt-in [SVG profile](UI_SVG.md), using the official RmlUi plugin and pinned
LunaSVG/PlutoVG. It preserves the snapshot/manifest ownership described above and
adds defensive source/reference limits and pre-raster size/cache budgets. It does
not expand the world image decoder or accept arbitrary uploaded SVG.
