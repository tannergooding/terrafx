# M03-B Scene preparation ownership and completion handoff

**Status: tentative, user-authorized unblocker for review, not an accepted contract, implementation, or qualification result.** This proposal addresses only the loader/result ownership and preparation-publication gates left by [workspace closure](m03b-scene-preparation-workspace-closure.md), sections 4.1, 5, and 7. It preserves [budget accounting](m03b-scene-preparation-budget-accounting.md), section 9, including separately funded activation and its delivery tail.

## 1. Decision and boundary

Precreate the existing `PreparedScene` as the preparation's stable retention root, before option freezing or asynchronous work. Keep the **one preparation `WorldGuidUse` in that root**, with two separately ended consumption roles:

1. preparation producers, including their last promise-publication access;
2. the prepared/admitted body, including its options backing and cleanup.

Activation transfers the second role, not an independently disposable alias of the preparation use. A request returns that role after its last preparation-funded access; the root releases the use only after both roles have ended and their claims have actually ended or transferred. Preserve the request's separate activation use through its own `TrySet*` return.

Use an explicit `TaskCompletionSource<PreparedScene>` with asynchronous continuations. A small, non-`async` publication boundary must replace compiler completion of the **public** preparation Task. A source `async finally`, an awaited Task, or `Task.IsCompleted` is not a producer join.

There is a concrete remaining runtime-accounting gate: the selected CLR can allocate continuation-dispatch objects in proportion to unrestricted external registrations **inside** completion. Consequently, no constant covering *all allocations performed by `TrySet*`* follows from P or from this source. Section 7 proposes the smallest ownership clarification and source boundary; it does not invent a numerical bootstrap or certify that lane.

Unchanged:

- `P = snapshot.MaximumPreparationBytes`, default `67_108_864`, range `[1, 2_147_483_648]`; preparation prepays exactly `256 + P`.
- Each activation independently prepays `256 + P`, using that result's captured P. No smaller-cap retry, use sharing between attempts, new public option, or quota increase.
- Existing common exact-World/domain accounting and the allocation-before-use rule.
- Plan/bundle R, Assets ownership, and retained-receiver lifetime work remain with the workspace proposal. This document supplies their root/handoff boundary, not another planner, cache, Rendering, or Persistence allocation design. Restore does not acquire an independent public-preparation use or inherit public P.
- No public signature, wire format, schema version, diagnostic identity, acceptance ID, finalizer, global registry, or new process-U role is proposed.

## 2. Reviewed source facts, not earlier proposal snapshots

Paths below are relative to `sources\TerraFX.Runtime`. Section 10 identifies the raw working-tree bytes, including dirty inputs.

| Current owner/path | Established behavior | Remaining issue in this scope |
|---|---|---|
| `Scenes\SceneLoadOptions.ValidateShape` | Instance-ID validation now uses a fully written 16-byte stack buffer and `Guid.TryWriteBytes`. | The earlier allocating `ToByteArray` criticism is obsolete for these bytes. Preserve validation and its existing regression. |
| `SceneLoader.PrepareAsync`, lines 145-222 | Acquires the exact use, preclaims **two** `WorldAdmissionDomain.ArrayCharge(n, IntPtr.Size)` binding arrays before freeze, then returns one temporary-copy claim. Passes the remaining backing claim to preparation. | This is implemented preallocation, not merely a postallocation estimate. It does not cover the result/Lock/promise/driver/sort envelope. |
| `SceneLoadOptions.ValidateAndFreeze`; `SceneLoadOptionsSnapshot` | Copies, RFC-sorts and checks duplicates, then creates the immutable backing. Snapshot is a readonly value containing an immutable-array wrapper, not a separately allocated snapshot object. | Value copies alias one new backing. Clearing one value does not end another frame's or request's use. Immutable binding elements are caller-supplied objects, not newly cloned Scene objects. |
| `SceneLoader.PrepareCoreAsync`, lines 246-310 | Initially roots the loader, awaits plan preparation, then constructs `PreparedScene`. Clears its local snapshot before returning. | Loader rooting does not root this invocation's options/claims. The result is constructed late. Returning from the C# body precedes the public Task's compiler/runtime publication. |
| Planner ordinary-entry/build boundaries | `SetRetainedOwner` replaces loader with root lease, builder, then plan. Builder cleanup can call `Quarantine(this)`. | Those objects do not each retain the complete options, outstanding producer, pending resource, and release-debt graph. A stable root must not be overwritten. |
| `PreparedScene.Dispose` | Clears options/plan/use fields under its gate, disposes the plan, returns the option claim, then releases the use. | There is no separate producer role. Failed option-credit return can leave only a numeric local after wrapper fields were cleared. A root must retain release debt until success. |
| `PreparedScene.DetachForActivation`; `SceneActivationRequest.Admit` | Already transfer plan, use, snapshot and backing-byte count together under the result gate. Constructor does **not** borrow the snapshot. | Preserve this admission-only snapshot capture. Replace only the preparation-use alias with the joined body-owner link. |
| `SceneWorldRegistry.TryEnqueue` | Checks queue capacity/persistence capture before `Admit`, so queue refusal does not consume the result. It can throw state/usage errors under `_requestGate`. | Preserve refusal-before-detach; deliver bounded state decisions outside gates. Scalar-cap inspection is not body capture. |
| `SceneActivationRequest` | Captures Task before enqueue can finish; body cleanup precedes publication; activation bootstrap/completion claims remain through `TrySet*`. Current constants are 256/512. | Do not move activation release earlier or treat these constants as preparation qualification. Change preparation-body completion only. Changed carrier fields still require requalification. |
| `SceneActivationExecutor.BuildEntityDescriptions`; `SceneComponentRestoreContext` | Callback contexts carry snapshot aliases and are revoked in `finally`. Their readonly `_options` currently remains populated after revocation. | End all valid callback/stack uses before returning the backing claim; sever the expired context's snapshot reference at revocation. No caller use of an expired context becomes legal. |
| `WorldGuidUseControl` | `Release` rejects active workspace. Successful release itself clears the retained owner; failed release changes state to quarantined. `IsValid` on the value carrier does not mean active/unreleased. | Keep the stable root installed during fallible release; do not clear it first or use a structurally valid alias as another release right. |

These are source observations, not claims that the dirty implementation passes its tests.

### Applicable authority

- Chapter 02, section 3: immutable capture, actual consumption before terminality, synchronous usage errors, asynchronous controlled continuations, completion outside gates. Its Runtime Async paragraph does not make compiler epilogues quiescent.
- Chapter 04, section 3: whole capacity, `ArrayCharge`, old/new coexistence, and separately owned controls/Tasks. The use's 256-byte control is not result/promise scratch.
- Chapter 12, process-registry owner table: one U with distinct `W_u/D_u`, aliasing rather than duplicate ownership, exact-generation cleanup and use-only quarantine.
- Chapter 16, Scene-loading paragraphs: funding before option freeze/async work; P covers result/task/untransferred cleanup; R transfers only identified capacity.
- Chapter 27, capture/disposal/closure paragraphs: capture-first ownership survives wrapper disposal; closure, target revocation and cleanup are different facts. This is an ownership analogy, **not** permission to import Persistence actors or its stale-target receipt into Scene loading.
- Chapter 30, Scene actors and surface: exact World/store binding, admission-only detach, refusal leaves the result reusable. Its parameterless issuer and old detach signatures are not the current implementation signatures; later promotion must update those actor rows explicitly.
- Chapter 35/current project roadmap: this remains an M03-B owner gate. Source inspection is neither managed qualification nor native/AOT/public-consumer or milestone acceptance.

## 3. Stable root and exact ownership state

### 3.1 Proposed private representation

Extend `PreparedScene`; do not allocate another per-operation owner. Existing `_gate`, `_assetId`, `_world`, `_assets`, `_options`, `_optionsWorkspaceBytes`, `_plan`, `_use` remain recognizable. Construct `_assetId` from the already validated input ID, not an existing plan.

Add these **private, prepaid** fields:

| Field | Type/purpose |
|---|---|
| `_producerState` | `PreparationProducerState`: `Entry`, `Running`, `Publishing`, `Ended`, `Quarantined`. `Ended` means the qualified final-access condition below, not merely a terminal Task. |
| `_producerParts`, `_publicationAccessEnded` | `int`, `bool`; count outstanding ordinary driver invocations/pending callbacks and separately latch returned publication. The count is at most three for this driver: Start, plan completion, resource completion. It is not a count of the planner's transitive async frames. |
| `_entryPartState`, `_planPartState`, `_resourcePartState` | Three `PreparationPartState` values: `Unused`, `Pending`, `Running`, `Ended`. A pending callback owns its part before registration can publish it; its callback consumes that part, not another use or a second increment. |
| `_bodyState` | `PreparationBodyState`: `Building`, `Available`, `Admitted`, `Cleaning`, `Ended`, `Quarantined`. |
| `_useReleaseState` | `PreparationUseReleaseState`: `Owned`, `Releasing`, `Released`, `Quarantined`; elects one final releaser. |
| `_bodyRequest` | `SceneActivationRequest?`; strongly roots the admitted body while the wrapper no longer holds its plan/options. |
| `_phaseOwner`, `_pendingOwner` | `IDisposable?`; current preparation graph and one staged receiver/resource during a sequential handoff. A child owns its complete partial graph; these are not an array of every allocation. |
| `_completion` | `TaskCompletionSource<PreparedScene>?`, constructed with `RunContinuationsAsynchronously`, cleared after the last publication access. |
| `_maximumPreparationBytes` | `readonly long`; exact P captured before admission and retained independently of the remaining purse. Freeze must reproduce it. |
| `_sharedWorkspaceBytes`, `_producerWorkspaceBytes`, `_bodyCleanupWorkspaceBytes`, `_publicationWorkspaceBytes` | `long`; disjoint qualified claim amounts, retained as release debt until the corresponding return succeeds. Shared carrier provision ends only at the two-role join; producer/publication portions end at proven producer last access; cleanup provision ends only after body cleanup or remains in quarantine. No numerical values are selected here. |
| `_firstCleanupFailure`, `_additionalCleanupFailureCount`, `_faultPair` | `Exception?`, `int`, `Exception[]?`; retain one original cleanup sample plus bounded additional-failure facts. The two-element publication array is prepaid, populated only for the two-failure case, and cleared after publication. Section 6 defines the exception order; there is no arbitrary aggregate clone or unbounded failure list. |
| Driver state | `_loader : SceneLoader?`, `_cancellationToken : CancellationToken`, `_pendingPlanTask : Task<SceneExpansionPlan>?`, `_pendingResourcesTask : Task<IDisposable?>?`, `_planReadyCallback : Action`, `_resourcesReadyCallback : Action`. Both callbacks are bound to this root. Include every field/delegate in bootstrap. No per-stage owner, continuation Task, or capturing lambda is assumed free. |

The enums are private implementation states, not public results. `_activationAttempted` may be represented by the one-way body transition; do not maintain a second competing admission flag.

`_optionsWorkspaceBytes` is the **single issuer ledger** for the frozen backing. In `Available`, the wrapper owns its snapshot and release right. In `Admitted`, the request owns that snapshot/release obligation; the root's amount records its delegated claim, not a second backing or a second right to refund. The request returns the obligation through the exact `_bodyRequest` link. Do not keep both an independently actionable request byte counter and this ledger.

Initially the root owns no plan/snapshot, producer is `Entry`, body is `Building`, and the one issued use remains `Owned`. Install `use.Control.SetRetainedOwner(root)` immediately after construction, before freezing, root loads, helper invocation or operation escape. Before that installation the synchronous entry guard alone owns the unescaped use and successful preclaims.

### 3.2 Private method boundary

The following are proposed signatures, not declarations added by this document:

```csharp
internal PreparedScene(
    World world, AssetStore assets, AssetId sceneId, WorldGuidUse use,
    long maximumPreparationBytes,
    long sharedWorkspaceBytes, long producerWorkspaceBytes,
    long bodyCleanupWorkspaceBytes, long publicationWorkspaceBytes);

internal Task<PreparedScene> PreparationTask { get; }
internal WorldGuidUseControl PreparationControl { get; }
internal void FreezeOptions(SceneLoadOptions options, long bindingArrayBytes);
internal void StartPreparation(SceneLoader loader, CancellationToken cancellationToken);

internal ScenePreparationAccess CaptureActivationHeader(
    World world, AssetStore assets, out long maximumPreparationBytes);

internal ScenePreparationAccess DetachForActivation(
    World world, AssetStore assets, SceneActivationRequest request,
    long expectedMaximumPreparationBytes);

internal void StagePreparationOwner(IDisposable nextOwner);
internal void CommitPreparationOwner(IDisposable? expectedOwner);
internal void AcknowledgePendingOwnerTransfer(
    IDisposable expectedPendingOwner, IDisposable receiver);
internal void InstallPreparedBody(SceneExpansionPlan plan);
internal void CompleteAdmittedBody(SceneActivationRequest request);
internal void QuarantineAdmittedBody(
    SceneActivationRequest request, Exception cleanupFailure,
    int stage, int category);
internal void RecordPreparationCleanupFailure(
    Exception failure, int stage, int category);

private void OnPlanPrepared();
private void OnResourcesPrepared();
private void PublishPreparationOutcome(
    ScenePreparationOutcome outcome, Exception? failure,
    CancellationToken cancellationToken);
private void ReserveProducerPart(PreparationProducerPart part);
private void EnterProducerPart(PreparationProducerPart part);
private void EndProducerPart(PreparationProducerPart part);
private void AbandonUnregisteredProducerPart(PreparationProducerPart part);
private void MarkPublicationAccessEnded();
private void EndPreparationProducer();
private void TryReleasePreparationUse();
```

`ScenePreparationAccess` is an internal, Scenes-only decision enum with `Available`, `NotAvailable`, `AlreadyAdmitted`, `WrongWorld`, `WrongAssetStore`, and `PolicyMismatch`. Only `Available` supplies a usable header or successful capture. `NotAvailable` covers unexposed building, logical disposal and cleanup/quarantine; the caller maps it to the existing usage/disposal channel outside gates. `PolicyMismatch` is invariant failure, not capacity refusal. `ScenePreparationOutcome` is a private enum with `Prepared`, `Faulted`, and `Canceled`; exactly one outcome is selected and one publication attempted. Faulted requires the preserved exception; Prepared requires an installed plan and frozen options; cancellation retains its selected token.

`PreparationProducerPart` is the private enum `Entry`, `PlanCompletion`, `ResourceCompletion`. Reserve changes that part Unused→Pending and increments the count before invocation/registration; Enter changes Pending→Running without increment; End changes Running→Ended and decrements once. All transitions are checked under the root gate, with invariant errors constructed outside it. A synchronous inline completion uses the same reserved part, never both an inline invocation and registered callback. Abandon is legal only for a proven-unregistered pending part; uncertain registration failure retains/quarantines it instead of refunding a possibly published callback.

The constructor's byte arguments are an exact partition of already acquired claims from the private qualified catalogue, not caller policy knobs. The root accepts their release obligation only on anchor installation. `PreparationControl` lends accounting access to the existing funding route while preparation is live; it conveys no independent use-disposal right.

`StagePreparationOwner` records the new receiver without dropping the old graph; `CommitPreparationOwner` makes that staged receiver current only after it owns the old graph's surviving resources. A null expected owner permits only the initial empty-slot adoption. For attachment into the current plan rather than replacement of it, `AcknowledgePendingOwnerTransfer` validates that exact pending object/current receiver and clears only the transferred pending slot. `InstallPreparedBody` moves the current plan into `_plan`, clears `_phaseOwner`, requires an empty pending slot and frozen options, and performs Building→Available after producer input aliases have ended. It does not leave a second phase-slot reference to a later detached/disposed plan. Validation failures retain both slots. No resource acquisition, callback, physical disposal, exception construction or Task completion occurs under `_gate`.

The loader's corresponding ordinary-route helpers have these proposed signatures:

```csharp
internal Task<SceneExpansionPlan> StartPlanPreparation(
    AssetId sceneId, SceneLoadOptionsSnapshot options,
    PreparedScene preparationOwner, CancellationToken cancellationToken);

internal ISceneExpansionResourcePreparer? ValidatePreparedPlan(
    SceneExpansionPlan plan, SceneLoadOptionsSnapshot options,
    CancellationToken cancellationToken);

internal Task<IDisposable?> StartResourcePreparation(
    ISceneExpansionResourcePreparer preparer, SceneExpansionPlan plan,
    PreparedScene preparationOwner, CancellationToken cancellationToken);
```

They reuse the existing planner, validation routines and resource participant/funding callbacks. A null participant goes straight to the ready state without inventing a Task. The existing package-bound `PrepareRestorePlanAsync` retains its distinct route; it is not rewritten into this independent-use driver.

The ordinary planner route needs this root as a **Scenes-owned retention link** in addition to its existing funding control/port. Its existing root-lease/builder/plan transitions use these methods instead of `SetRetainedOwner` or direct use quarantine. The package/restore route keeps its separate owner. This changes retention plumbing only; all allocation algorithms and receiver accounting stay in the workspace proposal.

### 3.3 State/graph transitions

| Transition | Rooted ownership after the transition | What may end |
|---|---|---|
| Entry guard → hidden root | Domain/process anchors → exact use control → root → use, claims and empty body slots | Entry guard becomes inert only after installation; construction failure before installation unwinds through that guard. |
| Freeze complete | Root → snapshot backing, with its one retained-array claim | Only the temporary copy/sort provision whose consumers returned. |
| Root lease → builder → plan | Root keeps current and staged owners through each all-or-none handoff; old owner clears only transferred fields | Only scratch with an actual end fact; never overwrite the sole root with a leaf. |
| Rendering result → plan attachment | Root stages the returned `IDisposable` before the fallible attachment operation; plan/lifetime assumes it before pending slot clears | No child grant is refunded merely because `AttachPreparedAssets` threw. |
| Preparation success | Root owns complete plan/options, body becomes `Available`, then producer becomes `Publishing` | Body may now be disposed or captured concurrently. Producer no longer reads options, plan, helper tasks or resource ports. |
| Available → admitted | Root → exact request → plan/snapshot; root retains preparation use and ledger | Wrapper's plan/snapshot fields clear, but no backing claim/use is returned. |
| Dispose wins | Body becomes `Cleaning`; root retains cleanup graph, snapshot/debt and use | Claims end only after actual corresponding consumers/cleanup stop. Concurrent Dispose is not another cleanup/release right. |
| Admitted body finishes | Request first clears all snapshot/borrow/plan aliases or transfers its plan to the installed receiver; then returns its role to the root | Root can return the option backing claim. It cannot end a still-live producer. |
| Producer finishes | Publication returned, all three driver parts are unused/ended, and all preparation-owned producer provisions have their qualified end/transfer facts | Publication/bootstrap claims can end; body stays owned if still Available/Admitted. |
| Both ended | One releaser is elected under `_gate`; lower accounting release runs outside it with root still installed | The one preparation U/`W_u/D_u` ends once, only after outstanding preparation claims are zero. |
| Cleanup/credit invariant fails | Exact use anchor still roots this same root, which retains failed graph, release debt, current role and bounded failure facts | No invented refund, successor retarget, normal-path quarantine, or retry on repeated Dispose. |

Plan/bundle grants already transferred to R do not return to P when those receivers later dispose. Installed or independently leased data must retain its own R/root after this preparation use ends; that remains the workspace proposal's retained-receiver gate.

## 4. Options capture, activation and disposal

### Entry and freeze

1. Preserve existing nonallocating shape/range/ID checks and pre-cancellation before use admission. Invalid/default/null/duplicate-binding behavior and RFC order do not change; duplicate detection remains synchronous during the funded freeze.
2. Acquire exactly `256 + P`. Preclaim the qualified bootstrap and protected terminal/cleanup provision **before** constructing the root/Lock/TCS/Task/delegates. If the valid P cannot support the minimum, use the existing synchronous entry-capacity refusal boundary, before creating an escaped promise or doing asset work. Its exception delivery is an admission observation, not an unfunded retained preparation Task or permission to borrow the control's 256 bytes.
3. Construct/install the hidden root. Capture `root.PreparationTask` once into the entry's return local while owned, before the driver can finish synchronously. Do not retrieve it again after publication/producer cleanup.
4. Compute and reserve the **existing two-copy** binding peak before `ValidateAndFreeze`. `FreezeOptions` commits the returned snapshot into the root before relinquishing entry aliases. Retain the current checked formula and one-array-after-freeze distinction. An allocated temporary array that is still being sorted/validated is not returned on a count change.
5. On synchronous freeze failure, no Task escapes. Clear all completed option consumers, release only proven-ended claims, and close both root roles through the same cleanup/join. Re-throw the original usage/duplicate failure. Root construction/anchor-install failure remains entry-guard cleanup; do not invoke a half-created promise to report it.
6. Only a successfully frozen root starts asynchronous preparation. The original options object is not retained as the operation's state. All later scalar selection uses the captured snapshot, not mutable remaining workspace.

### Activation header versus capture

`CaptureActivationHeader(world, assets, out P)` validates exact identities and Available state under the result gate and returns **only P**. It conveys neither a snapshot borrow nor a future admission reservation. Usage/disposal facts are delivered outside gates, before pre-cancellation under the Chapter 02/section 9 ordering.

After separate activation admission and request/bootstrap construction, queue capacity is checked **before** detach. Under the existing queue → result lock ordering, detach revalidates exact World, AssetStore, Available state, request identity and expected P. In one value-only transition it:

- installs `_bodyRequest = request` and the request's preparation-owner backlink;
- moves plan and snapshot to the request, while the root ledger delegates the backing obligation;
- clears the root's direct plan/snapshot fields and makes body `Admitted`.

Use an `internal void AcceptPreparationBody(PreparedScene owner, SceneExpansionPlan plan, SceneLoadOptionsSnapshot options)` value-only assignment method on the sealed request while the result gate is held: the request's fields/backlink are populated **before** root body fields clear. There is no out-local-only ownership interval and no allocating/virtual callback in that step. The queue makes the fully populated request visible only afterward. Its constructor continues holding no copied snapshot. Other loaders with the same exact World/store may use the result; a different store is not accepted merely because its schema/GUID matches.

For activation alone, use a specialized internal `SceneActivationEnqueueStatus EnqueueActivation(SceneActivationRequest request)` on the registry if necessary to return bounded queue/registry/capture decisions without throwing under `_requestGate`; leave unrelated request algorithms unchanged. Its complete internal statuses are `Queued`, `QueueFull`, `RegistryDisposed`, `PreparedNotAvailable`, `AlreadyAdmitted`, `WrongWorld`, `WrongAssetStore`, and `PolicyMismatch`. The loader maps them outside gates to the existing queue receipt or conventional usage/invariant error and cleans the unadmitted attempt through its existing activation tail. This is not a new public error layer.

### Request and wrapper release

In `SceneActivationRequest`, replace `_preparedUse` and the independently releasable `_preparedOptionsWorkspaceBytes` with `PreparedScene? _preparationOwner`. Add readonly `_assets : AssetStore` and `_maximumPreparationBytes : long` header fields; populate them through the revised internal constructor `(PreparedScene preparedScene, World world, AssetStore assets, long maximumPreparationBytes, WorldGuidUse activationUse, CancellationToken cancellationToken)`. Its activation-only admission entry `internal ScenePreparationAccess AdmitPreparationBody()` invokes the root's detach and clears `_preparedScene` only on success. Keep `_activationUse`, bootstrap, protected completion, Task capture and last-publication release ordering. These header/body-link fields are included in requalification of the request carrier, not hidden in the old constant.

`CompleteAdmittedBody(request)` is called once, after `SceneActivationExecutor` has returned/unwound, callback contexts have been revoked, request snapshot and local borrows are cleared, and the request plan has either transferred to the instance or completed cleanup. The request cannot call preparation `use.Dispose()`. The root validates the exact backlink, returns the delegated backing claim after last use, then marks body Ended and drops the request link. A failed return keeps the numeric claim/debt in the root.

Failed body cleanup instead calls `QuarantineAdmittedBody` with that exact request. It preserves the root/request graph and remaining claims, marks body Quarantined rather than Ended, and does not clear a failed plan/receiver. Independently ended option consumers may return A; failed return remains debt. This does not authorize replacing the activation use's request root with a leaf plan or returning its completion provision: that same activation delivery tail still publishes or retains its own failure/quarantine ownership.

`SceneComponentRestoreContext.Revoke()` must invalidate use and clear its snapshot alias; make that field mutable for this purpose. Callback stack aliases still have to unwind. Do not infer input consumption from revocation alone, or rewrite the rest of the callback allocation design here.

`PreparedScene.Dispose` wins only against Available, not an already admitted request. It moves to Cleaning while retaining graph/debt fields, disposes outside the gate, clears each field only after release/transfer, then returns the option claim. Disposal during public-promise dispatch may finish body cleanup and return without waiting for the producer; the still-live producer use/claims remain charged. Disposal after admission is inert for that body. Failed cleanup stays logically closed/quarantined; repeated Dispose does not retry or report physical release.

## 5. Claims and charge derivation

Use the workspace proposal's partition:

```text
P = F + X + T
remaining preparation D_u = 256 + F + X
```

F is free purse capacity, X active/protected claims, and T cumulative identified capacity transferred to receivers. Claim/return moves F↔X without changing total World/domain charge. Only actual X→R ownership transfer changes attribution. Never return an active claim merely to make the lower free-capacity transfer API succeed.

For n supplied bindings:

```text
A(n) = n == 0 ? 0 : checked(64L + RoundUp16(checked((long)n * IntPtr.Size)))
freeze-array peak = checked(2 * A(n))
retained option backing = A(n)
```

The zero case relies on this freeze path's shared empty arrays, not a claim that a separately allocated zero-length array has no header. On x64, n=1 gives A=80 and a 160-byte two-array peak. This is an accounting derivation, not an observed heap measurement. Snapshot value copies create no extra arrays; a request's copied struct does not create another A claim.

Keep the complete loader partition explicit:

| Claim | Derive from final supported implementation | End/transfer condition |
|---|---|---|
| J: bootstrap/driver | Final `PreparedScene` carrier, Lock, TCS/Task, exact driver fields/delegates and engine-created stage-registration/call holders, headers/alignment and first-use metadata not covered by another named grant | Last qualified producer/cleanup access, with any surviving body-control portion still funded. Do not release the entire carrier's provision while its Available/Admitted body remains engine-owned. |
| K: protected terminal/cleanup | Largest supported **Scene-owned** success/fault/cancel/refusal bookkeeping coexistence; original failure plus at most one original cleanup sample, the two-reference source array and actual destination List/EDI capacity, required new contextual capacity exception, and final release bookkeeping | Actual last publication/cleanup access. Optional work cannot spend K. |
| Temporary options | A plus actual supported sort/comparison/cold-delegate provision | Synchronous freeze consumer has returned or fully unwound. |
| Retained options | A | Available/result disposal or exact admitted-body completion, after every valid alias ends. Never queue refusal. |
| Other preparation X/R | Existing planner/resource/callback/receiver inventories from the workspace proposal | Their actual owner transitions; this proposal assigns them no additional allowance. |

J is partitioned into shared-carrier and producer-only provision; shared carrier/control provision remains until **both** roles end. Count it once at peak, not in both totals. K is partitioned into body-cleanup and publication provision. The four root ledgers record those partitions, not an overlapping second reservation. Cleanup-dependent portions remain in quarantine rather than being refunded at promise completion.

Before freeze, require space for the simultaneously live J, K, two A backings, and the actual sort peak. Preclaim in that order before corresponding construction. The old activation probes and the current 256/512 activation constants derive none of J/K. Do not use `Unsafe.SizeOf<class>()`, task count alone, an empty-continuation probe, or an arbitrary fraction of P as their catalogue.

Tests choose the independently derived final claim sum and one byte less at the **named reached boundary**. P=1 with one binding must continue refusing before copies/root reads; it does not qualify the minimum bootstrap or any later boundary. Budget admission does not guarantee CLR allocation success.

## 6. Failure, cancellation and closure matrix

| Path | Required owner/result disposition |
|---|---|
| Invalid shape/ID/range, pre-cancellation | Existing synchronous usage exception or pre-canceled Task; no independent use, root, option copies or asset work. Runtime creation of the ordinary entry observation is not retained Scene work. |
| Exact-use or minimum-entry refusal | Existing synchronous admission/capacity error, no escaped preparation promise, no smaller P. Undo only successfully acquired entry claims/use after actual synchronous unwinding. |
| Bootstrap construction/anchor failure | Entry guard retains grants; no resource acquisition has occurred. Release unescaped successful allocations/provision after unwinding. If bookkeeping fails, keep the exact preowned control/root and remaining grants; do not create an unfunded recovery owner. |
| Freeze duplicate/sort/allocation failure | No escaped promise; root/guard retains the full outstanding copy/entry partition until consumers unwind. Preserve primary usage/allocation failure; failed claim release remains numeric debt in that root. |
| Admitted plan/load/validation/resource failure | Stage returned resources before further fallible operations. Stop further body work, join valid consumers, attempt independent safe cleanup, then fault the public promise with the original execution exception. Capacity exhaustion after promise escape is the existing contextual `InvalidOperationException`, not a preparation receipt or partial result. |
| Admitted cancellation | Original Task becomes canceled only after actual reads/callbacks/option borrows end or transfer and required cleanup is accounted. Preserve selected cancellation-token semantics, including cancellation reported by an awaited producer; do not classify cancellation solely by the entry token's current flag. Cleanup failure remains a fault, not canceled success. |
| Resource cleanup fails | Keep root → failed phase/pending owner/request → remaining graph, outstanding X/R and release debt. Use-only quarantine; no World-binding quarantine. Publish through the bounded original-failure rule immediately below, not an arbitrary aggregate clone. If that protected disposition is not derivable, K's gate remains open. A still-consuming child prevents terminality; quarantine of stopped-but-unreleased storage is not a consumer join. |
| Available result disposed while preparation publication is held | Clean body/A once; producer retains J/K and its use until final publication access ends. Do not wait on a continuation needing that same thread. |
| Queue full/persistence-capture refusal | Request has no snapshot, body role or preparation-use release right. Its separate activation receipt/tail completes normally; preparation body/A remains reusable. Repeated attempts have distinct Tasks and activation uses. |
| Disposal or another admission wins between header and detach | Losing request captures nothing. Deliver usage/disposal facts outside gates and clean only that activation attempt. A scalar P read never extends snapshot ownership. |
| Admitted activation canceled/rejected/faulted/registry disposed | Request joins its real consumers, cleans or quarantines its body, clears aliases and returns the role to the root. Wrapper disposal cannot revoke it. Its activation use still funds its own terminal delivery. |
| Activation succeeds | Plan/storage/assets transfer to `SceneInstance` before request role completion; snapshot/A ends after final activation consumers. Installed plan/bundle R remains independently owned. Preparation producer may still keep its one use live. |
| Domain closes before independent admission | Refuse that new operation via existing admission channel; capture/issue nothing. No new U for cleanup. |
| Domain closes after admission | Already-owned cleanup/claim-return remains legal against exact original generations. Closure does not refund the use or authorize new body work. Do not re-admit a transfer or cleanup credit. |
| Exact World disposed/revoked during preparation or before activation | Ordinary binding revalidation prevents stale publication/application; unwind through original root. A published Available result can retain data without live application authority. Never resolve a same-GUID successor or another store. |
| Use release or claim-return invariant fails | Keep the root installed and its still-owned grant/debt facts. `WorldGuidUseControl.Release` can have partially advanced lower release; do not claim a previously ended process cell still exists, mint it again, or refund remaining domain interests. This is engine failure, not normal refusal or successful drain. |
| Publication itself fails | No recursive fallback completion or new unclaimed Task. Preserve fixed outcome if already terminal; never dispose a successfully published body as rollback. Retain/quarantine still-needed publication/cleanup grants and report through the existing permitted engine failure channel. If publication failed before terminality, do not claim its Task completed or its producer quiesced. Actual OOM need not yield a successfully delivered error. |
| Caller retains completed Tasks/results | Completed fault/cancel observations or disposed/consumed result wrappers retain no plan/options/request through the root. An intentionally undisposed successful result still owns its body, as before. No caller GC, finalizer or Task disposal ends a use. |

**Proposed preparation failure composition:** keep the original execution exception first and at most one original cleanup exception second, using the TCS's standard enumerable-exception overload only for that two-element case. Await still throws the original first exception; `Task.Exception` exposes both original identities. With cleanup failure but no execution fault, including cancellation followed by failed cleanup, publish the original cleanup exception as a fault rather than a canceled Task. Additional cleanup failures retain bounded numeric facts and rooted unreleased owners, not an allocating list of exceptions. The root's precreated two-slot array is used only when both entries are nonnull; its new runtime copy/list/EDI peak is part of K. This is a proposed Scene-specific bounded-sample rule under Chapter 02, not an assertion that current Scene cleanup already preserves its primary failure. A later Dispose or admitted activation failure uses its own existing synchronous/activation outcome channel and never edits the already completed preparation Task.

Final release must **not** call `ClearRetainedOwner(root)` before `use.Dispose()`. The current successful lower release already clears that anchor. Elect `Releasing`, invoke lower release outside the result gate while the full root is still anchored, and clear the root's use/debt state only on success. On failure retain the anchored state, including what the lower transaction actually still owns.

Quarantine of a plan alone is not sufficient if a child already cleared a failing resource or returned R in `finally`. The child staged-cleanup and retained-receiver fixes remain prerequisites from the workspace proposal; this root protocol cannot repair missing children by holding a different object.

## 7. Public Task producer: exact CLR gate and proposed closure

### Evidence and supported-lane boundary

The checkout selects `net11.0`, `LangVersion=latest`, and `Features=strict`. Local SDK metadata identifies `11.0.100-rc.3.26522.148`, runtime `11.0.0-rc.3.26522.148`, monorepo revision `5254d8bcf59917edc1e1abc33bdf2442b77b1c70`. `global.json` permits roll-forward; this local observation is not a new developer pin or shipping-support certification. Chapter 29's RC2 prose is older than the actual file and is not substituted for it. NativeAOT and optional engine `runtime-async=on` require their separate qualification; neither is executed here.

The exact upstream revision's source establishes:

| Source under `System.Private.CoreLib` | Relevant fact |
|---|---|
| `AsyncTaskMethodBuilderT.cs:643-680` | Builder `SetResult` calls `Task.FromResult` or `task.TrySetResult` after the source method's return path. Its box additionally clears state/context after `MoveNext` returns (`510-539`, `575-579`). |
| `Task_T.cs:357-389` | Stores result, publishes `RanToCompletion`, then calls `FinishContinuations`, before returning to the producer. |
| `Task.cs:3602-3785` | Exchanges the continuation object and processes either a single registration or a list. `RunContinuationsAsynchronously` disables continuation inlining; it does not skip dispatch or bound the list. |
| `TaskContinuation.cs:741-842` | Raw Action dispatch can execute `new AwaitTaskContinuation` at completion. Scheduler-bound await dispatch can create a Task (`491-507`). Cardinality follows registrations, not Scene input/count/P. |
| `TaskCompletionSource_T.cs:216-224` | Public `TrySetResult` returns only after the underlying completion call. This is a useful explicit source boundary, not a promise that observers wait for that return. |
| `Task.cs:3522-3549`; `TaskExceptionHolder.cs:143-154` | Fault publication lazily creates contingent/holder/list/EDI bookkeeping. One original exception is not zero publication allocation. |
| `ExceptionDispatchInfo.cs:21-34`; CoreCLR `Exception.CoreCLR.cs:242-249` | EDI captures runtime dispatch state. This revision uses `GetFrozenStackTrace`; do not import an older runtime's deep-copy claim or pretend this source is a portable object-size bound. |
| `TaskCompletionSource_T.cs:324-358` | `TrySetFromTask` can forward an already completed **same-result-type** Task's existing EDI/cancellation state. It is not a join, does not suppress external fanout, and does not directly turn `Task<SceneExpansionPlan>` into `Task<PreparedScene>`. |

No experiment was run for these statements. The older proposal's scheduler/layout observations are supporting prior evidence only, not current qualification.

### Distinguishing witnesses

**Early-body end:** a caller-owned scheduler can hold continuation dispatch after the public Task has a result. Another thread can retrieve that result and Dispose it, or admit and execute activation, before `TrySetResult` returns. Both may end body/A, but neither may return producer capacity. The current activation fixture demonstrates a suitable caller-scheduler pattern; applying it to preparation still needs implementation and execution.

**No fixed all-dispatch constant:** for any proposed fixed terminal allowance K, a caller may register additional raw Action continuations before completion. The selected CLR can allocate a dispatch object per registration while completing the Task. There is no public continuation-count bound tied to P. A larger K, `ConfigureAwait(false)`, `RunContinuationsAsynchronously`, or waiting on that same Task does not prove a finite K for all such allocations.

### Smallest proposed disposition

Adopt these two narrowly scoped decisions together:

1. **Make external continuation-dispatch provenance explicit.** Extend section 9's existing caller-continuation ownership rule: registrations made by an external observer own their continuation object/list capacity, captured context, scheduler work, and runtime adapters/queue capacity created solely to dispatch those registrations, even when dispatch runs inside the producer's `TrySet*`. Their ownership follows that registration/dispatch, not the thread performing allocation. They borrow no Scene body/World authority. This is a proposed ownership clarification, not a claim that setting TCS options already establishes it.
2. **Expose the Scene producer's last access.** Replace only the ordinary loader's outer `PrepareCoreAsync`/validation-resource orchestration with a thin staged non-`async` driver rooted in `PreparedScene`. It starts the existing planner Task, processes completion, validates, then starts the existing resource-preparation Task if needed. Use the root's prepaid callbacks and pending-task slots; no `Task.Run`, `async void`, `ContinueWith`-created orphan Task, new worker, generic async framework, or public promise wrapper. Preserve ExecutionContext behavior and avoid caller synchronization-context capture; `UnsafeOnCompleted` is not a free replacement for the existing flow semantics.

The callback driver handles already-completed and suspended stages through the same single-winner transitions. Start owns its entry part. Store each returned child Task and reserve the corresponding completion part **before** registration or inline invocation; a callback can otherwise finish on another thread before the registering method returns. Each registering part clears its body/snapshot/plan locals before enabling the next callback, leaving only driver/publication metadata in that still-running part. Take returned resources into root slots before fallible processing, clear task/snapshot/port aliases before publishing Available, and retain every child producer provision until that child's actual end/transfer fact under the workspace proposal. Merely awaiting or observing a completed child never supplies that fact.

The sole final publisher is an ordinary synchronous method within one running driver part. Outside all gates it selects exactly one `TrySetResult(this)`, `TrySetException` using the single original failure or the section 6 two-original-failure array, or `TrySetCanceled(selectedToken)`. After that call **returns**, it performs no further TCS/Task/result access, clears its root publication references and calls `MarkPublicationAccessEnded`. Every ordinary driver invocation ends its own part after its last preparation access, including return from continuation registration. `EndPreparationProducer` is eligible only when publication-access-ended and count zero both hold, with supplier provisions also discharged. There is no compiler-produced completion of the public Task afterward. The entry returns its previously captured Task without rereading the root.

For example, resource completion may publish and return while plan completion is still returning from registration, and Start may also still be returning from its registration. Their three prepaid parts prevent premature release even if body cleanup already finished. This is a fixed loader handoff counter, not independently escaping child operations or a general async scheduler. Pre-escape freeze failure has no driver parts and closes the unescaped producer directly after synchronous unwinding.

This solves the *source-level public-publication boundary*. It does not by itself qualify J/K or discharge a supplier's still-running lowered frame. If the final registration/context/runtime epilogue still performs preparation-owned work, keep its provision/live-producer condition until a qualified outer return boundary; do not mark Ended inside an `async finally`, add another Task observer, or quarantine normal work to hide that gap.

The ownership clarification excludes **only externally induced** dispatch. Scene-created callbacks, child-task/await registration metadata, its own diagnostics/exceptions, Task bookkeeping, outcome holders and cleanup overlap remain funded by Scene J/K or a specifically identified existing child grant. Task wait-handle/observer metadata requested by an external observer follows that observer by the same provenance rule. This is not a blanket “framework allocations are free” exemption.

**Open, exact gate:** the final chosen runtime/compiler/target/code-generation lane must establish a finite catalogue and last-access disposition for all remaining **Scene-owned** J/K objects and late runtime work, including cold paths, fault/cancel, cleanup overlap, instrumentation modes actually supported, and supplier-frame boundaries. The reviewed source plus the old probes does not establish that numerical catalogue. Review the external-dispatch ownership rider before implementation, then derive/qualify the explicit publisher's remaining finite graph. If the rider is rejected, unrestricted `Task<PreparedScene>` observer fanout prevents the claimed all-dispatch constant; no local larger constant or new passive owner closes it. A bounded registration/runtime funding hook would then require a separate .NET/public-boundary design, not an invented Scene cap in this proposal.

The same provenance clarification is consistent with the **existing** section 9 activation tail; it does not shorten it, alter its captured cap, or reaccept its current constants. Even externally owned dispatch can hold the producer inside `TrySet*`, so the Scene-owned provision remains charged through that entire call.

## 8. Required regressions after separately authorized implementation

Use existing real package/codecs/World registrations and ordinary Runtime NUnit tests. These are proposed checks, **not runs in this task**.

| Owning test area | Distinguishing coverage |
|---|---|
| `SceneLoadOptionsTests` and public package-loading tests | Keep nonallocating instance-ID validation, copied/RFC-sorted/duplicate/null/default semantics and exact P. Exercise zero/one/multiple bindings; exact and one-byte-short reached freeze overlap with J/K protected; no root read when denied. |
| `SceneAssetStoreTests` preparation failures | Unknown root binding, schema mismatch, root/Prefab read or codec failure, admitted cancellation at real asynchronous stages, and quota refusal after promise escape. Assert original fault identity/cancellation channel, joined consumers, no World mutation/identity, exact option/entry refunds and no lost root. Use real safe failures; do not add a throwing allocator or fake preparer. |
| Preparation last-publication race | Adapt the existing caller-owned blocking scheduler to preparation. Require a real pending preparation before registration; an already-completed Task is not this observation. While dispatch is held, observe terminal Task and retrieve the result; Dispose in one case, activate/pump it in another. Observe body/A ended/transferred while the exact remaining preparation use/J/K stays charged; a competing operation cannot spend it. Release dispatch and observe one final release. Do not wait for `PrepareAsync` itself to return on a synchronous-publication thread being deliberately held. If no safe real asynchronous stimulus can establish the hold, leave that gate open; do not insert a test-only yield/producer or pass an unexercised branch. |
| Admission/disposal and repeated queue refusal | Preserve `SceneLoaderRejectsOwnerQueueFullWithoutConsumingPreparedScene`; retain three refused attempt Tasks/receipts and then activate the same result. Race header with Dispose and competing admission: one body winner, no pre-admission array borrow, no losing-attempt refund of preparation credits. Include wrong World/store and pre-cancel precedence. |
| Activation outcomes | Preserve boundary activation, optional explicit-null resolution, nonlive-target refusal and `SceneLoaderCancelsBeforeOwnerBoundaryWithoutConsumingIdentity`. After admitted cleanup, root role completion replaces preparation-use disposal; activation tail still spans its complete publication. |
| Default asset retention | Keep `PreparedAssetLeaseRetainsBundleAcrossActivationUnloadAndDispose` with default World/P, its current nonempty external binding, pixel 17, unload/dispose leases and activation-tail observation. Attribute any still-live preparation tail separately rather than assuming an awaited preparation has already refunded it; assert zero only after observing actual owner-role completion. Do not remove the activation-tail assertion, use a smaller fixture-specific cap/larger World quota, or remove extraction. |
| Closure/retirement | External domain close before/after admission; World revocation while preparing, Available, or held in delivery; same-GUID successor cannot be targeted. Existing claims release after closure without new admission. Independently leased bundle/unused result surviving World disposal remains gated on the workspace proposal's retained-R owner, not passed by the current World-alive fixture. |
| Release failure/rooting | At a real supported cleanup-failure stimulus, assert root → failed child/request plus option claim/release debt and actual remaining grants. Repeated Dispose does not retry/refund; independent safe siblings still clean. If no safe production stimulus exists, record this exact unqualified gate rather than adding production failure switches or claiming quota refusal is release failure. |
| Runtime/code generation | Derive J/K from the **final** graph, then cover cold/warm, success/fault/cancel, cleanup overlap, external zero/one/many Action/await/scheduler registrations and supported instrumentation. Separately qualify baseline Debug/Release, optional Runtime Async, and actual NativeAOT/public consumers where claimed. A layout probe or finite fanout sample is not a proof of a universal bound. |

No source-only or test-count assertion replaces these behavior observations. Retain final-configuration evidence and fresh nonzero selected/executed counts under the normal artifacts layout when implementation is authorized; no alternate qualification runner is needed.

## 9. Review disposition and remaining gates

| Item | Proposed disposition / remaining condition |
|---|---|
| Stable result root and joined preparation use | Adopt tentatively: one root, one use, producer/body join, admission-only delegation of snapshot backing. Implement only with the preclaimed final carrier inventory. |
| Already implemented option/activation work | Preserve. Do not restore the old preclaim defect, constructor snapshot borrow, zero-cap activation, or early activation-use release. |
| Public preparation producer | Use the explicit TCS and thin non-async staged publisher candidate. Require final callback/child-frame last-access evidence; no async-finally inference. |
| Finite protected terminal provision | Open: approve the precise external-dispatch ownership rider and qualify remaining Scene-owned J/K for the selected lane. No numerical constant is established here. |
| Lower release invariant | Root remains installed during failure. Qualify exact remaining-grant retention through the existing lower release sequence; no claim that its two lower release calls are already an atomic successful bundle. |
| Child cleanup, X→R and retained receivers | Unchanged prerequisites owned by the workspace proposal. This document cannot accept them or expand into their allocation work. |
| Maintained-contract promotion | Separate authorized change to Chapters 02/04/12/16/30, with Chapter 27 ownership distinctions preserved, after review of this proposal and the final footprint/last-access evidence. No promotion or milestone status edit occurs here. |

This task edits only this new proposal. It runs no build, test, allocation probe or native work, and makes no commit, push or PR.

## 10. Frozen inputs and verification

### Working-tree baseline

HEAD: `31f156a51ccc462ae9c5cc8d4e87e00c0054dec4`.

The initial raw UTF-8/NUL output of `git status --porcelain=v1 --untracked-files=all -z` has SHA-256 **`890C6C4A7D0D2272CFA6E7DEF36F591F18515B9DBDC16A9ED27BCAC4442688F6`**: 30 ` M` and 8 `??` entries, no staged entries. In the table, `--` means absent from that output; ` M` retains its leading index-status space. Paths are displayed with Windows separators; raw Git status uses `/`, `XY`, one space, and trailing NUL in Git's original order.

All digests are SHA-256 of raw file bytes, without newline/encoding normalization. Dirty unrelated files are included for preservation/status comparison, not reviewed as new planner/Rendering/M05 scope. No schema file is changed or used to introduce another format. The new proposal is the sole expected additional `??` path and is excluded only from the final **baseline-status** comparison; its final output hash is reported separately to avoid a self-hash.

#### Repository inputs, including the complete original dirty set

| Baseline status | Input | Raw SHA-256 |
|---|---|---|
| `--` | `.editorconfig` | `342683DE2B1C262624546F312F7D8E2078F89185ADD39F43F1FB5013376FD553` |
| `--` | `.github\copilot-instructions.md` | `12439DF4BB5E46021891B234BEC1104ABDCA57C11FDF3F610C8F32C5DAF3001E` |
| `--` | `.github\workflows\ci.yml` | `03EFEA672CD52E9EF053E9CE0285E7CA02C6CE6FB0D21268ADE4255ED5A39D58` |
| `--` | `Directory.Build.props` | `770EE4B2A422861A6F23F0D39DD0ECA361A81CB87E64EBAC4E12170F71231D7A` |
| `--` | `Directory.Build.targets` | `AEEC1D08E8037470239035812351C4045124607D67A354D482112F4D642D4105` |
| `--` | `Directory.Packages.props` | `A5066BF0F3B0EFD28552B484A01A39726C2CE47584B3FBC8C077790C4F4AD57A` |
| `--` | `TerraFX.slnx` | `515FF9C2040BC17461E806231D43EDCC48649441F4DA56313D72729FAC58B9A4` |
| `--` | `docs\design\02-dotnet-api-memory-and-ownership.md` | `40F913A50434209E09941C22625A6D0722202BAB97EBAC0E153C1EA3368E2291` |
| ` M` | `docs\design\04-capacity-and-workload-profiles.md` | `A4D86F3FA373A64548435C53582BF5916E29D77F34650F9D161CCEC90E6F0245` |
| ` M` | `docs\design\12-entities-components-and-queries.md` | `174B2465746DA79CEF560C8506216EF4F99F9D1AABAC184F872C9E6CB0B94E0B` |
| ` M` | `docs\design\16-scenes-transforms-and-cameras.md` | `B012F42B23CEDE9C7CDB71203D8357CD19E1626823EA25EF3119961292D3698A` |
| `--` | `docs\design\27-state-serialization-replay-and-recovery.md` | `B9B6324C5B1B1543CA1D2287ECE579597A7E7AA62D885031C4FF1BB629F08777` |
| `--` | `docs\design\29-project-setup-build-and-deployment.md` | `3B55A013EEC327ECD648ACBD5B53717DD218AD288E8047AC21277609EFA2F47B` |
| ` M` | `docs\design\30-public-api-review-surface.md` | `15848C82F3858A77A87A0D3EA6FBCC07046EB6E77CA5B21748C72B90EFAB598B` |
| `--` | `docs\design\35-implementation-roadmap.md` | `24EEC6E92B7AD904D552F290EF748FFC8FCE13B6B94A89EBD9F39487D4584D87` |
| `--` | `docs\project\Building.md` | `F53AB58B89EEAE7E17B70970075BE9322A936D443BB6D5239C3238E594DEABC6` |
| `--` | `docs\project\Roadmap.md` | `C4FBE82155A6D82FFD2D13AD2F18056D938738C4FA5A66760C4877E24ED001C7` |
| `??` | `docs\proposals\m03b-scene-preparation-budget-accounting.md` | `3F8E4F0315C6CC2933FC982F64D5E94B9070EB1876A74B0688118BB17AFF5A62` |
| `??` | `docs\proposals\m03b-scene-preparation-workspace-closure.md` | `9D7280A89C5BB75890ADAF65E26F8C80766B8687EE0608623D0F97D519BE32C3` |
| `??` | `docs\proposals\m05-owner-contract-closure-rectification-addendum.md` | `C8DE6581D5CB126D18C294FC9EB22A01459B2D6BFC3AD933ECB1652582634F4A` |
| `??` | `docs\proposals\m05-owner-contract-closure-rectification.md` | `67134F02B92D316524848ECB8E0C917EF868DA3D2EDA5FC581DBAA25B1825632` |
| `??` | `docs\proposals\m05-owner-contract-closure-runtime-ports.md` | `605CF1D92DC362A8B73EA9BDA3A5467757942C268712F484AF53D9A9159D53E3` |
| `??` | `docs\proposals\m05-owner-contract-closure.md` | `6FEE16009BF0D1FB529E68C8DF1E3293A852468E71D9A291984D8A64A9A62BC8` |
| `--` | `global.json` | `1BCE01871321C2DF82E887DA428CE5C121A6C6541FE65017FE3D249BE47E190C` |
| `--` | `sources\Directory.Build.props` | `D44373AF315523F8AF9F09F36BDED2A688E2B5C1572FCC0732310C7EF590EAD8` |
| `--` | `sources\Directory.Build.targets` | `4C0527A12E48D07401C69C06CF942F9145D6A8CCF98C4A7F052B11E463E1CE9A` |
| ` M` | `sources\TerraFX.Runtime\Animation\AnimationClipAssetBinding.cs` | `E09FCA988BA58127B365D8F4D7EB0C22CA3FD17E07E8342879544FB81BBD4DB1` |
| ` M` | `sources\TerraFX.Runtime\Entities\World.cs` | `AD884C9DF4F9B48DB4751518237535BC11A67820EA1F96FC66CAE850935A7B31` |
| ` M` | `sources\TerraFX.Runtime\Entities\WorldAdmissionDomain.cs` | `1682BCDF2B478EAE6F94DCA331FB7E05ED5B5357A81B36D06796E247D75886D6` |
| ` M` | `sources\TerraFX.Runtime\Entities\WorldDomainReservation.cs` | `EA012B9B40ACEDAA820885EAFE27DCD11DF55F842B68F2849785DAB756E8E3BC` |
| ` M` | `sources\TerraFX.Runtime\Entities\WorldGuidRegistry.cs` | `470EC745500181A57D2E0625B3A93EE5E51A899E046C017B3DD490FDB02BE739` |
| ` M` | `sources\TerraFX.Runtime\Entities\WorldGuidRetirementPermit.cs` | `1849EAE697239482076F7EC3A1EA02DD6B3348094DAD6CB63668BF57FDB20529` |
| `??` | `sources\TerraFX.Runtime\Entities\WorldGuidUse.cs` | `A6120452989F29EC2EF9C2B90E7479CC6635B9B17848C6C683063B3F99D36D22` |
| `??` | `sources\TerraFX.Runtime\Entities\WorldGuidUseControl.cs` | `1931CD2B5DFD81A3F0CD8A4E6F3D09C5D9B006B2E29817F5C295C77F78202B68` |
| ` M` | `sources\TerraFX.Runtime\Entities\WorldLifetimeControl.cs` | `B1A9ADD0A1D68CAF4C385973CA4E2602C68E19E46078AA3CB09614848C09A634` |
| ` M` | `sources\TerraFX.Runtime\Entities\WorldSceneStorageReservation.cs` | `A6A487ABAE56A5512976CEFB5D342C37B414BB4F8615B1A124692C9696C673E5` |
| ` M` | `sources\TerraFX.Runtime\Rendering\RenderSceneAssetBundle.cs` | `4FD3F20576AD328BD3E9958E40EF8D475451BDCBFC5122C3BA7E599A0EA8361A` |
| ` M` | `sources\TerraFX.Runtime\Rendering\Renderer.CpuExtractionSupport.cs` | `EAF1F94AFF0886B3FA30AF97BD7F83180A38AB744D4241D0CDD275A055EADB55` |
| ` M` | `sources\TerraFX.Runtime\Scenes\ISceneExpansionResourcePreparer.cs` | `67AE936843F6329D95D12B784A4686A8B75B2CCEB3932159E56BDD24F21CA423` |
| `--` | `sources\TerraFX.Runtime\Scenes\PreparedAssetsLifetime.cs` | `D90ADF47F3DE2F5B71E69B4E7B5798D7DD4BD505D19FB89B22BDF8F46ECB60D9` |
| ` M` | `sources\TerraFX.Runtime\Scenes\PreparedScene.cs` | `E4837CEB4F9E8A62A3ABB6A50F6668207AB431D7FDA5896E92BA0FD22A5BFB29` |
| `--` | `sources\TerraFX.Runtime\Scenes\SceneActivationExecutor.cs` | `7D12EB728B0563ED673A86A1E1846AAF0D3FC37A07C139CADECF8F34C646EC69` |
| ` M` | `sources\TerraFX.Runtime\Scenes\SceneActivationRequest.cs` | `7EA293C13FB20992E04B3BC4FB7484A2708337ABDE759F86CDA609DAE030C16D` |
| `--` | `sources\TerraFX.Runtime\Scenes\SceneComponentRestoreContext.cs` | `0BE40B9D7A86A1C8112F85217C86A37474E785CB11E0BD332B5E3F5D9B5FCF82` |
| `--` | `sources\TerraFX.Runtime\Scenes\SceneExpansionLimits.cs` | `941B5A09B7CD60EA5704030ACE2AF2F3499EBE2843E14138182FAF609ACC4173` |
| ` M` | `sources\TerraFX.Runtime\Scenes\SceneExpansionPlan.cs` | `8FFCDE62282341B8C5E782D5262C0917002D1B48E35A5098CC9B4B360BEEFAE9` |
| `--` | `sources\TerraFX.Runtime\Scenes\SceneExternalBinding.cs` | `521DAE0BA263F4161E2F5F2DFDBB75A4B1CFCBFA63EB2E7403F4777580F9801D` |
| ` M` | `sources\TerraFX.Runtime\Scenes\SceneLoadOptions.cs` | `E95382B5F26B7DD398C34BA0B4F20A6A0E76BF9F392B5B3B4BAD88AD1F898A80` |
| ` M` | `sources\TerraFX.Runtime\Scenes\SceneLoadOptionsSnapshot.cs` | `004B203DC84037B76F854DDE1B47C78D570A8085B6A9B48EE6423E27496223E9` |
| ` M` | `sources\TerraFX.Runtime\Scenes\SceneLoader.cs` | `2E3F6C347AA01B5914980064CDBF7A2A0414DCD74C31A5223365F4DD4D15FEBC` |
| ` M` | `sources\TerraFX.Runtime\Scenes\ScenePrefabExpansionPlanner.cs` | `D8D807E1925A5CF2538A41076CA3606A7FE3F31005B452429F20C99C5612CA27` |
| `--` | `sources\TerraFX.Runtime\Scenes\SceneWorldRegistry.cs` | `667E8BF4D3091BE93A206A152C9AECBB071E5E9C42D1ECC49F17F1A37C46AEFF` |
| `--` | `sources\TerraFX.Runtime\TerraFX.Runtime.csproj` | `E019D4DBAD14FF2D5FF8D7365DDED0D09210575D5228A0D01CED70C99B3C2D33` |
| `--` | `tests\Directory.Build.props` | `281AAD4237121FEAB1903C2384F1A5F0AED7E218A704C918FE953E9FC8E75764` |
| `--` | `tests\Directory.Build.targets` | `4C5822FAA56E345DF0640BE0C19D627A21D6A63965C539AB3E0D13FB14D0FDCA` |
| ` M` | `tests\TerraFX.Runtime.Tests\Entities\WorldAdmissionDomainTests.cs` | `F01C956A1DF54BBB048BD40BE073A0190E843255EB71C0AF618FD3A345793429` |
| ` M` | `tests\TerraFX.Runtime.Tests\Entities\WorldLifetimeControlQuarantineTests.cs` | `B4E0683405ADAB476B5B4CF9108E8C7B1E30E33B6CD48EC55A28AECA57EDFFBB` |
| ` M` | `tests\TerraFX.Runtime.Tests\Scenes\PreparedSceneAssetRetentionTests.cs` | `E342237B7645BE805DD924855B2239907773D31753D22B8B2261BF26C20452F4` |
| ` M` | `tests\TerraFX.Runtime.Tests\Scenes\RenderSpriteCapacityTests.cs` | `CAA6610CF55F8FFFA077745FD96BAA45FCECFED2FD051CC763901AEED5500239` |
| ` M` | `tests\TerraFX.Runtime.Tests\Scenes\RenderSpriteComponentTests.cs` | `8E65B71B5AC1E0EF9D1F47AA56963054D091DF8C229E74DDBB1CAE37BB59AB60` |
| ` M` | `tests\TerraFX.Runtime.Tests\Scenes\RendererExtractionTests.cs` | `ABAEEA4D0F6BE941AF73E640B7FCE41EC753D2199654343FA4668D54EC20FC6A` |
| `--` | `tests\TerraFX.Runtime.Tests\Scenes\SceneAssetStoreTests.cs` | `1784651E891E29D88E84508DC7C78AD105F42F85ACF1AE125772F170FD22A587` |
| ` M` | `tests\TerraFX.Runtime.Tests\Scenes\SceneLoadOptionsTests.cs` | `D077505A70E96EA9FB530D1D4B5A030CBF2C6BF599A867DC79256A5A99B10DE8` |
| ` M` | `tests\TerraFX.Runtime.Tests\Scenes\SceneSupportRegistrationTests.cs` | `D474CBB730BA7ECA38F5B42886B43E417962241AA88DAD1202BF41DC9CF21E76` |
| `--` | `tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj` | `ECBFD99E4DD1EC2A32472D3187B6F2911D7297D1F74A52F2AFA600EB6AE5F06E` |

#### Local lane identity inputs

These ignored local files identify inspected metadata/binaries only; they are not build outputs qualified by this task.

| Input | Raw SHA-256 |
|---|---|
| `.dotnet\sdk\11.0.100-rc.3.26522.148\.version` | `0C5E90CAD5281608AFE604B3C0B944F7A180267EAC2CB25FE2C3F1A723488647` |
| `.dotnet\sdk\11.0.100-rc.3.26522.148\Roslyn\bincore\Microsoft.CodeAnalysis.CSharp.dll` | `25ED04EABEDF0B2ACF994D4606F4FD40328379106156C028AEB7D4A75154F442` |
| `.dotnet\shared\Microsoft.NETCore.App\11.0.0-rc.3.26522.148\.version` | `E7E8C5CF7C39859338D6A80725750B3E4B2BF214C342C660CD25FB0B74C96B4E` |
| `.dotnet\shared\Microsoft.NETCore.App\11.0.0-rc.3.26522.148\System.Private.CoreLib.dll` | `DF53885902E896A2B40988237CBE9CBB9F1BB0743200F05FE9257C56A29C5732` |

#### Exact .NET source inputs

Base URL: `https://raw.githubusercontent.com/dotnet/dotnet/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/`. The paths below are URL-relative (forward slashes are URI syntax, not local paths). Digests cover the complete downloaded raw bytes; quoted evidence uses only the bounded sections identified in section 7.

| Revision-relative source | Raw SHA-256 |
|---|---|
| `src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/AsyncTaskMethodBuilderT.cs` | `3E780AAE5A90604D6B15585AD64857D3BE51D8EE26DE19AAC11C91D60C7DB2B5` |
| `src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/Task.cs` | `342D6D28C6253316031DF91DBD1D796BCCD7A724263B1DEB048AFA1675498A99` |
| `src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/Task_T.cs` | `1F87175982C6AF5ADF96A1C245E42C519FB4D59B8FDEDD9C6350A413703A3EBA` |
| `src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/TaskCompletionSource_T.cs` | `069571DADDEFFD14ED006B8E552B3541E61B7C79BA7E6A142037E59EADAE1369` |
| `src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/TaskContinuation.cs` | `0142D7BAB411799A69216584732E7F9B723807AA51AD30A7C9057FB00FFE3465` |
| `src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/TaskExceptionHolder.cs` | `BC4DDA204DE133879E4E334B37F74FC6C57690C05A179AE4648FBFA653A38604` |
| `src/libraries/System.Private.CoreLib/src/System/Runtime/ExceptionServices/ExceptionDispatchInfo.cs` | `BF9021A960B704107A7E141AA06BC9D8A0A53C929178C2FB095B1597BE8AF8DC` |
| `src/coreclr/System.Private.CoreLib/src/System/Exception.CoreCLR.cs` | `44AC1884BCFB3857679BD0865CD9F2EB49192E0196F14C837F2AA1D043F985B7` |

### Final verification record

Raw recheck at `2026-11-16T02:55:14Z`: all **69 listed local input files and 8 exact upstream source files matched**. The wider initial discovery envelope (159 files) and five additional preservation-only discovery hashes also matched; these sets overlap and are not additive reviewed-file counts. HEAD remained unchanged.

**Concurrent dirty-state mismatch, not accepted into the baseline:** an unrelated new `?? docs\proposals\m05-owner-contract-closure-runtime-ports-addendum.md` appeared during this task. Excluding only this proposal's own new path, the observed raw status SHA-256 became `B6CE8BB88D285311C2FE4241A0A70DC839951B8933194B7BBC757AF5A857A14D`, not the original `890C6C4A7D0D2272CFA6E7DEF36F591F18515B9DBDC16A9ED27BCAC4442688F6`.

The concurrent file was hashed without reviewing its contents at `2026-11-16T02:58:21Z`: 33,038 bytes, SHA-256 `BD110DF7B0C3C48513A9B451BFA7B2FD20AD089DB3FA0B0C1A45C0A7613E0B5C`. This is a separate concurrent-change observation, **not** a refreshed review input or acceptance of that proposal. The original hashes/status baseline above remain intact. File-byte equality of the frozen inputs passed; full-worktree dirty-state equality did not. The final output hash is reported separately after rechecking these same fixed expectations.

The recheck at `2026-11-16T03:01:16Z` again matched all frozen file bytes, all eight upstream sources, HEAD, and that concurrent file's separately observed hash. Dirty status changed a second time: `?? docs\proposals\m05-skinned-normal-diagnostic-delivery.md` also appeared. Status excluding only this proposal then hashed to `9F5C530111EB6774318C248FE81A9F23E97FC59A8B17A1ABE682C42CE38FDA6C`. Neither concurrent proposal is reviewed or added to the original input baseline. These observed additions prevent a claim of full-worktree equality; they do not silently replace any original hash or qualify unrelated work.

At `2026-11-16T03:02:17Z`, all original reviewed/discovery input hashes, upstream sources and HEAD still matched, and status retained those two additions. The **unreviewed concurrent addendum's bytes changed** from the separately recorded `BD110DF7B0C3C48513A9B451BFA7B2FD20AD089DB3FA0B0C1A45C0A7613E0B5C` to `10B41119875D92E292459F3BAA6B50D0F1F3DF6C0CD66C9E1F8FECD3734A0BEF`. Both observations are preserved here; the latter is not substituted into the frozen input set. Concurrent unreviewed files are not treated as stable review inputs or waited on for acceptance.
