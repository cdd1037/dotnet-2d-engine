# Dotnet2D.Native.Linux.x64

Local package proof, version 0.1.0-preview.1. The native runtime contains graphics,
RmlUi UI, bounded SDL_mixer audio and Box2D physics in one `libgal.so`, together with
SDL3, FreeType and FreeType's non-system-runtime dependency closure.

All eight libraries are NuGet runtime assets under `runtimes/linux-x64/native/`.
Keep them together when deploying. `libgal.so` has a relative `$ORIGIN` ELF RPATH
which also locates transitive dependencies. A Linux x64 consumer references this
package explicitly alongside the managed engine package. This package contains
no managed assembly or game assets.

This proof targets the current Linux x64 build environment. It requires a glibc
runtime providing **GLIBC_2.38**, a C++ runtime providing **GLIBCXX_3.4.29** and
**CXXABI_1.3.15**, and the host's `libgcc_s`, `libm` and ELF loader. The `linux-x64`
RID is not a claim that this build works on every distribution with that RID.
It is not a musl/Alpine, Windows, macOS or mobile binary.

Graphics requires the host's Vulkan loader, suitable ICD/device and a usable
display. The included SDL build supports X11/offscreen; desktop X11 libraries
are host prerequisites. Device audio uses host ALSA. Driver/system service
libraries are loaded by SDL as needed and are not bundled. Headless and offline
audio operations do not establish physical GPU or speaker acceptance.

**No fonts are included.** UI requires a separately supplied compatible font;
the current CJK example uses `GAL_UI_FONT` to select an existing font. The package
does not download fonts, codecs, drivers or tools. No CMake or .NET SDK is a runtime
asset.

Original engine code is MIT licensed. Dependencies retain their own licenses.
`LICENSE.txt` contains the complete collected license texts and selected embedded
notices; individual provenance-bearing copies are in `licenses/`.
This software is based in part on the work of the FreeType Team.
Portions are copyright 1996-2024 The FreeType Project (https://www.freetype.org).

`manifest.json` records each packaged DSO's bytes, SHA-256, resolved source path,
upstream version, dependency source pins, selected license sources and build
configuration. Paths record the producing environment and are not runtime lookup
paths. This is a local-feed integration artifact, not a published release.
