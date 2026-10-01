# Dotnet2D.Engine local proof

Experimental net10.0 managed runtime, version 0.1.0-preview.1. No stable API or
cross-platform binary support promise. Namespace remains GameAuthoringLab.

Use EngineHost.Create, World, explicit AssetRoot/AssetCatalog mappings, authored
scene loading and BoundUiSession<T>. Dispose owners on their creating thread.
The repository's sample/test executable is a separate assembly. Raw ABI/probes and
unexercised package surfaces remain internal; this is not a complete SDK release.

For graphics, reference Dotnet2D.Native.Linux.x64 separately at the same version.
That local binary profile has specific Linux system prerequisites in its README.
No external font is included. UI requires a separately installed licensed CJK
font selected with GAL_UI_FONT, as documented in the source project.

Optional EngineAsset items copy caller-owned assets to build/publish output while
preserving relative paths. No game assets are included or selected automatically.
JSON uses explicit source-generated schemas. Public model binding uses delegates,
not reflection. Trimming cannot remove functions from the prebuilt native DSO.
