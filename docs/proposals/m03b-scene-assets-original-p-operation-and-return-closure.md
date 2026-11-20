# M03-B Scene Assets: original-P operation and return closure

**Status: tentative implementation recommendation under the requested blocker-proposal policy, pending review. Not a maintained contract amendment, numerical allocation profile, or executable Scene admission. `PreparedScene.QualifiedFootprint` remains null.**

## 1. Decision and scope

Make the **existing `PreparedScene` the sole original-P issuer and enclosing read invoker**, with one dedicated embedded Assets-operation slot. Extend its existing `ScenePreparationFunding`/producer-claim mechanism; do not acquire another World use. Put acquisition and read progress in an **Assets-owned `AssetLoadOperation<T>`**, installed in that slot before acquiring even a temporary backing reference. Its factory, start, materialization, installation, cleanup and return belong to one checked claim.

Use ordinary, synchronously returning Assets steps for acquisition, envelope/reference scanning, typed decoding, inspection, issue and cleanup. The actual Runtime invoker consumes the real lower I/O completion once and observes those steps returning. Do not retain the selected internal Task chain and add a second observer or a self-reported `Returned` flag.

This refines the owner recommendation in [closed-read allocation authority](m03b-scene-assets-closed-read-allocation-authority.md) and [actual-invoker return closure](m03b-scene-actual-invoker-return-closure.md). The [allocation-site catalogue](m03b-scene-assets-allocation-site-catalogue-addendum.md) continues to own the detailed data-site derivation; it is not duplicated here. Historical statements about absent Rendering receipts, reader builders, retained options, or earlier lease-construction ordering are not current source facts.

The typed family is exactly Scene/Prefab **2.0**, Mesh/Texture/Atlas/Material **1.0**, and Skeleton **1.0** for static BindPose. Every selected payload and automatic reference inspection requires package `None`; envelope remains 2.0. Existing factory origins already select this family. Automatic reference inspection does not invoke a target codec. Ordinary Assets APIs, custom/Brotli behavior, other asset versions, package opening/replacement, Persistence/restore/measurement and dynamic animation/LOD/particles are not admitted by this recommendation.

**There is still no safely admissible all-outcome operation at this revision.** The finite reader-data functions do not close arbitrary retained inputs, exception/EDI captures, actual lower I/O/close, or caller/context return. Sections 5 and 8 specify the missing capabilities and retry conditions. Tentative approval permits pursuing those implementations; it does not turn an unknown term into zero, authorize an unfunded experiment, or authorize implementation changes in this artifact-only assignment.

## 2. Verified boundary at the requested HEAD

The initial source-review boundary for this recommendation was:

```text
c83de08b181f4395b8c390834ac0c3982479db7f
```

This is not the current source revision. Section 9 records the current decision-input freeze and later source updates. The observations below are source evidence, not newly executed behavior, allocation measurements or native observations.

| Current source | Consequence for this operation |
|---|---|
| [`PreparedScene.CreatePreparation`](../../sources/TerraFX.Runtime/Scenes/PreparedScene.cs), constructor and `QualifiedFootprint` | The null guard precedes description/use acquisition. A separate CPU-resource-preparer refusal remains. The candidate constructor creates a Lock, TCS/Task, two exception slots and two callbacks; there is no Assets child slot. Neither those exception slots nor a domain interest-cell charge sizes their referenced graphs. |
| [`SceneSupport`](../../sources/TerraFX.Runtime/Scenes/SceneSupport.cs), [`Renderer.RegisterAssetCodecs`](../../sources/TerraFX.Runtime/Rendering/Renderer.CpuExtractionSupport.cs), [`RendererAssetCodecProfiles`](../../sources/TerraFX.Runtime/Rendering/RendererAssetCodecProfiles.cs), [`AnimationSupport`](../../sources/TerraFX.Runtime/Animation/AnimationSupport.cs) | Scene/Prefab and the Rendering/Skeleton dependencies already have private factory receipts and described-read dispatch. This work must not reimplement that completed selection or confuse it with allocation authority. |
| [`AssetEnvelopeDecoder.DecodeAction.Invoke`](../../sources/TerraFX.Assets/AssetEnvelopeDecoder.cs), [`AssetReferenceScanner`](../../sources/TerraFX.Assets/AssetReferenceScanner.cs) | The installed decoder still calls allocating `Scan` before the codec. `ScanInto`/`CopyUniqueReferences` have only test callers. There is no selected scratch owner. |
| [`AssetLease.LoadDescribedFromPackageAsync`](../../sources/TerraFX.Assets/AssetLease.cs), [`AssetStore.ReadDescriptor`](../../sources/TerraFX.Assets/AssetStore.ReadDescriptor.cs) | An anchored facade acquires its temporary backing reference before the async admission wrapper. Refusal cleanup therefore needs ownership even with zero admitted load credits and no issued result. |
| [`AcquireLoad`](../../sources/TerraFX.Assets/AssetStore.cs), [`LoadAssetAsync`](../../sources/TerraFX.Assets/AssetStore.PackageLease.cs) | Store reference/lease/wire/decode admission precedes payload work. Initial decode credit is returned before automatic target inspections, while the semantic value and reference result remain live. Wire-credit return is not managed-storage return. |
| [`AssetPayloadReader.ReadAsync`](../../sources/TerraFX.Assets/AssetPayloadReader.cs), [`AssetPackageBacking.ReadExactlyAtAsync`](../../sources/TerraFX.Assets/AssetPackageBacking.cs), [`AssetPackageReader.ReadExactlyAtAsync`](../../sources/TerraFX.Assets/AssetPackageReader.cs) | None still allocates a decoded array and reaches an async, potentially short-reading `RandomAccess.ReadAsync` loop. Existing engine source uses `Memory<byte>` at this edge; it is not a reviewed exception to Chapter 02's retained-I/O rule. None removes Brotli, not I/O ownership. |
| [`CreateLease`](../../sources/TerraFX.Assets/AssetStore.cs), [`AssetLeaseLifetime.ReleaseReference`](../../sources/TerraFX.Assets/AssetLeaseLifetime.cs), [`AssetPackageBacking.ReleaseReference`](../../sources/TerraFX.Assets/AssetPackageBacking.cs) | `ownershipTransferred` now becomes true only after successful lifetime **and** handle construction. Final issued release records backing success before Store accounting and retains failures. The backing's failed-release root supplies reachability, not funding or native-close attestation. Preserve these distinctions. |
| [`AssetStore.AcquirePackageLease`](../../sources/TerraFX.Assets/AssetStore.PackagePin.cs) | It now constructs the package-lease/LeaseState carrier inside Store admission before acquiring the backing reference or incrementing lease accounting; no allocating construction follows that ownership commit, so the former catch/release ordering window is removed. This source ordering does not qualify the general pin's full memory profile or native-close behavior. |
| [`ScenePrefabExpansionPlanner.PrepareCoreAsync`, builder construction and `GetPrefabAsync`](../../sources/TerraFX.Runtime/Scenes/ScenePrefabExpansionPlanner.cs) | Root staging follows the awaited load. Builder field initializers allocate before builder staging. A new Prefab lease remains local through context checks and dictionary insertion. These are real installation and caller-suffix intervals, not part of codec output alone. |
| [`RenderSceneAssetBundle.PrepareOwnedCoreAsync`](../../sources/TerraFX.Runtime/Rendering/RenderSceneAssetBundle.cs), [Atlas](../../sources/TerraFX.Runtime/Rendering/RenderAtlasPageLoader.cs), [Material](../../sources/TerraFX.Runtime/Rendering/RenderMaterialTextureLoader.cs), [Mesh/Skeleton](../../sources/TerraFX.Runtime/Rendering/RenderMeshAssetBinding.cs) helpers | The described dependencies coexist with helper builders, validation scratch, retained handles and Tasks. Mesh binding retains its Mesh before testing for a Skeleton. The funded bundle path still explicitly refuses final retained-storage settlement. |
| [`PreparedScene.Preparation`](../../sources/TerraFX.Runtime/Scenes/PreparedScene.Preparation.cs) | Scalar producer claims and pending/phase installation elections exist. `_supplierProducersUnjoined` is set before planner invocation and is never cleared. Completion Actions and public `TrySet*` do not supply the missing transitive return. |
| [`WorldGuidUseControl`](../../sources/TerraFX.Runtime/Entities/WorldGuidUseControl.cs), [`WorldAdmissionDomain`](../../sources/TerraFX.Runtime/Entities/WorldAdmissionDomain.cs), [preclaimed Scene workspace](../../sources/TerraFX.Runtime/Entities/WorldAdmissionDomain.ScenePreclaimedWorkspace.cs) | The real original use has exact generations, retained-owner/quarantine anchoring, checked workspace accounting and covered receiver construction/transfer. Its final release retains the exact parent's return part. Reuse these owners, not a second quota or a Scene imitation of their internals. |
| [Registry terminal invocation](../../sources/TerraFX.Runtime/Entities/WorldGuidRegistry.TerminalAnchor.cs), [domain terminal anchor](../../sources/TerraFX.Runtime/Entities/WorldAdmissionDomain.TerminalAnchor.cs) | `InvokeTerminalWorldClose` observes `DisposeWithinTerminalInvocation` returning before accepting terminal caller return. This is a concrete enclosing-caller pattern, not a general certificate for Scene Tasks or permission for Scenes to call Entities' terminal methods. |
| [`AssetStore.Bounded`](../../sources/TerraFX.Assets/AssetStore.Bounded.cs), [`AssetStoreBoundedProvision`](../../sources/TerraFX.Assets/AssetStoreBoundedProvision.cs), backing reference admission | Batch A preclaims an acquisition-free Static-Mesh opening/registration prefix. A pre-opening backing refuses ordinary reference acquisition. Its two-codec family, scalar outcomes and synchronous [Batch B selection](m03b-assets-batch-b-worker-envelope-rectification.md) do not provide an async Scene read, original-exception envelope or native close profile. |

Two distinguishing schedules follow directly from these edges; neither is an executed failure observation:

1. An anchored call takes a temporary reference; the source and Store then close; typed admission refuses. That temporary reference can be final. The installed operation must retain its release disposition even though no child lease ever existed.
2. A read becomes ready while its registration caller or completion dispatcher is still returning. A second read request cannot reuse the old claim merely because the data outcome is visible. Its returning interests must end or move to an identified, already funded parent/lower lifetime first.

## 3. Concrete owner, layouts and claim protocol

### Original issuer and retained root

The sole preparation root remains:

```text
exact domain/process independent-use anchors
    -> original WorldGuidUseControl anchor (RetainedOwner)
    -> PreparedScene
         +-- dedicated Assets slot -> installed AssetLoadOperation<T>
         |                             -> Assets obligations
         +-- existing phase/pending planner or bundle
                                       -> accepted results and retained claims
```

`PreparationControl` is an accessor for the original `WorldGuidUseControl`, not another proposed control type. `World.AcquireGuidUse`/`WorldLifetimeControl.AcquireUseCore` already reserve the requested whole cap before constructing that lower control. Keep the requested P unchanged.

There is an entry-order prerequisite as well: current `CreatePreparation` describes the root asset **before** acquiring that use. Successful description borrows metadata, but its refusal/static-initialization paths are not automatically allocation-free. For a later usable path, keep public shape/pre-cancellation checks first, then move potentially allocating description/profile work inside the original-use bootstrap provision before constructing the root. Include unescaped description/factory failure and actual return in that provision. Review the resulting admission-error precedence explicitly; do not pretend P already funded a pre-use exception or silently reorder accepted usage checks.

Use the existing accounting:

```text
P = F + X + T
operation attribution U = 256 + F + X
```

F is uncommitted original workspace, X is active/protected provision, and T is the portion transferred out of this use. A child subclaim subdivides the producer workspace **already reserved in X**. It does not call domain reserve again, reduce P, or spend fixed producer/publication/body-cleanup provision. Returning a child subclaim makes that producer partition reusable; it does not itself call `PreparationControl.TryReleaseWorkspace`. Only the existing owning ledger returns its domain workspace.

### Required control layout

The following selects private implementation membership, not public declarations or byte sizes. Final field lists, compiler-generated state, headers, alignment and coexistence must be frozen from the actual compiled candidate before admission. Do not infer a class charge from `Unsafe.SizeOf` of a reference, a proposed name, or the 128-byte domain cell.

| Existing/new location | Required members and ownership |
|---|---|
| Embedded slot in `PreparedScene` | One exact operation reference and one exact expected-destination reference; private typed-read-kind and slot-state enums; a nonwrapping `uint` generation; checked `long` total, temporary, protected, retained-upper and settled-retained amounts; checked `int` construction/start/registration/invocation/installation/return interest state or counts. These are embedded value fields within the whole-root charge, not separately allocated claim/receipt nodes. Keep `_phaseOwner` and `_pendingOwner` intact. |
| `AssetLoadOperation<T>` in Assets | Exact Store and source facade; descriptor/receipt; entry and selected `IAssetCodec<T>`; backing reference and individual owned-credit/progress facts; parent/target byte arrays; reference occurrence/index/result storage; `AssetCodecContext`; typed value; lifetime/handle construction candidates and staged result; current reference index and checked short-read offset/count; lower read identity/sequence/single-consumption state; selected primary exception/token, first cleanup exception and bounded scalar cleanup facts; separate resource-release, accounting, installation and return states. No captured Runtime delegate, World capability, `ScenePreparationFunding`, span, `Memory<T>` field or growable recovery list. |
| Runtime invocation fields in the existing root | Precreated wake delegate(s), running/pending-wakeup election, registration-in-progress and queued/running/returning interests, selected logical-context state and publication state. Wake callbacks retain the parent root, not a disposable child-generation closure. Their number/storage and remaining runtime suffix still require the proof in section 5; a bit field is not that proof. |
| Existing planner/bundle/helper destinations | Exact retained claim next to the real lease/cache/progress member. Prefab cache entry layout must carry its accepted charge if that is where the result lives. Existing helper `OwnedProgress` and pending lease fields retain construction/validation prefixes before another fallible step. No separate per-result preparation carrier. |
| Existing backing/Store | Original lifetime provision and actual lower read/close progress. Shared native/cache/registration storage must have this independent issuer before it can be excluded from Scene P. No Scene callback issues or replenishes that grant. |

An opaque slot can retain the exact generic operation; selected call sites remain explicitly typed for the seven roots. Do not add reflective activation, an arbitrary codec-kind switch in Assets referencing Runtime types, or a generic task-like framework. Any additional interface/base/control/delegate needed by the final implementation is part of its actual layout, not assumed free.

Select serialized mutation rather than another per-operation gate: only the root-elected invoker advances Assets progress. A lower completion may publish only into its own admitted completion state and request the root's wake election; close/cancellation does not concurrently clear operation fields. Existing Store/source/backing gates still own their respective admission/release transactions. This removes no contention/runtime term from those existing gates.

### Source-closed invariants

1. Only the exact `PreparedScene` driver elects and returns this Scene child claim. `ScenePreparationFunding` forwards to that root; it does not lend a separately disposable use. A number passed to Assets is **not** authority by itself: every production factory/start caller must belong to this checked preclaim/install sequence.
2. Under the root gate, require Building/Running state, an empty dedicated slot and sufficient unclaimed producer provision for the **complete** selected operation. Record construction/generation before leaving the gate. Refuse on generation exhaustion, occupancy, unavailable profile or insufficient provision; allocate no waiting record.
3. Construct the empty Assets owner outside gates. Its constructor acquires no package reference, Store credit, I/O or result. Install that exact owner before its sole start. Construction/attachment unwind is covered by the original elected claim even when no object returns.
4. Acquisition occurs inside the installed owner, not in the old anchored facade wrapper. Preserve source/lifetime/Store/backing lock order and established usage/pre-cancellation ordering. Record each reference/credit before the next fallible action or escape. No physical cleanup, callback, wait, codec call or Task publication runs under admission gates.
5. Revalidate exact descriptor, receipt, source facade and backing at acquisition. A stale Store-current descriptor refuses; an anchored old backing never retargets. Apply current Store admission policy again, including replacement-sensitive decode/lease limits. Description reserves no headroom.
6. One ordinary child operation occupies the slot through construction, result transfer, cleanup and actual return. Automatic inspections are subphases, not extra slots. Synchronous `Retain` calls also need prepaid construction/return coverage and an installed helper destination; they do not create a second decoded-value grant. No bound follows from `MaximumLiveAssetLeases` on returning controls or inspection work.
7. Assets accepts only the closed compiled implementation and partition fixed by the receipt/profile inputs. Assets receives no Runtime funding callback or upward scheduler. Adding a production call site, callback, allocating branch or retained field invalidates the relevant profile.

Extend the existing root cleanup/end checks, not just its scalar claim counter: `CleanupBuildingBody` must request/join child cleanup before disposing a consuming destination; `EndPreparationProducer` and producer-workspace return require the dedicated slot and all child construction/installation/return/debt interests to have ended. Clearing a pending Task or owner field does not fulfill those interests.

## 4. Read lifecycle, data functions and installation

### One operation, including failure prefixes

```text
claim/elect -> construct -> install -> acquire
  -> parent payload -> structural/envelope admission
  -> owned reference scan -> existing typed reader
  -> sequential automatic target inspections -> issue candidates
  -> result staged -> fenced destination installation -> transfer acknowledged
  -> remaining cleanup and return joins -> end temporary claim -> slot reusable
```

Fault or selected cancellation stops new issue and enters cleanup **after actual consumers stop**. A possibly issued lower read stays live until its actual disposition is known. A stopped operation with unreleased resources/accounting enters failed-retained state, keeping its slot, original claim and anchor. These are different states; neither is successful cancellation or an excuse to retain ordinary successful operations forever.

Retain the current **complete-handle** transfer point. The issue step constructs and attaches the candidate lifetime, then constructs and attaches the complete handle/state. Until then, the operation alone owns the original backing/Store obligations; the nonescaping candidate lifetime must not independently release them. After both constructions return, validated nonthrowing field stores commit those obligations to that exact issued lifetime and mark transfer, before the issue step returns or exposes the staged handle. A failed handle construction therefore takes only operation rollback; a later failure takes only the issued handle/lifetime release route, **never also** raw `CompleteLoad` rollback. The candidate's reference field is not a second backing acquisition. Factor the current `CreateLease` construction sites into this staging protocol rather than declaring transfer in a `finally` or leaving the between-construction prefix unattached.

### Checked inputs and disjoint partitions

Use checked `long` multiplication, addition and sixteen-byte rounding for the existing accounting function:

```text
A_T(n) = 64 + RoundUp16(n * selected managed stride(T))

C_op = J_child + DataUpper(kind, descriptor, limits, schemas) + K_child
```

`J_child` contains the whole operation/control and its additional invocation/return machinery. `DataUpper` contains the separately allocated data and issue carriers, including a retained-result upper partition. `K_child` protects the complete local fault/cancel/cleanup overlap. Root-fixed J, root publication/body K and independently granted lower/shared storage are charged once at their actual owners, not repeated in each term. The conservative sum is allowed; an unknown term makes the function unavailable, not zero.

The function inputs must include:

- Exact operation kind and factory receipt, Store/source/backing identity, entry codec/offset/stored and decoded lengths, package length, immutable effective read limits, schema registry identity and expanded nested-schema/role membership, spatial profile/settings, and selected compiler/runtime/architecture/code-generation lane.
- A same-backing target-length bound derived **without acquiring an unowned reference or copying the directory**: maximum declared decoded length over that exact directory, or its `MaximumDecodedAssetBytes` as a looser ceiling. The descriptor does not currently contain a precomputed directory maximum; adding one requires source-lifetime-safe description and revalidation. Parent length cannot bound a different target.
- Actual previously accepted root/cache/helper claims, protected remaining cleanup/publication provision, lower/shared grant identities and current Store admission state. `AssetStoreOptions.Validate` currently checks nonnegativity, not all documented ceilings; do not use defaults or unenforced option ceilings as observed implementation bounds.

After complete structural admission, the catalogue's record/field/array limits and validated extents bound semantic allocation-reaching prefixes. In particular:

```text
reference occurrences r <= min(1_048_576,
                               L.MaximumAggregateRecords,
                               floor(d / 64))
unique references u <= r
ScanFixed(r,u) = A_AssetReference(r) + A_int(r) + A_AssetReference(u)
```

These are valid-structure bounds, not a bound on malformed scanner/error/runtime work. Raw Bytes extents are not bounded by the aggregate array-element counter. Maximize the catalogue's semantic site functions over **allocation-reaching prefixes**, including the failing attempt, rather than only valid final objects. Preserve, for example, duplicate Mesh-stream and invalid-Omit allocation order. Checked overflow, array representability failure or inability to fund the upper function refuses before acquisition; do not read first to discover a cheaper grant.

Implement a selected, owner-bearing `AssetEnvelopeDecoder` path using the existing structural action and codec. Keep ordinary `Scan` unchanged:

1. Once the parent envelope is structurally admitted, use `CountAssetIds` to obtain r. The complete worst-case scratch/result allowance was already claimed before acquisition.
2. Allocate and attach the exact `AssetReference[r]` and `int[r]` arrays before invoking `ScanInto`; reserve their constructor/unwind prefixes as well. Attach each returned allocation before the next one.
3. Obtain u, allocate/attach exact `AssetReference[u]`, call `CopyUniqueReferences`, and adopt it immutably without a writable published alias. Preserve full `(AssetId, SchemaId, SchemaMajor)` deduplication and first-occurrence order.
4. Keep scratch through the **actual synchronous structural action/scanner/copy return**, not just the write of a result field. A scoped/ref-struct action is supported by `EbinStructuralReader.Read`; no view or span crosses suspension.
5. Invoke the existing typed reader in the same validation order. Retain the context, envelope, scan result, semantic graph and validation scratch for their actual coexistence. `encodedEnvelope = []` and Store decode-credit return alone do not prove last access.
6. Inspect automatic targets sequentially. Check known None metadata before each target reservation/read; schema ID/major and exact registration checks still follow actual envelope inspection. One target-array peak is usable only after the previous target's consumers/return obligations end or transfer; otherwise charge their real overlap.

DataUpper is the checked sum/phase maximum of the catalogue's parent, fixed scan, semantic-prefix, target-inspection and issue terms. Mesh triangle, sphere and weight predicates now use nine-limb fixed stack arithmetic for the derived 558-bit ceiling; the prior BigInteger limb/rent term is removed. The owning Mesh codec selection passes 10/10 in Debug and Release, including finite-extreme/subnormal geometry and the exact binary32 values around the 1/100000 weight tolerance. This closes only that predicate subproblem. Its fixed native-stack/J footprint and all remaining reader, issue, lower-I/O and return terms still belong in the complete target profile.

### Exact result handoff and receiver transfer

The operation stages the complete result before the caller can process it. Under one root installation/close election, identify the exact destination and generation, keep an installation interest across ordinary lower calls, install the result **and its charge**, then acknowledge that exact transfer to Assets. Do not call lower code under the root gate. If acceptance is uncertain, retain both rooted sides under that interest; neither side may dispose or refund the result speculatively.

For the root Scene, destination ownership must cover staging before builder construction. For a Prefab, it covers value/context checks and cache insertion. For Rendering it covers the actual bundle/helper pending field, validation, builder insertion and immutable-set construction. Existing `StagePreparationOwner`/`CommitPreparationOwner` and helper fields are the starting ownership mechanisms; merely assigning an awaited result to a local or pending field does not cover the preceding interval.

After successful acknowledgment, split the original child claim:

- **D:** exact surviving semantic graph, owned record/pixel/component/literal backings and actual lease/state/lifetime/gate carriers; retain under the real root/cache/helper destination.
- **Temporary/unused:** ended input/scan/validation/issue-candidate storage and unused upper provision; return only after relevant synchronous/lower/invoker returns.
- **Protected/failed debt:** still-required fault/cleanup/return provision and actual unreleased resources; never refund as unused.

`AssetCodecContext` and scanner scratch are not D merely because the final value retains the same schemas/limits. Aliases of one immutable value require one physical value grant lasting through its last consumer, plus each separate handle's carrier. A wire-byte counter is not that grant.

Later transfer only the exact retained set plus its qualified receiver-control provision Q through the existing covered-workspace transaction:

```text
R = D + Q
fully covered: X -= R; T += R; U -= R
               exact parent World-owned/storage attribution += R
```

No live bytes are refunded and reacquired. The World/domain total is unchanged for a fully covered transfer; partial coverage still requires the existing `R-C` admission. Disposing installed storage does not replenish original P. `TryReserveProducerStorage` currently requires the outstanding scalar claims to equal the retained data claim: settle temporary/return claims first, or implement an exact partition-aware transaction. Do not bypass that equality or pass all `C_op` as D. The current receiver constant and local eight-byte helper formulas are not a qualified whole-graph layout.

The planner's current `SceneLoader.StartPlanPreparation` callbacks reserve workspace separately through `SceneExpansionLimits`. For the selected path, make those callbacks consume the exact **preclaimed original-root partition**, using the existing lower preclaimed receiver mechanism, rather than reserving already-covered asset/plan D again. Replace the retained-asset heuristic in `GetRequiredByteReservation` with the accepted retained partition. Preserve exact owner/phase construction elections: the root's current single pending-bundle construction check cannot simply be reused for both plan and bundle receivers. This is an extension of the Scene-owned funding adapter, not an Assets upcall or another domain credit.

## 5. J/K closure: required action for each non-size-bounded term

The following are implementation/evidence requirements, **not assertions that the required capability exists**.

| Term | Required action; fail-closed boundary and retry condition |
|---|---|
| Whole controls and construction/unwind | Compile the complete real slot/owner/invoker, not an empty skeleton. Enumerate whole-root and per-operation layouts, constructor-generated allocations, TCS/Task, delegates, gates and unsuccessful construction prefixes. Keep construction provision through actual factory/attachment unwind. No owner may be constructed as an allocation probe before its construction envelope is funded. Retry admission only with final target-specific layout/code-generation evidence and complete original-P fit. |
| Cold state, locks and BCL helpers | Assign schema/identity/static/comparer/Enum/sort/generic initialization and contended Lock/Monitor storage to an existing independently funded initialization/lifetime owner or to J before first access. A warm run or `EnsureInitialized` call is not a grant. For remaining planner/helper builders, dictionaries, priority queues and immutable copies, establish selected BCL capacities and old-plus-new peaks or replace the actual sites with exact owned storage. Declared capacity/count does not bound runtime backing. |
| Assets Tasks/ValueTasks and continuations | Replace only the selected internal `LoadDescribed*`/payload/reference-validation wrapper chain with ordinary operation progress; retain public Task signatures and ordinary unselected paths. The Runtime driver owns the lower single-consumption token, registration, `GetResult` return/unwind and accept/advance calls. Include completion-before-registration-return, queued/running/returning callbacks, source reset/recycle and generated/native suffixes. A single slot bounds logical operations, not these carriers. Derive actual coexistence from the final implementation; otherwise do not admit. |
| Context and caller token graphs | Preserve initial entry isolation and logical default/nondefault/suppressed ExecutionContext behavior, AsyncLocal notifications/reentrancy and lack of caller SynchronizationContext capture. A non-flowing wake plus explicitly owned context invocation is a mechanism to prove, not a mechanical `UnsafeOnCompleted` or suppression fix. A sourced token/context may retain an arbitrary mutable caller graph, independently of lower cancellation registrations. Require an explicit accepted ownership/attribution boundary with a real lifetime-paying owner, or a separately reviewed narrower admission contract. Neither capability is supplied by today's receipt. Do not substitute `None`, a proxy token, or silent context loss. Unsupported branches refuse before acquisition. |
| Diagnostic and fresh failure construction | Catalogue actual codec catches, `BinarySchemaDiagnostic`/Path copying, Store/view exceptions, interpolated messages, schema/type/enum/numeric formatting and runtime/culture/pool cold paths, including a primary still live while cleanup fails. Keep existing diagnostic/exception ordering. `EbinReadDiagnostic` is a value; `BinarySchemaDiagnostic` is a class. A bounded internal fact may remove a specific lower formatting site only after its owning error mapping is reviewed; it cannot replace the original public failure contract. |
| Exception/EDI/Task-fault retention | Enumerate original exception origins, captures/rethrows, Task holders/lists/EDI/native stack/keep-alive capacities and publication overlap for the selected runtime. Carry originals as owned state between ordinary steps rather than recapturing through intermediate Tasks. That removes some sites, not the initial/final capture. `GetResult` followed by `TrySetException` is not a non-copying relay; same-result-type `TrySetFromTask` does not bridge the existing heterogeneous Tasks. Foreign graphs and caller mutation of an exposed exception's `Data` defeat a fixed retained K while an engine alias survives. Require a real independently funded lifetime/transfer boundary or an explicitly reviewed error-contract change; absent it, no all-outcome profile is available, even for an otherwise valid payload. Two exception fields are not a byte bound. |
| Real read, backing cache and shared dispatch | Require a qualified **original-Store/backing-lifetime** read capability before selecting this Scene path. Existing FileStream/RandomAccess generation ownership and reusable I/O machinery are not prepaid by declared wire bytes; no current grant is demonstrated. The [file-handle](m03b-assets-file-handle-release-acknowledgment.md) and [file-I/O](m03b-assets-file-io-port-implementation-closure.md) proposals specify the missing whole-generation acquisition/read/retirement boundary. Its typed array/range use, actual completion, lower publication return and cache/dispatch transfer must be implemented and funded, including concurrent public users of that backing. Do not add a `Memory<T>` bridge or wrap/migrate an already BCL-owned generation. The general worker profile remains unclosed; the later no-worker **offline tool** disposition does not apply to async Scene preparation. |
| Native close and late release | Native input-use end, file-reference consumption, lower control retirement and caller/accounting return are separate facts. Require the actual one-shot native disposition before accounting: consumed, proved retained, or indeterminate, with exact generation/attempt. Do not infer it from normal `Dispose`, `IsClosed`, another SafeHandle or a rooted FileStream. Final release can occur during failed admission or after Store/World logical close. No uncertain retry/reopen/handle-number probe. Until whole-generation construction/unwind and final close are supported by the real lower owner, refuse selected acquisition; no physical-close failure observation is asserted here. |
| Planner and Rendering helper suffix | The selected `SceneExpansionBuilder` count/fill/expand/Prefab-cache Tasks and ValueTasks, bundle loops, Atlas validation, Material texture-set work, Mesh/Skeleton Retain/validation/finally, immutable construction and cleanup remain higher-owned. Convert their selected suspension plumbing to operation-specific progress in the existing builder/root, preserving algorithms and traversal, or provide their genuine enclosing-invoker joins. Prepay field initializers and captured reservation delegates before construction/invocation. End helper interests after their **callers** observe return, not after Assets reports a lease. `_supplierProducersUnjoined` may be replaced only by the complete fulfilled supplier/return set. |
| Public publication and final root tail | Keep the sole existing public promise and outcome election. `TrySet*` may expose the result before its caller returns; retain Scene-owned publication and invocation provision through that return, context restoration, gate exits and any remaining Scene access. The [external-observer provenance rider](m03b-scene-preparation-handoff-closure.md#7-public-task-producer-exact-clr-gate-and-proposed-closure) is a proposed ownership clarification, not an implemented blanket exemption: externally induced dispatch and caller AsyncLocal bodies need their exact accepted attribution. Without it or another real funding capability, unrestricted observer work cannot be bounded by a Scene constant. Scene-created callbacks, bookkeeping and fault holders remain charged. |

The runtime-source findings in earlier proposals are **reference-lane analysis only**. This task did not inspect or run a newly selected CLR/native binary, ThreadPool mode, filesystem or AOT artifact. `global.json` selects an SDK, not all those qualification inputs.

### Actual-return protocol

The root's real ordinary caller must observe this sequence for each child:

```text
prepaid construction/start/registration/invocation interests
  -> lower read actually stops consuming its owned destination
  -> single consumption returns/unwinds, including reset/recycle
  -> Assets accept/semantic/cleanup steps return/unwind
  -> exact result/data transfer is acknowledged
  -> the owned logical-context invocation and restoration return
  -> old child aliases and registration/return interests end or transfer
  -> original child temporary-claim handback
  -> parent-only tail under its still-live original provision
```

Ordering between independent readiness/publication events can vary; all required joins must hold. Registration uncertainty retains its interest. Running/pending-wakeup election needs a synchronized exit recheck so a completion/close request cannot be lost. Do not start a successor generation while an old child interest survives.

This avoids an infinite observer chain only if the residual tail really is parent-only, or has a proved no-child-access/no-new-child-allocation suffix. Source/IL/native analysis must include the enclosing dispatcher, not merely a callback-local `finally`. Any transfer of surviving lower controls requires a grant already owned by that backing/runtime lifetime. Charging the next Scene operation is not such a transfer.

At final preparation settlement, retain shared/root provision until body and producer roles both end. Only then invoke existing use release. `WorldGuidUseControl.Release` and the domain's exact-parent/terminal-return machinery preserve their lower retirement fences; they do not discharge unjoined Scene work, fund its exceptions, or authorize early use disposal. Keep activation's independent captured-P admission unchanged.

## 6. Failure, cancellation and cleanup protocol

1. **Preserve selection.** Keep synchronous usage/shape and pre-cancellation precedence. After admission, preserve the selected original token/primary execution exception. Stop issuing new work on cancellation, but do not free a destination, backing or read record while an accepted native consumer can still use it. A canceled observation is not native completion.
2. **Own even zero-credit prefixes.** Track temporary-reference-held, typed lease/wire credits, initial/target decode credits, candidate construction and result/lifetime authority separately. Acquisition refusal with a temporary reference is an operation cleanup path, not “nothing acquired.”
3. **Record irreversible progress before accounting.** Prevalidate/elect the exact Store-credit return without holding admission gates over physical cleanup. Release an operation-owned backing reference once, record the returned/nonfinal or native disposition, then commit corresponding Store accounting. Once the issued lifetime owns those credits, use its existing state machine only. A successful lower release followed by accounting failure retains accounting debt, not permission to close again. If a future actual selected pin needs this seam, its pre-handle rollback must follow the same order; today's public pin catch is not evidence that it does.
4. **Continue independently safe cleanup.** Attempt unrelated acquired siblings after a failure, outside gates, without clearing a failed owner or allocating an exception list/recovery node. Preserve the first original cleanup failure and bounded scalar additional facts. The complete retained graphs and capture work still require K; sampling limits alone do not provide it.
5. **Publish the correct outcome.** Original execution fault precedes a distinct first cleanup fault in the existing preparation channel. Successful cleanup preserves cancellation/token. Cleanup failure faults an otherwise canceled operation. Publication failure retains the original root and obligations; do not recursively allocate/complete a replacement promise or report successful rollback after possible publication.
6. **Quarantine only actual debt.** Stopped-but-unreleased state retains its original claim and exact Assets/Scene owner through Store/World/domain closure. Still-consuming work remains live and nonterminal. Repeated close observes irreversible progress and retained failure, never silently retries. Ordinary successful work must have a finite supported return route rather than permanent quarantine.

The existing [pre-lease recovery proposal](m03b-assets-prelease-recovery-owner.md) supplies the progress-owner rationale, not a complete numerical K. Chapter 02 still links to an absent Chapter 11 “M03 4.7” recovery-delivery definition. Exact original/cleanup delivery, repeated-close and retained-state authority require owner-contract closure before activating this path; this proposal creates no public exception type or scalar-success fallback.

The [retained-input analysis](m03b-assets-retained-input-budget-closure.md) remains material with one source correction: current backing directly retains registration/digest/effective scalars, **not the original options/path object**. Registration still retains arbitrary caller codecs/origins through its maps. Per-read factory provenance excludes arbitrary executed codec bodies, not those retained graphs. A qualified selected backing therefore needs prior whole-retained-graph provision or a separately reviewed closed composition. Do not prune/rebuild a public registration, guess its graph size, or import the unrelated two-codec seal to obtain it.

## 7. Per-boundary validation and evidence

These are future checks on implemented production boundaries, not results from this artifact. Use existing owning NUnit/VSTest projects and [ordinary commands](../project/Building.md), Debug before Release, related filters together, fresh TRX and nonzero selected/executed membership. Pure sizing/scan checks and lower-owner checks can precede Scene availability; they do not establish an executing preparation root. Do not fabricate `PreparedScene`, a profile/grant, Scene producer, snapshot, read-return acknowledgment or failure hook to get past a closed gate.

| Boundary | Required distinguishing checks/evidence |
|---|---|
| Original-P issuance/election | Checked overflow, unavailable profile, exact fit/one byte short, occupied slot, stale generation, duplicate start and close during construction/installation. Use independently derived baseline plus complete delta; short trials must actually reach the named boundary. Assert no reference/credit/read before admission and no second World/domain debit. Existing `WorldSceneReceiverTests` supply real lower-use/receiver patterns, not Scene-root success. |
| Described acquisition | Real packages through exact factory receipts, stale-current refusal, old anchored backing across replacement, disposed source/Store, and temporary-reference refusal cleanup. Extend `AssetStoreTests`, `AssetStoreRegistrationTests`, `AssetPackageLeaseTests` and `ScenePrefabPackageLeaseTests` as applicable. Preserve ordinary custom/Brotli loads and current Store policy; selected paths never fall back. |
| Owned decoder scan | Literal expected triples for repeated/first-occurrence order, same ID with different schema **and major**, zero/one/two/odd/even occurrences, exact/short destinations, overlap refusal and immutable publication. Current scanner regression covers only 0–3 occurrences; it is not owner integration evidence. Test actual selected decoder routing only once its real owner/grant can be admitted; ordinary `Scan` must remain compatible. |
| Data prefix/capacity | Independently derived catalogue functions, constructor/copy coexistence, target bound larger than parent, duplicate Mesh stream, invalid Omit, late hierarchy/Skeleton rejection and fixed-limb Mesh boundary fixtures. Reconcile capacities/compiled lowering with functions; observed allocations corroborate, never define, maxima. Do not use production output as the only oracle. |
| Actual I/O and cancellation | Real synchronous and suspended completion where supported, short-positive reads/EOF, cancellation before issue and during accepted work, submission/registration races and source single consumption. Verify actual input-use end separately from readiness and lower publication/return. If the real filesystem/stimulus cannot reach a required schedule safely, record it as unsupported, not passed. |
| Installation/helper return | Root lease → builder, Prefab context check → cache, Atlas page/Material texture pending → immutable set, Mesh Retain with and without Skeleton, helper failure/cancellation/finally, destination close racing acknowledgment. Verify the same retained claim moves once, helper temporary provision survives actual return, and a second operation cannot spend old returning interests. |
| Cleanup/physical release | Actual source/Store replacement/close schedules, zero/admitted credit rollback, nonfinal versus final backing release, successful lower release before accounting and independently safe sibling cleanup. Constructor-allocation and genuine close-failure observations require a supported real stimulus; no fabricated OOM, corrupt private counts, throwing allocator fake, repeated uncertain close or managed-Dispose-only physical-release claim. |
| Root publication/World retirement | Actual default/nondefault/suppressed context, AsyncLocal notification/reentrancy and real observer-dispatch holds; Task terminality must not refund an active publisher. Preserve exact retained receiver across World/domain close and GUID nonreuse until real return. Existing terminal tests establish only their exercised lower boundary. |
| Positive consumers, later | After full admission review, actual public Scene/Prefab and static Mesh/Sprite/BindPose preparation, activation, real camera and CPU extraction. Preserve existing default-cap/nonempty-binding, package identity and pixel-17 retention expectations. Do not modify the five unrelated dirty tests in this assignment or weaken fixtures to fit a smaller profile. |

Baseline managed behavior of the reader reductions is previously reported; it was not rerun here. No selected native/AOT/consumer evidence or numerical `C_op` follows from test-source presence.

## 8. Hard prerequisites and implementation order

| Owner/boundary | Current disposition and condition for retry |
|---|---|
| Assets original backing issuer | **Blocked for qualified Scene use.** Supply the actual original grant/root for registration/backing/opening/shared-read controls across every old, failed and returning generation. Tentative same-envelope bootstrap guidance is not the maintained wire-budget contract or an implemented general grant. No Scene purse can repair this lower issuer. |
| Assets async read/native close capability | **Blocked.** The required lower mechanism is the already proposed **per-original-Store package-I/O port**, with its dedicated package worker, typed destination ownership and whole-generation native disposition; no alternative Scene worker/dispatcher is selected here. Complete its provider/runtime/worker, completion-publication and actual termination/control provision before it can serve this operation. That worker profile is unresolved; caller-executed synchronous tool I/O is not a substitute. Reconsider only on that real compliant lower capability and target-specific evidence, not another unchanged FileStream probe. |
| Context/token/original-fault/publication ownership | **Blocked.** Resolve the specific foreign-graph and mutable-exposed-original attribution/error boundary in sections 5–6. Ordinary signatures provide no independent graph grant. If retaining arbitrary current inputs/original cached errors is mandatory and no paying lifetime exists, a complete finite qualified lane cannot be admitted; further data-array reductions do not change that result. Any narrowing/error change requires explicit owner review, not an inferred exception from this proposal. |
| Final operation/root profiles | **Open.** Compile final whole layouts and all data/J/K paths on frozen inputs; derive useful full fit under unchanged legal P and World/domain ceilings, including failure and actual return. No constant, fraction of P, sampled peak or declared BCL capacity substitutes. |
| Scenes planner and Rendering receiver | **Open.** Complete all real higher claims/initialization/helper joins and exact D/Q transfer. Preserve null footprint, resource-preparer refusal and bundle-settlement refusal until their respective full closures are reviewed. A Scene-only read seam does not clear a Rendering consumer gate. |

**Next concrete implementation action, in a separately authorized batch:** implement the checked per-kind data/retained-partition functions from the existing catalogue and finalize the above root-slot/Assets-control field and transition layouts against their real call sites. Compile/inspect without constructing a Scene preparation. These are prerequisites for assigning J/K, not a numerical profile by declaration.

Then implement **one complete installed-read slice**—original-P election, acquisition inside the installed owner, real lower read consumption, selected fixed-buffer decoder, automatic inspection, successful issue/installation and all cleanup/return paths—only when its lower/input/fault prerequisites are fulfilled. Do not land an empty “owner” or placeholder read/return port as that deliverable. Prove this slice before converting the selected transitive planner/helper suspension machinery; no broad planner rewrite should conceal an unresolved lower boundary. Admission remains unavailable at every intermediate revision.

Before a later change can make any footprint usable, review the final compiled source/profile inputs, exact closed membership, all independent exclusions/grants, all-outcome K, retained receiver and actual-return proof together. Rehash before applying that disposition. A missing term keeps the existing pre-acquisition refusal; success-only availability is not selected.

### M05 and qualification separation

This recommendation enables future M03-B **CPU asset ownership implementation**, not a current Scene producer. Dependency-ready M05 CPU kernels/layout, shader and source-adapter work can continue under their own contracts. World-bound extraction, diagnostics and native pixels still require actual committed/restored Scene/camera/CPU-extraction producers and, where applicable, the real M04 GPU path. Do not infer them from the bounded Static-Mesh tool, empty-Scene restore or helper tests.

Apply [Chapter 35](../design/35-implementation-roadmap.md#m04m05-capability-and-observation-sequencing): managed implementation, native/NativeAOT observations, public-consumer observations, M03-B/M05 acceptance and M08 release qualification remain separate. Missing real native stimuli/capabilities retain their exact gate and retry condition; no synthetic producer closes them.

## 9. Precise proposal input freeze

UTC `2026-11-20T09:51:49.452977Z`; HEAD `5b894adeb57e8eead63643e5fbd673b68da07dd9`. This is a bounded **decision-input** freeze, not a repository inventory, complete executable closure or runtime/native attestation. Existing proposals are frozen as current guidance/context, not accepted maintained contracts. The new artifact itself is excluded.

For each group, hash raw file bytes with SHA-256. Its group digest is SHA-256 of UTF-8 without BOM/trailing newline over ordinal-sorted `relative-Windows-path<TAB>lowercase-raw-file-SHA256` lines joined with LF. ALL is the union. Prefixes below apply until the next group. Recheck HEAD, path membership, raw bytes and dirty state before disposition; changed inputs require reassessment.

```text
[C .]
.github\copilot-instructions.md
.github\workflows\ci.yml
Directory.Build.props
Directory.Build.targets
Directory.Packages.props
global.json
sources\Directory.Build.props
tests\Directory.Build.props
docs\project\Building.md
docs\project\Roadmap.md
docs\design\02-dotnet-api-memory-and-ownership.md
docs\design\04-capacity-and-workload-profiles.md
docs\design\11-assets-import-streaming-and-packages.md
docs\design\16-scenes-transforms-and-cameras.md
docs\design\29-project-setup-build-and-deployment.md
docs\design\30-public-api-review-surface.md
docs\design\35-implementation-roadmap.md
[P docs\proposals]
m03b-scene-assets-allocation-site-catalogue-addendum.md
m03b-scene-assets-closed-read-allocation-authority.md
m03b-scene-actual-invoker-return-closure.md
m03b-assets-prelease-recovery-owner.md
m03b-assets-prelease-bootstrap-and-close-closure.md
m03b-assets-file-handle-release-acknowledgment.md
m03b-assets-file-io-port-implementation-closure.md
m03b-assets-retained-input-budget-closure.md
m03b-assets-batch-b-worker-envelope-rectification.md
m03b-scene-preparation-handoff-closure.md
[A sources\TerraFX.Assets]
AssetId.cs
AssetReference.cs
AssetCodecContext.cs
AssetCodecRegistrationReceipt.cs
AssetEnvelopeDecoder.cs
AssetLease.cs
AssetLeaseLifetime.cs
AssetOwnedReadDescriptor.cs
AssetPackageBacking.cs
AssetPackageLease.cs
AssetPackageReader.cs
AssetPayloadReader.cs
AssetReferenceScanner.cs
AssetStore.cs
AssetStore.PackageLease.cs
AssetStore.PackagePin.cs
AssetStore.ReadDescriptor.cs
AssetStoreOptions.cs
AssetStoreRegistration.cs
AssetStoreRegistrationBuilder.cs
AssetStoreRegistrationFamily.cs
AssetStore.Bounded.cs
AssetStoreBoundedBootstrap.cs
AssetStoreBoundedProvision.cs
TerraFX.Assets.csproj
[F sources\TerraFX.Core]
BinarySchemaDiagnostic.cs
EbinCodec.cs
EbinReadLimits.cs
EbinScanState.cs
EbinStructuralReader.cs
[S .]
sources\TerraFX.Core\CompiledContentSchemaRegistry.cs
sources\TerraFX.Core\CoreContentSchemaCatalogue.cs
sources\TerraFX.Assets\NativeAssetSchemaCatalogue.cs
sources\TerraFX.Runtime\Scenes\SceneSchemaCatalogue.cs
sources\TerraFX.Graphics\RenderingSchemaCatalogue.cs
sources\TerraFX.Runtime\Animation\AnimationSchemaCatalogue.cs
[R sources\TerraFX.Runtime\Scenes]
PreparedScene.cs
PreparedScene.Preparation.cs
ScenePreparationFunding.cs
SceneLoader.cs
ScenePrefabExpansionPlanner.cs
SceneExpansionPlan.cs
SceneExpansionLimits.cs
SceneLoadOptions.cs
PreparedAssetsLifetime.cs
SceneAssetReader.cs
SceneEntityReader.cs
SceneHierarchyReader.cs
SceneHierarchyValidationWorkspace.cs
ScenePreparationDataBounds.cs
SceneSupport.cs
SceneAssetCodec.cs
ScenePrefabAssetCodec.cs
ISceneExpansionResourcePreparer.cs
[G sources\TerraFX.Runtime]
Rendering\Renderer.CpuExtractionSupport.cs
Rendering\RendererAssetCodecProfiles.cs
Rendering\RendererAssetReadMode.cs
Rendering\RenderSceneAssetBundle.cs
Rendering\RenderAtlasPageLoader.cs
Rendering\RenderMaterialTextureLoader.cs
Rendering\RenderMeshAssetBinding.cs
Rendering\RenderMeshAssetCodec.cs
Rendering\RenderTextureAssetCodec.cs
Rendering\RenderAtlasAssetCodec.cs
Rendering\RenderMaterialAssetCodec.cs
Animation\AnimationSupport.cs
Animation\AnimationAssetReader.cs
Animation\SkeletonAssetCodec.cs
[W sources\TerraFX.Runtime\Entities]
World.cs
WorldLifetimeControl.cs
WorldLifetimeControl.CosmeticCredits.cs
WorldGuidUseControl.cs
WorldAdmissionDomain.cs
WorldAdmissionDomain.SceneStorage.cs
WorldAdmissionDomain.ScenePreclaimedWorkspace.cs
World.ScenePreclaimedWorkspace.cs
WorldSceneStorageReservation.cs
WorldGuidRegistry.TerminalAnchor.cs
WorldAdmissionDomain.TerminalAnchor.cs
[T tests]
TerraFX.Assets.Tests\AssetStoreTests.cs
TerraFX.Assets.Tests\AssetStoreRegistrationTests.cs
TerraFX.Assets.Tests\AssetPackageLeaseTests.cs
TerraFX.Runtime.Tests\Rendering\RenderMeshAssetCodecTests.cs
TerraFX.Runtime.Tests\Scenes\ScenePrefabPackageLeaseTests.cs
TerraFX.Runtime.Tests\Scenes\SceneAssetStoreTests.cs
TerraFX.Runtime.Tests\Scenes\SceneHierarchyValidatorTests.cs
TerraFX.Runtime.Tests\Entities\WorldSceneReceiverTests.cs
[D tests\TerraFX.Runtime.Tests\Scenes]
PreparedSceneAssetRetentionTests.cs
RenderSpriteCapacityTests.cs
RenderSpriteComponentTests.cs
RendererExtractionTests.cs
SceneAssetStoreTests.EngineHost.cs
```

| Group | Files | SHA-256 |
|---|---:|---|
| C | 17 | `61d9531dbfa8d3a889eb24b29a615539170cc574ed46f66a7aa0c080721ca9d9` |
| P | 10 | `63118f6821ae18d01bdc78ca66052eae1e8cfbebd2661666abfba97d28d3122e` |
| A | 25 | `a3ddf491d41d54061c6a9cced5fc9632de0ef1327800474b32447fae43ffe6a9` |
| F | 5 | `75f25ab2acf23335aae73cb491b954bea989211fe73e96aebb30ba69e8d0bc23` |
| S | 6 | `9ce6a35219cab4410499867a67ca95cb346e24ed38f9ee2b990322a12e98e31a` |
| R | 18 | `c2ded1985fa781db98bdff3e8179385b56d3899c8658b8e61204903959c83d45` |
| G | 14 | `615efa2d285cf7012ddf011013b68ec90170274e97cf78429881307a5a6f6e5f` |
| W | 11 | `207a520be0fafdf8081028e9259dbb123646db98004ef284a60c26566f191e27` |
| T | 8 | `eaf9406eab12e78238fd38139b90fb2d2ee1633c662d74c759e8990d7311a0d9` |
| D | 5 | `0247a70f5dedcaac56865e99640c263109f72120dff8cf7a79b78257305b2162` |
| ALL | 119 | `a6194eebe2e39fdd190b07a8bc9aa2dd48637ce73d58c1e3dbbbb848223cda82` |

Schema/registry-source raw hashes, beneath `sources`:

| Path | SHA-256 |
|---|---|
| `TerraFX.Core\CompiledContentSchemaRegistry.cs` | `cfe040b8e51cb811aecccbb8b0bec0d2993b7a726a5b179968ce0db1415eae08` |
| `TerraFX.Core\CoreContentSchemaCatalogue.cs` | `91cf1aa2ff4feaba898d83e587d4be403de6cfea97cfbb59fb15d5b67edf2853` |
| `TerraFX.Assets\NativeAssetSchemaCatalogue.cs` | `6b9537ea8cefebb48b5dc24261a8c03fb059e3314e7da089f29a639d9e55620a` |
| `TerraFX.Runtime\Scenes\SceneSchemaCatalogue.cs` | `cf0253cf1c207644f0b6c76b0392977e02b0939ff355ed825f38d42705f2b32a` |
| `TerraFX.Graphics\RenderingSchemaCatalogue.cs` | `de25a8c0e249ff9ab5b592fe848c85429c004fe629694c64ef1afef3ba731614` |
| `TerraFX.Runtime\Animation\AnimationSchemaCatalogue.cs` | `c4447f84db8f60f21faf99c6ac9e8f67f13cf4de98f066c9feee8d07820c0daf` |

The five preexisting dirty files are preservation inputs, **not acceptance of their unrelated changes**:

| Path beneath `tests\TerraFX.Runtime.Tests\Scenes` | SHA-256 |
|---|---|
| `PreparedSceneAssetRetentionTests.cs` | `e342237b7645be805dd924855b2239907773d31753d22b8b2261bf26c20452f4` |
| `RenderSpriteCapacityTests.cs` | `caa6610cf55f8fffa077745fd96baa45fcecfed2fd051cc763901aeed5500239` |
| `RenderSpriteComponentTests.cs` | `8e65b71b5ac1e0ef9d1f47aa56963054d091df8c229e74ddbb1cae37bb59ab60` |
| `RendererExtractionTests.cs` | `abaeea4d0f6be941af73e640b7fce41ec753d2199654343fa4668d54ec20fc6a` |
| `SceneAssetStoreTests.EngineHost.cs` | `4c0841a40295a891b68974fddd8078dfd0181c31b35ab0bcb0b5114fe0fd1039` |

Fixed-limb Mesh predicates and their edge tests are implemented and behavior-tested; they remove only the selected BigInteger arithmetic term. Package-pin construction now creates the complete lease carrier before acquiring its backing reference or charging lease capacity, eliminating the old post-acquisition allocation catch window. The planner now uses an inline maximum-depth AssetId path, defers exact output builders/reference-list allocation until after its reservation callback, and avoids replacement empty builders after immutable ownership moves; the root builder and Prefab cache remain unclaimed before counting. Measured-layout regressions fix the PreparedScene root at 488 bytes, SceneExpansionBuilder at 448 bytes, SceneExpansionPlan at 160 bytes, and the Prefab lease Dictionary shell at 80 bytes on the selected .NET 11 x64 runner; a 65,536-entry collection-only test keeps Dictionary object plus cumulative backing-allocation traffic within `80 + 256*n` at every prefix. The Assets test project passes 69/69, the focused Scene hierarchy workspace/planner selection passes 12/12, a separate seven-test nested expansion/typed-read/refusal selection passes, the maximum-depth path/cycle selection passes 4/4, and the complete Scene hierarchy/workspace/carrier selection passes 16/16 in Debug and Release. These observations cover fixed-buffer scan partitions, hierarchy and planner/cache carrier measurements, shared sixteen-byte array sizing and the Planner's depth boundary; they do not prove the PreparedScene constructor's referenced objects, lease, decoded Prefab, operation, or Scene funding. The Scene owner/J/K, lower I/O/close, numerical `C_op`, actual-return, native/AOT and positive-consumer gates remain open. The five unrelated dirty Scene/Sprite tests remain unchanged from their listed hashes.
