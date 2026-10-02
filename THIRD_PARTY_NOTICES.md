# Source license and third-party notices

Original project code and associated documentation are licensed under the
[MIT License](LICENSE), copyright 2026 yi cheng.

Third-party code, dependencies, tools, and fonts retain their own licenses;
the project's MIT license does not relicense them. Retained upstream notices:

- [SDL3](docs/SDL3-LICENSE.txt): zlib
- [SDL3_image](docs/SDL3-IMAGE-LICENSE.txt): zlib
- [SDL3_mixer](docs/SDL3-MIXER-LICENSE.txt): zlib
- [stb_vorbis](docs/STB-VORBIS-LICENSE.txt): MIT selected from upstream dual license
- [Box2D](docs/BOX2D-LICENSE.txt): MIT
- [RmlUi](docs/RMLUI-LICENSE.txt): MIT; the UI build applies a hash-guarded,
  generated-copy [data-for ordering and generic draft-binding modifications](docs/UI_MODELS.md#pinned-rmlui-ordering-and-draft-hooks)
  to pinned 6.3 without editing its source checkout/archive
- Optional [LunaSVG](docs/LUNASVG-LICENSE.txt) 3.5.0 and [PlutoVG](docs/PLUTOVG-LICENSE.txt) 1.3.1: MIT
  with [embedded notices](docs/PLUTOVG-EMBEDDED-NOTICES.txt) and the
  [FreeType License](docs/PLUTOVG-FREETYPE-LICENSE.txt) for its derived rasterizer

See [dependency documentation](docs/dependencies.md) for versions and build
requirements. Dependencies and fonts are not vendored in this source tree.
Before distributing binaries, audit every included transitive library and font
and include its required notices. This list is not a complete license bundle
for a binary distribution.

The source-license decision here supersedes older milestone documentation
stating that the project's own license had not yet been chosen.
