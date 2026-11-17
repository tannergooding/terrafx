# M05 Animation aggregate attachment closure

## 1. Status and concrete gap

**Tentative implementation recommendation, pending owner review; not a maintained contract amendment, implementation result, or qualification acceptance.** Close the typed binding and reference-validation path for the already-proposed `animation/world-state` attachment. Preserve the [rectification](m05-owner-contract-closure-rectification.md), [Seek semantics](m05-owner-contract-closure-rectification-addendum.md), [runtime ports](m05-owner-contract-closure-runtime-ports.md), and [typed restore-source addendum](m05-owner-contract-closure-runtime-ports-addendum.md), except for the explicit Structural preparation-point correction below.

The existing proposals close **resource** record bindings and **attachment source ownership**, but not the aggregate attachment factory needed to produce that source:

Source files below are under `sources\TerraFX.Runtime\Entities`; the existing candidate-construction ordering is in `sources\TerraFX.Runtime\Hosting\WorldLoadPreparedState.cs:135–181`.

| Observed implementation | Consequence |
|---|---|
| `WorldAttachmentFactoryBinding<T,TAuthor>.CopyFieldDescriptors`, lines 134–181, descends required Records but rejects FixedArray/RecordArray and optional Records. | `animation/world-state` tag 2 Players cannot be registered, even when its actual count is zero. Its nested Layers/Clips, optional Transition, Fraction Bytes, and SourceWeights also exceed the selected field family. |
| `WorldAttachmentFieldBinding<T,TAuthor>`, lines 46–104, exposes only required nonreference fixed values and optional runtime-Entity/symbolic-Guid references. | Required player Entity references, SceneInstance references, and the controller AssetReference are not covered by an aggregate typed field family. |
| `WorldAssetReference` has no C# declaration in the inspected source tree; symbol lookup also finds none. | Runtime-port section 4.1 declares this value tentatively; neither the resource authors nor this attachment family can assume it is implemented or substitute an AssetId-only field. |
| `WorldPersistenceEcsReader.ValidateAttachmentEntityReferences`, lines 1102–1143, traverses flat descriptor paths and rejects required Entity-reference fields. | Adding only a RecordArray getter would not complete target-registration validation. `ComponentPropertySegmentKind` has no RecordArray occurrence segment. |
| `WorldPersistenceEcsReader.ValidateSchemaRecord/ValidateSchemaReferenceField`, lines 1543–1705, already recursively visits actual RecordArray occurrences and validates required/optional live Entity and SceneInstance references. | Reuse this real source validator; do not introduce a second Animation wire parser. |
| `World.Commands.cs:1145–1148` checks surviving attachments against the complete destruction projection before commit. | The runtime-port proposal's **after-playback** membership callback is too late to remove the engine-owned player's live references for that preflight. |

Chapter 30's attachment contract explicitly limits its current producer to scalar/optional-Entity fields (`30-public-api-review-surface.md:2727–2737`). Runtime-port section 5.3 is resource-specific and requires unmanaged partitions. The restore-source addendum section 2 retains a genuinely decoded attachment author; it does not supply these missing attachment bindings. This is a prerequisite to that existing route, not a replacement for its leases, one-Read rule, asset preparation, or post-restore join.

**Distinguishing input:** the proposed literal state `(NextCommandSequence=1, Players=[])` is valid but the current factory rejects its required kind-24 field before any Read can run. A nonempty state additionally needs every nested Entity reference checked, not just the first player or a flattened outer field.

## 2. Ownership and unchanged schema

Entities owns the captured typed record tree, registration checks, invocation/reference contexts, and installation. Animation owns immutable playback data, its concrete readers, and the existing Read/Restore/Validate/Count/Write/Release provider. Hosting uses the already-proposed typed source cache and exact retained package. No lower signature mentions Animation, Scenes, Assets, or Hosting types.

Keep all rectification section 2.3 tuples, tags, roles, cardinalities, ordering, and semantic predicates unchanged: the six `animation/*` state/helper records remain 1.0; nested `scene/local-transform` and `native/asset-reference` remain 1.0. No new wire record, public field-binding API, property-path segment, or migration is introduced.

Select one World-scoped transferred family with runtime type `AnimationRuntimeState`, author `AnimationRuntimeAuthor`, and factory identity `S("attachment/animation/world-state/factory/v1")`, version 1. Here `S` is the rectification's exact Chapter 10 name-UUID assignment under namespace `2ed8b750-e1a0-4b46-9000-000000000001`. Only the proposed built-in Animation support token constructs this internal aggregate binding; the ordinary public attachment factory remains unchanged.

## 3. Exact internal typed-binding seam

Add these Entities-owned declaration families. Constructors are private; omitted bodies contain only captured dispatch/bookkeeping:

```csharp
internal sealed class WorldAttachmentRecordBinding<T, TAuthor>
    where T : class where TAuthor : notnull
{
    private WorldAttachmentRecordBinding();
    internal static WorldAttachmentRecordBinding<T, TAuthor> Create(
        CompiledContentSchemaRegistry schemas, ContentSchemaReference schema,
        ImmutableArray<WorldAttachmentRecordFieldBinding<T, TAuthor>> fields);
    internal ContentSchemaReference Schema { get; }
    internal void ValidateAuthor(in TAuthor author);
    internal void ValidateRuntime(T value,
        IWorldAttachmentValidationContext context);
}

internal sealed class WorldAttachmentRecordFieldBinding<T, TAuthor>
    where T : class where TAuthor : notnull
{
    private WorldAttachmentRecordFieldBinding();
    // Exactly the internal static factories specified in the table below.
    internal void ValidateAuthor(in TAuthor author);
    internal void ValidateRuntime(T value,
        IWorldAttachmentValidationContext context);
}

// Added internal member on the existing WorldAttachmentFactoryBinding<T,TAuthor>:
internal static WorldAttachmentFactoryBinding<T, TAuthor> CreateAggregate(
    Guid factoryId, int factoryVersion, CompiledContentSchemaRegistry schemas,
    ContentSchemaReference schema, WorldAttachmentRecordBinding<T, TAuthor> root,
    IWorldAttachmentStateProvider<T, TAuthor> provider);

// Added internal dispatch members on that same existing factory binding:
internal void ValidateAuthor(in TAuthor author);
internal void ValidateRuntime(T value, IWorldAttachmentValidationContext context);

// Added internal getter on the existing WorldAttachmentFactoryDescriptor:
internal bool IsAggregate { get; }

// Added member on the existing internal IWorldAttachmentValidationContext:
void ValidateEntityId(Guid entityId);
```

Each factory below is an `internal static` member of `WorldAttachmentRecordFieldBinding<T,TAuthor>` returning that same closed field-binding type. Every `R` and `A` is a concrete struct reader, captured by value; their exact constraints are the listed interfaces. `V : unmanaged`; `C : class`; `CA : notnull`. The existing reader members remain `Get(in owner)`, `IsPresent/GetPresent(in owner)`, and `GetCount/GetElement(in owner,index)`.

| Complete factory signature | Runtime / author reader constraints |
|---|---|
| `Value<V,R,A>(ContentFieldDescriptor field, ContentReferenceKind reference, R runtime, A author)` | `IComponentValueReader<T,V>` / `IComponentValueReader<TAuthor,V>` |
| `Optional<V,R,A>(ContentFieldDescriptor field, ContentReferenceKind reference, R runtime, A author)` | `IComponentOptionalReader<T,V>` / `IComponentOptionalReader<TAuthor,V>` |
| `Bytes<R,A>(ContentFieldDescriptor field, R runtime, A author)` | `IComponentArrayReader<T,byte>` / `IComponentArrayReader<TAuthor,byte>` |
| `FixedValues<V,R,A>(ContentFieldDescriptor field, R runtime, A author)` | `IComponentArrayReader<T,V>` / `IComponentArrayReader<TAuthor,V>` |
| `Record<C,CA,R,A>(ContentFieldDescriptor field, WorldAttachmentRecordBinding<C,CA> child, R runtime, A author)` | `IComponentValueReader<T,C>` / `IComponentValueReader<TAuthor,CA>` |
| `OptionalRecord<C,CA,R,A>(ContentFieldDescriptor field, WorldAttachmentRecordBinding<C,CA> child, R runtime, A author)` | `IComponentOptionalReader<T,C>` / `IComponentOptionalReader<TAuthor,CA>` |
| `Records<C,CA,R,A>(ContentFieldDescriptor field, WorldAttachmentRecordBinding<C,CA> child, R runtime, A author)` | `IComponentArrayReader<T,C>` / `IComponentArrayReader<TAuthor,CA>` |
| `AssetReference<R,A>(ContentFieldDescriptor field, Guid targetSchemaId, ImmutableArray<int> targetMajors, R runtime, A author)` | `IComponentValueReader<T,WorldAssetReference>` / `IComponentValueReader<TAuthor,WorldAssetReference>` |

### Shared value prerequisite: proposed, not implemented

Implement the **existing** runtime-port section 4.1 declaration once, in Entities, before compiling either the root-motion author/provider family or this aggregate family. This is a missing concrete implementation prerequisite, not a new schema decision:

| Aspect | Exact existing proposed shape |
|---|---|
| Type and storage | Internal readonly `WorldAssetReference`, `[StructLayout(LayoutKind.Sequential, Pack=1)]`; two private signed-32-bit fields, `_assetId` then `_schemaMajor`, eight bytes total. Internal constructor `(int assetId, int schemaMajor)` requires both positive. No truncation to ushort. |
| Members/default | Internal get-only `int AssetId`, `int SchemaMajor`, `bool IsValid`. Default `(0,0)` is invalid, not absence; only IsValid may be queried on it. Body getters reject invalid values. Required fields cannot use zero as a null sentinel. |
| Version binding | SchemaId is fixed by the captured field, not omitted semantically or guessed from AssetId. Preserve the selected major in the pair and canonical `native/asset-reference` 1.0 wrapper. That wrapper has AssetId/SchemaId/Major but no Minor; the selected profile separately requires minor zero from the exact retained package. This attachment's controller and root-motion controller/Clip fields select major 1; v2 Mesh/Material policies remain separately owned. |
| Ownership | The value carries no package reference, lease, World authority, or disposal obligation. Copying it retains **no** asset. The actual prepared/live/request companion owner independently retains the exact package-backed asset leases through their real uses, including historical-only requests; an equal pair from a replacement package is not the same binding. |

`OptionalWorldAssetReference` is likewise only proposed: a packed presence byte plus this eight-byte pair, absent default all-zero, present constructor requiring a valid pair, IsPresent before Value, and zeroed absent payload. It is the existing v2 optional-field prerequisite, **not** an optional wrapper required by this attachment's mandatory Controller field. Do not silently widen this slice to implement those v2 consumers.

Construction and traversal rules:

- CreateAggregate preserves the existing nonempty factory ID, positive version, exact Content AttachmentState role, and nonnull provider checks. Its root must match the exact schema and registry by captured identity. Aggregate validation calls the captured tree through the factory's new internal dispatch members; all legacy runtime-field loops retain their existing behavior. Do not route an aggregate through an empty legacy Fields traversal.
- `Create` captures the exact registry, schema, fields, child families, and readers once. Every direct metadata field occurs exactly once in ascending tag order, including optional fields. Reject foreign descriptor objects/registries, missing/duplicate fields, null/default collections, unresolved or multiple nested alternatives, recursive closure, and depth beyond the existing 64-field-path limit. The only selected closure is the section 4 table plus the asset-reference helper; nesting `scene/local-transform` does not register another ECS component.
- `Value`/`Optional` require matching required/optional fixed kind and the **explicit exact** reference annotation. The selected primitive kinds/types are byte, sbyte, ushort, uint, long, bool, float, Guid, Vector3, Quaternion, and SpatialPosition3. Reference is None, or Entity/SceneInstance only for Guid; opaque Guid never gains reference meaning. SceneInstance is used only by the required player field.
- Bytes requires required kind 13 and byte elements. FixedValues requires required kind 23 with the matching permanent fixed element kind; this selection uses float only. Records requires required kind 24; Record/OptionalRecord require kind 22 with matching presence and one exact child tuple. These collection/ordinary-record factories require Reference=None.
- AssetReference requires a required kind-22 Asset field, exact nested `native/asset-reference` 1.0 metadata with all three fields, nonempty fixed target SchemaId, and initialized ascending unique positive majors. Its typed value retains both AssetId and major. This selection uses only controller major 1; the existing package/profile preflight verifies minor zero.
- Optional presence is tested before a value getter. Array count is checked against the exact descriptor before element access; every actual element is visited once in index order, including zero-weight clips. Negative/out-of-range counts, null child values, default immutable arrays, or inconsistent optional arms fail. There is no padding, absent-field default repair, sorting, normalization, or service lookup in a reader.
- Aggregate `Descriptor.Fields` contains the root's two direct descriptors in tag order; `IsAggregate=true` and the complete typed child tree are captured by the same factory. Legacy `Create` retains its existing flattened inventory and `IsAggregate=false`. No empty field list stands in for the tree. Nested nodes use their own direct Core descriptors, never invented RecordArray `ComponentFieldPath` indices.
- Registration dispatch validates the typed author after a successful issuer/extent/diagnostic check and **before** issuing its prepared source. It traverses the same captured tree for runtime validation/reference checks. A provider returning an inconsistent author or a reader violating its captured shape is a protocol failure, not a successful empty state. The concrete codec still owns canonical Read/Count/Write; the tree is not another wire decoder.

## 4. Complete Animation data and field membership

Use one deeply immutable symbolic graph for canonical data. `AnimationRuntimeState` wraps `AnimationRuntimeAuthor Value`; its runtime field readers read through Value. Below the root, runtime and author readers use the **same immutable node types**, not Entity handles or mutable player objects. This avoids requiring a handle-producing Restore capability absent from the existing phase grants.

Each table node is an internal sealed class with one constructor taking its listed fields in tag order and get-only properties with the listed names. Arrays are initialized `ImmutableArray<T>` with nonnull elements; optional scalars use nullable value types and optional Transition is a nullable class. No constructor, setter, or installation API is public. Constructors validate shape; the provider and existing joins additionally enforce the rectification/Seek semantic predicates.

`V`/`O` denote Value/Optional with Reference=None unless annotated; `R`/`OR`/`RA` denote Record/OptionalRecord/Records. The independently enumerated set is **41 direct fields across seven typed nodes**, plus the asset wrapper's three existing fields:

| Exact schema 1.0 / CLR node | Complete ordered field bindings and CLR data |
|---|---|
| `animation/world-state` / `AnimationRuntimeAuthor` | 1 NextCommandSequence `V<long>`; 2 Players `RA<AnimationPlayerStateAuthor>` [0,4096]. |
| `animation/player-state` / `AnimationPlayerStateAuthor` | 1 ActorEntityId `V<Guid,Entity>`; 2 SceneInstanceId `V<Guid,SceneInstance>`; 3 Controller `AssetReference<WorldAssetReference>` to controller major1; 4 Speed `V<float>`; 5 NextPlaySequence `V<long>`; 6 LastSampleTick `O<long>`; 7 Bindings `RA<AnimationTargetBindingAuthor>` [0,4352]; 8 Layers `RA<AnimationLayerStateAuthor>` [1,8]; 9 MotionActorId `O<Guid,Entity>`. |
| `animation/target-binding` / `AnimationTargetBindingAuthor` | 1 Domain `V<byte>`; 2 SourceKey `V<Guid>`; 3 EntityId `O<Guid,Entity>`; 4 Baseline `R<AnimationLocalPoseAuthor>`; 5 RootBaseline `O<SpatialPosition3>`. |
| `animation/layer-state` / `AnimationLayerStateAuthor` | 1 Index `V<byte>`; 2 Weight `V<float>`; 3 Clips `RA<AnimationActiveClipAuthor>` [0,4]; 4 Transition `OR<AnimationTransitionStateAuthor>`. |
| `animation/active-clip` / `AnimationActiveClipAuthor` | 1 ClipSlotId `V<Guid>`; 2 PlaySequence `V<long>`; 3 LocalTicks `V<long>`; 4 PreviousTicks `V<long>`; 5 Fraction `Bytes<byte>` [24,24]; 6 LoopOrdinal `V<long>`; 7 PreviousLoopOrdinal `V<long>`; 8 Weight `V<float>`; 9 ForwardCursor `V<uint>`; 10 ReverseCursor `V<uint>`; 11 StartDelivered `V<bool>`; 12 Completed `V<bool>`; 13 LastDirection `V<sbyte>`. |
| `animation/transition-state` / `AnimationTransitionStateAuthor` | 1 TransitionId `V<Guid>`; 2 ElapsedTicks `V<long>`; 3 Fraction `V<ushort>`; 4 SourceWeights `FixedValues<float>` [1,4]; 5 TargetClipSlotId `V<Guid>`. |
| `scene/local-transform` / `AnimationLocalPoseAuthor` | 1 Position `V<Vector3>`; 2 Rotation `V<Quaternion>`; 3 Scale `V<Vector3>`. |

The four RecordArray edges, one optional Record, one Bytes field, one FixedArray, three Entity-reference fields, and one SceneInstance-reference field are all explicit. Scalar ranges, conditional presence, source ordering, canonical geometry, 192-bit fraction interpretation, cursor limits, layer membership, and transition correspondence remain exactly with the existing owner proposals. Preserve signed-zero and fractional bits; no resampling repairs invalid state.

`AnimationRuntimeStateProvider` implements the existing complete `IWorldAttachmentStateProvider<AnimationRuntimeState,AnimationRuntimeAuthor>` interface. Its internal constructor takes the nonnull compiled registry and captures one `Binding` of type `WorldAttachmentFactoryBinding<AnimationRuntimeState,AnimationRuntimeAuthor>` using CreateAggregate, the assigned factory identity/version, and this whole tree. The support token captures that binding once. There is no new attachment Defaults callback: actual empty World construction explicitly installs the complete state `(1, initialized-empty Players)`, as part of the selected participant's admitted construction.

## 5. Reference, restore, and installation sequencing

1. **Freeze before construction.** Compile the selected descriptors, construct the complete aggregate/provider, then stage it through the already-proposed Animation support registration. A failed tree/provider registration leaves the receiving builder unchanged. Keep CPU support's existing component-only rule.
2. **Validate actual encoded occurrences before candidate issuance.** For `IsAggregate`, replace the flat optional-Entity-only attachment target check with the existing recursive `ValidateSchemaRecord` route using the captured target registry and the same complete source identity/depth context. Thread that context through the actual owner/row validator; all target-validation callers must finish this pass before a ready construction plan. Preserve ordinary schema/scope/owner checks and the legacy branch. Do not silently skip nested references or pretend the root descriptor list contains them.
3. **Use the existing typed-source route.** Under the real prepared-load source scope, invoke this registered Read once, validate its complete immutable author/tree, and cache `WorldAttachmentSource<AnimationRuntimeAuthor>`. The existing addendum supplies acquisition, retained package preparation, cancellation, and source/family checks. Its genuine empty-player case now has a constructible provider; no fabricated author is allowed.
4. **Restore without handle invention.** After the complete candidate Entity map exists, the captured provider restores this cached symbolic graph into the private runtime wrapper. It performs no second Read, component query, Scene lookup, asset load, or hidden tick. A shared author subgraph requires an actual retained/transferred storage credit; otherwise copy into separately admitted storage. Source-wrapper disposal cannot refund graph storage still installed in the candidate.
5. **Validate live references through real scopes.** The new internal ValidateEntityId checks invocation/thread first, resolves the exact live stable ID through the protected World's identity/slot map, and applies the same projected-membership and exclusion checks as existing ValidateEntity. It returns no Entity handle or authority. Required and present optional Entity leaves both use it. Opaque GUIDs do not. SceneInstance shape is checked here, source membership by step 2, and the exact receiving Scene map/provenance by the already-proposed Animation join after Scene restoration; do not require a completed Scene map during the earlier attachment Restore.
6. **Correct the membership preparation point.** For this engine-owned aggregate, issue the existing AnimationMembership grant on the **complete Structural shadow projection, before attachment-reference preflight and storage commit**, rather than after playback. In this grant, component counts/rows describe projected player membership, including prepared additions; Scene reads/writes and resource/control/event/result mutation are forbidden. PrepareMembership stages the complete replacement attachment and derived ownership using already-prepared content, never I/O. Validate that replacement together with ordinary projected references, reserve its full peak, then install it atomically with ECS/Scene changes without callbacks. Failure preserves the old structural storage/attachment; unchanged surviving references to destroyed targets still reject. Run ordinary post-Structural validation again. The stage-2 historical root-request array/pins are not pruned. No second membership callback or blanket exemption for World attachments is added.
7. **Finish the existing restore join.** Once Scene/hierarchy/clock restoration is complete, validate player/component/Scene/controller/target bijections and build derived roots without changing canonical data. Fresh Validate/Count/Write must reproduce the validated target projection before coherent cohort preparation and the restored marker. No event replay, desired-motion consumption, or canonical repair occurs.

Count/Write visit every actual node with the frozen presence/order and canonical EBIN encoders, check complete destination/overflow rules, and emit the existing fields only. Read copies source data into owned immutable storage and returns existing Rejected diagnostics for malformed input. Active provider/reader/family/scope misuse faults the actual operation and remains sticky even if caught; revoked use only throws and cannot fault a successor. Revoke all real bindings in `finally`. Release retains the existing exactly-once **installation** ownership semantics; sharing an immutable node does not share release authority or permit destructive disposal of that node.

All tree metadata, author copies, active/current/prepared/retained graphs, serialization/traversal workspaces, source interests, and cleanup ownership fit the existing load/Animation/World/domain envelopes **before allocation**. The 128-MiB Animation ceiling and count maxima are unchanged and promise no joint fit. This proposal establishes no allocation-size or lifetime qualification result.

## 6. First implementation boundary and evidence

First implement the existing copied reference value and its exact field/version checks; then implement this aggregate family and its real registration/validation/production Standard Branch route before treating the restore-source addendum's attachment cache as available. Deliver it with the affected membership transaction, not an array helper left disconnected from validation.

Required focused evidence, not executed results:

- Through the genuine selected World/Save/PrepareLoad/CommitLoad path, empty Players is an actual registered Read/Restore roundtrip. Observe one Read before candidate construction and one Restore using that cached author, followed by unchanged canonical bytes.
- Preserve the distinct copied pairs `(37,1)` and `(37,2)`; the controller field admits major1 and rejects major2 rather than collapsing both to AssetId 37. Reject an invalid default pair, wrong fixed SchemaId, and unsupported actual package minor. Copies do not create or release asset leases.
- A small native-authored Scene supplies two actual players; only the second contains a nested optional MotionActor/target reference. A missing/retired target rejects before candidate issuance. Required ActorEntityId is checked too; an opaque SourceKey equal to a retired ID remains opaque.
- Exercise a mid-transition graph with present/absent Transition, empty active clips, exact 24-byte fractions, SourceWeights, and optional root baselines. Literal bytes distinguish absence, initialized-empty arrays, defaults, and preserved signed-zero/fraction bits. Missing/duplicate/foreign field bindings and 23/25-byte fractions reject.
- Structural removal of a player stages its new aggregate before reference preflight; failure leaves the old structural storage/attachment. A retained surviving target reference still blocks destruction. Confirm this tick's historical root request survives player removal and restores through the existing request-only asset route.
- Verify real source/candidate scope revocation, provider-author shape failure, old scalar attachment behavior, source/shared-storage lifetime, and rejected registration without partial builder mutation. Use ordinary owning-project Debug then Release tests when implemented, with nonzero executed selections.

**Excluded:** new controllers/timeline algorithms, root-resource redesign, another asset parser or restore lease, v2 reference-policy changes, particle/LOD/picking work, full animated extraction, imported-animation execution, RecordedContinuation, Physics feedback, M03-B retained-operation-root/allocation closure, diagnostic budget closure, and native/AOT/milestone acceptance. Those existing producer and observation gates remain separate.
