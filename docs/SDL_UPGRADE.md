# SDL 3.4.16 upgrade

SDL **3.4.16** is the selected stable release, published 2026-09-02 and verified
against the official release metadata on 2026-10-01. This is a separate dependency
upgrade after the input milestone `4738832`; no engine/game schema or image-format
migration accompanies it. SDL_mixer 3.2.4 requires SDL >=3.4.0, so this also clears
that prerequisite for a later audio batch.

Sources:
- [SDL stable release](https://github.com/libsdl-org/SDL/releases/tag/release-3.4.16)
- [Official source archive](https://github.com/libsdl-org/SDL/releases/download/release-3.4.16/SDL3-3.4.16.tar.gz)
- [SDL_mixer requirement](https://github.com/libsdl-org/SDL_mixer/blob/release-3.2.4/CMakeLists.txt)
- [Linux build prerequisites](https://wiki.libsdl.org/SDL3/README-linux#build-dependencies)

## Reproduce without replacing the older install

`scripts/sdl-env.sh` records the version, archive SHA-256 and default prefix.
Download/extraction is an explicit opt-in step; ordinary tests do not download:

```sh
scripts/fetch-sdl.sh
scripts/configure-local-sdl.sh
scripts/build-ui.sh
```

The local dependency build reuses the documented compiler/CMake and prerequisite
sysroot. On a normal machine, supply the official prerequisites and use
`SDL3_PREFIX` with `scripts/build-sdl.sh`, or an equivalent SDL CMake installation.
These scripts are not an unattended whole-machine installer.

The new source/build/install are `.deps/SDL3-3.4.16`, `.deps/sdl-3.4.16-build` and
`.deps/sdl-3.4.16-install`. The old `.deps/SDL3-3.2.28`, `.deps/sdl-build` and
`.deps/sdl-install` remain untouched. Before promotion, the optional engine/UI was
built in `build-ui-sdl-3.4.16` and passed native/input/resource checks. Both ordinary
native build directories are then explicitly reconfigured to the new prefix;
rebuilding a stale CMake cache alone would not prove the selected SDL changed.

Build configuration requires SDL >=3.4.16. Runtime startup checks the loaded SDL
version too, producing a clear error if library search paths select the old runtime.
The verified new runtime reports `3004016` / `SDL-release-3.4.16-0-gfa2c02bb6`.
A deliberate old-runtime test rejects 3.2.28 before creating a window.

The official source archive SHA-256 is:

```text
7322236cd12090c3eb40b9728be4d49c76f66ad17d04369584d4ecad5cf77c68
```

SDL 3.4's stricter X11 configuration initially rejected missing extension headers.
Required official Debian Xcursor/XInput/XFixes/XRandR/XScrnSaver/XTest/XRender
packages (development and corresponding runtime packages) were verified against
the signed trixie Packages index and extracted only into `.deps/sysroot`. No
system-wide package install or disabled-feature workaround was used. Wayland and
additional audio-server development stacks remain absent/unvalidated. Exact local
package versions, release metadata and hashes are in `evidence/sdl-3.4.16/`.

## License, footprint and unchanged dependencies

SDL remains zlib-licensed. Its [retained notice](SDL3-LICENSE.txt) is refreshed to
the 2026 notice; license terms are unchanged. SDL3_image **3.2.4** and RmlUi **6.3**
remain the same libraries and build profiles. The SDL_image static archive hash is
unchanged. Built-in SDL PNG support is not substituted for the current image
library, and authored BMP/UI profiles do not gain other accepted formats.

The local Release SDL shared-library file grew from **3,566,664** to **4,003,064
bytes** (+436,400 bytes). The new build also enables the previously missing X11
extensions, so this is the observed upgrade result, not a controlled version-only
size attribution. These are unstripped library files, not a complete distribution.
The **4,990,752-byte** aggregate NativeAOT host is unchanged and reused from the
fresh input-milestone publish; no managed code or interop layout changes in this
upgrade require republishing it. Binary/package distribution and transitive license
bundling remain separate work.

## Rollback and verification boundary

The pre-upgrade engine/source state is commit `4738832`. Old runtime/source/build
paths above remain available, and the two old native engine libraries are retained
locally in `.deps/rollback-sdl-3.2.28/{native,ui}/libgal.so`. Rollback uses the prior
source/build configuration and old runtime together; setting an old prefix on the
new minimum-version source is intentionally rejected. No dependency archive or
compiled library is added to Git.

Final full-JIT/AOT runtime checks, software-rendered captures and the displayed
window result are recorded in [validation](validation.md). Existing managed AOT
source/output identity is checked before reuse. Tests establish this Linux x64
software-rendered integration, not physical GPU/audio, real IME, Windows, macOS,
mobile or every newly available SDL feature. SDL_Renderer additions do not
implicitly add features to this engine's direct SDL_GPU path.
