# M03-B Scene Assets allocation-site catalogue

**Status: tentative implementation disposition only. This authorizes compile/profile work, not Scene execution, a numerical original-P claim, or qualification. `PreparedScene.QualifiedFootprint` stays null until numerical funding and actual return/cleanup gates close.**

## Scope and current boundary

This is the source-site addendum to [closed-read allocation authority](m03b-scene-assets-closed-read-allocation-authority.md), against HEAD `89bf88208cd83e461fe04b450ef25a6ec8950429`. Earlier input hashes are superseded by the current freeze below. The five unrelated dirty tests were rehashed and match their previously recorded bytes.

The selected typed calls are Scene/Prefab 2.0, Mesh/Texture/Atlas/Material 1.0, and Skeleton 1.0, all package `None`. Current [Rendering origins](../../sources/TerraFX.Runtime/Rendering/RendererAssetCodecProfiles.cs), [Skeleton origin](../../sources/TerraFX.Runtime/Animation/AnimationSupport.cs), bundle roots and owned helper paths establish that dispatch selection; they do not supply allocation authority. Ordinary helper branches remain distinct through `RendererAssetReadMode`. The exact-array Scene/Prefab/Entity/Hierarchy/Mesh/Texture/Atlas/Skeleton decode changes are behavior-tested, but these tests do not supply allocation or Scene-return evidence.

Owners remain Chapters [04](../design/04-capacity-and-workload-profiles.md#3-budget-composition-and-failure), [11](../design/11-assets-import-streaming-and-packages.md#3-package-loading-leases-and-update-behavior), [16](../design/16-scenes-transforms-and-cameras.md#2-prefabs-and-streaming), [30](../design/30-public-api-review-surface.md#6-assets-schema-codecs-and-packages), and [35](../design/35-implementation-roadmap.md#m04m05-capability-and-observation-sequencing). This catalogue does not assess the bounded native-port implementation.

## 1. Functions and admissible inputs

Use checked `long` products, additions and sixteen-byte rounding:

```text
A_T(n) = 64 + RoundUp16(n * stride(T))
```

`stride(T)` is the selected managed array-element stride, not its wire width. Reference-element stride is the target pointer size; value-type strides and whole object charges require the actual compiled layout. `H(C)` below names one whole managed carrier, excluding separately allocated children. **No value for H is inferred from a class name, a domain cell charge, or another type's measurement.**

Notation for present code:

- `CI_T(n)`: a copy-producing immutable collection expression or `CreateRange` call. Qualify its lowering and temporary backing, or replace it with an explicitly owned exact array. Do not equate every spread with either zero copies or two copies.
- `CP(n)`: the explicit `ReadOnlySpan<byte>.ToArray()` site, with an `A_byte(n)` result.
- `G_container`: BCL capacity/growth/constructor, comparer and cold-state terms not yet assigned numerical values.

Inputs are the exact operation kind/receipt, Store/backing token, validated entry lengths, immutable limits and schema registry, spatial context, selected compiled/runtime/architecture, and independently funded shared backing. Describing reserves no Store credits. Revalidate at acquisition; quota headroom and the currently applicable replacement/admission policy may have changed.

Automatic-reference identities are not known before reading the parent. Pre-acquisition sizing therefore uses the maximum declared decoded length over the exact backing's directory (at most 131,072 entries), or its `MaximumDecodedAssetBytes` as a looser target bound. Derive that scalar without copying the directory or acquiring an unowned reference. Revalidate the backing snapshot; each target's None/credit checks still precede its own read. Do not size target inspection from the parent entry's d.

For a completely structurally admitted envelope of decoded length d and exact limits L:

```text
R(d,L) = min(L.MaximumAggregateRecords, floor(d / 64))
F(d,L) <= min(L.MaximumAggregateFields, floor(d / 32))
E(d,L) <= L.MaximumAggregateArrayElements
```

The record/field bounds follow from disjoint 64-byte headers and 32-byte field entries in structurally admitted ranges. E covers kinds 23/24, **not** arbitrary Bytes payloads. Each record-array count also obeys its field's limit, `L.MaximumArrayElements`, and representability checks. On malformed structural prefixes, use the scanner's checked depth/aggregate limits and failing attempt, not a fictional complete header per attempted record; those stack/error paths remain in J/K. UTF-8 validation does not construct strings. `AssetPackageBacking.CreateReadLimits` supplies actual limits; Scene expansion options do not lower the decoder's authored counts.

Let q be the count/extent vector for an **allocation-reaching prefix**, not merely a valid final value. Every count used by a site must have passed that site's preceding checks. Later checks cannot retroactively reduce its bound. For an unknown count, use the minimum of the preceding owner/structural limits and R where each element needs a record. Fixed-array count/stride and Bytes extents use their actual validated wire ranges. Across one admitted tree, sums of record extents are at most `L.MaximumDepth * d`: each byte belongs to at most that many containing records. Apply that ancestry argument per copy-producing site/set, not a guessed codec multiplier.

The body function for a root is the checked sum of the relevant rows below, maximized over these prefix vectors. A conservative implementation uses their source-derived count ceilings and mutually possible branch maxima; it need not enumerate all messages or implement a second decoder. Keep explicit source-copy sums when nesting makes a tighter disjoint-range argument unavailable. Overflow or inability to fund the complete upper bound refuses **before acquisition**. Reading an unfunded payload to discover a cheaper size is not an allowed escape.

Nested structural rescans each reset their scan state. Count their invocations/stack and diagnostic paths; do not multiply their aggregate limits into a new entitlement or charge the same borrowed input array again.

## 2. EBIN, envelope and reference sites

| Source site | Actual storage and capacity function |
|---|---|
| Assets [`AssetPayloadReader.ReadAsync`](../../sources/TerraFX.Assets/AssetPayloadReader.cs) | One `new byte[checked((int)DecodedLength)]`: `A_byte(d)`. None has no Brotli input rent, overflow byte or decoder. Its Task and lower read machinery are J, not zero. |
| Core [`EbinCodec.ScanRecord`, `ScanPayload`, `ScanRecordArray`](../../sources/TerraFX.Core/EbinCodec.cs), [`EbinScanState`](../../sources/TerraFX.Core/EbinScanState.cs) | Borrowed spans/value state, two inline eight-byte scratch values per `ScanRecord` frame, bounded recursive depth; scalar/UTF-8 validation has no explicit heap buffer. Fixed/record arrays are scanned, not materialized. Compiler lowering, native stack provision and cold dependencies remain separate J terms. |
| Core [`EbinStructuralReader.Scan`](../../sources/TerraFX.Core/EbinStructuralReader.cs), required-kind and canonical passes | Additional recursive borrowed-view passes, no `EbinRecord` tree. `EbinReadDiagnostic` is a **value type**, including its nullable form; do not charge a diagnostic class per scan. `EbinRecordReadView`/`EbinRecordView` are ref structs and their field/header projections are values. Invalid view access can still allocate exceptions. |
| Assets [`AssetEnvelopeDecoder.DecodeAction.Invoke`](../../sources/TerraFX.Assets/AssetEnvelopeDecoder.cs) | `H(AssetCodecContext)` from the per-load `new` in `LoadAssetAsync`, then scanner result, then the exact codec call. Envelope/action/schema-reference values are not new heap owners. Reference results coexist with semantic decoding. The context wrapper is not retained semantic output; do not transfer its claim to D merely because its schema/limits references survive there. |
| `AssetEnvelopeDecoder.Inspect` / `InspectEnvelope` | Envelope schema/ID checks and registry lookup only; **no** target typed reader, reference List/HashSet, context allocation, semantic value or payload copy beyond the read array. Thus successful inspection **data** is `A_byte(d_target)`; read/invocation/cold/fault storage is not included in that equality. |
| [`AssetReferenceScanner.Scan` / `AddReference`](../../sources/TerraFX.Assets/AssetReferenceScanner.cs) | `new List<AssetReference>()`, `new HashSet<AssetReference>()`, their backings, and `[.. references]`. The set deduplicates complete `(AssetId, SchemaId, SchemaMajor)` values in first-occurrence order. For u unique references, charge List capacity, HashSet bucket/entry capacities, old-plus-new growth peaks and the immutable result, plus comparer/cold state. Neither capacity equals u by declaration. |
| `CountAssetIds` / `CopyAssetReferences` | Existing non-container walks. Count/copy retain **occurrences**, unlike `Scan`'s unique result; blindly substituting them changes duplication behavior. Copy also performs a count pass. At canonical input, occurrence r and unique u satisfy `u <= r <= min(1,048,576, R(d,L))`. |
| Generic nested membership | Scanner follows exact registered Content tuples and role masks; component validation's `ResolveNestedSchema` can also select Foundation metadata. Expand the actual registry selection. No metadata lookup invokes an unexecuted codec body. |

The present scanner's `ScanPeak` is therefore **not numerical**: `G_List(u) + G_HashSet(u) + CI_AssetReference(u)`, with both containers' constructor/resize prefixes. Pinning only the application assemblies does not pin these capacities.

### Implemented allocation-free scanner primitive

`AssetReferenceScanner.ScanInto` and `CopyUniqueReferences` now provide the fixed-capacity scan as an internal primitive. `ScanInto` requires caller-owned `AssetReference[r]` and `int[r]` spans, counts then copies the existing visitor's occurrences, heap-sorts indices by the full `(AssetId, SchemaId, SchemaMajor)` tuple with source index as a tie-breaker, compacts the first index per tuple, then sorts the retained indices back into source order. This avoids the open-addressing proposal's quadratic collision worst case and gives O(r log r) work. `CopyUniqueReferences` copies into a caller-owned exact `AssetReference[u]` destination without another backing allocation. `GetFixedScanDataBytes(r,u)` implements the checked `A_AssetReference(r) + A_int(r) + A_AssetReference(u)` term from `Unsafe.SizeOf<T>()`, checked `long` products and sixteen-byte rounding; it rejects counts outside `0 <= u <= r <= 1,048,576`. `AssetStoreTests` covers the 0-, 1-, 2- and 3-occurrence cases, short exact-capacity refusals, record/workspace overlap, and each prohibited pairwise overlap for `CopyUniqueReferences`; the full Assets suite passes 69/69 in Debug and Release. These are helper precondition, ordering and one data-term observation, not selection by an installed owner or a complete operation partition.

The data peak for a selected operation is the checked, source-derived:

```text
ScanFixed(r,u) = A_AssetReference(r) + A_int(r) + A_AssetReference(u)
```

The scanner is currently exercised only by its regression test; it is not selected by `AssetEnvelopeDecoder`, has no installed scratch owner, and does not establish `C_op`, a Scene footprint, or actual-return evidence. Keep ordinary `Scan` unchanged. Do not infer funding from `RequiredPackageCodec == None` or `RendererAssetReadMode`; selection still requires the accepted closed-operation path. Reserve and attach all three exact arrays before allocation, and retain the scratch arrays through the actual synchronous visitor and copy return. Preserve immutable ownership after transfer; no writable alias may survive publication.

## 3. Scene/Prefab semantic sites

Read-site links: [`SceneAssetReader`](../../sources/TerraFX.Runtime/Scenes/SceneAssetReader.cs), [`SceneEntityReader`](../../sources/TerraFX.Runtime/Scenes/SceneEntityReader.cs), [`SceneHierarchyReader`](../../sources/TerraFX.Runtime/Scenes/SceneHierarchyReader.cs). Every array/copy term below is an actual site, not a per-entity heuristic.

| Site / validated dimensions | Body term, including nested construction |
|---|---|
| `SceneAssetReader.Read` / `ReadPrefab` | `H(SceneAsset or ScenePrefabAsset) + CI_byte(rootRecordLength)`; exact owned arrays `A_SceneEntity(e)`, `A_ScenePrefabPlacement(p)`, `A_SceneEntityReferenceBinding(b)`, `A_SceneExternalSlot(s)`, plus Scene-only `A_ScenePrefabOverride(o)`. Scene e ≤1,048,576; Prefab e ≤65,536; p/b ≤1,048,576; s/o ≤65,536, all further bounded by L/ranges. Add each child row once per occurrence. Root constructors adopt these arrays; they do not clone them. Nested component and hierarchy rows remain separate. |
| `SceneEntityReader.Read` | Per entity `H(SceneEntity) + A_SceneAuthoredComponentRecord(c)` with c ≤256. Per component `H(SceneAuthoredComponentRecord) + CP(componentLength)`. Local-transform/root-position validation projects values; it does not cook another record here. |
| `SceneHierarchyReader.ReadHierarchy` / `ReadNode` | `H(SceneHierarchy) + A_SceneHierarchyNode(hn) + sum H(SceneHierarchyNode_i) + A_SceneHierarchyEntry(hr) + sum A_SceneHierarchyEntry(children_i)`. Nodes are class instances; entries are **struct** elements, not heap objects per `new`. Counts use L and actual ranges. Do not assume hn = e before the later validator checks it. |
| [`SceneHierarchyValidationWorkspace`](../../sources/TerraFX.Runtime/Scenes/SceneHierarchyValidationWorkspace.cs) constructor | `H(workspace) + A_Guid(e) + A_SceneHierarchyPlacement(p) + A_byte(e) + A_byte(p) + A_int(e) + A_byte(e) + A_ushort(e) + A_int(e)`. All eight arrays coexist with the semantic tree. Existing `64 + ArrayBytes(...)` with eight-byte rounding is not this profile. |
| `ReadEntitySelector` | `A_Guid(pathLength)`, path ≤32. Selector is a struct. The path is allocated **before** its Scope enum rejection. |
| `ReadEntityReferenceBinding` / `ReadEntityReferenceBindings` | Outer exact array above; per binding `H(SceneEntityReferenceBinding)`, source-selector path, target row, and exact path `A_ScenePropertyPathSegment(l)` with l in 1..64. Reader-private owned construction adopts the immutable path without another copy. |
| `ReadExternalSlot` / `ReadExternalSlots` | Outer exact array above; per slot `H(SceneExternalSlot) + A_ContentSchemaReference(c)`, c ≤256. Reader-private owned construction adopts RequiredComponents without another copy. |
| `ReadPrefabPlacement` | `H(ScenePrefabPlacement)`; exact arrays `A_ScenePrefabRootPose(rp)`, `A_ScenePrefabOverride(o)` and `A_ScenePlacementSlotBinding(sb)`, each count ≤65,536; all nested rows; one `CP(placementLength)` via `record.EncodedBytes.ToArray()` followed by immutable adoption. The placement constructor adopts its arrays and encoded record. |
| `ReadPrefabRootPose` | `H(ScenePrefabRootPose)`; selector path; `H(SceneAuthoredComponentRecord) + CP(localTransformLength)` from `ReadLocalTransformRecord`; optional `CI_byte(24)` for RootPosition. No synthesized RootPosition record is constructed during this read. |
| `ReadPrefabOverride` / `ReadPrefabOverrides` | Exact outer array above; per override `H(ScenePrefabOverride)`; target-selector path; exact `A_ScenePropertyPathSegment(l)`; optional `H(SceneAuthoredRecord) + CI_byte(literalLength)`; optional reference-target row. Value/reference operands are materialized **before** the final Set/Omit consistency rejection: an invalid Omit still needs those allocations. Fixed-literal schema membership is borrowed metadata. |
| `ReadPlacementSlotBinding(s)` | Exact array in placement row; per item `H(ScenePlacementSlotBinding)` plus optional target row. |
| `ReadReferenceTarget` | `H(SceneReferenceTarget)` plus selector path only for Internal. ExternalSlot uses an inline nullable Guid, not another owner. |
| `ReadPropertyPath` / schema references | Exact segment array above; segments and `ContentSchemaReference` are values, not per-element classes. |
| [`SceneEntityReferenceBindingValidator`](../../sources/TerraFX.Runtime/Scenes/SceneEntityReferenceBindingValidator.cs), [`SceneHierarchyValidator`](../../sources/TerraFX.Runtime/Scenes/SceneHierarchyValidator.cs) | Component walk has `stackalloc ScenePropertyPathSegment[65]`, borrowed metadata and bounded recursion. Hierarchy walk reuses workspace arrays; its two static comparer objects and `Span.Sort` helper/cold/runtime stack belong in J/shared initialization, not an imaginary zero-cost sort guarantee. |

Constructor-copy evidence: [`SceneEntityReferenceBinding`](../../sources/TerraFX.Runtime/Scenes/SceneEntityReferenceBinding.cs), [`SceneExternalSlot`](../../sources/TerraFX.Runtime/Scenes/SceneExternalSlot.cs), [`ScenePrefabOverride`](../../sources/TerraFX.Runtime/Scenes/ScenePrefabOverride.cs), [`SceneAuthoredComponentRecord`](../../sources/TerraFX.Runtime/Scenes/SceneAuthoredComponentRecord.cs), [`SceneAuthoredRecord`](../../sources/TerraFX.Runtime/Scenes/SceneAuthoredRecord.cs).

For example, the Scene body function is the sum of its root row, one hierarchy row, one workspace row, and the entity/placement/binding/slot/override rows recursively selected by q. Shared child objects are charged once; root, placement, component and literal byte copies are **different backings**, even where they encode overlapping source bytes. Retained D includes the final graph, not reader builders, discarded copies or validation workspace.

**Implemented:** Scene/Prefab root outputs, authored-component arrays, hierarchy arrays, placement bytes, selectors, binding paths, RequiredComponents, placement subarrays, and override paths now use exact owned arrays and reader-private immutable adoption where needed. Per-element class carriers, copied record payloads, the hierarchy validation workspace, and the original-P operation owner remain separate rows. Keep existing copying construction for non-reader callers and preserve immutable aliasing.

## 4. Rendering and Skeleton semantic sites

### Mesh 1.0

[`RenderMeshAssetCodec`](../../sources/TerraFX.Runtime/Rendering/RenderMeshAssetCodec.cs) has:

| Site | Function over checked source counts |
|---|---|
| `ReadVector2/3/4Array`, fixed UInt16/UInt32 arrays | One exact `A_T(n)` per call; helpers validate count, stride and `32 + n * wireStride` before allocating. Stream n is v ≤262,144, except joint UInt16 count = `4*v`. Position/normal use Vector3; tangent/color/weights Vector4; UV0/UV1 Vector2. Up to eight stream records are processed. |
| `ReadIndices` | UInt32: exact `A_uint(i)`. UInt16: exact `A_ushort(i) + A_uint(i)`; the widened output and original array coexist. i ≤1,048,576, checked divisibility and vertex membership afterward. |
| `ReadMaterialSlots` / `ReadGroups` | Exact `A_AssetId(m)` with m ≤256; exact `A_RenderMeshGroup(g)` with g ≤65,536. Group and AssetId elements are values. |
| `ReadMesh` result | `H(RenderMeshAsset) + CI_byte(recordLength)` plus the final stream/index/group/slot backings. [`RenderMeshAsset`](../../sources/TerraFX.Runtime/Rendering/RenderMeshAsset.cs) adopts them. Its `Enum.IsDefined(indexFormat)` adds an Enum/runtime cold-path obligation, not a semantic output array. |
| Post-decode semantic rank check | Each stream is decoded/assigned **before** uniqueness/order rejection. A second Position can allocate another complete exact Position array before failure. The prefix function must allow that candidate in addition to still-accessible prior storage; a valid-only “one stream per semantic” sum is insufficient. |

A direct regression witness is a structurally valid stream array containing two finite Position streams with vertex count one: rejection occurs only after the second stream read. This is a source schedule, not an allocation measurement or a valid final Mesh.

Exact-array ownership/adoption is implemented for the listed outputs. UInt16 widening still has the original `ushort[]` and a separate `uint[]`; direct widening would be a further change and must preserve wire validation and failure order. Keep the current semantic-rank validation order and fund the duplicate-stream failure prefix. Do not remove exact triangle/weight/bounds predicates.

#### Fixed-limb exact dyadic predicates

The exact Mesh predicates still have these finite input bit domains; they size the fixed-limb arithmetic independently of its implementation:

| Expression | Derived magnitude-bit ceiling and invocation membership |
|---|---|
| Finite binary32 scaled by `2^149` | At most 24 significand bits plus shift 253 = **277** bits; this follows from raw finite exponent ≤254. |
| Difference of two scaled values; triangle products/cross differences | Difference ≤278 bits; product ≤556; cross difference ≤557. Each triangle converts twelve binary32 coordinate operands, forms six deltas, six products and three cross differences; triangles = i/3. |
| Bounds | Radius square ≤554 bits. Three squared deltas are ≤556 bits each and their sum ≤558. Eight corners, six scaled coordinate/center calls per corner, plus one radius call. |
| Weights already checked in [0,1] | Each scaled operand ≤150 bits; four-way sum ≤152; `abs(sum-one)` ≤151; multiplying by 100,000 needs at most 168 bits. Four component conversions and one exact-one initialization per vertex with a weight stream. |

The source now replaces those `BigInteger` operators with private fixed-limb signed-magnitude arithmetic in `RenderMeshAssetCodec`. Every finite binary32 is mapped to its exact integer in units of `2^-149`; checked workspaces use nine 64-bit limbs (576 bits), enough for the 558-bit sum-of-squares ceiling. The implementation uses bounded stack storage: 11 limbs-vectors per triangle pass, 9 for bounds validation, and 4 for each weight-sum check. No BCL limb array, rent or operator result is retained by these predicates.

The fixed-limb boundaries are tested with a finite extreme/subnormal triangle, a sphere boundary whose exact result differs by a subnormal term, and literal binary32 weight values immediately inside and outside the exact `1/100000` tolerance. These checks preserve the prior exact predicates; no float tolerance or arbitrary limb cap is substituted. The stack workspace and checked-overflow invariant remain part of the selected target's native-stack/J review, and impossible-overflow or nonfinite-input exceptions still belong to failure analysis. This closes the Mesh BCL arithmetic subproblem, not operation funding, `C_op`, or actual return.

### Texture, Atlas and Material 1.0

| Source site | Complete body-data expression |
|---|---|
| [`RenderTextureAssetCodec.ReadTexture`](../../sources/TerraFX.Runtime/Rendering/RenderTextureAssetCodec.cs) | `H(RenderTextureAsset) + A_RenderTextureMip(m) + sum CI_byte(4*w_l*h_l)`. Width/height are in 1..4096; expected m is one or the exact full chain, at most 13, checked **before** the exact array. Each mip validates its exact expected dimensions/payload before copying. Mip is a struct; the model adopts mips and stores **no complete encoded-record copy**. All copied pixels coexist with the input envelope. |
| [`RenderAtlasAssetCodec.ReadAtlas`](../../sources/TerraFX.Runtime/Rendering/RenderAtlasAssetCodec.cs) | `H(RenderAtlasAsset) + A_AssetId(pages) + A_RenderAtlasFrame(frames)`; pages 1..16, frames 1..1,048,576. The model adopts both exact arrays, no complete encoded-record copy. Frames are structs. |
| [`RenderMaterialAssetCodec.ReadMaterial`](../../sources/TerraFX.Runtime/Rendering/RenderMaterialAssetCodec.cs) | `H(RenderMaterialAsset) + CI_byte(recordLength)`. ParameterValues, TextureBinding, Sampler, nullable bindings and `RenderMaterialAssetData` are inline structs. Sprite has at most one optional binding; PBR at most four. Scalar/nested record projection adds no semantic heap container. |

These are read paths. Their `CreateRecord`/Write/record-array writer graphs are not invoked by an asset load and are excluded. Texture page/material-binding helpers' retained lease arrays, Atlas validation tree/priority queue and bundle indexes are **caller/helper** storage, not allocations of these typed codecs; their separate original-P claims must remain live.

### Skeleton 1.0

[`SkeletonAssetCodec.Read`](../../sources/TerraFX.Runtime/Animation/SkeletonAssetCodec.cs) invokes [`AnimationAssetReader.ReadSkeleton`](../../sources/TerraFX.Runtime/Animation/AnimationAssetReader.cs):

```text
SkeletonBody(j,q) =
    H(SkeletonAsset) + A_SkeletonJoint(j) + CI_byte(recordLength)
  + A_Matrix4x4(j) + A_int(j)
```

j is validated in 1..256 before these allocations. `SkeletonJoint` is a struct. Joint output uses an exact array; duplicate-ID and RootMotionJoint lookup use bounded scans of the filled joint prefix/final array, at most 256 joints. `ReadMatrix4x4` uses four fully assigned Vector4 locals, so it has no per-joint heap scratch array. Preserve original joint order and duplicate/parent/depth validation. The separate bind-model and hierarchy-depth arrays coexist with the joint array.

## 5. Issue, retention, inspection and shared-graph exclusions

Source: [`AssetStore.CreateLease`](../../sources/TerraFX.Assets/AssetStore.cs), [`AssetLease`](../../sources/TerraFX.Assets/AssetLease.cs), [`AssetLeaseLifetime`](../../sources/TerraFX.Assets/AssetLeaseLifetime.cs), [`AssetPackageLease`](../../sources/TerraFX.Assets/AssetPackageLease.cs), [`AssetStore.PackagePin`](../../sources/TerraFX.Assets/AssetStore.PackagePin.cs).

| Operation/site | Source-fixed carrier membership; bytes still require layouts |
|---|---|
| New typed issue | `H(AssetLeaseLifetime) + H(System.Threading.Lock) + H(AssetLease<T>) + H(AssetLease<T>.LeaseState) + H(object gate)`. All seven T are reference-type semantic roots. The reference stored in LeaseState is not another semantic value. |
| `Retain` | New handle + new LeaseState + its object gate; shared existing lifetime/value/backing. No new lifetime, decoded array, or second decoded-value wire charge. Its source construction rollback and actual caller return still need protection. |
| Package pin | `H(AssetPackageLease) + H(its LeaseState) + H(object gate)`, one backing reference and live-lease credit; no payload/context/semantic value/decode credit. `AcquirePackageLease` constructs this complete carrier inside Store admission before acquiring the backing reference or incrementing lease accounting, so no allocating construction follows the ownership commit. |
| Explicit inspection | Temporary backing reference, `A_byte(d_target)`, structural/lookup value state, read/return and failure provision. No issued lease or live-lease credit. |
| Description / provenance getters | `AssetOwnedReadDescriptor` and its codec receipt are structs, not issued handles; successful description borrows existing metadata. Provenance getters temporarily acquire/release a backing reference. Their enclosing issued-handle gate preserves its real lifetime reference, so prove that nonfinal-release closure rather than charging every getter as a potentially final file close. Allocating refusal still belongs to the entry/fault provision. |
| Successful issue versus failed construction | Lifetime is constructed before the handle/state; retain each prefix in the preinstalled operation. `ownershipTransferred` becomes true only after successful handle construction. Do not return backing/credits when a failed issued/lifetime release retained them. |
| Gates and release | Object/Lock carriers above do not bound contended Monitor/Lock runtime storage. Current release uses Monitor.Wait/PulseAll and failure rethrow uses `ExceptionDispatchInfo.Capture`. These remain runtime/fault terms, even though valid accounting commits allocate no explicit arrays. |

`C_pin` is its defined empty-operation J/K plus the pin row and backing/rollback/actual-return overlap. `C_inspect` is its J/K plus the explicit-inspection row. `C_load` uses the listed source data/issue rows, not their product with `MaximumLiveAssetLeases`. Pin/standalone-inspection existence does not introduce a Scene caller or fund Persistence calls.

The Mesh binding helper performs `Retain` **before** checking whether a Skeleton is present. A Mesh with no Skeleton therefore still needs the handle/state/gate construction and helper-return allowance, though no Skeleton typed read occurs.

Registration dictionaries/builders, compiled schema/profile arrays, canonical schema/static identity construction, file/Store/directory backing and backing-lifetime I/O caches are **not recreated per read**. Do not charge a full public registration build per `SemanticPeak`. Conversely, exclusion is valid only with an identified independent provision that owns its actual retention through these operations.

In particular, a General registration can retain **arbitrary user codec object graphs even when they are never invoked**. Receipt selection proves dispatch closure, not the size of that retained graph. Do not follow those user fields speculatively or call them free. A qualified selected Store must supply lifetime funding/provenance for its retained registration/backing closure; otherwise it is ineligible for qualified Scene admission. If opaque General registrations prevent that proof, the minimal opt-in boundary is a preconstructed, separately preclaimed registration/backing composition with a closed retained codec set, not changing or copying arbitrary public codecs during a load. Ordinary public Assets and existing receipt-selection helpers remain usable; they do not acquire a whole-graph qualification flag by passing selection tests.

The lookup portions of [`CompiledContentSchemaRegistry`](../../sources/TerraFX.Core/CompiledContentSchemaRegistry.cs) borrow its existing immutable arrays. The scanner/component validator's metadata-only nested selection does not execute `ScenePrefabOverrideApplier.Apply`/rewrite, persistence, writers or component providers. Those graphs are not semantic-read charges. Exact schema/role membership is nevertheless a required profile input, not a schema-count heuristic.

## 6. J/K and actual-return membership: what is still undefined

| Boundary/site | Membership that a numerical profile must close |
|---|---|
| Proposed Assets operation controls / original-P claim slot | Their final layouts are not implemented. No numerical J follows from the proposed type names or the 128-byte domain interest cell. Fix fields, gates, retained inputs, progress/fault facts and installation/return states before measuring whole carriers. |
| Existing `PreparedScene` construction | Root + its Lock; TCS/owned Task; `Exception[2]`; two Action delegates; source entry/plan/resource/publication state. See [`PreparedScene.cs`](../../sources/TerraFX.Runtime/Scenes/PreparedScene.cs). Compiler-generated captures from `SceneLoader.StartPlanPreparation` and planner/helper invocations are additional. Root-embedded fields are charged once in the root, never again per subclaim. |
| Selected typed load chain | One described Store wrapper invocation, `LoadAssetAsync`, one initial `AssetPayloadReader.ReadAsync` and `ReadExactlyAtAsync`, then `ValidateAssetReferencesAsync`; each of u references can invoke another payload/read-exactly pair. Backing's forwarding method itself is not async. There is no source-level Task object for an ordinary forwarding call, but an async invocation is not proved allocation-free when it completes synchronously. |
| Explicit inspection chain | Package facade `MatchesAssetReferenceCoreAsync` → Store `InspectAssetReferenceAsync` → payload Task → read-exactly ValueTask/native read, plus their cleanup finally paths. Pin is synchronous but may reach final backing release during rollback. |
| Short reads | For a positive destination extent d, a successful positive read advances at least one byte; zero/throw terminates. Thus issued lower-read invocations are at most d, or `d + sum d_target` across a load's envelopes. This bounds source invocation opportunities, **not** callback/control bytes or actual-return overlap. No per-64-KiB None chunk count may be substituted. |
| Caller/helper suffix | Bundle/planner/Atlas/Material/Skeleton async callers continue after outcome visibility. Their producer/context/publication interests do not disappear into an Assets Task. One logical Assets slot does not imply one returning compiler box, dispatch interest or continuation. The existing supplier-unjoined latch remains a refusal. |
| Cold/runtime state | Selected reader/static identity initialization, generic builders/empty arrays/comparers, hierarchy sort helpers, Enum.IsDefined metadata, Task/ValueTask and lock contention machinery. Assign them to a separately funded, actually completed initialization owner or to J; “warm it once” is not a funding receipt. |
| Fault/cancellation | Codec catches construct `BinarySchemaDiagnostic` (a class, unlike EbinReadDiagnostic) with empty path; its constructor copies Path. Store/view errors construct conventional exceptions and interpolated messages; enum/type/schema/numeric formatting can involve BCL caches/pools/culture. Cancellation constructs/publishes original-token outcomes. Include failed-constructor/unwind and primary-plus-cleanup coexistence. |
| EDI / Task fault / native/context return | Existing failure state preserves original graph, but `Capture`, Task exception holders, native trace storage and enclosing caller/context/dispatch suffix have no source-derived byte ceiling here. Two exception slots are not a capture bound. Do not replace required original exceptions with small success-shaped/status fallbacks or permanently quarantine ordinary successful calls. |

The first five data sections make body allocation sites finite and explicit. They do **not** make K numerical: unexpected runtime/I/O exceptions and caller/context/native capture are not functions of decoded length. Limit/shape failures should retain existing diagnostics and ordering; an internal bounded fact representation could avoid particular formatting sites only through an expressly reviewed error boundary. It is not a global exception-envelope solution.

For the catalogue's phase ledger, require all of:

```text
load body peak =
    max(read-parent,
        parent-buffer + references + typed-read prefix,
        still-accessible parent + semantic-value + references + target-inspection,
        semantic-value + references + issue/installation prefix,
        each cleanup prefix)
```

The conservative sum of those **disjoint backing terms** is also valid. Add the still-unqualified J/K once at their actual owners. Clear/transfer a term only on proved last access; preserve the same retained-data partition in original X or the covered Scene receiver. Metadata/source quotas and Store lease/decode counters cannot substitute for physical data/control provision.

## 7. Remaining implementation and acceptance

1. **Finish exact data-site closure, without invoking Scene preparation.** The fixed-buffer scanner, its checked `GetFixedScanDataBytes` term, placement-byte adoption, reader-private path/slot adoption, exact-array Scene/Prefab/Entity/Hierarchy reads, Mesh/Texture/Atlas outputs, and Skeleton joint/scratch reductions are implemented. Complete the remaining checked per-kind data functions and explicit current/candidate/retained partitions from the actual source sites. Do not select the scanner in `AssetEnvelopeDecoder` before an installed operation owner has funded its scratch and result arrays. Keep ordinary public codec/None/Brotli behavior and schema bytes unchanged.
2. **Make failure prefixes reviewable.** Install each scratch/output prefix before its next allocation, preserve as-coded validation ordering or document/test an intentional precedence change, and use the exact source vector ceilings. Keep scratch until actual synchronous helper return. Do not implement a count-only clone of semantic validation, read before funding, or reuse the hierarchy's unqualified eight-byte formula.
3. **Close remaining compile/profile and operation-owner inputs.** Obtain exact carrier/stride and generated-construction evidence for the selected target, remaining CI/data sites, fixed-limb stack/J evidence, static initialization and gates. Complete the original-P single-slot/control layouts and source-closed issuer/start/transfer protocol; the [operation-owner proposal](m03b-scene-assets-original-p-operation-and-return-closure.md) identifies current J/K and actual-return blockers. No Assets-to-Runtime funding callback or new Store pool.
4. **Keep runtime/native ownership as an explicit admission gate.** Task/context/fault captures, real read/cache and physical-close/actual-return evidence must come from their real owning paths. This batch neither reviews that implementation nor supplies synthetic substitutes. Until those and the numerical original-P claim close, publish no usable footprint and remove neither Scene refusal.

Required focused evidence for that batch:

| Evidence | Distinguishing acceptance condition |
|---|---|
| Reference fixtures | Repeated identical triples retain one first occurrence; same AssetId with a different required schema/major remains distinct; empty and 1/2/odd/even occurrence counts exercise heap boundaries; exact/short and overlapping caller-span cases; actual typed target validation order unchanged. Expected sequence comes from literal triples, not the old scanner as sole oracle. |
| Semantic fixtures | Scene paths/slots/placement bytes survive publication and source disposal with no mutable alias; independent counts include copied and retained partitions. Include the duplicate-Mesh-stream and invalid-Omit prefixes, malformed hierarchy count and late Skeleton-parent/RootMotion rejection. |
| Capacity proof | Arrays have the recorded capacities, all old/new/copy prefixes are paid, overflow refuses before allocation/acquisition, and failure retains exact claims. Body functions must cover these real-package paths; allocation samples/code generation corroborate the site catalogue rather than define expected bytes. |
| Compatibility | Existing factory-origin/None-only selection and ordinary custom/Brotli/package-replacement/Retain behavior remain. No test disables the footprint guard or manufactures a PreparedScene/snapshot/producer. |
| Final profile | Exact runtime/compiled input identities, final operation/issuer layouts, all J/K and shared-registration/backing exclusions with real grants, actual caller/consumer return and cleanup. Unsupported native/constructor-failure observations stay unsupported until a valid capability/stimulus exists. |

Use the existing owning NUnit projects and [ordinary developer workflow](../project/Building.md) when implementation warrants it, Debug before Release, fresh nonzero selected/executed membership. No build/test, allocation probe or commit is part of this documentation addendum. Even a closed body profile does not establish a real M03-B Scene/camera/CPU-extraction consumer; M05 World/native observations still require those producers and the applicable real M04 GPU path.

## Current input freeze

UTC `2026-11-20T06:59:05.458656Z`; HEAD `73df6e4f669af0cfb2d4bf9a9f0719ca3da51f04`. HEAD, the input paths and the separately listed dirty Scene/Sprite tests were checked before this freeze. The old proposal's **96-path manifest is reused only as path membership**, not as accepted bytes/hashes. Its current raw SHA-256 is `ca7d2505b012f4b79becc1be6d5c8a410918167d445b55f1dc540cb7ef6c50a5`. BASE below rehashes those paths at the current revision, including the scanner, exact-array Scene/Prefab/Entity/Hierarchy/Mesh/Texture/Atlas/Skeleton outputs, fixed-limb Mesh predicates, package-pin acquisition ordering, Roadmap and described-read helpers/tests. ADD is the following closed supplemental set; ALL is the union. Neither set includes bounded native-port-specific implementation inputs or this proposal artifact.

```text
docs\proposals\m03b-scene-assets-closed-read-allocation-authority.md
sources\TerraFX.Assets\AssetReference.cs
sources\TerraFX.Assets\AssetId.cs
sources\TerraFX.Core\BinarySchemaDiagnostic.cs
sources\TerraFX.Core\ContentSchemaReference.cs
sources\TerraFX.Core\EbinCodec.cs
sources\TerraFX.Core\EbinDiagnostic.cs
sources\TerraFX.Core\EbinPrimitives.cs
sources\TerraFX.Core\EbinReadDiagnostic.cs
sources\TerraFX.Core\EbinRecordHeader.cs
sources\TerraFX.Core\EbinRecordReadView.cs
sources\TerraFX.Core\EbinRecordView.cs
sources\TerraFX.Core\EbinScanState.cs
sources\TerraFX.Runtime\Animation\SkeletonAsset.cs
sources\TerraFX.Runtime\Animation\SkeletonJoint.cs
sources\TerraFX.Runtime\Rendering\RenderAtlasAsset.cs
sources\TerraFX.Runtime\Rendering\RenderAtlasFrame.cs
sources\TerraFX.Runtime\Rendering\RendererAssetCodecProfiles.cs
sources\TerraFX.Runtime\Rendering\RendererAssetReadMode.cs
sources\TerraFX.Runtime\Rendering\RenderMaterialAsset.cs
sources\TerraFX.Runtime\Rendering\RenderMaterialAssetCodec.Shared.cs
sources\TerraFX.Runtime\Rendering\RenderMaterialAssetData.cs
sources\TerraFX.Runtime\Rendering\RenderMeshAsset.cs
sources\TerraFX.Runtime\Rendering\RenderMeshBounds.cs
sources\TerraFX.Runtime\Rendering\RenderMeshGroup.cs
sources\TerraFX.Runtime\Rendering\RenderTextureAsset.cs
sources\TerraFX.Runtime\Scenes\SceneAsset.cs
sources\TerraFX.Runtime\Scenes\SceneAuthoredRecord.cs
sources\TerraFX.Runtime\Scenes\SceneEntity.cs
sources\TerraFX.Runtime\Scenes\SceneEntityReferenceBinding.cs
sources\TerraFX.Runtime\Scenes\SceneEntitySelector.cs
sources\TerraFX.Runtime\Scenes\SceneExpansionPlan.cs
sources\TerraFX.Runtime\Scenes\SceneExternalSlot.cs
sources\TerraFX.Runtime\Scenes\SceneHierarchy.cs
sources\TerraFX.Runtime\Scenes\SceneHierarchyEntry.cs
sources\TerraFX.Runtime\Scenes\SceneHierarchyNode.cs
sources\TerraFX.Runtime\Scenes\SceneHierarchyPlacement.cs
sources\TerraFX.Runtime\Scenes\SceneHierarchyValidationResult.cs
sources\TerraFX.Runtime\Scenes\SceneHierarchyValidator.cs
sources\TerraFX.Runtime\Scenes\ScenePlacementSlotBinding.cs
sources\TerraFX.Runtime\Scenes\ScenePrefabAsset.cs
sources\TerraFX.Runtime\Scenes\ScenePrefabOverride.cs
sources\TerraFX.Runtime\Scenes\ScenePrefabOverrideApplier.cs
sources\TerraFX.Runtime\Scenes\ScenePrefabPlacement.cs
sources\TerraFX.Runtime\Scenes\ScenePrefabRootPose.cs
sources\TerraFX.Runtime\Scenes\ScenePropertyPathSegment.cs
sources\TerraFX.Runtime\Scenes\SceneReferenceTarget.cs
tests\TerraFX.Runtime.Tests\Rendering\RenderMeshAssetCodecTests.cs
tests\TerraFX.Runtime.Tests\Scenes\RendererAssetCodecProfileTests.cs
```

Hash recipe: raw-file SHA-256; set SHA-256 over ordinal-sorted `relative-Windows-path<TAB>lowercase-raw-SHA256` lines, UTF-8 without BOM/trailing newline, joined with LF. This is proposal-input identity, not a transitive runtime/qualification attestation. Recheck before implementation disposition; changed inputs invalidate that disposition.

| Set | Files | SHA-256 |
|---|---:|---|
| BASE | 96 | `0d82dc5c16afc939400b3b4de1a36218b33cc25e17385096efb06d740572df90` |
| ADD | 49 | `a32d646d9cb2c0661719fe46f2ddedcc02f5475be3d3c45945ec56be298f2f71` |
| ALL | 145 | `1f014ec86c60ecf12b1b059eb7d2d16ad4c172c05ff76d1db4339128834378e9` |

| Unchanged schema/registry source beneath `sources` | Raw SHA-256 |
|---|---|
| `TerraFX.Core\CompiledContentSchemaRegistry.cs` | `cfe040b8e51cb811aecccbb8b0bec0d2993b7a726a5b179968ce0db1415eae08` |
| `TerraFX.Assets\NativeAssetSchemaCatalogue.cs` | `6b9537ea8cefebb48b5dc24261a8c03fb059e3314e7da089f29a639d9e55620a` |
| `TerraFX.Runtime\Scenes\SceneSchemaCatalogue.cs` | `cf0253cf1c207644f0b6c76b0392977e02b0939ff355ed825f38d42705f2b32a` |
| `TerraFX.Graphics\RenderingSchemaCatalogue.cs` | `de25a8c0e249ff9ab5b592fe848c85429c004fe629694c64ef1afef3ba731614` |
| `TerraFX.Runtime\Animation\AnimationSchemaCatalogue.cs` | `c4447f84db8f60f21faf99c6ac9e8f67f13cf4de98f066c9feee8d07820c0daf` |

| Dirty test beneath `tests\TerraFX.Runtime.Tests\Scenes` | SHA-256, unchanged from prior freeze |
|---|---|
| `PreparedSceneAssetRetentionTests.cs` | `e342237b7645be805dd924855b2239907773d31753d22b8b2261bf26c20452f4` |
| `RenderSpriteCapacityTests.cs` | `caa6610cf55f8fffa077745fd96baa45fcecfed2fd051cc763901aeed5500239` |
| `RenderSpriteComponentTests.cs` | `8e65b71b5ac1e0ef9d1f47aa56963054d091df8c229e74ddbb1cae37bb59ab60` |
| `RendererExtractionTests.cs` | `abaeea4d0f6be941af73e640b7fce41ec753d2199654343fa4668d54ec20fc6a` |
| `SceneAssetStoreTests.EngineHost.cs` | `4c0841a40295a891b68974fddd8078dfd0181c31b35ab0bcb0b5114fe0fd1039` |

The source and Roadmap now include a fixed-buffer scanner with a checked data-size term, exact-array Scene/Prefab/Entity/Hierarchy/Mesh/Texture/Atlas/Skeleton outputs, fixed-limb exact Mesh predicates, and package-pin construction before backing-reference/lease-count commit. The full Assets suite passes 69/69 in Debug and Release, including scanner workspace-boundary and data-size cases. These changes do not select the fixed-buffer scanner in `AssetEnvelopeDecoder`, change schema bytes, or open the footprint gate. The five unrelated dirty Scene/Sprite tests remain unchanged from their listed hashes; this addendum remains outside its own input set.
