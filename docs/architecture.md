# Game Authoring Lab: isolated first slice

## Ownership

C# is the executable and owns the main thread, game loop, timing, world state, reusable sprite command storage and camera controls. C++ owns SDL initialization, window, input sampling, GPU objects, submission and audio. No native callbacks into managed code, no runtime embedding, no reflection dependency. The production Godot tree is unrelated and untouched.

The C ABI v1 uses an opaque context, fixed-width scalars, blittable structs, explicit struct sizes/version and integer status codes. The borrowed error string is thread-local UTF-8. One live context, with calls required on the creating/main thread. Destroy is explicit on that thread; finalizer-thread disposal must never be used. Pointer validity is checked against the active context; as usual for pointer handles, reuse of an address after a new allocation cannot distinguish an obsolete handle. Callers must discard destroyed handles.

## GPU path

SDL_GPU directly, not SDL_Renderer. One generated 32×32 RGBA soft-circle texture, one sampler, one alpha-blend graphics pipeline and a bounded dynamic vertex batch. CPU camera transform emits six vertices per quad. Upload buffers and destination vertex buffers cycle to avoid overwriting resources in flight. Insertion order is preserved for alpha compositing. A frame produces one draw for all sprites; a zero-sprite frame only clears. GLSL is compiled offline to SPIR-V and embedded in the native binary. The app has no shader compiler dependency.

The second slice adds context-owned BMP texture handles and full affine `gal_draw` commands while preserving the original ABI structs. Batches group adjacent identical textures, retain alpha order and expose resource-lifetime checks.

Current implemented shader/backend pairing is Vulkan + SPIR-V. Windows x64 ABI and CMake structure are designed to be portable, but D3D12 needs HLSL/DXIL assets and Windows runtime validation before enabling. Metal is future work. No silent backend fallback.

Per-sprite UV rects/atlas IDs, public offscreen-target APIs, custom materials, editor UI, physics and asset pipelines are not part of this initial slice. The sprite contract deliberately starts with an engine-owned texture to validate rendering and interop before expanding resource ownership.

An opt-in diagnostic capture renders to a same-format color target, blits it to the swapchain, and reads it back behind a fence. This validates the actual pipeline under SDL offscreen + software Vulkan without changing the gameplay ABI; it is not a general render-target API.

## Minimal world model

C# `World` owns ordinary `Entity` objects, stable runtime IDs, optional Sprite2D and behavior, and translation/rotation/positive-scale/shear transforms. Transform parent, lifetime owner and scene membership are distinct relations. `CreateChild` provides convenient shared defaults; explicit operations change each relation independently. Pickup/drop preserve identity and world transform. Room unload preserves explicitly persistent entities and safely detaches surviving links. Scene/lifetime operations validate affected transforms before committing. See `managed/WORLD.md` for exact rules and limitations.

## Failure and capacity policy

Maximum sprites configured once (1–65,536). Native and managed buffers are reused. Capacity or malformed batch submissions fail before mutating a batch. Nonfinite camera/sprite coordinates and invalid color/alpha are rejected. Audio uses a 200 ms precomputed mono float tone with click-reducing envelope and a bounded queue. Audio startup errors fail requested initialization instead of pretending sound exists.

HEADLESS is a deliberate contract-validation mode: no SDL window, GPU rasterization or audio. Its stats always show zero draw calls. A headless-only build rejects graphical creation. This allows lifecycle/ABI/camera validation in CI without mislabeling GPU success.

## Extension boundary

Before general assets, add generation-tagged resource handles, create/destroy texture operations, UV rectangles and explicit scene-vs-render snapshots. Before concurrency, retain SDL operations on the main thread and pass a bounded immutable render packet from workers. NativeAOT is a desktop deployment experiment, not a requirement to move world logic into C++.
