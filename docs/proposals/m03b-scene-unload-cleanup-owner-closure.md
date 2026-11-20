# M03-B Scene unload and registry cleanup ownership

**Status: tentative proposal, not an accepted owner contract, implementation, or qualification result.**

Keep a structurally retired Scene in its existing registry slot as an **inactive cleanup owner** until its cleanup obligations have returned. A failed cleanup becomes terminal, charged quarantine in that slot; it does not reactivate the Scene, retry subordinate disposal, or disappear behind `AlreadyUnloaded`. Registry shutdown attempts every safe sibling cleanup and remembers its first failure.

This proposal selects that behavior for the cleanup-owner gap at `2828fd41d2b602a0222940e1b4f056cf104efeca`. It does not reopen Scene preparation, change a public signature or enum, assign a schema/budget/acceptance ID, raise a ceiling, or claim native release evidence.

## 1. Evidence and authority

The production sources and owner chapters inspected were clean at the prerequisite revision. No build, test, native probe, or allocation measurement was run for this proposal. Existing test assertions below describe source coverage, not newly observed results.

| Inspected owner | Current evidence and consequence |
|---|---|
| [`SceneUnloadRequest.Execute`](../../sources/TerraFX.Runtime/Scenes/SceneUnloadRequest.cs#L118-L145) | Calls World destruction, removes registry ownership, revokes the instance, and only then commits the recorded control. A cleanup exception faults the Task after structure has changed. The cancellation catch does not distinguish a postcommit cleanup `OperationCanceledException` from precommit cancellation. |
| [`SceneInstance`](../../sources/TerraFX.Runtime/Scenes/SceneInstance.cs#L412-L417) | Revocation first clears activity and external bindings, then exchanges `_plan` to null before disposal. `EntityCount` dereferences `plan.Entities`; that getter throws after plan disposal begins. It cannot be used to maintain indexes during repeated failed cleanup. |
| [`SceneWorldRegistry`](../../sources/TerraFX.Runtime/Scenes/SceneWorldRegistry.cs#L125-L165) | Shutdown has a one-shot latch, dequeues requests before canceling them, and clears each instance slot before revocation. An exception stops the loop. Active counts, active lookup, ordered Entity lookup, and persistence all currently interpret the same slot array as active membership. |
| [`SceneExpansionPlan.Dispose`](../../sources/TerraFX.Runtime/Scenes/SceneExpansionPlan.cs#L286-L399) | Elects one first invocation, attempts independent prepared-resource/Prefab/Scene lease cleanup, releases storage only if earlier cleanup succeeded, retains the first EDI, and never retries a failed subordinate graph. A concurrent `Running` invocation returns normally; that return is **not** completion evidence. |
| [`World.DestroySceneEntitiesAtomically`](../../sources/TerraFX.Runtime/Entities/World.OwnerCommands.cs#L265-L480), [`CommitCommandBatch`](../../sources/TerraFX.Runtime/Entities/World.Commands.cs#L1948-L2040) | Scene identity retirement participates in the structural batch. The batch installs identities/rows and advances canonical revision before attachment Release and preparation cleanup. It can therefore throw after structural commit, before `DestroySceneEntitiesAtomically` returns. A latch set only after that return is insufficient. |
| [`WorldSceneControlAppend` / `PreparedControlAppend`](../../sources/TerraFX.Runtime/Entities/World.Controls.cs#L573-L781) | A Scene control is prepaid, but `Commit` combines record installation with fallible credit return. `_committed` is already true when `ReleaseAdditional` runs. Moving this call earlier without separating its phases does not establish an infallible postcommit tail. |
| [`World.Dispose`](../../sources/TerraFX.Runtime/Entities/World.cs#L1453-L1681) | Scene participant disposal precedes subscriptions, attachments, systems, diagnostics, and lifetime finalization. An escaping Scene exception prevents those later safe attempts. `_sceneParticipant` happens to remain set on that exception, and the final `CompleteWorldClose(false)` retains the failed World interest; this is useful existing retention, not a completed shutdown protocol. |
| [`WorldSceneStorageReservation`](../../sources/TerraFX.Runtime/Entities/WorldSceneStorageReservation.cs), [`WorldAdmissionDomain.SceneStorage`](../../sources/TerraFX.Runtime/Entities/WorldAdmissionDomain.SceneStorage.cs#L279-L376) | A receiver has a 72-byte control charge and an explicit retained-owner link. Successful return unlinks it, clears its owner, and refunds its bytes before the final World-return-part handback. A return exception must not be assumed to mean that R is still live: inspect the actual returned state. |
| [Planner transfer](../../sources/TerraFX.Runtime/Scenes/ScenePrefabExpansionPlanner.cs#L1031-L1076), [domain enrollment and retirement](../../sources/TerraFX.Runtime/Entities/WorldAdmissionDomain.SceneStorage.cs#L175-L204) | The planner installs the actual plan as the receiver's retained owner. The World interest holds `WorldCloseCarrier` and `SceneStorageHead`; final retirement additionally requires successful close, zero receiver/construction bytes, and ended return parts/uses. Thus a funded plan already has a lower retention route. The defect is not proof that every failed plan is currently collectible; registry cleanup ownership and terminal/public behavior are nevertheless lost. |
| [`PreparedScene.Dispose`](../../sources/TerraFX.Runtime/Scenes/PreparedScene.cs#L221-L271), [`SceneActivationRequest`](../../sources/TerraFX.Runtime/Scenes/SceneActivationRequest.cs#L292-L373) | These paths preserve their plan/root on failed physical cleanup and quarantine the admitted use instead of treating failure as successful return. Reuse this ownership principle, not their operation-use aliases or a new preparation owner for an active Scene. |
| [`PreparedAssetsLifetime`](../../sources/TerraFX.Runtime/Scenes/PreparedAssetsLifetime.cs), [system quarantine](../../sources/TerraFX.Runtime/Entities/WorldSystemInstanceRegistry.cs#L33-L135) | Failed last release preserves the value without retry. Independently retained resource leases have their own owners. World lifetime quarantine already has one pre-reserved lifetime-control slot per World; no new global Scene failure list is needed. |
| [`ScenePersistenceRestorer.RestorePrepared`](../../sources/TerraFX.Runtime/Scenes/ScenePersistenceRestorer.cs#L307-L375) | Also calls `RevokeFromWorld` for a candidate instance that has not successfully entered the active registry. The revised cleanup entry must cover this ownership handoff, not assume every instance already has an active slot. |

Applicable contracts:

- [Chapter 16, section 2](../design/16-scenes-transforms-and-cameras.md#2-prefabs-and-streaming): one exact registry, explicit structural unload plus own lease release, retained occurrences, unchanged requested W, and ownership-preserving `D_u` to R transfer.
- [Chapter 27, sections 1 and M03 8.3–8.4](../design/27-state-serialization-replay-and-recovery.md): the Scene projection equals the live scene-instance/history subset; historical controls are not active assets. Save requires an idle, nonfaulted, quiescent World with a matching committed/restored marker. Old operational cleanup remains outside replacement canonical state.
- [Chapter 30, section 5](../design/30-public-api-review-surface.md#5-scenes-cameras-and-animation): unload terminality includes the instance's own lease retirement; `Rejected` is not a postcommit status; independent render/audio leases remain independent. The existing system-cleanup contract retains its exception/report precedence and completed-World repeated-disposal behavior.
- [Chapter 35](../design/35-implementation-roadmap.md), and [current roadmap](../project/Roadmap.md): ownership behavior requires owner review; managed helpers, public consumers, allocation qualification, native/AOT observations, and milestone acceptance are distinct. `PreparedScene.QualifiedFootprint == null` remains a fail-closed producer gate.

## 2. Selected lifecycle and bounded owner

### 2.1 Membership is not cleanup ownership

Reuse the existing fixed-capacity `_activeInstances` backing array as `_ownedInstances`. Do not allocate another table, a linked failure queue, a per-unload owner object, or a process registry.

Replace `SceneInstance._isActive` with one private monotonic lifecycle value:

| State | Active membership | Existing owned slot | Permitted next transition |
|---|---|---|---|
| Constructed / Activating | None | Candidate reservation owns the unpublished candidate | Active, or unpublished cleanup |
| Active | Present in active counts and the ordered Entity index | Occupied | InactivePending after structural retirement, or registry shutdown |
| InactivePending | Absent | Occupied; same instance owns plan and any admitted unload-control tail | Cleaning |
| Cleaning | Absent | Occupied throughout the physical invocation and its returned tail | CleanupComplete or CleanupFailed |
| CleanupFailed | Absent | Occupied quarantine; first failure and every unreleased obligation remain rooted | No automatic retry or reactivation |
| CleanupComplete | Absent | Clear only after confirmed returns and ended invocation aliases | Terminal; public instance may remain caller-held |

Activation remains one-shot. An inactive retained plan must not make activation legal again. Cache the original Entity count at construction, alongside the existing occurrence count and first identity ordinal. Index removal never reads a disposed plan.

Under `_instanceGate`, logical deactivation removes the active ordered entry and decrements active instance/occurrence counts once, but leaves the owned slot occupied. This transition precedes physical cleanup and any other fallible postcommit work. Clear external bindings only after their active readers have ended; they do not authorize continued Scene membership.

Maintain separate cleanup-pending instance and occurrence counters. Admission uses:

```text
activeInstances + cleanupPendingInstances + reservedInstances <= N
activeOccurrences + cleanupPendingOccurrences + reservedOccurrences <= O
```

Here N and O are the existing `MaximumActiveSceneInstances` and `MaximumSceneOccurrences`, unchanged. Pending occupancy is deliberately not reusable capacity. It reserves the original instance/occurrence allowance until the whole owner has returned; terminal failure keeps that allowance even if some physical releases succeeded. Empty scenes still consume one root occurrence and one owned slot.

All active lookup and capture paths must filter by lifecycle:

- `ContainsInstance`, `TryGetInstance`, and the first-Entity-ordinal index mean **active**, not merely retained.
- `CopyPersistenceInstances`, `GetPersistenceInstanceCount`, Scene AssetId count/copy, and all instance persistence accessors observe exactly the active subset.
- The owned-slot walk is used only for cleanup/admission ownership. It never supplies canonical Scene rows.

### 2.2 Retention graph

For a failed subordinate cleanup with a live receiver, retain both existing routes:

```text
World admission interest -> WorldCloseCarrier -> World -> Scene participant
                                                     -> owned slot -> SceneInstance
                                                                   -> plan
                                                                   -> unload-control tail, if any
                                                                   -> admitted unload request until returned

World admission interest -> SceneStorageHead -> original receiver -> plan
plan -> original first EDI + unreleased prepared-resource/lease/reservation graph
```

The registry slot is the real Scene instance, not a tombstone standing in for a lost owner. Its failure field aliases the original EDI when one already exists in the plan or lower operation. Do not make an exception the sole owner of leases or reconstruct a reservation from a byte count.

Only confirmed releases refund bytes. Do not infer release from cancellation, Task completion, a normal concurrent `Dispose` return, a cleared wrapper, or GC. Partial successful cleanup does not refund the whole Scene R.

There is one important last-stage distinction. Receiver return can throw **after** its storage unlink/refund. At that point:

1. Do not report a live R from `RequiredByteReservation`; consult the receiver's actual state.
2. Before attempting that final return, end all plan-payload consumers and detach the now-unused managed payload containers, but only after all prior plan-owned subordinate releases have succeeded. Earlier subordinate failure preserves the complete graph and R.
3. If return failed before confirmation, retain the original receiver/plan and its charge. If storage is confirmed returned and no subordinate owner remains, retain the original failure in the instance/World return owner, not an uncharged large plan payload.
4. The original World-return interest remains failed/retained where its handback failed. Do not manufacture a replacement R or retry a destructive return.

This last-stage container detachment changes no usable plan API: disposal has already closed every payload getter. It adds no fields or allocations and preserves the first-EDI/no-subordinate-retry behavior of `2828fd41`. The receiver's bookkeeping state is not proof that some unrelated native consumer closed.

### 2.3 Unpublished candidates

`ScenePersistenceRestorer` must not transfer the only plan owner to a candidate before the registry owns that candidate. Its already reserved slot adopts the concrete candidate immediately after construction and before the caller marks the plan transferred or invokes the fallible activation/commit tail. Until adoption succeeds, the existing prepared/receiver owner retains the plan.

Commit validates the exact adopted candidate in that reserved slot, rather than requiring an empty slot. Success converts reserved counts to active counts. Failure converts reserved counts to cleanup-pending counts and clears only the reservation flag, not the candidate slot, in one gate transaction. The reservation token then has no separate right to free that adopted slot.

Unpublished cleanup has no live registry/persistence membership and performs no World entity rollback. It follows the same once-only physical cleanup rules. Failed cleanup keeps the candidate in its original slot/receiver graph. The reservation's disposal releases only ownership that was not transferred; it cannot erase an adopted failed candidate.

This is a private handoff correction to an existing `RevokeFromWorld` caller, not a new restore intent, source schema, or candidate producer.

## 3. Unload commit and failure ordering

### 3.1 Establish the structural fact inside Entities

Introduce a small, explicitly initialized **value** commit-facts output for the Scene destruction path. In field order it contains committed revision (`long`), lower retained EDI reference, failure kind (`int`-backed None / CleanupTail / CanonicalOrOwnershipInvariant), installation-started flag, and committed flag. Initially revision is zero, EDI is null, kind is None, and both flags are false. Set installation-started before the first structural store; set committed at canonical revision installation, before work-plan rebuilding, attachment Release, or preparation handback can throw. Classify failures by the phase/owner facts, not by exception type.

Keep ordinary command callers' behavior unchanged. The Scene caller receives the facts even when the lower call throws. Forward an already retained lower EDI instead of repeatedly capturing/aggregating the same exception.

Neither method return nor a later GUID search is the commit oracle. Expected refusal/cancellation before installation starts leaves active membership and its plan intact. Once the commit fact is true, the Scene identity retirement and installed World structure remain committed. No rollback, entity recreation, issuance refund, or second destruction is proposed.

An unexpected invariant failure after installation starts but before the complete commit is different from preflight refusal. Use the existing World fault path, deny active Scene access, and retain its concrete owner for safe World closure; report no successful atomic unload, rejection, cancellation, or rollback. Do not dispose a still-needed plan merely because its full-commit flag is false. Repair of a corrupted structural batch is not claimed by this proposal.

### 3.2 One postcommit sequence

Before destruction, reserve and write the existing unload control, validate its complete kind/extent/sequence/capacity arithmetic, and prepare its installation facts. Preserve the current mode, binding, hierarchy, cancellation, count, byte, and identity guards.

After structural commit, including a lower exception carrying committed facts:

1. Atomically move the instance Active to InactivePending in its owned slot. Transfer the actual `WorldSceneControlAppend` into that instance before relinquishing the request's release right.
2. Install exactly one already validated unload control without allocation, callbacks, or credit return. Do not append another control for cleanup observation or repeated unload.
3. Attempt the instance's physical plan cleanup once, outside registry gates. Also attempt the independent control-preparation return tail, even if another cleanup failed.
4. Retain every failed owner in the slot. Clear each successful obligation only after its actual return, and end local aliases before declaring the instance complete.
5. Select `Unloaded` only if structural installation, recorded control, and own plan cleanup succeeded. Otherwise fault the Task with the selected original failure; no `Rejected`, `AlreadyUnloaded`, or canceled success-shaped substitute. Retain the actual admitted unload request in the instance until its completion call and `Execute` invocation return. Registry finalization after that return, not Task visibility, clears successful pending occupancy.

Split the Scene-control wrapper's existing install and return phases. Keep the existing `PreparedControlAppend` as the owner of the prepared arrays; retain the outer concrete wrapper for any unpaid return tail. The wrapper records the exact return debt, terminal return state, and original return failure. Do not let its automatic `using` cleanup release an obligation already transferred to the instance.

The recorded debt is the issued amount plus its confirmation state, not an assertion that a throwing return refunded nothing. Detach unused control scratch before returning its credit, and distinguish confirmed refund from failed final handback through the lower return facts. An uncertain attempted return is retained/quarantined and never blindly repeated. The installed payload remains World-owned.

If an unexpected invariant prevents canonical control installation despite prevalidation, or credit bookkeeping/ownership validation fails, keep the inactive owner rooted and use the existing World fault path to prevent Step/save publication. Only a physical cleanup failure with coherent canonical state and retained credits is the healthy cleanup-only case. Do not classify these cases by the exception's CLR type or present an incomplete canonical record as a healthy cleanup-only failure.

### 3.3 Primary failure and cancellation

The per-unload primary is the first failure in this fixed phase order:

1. lower structural operation/postcommit tail;
2. recorded-control installation;
3. plan cleanup, with the plan's existing subordinate order;
4. control-preparation return;
5. terminal delivery/owner-return failure.

All safe later cleanup still runs. Keep additional failures with their actual plan/control/request owners, rather than allocating an arbitrary `AggregateException` list. A pure plan failure reuses its existing first EDI. A lower E0 plus plan E1 faults the unload with E0 and preserves E1 in the retained plan; both owners remain reachable.

Cancellation wins only at the existing preapplication/precommit checks. After structural commit, token changes do not undo anything. A cleanup `OperationCanceledException` is a fault, even if the original token has since been canceled. The exception catch must test the commit fact before selecting cancellation.

Task completion is outside gates, uses the existing asynchronously continued promise, and does not release owner debt before the completion call and required producer accesses return.

Promise visibility can precede `TrySet*` return. A delivery failure before terminal publication faults the original Task. A failure after publication cannot rewrite that Task's already visible result, just as cleanup cannot undo structural commit: retain the actual request and original failure in the slot, report it through the owning boundary/retirement channel, and make later cleanup observations fault. Do not retry publication or call the retained instance fully returned. This does not certify the currently unqualified producer/publication-return profile.

Concretely, the registry catches such escaped delivery failures, attempts later safe FIFO requests, and only then throws the retained primary through the existing `World.PumpControlBoundary` exception channel. Normal request faults still complete their own Tasks without throwing out of the pump. World retirement also observes the retained failed owner. No new public notification or receipt channel is needed.

## 4. Registry shutdown, concurrency, and World disposal

### 4.1 Registry first invocation

Repurpose `_isDisposed` as Open / Draining / Complete / Failed, not a one-shot success latch.

- Close admission under `_requestGate` before selecting the drain. Disposal-first rejects enqueue through the existing disposed channel; enqueue-first remains an admitted request canceled by this shutdown.
- Logically deactivate all remaining active instances before the first physical cleanup callback, including queued-request cancellation. Update counts/indexes once under `_instanceGate`.
- Walk the finite pending-request selection in original FIFO order. Keep each slot until `CancelFromRegistryDisposal` returns. A throwing cancellation retains that request in the already allocated backing slot; advance the finite selection so it cannot prevent later cancellations. Closed-registry failed slots are not pumped or reused.
- A normal cancellation return is not sufficient if the request retained failed cleanup in its preparation/activation owner. Use a private request outcome query over those existing owners to identify that failure; retain the request slot and its original failure rather than infer success from its Task. This query adds no public result, owner, or request field.
- Walk owned instances in ascending physical slot order, including instances already inactive after failed unload. A failed plan is observed/rethrown, never physically disposed again. Attempt every independent unattempted sibling.
- Retain the first shutdown EDI in FIFO-request order followed by instance-slot order, and count additional failures in a checked scalar bounded by Q + N. Each failed request/instance retains its own actual graph.
- Publish Complete only after all selected invocations and return tails have ended successfully. Otherwise publish Failed after all safe attempts, then rethrow the first failure outside gates.

No snapshot list of instances, error list, new Lock, completion Task, or dynamically growing quarantine storage is required.

### 4.2 Concurrent and inline callers

There is one elected physical cleanup invoker. Record its managed thread ID in the registry while it is in cleanup.

- Repeated disposal after Complete is a no-op.
- Repeated disposal after Failed rethrows the stored original failure and performs no subordinate cleanup.
- A concurrent disposal observer of Draining joins its terminal state outside both gates. It cannot return success while the elected invocation is still running. Use a nonallocating state wait; no new per-call waiter or promise.
- A same-thread recursive cleanup/disposal call cannot join itself. Reject it synchronously before the fast path or any second callback. A caught rejection grants no mutation or cleanup authority.
- Actual physical invocation continues to obey the World/child owner's legal-thread rules. A concurrent observer is not a new legal native-close actor.

Expose only private lower-owned scalar ports on `IWorldSceneParticipant` for cleanup-invocation activity, retained-cleanup presence, and its retained failure. `World.Dispose`, structural entry, and persistence capture check active Scene cleanup before permitting recursive disposal/mutation/capture. The existing attachment/system sticky guards retain their precedence.

After activation, the instance owns the sole plan-disposal right. Add a read-only internal plan terminal/failure query, with no storage fields. An observer of an instance already in Cleaning joins the elected registry invocation; it does not call `plan.Dispose` again. If that sole elected invoker instead encounters an unexpectedly Running plan or its nonterminal fast return, a competing unauthorized disposal actor exists: retain ownership and report protocol failure, rather than wait on a potentially same-thread actor or erase the slot. The registry thread guard rejects legitimate recursive entry before this point.

### 4.3 World shutdown

Catch the Scene participant's retained cleanup failure at the World disposal boundary, keep the participant reference, and continue the existing safe subscription/attachment/system cleanup and diagnostic/lifetime handbacks. Scene failure must not skip healthy siblings in those already prepared cleanup sets.

Include Scene failure/retained cleanup in `cleanupRequiresQuarantine` and `closeSucceeded`. Use the existing exact World lifetime quarantine permit and pre-reserved lifetime-control capacity; preserve `WorldCloseCarrier`, the original parent interest, and receiver links. Clear `_sceneParticipant` only after successful Scene retirement. Do not refund the registry's standing charge while it remains the cleanup owner.

Preserve existing attachment/system report and exception rules. If an attachment/system channel already determines the thrown outcome, do not replace that accepted exception; Scene failure stays in its retained participant. Otherwise rethrow the Scene EDI after safe sibling cleanup. `LastSystemCleanupReport` is still a system report, not a disguised Scene report.

The existing public completed-World repeated `Dispose` remains a no-op retaining reports and quarantine. Unlike the faulty current registry latch, the first World invocation has now completed every safe selected attempt. That no-op is not proof of successful domain retirement and clears no failed Scene owner. `OldWorldRetirement` faults rather than turning an already published replacement into `Rejected` or undoing it.

## 5. Public behavior and persistence

| Case | Selected result |
|---|---|
| Active Scene, precommit expected refusal | Existing `Rejected` receipt; active membership and plan remain. No destruction/control/assignment refund fiction. |
| Active Scene, successful unload | `Unloaded`, committed structural revision, empty diagnostics, no retained own cleanup obligation. |
| Structurally retired Scene with failed cleanup | Original unload Task faults. Lookups/provenance return false, prepared-asset access is unavailable, and `BeginTransaction` rejects. The instance is inactive, not absent from ownership. |
| Failure after unload promise publication | Preserve the already published Task result; retain the request/failure until its actual return obligations are resolved. Later unload/disposal observes the failure, not successful cleanup replay. No receipt rewrite or publication retry. |
| Repeated unload of that cleanup-failed instance | After existing argument/cancellation and queue-admission checks, fault with its retained original EDI. No destruction, control append, subordinate retry, or `AlreadyUnloaded`. |
| Repeated unload after confirmed successful cleanup | Existing `AlreadyUnloaded` with the current World revision. Slot removal alone is insufficient to select this result. |
| Registry/World closure before a new admission | Existing exact-binding/disposed refusal; no same-GUID successor retargeting. Previously admitted requests follow the shutdown selection above. |
| New preparation/activation while an inactive owner remains | Existing World/domain byte admission still includes its live R/uses. Activation also includes pending slot/occurrence occupancy. Refuse through existing channels before allocations/identity publication when capacity cannot fit. |
| Save immediately after committed unload | Existing dirty-marker refusal until a successful Step or legal restored marker covers the current revision. No hidden Step or save eligibility repair. |
| Save after a later successful Step, with terminal cleanup-only quarantine | Selected behavior: allow only under all unchanged idle/nonfaulted/queue/marker guards. Capture active Scenes/assets only, plus complete identity history and the recorded unload. Quarantine is operational debt, not a live Scene row or an active AssetId solely because its old lease remains. |
| Save during a running cleanup, incomplete control install, faulted World, or World shutdown | Reject before I/O through the existing eligibility/usage channel. |
| Load/restore of a snapshot taken after that successful Step | Reconstruct only its live Scene subset and retired history. Do not resurrect the cleanup-pending instance or serialize a pointer/failure ledger. Old cleanup stays with the original World/domain, not the replacement. |
| Independently retained render/audio/resource output | Its own lease and storage owner remain valid under their contracts. Neither successful unload nor a failed cleanup falsely certifies their final native release. |

**Owner decision still requiring explicit approval:** Chapters 16/27/30 do not expressly resolve whether an otherwise healthy World may save after an unload Task faults solely on cleanup. This proposal selects the operational-debt classification in the table, supported by Chapter 27's live/historical distinction. Approval of that classification and terminal no-retry quarantine is required before implementation; it is not claimed as already normative. No automatic recovery API is proposed.

## 6. Storage and layout changes

Use the existing registry charge, with checked arithmetic:

```text
A(c,s) = 64 + align8(c*s)
G(N,Q) = 512
       + 2*A(N,IntPtr.Size)
       + A(N,256)
       + A(N,sizeof(bool))
       + A(Q,IntPtr.Size)
       + A(Q,256)
```

For the current 64-bit defaults N = 65,536 and Q = 128, G is **17,926,016 bytes**. This is the source's reservation formula, not measured heap occupancy. The 256-byte per-slot allowances are already charged prospective instance/request carrier capacity; no physical 256-byte owner-array is created.

| Proposed representation effect | Exact field/capacity accounting |
|---|---|
| `SceneInstance` | Reuse its activity `int` for lifecycle. Add cached Entity count (`sizeof(int)`), first failure EDI reference, retained concrete unload-control reference, and actual admitted unload-request reference (three `IntPtr.Size` fields). Raw added fields: 28 bytes on 64-bit. No new plan, promise, array, or Lock. Qualify the changed complete carrier and its retained invocation ownership against the existing slot funding; do not assume the 256-byte carrier allowance pays every transitive Task/fault graph. |
| `SceneWorldRegistry` | Reuse disposal `int`. Add two pending counters, invoker thread ID, additional failure count (four `int`s), and one first-EDI reference. Raw added fields: 24 bytes on 64-bit. Qualify the complete registry plus its existing Locks within the existing 512-byte envelope. G and backing capacities are unchanged. |
| `SceneExpansionPlan` | No added fields. Retain the existing 168-byte selected-profile shell and its existing `IntPtr.Size` planner surcharge from `2828fd41`. Terminal queries/container detachment do not add another EDI, reservation, owner, or payload copy. Mutable terminal container fields must retain the same representation and closed-access guards. |
| Scene control return tail | Add one `long` debt, one return-state `int`, and one EDI reference to the existing concrete Scene-control wrapper: 20 raw added bytes on 64-bit. Precharge **align8(20) = 24 additional bytes** through its existing Scene-control preparation reservation before wrapper construction; retain them through the actual return tail. Do not assume incidental CLR padding pays for a new field. The shared 128-byte `ControlAppendPreparationBytes` is not thereby certified as the full existing Scene-control peak. |
| Lower commit facts | In the field order specified in section 3.1: one `long`, one EDI reference, one `int`-backed kind, and two flags. Raw fields total 22 bytes on 64-bit; natural aligned stride is 24 bytes. No heap carrier, delegate, or new World field. Its bounded stack/workspace belongs to the existing lower structural operation. |
| Receiver/domain | `WorldSceneStorageReservation.ControlByteCharge` remains 72; its links/states/owner are unchanged. The existing domain `InterestEntry` stride remains 128, including its World carrier and receiver head. No extra domain cell or credit kind. |

Raw field additions are exact; **new CLR shell sizes have not been measured**. Do not substitute those sums for automatic-layout observations. The next implementation must measure the changed carriers on its selected SDK/runtime and include exceptional-path allocations and actual return tails in the existing owner ledgers. Reusing an EDI reference does not allocate another EDI; newly required capture/dispatch storage is not assumed free or covered merely by counting that reference.

While a plan R is live, its full original receiver bytes remain attributed to the exact World/domain. Registry occupancy does not double-charge R or transfer it into the slot allowance. Original W and `256 + W` admission are unchanged. A confirmed returned R is not charged twice after a bookkeeping failure; the remaining failure/carrier/return debt must remain in its existing, actually funded owner.

This proposal closes an ownership protocol, not the presently unqualified complete planner/cache/Rendering/exception/publication ledger. If a carrier or exceptional envelope cannot fit its existing funding, increase its **actual charged demand within the existing ceilings** before allocation and keep the consumer gated; do not reduce W, invent a fallback cap, or declare qualification from the standing constants.

## 7. Focused regression and evidence plan

Add a small coherent set to the existing clean Scene/Entities test areas, using actual World construction, Scene registry/reservations, package-backed plans, and existing controlled cleanup callbacks. Use data-driven modes/failure positions. Do not fabricate a World, `PreparedScene` qualification, render snapshot, restore binding, or native-close receipt.

Until positive public preparation is available, directly exercising existing lower Scene/World actors establishes only their managed owner protocol; it does not establish a `SceneLoader` public consumer. Uninitialized-object allocation is appropriate only for the existing isolated carrier-layout observation, never for behavioral ownership tests.

| Distinguishing case | Required assertions |
|---|---|
| Postcommit plan failure, both destroy modes | Retired Scene identity and correct actual Entity destruction survive the fault; first exception identity is preserved; active counts/indexes/lookups are empty for that instance; the same slot/plan/receiver remain reachable and correctly charged. Outside-instance hierarchy survival follows the selected mode. |
| Lower postcommit attachment Release failure plus plan failure | Commit facts are true despite the lower throw. Scene deactivation and the unload control still occur; E0 is primary, E1 is retained; safe later cleanup is attempted once. No second structural transaction. |
| Repeated unload / repeated registry disposal | Failed owner rethrows its retained original, success alone produces `AlreadyUnloaded`/no-op. Callback counts, identity cursor, unload-control count, pending counts, and live receiver bytes do not change on failure replay. |
| Cleanup cancellation exception / token race | Precommit cancellation changes no structure. A token canceled during the cleanup callback cannot turn a committed failure into a canceled Task. |
| Sibling and queued-request shutdown failures | FIFO requests and ascending-slot instances are all safely attempted; healthy siblings retire; every failed concrete owner stays rooted; primary ordering/count is deterministic. |
| Concurrent/inline teardown | Hold a real cleanup callback at a barrier. No observer reports completion early; no gate is held across the callback or join. Recursive World mutation/disposal/capture is denied without self-wait or a second cleanup. |
| Capacity with zero active Scenes | For N = 1, an inactive failed owner still prevents slot reuse; an empty Scene's root occurrence still consumes its allowance. Existing refusal consumes no new identity and does not overwrite the owner. Remaining byte admission includes live R independently. |
| Persistence after structural retirement | Immediate save refuses dirty state. Under the approved cleanup-only classification, a later successful Step permits canonical capture containing zero rows for that inactive instance, its retired history and one unload control, and no active source AssetId solely from quarantine. A fresh restore cannot resurrect it. |
| World disposal with Scene failure | Healthy prepared attachment/system cleanup still executes; the system report is sealed normally; Scene participant and original World/domain interest remain retained; domain retirement is not reported complete. Old-World retirement failure does not undo a published replacement. |
| Unpublished restored-instance handoff | Failure between reserved-slot adoption and active publication leaves a concrete candidate cleanup owner; the outer reservation/restore cleanup cannot erase it or retry a failed plan. |
| Exact storage and return facts | Changed carrier layouts and preallocation refusal cover the final fields. Preserve the 168-byte plan and 72/128-byte receiver/domain facts. Distinguish an unreturned receiver from storage returned before failed final handback; retained managed containers/credit debt must match the selected path. |

Existing clean regressions to retain include `RepeatedPlanDisposeRethrowsCleanupFailureWithoutRetry`, `SceneExpansionPlanCarrierMatchesSelectedRuntimeLayout`, `PreparedAssetsLifetimeTests`, and `WorldSceneReceiverTests`' original-parent, out-of-order-return, closed-World-carrier, and compiled-interest-stride checks. The existing positive public Scene test sources are not current producer qualification while `QualifiedFootprint` is null.

After implementation, use the SDK selected by `global.json` and ordinary focused `dotnet test` on `tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj`, Debug before Release, combining the affected Scene and World-receiver fixture selectors in one run per configuration. Confirm nonzero selected/executed cases and retain current-run TRX under `artifacts\tst\<Configuration>`. Public positive preparation/consumer observations wait for their real producer gates; do not clear the footprint guard to make a test pass.

These tests contribute only their exercised M03-B/EN020/EN021 and hierarchy observations under the existing IDs. Managed throwing callbacks prove managed owner retention and ordering, **not** successful file-handle, GPU, audio, or other native closure. Missing actual native-close capability/stimulus remains a Chapter 35 qualification blocker with its real retry condition, not a synthetic substitute.

## 8. Bounded implementation sequence

1. **Approve the owner behavior.** Resolve the explicit save/cleanup-debt decision in section 5; promote the lifecycle/completion clarification in Chapters 16/27/30 through their normal owner review. Preserve every schema, public declaration, limit, and acceptance ID.
2. **Implement fixed-slot ownership and handoff together.** Add instance lifecycle/cached count/failure/control links, registry pending counts and active-only enumeration, and unpublished reserved-slot adoption. Update all existing `RevokeFromWorld` callers in the same batch. Add no independent cleanup collection.
3. **Implement the complete unload tail.** Carry the lower commit facts, prevalidate/install the existing control separately from return, retain its concrete tail, and implement logical deactivation before cleanup. Preserve the plan's EDI/no-retry rules and close the last-stage container/receiver distinction. Update the actual preallocation charge for changed control storage.
4. **Integrate disposal and reentrancy.** Elect one registry drain, retain failed queued/instance owners, attempt safe siblings, replay terminal failure without retry, and join concurrent observers outside gates. Connect the private activity/failure ports to World entry/disposal/capture, continue World sibling cleanup, and retain/quarantine the original failed interest.
5. **Validate that integrated revision.** Add the focused distinguishing tests and carrier observations above; run the smallest combined Debug/Release selection. Reconcile actual field/exception/tail storage with existing funding before any public producer activation. Keep allocation/native/AOT/public-consumer gates separately open where their required observations have not occurred.

### Proposal-only preservation

The following five dirty test files were excluded from the change, and their SHA-256 values matched the initial inspection immediately before authoring:

- `tests\TerraFX.Runtime.Tests\Scenes\PreparedSceneAssetRetentionTests.cs`
- `tests\TerraFX.Runtime.Tests\Scenes\RenderSpriteCapacityTests.cs`
- `tests\TerraFX.Runtime.Tests\Scenes\RenderSpriteComponentTests.cs`
- `tests\TerraFX.Runtime.Tests\Scenes\RendererExtractionTests.cs`
- `tests\TerraFX.Runtime.Tests\Scenes\SceneAssetStoreTests.EngineHost.cs`

Only this proposal is authored. No production source, test, maintained owner chapter/roadmap, dependency, or toolchain input is changed; no test or commit is part of this deliverable.
