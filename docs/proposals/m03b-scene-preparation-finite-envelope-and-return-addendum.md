# M03-B Scene preparation finite-envelope and return addendum

**Status: tentative rectification proposal for later review, not a qualified footprint, implemented producer, maintained-contract amendment, or permission to execute the fail-closed candidate.**

## Disposition

The existing proposals do **not** close the concrete preparation blocker. The [owner-aware handback assessment](m03b-scene-preparation-owner-aware-async-handback.md), sections 6-8, deliberately selects an unavailable-capability prerequisite, not an implementation of it. The [supplier-return proposal](m03b-scene-preparation-supplier-return-closure.md) likewise supplies neither finite J/K nor a fulfilled return channel. Their safety conclusions remain valid.

This addendum supplies three missing decisions/inputs rather than proposing the stable root again:

- Record the verified preparation-versus-activation cap mismatch explicitly.
- Resolve a previously unexamined **input-proportional native fault-capture path**; a fixed exception count does not bound its bytes or work.
- Select an operation-specific, owned-invocation implementation route and give its funding, cleanup and release proof order. Do not select a decorator over the current stock builders, another completion observer, or a hypothetical CLR return notification.

The route below can advance prerequisite implementation under the user's tentative-unblocker policy. It cannot turn an unresolved native allocation or invocation suffix into a qualified producer. Keep `QualifiedFootprint` null until both obligations actually close. Chapter 35 already permits unrelated dependency-ready M03-B/M05 work; this proposal creates no new milestone gate or API/acceptance identifier.

## Current source findings

Source paths in this section are beneath `sources\TerraFX.Runtime`.

| Boundary | Verified fact and consequence |
|---|---|
| `Scenes\PreparedScene.CreatePreparation` | `QualifiedFootprint` is null; the throw precedes `world.AcquireGuidUse`. The current ordinary preparation candidate is not executable. Its fields do not establish an allowance. |
| `Scenes\PreparedScene.Preparation.cs`, `StartPlanPreparationCore` / `EndPreparationProducer` | `_supplierProducersUnjoined` is set before invoking the planner and has no clearing assignment. Removing the guard alone would not produce a draining implementation. Task readiness is not a valid replacement signal. |
| `Scenes\SceneLoader.StartPlanPreparation` / `StartResourcePreparation` | Captured funding delegates are constructed before the supplier calls. A returned storage reservation cannot retroactively fund those closures, the call envelope, or continuation registration. |
| `Scenes\ScenePrefabExpansionPlanner` | The closure includes `PrepareCoreAsync`, `PrepareTakingLeaseCoreAsync`, `BuildAsync`, `CountAndMeasureAsync` and the nested Task/ValueTask traversal identified by the existing handback assessment. Two top-level suppliers and three driver parts are not bounds on these invocations. |
| `Scenes\ISceneExpansionResourcePreparer`; `Rendering\Renderer.CpuExtractionSupport.cs`; `Rendering\RenderSceneAssetBundle.PrepareAsync` | The actual adapter awaits the bundle; the bundle awaits Atlas, Material/texture and Mesh/Skeleton helpers. A selected participant returning no render data has still invoked its producer. Only absence of the participant skips that role. |
| `Scenes\PreparedScene.Preparation.cs`, completion registration/callbacks | Both registrations use flowing `OnCompleted`. Callback-local part completion is inside the runtime's invocation/context boundary. The explicit `TrySet*` publisher supplies one useful returned-call boundary, not the missing supplier or callback-invoker join. |
| `Scenes\PreparedAssetsLifetime.Dispose` / `ReleaseLease` | The last reference clears `_value` before fallible physical disposal. A retained wrapper then need not retain the failed value. A wrapper/root reference alone is not a complete cleanup graph. |
| `Rendering\RenderSceneAssetBundle.Dispose` | Nested `finally` blocks return `_storageReservation` even when an entry release throws. A failed resource graph can therefore lose its funding. Construction's catch/finally ownership needs the same correction. |
| `Scenes\SceneExpansionPlan.Dispose` | Logical disposal is latched before child disposal. It preserves some failed child fields, but a second no-op Dispose is not an acknowledgment that their physical release succeeded. |
| `Entities\WorldSceneStorageReservation`; `World.ReleaseSceneStorageReservation` | The reservation contains bytes/World/workspace-control fields, not the retained-receiver pin required by workspace closure. World-close tolerance in the release helper is not proof that surviving R remains charged. |
| `Scenes\PreparedScene.TryReleasePreparationUse`; `Entities\WorldGuidUseControl.Release`; domain/process release helpers | The root clears its own state before successful lower release. The lower control still accesses its anchor/state after process/domain release, and domain release can publish its retirement Task after removing the operation interest. These suffixes and partial-release ownership require separate proof. Do not restore the superseded claim that the root itself writes fields after successful `use.Dispose()`. |

The active-workspace-to-storage transaction now exists in `WorldAdmissionDomain.TryTransferActiveWorldGuidUseWorkspace`. Do not repeat the workspace proposal's historical assertion that only free-capacity transfer exists. Its presence does not establish receiver survival, complete allocation funding, or final-return safety.

**Source/provenance correction (2026-11-17; original input freeze unchanged):** The workspace proposal's section 3 rollback-rejection item is stale. `WorldAdmissionDomain.cs:2278-2329`, `TryReturnWorldGuidUseWorkspace`, has no `reservation.IndependentUseBytes < bytes` rejection; return adds those bytes. The predicate remains in the transfer methods, where subtraction requires it. This specific rollback defect is not an open prerequisite. The domain file still matches its original hash, so the return-method bytes are unchanged. The regression's **whole test file does not**: `MeasuresSceneStorageReservationConstruction` was added after the original snapshot.

`SceneWorkspaceTransferRollbackReturnsPreviouslyTransferredCapacity` exercises P=4096, C=3072 and residual 1280, then verifies restoration. The newer [Debug](..\..\artifacts\tst\Debug\m03b-scene-owner-measure-debug-20261118.trx) and [Release](..\..\artifacts\tst\Release\m03b-scene-owner-measure-release-20261118.trx) TRX each record 16/16 executed/passed, including that regression and the added construction measurement. These existing records were inspected, not rerun; they do not qualify receiver retention. The original digest remains historical. **Require a fresh, complete source/test/evidence freeze before any disposition**; these newer observations neither replace that digest nor establish equality with its input set.

## Verified cap contract/implementation mismatch

The pending bytes implement **two separately admitted envelopes selected by one option**, not just the whole-preparation cap described by the maintained diff:

1. `SceneLoadOptions.MaximumPreparationBytes` has XML documentation saying “Scene preparation and per-activation byte budget”; its default is `67_108_864`, valid range `[1, 2_147_483_648]`.
2. Freeze copies the selected value into `SceneLoadOptionsSnapshot`; `PreparedScene` stores it independently of remaining workspace.
3. `CaptureActivationHeader` returns that captured P without borrowing the body. `SceneLoader.ActivateAsync` passes it directly to `_world.AcquireGuidUse(activationWorkspaceLimit)`, before queue admission. Each activation attempt therefore independently requests `256 + P`, while preparation/retained storage may still be charged.
4. The **current diff** in Chapter 16, section 2, calls the option an “explicit per-operation whole-preparation cap.” Chapter 30's added declaration and prose at the Scene-loading description/completeness table describe `PrepareAsync` selecting W; they do not specify that this option also selects every activation attempt's W. The surrounding statement that activation has a separate use does not define its byte selection.

[Budget accounting, section 9](m03b-scene-preparation-budget-accounting.md#9-concrete-tentative-rectification-captured-cap-and-funded-delivery-tail) explicitly proposes `W_a = P`. That explains the code, but a tentative proposal and source XML do not silently amend Chapters 16/30. This is a verified contract/documentation gap, not evidence that the option is currently preparation-only in code.

**Recommended later reconciliation:** retain that existing tentative selection, with separate preparation and per-attempt activation admission, unchanged P/default/range, and unchanged activation delivery-tail ownership. Before claiming contract conformance, explicitly reconcile the option's dual role in its maintained owner/public descriptions under a separate authorized change. This task changes none of those descriptions. Do not “repair” the mismatch with zero-cap activation, shared release rights, a smaller adaptive P, or a new option.

**Distinguishing static example:** with fully covered `C = R` transfers and no intervening release, preparation plus one activation reserves `2*P + 512` above the earlier baseline, counting transferred R once. At the default this is **134,218,240 bytes**. A cheap activation can refuse solely because its independently selected P will not fit. Preparation success does not promise activation headroom. This is derived accounting, not an executed observation.

## Additional finite-K evidence: native capture is not just an EDI holder

The inspected local reference CoreLib is RC `11.0.0-rc.3.26522.148`, product commit `5254d8bcf59917edc1e1abc33bdf2442b77b1c70`. The public source below was fetched at that commit and inspected read-only. This identifies source semantics for that reference lane, **not the CLR loaded by a future testhost**.

| Pinned source under `src/runtime` | Relevant allocation/work |
|---|---|
| [`src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/TaskExceptionHolder.cs:145-187`][holder] | Single/enumerated exceptions are captured into new EDI objects; an existing EDI can instead be stored directly. These are different paths. |
| [`src/coreclr/System.Private.CoreLib/src/System/Exception.CoreCLR.cs:239-249`][exception] | `CaptureDispatchState` calls `ExceptionNative_GetFrozenStackTrace`. Managed fields alone do not describe that operation's allocation. |
| [`src/coreclr/vm/comutilnative.cpp:83-118`][native] | Capture first calls `ExceptionObject::GetStackTrace`, then marks the returned stack frozen. Looking only at the subsequent freeze misses preceding work. |
| [`src/coreclr/vm/object.cpp:1629-1738`][object] | A nonnull stack whose recorded thread differs from the current thread enters `GetStackTraceClone`. It allocates a new stack at the **old capacity**, copies/scans frames, and can allocate a keep-alive array at the **old array capacity**. |
| [`src/coreclr/vm/object.h:1875-1908`][object-header] | `GetObjectThread` returns the recorded thread; freezing stores the sentinel `(Thread*)1`. “Already frozen” is not a source-level exemption from that mismatch test. |

Consequently, a fixed number of original exceptions does not establish a useful fixed all-path K. The new clone bytes and copy/scan work depend on preexisting stack/keep-alive capacities. Retaining the foreign exception graph is distinct from paying for newly created copies. This source proves an input-dependent allocation path; it does **not** establish mathematical unboundedness of CLR objects, a numerical maximum for this product lane, or that a particular Scene execution took it.

The first finite-K work item is therefore exact and bounded: follow those two allocation sites and their callers, establish their admissible capacities/multiplicities/coexistence, and identify a real **before-allocation** grant. Include the current child `GetResult` fault/cancel rethrows and the two-original-exception public publication path. Do not run live preparation to discover the amount.

If existing admitted inputs cannot yield a useful bound fitting P, a larger fixed K is not the remedy. A fulfilled runtime-owner solution is required: either budgeted capture with a legal preserved-fault disposition, or a non-copying relay of already captured dispatch state with explicit lifetime ownership. The latter must preserve the original exception identities/order and selected token; it still owes the destination's finite holder/list/publication provision. Current public `TaskCompletionSource<PreparedScene>.TrySetFromTask` requires a `Task<PreparedScene>`: the planner's `Task<SceneExpansionPlan>` and resource `Task<IDisposable?>` do not supply that capability. Fault extraction followed by `TrySetException` is not a no-copy relay.

Neither capability is asserted to be installed, and this document introduces no API declaration for it. In particular, denial of a dynamic capture claim must not silently replace the required original fault, recursively attempt the same unfunded capture, or report a successfully completed Task. If a legal funding/relay implementation cannot be specified, this precise publication boundary remains blocked. The external-observer dispatch rider does not cover it.

## Selected return-ownership implementation route

Do not extend the current candidate by clearing the supplier latch in a completion callback. Replace the unowned Scene invocation boundary rather than trying to observe it afterward.

**Tentative selection:** use operation-specific, explicit non-async progress state in the existing planner/builder and Rendering construction owners, driven by owned ordinary invocations. Retain the current public Task surface and real Assets I/O. This is an extension of the existing staged Scene driver, not a reusable task-like type, method-builder framework, scheduler, worker, ambient grant scope, or second planner algorithm. Chapter 02's async-abstraction restriction must be assessed against this concrete scope before implementation; it is not silently waived.

The required construction/invocation sequence is:

1. **Preclaim the call, then construct it.** From the exact existing issuer, claim the whole cursor/callback/context/registration peak before creating it or invoking a helper. Replace self-funding captured delegates with the workspace proposal's explicitly prepaid funding port. Keep one stable root and its original preparation use. A port alias conveys no use-disposal right.
2. **Make the actual Scene-owned supplier closure explicit.** Convert the ordinary planner's counting, selector/cache, root-count-fill and expansion suspension points, not only its outer Task methods. Do the same for the real resource adapter, bundle, Atlas/page, Material/texture and Mesh/Skeleton chain. Reuse shared synchronous semantic steps with restore/measurement; do not duplicate traversal or allow null-as-unlimited funding. Restore/measurement retain their own actual Persistence issuer, not public preparation P.
3. **Separate lower readiness from owned invocation return.** Stage returned values and partial leases in the existing rooted owner before fallible validation/attachment. A pending lower Task may supply readiness only. Any Assets machinery excluded from Scene X must have a named, independently funded Assets lifetime and no remaining Scene-grant access; decoded-byte/lease-count checks alone do not establish this. Otherwise that call envelope remains an unresolved prerequisite.
4. **Own every Scene resumption, including the root's two Actions.** An ordinary outer thunk must enclose the complete semantic step and its context scope, then clear the step's context/awaiter/task/input aliases, and only then hand back that invocation. Initial `AsyncTaskMethodBuilder.Start` already provides a usable initial context-return boundary; it is not a resumption solution. For resumed work, a non-flowing wake plus explicitly owned context invocation is a candidate implementation, **not** a mechanical `OnCompleted`→`UnsafeOnCompleted` substitution.
5. **Specify context before choosing registration.** Prove initial isolation, nondefault/default/suppressed ExecutionContext, AsyncLocal notifications/reentrancy, changes made inside a step, restoration, and lack of arbitrary caller SynchronizationContext capture. Public `ExecutionContext.Capture()==null` under suppression is not the stock builder's internal sentinel. Every supported branch needs a concrete public mechanism and funded holder; no unowned default-context bootstrap or late ambient lookup is assumed.
6. **Join registration and invocation separately.** Reserve the exact pending registration/role before exposure. Retain the registering invocation through registration return even if its callback runs first. Serialize cursor mutation and prevent clearing/reusing a slot while an older invocation can access it. Derive the number of coexisting invocations from the actual control flow; “one pending asset read” does not bound registration-return overlap. Uncertain registration failure retains the original obligation.
7. **Hand back only as the final owned operation.** After all context/publication scopes return and owned aliases end, the invoker transfers the exact original claim/role once to the root, outside gates. Its remaining managed/native suffix must neither access the retired grant's state nor allocate Scene-owned storage, including exception/tracing paths. Prove the enclosing raw Task/ThreadPool suffix as well; moving the signal into another Action does not close it. A branch without that proof stays unsupported, not normally quarantined.

This route removes Scene-owned compiler boxes rather than decorating their still-opaque invokers. It is not yet a qualified cursor implementation. A final compiled graph retaining any such Scene-owned async box must either supply its own actual owned-invoker proof or fail this route's closure. Standard Task terminality, source `finally`, GC, elapsed time, `WhenAll`, another observer Task and profiler method-leave events remain insufficient.

## Cleanup and final-return sequence

Close these lower ownership prerequisites before integrating the producer; they can be implemented and checked through the real lower owners without executing the unavailable Scene candidate:

1. **Keep failure roots before fallible work.** Each plan/bundle/lifetime retains its value, loaded-entry counts, current cleanup item and exact grants before acquisition/release. Clear only a successfully released/transferred item. Independently safe siblings may clean; retain failed items and bounded debt. Logical disposal prevents reuse/retry but does not acknowledge physical cleanup. Correct the currently early `_value` clearing and reservation-returning `finally` paths.
2. **Keep R independent of preparation.** Implement the [bounded lower-owner protocol below](#retained-r-bounded-lower-owner-protocol) before X→R transfer. Preserve active-claim transfer and exact rollback provenance. A retained bundle or unused prepared result surviving World disposal must retain its own R/root; preparation U cannot be extended or reacquired as a substitute.
3. **Make lower release an owned terminal transaction.** Before removing the final operation funding/root, finish or validly transfer all control-field clearing, deferred retirement, domain-notification publication and failure bookkeeping. Use existing process/domain/retirement owners in their established lock order; no callback, completion or allocation runs under their gates. If another existing owner assumes a suffix, identify its original grant, actual transfer transaction and final end. The control's 256-byte charge is not automatically funding for a new retirement mechanism.
4. **Record irreversible progress, not an imagined atomic drain.** Process release can succeed before domain release fails. Preserve the still-live domain anchor and exact remaining interests/debt; do not recreate a process U, retarget a successor, or refund remaining claims. A failure after *both* roots were removed cannot be repaired merely by calling `Quarantine`; its safe retention must be arranged before that point.
5. **Release the preparation once.** Require returned public publication, ended ordinary parts, every invoked supplier/invoker returned or validly transferred, and actual body cleanup/installed transfer. An absent resource role is unstarted, not returned; a selected null-result role still owes return. Quarantined retained storage is not body Ended. Only then elect one final releaser. Preserve the current root's successful no-root-access-after-lower-release ordering and qualify the lower transaction's own suffix.

Body disposal/admitted-body return and supplier return remain separate facts. Queue refusal ends neither preparation body ownership nor its producer claim. The activation request returns its delegated body through the exact root; its separately funded activation publication tail is not a source of preparation funding.

## Retained R: bounded lower-owner protocol

**Supplemental current-source proposal, not part of the original freeze or an implemented pin.** Observations below were checked at HEAD `cf4d53518de0c31663a200f9781062654f30d614`, including the dirty source, on `2026-11-17T21:22Z`. The earlier rollback and test-provenance corrections remain applicable. The original `05fe8c05...` digest is preserved, not refreshed. A fresh complete review freeze is required before any implementation/qualification disposition.

### Current boundaries and selected owner disposition

The four named sources still have these concrete gaps:

- `WorldDomainReservation.Dispose` delegates to `ReleaseWorld`; that operation removes the **whole** World interest and marks the reservation released. There is no separate admission-closed/data-live state.
- `WorldSceneStorageReservation` retains a World and an optional workspace use, not a domain-rooted retained segment. Its release reenters the World; after World disposal, `ReleaseSceneStorageReservation` tolerates an already-released domain reservation rather than proving R remained charged.
- `World.Dispose` attempts lineage retirement before disposing its domain reservation. Its outer `finally` still releases restore/quarantine resources and writes `_isStructuralOperationActive` afterward. The close invocation itself therefore has a remaining funded tail.
- `WorldAdmissionDomain.TryReserveWorld` creates an `InterestKindWorld` entry but does not bind that entry's `Reservation` reference to the subsequently constructed reservation. The field already exists; a receiver list in an otherwise unrooted reservation would not complete the root chain.
- `_scenePlanStorageByteReservation` initially includes unassigned restore hierarchy workspace. It is not, without a new partition, the sum of installed receiver segments.

**Select the conservative existing-owner route:** retain the original revoked `WorldLifetimeControl` and its original `C_o` while retained R or receiver construction is outstanding. Do not invent a history interest, transfer C_o to a new data owner, or acquire a preparation/independent use to pin R. This reuses Chapter 12's retiring-World ownership through actual storage cleanup.

The consequence is explicit: the original process binding-owner O/U/K also remains retained, without application authority; **same-WorldGuid reuse is delayed until that storage and the other original retirement obligations end**. This is a proposed owner disposition requiring review, not a claim about current R behavior. If the required contract instead allows the process binding to end while R survives, this route does not supply that contract: an exact C_o/data-owner handoff must be reviewed separately. Do not silently drop lineage protection or add an uncharged interest to obtain earlier GUID reuse.

### Exact accounting and required field funding

Use the following accounting notation, not new API fields or numerical allowances:

```text
R_i = receiver i's identified data/reserved capacity
      + its complete receiver-control/retirement provision
S   = sum of all linked, unreleased R_i, including failed-release segments
C   = World-funded, uninstalled construction/prospective capacity
H   = qualified shared reservation/lifetime/close-driver control provision
A   = other still-owned ordinary World capacity
B   = WorldDomainReservation.Bytes = A + H + C + S
U   = WorldDomainReservation.IndependentUseBytes, separate from B
this World's domain attribution = B + U
```

Use-funded workspace/construction remains inside its original U, not also C. Unassigned restore capacity belongs to A until an exact partition moves it to C or S. After successful ordinary World cleanup, A may end; **B remains `H + C + S`**, while U is unchanged. An unrelated cleanup quarantine is not proof A ended.

Necessary state belongs in the **existing** two reservation carriers: parent admission/data/close-progress state, intrusive receiver head and exact S/C totals; receiver exact parent identity, immutable settled charge, lifecycle, intrusive predecessor/successor and opaque retained-graph reference. Retain the original lifetime/root identity through the existing `WorldLifetimeControl`. These are representation requirements to qualify, not assertions that those fields exist or fit unused object space. No receiver invokes the opaque higher-owner graph, and no new allocating collection, per-node Lock, worker or callback list is needed.

Before construction, independently derive the **final whole managed extents**, headers/alignment, any supported lazy state, and retirement/failure coexistence. Preclaim the receiver-control provision within R_i before `new WorldSceneStorageReservation`; include it once, not as both a hidden extra and part of R_i. H must retain whole shared carriers still accessed, including the World close carrier through its last access. Identify its covered slice of the existing World control admission and prove coverage before reclaiming the remainder. `FreshWorldControlBytes = 4096` is an existing aggregate grant, not proof of available padding or a derived H.

The newer TRX record a **48-byte warm synchronous allocation delta** for the current unextended reservation constructor on the RC x64 runtime. That is component evidence only: it neither sizes the proposed linked carrier nor pays for parent state, retained graphs, lazy work or the close tail. **No final receiver-control charge or H is established here.** If the final layout cannot be covered by the existing admitted grants, that exact control-funding contract is a blocker; do not install a guessed overhead or silently raise a quota.

### States, root chain and lock order

| Existing owner | Proposed state/transition | Funding and authority |
|---|---|---|
| World domain reservation | Open → admission closed / World cleanup running | Keep `_domain`, interest generation, B/U and all roots. Refuse new growth, uses and receiver installations after the close linearization point. Closure is not release. |
| World domain reservation | Cleanup running → data live | Only after ordinary cleanup succeeds, return identified A. Retain `H+C+S`, the original retirement owner, and pending close-driver work. This normal deferred state is not quarantine. |
| World domain reservation | Data live → original-owner retirement eligible | Require `C=0`, `S=0`, successful body cleanup and the close invocation's actual last-access acknowledgment. Elect one ordinary retirement caller; do not wait under a gate. U may still require the existing deferred-use retirement. |
| World domain reservation | Retirement pending → released | Require original C_o/binding retirement acknowledgments, `U=0`, no receiver/construction/cleanup debt or live close/return part. Clear parent/root links at their last funded access, then end its remaining interest/H once. |
| World domain reservation | Actual cleanup/return failure → retained failure | Preserve the exact graph, linked segments and irreversible progress. Existing whole-reservation quarantine is not an independently derived disjoint-byte partition; retain its unresolved charge rather than subtracting an assumed A. |
| Scene storage reservation | Uninstalled / workspace-owned → linked R | One exact installation transaction moves its identified original claim into R and links the same object. Workspace-only storage is not a data pin. |
| Scene storage reservation | Linked R → release elected → disposed | Only the actual owner may acknowledge ended/transferred data consumption. Validate and unlink once; no newly admitted credit or caller GC is involved. A duplicate/foreign attempt cannot subtract another segment. |
| Scene storage reservation | Failed release → linked retained failure | Keep its graph, R_i and exact phase/debt; do not make a second no-op Dispose count as physical release. |

Bind the existing occupied World interest to its exact `WorldDomainReservation` before that reservation escapes. The data root is then **domain → exact World interest → parent reservation → intrusive receiver → opaque retained graph**, with the original lifetime/lineage retirement owner also retained. Link membership and interest/root generations, not GUID equality, authorize release. Ordinary raw domain reservations not enrolled in a World retain their existing non-receiver semantics.

Keep the domain attachment valid throughout data-live state. Split exact live-interest validation from permission for **new admission** at the existing growth/use/install callers; do not make `IsOwnedBy` false at logical close. Exact original cleanup/provisional-return paths use the former, not an open-admission requirement. Once B shrinks to `H+C+S`, `InitialBytes` is historical bootstrap information, not a valid closed-state byte floor or rollback allowance.

The authoritative S/C ledger must live with the parent/domain, so a post-close release does not need a World callback, participant lookup or owner thread. Reconcile or replace the World's current Scene-byte mirror against this ledger; do not maintain independently spendable copies. A live `GetWorldOwnedStateBytes` calculation must also include World-funded prospective C and unassigned restore capacity exactly once. U remains separately checked in common headroom.

**Lock order:** live World enrollment/close takes `_sceneStorageGate` before domain `_growthGate` then `_gate`. Existing persistence/owner-control gates used to latch disposal are exited before acquiring the domain pair. Receiver-only link/unlink/return uses only the domain pair, never reenters a World, lifetime gate or process registry. Original lifetime/process retirement runs after dropping that pair; its existing process-before-domain ordering is preserved. No reverse domain→scene/lifetime/process acquisition, allocation, physical cleanup, callback or Task delivery is permitted in those transactions.

Take/revalidate indices and generations, not refs into interest arrays across gate release. Existing `using var growthScope` in domain close/release paths can span the later `TrySetResult`; dropping only `_gate` is insufficient. End **both** scopes before any retirement Task publication. The domain's own publication/control tail retains its separately proved funding; R is not its emergency allowance.

### Construction, installation and rollback

1. **Fund without self-allocation.** An ordinary use-backed request preclaims the complete next R_i/control construction peak through the existing scalar issuer before constructing the carrier. If starting from free F, move the claim into active X first; the existing active-X transfer can settle it later. An already active workspace reservation does not acquire a second claim. Direct World/restore routes must identify their existing admitted World or preflight funding instead; no null-as-unlimited path is valid.
2. **Keep an uninstalled attempt owned.** A direct World-funded attempt records its exact pending C/constructor interest before leaving the gates; a use-funded attempt keeps its original X/U. Its unescaped ordinary caller owns the construction guard, with no escaping token or new guard object. Close cannot refund that pending capacity while the constructor runs. Allocate/initialize outside the scene, growth and domain gates; there is no materialized payload yet.
3. **Install by one value-only transaction.** Revalidate exact parent/interest, participant selection at the live World boundary, open admission and the original claim. For active X, atomically perform `B += R_i`, `U -= R_i`, original active claim/operation charge `-= R_i`, `S += R_i`, link the receiver and commit its state. For already prepaid direct/restore C, move `C -= R_i; S += R_i` without another B/domain charge. Root the receiving construction/data graph before transferring its materialized capacity; an empty prospective reservation owns only its stated unspent capacity. No payload acquisition may escape solely through an unrooted local.
4. **Do not allocate after settlement to finish constructing the control.** The carrier exists before the X→R/link commit, so its constructor cannot fail after the transfer. Any subsequent fallible attachment preserves the staged receiver/graph; it is not permission to reverse an installed data transfer.
5. **On failed construction or lost admission, return only the original uninstalled grant.** Constructor failure has no linked R. After unwinding, cancel that attempt's exact C, or return newly claimed X to its original F; a refused transfer of an existing workspace reservation leaves that original X owned. Close-first refuses installation and permits these exact returns after closure, without new admission. Failed rollback retains its original parent/use anchor and debt.

This sequence avoids release-then-reacquire of live capacity and removes the current allocate-after-transfer constructor window. The existing arithmetic rollback correction remains **closed**: `TryReturnWorldGuidUseWorkspace` has no independent-use lower-bound predicate, and P=4096/C=3072/residual 1280 passes in the newer Debug/Release records. That helper's successful uninstalled-transfer rollback is not authority to return an already linked receiver's R to P. A normal R disposal never replenishes the preparation cap.

### World close, lineage and last-receiver release

1. **Close admission, not data.** Preserve the current owner-thread, callback and structural-exclusion checks. Serialize the World disposal latch with parent admission closure under the lock order above. Any earlier accepted constructor/use retains its original credits; later receiver installation/growth/new-use admission refuses. If binding revocation fails before cleanup, reopening is legal only for the same exclusive close attempt and still-active exact binding, using current ledgers, not restored old totals. Otherwise retain the closed failure; do not claim the World remains active.
2. **Clean ordinary state outside gates.** Return only capacity whose actual consumers/cleanup ended. Do not infer the released amount from cleared fields or refreshed headroom. Keep C, S, U and the whole required H. A close racing the last R must retain H until `World.Dispose` has also finished its restore/quarantine cleanup and final World-field writes; a final ordinary handback needs a proved harmless suffix, not an acknowledgment inside an earlier `finally`.
3. **Defer C_o retirement while R/construction remains.** The original lifetime retains its existing lineage grant and revoked binding. `TryRollbackFreshLineage` must not clear that root merely because `HistoryInterestCount==0` while these data obligations remain. Route World teardown and construction/unwind end requests through the same owner condition; `WorldConstructionPlan.ReleaseUntransferredRestoreResources` is an existing bypass to account for. A deferred data end is normal success, not the current `lineageRetired=false` failure/quarantine branch.
4. **Keep data-wait distinct from the existing independent-use wait.** While waiting for R/C, the lifetime must not report the existing `IsRetirementPending` condition as though the last U could complete it: `WorldGuidRegistry.TryReleaseIndependentUse` uses that condition to invoke `CompleteDeferredRetirement`, and a normal “still waiting for R” must not become a failed-use quarantine. The original owner can remain revoked until R/C and the close driver end; then perform C_o retirement and the existing process-retirement handshake outside the domain gates.
5. **Unlink an ended receiver exactly once.** A storage-credit Dispose is not itself proof that a plan/bundle or its cleanup epilogue stopped. Its actual owner must end/transfer all R-funded graph accesses before this final handback, with a harmless caller suffix or a specifically funded remaining owner. Under the domain pair, validate the exact parent, state, charge and neighbor links; clear the retained-graph/parent/link state at the valid final access, unlink, decrement S and B/domain bytes by precisely R_i, and leave all U/other segments untouched. Prevalidate checked arithmetic before mutation. The ordinary success suffix must not write/read the retired receiver or allocate Scene-owned storage. On an uncertain partial return, retain the exact remaining owner/debt rather than resetting to retryable.
6. **Let the last eligible ordinary caller finish the original owner.** The caller returning the last R/C, the World close driver, or the already owned last-use path can elect progress; no new worker/Task/list is needed. Finish C_o exactly once. If W_u/history interests remain, use the existing `OwnerRetired`/history-count behavior: those interests, not the freed receiver, retain the root until their own exact release. If process retirement waits for U, retain the parent/H and finish through its real later acknowledgment. Do not rerun an already completed lineage retirement or recreate a released U.
7. **Finish the parent last.** With C/S/U and cleanup/close parts zero and actual original-owner retirement acknowledged, clear its remaining links/ownership state before the final interest/H refund. No later parent/receiver access may rely on that ended grant. Only then can that interest stop delaying domain retirement. Closing the default domain remains the original owner's existing one-time authority; old replacement retirement never closes the successor's domain, and receiver release never acquires close authority.

Already accepted X→R transfer after **external domain** closure remains distinct from installation after **World admission** closure: the former may settle original prepaid capacity while its exact World remains open; it does not authorize fresh growth. The latter is refused by this protocol. Cleanup/return against original claims remains legal after both closures.

### Focused lower-owner observations and exact open obligations

Future tests use real Worlds, registered Scene participants, real independent uses and these reservation methods only—no `SceneLoader.PrepareAsync`, fabricated plan, fake supplier or production failure switch. Extend the existing lower-owner test areas together in Debug then Release:

| Case | Required distinguishing assertion |
|---|---|
| Two or more retained receivers, no preparation use left | Dispose the World, close an external domain before/after it, release head/middle/last nodes out of order and repeat Dispose. After successful ordinary cleanup, the exact World interest remains with `H+C+sum(R_i)`; each release removes only its own R_i. Domain retirement remains pending until all real interests/tails end. |
| R/U release permutations | End U before the last R, and the last R before U. Verify no premature C_o/root-generation release, no data-wait-induced use quarantine, no U bytes included in the baseline refund, and exactly one final original-owner/parent end. |
| Revoked owner and same GUID | With the original domain still open, verify new binding admission is refused while retained R keeps the old revoked owner, then succeeds after its actual final retirement. A closed domain must not be used as the reason for the expected refusal. This verifies the selected conservative disposition, not a historical test result. |
| Reached control/transfer capacity | Derive the final control charge first. Exercise exact and one-byte-short constructor peaks with all setup funded; deny before control allocation. Cover free→active→R, existing active X→R and direct World funding, with B/U/S/C conservation and no header double charge. Preserve the verified 4096/3072 rollback case unchanged in meaning. |
| Close versus constructor/install/release | Race World-owner-thread close against real reservation calls/releases. Close-first returns only that uninstalled original claim; install-first remains linked/charged until actual release. Verify both reached orders and exact parent/generation refusal without touching another World. External stops at real sites may establish an otherwise unobserved interleaving; absence of a reached order is not a pass. |
| Construction/release failure | A capacity refusal is not an actual constructor OOM or physical-cleanup failure. Verify ordinary refusal rollback now; leave real constructor/partial-release failure observations open if no safe stimulus exists. The static protocol must still retain exact funding/roots on those paths, without adding a throwing test allocator. |

**Remaining blockers, not invented implementation facts:** final receiver and shared-control layout/funding H; original-owner retirement integration across World close, last U and constructor/restore unwind; a complete disjoint baseline/quarantine partition before any partial refund; and final parent/domain-publication last-access proof. The 48-byte component measurement and 16/16 lower tests establish none of those extensions. The state machine above supplies a concrete conservative route, but **a four-file pin-only patch is insufficient** while those coupled owner obligations are unresolved.

Supplemental observation identities (raw SHA-256; no replacement of the original manifest):

| Current input | SHA-256 |
|---|---|
| `Entities\WorldDomainReservation.cs` | `ea012b9b40acedaa820885eafe27dcd11df55f842b68f2849785dab756e8e3bc` |
| `Entities\WorldSceneStorageReservation.cs` | `965c6afb05a49b80c9e81c6147734de57f7d01a1aa9dd739e777ea7be4940ade` |
| `Entities\World.cs` | `692f721bead29e5ec258bf7b1844ec785e2d1548e9f53dfa9bdb57a22a01f09f` |
| `Entities\WorldAdmissionDomain.cs` | `2ce1c0b90157ade6f82605409ab599f7415b7d8aa0a63a4bf4327263610248d9` |
| `Entities\WorldLifetimeControl.cs` | `7f47be8c4943ce6fc2610fd98d3d2b7470b8f83ea00ec5d9c6c4a3f5c46a9beb` |
| `Entities\WorldGuidRegistry.cs` | `1a6dd92185afe246187c09b09789fc9cddfc195e6c71f9eeb57f264e59092e53` |
| `Entities\WorldConstructionPlan.cs` | `3a6e51a8099482a0fca32f7faac3bd55cfc7c53d233fc88b1bd369dedb32c066` |
| `tests\TerraFX.Runtime.Tests\Scenes\SceneSupportRegistrationTests.cs` | `2728f61bcdaf6ebaaaaf670f8d355e5fb3300700294a27cfa9c1e1cb44937d5b` |
| Newer Debug TRX linked in the provenance correction | `4a7925cd9acf93e920e4e6f5c6a50ee31fee7cb526f3fa0d5a839cc201bca69d` |
| Newer Release TRX linked in the provenance correction | `52a6ebba34fbeb7bdcd2350d076ed9289ba97ba7a1ed52922c6e224b0b27ca3a` |

Abbreviated Entities paths are under `sources\TerraFX.Runtime`. These identify the inspected observations, not compiled candidate correspondence or a new accepted review freeze. Changed bytes invalidate the affected observation. No new lower-owner tests or implementation ran during this proposal update.

## Finite catalogue, work and enablement obligations

Keep the four ledgers from [footprint closure](m03b-scene-preparation-footprint-closure.md) and supplier X/receiver R from [workspace closure](m03b-scene-preparation-workspace-closure.md). No numerical J/K is established here.

| Partition | Required proof before use |
|---|---|
| `J_shared` | Whole final root/control carrier and supported synchronization/lazy state, retained through the producer/body/release join. Old constructor-only measurements do not identify a changed layout. |
| `J_producer` | Final fixed driver, TCS/Task, callbacks and loader-owned invocation/registration/context storage. Distinguish borrowed immutable input references from newly created holders/copies. Include cold and overlap paths. |
| `K_body` | Protected cleanup/return capacity, bounded original-cleanup sampling and exact failure debt; actual retained failed graphs remain on their original X/R grants. Include the cleanup invoker's final work. |
| `K_publication` | Exact success/fault/cancel/refusal overload graphs, source-array/enumeration/copy/list/EDI/native capacity, cleanup overlap and publication/release failure. The native clone row above must have a real disposition, not a guessed constant. |
| Variable supplier X | Every cursor/traversal/cache/helper/call/registration capacity before construction/growth, using admitted input dimensions and actual simultaneous invocation count. Do not bury this graph in fixed J or “two suppliers.” |
| Retained R | Only identified receiver-owned capacity plus its funded retirement control; not counting scratch, publication work or an ended preparation use twice. |

Every row records creating site/type, checked capacity/multiplicity, independently derived bytes, copy/scan/cleanup work, coexistence, original issuer/grant, rooted path, last access/transfer and the handback's own suffix. Enforce `P = F + X + T`; remaining preparation operation charge is `256 + F + X`, with T cumulatively transferred capacity. Each newly materialized capacity is claimed before allocation. Returning a live claim to free F and reacquiring it as R is not a transfer.

J/K are byte provisions, not a newly invented timeout or CPU-work cap. Prove finite engine-created iteration/registration/cleanup demand from admitted counts and claimed capacities, including native input-dependent copying. Do not infer this from finite sampled allocation totals. External scheduler/notification code can block indefinitely; that supplies no refund or finite-time guarantee. Any ownership exclusion for that code must be explicit and must not exclude Scene-created invocation/restoration holders.

**Ordered enablement:** resolve the cap owner selection and native-capture funding/relay disposition; implement lower cleanup/receiver/terminal-return ownership; compile the actual owned-progress candidate with the guard still closed; freeze its complete source/compiler/runtime/native closure; derive and install its real claims; only then execute funded integration. A missing finite native row, lower owner or post-last-access edge is a hard stop, not authorization to install a provisional constant.

### Required future observations

Use ordinary owning-project builds and focused NUnit runs, Debug before Release, as described in `docs\project\Building.md`; confirm fresh nonzero selected/executed TRX results. The existing `PreparedAssetLeaseRetainsBundleAcrossActivationUnloadAndDispose` source contains an **activation** scheduler hold, nonempty binding and pixel-17 retention witness. It is not an executed preparation/invoker-return test in this assessment.

- Hold a real supplier after outcome readiness but before its owned return, and separately hold a root completion thunk before context/registration return. Body disposal or completed activation must not refund their original grants. Release the stops and observe one joined refund.
- Hold public `TrySet*` dispatch after terminal visibility. End/transfer the body while shared/producer/publication provision stays charged. Keep registration-before-return and public-promise-escape cases distinct.
- Reach catalogue-derived exact/one-byte-short claims with earlier setup funded. Cover cold/warm, actual synchronous/suspended, no participant/selected null-result/real-resource, original fault plus cleanup fault, selected-token cancellation, uncertain registration and post-close cleanup. A bootstrap refusal is not a later native/cache/resource claim witness.
- Demonstrate retained R surviving World close, final-lease release, and actual cleanup/return failure retention where safe real stimuli exist. Unsupported stimuli remain open; do not add a throwing fake supplier, synthetic OOM, test-only yield or fabricated Scene.
- Verify the selected dual-cap behavior separately: each attempt admits its own unchanged `256 + P`, queue refusal preserves the preparation, and held preparation/activation tails remain independently charged.

Allocation-complete corroboration remains subject to the existing [evidence-capability proposal](m03b-scene-preparation-allocation-evidence-capability.md); sampled GC ticks are not a substitute. Bind the actual loaded CLR and final native code versions, not just SDK/muxer versions. Other runtime/instrumentation modes, actual public consumers, NativeAOT and Chapter 35 acceptance remain separate, mandatory where applicable.

## Scoped input identity and limits

Snapshot: `2026-11-17T20:35:11Z`, HEAD `22d9e48f9cf028d4b52033b8411f91c80d930691`. The 59 direct local inputs are the following exact set; brace lists expand only the listed names. SHA-256 is over sorted repository-relative Git-style paths, each followed by NUL, lowercase raw-file SHA-256 and LF, UTF-8 encoded. Set digest: **`05fe8c05f0ea0f36b9060a8eedc30b650511bf47f3041f6a106e24a453249f10`**.

```text
.github\{copilot-instructions.md,workflows\ci.yml}
global.json
{Directory.Build.props,Directory.Build.targets}
sources\{Directory.Build.props,Directory.Build.targets}
tests\{Directory.Build.props,Directory.Build.targets}
sources\TerraFX.Runtime\TerraFX.Runtime.csproj
tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj
docs\project\{Building.md,Roadmap.md}
docs\design\{02-dotnet-api-memory-and-ownership,04-capacity-and-workload-profiles,11-assets-import-streaming-and-packages,12-entities-components-and-queries,13-jobs-and-frame-pipeline,16-scenes-transforms-and-cameras,29-project-setup-build-and-deployment,30-public-api-review-surface,34-engine-acceptance-cases,35-implementation-roadmap}.md
docs\proposals\m03b-scene-preparation-{allocation-evidence-capability,budget-accounting,compiled-producer-evidence-addendum,footprint-closure,handoff-closure,owner-aware-async-handback,supplier-return-closure,workspace-closure}.md
docs\proposals\m05-m03b-owner-contract-closure.md
sources\TerraFX.Runtime\Scenes\{PreparedScene,PreparedScene.Preparation,SceneLoader,SceneLoadOptions,SceneLoadOptionsSnapshot,ScenePrefabExpansionPlanner,ISceneExpansionResourcePreparer,PreparedAssetsLifetime,SceneExpansionPlan,SceneActivationRequest}.cs
sources\TerraFX.Runtime\Rendering\{Renderer.CpuExtractionSupport,RenderSceneAssetBundle,RenderAtlasPageLoader,RenderMaterialTextureLoader,RenderMeshAssetBinding}.cs
sources\TerraFX.Runtime\Entities\{WorldGuidUseControl,WorldAdmissionDomain,WorldGuidRegistry,WorldSceneStorageReservation,WorldDomainReservation,World}.cs
sources\TerraFX.Assets\AssetStore.PackageLease.cs
tests\TerraFX.Runtime.Tests\Scenes\{PreparedSceneAssetRetentionTests,SceneLoadOptionsTests,SceneSupportRegistrationTests}.cs
.dotnet\shared\Microsoft.NETCore.App\11.0.0-rc.3.26522.148\{.version,System.Private.CoreLib.dll}
```

Pinned upstream raw-byte SHA-256 values for the additional native/fault conclusions:

| Linked input | SHA-256 |
|---|---|
| `TaskExceptionHolder.cs` | `bc4dda204de133879e4e334b37f74fc6c57690c05a179ae4648fbfa653a38604` |
| `Exception.CoreCLR.cs` | `44ac1884bcfb3857679bd0865cd9f2eb49192e0196f14c837f2aa1d043f985b7` |
| `comutilnative.cpp` | `13efef9576d111d7f11134055b3745e528f5b072127bb1ff0237eae7874b73da` |
| `object.cpp` | `2f2dc344290dd316a704eede537dfde34288fbc71cab04006ae379796c9f77c7` |
| `object.h` | `a345711a7d7010b4bab264a10f8c1fdd4fde19544d3ecc923ee4f4a19ab380aa` |
| `TaskCompletionSource_T.cs` | `069571daddeffd14ed006b8e552b3541e61b7c79ba7e6a142037e59eadae1369` |

This is a scoped evidence identity, not a repository inventory, transitive runtime proof or acceptance receipt. No new schema or fixture bytes are inputs to this proposal. Current dirty-file bytes, including dirty maintained contracts, are covered by the scoped digest where listed; their inclusion does not accept those changes.

The initial 79 dirty/untracked paths had aggregate digest `cf6d32dbadabe05af2dc591466ea87f8264d8b1e3623c2bbd4b6d05d16b22293` using the same path/NUL/hash/LF algorithm over `git ls-files -m -o --exclude-standard -z`. Before this file was authored, concurrent work had increased that set to 82 paths with digest `bdb45a10252bb5cab7f9b440ffc6f8bb2288c486e610baf083ab0a79bbb04c78`. Those changes were left untouched; no full-worktree equality with the initial baseline is claimed. Recheck the scoped digest before applying these source-specific dispositions; a mismatch requires reassessing the affected input, not replacing the expected digest.

Only this new proposal is authored. No source, test, maintained-contract, roadmap, existing-proposal or schema edit, build/test run, Scene execution, allocation experiment, native/AOT observation, commit, push or remote mutation is part of this task. Earlier proposals' run claims are not imported as current evidence.

[holder]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/TaskExceptionHolder.cs#L145-L187
[exception]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/coreclr/System.Private.CoreLib/src/System/Exception.CoreCLR.cs#L239-L249
[native]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/coreclr/vm/comutilnative.cpp#L83-L118
[object]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/coreclr/vm/object.cpp#L1629-L1738
[object-header]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/coreclr/vm/object.h#L1875-L1908
