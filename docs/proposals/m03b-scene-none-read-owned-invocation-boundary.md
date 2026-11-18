# M03-B Scene None reads: finite storage and owned invocation return

**Status: tentative implementation guidance under the requested unblocker policy. No complete byte profile or executable preparation lane is established. Keep `PreparedScene.QualifiedFootprint` null.**

## Decision

Keep the selected factory-registered Scene/Prefab 2.0, package-None, no-resource-preparer slice. The [pre-read and owner reconciliation](m03b-scene-none-profile-pre-read-and-owner-reconciliation.md) remains its current-state index; this proposal selects the next implementation boundary, not another status ledger. Commits `295e96ee` and `39874518` are already present.

The managed semantic representation is not intrinsically unprofileable. With the exact schema metadata, built-in readers, compiler/BCL layouts and target fixed, its bounded visits, copies and collection capacities admit a finite allocation catalogue. **Do not replace it with an arena, opaque wire multiplier, second decoder or second preparation carrier.**

The present **Task-only acquisition/result boundary cannot establish actual producer return**, even on a pinned runtime. Pinning software also does not bound arbitrary caller-context behavior or foreign exception capture by the envelope length. Select a narrow internal restructuring: one caller-installed, Assets-owned read-progress control, with acquisition inside that control, a separately accounted retained result, and invocation ownership supplied by the actual enclosing invoker. Split the selected read into ordinary synchronous semantic steps and explicitly owned I/O/resumption steps; preserve the existing decoding and validation algorithms.

Prove that invocation boundary on the real None read **before** undertaking a transitive planner rewrite or assigning numerical `C_op`, J or K. A new `Returned` method called inside the old async producer is not an implementation of it. If the lower I/O/context/publication boundary cannot fulfil the contract below, this lane remains unavailable; more Scene ledger fields cannot rectify it.

Scope excludes Brotli, Rendering participation, package opening/pinning, Persistence, restore and measurement admission. Automatic reference inspection belongs to the one load operation and invokes no target codec body. Unused/custom registrations do not disqualify an otherwise admitted Store; selected typed reads still require their exact factory receipts. Public signatures, exception identities/order, selected cancellation token, context behavior, World ceilings and the original P are unchanged.

## Current source evidence

Locations refer to the frozen working-tree inputs below, not historical proposal line numbers.

| Source | Established fact |
|---|---|
| `sources\TerraFX.Runtime\Scenes\PreparedScene.cs:61-160` | Constructor creates the promise, fault array and callbacks; the null-footprint guard precedes description and use acquisition. The descriptor is already a root field. There is no child Assets operation slot. Old constructor-only byte observations do not size the final carrier. |
| `sources\TerraFX.Runtime\Scenes\SceneSupport.cs:22-54,148-165`; `sources\TerraFX.Assets\AssetOwnedReadDescriptor.cs:7-93` | Factory registration selects the two exact origins and None restriction. The descriptor binds source, registration, codec, lengths, limits and spatial context; `Matches` rechecks them. This is provenance/revalidation, not ownership or a storage grant. |
| `sources\TerraFX.Runtime\Scenes\ScenePrefabExpansionPlanner.cs:143-183,254-313,2135-2177` | The candidate uses described root/Prefab reads, but stages a root lease only after awaiting it. Builder initializers allocate before builder staging. A new Prefab lease remains local through value/context validation and cache insertion. |
| `sources\TerraFX.Assets\AssetLease.cs:139-147`; `AssetStore.ReadDescriptor.cs:59-102` in the same Assets directory | The anchored facade acquires a temporary backing reference before the async admission wrapper. Refusal releases it in a catch without an installed operation retaining that obligation. |
| `sources\TerraFX.Assets\AssetStore.cs:58-87,377-449`; `AssetStore.PackageLease.cs:130-223` | Admission owns counters/reference before the pipeline. `CreateLease` constructs its lifetime before `try` and sets transfer in cleanup `finally`. The pipeline's final rollback can return counters before backing release. Issued-handle repair alone cannot cover this pre-lease interval. |
| `sources\TerraFX.Assets\AssetPayloadReader.cs:16-28`; `AssetPackageReader.cs:219-242` | Even None allocates a payload array and traverses async wrappers plus a potentially short-reading `RandomAccess.ReadAsync` loop. None removes Brotli, not I/O, cancellation or invocation machinery. |
| `sources\TerraFX.Assets\AssetEnvelopeDecoder.cs:225-288`; `AssetReferenceScanner.cs:80-93`; `AssetStore.cs:498-571` | Scanning builds a List/HashSet/result before typed decoding. The decoded value and reference result coexist with sequential target inspection. The target None check precedes target decode reservation/read; actual schema/exact-codec checks follow inspection. |
| `sources\TerraFX.Runtime\Scenes\SceneAssetReader.cs:99-233,237-360`; `SceneEntityReader.cs:47-148`; `SceneAuthoredComponentRecord.cs:11-18` | Readers build the semantic object graph, including component-byte copies and a final encoded-record copy. Immutable wrappers are not necessarily new backings; aliases and actual builder/copy behavior must be distinguished. |
| `sources\TerraFX.Runtime\Scenes\SceneHierarchyValidationWorkspace.cs:19-35,58-80`; `Entities\WorldAdmissionDomain.cs:98-103` in Runtime | The workspace has eight arrays. Its local eight-byte rounding is not the domain's sixteen-byte accounting rule. Neither returned reservation number is whole-operation qualification. |
| `sources\TerraFX.Runtime\Scenes\PreparedScene.Preparation.cs:400-430,790-850,874-910` | The supplier latch is set before planner invocation and never cleared. Both flowing completion registrations expose Task readiness, not the enclosing invoker's return. Three ordinary driver parts do not count transitive producers. |
| `sources\TerraFX.Assets\AssetLeaseLifetime.cs:162-235`; `AssetPackageBacking.cs:167-246` | Issued final release now retains backing/Store state through failure and records successful backing release before accounting. The backing retains its stream during `Dispose`. This does not prove pre-lease rollback or ownership below FileStream/SafeFileHandle. |

The [owner-aware handback assessment](m03b-scene-preparation-owner-aware-async-handback.md), sections 3-5, supplies the pinned-runtime ordering counterexample: stock boxes clear generated state/context after publishing outcomes. The [finite-envelope addendum](m03b-scene-preparation-finite-envelope-and-return-addendum.md) identifies input-capacity-dependent native stack/keep-alive cloning during fault capture. Those are scoped prior source analyses, not fresh runtime observations or qualification of current binaries.

## What is finite, and what is not yet a profile

For described envelope length `d` and exact immutable limits `L`, retain the already derived successful-structure bounds:

```text
r = min(L.MaximumAggregateRecords, floor(d / 64))
a = min(L.MaximumArrayElements, L.MaximumAggregateArrayElements)
n = min(a, r)
R = min(r, 1_048_576)
q = min(descriptor.MaximumDecodedAssetBytes, descriptor.PackageByteLength)
```

`r` bounds record headers, `n` any record array, `R` successful unique scanned references, and `q` a same-backing None target's decoded length. Reader-specific ceilings tighten these bounds. They are **counts/lengths**, not bytes per object, complete work counts, simultaneous invocation counts or return acknowledgments.

Under `sources\TerraFX.Core`, `EbinStructuralReader.cs:23-39` calls the semantic action only after complete validation; `EbinCodec.cs:556-582,909-1004` excludes overlapping positive ranges and overlapping record-array children; `EbinScanState.cs:25-85` enforces aggregate counts/depth. Malformed structural input therefore cannot enter the semantic builders. Its own validation/diagnostic/I/O/fault paths still require provision. Semantic-invalid input can allocate a bounded prefix and then fail; catalogue that prefix and the failing attempt, not only successful results.

| Obligation | Decision and derivation required |
|---|---|
| CLR semantic graph | **Finite in principle for the closed reader/metadata set.** Enumerate each creating site in Scene/Prefab, entity/hierarchy, selector/reference/override/slot/literal readers and constructors. Derive multiplicity from actual bounded loops/nesting and copy length from the borrowed extent. Account whole carriers, spare capacities, old/new growth overlap, final copies and constructor failures. Freeze schema metadata or derive its independent checked bounds; factory codec identity does not size arbitrary retained metadata. |
| Decode/reference work | **Bounded semantic work is derivable, not yet derived completely.** Include repeated structural/semantic scans, schema walks, sort/comparison work and scanner growth. Successful short reads make positive byte progress, bounding high-level read-loop iterations, not native retries or latency; include the terminal fault/cancel branch. Automatic target checks are inspections, not recursive typed reads. Do not substitute directory count for distinct malformed reference tuples. |
| Pre-lease rollback | **Can be made an owned finite state machine.** Every acquired obligation must first enter the installed control. A finite list of progress states does not prove that native final close is nonthrowing or ownership-preserving. |
| Actual async producer/invoker return | **Not inferable at the current interface.** Identical Task outcomes admit both “invoker still accessing its funded frame/context” and “invoker returned” schedules. A closed binary identifies what to inspect; it creates no missing happens-before edge. |
| Complete `C_op`, J/K | **Not established.** Runtime pinning alone leaves external context behavior, fault-capture input capacities, cold state, native/runtime controls and final-return disposition open. A mathematically finite CLR/platform maximum exceeding every legal P would not admit a useful read. |

Use the existing checked `A(c,s) = 64 + RoundUp16(c*s)` for separately allocated arrays. Whole object/collection/runtime charges need independent target-specific derivations; `Unsafe.SizeOf<T>()` is not a class's retained graph size. For example, `A(d,1)` and even `R*A(q,1)` are partial payload-array terms only. The latter can avoid assuming target tails joined, but pays none of their omitted controls/results. No decoded-output measurement or average multiplier may select the pre-read grant.

J must cover the final whole root/shared controls and fixed producer machinery; K must protect body cleanup and public publication, including their overlap. A complete child `C_op` is a disjoint subdivision of the original producer allowance, not a second purse added to it. Separate fixed producer controls from spendable child workspace in that ledger. Neither a receipt nor today's scalar `TryClaimProducerWorkspace` establishes that partition. Preserve both binding-copy provision and independently funded activation; no released/remaining P is itself a profile.

## Required internal boundary

### One installed operation, before any lower acquisition

Implement the earlier proposed `AssetLoadOperation<T>` as an Assets progress owner, not a new preparation owner or public API. The only preparation anchor remains:

```text
original domain/use anchor -> PreparedScene -> one installed Assets child
```

1. Under the existing preparation gate, require Building/Running state and the empty child slot; elect construction and subdivide the complete original-P claim before its factory/call can allocate. Keep construction and close interests until installation or complete unwind. Construct outside gates. The factory creates an empty owner and acquires no backing, lease, decode credit or I/O.
2. Install that exact control in a dedicated fixed slot, separate from `_phaseOwner`/`_pendingOwner`; only then authorize its single start. Failed installation starts nothing. Assets receives the exact descriptor and source facade, never `ScenePreparationFunding`, a World capability, a Runtime sizing callback or a separately actionable use alias.
3. Move source-reference acquisition inside this boundary. Assets revalidates the descriptor and source admission at acquisition and records each acquired reference/credit in the installed owner before the next fallible step or async escape. Preserve existing lock order and usage/cancellation precedence. Stale Store-current descriptors refuse; an admitted old anchored backing does not rebind after replacement.
4. Keep one slot, no queue. Automatic reference inspection reuses it. The next root/Prefab operation cannot start while construction, registration, result installation, cleanup or actual-return interests remain. An execution fault with successful rollback drains only after that same return join. Uncertain registration or failed cleanup retains the slot and original claim; live consumers remain live, genuine stopped-but-unreleased failures retain debt.

### Split temporary provision from the retained result

The read's up-front requirement includes a conservative **retained-result upper partition**, alongside temporary workspace and protected local terminal work. After decoding, derive the retained charge from the admitted actual counts/capacities and final object catalogue, within that already funded upper bound. This is settlement, never retroactive funding.

The retained set includes the semantic value, component/literal/asset-byte backings, surviving collections and issued lease/state/lifetime/gates, plus any metadata/backing storage without independently established live provision. Existing Assets wire-byte counters do not pay these managed objects. Shared aliases are counted once under their actual surviving owner.

Stage the exact lease in the operation before exposing readiness. Install it into the existing planner destination under the parent's installation/close fence, then acknowledge that exact transfer once. Keep its exact claim in existing root/cache/plan ledger fields; if the private cache value layout changes to carry that ledger, include its new capacity/stride. No extra per-result preparation owner is needed.

Emptying the operation slot returns only ended temporary/unused provision. The retained partition stays charged through builder/cache/plan ownership. Later use the existing exact covered-workspace receiver transaction for the identified surviving graph and receiver control; do not transfer all `C_op`, an estimated residual, or refund-and-reacquire live storage. Result installation and invocation return are independent joins.

### Own the producer boundary, rather than observe its Task

Replace the selected private Task-returning load seam with an installed-progress protocol. Reuse `AssetEnvelopeDecoder.Decode`/`Inspect` and the real Scene/Prefab readers as synchronous semantic calls. Represent only the selected read/inspect/I/O suspension progress explicitly in that same Assets control; do not introduce a generic builder, task-like type, scheduler, worker service or another decoding algorithm.

The actual entry/resumption invoker must perform this sequence:

```text
prepaid invocation/registration interest
  -> ordinary semantic/I/O-progress step returns or unwinds
  -> owned context/restoration and outcome-publication scopes return
  -> step/awaiter/context/result aliases end or transfer
  -> registration, overlapping invocation and child-I/O interests join
  -> exact original-claim handback as the last owned operation
  -> independently proved no-retired-state-access/no-new-Scene-allocation suffix
```

This sequence is a required implementation contract, **not an assertion that such an invoker exists today**. Physical stack return is unnecessary only when the remaining suffix has that independent proof. Inspect the enclosing runtime/native caller too; placing a new Action around the existing async body merely moves the gap.

The lower `RandomAccess` completion must supply actual consumption end **and** a proved end/transfer of every invocation control charged here. Buffer readiness alone does not join its Task/ValueTask-source suffix. If that cannot be established for the selected stock implementation, change only the internal Assets I/O boundary to an actual-completion-owned read whose controls/backing remain attached through their real last access. Chapter 02 permits reviewed native async ownership; naming a new callback or claiming unprovided independent Store provision does not implement it. Do not begin a broad Scene cursor conversion until this real lower boundary is fulfilled.

Preserve initial isolation, nondefault/default/suppressed ExecutionContext behavior, AsyncLocal notification/reentrancy and lack of arbitrary caller SynchronizationContext capture. A non-flowing wake plus explicitly owned context invocation is only a candidate mechanism; a mechanical `UnsafeOnCompleted`/flow-suppression substitution is rejected. Pinning the CLR does not bound arbitrary user callback behavior. The existing external-observer provenance rider covers only its specified external registrations, not Scene-created context holders, restoration or fault capture. Any further ownership clarification needs explicit owner-contract review, not a free-runtime allowance.

Fault delivery must likewise be concrete: either derive and preclaim all new dispatch-state cloning from admitted inputs, or implement an ownership-preserving non-copying relay of already captured faults. Preserve original identities/order and the selected token. `GetResult` followed by `TrySetException` is not such a relay; same-result-type `TrySetFromTask` neither bridges the current heterogeneous Tasks automatically nor proves return. No supported capture/relay means no all-outcome Ready profile, not successful cancellation or replacement of the original fault.

The planner's own nested Task/ValueTask invocations and the root's completion Actions remain separate obligations. Once the lower seam works, replace their selected suspension plumbing with operation-specific progress in the existing builder/root, reusing synchronous semantic helpers, or provide their actual enclosing-invoker proof. Do not clear `_supplierProducersUnjoined` on lower read success. Unselected restore/measurement paths retain their actual original owners; null must not become unlimited funding.

## Pre-lease rollback proof

The installed child retains exact progress for backing references, each Store credit, payload/reference/value intermediates, lifetime/handle construction, selected outcome and bounded cleanup facts.

| Boundary reached | Sole authority and required disposition |
|---|---|
| Empty owner / refusal before lower acquisition | No lower obligation exists. Return only successfully acquired original construction provision, after its real consumers unwind. |
| Temporary anchored reference, admission not committed | Operation owns even this reference and zero typed-load credits. It may become the final backing reference after source/Store close. Refusal cannot discard it or assume nonfinal release. |
| Typed credits and payload/reference inspection admitted | Record reference, decoded/live-lease/decode credits before reading; keep active I/O consumers distinct from ready outcomes. Rejecting an incompatible target package codec occurs before that target's reservation/read, not before already-performed root work. Exact target schema/registered-codec membership remains a post-inspection check. |
| Lifetime or handle construction | Lifetime construction failure leaves responsibility in the operation. Attach a created lifetime before another fallible construction. Transfer each lower obligation to it exactly once; a cleanup `finally` cannot declare successful transfer. Use either lifetime release or operation rollback, never both. |
| Result installation or close | One installation interest fences both owners until the exact destination accepts the result/retained claim or the operation retains cleanup. A historical field assignment is not acknowledgment. |
| Cleanup and accounting | After consumers stop, attempt independently safe cleanup. For an operation-owned final reference, prevalidate accounting, release backing, record success, then commit associated credit returns. Preserve backing-success if later accounting fails. Never retry uncertain close, invent rollback, or refund still-live storage. |

The selected private read-only FileStream/SafeFileHandle close must be proved nonthrowing for valid reachable states, or replaced below Assets with an ownership-preserving acknowledgment. Retaining FileStream after a throw does not recover an already severed OS handle. Lack of a genuine failure stimulus is not a mandate to inject allocator failure, corrupt counters or repeat an unchanged blocked probe. This is conditional missing evidence, not an observed valid close failure.

**Distinguishing source-derived schedule:** a None Scene names a None Prefab. Its root read stages a lease while its producer still returns; body/Store close or an early observer cannot reuse the child slot or refund the retained value. After the exact root transfer and true return, the Prefab descriptor comes from that root's backing, even if the Store has replaced its current package. If anchored admission then refuses after taking a temporary reference, the installed child still owns that reference and its rollback, even when no lease was issued. A Brotli referenced target instead refuses before that target's payload work. These are required orderings, not executed tests.

## Phased implementation and evidence

| Phase | Bounded deliverable and exit evidence |
|---|---|
| Prove the lower invocation lane | Freeze the actual selected SDK/compiler, loaded CoreLib/CoreCLR/JIT, architecture, build configuration, code-generation/tiering/instrumentation modes and relevant native I/O implementation. Start with managed Windows x64 Debug and Release separately; `global.json` selects developer tooling, not the loaded runtime proof. Establish an actual end/transfer edge for the real None I/O, resumption/context and fault/publication closure. No guarded preparation execution or wholesale cursor rewrite to discover this prerequisite. |
| Implement acquisition/result ownership | Add the single slot, preconstruction election, empty Assets control, inside-owner acquisition, irreversible rollback progress, retained partition and exact installation acknowledgment. Keep the guard. Use the existing original-use and covered-receiver machinery; no second preparation root/use or independent Store pool. Final layouts, not proposed type names, become catalogue inputs. |
| Close numerical profiles | Catalogue every creating site, concrete layout/capacity, maximum multiplicity, coexistence, before-work issuer and actual end/transfer for `C_op` and final J/K. Include failed/malformed/cold/cancel/cleanup-publication paths and existing retained results while another read runs. Corroborate with allocation-complete component evidence, not sampled/warm averages or production output as the only expectation. Demonstrate that at least one real Scene plus Prefab fits a legal unchanged P; an always-refusing formula is not an executable lane. |
| Integrate the selected producer closure | Finish the existing planner/root's own invocation funding/return and retained receiver obligations without Rendering. Replace the supplier latch only with checked fulfilled joins. Enable `QualifiedFootprint` only for bytes/lanes whose complete entry, body, cleanup and public delivery obligations close; a lower read profile alone is insufficient. |
| Validate through actual owners | Use real factory registrations/packages and ordinary Assets/Runtime NUnit runs, Debug before Release, fresh nonzero selected/executed membership and TRX. Cover exact/short grant refusal before lower work, no queue/reuse before return, cumulative retained charges, stale-current versus old-anchored descriptors, malformed/reference/None rejection, cancellation and source/Store/World close, result-install/close and early-body-end races. Preserve the existing package-identity and retained-asset consumers; do not forge return acknowledgments or substitute a test-only owner/profile. |

Existing focused regressions include `AssetStoreDescribedReadRejectsBrotliReferenceBeforePayloadRead`, `AssetStoreDescribedReadsRejectReplacementAndRetainPinnedSource` and `ScenePackageLeaseDescribesOnlyFactoryRegisteredSceneAndPrefabReads`. Extend owning test files for new behavior rather than adding a qualification runner. `docs\project\Building.md` owns commands and artifacts. Managed success does not establish NativeAOT, other targets, real public renderer consumers or Chapter 35 milestone acceptance; dependency-ready M05 work remains independently eligible.

### Explicit NO-GO conditions

- Any selected code/metadata/runtime allocation lacks a finite before-work charge, or no real nonempty read fits legal P. Never use remaining P, a wire multiplier, historical constructor sizes or fixed J/K as optional scratch.
- Acquisition precedes operation installation; result/cleanup can escape its root; transferred result bytes are refunded with the slot; or cleanup needs a successor use, queue or retry.
- The alleged return is Task terminality, `GetResult`, source `finally`, another observer, an unsafe callback substitution or normal-success permanent quarantine. Every still-live suffix must end or transfer to a specifically identified, already funded real owner.
- Required context semantics or original fault/cancellation delivery cannot be preserved with bounded owned machinery. A changed runtime pin alone does not resolve those external-input obligations.
- Native final-release safety remains neither proved nor ownership-preserving; retained receiver or full planner/public-publisher obligations remain incomplete. Keep the affected lane blocked without claiming unrelated qualification failed.

The genuinely unresolved point is fulfilment of the **actual lower invoker/context/fault and native-release boundary**, not whether managed objects have finite layouts. No current-source proof or installed return/capture capability closes it. This is not a theorem that public .NET APIs can never implement a replacement; the proposed narrow owner/invoker restructuring must supply and prove the missing edge before this preparation can execute.

## Decision input freeze

Captured UTC `2026-11-18T20:39:19.8598410Z` at HEAD `39874518cdbe2680c11959da4d4dc9389adaa55e`. The following 64-file closed set is the input to this decision, not a transitive binary/runtime attestation. Earlier proposal freezes and observations are historical, not reaccepted. Dirty Chapters 04/16/30 are context, not accepted amendments. No schema/fixture bytes or versions are changed.

```text
.github\workflows\ci.yml
Directory.Build.props
Directory.Build.targets
global.json
sources\Directory.Build.props
docs\project\Building.md
docs\project\Roadmap.md
docs\design\02-dotnet-api-memory-and-ownership.md
docs\design\04-capacity-and-workload-profiles.md
docs\design\11-assets-import-streaming-and-packages.md
docs\design\16-scenes-transforms-and-cameras.md
docs\design\29-project-setup-build-and-deployment.md
docs\design\30-public-api-review-surface.md
docs\design\35-implementation-roadmap.md
docs\proposals\m03b-assets-prelease-operation-profile-capacity-addendum.md
docs\proposals\m03b-assets-prelease-recovery-owner.md
docs\proposals\m03b-scene-built-in-codec-profile-admission.md
docs\proposals\m03b-scene-none-profile-pre-read-and-owner-reconciliation.md
docs\proposals\m03b-scene-preparation-compiled-producer-evidence-addendum.md
docs\proposals\m03b-scene-preparation-finite-envelope-and-return-addendum.md
docs\proposals\m03b-scene-preparation-handoff-closure.md
docs\proposals\m03b-scene-preparation-owner-aware-async-handback.md
docs\proposals\m03b-scene-preparation-supplier-return-closure.md
docs\proposals\m03b-scene-producer-return-and-assets-release-rectification.md
sources\TerraFX.Assets\AssetCodecRegistrationReceipt.cs
sources\TerraFX.Assets\AssetEnvelopeDecoder.cs
sources\TerraFX.Assets\AssetLease.cs
sources\TerraFX.Assets\AssetLeaseLifetime.cs
sources\TerraFX.Assets\AssetOwnedReadDescriptor.cs
sources\TerraFX.Assets\AssetPackageBacking.cs
sources\TerraFX.Assets\AssetPackageLease.cs
sources\TerraFX.Assets\AssetPackageReader.cs
sources\TerraFX.Assets\AssetPayloadReader.cs
sources\TerraFX.Assets\AssetReferenceScanner.cs
sources\TerraFX.Assets\AssetStore.cs
sources\TerraFX.Assets\AssetStore.PackageLease.cs
sources\TerraFX.Assets\AssetStore.PackagePin.cs
sources\TerraFX.Assets\AssetStore.ReadDescriptor.cs
sources\TerraFX.Assets\TerraFX.Assets.csproj
sources\TerraFX.Core\EbinCodec.cs
sources\TerraFX.Core\EbinReadLimits.cs
sources\TerraFX.Core\EbinScanState.cs
sources\TerraFX.Core\EbinStructuralReader.cs
sources\TerraFX.Runtime\Entities\WorldAdmissionDomain.cs
sources\TerraFX.Runtime\Entities\WorldGuidUseControl.cs
sources\TerraFX.Runtime\Scenes\PreparedScene.cs
sources\TerraFX.Runtime\Scenes\PreparedScene.Preparation.cs
sources\TerraFX.Runtime\Scenes\SceneAsset.cs
sources\TerraFX.Runtime\Scenes\SceneAssetCodec.cs
sources\TerraFX.Runtime\Scenes\SceneAssetReader.cs
sources\TerraFX.Runtime\Scenes\SceneAuthoredComponentRecord.cs
sources\TerraFX.Runtime\Scenes\SceneEntityReader.cs
sources\TerraFX.Runtime\Scenes\SceneExpansionPlan.cs
sources\TerraFX.Runtime\Scenes\SceneHierarchyValidationWorkspace.cs
sources\TerraFX.Runtime\Scenes\SceneLoader.cs
sources\TerraFX.Runtime\Scenes\ScenePrefabAsset.cs
sources\TerraFX.Runtime\Scenes\ScenePrefabAssetCodec.cs
sources\TerraFX.Runtime\Scenes\ScenePrefabExpansionPlanner.cs
sources\TerraFX.Runtime\Scenes\SceneSchemaCatalogue.cs
sources\TerraFX.Runtime\Scenes\SceneSupport.cs
tests\TerraFX.Assets.Tests\AssetStoreTests.cs
tests\TerraFX.Runtime.Tests\Scenes\ScenePrefabPackageLeaseTests.cs
tests\TerraFX.Runtime.Tests\Scenes\SceneRestorePreparationGateTests.cs
tests\TerraFX.Runtime.Tests\Scenes\SceneSupportRegistrationTests.cs
```

Digest recipe: SHA-256 of UTF-8 without BOM/trailing newline, over ordinal-path-sorted `relative-Windows-path<TAB>lowercase-raw-file-SHA256` lines joined with LF. Exclude this new proposal from its own input set. Recheck HEAD, membership, dirty state and hashes before disposition; changed inputs require reassessment.

Input SHA-256: `4bad4a089426c6df0f833ea33b9a239c0a99b742606c19ebdb33d64c0ef6491b`. All 22 pre-existing dirty/untracked files were separately fingerprinted and remain unchanged. This document authors guidance only; no source/design/test edits, builds, tests, runtime experiments, commit or publication are part of this task.
