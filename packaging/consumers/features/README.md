# Independent recent-feature consumer

This ordinary .NET 10 executable references only `Dotnet2D.Engine` and
`Dotnet2D.Native.Linux.x64` packages. It has no project/source references,
reflection access, test-host friend identity or native search path into the
checkout. Its caller-owned JSON map and generated two-pixel BMP are sufficient
for the combined feature scenario.

After rebuilding both local packages, run from the repository root:

```sh
PACKAGE_FEATURE_PROOF_ROOT=/tmp/dotnet2d-features-new \
  bash scripts/test-package-features.sh
```

Choose a fresh path outside the checkout every time. `DOTNET`, `PACKAGE_FEED`,
`PACKAGE_SOURCE_CACHE` and `AOT_CXX` select existing local prerequisites, using the
same defaults as the earlier module proof. The prepared SDK must be 10.0.401,
and the six local AOT/runtime packs must be version 10.0.12. The script never
builds the packages, downloads dependencies or publishes to a package feed.

The script copies this consumer, creates a fresh local-only feed/cache, runs an
ordinary Release JIT consumer, then performs exactly one fresh Linux x64
NativeAOT publish and run. It does not repeat the broad negative-compile or
minimal trimmed-consumer matrix. `logs/inputs.sha256`, per-stage logs, the AOT
map, retained JIT/AOT outputs and `results.json` identify the inputs and results.

The consumer checks:

- A real dynamic capsule contacts a generated floor, falls through an edited
  gap, and lands again after restoration
- Invalid tile-edit batches leave both the immutable map and collision intact;
  caller arrays, previous snapshots, the loaded asset and sibling instances stay
  independent after valid edits
- Exact circle and rotated-box queries reject known broad-phase false positives,
  honor body-local capsule rotation, reciprocal category/mask and sensor choices,
  return stable sorted shape IDs, and reject insufficient capacity without writes
- Documented narrow-phase tolerance and zero managed allocation across 4,000
  successful warmed exact queries
- Polled frame-entry events, repeated selection, explicit clip switching and
  bounded overflow, plus paused/real-time camera follow and zoom-aware bounds
- The actor and edited map share their tiny caller atlas, submit three headless
  frames, and release every physics body, shape and texture owner

This establishes public package/AOT boundaries and real Box2D behavior. Headless
submission is not a pixel-rendering, physical-GPU or device test. It is a focused
integration proof rather than a repetition of the complete engine test suite.

## Opt-in rendered author smoke

`bash scripts/test-author-smoke.sh` copies this same fixture into a fresh external
folder, runs its normal packaged headless path, then invokes `--author-smoke`
against an explicitly named **SVG-on source-runtime overlay**. Three frame markers
close/open/restore the floor and drive the actor image, collision queries, camera
follow and typed UI with raster/SVG art. The script checks three real framebuffer
captures and releases all owners. See [the author-smoke guide](../../../docs/AUTHOR_SMOKE.md)
for prepared dependencies, profile identity and verification limits. The default
native package stays SVG-off; this focused JIT check does not republish AOT.
