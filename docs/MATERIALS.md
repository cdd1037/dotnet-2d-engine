# Sprite material contract

For managed application code, prefer [typed sprite commands and pass factories](SAFE_AUTHORING_BOUNDARY.md). The low-level ABI contract below remains available.

Materials are a bounded, opt-in extension of the existing Linux Vulkan sprite
renderer. The old ABI, default sprite shader, atlas coordinates, flips, camera and
straight-alpha order remain available. The subsequent [target/pass slice](RENDER_TARGETS.md)
adds RGBA8 destinations and basic post-processing. Lighting, shader graphs,
vertex-layout customization and other backends are not added.

## Authoring and preparation

Use ordinary GLSL fragment files under `shaders/materials/`. The two examples are
complete sources with `#version`, a compiler-resolved fixed include, and `main`.
The engine convention fixes UV/color inputs, one sampled texture, RGBA output and
one std140 fragment block containing two `vec4` values. See exact bindings and
sample parameter meanings in [the shader README](../shaders/materials/README.md).
Ordinary macros, conditionals, functions and computations pass unchanged to
shaderc; this is not a shader DSL or a source-rewriting compiler.

`python3 scripts/compile-materials.py` uses the existing installed libshaderc
offline, targeting SPIR-V 1.0. It compiles every requested source before modifying
outputs. Each file replacement is atomic; the set is not a transaction across a
process interruption. Syntax/preflight failures retain all prior artifacts.
Generated `.spv` files are ignored. Deterministic `.material.json` manifests are
tracked and name a sibling shader file, SHA-256, profile and parameter byte count.
The demo build prepares missing fixture binaries automatically and copies all
prepared material assets. After changing source or the helper, explicitly rerun
the builder. No compiler is loaded into the game.

The fixed profile's text checks catch direct resource/interface declarations,
but cannot prove preprocessor-expanded interfaces or SPIR-V semantics. Runtime
manifest/hash checks establish expected metadata and byte consistency, not proof
of binary layout. The native boundary validates bounded SPIR-V headers; SDL/Vulkan
creates the pipeline. **Custom shaders are trusted authored programs**, not an
untrusted-input sandbox. There is no installed shader-reflection dependency.
DXIL/D3D12 and Metal compilation/support remain unvalidated.

## Ownership and loading

`engine.Materials.Acquire(assetRoot, "materials/tint.material.json")` returns a
`MaterialLease`. Cache identity is the canonical manifest path, independent of the
physical shader file it names. Asset-root confinement and descendant link rejection
apply to both manifest and fragment. The manifest is source-generated JSON with
duplicate, missing, unknown and null field rejection. Bounds are 16 KiB of manifest
and 256 KiB of shader, with version 1, `sprite-fragment-v1` and 32 parameter bytes.

A context owns at most 64 distinct materials. Repeated acquisition shares one
pipeline; a live entry is an immutable loaded snapshot. Source edits are observed
only after its final release and a later acquisition. Failed source/hash/pipeline
loads publish no entry and retain other live materials. `MaterialInfo` exposes the
logical fragment path and source digest; native handles never enter authored JSON.
No implicit hot reload or global cache is introduced.

Keep the lease alive through submission. Release occurs on the engine's creating
thread and outside an active frame; repeated disposal is safe. Context destruction
releases every pipeline, invalidates remaining leases, and permits their later
disposal. Context-scoped monotonically issued native identities are never reused
after release or context replacement. A material owns its pipeline, while textures
retain their existing independent leases.

## Drawing and parameters

`MaterialDraw.Create(spriteV2, lease.Handle, parameters)` copies a region sprite,
material identity and `MaterialParameters(first, second)` into a 136-byte record.
Both vectors accept finite floats; their interpretation belongs to the shader.
There are no reflective property setters or mutable parameter objects. Handle zero
selects the original default shader and requires all parameters to be zero.

`EngineHost.Draw(camera, materialDraws, clips)` accepts the same optional zero,
one-broadcast or per-draw framebuffer clips. Per-draw lists must match the final
sorted draw order. Existing `SpriteBatch.RegionDraws` can be copied into a reusable
material array by the caller; this slice does not add a material field to the
authored scene schema or silently change its version.

The additive C entry point validates the entire submitted batch before mutation.
The current maximum sprite count covers both old and new submissions. Material
create/release is forbidden while a frame is active, so an accepted run cannot
lose its pipeline before `End`. Runs copy all eight floats, and merge only adjacent
draws with the same texture, clip, material and parameter bytes. No global material
sort changes painter order. Legacy submissions restore the default pipeline even
when mixed into a custom-material frame. Managed failed submissions abort the frame
so the next valid draw can proceed.

## Validation and sample

- `scripts/test.sh quick materials`: source/cache/ownership/ABI/failure contracts
- `python3 scripts/test-material-builder.py`: deterministic preparation, relocated
  source paths, syntax/resource errors, ordinary preprocessing and failure retention
- `--material-demo`: default/tint/desaturate, varied parameters and alpha order
- `--material-graphics-test`: actual custom/default pipelines, clipping, copied
  parameter values, released handles and reacquisition. Set
  `GAL_MATERIAL_CAPTURE_DIR`; validate with
  `python3 scripts/validate-material-pixels.py <directory>`

Headless material handles validate ownership and record submission only; they do
not compile or execute a shader. Software Vulkan readbacks establish the narrower
pixel behavior. No hardware timing or cross-platform support claim follows.
