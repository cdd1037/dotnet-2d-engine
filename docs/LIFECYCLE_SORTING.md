# Explicit behavior ownership and stable sprite sorting

For entity-owned resources that must survive behavior replacement, see the
additive [OnDestroy/composition contract](CSHARP_COMPOSITION.md). Its callbacks run
after all behavior cleanup, ordered by lifetime ownership. The behavior attachment
contract and its creation-order cleanup below are unchanged.

## Owned attachment

`World.AttachBehavior(entity, behavior, attach)` is an opt-in lifetime boundary.
The `attach` callback receives a `BehaviorLifetime`; register explicit undo work
with `OnDetach(Action)`. No component registry, ECS or implicit IDisposable scan
is involved. The existing `entity.Behavior = behavior` remains a non-owning
assignment, but replacing a previously owned attachment retires that ownership.
Assigning the identical behavior through the property is a no-op.

```csharp
Action changed = () => behavior.OnChanged();
world.AttachBehavior(entity, behavior, lifetime =>
{
    // Register undo first, so later setup failure can unwind this acquisition.
    lifetime.OnDetach(() => publisher.Changed -= changed);
    publisher.Changed += changed;
    // Explicitly own a resource only when this attachment is its sole owner:
    // lifetime.OnDetach(resource.Dispose);
});
```

The lifetime object is registration-only during setup. Keeping a reference does
not permit registering cleanup after attach returns; callers cannot manually
release it. There is no automatic discovery of subscriptions or native resources.
Unregistered side effects remain the caller's responsibility. Avoid firing the
publisher from setup: candidate setup runs while the old attachment is still live.

### Defined ordering and failures

- Setup runs synchronously on the world's normal thread. The previous attachment
  remains active until setup succeeds. Failed setup runs all candidate cleanup,
  keeps the old behavior/lifetime, and throws AggregateException containing the
  setup error followed by cleanup errors.
- Successful replacement publishes the new behavior/lifetime before retiring
  the previous one. If old cleanup fails, the replacement **stays committed**.
- Cleanup runs in reverse registration order within each attachment. Destroyed
  entities are cleaned in world creation order, not parent/child traversal order.
- Destruction/unload first performs all existing transform preflight. Failure
  there changes neither world state nor attachments. After preflight succeeds,
  all doomed objects are invalidated, lookup entries removed, surviving relations
  detached, and an unloading scene marked unloaded before cleanup runs.
- Every registered cleanup is attempted once even if another throws. Errors are
  reported together after the operation commits. Do not retry destruction as if
  it had rolled back. Fix the failed external cleanup separately if necessary.
- Cleanup must use captured publisher/handler/resource references. A destroyed
  entity's guarded properties are no longer readable. IsAlive, identity and world
  lookup can be used to observe the committed boundary.
- Same-world mutation, behavior replacement, Update and sprite extraction from
  attach/detach callbacks are rejected. This includes property setters. Defer
  subsequent gameplay work until the outer operation returns. Read-only lookup
  remains available; unrelated worlds are not globally locked.
- Destroying an entity during its ordinary Update remains allowed. If cleanup
  throws, Update propagates the aggregate but its finally block restores update
  state and compacts dead entities. Later Update calls work normally.
- Moving/picking up/persisting an entity retains its attachment. Persistent carried
  items survive scene unload. Explicit owner destruction retires owned descendants.
- Neither behaviors nor lifetime callbacks are serialized. Loading a world must
  bind runtime behavior and ownership explicitly, as before.

Tests cover successful/repeated replacement, stale registration, setup and rollback
failures, committed replacement/unload failures, all-cleanups-attempted, reentrancy,
update recovery, numeric preflight rollback, 25 repeated subscribed scene cycles,
and persistent pickup ownership.

## Sprite sorting

The former stable insertion sort was O(n²) for reversed/mixed layers every frame.
It is replaced by iterative stable merge sort, O(n log n), with an O(n) already-
ordered fast path. Equal layers always select the left element first, preserving
creation/snapshot array order. No GUID canonicalization or texture regrouping is
performed. Alpha rendering order and the legacy/affine views stay aligned.

Three scratch arrays are allocated only when an unordered batch first needs them
or capacity grows, then reused. This adds approximately 92 bytes per capacity
slot of managed scratch storage once sorting is needed (plus array headers), in
exchange for bounded sorting work. The default already-ordered case does not
allocate scratch. Warmed mixed sorting verifies zero managed allocations.

Tests include odd sizes, random/equal/reversed/changing layers, int.MinValue and
int.MaxValue, capacity growth, empty/single batches, stable ties, aligned legacy
and affine values, and warmed allocation checks. Allocation claims exclude initial
construction, first unordered extraction and capacity growth.

Exact regression and benchmark results are recorded in `evidence/lifecycle-sorting/`
and the isolated performance report. No package archive was replaced or uploaded.

## Provisional XML dependency experiment

This remains provisional pending the requested size-ancestry comparison with
the earliest 1.33 MB prototype; package size is not considered resolved.
The aggregate host currently sets the existing .NET property
`XmlResolverIsNetworkingEnabledByDefault=false`. This is independent of lifecycle
and sorting. It avoids rooting the default XML non-file URL resolver and its
unused HTTP/cryptography dependency closure in NativeAOT. `UiAuthoring.cs` is
unchanged: its explicit `XmlResolver=null`, `DtdProcessing.Prohibit`, bounds and
strict diagnostics remain the actual document-security controls. This setting is
not a global networking disable and does not alter JSON scene loading.

An isolated same-source comparison before the authored-scene addition measured
8,634,368 bytes with the default versus 5,165,552 bytes with the switch. It passed
identical JIT/AOT tests, UI captures and 26 valid/adversarial fixture results. Those
sizes must not be confused with the final aggregate binary, which also includes
the new authored-scene loader/tests. No validation path was removed or bypassed.

## Comparable sorting measurements

Same isolated .NET 10.0.12 JIT harness and virtual runner, tiering disabled:

| Extraction | Previous insertion sort | Stable merge sort |
|---|---:|---:|
| 1,000 ordered layers | 94.895 µs | 107.469 µs |
| 1,000 reversed layers | 5,066.540 µs | 156.220 µs |
| 10,000 ordered layers | 1,127.568 µs | 1,009.187 µs |
| 10,000 reversed layers | 578,798.740 µs | 2,983.740 µs |

All four warmed paths report 0 managed B/operation. Reversed cases use only ten
measured iterations, so these are rough means rather than percentile guarantees.
The large improvement supports removal of quadratic work; small ordered-path
changes may be runner noise. This measures CPU world extraction/sorting, not GPU
rendering, native allocation, target hardware or visible frame rate. Initial
scratch allocation and capacity growth are intentionally outside the warm sample.
Exact current output: `evidence/lifecycle-sorting/sort-benchmark.log`.


## Pre-XML-refactor aggregate verification, 2026-10-01

The shared source at this stage (including the separate authored-scene loader) passed:

- 8,574 assertions under both Release JIT and NativeAOT
- Original UI scenario: 23 assertions in each runtime, identical captured pixels
- Combined gameplay/UI: 16 assertions in each runtime
- Native UI-build CTest: 3/3
- Authored scene validation and three headless frames in both runtimes
- Three authored-scene software Vulkan frames in NativeAOT; capture visually
  checked (two composed cell instances on the expected dark background)

The pre-XML-refactor aggregate executable measurement was 5,290,432 bytes. It includes the
new authored loader/tests, so it is not the isolated 5,165,552-byte switch probe.
This is a measurement only, not a final shipping-size claim. Existing package
archives remain unchanged. Physical GPU, physical input and actual IME remain
outside this run's verification.

For the subsequent integrated XML change and final milestone totals, see
[validation](validation.md). This section retains the earlier comparison point.
