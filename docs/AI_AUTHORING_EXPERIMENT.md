# Small AI authoring experiment

2026-10-02 UTC · .NET2D verified packages · official Godot 4.6.3 .NET

## Finding

Both engines passed both small maintenance tasks on the **first submitted
candidate**, independently checked through native execution and unchanged
regressions. No implementation-correctness advantage was observed. Godot's two
observed submission windows were shorter, but four attempts do **not** establish
a general productivity advantage, reliability rate or token-efficiency result.
Visual-editor/tooling differences were excluded.

| Task | Engine | Observed window | Production diff | Logged shell sessions | Failed build/runtime sessions |
|---|---|---:|---|---:|---:|
| Inventory Discard | .NET2D | 5m47s | 5 files, +17/−5 lines | 17 | 3/0 |
| Inventory Discard | Godot | 4m21s | 2 files, +14/−5 lines | 9 | 0/0 |
| Enemy pulse toggle | Godot | 3m44s | 2 files, +44/−7 lines | 10 | 0/0 |
| Enemy pulse toggle | .NET2D | 4m00s | 1 file, +47/−14 lines | 11 | 0/0 |

All four first candidates passed evaluator-owned acceptance. Diff lines are
ordinary physical additions/deletions, not a productivity score. Verification
was not hidden: the four attempts respectively added/modified 2, 2, 2 and 2 test
files, with +179/−0, +195/−0, +180/−0 and +168/−1 lines. The last includes
Program.cs changes solely for a test entry point; its production change is
Composition.cs. Godot test scripts/scenes remain classified as tests even when
inside the application project directory.

## Frozen tasks and acceptance

The protocol and separate evaluator fixtures were frozen at
**2026-10-02T13:37:08.319246Z**, before the first attempt. No fixture was changed,
no solver received a solution or repair feedback, and no existing test was weakened.

- **Inventory:** third per-card Discard action; exactly one stock removed only
  when unpaused, visible and positive. Health, gold, selection and visibility stay
  unchanged. Check unknown/removed/empty/paused guards, CanDiscard, stable command
  IDs, save/load/restart, actual native pointer routing of ulong IDs above 2^53,
  disabled interaction and correct identity after reorder/shrink
- **Lifecycle:** optional initially enabled/disabled enemy; idempotent toggles;
  exact external subscriber counts and independent delivery; preserved native
  body/node and nested weapon identity; destruction of enabled/disabled enemies
  and rooms; retained-handle toggles become safe no-ops after destruction; failed
  construction and external parent destruction clean up correctly

Each evaluator ran new native checks followed by the existing UI or gameplay
regression. Original shared inventory-rule tests were also independently rerun.
.NET disabled-button pixels were inspected offscreen; Godot native disabled/style
and layout checks passed, but its rendered pixels were not verified.

## What changed and what failed

The inventory rule edit was identical in the two independent submissions.
.NET2D additionally crossed schema/command registration, command dispatch, RML
and RCSS; Godot changed its existing typed widget adapter. This establishes a
specific cross-file wiring difference, not evidence that RML inherently causes
AI errors.

Both lifecycle implementations retain explicit per-instance subscription state.
.NET2D uses entity-owned cleanup and Root.IsAlive; Godot uses tree entry/exit and
IsInstanceValid for retained handles. Both fit ordinary typed application objects
without engine changes.

The three .NET UI build-session failures were not demonstrated game-code defects:

1. An optional rules-test project was invoked with --no-restore before its assets
   metadata existed. A local restore fixed it
2. Two new test-project parallel builds failed with zero diagnostics. Single-node
   building succeeded without source changes; the cause was not established

There were no runtime-session failures or independently rejected candidates.
All failed outputs were preserved in the original companion study.

## Measurement boundaries and confounds

- Window starts immediately before the first logged shell session reads TASK.md
  and ends immediately before the explicit submission-marker command starts.
  Exact fractional UTC endpoints and dispatch/receipt records are in the JSON
- Windows include deliberation between commands, inspection, editing, chosen
  verification, tool latency, builds and runtime checks. They exclude startup
  before the first read, study/evaluator preparation, orchestration gaps and
  independent post-submission evaluation. They are not isolated thinking time
- Approximately 64 seconds of baseline/preflight execution occurred outside
  these windows. Existing local SDK/packages were used; no downloads, installation
  or network waits. The optional-test restore miss above stayed inside run 01
- Shell sessions may bundle operations and are **not total model tool calls**.
  Run 01 concurrent build/test sessions reused ID 8, but category/UTC and separate
  outputs preserve both; totals count start events. Command durations can overlap
- Four fresh agents, no inherited conversation, same inherited model, xhigh
  reasoning, sequential order as listed. Model identity and token telemetry were
  unavailable; **no token or context-size metric is claimed**
- Both sides received equivalent working samples, the same behavioral task,
  immutable regressions, relevant API docs and prevalidated commands. Their
  architectures, documentation length/format, prior model familiarity and chosen
  verification effort differ. No new-feature solution was supplied to either
- One attempt per task/engine is a convenience sample. The lifecycle task is close
  to .NET2D's recently designed ownership API and may favor that starting point.
  Do not calculate a general speed ratio or statistical significance
- Headless/offscreen Linux only: no manual playability, physical-input, Godot pixel
  parity, cross-platform, runtime-performance or AOT conclusion

## Durable record and practical use

[Machine-readable summary](verification/ai-authoring-experiment.json) preserves
exact timings, categories/failures, acceptance contracts and frozen fixture hashes,
source/candidate hashes, package identity and evaluator-log hashes. Inputs were
copies of the earlier authoring samples, guides at `974a550`, and the verified
package feed; the separate character recipe at `67992d5` was not part of these
tasks. Final integrity checks found 98 original source/data identities unchanged,
unchanged packages/fixtures/tests, and a clean engine checkout.

The historical full source/capture/log directory was
`../ai-authoring-efficiency/`. This repository record survives its cleanup but
is **not a self-contained executable reproduction**; hashes identify evidence,
they do not replace missing files. No runtime files, large logs or captures are
copied into this documentation commit.

Prioritize clear local contracts, discoverable source boundaries, reproducible
native acceptance and actionable diagnostics over LOC alone. The concrete UI
opportunity is simplifying or improving feedback along
rule → schema/command → RML → dispatch. This sample does not justify another broad
lifecycle framework or a universal engine ranking.
