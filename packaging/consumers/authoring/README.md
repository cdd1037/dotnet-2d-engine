# Safe authoring package consumer

An ordinary public `PackageReference` application with unsafe blocks disabled.
It exercises managed draw/pass descriptions, borrowed typed resources, copied input
views, map-allocated actions and fixed-step edge helpers. The `compilefail` project
must fail for eight specific resource-kind/numeric-ID mistakes; it is excluded from
the runnable application.

From the checkout, use `scripts/test-authoring-packages.sh` with `PACKAGE_FEED`
pointing to the prepared engine/native packages. It runs JIT, checks the expected
compiler errors, and publishes/runs NativeAOT once. Set
`PACKAGE_AUTHORING_PROOF_ROOT` to a fresh directory outside the checkout on a
filesystem with room for the existing runtime packs. No native rebuild, dependency
installation or package publication is performed.

The tiny BMP and tint shader/material are caller fixtures. The SPIR-V fixture is
built from this repository's MIT-licensed `shaders/materials/tint.frag`; its ordinary
material manifest follows the runtime's existing asset contract. They are not
included in the engine package. Headless tests establish validation, lifetime and
allocation behavior, not rendered pixels or device acceptance.
