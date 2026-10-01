# Flat authored scenes, version 1

This is a small authoring slice, separate from the two-room runtime save. JSON remains canonical. `assets/compositions.scene.json` contains two expanded root/child compositions with stable GUIDs and one reusable resource key. There is no new DSL, prefab inheritance, editor, binary format or dependency.

## Run and edit

From the repository root, with the usual native library search path configured:

```sh
managed/bin/Release/net10.0/GameAuthoringLab --validate-scene assets/compositions.scene.json
managed/bin/Release/net10.0/GameAuthoringLab --authored-demo assets/compositions.scene.json --headless --frames 3
managed/bin/Release/net10.0/GameAuthoringLab --authored-demo assets/compositions.scene.json --frames 120
```

The last command renders sprites and requires an SDL GPU backend. Headless runs only validate/extract/submit through the headless ABI, not pixels. The scene is intentionally static; Escape exits the displayed demo. This loader does not change `RoomGame` collision/gameplay rules or make its runtime save an authored-level format.

To try an AI/manual edit, copy the JSON to another filename in the same assets directory, find an entity by `id`, change its `transform.x`, validate and run that copy. Keep IDs and reference keys unchanged. In C#, `AuthoredScene.LoadFile`, `Write` and `SaveFile` use the same validator. Edit the source document then load a new candidate; editing `LoadedAuthoredScene.Source` does not mutate the already-created `World`. Runtime world mutations must not overwrite source defaults via the runtime save path.

## Contract

- Required root fields: `kind: "gal-authored-scene"`, `version: 1`, `id`, `persistentScopeId`, `name`, `resources`, `entities`
- `id`, `persistentScopeId` and every entity `id` are nonempty GUIDs and globally unique within this document. Names are labels, not identity. Instantiation retains authored IDs; merging multiple copies into a single world is outside this slice
- Each entity uses the existing explicit snapshot entity shape: `id`, `name`, `sceneId`, nullable `parentId`/`ownerId`, `transform`, nullable `sprite`. Every `sceneId` must equal the authored scene ID. Parent and lifetime owner are independently resolved within this file; no external references
- `transform` uses finite x/y, positive scaleX/scaleY, rotation in radians, and shear. Sprite fields are width/height, RGBA, nullable logical `assetKey`, and integer `layer`
- A resource record maps stable case-sensitive `key` to `path`. Paths are relative to the scene file and use `/`; absolute paths, URI/drive syntax, empty/dot/traversal segments and symbolic-link resource entries are rejected. Keep scene copies beside their resources, or update mappings explicitly. Renaming a physical resource only changes its mapping
- Only BMP resources are supported. Every declared file must exist with a plausible 54-byte BMP header and dimensions 1..4096. This is preflight, not a complete image decoder; native upload may still reject corrupt pixel data. Texture IDs are runtime-only and never serialized
- Maximum 1 MiB UTF-8 source, 128 resources, 4096 entities, 512 levels per relation. Unknown properties, duplicate JSON keys, null records, missing required fields, dangling references and cycles are rejected
- Entity array order is meaningful. Equal-layer sprites preserve it; writers do not sort by GUID. Template expansion must emit a deterministic intended order
- No game-progress state, arbitrary CLR type names, script execution, extension metadata or hidden editor state. Unknown fields fail loudly rather than being lost on save. Whitespace and formatting are normalized by `Write`; arbitrary textual lossless editing is not claimed

`AuthoredSceneException` exposes a stable diagnostic code, source filename, JSON path and, for semantic entity errors, the offending GUID. JSON parse errors also include 1-based line/byte column. Examples: `SCENE_RESOURCE` at `$.entities[0].sprite.assetKey`; `SCENE_REFERENCE` at `$.entities[1].parentId`. Semantic errors identify a field path rather than a fabricated source line.

Loading builds a fresh candidate before returning it. Caller-owned live worlds are not changed on rejection. Saving validates before writing a unique same-directory temporary file and renaming it over the destination. Failed validation or tested replacement failure retains the prior destination; this is not a crash-durability/fsync guarantee.

Root-backed loading and shared texture lifetime are described in [Resource foundation](RESOURCES.md). `LoadAsset(root, logicalPath)` keeps version-1 sibling resource semantics; the loaded catalog feeds the same `TextureBank` used by the gameplay hosts.

## Compositions and evidence

The two fixture roots each have one child with local parent/owner references and globally unique IDs. They are expanded JSON, not live prefab instances: editing one does not propagate to another and no template provenance is retained. A future copy/instantiate operation must remap *all* IDs/internal references; don't implement inheritance or nested overrides just for this fixture.

`AuthoredSceneTests` checks load/write/load, single-instance edits, identities and runtime ID distinction, shared resources, array-order rendering ties, unknown/invalid fields, file/path/entity diagnostics, graph depth/cycles, numeric bounds, separate save/source documents and failed-write retention. The aggregate JIT/NativeAOT run owns final test counts. Additional CLI headless and software-Vulkan checks are recorded with the implementation evidence; physical GPU/platform acceptance is not implied.
