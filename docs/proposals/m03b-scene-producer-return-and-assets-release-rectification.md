# M03-B Scene producer return and Assets release rectification

**Status: tentative implementation guidance for later review, not qualification evidence or permission to execute Scene preparation. Keep `PreparedScene.QualifiedFootprint` null.**

## Decision and scope

Finish ownership at the existing Scene/Rendering/Assets boundaries; do not add another preparation root, completion observer, generic async builder, task-like type, scheduler, or worker. Preserve the original preparation use and existing retained receiver. The Atlas/Material progress owners are useful partial-resource roots, **not supplier-return acknowledgments**.

The bounded rectification has two independently actionable parts:

1. Preclaim and retain the actual construction/release graph, and make Assets release preserve its graph and irreversible progress on failure.
2. Admit a return lane only when its **actual ordinary enclosing invoker** proves last access. No such complete lane is established for the current suspended suppliers or flowing completion callbacks. Rewriting them as explicit operation-specific progress is necessary for the previously selected replacement route, but is not itself that proof. Do not start a transitive rewrite on the assumption that an unsafe Task callback supplies the missing final boundary.

The [resource-bundle proposal](m03b-resource-bundle-preconstruction-owner-and-return-closure.md) owns the broader allocation inventory. The [finite-envelope addendum](m03b-scene-preparation-finite-envelope-and-return-addendum.md), [supplier-return assessment](m03b-scene-preparation-supplier-return-closure.md), and [owner-aware handback assessment](m03b-scene-preparation-owner-aware-async-handback.md) retain their unresolved return/native-capture obligations. This proposal updates the next implementation decision for the current bytes; it does not reaccept their historical source/binary freezes or component measurements.

Authorities remain Chapters 02/04/11/16/29/30/35 and `docs\project\Roadmap.md`. Dirty maintained-contract inputs below are inspected context, not accepted amendments. No public declaration, schema, acceptance identifier, quota, or milestone exit is introduced.

## Confirmed present-source facts

Paths abbreviated `Runtime\...` below are beneath `sources\TerraFX.Runtime`; `Assets\...` are beneath `sources\TerraFX.Assets`.

| Boundary | Confirmed fact; remaining obligation |
|---|---|
| `Runtime\Scenes\PreparedScene.CreatePreparation` | The null-footprint refusal precedes `world.AcquireGuidUse`. This assessment did not execute preparation or bypass that refusal. |
| `PreparedScene.StartPreparation`, `StartPlanPreparationCore`, `EndPreparationProducer` | `AsyncTaskMethodBuilder.Start` encloses initial entry/context restoration without requesting its Task. `_supplierProducersUnjoined` is set before planner invocation; the bounded partial files contain no clearing assignment. The three ordinary parts and publication flag cannot end an invoked supplier path. |
| `Runtime\Scenes\SceneLoader.StartPlanPreparation` / `StartResourcePreparation`; `ISceneExpansionResourcePreparer` | The planner still constructs captured funding delegates before its supplier call. The resource entry now passes `ScenePreparationFunding` by value, but still returns only `Task<IDisposable?>`. The actual implementation in `Rendering\Renderer.CpuExtractionSupport.cs` is an async adapter awaiting the async bundle. |
| `Runtime\Scenes\ScenePrefabExpansionPlanner` | Root loading precedes staging the returned Scene lease; allocating builder field initializers precede builder staging. `BuildAsync` reserves after counting. `GetPrefabAsync` holds a newly returned lease locally through validation/cache insertion. The closure still contains nested Task and ValueTask producers, not merely two top-level supplier calls. |
| `Runtime\Rendering\RenderSceneAssetBundle.PrepareOwnedCoreAsync` | For nonempty render demand, the pending-owner election now publishes the empty bundle before its arrays or loads. Current fields retain intermediate leases, arrays and both helper progress objects. This supersedes older descriptions that the bundle/helpers are only late/local owners. **Carrier/call funding still does not precede this construction.** |
| Same bundle, allocation/settlement | The ordinary branch now records scalar ID/Material scratch claims through the funding value. Those formulas do not cover the then-allocated Atlas/Mesh/Material entry arrays or the transitive helper graph. The ordinary branch deliberately throws before retained settlement because its complete charge is unqualified; only the delegate-based nonordinary branch reaches the old final storage reservation. Local `ArrayBytes` still rounds payloads to eight bytes, unlike Chapter 04's sixteen-byte accounting rule. |
| `ScenePreparationFunding`; `PreparedScene.TryClaimProducerWorkspace` / `TryReserveProducerStorage` / `ReturnWorkspace` | Resource subclaims divide the already preclaimed `_producerWorkspaceBytes`; they are not new domain debits. Whole producer return now refuses while scalar claims remain. The storage helper claims a new scalar portion and constructs a workspace receiver through the existing lower preclaimed path; it is not settlement of an identified already-materialized D partition. No qualified producer allowance or return acknowledgment follows. |
| `RenderAtlasPageLoader` / `RenderMaterialTextureLoader`, `OwnedProgress` | Commit `d176842a` supplies typed fields for current leases, builders/copies/results and Atlas validation scratch. The bundle roots these helpers before `RetainOwnedAsync`. They remain async. Atlas tree arrays are still allocated inside `RangeMaximumTree` construction before that object reaches the progress field; queue backing grows normally. No complete preclaim or invoker-return protocol follows from these fields. |
| Bundle/set/plan/lifetime cleanup | Bundle reservations now return after child cleanup, not unconditionally from a child-cleanup `finally`. Its workspace-return helper attempts the scalar claims and reservation owners independently, then rethrows the first failure through `ExceptionDispatchInfo.Capture(...).Throw()`; include that capture in K. `PreparedAssetsLifetime` now clears `_value` only after successful child disposal. Nevertheless, bundle/page-set/texture-set/plan disposal latches and a repeated no-op are not physical-release acknowledgments. A throwing entry loop can leave later siblings unattempted. The wrapper still accesses its fields/gate after nested bundle disposal. |
| `Assets\AssetLease.Dispose`; `AssetLeaseLifetime.ReleaseReference` | The handle clears `_state` before lower release. Final lifetime release decrements its count and clears `_backing`/`_store` before calling store/backing release. Retaining that public handle after a throw therefore need not retain the original value/store/backing graph. |
| `Assets\AssetStore.ReleaseLeaseReference`; `AssetPackageBacking.ReleaseReference` | Store lease/decoded counters change before backing release returns. Last backing release clears `_stream` before `FileStream.Dispose`. `AssetPackageReader.OpenAsync` constructs a private read-only FileStream. Source inspection has **not** established a reachable valid-input close failure or proved its complete platform release closure nonthrowing. Counter-underflow/inconsistent-accounting exceptions are invariant failures, not valid failure stimuli. |
| Other reached Assets release sites | `CreateLease` sets `ownershipTransferred` even if construction cleanup throws; `LoadAssetAsync` performs counter/backing cleanup in `finally`; package-lease disposal, package-pin construction rollback, store disposal and replacement also release references after local/detached ownership changes. Fixing only the public typed handle cannot establish these paths. |
| Existing lower owner | `World.TryReserveSceneWorkspaceFromPreclaimed` and `WorldAdmissionDomain.ScenePreclaimedWorkspace.cs` already provide covered-workspace construction/cancellation without a fresh F debit. `TryInstallSceneStorage` owns the checked X-to-R election. `WorldGuidUseControl.Release` already retains an outer exact-parent return part across its process/domain calls. Do not repropose these as absent or repeat the superseded rollback-predicate/control-write findings. |
| Existing helper tests | `RenderAtlasAssetCodecTests.cs` and `RenderMaterialAssetCodecTests.cs` contain real-package `OwnedProgress` success/validation-failure consumers. Both belong to `SceneAssetStoreTests`. Their presence, and the helper commit, are source facts; no test result is asserted here. |

Two conclusions are **inferences**, not runtime observations: the Task-only surface cannot establish the missing post-outcome return ordering; and retaining an already-cleared lease is insufficient as a failure root. Neither conclusion asserts an observed collection, leak, I/O failure, physical release, or producer race.

## Proposed ownership and ordering

The following are proposed states/roles inside existing owners, not new API declarations.

### Construction, funding and retained transfer

1. **Preclaim through the original issuer.** Ordinary preparation uses exactly `PreparedScene.PreparationControl`. Its bootstrap must cover the whole root, TCS/Task, delegates/context/registration/call controls and protected cleanup/publication peaks before constructing them. Within that purse, claim each next complete helper/carrier/backing before invocation/allocation. Complete the existing resource by-value funding route through the actual helpers; replace the remaining planner captures through `SceneExpansionLimits`. An alias conveys neither a new grant nor use-disposal authority. No Runtime funding callback enters Assets.
2. **Preserve the stable anchor.** Keep `use anchor -> PreparedScene`; do not replace it with a leaf lease or bundle. The root's elected pending construction remains live until commit or full unwind. Preclaim the empty bundle and its actual constructor interval, then use the existing election/commit before its first dependent allocation. Any fallible attachment before publication remains a refusal condition, not an incidental stack root.
3. **Apply the same rule to the actual planner.** Preclaim root-load/call/holder state before `LoadAsync`; install received ownership before a fallible getter or stage transition. Move builder allocating initializers under its rooted construction sequence. Keep a pending Prefab lease owned before context validation/cache insertion. Preserve the existing traversal, selector, cycle, override and package-identity semantics; do not add a second planner.
4. **Finish the helper graph, not just its top-level pointer.** Claim each `OwnedProgress` before constructing it. Store separately allocated Atlas tree arrays before a later fallible constructor/allocation; preclaim the queue's complete chosen capacity. Include builder/copy coexistence and each current lease before validation/insertion. The Mesh/Skeleton helper's retained Mesh and pending Skeleton still need the same owned-progress treatment in the actual bundle path. Clearing a progress field does not end a still-running helper's access.
5. **Close D before receiver settlement.** D is the actual retained bundle/plan graph, including surviving sets, holder backings/carriers, wrapper/Lock, release-state and return bookkeeping not independently funded elsewhere. Scratch, option copies and invocation provision are not automatically D. Preclaim Q for the existing receiver separately; reserve the complete `D + Q` as disjoint covered X before calling the existing preclaimed construction path. Its current Q constant is 72; that source constant is not a whole-graph qualification.
6. **Use, do not duplicate, covered construction.** Elect the exact covered partition in the bundle/root ledger first: the lower active-total check is not a per-subclaim ledger and does not authorize two receivers against the same X. Extend the root's current storage helper to consume that exact already-claimed partition rather than taking another scalar subclaim for live D. Construct outside gates, retain the exact receiver in the bundle, and set its opaque retained owner before `TryTransferWorkspaceToStorage`. On construction/install refusal, cancel only its constructor interest; the preclaimed path deliberately leaves original live X owned. Return unused Q only after its construction consumers unwind. Do not dispose an uninstalled receiver or refund live D to F to manufacture transfer capacity.
7. **Attach the existing lifetime before closing its funding.** Split `SceneExpansionPlan.AttachPreparedAssets` into preclaimed wrapper construction and installation of that same wrapper, rather than allocating another wrapper after settlement. Through the existing internal Scenes/resource-preparer seam, bind the exact receiver's terminal-return authority to the existing lifetime and give the receiver-rooted graph a retention-only edge to that wrapper. This edge conveys no recursive disposal authority. Include the whole wrapper/Lock and required join state in D before transfer. Failed attachment leaves the pending bundle and constructor inputs with the original root; acknowledgment follows successful installation.
8. **Settle once.** Reuse the lower value-only X-to-R transaction with `R = D + Q`: X decreases by R, transferred capacity increases by R, parent B increases by R, parent U decreases by R, and F/domain total are unchanged. Only the installed receiver independently roots R. Return scratch after its actual last helper/invoker consumer, not merely after assigning empty arrays. Receiver disposal never replenishes preparation P.

Use the existing accounting `P = F + X + T`: F is free purse, X active/protected claims, and T cumulatively transferred capacity; remaining operation charge is `256 + F + X`. Preserve disjoint `J_shared` (whole root/shared controls), `J_producer` (promise/invocation controls), `K_body` (protected cleanup) and `K_publication` (protected terminal delivery). The current scalar producer purse must not make live fixed promise/invoker provision spendable by scratch; derive its separate ranges and coexistence before assigning an amount. Embedded fields are charged in their whole carrier once, not again as scalar K. Body cleanup and publication may overlap.

**No numerical D, J or K is established.** The final field set changes their derivation. No current `256 + entry arrays`, measured root constructor, lease count, historical byte observation, or fraction of P is an acceptable replacement. Preserve the earlier proposals' external-observer provenance rider only in its stated scope; it does not exempt Scene-created registration/context/capture work.

### Body close is not producer or receiver return

Keep these conditions independent:

| Existing owner | Proposed progress and terminal condition |
|---|---|
| `PreparedScene` producer | Role elected before invocation -> result/fault ready -> actual supplier return proved; independently join registration return, callback-invoker/context return and public publication return. Readiness does not advance the return state. |
| Plan/bundle/helper | Building -> ready/transferred; close may be requested meanwhile. Cleanup is eligible only after all semantic users end or explicitly transfer to the same funded owner. A helper still running cannot race its fields' cleanup/reuse. |
| `PreparedAssetsLifetime` / retained bundle | Owner/lease close requested -> last reference and construction/invocation users joined -> physical cleanup completed, or genuine failed cleanup retained. A deferred close needs an explicit transfer to this already funded graph; an ordinary `Dispose` return alone is not that transfer. |
| Receiver | Installed R -> children released and all R-funded outer cleanup/invoker accesses ended -> one terminal receiver return. On actual failure, retain exact remaining R/root/debt, not a success-shaped disposed state. |

In particular, **nested `bundle.Dispose()` must not return wrapper-funded R** while `PreparedAssetsLifetime.Dispose`/`ReleaseLease` still needs its gate/fields. The wrapper finishes its bookkeeping and exits its gate first; the elected final ordinary owner then performs the exact receiver handback as its last owned operation, with no later access to the retired graph. A later `PreparedAssetsLifetime.Lease.Dispose` clears its own carrier under that consumer's separately identified provision. Do not charge arbitrary future lease carriers to one retained wrapper.

Use the existing entry arrays/progress fields for per-child release progress. Attempt independently safe siblings outside gates; retain failed/unattempted items, one original cleanup sample and bounded numeric additional-failure facts. Do not allocate a failure list, retry an uncertain release, or interpret a repeated latch/no-op as success.

Before R installation, the independent path is `original use -> PreparedScene -> phase/pending owner -> progress/leases`. After installation it is `original domain World interest -> exact parent -> linked receiver -> retained graph`, including its wrapper and Assets dependencies. Preserve the current whole-parent close fence and original binding owner; do not introduce an estimated H carve-out. World/body close supplies no new use, admission, cleanup budget or successor authority.

`PreparedScene.TryReleasePreparationUse` may finish only after its producer and body conditions join and all residual X is ended or validly transferred. Preserve its successful no-later-root-write ordering and the implemented lower outer-return part. A genuine failure keeps the original control/root/debt. The preparation use is not prolonged merely to fund independent R, and body completion cannot discharge producer provision.

## Assets-owned release rectification

**Select failure-retaining terminal release, not Runtime introspection of private lease fields.** Narrow implementation to the actual reference-release chain. This does not propose a general recovery service, retry engine, or a new Assets-to-Runtime dependency.

For an already issued Scene-owned lease, the existing preparation/receiver graph can retain it on failure, but only after Assets preserves its internal state:

1. Before invalidating the handle, elect its single release while keeping its typed `LeaseState` strongly attached. Logical close rejects `Value`, `Retain` and new package operations immediately; logical invalidation must not erase the cleanup graph. Serialize release election against alias acquisition and other releases using Assets-owned state. A simple change from exchange-before-call to exchange-after-call is insufficient under concurrent disposal.
2. In `AssetLeaseLifetime`, record the exact handle-reference disposition, store-accounting disposition and backing-reference disposition separately. Keep the final store/backing references until their required release succeeds or a **specific already funded owner accepts them**. Do not decrement a reference twice, recreate a consumed reference, or refund decoded/live-lease charges solely because logical count reached zero.
3. In `AssetPackageBacking`, elect last release against `TryAcquireReference` and active reads. Keep the stream and final-release progress attached while closing outside all Assets/Scene/domain gates. Do not null the only stream field before the call. A successful nonfinal reference release transfers responsibility to remaining real references; it does not assert the file has physically closed.
4. Return store counters only after the required dependent release/ownership transfer is known; prevalidate the store-side commit while still funded. Record irreversible progress if a later invariant failure occurs. A successful lower close followed by failed accounting is not permission to close again or invent a rollback. Finish state/field cleanup before handing back its original provision.
5. Propagate success upward before the caller clears its owner slot. If a valid lower release fails, preserve the original exception, typed value, still-required store/backing/stream references, exact remaining charges and progress. Repeated disposal is idempotent, **not a fresh attempt or evidence that the earlier failed release succeeded**. Runtime retains that failure state without replaying it. No refund follows from GC, finalization, elapsed time or a second no-op.

Keeping a FileStream object is not proof of OS-handle retention or release. Qualify its actual FileStream/SafeFileHandle/native close boundary: if a valid lower failure can sever ownership before reporting failure, moving Assets' null assignments cannot reconstruct that ownership. That physical-release lane requires a lower ownership-preserving release acknowledgment or a proved nonthrowing valid closure; it remains unavailable otherwise. A second successful/no-op Dispose is not the missing acknowledgment.

The same terminal protocol must cover temporary backing references and all reached acquisition rollback sites, not just `AssetLease.Dispose`:

- `AssetLeaseLifetime` provenance getters and `AddReference` rollback;
- `AssetStore.CreateLease`, `LoadFromPackageAsync` admission failure and `LoadAssetAsync` cleanup;
- `AssetPackageLease.Dispose`, `AssetStore.AcquirePackageLease` construction rollback, displaced backing release in replacement, and store-owner release.

For a failure **before a public lease exists**, an upper retained handle cannot be the recovery root. Before fallible work, Assets must either have a funded, installed acquisition/release recovery owner retaining those exact inputs, or prove that complete valid cleanup closure nonthrowing. Current `LoadAssetAsync` locals, `ownershipTransferred = true` in a `finally`, a terminal faulted Task, or an unrooted `store <-> recovery-record` cycle supply neither fact. Any pending recovery linkage must be installed before acquisition, sized/claimed before construction and retained by its actual original owner, including after store admission closes. It cannot be allocated in the failing cleanup path.

**This is an unfulfilled lower capability, not an assertion that such a record/grant already exists.** If the supported FileStream/native release closure is proved nonthrowing for every valid selected path, that proof can discharge that precise failure-retention branch. Core's credit-release guarantee does not establish filesystem release. Otherwise Assets needs a pre-work opaque operation-ownership/cleanup acknowledgment capability and its own named funding/root; the higher Scene caller must be able to distinguish ended ownership from retained debt without passing a Runtime callback downward. No signature or new public API is fabricated here.

Until that choice is fulfilled, the affected failed-load/cancel/last-backing-release lane stays unavailable. Do not quarantine every ordinary Assets fault/cancellation to hide an unknown cleanup status, exempt all Assets machinery because decoded counters exist, or claim safe recovery from corrupted private counts. No valid physical failure was induced in this assessment; no fake stream, allocator, producer, or failure hook is proposed as its qualification.

## Supplier and callback return: supported proof versus unavailable lanes

The [owner-aware assessment](m03b-scene-preparation-owner-aware-async-handback.md) identifies stock builder state clearing after outcome publication and flowing Action/ThreadPool context suffixes; the [finite-envelope addendum](m03b-scene-preparation-finite-envelope-and-return-addendum.md) identifies input-dependent native fault capture. Those are imported analyses at their recorded runtime revision, **not newly verified runtime binaries or a future testhost identity**.

| Lane | Required real return evidence / current disposition |
|---|---|
| Initial direct driver entry | Return from the existing `AsyncTaskMethodBuilder.Start`, then driver-alias clearing, encloses initial context restoration. This useful boundary proves only that invocation, not suspended descendants. |
| Absent resource participant | No resource supplier is invoked. The role may remain unused; this does not discharge planner or callback return. |
| Selected participant returning null | The current async adapter/bundle still run. Null is an outcome, not a skipped role or return acknowledgment. |
| Genuinely ordinary synchronous progress | An actual enclosing caller can acknowledge **after the concrete non-async call returns/unwinds**, its context scope returns, aliases end and publication calls return. That caller retains its own original provision throughout. This requires a control-flow proof of the complete direct-call closure; `Task.IsCompleted` does not prove that no asynchronous invocation raced to completion. The current overall Scene supplier is not such a closure. |
| Current suspended planner/resource/helper | A terminal Task/ValueTask exposes no post-invoker return acknowledgment. Source `finally`, `GetResult`, another await/observer/`WhenAll`, or a decrement in `MoveNext` does not enclose the stock box/context suffix. **Unavailable under the present boundary.** |
| Root's registered completion Actions | Both use flowing `OnCompleted`. Their internal `EndProducerPart` runs before their enclosing invoker returns. A non-flowing wake plus explicitly owned context invocation is only a candidate, not an equivalent substitution or a proved queued/inline/failure/native suffix. **Unavailable until that actual invoker is closed.** |
| Lower Assets Task | May signal readiness. Excluding its remaining machinery from Scene X requires a named independent Assets owner/grant through actual return and no further Scene-grant access; current decoded-byte/lease counters do not establish that. Otherwise keep its real call envelope on original Scene provision and require its return proof too. |

For the operation-specific replacement route, the ordinary caller must preclaim and retain each invocation before exposure; record the separate registering part before registration; and serialize progress mutation without clearing a slot while an older invocation can access it. Callback-before-registration-return can make both obligations coexist. Derive that peak from actual control flow, not “one pending I/O.”

An owned outer invocation would have to enclose: the actual semantic step; every explicit context scope and its restoration/AsyncLocal notifications; all publication calls; and clearing/transferring step/task/context/input aliases. Only then may its final operation hand back the exact original part/claim once, outside gates and legally after World/domain close. The remaining managed/native suffix must be independently proved to perform no retired-grant access or Scene-owned allocation. Physical stack return is unnecessary only under that positive suffix proof.

For the planner this includes `PrepareCoreAsync`, `PrepareTakingLeaseCoreAsync`, `BuildAsync`, `CountAndMeasureAsync`, the actual recursive `Count*`, `Expand*`, `Fill*RootCountsAsync`, `GetPrefabAsync` and `GetSelectorTargetSourceEntityAsync`; for resources it includes the real interface adapter, bundle, Atlas, Material/texture and Mesh/Skeleton calls. Shared measurement/restore helpers retain their actual separate issuer. Converting only outer methods or adding progress objects leaves those invokers unjoined.

Public `ExecutionContext.Run` can enclose its own nonnull captured context; it does not enclose an already-flowing outer Action. Suppressed/default/nondefault flow, initial isolation, changes inside a step, AsyncLocal reentrancy, restoration and no arbitrary SynchronizationContext capture all need concrete preserved mechanisms. Public capture-null under suppression cannot simply stand for the stock builder's internal sentinel. Do not silently narrow accepted caller context semantics.

**No complete public raw-Task/ThreadPool suffix proof is established for those branches here.** If it cannot be supplied without the prohibited generic async machinery, leave that lane unavailable. The necessary new platform/API capability would be an admission-funded enclosing invocation that retains the exact callback/state/context and registration obligations until after their actual last access/restoration, then permits one ownership handback on success, fault and cancellation. Alternatively the lower runtime owner must accept and fund that whole suffix through a proved transfer. An outcome notification alone, or a new callback around another callback, is not that capability. No such installed capability is asserted.

**Distinguishing static schedule, not an executed Scene:** stop a real supplier after its Task becomes terminal but before its box/context cleanup, then allow outcome observation and body close. Task-visible facts can match a schedule in which that suffix has returned. Clearing the supplier latch from those facts refunds too early in the first schedule; leaving the current latch never drains either schedule. Only the actual enclosing return/accepted suffix transfer distinguishes them. The analogous last-lease example is conditional: if valid final backing close throws, keeping a public lease whose state was already cleared does not retain the failed release inputs.

## Prerequisites, refusal and future validation

Implementable prerequisite work remains compile-only for the producer. Lower-owner/Assets/helper behavior can be checked independently through their real owners without executing the guard-blocked Scene candidate.

| Open gate | Exact prerequisite / refusal condition |
|---|---|
| D/J/K | Freeze the final real field/construction/invocation graph. For every allocation: creating site, actual capacity/stride, multiplicity, old/new coexistence, original issuer, independent root, preclaim and last use/transfer. Close whole carriers, cold/lazy Lock/context/delegate/Task state and all cleanup/publication overlap. Missing rows keep the footprint null. |
| Native fault/cancel publication | Derive the actual `GetResult` rethrows and selected TCS overloads, including contingent/holder/list/EDI state and native stack/keep-alive copying at their admissible capacities. Two original exceptions do not bound new capture bytes. If no useful funded bound fits admitted P, a fulfilled budgeted capture or non-copying preserved-fault relay is required; differently typed child Tasks do not provide it. Preserve original identities/order/token. |
| Return | Prove every tabled actual ordinary invoker and all supported inline/queued/registration-failure/context branches. No Task readiness shortcut, normal-path permanent hold/quarantine, new worker, generic builder or observer chain. Missing capability means unsupported lane, not provisional activation. |
| Assets release | Prove complete valid nonthrowing release, or implement the preinstalled funded failure owner/progress/acknowledgment above. A capacity refusal or invariant-corruption throw is not physical-failure evidence. Missing legal physical stimulus remains an explicit observation gap with retry only when the real supported stimulus/capability exists. |
| Separate callers/contracts | Shared measurement/Branch restore need their own already admitted Persistence seed/root before measurement, not ordinary P or null-as-unlimited funding. Bundle measurement must copy borrowed Material IDs while the Mesh lease is live or keep that lease through last use. The preparation/per-activation cap-description mismatch recorded in the finite-envelope addendum remains a separate owner-contract reconciliation; this document changes no cap or description. |
| Product lane | First bind compiler, actual loaded CoreLib/coreclr/clrjit, generated IL and executable tiers/instrumentation to the final Windows x64 managed Debug/Release candidate. `global.json` is SDK selection, not loaded-runtime evidence. Runtime Async, other targets, NativeAOT and public-consumer/milestone acceptance require their own applicable proof/observations. |

Future execution order and acceptance observations:

1. **Compile/inspect with the guard still null.** Use `dotnet build .\sources\TerraFX.Runtime\TerraFX.Runtime.csproj --configuration Debug`, then Release. These ordinary builds also build the Assets dependency. Freeze the final changed sources and compiled/native inputs; compile success alone cannot enable preparation.
2. **Validate the lower changes through real packages and Worlds.** In `.\tests\TerraFX.Assets.Tests\TerraFX.Assets.Tests.csproj`, select `FullyQualifiedName~TerraFX.Assets.Tests.AssetStoreTests|FullyQualifiedName~TerraFX.Assets.Tests.AssetPackageLeaseTests`. In `.\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj`, select `FullyQualifiedName~TerraFX.Runtime.Tests.Entities.WorldSceneReceiverTests` plus exact `FullyQualifiedName=TerraFX.Runtime.Tests.Scenes.SceneAssetStoreTests.<member>` alternatives for the six members below and any actual added regressions. Use ordinary `dotnet test <owning-project> --configuration Debug --filter "<selection>" --logger trx`, then Release. Verify fresh nonzero selected/executed membership under `artifacts\tst\<Configuration>`, not file names or empty selections.
   - `AtlasAssetCodecAccepts16PagesAndRejects17`
   - `AtlasPageSetLoadsSamePackageTextureAfterReplacement`
   - `AtlasPageSetRejectsNonClampedBorderAndOverlappingPaddedFrames`
   - `MaterialAssetCodecLoadsSpriteAndPbrProfilesWithPackageTextures`
   - `MaterialAssetCodecRejectsInvalidStateAndTextureSlotMetadata`
   - `MaterialTextureLoaderResolvesDependenciesFromPinnedPackage`
3. **Cover the changed ownership behavior, not constants.** Preserve exact old-package values across replacement/store close and final retained-handle release; cover concurrent Retain/Dispose election and once-only accounting using valid owner paths. For covered construction: exact/one-byte-short claims, disjoint claims, original cancellation across close, X-to-R conservation, duplicate/foreign return, no R-to-P refund, and original binding retention through outer returns. For any actually reachable cleanup failure, verify independent siblings, exact failed graph/remaining debt and no implicit retry. No synthetic OOM, private corruption, fake release source or test-only producer counts as this observation.
4. **Only after the prerequisites close, enable derived values and exercise real preparation.** Use actual package/codecs/registrations, mixed Sprite/Mesh/Skeleton and multi-slot/shared-resource cases, no participant versus selected-null versus resources, real success/fault/selected-token cancellation and early/final budget refusals. Preserve the existing nonempty-binding/default-options retention witness `PreparedAssetLeaseRetainsBundleAcrossActivationUnloadAndDispose`, including its original pixel-17 observation. Do not execute it now as an allocation experiment.
5. **Distinguish readiness from return on the final compiled path.** Use safe external stops/caller scheduling at the actual supplier outcome-before-context-clear site, callback-before-invoker-return site, callback-before-registration-return race and public `TrySet*` terminal-before-return site. Close/admit/dispose the body and close the original World/domain while held. Original producer/shared/return provision must remain charged; release only once after the actual covered end. Independently retain R through World close and final lease/wrapper return. If a required site/stimulus cannot be reached safely, record it as unobserved, not passed.
6. **Corroborate the catalogue completely.** Required allocation/native-return coverage must match the final admitted graph on every participating thread; sampled heap ticks or warm constructor totals cannot replace it. Then validate real funded restore and affected public Scene/renderer consumers separately, including required native/AOT paths. No helper test or this tentative guidance closes Chapter 35.

Chapter 35 already permits genuinely dependency-ready M05/M03-B work to continue. Scene-dependent public consumers remain gated; this proposal does not supply their missing producer.

## Bounded current input freeze

Freeze UTC: **2026-11-18T03:59:32.0508619Z**. HEAD: **`d176842a6b3087885500c2f74e372f4151d57bf6`**, branch `tannergooding-m05-implementation-plan`. That local commit contains the Atlas/Material progress change and its package-backed test edits; uncommitted bundle/interface/root wiring is included below, not attributed to the commit.

**Reassessment:** final verification detected concurrent changes to the six resource-adapter/bundle/interface/root/driver/loader inputs. Their changed funding/release methods were re-read, the new `ScenePreparationFunding.cs` was added to scope, and the findings above now distinguish the existing scalar funding port, independent workspace-return attempts/new capture site and ordinary settlement refusal from still-proposed work. The initial freeze is superseded, not silently reaccepted; the other 58 inputs remained unchanged.

These **65 raw-file SHA-256 values** identify the bounded directly inspected source, relevant guidance/proposals and targeted test-source inputs. `C` means tracked/index/worktree clean at HEAD; `M` means unstaged worktree modification with unchanged index; `U` means untracked. No listed input was staged. Hashes include actual dirty/untracked disk bytes, not Git-normalized contents. No schema/fixture bytes were changed or qualified.

```text
12439df4bb5e46021891b234bec1104abdca57c11fdf3f610c8f32c5daf3001e  C  .github\copilot-instructions.md
03efea672cd52e9ef053e9ce0285e7ca02c6ce6fb0d21268ade4255ed5a39d58  C  .github\workflows\ci.yml
770ee4b2a422861a6f23f0d39dd0eca361a81cb87e64ebac4e12170f71231d7a  C  Directory.Build.props
aeec1d08e8037470239035812351c4045124607d67a354d482112f4d642d4105  C  Directory.Build.targets
1e0e79719efc9259390d9cf4e63ac697561725c8cd910d2ab2541d6e5bb95a6d  C  Directory.Packages.props
40f913a50434209e09941c22625a6d0722202bab97ebac0e153c1ea3368e2291  C  docs\design\02-dotnet-api-memory-and-ownership.md
a4d86f3fa373a64548435c53582bf5916e29d77f34650f9d161ccec90e6f0245  M  docs\design\04-capacity-and-workload-profiles.md
de1032a04217b2a08933332aee7db22df849cbfc069e76cf6b2263bf469df109  C  docs\design\11-assets-import-streaming-and-packages.md
b012f42b23cede9c7cdb71203d8357cd19e1626823ea25ef3119961292d3698a  M  docs\design\16-scenes-transforms-and-cameras.md
3b55a013eec327ecd648acbd5b53717dd218ad288e8047ac21277609efa2f47b  C  docs\design\29-project-setup-build-and-deployment.md
15848c82f3858a77a87a0d3ea6fbcc07046eb6e77ca5b21748c72b90efab598b  M  docs\design\30-public-api-review-surface.md
24eec6e92b7ad904d552f290ef748ffc8fce13b6b94a89ebd9f39487d4584d87  C  docs\design\35-implementation-roadmap.md
f043a352870efb6c7d4a61998225e2508a8f4370a57e784483a41819d452075f  C  docs\project\Building.md
1c8c3f8e140f47492255534284abbf67c5776210dc7a68f2c01155d0be773f46  C  docs\project\Roadmap.md
8a60dd16f9dc54f6616d1d3ae8dcd064e6bc11929311cbbe3474c4fdbc95a9cf  C  docs\proposals\m03b-resource-bundle-preconstruction-owner-and-return-closure.md
3f8e4f0315c6cc2933fc982f64d5e94b9070eb1876a74b0688118bb17aff5a62  C  docs\proposals\m03b-scene-preparation-budget-accounting.md
3eb20e1051c55ee77d008cf734007fde8c28aec24abd9755f4d1788475bd4605  C  docs\proposals\m03b-scene-preparation-finite-envelope-and-return-addendum.md
940baef78ccfbd720e292167c3af9c7c00aabdbc4f448e6e4a00793bb54230da  C  docs\proposals\m03b-scene-preparation-footprint-closure.md
0a9ae81eb42d886168d9e05970c7395b33c9ef70fccada9ed311cf2e1dc7ad89  C  docs\proposals\m03b-scene-preparation-owner-aware-async-handback.md
199158c2357ac6b3fe551f568c0da69b739a0a30afdfc2372113dd4c6597f8fd  C  docs\proposals\m03b-scene-preparation-supplier-return-closure.md
9d7280a89c5bb75890adaf65e26f8c80766b8687ee0608623d0f97d519be32c3  C  docs\proposals\m03b-scene-preparation-workspace-closure.md
632fe2eadf72026d0d68fa093d260b92ca1be8d5eb6deae5fdf08bf48f471ab5  C  docs\proposals\m03b-world-binding-owner-retirement-fence.md
1bce01871321c2df82e887da428ce5c121a6c6541fe65017fe3d249be47e190c  C  global.json
d44373af315523f8af9f09f36bded2a688e2b5c1572fcc0732310c7ef590ead8  C  sources\Directory.Build.props
a639a5949353f2fb594b2c57569b2491d08988bfba48f551e666a36a6ea8d3da  C  sources\TerraFX.Assets\AssetLease.cs
2749b26e3a42809558558704b6d11c51e886ae40a933e29b7a205a10357881b2  C  sources\TerraFX.Assets\AssetLeaseLifetime.cs
784a190d7e704e626c698ac458ef5b5cc671c25c629cf93748da70b0caab5677  C  sources\TerraFX.Assets\AssetPackageBacking.cs
f30ec516d7407c8ee8e61dba65c75ed50d88ccfc39ac38165dbb690e06795807  C  sources\TerraFX.Assets\AssetPackageLease.cs
06f12e7b2d7d89a44a92e28f41a8581af3a64f963df0dbdd935da2722984b4fd  C  sources\TerraFX.Assets\AssetPackageReader.cs
1bdf040211fdaad0a1347ce9c1d8a478d65743ed543f362bf60d1e16159b5d03  C  sources\TerraFX.Assets\AssetPayloadReader.cs
6ee57d6c011bece27e5f1de33a5c1d27d9ae76a510ce825022c0cfe070b02b74  C  sources\TerraFX.Assets\AssetStore.cs
79a5872aebab1ea7447e064bc923227660056458b5b8e2b4af32a891e11789dc  C  sources\TerraFX.Assets\AssetStore.PackageLease.cs
270dabda38f2474b7d48b02a230f1492e2a3bcfcc6963a8ad1bd102ca6ceca53  C  sources\TerraFX.Assets\AssetStore.PackagePin.cs
76f077c074a4198c78984e46cf7adff94e23092abb3fa87ce18865bb31621101  C  sources\TerraFX.Assets\TerraFX.Assets.csproj
c7dcba4dfef58346c6c851f77a8e130b1a4b776248e94663438a5d9e449245a8  C  sources\TerraFX.Runtime\Entities\World.cs
f4cf773ccbadd8564bd4eeff261c6a0bbc89f1a00ed71507bbdfafbe8e2021a6  C  sources\TerraFX.Runtime\Entities\World.ScenePreclaimedWorkspace.cs
a250eecca2cfb4e0eebff7f447e66ac7e44a8d23ad0d6de141e8e1d4c30614e3  C  sources\TerraFX.Runtime\Entities\WorldAdmissionDomain.cs
ceac98719116305ba2727b892ce540a3b6b26d1ddf72afe06e21504909747fb0  C  sources\TerraFX.Runtime\Entities\WorldAdmissionDomain.ScenePreclaimedWorkspace.cs
53111972c5b480e8357c93d471edcb4e08c7bbe65bd341bfc7260fdc356ba5c3  C  sources\TerraFX.Runtime\Entities\WorldAdmissionDomain.SceneStorage.cs
d6cdd6105850b8076d40891b0ed1f583d9fcde6ee190444a7d570879964403dc  C  sources\TerraFX.Runtime\Entities\WorldDomainReservation.cs
f00b54b0e8f305f90df0d63ffdf6c3419feb8f3d2542e72d76a79ad2d0990946  C  sources\TerraFX.Runtime\Entities\WorldGuidUseControl.cs
a91aac1a9eb2ba7b43c111fab8baa9d6588a6c75e725c28ea9825f049472c969  C  sources\TerraFX.Runtime\Entities\WorldSceneStorageReservation.cs
4ac1a8c78e65b73dc6b4f4b6cbf4106af81e2b405ef6c9f59842c4db043c0e23  C  sources\TerraFX.Runtime\Rendering\RenderAtlasPageLoader.cs
abc653b3150c4c3bde95a90fe39527d0f4b068c62dad96577a183e2c681f82bb  C  sources\TerraFX.Runtime\Rendering\RenderAtlasPageSet.cs
624a1ce93559ed6cf055c6f46b0e92cbac719c45d9132cbf16ade4f4302d67ad  M  sources\TerraFX.Runtime\Rendering\Renderer.CpuExtractionSupport.cs
9251a97dbe28692d52acfd50279fe4a9b7d0395eee548d383b0b52a8733196e6  C  sources\TerraFX.Runtime\Rendering\RenderMaterialTextureLoader.cs
fc96d4afff183dc0ca5c9476f6ffb508e6c4ca3f98a4e6edbdbf3001bdf3656a  C  sources\TerraFX.Runtime\Rendering\RenderMaterialTextureSet.cs
e4b7edbbc746cf29568d683c8ed0a8e0987fde6dc10ad4e390370bb8d12865d5  C  sources\TerraFX.Runtime\Rendering\RenderMeshAssetBinding.cs
a515c099e5f2aa7047c053b69945acd9e82b71eba43d05caf85985a965db1aa3  M  sources\TerraFX.Runtime\Rendering\RenderSceneAssetBundle.cs
ec7d3ad34c0f4e7f97d99e7f7d2b568dab6109c42d423f4ce74716030357f756  M  sources\TerraFX.Runtime\Scenes\ISceneExpansionResourcePreparer.cs
846ddd30c039bc29f8a41dddc8642085e841f1d5bd39deff40daac3710e0111e  C  sources\TerraFX.Runtime\Scenes\PreparedAssetsLifetime.cs
2aca76495eec336c1eded6e52a2cc2cf6ffd4ee3c2d4dd1b56f58f0cf5e6cbae  M  sources\TerraFX.Runtime\Scenes\PreparedScene.cs
640bb93c7e4e98be3ed1b0d3679186b6ff3dbeb26d7e9986e75aa14991464b76  U  sources\TerraFX.Runtime\Scenes\PreparedScene.Preparation.cs
af9626c539b35c73338baac12409b647fa37bf92ee38fd5c2e6be892c40fe771  M  sources\TerraFX.Runtime\Scenes\SceneExpansionLimits.cs
4ec58808bb5400448f71327e75cab787ed03c1ce43fa39ffae832cea8c4ad137  M  sources\TerraFX.Runtime\Scenes\SceneExpansionPlan.cs
4903a86fc323ed906a0045288bebab68b37c5b6beeff6312ea0440a9d05d453c  M  sources\TerraFX.Runtime\Scenes\SceneLoader.cs
3fd4285447057dd136412b1797e61946696c9017342454a577798b68403bbcab  U  sources\TerraFX.Runtime\Scenes\ScenePreparationFunding.cs
1e01cc392652771a948d45f79fca19fc1ed5c4659439ad4ec8879c2e99127876  M  sources\TerraFX.Runtime\Scenes\ScenePrefabExpansionPlanner.cs
2f1c81e54f0fbf12d01e34d0d840885f5eb0e94a8b8890865ef721b8c6a3c65d  M  sources\TerraFX.Runtime\TerraFX.Runtime.csproj
a0af6871ed04700a118a52a3a138beb152f06535f4220eb7aa76ef3f8a00ba89  C  tests\TerraFX.Assets.Tests\AssetPackageLeaseTests.cs
38e99e3837a83d86cdfa43b2dc16d1e5bd5af757bd26bc8cb0745c68dfbb2896  C  tests\TerraFX.Assets.Tests\AssetStoreTests.cs
70305f994186d58a14fa5a53dcab0bd976d8beaa0cf37106dff13816ef9e58b2  C  tests\TerraFX.Runtime.Tests\Entities\WorldSceneReceiverTests.cs
e342237b7645be805dd924855b2239907773d31753d22b8b2261bf26c20452f4  M  tests\TerraFX.Runtime.Tests\Scenes\PreparedSceneAssetRetentionTests.cs
017580601c1d2b9c1ee61060a76f07606a3e2eb973244fc97cde708fb4aed015  C  tests\TerraFX.Runtime.Tests\Scenes\RenderAtlasAssetCodecTests.cs
8cd0534b62ca53964476ba681791f3bd5ed2a651b6c1f04f9b1b4685872d82d5  C  tests\TerraFX.Runtime.Tests\Scenes\RenderMaterialAssetCodecTests.cs
```

Recheck every listed raw hash and Git state, and HEAD, before applying this assessment; changed bytes require reassessment, not silent hash refresh. The authoring check reverified this bounded set before finalization. This is not a repository inventory, transitive build/runtime/native freeze, source-to-binary attestation, schema review or acceptance receipt.

Only this proposal was authored. No source, tests, maintained design/roadmap, other proposal or build configuration was edited or staged. No build/test, Scene preparation, physical-failure experiment, native/AOT observation, commit, push or remote publication is claimed.
