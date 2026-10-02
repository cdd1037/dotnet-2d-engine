# Optional static SVG in UI

SVG is an explicit, optional UI resource supported by the generic model bridge.
Use `UiModelSession<T>.StageAsset` for public authoring; the former target-binding
profile remains internal regression coverage. It uses the pinned
RmlUi 6.3 official SVG plugin with LunaSVG 3.5.0 and its bundled PlutoVG 1.3.1.
The world image API remains BMP/PNG/JPEG-only. Settings and game UI retain their
fixed contracts. This is a profile for trusted, authored project assets with
defensive limits; it is **not** an upload sanitizer or a CPU/memory sandbox.

## Build and use

With the existing SDL/RmlUi dependencies prepared, the explicit setup is:

```sh
bash scripts/bootstrap-svg-deps.sh
GAL_WITH_SVG=ON bash scripts/build-ui.sh
```

The bootstrap verifies the official release archive's SHA-256
`1abf1472ee6c4d19797916e8cc3c2e4b628e0d81178ffac60bdb0d457e32c690`.
It builds static libraries with `LUNASVG_DISABLE_LOAD_SYSTEM_FONTS=ON`.
Ordinary builds/downloads are unchanged: `GAL_ENABLE_SVG` defaults to `OFF` and
requires `GAL_ENABLE_RMLUI=ON`. Point `lunasvg_DIR` and `plutovg_DIR` at the prepared
CMake packages when configuring directly. `GAL_RMLUI_CORE` must be the prepared
core without the upstream `RMLUI_SVG_PLUGIN` flag: this target compiles/registers
the official plugin once with the host boundary hooks. The existing package proof explicitly
keeps SVG disabled; it is not silently enlarged by this optional feature.
A disabled native UI build reports an explicit error when an SVG manifest is
loaded. CPU authoring validation remains available without native SVG libraries.

```xml
<svg id="badge" src="images/badge.svg" width="32" height="32" />
```

```css
#button { decorator: svg(images/button.svg); image-color: #80c0ff; }
```

RML `svg` accepts exactly the same `id`, `class`, `src`, integer `width`/`height`
attributes as the bounded raster `img` element. It must be empty and cannot be a
binding target or a child of a replaceable text/action binding. The decorator
syntax is exactly `svg(path)` or `none`, with one file and no crop/tiling options.
`img` and `image(path)` remain raster-only. Inline SVG and image-valued model
bindings are not added. Raster and SVG references share the same 32-path manifest.

## Accepted SVG files

Source must be well-formed UTF-8 XML. Supported elements are `svg`, `g`, `defs`,
`symbol`, `use`, `clipPath`, `mask`, `linearGradient`, `radialGradient`, `stop`,
`rect`, `circle`, `ellipse`, `line`, `polyline`, `polygon`, `path`, `title`, `desc`.
Only relevant geometry/presentation attributes from the strict validator are
accepted. Fill, stroke, transforms, opacity, gradients, clipping, masks and
file-internal fragment references are supported. Use presentation attributes
rather than an embedded stylesheet. Named colors, hex colors, bounded comma-separated `rgb()`/`rgba()` and
`currentColor` are accepted; the full browser CSS color language is not.

The root needs a positive width/height (unitless or `px`), or a `viewBox` to provide
missing intrinsic dimensions. No system font discovery is performed. Convert
SVG typography to paths, and use RmlUi for live/localized text.

Unsupported content produces an authoring error, not a silent fallback:

- `image`, embedded raster/data URLs, external files/URLs and external fragment references
- DTDs, entity references (including numeric/predefined entities), CDATA and processing instructions other than the XML declaration
- `text`, scripts, animation, filters, patterns, foreign objects and embedded CSS/style/class attributes
- Arbitrary namespace prefixes, event handlers, unsupported attributes or malformed numeric/path syntax
- Missing/wrong-type fragment targets, reference cycles and excessive expansion

This is deliberately smaller than LunaSVG's own feature set. The `image` ban is
important: LunaSVG's internal image loader can read files without going through
RmlUi's file interface. Both managed and native validation run before LunaSVG
parses any file. SVG files are never delegated to SDL_image's NanoSVG decoder.

## Limits

- Each SVG: 256 KiB encoded; depth 32; 2,048 XML element nodes
- Source numeric tokens: 16,384; absolute numeric value at most 1,000,000
- Transform attributes: 128 operations, finite composed components and
  conservative inherited affine bounds at most 1,000,000; near-singular skew
  angles and intrinsic/viewBox scale ratios over 1,000,000 are rejected
- Attribute/path budgets count XML-normalized values; full source bytes retain
  the encoded limit above
- Path data: 64 KiB total; valid command arities, initial moveto and arc flags
- Internal reference depth: 16; expanded graph at most 16,384 nodes,
  65,536 numeric tokens and 256 KiB path data
- Intrinsic/root viewBox dimensions: positive and at most 4,096 per axis
- Resolved raster dimensions: at most 4,096 per axis, checked **before** bitmap
  allocation or LunaSVG rendering; zero-sized layout is non-rendering
- Each document: 128 retained nonzero SVG raster variants; 64 MiB retained RGBA
  combined with its raster-image resources; 16 MiB encoded manifest total

A raster variant is a source plus resolved dimensions and crop mode. Different
`image-color` values reuse its pixels. Resizing temporarily counts the old and new
variant together, rather than evicting a live image to make a replacement fit.
Current and candidate documents have independent budgets, so transactional reload
can retain twice the per-document amount. CPU preflight conservatively also
counts SVG intrinsic-size RGBA in the existing snapshot metadata budget.

These limits describe inputs, expanded complexity and retained output. LunaSVG
may allocate additional temporary surfaces for masks/groups and paths; peak
process memory and rendering time are not hard-capped. Untrusted SVG ingestion
would require a separate isolated conversion service with its own resource limits.

## Ownership, cache and errors

All files are copied to generation-specific owned staging directories. Native
staging copies and revalidates the complete manifest, including hidden elements
and hover-only rules. A 1×1 plugin raster/upload preflight covers every SVG source
before the candidate is accepted. The plugin then reads only the owned byte
manifest, never a newly edited source file. The staged RML/RCSS retains the existing
snapshot lifetime and path rules described in [UI images](UI_IMAGES.md).

The official plugin still owns SVG geometry, parsed documents and textures. A
small, digest-checked build-time patch adds only these host boundaries:

1. File-only loading through validated manifest bytes
2. Per-render-manager generation identity in cache keys, avoiding the upstream
   global cache's cross-document render-manager collision
3. A retained-size reservation released with each cache entry, before rasterization
4. The all-manifest preload entry point

The dependency source tree is not edited or vendored. See
`scripts/patches/patch-rmlui-svg.py`; changing the RmlUi pin requires reviewing and
rebasing its expected source digest. Failed source validation, rasterization or
upload keeps the existing live document/model/actions. A native replacement may
retire an earlier pending candidate, as with raster images. Inspect `Status` after
drawing for publication or a candidate diagnostic. A later oversized live
resize reports an error instead of allocating an oversized texture.

Closing or replacing a document releases its SVG references, variants and GPU
textures with its render manager. SVG resources are not world texture handles
and do not populate `EngineHost.TextureCount`. Unique native cache identities
also isolate direct C ABI calls that reuse the same staging pathname.

## Sizing and color

The plugin rasterizes at the rounded, laid-out RmlUi content box. Intrinsic sizes
and width/height attributes use RmlUi's density-independent sizing; RCSS `px`
retains the existing layout semantics. Test the target DPI and resize flows.
SVG `viewBox`/`preserveAspectRatio` apply inside the SVG viewport.

`currentColor` is resolved **inside the SVG**; an external SVG does not inherit
the surrounding RML text color. RmlUi `image-color` and opacity multiply the
rendered pixels. White artwork makes a useful tintable mask. LunaSVG's output is
already premultiplied: the official plugin swaps red/blue channels only, and the
host does not run its raster-image premultiply pass on SVG pixels.

## Validation commands

```sh
scripts/test.sh quick svg
scripts/test.sh jit
source scripts/ui-env.sh
dotnet managed/bin/Release/net10.0/GameAuthoringLab.dll --svg-graphics-test
dotnet managed/bin/Release/net10.0/GameAuthoringLab.dll --image-graphics-test
```

`GAL_UI_SVG_CAPTURE_DIR` selects focused pixel readback output. The graphics suite
covers ordinary vector features, premultiplied alpha/tint, file snapshots,
transactional failure, DPI/resize and resource ownership. Tests use software
Vulkan, not a physical-device matrix. See [validation](validation.md) for the exact
executed results and any verification limits.

Retained notices: [LunaSVG](LUNASVG-LICENSE.txt), [PlutoVG](PLUTOVG-LICENSE.txt),
[embedded FreeType/stb notices](PLUTOVG-EMBEDDED-NOTICES.txt) and
[FreeType license](PLUTOVG-FREETYPE-LICENSE.txt).

The optional deterministic native parser mutation smoke is reproducible without
SDL, LunaSVG or a renderer:

```sh
c++ -std=c++17 -g -O1 -fsanitize=address,undefined -fno-omit-frame-pointer \
  -I native/src tests/svg_validation_mutation.cpp -o /tmp/gal-svg-mutate
ASAN_OPTIONS=detect_leaks=0 /tmp/gal-svg-mutate
```

The run checks 60,000 mutations for parser crashes/undefined behavior; it is not an
exhaustive fuzzer. Leak detection is disabled because this runner uses ptrace.
