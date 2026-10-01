# Dotnet2D.Engine local proof

Experimental net10.0 managed runtime, version 0.1.0-preview.1. No stable API or
cross-platform binary support promise. Namespace remains GameAuthoringLab.

Use EngineHost.Create, World, explicit AssetRoot/AssetCatalog mappings, authored
scene loading and BoundUiSession<T>. Dispose owners on their creating thread.
Clipping, diagnostics, materials and render targets also have public experimental
APIs; their runtime behavior is tested separately from the small package consumers.
The sample/test executable is a separate assembly. Raw ABI/probes and current
audio, physics, animation/timing and TileMap entry points remain internal. This is
not a complete SDK release.

For graphics, reference Dotnet2D.Native.Linux.x64 separately at the same version.
That local binary profile has specific Linux system prerequisites in its README.
No external font is included. UI requires a separately installed licensed CJK
font selected with GAL_UI_FONT, as documented in the source project.

Optional EngineAsset items copy caller-owned assets to build/publish output while
preserving relative paths. No game assets are included or selected automatically.
JSON uses explicit source-generated schemas. Public model binding uses delegates,
not reflection. Trimming cannot remove functions from the prebuilt native DSO.

The MIT notice is copied to licenses/Dotnet2D.Engine/LICENSE.txt in application
build and publish outputs. The separate native package copies its complete notice
bundle beside it. Do not omit these notices when redistributing those outputs.
