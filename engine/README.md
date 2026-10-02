# Dotnet2D.Engine local proof

Experimental net10.0 managed runtime, version 0.1.0-preview.1. No stable API or
cross-platform binary support promise. Namespace remains GameAuthoringLab.

In the source checkout, start with the [author guide](../docs/AUTHOR_GUIDE.md)
for the starter, edit/run workflow and public API boundaries.

Use EngineHost.Create, World, explicit AssetRoot/AssetCatalog mappings, authored
scene loading and BoundUiSession<T>. Dispose owners on their creating thread.
Reusable C# composition uses ordinary application factories and typed references;
`World.OnDestroy(entity, cleanup)` explicitly binds instance resources to entity
destruction, separately from behavior replacement. See the
[composition guide](../docs/CSHARP_COMPOSITION.md) and public-package consumer.
Clipping, diagnostics, materials and render targets also have public experimental
APIs; their runtime behavior is tested separately from the small package consumers.
Audio, physics, frame animation/tween/timers and bounded TileMap entry points are
also public. `TileMapInstance.CreateEditable` enables bounded transactional cell
edits over immutable map snapshots with synchronous collision replacement.
Audio/physics expose immutable copied state views; native-backed
wrappers come from owner factories, never arbitrary-handle constructors. The
sample/test executable, raw interop, probes, source-generation contexts and
collision planning helpers remain internal or outside this library. This is an
experimental package API, not a stable SDK release.

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
