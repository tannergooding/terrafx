# M03-B Scene preparation workspace closure

**Status: tentative implementation proposal retained for review, not implementation or milestone acceptance.** This document complements `m03b-scene-preparation-budget-accounting.md`, including its selected section 9 activation delivery tail. It does not replace that decision, promote a maintained contract, or certify the dirty implementation.

## 1. Disposition

Keep the exact captured `P = SceneLoadOptions.MaximumPreparationBytes`, its existing 67,108,864-byte default and range, independent preparation admission of `256 + P`, per-World aggregate enforcement, and separately admitted activation using the same captured P. Close the remaining blocker by making the existing prepaid use an **enforced purse before allocation**, not an estimate checked after counting.

Recommended owner design:

1. Preclaim entry/result/publication capacity and option-copy overlap before constructing them.
2. Precreate the existing `PreparedScene` carrier as the operation's stable retention root; publish it only on successful preparation. Keep its one preparation use through producer quiescence and the prepared/admitted body lifetime.
3. Give the planner and Rendering helpers a private, by-value funding port to existing issuers. Every allocating callee must either consume an existing named grant or preclaim its complete next allocation/copy/growth peak.
4. Transfer only identified retained allocations/capacity into R. Add an atomic **active-claim-to-R** transition to the existing Entities accounting, rather than returning a live claim and reacquiring it.
5. Root partial cleanup and retained R receivers before resource acquisition. Return credits only after actual consumption/release, including after closure or revocation.

No smaller cap, adaptive fallback, global allocator, new public option, schema, diagnostic ID, test-only producer, or new process-use role is needed. The fixed private footprint catalogue and the specific missing lower-owner transitions in section 7 must nevertheless close before claiming the complete path funded. This proposal assigns no guessed bootstrap or callback constant.

## 2. What the current bytes establish

Paths in the following trace are under `sources\TerraFX.Runtime` unless qualified otherwise. Section 9 freezes the working-tree bytes, including dirty and untracked inputs; older snapshots in the previous proposal do not identify this review.

| Stage / owning source | Allocation and ownership facts | What existing funding establishes |
|---|---|---|
| `Scenes\SceneLoader.PrepareAsync`; `SceneLoadOptions.ValidateShape` | Entry reads the explicit cap and acquires a use before `ValidateAndFreeze` or invoking `PrepareCoreAsync`. However, a supplied nonempty instance GUID reaches `IsUuidV8`, which calls `Guid.ToByteArray` during the supposedly nonallocating shape check, before admission. | Exact independent admission exists. The valid-instance-ID shape path is not allocation-free. |
| `SceneLoadOptions.ValidateAndFreeze`; `SceneLoadOptionsSnapshot` | Freeze makes a reference array with `ExternalBindings.AsSpan().ToArray()`, sorts it, then creates immutable backing with `[.. externalBindings]`. The two backings coexist. `SceneExternalBinding` is an immutable reference type; this does not clone its Entity value or authorize borrowing the caller's backing. There is no workspace claim for either copy. | Admission reserves a purse, but does not check or debit this peak. Binding count has no separate public ceiling; it must fit P, not an invented count limit. |
| `SceneLoader.PrepareCoreAsync` / `PreparePlanCoreAsync` | Async machinery, captured storage/workspace delegates, validation work and `new PreparedScene` follow freeze. `ValidateComponentRegistrations` creates and grows a `HashSet<ContentSchemaReference>`. The use's retained-owner reference successively names the loader, root lease, builder, plan and result; none is currently an explicit whole-operation cleanup graph. | Immutable P reaches `SceneExpansionLimits.MaximumExpandedBytes`. These carriers/sets are not individually claimed. The loader reference alone does not retain its invocation's options or async locals. |
| `Scenes\ScenePrefabExpansionPlanner.PrepareAsync` / `PrepareTakingLeaseAsync` | The root Scene load is awaited before constructing `SceneExpansionBuilder`. Builder field initializers allocate List/Dictionary/builder carriers and default builder backing before its constructor body. | `_useControl` supplies rooting/quarantine hooks, not an allocation debit. The planner makes no `TryReserveWorkspace` call. |
| Builder `CountAndMeasureAsync`, `CountOccurrenceAsync`, `CountPlacementAsync`, `GetSelectorTargetSourceEntityAsync`, `GetPrefabAsync` | Counting traverses asynchronously, grows `_assetPath`, loads distinct Prefabs and inserts leases into a growing dictionary. Selector validation can load nested Prefabs too. Hits return the already retained lease value; misses load through the root Scene lease's exact package. Counters accumulate proposed byte terms, but do not claim them as work proceeds. | Count/depth checks and checked arithmetic constrain content. Only after counting does `GetRequiredByteReservation` compare its total with the selected byte limit. That is too late to authorize prior cache/frame/growth allocations. A miss can do work before that comparison; a hit does not create another decode or lease. |
| Builder `BuildAsync` | **The retained reservation is requested after counting, but before expansion**, at the start of `BuildAsync` after `CountAndMeasureAsync`. It precedes `_rootCounts`, exact-capacity expanded-array builders, map/identity arrays and most rewriting/validation work. | It is incorrect to describe the frozen source as reserving only after the entire build. This R can prospectively cover its declared output allocation capacity, but cannot retroactively admit counting. |
| Builder root poses, override layers and reference validation | Further allocations include resolved-override arrays, covered-root arrays, component-reference replacements, rewritten EBIN arrays and final copies, slot-resolution builders/objects/closures, local reference-binding objects/path copies, two local-binding reference arrays, and the final `[.. _referenceBindings]` copy while its source remains live. Successful build also constructs new empty builders when clearing fields. | `GetRequiredByteReservation` includes several such terms, but mixes retained state, temporary scratch, borrowed encoded-record lengths, future external-binding capture rows and `256 * prefabCount + 2048`. It is not a partition of actual receiver ownership. No separate claims establish pre-count or residual workspace coverage. |
| `Scenes\SceneOccurrenceIdentity`; Core `FoundationNameUuid` | Expansion creates formatted GUID/AssetId/name strings and may require boxing/concatenation carriers. Name UUID generation uses the existing Core recipe. For these bounded Scene names, its input fits the existing 1024-byte inline `RentedBuffer<byte>` path; it is not evidence of an ArrayPool rent on this path. RFC comparison also uses inline buffers. | Charge newly allocated names/formatting and actual carriers/call metadata, not imaginary pooled backing. Do not change identity bytes or introduce another UUID recipe. |
| `Scenes\SceneExpansionPlan` | Constructor validates and stores the supplied arrays, dictionary, leases and reservation; it does not defensively recopy those arrays. `AttachPreparedAssets` does allocate `PreparedAssetsLifetime` and its Lock. `MoveToImmutable` is an ownership move when capacity/count agree; collection expressions over existing collections must not be presumed moves. | Constructor equality between `RequiredByteReservation` and reservation bytes proves agreement between numbers, not the underlying ownership inventory. |
| `Rendering\Renderer.CpuExtractionSupport` | The participant forwards measurement directly to `RenderSceneAssetBundle.MeasureStorageByteReservationAsync`; measurement has no funding parameter. Preparation forwards distinct retained-storage and workspace callbacks. | The participant's fixed `OwnedByteReservation` is not a per-Scene variable scratch grant. No transfer or scratch claim occurs in its forwarding methods. |
| `RenderSceneAssetBundle.MeasureStorageByteReservationAsync` | Measurement allocates three ID arrays, a `meshMaterialSlots` array and a material-ID array, and loads Mesh assets before returning a number. It temporarily retains each Mesh's immutable MaterialSlots array after disposing that Mesh lease. It returns initial scratch + material scratch + the retained-entry estimate. | No claim precedes measurement's arrays/loads. Holding a MaterialSlots reference is not proof its decoded-owner credit remains held. Returning a number later neither funds that scratch nor makes scratch part of retained R. |
| `RenderSceneAssetBundle.PrepareAsync` | It claims initial workspace before three ID arrays, and later claims material workspace before material-ID arrays. But Atlas/Mesh entry arrays, Atlas/page helpers, Mesh/Skeleton retains and loads occur before `finalReservation`. Final R precedes the Material-entry array and later Material/texture helpers. | The top-level scratch claims are real: do not report all Rendering scratch as absent. Their formulas describe the ID-array phases, not all nested allocations. Entry arrays allocated before final R need prior identified coverage too. |
| `RenderAtlasPageLoader` / `RenderAtlasPageSet`; `RenderMaterialTextureLoader` / `RenderMaterialTextureSet`; `RenderMeshAssetBinding.LoadSkeletonAsync` | Atlas preparation allocates lease-builder/copy backing and a set carrier; validation allocates frame rectangles, a tree object with **two `int[16384]` arrays**, and growing per-page priority queues. Material preparation grows/copies lease backing and creates a texture-set carrier. Skeleton binding retains a Mesh handle even when no Skeleton is selected. | Assets owns loaded payloads through its budgets; these higher-owner arrays, sets, queues and call/cleanup frames do not thereby acquire a Scene grant. Neither bundle retained-entry formula nor scratch formula inventories the complete helper graph. |
| `Scenes\PreparedScene`, `PreparedAssetsLifetime`, `SceneActivationRequest`, `SceneInstance` | Success retains the same preparation use. Queue refusal does not detach it; accepted activation takes the plan/use, and success installs the plan in the instance. Existing request code releases the preparation use during body cleanup, separately from its activation delivery tail. Retained bundle leases can outlive plan unload or result disposal. | Adding preparation claims requires corresponding body/handoff releases; an unchanged `use.Dispose()` would encounter the active-workspace guard. Ending the preparation use must not end independently owned bundle R. Section 9 of the previous proposal still owns activation's distinct envelope/tail. |

### Distinguishing witnesses, derived without execution

**Option copies:** P=1 is a valid explicit request. With otherwise sufficient admission headroom and one valid nonnull `SceneExternalBinding`, current entry admits 257 operation bytes and then starts the copies without a workspace test. Under Chapter 04's array rule, on a 64-bit profile each one-reference backing costs `64 + RoundUp16(8) = 80` accounting bytes; their overlap alone is 160, before async/result costs. No root asset or large Scene is needed to distinguish this from enforced preallocation funding. This is a static path witness, not a reported test result.

**Atlas scratch:** its two tree arrays alone cost `2 * (64 + RoundUp16(16384 * 4)) = 131200` accounting bytes. They are newly created by higher-owner validation, not Texture pixels owned by Assets. The existing top-level ID-array allowances are not a derivation of this peak. A successful default-P load would not prove a smaller funded P was respected.

**R timing:** the source disproves the broad “R is acquired after building” suspicion. The actual unresolved interval is entry/count/cache work before R, plus incomplete attribution of the work coexisting with or following R.

## 3. Accounting and representation rules

For one preparation, use the following private accounting names; they are not public declarations:

```text
P = F + X + T
remaining D_u operation charge = 256 + F + X

F = unassigned capacity still owned by the use
X = active or protected workspace claims, including cleanup/publication
T = capacity transferred out of this use, cumulatively
```

Claiming b changes `F -= b; X += b`; returning a genuinely ended claim changes `X -= b; F += b`. Neither changes the World/domain total: full P was prepaid. Ordinary disposal of transferred R does **not** replenish P, matching current `SceneWorkspaceReturnsToIndependentUseWhileRetainedStorageTransfersOut`.

Initially use only fully covered transfers, `C=R`. Moving already claimed retained capacity changes `X -= C; T += C; D_u.Bytes -= C`, while receiver-owned World storage increases by R, in one checked transaction. Free prospective capacity may first be claimed, then transferred by that same transition. Preserve:

```text
Q_W = other exact-World owned/reserved bytes
      + all independent-use remaining operation charges
      + all transferred Scene R, counted once
Q_W <= World.MaximumOwnedStateBytes
```

Raw tickets, other operations, retained uses, data-only publication tails and quarantines remain in the common total. Domain metadata, domain data bytes and process/count limits still apply independently.

The current aggregate foundation is useful, not missing: `WorldDomainReservation.IndependentUseBytes`, `WorldAdmissionDomain.TryReserveWorldGuidUseRaw` and `TryGrowWorldReservation` share the per-World headroom check. Workspace claims move allowance inside D_u. `TryTransferWorldGuidUseWorkspace` currently transfers **free `WorkspaceLimit`**, not `ActiveWorkspaceBytes`. It cannot directly transfer a claim protecting an already allocated cache or bundle.

Extend those existing Entities transactions with an active-claim transfer and exact rollback provenance. Do not release X while its objects are still live merely to make the free-capacity transfer succeed. Preclaim receiver-control creation too; construct it outside admission/storage gates, then install it by a value-only exact-generation transaction. A refused installation leaves the original claim/graph owned. Constructor/install rollback restores the original active or free partition, not an arbitrary current allowance.

There is a specific existing rollback edge to repair with that work: `TryReturnWorldGuidUseWorkspace` refuses when `reservation.IndependentUseBytes < bytes`, although the return adds to that counter. For an otherwise valid isolated transfer with P=4096 and C=3072, the residual independent charge is 1280, so that predicate rejects returning 3072. This is a source-derived boundary example, not construction-failure execution evidence. Verify the complete transaction, not just removal of that predicate.

Use Chapter 04's `ArrayCharge(n,s) = 64 + RoundUp16(checked(n*s))`, whole capacity, headers and coexistence. Planner/bundle local `ArrayBytes` currently round payload to **8**, not 16; do not copy that formula into the closure. An `Unsafe.SizeOf<T>()` value is suitable for the payload stride of an inline value row, not the managed size of a class instance. Shared empty backing and separately allocated zero-length arrays are different cases.

For growth, the old capacity stays claimed and the **whole new capacity** must fit before allocation/copy. For example, an `int` array growing 4→8 costs 80 old + 96 new accounting bytes, not old + a 16-byte delta. Release the old claim only after no engine consumer retains/reads that backing. Do not reclaim installed high-water capacity just because a count fell.

## 4. Selected owner protocol

### 4.1 Entry, freeze and stable retention

Keep nonallocating shape/scalar checks and pre-cancellation before admission. Replace the valid-GUID `ToByteArray` check with an allocation-free equivalent preserving its current version-byte predicate. Do not alter UUID acceptance, count limits or validation semantics.

After acquiring exactly `256+P`, preclaim the qualified entry/result/promise bootstrap, protected terminal/cleanup provision, and the next freeze peak before constructing them or invoking any async helper. These are parts of P, not additions to it; the control's 256 bytes are unavailable for this purpose. A valid P too small for the minimum entry envelope explicitly refuses before constructing that envelope. It is not an invalid option or a smaller-cap retry.

Precreate **the existing `PreparedScene` object**, internally in a nonpublic Preparing state, using the known root AssetId rather than requiring an already built plan. Anchor it through `WorldGuidUseControl` before freezing or loading. Extend it with bounded preparation-state/claim and publication/body-ownership fields under its existing gate. No second per-operation owner allocation, global registry, per-allocation claim object or new U is proposed.

Initially retain the current two-copy freeze and charge both full backings before the first copy. Sort/duplicate checks remain synchronous as now. Return temporary-copy capacity only after that array is no longer used; retain the immutable snapshot's claim through its last prepared/request consumer. Binding objects supplied by the caller are immutable input values, not newly cloned Scene objects. The newly owned array and retention bookkeeping still need funding. Eliminating a redundant copy can be a later measured improvement, not a prerequisite or assumed saving.

Make the anchored carrier retain the in-flight builder, currently owned root/Prefab leases, options, plan, callback construction state and failed-cleanup progress. Do not overwrite that root with just the most recently returned lease or plan. All nonescaped entry failures release their claims/use only after actual unwinding; after the stable carrier takes ownership, only that carrier unwinds.

### 4.2 Funding port and counting

Add a private by-value funding port to the existing `SceneExpansionLimits`/resource-preparer flow. It conveys no credits or allocation authority of its own. It selects a specific existing issuer:

- ordinary preparation: the exact `WorldGuidUseControl`;
- package measurement/restore: the caller's already admitted persistence workspace/candidate Scene grant.

Its operations are preclaim, release-after-use, and covered transfer to existing `WorldSceneStorageReservation`. Entities continues to own the accounting; Assets/Core acquire no Runtime reference or Scene callback. Any delegate/closure implementation of the port is itself prepaid; it must not allocate a guard before claiming that guard. Remove null-as-unlimited production routes, including measurement. Internal planner tests must use real existing owner reservations, not a fabricated test issuer.

Before calling the root load, claim the Scene-owned load-await/lease-holder provision and async layer. Before constructing the builder, claim its carrier, initial backing and nested invocation peak; constructor-body claims are too late for field initializers.

Keep traversal order, cycle detection, root-pose/override order, source-package identity and count/depth semantics. Use a fixed `_assetPath` buffer of `MaximumPrefabDepth + 1` entries, including the root. Charge it before construction. Each recursive async invocation must be preclaimed by its caller for that helper's qualified suspendable frame/call metadata before invocation, not after its first suspension. Return such provision only at proven producer quiescence; a terminal child Task alone is not proof its publication call has returned. A frame pool may retain its funded high-water provision until the enclosing preparation producer quiesces.

Replace only the Prefab cache's opaque automatic dictionary growth with a builder/plan-local typed, exact-capacity array table: positive AssetId supplies an empty sentinel; power-of-two capacity, at most half occupied, deterministic probing, checked doubling. Reserve the complete new entry array while old backing remains claimed, allocate/reinsert outside gates, publish, then retire the old backing. This keeps hit lookup and amortized growth bounded without maintaining a second implementation for tests or introducing a general collection/allocator subsystem.

On a hit, reuse the exact existing lease with no decode or duplicate lease claim. On a miss, fund pending carrier/await and any required cache growth **before** calling `LoadFromPackageAsync`; retain a returned lease in a charged pending slot before validation/insertion. Failure disposes that pending lease or retains it in the anchored graph. Unique-cache demand is discovered incrementally, not guessed from the final occurrence count: selectors can request assets while counting/validating.

Counters describe semantic demand, not permission to allocate. Byte exhaustion refuses at the first next claim even if all count limits still admit the content.

### 4.3 Expansion, copies and retained-plan R

Keep separate ledgers for temporary workspace and retained candidate capacity. After counting, claim exact-capacity output builders/arrays before construction; prevent automatic growth past those capacities. For reference bindings, use count-sized backing and an explicit live count, then fund any final immutable copy while the source remains live. `MoveToImmutable` moves backing and its existing grant; it does not mint another allocation charge. Remove the successful builder's unnecessary new-empty-builder resets rather than inventing a permanent allowance for them.

Preclaim helper work using immutable input facts:

| Existing helper | Required preallocation rule |
|---|---|
| Root poses | Claim covered-root bool backing, replacement component-reference backing and the newly owned RootPosition record graph/copy before each call. The 128-byte encoded record constant is not its complete object/temporary peak. Prefer the existing caller-workspace `EbinStructuralWriter` path for this fixed record instead of the convenience object-graph codec; preserve identical bytes. |
| `ScenePrefabOverrideApplier` | Before each rewrite, claim field-source backing from `outputFieldCount`, payload storage from checked field extents, candidate bytes from the nonallocating measure, literal/fixed-array copies, ancestor rewrite coexistence, and `SceneAuthoredComponentRecord`'s final defensive `ToArray` plus its object and component-reference replacement. Fixed-width Set/Omit do not grow the canonical input record, but this does not eliminate array headers or the final copy. `CalculateOverrideRewriteReservation`'s length multiplier is supporting payload evidence, not the complete inventory. |
| `ScenePrefabSlotBindingResolver` | Claim its exact child-slot reference array/builder, per-slot `ScenePlacementSlotBinding` objects and closure/call provision before invocation; returned bindings are temporary until mapped into the retained expanded rows. |
| `ValidateExpandedReferenceBindings` | Before local binding construction, claim both reference arrays, each binding object and its copied PropertyPath backing using actual path length. `SceneEntitySelector` and expanded binding rows are structs; do not invent per-row class allocations for them. The validator's 65-segment stack buffer and struct action are not heap arrays. |
| Component registration validation | Remove the temporary deduplication HashSet: repeated registration lookup has the same validation semantics and needs no growing deduplication owner. Do not add a competing schema cache. |
| Identity generation/sorting | Cover bounded name/formatting carriers and actual sorting metadata from the selected profile, while preserving the existing Core recipe and ordering. Do not charge a hypothetical full UUID pool buffer or change identity derivation. |

Derive final `R_plan` from retained expanded arrays/maps/identities, surviving owned rewritten component objects/backing/reference arrays, the actual Prefab cache **capacity**, lease-carrier provision not already funded by Assets, plan/control headers and receiver retirement capacity. Base component/property-path/schema backing still owned through Assets leases is not a second Scene allocation. Temporary root counts, traversal/validation arrays, resolved overrides, callback scratch and future activation external-binding capture rows are not automatically retained-plan R.

While the builder owns these objects, X protects them. Once the receiver/control and inventory are ready, transfer precisely that retained partition X→R atomically; keep unrelated scratch, options, producer and cleanup claims in X. Partial outputs remain owned on refusal. Measurement equality may validate the retained inventory across repeatable input, but cannot substitute for preallocation claims.

### 4.4 Rendering and measurement

Thread the same funding port through `ISceneExpansionResourcePreparer`, its extraction participant, the bundle, and the Atlas/Material/Skeleton helpers. Preclaim a callback's fixed construction/async envelope **before invoking it**; the callee then claims its variable work before each allocation. The fixed CPU participant reservation is not available for per-Scene spending.

Use these concrete phase boundaries:

1. Nonallocating component scans derive Sprite/Mesh counts. Claim all three initial ID-array capacities before allocation.
2. Before allocating Atlas/Mesh entry arrays, claim them explicitly. Before each Atlas or Mesh load/retain, claim the relevant higher-owner holder/call peak. Root in-flight entries and loaded counts in the charged bundle construction carrier, not solely async locals.
3. Atlas inputs expose page/frame counts before helper allocation. Claim builder/copy/set holders, all frame rectangles, both existing 16384-element tree arrays and the queue peak. Pre-size/reuse the queue from the known frame count so validation cannot grow an unclaimed queue. Count shared page/Atlas/Texture payload once, with Assets.
4. MaterialSlots lengths become known while funded Mesh leases remain held. Claim combined-material and Mesh-material ID backing before copying; include old/new growth if collecting incrementally. The Material helper can derive its selected nonnull slots before construction: the current Sprite branch selects at most one texture binding; PBR selects at most four, with deduplication. Preclaim actual holder/copy capacity and set/call provision, not an invented general texture limit.
5. Before creating Material entries, sets, or the final bundle, claim their carriers/backing. Retained entry arrays, page/texture set holders and their lease-array backing belong in `R_bundle`; validation rectangles/tree/queues, ID arrays and callback frames remain X. Transfer only the retained partition when safely owned by the receiver.
6. Preclaim `PreparedAssetsLifetime` and its Lock before attachment. Its retained bundle grant follows its actual last owner/lease, independently of the plan's result/use.

**Measurement must itself be funded.** Change the measurement signature to require the same workspace port and preclaim callback entry plus every array/load/copy peak. Do not retain `Mesh.MaterialSlots` beyond its lease by treating the borrowed immutable array as free. Copy IDs into claimed owned scratch before disposing the lease, or retain that lease through the last consumer; the selected minimal sequence is the former, processing one Mesh at a time with preclaimed collection growth.

Return **retained bundle R only**, not `scratch + R`. Retained R must include the nested holders just listed, not merely the three entry arrays. Measurement's own scratch ends after its consumers; future preparation scratch is separately claimed when used. Where caller admission needs a simultaneous peak estimate, carry that as a separately named workspace requirement with its own lifetime, not as inflated retained R.

The public `SceneLoader.PrepareAsync` does not call measurement first; it calls resource preparation. The measurement route is used by `ScenePersistenceRestorer.MeasureRestoredScenePlanStorageBytesAsync`, called by `WorldPersistence.PrepareLoadCoreAsync`. That route currently measures the planner before obtaining its temporary plan reservation, then invokes Rendering measurement after reserving the measured **plan** amount. Neither that sequence nor the returned resource number establishes preflight scratch coverage. Its noncircular persistence seed grant is a specific remaining owner gate in section 7.

### 4.5 Assets boundary

Keep package decoding/backing with Assets. `AssetStore.AcquireLoad` reserves the indexed decoded wire length against live/decode budgets and one live-lease credit before payload reading; `LoadAssetAsync` holds/relinquishes decode ownership and independently retains backing for the returned lease. A new package-bound load still consumes those ordinary budgets; it is not a free cache lookup. `Retain` shares the decoded lifetime/backing and consumes another lease-count credit; current source does not add another `_liveAssetBytes` charge for it.

Those counters do not establish a managed-byte reservation for every lease/task wrapper. Conservatively preclaim the caller-visible carrier portion before each built-in load/retain: new lease and LeaseState, new shared lifetime on a fresh decode, and the qualified Scene-side await/holder peak. A shared lifetime is charged once, not once per retained alias. Its carrier provision follows the surviving retained owner. Do not reattribute encoded/decoded payload, streams or package indexes to Scene merely to make a formula large enough, and do not claim decoded wire length proves their complete managed footprint.

If qualification establishes a specific existing Assets byte grant already covers a carrier, name that grant and remove the duplicate Scene claim. There is currently no basis here for excluding all carriers solely because a lease-count check succeeded. No Runtime funding callback is passed downward into Assets.

## 5. Cleanup, result delivery and receiver lifetime

### Preparation body and publication

The precreated `PreparedScene` remains hidden until its plan/options are complete. Its single preparation use owns two **internal consumption conditions**, not two U credits: the preparation producer has stopped, and the prepared/admitted body has stopped or transferred all its resources. Coordinate these with fixed state under the existing gate.

On success, publication makes the body available; disposal or activation may race immediately with promise publication. Neither may refund producer workspace while the producer still accesses it. Accepted activation moves the one body role and required snapshot/claims to the request; wrapper disposal thereafter cannot revoke it. The stable carrier retains the current body owner/graph until final cleanup, even if its own preparation publication has not returned yet. `SceneActivationRequest` must notify that owner after clearing its final preparation-funded inputs, instead of blindly disposing an alias of the use.

Keep a protected, data-only preparation publication provision through the final producer access, following the preceding proposal's section 9 distinction between body consumption and promise delivery. Applying that distinction to preparation is proposed here; approval of the activation decision alone is not acceptance of this new handshake. An explicit promise/publication sequence is required if the current async lowering cannot expose that last-access boundary. **An `async finally` before the compiler's eventual `SetResult` is not a proof.** Qualify the producer/quiescence handshake against the selected code-generation profile before releasing its provision. Terminal observation must still imply that retained input/resource consumption ended or moved to an installed/quarantine owner; a delivery tail cannot reopen Scene work. Caller-retained completed Tasks do not require new disposal or indefinite U retention.

Retain synchronous usage/duplicate errors and the existing independent-admission refusal channel. Once a valid operation has a funded escaped promise, byte refusal faults that promise with contextual `InvalidOperationException` after cleanup; it does not invent a preparation receipt or successful partial plan. Original admitted I/O/codec failures and cancellation keep their existing channels and actual-consumption boundary. Protect the supported capacity/fault/cancel publication coexistence before optional work consumes P; do not manufacture an unfunded replacement exception after exhaustion.

### Failed cleanup

Each owner retains its arrays, loaded-entry counts, pending lease and cleanup stage **before** a fallible acquisition/release. Clear each item only after its release succeeds or its ownership transfers. On failure retain the complete still-owned graph and its necessary active/prospective/R grants in the exact use/receiver's preowned anchor.

This requires more than adding `using` claims:

- Rendering's catch currently releases local entries, then its `finally` unconditionally disposes final/material/initial reservations. A release exception can therefore refund a grant while remaining entries were only in locals.
- `RenderSceneAssetBundle.Dispose` returns R in nested `finally` blocks even if an entry release throws.
- `PreparedAssetsLifetime` can clear `_value` before calling its fallible `Dispose`; `SceneExpansionPlan.Dispose` marks itself disposed before child release. Quarantining a plan whose holder lost the failing value is not proof that the full graph remains rooted.

Make these release state machines preserve failed ownership; do not reset active counters or treat a second no-op Dispose as successful physical cleanup. Quarantine only the exact independent use when it still owns the failure, never an active binding or a same-GUID successor. Canceling a wait, closing a domain, losing a wrapper or completing a Task is not a refund.

### Retained R after the preparation use ends

R must have release authority and a failure root independent of the preparation use. Its existing receiver is `WorldSceneStorageReservation`; make that receiver retain the exact plan/bundle construction/retirement graph before transferring X→R.

A concrete lower-lifetime gap needs closure here: `WorldSceneStorageReservation` currently stores bytes and a World reference, not a rooted receiver graph or retention pin. `World.Dispose` can dispose its domain reservation after registry cleanup; `WorldAdmissionDomain.ReleaseWorld` releases that reservation's whole byte charge, and `ReleaseSceneStorageReservation` tolerates the already-disposed World case. Existing retained-asset tests keep the World alive. They do not prove R stays charged if an unadmitted PreparedScene or independently retained bundle survives World disposal.

The smallest proposed extension stays in the existing reservation owners: a prepaid intrusive receiver link/pin in `WorldSceneStorageReservation` and its exact `WorldDomainReservation`, no allocating registry/list and no new global interest/allocator. Close ordinary World admission immediately, release proven-dead baseline storage, but retain the outstanding receiver segments, their control/cleanup charge and strong roots until their own actual release or quarantine. Normal data retention conveys no binding/application authority. Final receiver release must work after World/domain closure and cannot refund another reservation.

Concretely, track the sum of linked receiver segments separately within the reservation's existing byte total. Install the pin and X→R attribution together. On World close, latch admission closed and release only the nonreceiver portion after its cleanup; the same existing interest retains the receiver portion. Its data-live state must be distinct from admission-open state: growth/new uses refuse, while exact existing receiver cleanup and original-grant rollback remain legal. Each actual receiver release subtracts its exact segment from reservation/domain bytes and unlinks it; after owner close, the last segment ends that retained interest. Do not infer the nonreceiver portion from a refreshed headroom value or release independent D_u bytes with it. Required history/root generations must remain valid through this split.

The pin's control fields, installation/rollback, exact-root retirement interaction and receiver-only domain release transaction need the owner disposition in section 7; current helpers do not already implement them. Do not assume a World-attached R remains valid merely because an object still references that World.

## 6. Bounded quantities and what is not a bound

No single numerical whole-operation footprint follows from the count ceilings. Nor is one required: exact P prepays policy, then checked per-allocation claims enforce it. The following input-derived obligations are implementable without guessing a total:

| Obligation | Derivation / restriction |
|---|---|
| Freeze with n bindings | With current two-copy implementation, at least `2 * ArrayCharge(n, IntPtr.Size)` plus qualified sort/entry carriers at peak. Retain one array afterward. Check multiplication, rounding and conversion before allocation. |
| Traversal | Root plus at most the selected Prefab depth for the asset-path buffer. Charge each actual suspendable call/high-water frame provision before invocation. Hierarchy and selector limits constrain traversal but are not an excuse to assign every compiler frame one pointer. |
| Array-backed cache growth | Current capacity and selected checked next power-of-two capacity determine full old/new coexistence. One pending miss has its own holder provision. Do not use `Dictionary.EnsureCapacity`'s returned post-allocation capacity as prior admission evidence. |
| Reference validation with k active bindings | Two k-reference backings at copy peak, plus each actual binding object and `ArrayCharge(propertyPath.Length, sizeof-segment)` copy. Paths have at most 64 segments. Existing encoded-schema/read-view validation uses value/stack storage on its success path. |
| Override rewrite | Actual field count, payload extents, literal/fixed-array length, path depth and nonallocating candidate measurement determine every array. Qualify class/call carriers separately; do not multiply only raw encoded bytes and omit headers/copies. |
| Atlas/Material helpers | Actual immutable page/frame/material-slot counts determine higher-owner holder, rectangle and copy arrays. Tree dimensions are existing algorithm inputs, not a new cap. Queue capacity is chosen explicitly before allocation. Texture/Mesh/Skeleton payload remains Assets-owned. |
| Final R | Enumerate only receiver-owned objects/capacity plus its prepaid retirement control. Spare capacity is included. Temporary scratch has a separate owner and never disappears through measurement arithmetic. |

Layout and framework metadata require a **qualified final private charge catalogue** for the selected SDK/TFM/runtime/code-generation lane. Source `new` expressions for structs are not heap objects; conversely, field counts and `Unsafe.SizeOf<class>()` do not bound class, Lock, Task/TCS, deferred fault/cancel, continuation-dispatch or compiler-state-machine storage. Old activation probes and the current activation 256/512 constants are not preparation qualifications.

For exceptions/diagnostics, distinguish immutable producer-owned input referenced without copying from newly engine-created formatting, carriers and fault bookkeeping. Retaining a foreign graph needs a named valid owner; catching it does not make it free. There is no aggregate arbitrary-exception-graph or arbitrary continuation-graph byte bound in the current Scene types. Preserve original failures, do not truncate them to invent one, and do not charge caller-authored continuation graphs as Scene work. If the protected supported-outcome envelope cannot be closed with those ownership boundaries, its profile remains blocked.

## 7. Exact owner gates before closure

| Owner | Missing fact / required disposition | Stop condition |
|---|---|---|
| Entities use → Scene R | Active-claim transfer, qualified receiver-control footprint, single-use exact-partition rollback, and unchanged common World/domain totals. Current transfer only spends free allowance; rollback has the boundary predicate identified in section 3. | Do not implement release-then-transfer or re-admit already prepaid capacity after closure. |
| Entities retained receiver lifetime | Define/implement the exact-reservation receiver pin, remaining-segment accounting and root through World teardown, failure/quarantine and final release. It must not extend or reacquire binding authority. | If the existing World reservation cannot represent those retained data segments safely, that lower owner contract remains unresolved; a larger R constant cannot close it. |
| Scenes/CLR preparation producer | Final fixed bootstrap/protected terminal catalogue and a synchronized publication/body join that covers the actual lowered producer's last engine access, including supported Runtime Async/AOT lanes when claimed. | Do not infer this from an async finally, Task terminality, old binary measurements or a cap-sized reservation alone. No numerical preparation bootstrap is derived here. |
| Rendering transitive ownership | All reachable construction/validation helpers accept funding, root partial state, and distinguish retained holder R from callback scratch. Measurement returns the retained requirement and prepays its own work. | The current internal `IDisposable` return type and two callbacks alone do not declare a finite transitive peak. Close the actual built-in implementation; do not add a fake preparer to prove the interface. |
| Persistence preflight | `WorldPersistence.PrepareLoadCoreAsync` must supply a finite, already admitted measurement envelope before planner measurement, not reserve its output after measuring. Existing `WorldPersistenceAdmissionReservation` has retained-use accounting, but the inspected call chain supplies no Scene-preflight workspace port or derivation that existing unrelated grants cover it. | Select/attribute the seed from the existing persistence coordinator's admitted envelope and enforce its claims, or leave this route explicitly blocked on that owner contract. Do not apply public P/defaults to a not-yet-active candidate or acquire a new independent U for it. |
| Assets carrier boundary | Map each lease/await carrier to its actual grant; keep package/payload limits separate. Existing counters primarily measure decoded wire length and lease counts. | Do not assert this proposal qualifies Assets' complete managed heap or double-count shared backing to disguise a missing Scene holder charge. |

These are private owner/qualification obligations within Chapters 02/04/12/16/27/30 and the current M03 gate, not new acceptance IDs. The input types provide many finite content dimensions, but do **not** yet supply all these ownership/profile contracts. Therefore this document proposes the closure mechanism; it does not assert a currently provable finite numerical workspace bound.

## 8. Ordered implementation and regression plan

Each row is a coherent dependency boundary, not a commit quota. Proposal files remain for review; implementation, related contract promotion and validation require their separate authorized work. No implementation is performed by this document.

| Order | Existing owners / bounded change | Required regression evidence |
|---|---|---|
| 1 | Close the private final footprint catalogue and the section 7 Entities transitions/receiver pin before changing consumers. Extend `WorldGuidUseControl`, `WorldAdmissionDomain`, `WorldDomainReservation`, `WorldSceneStorageReservation` and the existing World Scene helpers, not global ceilings. | Extend `SceneSupportRegistrationTests`' scratch-return/retained-transfer and aggregate/race cases; use existing domain APIs. Exact/one-byte-short active claims and full-new-array growth, X→R conservation, transfer exceeding free F while covered by X, the P=4096/C=3072 rollback edge, wrong World/generation refusal, repeated disposal, retained/quarantined totals and release after closure. Receiver construction-failure evidence must be real or explicitly unqualified, not simulated by quota refusal. |
| 2 | Precreate/anchor `PreparedScene`; implement entry preclaims, funded freeze, scalar policy capture and producer/body handoff. Carry its preparation claims through `SceneActivationRequest` cleanup without changing the separate activation-P decision. | `SceneLoadOptionsTests` and public `SceneAssetStoreTests`: valid explicit instance ID without a pre-admission array, copied/sorted/duplicate/null behavior, exact P unchanged, P too small for entry, binding-array exact/short overlap before root work. Dispose/admit/publication races must refund once and never borrow a snapshot after its funding ends. Use a legal caller-owned blocking scheduler for last-publication observations, not a production hook. |
| 3 | Meter counting/cache/frame/build helpers and partition `R_plan` in the planner/plan plus override, slot and reference helpers. Replace opaque Prefab-cache growth only; preserve semantic traversal and package anchoring. | Extend existing `SceneAssetStoreTests` nested, empty-Prefab, count/depth/cycle and byte-refusal cases, `ScenePrefabPackageLeaseTests`, and `ScenePrefabOverrideApplierTests`. Cover repeated hits versus the next unique miss, nonempty old/new cache growth, selector-discovered misses, multiple override layers, FixedArray/Omit and 64-segment reference paths. Show the next denied claim precedes its load/copy/growth, all prior stages fit, and no plan/identity is published. Existing “before publishing a plan” tests are not preallocation witnesses. |
| 4 | Meter the real CPU extraction participant, bundle and all reached Atlas/Material/Skeleton helpers. Give restore/measurement an actual preadmitted port; stop on the persistence seed gate if it cannot be derived. | Extend package-backed `RenderAtlasAssetCodecTests`, `RenderSpriteComponentTests`, `RendererExtractionTests` and the existing Sprite/Mesh package fixtures. Cover Atlas tree/queue/rectangle peak with one/multiple pages, deduplicated and multiple Materials/textures, static Mesh/Skeleton and mixed Sprite/Mesh, repeated package-bound loading after replacement, entry arrays before final R, retained-versus-scratch measurement, and exact/short callback claims. Keep `EngineHostPersistenceCommitTests.CompletesCapturedLoadAfterExternalDomainClosure`'s no-Scene/Sprite/Mesh variants through real Branch restore. |
| 5 | Integrate staged cleanup and receiver retention across loader, builder, plan, bundle, `PreparedAssetsLifetime`, result, request and World teardown. End grants at actual quiescence. | Keep `PreparedAssetLeaseRetainsBundleAcrossActivationUnloadAndDispose`'s default World/options/package, pixel 17, unload/dispose lease assertions and activation-tail observation. Add preparation cancellation/fault cleanup, domain closure before versus after admission, exact-binding revocation/same-GUID replacement, retained bundle/unused prepared result surviving World disposal, and last-lease release/quarantine. Retain caller Tasks/receipts without retaining body inputs. No artificial OOM/release-failure producer or test-only production switch; unsupported safe failure stimuli remain open. |

For every allocation-boundary trial, derive the expected catalogue/array sizes independently from the reviewed inputs. Let B be the already owned/reserved baseline at the targeted boundary and D its **complete additional simultaneous peak**. Test B+D and B+D−1 by varying only that budget; hold other World/domain/count gates fixed. Confirm the named stage was reached and preceding setup fits. A small P that refuses bootstrap does not qualify cache or Atlas admission.

Use actual production package/codecs, owner registrations and ordinary .NET tests; do not fabricate plans/snapshots or add an alternate qualification runner. Combine the affected Runtime test classes in one focused Debug run, then Release, using `global.json` and `docs\project\Building.md`; confirm fresh nonzero selected/executed counts and the boundary rows, with TRX/logs under the existing artifacts layout. Run Assets tests only if its implementation changes. Qualify affected public Scene consumers and supported AOT/code-generation lanes separately; helper layout tests do not establish them or full M03 acceptance.

**Evidence for this proposal:** source/contract inspection and the explicitly derived examples above only. No build, test, allocation probe, production/test/maintained-contract edit, commit, push or PR was performed.

## 9. Frozen review inputs

HEAD: `31f156a51ccc462ae9c5cc8d4e87e00c0054dec4`. Snapshot started at `2026-11-16T01:11:11Z` UTC. The manifest contains **122 distinct files**, including all 30 pre-existing modified and four pre-existing untracked paths. C means clean, M means pre-existing modified, U means pre-existing untracked. SHA-256 is over raw on-disk bytes, not normalized text, the index or HEAD.

This is the complete relevant source/contract/configuration/test input set for the stated conclusions and planned regressions. The final four preservation-only entries additionally freeze unrelated pre-existing dirty work; their inclusion does not claim review of its behavior. Test fixture dependencies included here are review inputs, not evidence that tests ran. No schema/fixture version, new public declaration membership or native acceptance is proposed.

This new proposal is the output, not its own input, and is excluded to avoid a self-referential hash. Shared worktree access was read-only for every listed input; this manifest is a review snapshot, not an exclusive filesystem lock. Reverify every hash and state before disposition; changed inputs invalidate the affected review. Final verification must also ensure no pre-existing dirty path is missing and only this proposal was added.

### Context and contracts

```text
342683de2b1c262624546f312f7d8e2078f89185add39f43f1fb5013376fd553  C  .editorconfig
03efea672cd52e9ef053e9ce0285e7ca02c6ce6fb0d21268ade4255ed5a39d58  C  .github\workflows\ci.yml
770ee4b2a422861a6f23f0d39dd0eca361a81cb87e64ebac4e12170f71231d7a  C  Directory.Build.props
aeec1d08e8037470239035812351c4045124607d67a354d482112f4d642d4105  C  Directory.Build.targets
a5066bf0f3b0efd28552b484a01a39726c2ce47584b3fbc8c077790c4f4ad57a  C  Directory.Packages.props
1bce01871321c2df82e887da428ce5c121a6c6541fe65017fe3d249be47e190c  C  global.json
d44373af315523f8af9f09f36bded2a688e2b5c1572fcc0732310c7ef590ead8  C  sources\Directory.Build.props
281aad4237121feab1903c2384f1a5f0aed7e218a704c918fe953e9fc8e75764  C  tests\Directory.Build.props
e019d4dbad14ff2d5ff8d7365dded0d09210575d5228a0d01ced70c99b3c2d33  C  sources\TerraFX.Runtime\TerraFX.Runtime.csproj
ecbfd99e4dd1ec2a32472d3187b6f2911d7297d1f74a52f2afa600eb6ae5f06e  C  tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj
f53ab58b89eeae7e17b70970075be9322a936d443bb6d5239c3238e594deabc6  C  docs\project\Building.md
c4fbe82155a6d82ffd2d13ad2f18056d938738c4fa5a66760c4877e24ed001c7  C  docs\project\Roadmap.md
40f913a50434209e09941c22625a6d0722202bab97ebac0e153c1ea3368e2291  C  docs\design\02-dotnet-api-memory-and-ownership.md
a4d86f3fa373a64548435c53582bf5916e29d77f34650f9d161ccec90e6f0245  M  docs\design\04-capacity-and-workload-profiles.md
de1032a04217b2a08933332aee7db22df849cbfc069e76cf6b2263bf469df109  C  docs\design\11-assets-import-streaming-and-packages.md
174b2465746da79cef560c8506216ef4f99f9d1aabac184f872c9e6cb0b94e0b  M  docs\design\12-entities-components-and-queries.md
b012f42b23cede9c7cdb71203d8357cd19e1626823ea25ef3119961292d3698a  M  docs\design\16-scenes-transforms-and-cameras.md
b9b6324c5b1b1543ca1d2287ece579597a7e7aa62d885031c4ff1bb629f08777  C  docs\design\27-state-serialization-replay-and-recovery.md
3b55a013eec327ecd648acbd5b53717dd218ad288e8047ac21277609efa2f47b  C  docs\design\29-project-setup-build-and-deployment.md
15848c82f3858a77a87a0d3ea6fbcc07046eb6e77ca5b21748c72b90efab598b  M  docs\design\30-public-api-review-surface.md
532a68f1717dc05901dfc4537204e65b31b28ee46326f64f5a1cd7834332aa41  C  docs\design\34-engine-acceptance-cases.md
24eec6e92b7ad904d552f290ef748ffc8fce13b6b94a89ebd9f39487d4584d87  C  docs\design\35-implementation-roadmap.md
3f8e4f0315c6cc2933fc982f64d5e94b9070eb1876a74b0688118bb17aff5a62  U  docs\proposals\m03b-scene-preparation-budget-accounting.md
```

### Assets and Core callees

```text
882cceac8b4ef430e9b95e6886c7bb802c0bd2c0a5993d006a588d9f3928bd42  C  sources\TerraFX.Assets\AssetId.cs
a639a5949353f2fb594b2c57569b2491d08988bfba48f551e666a36a6ea8d3da  C  sources\TerraFX.Assets\AssetLease.cs
2749b26e3a42809558558704b6d11c51e886ae40a933e29b7a205a10357881b2  C  sources\TerraFX.Assets\AssetLeaseLifetime.cs
784a190d7e704e626c698ac458ef5b5cc671c25c629cf93748da70b0caab5677  C  sources\TerraFX.Assets\AssetPackageBacking.cs
c0dfe89dbeb9760ee8c22cc583858d6112f9cda5417c001f079eb4069d990d96  C  sources\TerraFX.Assets\AssetPackageLease.cs
6ee57d6c011bece27e5f1de33a5c1d27d9ae76a510ce825022c0cfe070b02b74  C  sources\TerraFX.Assets\AssetStore.cs
c58fa6d3e3c1e9ba5792e2e3d590fa066fc4710d81c43c80d6cfd6fd08948ecf  C  sources\TerraFX.Assets\AssetStore.PackageLease.cs
bb2123297d3e65309bd38ce5da0083e14319810139fa233ca0b0ebc4e5404594  C  sources\TerraFX.Assets\AssetStoreOptions.cs
65d47557eb96fd79831bf67f3198e46c6ae82567788ee92f13e0870bd211ab00  C  sources\TerraFX.Core\EbinCodec.cs
37feeec61e4709a2f1e9edb4470a180ed1d07c0d07f26b34740140682e4b0888  C  sources\TerraFX.Core\EbinFieldSource.cs
ec0f5ba10d1bca6bea3345c60de7bfa8b665e909b61f1f097e61d32783cf30a9  C  sources\TerraFX.Core\EbinReadLimits.cs
cc7d6e1204e818e4710525cd94491f592c771af3c24f946e47b0c9f08ffd0503  C  sources\TerraFX.Core\EbinRecord.cs
05dbde5a42dbefe96cfc14d5984d9ba74c1b0fab515c79c322a62baca2cc21a5  C  sources\TerraFX.Core\EbinRecordReadView.cs
98b7c0fd9b00ec7f0214ddbb8ff39e99a67f4885faf5c9c1c30b7b18933e7ee5  C  sources\TerraFX.Core\EbinRecordWriteView.cs
19b4b605fd018b7e5650cd96962211c6b7d41bdf93a2d036043f5d9570d3c214  C  sources\TerraFX.Core\EbinScanState.cs
43b455f4a056a6df2bee31e52e0ecacf7c128bb33a8cb6a662386e8071575e1c  C  sources\TerraFX.Core\EbinStructuralReader.cs
f06e3598896c9e012d27893fd24ba9b3bc7507eb4546189921d69c0d9492937a  C  sources\TerraFX.Core\EbinStructuralWriter.cs
c7973b49986fb93a43ed3c7e4fb69ddb37a7185678a3466e5f866dad0ebf4ea9  C  sources\TerraFX.Core\FoundationCatalogue.cs
d11f4e31287c9ca9be7ffac6e0008033f16e33706e3e59cd7b814c99dd593ae2  C  sources\TerraFX.Core\FoundationNameUuid.cs
3d0910a70183fb6c63416c7e1e259a17dc1617a6c824da610210ebf8bedd53fb  C  sources\TerraFX.Core\RentedBuffer.cs
a03462b6221587eeb52c446e861622d2e5029fc264a25feb8fa8162d779b7ed9  C  sources\TerraFX.Core\RentedBuffer`1.cs
```

### Entities, Scenes and Rendering

```text
ad884c9df4f9b48db4751518237535bc11a67820ea1f96fc66cae850935a7b31  M  sources\TerraFX.Runtime\Entities\World.cs
55b61437fe42a622bbb534f2224d569ee3076f30f8fdb50691135ae23ee8d8ee  M  sources\TerraFX.Runtime\Entities\WorldAdmissionDomain.cs
ea012b9b40acedaa820885eafe27dcd11df55f842b68f2849785dab756e8e3bc  M  sources\TerraFX.Runtime\Entities\WorldDomainReservation.cs
a6120452989f29ec2ef9c2b90e7479cc6635b9b17848c6c683063b3f99d36d22  U  sources\TerraFX.Runtime\Entities\WorldGuidUse.cs
1931cd2b5dfd81a3f0cd8a4e6f3d09c5d9b006b2e29817f5c295c77f78202b68  U  sources\TerraFX.Runtime\Entities\WorldGuidUseControl.cs
b1a9add0a1d68caf4c385973ca4e2602c68e19e46078aa3cb09614848c09a634  M  sources\TerraFX.Runtime\Entities\WorldLifetimeControl.cs
00a97589443f847ca4111314976ae0cd8bbb93be041424550de382c6e0372da8  C  sources\TerraFX.Runtime\Entities\WorldPersistenceAdmissionReservation.cs
ac53bbc6892880dcf0fd12541471f7099956fb0704116177cbcd5b1dc6df2a53  C  sources\TerraFX.Runtime\Entities\World.PersistenceAdmission.cs
a6a487abae56a5512976cefb5d342c37b414bb4f8615b1a124692c9696c673e5  M  sources\TerraFX.Runtime\Entities\WorldSceneStorageReservation.cs
497b007892c2f5e2c7b87665df15656ed0788dc810c4fb37d2213719f22ddf61  C  sources\TerraFX.Runtime\Entities\WorldSchedulingLimits.cs
bc2d253adff70f8a14301fce2aceba65f20578258be1c94434b4de9f558849a6  C  sources\TerraFX.Runtime\Hosting\WorldPersistence.cs
67ae936843f6329d95d12b784a4686a8b75b2cceb3932159e56bdd24f21ca423  M  sources\TerraFX.Runtime\Scenes\ISceneExpansionResourcePreparer.cs
d90adf47f3de2f5b71e69b4e7b5798d7dd4bd505d19fb89b22bdf8f46ecb60d9  C  sources\TerraFX.Runtime\Scenes\PreparedAssetsLifetime.cs
50b55ad7c2b78867ba994b9f93ff4906daa979e58a5730be1bdbb2a752b2ed26  M  sources\TerraFX.Runtime\Scenes\PreparedScene.cs
7d12eb728b0563ed673a86a1e1846aaf0d3fc37a07c139cadecf8f34c646ec69  C  sources\TerraFX.Runtime\Scenes\SceneActivationExecutor.cs
64dd117d46676c422b932ac1b8c1df12a207313b99f6f3f5b55749945ec57786  M  sources\TerraFX.Runtime\Scenes\SceneActivationRequest.cs
1865fdc7ad0b77fdcedc4a6d26b56358aceca95647ad55a63f1dbcd4d716ead6  C  sources\TerraFX.Runtime\Scenes\SceneAsset.cs
d0493b1f2d447db5a7ea5a5df501e046d0c53c3bae5219a0e626c0f25da6f90e  C  sources\TerraFX.Runtime\Scenes\SceneAssetReader.cs
3a66068edf88575f61fbe23d54d745a1422465c6f7a3597e39714e39522b62f4  C  sources\TerraFX.Runtime\Scenes\SceneAuthoredComponentRecord.cs
78738c7d0e502ee400fc4160394b4e419aa3c5fa6855a7165114721040a9c8a6  C  sources\TerraFX.Runtime\Scenes\SceneAuthoredRecord.cs
5678f2667e408562758aab6c23d0f0a57e34f5c3019daf2fb6f625da31a0d08b  C  sources\TerraFX.Runtime\Scenes\SceneEntityReader.cs
eb4f2399b5f205f91c240d39c054331d63c6196bfd98701d6d119ef06fcd01fc  C  sources\TerraFX.Runtime\Scenes\SceneEntityReferenceBinding.cs
a0db38b6008960e7e36779b7e42a2f291dcd830d0e2679037a02dbdfaa10512f  C  sources\TerraFX.Runtime\Scenes\SceneEntityReferenceBindingValidator.cs
343218eaa00bf04b2df9a79276f274ea2f78127332980ad014a46b02ddf6effb  C  sources\TerraFX.Runtime\Scenes\SceneEntitySelector.cs
b6bc877ab9aefccce5b8244b5825c7ea30af4c41c29b89e0bac1daadc5d15ff4  C  sources\TerraFX.Runtime\Scenes\SceneExpandedEntity.cs
e6f6f41e593bf6a70189161876d4794e5fc27573d9f15a16d2dfc7ceb58d08c9  C  sources\TerraFX.Runtime\Scenes\SceneExpandedExternalSlotBinding.cs
d0422ab1b7ccf3e7fe5f9805786576c7d5047c1bbe0be4721d05f77604de7a6d  C  sources\TerraFX.Runtime\Scenes\SceneExpandedOccurrence.cs
e356754f7fbbc840ab5f007588d70f7e7f12bfccbfcb1d91248f4335a3fe4e53  C  sources\TerraFX.Runtime\Scenes\SceneExpandedReferenceBinding.cs
941b5a09b7cd60ea5704030ace2af2f3499ebe2843e14138182faf609acc4173  C  sources\TerraFX.Runtime\Scenes\SceneExpansionLimits.cs
8ffcde62282341b8c5e782d5262c0917002d1b48e35a5098cc9b4b360beefae9  M  sources\TerraFX.Runtime\Scenes\SceneExpansionPlan.cs
7e97c4bd0c33eb7139919937437fba284eca3ea46a1e1516ed9aa266bde83674  C  sources\TerraFX.Runtime\Scenes\SceneExpansionSource.cs
521dae0ba263f4161e2f5f2dfdbb75a4b1cfcbfa63eb2e7403f4777580f9801d  C  sources\TerraFX.Runtime\Scenes\SceneExternalBinding.cs
36e1ecd2dbfccdcabaf0f4ad8724bb3fe2cad03d77659ecd6bbdf244ef359980  C  sources\TerraFX.Runtime\Scenes\SceneExternalBindingCaptureRow.cs
56cc97329bcf128c270873f0b3743944f435e932822e0d3e413f33df80fcd7a7  C  sources\TerraFX.Runtime\Scenes\SceneInstance.cs
2334f0ac87d731a9193f7f185da37105c7ba3054784a566331602195f9009a64  M  sources\TerraFX.Runtime\Scenes\SceneLoader.cs
e51694144db9b4898ff2bfc0e9982b4c898e2de907d957146e89737ca954a92d  M  sources\TerraFX.Runtime\Scenes\SceneLoadOptions.cs
004b203dc84037b76f854dde1b47c78d570a8085b6a9b48ee6423e27496223e9  M  sources\TerraFX.Runtime\Scenes\SceneLoadOptionsSnapshot.cs
504b9d956b84003d44fab50a0e10c0b8218db5c66e51dd148d6427fbbd31bdf0  C  sources\TerraFX.Runtime\Scenes\SceneOccurrenceIdentity.cs
698f36fb6207d09b395af7a0da85ff132dc8e7ed549e9b4e804df5d6728348c9  C  sources\TerraFX.Runtime\Scenes\ScenePersistenceRestorer.cs
2f1fe3abc598765e24407c58aafaedd6f790ebc58747805261ee17ba920b56cb  C  sources\TerraFX.Runtime\Scenes\ScenePlacementSlotBinding.cs
70e859e5d7dd16731d731ae8f1347a7c406218e0180b2f0a7a6d06b90ae5c7c3  C  sources\TerraFX.Runtime\Scenes\ScenePrefabAsset.cs
d8d807e1925a5cf2538a41076ca3606a7fe3f31005b452429f20c99c5612ca27  M  sources\TerraFX.Runtime\Scenes\ScenePrefabExpansionPlanner.cs
24f869814ad49ddffd8cba7fe4e5d0ddf91a84b0ce3955e60db3878e6706638c  C  sources\TerraFX.Runtime\Scenes\ScenePrefabOverrideApplier.cs
a4e521d90f822ffdf1d8c539fb5ea8a154c91a3689b85c88b4bccd87b70ecb99  C  sources\TerraFX.Runtime\Scenes\ScenePrefabSlotBindingResolver.cs
974ad1c933bc3acbae57354e0a96cae5930111e15d765a65c7d0653961e6d236  C  sources\TerraFX.Runtime\Scenes\ScenePropertyPathSegment.cs
4656b39f2a0f4f444388c8020be0aa5b04488d8811ab9d8445b1a30ef876afde  C  sources\TerraFX.Runtime\Scenes\SceneReferenceTarget.cs
667e8bf4d3091be93a206a152c9aecbb071e5e9c42d1ecc49f17f1a37c46aeff  C  sources\TerraFX.Runtime\Scenes\SceneWorldRegistry.cs
d8796b1c243915d87db9a88f0adf888bc8b1f34b3f50b80521932404c898c342  C  sources\TerraFX.Runtime\Rendering\RenderAtlasAsset.cs
c8a80f5a957ba0e1db6a56d390ccb04a8d807399ad18f4fb233297a10c8411be  C  sources\TerraFX.Runtime\Rendering\RenderAtlasPageLoader.cs
abc653b3150c4c3bde95a90fe39527d0f4b068c62dad96577a183e2c681f82bb  C  sources\TerraFX.Runtime\Rendering\RenderAtlasPageSet.cs
f4c183b5b28bc45f570166f3ecf8bb55d65bcbefbda11d6e68d40ed8bf36e1d7  C  sources\TerraFX.Runtime\Rendering\RenderMaterialAsset.cs
322e6f5e003ff4aac109069941883f570bddc8030749fa7470994b3ee93db56b  C  sources\TerraFX.Runtime\Rendering\RenderMaterialTextureLoader.cs
fc96d4afff183dc0ca5c9476f6ffb508e6c4ca3f98a4e6edbdbf3001bdf3656a  C  sources\TerraFX.Runtime\Rendering\RenderMaterialTextureSet.cs
de4681a7a5edfc39502faf9339bc5d97afd0ef2bbdf024aa0fd67f535829c49e  C  sources\TerraFX.Runtime\Rendering\RenderMeshAsset.cs
e4b7edbbc746cf29568d683c8ed0a8e0987fde6dc10ad4e390370bb8d12865d5  C  sources\TerraFX.Runtime\Rendering\RenderMeshAssetBinding.cs
167d1adf373643e47c6f489a61d35f51fe6f4829349c82fdbc2131b0e772a20f  C  sources\TerraFX.Runtime\Rendering\RenderMeshComponentDefinitions.cs
4fd3f20576ad328bd3e9958e40ef8d475451bdcbfc5122c3ba7e599a0ea8361a  M  sources\TerraFX.Runtime\Rendering\RenderSceneAssetBundle.cs
f29807904a88273c7fb7e28076453a8465bfdf7d3f50d4a3733ca759e6a83ce7  C  sources\TerraFX.Runtime\Rendering\RenderSpriteComponentDefinitions.cs
eaf1f94aff0886b3fa30af97bd7f83180a38ab744d4241d0cdd275a055eadb55  M  sources\TerraFX.Runtime\Rendering\Renderer.CpuExtractionSupport.cs
```

### Regression and fixture inputs

```text
f01c956a1df54bbb048bd40be073a0190e843255eb71c0af618fd3a345793429  M  tests\TerraFX.Runtime.Tests\Entities\WorldAdmissionDomainTests.cs
b4e0683405adab476b5b4cf9108e8c7b1e30e33b6cd48ec55a28aeca57edffbb  M  tests\TerraFX.Runtime.Tests\Entities\WorldLifetimeControlQuarantineTests.cs
48d52b84628b9716a0e95b6c420a021724e494cf797d6f1c704677fd115d5bef  M  tests\TerraFX.Runtime.Tests\Scenes\PreparedSceneAssetRetentionTests.cs
358ec263d75f1197e46b31c0686069a2a758fa9d27f75840285cac668aca084e  C  tests\TerraFX.Runtime.Tests\Scenes\RenderAtlasAssetCodecTests.cs
abaeea4d0f6be941af73e640b7fce41ec753d2199654343fa4668d54ec20fc6a  M  tests\TerraFX.Runtime.Tests\Scenes\RendererExtractionTests.cs
caa6610cf55f8fffa077745fd96baa45fcecfed2fd051cc763901aeed5500239  M  tests\TerraFX.Runtime.Tests\Scenes\RenderSpriteCapacityTests.cs
8e65b71b5ac1e0ef9d1f47aa56963054d091df8c229e74ddbb1cae37bb59ab60  M  tests\TerraFX.Runtime.Tests\Scenes\RenderSpriteComponentTests.cs
57becc4ac96c0d8a43ec718a71333224e6ecc2d0b8a0b456d3a0b28444feef85  C  tests\TerraFX.Runtime.Tests\Scenes\SceneAssetReaderTests.cs
1784651e891e29d88e84508dc7c78ad105f42f85acf1ae125772f170fd22a587  C  tests\TerraFX.Runtime.Tests\Scenes\SceneAssetStoreTests.cs
b547c59bd829cff197affdacdb5daccd86386093db9b5e9d0202230551ed07b4  C  tests\TerraFX.Runtime.Tests\Scenes\SceneAssetStoreTests.Persistence.cs
6c3cc41aec6438e08b02bf7da3036afe8c925e76fed557754cd3e513c951e49f  M  tests\TerraFX.Runtime.Tests\Scenes\SceneLoadOptionsTests.cs
f0bccffa4ddf9a03678057ac03324602aff36606b10292cec162ee74d99f7efd  C  tests\TerraFX.Runtime.Tests\Scenes\ScenePrefabOverrideApplierTests.cs
53ba0a7829da362b3596c8c5e4f322fc9d87c9ac3c5075978557ee77e9404f4b  C  tests\TerraFX.Runtime.Tests\Scenes\ScenePrefabPackageLeaseTests.cs
0f7b4ce285cb4f90cb784520d4fbe472885ed86503a67ddd2e59a8e6e748f732  M  tests\TerraFX.Runtime.Tests\Scenes\SceneSupportRegistrationTests.cs
6117160727fcc4cd3c88355064d889c9294d667a71bc19f8b74b38b33ea8cda5  C  tests\TerraFX.Runtime.Tests\Hosting\EngineHostPersistenceCommitTests.cs
```

### Preservation-only dirty inputs

```text
6fee16009bf0d1fb529e68c8df1e3293a852468e71d9a291984d8a64a9a62bc8  U  docs\proposals\m05-owner-contract-closure.md
e09fca988ba58127b365d8f4d7eb0c22ca3fd17e07e8342879544fb81bbd4db1  M  sources\TerraFX.Runtime\Animation\AnimationClipAssetBinding.cs
470ec745500181a57d2e0625b3a93ee5e51a899e046c017b3dd490fdb02be739  M  sources\TerraFX.Runtime\Entities\WorldGuidRegistry.cs
1849eae697239482076f7ec3a1ea02dd6b3348094dad6cb63668bf57fdb20529  M  sources\TerraFX.Runtime\Entities\WorldGuidRetirementPermit.cs
```

**Verification:** at `2026-11-16T01:20:31Z` UTC, all 122 hashes and recorded file states matched: 88 clean, 30 pre-existing modified and four pre-existing untracked inputs. HEAD was unchanged; no pre-existing dirty path was omitted or changed, and this proposal was the only added output. Formatting checks passed. All inputs were successfully frozen and verified. This verification record changes only the excluded output, not the verified inputs.
