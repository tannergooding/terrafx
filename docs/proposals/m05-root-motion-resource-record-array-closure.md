# M05 root-motion ResourceState RecordArray closure

## 1. Status and bounded decision

**Tentative proposal, pending review.** The standing user direction permits tentative implementation sequencing, not owner-contract acceptance or qualification. This document proposes the missing internal ResourceState binding/validation path for the exact root-motion resource in [rectification section 2.3](m05-owner-contract-closure-rectification.md#23-canonical-state-and-controls) and [runtime ports section 5](m05-owner-contract-closure-runtime-ports.md#5-complete-root-motion-resourcestate2-family).

Keep the proposed wire shape, including slot tag 13 Controller, subject to the explicit transport closure in section 3.4. Keep all existing public resource signatures, the public FixedArray factory route, old factory families, and their wire behavior. Refine the unimplemented internal factory and validator declarations in runtime ports sections 5.3–5.4 as specified below. No resource-to-attachment substitution, opaque byte payload, new wire kind, public resource service, or fabricated field binding is proposed.

The only deliverable here is this proposal. Production code, tests, maintained design chapters, Roadmap, existing dirty work, and remote state are unchanged. No build, test, implementation completion, or qualification is claimed.

Excluded: playback algorithms, root-transform composition, general-purpose resource graphs, motion-feedback registration, Physics/Integration execution, particles, RecordedContinuation, imported-animation qualification, and M05 acceptance. Chapter 35's separate registration/state/extraction review gate remains open.

## 2. Verified blocking seam

Source paths below are under `sources\TerraFX.Runtime` unless fully qualified; the manifest freezes the inspected bytes, including dirty inputs.

| Existing implementation | Consequence for this resource |
|---|---|
| ``Entities\WorldResourceFactoryBinding`2.cs``, `CollectFixedArrayFields` and `CopyAndValidateFields` | Accepts required FixedArrays, optionally beneath required unambiguous nonreference records. A root RecordArray fails before registration. Removing that rejection alone would not supply a record binding. |
| ``Entities\WorldResourceFieldBinding`2.cs``, `PartitionArray`; ``Entities\IWorldResourceFieldAccess`1.cs`` | Captures one primitive element reader per partition and one primitive author array. It has no slot/source record, optional-arm, or typed AssetReference traversal. |
| ``Entities\ResourceFactoryFamily`2.cs``, `ValidateAuthor`/`ValidateRuntimePartition` | Checks partition-domain array counts and invokes readers, but does not close nested fields or their value predicates. |
| ``Entities\WorldResourceCaptureData`1.cs`` | Checks the captured descriptor and element CLR type. It needs a distinct whole-record access kind, not an invented FixedArray ElementKind. |
| `Entities\WorldResourceValidationBinding.cs`, `ValidateFields` | Checks operation/view identity and returns empty diagnostics. It does not validate this aggregate. |
| ``Entities\WorldResourceCodecDispatch`2.cs`` and resource Read/Restore bindings | Already enforce issuer-bound complete Read results, sticky recorded failures, revocation in `finally`, and exactly one restored write per partition. Preserve these mechanisms. They currently supply no containing-World/historical-creation context. |
| ``Entities\WorldResourceRegistration`1.cs``; `Entities\WorldRegistrationBuilder.cs` | Registration compares PartitionCount with each captured root field's cardinality. Standing resource reservation covers the partition array, not the complete new author/validation/capture/pin workspace. |
| `Entities\WorldPersistenceEcsReader.cs`, `ValidateSchemaRecord` | Already traverses RecordArrays and checks declared tags/kinds/cardinalities/nested schemas. HistoricalEntity validation checks identity-kind membership, not the complete creation-lineage/branch triple or the Animation-specific predicates. |
| `Hosting\WorldLoadPreparedState.cs`, `CreateRestoredCandidate` | Restores resources before restoring the candidate clock. Resource Restore cannot obtain its required tick from that not-yet-restored clock. |
| Animation request/source and symbolic author files | Copied values/authors exist in the dirty worktree. Their constructors are not a registered ResourceState family. The inspected Animation catalogue does not yet declare these three resource records. |
| `sources\TerraFX.Core\EbinCodec.cs`, `Prepare`; `sources\TerraFX.Core\EbinPrimitives.cs`, `WriteRecordArray` | Hard-cap the legacy record/record-array encoding path at 65,536 bytes. The fixed 4096-slot aggregate cannot use that path, even with no present requests. Widening persistence read limits does not change this writer cap. |

`ContentFieldDescriptor` construction in the inspected Animation catalogue carries shape, reference/unit annotations, and author defaults, not executable numeric predicates. The extension must capture real validators; schema-range prose and successful getters are insufficient.

## 3. Exact schema and membership closure

### 3.1 Identities and complete field sets

Use the rectification's assigned `S(key)`: the exact Chapter 10 `TerraFXNameUuidSha256V1` recipe and namespace `2ed8b750-e1a0-4b46-9000-000000000001`. These are fixed identities, not implementation-selected placeholders.

- Resource: `S("resource/animation/root-motion/v1")`; partition CLR type `RootMotionRequest`; PartitionCount **4096**; AllowParallelAccess **false**.
- Factory: `S("resource/animation/root-motion/factory/v1")`, version **1**.
- New record tuples below: `(S(key), 1, 0, Content)`, owner Animation, NotComponent/NotApplicable. The root is ResourceState; slot/source are Auxiliary.
- Reuse `native/asset-reference` **1.0** exactly. Asset targets are `native/animation-controller` **1.0** and `native/clip` **1.0** under the exact retained package/content-set backing. No helper/target minor migration or latest-version lookup is implied.

`RA` means RecordArray. A scalar or Record has count one when encoded. Required fields have the required flag; optional fields retain the optional flag even when encoded. Omission, not a zero-length scalar/record, represents absence. Other annotations are None; no field has an implicit wire default.

| Record | Tag | Exact kind, cardinality, and source |
|---|---|---|
| `animation/root-motion-resource` | 1 | Required Values RA(`animation/root-motion-slot` 1.0), `[4096,4096]`; partition `i` maps to element `i`. No other root field. |
| `animation/root-motion-slot` | 1 | Required Present Bool. |
| Slot | 2, 3, 4 | Optional Guid PlayerId, PlayerCreationLineage, PlayerCreationBranch. Only tag 2 has HistoricalEntity annotation. |
| Slot | 5, 6, 7 | Optional Guid ActorId, ActorCreationLineage, ActorCreationBranch. Only tag 5 has HistoricalEntity annotation. |
| Slot | 8 | Optional Int64 Tick, nonnegative. |
| Slot | 9 | Optional Vector3 Translation, EngineDistance; all lanes finite. |
| Slot | 10 | Optional Quaternion Rotation; admitted finite unit quaternion. |
| Slot | 11 | Optional Sources RA(`animation/root-motion-source` 1.0), `[0,4]` when present. |
| Slot | 12 | Optional UInt8 Policy, exactly Extract=1. |
| Slot | 13 | Optional Record Controller, exact `native/asset-reference` 1.0, Reference=Asset, fixed controller target major 1. |
| `animation/root-motion-source` | 1 | Required nonempty Guid ClipSlotId, Reference=None. |
| Source | 2 | Required Record Clip, exact `native/asset-reference` 1.0, Reference=Asset, fixed Clip target major 1. |
| Source | 3 | Required Int64 PlaySequence, at least 1. |
| Source | 4 | Required Int64 LoopOrdinal, full signed range. |
| Source | 5 | Required Binary32 Weight, finite and in `(0,1]`. |

Every AssetReference helper has exactly required tags 1 UInt32 AssetId, 2 Guid SchemaId, 3 UInt32 SchemaMajor. Require `1 <= AssetId <= int.MaxValue`, the field's fixed target SchemaId, and SchemaMajor=1. There is no wire Minor field. The typed readers use `WorldAssetReference`; captured fixed target/version policy supplies the helper's constant fields explicitly. A bare integer is not a substitute for checking the complete incoming reference.

### 3.2 Presence and set membership

1. **Absent slot:** Present=false and exactly tag 1. Tags 2–13 are forbidden. Its author has twelve null body properties, not default body values.
2. **Present slot:** Present=true and exactly tags 1–13. Each body property is present, including an initialized Sources array of length zero when there is no contribution. Missing Sources is not an empty Sources array.
3. **Runtime storage:** `_present` is exactly 0 or 1. An absent partition is entirely explicitly zero initialized. For a present partition, SourceCount is `[0,4]`; all unused inline source storage is bitwise zero. WorldGuid is runtime-only and equals the containing World; SourceCount is runtime-only and equals the encoded Sources count.
4. **Stage-2 slot set:** Independently derive the complete Extract-mode player set from the actual admitted stage-2 membership. Ignore-mode players supply no slot. Sort that derived membership by unsigned RFC Player EntityId and emit its complete requests into `[0,N)`, followed by absent `[N,4096)`. Player IDs are strictly increasing. Motion actors are exclusive as required by the owner contract; duplicate qualified actors reject. Do not silently drop a player to fit.
5. **Historical persistence set:** Capture/Restore validates this dense last-produced set, not equality with the current live component/attachment set. Stage-7 destruction removes no request. Player and Actor triples must resolve against complete containing identity history, including retired assignments. Creation lineage/branch cannot be inferred from a current branch, bare Guid, or successor.
6. **Tick:** All present slots use the issued RequiredRequestTick. That is the executing tick for production and the containing completed tick for capture/source/restore. A missing required-tick context is allowed only for fresh all-absent defaults. An all-absent array encodes no invented tick.
7. **Source set:** At production, independently derive the positive base-layer root contributors selected by the existing extraction/Seek policy. Emit exactly one row per selected ClipSlotId, in the retained controller definition's clip-slot order, at most four. Positive means its final stored normalized coefficient is positive; higher-layer/noncontributing/zero-weight entries are excluded. Copy that contribution's exact Clip, PlaySequence, LoopOrdinal, and normalized weight bits. Serialization performs no further normalization.
8. **Source joins:** ClipSlotIds are unique and resolve in slot tag 13's exact retained controller; each Clip reference equals that definition slot's Clip and has the required extractable content. The array is not the whole allowed-clip inventory. Capture/Restore must not substitute the live player's current active clips or attempt to reconstruct a destroyed player's historical blend.
9. **Zero sources:** A present zero-source request has bitwise positive-zero Translation and Quaternion.Identity. A nonzero source count may also produce identity motion. An absent partition must not be filled with identity rotation.

### 3.3 Numeric validator closure

Preserve the current value predicate for Rotation: all lanes and `Quaternion.Dot(q,q)` finite, with `MathF.Abs(Quaternion.Dot(q,q) - 1.0f) <= 2.0e-6f`. Preserve admitted quaternion signs and float bits; do not normalize, sign-flip, or repair input.

Make the prior phrase “normalized weights” executable without requiring an exactly representable sum of one:

- Zero sources follows the identity rule above. One source requires weight exactly `1.0f`.
- For two through four sources, each weight satisfies `(0,1]`, and their **exact mathematical binary32-value sum** differs from one by at most `2^-21`.
- This is a conservative wire/state admissibility predicate, not a replacement normalization algorithm or proof of historical sampling. With at most four positive inputs, ordered binary32 total formation and rounded division have a combined error below that bound. The producer must still copy the exact vector from the already-selected ordered normalization, not merely generate any vector within this bound.
- Evaluate the comparison without a new floating reduction: scale each positive binary32 value by `2^149`, sum in three unsigned 64-bit limbs, and compare with `2^149`, allowing difference at most `2^128`. Four terms require at most 152 bits. No allocation, unbounded integer, tolerance chosen from observed output, or mutation of the stored vector is needed.

This explicitly refines the previously unimplemented validation predicate; it changes no implemented payload or blend arithmetic. Literal fixtures must cover its boundary and ordinary rounded vectors, such as three binary32 one-thirds.

### 3.4 Transport bound and existing State2 envelope

**Verified constraint:** `EbinCodec.MaximumRecordBytes` is **65,536**. `EbinCodec.Prepare` rejects a larger aligned record extent. `EbinPrimitives.WriteRecordArray` rejects a larger array payload, prepares every child through that same capped writer, and its `RecordArray` convenience method allocates only a 65,536-byte destination. `EbinPrimitives.Record` also calls the capped encoder. Increasing `WorldPersistenceEbinLimits` or selecting a larger read limit cannot make these writers encode the aggregate.

The failure is not confined to densely populated requests. The helper's own first size check needs at least **294,928 bytes** for 4096 minimum 64-byte child records plus the aligned array index table. Under this proposal's actual schema, an absent slot is 112 bytes: the 64-byte record header, one 32-byte field entry, and its Bool payload with 16-byte alignment. The 4096-absent Values payload is **491,536 bytes**, and its containing ResourceState root is **491,632 bytes**. At the other bound, an AssetReference is 208 bytes, a source is 496 bytes, and a present four-source slot is 2896 bytes; the complete 4096-by-four root is **11,894,896 bytes**. These are independently derived canonical wire extents, excluding the enclosing State2/ECS/World records and all workspace. They are not the 315-byte in-memory partition layout.

**Envelope finding:** existing State2 code structurally supports a larger **single payload record** under its admitted snapshot limits, but does not supply a per-resource chunk sequence:

| Existing path | Verified behavior |
|---|---|
| `WorldPersistenceEbinLimits.Create` | Sets `EbinReadLimits.MaximumRecordBytes` to the selected World's `MaximumSnapshotBytes`; it also supplies finite aggregate/depth/field/array limits. The name describes a read-limit type, not an automatic change to `EbinCodec.Prepare`. |
| `EbinPayloadWriter.MeasureRecordArray` / `WriteRecordArray` in `sources\TerraFX.Core\EbinPayloadWriter.RecordArrays.cs` | Different APIs from `EbinPrimitives.WriteRecordArray`. They accept encoded child slices, caller-owned storage, and explicit limits; the payload bound is `limits.MaximumRecordBytes`, not the legacy 65,536 constant. |
| `EbinStructuralWriter.Measure` / `Prepare` / `Write`, and `EbinStructuralReader` | Use an `EbinScanState` constructed with the supplied limits. The structural writer validates its complete extent against those limits and uses the existing 64-bit EBIN length/offset fields; it does not call the legacy `Prepare`. |
| `WorldPersistenceStateRowWriter.WriteResource` → `WorldPersistenceEcsWriter` → `WorldPersistenceWorldRecordWriter` | Already validate/embed the resource payload and build the enclosing records/arrays using those limits-driven APIs. Actual ECS capture supplies `WorldPersistenceEbinLimits.Create(world.Options)` to this path. Every enclosure, including the complete snapshot, must fit; fitting the resource payload alone is insufficient. |
| Resource row validation and Restore | Expect one ResourceId and one exact payload Record per resource, with unique ordered ResourceIds. They do not concatenate repeated resource rows or reassemble chunks. |

**Tentative transport decision:** retain the exact flat 4096-slot schema and use the existing bounded structural/payload writer path, with the actual owner-issued persistence limit profile, for the complete new resource tree. Count, Read, Write, source preflight, and post-restore canonical comparison must use the same admitted profile. Do not route any aggregate-sized parent/array through `EbinCodec.Prepare`, `EbinPrimitives.Record`, or `EbinPrimitives.RecordArray`; a wider outer wrapper cannot rescue a capped inner encoding. Preserve the legacy constants, Foundation behavior, existing small-record paths, and public APIs.

This is a proposed codec/limit-plumbing requirement, **not an implemented large-resource observation**. The private resource bindings must carry the actual profile as specified in section 5, and section 6 must fund child storage, slice tables, array payloads, candidate/output buffers, and simultaneous enclosing State2 copies. Use the existing snapshot/Animation/World/domain ceilings; do not manufacture a permissive profile from untrusted source length or raise a ceiling merely to make a test pass.

If review instead requires the 65,536-byte helper bound for each owner payload, the current flat schema and resource envelope cannot meet the 4096-slot contract. The owner must close a compatible versioned cross-record/chunk envelope or an explicit changed cardinality/bound before selecting that profile: exact chunk identity/order/membership, total counts/bytes, missing/duplicate rejection, complete reassembly, canonical capture, and atomic Restore publication must be defined. Nesting smaller chunks inside one still-capped parent, repeating ResourceId rows, omitting absent slots, or silently lowering 4096 does not solve the existing contract. This proposal changes no maintained contract to authorize such an alternative.

The related playback transport is also not closed by fixing this resource alone: current `AnimationRuntimeStateCodec.Encode` calls `EbinCodec.Prepare`, its record-array construction uses `EbinPrimitives.RecordArray`, and its reader selects `EbinReadLimits.Foundation`. The M05 4096-player playback contract therefore needs the same owner-reviewed wide codec/profile closure, or a separately closed compatible bound decision, before claiming the complete live Animation composition. No playback-code change or qualification is made here.

## 4. Smallest internal typed extension

### 4.1 Keep the public branch closed

Keep the existing public `WorldResourceFactoryBinding<T,TAuthor>.Create` and `PartitionArray` behavior unchanged, including required-FixedArray closure, storage audits, exceptions, and registration cardinality behavior.

Implement only the internal record binding/reader operations already projected in runtime ports section 5.3: `WorldResourceRecordBinding`, `WorldResourceRecordFieldBinding.Value/Optional/AssetReference/OptionalAssetReference/OptionalRecords`, `IWorldResourceOptionalArrayReader`, and `PartitionRecords`. Needed primitive types are Bool, Guid, Int64, UInt8, Binary32, Vector3, and Quaternion; AssetReference is a closed record policy, not another primitive.

Use a distinct internal factory entry for the new path. Refine the proposed record `Create` to require its validator as well:

```csharp
// TerraFX.Entities; existing T : unmanaged, TAuthor : notnull constraints apply.
internal interface IWorldResourceRecordValueValidator<T, TAuthor>
    where T : unmanaged where TAuthor : notnull
{
    bool IsValidRuntime(in T value);
    bool IsValidAuthor(in TAuthor value);
}

internal interface IWorldResourcePartitionRecordsValidator<T, TAuthor>
    where T : unmanaged where TAuthor : notnull
{
    bool IsValidRuntime(scoped ReadOnlySpan<T> partitions);
    bool IsValidAuthor(in TAuthor value);
}

// On WorldResourceRecordBinding<T,TAuthor>; replaces its proposed
// no-validator Create, not an implemented public member.
internal static WorldResourceRecordBinding<T, TAuthor> Create<TValidator>(
    CompiledContentSchemaRegistry schemas, ContentSchemaReference schema,
    ComponentStorageDeclaration storage,
    ImmutableArray<WorldResourceRecordFieldBinding<T, TAuthor>> fields,
    TValidator validator)
    where TValidator : struct, IWorldResourceRecordValueValidator<T, TAuthor>;

// On WorldResourceFactoryBinding<T,TAuthor>.
internal static WorldResourceFactoryBinding<T, TAuthor> CreateRecords<TValidator>(
    Guid factoryId, int factoryVersion, WorldResourceSchema<T> schema,
    ComponentStorageDeclaration partitionStorage,
    WorldResourceFieldBinding<T, TAuthor> values,
    IWorldResourceDefaultProvider<T> defaults, TValidator validator)
    where TValidator : struct,
        IWorldResourcePartitionRecordsValidator<T, TAuthor>;
```

`CreateRecords` admits exactly one required root RecordArray with one exact Auxiliary child tuple and a positive fixed domain. It must receive a `PartitionRecords` binding, not a FixedArray or arbitrary record field. The root-motion domain is exactly 4096. This replaces only runtime ports section 5.4's instruction to use the public `Create` for this new family.

The Animation provider captures three stateless readonly validator structs: SourceValidator for `RootMotionSource/RootMotionSourceAuthor`, SlotValidator for `RootMotionRequest/RootMotionSlotAuthor`, and ResourceValidator for the complete partitions/`RootMotionResourceAuthor`. Their defaults are valid configurations; none owns services, assets, a World, or disposal. `false` means invalid value; thrown exceptions remain callback/protocol failures and are not converted into successful validation.

### 4.2 Closure and storage audit

Capture the exact registry, schema/field objects, concrete readers, validators, defaults, and child bindings once. Require initialized/non-null collections, exact field membership in ascending tag order, exact tuples/roles/annotations, finite acyclic nesting, and the existing depth limit. Reject role-mask discovery, ambiguous or foreign descriptors, duplicate/missing fields, incompatible primitive/author types, and unknown binding kinds before World construction.

`PartitionRecords` means one **whole partition** per Values element. Child field descriptors are direct fields of their own exact record, not resource field paths containing invented RecordArray indices. `WorldResourceSchema.GetField` and `ComponentFieldPath` need no extension.

Preserve runtime ports section 5.2's 315-byte request and 40-byte source storage inventory, subject to implemented `Unsafe.SizeOf`/offset verification. Audit every scalar physical leaf, explicit padding if any, and each of the four inline source repeats. Every schema-value leaf is claimed once. For Sources, verify four repetitions of the complete 40-byte source declaration within the 160-byte inline region; a partial group, wrong stride/offset, or overlapping claim rejects. WorldGuid/SourceCount are declared portable runtime-only derivations, not omitted schema values. A shared Present gate is a read dependency, not thirteen claims on its byte. Asset helper constant fields are captured policy, not hidden storage or guessed defaults.

Add only internal self-inspection needed by the validator: `RootMotionRequest.HasCanonicalStorage` and `RootMotionSource.IsZeroStorage`. They inspect their own private fields; the former rejects raw presence bytes other than 0/1, invalid present bodies, nonzero absent bodies, and nonzero unused source tails. The latter requires all source fields, including Weight bits, to be zero. Do not use reflection, reinterpret the whole struct as EBIN, expose private storage publicly, or infer initialization from `IsValid == false`.

The descriptor's public `Fields` remains the single captured root Values descriptor. The full child/validator closure is retained by the same `ResourceFactoryFamily`/`ResourceFactoryControl`; pair the codec through the existing `WorldResourceCodecBinding.Create` with that exact family. Equal separately constructed descriptors are not interchangeable capture handles.

### 4.3 Actual dispatch changes

| Seam | Required aggregate-only behavior |
|---|---|
| Factory collection and field access | Add a discriminated internal partition-record access kind. Reuse typed getters; do not treat RecordArray as FixedArray or compare Sources `[0,4]` with PartitionCount. Builder domain validation sees only root Values. |
| `ResourceFactoryFamily.ValidateAuthor` | Check root Values count before indexing, validate every slot/source through the captured record tree, then invoke ResourceValidator. A provider returning a malformed author after successful Complete is a protocol failure. |
| `ValidateRuntimePartition` | Run SlotValidator's raw/local checks before optional getters, then deep traversal/SourceValidator. A hidden invalid body cannot become an absent record. |
| `WorldResourceValidationBinding.ValidateFields` | For this family, perform the captured complete aggregate traversal and ResourceValidator through the exact operation/view/family. Return initialized bounded diagnostics. Preserve the legacy FixedArray branch; do not return empty merely because the aggregate visitor is missing. |
| `WorldResourceCaptureData` and capture binding | Retain exact descriptor/type/index checks. `GetCount(Values)` returns 4096; `GetElement<RootMotionRequest>(Values,i)` copies partition `i` through the record access kind. Deep traversal uses the captured child tree. Account for erased-access boxing rather than claiming it is allocation-free. |
| Codec dispatch and registration | Invoke the same complete local validators for defaults, successful author admission, restored arrays, live validation, and each protected capture. Preserve result issuer checks, consumed/written extent checks, sticky failures, and `finally` revocation. |

For each record, first check local arms/raw storage; then walk direct fields in tag order. For an optional field, call IsPresent before any body/count/element reader. Never invoke a body getter for absence. A present Sources count of zero is valid; reject an out-of-range count before indexing. Invoke no child reader for an absent array.

All new aggregate field/validator operations check the issued operation/thread/family before operands. Record active lookup/type/index/presence misuse on that operation, even when the provider catches the exception. The real dispatch checks failure before accepting a result. Revoked or foreign scopes confer no authority on a later invocation.

## 5. Contextual validation and canonical operations

Keep `RootMotionResourceProvider` and the resource-only `WorldResourceReferenceBinding`/`IWorldResourceRecordCodec` from runtime ports section 5.4. They remain proposed internal ports, not existing APIs. No additional public context, resolver, binding issuer, or arbitrary validation callback is needed.

Thread that exact operation-bound reference scope through the actual registration, capture, source-validation, Read, and Restore calls. Use the complete validated source history before candidate construction and the protected actual World ledger thereafter. Missing context is protocol failure, not empty WorldGuid/history, a default identity, or a request to consult a global registry.

Add only an internal `EbinReadLimits PersistenceLimits { get; }` to the aggregate's Read/Capture bindings and source-preflight reference binding. It exposes the one immutable limit profile captured by the actual protected World or validated load/construction owner. World operations derive it from `WorldPersistenceEbinLimits.Create(world.Options)`; the source-history binding/issuance path receives the selected target profile explicitly before callbacks. Each getter checks its exact active operation/thread/family; a missing profile rejects rather than defaulting to Foundation or synthesizing wider limits from the record's claimed size. Existing public codec signatures and FixedArray behavior do not change.

Local captured validators own shape, optional arms, raw initialization, finite/range/quaternion/weight predicates, dense prefix, unique/order membership, and cross-slot tick/World consistency. The provider additionally validates the **issued** containing World/tick and historical triples. The higher-owned prepared-assets/join path validates the exact controller/Clip backing and source membership/order. Neither Entities nor a resource codec loads assets.

| Operation | Required sequence and result |
|---|---|
| Defaults | Verify exact resource/schema/count/index facts from the actual default invocation. Explicitly initialize all 4096 fresh partitions to the absent zero value. No identity-body fill, package load, task, hidden player, or substitute source record. |
| Source preflight | After complete schema/identity/clock validation, validate this literal root/slot/source shape, presence, counts, numeric predicates, dense prefix, historical triples, and exact target registration before candidate/default/Restore callbacks. Prepare request-only content joins from the owned target projection and exact package, not a live Scene. |
| Read | Check cancellation and full structural EBIN canonicality under the issued transport profile: tuples, required flags, kinds/counts, lengths/index tables/alignment, known tags, and complete consumption. Bound counts before allocation. Build only the complete immutable symbolic graph; apply the same local/context predicates before issuing `binding.Complete`. Preserve Sources absence versus present-empty. |
| Restore | Use `checked(Checkpoint.NextTick - 1)` passed from `WorldLoadPreparedState` through `RestoreEcsRows`/resource registration, not the candidate clock. Resolve each present Player/Actor triple through References, construct its copied request with that containing WorldGuid and exact source bits, and SetPartition once at each index `[0,4096)`. Explicitly set absent slots too. Run complete local/context validation. |
| Validate | Validate all 4096 partitions and the whole-resource predicates. Dead identities are legal when their complete historical facts match. A matching Guid with a different creation branch, a foreign WorldGuid, or stale tick rejects. No missing visitor/context/asset preparation may produce a success-shaped empty result. |
| Count | Under one protected capture lease/revision and the issued transport profile, walk the exact captured Values/slot/source/reference tree. Include optional field presence, tag 13, all record/array headers and index tables, alignment, and checked extents. No padding, OwnerId, runtime WorldGuid, SourceCount byte, or raw native memory enters the encoding. |
| Write | Use the same frozen traversal, presence decisions, and limits-driven writer path as Count. Check destination capacity before emitting. Emit exactly Count bytes; leave any destination tail untouched. Preserve accepted float/quaternion/source-order bits. Do not sort, normalize, omit required empty arrays, insert absent slots, change versions, or repair invalid input. Cancellation may discard unpublished staging; it cannot publish a partial record. |
| Join/publication | Prepare and transfer exact request-only controller/Clip pins, including for destroyed players/actors. The post-restore join changes derived ownership only. Fresh protected Validate/Count/Write must equal the already validated target projection after the join and before restored marker installation. No motion application, command/event replay, hidden Step, or canonical repair. |

The bounded span preflight and author/runtime validators share the same captured field closure and private value/membership predicate helpers. Preflight can inspect one slot and at most four sources at a time, with explicitly initialized bounded scratch; it must not construct an uncharged full author/request graph merely to measure that graph. It is not a second playback implementation or an expected-value oracle.

Read reports malformed input through the existing structural or `WorldResourceReadFailureKind` rejection channel with nonempty bounded diagnostics. Use SourceOffset and the valid root Values path; do not invent RecordArray-index `ComponentFieldPath` segments. A null/foreign/duplicate result, invalid provider-produced author, missed/duplicate restored index, caught binding misuse, or consumption/count discrepancy is protocol failure, not bad input to repair. Use existing load/admission failure channels for capacity and missing exact content.

After restored publication, the copied resource authorizes no movement callback. The next real stage 2 replaces every partition before ordinary application resource readers. Application Write remains forbidden; read phase permissions and current executing-tick checks remain those in the cited contracts.

## 6. Capacity and budget sequencing

The exact finite payload envelope is 4096 slots, at most 16384 source records, and at most 20480 AssetReference records. Including the root, maximum payload membership is **40961 records and 196609 fields**. Payload nesting has four record levels; enclosing State2 depths still count against existing limits. These are independently derived count bounds, not preallocated object/byte entitlements or a reason to raise EBIN limits. Section 3.4's **491,632–11,894,896-byte** resource-root extent requires the existing admitted snapshot-wide structural path, not an assumption that this aggregate fits one 65,536-byte record.

For the proposed 315-byte layout, the existing partition-array reservation formula gives **1,290,304 bytes**: 4096 partitions plus its existing 64-byte envelope/alignment policy. That arithmetic is not a measurement of the complete implementation.

Required order:

1. **Freeze registration first.** Validate schema/record/storage/validator closure and exact 4096 domain before World issuance. No speculative callbacks or late resource registration.
2. **Fund fresh World storage.** Include the standing resource array in the existing resource/system sublimit and World/domain construction admission before `CreateStorage` or defaults. Include all selected support-owned standing validation/staging/cleanup backing in its real owner reservation; do not charge it twice or omit it because the partition array is already charged.
3. **Bound source, then fund Read/Restore.** Fund preflight's own bounded scratch/control/diagnostic envelope before invoking it. Its span traversal determines exact membership/extents before building the author graph. The existing load owner then reserves complete source/current/old-World/candidate coexistence, root/slot objects, source/Values arrays and constructor copies, transient constructor sets, erased-reader boxes, operation/reference controls, diagnostics, Restore's written-index map, canonical writer workspace, pins, and cleanup before Read/Restore materialization. No allocation-first/refund-later accounting or assumed GC is used to make the peak fit.
4. **Fund a whole stage-2 replacement.** Count the complete projected request/source/pin set; reserve new array/working/validation storage and acquire new controller/Clip ownership while old actual readers/output owners still retain their backing. Prepare/validate all 4096 values before installing the resource and its companion pin root together outside callbacks. Refusal installs neither. This does not promise rollback of other earlier tick writes.
5. **Fund capture before traversal.** Count/Write validation, boxed values, encoded child storage/slice tables, array payloads, structural candidate/output buffers, simultaneous State2/ECS/World copies, retained package ownership, and simultaneous passes must have a finite admitted envelope. Check each enclosure and the final snapshot against the actual issued limit profile before publication. The full admitted upper bound may be retained until operation end; do not assume immediate collection or silently use an uncharged second materialized graph.
6. **Release actual ownership only.** Overwrite, Stop, Structural destruction, canceled waits, dropped tasks, Scene unload, or World closure does not prove that retained request/output/capture backing is unused. Transfer charges with real ownership. Failed cleanup anchors remaining graph and credits under the existing cleanup/quarantine owner; attempt independent cleanup without premature refunds.

All Animation-owned current/derived/staging/retained storage stays within the existing **128-MiB** Animation ceiling, the applicable resource/system limit, **2-GiB** World limit, and exact admission-domain limits. Assets retains its separate decode/lease accounting; Animation still funds its own wrappers/retentions. Count maxima do not promise joint byte fit. Use qualified backing/header/alignment sizes and checked arithmetic, not arbitrary object estimates. A complete reservation recipe is required before selecting the resource token; `4096 * sizeof(request)` alone is a no-go.

Synchronous codec scopes acquire no independent active-World authority. Existing asynchronous operation uses retain their own credits; unpublished restore candidates use the load/construction owner, not a fabricated `AcquireGuidUse` or borrowed Scene-command budget.

## 7. Focused implementation and evidence

These are required focused checks when implementation is authorized, **not results or new acceptance IDs**. Use ordinary owning-project NUnit/VSTest commands, Debug before Release, with nonzero selected/executed tests and current-run TRX evidence. No tests are added or run by this proposal.

| Area | Distinguishing checks |
|---|---|
| FixedArray preservation | Existing real resource registration/default/Read/Validate/Count/Write/Restore tests and literal bytes remain unchanged. Public signatures/member set and rejection of unsupported public factory shapes remain unchanged. |
| Transport boundary | Independently literal 4096-absent and 4096-by-four resource roots exceed 65,536 bytes and have the section 3.4 extents. Verify the legacy helpers still reject their oversized payloads even when an outer reader has wider limits. Exercise the proposed limits-driven resource Count/Write/Read through actual State2 Save/Restore with sufficient budgets, then fail missing/foreign profiles, undersized workspace, and the complete snapshot limit without partial publication. Large playback-state transport needs its own real producer selection; small playback fixtures are not a 4096-player pass. |
| Captured closure | Reject missing/extra/duplicate/reordered tags, wrong tuple/role/annotation/primitive/author type, foreign registry/descriptor handles, recursive/ambiguous closure, partial/overlapping storage repeats, hidden runtime/native/Entity-handle leaves, and omitted validators. A Sources maximum of four is never compared with PartitionCount. |
| Literal canonical payloads | Independently author the exact 4096-absent payload; one present zero-source identity plus absent suffix; and a present four-source request with controller tag 13. Verify Read→Restore→Count/Write bytes through real dispatch. Cover signed-zero preservation where admitted and positive-zero identity requirements. Expected bytes/member sets come from schema literals, not production output. |
| Negative values/membership | Values counts 4095/4097, Sources counts 0/4/5, missing Sources versus present-empty, false Present with body, true Present missing any body tag, nonprefix presence, duplicate/descending players, duplicate actors/ClipSlotIds, wrong controller slot/Clip/order/backing, invalid weights/quaternion/policy/tick, wrong historical creation branch, and UInt32 AssetId outside signed range. Count/source failures precede candidate/default/Restore callbacks. |
| Raw storage and validators | Raw presence=2, nonzero absent body, nonzero unused source tail, default source, mismatched SourceCount, foreign WorldGuid, stale tick, and a provider attempting to return an invalid author. Exercise the normalized-vector exact bound, single-source unit weight, three rounded one-thirds, and clearly unnormalized vectors without rewriting their bits. |
| Scope/protocol and cancellation | Real World/load-issued bindings only. Check foreign/expired/wrong-thread access, wrong record element type, absent getter misuse, duplicate/foreign Read result, caught active misuse, missing/duplicate SetPartition, Count/Write mismatch, and cancellation between slots. No partial candidate publishes; old World remains installed on failed replacement. |
| Budget/ownership | Exercise complete 4096-by-four membership and actual measured graph/workspace, budget refusal just below a whole admitted peak before materialization, repeated overwrite with real retained owners, cancellation, and cleanup failure retaining its actual credits. Verify no hidden extra resource/system registration or public allocation entitlement. |
| Historical-only production witness | Real package→Scene→Animation stage 2 produces a request; Structural destroys the player/actor/Scene; production Save→PrepareLoad→Standard Branch CommitLoad retains the request-only exact assets and restores canonical bytes. No live-player fallback, movement/event replay, hidden Step, or changed post-join Count/Write is permitted. Next tick overwrites before readers. |

Implementation can proceed in three dependent batches: (1) internal record/storage/validator family plus FixedArray regressions; (2) exact Animation schemas/provider, owner-reviewed wide transport/profile closure, and funded reference/Read/Write/Restore dispatch; (3) actual stage-2/pin-owner installation and historical-only production save/restore. Do not select the built-in resource registration before its complete batch-2 callable/budget closure exists. The complete Animation token also requires playback transport and its actual phase/participant prerequisites; resource-only closure does not qualify that composition.

Managed family/provider behavior, compiled actual roots, real World/package consumers, native/GPU output, NativeAOT, and full Chapter 35/M05 acceptance are distinct. The focused managed checks cannot qualify a live producer that has not run. Native rendering and actual Physics feedback retain their own prerequisites/observations; this proposal creates no substitute evidence or blanket M03/M04 exit dependency.

## 8. Frozen proposal inputs

Base HEAD: `31f156a51ccc462ae9c5cc8d4e87e00c0054dec4`. Hashes are SHA-256 of exact worktree file bytes, not HEAD substitutes. C=tracked clean, M=tracked modified, ??=untracked at inspection. Only the relevant sections/symbols were inspected; this manifest is not a review of unrelated file content or the rest of the dirty worktree. Recheck these hashes before applying a review disposition; acceptance would cover only the frozen proposal/input scope.

| Input path | State | SHA-256 |
|---|---|---|
| ``docs\proposals\m05-owner-contract-closure-rectification.md`` | ?? | `67134F02B92D316524848ECB8E0C917EF868DA3D2EDA5FC581DBAA25B1825632` |
| ``docs\proposals\m05-owner-contract-closure-runtime-ports.md`` | ?? | `605CF1D92DC362A8B73EA9BDA3A5467757942C268712F484AF53D9A9159D53E3` |
| ``docs\project\Roadmap.md`` | M | `508F59CCCD75C74F3BF70EC3DCC01F107EC4F0B9AF52A1C78DA260E6E57A1BE4` |
| ``docs\design\35-implementation-roadmap.md`` | C | `24EEC6E92B7AD904D552F290EF748FFC8FCE13B6B94A89EBD9F39487D4584D87` |
| ``sources\TerraFX.Runtime\Entities\WorldResourceFactoryBinding`2.cs`` | C | `551A85B6B8B0D0A31EC75B526C7C744EF6BFCFA0CCF4535859C3FF780A46034C` |
| ``sources\TerraFX.Runtime\Entities\WorldResourceFieldBinding`2.cs`` | C | `5AECB7B86050406E5AD70F21D1C355F0B97E559D4729CD4C478675566E109164` |
| ``sources\TerraFX.Runtime\Entities\ResourceFactoryFamily`2.cs`` | C | `73B98AF20C28E970B4589E7DE0F5B90BB6B1F0345E301E81BEE13247DD15A022` |
| ``sources\TerraFX.Runtime\Entities\WorldResourceCaptureData`1.cs`` | C | `FECDB8B8F90D5BA175F8E7D6C903713778692A81CA0C28C5ADEB32F84189734C` |
| ``sources\TerraFX.Runtime\Entities\WorldResourceSchema`1.cs`` | C | `E5D2D8500A02E5D0AE560CF572BE2BEBA57F8DB457771D9AF45CA03A179B3860` |
| ``sources\TerraFX.Runtime\Entities\WorldResourceFieldDescriptor.cs`` | C | `545A4EEF9B629F773EDD54FF1739BDD3638EA9E6FECEA9808522E551CAD9894E` |
| ``sources\TerraFX.Runtime\Entities\WorldResourceValidationBinding.cs`` | C | `C5BED2DFD1FC87F1BDF1AD4C89FAE523798D8925C238D57B0F32D374A9D6FBA9` |
| ``sources\TerraFX.Runtime\Entities\WorldResourceCodecDispatch`2.cs`` | C | `3B3C76F7545015E945437AA999AC82D42BA60A11BE5B55DDF0C196A07F177B7D` |
| ``sources\TerraFX.Runtime\Entities\IWorldResourceFieldAccess`1.cs`` | C | `1262DECE7DA25EDD09C5513783E7FD1CC9459ED926ABD2899B1ED4210330DB2D` |
| ``sources\TerraFX.Runtime\Entities\WorldResourceCaptureBinding.cs`` | C | `E293A651873145E9EC126ECC566DF31A58EA832904AE57F70C28141235AFB66E` |
| ``sources\TerraFX.Runtime\Entities\WorldResourceReadBinding`1.cs`` | C | `3E6FE156C2A28F6FB085B9A98B533E0AA81F8FCEFBDF50DF0E782721F41809C4` |
| ``sources\TerraFX.Runtime\Entities\WorldResourceRestoreBinding`1.cs`` | C | `956F078BE58B652335AE25F0A577266A6EE116AF9B9AA2D20EED7EC559E82592` |
| ``sources\TerraFX.Runtime\Entities\WorldResourceOperationControl.cs`` | C | `412807B98C8C71AD17E169A53D63DCADE1408BDC138316D8C2D7A8CBE59E261C` |
| ``sources\TerraFX.Runtime\Entities\WorldResourceRegistration`1.cs`` | C | `3AB22DF1D073B35578982700D55AB8C4C22AF152E668C89236CF1C7880BEC57B` |
| ``sources\TerraFX.Runtime\Entities\ResourceFactoryControl`1.cs`` | C | `CA0885D913FADB9656A75361B257405067B868534324C6B0E821555F06DDDBF4` |
| ``sources\TerraFX.Runtime\Entities\WorldResourceCodecBinding`1.cs`` | C | `2CEFB29B7291F43B3A0748C988E094223AD4D94118FF73D29AC12A5EF2EC9CB0` |
| ``sources\TerraFX.Runtime\Entities\WorldResourceFactoryDescriptor.cs`` | C | `FBB94742E6180F80291F47967767920DB404FE1DCAD01FF0454C0EF084622CE8` |
| ``sources\TerraFX.Runtime\Entities\WorldPersistenceEcsReader.cs`` | M | `382EA7B9D4C48A6507F9C7DA59CAFA24FC3330A1F970B5613D3AC48DD2F88569` |
| ``sources\TerraFX.Runtime\Entities\World.PersistenceRestore.cs`` | C | `CB87DC7B8645EB07B58084E6F4A6E772E085A86883D2D7716A82CBAD965CBE0F` |
| ``sources\TerraFX.Runtime\Entities\WorldRegistrationBuilder.cs`` | C | `BB60686B9C2B2090F01D9B9C1C27F3D22DFE692EB6B5F71D652B662F61BD9CA7` |
| ``sources\TerraFX.Runtime\Entities\WorldPersistenceResourceRegistrationWriter.cs`` | C | `568D952D61E46333BA80BA8BF68A5C1E91A7496320B241761765BFBC6C04918F` |
| ``sources\TerraFX.Runtime\Entities\World.cs`` | M | `ED008B0CCBF8CB1A1F613F7C9CC4949D9970889DE7DAD6D9B746F79654F13E11` |
| ``sources\TerraFX.Runtime\Entities\World.PersistenceCapture.cs`` | C | `482F45529A56BF60936A1CAF43282828476A1A2D0F52C2DC94EB3646349B0250` |
| ``sources\TerraFX.Runtime\Entities\IWorldResourceStateCodec`2.cs`` | C | `AEDDE44A544E36017482582F9BD6EF8EA62A6B409E3682304A6AE94CAD6C5279` |
| ``sources\TerraFX.Runtime\Entities\IWorldResourceFactoryDispatch`1.cs`` | C | `DF9D5F0F31C49B80E5E5BD78CA5141C3E95C8CB0BFB239E64EB98DC9A68ED100` |
| ``sources\TerraFX.Runtime\Entities\IWorldResourceRegistration.cs`` | C | `33150C0A51E172E5FFD9CDBB50A281392AEE71B64F3448082078233506DF3841` |
| ``sources\TerraFX.Runtime\Entities\WorldResourceDefaultBinding.cs`` | C | `923260907AD619C318D8737B1147D5922BD199555F42EEDBB9363106044B9450` |
| ``sources\TerraFX.Runtime\Entities\WorldResourceReadFailureKind.cs`` | C | `8152897AF409403543C49FE0F37413A2D1657195651F5E3B388E030F509BB085` |
| ``sources\TerraFX.Runtime\Animation\RootMotionResourceAuthor.cs`` | ?? | `DB5398794091A167A6C44ABAD63B73B143F36170852925981F4B35F6F6F16286` |
| ``sources\TerraFX.Runtime\Animation\RootMotionSlotAuthor.cs`` | ?? | `97FD180FDB4DE8A2D837FD31EEF2AD8B23CFF1614DD902976D0C2A169943ED1C` |
| ``sources\TerraFX.Runtime\Animation\RootMotionSourceAuthor.cs`` | ?? | `DA0235FA03B46A38BA656DA29C7F5CFFAB1B408BC8C18F362F0DBA8B2FA04F09` |
| ``sources\TerraFX.Runtime\Animation\RootMotionRequest.cs`` | ?? | `8DA6B2AAD66A152D5B47FBC913A90FFC2EA0AABFCF4CC9DD6A3CA5E442B4EDAD` |
| ``sources\TerraFX.Runtime\Animation\RootMotionSource.cs`` | ?? | `32B4E69A155275635CB2C68CC2CBA2387E4EE49DB2DAD2A8ED69D8E7FE46BB36` |
| ``sources\TerraFX.Runtime\Animation\AnimationSchemaCatalogue.cs`` | M | `D00CA5C66CEE9DB8FF01C2454AE2B64D7B4968108D86C492A33885924A509F16` |
| ``sources\TerraFX.Runtime\Hosting\WorldLoadPreparedState.cs`` | C | `0AAFE467247DCECFF3EDCFD30F36A19DA8070CEBA7FCF83B8B9ACB44B7B59A28` |
| ``sources\TerraFX.Core\EbinCodec.cs`` | C | `65D47557EB96FD79831BF67F3198E46C6AE82567788EE92F13E0870BD211AB00` |
| ``sources\TerraFX.Core\EbinPrimitives.cs`` | C | `97FC31FC2C0B7A6893AF8B8443283F3FABB1AF6620A3AF439E14FD6CB83F0DA4` |
| ``sources\TerraFX.Core\EbinStructuralWriter.cs`` | C | `F06E3598896C9E012D27893FD24BA9B3BC7507EB4546189921D69C0D9492937A` |
| ``sources\TerraFX.Core\EbinPayloadWriter.RecordArrays.cs`` | C | `3EE93DDE659B6E5C95E69F95D6A2B4C6B1179A19BA903B5CF1EAAAE246A82AB9` |
| ``sources\TerraFX.Core\EbinScanState.cs`` | C | `19B4B605FD018B7E5650CD96962211C6B7D41BDF93A2D036043F5D9570D3C214` |
| ``sources\TerraFX.Core\EbinStructuralReader.cs`` | C | `43B455F4A056A6DF2BEE31E52E0ECACF7C128BB33A8CB6A662386E8071575E1C` |
| ``sources\TerraFX.Runtime\Entities\WorldPersistenceEbinLimits.cs`` | C | `631EF23596EE3F63AD11B706F3B7BC0AE5C847320DB13CEAE376D4A8B694F14E` |
| ``sources\TerraFX.Runtime\Entities\WorldPersistenceStateRowWriter.cs`` | C | `3F03326813CC5FB151B2D760DE928BA627557DC1E61368DC239DF1450E8BD91F` |
| ``sources\TerraFX.Runtime\Entities\WorldPersistenceEcsWriter.cs`` | C | `9A44BE27ABDF85F745383AAE21322D26177714AE0CC9EC2ED0F4DD6646C22296` |
| ``sources\TerraFX.Runtime\Entities\WorldPersistenceWorldRecordWriter.cs`` | C | `348D4F30F5FDF37CBE3CE533206E4887465BD005256D3CFC74E2606B6196D796` |
| ``sources\TerraFX.Runtime\Entities\WorldPersistenceEcsCaptureCoordinator.cs`` | C | `A061C5B17A61DBC30609ED064561CB6E1255C4FA509FF1FC94308A49C44A82DF` |
| ``sources\TerraFX.Runtime\Entities\WorldPersistenceCaptureLease.cs`` | C | `B2B08CBAC2BF4F9E3EF549B13DDB9E4BA430927559C1D59F5F685C457BE2DEEE` |
| ``sources\TerraFX.Runtime\Animation\AnimationRuntimeStateCodec.cs`` | ?? | `BE23AC29E520F8CE493FC166DD5238163A26537B130D7E5A7C888B19283A962F` |
