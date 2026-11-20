# M03-B Scene Assets closed-read allocation authority

**Status: proposed disposition, tentatively approved for continued implementation only. Not an accepted maintained contract, numerical `C_op`, executable Scene admission, or qualification. Keep `PreparedScene.QualifiedFootprint` null.**

## Decision and authority

Select a **closed compiled read profile for the ordinary Scene preparation path**, funded by disjoint subclaims of its original `PreparedScene` purse. Do not change the public codec interfaces or require arbitrary public Assets callers to adopt a workspace allocator. Extend the existing described-read/codec-receipt mechanism, not the separate bounded static-Mesh Store family.

This resolves the allocation-authority choice left by the [capacity addendum](m03b-assets-prelease-operation-profile-capacity-addendum.md). The [pre-lease ownership proposal](m03b-assets-prelease-recovery-owner.md) still owns the Assets progress controls and result-installation protocol. The [actual-invoker proposal](m03b-scene-actual-invoker-return-closure.md) owns the caller/return boundary; this proposal neither designs nor reviews another native worker.

Chapters [04](../design/04-capacity-and-workload-profiles.md#3-budget-composition-and-failure), [11](../design/11-assets-import-streaming-and-packages.md#3-package-loading-leases-and-update-behavior), and [16](../design/16-scenes-transforms-and-cameras.md#2-prefabs-and-streaming) own funding, Assets behavior, and Scene admission. Chapters [18](../design/18-shaders-and-material-pipeline.md), [20](../design/20-animation-and-deformation.md), and [21](../design/21-2d-and-3d-rendering.md) own the selected Rendering/Skeleton semantics; [30](../design/30-public-api-review-surface.md#6-assets-schema-codecs-and-packages) owns public compatibility. Ratifying the scoped refusal boundary belongs to those owners before activation, not to an internal receipt or this tentative disposition.

## Present-source evidence

Paths below are beneath `sources`; named methods identify the assessed boundaries.

| Evidence | Consequence |
|---|---|
| Runtime [`SceneSupport.RegisterAssetCodecs`](../../sources/TerraFX.Runtime/Scenes/SceneSupport.cs), planner `PrepareCoreAsync` / `GetPrefabAsync` | Ordinary preparation already selects private factory origins for Scene/Prefab **2.0**, with package codec `None`, through described reads. A same-type/schema custom registration does not satisfy those receipts. The older addendum's blanket absence of this restriction is no longer current. |
| Runtime [`Renderer.RegisterAssetCodecs`](../../sources/TerraFX.Runtime/Rendering/Renderer.CpuExtractionSupport.cs), [`AnimationSupport.RegisterAssetCodecs`](../../sources/TerraFX.Runtime/Animation/AnimationSupport.cs), [`RenderSceneAssetBundle.PrepareOwnedCoreAsync`](../../sources/TerraFX.Runtime/Rendering/RenderSceneAssetBundle.cs) and its helpers | Rendering/Skeleton factories still use ordinary `RegisterCodec`; dependency helpers call ordinary package-bound loads. The described-read restriction does not yet close this transitive family. |
| Assets [`AssetOwnedReadDescriptor.Matches`](../../sources/TerraFX.Assets/AssetOwnedReadDescriptor.cs), [`AssetCodecRegistrationReceipt`](../../sources/TerraFX.Assets/AssetCodecRegistrationReceipt.cs), [`AssetStore.AcquireLoad`](../../sources/TerraFX.Assets/AssetStore.cs) | Existing receipts bind exact codec instances/origins, backing token, Store, registration, limits, lengths and spatial context. They supply provenance and revalidation, **not an allocation grant**. |
| Assets [`AssetLease.LoadDescribedFromPackageAsync`](../../sources/TerraFX.Assets/AssetLease.cs), [`AssetPackageLease`](../../sources/TerraFX.Assets/AssetPackageLease.cs), [`AssetStore.ReadDescriptor`](../../sources/TerraFX.Assets/AssetStore.ReadDescriptor.cs), [`AssetStore.PackagePin`](../../sources/TerraFX.Assets/AssetStore.PackagePin.cs) | Temporary backing references and pin credits can precede a new issued handle. They still need a preinstalled, funded operation owner, including admission-refusal cleanup. |
| Assets [`LoadAssetAsync`](../../sources/TerraFX.Assets/AssetStore.PackageLease.cs), [`AssetReferenceScanner.Scan`](../../sources/TerraFX.Assets/AssetReferenceScanner.cs) | A decoded value/reference array survives while targets are inspected sequentially. Scan allocates a List, HashSet and immutable copy; returning the original decode-wire reservation does not end this graph. Inspection invokes no target codec body. |
| Assets [`AssetLeaseLifetime.ReleaseReference`](../../sources/TerraFX.Assets/AssetLeaseLifetime.cs), [`AssetPackageBacking.ReleaseReference`](../../sources/TerraFX.Assets/AssetPackageBacking.cs) | Issued final-release failure already retains the graph and irreversible progress. Store replacement/disposal return and successful-construction transfer ordering also exist. Preserve them; the missing work is pre-lease ownership/funding, not reimplementation of these fixes. |
| Runtime [`PreparedScene.CreatePreparation`](../../sources/TerraFX.Runtime/Scenes/PreparedScene.cs), [`PreparedScene.Preparation`](../../sources/TerraFX.Runtime/Scenes/PreparedScene.Preparation.cs), [project roadmap](../project/Roadmap.md) | The footprint is null; CPU resource preparation has an additional refusal. The supplier-return latch and full root/resource profile remain unclosed. No successful Scene consumer is established here. |

## Closed family and public compatibility

The selected **typed dispatch set** is exactly:

| Semantic root | Existing implementation | Exact Content tuple |
|---|---|---|
| Scene / Prefab | `SceneAssetCodec` / `ScenePrefabAssetCodec` | `native/scene` / `native/prefab`, each 2.0 |
| Mesh | `RenderMeshAssetCodec` | `native/mesh` 1.0 |
| Texture | `RenderTextureAssetCodec` | `native/texture` 1.0 |
| Atlas | `RenderAtlasAssetCodec` | `native/atlas` 1.0 |
| Material | `RenderMaterialAssetCodec` | `native/material` 1.0 |
| Static BindPose Skeleton dependency | `SkeletonAssetCodec` | `native/skeleton` 1.0 |

All selected payload reads, including automatic reference inspections, require package `None`; `native/envelope` remains 2.0. No typed Clip/controller, Mesh/Material 2.0, LOD or particle-emitter decoding, custom-codec invocation, opening, replacement, or restore/measurement operation is admitted by this set.

1. Extend private factory-origin receipts to the four Rendering roots and Skeleton. Only the owning built-in factories create those origins; schema identity, CLR type, an assembly name, or a user-supplied size declaration cannot substitute. Keep one registered tuple per CLR type.
2. Route **every selected dependency load** through receipt-checked operations: planner root/Prefab, bundle Atlas/Mesh/Material, Atlas pages, Mesh-to-Skeleton binding, and Material textures. Prepay their `Retain` handle construction too. Do not fall back to ordinary `LoadFromPackageAsync` when a receipt is missing.
3. Keep the public builder, arbitrary `RegisterCodec` implementations, ordinary `LoadAsync`/package-bound loads, and None/Brotli behavior unchanged. Public admission has no expected descriptor and does not apply its `RequiredPackageCodec`; adding private origins to the factories must preserve that distinction. Existing `AssetStoreLoadsProfiledRegistrationBrotliPayloadsWithoutChangingOrdinaryLoad` exercises it.
4. Extra unexecuted codecs are permitted in the registration. Envelope-only reference inspection checks the target's actual exact registered tuple without invoking its `Read`; it need not reject a custom target merely for having a custom codec. A later **typed** read requires membership in the seven-root set. Inspection's structural and registration-lookup work still belongs in the profile.
5. Bind sizing to the exact immutable schema registry and expand nested-schema and role-mask selections over its actual finite membership. Scene components can contain application metadata: [`SceneEntityReader`](../../sources/TerraFX.Runtime/Scenes/SceneEntityReader.cs) copies records and the reference validator walks descriptors; this is not permission to invoke application providers inside an asset read. Cover that generic metadata-only walk explicitly, including its failure paths. A tuple list alone does not close it. Registry construction and any profile tables need their own prior construction provision; do not build a new registry/cache during admission.

**Distinguishing example, not an executed observation:** a textured Mesh 1.0 uses a Material 1.0 referencing Texture 1.0. Changing only that Texture entry to Brotli preserves its decoded bytes and public typed-load behavior, but the selected Material-reference step refuses before reading that target. Replacing the Texture registration with a same-tuple user codec instead preserves ordinary codec dispatch but refuses its selected typed load before acquiring a temporary reference. Unrelated custom registrations do not cause either refusal. This is a scoped internal Scene capability restriction, not a universal Assets allocation promise.

## Bounds and the required `C_op` ledger

Use the existing checked accounting `A(n,s) = 64 + RoundUp16(n*s)` for each separately allocated array at **capacity**, not count. Whole carrier/layout charges include their fields once. These accounting rules are not measured CLR object sizes. Public Assets' wire-byte counters remain independent admission quotas: [Chapter 30](../design/30-public-api-review-surface.md#6-assets-schema-codecs-and-packages) explicitly excludes codec-owned managed allocation.

### Inputs available before acquisition

The profile binds:

- Operation kind, exact typed receipt, source facade and originating Store/backing token; immutable registration/schema membership, spatial profile/settings and exact `EbinReadLimits`.
- Checked entry stored/decoded lengths `d`; for None they are equal. A conservative reference-target bound `d_ref` is the maximum declared decoded length among that exact backing's directory entries that could be inspected. Assets may derive this scalar by a nonallocating directory scan under its existing source gate; using the backing's `MaximumDecodedAssetBytes` is a looser safe ceiling, not a new allowance. Revalidate the snapshot before acquisition.
- The actual Store CPU/decode/lease limits, plus original requested preparation `P`. Description reserves no headroom: apply the currently applicable Store policy again at credit admission. Store counters may refuse independently; they never fund controls.
- The selected compiled/runtime/architecture and actual read, context, fault and return lanes. SDK selection comes from `global.json`; it is not evidence of the runtime/native artifacts later used by qualification.

[`AssetPackageBacking.CreateReadLimits`](../../sources/TerraFX.Assets/AssetPackageBacking.cs) sets record bytes to `max(64, MaximumDecodedAssetBytes)`, fields/depth to 64, array and aggregate record/field/array-element limits to 1,048,576, and UTF-8 bytes to the decoded-asset limit. The directory has at most 131,072 entries. Use these **actual inputs**, not option defaults or the requested Scene expansion count, to bound decoding.

For a completely structurally validated envelope, record occurrences are bounded by `min(MaximumAggregateRecords, floor(d/64))`; each has a disjoint 64-byte header. Unique scanned references are no greater than those occurrences and the scanner's 1,048,576 limit. Fixed/record arrays additionally obey the limits actually checked for their wire kind. Bound raw byte copies by their validated extents, not by an unrelated element-count limit. Nested rescans reset structural budgets: count every actual invocation/copy and its coexistence, rather than treating one scan's limit as an aggregate allocation credit.

The semantic sizing functions must close these source-derived dimensions:

| Family | Required count/extent dependencies and allocation coverage |
|---|---|
| Scene/Prefab | [`SceneAssetReader`](../../sources/TerraFX.Runtime/Scenes/SceneAssetReader.cs), entity/hierarchy readers and validation workspace: authored entities (Scene ≤1,048,576; Prefab ≤65,536), placements/reference bindings ≤1,048,576, slots/Scene overrides ≤65,536, components/entity and required-components/slot ≤256, selector/property paths and every nested owner limit. Include all hierarchy workspace arrays, semantic carriers/builders, per-component copied bytes, literals/overrides and retained complete record bytes. Expansion options do not reduce these pre-expansion decoder bounds. |
| Mesh | [`RenderMeshAssetCodec`](../../sources/TerraFX.Runtime/Rendering/RenderMeshAssetCodec.cs): vertices ≤262,144, up to eight streams, indices ≤1,048,576, groups ≤65,536, Material slots ≤256; stream strides, four joint/weight lanes, and retained values. UInt16 input and widened UInt32 output coexist. Include `BigInteger` limb/workspace capacities from finite-binary32 `ToScaledInteger` and the complete predicate expression graph; “small arithmetic” is not a storage bound. |
| Texture | [`RenderTextureAssetCodec.ReadTexture`](../../sources/TerraFX.Runtime/Rendering/RenderTextureAssetCodec.cs): dimensions ≤4096 each, up to 13 exact mip levels, checked sum of RGBA8 mip byte extents, mip carriers/builders and pixel copies. The encoded envelope coexists with copied pixels. |
| Atlas / Material | [`RenderAtlasAssetCodec`](../../sources/TerraFX.Runtime/Rendering/RenderAtlasAssetCodec.cs): pages ≤16, frames ≤1,048,576 and their carriers/builders. [`RenderMaterialAssetCodec`](../../sources/TerraFX.Runtime/Rendering/RenderMaterialAssetCodec.cs): its exact Sprite/PBR parameter/binding variants and retained record bytes. Dependency helpers add at most one Sprite or four PBR texture slots, but their lease arrays/copies are separately funded helper storage. |
| Skeleton | [`AnimationAssetReader.ReadSkeleton`](../../sources/TerraFX.Runtime/Animation/AnimationAssetReader.cs): joints ≤256, joint builder/carriers, bind-model/depth arrays, actual Dictionary capacity, per-joint matrix-read scratch and retained record bytes. |

Before any numerical value is usable, enumerate **each allocation site** in those functions and their model constructors, Core scans, diagnostics and BCL dependencies: element stride, capacity function, invocation multiplicity, current/candidate/copy overlap, owner, and actual last-access boundary. Check the malformed-prefix/failure paths as well as successful outputs. Existing `ByteReservation` helpers are not this catalogue; for example, the hierarchy workspace still uses local eight-byte rounding.

A conservative selected-load envelope can deliberately retain all of the following allowance through the operation instead of assuming early local death:

```text
C_load = J_load + K_load + A(d,1)
       + ScanPeak(d, limits, schemas)
       + SemanticPeak(codec, d, limits, schemas)
       + InspectPeak(d_ref, limits, schemas)
       + IssuePeak(value type)
```

`ScanPeak` includes List/HashSet growth, old/new capacities and the resulting reference array. `SemanticPeak` includes the eventual semantic value and simultaneous validation/build/copy scratch. `InspectPeak` includes one target envelope array and its envelope-inspection/read storage. `IssuePeak` includes lifetime, its Lock, typed handle, LeaseState and gate, construction/installation overlap. Assign each allocation to exactly one term, including shared versus per-read invocation machinery; do not count J's controls again in `InspectPeak`. Summing phase peaks conservatively reserves their possible coexistence. It may refuse a request that a later proved phase-peak formula could admit; do not silently lower P or invent a codec multiplier to make it fit.

| Ledger interval | Funding/ending requirement |
|---|---|
| `J_load`: construction, installation, start and progress | Whole empty owner, gates, receipt/provision state and factory/invocation overlap paid before construction. Root-embedded slot fields belong to the whole root charge, not another scalar charge. |
| Payload / scan / typed read | Decoded array, context, scanner/result and all semantic allocations paid before their respective calls. Existing growable containers require the selected BCL's **capacity** and old-plus-new growth functions; alternatively pre-size a current container from validated counts without changing semantics. No unbounded pool-rent backing is assumed. None excludes the current Brotli rent/decoder, not I/O. |
| Automatic target inspection | One target at a time; original semantic value/reference result and any still-accessible parent envelope remain charged. Admit/release the target's exact Store decode credit separately. Advance only after the previous target's consumers/invokers actually return. |
| Result staging and installation | Keep value and every lifetime/handle construction prefix attached. Destination acceptance moves the same retained-data partition; it does not refund it or duplicate it. |
| `K_load`: cancellation, fault and cleanup | Protected worst coexistence of partial construction, original outcome, bounded original cleanup facts, diagnostics/strings, exception/EDI/Task-fault storage, and release/publication work. Include cold initialization and constructor unwind. Two exception references do not bound captures or native stack arrays. Do not allocate an exception list or recovery record during failure. |
| Actual return | `J_load` includes the selected Task/ValueTask/registration/context/dispatch/native suffix and enclosing publication/return interests. Outcome readiness, field clearing, or a callback-local `finally` cannot return this provision. Unknown runtime/fault terms make the profile unavailable. |

For package-pin construction, `C_pin` contains its own complete J/K, package handle/state/gate and acquisition/rollback/actual-return overlap, with **no** payload/semantic term. For standalone reference inspection, `C_inspect` contains its J/K, temporary-reference ownership, target envelope/structural inspection and return graph, with **no** typed semantic result or live-lease credit. These are operation kinds in the same slot, not three independently spendable pools. Their enumeration creates no new caller: use them only at an actual selected Scene boundary. Persistence pins/inspections need their own original Persistence provision, never Scene P.

Preexisting backing/registration/file/I/O-cache storage may be excluded only against an identified independent grant that survives every selected user. Today's wire-byte budgets do not establish that grant. Storage retained by a file-handle cache after read return needs that lifetime's provision, not an assumption that `C_load` ended it. Receipts do not isolate a backing from public reads: its shared provision must cover that coexistence, or the selected admission must refuse that backing without changing public Assets behavior. One Scene slot does not bound Store-wide/native concurrency. Until the separate producer/backing capability supplies this boundary, admission remains unavailable; this proposal does not supply a native-worker design.

## Original-P issuance, membership and transfer

Preserve `P = F + X + T`: free, active/protected and cumulatively transferred portions. The operation charge stays `256 + F + X`; a receiver transfer does not replenish P. All previously retained planner/helper/value claims coexist with the next operation's claim.

1. **Sole issuer:** the exact original `PreparedScene.PreparationControl`, through its Scene-owned funding adapter. Extend the current scalar [`TryClaimProducerWorkspace`](../../sources/TerraFX.Runtime/Scenes/PreparedScene.Preparation.cs) seam with an exact one-operation claim record: kind, generation, descriptor/receipt, complete amount, retained partition and progress. This subdivides the already preclaimed producer purse; it is not another domain debit or a successor use. Fixed producer/return and body/publication K remain unavailable to scratch.
2. **One slot per preparation:** a dedicated embedded child slot, independent of the planner/bundle pending-owner field. Root/Prefab loads, Rendering dependencies and any selected explicit inspection/pin serialize through it; automatic reference inspections are subphases of its load. No queue, waiting allocation, parallel page load, or slot derived from `MaximumLiveAssetLeases`. The slot remains occupied through result handoff, cleanup and actual enclosing return. This bounds logical operation membership, not returning suppliers: J must separately prove finite entry/registration/completion/reentrant-invocation membership and overlap. Generation exhaustion refuses before work. A failed-retained operation stops further admission in that preparation and retains its original claim. Across preparations, live and failed-retained slots remain covered by their actual independent-use admissions and original-P claims, not a new Store-wide pool.
3. **Construction/acceptance protocol:** under the preparation gate elect the empty slot and debit the complete claim before the empty Assets factory runs outside gates. Include metadata/profile/refusal work in the prepaid entry/operation envelope; a supposedly nonallocating preflight must not hide allocating diagnostics. Install the exact Assets-owned operation before its one start. The opaque acceptance identity is that factory-created owner plus its one-shot state, not a public byte amount. Keep its construction interest rooted through failed attachment/unwind.
4. **Closed issuer bridge:** only the Scene funding/installation path may authorize the selected Assets start after that debit. Bind its Assets-owned acceptance state to operation kind, receipt, exact source and provision; reject default, foreign, reused, undersized or mismatched acceptance. The funding protocol passes no Runtime root/object, World capability, funding callback or sizing delegate; existing registered owner-codec dispatch is unchanged. Its private factory/start seam and the caller's single-use claim must be reviewed together: Assets cannot independently infer a domain debit from a number. The closed call path enforces this provenance; assembly friendship is not a security or funding proof.
5. **Acquisition:** Assets revalidates the descriptor and source facade, then acquires/records temporary backing references and Store credits in the installed owner before the next fallible step/escape. A Store-current descriptor made stale by replacement refuses; an anchored old backing never retargets to current. No physical cleanup, callback, wait or publication runs under admission gates.
6. **Credits have different issuers:** the originating Store alone issues backing references, live-handle and declared-wire decode/live-value credits. The operation owns admission-refusal cleanup even when those amounts are zero. A typed load transfers those exact credits to its installed lifetime once; a pin transfers one lease credit and backing reference; inspection takes only its temporary reference/decode obligations. `Retain` adds a handle credit, not a second decoded-value charge.
7. **Successful handoff:** stage the exact result in the rooted planner/helper under its existing installation/close election, then acknowledge that result to Assets. The complete semantic value/handle partition remains within original X until released or transferred with its real retained Scene receiver. Include it in D where no real independent Assets allocation grant exists. Reuse the existing covered X-to-R receiver transaction; wire-byte counters do not justify subtracting it. Shared retained aliases need one physical value grant lasting through their actual final consumer and separately charged handle carriers.
8. **Failure/return:** preserve the existing issued-lifetime/backing state machines. Before a lifetime accepts ownership, the operation retains each acquisition/construction prefix and exact release disposition. Record successful lower release before accounting commit; do not replay uncertain close or release a credit twice. Return an unused claim only after factory/attachment unwind actually ends; return an admitted claim only after debt ended/transferred **and** the real enclosing invoker returned. A close request supplies neither fresh P nor new Store admission.

Missing profile/membership, an unsupported package codec, insufficient complete claim, occupied slot or failed attachment refuses **before that operation acquires a reference/credit, reads, decodes or constructs a lease**. Known target metadata is checked before target acquisition/read. Payload-only errors necessarily occur inside the already funded parent operation; do not pretend schema validity is known from the EIDX entry. Preserve established shape/owner/pre-cancellation ordering, error channels and original cancellation token. No fallback to an unprofiled load, partial success, silent W reduction, or successful-looking cleanup is allowed.

## Implementation and evidence sequence

| Stage | Bounded implementation and evidence; remaining gate |
|---|---|
| Close selected dispatch | Extend factory receipts and described-read restrictions through the named Rendering/Skeleton helper calls; preserve ordinary public registration/Brotli behavior. Freeze exact typed and metadata membership, implementations and compilation inputs. No new public API/schema/acceptance ID. |
| Close data/control catalogue | Derive the functions above, final empty-control/claim layouts, capacities and all construction/cleanup overlaps. Install the single original-P slot/issuer bridge and Assets progress before lower acquisitions. Keep candidates unreachable behind existing refusals while any J/K, backing or return term is unknown. |
| Prove scoped managed behavior | Extend existing Assets registration/store/package-lease and Runtime `SceneAssetStoreTests` fixtures with real package bytes: built-in versus same-tuple custom receipt selection; ordinary custom/Brotli compatibility; None-target rejection; exact/one-short funding and duplicate start; malformed envelope/reference rejection; pre-cancellation and cancellation at real operation boundaries; old anchored backing versus stale current descriptors; result-installation/close ordering and exactly-once reclamation. Lower-owner checks use real World-use admission, not a fabricated `PreparedScene` or a guard bypass. They are not positive Scene evidence. |
| Integrate actual ordinary Scene | First close the independent producer/context/fault/native-return, root J/K, planner/cache/helper and D/Q receiver admission evidence. Review the complete profiles on the exact revision before changing either footprint/resource-preparer refusal in a subsequent implementation batch; this proposal changes neither. Positive Scene qualification then requires actual public Scene/Prefab and static Mesh/Sprite/BindPose preparation, activation, camera and CPU extraction consumers. Preserve package-identity and pixel-17 expectations; opening an evidenced lane is not itself consumer evidence. |

Use ordinary owning-project NUnit/VSTest commands from [Building](../project/Building.md), Debug before Release, related selectors together, fresh nonzero discovery/execution and TRX results. Derive expected membership/bytes from reviewed literal fixtures and owner contracts, not production output alone. Validate allocation/capacity functions against the selected runtime's cold/hot behavior and generated code; samples corroborate the allocation-site proof, not replace it.

No valid deterministic constructor-allocation or physical-close failure stimulus is established here. Keep those observations explicitly unsupported until a real capability/stimulus exists; do not inject a fake native owner/allocator, corrupt private counts, repeat unchanged blocked probes, or interpret a repeated disposal latch as release evidence.

## Residual gates and M05 handoff

The allocation decision permits implementing the closed dispatch, original-P issuer and per-codec/Assets ledgers now. It does **not** close Task/context/registration return, bounded original-fault capture, actual native/read/cache lifetime, FileStream/SafeFileHandle ownership-preserving close, full root construction/publication, planner/cache or Rendering helper/receiver settlement. The current async chains and successful managed disposal returns do not establish those facts; permanent quarantine of ordinary successful calls cannot substitute for a proved return lane. The [producer/release rectification](m03b-scene-producer-return-and-assets-release-rectification.md) remains the owning higher-lifetime assessment, subject to the present-source corrections above.

Once implemented, this capability gives M05 a real allocation-authorized **CPU asset dependency seam** for funding the existing Atlas/Material helpers, Mesh/Skeleton binding, retained static resource bundle and their refusal/cleanup integration. CPU kernels, layout/shader work and source-adapter/package work can continue under their own existing prerequisites. It does not by itself authorize a positive diagnostic consumer or qualify a whole-graph byte ledger.

Under [Chapter 35's sequencing](../design/35-implementation-roadmap.md#m04m05-capability-and-observation-sequencing), World-bound M05 evidence still needs actual M03-B Scene/camera/CPU-extraction producers and a real committed/restored World passed to `Renderer.Extract`. No manufactured snapshot/producer or helper test substitutes. Native GPU observations additionally require the applicable real M04 device/residency/pipeline/graph/submission/target and completion/readback/presentation paths; existing Scene-dependent native pixels require requalification. Dynamic animation, LOD and particles retain their separate producer/registration contracts and are outside this typed family. Neither M03-B nor M05 acceptance follows from this proposal.

## Frozen proposal inputs

UTC `2026-11-20T00:27:19.7689042Z`; HEAD `f301e6b77d4e3442184c5a63ea63454e618e19ae`. The following is the bounded evidence set for this decision, **not** the complete future executable allocation/runtime closure. No schema or fixture bytes are changed. Cited schema sources are frozen in S; their hash does not certify expanded membership or qualification.

In the manifest, `[group directory]` prefixes following filenames. Hash each raw file; a group hash is SHA-256 of UTF-8 without BOM/trailing newline over ordinal-sorted `relative-Windows-path<TAB>lowercase-raw-SHA256` lines joined with LF. ALL is the union. Recheck HEAD, dirty bytes and hashes before disposition; changed inputs require reassessment.

```text
[C .]
.github\copilot-instructions.md
.github\workflows\ci.yml
Directory.Build.props
Directory.Build.targets
global.json
sources\Directory.Build.props
tests\Directory.Build.props
docs\project\Building.md
docs\project\Roadmap.md
docs\design\02-dotnet-api-memory-and-ownership.md
docs\design\04-capacity-and-workload-profiles.md
docs\design\10-binary-data-and-authoring-formats.md
docs\design\11-assets-import-streaming-and-packages.md
docs\design\16-scenes-transforms-and-cameras.md
docs\design\18-shaders-and-material-pipeline.md
docs\design\20-animation-and-deformation.md
docs\design\21-2d-and-3d-rendering.md
docs\design\29-project-setup-build-and-deployment.md
docs\design\30-public-api-review-surface.md
docs\design\35-implementation-roadmap.md
[A sources\TerraFX.Assets]
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
IBinarySchemaCodec.cs
IContextualBinarySchemaCodec.cs
TerraFX.Assets.csproj
[F sources\TerraFX.Core]
EbinReadLimits.cs
EbinStructuralReader.cs
[S .]
sources\TerraFX.Core\CompiledContentSchemaRegistry.cs
sources\TerraFX.Assets\NativeAssetSchemaCatalogue.cs
sources\TerraFX.Runtime\Scenes\SceneSchemaCatalogue.cs
sources\TerraFX.Graphics\RenderingSchemaCatalogue.cs
sources\TerraFX.Runtime\Animation\AnimationSchemaCatalogue.cs
[R sources\TerraFX.Runtime]
Entities\WorldAdmissionDomain.cs
Scenes\PreparedScene.cs
Scenes\PreparedScene.Preparation.cs
Scenes\SceneAssetCodec.cs
Scenes\SceneAssetReader.cs
Scenes\SceneAuthoredComponentRecord.cs
Scenes\SceneEntityReader.cs
Scenes\SceneEntityReferenceBindingValidator.cs
Scenes\SceneHierarchyReader.cs
Scenes\SceneHierarchyValidationWorkspace.cs
Scenes\SceneLoader.cs
Scenes\ScenePrefabAssetCodec.cs
Scenes\ScenePrefabExpansionPlanner.cs
Scenes\ScenePreparationFunding.cs
Scenes\SceneSupport.cs
[G sources\TerraFX.Runtime]
Animation\AnimationAssetReader.cs
Animation\AnimationSupport.cs
Animation\SkeletonAssetCodec.cs
Rendering\RenderAtlasAssetCodec.cs
Rendering\RenderAtlasPageLoader.cs
Rendering\Renderer.CosmeticParticleAssets.cs
Rendering\Renderer.CpuExtractionSupport.cs
Rendering\Renderer.M05AssetCodecs.cs
Rendering\Renderer.MeshLodAssets.cs
Rendering\RenderMaterialAssetCodec.cs
Rendering\RenderMaterialTextureLoader.cs
Rendering\RenderMeshAssetBinding.cs
Rendering\RenderMeshAssetCodec.cs
Rendering\RenderMeshAssetCodec.Shared.cs
Rendering\RenderSceneAssetBundle.cs
Rendering\RenderTextureAssetCodec.cs
Rendering\RenderTextureMip.cs
[T tests]
TerraFX.Assets.Tests\AssetPackageLeaseTests.cs
TerraFX.Assets.Tests\AssetStoreRegistrationTests.cs
TerraFX.Assets.Tests\AssetStoreTests.cs
TerraFX.Runtime.Tests\Scenes\PreparedSceneAssetRetentionTests.cs
TerraFX.Runtime.Tests\Scenes\RenderAtlasAssetCodecTests.cs
TerraFX.Runtime.Tests\Scenes\RenderMaterialAssetCodecTests.cs
TerraFX.Runtime.Tests\Scenes\RenderSpriteCapacityTests.cs
TerraFX.Runtime.Tests\Scenes\RenderSpriteComponentTests.cs
TerraFX.Runtime.Tests\Scenes\RendererExtractionTests.cs
TerraFX.Runtime.Tests\Scenes\SceneAssetStoreTests.cs
TerraFX.Runtime.Tests\Scenes\SceneAssetStoreTests.EngineHost.cs
[P docs\proposals]
m03b-assets-prelease-operation-profile-capacity-addendum.md
m03b-assets-prelease-recovery-owner.md
m03b-scene-actual-invoker-return-closure.md
m03b-scene-producer-return-and-assets-release-rectification.md
```

| Set | Files | SHA-256 |
|---|---:|---|
| C | 20 | `cdd68de860a45e8c8576ebae5e428cca6ff3299ae39ea866e501687cba8893b0` |
| A | 22 | `f72a7855f858d5396ecab6808f65abde8cf046cceec9a6030fcc927836a749d1` |
| F | 2 | `0883f1129bf4bafd26d17a84e25f0906aa50d917aee147088c2fd12ccc7e1fb4` |
| S | 5 | `0f96cb985c035e8840f8705ad8390e48cd5f7bbbb1ecff00803fca7cb6d419e8` |
| R | 15 | `5249622db8127cd326ce7542550694cda9bd464607d2b7fd66f18009891814c4` |
| G | 17 | `0f8ffeb9566bdd8c15c462634dc2845256f1103bf4eea9e189a9c6c7c9193c25` |
| T | 11 | `d4519404bc5a9ca696b3b7ec21f6950543e31ace7c4227f0dee838ee2dcb0fc2` |
| P | 4 | `ffce6bc9298cfefaae77b92914c7f7929b94a12d6261b01f60900b5d5c303302` |
| ALL | 96 | `488142b1f0cd6a6f03804bb535f81f049cac6884626941dbeff4b36afff9e3b2` |

| Schema/registry source in S | Raw SHA-256 |
|---|---|
| `TerraFX.Core\CompiledContentSchemaRegistry.cs` | `cfe040b8e51cb811aecccbb8b0bec0d2993b7a726a5b179968ce0db1415eae08` |
| `TerraFX.Assets\NativeAssetSchemaCatalogue.cs` | `6b9537ea8cefebb48b5dc24261a8c03fb059e3314e7da089f29a639d9e55620a` |
| `TerraFX.Runtime\Scenes\SceneSchemaCatalogue.cs` | `cf0253cf1c207644f0b6c76b0392977e02b0939ff355ed825f38d42705f2b32a` |
| `TerraFX.Graphics\RenderingSchemaCatalogue.cs` | `de25a8c0e249ff9ab5b592fe848c85429c004fe629694c64ef1afef3ba731614` |
| `TerraFX.Runtime\Animation\AnimationSchemaCatalogue.cs` | `c4447f84db8f60f21faf99c6ac9e8f67f13cf4de98f066c9feee8d07820c0daf` |

The five preexisting dirty files are preserved as supplied; T includes their bytes for preservation, not acceptance of unrelated changes.

| Dirty path beneath `tests\TerraFX.Runtime.Tests\Scenes` | SHA-256 |
|---|---|
| `PreparedSceneAssetRetentionTests.cs` | `e342237b7645be805dd924855b2239907773d31753d22b8b2261bf26c20452f4` |
| `RenderSpriteCapacityTests.cs` | `caa6610cf55f8fffa077745fd96baa45fcecfed2fd051cc763901aeed5500239` |
| `RenderSpriteComponentTests.cs` | `8e65b71b5ac1e0ef9d1f47aa56963054d091df8c229e74ddbb1cae37bb59ab60` |
| `RendererExtractionTests.cs` | `abaeea4d0f6be941af73e640b7fce41ec753d2199654343fa4668d54ec20fc6a` |
| `SceneAssetStoreTests.EngineHost.cs` | `4c0841a40295a891b68974fddd8078dfd0181c31b35ab0bcb0b5114fe0fd1039` |

Only this proposal is authored. No source/test/roadmap/existing-proposal edit, build/test execution, numerical allocation measurement, native observation, commit, push or PR is supplied.
