# M03-B Assets file-I/O port implementation closure

**Status: tentative implementation guidance under the user's proposal policy; no implemented or qualified port, numerical admission profile, or Scene producer approval. Keep `PreparedScene.QualifiedFootprint` null.**

## Decision

Select **one dedicated package-I/O worker per original Store issuer, shared by that Store's backing generations**. Windows uses an overlapped file with an explicitly owned event and `OVERLAPPED`, not `ThreadPoolBoundHandle`/IOCP. Linux executes offset `pread` on that worker. Both implementations own acquisition and capture the actual one-shot native file-close disposition.

This is the smallest selected implementation that avoids both the BCL file-handle cache and an additional native completion dispatcher. It deliberately serializes native package operations; concurrent admitted loads may still decode independently. It is not a general scheduler, process-wide worker service, new public option, or Runtime/World worker dependency. The extra copy and serialization are engineering tradeoffs, not measured performance improvements.

Use the [file-handle proposal](m03b-assets-file-handle-release-acknowledgment.md) for native-disposition semantics, the [bootstrap proposal](m03b-assets-prelease-bootstrap-and-close-closure.md) for the original issuer, and the [Scene invoker proposal](m03b-scene-actual-invoker-return-closure.md) for the higher caller. This follow-up selects their implementation order and lower mechanism; it does not re-review their completed analysis.

**A useful fully funded lane cannot yet be derived from repository evidence.** The package buffers can be bounded, but the complete worker/provider/control charges and retained fault/input graphs cannot. The unresolved terms below are prerequisites, not zero allowances or permission to reserve an unexplained amount.

## 1. Current source and contract boundary

Paths in this table are under `sources\TerraFX.Assets` unless stated otherwise.

| Actual source | Implementation consequence |
|---|---|
| `AssetPackageReader.cs:23-54,194-242` | FileStream construction precedes the cleanup `try`; hashing uses that stream, and offset reads use `RandomAccess.ReadAsync`. A backing is constructed only after validation. The new port must replace the entire generation's acquisition/read/release path, including hashing, rather than only final disposal. |
| `AssetPackageBacking.cs:14-40,107-112,171-264` | The existing backing owns the stream, reference gate and intrusive failed-release root. Extend this carrier into acquisition-free opening progress; do not introduce a competing file owner. Its current caught-disposal failure is not a native receipt. |
| `AssetStore.cs:41-55,220-335,360-496` | Store construction follows opening. Replacement elects one body, but its final barrier is body completion. CPU admission charges decoded wire length; there is no control/worker claim. Preserve current successful lease-construction transfer and backing-before-load-accounting rollback ordering. |
| `AssetStore.PackageLease.cs:79-126` | Reference inspections reserve decode bytes without obtaining another caller-visible lease credit. Therefore `MaximumLiveAssetLeases + 1` is **not** a complete queue bound. |
| `AssetLease.cs:139-147`; `AssetStore.ReadDescriptor.cs:59-102` | An anchored temporary reference exists before typed admission and can become final on refusal. Preinstall its progress owner before that acquisition; final close cannot be attached only to successful leases. |
| `AssetPayloadReader.cs` | None and Brotli share backing reads. Brotli additionally rents input and owns decoder workspace. Routing only Scene None reads through the port would leave the generation's lower ownership incomplete. |
| Runtime `Scenes\PreparedScene.cs:85-86`; `PreparedScene.Preparation.cs:894-902` | The footprint is null and the supplier-return latch remains set. A working file port would establish neither the planner's actual return nor preparation availability. |

[02 sections 2-4](../design/02-dotnet-api-memory-and-ownership.md#2-reuse-ledger) permit dedicated-I/O execution and direct private native ownership, require actual-use retention, forbid a `Memory<T>` bridge exception, and require partial-construction/finalizer closure. [04 section 4](../design/04-capacity-and-workload-profiles.md#4-content-package-and-asset-loading-limits) bounds package extents, not a complete allocation grant. [11 sections 3-5](../design/11-assets-import-streaming-and-packages.md#3-package-loading-leases-and-update-behavior) own checksum, lease, replacement and package semantics. [29](../design/29-project-setup-build-and-deployment.md#1-repository-and-assembly-dag) keeps Assets dependent only on Core; private OS imports do not authorize a Platform or Runtime dependency.

[30 section 6](../design/30-public-api-review-surface.md#6-assets-schema-codecs-and-packages) still describes wire-byte accounting and excludes measurement of arbitrary codec-managed allocations. The same-envelope control earmark is an intentional contract amendment, tentatively authorized by the user, not an already implemented meaning of that property. Chapter 02's linked Chapter 11 recovery-delivery section is still absent. These owner-contract updates precede an executable replacement, under [35 section 2.3](../design/35-implementation-roadmap.md#23-unit-prerequisites-and-handoff).

## 2. Selected lower mechanism

### Ownership and fixed provision

- The unpublished Store is the original issuer. Its admitted private worker control contains the queue, start/stop/return interests, native scratch and completion-dispatch provision. These are real subordinate controls, not another purse or recovery owner.
- The backing contains its own generation identity, native file field, opening/validation progress, final-close request and immutable native receipt. Windows file invalidity is `INVALID_HANDLE_VALUE`; event invalidity is null. Linux descriptor invalidity is `-1`, not zero.
- One **65,536-byte native I/O scratch buffer** belongs to the worker. Windows additionally has one initialized native `OVERLAPPED` and one manual-reset completion event. Only one native read uses them at a time. This explicit capacity is an implementation choice, not an estimated all-in byte allowance.
- A read request names the exact Store/backing, checked operation sequence, checked file offset and retained **typed byte-array/range owner**. Native I/O targets the fixed scratch; after proved completion, an ordinary synchronous copy fills that destination. No destination span survives suspension, and no new `Memory<T>` use is introduced.
- A request's actual private single-consumption completion source and its dispatch are charged before construction/registration. Prefer a source embedded in the operation record, with asynchronous continuation scheduling and raw count/error/cancellation facts; do not repeatedly capture exceptions inside lower sources. The final source/wake implementation must close its real queue and caller suffix, not merely select `RunContinuationsAsynchronously`.

The worker's static entry runs without inheriting application `ExecutionContext`. It executes only package-native work and owned copies, never codecs, Runtime progress bodies or public Task completion. Lower readiness wakes the existing control driver through the read-completion protocol. Assets learns no Runtime type or funding callback. Public wrappers and the Scene invoker retain their own required context semantics; suppressing worker flow is not permission to suppress those semantics.

### Queue and cancellation

Use intrusive request links in the already admitted opening/read/backing controls: one linked request per such control, checked enqueue/dequeue identity, and no new node allocation on cancellation or close. A read's next short-read issue cannot reuse its node/source while the previous consumption/return interests survive. Reserve final-close nodes in backing provision and stop signaling in issuer provision, so full ordinary admission cannot prevent retirement.

Queue membership includes queued, executing, ready-but-unconsumed and returning records. Existing load/inspection admission and positive record charges jointly bound it; a refunded decode credit does not refund a returning control. Replacement has its existing single opening election, but arbitrarily many lease-retained older generations must remain represented and funded. Never allocate a nominal `L+1` queue or hide waiting work in ThreadPool tasks.

Select cancellation **without a port-owned `CancellationToken` registration**:

1. Check the selected token before enqueue/issue and on worker dequeue. Queued cancellation may wait for the worker; no prompt-cancellation guarantee is added.
2. Windows waits on its read event in finite 50-ms quanta, checking the token between waits and requesting `CancelIoEx` once when cancellation is observed. This is a private polling policy, not an operation deadline or a hard latency promise. `CancelIoEx` success/`ERROR_NOT_FOUND` does not complete the read.
3. Linux checks before entering `pread`; a running syscall and its `EINTR` retries retain their operation until actual return, matching the selected existing boundary. There is no thread cancellation, `close`-to-cancel, signal protocol or `Task.Run`.

This removes cancellation-registration callbacks from the **port**, not caller token-source graphs, higher registrations, public observation cancellation, or their funding obligations.

### Platform operations

| Platform | Acquisition, read and retirement |
|---|---|
| Windows | Prevalidate and prepare the native path before acquisition, preserving the selected FileStream path/error behavior. Use direct blittable private imports for read-only `CreateFileW`, `FILE_SHARE_READ`, `OPEN_EXISTING`, overlapped/random-access flags; install the returned HANDLE immediately. Query that same handle's type/length. The worker resets its event/OVERLAPPED, issues `ReadFile` for at most 65,536 bytes, distinguishes immediate completion, `ERROR_IO_PENDING` and nonaccepted failure, and obtains the actual completion with `GetOverlappedResult`. There is no IOCP association or completion callback. Do not reset/free scratch after an uncertain wait/completion failure. Once generation use is quiescent, call `CloseHandle` exactly once and capture result/error before later cleanup or exception construction. |
| Linux | Use the admitted Ubuntu/libc 64-bit ABI for x64/arm64, with reviewed private bindings, not the private System.Native PAL. Prepare a bounded UTF-8 path; `open` uses read-only/close-on-exec and nonblocking acquisition so a FIFO cannot stall before type inspection. Install the descriptor immediately, then `fstat` that descriptor and require the selected regular-file/length shape. Retain the selected advisory shared-lock/random-access policy without claiming Windows mandatory sharing. The worker performs bounded offset `pread`, retries `EINTR` without losing ownership, and copies only returned bytes. After quiescence, call libc `close` once and preserve its actual return/errno. Other Unix systems/ABIs are not silently admitted. |

Preserve complete-package SHA-256 and checksum-before-layout-error precedence. Hash via BCL incremental SHA-256 over this same port's bounded chunks; do not add a cryptographic implementation or a second BCL file handle/cache. Reuse the current Foundation/profile/directory validators, exact-count entry construction, None/Brotli decoding and reference inspections. A single reusable opening padding buffer can cover the existing at-most-15-byte gaps.

Short positive reads advance checked offsets; zero before a declared extent remains EOF failure. Windows cancellation may lose to success; an aborted read is not decoded from an incidental byte count. A native call with unproved completion retains scratch, file use and a nonterminal operation. A timeout or failed observation is not permission to unwind those resources.

## 3. Acquisition prefixes, return joins and retirement

### Construction order

1. Perform usage/options/pre-cancellation checks and an acquisition-free scalar preclaim under the original `B0`, before owner/control materialization. A failure at this point acquires no package resource.
2. Construct and install the unpublished Store/backing and protected failure/return storage in the existing Assets root. Reserve worker startup, cold controls and every acquisition prefix before starting the worker or entering native code.
3. Prepare native paths/import entrypoints, scratch and Windows event/OVERLAPPED into those installed controls. Each acquired result is stored before another fallible action; no SafeHandle-returning constructor or string marshaler conceals an intermediate native owner.
4. Start the selected BCL `Thread` with a preinstalled start interest. A startup permit prevents package acquisition before the initiating ordinary caller has observed successful start return. Partial startup owns no package file; any possibly started thread/control still requires retained cleanup and a real join. `Thread.Start` itself is not asserted to have a free or closed native prefix.
5. Execute file opening and same-handle checks on the worker, then owned hash/validation phases. Failed opening retains its original carrier even if no backing/Store is returned. Reverse-order cleanup continues through independent safe siblings; primary failure is not replaced by cleanup failure.
6. Publish only validated backing data. Factory/replacement transfer requires exact acceptance; cancellation after atomic replacement does not undo that switch. Never retrofit the port around a previously acquired BCL-owned generation.

### Actual enclosing callers

| Boundary | Required observer; what remains charged |
|---|---|
| Worker loop → ordinary native read/open/close helper | The loop observes helper return/unwind and its raw receipt. Native completion is separately established before ending access. A receipt written by the helper does not attest loop/thread return. |
| Worker loop → completion publication | The enclosing loop observes publication return and ends old request/buffer/outcome aliases. Its remaining loop/monitor/runtime tail stays under the **independently prepaid issuer worker provision**, not the next read's claim. Readiness may race this return; it cannot refund the record. |
| Runtime/control invoker → ticket registration and single consumption | The actual enclosing invoker observes registration and `GetResult` return/unwind, including source recycling and context restoration. Source-local `finally`, a ready Task, or another observer is not this join. Preserve the Scene proposal's exact outer-invoker obligations. |
| Invoker → Assets accept/advance/cleanup/result transfer | Ordinary calls return before their invocation interests end. Exact lease acceptance transfers surviving ownership once; unsuccessful anchored admission still owns and releases its temporary backing reference. |
| Independent retirement caller → worker termination | Close admission, drain/retain actual work, signal stop, then join the real thread **outside gates and never from that worker**. `Thread.Join` must be checked against the selected runtime's remaining native/control suffix. Only then retire worker scratch/event/control storage and its grant. `IsAlive`, a worker-written exit flag and Task terminality are not substitutes. |

The worker cannot wait for the Runtime invoker that must consume its result. A synchronous final lease/package-lease `Dispose` may wait outside gates for its already-reserved lower close; the package worker must never execute that Dispose or run its consumer inline. Async Store disposal uses its owned control path. No new waiter/control allocation is permitted merely because retirement started.

Store logical disposal still releases only its package-owner reference: **do not join/stop the issuer worker while an old lease, temporary reference or returning operation can require it**. Each retained generation keeps the original issuer and its grant alive. Final generation retirement may therefore occur after Store disposal, from the last real consumer. Candidate failure and independently failed current/displaced releases retain separate progress.

Idle-worker rooting also needs an explicit implementation, not a self-rooting Store delegate. Use a static thread entry and a prepaid subordinate I/O control with no strong public-Store/backing alias while parked; actual queued/active/return work supplies strong roots. A native-owning finalizer uses its preinstalled nonblocking retirement handoff, never a new allocation, blocking join or implicit close retry. Partial construction, handoff versus worker shutdown, and disposal/finalizer races must close before activation. A weak idle association is not itself that proof.

Record native file consumption **before** independent event/control cleanup and accounting. Follow the earlier proposal's Windows consumed/known-retained/indeterminate classifications and Linux-specific consumed-with-error rules, including reached valid-descriptor `EINTR` without retry. `EBADF`, interposed/denied calls and unclassified outcomes are not successful release. Preserve the original owner/possible debt on uncertainty. Later cleanup failure or repeated disposal cannot issue a second close against a consumed or uncertain handle number.

## 4. Same-envelope derivation and the exact missing bounds

Let `B0` be the original Create options' `CpuAssetBudgetBytes`. Replacement options are not another purse and cannot refinance old debt. Preserve their applicable existing wire/decode/count restrictions, but never raise the original total. The new control scope must be stated explicitly in Chapters 04/11/30; zero-budget opening intentionally becomes a pre-acquisition refusal. Current `AssetStoreOptions.Validate` checks nonnegativity only: do not use Chapter 04 hard option ceilings as observed implementation bounds until enforced.

For qualified actual charges `q`, use disjoint membership:

```text
Debit(t) =
    FixedIssuerAndSharedAnchor
  + FixedWorker(t)                         // thread, queue, scratch, event, dispatch
  + sum Keep(g,t)                          // every exact retained generation
  + sum OpeningPhase(o,t)
  + sum PortRequest(r,t)                   // includes ready/returning records
  + ExistingDecodedWireDebit(t)
  + UnspentProtectedCleanupAndReturn(t)
Debit(t) <= B0
```

`Keep` includes the backing, entries, file, final-close node/receipt and retained inputs; its close node is not charged again as a request. Opening includes its actual hash/parser controls and buffers. `PortRequest` includes the lower source, queue/native-use/consumption/return controls, not the shared scratch again. Its destination must already have a genuine originating buffer grant: wire length alone is not that proof. Scene invoker/semantic/result costs remain under their original Scene claims; no Scene grant pays the Store worker, and no World/P term is silently removed by this proposal.

Spending converts reserved provision into actual charge; it does not add another entitlement. Before Create, reserve the full possible candidate/opening/failure/return peak plus fixed provision. Before replacement, add the candidate peak and protected retirement suffix to **all existing debt**, including every older leased, failed and returning generation. Admission precedes allocation/queueing; release only ended or acknowledged-transferred portions. A stopped failed worker or retained native outcome keeps its original claim.

### What can already be bounded

The current package reader and `EbinReadLimits.Foundation` provide:

```text
header                    64 bytes
profile buffer            <= 65,536 bytes
directory buffer          <= 64 + 32 * 131,072 = 4,194,368 bytes
one padding buffer        <= 15 bytes
selected worker scratch   65,536 bytes
```

Their conservative simultaneous **raw storage capacities total 4,325,519 bytes**. Add the actual entry-array capacity for at most 131,072 `AssetPackageEntry` values, its CLR stride/header, backing/Store/control layouts, validators, hash state, native/runtime controls and protected outcomes. The 32-byte wire entry is not authority for CLR stride. No World `ArrayCharge` formula automatically applies to Assets.

Hashing an 8-GiB file is streamed, not an 8-GiB array or queue. Short reads/retries reuse their existing records. Once a positive qualified record charge exists, remaining `B0` bounds simultaneous queue/control records; still-returning records continue to consume that capacity. This establishes finite **membership**, not a numerical charge or useful default fit.

### What repository evidence does not bound

| Unclosed term | Exact reason and required closure |
|---|---|
| `q(FixedWorker)` and its cold/shared suffix | There is no Assets worker implementation or qualified charge for Thread startup, stack reservation/commit/guards/TLS/runtime controls, contended wait machinery, completion dispatch and actual termination. Requesting a stack size or observing a warm allocation count does not bound that graph. Close the chosen Thread/runtime/OS prefix, storage and return implementation before assigning a numerical provision. |
| `q(OpeningPhase.HashProvider)` | BCL incremental SHA-256 still owns algorithm/provider construction, native workspace and disposal. Package extent and a 32-byte digest do not bound the CNG/OpenSSL/provider graph from repository evidence. Keep BCL crypto; qualify its selected implementation and simultaneous workspace rather than replacing it with an invented constant. |
| `q(RetainedInputsAndOriginalFaults)` | Registrations retain arbitrary codec objects through `AssetStoreRegistration`; tokens may retain caller sources; logical contexts and original exceptions/EDI may retain graphs not bounded by package counts, wire length or two exception slots. The selected port avoids invoking custom codecs and avoids its own cancellation registrations, but does not erase those retained edges. Close exact selected input ownership/independent grants and original-fault delivery/capture capacities; arbitrary graph traversal, truncation or relabeling as “borrowed” is not a grant. No uniform finite formula for all currently accepted registrations/tokens follows from these options. |
| `FixedIssuerAndSharedAnchor` / protected return | The existing static intrusive root and `Lock` provide reachability, not cold/lifetime funding. No actual Assets control-layout catalogue or original grant covers their complete standalone-failure, finalizer-handoff and public fault/publication suffix. Chapter 11's missing primary/cleanup delivery definition must also close; a raw native receipt only avoids lower recapture sites. |

The first two terms can be finite for a selected implementation but have **no established bound here**; the retained-input/fault term additionally lacks an input-size or independently funded-ownership contract. Thus the raw-buffer subtotal does not establish a useful lane under the default 536,870,912-byte budget, much less every allowed smaller budget. Reserving all `B0`, rooting all successes forever, or always refusing is not closure.

**Distinguishing schedules, derived from source rather than executed:** an empty package currently opens with zero CPU/decode/lease limits despite allocating opening state; the new admission must refuse before acquisition. Holding old package leases over successive replacements accumulates real `Keep(g)` debt even with no current decoded asset bytes. Concurrent package-reference inspections invalidate an `L+1` queue derivation. Each case must affect admission without inventing another quota.

## 5. Coherent batches and dependency order

Future filenames below are implementation locations, not files created by this proposal.

| Batch / dependencies | Source and owning contracts | Tests and exit condition |
|---|---|---|
| **A. Original issuer and bounded accounting contract — first** | Chapters 02/04/11/30: close the acquisition-free original Store grant, same-envelope compatibility, original shared-root attribution, retained input/fault delivery and late-release authority. Then `AssetStore.cs`, `AssetStoreOptions.cs`, `AssetPackageBacking.cs`: acquisition-free controls and actual preclaims; no file/worker execution before the grant exists. | Extend `AssetStoreRegistrationTests.cs` and `AssetStoreTests.cs` for real admission boundaries, zero/insufficient budget and unchanged existing wire/decode/lease restrictions. Do not create test-only issuers. Unknown profile charges keep the executable route unavailable, not zero-cost. |
| **B. Real native port and worker — after A's authority** | Extend backing through private `AssetPackageBacking.File.Windows.cs` / `.Linux.cs`; worker/control in `AssetStore.PackageIo.cs`, with actual request storage in the owning operation. Implement acquisition prefixes, event/pread execution, intrusive queue, cancellation, one-shot close, partial construction and actual caller joins together. Preserve Assets→Core only. Compile the real complete candidate to derive layouts/closed membership before enabling acquisition; no stub native backend. | Extend `AssetPackageReaderTests.cs`, `AssetStoreTests.cs`, `AssetPackageLeaseTests.cs` with real files for missing/inaccessible path, exact ranges, empty/large directory, cancellation, repeated close, held generations and worker retirement. Execute only once the complete lower preclaim fits. Native error/return branches without safe real stimuli remain explicit observation gaps. |
| **C. Whole-generation opening and read integration — after B's useful funding/return closure** | `AssetPackageReader.cs`, `AssetPayloadReader.cs`, backing and Store/package-lease/read-descriptor paths: route hash, metadata, ordinary loads, anchored loads and inspections through the same port. Reuse semantic validation and codecs; install real pre-lease owners before reference/credit acquisition. Existing typed-load proposals retain their separate funding obligations. A capable generation cannot retain a hidden BCL path. | Reuse current literal package/profile fixtures. Preserve checksum-before-parse behavior, None/Brotli lengths/errors, exact descriptor/backing identity, load rollback, replacement versus disposal and old lease usability. Low-budget cases use the newly derived complete claim, not merely declared wire bytes. No production switch until all sharing paths have genuine operation grants. |
| **D. Selected Scene read/invoker seam — after C; keep footprint null** | Assets' real `AssetLoadOperation<T>` and `AssetStore.ReadDescriptor.cs`; Runtime `PreparedScene.cs`, `PreparedScene.Preparation.cs`, `ScenePrefabExpansionPlanner.cs`: install the child before anchored acquisition, consume actual lower completion through the existing root, and close ordinary return/context/result handoffs. Then convert the selected planner/cache suspension chain, preserving traversal and validation. | Extend existing Scene/Prefab package-lease and prepared-Scene ownership tests for the actual factory Scene/Prefab 2.0, package-None, no-resource-preparer slice. Preserve stale-current refusal, old anchored reads, original cancellation/fault ordering and registration/consumption/close races. Do not clear the supplier latch merely because the lower Task completed. |
| **E. Composite profile and observations — after each actual producer is closed** | Final Assets worker/open/read/cleanup profile plus Scene planner/receiver/J/K/publication profile, under unchanged original limits. Only complete useful fit and fulfilled actual-return gates permit a later footprint decision. | Ordinary owning-project Debug then Release validation with nonzero fresh selections/TRX; separately execute required native/AOT/public consumers under Chapter 35. Lower native success, managed tests and empty-Scene restore are not nonempty Scene or M05 acceptance. |

The immediate implementable work is A's owner-contract closure and then B's **real compile-only** implementation/layout analysis under those authorities. Do not begin with recursive planner rewrites, another observer, a broad allocation probe or M05 pixel tests. Resolve the specific unbounded inputs and missing fixed charges before executing the candidate; that is the admission gate, not a prerequisite to writing and inspecting its actual private implementation.

Existing ordinary Assets behavior remains on its current implementation until an explicitly selected whole-generation replacement satisfies these gates. There is no fallback from a refused/failed capable generation to an unaccounted BCL path, no migration of an already owning SafeFileHandle, and no claim that the old path is thereby qualified.

## 6. Remaining gates and evidence

Required lower gates are original issuance and recovery delivery; complete acquisition/startup/finalizer prefixes; useful numerical fit; queue/source/caller/thread return; and one-shot native disposition for every release site. The Scene driver still separately needs its full context/fault/planner/cache/receiver/publication closure. M05 Scene-dependent extraction/rendering and nonempty restore remain gated on those real producers; no fabricated snapshots, placeholder owners or test-only producers can advance them.

Use the existing test projects and [Building](../project/Building.md) procedures when implementation is ready. `AssetPackageBackingFailedFinalStreamReleaseRootsBackingForProcessLifetime` proves managed reachability for a throwing stream subclass only; it is not a real constructor, native-close or worker-return witness. If actual short-read, cancellation race, constructor-prefix, native failure or return stimuli cannot safely reach a required branch, record that exact Chapter 35 observation, missing capability/stimulus and retry condition. Do not repeat an unchanged blocked probe or force a failure by corrupting raw handles.

No build, test, allocation probe, native experiment, AOT execution or remote action was performed for this proposal. No implementation, existing proposal, maintained contract, fixture or roadmap is changed.

## Bounded analysis inputs

Snapshot UTC `2026-11-19T10:47:31.0259561Z`, HEAD `2221dd5f88805adb0b31387feb7426998dda2004`. The worktree contains concurrent unrelated work; dirty Chapters 04/30 are inspected context, not accepted amendments. This is a local analysis freeze, not a repository inventory or native/binary attestation. The two schema catalogues are preservation/identity inputs only.

Each set digest is SHA-256 of ordinal-sorted `relative-Windows-path<TAB>lowercase-raw-file-SHA256` lines, LF-joined, UTF-8 without BOM/trailing newline. Exclude this output.

```text
A: sources\TerraFX.Assets\
AssetPackageReader.cs, AssetPackageBacking.cs, AssetPackageEntry.cs,
AssetPayloadReader.cs, AssetStore.cs, AssetStoreOptions.cs,
AssetStore.ReadDescriptor.cs, AssetStore.PackageLease.cs, AssetStore.PackagePin.cs,
AssetOwnedReadDescriptor.cs, AssetLeaseLifetime.cs, AssetLease.cs,
AssetPackageLease.cs, AssetStoreRegistration.cs, TerraFX.Assets.csproj,
NativeAssetSchemaCatalogue.cs;
sources\TerraFX.Core\EbinReadLimits.cs;
sources\TerraFX.Runtime\Scenes\
PreparedScene.cs, PreparedScene.Preparation.cs, ScenePrefabExpansionPlanner.cs,
SceneSchemaCatalogue.cs

D: docs\design\
02-dotnet-api-memory-and-ownership.md, 04-capacity-and-workload-profiles.md,
11-assets-import-streaming-and-packages.md, 29-project-setup-build-and-deployment.md,
30-public-api-review-surface.md, 35-implementation-roadmap.md;
docs\project\Building.md, Roadmap.md;
docs\proposals\
m03b-assets-file-handle-release-acknowledgment.md,
m03b-assets-prelease-bootstrap-and-close-closure.md,
m03b-scene-actual-invoker-return-closure.md

T: tests\TerraFX.Assets.Tests\
AssetPackageReaderTests.cs, AssetStoreTests.cs,
AssetPackageLeaseTests.cs, AssetStoreRegistrationTests.cs
C: .github\copilot-instructions.md, .github\workflows\ci.yml,
Directory.Build.props, Directory.Build.targets, global.json
```

| Set / input | Files | SHA-256 |
|---|---:|---|
| A | 21 | `bc9214481344098345fb2d405083aecae9ef3ddbb207fd20a19ab1c4de3c416f` |
| D | 11 | `368a9ed9db8be8400805b7a966baddf6a63a0b003cb108a0fa684d5c1144cad1` |
| T | 4 | `a8d71a83359dd1a1bd954c796215065fb726d6387fca4a6e1308ca0b60ff4390` |
| C | 5 | `dae084263f610f43a0d4b34730bfeffd8883fb9e5296ca806efddb1498e7f06b` |
| ALL | 41 | `5091268e4c18097cc02a1bd5138fddd4bf886e7666b3b56e9b7cb9d0d94f991a` |
| Dirty Chapter 04 | 1 | `a4d86f3fa373a64548435c53582bf5916e29d77f34650f9d161ccec90e6f0245` |
| Dirty Chapter 30 | 1 | `d4f622d69ed58ab96118c36fbbf4e2d31e062a3cbf3ce3ba1d64309f662fe4ed` |
| `NativeAssetSchemaCatalogue.cs` | 1 | `6b9537ea8cefebb48b5dc24261a8c03fb059e3314e7da089f29a639d9e55620a` |
| `SceneSchemaCatalogue.cs` | 1 | `cf0253cf1c207644f0b6c76b0392977e02b0939ff355ed825f38d42705f2b32a` |

Recheck these inputs before implementation or disposition. Changed bytes require scoped reassessment; this proposal installs no profile and authorizes no qualification claim.
