# M05 standing-owner funding and final-return route

## Status and recommendation

**Tentative implementation guidance for the requested Sol maximum-reasoning review, not an accepted contract, executable profile, or qualification result.** This proposal addresses the remaining blocker recorded in the [standing-owner construction boundary](m05-cosmetic-particle-standing-owner-construction-addendum.md). It does not amend that document or maintained design chapters.

Select one route: **one selected-only standing record retained by the existing World lifetime, with the original World debit held through an existing process-registry terminal invocation**. Extend that invocation's accounting/return protocol rather than invent a particle allocator, operation use, or independent close owner. Admit the complete derived peak once; do not refund standing storage at logical owner return or at the present `FinishWorldRetirement` debit.

The route is finite in membership for the input below. Its byte values and compiled final suffix are not established. Keep both selection guards and `WorldCosmeticCreditOwner.HasQualifiedStandingOwner` unchanged. No selected World/owner, credit operation, pool, Scene participant, Task producer, or GPU consumer is enabled by this proposal.

**First implementation boundary: choose (a), analysis/constructor-free observation of existing types only (§6). Do not add the proposed standing record or lifetime field in this batch.** The future representation below is not authorization for a declaration-only production change under existing charges.

### Authority and fixed first input

Chapter 04 owns exact admission, prospective grants, allocation/growth overlap and retained debt. Chapter 35 owns implementation/observation gates. Chapter 20 owns animation/deformation; it contains no particle standing-owner contract. The maintained particle behavior is **Chapter 21 §5**, supplemented here only by the existing tentative M05 particle-owner/addendum chain. Storage-only work changes neither chapter's behavior.

Use the real `World.Create -> AdmitWorld -> new World -> initialization -> Dispose` route, with:

- One immutable registration from an empty `CompiledContentSchemaRegistry`, selecting only `RegisterCosmeticCreditStorage()`: no component, tag, system, resource, attachment, Scene, or CPU-extraction support.
- Standalone construction, `AdmissionDomain = null`, `CpuWorkerCount = 0`, explicit nonempty World/replay GUID inputs, and otherwise default `WorldOptions`. Zero workers is already legal; it avoids machine-dependent worker counts and creates no worker threads/queues. The existing worker-pool object/lock and its two zero-length array expressions still belong in the catalogue.
- One owner-thread Create-to-Dispose invocation; no Step, entity/control/subscription mutation, use/history/operation acquisition, restore, host, domain handoff, extraction, recording, or domain `Retirement` observer. All 128 cells remain unused.
- The SDK selected by `global.json`, initially the Windows x64 managed RC3 lane. Debug and Release are separate compiled inputs. Other targets/compositions remain unsupported, not implicitly covered.

This is the initial **profile input**, not a new public restriction or a fake producer. A later internal caller must match its actual closed registration/options and execution roots before using its profile. A Boolean saying “empty” is insufficient. A different worker count, producer, observer, or registration composition reopens the corresponding rows.

## 1. Production facts driving the choice

`E` below means `sources\TerraFX.Runtime\Entities`; `T` means `tests\TerraFX.Runtime.Tests\Entities`.

| Current production/test evidence | Consequence for this route |
|---|---|
| `E\World.cs`, `AdmitWorld`, checks selection after option validation and before `Guid.NewGuid`, registry reservation, default-domain creation and World allocation. `E\WorldConstructionPlan.cs`, `ValidateForConstruction`, checks again. | Preserve this boundary until the **whole** profile is available; no measurement may bypass it to construct the candidate. |
| `E\WorldCosmeticCreditOwner.cs:12–34` copies existing bindings and allocates one `Cell[128]`. `T\WorldLifetimeControlQuarantineTests.CosmeticCreditCellBackingUsesCompiledStrideAndWorldAccounting` observes stride 80 and charge 10,304. | One owner header and one array, not another lifetime/domain/registry or 128 acquired controls. Array accounting is not GC allocation size. Cell referent multiplicity is zero for this input. |
| `E\World.cs:640–687` creates the lifetime, identity ledger, quarantine reservation, worker pool and plan before `new World`. Its constructor establishes OwnerId/thread and enrolls the World before returning; `unpublishedWorld` is assigned before initialization. | Put the storage initializer **after that assignment, inside the existing initialization try**, before any application factory/publication. No cosmetic owner need exist in an unreturned `World` constructor. |
| `E\WorldConstructionPlanControl.cs` constructs a replacement operation for a target/replacement preparation. | Do not instantiate it for fresh storage. The existing plan/lifetime/reservation edges suffice. |
| `E\WorldSystemInstanceRegistry.ReserveQuarantineCapacity(0)` ensures one lifetime-list position and constructs a reservation, but reserves no system-domain position and performs no byte admission. | Use the real lifetime anchor for construction/close debt, pricing its registry-owned backing/growth separately. Do not call `RetainDomainReservation()` at N=0 or add a dummy system. |
| `E\WorldAdmissionDomain.SceneStorage.cs:676–793` elects retirement, completes the lifetime, removes the World interest/**all** its data bytes, then can publish domain retirement. `DisposeWithinTerminalInvocation` still resets a World field after the inner Dispose returns. | The original debit currently ends too early to be the new standing caller's final-return boundary. A longer-lived reference alone does not repair it. |
| `E\WorldGuidRegistry.TerminalAnchor.cs`, `InvokeTerminalWorldClose`, already encloses `DisposeWithinTerminalInvocation`; the domain retains its registrar charge through publication until terminal acceptance. | Reuse the exact original O/key/owner-U anchor. It is not automatically attached, nor a funded allowance for its referents. Current completion still consults the lifetime permit after the World debit and zeros registrar charge inside domain gate scopes. Those are the specific suffixes to rectify. |
| `T\WorldGuidRegistryTests.TerminalAnchorReleasesExactBindingAfterEnclosingWorldDisposeReturns` checks binding retention/reuse; `T\WorldSystemFactoryTests` checks one anchored throwing-system close is not retried. | Useful existing identity/failure regression patterns, not allocation/last-access proof for the new storage graph. The system-failure producer is excluded from this first profile. |

Symbol references currently show only test callers of `TryAttachTerminalAnchorForDefaultDomain` and no production reader of `WorldAdmissionDomain.Retirement`. Thus this input has no arbitrary observer fan-out. The actual promise/setter/cold runtime path still needs its own row; `RunContinuationsAsynchronously` is not that proof.

## 2. Concrete representation and construction

Refine the earlier [common-representation suggestion](m05-cosmetic-particle-common-representation-addendum.md), without importing its historical sizes:

| Location | Proposed exact additional representation |
|---|---|
| `WorldLifetimeControl` | One nullable `WorldCosmeticCreditStandingControl` reference, null when unselected. This affects **every** lifetime's shallow layout and must have common funding before operational integration. |
| Proposed engine-private sealed `WorldCosmeticCreditStandingControl` in Entities | Three readonly references: exact lifetime, original reservation, immutable registration; one copied readonly `WorldGuidGrant`; one readonly `long` admitted standing/protection amount; one nullable actual `WorldCosmeticCreditOwner`; two nullable `Exception` references for first construction and storage-cleanup failure; one `int` owner-thread ID; one `int` phase. No finalizer, lock, Task, delegate, collection, runtime callback, or extra grant. |
| `WorldConstructionPlan`, `World`, `WorldDomainReservation` | No new instance fields. Plan reads the once-bound amount through its existing lifetime. World attribution reads the same record; it is not another physical debit. Preserve the reservation's existing envelope. |
| Domain/process terminal state | Reuse the existing domain registry/grant/lifetime/failure/state fields and binding-cell terminal domain/failure/state. Add a private construction-pending phase and exact-lifetime promotion/terminal-held transitions, not a second anchor object or another O/P/U. Any discovered layout change invalidates this field inventory and requires repricing first. |
| Builder/registration | Retain their implemented selection fields and staging restrictions. Close their actual shared metadata accounting; do not add another per-World copy of registration metadata. |

The standing record is necessary because neither the owner nor the existing lifetime has a place to preserve a failed, not-yet-returned child construction and its original amount. Putting the record's fields in World and plan would duplicate state and enlarge unselected objects. The record grants no additional release authority; the exact lifetime remains sole owner.

These are future declarations, not the next batch. A production lifetime field changes every fresh/restore instance, including unselected Worlds; a false selection guard or separate build output does not account for that layout. The eventual construction sequence would be:

1. Before issuance, resolve a source/toolchain/input-bound profile and calculate the combined World peak and prospective default-domain peak (§3). Absence/mismatch refuses at the existing guard. Capture numeric/profile facts in synchronous construction locals; no heap profile wrapper or alternate allocator is needed.
2. Add a fixed synchronous **process-registry caller of the selected fresh `AdmitWorld` body**, analogous to its existing terminal close invocation, with no delegate or heap wrapper. Its stack-local context contains the exact original fresh grant, actual domain/lifetime references as they return, captured amounts and phase. Use the existing fresh role acquisition. Before default-domain allocation, preadmit its exact prospective registrar construction charge against its own Chapter 04 metadata/aggregate limits. The present constructor allocates its TCS/backing **before** assigning `_registryCharge`; a later World reservation cannot retroactively fund that interval. The private selected construction entry spends the prospective original-domain provision and installs the same charge, not a second debit. The process caller retains construction/unwind provision until the actual body returns/throws; it uses no process metadata bytes for domain data.
3. Immediately after the domain constructor returns, root that actual domain in the existing original reserved binding, before the next fallible construction. Add the construction-pending phase to the existing anchor protocol. Only the exact fresh invocation may reserve/enroll/configure/commit its original World through this phase; ordinary terminal-prepared admission remains refused. Bind the actual lifetime when it returns. Cancellation cannot detach a construction-pending anchor. A constructor that never returns a domain has no invented domain object: its prospective provision covers the real partial-construction/unwind, and release requires the same returned-body/harmless-suffix proof.
4. Before lifetime allocation, include its measured common delta and the entire selected provision in `TryReserveWorld`, checking the exact World limit as well as domain headroom. Reserve the existing lifetime-quarantine capacity before any cosmetic allocation. Keep all original lineage/binding ownership.
5. After `new World` returns and `unpublishedWorld` is assigned, construct and immediately install the standing record on the actual lifetime. Its own unescaped constructor is paid by the original construction provision. If it fails before returning, there is no invented standing object or acquired use; the enclosing original claim still owns unwind.
6. Mark child construction pending, call the still-private qualified owner entry using the actual owner thread and bindings, and install the returned owner in the record **before publication or any operational bridge**. Verify binding/capacity/amount. Constructor failure leaves the record/pending claim/first failure retained; success followed by later failure retains the actual owner.
7. Finish ordinary initialization and exact budget configuration. Commit lineage/WorldGuid and `PublishFresh` as before. Promote the construction anchor to the exact active-lifetime terminal-prepared binding **before setting `publicationCommitted` and returning the selected World**. Revalidate the existing active-lifetime anchor conditions; this is promotion of the same original anchor, not a second attachment. Treat failed promotion as construction failure. Only an ordinary successful construction-body return to the process caller transfers ownership to the returned World.

For this first private profile, **a construction failure after the domain has returned conservatively retains the remaining original construction/domain/binding claims until process shutdown**, even if some child cleanup succeeded. The original process anchor covers the pre-lifetime interval; where a lifetime exists, use its existing quarantine path as well. This explicitly avoids claiming a clean failed-construction refund from inside an unreturned `AdmitWorld` finally. It adds no role or emergency owner and preserves confirmed subreleases. Supporting a reclaimable failed-construction path later needs the same prepared end-or-retain commit as normal close. Unselected routes remain unchanged.

The live operation bridges must separately remain unavailable: a successfully installed storage owner must not authorize `TryReserve`, `AcquireCosmeticUse`, a pending spawn, or an external borrowed owner getter. The first profile does not pay `128 * (256 + 65536)`.

## 3. Closed membership and derivation of the exact preclaim

An accounting row is a preclaim with one physical owner, not a measured object size. Derive and retain both columns. Use the actual compiled type's constructor-free shallow extent for reference types, actual managed stride for value arrays, the owning array rule for accounting, and source/IL/native allocation-site analysis for children. **Do not carry forward the old 10,592 subtotal or a guessed residual from any 4,096-byte row.**

| Disjoint row and finite membership for this input | How to obtain its real bound | Original reservation and held interval |
|---|---|---|
| Shared registration: the existing builder/registration selection fields plus the actual empty Build graph. The builder has six list fields; Build creates two description builders/schedule; the registration constructor creates four validation sets and empty-copy/sort paths. Schema backing is borrowed, not recreated. | Compare actual compiled field inventories/layouts with the pre-selection declarations; trace the exact empty `Build`/schedule/constructor path, including cold comparer/empty-array helpers and temporary/retained overlap. Validate the applicable `MaximumRegistrationBytes` at Build and the configured World registration boundary; current schedule graph/conflict checks do not do this. | Existing caller/registration construction owner, once per actual shared registration; not World data. The measured selection increment alone is not complete registration-budget closure. |
| Common lifetime change: one nullable reference in every lifetime. No new World/plan/reservation fields. | The existing type's measured steady shallow input is **224 B before that reference** (§6). Its candidate delta is unmeasured; audit actual candidate layout/initialization in a later common-accounting revision, not this batch. | Original World control on selected **and unselected** routes, before lifetime allocation through lifetime's last owning access. |
| Selected record: one instance with precisely §2's fields; credit owner: one instance with its existing fields. | The existing credit owner's measured steady shallow input is **136 B** (§6). The proposed record does not yet exist or have a measured size. Its later actual declaration needs shallow/GC-layout and constructor-allocation evidence; no shadow class or operational uninitialized object. | Selected original World provision, before either allocation through returned helper/acceptor/lower-close use. |
| Cells: one fixed `Cell[128]`; three reference slots/cell, initially null. | Reconfirm the existing literal field inventory/stride test for final binaries. The known lane gives `64 + RoundUp8(128 * 80) = 10304` World bytes. Obtain actual array allocation size separately; do not add an extra accounting header. | Same World provision. Zero controls, cell exception referents, operation workspaces and journals are allocated by this storage-only constructor. Journal/handback values must remain unboxed/uncaptured. |
| Existing World construction/close graph, including its 4,096 row: actual empty World/lifetime/default owner/permit/plan/worker pool/locks, diagnostics, identity/archetype/entity-slot directories, subscriptions, system claim/cleanup preparation, and empty attachment release paths. | Expand the **actual** `AdmitWorld`, `World` field initializers, zero-worker constructor, `WorldSystemCleanupPreparation(0)`, and Dispose call graph. N=0 does not remove cleanup report/fault/finalization objects. Reconcile each allocation to an existing charged row; correct any uncovered baseline rather than assigning it to the new child. | Existing World reservation and appropriate diagnostic sublimit. The baseline is not “whatever fits in 4096” and must be established before using it as B. |
| Protected selected construction/abort/close work: one synchronous construction attempt or one close; no reentrancy/retry, publishers, receivers or independent uses. At most the record's two retained failures plus any distinct lower escaping failure in the domain's existing failure slot. | Enumerate each reached allocation/throw site and simultaneous primary/cleanup/terminal failure closure, including actual exception, trace/dispatch/string/backing/native children. Derive an upper bound from pinned runtime paths, then check it with precise allocation observations. A count of two exception references is **not** a byte bound. | World protection for World/storage work; domain protection for its disjoint publisher/terminal failure work. Protection remains reserved for its admitted lifetime. A lower failure alias is charged once; a distinct graph is a distinct row. |
| Default-domain registrar: one domain, one TCS and its Task, its gate/runtime children; current 8-GiB construction selects one lineage entry, one lineage GUID, one interest entry, one reservation entry and 16 index buckets in five arrays. No cosmetic-driven growth. | Derive these capacities from `GetInitialRegistryCapacities`/`FreshRootLineageKeyCount`; price with `GetRegistryArrayCharge` and required Chapter 04 cell/backing rules. Measure actual object/array extents and inspect the real no-observer setter. Include cold construction, publication, failure and gate-return peak. Additional uncovered control is an explicit separately owned metadata row, not free space in the named 4096. | Original prospective, then installed domain registrar charge, within metadata B and aggregate domain bytes; never counted again as World data. Retain the protected control through §4's last domain access. |
| Quarantine reservation and shared lifetime-list backing. Exactly one lifetime slot per construction; zero selected system/domain-retention slots. | One actual reservation object under World control; use current list count/capacity and outstanding reservations for shared backing. Before `EnsureCapacity`, reserve full old+new backing and real copy/runtime peak; installed capacity remains charged at high water. Preadmit actual static registry/control initialization before its allocating initializers, not retrospectively. No duplicate full-list charge per World. | `WorldSystemInstanceRegistry` owns shared backing under Chapter 04's existing **16-MiB system-factory freshness metadata qualification target**, together with its other ledger storage, not a new particle purse. Implement checked original byte preclaim/pending-growth/high-water counters in that owner before this route can use it. This accounting is currently absent; a slot count is not a debit. The per-World reservation object belongs in B once. |
| Process registry: existing fresh K/O/P/U = 1/1/1/2, existing page-growth tickets/backing, and the fixed terminal invocation. | Preserve `BindingCellChargeStride=128` and its 32,832-byte O-page charge; inspect final generated invoker/release code, process-gate/runtime children, failure capture and surviving high-water backing. Prove complete existing control coverage or derive separately charged process metadata within the existing B. | Existing process registry control/pages, not World/domain money. Attachment adds zero roles. Original O/key/owner-U last longer; ordinary fresh commit ends P/coordinator U. No cosmetic operation U is issued. |

Rows with no added allocation still require a path argument. In particular, owner close currently scans fixed cells and returns ref-struct handbacks; no additional close object appears in source. Neither that fact nor a zero-worker choice proves cold synchronization, throwing paths, or native stack/runtime work free.

### Calculation, not a selected number

Use these quantities only after the row derivations:

```text
B  = complete disjoint qualified existing World provision for the fixed input
L  = uncovered common lifetime-layout increment, excluding anything already in B
S  = record charge + owner charge + 10304
PW = maximum coexisting protected World work/debt across construction,
     construction unwind, close and retained failure, excluding B/L/S
W  = B + L + S + PW

R  = actual domain registrar backing/control/protection, including any explicit
     uncovered metadata beyond its named 4096 control and applicable array rows
Q  = actual shared system-registry original provision/growth peak, including
     other live freshness/weak-handle/index/quarantine metadata in its target
G  = actual process-registry original provision/growth/invocation peak
```

`PW` is a maximum over **complete simultaneous phase rows**, not the sum of scratch for mutually exclusive phases. Conversely, W and R are both held during close/publication and must fit together even if their protected work executes serially. Allocation spends an already held provision; installation/live attribution is not another grant. Do not deduct an unmeasured baseline overlap or release protected amounts on successful construction.

Admit W against the exact configured `MaximumOwnedStateBytes` and the domain's remaining aggregate headroom. Attribute S/PW and the applicable selected portion of L to the cosmetic standing sublimit, not the entire existing baseline B. R obeys `min(128 MiB, domain.MaximumBytes)` metadata B and participates in the same 8-GiB aggregate. Q/G obey their actual owners; they are not added as duplicate physical World charges. Q's 16-MiB value is an existing **unqualified target**, not current reserved credit: close its shared catalogue and admission before relying on it, or refuse before lifetime/slot construction. Preserve all existing count/byte ceilings and the separate diagnostic/resource sublimits.

Put the same still-held standing amount into `GetOtherWorldOwnedStateBytes`/`GetWorldOwnedStateBytes` and `ConfigureOwnedStateBudget`, including revoked/quarantined ownership. Add the **complete initial World check before allocation**; the present `Math.Max(0, maximum - other - scene)` is not admission of an over-limit base.

**Distinguishing witness:** with independently established baseline B at the selected request boundary, let D be the complete additional simultaneous demand, including L/S/PW and any newly concurrent lower rows in that budget. `B + D` must fit and `B + D - 1` must refuse at that same reached boundary. `B + 10304` is not the positive trial. For aggregate-domain tests use the domain's independently established baseline instead; do not mix the two B values. If an earlier minimum/diagnostics stage fails first, or D is still unknown, the intended endpoint witness is unsupported.

## 4. Actual lower suffix and last return

### Selected normal close

Use `WorldGuidRegistry.InvokeTerminalWorldClose` as the real enclosing actor; no delegate, observer, worker, or new public API is required. The required change is larger than calling the existing attach helper:

1. While the original World claim is held, deny entry, call `BeginRetirement`, then the credit owner's `BeginClose` and lifetime `TryReturnCosmeticOwner`. Accept only its ordinary returned handback with no occupied/retained cell. Keep record/owner/backing/protection after `AcceptOwnerReturn`; Returned is logical.
2. Run the entire current World close body and nested cleanup finally chain under that original W, including `DisposeWithinTerminalInvocation`'s final flag reset. Preserve independent cleanup ordering and failure precedence. The domain terminal anchor remains a strong root.
3. Rectify the selected `FinishWorldRetirement` branch: retire the *semantic* World interest/lineage only after storage-close debt is absent and exact permit escrow succeeds, but leave its **original data debit W outstanding as terminal-held ownership**, not reusable headroom. The existing terminal domain-to-lifetime edge plus lifetime-to-record edge retain the remaining graph. A private terminal-held disposition must make reservation Dispose/ordinary lineage release unable to refund it. This is a transfer of disposition of the original claim, not a new interest/operation credit. The fixed input has no later growth; retain the captured actual reservation total, not just S.
4. Zero semantic interests allow the real domain retirement publisher to run without a World-interest/last-U cycle. Keep the existing registrar/protection R and terminal-held W during `_retirement.TrySetResult`, its field/gate suffix, domain callers, `CompleteWorldClose`, and the entire enclosing World helper. Current interest-count readiness and data-debt accounting must therefore be represented separately. The public Task may complete before ownership handback; its terminal state never triggers a refund.
5. Only when `DisposeWithinTerminalInvocation` has **ordinarily returned to the process invoker** may final acceptance start. This catches the currently uncovered World flag-reset and publisher callers. On an exception, take the retain path instead.

Terminal-held bytes must remain in `_worldBytes` (or an explicitly equivalent original-data debit in the same domain total) after the semantic interest is removed; `ReleaseRegistry` must not erase them or their terminal retention. The standing record already captures the original reservation; the domain terminal fields already capture the lifetime. No extra heap capsule is selected. Every ordinary debit/release/election path must recognize this disposition before mutation.

### Prepared final commit

Replace the selected equivalent of the current `TryCompleteTerminalAnchor`/`TryCommitTerminalAnchorCompletion` ordering with this fixed progression:

| Phase | Required work and surviving owner |
|---|---|
| Validate while W/R/O are live | Under existing process-before-domain gate order, validate all exact generations, escrow/returned phases, byte totals, Task publication result, absent failures/interests/uses and final key/index/role effects. Capture the original grant and debit scalars. Consume no permit twice. |
| Complete World accesses while W is live | Finish all lifetime/record/owner/reservation cleanup and detach ended selected backing/links only after their callers have returned. Finish any permit/lifetime consultation here. Prepare the final process release from the **already escrowed exact binding state**; do not later call `lifetime.RetirementPermit` after ending W. |
| Complete domain accesses while R is live | Clear reciprocal terminal references/state and leave **all domain gate scopes** while W/R are still held. Retain failure authority until the nonthrowing commit is prepared. Serialize against cancellation, disposal, another publisher or admission using the exact terminal phase/process gate. |
| Original data debit, then last domain debit | Outside domain gate scopes, perform the prepared nonthrowing debit of terminal-held W, then the original registrar/protection debit. The latter is the **last domain-owned access**. No subsequent domain/lifetime/record/reservation access, validation, publication, allocation, refusing callback or cleanup is permitted. |
| Process-only suffix | Clear the original binding's terminal root and release its exact remaining O/owner-U/key roles with prevalidated process-local operations. Exit only the process gate and return. Process control/slab/index backing remains charged at high water; no domain callback or spent-permit validation occurs. |

This is an implementation target requiring source/IL/generated-code proof, not a claim that moving an `Interlocked.Exchange` already provides it. The current domain completion method debits inside `using` scopes, and the current registry completion calls the permit; those bytes do **not** meet the target. An invariant/overflow/refusal discovered after debit is unacceptable: move the validation earlier or retain, rather than invent recovery after destroying the owner.

The **final original standing return point** is the prepared W debit, after ordinary World/publisher return and last actual World-state access. The lower domain return point is its later last-access debit. The remaining registry invocation/public Dispose forwarding epilogues must be demonstrated harmless with respect to the ended graph: no access/allocation/owner callback, including inlining/tiering/EH/gate disposal. Passive scalar/reference copies grant no authority. Literal physical return of every public caller is not claimed.

This ends the “who pays the caller's caller?” chain at already process-owned, retained-high-water backing with a proved process-only suffix. It does **not** assign an unmeasured new allowance to the process 4096 row or transfer domain storage there.

### Publication membership

The selected route supports `FinishWorldRetirement` publication only through the enclosing invocation. The seven other existing main-file publication entries remain relevant:

- Domain `Dispose` is the exact default-owner logical close while its World interest still exists; it must not become a competing last publisher. In a no-World-interest construction failure, the construction-pending phase likewise preserves the original registrar provision until the enclosing fresh invocation accepts the failed body return and retains its debt.
- `ReleaseWorld` covers unenrolled admission rollback before selected child allocation. Its whole return/unwind runs inside the proposed fixed fresh-construction invocation. It must not debit the construction-pending terminal provision or publish/refund from an inner finally. Failed construction retains the actual domain and remaining claims under the original anchor; no selected owner is invented there.
- `ReleaseHistoryInterest`, `ReleaseOperationCredit`, `RollbackUnissuedWorldGuidUse`, `RollbackWorldGuidUseRaw`, and `TryReleaseIndependentUse` have no issued producer in the first profile. Keep those acquisitions unavailable for selected storage; do not reject only after issuing their raw/control/interest debt.

Similarly exclude Scene receiver/construction-return and restore/resource-operation ingresses into `EndWorldReturnPart` before acquisition. Preserve ordinary unselected behavior. A later supported producer needs its own enclosing caller/profile; this World-close actor is not its substitute.

## 5. Failed construction, disposal and retained debt

The failure disposition is deliberately not “best-effort cleanup then refund”:

- Distinguish **unresolved construction/credit/close debt** from **still-owned backing/caller provision**. Debt blocks semantic retirement; backing/provision blocks physical refund unless the exact terminal-held transfer preserves the original debit. Include pending construction, actual owner, failed/unfinished handback, terminal-held return and retained failure in those checks, not merely `HasCommittedHistory`. Check every fresh-lineage rollback/release, reservation disposal, attribution clear, retirement election, `CompleteRetirement` and deferred completion. Normal semantic retirement is permitted only after the logical storage handback and protected terminal transfer, never by dropping the owner. A never-published lineage is not exempt.
- If an owner constructor fails, preserve the original exception as primary and its actual pending claim until the constructor and enclosing cleanup have unwound. If an owner was returned, keep that exact object through attempted cleanup. Attempt independent safe releases; never repeat a confirmed destructive subrelease.
- With unresolved storage cleanup, revoke, use `QuarantineAfterCleanupFailure`, and install the lifetime in the pre-reserved `QuarantineLifetimeControl` slot **before** the outer unwind can release anything. Its strong path is registry list -> lifetime -> standing record -> owner/pending debt/reservation. Keep W and the actual lineage/binding claims. N=0's absent system-domain slot is not used.
- On the selected construction-failure route, keep the construction-return debt even after a locally successful child release. Before a lifetime/quarantine reservation exists, retain through original binding -> actual domain; after lifetime binding, that path also reaches the lifetime/standing debt. After the whole fresh body unwinds, its real process caller records the actual escaping failure in the already funded anchor and rethrows. Do not retry construction or detach this retained anchor.
- Preserve the original construction exception/cancellation through ordinary storage unwind. Retain a secondary storage-cleanup exception privately, with existing mandatory lower retirement/quarantine precedence unchanged. Disposal retains attachment/system precedence; absent an earlier failure, throw the actual storage-close exception, or conventional `InvalidOperationException` for retained-without-throw. No fabricated System failure or aggregate.
- During terminal close, a failure before or after permit escrow follows existing `TryRetainTerminalFailure` authority for the actual phase, retains the domain anchor and all **remaining** original claims, and rethrows the actual escaping exception after child finally cleanup. Preserve confirmed partial facts; never pretend debited bytes/consumed authority were restored. Do not republish a terminal Task.
- Retention can last until process shutdown; it is not free capacity. Repeated Dispose must not return credits or retry confirmed releases. A failure root/Boolean does not pay the reachable exception/native graph: §3's protected failure bound remains mandatory.

When construction fails before a standing record or lifetime escapes, only original prospective/baseline construction ownership exists. Do not fabricate a record to explain it. Qualify that actual unwind; retain original authority if cleanup cannot establish release. The candidate must not depend on allocating a new emergency owner in an out-of-memory catch.

## 6. Evidence sequence and the next implementation batch

### First-batch correction: (a), no production field

The earlier instruction to compile the proposed record/reference as the next batch is withdrawn. Separate outputs can prevent accidental execution; they do not make a field in the production type free or leave its common layout unchanged. **Do not add a production field, record type, accessor, initializer, or reservation change in the next batch.** Keep all guards unchanged.

The user-run measurement at `4785e1a3b38fd85fedf37f516e264253c81085cb` supplies these existing-type inputs:

| Existing type, RC3 Windows x64 | First measured batch, reported B/instance | Steady batch, reported B/instance |
|---|---:|---:|
| `WorldLifetimeControl`, without the proposed reference | 225 | 224 |
| `WorldCosmeticCreditOwner`, without running its constructor | 137 | 136 |

Both Debug and Release retained TRX files contain the passing `WorldLifetimeAndCosmeticOwnerHaveMeasuredConstructorFreeAllocations` case; each full selection executed/passed 5/5 tests. That selection also contains the existing operational lifetime-quarantine test: it is not five constructor-free cases or selected-owner lifecycle evidence. The measured Runtime module MVIDs are Debug `4627acc8-ed3a-46e8-9796-9a27e77bca91` and Release `4921ed43-8dbe-4838-b9db-bad8dd549bf8`.

`MeasureUninitializedClassAllocation` warms 128 subjects, collects, then reports integer-divided totals for two measured 128-subject batches. The one-byte difference in each reported average is consistent with 128 additional first-batch bytes, not a one-byte field cost. The output alone does not identify a single 128-B allocation/site or whether it belongs to measurement/runtime setup versus the production path. Preserve that unresolved row; neither discard it nor charge it once per owner without attribution. The 224/136 values exclude constructor/field-initializer children, the cell backing, protected work and caller suffixes.

**Narrowest remaining batch:**

1. Reuse these completed header inputs and frozen source/TRX identities; do not repeat unchanged size tests merely to obtain the same numbers.
2. Resolve the first-batch allocation attribution against the actual helper/runtime path and begin the existing lifetime's constructor allocation-site catalogue: its `Lock` field initializer and `WorldGuidRetirementPermit` construction are separate from the 224-B shell. Observe no valid owner/lifetime instance or World construction to discover those costs. If more measurement is necessary, narrowly extend only the existing test-owned helper to report raw batch totals and distinguish setup; select only the constructor-free measurement case, not the whole quarantine fixture. Use ordinary Debug/Release workflows, no production hooks or new runner.
3. Record the resulting common-baseline/protected rows and actual owner/end from §3. Leave the proposed record size, candidate lifetime delta, standing preclaim and return qualification explicitly open. Do not populate them with padding assumptions, shadow-type measurements or a guessed margin.

This batch can be analysis-only if the retained evidence and runtime allocation-site inspection suffice. It needs no production declaration or World creation tests.

### Later option (b) is a common-accounting revision, not this batch

Before any World creation tests execute with a new lifetime reference, measure its **actual compiled delta**, `candidateLifetimeShallowBytes - 224`, in the same lane and account for every other introduced common allocation/retention interval. Use final, analyzer-clean declarations with legitimate uses, after member-order/usage corrections. A reference occupies eight bytes on this lane, but that does not prove the rounded allocation delta. The first-batch 225 is not the candidate lifetime size.

Conservatively add the uncovered actual common delta to the existing **4096-byte preclaim**, with any disjoint new common costs accounted separately; do not call `4096 - 224` spare capacity or assume zero cost because selection refuses. `World.AdmitWorld` and `World.CalculateRestoreWorldOwnedBytes` (`World.PersistenceAdmission.cs`) both use `FreshWorldControlBytes` before their actual `new WorldLifetimeControl` sites. Cover both routes, hosted/unselected variants, exact World/domain checks and retained attribution; audit direct-constructor fixtures too. Keep this common charge separate from the selected record/owner/backing/protection charge. A later accounted common revision may run ordinary unselected regressions while selected owner construction/selection/operations remain guarded; it does not require pretending the complete selected profile is closed.

That revision must also be an ordinary build-clean implementation, not an inert field experiment. `TFX0002` requires category/staticness/accessibility/ordinal-name ordering subject to the actual initializer/layout barriers in `tools\Shared\CodeAnalysis\MemberOrdering.cs`; `.editorconfig` makes it an error. The §2 inventory is not declaration order. `Directory.Build.props` enables code style and warnings-as-errors, so an unused private slot can also fail compiler/code-style diagnostics such as CS0169/CS0414. Reflection-only measurement does not provide a real production consumer. Do not suppress diagnostics, disable analyzers, add dummy reads/getters, or put two top-level types in one file to force a candidate through. A later field/record needs legitimate capture/read/retention/accounting uses in the same coherent revision and must follow TFX0004's one-top-level-type rule. Until that revision and its common funding are ready, choose (a).

### Before any selected operational execution

Close the following together for the exact candidate bytes:

| Required evidence | Concrete closure |
|---|---|
| Numeric phase ledger | Fill every §3 row from actual object/array/control layouts and source-derived allocation-site multiplicities. Use a precise managed-allocation observation, not sampled allocation ticks, to check the derivation. Account reached native synchronization/runtime children independently. Measurements corroborate the derived bound; sampling/working-set deltas do not define it. Record cold and already-initialized branches separately. |
| Funding before allocation | Supply W/R/Q/G and each exact owner/end; implement common/registration accounting before operational integration. Check overflow, exact World limit, diagnostic/resource/cosmetic sublimits, domain metadata/aggregate headroom and existing process limits. No changed lifetime layout first exercised by ordinary World tests under an unverified base charge. |
| Protected faults | Derive finite reachable exception/trace/dispatch/native graphs for the fixed lane. If caller stack/context, runtime callbacks or an unbounded retained referent defeats that derivation, retain the guard and identify that exact site. Neither two slots nor a heap cap proves the bound. Obtain an explicit owner/provenance boundary or bounded implementation for that site; do not silently assign it to “framework overhead.” |
| Original return | Integrate the complete real close and fresh-unwind callers, terminal-held data debit, escrow and prepared final commit as one coherent batch. Inspect actual IL/generated tiers/EH for the no-ended-owner-access suffix. No logical handback, Task observation, local finally flag or after-close public assertion alone proves it. |
| Scope enforcement | Match the actual frozen registration/options. Refuse hosted, supplied-domain, restore/handoff and unsupported producer acquisitions before allocation. Install no Scene/pool/phase actor to make a test run. Keep operation authority independently unavailable. |

The existing runtime/issuer contracts do not supply a literal outer physical-return callback. **If owners reject the harmless-suffix boundary, that is the smallest explicit decision blocking this route**: either accept the above last-access/ordinary-caller criterion with proof, or supply an originally funded outer acknowledgment before enabling it. An arbitrary larger byte margin cannot resolve that decision.

Likewise, if exact native/runtime/failure bounds cannot yet be obtained, the missing item is the named allocation/retention site and its owner bound—not “particles need a producer.” Continue the constructor-free/site-catalogue batch; do not run the guarded constructor to discover its budget.

### Focused ordinary tests after those prerequisites

Use the existing Runtime NUnit project, Debug before Release, with nonzero selected/executed counts and current TRX. Add focused cases beside the existing Options/lifetime/registry/domain tests, not a new acceptance suite:

- Preserve duplicate/frozen/staging selection checks and early same-GUID reuse after missing-profile refusal. Cover mismatched profile/input, unsupported routes and unchanged unselected construction.
- With the complete profile available, real empty selected `World.Create` installs distinct per-World records/owners, exact bindings/thread/capacity and one original reservation. No operational credit or Task observer is introduced.
- Exact-fit and one-byte-short reached World/registration/domain endpoints use independently derived B/D. Verify the refusing trial performs no selected allocation/identity publication. Count/byte limits remain separate.
- Real successful Create/Dispose accepts the owner return, retains the original debit through publication/World helper return, then releases it exactly once at the prepared commit. Retained roots/bytes and same-GUID refusal/reuse must agree. Instrument with external runtime/debugger observations of actual sites where needed; add no test-only callback to manufacture a return.
- Actual available allocation/refusal/failure stimuli exercise pending construction, already installed owner cleanup, and before/after-escrow failure, preserving primary exception and confirmed subreleases. A fixed input has no application release callback: do not add one to claim its missing cleanup case. If an isolated, real runtime failure cannot reach the target after setup, that row remains unqualified. Do not induce process-wide memory pressure in the shared environment.
- Preserve existing terminal binding-page accounting, quarantine rooting, default-domain/ordinary Scene-receiver and unselected factory-failure behavior for the affected lower transitions. These regressions are not new Scene consumer or pool qualification.

Do not open admission on header tests alone. Managed behavior, compiled last-access proof, exact allocation accounting, native/AOT qualification, real Scene/pool consumers and milestone acceptance remain distinct.

## 7. Inspected input boundary

This is a current source-backed proposal, not a replay of historical module results. The original route boundary was checked on 2026-11-19 at `5a1ae264eb737618ae44e8b86ae16a8162520809`; the §6 correction incorporates the subsequent user-run existing-type measurement committed at `4785e1a3b38fd85fedf37f516e264253c81085cb`. Working-byte hashes below include the updated measurement fixture and correction inputs; source, tests, maintained design and Roadmap were not edited by this proposal/correction. No new build, test, allocation probe, owner/World/Scene/pool operation or runtime-publisher execution was performed for this documentation change.

The complete standing-boundary inventory was checked: its source/contract/dirty-file entries matched; concurrent updates refreshed its binary hashes and clarified the unrelated executed selections. Those changes were inspected, not authored here. Those binaries were not used as evidence here. No current source-to-binary or old-test-to-current-binary correspondence is asserted. Rehash changed inputs before disposition.

`E`/`T` have the meanings above. These hashes are inspection identity, not acceptance receipts.

| Input | SHA-256 |
|---|---|
| `docs\proposals\m05-cosmetic-particle-standing-owner-construction-addendum.md` | `AB37920CF44BFF419E3DF7B77800E4428F95F8E139A498839ABF3919D3046BC8` |
| `docs\design\04-capacity-and-workload-profiles.md` | `60047DD1EACF26196B0BF23F16798E2AA265FD26CD36B9A0B4DF1B79135730D7` |
| `docs\design\12-entities-components-and-queries.md` | `174B2465746DA79CEF560C8506216EF4F99F9D1AABAC184F872C9E6CB0B94E0B` |
| `docs\design\20-animation-and-deformation.md` | `B5AE58EA0D5821FAB70001A44D59A100CAF789CFB148107776A026355E9C61D1` |
| `docs\design\21-2d-and-3d-rendering.md` | `989A6183186592F165FA9614CC19A8FED63826E2A51170CE55FBFFFCA8116ED2` |
| `docs\design\29-project-setup-build-and-deployment.md` | `49598161138FEBBF889F2A5176E3504AF8D8BB8481D2157FBCFBBD94D2B46604` |
| `docs\design\35-implementation-roadmap.md` | `24EEC6E92B7AD904D552F290EF748FFC8FCE13B6B94A89EBD9F39487D4584D87` |
| `docs\project\Roadmap.md` | `FD84BBDF11FEF04C15FC09489DD663864664EFADEBF1E294748C83E8A73B1BF8` |
| `global.json` | `1BCE01871321C2DF82E887DA428CE5C121A6C6541FE65017FE3D249BE47E190C` |
| `.editorconfig` | `342683DE2B1C262624546F312F7D8E2078F89185ADD39F43F1FB5013376FD553` |
| `Directory.Build.props` | `770EE4B2A422861A6F23F0D39DD0ECA361A81CB87E64EBAC4E12170F71231D7A` |
| `tools\TerraFX.Diagnostics.Analyzers\MemberOrderingAnalyzer.cs` | `03E593E4D540EDB30A74A2832F96E0FED7B9C87CAD9D7E48481F0810272446BF` |
| `tools\Shared\CodeAnalysis\MemberOrdering.cs` | `21B06DF3E8F95AAB9BE1AE264DCB794C9B7BCBC58AC98624EC1370A608E6D895` |
| `E\World.cs` | `99DF71A9956B22E072FDF5314056A6854880BBDE83654D09E5EAB4A197B92FE8` |
| `E\World.PersistenceAdmission.cs` | `1DBAC6F5D3DA19A6E616BDD03E8F4A9F885CF74CC0FD40D4F3E34ADAC5DA62B4` |
| `E\WorldLifetimeControl.cs` | `5A304AAF8B2353E28F6F743D5587E82F12E979DCF0DFEBF55103E075CCAF54F2` |
| `E\WorldLifetimeControl.CosmeticCredits.cs` | `C6F41DE02F0896016FA55845A61661BCD1A4DFE085BF68302767FE9C94DA3228` |
| `E\WorldConstructionPlan.cs` | `B2EE768A7CFD14753882452CE019FFFF3DF274969B3368572E3EF26DDFFBFB06` |
| `E\WorldConstructionPlanControl.cs` | `D052B6C1B276601E5F7D75D546D9373AE82A3350140886299CD21D61577F2BE7` |
| `E\WorldCosmeticCreditOwner.cs` | `7EEFB227D9654552997BF3515E29CA32E3791F0A155FBF1AA37C274508E2CEA6` |
| `E\WorldCosmeticCredit.cs` | `33B7E51F1B36F7BB2860F6BB48426375523398C4AB1E0BE7E692E363780B6707` |
| `E\WorldCosmeticCreditHandback.cs` | `DAD1757D41E9F419B3AB18F5A137C0484F4C368363422ECAFFCD137380A0AC55` |
| `E\WorldCosmeticOwnerHandback.cs` | `D779373743112441F594CF0325E23F8FEE3615317A7D47E35EB0599606079BE5` |
| `E\WorldAdmissionDomain.cs` | `0AFBC247E5CE649EDBD6F26A01F67BD291D7F23CD1529DD5CD735E89DFFD70F7` |
| `E\WorldAdmissionDomain.SceneStorage.cs` | `C8595AF75DCDB5B7128AB6C180A0C07C53E5D8109BBE481F96B4DA3634375608` |
| `E\WorldAdmissionDomain.TerminalAnchor.cs` | `63092FD413CF296E188116A3910C1E3031AE42D6C86E8BBB80820B8A62E7970B` |
| `E\WorldGuidRegistry.cs` | `45C2A4D3B48F370175485A0D7BC6C13A3F6B8230F976B34032CE2C13D3125C58` |
| `E\WorldGuidRegistry.TerminalAnchor.cs` | `EF6E0389A5C303B3EE7F0DB37D66A70EC5FAEB921670CBCB0A65BEA2D91843D2` |
| `E\WorldGuidRetirementPermit.cs` | `81F87F907579884295E3B441F21B5EA4FE6B2509790735B1577F2B27B16D2BD6` |
| `E\WorldGuidGrant.cs` | `A371A0AFD89C3D987815FA57A866518D39B664E1561F2050346285DB5FFA3399` |
| `E\WorldDefaultDomainOwner.cs` | `BCF8F830FBB29E652D480608A3BDFC197D89D7E58299622941E87685B4C2AAD4` |
| `E\WorldDomainReservation.cs` | `D6CDD6105850B8076D40891B0ED1F583D9FCDE6EE190444A7D570879964403DC` |
| `E\WorldSystemInstanceRegistry.cs` | `70DCB73D187AA6FC27B099F8C27B6716D3D63EF6C4022662017D34C25F5A4E20` |
| `E\WorldSystemQuarantineReservation.cs` | `F864C7C0E250D4F6DA38B587C964CE0366E54FFFE22509120B84726E4354361F` |
| `E\WorldSystemCleanupPreparation.cs` | `AC0CA1A32B96702985F1286D923142BC4B42B014CD4B5CA82AC531D5FCECB723` |
| `E\WorldWorkerPool.cs` | `9B9DAB85A6FE87E184D85FE2D7F74BBAD9B7AC9B5842E5B562E1C80DC635D856` |
| `E\WorldRegistrationBuilder.cs` | `63B526D8A01A8795BB54D8522F25F33E5C8DE492B8DFFD7A7F731FA2A290EC84` |
| `E\WorldRegistration.cs` | `05368A6952DE904A07DA3782D11837C82FF2A37C515D2A11A669B5219A79ADB7` |
| `E\WorldOptions.cs` | `1DACC0CCF24E4E3FF5459E53BC6CF5F562F565A3B7520C24A25451BD12BA4C54` |
| `E\WorldSchedulingLimits.cs` | `497B007892C2F5E2C7B87665DF15656ED0788DC810C4FB37D2213719F22DDF61` |
| `T\WorldOptionsTests.cs` | `AE075BEBF688FA767B0FE6906495DBA8DCABA6DD4B59E82BCC9658BBA4C6579C` |
| `T\WorldLifetimeControlQuarantineTests.cs` | `82BA06D3855B045AF5D473B23BA5BA72EF54160480CA276DDB283C9AC474AE40` |
| `T\WorldGuidRegistryTests.cs` | `37A57BBA342B8454C399CE60CD9A02AA9CF29009D9A97ABFF2A0C562417A2105` |
| `T\WorldAdmissionDomainTests.cs` | `9F884C2185CB3372914457DC72EC760666EDE1BD2388A387A08B64F3E17B894C` |
| `T\WorldSystemFactoryTests.cs` | `87DAFCDB1C57F622625BC2538C72A8A7D2EE54090D8C11A79DD75C0A1FFAA2B6` |
| `docs\proposals\m05-cosmetic-particle-pool-owner-closure.md` | `888D97BA6BC9AC2F06295D725162074F85C275658CDB3D0DDF31B8DA89904A95` |
| `docs\proposals\m05-cosmetic-particle-common-representation-addendum.md` | `F79FAE8F40CC5D7C014DA96B4B336C9EF59C851E3A80A83BB0AA4F620D2AF443` |
| `docs\proposals\m05-cosmetic-particle-credit-handback-addendum.md` | `6FF6EA4E6BA175558C492155FC95E08B936C1171B6747CA6E499166A2F8CB19C` |
| `docs\proposals\m05-cosmetic-particle-budget-return-addendum.md` | `1574E6D573BDFE2AF45A3E44EE3BAB5FE52131A02547C8610294F8BBF3C4B119` |
| `docs\proposals\m05-cosmetic-particle-credit-layout-evidence-addendum.md` | `BFAFB74AA1570308830BB95C92058963101588419D196E4929EC5E8B786FDC50` |
| `artifacts\tst\Debug\m05-cosmetic-owner-layout-steady-debug.trx` | `E7C994BBE87D00DBABA9992BB5AACF81FC79B2A952E56FC43F27448061E34FD8` |
| `artifacts\tst\Release\m05-cosmetic-owner-layout-steady-release.trx` | `8774E861518F3BF554840680EDF124719C107652C5F5B55BDF50E523EB81A235` |

The five preexisting dirty Scene test files listed in the standing boundary were not edited or used as qualification inputs. No schema/fixture changes are proposed. This document supplies one construction/funding/return candidate and its next bounded work, not permission to promote any of the guarded capabilities.
