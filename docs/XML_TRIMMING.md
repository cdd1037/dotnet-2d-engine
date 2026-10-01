# Selected XML trimming integration

Date: 2026-10-01. The approved temporary design is merged into the main managed
host. JSON source-generation, native code and XML safety settings are unchanged.
The only implementation changes are `managed/UiAuthoring.cs` and the new
`managed/UiXmlModel.cs`. All pre-existing main C# files matched the controlled
research baseline before this merge; their hashes are retained in `docs/verification/`.

## Fresh merged verification

- JIT Release build: zero warnings/errors; self-test **8,574 assertions passed**
- NativeAOT Release/linux-x64: publish succeeded; self-test **8,574 passed**
- JIT and AOT UI scenario: **23 assertions each passed**
- JIT and AOT combined room/UI scenario: **16 assertions each passed**
- JIT/AOT final UI captures byte-identical to each other and the original
  XDocument validator's capture; SHA-256 in `verification/capture-sha256.txt`
- Physical keyboard/IME, hardware GPU and audible output are not covered by
  these scripted offscreen/software-rendering tests

Use [test tiers](TESTING.md) for ordinary iteration and explicit NativeAOT/UI
verification. The local historical research scripts depend on this workspace's
extracted toolchain and are not a portable installer.

## Differential evidence reused, not rebuilt

The merged `UiAuthoring.cs` and `UiXmlModel.cs` are byte-for-byte the previously
verified candidate source. `verification/reused-jit.log` and `verification/reused-aot.log`
record **7,092 cases, 2,579 original acceptances, zero differences** for each
execution mode, plus 203 existing UI authoring assertions. The comparison checks
accepted models and complete diagnostics (code/file/line/column/field/cause).
Cases include namespaces, comments, character/entity handling, declarations,
DTD/external-entity rejection, limits, malformed XML and deterministic mutations.
Those differential runs were reused rather than rerunning their AOT build after
an exact-file copy. `verification/candidate-source-parity.txt` records that verified identity.
`tests/xml-differential/` retains the original implementation and comparison harness; its
project now references the merged main validator for future explicit reruns.
Timing lines in reused logs are historical research microbenchmarks, not new
merged performance measurements.

## Exact size and why

Same-source full host: **5,290,432 B before; 4,742,560 B merged; 547,872 B saved**.
Both use .NET SDK 10.0.401, runtime/compiler packs 10.0.12, stripped Release
NativeAOT linux-x64, invariant globalization and the default XML networking switch
disabled. Source hashes and final executable SHA-256 are retained. The isolated
candidate and main output have equal file size, but different binary hashes;
byte-identical executable output is not claimed across project/build paths.

The bounded read-only profile model avoids general LINQ-to-XML virtual
`XNode.ToString` -> XML-writing dependency roots identified in compiler graphs.
The framework `XmlReader` remains, including its general XML support costs.
This is the entire changed dependency closure, not a standalone library-byte
measurement. No checks, tests, diagnostics or required runtime capabilities were
stripped. It is a prototype host measurement, not a minimal release package.
No existing ZIP was rebuilt or uploaded.

The selected interim design, future native/RmlUi diagnostic direction and the
inherited comment-boundary binding limitation are documented in
[UI prototype](UI_PROTOTYPE.md).

## Optional research reproduction

From the repository root, run `dotnet run --project tests/xml-differential/Compare.csproj -c Release -p:PublishAot=false -- assets/ui evidence/xml-differential` for the differential harness. Publishing that project with NativeAOT is a separate opt-in toolchain operation. Neither command belongs to the default quick loop. The harness preserves the old validator as a comparison fixture, not production code.
