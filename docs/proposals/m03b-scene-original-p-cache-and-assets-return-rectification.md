# M03-B Scene original-P, cache, and Assets-return rectification

**Disposition: tentatively approved for bounded implementation under the requested blocker-proposal policy; owner review and qualification remain open. Keep `PreparedScene.QualifiedFootprint` null, the CPU-resource-preparer and bundle-settlement refusals intact, and Scene-dependent native/public-consumer and bounded-tool CLI guards closed.**

## 1. Decision and bounded scope

Make the existing `PreparedScene` the issuer of every Scene-owned construction, cache, read-invocation, build, installation, and return partition of its **original** preparation cap. Keep the same dictionary and exact package-backed Scene/Prefab leases across pre-count and build. Install an Assets-owned acquisition/read owner before taking a temporary backing reference; retain that owner through destination installation and actual enclosing return. Settle only the retained graph into the existing Entities-owned receiver, leaving temporary and protected return claims with the preparation.

The first slice is the existing built-in Scene/Prefab **2.0**, EPKG2/envelope **2.0**, package-`None`, **no CPU-resource-preparer** path. Automatic reference inspections still check same-backing envelope/schema/registration membership; they do not execute target codecs. This proposal does not admit Rendering bundles, ownerless package/measurement overloads, nonempty restore, custom/Brotli reads, dynamic animation, or the separate two-codec tool lane. Those existing paths retain their own owners and gates.

**No all-outcome executable route is supported by the current source/contracts.** The missing installed Assets operation, original-Store I/O/lifetime provision, finite fault/context attribution, and actual-return joins cannot be supplied by another Scene array allowance. The ordered work below permits implementation to progress tentatively without treating any unavailable term as zero or making a footprint usable.

This is one current-revision rectification proposal, not a replacement allocation-site catalogue or a competing public contract. The pre-count proposal's field-free bookkeeping was an unproved candidate, not a requirement: a dedicated Assets slot is needed to avoid overwriting destination owners while read/install/return obligations overlap. Its actual fields and generated storage must be funded; proposed names do not establish sizes.

## 2. Current revision and confirmed evidence

Source review and the fresh bounded input freeze use:

```text
HEAD: 14f520edaaaaeb03ad08096afdc92c0169a47745
Commit: Reserve Scene reference validation arrays
Freeze UTC: 2026-11-20T20:48:50.1559485+00:00
```

There are five pre-existing dirty Scene/Sprite test files, separately preserved in section 9. No source or contract file is dirty at this boundary. This assignment adds only this document; it performs no build, test, native execution, commit, or remote action.

In the following tables, `Scenes\...` means `sources\TerraFX.Runtime\Scenes\...`, `Entities\...` means `sources\TerraFX.Runtime\Entities\...`, and `Assets\...` means `sources\TerraFX.Assets\...`.

| Current source location | Confirmed fact and consequence |
|---|---|
| `Scenes\PreparedScene.cs:61-149` | The null footprint guard precedes description/use acquisition. A separate CPU-resource-preparer refusal remains. Description currently precedes `AcquireGuidUse`; its allocating failure/cold prefixes are not paid by a use that does not yet exist. The constructor additionally creates its gate, TCS/Task, fault array, and callbacks. |
| `Scenes\PreparedScene.Preparation.cs:129-267`; `Scenes\ScenePreparationFunding.cs:13-44` | Producer subclaims subdivide already reserved producer workspace. They are not new World/domain reservations. The receiver helper requires the exact **pending** owner, outstanding claims equal to retained data, and one global construction-attempt election. A phase-owned builder with live temporary/return claims cannot use this helper unchanged. |
| `Scenes\SceneLoader.cs:236-269`; `Scenes\ScenePrefabExpansionPlanner.cs:143-317` | The owned supplier constructs captured reservation delegates and invokes the described root read. The returned root lease is staged only after the await. Builder construction allocates the Prefab dictionary in its field initializer before builder staging. Neither interval currently claims the producer provision. |
| `Scenes\ScenePrefabExpansionPlanner.cs:914-1076,1466-1497,2222-2320` | Pre-count acquires and retains Prefabs before computing `requiredBytes`; the ordinary reservation follows counting but precedes fill/build/validation arrays. A miss keeps the issued lease local; `lease.Value` at line 2242 is even outside the insertion cleanup `try`. Context checking and `Add` precede installation acknowledgement. The same cache moves into the plan. |
| `Scenes\ScenePrefabExpansionPlanner.cs:284,1641,1807-1812,2301-2303`; `Scenes\ScenePreparationDataBounds.cs:18-25` | HEAD now retains the component-group scalar and adds `144K + 16R_bind`. K counts Component-role records per expanded occurrence, excludes EntityTags, and applies RootPosition add/remove deltas. This closes the grouped-array reservation candidate, not acquisition, original-P, retained storage, or return. |
| `Scenes\ScenePrefabExpansionPlanner.cs:2065-2090,2172-2192,2323-2398` | Fill/expand still call the miss-capable `GetPrefabAsync`; selector pre-count can also load nested Prefabs. `GetSource` already requires retained lookup. A cache-only post-count route must prove membership for all these callers, including RootPose and override selectors. |
| `Assets\AssetLease.cs:140-147`; `Assets\AssetLeaseLifetime.cs:77-93`; `Assets\AssetPackageLease.cs:95-117`; `Assets\AssetStore.ReadDescriptor.cs:59-104` | Anchored facades acquire a temporary backing reference before typed admission/cancellation checks. Refusal can therefore require final physical release with no issued result or admitted lease credit. The package facade has the same ownership interval. |
| `Assets\AssetStore.cs` (`AcquireLoad`, `ValidateAssetReferencesAsync`); `Assets\AssetStore.PackageLease.cs:127-209` | Store admission revalidates the descriptor and reserves wire/lease/decode credits. Initial decode credit is returned before sequential reference inspections, while semantic output and references remain live. Those counters do not fund their managed graph, invoker, or return storage. |
| `Assets\AssetEnvelopeDecoder.cs:474-537`; `Assets\AssetReferenceScanner.cs:116-205` | Ordinary described Scene reads still select allocating `Scan` (List, HashSet, immutable result). Fixed scan sizing and caller-buffer scan/copy helpers exist. `DecodeBounded` at lines 70-152 already uses them in a separate scalar-failure candidate, but its indexed callers are three Assets tests, not the Scene read path. Do not import its different failure mapping silently. |
| `Assets\AssetPayloadReader.cs:16-37`; `Assets\AssetPackageBacking.cs:187-193`; `Assets\AssetPackageReader.cs:583-606` | `None` still allocates the decoded array and reaches the potentially short-reading `RandomAccess.ReadAsync` loop through engine-authored `Memory<byte>`. It removes Brotli work, not I/O ownership, suspension, or Chapter 02's source restriction. |
| `Assets\AssetStore.cs:63-78`; `Assets\AssetStore.PackagePin.cs:14-31`; `Assets\AssetLeaseLifetime.cs:163-237`; `Assets\AssetPackageBacking.cs:300-408` | Lease ownership commits only after lifetime and handle construction. Package-pin carrier construction now precedes reference/count acquisition. Final issued release records backing consumption before Store accounting; failures retain distinct debt. Preserve these completed orderings. The failed-release root establishes reachability, not funding or native-close attestation. |
| `Entities\World.ScenePreclaimedWorkspace.cs:15-72`; `Entities\WorldAdmissionDomain.ScenePreclaimedWorkspace.cs:52-151`; `Entities\WorldGuidUseControl.cs:103-171` | Real same-use preclaimed construction and checked ownership transfer exist. Exact World/domain/control/generation and active-workspace invariants remain mandatory. Scenes needs a partition-aware adapter, not another issuer or relaxed lower guards. |
| `Scenes\PreparedScene.Preparation.cs:400-430,580-776,790-912`; `Scenes\PreparedScene.cs:638-755` | `_supplierProducersUnjoined` is set and never cleared. Child Task readiness, callback-local completion, cleared aliases, and public `TrySet*` do not authorize supplier/context/dispatcher handback. Producer workspace cannot return with outstanding scalar claims. |
| `Scenes\ScenePrefabExpansionPlanner.cs:819-912`; `Scenes\SceneExpansionPlan.cs:285-369`; `Scenes\SceneInstance.cs:411-416`; `Entities\WorldSceneStorageReservation.cs:69-104` | Cleanup attempts independent leases before storage return. However, repeated plan `Dispose` can return after its initial latch even when first cleanup failed, and a returning reservation can also return without completed credit release. Instance revocation detaches its plan before cleanup. Neither a no-op return nor detachment proves actual cleanup; surviving reservation/preparation roots must retain debt. |

### Existing observations, not new qualification

The existing focused Runtime TRX files were read and hashed:

| Configuration | Artifact under `artifacts\tst` | Selected/executed/passed | SHA-256 |
|---|---|---:|---|
| Debug | `Debug\tagoo_GOODING-ZEN4_2026-11-20_12_34_29_net11.0.trx` | 62/62/62 | `ed7cfe4f06cd14100827d635e69839b002d84b62cb2ce04218005f5470616b1c` |
| Release | `Release\tagoo_GOODING-ZEN4_2026-11-20_12_35_32_net11.0.trx` | 62/62/62 | `319878e14f354ede1d7c0838987855e7e39d3f4e706341f18cb9ee4904111542` |

Membership is 16 `SceneAssetReaderTests`, 25 `SceneAssetStoreTests`, and 21 `SceneHierarchyValidatorTests` results per configuration, not a full-suite or conformance-case count. The current builder-shell test asserts **456 B**, replacing 448 B (`SceneHierarchyValidatorTests.cs:484-512`). The group-array test covers zero and nonzero group counts (`:276-300`).

Both focused artifacts also report dictionary growth over 1,048,575 entries: `AllocatedBytes=75443600; AllowanceBytes=268435280`. The test uses **null values**, precreated keys, and thread allocation traffic (`SceneHierarchyValidatorTests.cs:110-159`). It corroborates the collection shell/cumulative-growth allowance only; live lease/value graphs, cold initialization, failed insertion, cleanup enumeration, and return are excluded.

A neighboring broader Debug artifact (`tagoo_GOODING-ZEN4_2026-11-20_12_33_01_net11.0.trx`) records 258 total, 248 executed, and 141 failed results. Inspected failures reach the null-footprint refusal. It is not a wider pass or native/consumer evidence.

## 3. Owning contracts and conservation

| Owner contract | Applicable requirement |
|---|---|
| Chapter 04, lines 7-21, 58, 105-153, 193-207 | Reserve complete allocation/growth/retirement peaks before work; preserve the exact requested preparation cap and existing World/domain/Assets limits. Count ceilings do not promise joint byte fit. |
| Chapter 11, section 3 | Exact immutable backing, independent admitted references, unchanged Store admission/replacement/disposal semantics, and actual decoder/consumer end. Wire/count credits are separate from managed storage and I/O control funding. |
| Chapter 16, lines 116-128 | W explicitly includes pre-count/cache, Tasks/results, callbacks, and unreleased cleanup. Receiver coverage is actual owned allocations/capacity, not `256 * prefabCount + constant`; temporary work cannot be transferred as retained D. |
| Chapter 27, lines 55-69 | Capture/consumer, data-owner, cleanup, and quarantine lifetimes are distinct. Publication/readiness/cancellation do not discharge actual I/O/callback consumers. Empty-Scene restore does not qualify this producer. |
| Chapter 29, lines 47-109 | Entities alone issues World/use/domain capabilities. Assets remains below Runtime and depends only on Core; no Runtime funding callback, Scene scheduler, or World capability enters Assets. |
| Chapter 30, lines 3682-3683, 3913 | Preserve the public Scene/Assets signatures, exact cap, input-copy ordering, exact-backing semantics, ordinary quota checks, and fault/cancellation channels. |
| Chapter 35, capability sequencing and sections 2.3-4 | A missing capability blocks its consumers, not unrelated units. Compile/layout, managed behavior, native/AOT/public-consumer observations, and milestone acceptance are separate. |
| Chapter 02, lines 76, 101-121 (supporting constraint) | Use typed owners for retained I/O, actual completion, original faults and bounded cleanup samples. The linked Chapter 11 “M03 4.7” recovery-delivery definition is absent from the current Chapter 11 and needs owner closure, not an invented exception API. |

Use the existing original-use accounting, with disjoint membership:

```text
P = W = the original MaximumPreparationBytes, unchanged
P = F + X + T
U = 256 + F + X

F: original capacity not committed to active provision
X: active/protected original provision, including unspent issued partitions and debt
T: transferred-out capacity; installed receiver release does not replenish P
```

The process registry's P credits are a different quantity. The 256-byte independent-use control is not spendable Scene scratch. `_producerWorkspaceClaimedBytes` partitions producer provision already inside X: issuing/returning a child subclaim does not reserve/release the same domain bytes again.

### The new eight-byte field

`_referenceBindingComponentGroupCount` is one `long` in **each builder shell**, not eight bytes per group, Prefab, or binding. On the observed x64 runner:

```text
H_builder(current) = 456 B = H_builder(previous) + 8 B
```

Charge the **whole 456-byte shell** in the original producer construction partition before calling the constructor, together with its separately allocated dictionary, invocation/generated state, and constructor/unwind/return provision. Do not add another eight-byte charge after already charging that shell. The builder is temporary X, not retained plan D; its charge lasts through actual builder/invoker return, including failed construction and cleanup. Future slot/progress fields invalidate the shell measurement and require whole-layout remeasurement.

The separate array bound is:

```text
A_T(n) = 64 + RoundUp16(checked(n * selected managed stride(T)))
two group arrays <= 144K + 16R_bind
```

K is the counted non-EntityTag component-group total after RootPosition deltas; `R_bind` is the expanded source binding count, bounding present group bindings. This accounts for the two reference-element arrays per group, including groups with no present binding. It does not pay binding/path objects, sort/comparer initialization, other validation, the builder field, or return. These local validation arrays are **temporary**, not retained D.

The existing final `2048` and `256 * prefabLeases.Count` cannot be presumed to cover the shell increase or whole root merely because they look large. Reconcile every site once against the original ledger.

### Cache/read partitions

Define, as private checked sizing functions rather than new quotas:

- `CachePrefix(n)`: a monotone upper provision for the existing dictionary shell, buckets/entries, old-plus-new growth, and allocation-reaching failure prefixes after n successful unique insertions. The observed `80 + 256*n` is a candidate collection allowance, not a qualified live-cache profile.
- `CacheRetained`: the shell plus the **actual surviving capacities**. Obtain capacities through a qualified non-growing query of the existing dictionary; `EnsureCapacity(0)` is a candidate BCL query to verify against the selected implementation, not permission to pre-grow or use private-field reflection.
- `C_op`: installed Assets control/construction + Runtime invocation/registration + parent/scan/semantic/inspection/issue prefixes + retained-output upper provision + cleanup/fault/return. Its J/K terms remain unavailable.
- `D`: the exact surviving Scene/Prefab semantic graphs, owned record/path/component/literal backings, actual lease/state/lifetime carriers, retained cache capacities, plan shell, and retained expansion/assignment/provenance arrays. Count a shared immutable backing once, each separate carrier once, and occurrence-specific output arrays separately.

Before a miss at count n, fund the positive cache-prefix increment and the complete selected operation **while all previous cache D remains charged**. Description/cold/refusal work must already have provision. Use the descriptor's actual parent length and exact backing limits; automatic-target sizing requires that backing's maximum admitted target length, not the smaller parent length. Do not read first to discover a cheaper allowance.

Reader copies, the eight-array hierarchy workspace, reference occurrence/index/result arrays, RootPose/override work, builder/List backings, discarded copies, and return machinery remain separate temporary/protected terms. Reuse the allocation-site catalogue's source-derived prefix functions, revalidated against current bytes; do not implement a second decoder as an oracle. Shared schemas/registration/Store/backing/I/O may be excluded from Scene P only when their actual independent lifetime provision is identified.

For a fully covered retained receiver:

```text
R_receiver = D + Q_receiver
X -= R_receiver; T += R_receiver; U -= R_receiver
exact World-owned receiver attribution += R_receiver
```

Only exact D and receiver-owned provision transfer. The current `WorldSceneStorageReservation` helper adds **72 bytes** (`Entities\WorldSceneStorageReservation.cs:10,63-67`); that local constant is not a whole-graph profile. Temporary/protected claims remain under the preparation. Construction can first place R in a reservation's **workspace** ownership without a second debit; installed-storage transfer occurs once through the existing lower transaction. Preserve existing `+R/-C` and `R-C` checks for any genuinely partial coverage. No refund/reacquire interval is allowed.

## 4. Allocation and ownership intervals

| Real owner | Begins before | Required end/transfer |
|---|---|---|
| Original `WorldGuidUseControl`, then `PreparedScene` | Funded bootstrap description/root construction, option copies, supplier/delegate invocation | Unescaped failure/unwind retains its existing lower anchor; normal use ends only after body and producer roles, outstanding claims, and actual return all end. |
| `SceneExpansionBuilder`, retained through the preparation phase slot | Constructor/field initializers and traversal, including the new scalar's whole shell | Exact cache/data adoption into the staged plan; builder-only storage and enclosing return remain X until their actual end. |
| Builder's existing Prefab dictionary | Empty-shell construction and each positive growth/miss prefix | Same dictionary/capacities move to the plan. No eviction/reload, replacement dictionary, or count/build gap. |
| Dedicated installed Assets operation, rooted by `PreparedScene` | Source reference acquisition, Store credits, payload/scratch/value/candidate construction | Exact result-and-charge installation; independent cleanup and actual lower/caller/registration/context return. The slot remains occupied through old-generation debt. |
| Original Assets Store/backing lifetime | Generation construction/open/hash/metadata and shared I/O machinery, independently of Scene | Actual final references/native use end, one-shot file disposition, and lower control/worker/publication retirement, possibly after logical Store/World close. |
| Assets operation's typed buffers/reference/semantic progress | Each allocation and native input-use interval | Synchronous scan/codec/inspection return and actual I/O consumption end; semantic surviving subset transfers to the cache, not scratch or the codec context wrapper. |
| Existing Scene pending/phase installation slots | Root-lease→builder and builder→plan adoption; independently staged receiver construction | Exact donor/receiver election and acknowledgement. Occupied/failed slots cannot be overwritten to free an Assets slot. |
| `WorldSceneStorageReservation` and actual plan/instance | Preclaimed receiver construction and installed retained ownership | Actual retained consumers/lease/storage/accounting release. Returning/disposed latches alone do not close a failed release. |
| Preparation publication/cleanup provision and each real enclosing invoker | Failure capture, completion registration, public publication and context scopes | Registration, single consumption/reset, callbacks/dispatch/context restoration, publication calls and own tails return or transfer to an identified prepaid lifetime. Readiness is insufficient. |

**Distinguishing schedule, derived rather than executed:** Chapter 16's P and Q placements both select B. Retain one B lease but expand two B occurrences; entity count and K are not unique-cache cardinality. Replacement between count and build must not change that lease's backing. Store close can refuse a fresh anchored load while an existing cache hit remains usable. A miss that issues a lease but fails `Value`, context validation, or insertion must remain rooted even though dictionary `Count` did not increase.

## 5. Ordered implementation sequence

Each step is a bounded coherent batch. No intermediate step makes a footprint available.

1. **Close current data/cache projections first, without Scene execution.** In the existing Scene sizing area, derive checked parent-reader prefix upper bounds, live retained-graph projection, dictionary construction/growth and post-build retained capacities. Keep the implemented K bound and 456-byte shell as separately classified inputs. Include malformed/late-rejecting prefixes, not only final valid records. Freeze final code-generation/profile inputs; no unexplained fixed padding or production-output oracle. This can progress while lower I/O/contract decisions remain open.

2. **Make original-root provision and installation elections explicit.** In `PreparedScene`, its preparation partial, and `ScenePreparationFunding`, add one dedicated Assets owner slot with embedded exact operation/destination identity, nonwrapping generation/state, checked claim partitions, and construction/install/return interests. Preserve phase/pending slots. Elect/preclaim under the existing root gate before supplier/delegate/builder construction; construct/advance/cleanup outside gates. Move potentially allocating root description inside the issued use's bootstrap provision, preserving public shape/pre-cancellation checks and reviewing admission-error precedence. Include failed description/root/child construction and all generated/cold storage.

3. **Complete the lower Assets prerequisite, not a Scene workaround.** Continue the already proposed original-Store, whole-generation package-I/O owner/port direction. It must have acquisition-free original funding, retained registration/backing eligibility, real typed-buffer input ownership, all sharing/read/inspection/replacement paths, one-shot native close, and actual request/control/worker return. Do not wrap or migrate an already BCL-owned generation. The existing B1 native port/metadata parser/bounded decoder are compile-only components of a different, synchronous, two-codec lane: ordinary backing construction leaves `_boundedNativeFile` null; bounded `TryAcquireReference` refuses ordinary references. B1 preclaim rejects cancellable tokens and incompatible context (`AssetStore.Bounded.cs:36-52`). These components cannot supply the async Scene capability or original-fault channel by substitution.

4. **Implement one complete installed Scene/Prefab read slice.** Only after step 3's lower and section 6's ownership prerequisites are supplied, implement acquisition, typed read/reference inspection, lease issue, result staging, cleanup, and return in the real Assets progress owner. Its empty constructor acquires nothing. Start only after the root has attached that exact owner and C_op. Preserve descriptor/receipt/source/backing revalidation and current Store policy. Install every acquired reference, credit, array, semantic prefix, lifetime/handle candidate, and issued result before the next fallible step. Use the existing caller-buffer scanner in an owner-bearing decoder path that preserves ordinary Scene failure mapping; do not switch blindly to `DecodeBounded`.

5. **Connect acquisition to the existing cache, including failure.** Root-lease delivery remains owned before builder construction. Keep the builder phase owner; retain the Assets slot during Prefab `Value`/context/insertion and exact result/charge acknowledgement. Hits borrow the same lease and need no new read; misses preclaim before description/acquisition/growth. On an uninstalled result, the installed operation/destination election owns cleanup. Do not clear the operation merely because the cache accepted its lease: old read/invoker return still owns its separate partition. Aggregate retained claims and existing cache membership avoid new per-entry claim nodes.

6. **Use a phase-specific, partition-aware receiver adapter.** Extend Scene-owned funding rather than removing `TryReserveProducerStorage` safeguards. Bind a one-shot plan construction election to the exact current builder and candidate receiver, separately from a future bundle election. Verify that the retained D slice and Q are included in outstanding original claims while temporary/protected slices remain outstanding; total-claim equality with D is not the right predicate here. Use the existing Entities preclaimed constructor. Stage/validate the real plan carrier and exact adoption before irreversible installed-storage transfer; construction must retain donor, candidate, and installation provision across failure/World close. Transfer only D/Q once, then drop donor aliases. The current `requiredBytes` mixes scratch/estimates, so it cannot be passed unchanged as retained D.

7. **Close the higher actual-invoker route before handback.** Prove the pre-count cache membership union, then make post-count fill/expand/RootPose/override lookup cache-only; a missing entry is an invariant failure, never fallback loading. Convert only the selected suspension plumbing to ordinary operation-specific traversal/advance calls driven by the existing root, sharing the existing algorithms rather than duplicating them. A resumable count traversal needs its real bounded state/frames funded; the inline AssetId path is not a complete free resume stack. The actual Runtime invoker consumes each real lower completion once and observes ordinary Assets/build/installation/cleanup calls plus their context scopes returning. Preserve logical default/nondefault/suppressed context behavior and no caller SynchronizationContext capture. Replace `_supplierProducersUnjoined` only with the complete fulfilled join set, not another observer or a producer-written `Returned` bit.

8. **Review the composite profile, then separately qualify consumers.** Join construction/start, registration, native input-use, single consumption/reset, installation, helper/context/publication and cleanup/accounting returns. A successor cannot spend an old returning partition. Prove any remaining dispatcher/native tail is parent-only or belongs to a named already funded lower lifetime; otherwise keep the gate closed. Rehash final inputs and demonstrate useful all-outcome original-P fit before a later footprint decision. Rendering/helper/measurement receivers and their producer profiles require their own closure before CPU-resource-preparer refusal can change.

A complete ordinary read/advance slice is the integration boundary, not an empty owner type or stub port. Implementation of pure data/partition/election pieces may precede it; operational acquisition may not.

## 6. Smallest missing capability and owner decisions

There is no new cache-policy decision to make: Chapters 16/30 already require original-cap funding and exact retained transfer. The receiver adapter and dedicated installed child are private implementation gaps within that policy.

Two genuinely missing owner boundaries prevent a useful all-outcome C_op:

1. **Original Assets generation financing and actual release capability.** Current wire/decode/lease budgets and the intrusive failed-release root do not provide a qualified Store/open/shared-I/O/late-close grant. The existing same-original-envelope B0 direction requires an explicit Chapters 04/11/30 control/lifetime earmark decision before operational selection; it is not the present meaning of wire-byte accounting. Require a real eligible retained registration/backing composition. Factory receipts close executed codec selection, not arbitrary retained General-registration graphs. Do not prune/copy caller registrations or import the unrelated two-codec seal to manufacture eligibility.
2. **Retained input, original-fault, and public-return attribution.** Current tokens/ExecutionContext, runtime exception/EDI captures, original retained cleanup failures, and exposed mutable exception graphs have no useful bound derived from payload length or two exception slots. Recommend closing the Chapter 11 recovery-delivery boundary referenced by Chapter 02 for this selected lane: identify the actual lifetime-paying owner and transfer/end boundary for these graphs and for pre-admission/public promise delivery, while preserving original primary/cleanup ordering, original cancellation token, repeated-close debt, and context behavior. Engine-created holders/captures/dispatch remain funded; merely relabeling arbitrary references as borrowed is not an independent grant.

The narrow decision is **ownership/attribution and original-fault delivery at those boundaries**, not a new public exception, smaller P, new cap, or global free-runtime exemption. If a finite compatible paying lifetime/transfer cannot be supplied, owner review must explicitly choose a narrower input/error/context contract before admission. This proposal does not choose that semantic change. An all-outcome numerical profile remains unavailable meanwhile, even with valid small payloads; ordinary-success permanent quarantine is not a solution.

The prior original-P, actual-invoker, allocation-site, pre-count, and file-I/O proposals remain tentative historical guidance. Their earlier source manifests, runtime-source lanes, and measurements are not current executable qualification. This proposal's freeze is newly computed at HEAD, not copied from those manifests.

## 7. All-outcome behavior

| Outcome/interval | Required behavior |
|---|---|
| Usage/pre-admission cancellation | Preserve the current synchronous usage and pre-canceled Task channels and their ordering; issue no Scene/Assets work. Do not claim that a never-issued P paid pre-admission promise/error capture; its ownership boundary is included in section 6. |
| Unknown profile, insufficient provision, occupied/exhausted election | Refuse before the next constructor, miss, acquisition, decode, callback, or receiver work. No unbounded waiter, reduced cap, ordinary-load fallback, zero-byte interpretation, or empty successful Scene. |
| Stale current descriptor, wrong receipt/backing, disposed source/Store, quota refusal | Revalidate through existing admission policy. An anchored old backing never retargets. Record/release any temporary reference already acquired at the existing check point, even with zero typed credits; source/Store close can make that release final. |
| Cancellation after acquisition/issue | Stop new work, but retain native destinations/backing/progress until accepted consumers and actual return join. Observe cancellation at the existing contract points; cancellation is not input-use end. Successful unwind preserves the original token; cleanup failure faults an otherwise canceled operation. |
| Decode/inspection/constructor failure | Retain partial arrays, semantic graph, candidate lifetime/handle, credit/reference facts, and construction/return provision. Preserve codec translation and original failure precedence. Inspect targets sequentially only when the previous target's actual consumers/returns ended or their overlap is charged. |
| `Value`/context/`Add` or installation failure | Before acknowledgement, the exact issued lease remains operation/destination-rooted, regardless of dictionary count. On uncertain adoption retain both relevant roots and the installation interest; do not dispose/refund one speculative side. |
| Successful cache/plan installation | Move exact lease/value/cache or plan ownership and its retained claim once. Keep temporary/protected return claims separate. Cache acceptance and a ready plan Task do not recycle the old operation slot. |
| Physical release succeeds, accounting fails | Record irreversible reference/native consumption before Store accounting. Preserve accounting debt and its original owner; never close or consume the same reference again. Use the issued lifetime's existing distinct release states once it owns the credits. |
| Failed/indeterminate close, cleanup, registration, publication, or claim return | Continue independently safe sibling cleanup outside gates, preserving original samples and bounded scalar facts without recovery lists. Still-consuming work remains live; stopped-but-unreleased state stays charged/quarantined through Store/World/domain closure. No-op repeated `Dispose`, `IsClosed`, Task terminality, cleared fields, or GC are not acknowledgements. |
| Actual return | The real enclosing caller observes lower consumption/reset, ordinary calls, installation, cleanup/accounting, context restoration and publication returning. End only the exact completed claim; all surviving tails have named funded ownership. Do not reissue into an old generation's returning interests. |

Native disposition must distinguish proved nonfinal reference release, confirmed final consumption, proved retention, and indeterminate ownership using the actual lower owner. A raw handle number, another SafeHandle, or normal managed `Dispose` return cannot reconstruct that fact. No physical-close failure is observed by this document.

## 8. Acceptance evidence and producer prerequisites

These are existing owner obligations and future measurements, not new acceptance IDs or results. Use ordinary owning-project NUnit/VSTest commands from `docs\project\Building.md`, the SDK selected by `global.json`, Debug before Release, combined related filters, nonempty executed selections, and fresh retained artifacts.

| Boundary | Measurable evidence / producer prerequisite |
|---|---|
| Whole-root/data/cache provision | Final whole carriers, field initializers, generated invocation/resume state, array strides/capacities, cold/helpers/gates, constructor failure and return coexistence. Reconcile the 456-byte builder baseline and subsequent layout changes once. Derive conservative bounds independently; observed allocation traffic corroborates them. |
| Live cache prefixes | Real non-null Scene/Prefab leases at insertion/growth boundaries; prior retained values coexist with the next complete miss. Distinguish unique-cache count, occurrences, entities and K; exercise empty Prefabs and zero-binding component groups. Null-value million-entry traffic remains collection-only evidence. |
| Original-P admission | Actual precreated/rooted production issuer and complete all-outcome profile. Exact-fit/one-byte-short trials use independently measured baseline plus the complete delta and actually reach the named boundary. Verify no second World use/debit, no acquisition before claim, and no early reuse/refund. |
| Exact-backing acquisition | Real packages, current factory receipts and original-Store capability. Exercise stale-current refusal, retained old backing across replacement, Store/source close, and failed admission with a temporary reference. Preserve ordinary unselected Assets behavior. |
| Owned decoder/reference work | Installed read owner and prepaid buffers. Literal full-triple deduplication/first-occurrence expectations, zero/one/odd/even counts, exact/short/overlap cases, target larger than parent, semantic-invalid/late-rejecting prefixes, and immutable publication. Test the actual selected owner path, not just scanner helpers. |
| Retained receiver conservation | Real exact World/use/participant and staged plan, with live temporary/return claims. Prove D/Q provenance, capacity/alias membership, phase-specific one-shot construction, closed-World/refusal/failure behavior, and exact `+R/-C` conservation. Existing lower receiver tests do not certify a Scene root. |
| Actual lower and higher return | Real completion source/port, single-consumption and registration/queued/running/returning schedules, synchronous and suspended outcomes where supported, context restoration/reentrancy, installation and public publication holds. Freeze selected runtime/compiled/native correspondence; a self-report or another await is not evidence. |
| Actual cleanup/debt | Real acquired references/generations, nonfinal/final release, accounting after consumption, sibling cleanup, retained plan/reservation debt, and Store/World/domain close. Native constructor/short-read/cancellation/close-failure cases require a safe real stimulus; report unavailable branches and their retry condition, not throwing fakes as physical evidence. |
| Scene producer, then M05 consumers | Closed original-P/C_op/J/K, Assets generation/read/return, cache/build/receiver, and body/publication lifetimes first. Rendering then needs its actual Mesh/Texture/Atlas/Material and static Skeleton helper/bundle profiles and receivers; `RenderSceneAssetBundle.cs:469-473` still refuses funded settlement. Only real committed/restored Scene/camera/CPU extraction and the applicable M04 device can produce native renderer observations. |

Do not weaken the five dirty tests, literal fixtures, schemas, caps, or guards to obtain a green selection. No synthetic producer, fabricated `PreparedScene`/snapshot, corrupted private counter, or fake native-close acknowledgement closes a missing producer.

**Independent M05 path:** continue contract-complete CPU kernels/layout, shader compilation/reflection, source-adapter/native-content work, diagnostic/whole-graph ledger work, and other dependency-ready implementations under their own actual owners. Keep their acceptance local. Scene-dependent World extraction, nonempty restore, positive native diagnostic/pixel and public-consumer observations wait for the named Scene/Assets/Rendering producers; full M03-B is not a blanket implementation prerequisite.

## 9. Fresh bounded decision-input manifest

This is an **81-file task-scoped decision freeze**, not a repository-wide inventory, complete executable closure, runtime/native attestation, or new qualification pipeline. Whole-file hashes bind inspected ranges. The output file is excluded.

For each group below, expand its prefix into repository-relative Windows paths. Hash raw file bytes with SHA-256. The group digest is SHA-256 of UTF-8 without BOM/trailing newline over ordinal-sorted `relative-Windows-path<TAB>lowercase-raw-file-SHA256` lines joined with LF. ALL is the unique union. Before applying a later disposition, recheck HEAD, membership, raw hashes and dirty state; changed bytes require reassessment.

| Group | Files | Fresh SHA-256 |
|---|---:|---|
| C — conventions/owners | 23 | `ca3f0021620c485d2ca895f46f6c4412b7314328d215f9a6d915ce536c63098a` |
| G — historical guidance | 5 | `b743403791978e5dc61c3b654ae4e3a208e3a17ff7429635bd49726e2e0814e4` |
| S — Scene implementation/schema | 18 | `f767ca70d15eca135cb9b1c43bb8a9cae20e71e41b03888dd2ae7afb15b7873e` |
| A — Assets implementation/schema | 17 | `73e8ef3cd1091531e8bfbb6596a458dafb21d153fd2c013af8e93bf92919a013` |
| E — Entities ownership | 5 | `978ac21546bdf207a57c810102a803b0c2ca37e30fcc649286a06dbc5fb85e46` |
| R — Rendering guard | 1 | `1987a9cc43dc2783efac3abb7d4d0ff4763ccbb63a46694b8b3fa110ab586d98` |
| T — related test source | 4 | `4e86dd3916bc87d40fef34566f85868eb07ca519bcd90bda21e947863384995d` |
| D — dirty preservation inputs | 5 | `0247a70f5dedcaac56865e99640c263109f72120dff8cf7a79b78257305b2162` |
| V — inspected existing TRX | 3 | `bd4cfc2cce94c30aa8070b20c13850fcf85a5a436ff47556af6db04128903bc5` |
| ALL | 81 | `9d00f185259a4152dee8029dff95bf36f5604e1fa7168eebaeb24906c8cf39d4` |

```text
[C .]
.editorconfig
.github\copilot-instructions.md
.github\workflows\ci.yml
global.json
Directory.Build.props
Directory.Build.targets
Directory.Packages.props
sources\Directory.Build.props
tests\Directory.Build.props
sources\TerraFX.Assets\TerraFX.Assets.csproj
sources\TerraFX.Runtime\TerraFX.Runtime.csproj
tests\TerraFX.Assets.Tests\TerraFX.Assets.Tests.csproj
tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj
docs\project\Building.md
docs\project\Roadmap.md
docs\design\02-dotnet-api-memory-and-ownership.md
docs\design\04-capacity-and-workload-profiles.md
docs\design\11-assets-import-streaming-and-packages.md
docs\design\16-scenes-transforms-and-cameras.md
docs\design\27-state-serialization-replay-and-recovery.md
docs\design\29-project-setup-build-and-deployment.md
docs\design\30-public-api-review-surface.md
docs\design\35-implementation-roadmap.md
[G docs\proposals]
m03b-scene-prefab-precount-funding-rectification.md
m03b-scene-assets-original-p-operation-and-return-closure.md
m03b-scene-assets-allocation-site-catalogue-addendum.md
m03b-scene-actual-invoker-return-closure.md
m03b-assets-file-io-port-implementation-closure.md
[S sources\TerraFX.Runtime\Scenes]
SceneLoader.cs
PreparedScene.cs
PreparedScene.Preparation.cs
ScenePreparationFunding.cs
ScenePreparationDataBounds.cs
SceneExpansionLimits.cs
ScenePrefabExpansionPlanner.cs
SceneExpansionPlan.cs
SceneInstance.cs
SceneSupport.cs
SceneAsset.cs
ScenePrefabAsset.cs
SceneAssetCodec.cs
ScenePrefabAssetCodec.cs
SceneAssetReader.cs
SceneHierarchyValidationWorkspace.cs
SceneEntityReferenceBinding.cs
SceneSchemaCatalogue.cs
[A sources\TerraFX.Assets]
AssetStore.cs
AssetStore.ReadDescriptor.cs
AssetStore.PackageLease.cs
AssetStore.PackagePin.cs
AssetOwnedReadDescriptor.cs
AssetStoreRegistration.cs
AssetLease.cs
AssetLeaseLifetime.cs
AssetPackageLease.cs
AssetPackageBacking.cs
AssetPackageReader.cs
AssetPayloadReader.cs
AssetEnvelopeDecoder.cs
AssetReferenceScanner.cs
AssetStore.Bounded.cs
AssetBoundedNativePort.cs
NativeAssetSchemaCatalogue.cs
[E sources\TerraFX.Runtime\Entities]
WorldGuidUseControl.cs
WorldSceneStorageReservation.cs
World.ScenePreclaimedWorkspace.cs
WorldAdmissionDomain.ScenePreclaimedWorkspace.cs
WorldAdmissionDomain.SceneStorage.cs
[R sources\TerraFX.Runtime\Rendering]
RenderSceneAssetBundle.cs
[T .]
tests\TerraFX.Runtime.Tests\Scenes\SceneAssetReaderTests.cs
tests\TerraFX.Runtime.Tests\Scenes\SceneAssetStoreTests.cs
tests\TerraFX.Runtime.Tests\Scenes\SceneHierarchyValidatorTests.cs
tests\TerraFX.Assets.Tests\AssetStoreTests.cs
[D tests\TerraFX.Runtime.Tests\Scenes]
PreparedSceneAssetRetentionTests.cs
RenderSpriteCapacityTests.cs
RenderSpriteComponentTests.cs
RendererExtractionTests.cs
SceneAssetStoreTests.EngineHost.cs
[V artifacts\tst]
Debug\tagoo_GOODING-ZEN4_2026-11-20_12_34_29_net11.0.trx
Release\tagoo_GOODING-ZEN4_2026-11-20_12_35_32_net11.0.trx
Debug\tagoo_GOODING-ZEN4_2026-11-20_12_33_01_net11.0.trx
```

Critical current source/schema hashes, computed from current raw bytes:

```text
2fdd86c3b607caaef3e221d8b83015858e630b26a010d90e5d9bdc0e39a43510  sources\TerraFX.Runtime\Scenes\ScenePrefabExpansionPlanner.cs
574a43bb4a07178bb9fed81f0b781d9c413562b6c33b492d36daf77a0380af61  sources\TerraFX.Runtime\Scenes\ScenePreparationDataBounds.cs
8a822c991792d18bbcf3d58718c7c420833c45a261f406487c7a6dd2e2ef3df0  sources\TerraFX.Runtime\Scenes\PreparedScene.cs
beed8cc76c4ccd55b487a96b52a31d4765a4e40513ef09883d7b0f56a201a8ff  sources\TerraFX.Runtime\Scenes\PreparedScene.Preparation.cs
cf0253cf1c207644f0b6c76b0392977e02b0939ff355ed825f38d42705f2b32a  sources\TerraFX.Runtime\Scenes\SceneSchemaCatalogue.cs
6b9537ea8cefebb48b5dc24261a8c03fb059e3314e7da089f29a639d9e55620a  sources\TerraFX.Assets\NativeAssetSchemaCatalogue.cs
```

The only dirty paths at the freeze are these five preservation inputs. Their hashes are newly captured here; they are not acceptance evidence and this assignment does not edit or execute them:

```text
e342237b7645be805dd924855b2239907773d31753d22b8b2261bf26c20452f4  tests\TerraFX.Runtime.Tests\Scenes\PreparedSceneAssetRetentionTests.cs
caa6610cf55f8fffa077745fd96baa45fcecfed2fd051cc763901aeed5500239  tests\TerraFX.Runtime.Tests\Scenes\RenderSpriteCapacityTests.cs
8e65b71b5ac1e0ef9d1f47aa56963054d091df8c229e74ddbb1cae37bb59ab60  tests\TerraFX.Runtime.Tests\Scenes\RenderSpriteComponentTests.cs
abaeea4d0f6be941af73e640b7fce41ec753d2199654343fa4668d54ec20fc6a  tests\TerraFX.Runtime.Tests\Scenes\RendererExtractionTests.cs
4c0841a40295a891b68974fddd8078dfd0181c31b35ab0bcb0b5114fe0fd1039  tests\TerraFX.Runtime.Tests\Scenes\SceneAssetStoreTests.EngineHost.cs
```
