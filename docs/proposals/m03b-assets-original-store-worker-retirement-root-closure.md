# M03-B original-Store worker retirement-root closure

**Disposition: tentatively selected for compile-only implementation progress under the user's unblock policy. This closes the proposed idle-root representation, not the complete native-thread/outer-return profile. No native-owning worker construction, native activation, stable-runtime admission or Scene/Consumer/AOT gate is authorized.**

## 1. Selected lifecycle and remaining blocker

Split the [package-I/O proposal's](m03b-assets-original-store-package-io-source-capacity-closure.md) strong worker into:

- **`OriginalPackageIo` execution state**, nonfinalizable, passed to the one `Thread.UnsafeStart(state)` entry. It owns the thread/queue/control protocol but no package-native pointers, generation registry, application context or retained service fault.
- **`OriginalIoLifetime`**, the finalizable original-I/O owner, strongly held by the Store and every actual native-use capability. It owns scratch, OVERLAPPED, Windows I/O event, service-return operation and exact generation registry. It strongly references execution state; ordinarily idle execution state has **no reference to L**, weak or strong.
- One preinstalled **retirement mailbox** in execution state. It is null during ordinary idle life and becomes a strong reference to the original lifetime owner only when explicit retirement or its finalizer accepts the one-shot transfer.

The finalizer publishes that mailbox and an atomic stop request. It does not enqueue a ThreadPool item, signal an event, take a gate, traverse generations, release native resources, allocate, wait or join. The existing worker observes the request through finite idle polling, drains actual I/O, retires owned package-native resources and queues its prepaid terminal callback **from the worker**, not the finalizer. That callback is the independent join caller. There is no restart worker, permanently registered waiter, process-global worker root or additional scheduler.

**Precise remaining blocker:** public `Thread.Join` and the terminal ThreadPool callback do not supply the complete native-thread/control and outer-dispatch return receipts required by the selected contracts. The execution-state/source retirement suffix and partial-start/dispatch-failure branches remain unqualified. They cannot be refunded or activated merely because the native I/O resources were separately consumed. Section 6 gives the exact stop.

This is a bounded rectification of the parked-owner root, not a new plan for M03-B/M05. It proposes private ownership/layout changes under Chapters [02](../design/02-dotnet-api-memory-and-ownership.md), [11](../design/11-assets-import-streaming-and-packages.md), [29](../design/29-project-setup-build-and-deployment.md) and [30](../design/30-public-api-review-surface.md). Their acceptance and [35](../design/35-implementation-roadmap.md#4-completion-gates) remain separate. Keep `PreparedScene.QualifiedFootprint` null and the ordinary BCL path unqualified; no refused native generation falls back to it.

## 2. Reachability and actual-use invariants

```text
Store / live generation / accepted use -> OriginalIoLifetime L
L -> OriginalPackageIo E
L -> native blocks, service operation, exact generation registry

running Thread -> E                                  // no idle E -> L edge
E -> queued/active/returning request -> backing -> L   // only actual work
E.retiringOwner -> L                                  // only accepted retirement
```

`E` has no L/service/registry/public-Store edge while ordinarily parked. Its Thread is started without application ExecutionContext flow; its static entry and terminal callback capture no L. Retained original faults/context stay in L's service operation, not E. Otherwise an exception, Task result or mutable application context could recreate the forbidden idle edge.

No path needs an E-to-owner lookup: an ordinary active request obtains L from its exact retained backing; a retirement helper obtains L from the mailbox; startup has the initiating caller's strong L; and L's finalizer reaches E through its own field. No WeakReference, GC weak handle, owner-ID lookup or registry search is selected.

A separate non-inlined active-work helper obtains that request's strong owner, validates exact issuer/backing/admission, owns all native access/copy/publication aliases, and returns before the entry parks. Dequeued request and owner locals live only in that helper. Clear E's active alias through the enclosing returned-call protocol; leaving a local owner in the outer loop and assigning null is not the chosen lifetime boundary.

Queue/ready/returning requests intentionally root L until their actual uses/returns end. They are not idle leakage, but an unjoined record can still block collection and retirement. Do not prune it to obtain finalizer eligibility. Every raw file/block operation requires the admitted strong L and existing native-use credit, atomically against closing.

An empty queue/mailbox is not an abandonment receipt. E keeps polling until real work or L's retirement transfer arrives; it does not discover, reconstruct or replace an owner. An unexpected E-only loop failure likewise has no hidden owner lookup and cannot authorize native cleanup; its original pending-mailbox/exit disposition remains a gate.

Move the original generation-ledger head from Store into L, reusing the backing's existing intrusive generation link. This gives the finalizable original owner a finite, funded membership of every owned native file even when no Store is returned or reachable. E does not retain that list while idle. Successful final release unlinks ended membership; no history list or permanent root of successful Stores is added.

All package access capabilities must retain L. Abandonment retirement closes L's admission before walking the registry; a resurrected wrapper cannot reacquire native use afterward. Worker cleanup may retire orphaned native resources only after actual users stop. It does not manufacture per-lease Dispose calls, decrement arbitrary reference counts or invalidate CPU values still legitimately retained. This proposed abandonment rule and finalization-order behavior require owner review; ordinary explicit Store disposal still releases only its own backing reference.

### Registry cycles, queued finalization and resurrection

The `L -> generation registry -> backing -> L` cycle is not itself a GC root. [GC traces reachable graphs from roots][gc-roots]; without a Store/use/request/mailbox root reaching L, that cycle does not prevent finalization. Once L is finalization-reachable, its registry/backings remain reachable with it until its finalizer transfers the same intact graph into E's mailbox. The finalizer does not unlink registry entries or release child resources.

L is the **sole finalizable native-ownership authority** for these ordinary files/blocks. The current AssetPackageBacking has no finalizer, and this candidate adds no independent native-file finalizer or owning SafeHandle beneath it. Backing records carry fields/receipts, not a second independently finalizable owner. Finalizer ordering cannot therefore cause a backing finalizer to close a handle before L's worker uses it. Any later independent finalizable subordinate changes this contract and needs a reviewed ownership transfer; general finalizer order is [not guaranteed][finalize].

Publishing `this` into the mailbox is deliberate **retirement-only resurrection**, not renewed public ownership. Lifecycle/admission stays closed, resources/claims stay installed, no ReRegisterForFinalize is issued, and the mailbox stays a strong root through actual retirement/return. A resurrected Store/lease may observe existing CPU data but must not obtain new native use after closure.

A strong reference acquired **after L was queued for finalization does not cancel that queued finalizer**. Therefore eligibility/entry alone cannot prove zero actual users. A resurrected wrapper can race with finalizer closing:

- Admission's last open-state check and publication of its charged request/use occur within the same E gate. An open-state check that precedes the finalizer's atomic closing transition can linearize an already admitted operation; a later check refuses it.
- The finalizer closes L and publishes mailbox/stop without taking that gate. The worker takes E's gate before freezing retirement, so a producer already inside the admission critical section must finish its publication first.
- Retirement waits for the actual queued, active, ready/returning, native-use and publication interests to end before walking or unlinking native registry state. Source controls remain strongly rooted during that drain. No resource is reclaimed merely because L is on the finalizer queue or has been resurrected.

This uses the existing gate, lifecycle/state bits and queue/use fields; it adds no counter or storage term. The compiled admission/closing handoff and partial-construction branches must still be proved. If a path can acquire a raw handle/use without this retained owner/publication discipline, cycle reachability is not a safety repair and native activation remains unavailable.

## 3. Construction, handoff and stop order

### Installed construction

1. Issue the original checked B0 source claim before carrier construction, preserving usage/pre-cancellation precedence. Install acquisition-free L/E, the original issuer/service operation and generation membership before external acquisition.
2. Install L's E reference and E's precreated static terminal callback; the retirement mailbox is null. Native fields remain at their invalid sentinels. All paths/imports/blocks and failure/return provision have their original grants; no native acquisition precedes the missing qualification gates.
3. Construct at most one Thread with the static parameterized entry. Pass **E**, never L. A start permit prevents native work before the initiating ordinary caller observes UnsafeStart return. Stop/mailbox is checked even while that permit is closed.
4. The initiating construction caller strongly holds L until publication or an accepted failure-retirement transfer. A known never-started prefix is retired by that actual initiating caller; it does not invent a started-thread Join. A possibly started/unreturned prefix transfers to the same mailbox/worker and remains debt until its exact start/termination disposition is established.

The Thread constructor has its own opaque construction interest. Its unreturned prefix is not closed by installing a source holder. No weak-wrapper/GC-handle construction occurs, and no new worker is created to recover from a failed prefix.

### Allocation-free finalizer transfer

L's finalizer uses only the already installed E and scalar/reference intrinsics:

```text
elect L's closing/transfer state once
compare-exchange E.retiringOwner from null to this L
publish E.stopRequested with release ordering
return
```

The mailbox is the ownership transfer: E, already rooted by the running thread, strongly retains the same L and its existing grants. Stop publication follows mailbox publication; observing stop cannot authorize use of an uninstalled payload. Duplicate transfer accepts only the identical owner/epoch and does not overwrite another payload or request another worker.

Finalization does not acquire E's queue lock or call a potentially allocating helper. Intrinsic entrypoints and partial-state branches need their actual compiled proof. No finalizer diagnostics formatting, Task/TCS publication, ThreadPool queueing, native close or managed-owner traversal is selected. `GC.SuppressFinalize` follows actual completed cleanup or accepted retaining transfer, never a timeout. A finalizer transfer does not reopen admission or request another finalization.

For partial construction with no installed E and no acquired native resource, the finalizer has no native action. A prefix that acquired anything without a usable installed handoff is an implementation error/gate, not a silently successful fallback. Existing original-owner recovery debt remains attributed to B0.

### Explicit close and finalization races

Store logical disposal closes ordinary admission and releases its package-owner reference. Old leases/generations and admitted/returning operations retain L and the worker until their real I/O interests end. It does not publish full-worker stop solely because the Store body completed.

After the last required generation/use ends, the actual ordinary retirement caller elects the **same** L transfer and mailbox used by the finalizer. Reference-bearing explicit callers keep L alive but do not unschedule an already queued finalizer. Partial construction, queued finalization and resurrection share one monotonic close/transfer election and one mailbox, with no double resource close. The loser observes the winning generation and leaves its ownership intact.

A finalizer recognizes abandonment, not an arbitrary zero lease counter. Its ordinary worker may then close the original registry after admission is closed and actual native use is quiescent. A failed or still-consuming generation remains debt under that original owner; repeated finalization/disposal does not retry uncertain close. Failed roots must not become an unconditional root for healthy workers.

## 4. Exact Windows/Linux wake and retirement behavior

Select **finite idle polling**, not another wake event. This avoids both native use of L's event from an ownerless idle frame and a finalizer queue/signal call that might allocate or block.

| State | Windows | Linux |
|---|---|---|
| Queue admission | Publish the charged intrusive request under E's existing object gate. Native access has its strong L. | Same. |
| Idle | Entry checks queue/stop under the control protocol, leaves the gate, rechecks stop with Volatile.Read, then calls `Thread.Sleep(50)` outside every gate. No L lookup or L-owned event use occurs while sleeping. | Identical managed call; the selected runtime uses WaitSubsystem.Sleep. No eventfd, pipe, pollfd array or signal handler is added. |
| Finalizer stop | Publish mailbox then stop only; no Pulse, SetEvent, CancelIoEx, close or wait on the finalizer. | Same; no write/close/signal syscall on the finalizer. |
| Request/stop races with park | A post-recheck publication waits for the next finite poll. A wake cannot be lost indefinitely merely because no Pulse occurred. The 50-ms interval is policy, not a scheduling or shutdown deadline. | Same. |
| Active native read | Keep the package proposal's WaitForSingleObject(50)/CancelIoEx/GetOverlappedResult protocol and strong actual-use owner. The I/O completion event remains L-owned and is never repurposed as an idle stop event. | A running pread/EINTR sequence retains L until actual return. Stop does not interrupt it, close-to-cancel it or free its scratch. |
| Native retirement | Worker observes the mailbox, rejects new work, drains actual request/return uses and runs ordinary registry cleanup with strong L. Capture each file/event CloseHandle result once; only proved quiescent blocks may be freed. | Same original membership; libc close is issued once, preserving the reviewed EINTR/consumed-with-error rule. Native block free requires proved last use. No fd-number probing or close retry. |

The [Windows Sleep path][thread-windows] uses runtime thread/wait state and finite SleepEx; [Linux][thread-unix] delegates to WaitSubsystem. Those opaque descendants belong to the execution-thread/wait interests. They are not L-owned blocks and are not assigned a byte price. Native/Runtime Async/AOT variants still need their own execution evidence.

The worker's ordinary enclosing loop observes the native-retirement helper return; that helper records immutable file/event/block dispositions before accounting. Scratch and the separate OVERLAPPED allocation have independent ended-use/free facts, represented in the existing release-state bits. No native I/O is issued after that helper returns. Failed independent siblings still retain their source/resource debt; caller failure does not replace the primary.

Only then does the worker call the precreated terminal callback's unsafe queue API once. The terminal callback is already represented by the package proposal's one terminal-dispatch interest. **Queueing may allocate an opaque runtime wrapper/queue segment on the worker**; it is not claimed allocation-free. The pinned [queue implementation][queue] constructs `QueueUserWorkItemCallbackDefaultContext<TState>` for an ordinary callback. No source node, public Task or new waiting service is introduced.

Use the existing retirement-queue state to mark the attempt before invoking the API. Do not enqueue a second callback on an uncertain queue result. The independent static callback's first ordinary action publishes **join-caller entered** in E's existing join state, then calls Thread.Join outside gates. The worker waits by finite polling for this entered fact before returning. This handshake prevents an uncertain/rejected enqueue from making the thread disappear with no independent retirement caller.

The callback does not wait for a worker flag before entering; it publishes entered **before** Join, so the worker can return and the callback cannot self-deadlock. Conversely, the worker never waits for Join completion. The actual worker entry observes loop return, then exits; any written loop-return fact is only that helper's returned-call evidence, not native termination.

If terminal dispatch never enters, the one original worker remains in stopped/retiring ownership with mailbox/grants live. It does not restart, retry an uncertain queue or complete shutdown. This is a genuine failed-dispatch/nonprogress branch, not normal-success permanent rooting or a process-global worker root.

The join caller separately owns Thread/join/control retirement and the remaining source handback. Native file/event/block consumption can be established by their actual cleanup calls before thread exit; **that proves no OS-thread, CLR-control, callback or outer-dispatch retirement**. Keep remaining claims until their real caller/transfer boundary is supplied.

## 5. Minimum declared source and opaque-interest delta

These are the minimum additions/moves **for the selected representation**, not a proof of globally smallest code or compiled layout. Use the package proposal's Windows-x64 RC field-slot Q; rederive actual changed declarations before any construction.

| Carrier | Complete proposed instance storage | Raw fields | Q |
|---|---|---:|---:|
| Nonfinalizable E | Nine refs: issuer, object gate, Thread, queue head/tail, active, returning head, static retirement callback, retirement mailbox; one Int64 request sequence; eight Int32: start permit, queued count, native use, publication use, stop request, loop-return observation, queue-attempt state, join state | 112 | 160 |
| Finalizable L | Three refs: E, service-return operation, generation head; three native pointers: scratch, OVERLAPPED, I/O event; two Int64: source claim, native-generation interests; four Int32: lifetime/transfer state, event-close state/error, block-release state | 80 | 112 |
| Store | Remove the transferred generation-head slot from the package proposal's 160-byte Store; keep the existing owner/service/issuer/operation fields | 117 | 152 |
| Backing / request / opening / issuer | Retype their existing worker reference to the exact lifetime/execution ownership as required; no added slot. Reuse the existing generation link and authoritative backing reference count | unchanged | 328 / 256 / 104 / 64 |

No second service operation is constructed: its existing 696-byte source provision moves under L. Native-generation interests count distinct required I/O generations, not a competing lease reference count. L reaches the original scalar issuer through E; the issuer and shared first-paying anchor gain no L/Store/worker reference.

```text
Old worker Q                         232
New E + L                           160 + 112 = 272
Store generation-head transfer      -8
Additional fixed source provision   272 - 232 - 8 = 32

S_root = 152 + 64 + 696 + 160 + 112 + 65,536 + 32 = 66,752
Cold source candidate = 42,152,248 + P(opening) + F(generation)
Two 512-char buffers / 511-char reported-name example = 42,155,392
```

K, OpenBase, the 256-byte one-shot request and its exact versioned membership remain the package proposal's expressions. The 65,536/32 native block reservations move into L, never receive another debit. No wake event/block/array term is added. Partial retirement returns only independently ended source subsets, not the full S above at logical Store disposal.

| Opaque interest | Delta / attribution |
|---|---|
| Owner association | No E-to-L weak association, wrapper or GC-handle interest. Actual requests and the accepted mailbox supply the only E-to-L strong paths. |
| Thread/start/join | Still one package Thread construction/start protocol, no restart. E is the start argument; full native/CLR tails stay separately owned and unqualified. |
| Idle wait | Thread.Sleep calls attach to the one execution-thread/wait protocol. No additional native owner event or per-poll source object. Framework cold/contended wait state is opaque. |
| Terminal dispatch | Same one precreated callback and at most one terminal queue attempt as the package proposal; move its issuer from finalizer to worker. Its actual enqueue wrapper, dispatch/context reset and return remain counted, not removed. |
| Native blocks/event/file | Same extents and exact generation membership, now under L. One Windows I/O event; none on Linux. |

A new acknowledgment object, root carrier, registry link, callback, source fault holder or array would change these numbers. The 32 is not padding reserved to conceal it. Removing the unused weak holder reduces E/S/cold source expressions by exactly eight bytes and removes its opaque wrapper/GC-handle interest.

## 6. Exact residual proof and implementation boundary

The root mechanism has a distinguishing schedule: public Store/backing use disappears while E's thread is parked; E retains no L, so L can enter finalization; its prepaid mailbox makes L strongly reachable again only for retirement; finite polling consumes that transfer. By contrast, any real pending/returning request retains L and prevents that collection. This is a source-derived lifecycle, not an executed GC/native observation.

The remaining blockers are specific:

| Gate | Evidence / required closure |
|---|---|
| Native thread/control termination | [CoreCLR OnThreadExited][thread-core] explicitly runs while the native thread remains alive. Unix Join observes its managed exit event; Windows Join has native-handle and closed/invalid/finalizer paths. Establish the actual selected path and any remaining opaque-tail ownership transfer. Join, IsAlive and an entry-written exit flag are not universal native-control receipts. |
| Terminal callback outer return | The pinned queue wrapper calls the callback, then [Dispatch][queue] resets ExecutionContext/SynchronizationContext, thread state and runtime completion bookkeeping. The callback cannot observe those enclosing returns. No accepted source/caller acknowledgment or complete prepaid tail transfer is derived here. Do not clear the mailbox/refund E/L/service claims on callback-local finally or readiness. |
| Partial start / unexpected exit / dispatch failure | Before-start, possibly-started, ownerless idle failure, helper failure, interruption and queued-but-not-entered retirement require exact retained roots and startup/exit disposition. A failure cannot bypass the mailbox or leave native ownership beneath an unrooted finalized owner. Compile the full branch set; no native probe is authorized to discover its funding afterward. |
| Finalization and compiled liveness | Confirm the no-owner parked frame, retained request-to-L path, non-inlined active helper, monotonic mailbox election, no finalizer gate/call allocation, intact registry cycle through handoff, queued-finalization/resurrected-admission drain and orphaned-generation cleanup against the actual compiler/runtime. Finalizer eligibility is not native-use quiescence. |

Thus the proposal selects a concrete **root/stop/handoff representation and finite source delta**, but does not assert a fully qualified retirement service. The materialization/native-activation gate remains closed on every target until these branches and actual enclosing-return/transfer obligations are closed. The stable .NET 11 PATH-child mismatch, Linux ABI/native profile, NativeAOT and Runtime Async gates in the package proposal remain unchanged. No Scene, CLI, nonempty restore, native diagnostic, rendering or public consumer result follows.

Next bounded implementation: implement the two complete private source carriers and mailbox/active-helper/terminal-handshake branches; migrate only their owning generation links/grants; compile and rederive the final catalogue. Extend existing Assets ownership/allocation/entry tests for ownerless parked eligibility, active/returning strong retention, registry-cycle handoff, already-queued finalization versus resurrected admission, explicit close with old generations, one transfer under races, partial-prefix/no-mailbox refusal and queue-attempt/entered ordering. Keep fixtures literal and test only the real selected controls; managed GC/wakeup tests are not native/thread/outer-return witnesses.

After a coherent source batch, use the package proposal's ordinary Assets Debug then Release selection and current `global.json`/central build settings. Confirm fresh nonzero cases/TRX. Execute native retirement only after its gates are genuinely closed; record an unsupported branch's exact Chapter 35 gate, missing stimulus/capability and retry condition rather than fabricating a thread/handle receipt.

## 7. Input freeze and preservation

HEAD **`9843f4f95ebb25890476a7c504b3d9429b35fb63`**. The bounded workspace input is the package proposal's exact 61-member section 8 set plus that proposal itself, whose raw SHA-256 is **`a06d30e336cdd3745e2cd93a5a577dd07a4956bc85d7bc95f0e013312c0a1112`**. The resulting 62-input union is **`bff1f2baf034bc2d835fb40044efde5055e417c7530dca7c7a158cc07bef859c`**, using the same ordinal Windows-path/TAB/raw-hash, LF/UTF-8 set-digest definition. Original 61-union, schema hashes and five protected dirty test bytes remain unchanged. Rehash before later disposition; acceptance cannot apply to changed inputs.

Public source evidence is pinned to unified revision `5254d8bcf59917edc1e1abc33bdf2442b77b1c70`: Thread/Thread.CoreCLR/Thread.Windows/Thread.Unix and ThreadPoolWorkQueue. The registered-wait source was inspected only to reject adding a permanent wait/extra callback service; no registration is selected. GC-root/finalization references support the cycle/resurrection discussion, not a native or binary attestation. The pre-review retirement draft SHA-256 is `2e00ddadadf3d214f59e3834e95a3753dfe15f26c53fa4ec53391b64cd9f1411`; this correction changes only this output and adds no workspace input to the preserved 62-member set.

Only this new proposal is created. Existing proposal, source/tests/design chapters/plan artifacts and protected dirty files are preserved. No build, test, allocation/GC/native/AOT experiment, commit or remote operation is performed. Source arithmetic/input hashes are checked.

[gc-roots]: https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/fundamentals
[finalize]: https://learn.microsoft.com/en-us/dotnet/api/system.object.finalize?view=net-11.0
[thread-core]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/coreclr/System.Private.CoreLib/src/System/Threading/Thread.CoreCLR.cs
[thread-windows]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Threading/Thread.Windows.cs
[thread-unix]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Threading/Thread.Unix.cs
[queue]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Threading/ThreadPoolWorkQueue.cs
