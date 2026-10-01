# Generated room assets

The initial GitHub import omits exactly `assets/room-a.bmp` and
`assets/room-b.bmp` from historical trees. Each is a 2,073,722-byte generated
sample image. Other small required BMP and SPIR-V files remain tracked.
The milestone commit order and all other original file bytes/modes are kept;
GitHub commit IDs differ from the original local history.

Before a graphical run, prepare the rooms:

```sh
bash scripts/prepare-room-assets.sh
```

Managed builds automatically run this preparation if either room is absent.
The explicit room Content items ensure a first build copies newly generated
images into its output. A launcher using an already-built DLL does not build
or install prerequisites; prepare assets first when using such a launcher.

The current generator requires Python 3, Pillow, and these DejaVu fonts:

- `/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf`
- `/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf`

These are Linux development prerequisites, not automatic downloads or bundled
runtime fonts. The preparation script stops with an actionable message if a
prerequisite is missing. Windows/macOS generation is not validated. The Python
generator also regenerates the five small tracked BMPs; no external art is used.
Font rasterizer/version changes can alter image bytes. On the validated local
toolchain, all seven generated BMPs match the original Git blob hashes exactly:

- room-a: `5f2fcfb5f655e31e2cbf7d7c08f0e233fff231ad`
- room-b: `6c79c97ca765f7d5460a7c81853671a5b3090c9f`

Third-party fonts and build tools keep their own licenses. See
[third-party notices](../THIRD_PARTY_NOTICES.md) before distributing binaries.
