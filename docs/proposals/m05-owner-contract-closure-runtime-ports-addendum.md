# M05 typed restore-source and asset-reference joins

## 1. Scope and disposition

**Tentative proposal, not implementation or acceptance.** This narrowly supplements [the private runtime-port proposal](m05-owner-contract-closure-runtime-ports.md), SHA-256 `605CF1D92DC362A8B73EA9BDA3A5467757942C268712F484AF53D9A9159D53E3`. It replaces that proposal's raw-input Animation asset-preparation handoff with source-owned ResourceState and World AttachmentState reads, and closes the adjacent versioned component-reference persistence edge. The existing proposal is not edited.

One factual clarification: `validatedEcsRecord` is not intrinsically component-only. Current `state/ecs` includes ResourceState rows. The missing declaration is a **typed, owned, issuer-checked handoff from the registered ResourceState reader**, not another wire envelope. Animation must not add its own resource parser to discover the Controller/Clip references. Read the selected resource once before candidate construction; use its immutable author for both asset preparation and candidate Restore.

Preserve the parent's request-slot tag 13 Controller, complete request fields, historical identity rules, all capacities/byte ceilings, old v1 behavior, and the rectification/addendum's Seek and particle identities. No new schema, acceptance ID, blanket M03/M04 dependency, feedback execution, or imported-animation execution is selected. M06/M07 feedback and other existing producer/qualification gates remain.

### Verified evidence and limits

| Observed fact | Exact reviewed seam |
|---|---|
| ResourceState payloads are inside ECS, selected by resource ID and exact registered schema. | `WorldPersistenceEcsReader.cs:1488–1539`, `GetRecordArrayElement(3,index)`; `World.PersistenceRestore.cs:805–845`. |
| Resource Read already returns a typed immutable author, verifies the result's issuer/complete consumption, and validates its captured author family. | `WorldResourceReadResult<TAuthor>`; `WorldResourceCodecDispatch<T,TAuthor>.Read`. |
| Current candidate resource restoration reads encoded payload again before calling Restore. There is no retained typed source handoff. | `WorldResourceRegistration<T>.RestoreStateRecord`; `WorldResourceStateCodecReadOutcome` retains only an erased author/result, not the proposed source ownership/provenance. |
| Attachment Read is already independent of a World: it issues a schema/extent-bound typed Read binding, checks the complete result and revokes it. Candidate restoration currently reads again, then separately restores the author. | `WorldAttachmentRegistration<T,TAuthor>.ReadStateRecord/RestoreState`; `IWorldAttachmentRegistration`; `World.PersistenceRestore.cs:929–1058`. This supports a pre-candidate typed handoff, not a claim that the proposed Animation aggregate provider is implemented. |
| Current candidate creation precedes Scene/ECS restoration; the prepared-load owner retains the selected checkpoint and package. | `WorldLoadPreparedState.CreateRestoredCandidate`, constructor and Dispose; `WorldRetainedHistoryControl.Attach/Dispose`. Thus request-only preparation must be completed before the existing `World.CreateRestoreCandidate` call, not hidden inside JoinRestored. |
| Persistence collection walks encoded records using compiled descriptors, not component accessor kinds. | `WorldPersistenceAssetIdSetWriter.CopySortedUniqueAssetIds/CopySortedUniqueAssetReferences`; `AssetReferenceScanner.VisitRecord/VisitNested/ReadReference`. |
| Save collects full reference tuples and checks the retained package. | `WorldPersistence.cs:858–919,405–446`; `AssetPackageLease.MatchesAssetReferenceAsync`. |
| The inspected PrepareLoad front end checks AssetId presence and delegates Scene closure. That alone does not establish a general v2 component major check. | `WorldPersistence.cs:1297–1316`. No uninspected Scene-validator body is treated as proof of the missing edge. |
| Package-specific loading pins the selected backing independently; the existing reference matcher checks SchemaId/Major and registered codec, but not an explicitly requested Minor. | `AssetPackageLease.LoadFromPackageAsync/MatchesAssetReferenceAsync`; `AssetStore.PackageLease.cs:12–92`. |

Chapter 27 owns source/candidate/credit/cancellation ordering; Chapter 29 owns dependency direction; Chapter 35 and the current roadmap keep actual M05 producers gated. The following declarations are recommendations, not claims that these APIs exist.

## 2. One lower-owned typed resource read

All types in this section are **internal `TerraFX.Entities`**. Bodies/private bookkeeping are omitted; member sets and ownership below are complete. No Entities signature mentions Animation, Assets, Scenes, or Hosting types.

```csharp
internal interface IWorldRestoreSourceOwner
{
    WorldRegistration Registration { get; }
    ValidatedWorldIdentityHistory IdentityHistory { get; }
    long CompletedTick { get; }
    EbinReadLimits Limits { get; }
    WorldRestoreSourceLease AcquireRead();
}

internal sealed class WorldRestoreSourceLease : IDisposable
{
    internal WorldRestoreSourceLease(IWorldRestoreSourceOwner owner,
        ReadOnlyMemory<byte> ecsRecord, ReadOnlyMemory<byte> scenesRecord,
        IDisposable sourceUse);
    internal ReadOnlySpan<byte> EcsRecord { get; }
    internal ReadOnlySpan<byte> ScenesRecord { get; }
    internal void EnterProviderRead();
    internal void ExitProviderRead();
    internal Exception RecordFailure(Exception exception);
    internal void ThrowIfFailed();
    public void Dispose();
}

internal interface IWorldPreparedResourceSource : IDisposable
{
    Guid ResourceId { get; }
    ContentSchemaReference Schema { get; }
    int PartitionCount { get; }
}

internal sealed class WorldResourceSource<TAuthor> : IWorldPreparedResourceSource
    where TAuthor : notnull
{
    internal WorldResourceSource(IWorldResourceRegistration registration,
        IWorldRestoreSourceOwner owner, TAuthor author, IDisposable ownership);
    public Guid ResourceId { get; }
    public ContentSchemaReference Schema { get; }
    public int PartitionCount { get; }
    internal TAuthor Author { get; }
    internal bool IsFrom(IWorldRestoreSourceOwner owner);
    internal WorldResourceSource<TAuthor> Retain();
    public void Dispose();
}

// Added internal member on WorldRegistration:
internal bool TryReadResourceSource<TAuthor>(Guid resourceId,
    IWorldRestoreSourceOwner owner,
    out WorldResourceSource<TAuthor>? source,
    out ImmutableArray<Diagnostic> diagnostics,
    CancellationToken cancellationToken) where TAuthor : notnull;

// Added members on IWorldResourceRegistration:
Type SourceAuthorType { get; }
bool TryReadSource(IWorldRestoreSourceOwner owner,
    out IWorldPreparedResourceSource? source,
    out ImmutableArray<Diagnostic> diagnostics,
    CancellationToken cancellationToken);
void RestorePreparedSource(IWorldPreparedResourceSource source, object storage,
    World candidate, long completedTick, CancellationToken cancellationToken);

// Added members on IWorldResourceCodecDispatch<T>, T : unmanaged:
Type SourceAuthorType { get; }
bool TryReadSource(IWorldResourceRegistration registration,
    IWorldRestoreSourceOwner owner, out IWorldPreparedResourceSource? source,
    out ImmutableArray<Diagnostic> diagnostics,
    CancellationToken cancellationToken);
void RestorePreparedSource(IWorldPreparedResourceSource source,
    WorldResourceOperationControl operation, T[] partitions,
    CancellationToken cancellationToken);
```

### Issuance, validation, and defaults

- The concrete source owner is the actual **`WorldLoadPreparedState`**, extended to implement `IWorldRestoreSourceOwner`. It is one-use and bound to its retained, validated Standard Branch target projection, complete identity history, selected frozen registration, decoded completed tick, and source/commit ownership. It borrows the retained checkpoint storage from its existing `WorldRetainedHistoryControl`. No caller/provider implementation, GUID lookup, live World, or synthetic read binding issues a source.
- AcquireRead books an actual source use before lending bytes. Only this owner constructs its lease, over its private engine-owned validated record storage. The constructor's memory arguments are not a new caller input route: buffers must belong to that same retained projection and have no mutable writer/alias after validation. Do not pass an external `byte[]`, expose `FileRecord`, or treat arbitrary ReadOnlyMemory as immutable ownership. The lease exposes spans only for synchronous reader/collector work; no span is retained or held across await. Dispose closes that use; the source remains charged through all other uses.
- Source provider dispatch uses EnterProviderRead/ExitProviderRead in `try/finally`, checks ThrowIfFailed after unwind, and preserves the first RecordFailure exception even if caught. This is the exact prepared source's nonreentrant reader scope, not World.StartAttachmentCallback on a fabricated/preconstructed World. It grants no World mutation/Restore authority; reentrant source acquisition/admission during that callback is a sticky source-operation failure. Resource and attachment reads use this same scope. Expired-source misuse cannot fault another load or the active World.
- Required references are nonnull; default/null wrappers are unissued. Registration/schema/author type must match the **captured factory and codec family**, not just equal descriptor values. SourceAuthorType is the captured `typeof(TAuthor)`, never discovery. Missing selected registration, wrong author family, foreign owner, duplicate decode, or caught scope misuse is protocol/usage failure, not a capacity rejection.
- TryReadResourceSource selects the exact existing resource registration from `owner.Registration`, requiring reference equality with the receiver. Its only M05 selection here is the parent's 4096-partition root-motion request resource. The existing ECS envelope validation establishes ID/schema/count/role before the registered Read callback. Resource-envelope selection may use the existing validated ECS traversal; **no new root-motion payload parser is introduced**.
- `WorldResourceCodecDispatch<T,TAuthor>` invokes its already captured `IWorldResourceStateCodec<T,TAuthor>.Read` exactly once for this resource occurrence, through the real issuer-bound Read binding and source-history reference context declared by the parent. It checks full consumption, initialized diagnostics, exact typed author/family, all slot/source bounds and creation facts. Source bytes never go to Animation.
- Success returns a nonnull `WorldResourceSource<TAuthor>`, initialized-empty diagnostics, and one complete immutable copied author. False returns null and initialized nonempty existing read/load diagnostics; malformed source remains Rejected before candidate issuance. Cancellation throws through the actual preparation operation; unexpected/protocol failures fault it. No partial author, success-shaped empty source, or arbitrary default is returned.
- Only that successful captured dispatch constructs WorldResourceSource. It records exact owner identity/generation, resource occurrence, registration/family controls and author ownership. No provider may construct one from an equal author. The owner caches this one result; later consumers use Retain, not another Read. The cache is per prepared load, never per WorldRegistration or process.
- Author is a borrow from that live lease and is deeply immutable, with initialized immutable arrays and no borrowed encoded buffers. No author borrow may outlive its lease. Retain acquires another accounted interest in the same graph, not another decode or uncharged clone. After disposal Author/Retain throw; copied ID/schema/count facts remain metadata only. Disposal is idempotent after actual release and cannot invalidate another lease.

### Remove the second resource parse

For this aggregate family, replace the parent's proposed raw-byte `ValidateSourceReferences` provider member with:

```csharp
// On IWorldResourceRecordCodec<T,TAuthor> and its concrete provider:
ImmutableArray<Diagnostic> ValidateSourceReferences(in TAuthor author,
    WorldResourceReferenceBinding binding, CancellationToken cancellationToken);
```

Read performs the sole owner-specific wire decode. This observational validation traverses the **same author**, using a fresh real source-history scope if Read's scope has already been revoked. It performs no byte parsing, asset I/O, canonical repair, or World issuance. Standard structural/descriptor/reference scanning is not a second Animation implementation. Check the context/failure ledger and revoke every scoped binding in `finally` before publishing the owned source handle.

The prepared-load state retains the resulting `WorldResourceSource<RootMotionResourceAuthor>` until its consumers end. Replace the parent's internal entry with:

```csharp
internal void RestoreEcsRows(ReadOnlyMemory<byte> ecsRecord,
    WorldPersistenceEcsContents contents, long completedTick,
    ImmutableArray<IWorldPreparedResourceSource> preparedSources,
    ImmutableArray<IWorldPreparedAttachmentSource> preparedAttachments,
    CancellationToken cancellationToken);
```

The preparedSources array must be initialized. Its exact selected set is one root-motion source when Animation support is selected, otherwise empty; no arbitrary extra source is admitted.

At that resource row, `WorldResourceRegistration<T>.RestorePreparedSource` checks the candidate's exact frozen registration/family and prepared-load source binding, schema/count, actual partition array, and validated completed tick. Typed codec dispatch casts only to its own `WorldResourceSource<TAuthor>` and invokes the **existing captured Restore** with that author and a fresh candidate reference/partition binding. It must not call Read or the raw RestoreStateRecord path again. All 4096 SetPartition obligations and post-Restore validation remain. Other resource families retain their existing path.

Bind the exact source-owner identity into the lower construction plan before the plan becomes available for CommitLoad, using a new lower-only `WorldConstructionPlanControl.BindPreparedResourceSourceOwner(IWorldRestoreSourceOwner owner)`. It is single-use, validates the already selected registration/history/tick, and rejects rebinding. Candidate restoration compares that captured owner/family, not WorldGuid or hashes alone. An equal-looking source from another preparation cannot substitute. This adds neither lower knowledge of Hosting nor a second domain/owner issuer.

### The matching typed World AttachmentState read

The same source owner must also issue and retain the actual `animation/world-state` author; an unqualified `AnimationRuntimeAuthor` parameter is not its provenance. Add these **Entities-owned internal** declarations:

```csharp
internal interface IWorldPreparedAttachmentSource : IDisposable
{
    AttachmentScope Scope { get; }
    ContentSchemaReference Schema { get; }
    object GetAuthor(IWorldAttachmentRegistration registration,
        IWorldRestoreSourceOwner owner);
}

internal sealed class WorldAttachmentSource<TAuthor> : IWorldPreparedAttachmentSource
    where TAuthor : notnull
{
    internal WorldAttachmentSource(IWorldAttachmentRegistration registration,
        IWorldRestoreSourceOwner owner, TAuthor author, IDisposable ownership);
    public AttachmentScope Scope { get; }
    public ContentSchemaReference Schema { get; }
    internal TAuthor Author { get; }
    internal bool IsFrom(IWorldRestoreSourceOwner owner);
    internal WorldAttachmentSource<TAuthor> Retain();
    object IWorldPreparedAttachmentSource.GetAuthor(
        IWorldAttachmentRegistration registration, IWorldRestoreSourceOwner owner);
    public void Dispose();
}

// Added internal member on WorldRegistration:
internal bool TryReadWorldAttachmentSource<TAuthor>(ContentSchemaReference schema,
    IWorldRestoreSourceOwner owner,
    out WorldAttachmentSource<TAuthor>? source,
    out ImmutableArray<Diagnostic> diagnostics,
    CancellationToken cancellationToken) where TAuthor : notnull;

// Added members on IWorldAttachmentRegistration:
Type SourceAuthorType { get; }
bool TryReadWorldSource(IWorldRestoreSourceOwner owner,
    out IWorldPreparedAttachmentSource? source,
    out ImmutableArray<Diagnostic> diagnostics,
    CancellationToken cancellationToken);
```

For this selection, the schema is exactly `S("animation/world-state")` 1.0 Content, scope World, with no owner EntityId. SourceAuthorType is the captured `typeof(TAuthor)` on `WorldAttachmentRegistration<T,TAuthor>`. TryReadWorldAttachmentSource requires the same registration/source-owner reference binding as the resource path and selects exactly one validated World attachment occurrence from ECS tag 4, payload tag 3. Missing/duplicate rows, entity-scoped substitution, a wrong tuple, or a foreign family cannot produce an author.

The concrete captured registration factors its existing ReadStateRecord dispatch: create the real `WorldAttachmentReadBinding<TAuthor>(Descriptor.Schema, payloadLength)`, call the **same frozen `_binding.Provider.Read` once**, run its existing ValidateComplete/issuer/full-consumption/diagnostic/typed-author checks, check sticky failures, and revoke in `finally`. Preserve rejected-result diagnostics rather than parsing exception messages. Do not invoke Defaults, Restore, a live component read, or another Animation parser to manufacture the author before candidate construction.

Only successful dispatch constructs `WorldAttachmentSource<TAuthor>`, capturing the exact registration/binding control, source-owner generation and occurrence, and accounted immutable author ownership. Success/false/diagnostic/null/cancellation behavior matches TryReadResourceSource. Each World attachment is decoded once into this source's cache. Later acquisitions call Retain; duplicate decode attempts reject. The metadata is copied, but Author/GetAuthor/Retain require a live lease and exact family/source ownership; null/default/foreign operands reject. GetAuthor's erased return is lower Restore dispatch only, not a service lookup or mutable application value.

WorldLoadPreparedState caches `WorldAttachmentSource<AnimationRuntimeAuthor>` alongside the request source **before asset preparation**. Even an empty Players array comes from that genuine Read. AcquireAnimationAssets retains both caches under the same source generation; it cannot assemble unrelated authors or default an unavailable one.

The new preparedAttachments argument on RestoreEcsRows is initialized and contains exactly this one World attachment source when Animation is selected, otherwise empty. In RestoreTransferredAttachmentStateRecords, this row uses `prepared.GetAuthor(registration, capturedSourceOwner)` instead of RunAttachmentStateRead. It then uses the existing **RunAttachmentStateRestore → registration.RestoreState → `_binding.Provider.Restore`** with the candidate's real WorldAttachmentRestoreBinding. Preserve its `try/finally` revocation/sticky checks, transferred installation, validation and release family. Restore executes once; Read does not execute a second time. Other World/Entity attachments keep their existing route.

Both typed caches stay charged through preparation and candidate Restore/fresh validation. A restored immutable runtime attachment may share an author subgraph only with an explicit retained/transferred storage credit to the candidate; dropping the source must not refund bytes still rooted by that attachment. Before runtime construction, cleanup releases symbolic source storage, never calls the runtime attachment Release on an author. Once Restore creates the runtime value, its existing registered release family owns cleanup.

**Remaining producer gate:** the inspected source establishes a World-independent typed Read/author/Restore seam, but does not implement or qualify the proposed Animation aggregate reader, deep immutable author, reference joins, or this retained source cache. Until the actual registered `animation/world-state` Read supplies the complete author under these rules, combined Animation asset preparation/restoration is blocked. Neither the resource source alone nor an empty/default playback author closes that gate.

## 3. Typed Animation asset-preparation input

The concrete higher owner is again WorldLoadPreparedState, implementing this **`TerraFX.Animation`** interface in addition to the lower source interface. Hosting may compose these owners; Entities never references this interface or Assets.

```csharp
internal interface IAnimationRestoreAssetSourceOwner : IWorldRestoreSourceOwner
{
    AnimationRestoreAssetSourceLease AcquireAnimationAssets();
}

internal sealed class AnimationRestoreAssetSourceLease : IDisposable
{
    internal AnimationRestoreAssetSourceLease(
        IAnimationRestoreAssetSourceOwner owner,
        WorldAttachmentSource<AnimationRuntimeAuthor> playbackState,
        WorldResourceSource<RootMotionResourceAuthor> rootMotionRequests,
        AssetPackageLease packageLease, IDisposable sourceUse);
    internal WorldAttachmentSource<AnimationRuntimeAuthor> PlaybackState { get; }
    internal WorldResourceSource<RootMotionResourceAuthor> RootMotionRequests { get; }
    internal AssetPackageLease PackageLease { get; }
    public void Dispose();
}

// Replaces only the parent's AnimationPreparedRestoreAssets.PrepareAsync:
internal static Task<AnimationPreparedRestoreAssets> PrepareAsync(
    IAnimationRestoreAssetSourceOwner sourceOwner,
    CancellationToken cancellationToken);
```

Only the actual prepared-load owner constructs the lease: PlaybackState and RootMotionRequests are its retained typed attachment/resource sources from section 2, both satisfying IsFrom(this); PackageLease is the **same actual package lease** already selected and retained by this preparation. Equal AssetIds/package digest/WorldGuid do not establish this relationship. The lease owns both retained source wrappers and borrows their immutable authors under its independently acquired actual source use; it accepts no caller-created author graph or buffer.

The active-state author reader is the existing proposed Animation attachment family, not a new parser in this addendum. If that real typed reader is unavailable, combined active-player restoration remains blocked on that specific existing producer. The request-only case still requires its legitimately decoded empty-player attachment, not a fabricated default. No active-player/Scene inventory is used to infer which historical requests exist.

PrepareAsync's synchronous front end checks nonnull/lifetime/selection, observes pre-admission cancellation, and acquires its own input lease **before a task can escape**. It then:

1. Traverses PlaybackState.Author for the existing active playback dependencies.
2. Independently traverses every Present request slot in RootMotionRequests.Author, including slots whose Player, Actor or Scene is no longer live. Take Controller tag 13 even when Sources is empty; take each Clip from Sources. Absence contributes no asset.
3. Forms one bounded union of exact `(AssetId, fixed SchemaId, major, minor0)` demands, preserving separate validation contexts for each slot/source. Deduplication of loads must not erase a conflicting tuple or skip a slot's controller/ClipSlotId/order/weight join.
4. Uses **`lease.PackageLease.LoadFromPackageAsync<AnimationControllerAsset>`** and **`LoadFromPackageAsync<AnimationClipAsset>`**, and the existing owner-specific dependency loaders, all from that same package. Those calls already acquire backing references before await. Do not call current-store LoadAsync/AcquirePackageLease, resolve a current Scene, implicitly activate a Scene, substitute an equal ID from a replacement package, or accept a different target tuple.
5. Validates the retained controller/allowed-slot/Clip pairing, Extract policy, source order and all parent content predicates. Returns one complete prepared owner holding the actual controller/Clip/dependency pins, or the enclosing load's existing failure after safe cleanup. No I/O is deferred to JoinRestored.

PackageLease is borrowed from the source use, not disposed by the lease/preparer. The lease owns its retained attachment/resource source wrappers and source-use interest; dispose them only after actual enumeration/decodes have ended. The source owner's close/dispose linearizes against acquisition: closure-first refuses a new use; acquisition-first keeps the checkpoint, typed caches, package wrapper and credits alive until that use ends. Merely keeping a reference to a wrapper does not implement this rule; WorldLoadPreparedState must join/retain actual uses before releasing its existing private package/history owners.

Prepared output owns separate actual asset leases. It remains bound to this exact source/registration/package relationship, is transferred once by the parent's TransferTo(candidate), and supplies request pins to JoinRestored without reading wire data or loading assets. On cancellation/failure, attempt every independently owned cleanup; only confirmed releases refund, and unreleased graphs stay charged/anchored. Dispose of a plan, canceled wait, or loss of the old target binding cannot free admitted work.

All typed author/cache/control/retained-use/asset-demand arrays and decode/coexistence/cleanup storage are charged to the **existing load preparation/source/candidate** envelopes and the parent's Animation/World/domain ceilings before allocation. Include up to all 4096 slots and four sources per slot plus their actual transitive dependencies; count maxima promise no joint byte fit. Assets keeps its own decode/lease accounting. No new budget, Scene-credit borrowing, or independent active-binding U is minted for a child of the same accepted load. In particular, do not AcquireGuidUse on an unpublished candidate.

## 4. Adjacent v2 component asset-capture/restore edge

### What the current path already supplies

The collector does not need new visitors for `VersionedAssetReference` or `OptionalVersionedAssetReference` accessor kinds. It consumes their **canonical codec output**, not unmanaged component memory:

```text
registered Component Read/Restore/Count/Write family
  -> encoded state/ecs component records
  -> WorldPersistenceAssetIdSetWriter.CopySortedUniqueAssetReferences
  -> AssetReferenceScanner.CopyAssetReferences / VisitRecord / VisitNested
  -> Assets.AssetReference(AssetId, SchemaId, SchemaMajor)
  -> retained AssetPackageLease.MatchesAssetReferenceAsync
```

The exact collector signature already takes compiled schemas, scoped ECS/Scenes records, limits, and `Span<AssetReference>`; its sibling CopySortedUniqueAssetIds produces only the stored ID set. Scanner visits exact registered nested Record/RecordArray descriptors and reads the existing `native/asset-reference` 1.0 wrapper. It preserves target SchemaId/Major and skips an absent optional field. Full tuples are deduplicated as tuples; the ID-only set is **not** version validation.

Consequently, the parent's v2 codec obligations are the required producer edge: Mesh2 writes both AssetId and selected Mesh major to tag 1's wrapper; Sprite2 writes a present Material's complete wrapper at tag 9 or genuinely omits it. Metadata retains the exact component 2.0 tuple, nested wrapper 1.0, and Asset reference annotations. Existing v1 codecs/wrappers remain unchanged. No additional accessor-side asset collector, Entities-to-Animation dependency, or Assets-to-Runtime dependency is needed.

### Exact missing/unverified preflight edge and recommendation

Save's inspected full-reference loop checks the selected package's SchemaId/Major and codec. PrepareLoad's inspected AssetId-presence check plus delegated Scene validation is **not sufficient evidence** that every arbitrary v2 component reference has that check before candidate construction. Rather than infer unreviewed behavior, require this specific higher-owned pass on WorldLoadPreparedState:

```csharp
// TerraFX.Hosting; new internal method, no caller-supplied records/package:
internal Task<ImmutableArray<Diagnostic>> ValidateAssetReferencesAsync(
    CancellationToken cancellationToken);
```

Under its actual source use, run the **existing** MeasureReferenceCount/CopySortedUniqueAssetReferences over the same validated ECS and Scenes records with the same compiled registry/limits. Reserve the complete tuple-array/workspace peak first. Copy into owned storage synchronously; do not hold spans across await. Check every unique tuple against this owner's existing package lease before candidate construction. Return initialized-empty on success, otherwise initialized nonempty diagnostics through the existing load Rejected channel; unexpected/protocol failures fault. This pass includes the new resource's Controller/Clip references as well as v2 component references, without another resource-specific parser.

The existing matcher preserves major, but its inspected body does not request Minor=0. Close that profile predicate with these **Assets-internal overloads**, retaining the existing overloads and all public signatures:

```csharp
// AssetPackageLease:
internal Task<bool> MatchesAssetReferenceAsync(AssetReference reference,
    int requiredMinor, CancellationToken cancellationToken);

// AssetStore, reached only with that retained backing:
internal Task<bool> MatchesAssetReferenceAsync(AssetPackageBacking backing,
    AssetReference reference, int requiredMinor,
    CancellationToken cancellationToken);
```

Validate a nonnegative requiredMinor, then use the existing backing-reference/decode-reservation/Inspect/finally-release route and additionally require `actualSchema.Minor == requiredMinor`. Keep its SchemaId/Major and exact registered-codec checks. Assets receives the explicit minor and never learns Runtime schema owners or component CLR types.

Select it by this **finite target-tuple policy**, not owning-field reconstruction. On the existing higher-owned `TerraFX.Hosting.WorldPersistence`, add:

```csharp
internal static int? GetM05RequiredAssetMinor(AssetReference reference);
```

The exact membership, using the existing S(name) assignments, is:

| Target SchemaId | Target major | Result |
|---|---|---|
| `S("native/mesh")` | 1 or 2 | `0` |
| `S("native/material")` | 1 or 2 | `0` |
| `S("native/animation-controller")` | 1 | `0` |
| `S("native/clip")` | 1 | `0` |
| Every other exact `(SchemaId,SchemaMajor)` pair | Any otherwise well-shaped major | `null` |

This is six exact keys, not a name/prefix, role, owner-enum, AssetId range, wrapper-SchemaId or CLR-type test. The input is each **full AssetReference tuple** from CopySortedUniqueAssetReferences; never the UInt32 ID-set output. No owning-field context is inferred from either output. For a selected M05 Save/PrepareLoad pass, result0 chooses the explicit-minor overload; null chooses the original overload and all existing owner validation. The same selector is used in both directions. Other compositions retain their existing validator; merely compiling M05 descriptors does not enlist the M05 profile.

The rule is target-owned: all references to one of these exact native target-major keys in this selected composition share that target's frozen minor0 profile, including a reference from an application field. Such a reference is not an unrelated target. A different application SchemaId, or a target major outside the table, retains existing policy; null does not bypass its actual codec/owner validation. Field-specific allowed-target/major checks still belong to the registered component/native codec and are not replaced by this dependency pass.

**Policy boundary:** if a future requirement permits different minor policies for two occurrences of the same target SchemaId/major, this tuple selector cannot represent it. That affected requirement remains gated on a reviewed occurrence/owning-field-aware binding; neither the ID set nor a deduplicated tuple list can recover that provenance. This proposal selects no such field-local exception and does not globally forbid unrelated application asset minors.

This closes the **declaration**, not the implementation. Until the complete-tuple preflight and exact selected tuple resolution are implemented, the affected M05/v2 restore remains blocked. Generic existence of the collector or a codec does not remove that bounded blocker. Typed Mesh/Material/Animation preparation must still load the exact declared CLR codec/tuple from the same package and retain its dependencies; a successful ID/header check alone is not asset execution or residency.

## 5. Required ordering and focused evidence

Before any World.CreateRestoreCandidate call for this composition:

1. Own and validate the selected immutable source/target projection and complete history/clock/registration; reserve the actual source/read/cache/cleanup peak.
2. Read the root-motion resource and World Animation attachment through their exact registered codecs once each, validate their author/reference context, and cache both owned typed results. Do not manufacture an active author or defer that Read to candidate Restore.
3. Run section 4's complete-reference/package/profile preflight and section 3's measured asset preparation, including historical-only requests. Finish these before returning a ready prepared load/candidate-construction permission. Existing World-bound Scene restoration retains its own phase; this addendum does not move it before World construction.
4. On accepted CommitLoad use CancellationToken.None as today. Construct the exact candidate; Restore requests and the World Animation attachment from their cached authors, without either second Read; restore remaining state/Scene/clock, transfer prepared assets, and run the parent's I/O-free JoinRestored.
5. Fresh Validate/Count/Write must equal the validated target projection; then prepare the coherent cohort and restored marker and perform the existing callback/allocation-free publication. No canonical repair, movement, event replay, hidden Step, or post-publication asset preparation closes an earlier gap.

Preparation cancellation admits no candidate and retains resources until actual consumers unwind; accepted commit cancellation cannot undo the operation. Source identity, credits and actual uses survive wrapper disposal as above. Failure before publication leaves the old World installed; old retirement remains separately reported after a successful swap.

Required focused cases, **not executed results or new acceptance IDs**:

| Case | Distinguishing observation |
|---|---|
| Historical-only requests | Produce a request, destroy Player/Actor/Scene, save, then prepare Standard Branch with no live player. Before candidate issuance, retained Controller/Clip dependencies and slot joins are complete. Restore uses the cached author; JoinRestored performs zero asset I/O and no event/motion replay. |
| Present-empty Sources | A valid Seek/identity slot still pins Controller tag 13; an absent slot pins nothing. Empty current player inventory cannot suppress request discovery. |
| One reader/family | Actual load operation reaches registered Resource Read once, reuses its author for validation/preparation/Restore, and rejects wrong source occurrence/factory/author type/count/tick. Do not manufacture a binding or a second test parser. |
| Attachment source ownership | Through the actual load path, observe one registered World Animation attachment Read before candidate construction and one later Restore using that same cached immutable author. Reject an Entity-scoped row, foreign source/family, duplicate Read and unavailable author; empty Players still uses a real Read. Retained preparation survives input-wrapper disposal, and shared runtime storage keeps transferred credits. |
| Package replacement and close | Replace the store's current package or dispose preparation wrappers after admission. In-flight work uses its original package/source interests; same IDs/digest-looking values from another preparation cannot substitute. Failed joins/cancellation/cleanup never refund actual work early. |
| Old-v1/v2 persistence | Save/prepare/commit real Worlds selecting v1 versus v2 components. Mesh2 major2 and Sprite2 present Material2 survive canonical roundtrip and appear as full collector tuples; an absent Material contributes none. Old v1 remains major1 and refuses major2. Assert literal tag-3 majors, not just equal ID sets. |
| Same ID, wrong tuple | Supply the same ID under a package entry with wrong SchemaId/major, missing codec, or unsupported selected minor. PrepareLoad rejects before candidate issuance even when its ID set and Scene references are otherwise valid. The old v1 and new v2 package cases remain separately selected. |
| Exact minor-policy membership | Exercise the six listed target-major keys through real Save/PrepareLoad tuple collection, including Mesh2/Material2 rejection of a nonzero minor. An unrelated application target with a supported nonzero minor retains the original matcher. Verify lookup uses SchemaId/major, not ID, wrapper identity or a broad native/owner test; an application field referencing the exact listed native target still shares its target policy. |

M06/M07 feedback/imported execution and native/AOT/public-consumer qualification are unchanged. No code, tests, native work, commits, push, or PR are part of this proposal.

## 6. Frozen inputs and output

Baseline HEAD: `31f156a51ccc462ae9c5cc8d4e87e00c0054dec4`. Freeze date: **2026-11-16 UTC**. This revision starts from this addendum's raw SHA-256 `BD110DF7B0C3C48513A9B451BFA7B2FD20AD089DB3FA0B0C1A45C0A7613E0B5C`; the revised output is sealed separately below. The **24 complete files** below are the direct reviewed evidence, including relevant-range reads. Discovery-only matches do not add unreviewed files. `C`=unchanged, `M`=modified, `U`=untracked at this boundary; hashes are raw-file SHA-256. Dirty bytes, not substituted HEAD contents, are the inputs.

The runtime-port proposal's raw hash and normalized seal are verified; its 85-input manifest and the inherited rectification/addendum 73/18-input manifests were also rechecked. They overlap this evidence and are not an expanded direct review inventory. The parent's older dirty-state limitation remains historical; it is not silently repaired or reused as this task's baseline.

For this revision, excluding this addendum, the pre-edit snapshot contains **40 paths**, digest `4F997A8C0CCE21E55350681ABACFAF94AAD54601D5A18F29A4FB3937F6BBFFF8`. Compute it from `git status --porcelain=v1 -z --untracked-files=all`, backslash paths sorted by ordinal code point, concatenating `XY + TAB + path + TAB + uppercase raw-file SHA256 + LF`, UTF-8/no BOM. Recheck that exact non-output snapshot and HEAD after editing. Nonreviewed dirty files are protected as snapshot context, not dispositioned or added to the review inventory.

**Verification limitation:** this revision's final wider-snapshot check also differs from that pre-edit digest, while all 24 reviewed hashes/states and inherited 85/73/18 input manifests still match. Complete non-output worktree preservation therefore cannot be certified. Preserve that failed baseline check; do not silently refreeze it, attribute changes, or expand the review inventory. The handoff captures the final observed snapshot separately.

The earlier draft's wider-snapshot limitation is not erased or reclassified as a pass. This revision has its own explicit starting output hash and failed non-output baseline check. Only this addendum is edited here; no other dirty file is dispositioned.

The output seal hashes this whole file with only the following line's 64 hexadecimal digits replaced by 64 ASCII zeroes. The handoff additionally freezes/rechecks the ordinary raw-file output hash and full final dirty-state digest including this new untracked file. No recursive raw self-hash is claimed.

Addendum-seal-sha256: E39E0A9FEC3BAE882E9A25AC29459E1F089F14ABEF10FD759AFEB080A2EC2DDD

| Input | State | SHA-256 |
|---|---|---|
| ``docs\project\Roadmap.md`` | C | `C4FBE82155A6D82FFD2D13AD2F18056D938738C4FA5A66760C4877E24ED001C7` |
| ``docs\design\27-state-serialization-replay-and-recovery.md`` | C | `B9B6324C5B1B1543CA1D2287ECE579597A7E7AA62D885031C4FF1BB629F08777` |
| ``docs\design\29-project-setup-build-and-deployment.md`` | C | `3B55A013EEC327ECD648ACBD5B53717DD218AD288E8047AC21277609EFA2F47B` |
| ``docs\design\35-implementation-roadmap.md`` | C | `24EEC6E92B7AD904D552F290EF748FFC8FCE13B6B94A89EBD9F39487D4584D87` |
| ``docs\proposals\m05-owner-contract-closure-rectification.md`` | U | `67134F02B92D316524848ECB8E0C917EF868DA3D2EDA5FC581DBAA25B1825632` |
| ``docs\proposals\m05-owner-contract-closure-rectification-addendum.md`` | U | `C8DE6581D5CB126D18C294FC9EB22A01459B2D6BFC3AD933ECB1652582634F4A` |
| ``docs\proposals\m05-owner-contract-closure-runtime-ports.md`` | U | `605CF1D92DC362A8B73EA9BDA3A5467757942C268712F484AF53D9A9159D53E3` |
| ``sources\TerraFX.Assets\AssetPackageLease.cs`` | C | `C0DFE89DBEB9760EE8C22CC583858D6112F9CDA5417C001F079EB4069D990D96` |
| ``sources\TerraFX.Assets\AssetStore.PackageLease.cs`` | C | `C58FA6D3E3C1E9BA5792E2E3D590FA066FC4710D81C43C80D6CFD6FD08948ECF` |
| ``sources\TerraFX.Assets\AssetReferenceScanner.cs`` | C | `EF0232BCC4F384FE6025A8F98CB11D7C6E70033FF36F31E796994A314AE67BBF` |
| ``sources\TerraFX.Runtime\Entities\World.PersistenceRestore.cs`` | C | `CB87DC7B8645EB07B58084E6F4A6E772E085A86883D2D7716A82CBAD965CBE0F` |
| ``sources\TerraFX.Runtime\Entities\WorldPersistenceEcsReader.cs`` | C | `8089307A8BF1FCD2CC789FD100DFFC8995BB91200896C99507DAD5221CBA06D8` |
| ``sources\TerraFX.Runtime\Entities\WorldRegistration.cs`` | C | `8E2A485E17B50A4376884AAD77BF40E603BA51240934B619543964D393D020AB` |
| ``sources\TerraFX.Runtime\Entities\WorldResourceCodecDispatch`2.cs`` | C | `3B3C76F7545015E945437AA999AC82D42BA60A11BE5B55DDF0C196A07F177B7D` |
| ``sources\TerraFX.Runtime\Entities\WorldResourceReadResult`1.cs`` | C | `6E80404A7C10C54CC8548C6256E3234E63B0FBE4CB06BFA1BED978B6CCBDFF64` |
| ``sources\TerraFX.Runtime\Entities\WorldResourceRegistration`1.cs`` | C | `3AB22DF1D073B35578982700D55AB8C4C22AF152E668C89236CF1C7880BEC57B` |
| ``sources\TerraFX.Runtime\Entities\WorldResourceStateCodecReadOutcome.cs`` | C | `900CE98370D80515D8DB92547CE2949D25E87E07C2C972B93F6872D8D6369623` |
| ``sources\TerraFX.Runtime\Entities\IWorldAttachmentRegistration.cs`` | C | `0C97349A13821F83B3A4DE9D45C6BB263FB5D2A6940F64FE6F2638E2A1A14459` |
| ``sources\TerraFX.Runtime\Entities\WorldAttachmentRegistration`2.cs`` | C | `AC694CC575DAD6BF9D246B3F0B9BE31B0B53F30DB1F3F6CE1AA864ABEC5622FD` |
| ``sources\TerraFX.Runtime\Entities\WorldPersistenceAssetIdSetWriter.cs`` | C | `7870193A4552AAE5C94C0D836DEB1362F69916BC18EEE00A5713F79E49655684` |
| ``sources\TerraFX.Runtime\Hosting\WorldLoadPreparedState.cs`` | C | `0AAFE467247DCECFF3EDCFD30F36A19DA8070CEBA7FCF83B8B9ACB44B7B59A28` |
| ``sources\TerraFX.Runtime\Hosting\WorldRetainedHistoryControl.cs`` | C | `2105F6485425CF15F0A2C21D575F20C38400EFF50D3B1E12E954ECC89BE2CAE5` |
| ``sources\TerraFX.Runtime\Hosting\WorldReplacementOperation.cs`` | C | `C6DFA0EE44438C005E115FE9A15FE51643CF49176FB266A59AE5CFE67250EFF5` |
| ``sources\TerraFX.Runtime\Hosting\WorldPersistence.cs`` | C | `BC2D253ADFF70F8A14301FCE2ACEBA65F20578258BE1C94434B4DE9F558849A6` |

**Verification record:** all 24 reviewed hashes/states and inherited 85/73/18 input manifests match; this revision's 40-path non-output baseline check fails as stated above. After the final edit, recheck named proposal hashes/seals and HEAD and independently reread the sealed revised output and final observed dirty state before handoff. Report any further mismatch or inability to verify. This is file/hash/state inspection only, not execution evidence or acceptance.
