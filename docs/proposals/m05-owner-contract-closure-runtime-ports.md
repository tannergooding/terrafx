# M05 private runtime-port closure

## 1. Status, scope, and evidence

**Tentative recommendation under the authorization for GPT-6.1 Sol maximum-reasoning proposals; not an accepted contract or implemented surface.** This closes the three lower-port declaration holdpoints in section 6 of [the rectification](m05-owner-contract-closure-rectification.md), SHA-256 `67134F02B92D316524848ECB8E0C917EF868DA3D2EDA5FC581DBAA25B1825632`. Preserve [the Seek/particle addendum](m05-owner-contract-closure-rectification-addendum.md), SHA-256 `C8DE6581D5CB126D18C294FC9EB22A01459B2D6BFC3AD933ECB1652582634F4A`, without reinterpretation.

The recommendations cover preconstruction registration and engine phases, the Standard Branch post-restore join, version-selecting component AssetReferences, and the root-motion request Resource/State2 family. They do **not** close actual Physics feedback, imported-animation execution, RecordedContinuation, qualification, or milestone acceptance. No blanket M03/M04 dependency is introduced. All rectification feature semantics, count/byte ceilings, independent-use charges, old v1 paths, and M06/M07 deferrals remain.

There is one explicit refinement of a **previously proposed, unimplemented resource payload**: section 5 adds Controller at slot tag 13. It closes the retained request's content join after destruction; it changes neither an implemented v1 tuple nor a public request member. No other schema membership is changed here.

Only this proposal is written. Maintained contracts, implementation, tests, and existing dirty work are inputs, not edit targets. No build, test, native execution, commit, push, PR, or acceptance is claimed.

### Observed facts

Paths below are repository-relative; source basenames identify the full paths in section 7. These facts motivate the recommendations; the declaration blocks that follow are **new**, not quotations of existing APIs.

| Fact verified by reading | Owner/source evidence |
|---|---|
| An affected unit needs complete supporting declarations, not a full milestone exit. | Chapter 35, capability sequencing and section 2.3; `docs\project\Roadmap.md`, M05/Next work. |
| Animation support currently registers Skeleton/Clip codecs only. | `AnimationSupport.cs:11–27`. |
| CPU support stages a nonempty component set only; it rejects resources, attachments, systems, and tags. | `WorldRegistrationBuilder.cs:176–235`; `CpuExtractionSupportRegistration.cs`; `IWorldCpuExtractionSupportRegistration.cs`. Its restriction must not be silently relaxed. |
| Current tick execution runs scheduled systems, structural playback, and publication; it has no live Animation/particle dispatch. | `World.Ticks.cs:37–196`. Chapter 13 specifies engine work before Animation systems, after Structural playback, after Extract systems, and callback-free marker installation. |
| Frame reads are exact-World, owner-thread, publication/revision/cohort checked. The current cohort storage itself contains frames/generation, not the proposed new roots. | `World.CpuExtraction.cs:18–177`; `WorldFrameReadLease.cs`; `World.Controls.cs:846–862`. Chapter 16 owns the stronger participant transaction to implement, not evidence that every actor port exists. |
| Production Branch construction restores ECS/resource/system/attachment state, Scene instances/hierarchy, then clock/controls. Validation and marker preparation follow. | `WorldLoadPreparedState.cs:139–181,273–330`; `World.PersistenceRestore.cs:733–736`; `ScenePersistenceRestorer.cs:291–360`; `WorldReplacementOperation.cs:71–94`. |
| Attachment/system Restore has no component-read entitlement. Historical references are distinct from live references. | Chapters 12/27/30; `World.PersistenceRestore.cs`, restore contexts; `WorldPersistenceEcsReader.cs:1651–1706`. |
| Existing AssetReference component bindings use `int` readers; old Mesh/Sprite codecs require target major 1. | `ComponentFieldBinding<T,TAuthor>.AssetReference/OptionalAssetReference`, lines 308–329; `RenderMeshComponentDefinitions.cs:220–258`; `RenderSpriteComponentDefinitions.cs:252–288`. |
| The wire reference is already `native/asset-reference` 1.0: UInt32 AssetId, Guid SchemaId, UInt32 SchemaMajor. The Assets scanner retains all three. | `NativeAssetSchemaCatalogue.cs:63–67`; `AssetReference.cs`; `AssetReferenceScanner.cs:170–206`. There is no wire Minor field or automatic migration. |
| Resource factory field collection accepts required FixedArrays, or required unambiguous records leading to them; its field access assumes one primitive element per partition. | `WorldResourceFactoryBinding<T,TAuthor>.CollectFixedArrayFields`; `WorldResourceFieldBinding<T,TAuthor>.PartitionArray`; `ResourceFactoryFamily<T,TAuthor>`; `WorldResourceCaptureData<T>`. It cannot presently bind the proposed slot/source RecordArrays. |
| Resource Read results are issuer-bound; Restore sets each partition exactly once; codec dispatch revokes operations in `finally`. Common resource validation currently checks the view, not the proposed aggregate's predicates. | `WorldResourceReadBinding<TAuthor>`; `WorldResourceCodecDispatch<T,TAuthor>`; `WorldResourceRestoreBinding<T>`; `WorldResourceValidationBinding.cs:27–45`. |
| Scene preparation already has measured asynchronous resource preparation over the exact expanded plan. | `ISceneExpansionResourcePreparer.cs`; `SceneExpansionPlan.cs`; `Renderer.CpuExtractionSupport.cs:169–182`. This is a useful higher-owned composition seam, not a lower asset-loading service. |

Chapter 29's thirteen-product DAG and logical owner rules apply throughout: Entities defines opaque engine ports and owns issuance/installation; Animation owns playback/request content; Scenes owns pose interpretation and expanded-instance maps; Rendering owns the cosmetic pool and render resources. Assets/Graphics acquire no Runtime dependency, Physics acquires no Entities/Assets dependency, and Runtime acquires no Content.Tools dependency.

## 2. Preconstruction and engine-phase declarations

The blocks are declaration projections: bodies and private implementation bookkeeping are omitted, not additional unspecified callable members. All new lower types are in `TerraFX.Entities`; `System`, `System.Collections.Immutable`, `System.Threading`, `System.Threading.Tasks`, and TerraFX numerics have their ordinary meanings. Existing generic constraints continue to apply. No reflection discovery, public binding constructor, general participant registration API, or late enlistment is added.

### 2.1 Tokens, factories, and per-World instances

Retain the rectification's public token factories and two builder methods. Close their private contents as follows:

```csharp
public sealed class AnimationSupportRegistration
{
    internal AnimationSupportRegistration(IWorldAnimationSupportRegistration registration);
    internal IWorldAnimationSupportRegistration Registration { get; }
}

public sealed class CosmeticParticleSupportRegistration
{
    internal CosmeticParticleSupportRegistration(
        IWorldCosmeticParticleSupportRegistration registration);
    internal IWorldCosmeticParticleSupportRegistration Registration { get; }
}

internal interface IWorldAnimationSupportRegistration
{
    ImmutableArray<Type> RootTypes { get; }
    void RegisterStorage(WorldRegistrationBuilder builder,
        CompiledContentSchemaRegistry schemas);
    long GetWorldStorageByteReservation(WorldOptions options);
    IWorldAnimationParticipant CreateParticipant(World world, WorldOptions options,
        IWorldSceneParticipant sceneParticipant);
}

internal interface IWorldCosmeticParticleSupportRegistration
{
    ImmutableArray<Type> RootTypes { get; }
    void RegisterStorage(WorldRegistrationBuilder builder,
        CompiledContentSchemaRegistry schemas);
    long GetWorldStorageByteReservation(WorldOptions options);
    IWorldCosmeticParticleParticipant CreateParticipant(World world,
        WorldOptions options, IWorldSceneParticipant sceneParticipant);
}

internal interface IWorldAnimationParticipant
{
    long OwnedByteReservation { get; }
    ImmutableArray<IWorldEngineFrameActor> FrameActors { get; }
    bool PrepareNextCommand(WorldEnginePhaseBinding binding);
    void PrepareAdvance(WorldEnginePhaseBinding binding);
    void PrepareMembership(WorldEnginePhaseBinding binding);
    void PrepareOutput(WorldEnginePhaseBinding binding);
    void JoinRestored(WorldEngineRestoreJoinBinding binding,
        CancellationToken cancellationToken);
    void Dispose();
}

internal interface IWorldCosmeticParticleParticipant
{
    long OwnedByteReservation { get; }
    ImmutableArray<IWorldEngineFrameActor> FrameActors { get; }
    void PrepareOutput(WorldEnginePhaseBinding binding);
    void JoinRestored(WorldEngineRestoreJoinBinding binding,
        CancellationToken cancellationToken);
    void Dispose();
}
```

The tokens are immutable reusable factory descriptions, not per-World owners. Their language default is null; required null references throw `ArgumentNullException`. Only `AnimationSupport.CreateWorldSupportRegistration` and `Renderer.CreateCosmeticParticleSupportRegistration`, respectively, construct them with their closed built-in implementations. They expose no public members, own no package/World lifetime, and are not disposable. Capture all factory properties and roots once; a later property read cannot substitute another family.

Both `RegisterStorage` calls run against isolated staging builders. Validate and merge their **whole** inventories atomically; rejection leaves the receiving builder mutable and unchanged:

| Selection | Exact storage inventory and private roots |
|---|---|
| Animation token | One `animation/player` component family, one World-scoped transferred immutable `animation/world-state` attachment family, and one root-motion request resource registration from section 5. No tags, application systems, other attachments, or other resources. Root slots: 0 = immutable current/previous Animation pose/binding output; 1 = immutable request-resource pin ownership. |
| Particle token | One `render/particle-emitter` component family. No canonical pool resource, attachment, system, control payload, RNG state, or tag. Root slots: 0 = immutable prepared/committed cosmetic pool, counters, recording, and birth-resource state; 1 = PlaneXY dimensional output; 2 = 3D billboard output. |
| Selected CPU support | Its existing component-only registration rule stays. The selected v2 composition owns Mesh2/Sprite2 and the three unchanged light families. CosmeticParticles **enlists the particle token's single component registration**; CPU support does not register that component a second time. This makes ownership of the rectification's combined inventory explicit. |

`RootTypes` is exactly two or three initialized, nonnull, distinct, sealed immutable CLR root types in the slot order above. These are opaque payload types, not services or persisted type identities. The actual closed higher factory supplies them; installation/read checks exact captured `Type` identity, not a name, interface search, or assignable substitute. Their internal storage implements the rectification's already-selected pose/pool/output content; lower code cannot enumerate or invoke payload members.

Both tokens require the same World's Scenes support. Selected CPU Animation/CosmeticParticles bits additionally require their matching tokens; tokens never add another token or change component versions implicitly. A token alone does not assert that a renderer consumer is selected. Reject duplicate/conflicting tokens, duplicate storage identities/types, missing descriptors, incompatible component versions, invalid access, or an incomplete selected factory graph before World issuance. AssetStore's separately frozen codec family is checked by its own construction/loading path, not by pretending the World builder can inspect an unrelated AssetStore.

After complete peak reservation and fresh lower storage creation, construct Scenes, Animation, particles, then CPU extraction instances in dependency order. A participant is fresh, nonreentrant, tied to the exact World object/construction generation, and borrows `world`, `options` facts, and the exact Scene participant. Its `OwnedByteReservation` must equal the previously admitted amount. Construction allocates only admitted empty owners/backing; no asset I/O, hidden player, tick, task escape, or native service starts. Failure attempts every independent reverse-order cleanup; confirmed releases alone refund. `Dispose` is lower-serialized, ends new admissions, joins actual participant uses, and releases only that participant's ownership, never the borrowed World/Scenes/Host. Cleanup failure keeps the actual remaining graph and credits anchored.

On `World`, add only these fixed internal bridges:

```csharp
internal IWorldAnimationParticipant? GetAnimationParticipant();
internal IWorldCosmeticParticleParticipant? GetCosmeticParticleParticipant();
internal void CompleteM05RestoreJoin(CancellationToken cancellationToken);
internal TRoot ReadEngineRoot<TRoot>(in WorldFrameReadLease lease, int actorKey)
    where TRoot : class;
```

The first two return borrowed exact instances, or null only for an unselected token; disposal throws. They are for the fixed owner compositions/controllers, not arbitrary type lookup. Higher Animation/Rendering participants implement the existing Scenes-owned `ISceneExpansionResourcePreparer` in addition to their lower interface. Scenes composes the actual selected preparers in fixed Rendering-resource, Animation-resource, particle-resource order over **one** real `SceneExpansionPlan`. Measure all bundles and their coexistence/workspace before preparation; retain the exact plan/package backing and expanded-key map. Empty selected preparers return measured empty ownership, not a fabricated Scene or a skipped lifecycle.

Activation/structural admission must have complete measured asset/target/pin ownership before installation. It validates the projected player/emitter set and exclusive Animation target/channel/motion-actor ownership as a whole. A new component without its required exact Scene provenance or prepared assets rejects the enclosing admission; stage 7 does not perform I/O to repair it. Loading a Clip alone still creates no player.

### 2.2 Phase grants and staged operations

```csharp
internal enum WorldEnginePhase : byte
{
    AnimationCommand = 1,
    AnimationAdvance = 2,
    AnimationMembership = 3,
    AnimationOutput = 4,
    ParticleOutput = 5
}

internal sealed class WorldEnginePhaseBinding
{
    internal WorldEnginePhaseBinding(World world, object participant,
        long invocationEpoch, WorldEnginePhase phase);
    internal WorldEnginePhase Phase { get; }
    internal SimulationStepContext Step { get; }
    internal SpatialFrame3 Frame3 { get; }
    internal SpatialFrame2? Frame2 { get; }
    internal int GetComponentCount<T>() where T : unmanaged;
    internal T ReadComponent<T>(int row, out WorldEntityIdentity entity)
        where T : unmanaged;
    internal T ReadWorldAttachment<T>() where T : class;
    internal WorldEngineScenePose ReadScenePose(WorldEntityIdentity entity);
    internal (SpatialPosition3 Position, Matrix4x4 LinearTransform,
        Quaternion Rotation) ReadSceneTransform(WorldEntityIdentity entity);
    internal void StageWorldAttachment<T>(T value) where T : class;
    internal void StageResource<T>(Guid resourceId,
        scoped ReadOnlySpan<T> values, IDisposable retainedOwner) where T : unmanaged;
    internal void StageScenePose(WorldEntityIdentity entity,
        WorldEnginePoseChannels channels, in WorldEngineScenePose pose);
    internal void StageControl(scoped ReadOnlySpan<byte> payloadRecord);
    internal void StageEvents(ImmutableArray<CommittedWorldEvent> events);
    internal void StageCompletion<T>(WorldGuidUseControl use,
        TaskCompletionSource<T> completion, T result) where T : notnull;
    internal WorldEngineOwnedRoot CaptureRoot<TRoot>(int slot, TRoot value,
        IDisposable ownership) where TRoot : class;
    internal void StageRoot(int slot, WorldEngineOwnedRoot root);
    internal void Revoke();
}
```

Only actual World dispatch constructs a grant. Construction validates a nonnull registered exact participant, checked positive invocation epoch, currently active engine phase, owner thread, World lifetime, and selected registration. An arbitrary object, guessed epoch, same GUID, or constructor call outside dispatch cannot issue authority. Null/default is unissued; there is no public constructor. Every operation checks the active exact invocation/thread/phase/family **before** its operands. Validate known active misuse as Chapter 13's sticky first failure; catching it cannot turn the callback into success. Revoked/wrong-thread use cannot select or fault a successor. `Revoke` is lower-only and always runs in `finally`; bindings cannot be retained, awaited, or used by child work.

Counts/rows describe one complete protected membership epoch, sorted by unsigned RFC EntityId, not archetype slots. Repeated reads in that invocation see the same set. A row is `[0,count)`; an empty set has count zero. No Entity handle, mutable column/span, package service, arbitrary attachment, or resource discovery escapes.

| Grant / dispatcher point | Exact permissions and installation/completion |
|---|---|
| AnimationCommand, stage 2 **before** application Animation systems | Invoke `PrepareNextCommand` once per command in the stage-0-frozen accepted prefix, at most 128 commands. It stages that command's complete attachment/pose/control/event/result change. `true` consumes exactly one admitted command, including an application-time rejection; `false` means exhausted and stages nothing. Lower validates then installs each successful local edit before the next command. No resource replacement yet. |
| AnimationAdvance, stage 2 after that prefix | Invoke `PrepareAdvance` once. It performs the rectification/addendum's ordinary advance, suppression, and transition-choice pass, prepares poses, and stages the **entire** 4096-partition request array and its pin owner. Only this grant may replace that resource. Install before any application Animation system. |
| AnimationMembership, stage 7 after atomic playback/hierarchy resolution, before post-playback reference validation | Invoke `PrepareMembership` once over the complete new set. Reconcile the attachment/player bijection and derived pose roots; remove destroyed player bindings and their own playback ownership. Do **not** erase historical request slots/pins produced at stage 2, replay events, sample new time, or activate unprepared players. |
| AnimationOutput, stage 8 after all Extract systems and final authoritative validation | Invoke `PrepareOutput` once. Read selected player/Scene state and prepare immutable pose output/cohort roots only. No canonical attachment/resource/Scene/control/event mutation or command result is permitted. |
| ParticleOutput, stage 8 after Animation output, before final CPU output preparation | Invoke the particle `PrepareOutput` once over post-Structural emitters/transforms and the last committed pool. Stage the complete cosmetic pool/output roots and explicit spawn results. No authoritative component, attachment, resource, control, gameplay event, or RNG write is permitted. |
| Stage 9 | Invoke **no participant**. Transfer/install the fully prepared roots with the existing single publication marker. Only afterwards release staged particle results for asynchronous completion. |

Read allowlists are frozen by the selected owner: Animation's own player component/World attachment and exact activated Scene targets; particles' own emitter component and actual Scene transforms. CPU extraction reads only the installed declared output roots. Generic type parameters must match these exact captured families. Applications cannot request these grants.

Every `Stage*` call appends to a pre-admitted lower edit/output record; it does not immediately mutate storage. Validate complete projected values, checked revision/control issuance, ownership transfers, and all required capacity before the dispatcher installs a local edit outside callbacks. Invalidate capture/extraction eligibility and advance the checked canonical mutation revision before the first actual canonical write. `StageWorldAttachment` takes the exact registered immutable value through its transferred-ownership/release family; it never exposes unsignaled mutation. `StageResource` copies exactly the registered complete partition count into reserved candidate storage and transfers a nonnull independently owned companion pin graph only on successful installation. Empty state uses an explicit empty owner. Replacing a resource releases the prior companion only after its actual readers finish.

`StageScenePose` is limited to the participant's admitted target/channel map and the existing dynamic-root ownership refusals. Scene reads/write preparation use the exact Scene participant, not public idle `World.TryGet` calls during a tick. Close the added private Scene bridge as:

```csharp
[Flags]
internal enum WorldEnginePoseChannels : byte
{
    Position = 1, Rotation = 2, Scale = 4
}

internal readonly struct WorldEngineScenePose
{
    internal WorldEngineScenePose(Vector3 localPosition, Quaternion rotation,
        Vector3 scale, SpatialPosition3? rootPosition);
    internal Vector3 LocalPosition { get; }
    internal Quaternion Rotation { get; }
    internal Vector3 Scale { get; }
    internal SpatialPosition3? RootPosition { get; }
}

// Added to IWorldSceneParticipant:
WorldEngineScenePose ReadEnginePose(WorldEnginePhaseBinding binding,
    WorldEntityIdentity entity);
WorldEngineScenePose ReadRestoredEnginePose(WorldEngineRestoreJoinBinding binding,
    WorldEntityIdentity entity);
ImmutableArray<ComponentInitialization> PrepareEnginePose(
    WorldEnginePhaseBinding binding, WorldEntityIdentity entity,
    WorldEnginePoseChannels channels, in WorldEngineScenePose pose);
```

The copied pose has no authority/disposal; default is not an admitted pose. Construction uses the existing finite LocalTransform/scale/quaternion/spatial predicates. RootPosition is present iff this is an actual root, whose local Position stays exactly zero; child positions remain parent-local. Unknown/zero channel masks reject. Scenes prepares the exact registered LocalTransform/RootPosition `ComponentInitialization` values, merging untouched channels; lower installs their typed values with the same edit. No Scene callback occurs inside installation, and Animation never names Scene component implementation types. An invalid pose is a sampling/tick failure, not an identity repair.

`StageControl` admits one complete exact `animation/control` payload per applied command, with the existing lower global control wrapper/sequence; no raw owner ID or caller sequence is trusted. `StageEvents` admits only the two selected Animation event types/schemas and preserves the rectification/addendum's complete occurrence order and limits. Both are provisional until the marker.

`StageCompletion` accepts only the captured task/result family of a genuinely admitted command/spawn and its exact `WorldGuidUseControl`; it cannot register new work. Required references/results are nonnull, and the task source must use `RunContinuationsAsynchronously`. Dispatcher completion happens outside gates **and after the participant callback**: Animation receipts after that command's actual local installation/refusal; particle receipts after the successful marker. A later World failure does not retract an Applied Animation receipt, but publishes none of its provisional gameplay events. Failed encompassing ticks fault unevaluated/uncommitted particle work after cleanup. Close rejects pending unapplied Animation commands and returns the particle WorldClosed disposition; no successor retargeting. The existing pre-admission-only cancellation and `256+W` use/credit rules are unchanged. No token is accepted by synchronous tick callbacks and no cancellation can release actual work.

### 2.3 Opaque roots and frame participation

```csharp
internal sealed class WorldEngineOwnedRoot : IDisposable
{
    internal WorldEngineOwnedRoot(WorldEnginePhaseBinding binding, int slot,
        object value, IDisposable ownership);
    internal WorldEngineOwnedRoot(WorldEngineRestoreJoinBinding binding, int slot,
        object value, IDisposable ownership);
    internal WorldEngineOwnedRoot(WorldEngineFrameBinding binding,
        object value, IDisposable ownership);
    public void Dispose();
}

internal interface IWorldEngineFrameActor
{
    int Key { get; }
    WorldEngineFrameCheck Check(WorldEngineFrameBinding binding);
    WorldEngineOwnedRoot Prepare(WorldEngineFrameBinding binding,
        WorldEngineFrameCheck check);
}

internal sealed class WorldEngineFrameBinding
{
    internal WorldEngineFrameBinding(World world, object actor, long invocationEpoch,
        int actorKey, bool preparing, SpatialFrame3 proposedFrame3,
        SpatialFrame2? proposedFrame2);
    internal SpatialFrame3 CurrentFrame3 { get; }
    internal SpatialFrame2? CurrentFrame2 { get; }
    internal SpatialFrame3 ProposedFrame3 { get; }
    internal SpatialFrame2? ProposedFrame2 { get; }
    internal TRoot ReadCurrentRoot<TRoot>(int slot) where TRoot : class;
    internal WorldEngineOwnedRoot CaptureRoot<TRoot>(TRoot value,
        IDisposable ownership) where TRoot : class;
    internal void Revoke();
}

internal sealed class WorldEngineFrameCheck
{
    internal WorldEngineFrameCheck(WorldEngineFrameBinding binding,
        int? extentFailureEntry, int? precisionFailureEntry,
        long preparationBytes, long retirementBytes);
    internal int? ExtentFailureEntry { get; }
    internal int? PrecisionFailureEntry { get; }
    internal long PreparationBytes { get; }
    internal long RetirementBytes { get; }
}
```

Only the corresponding lower binding's `CaptureRoot` constructs a capsule. All operands are nonnull, slots are in the exact captured roster, and payload type equals that slot's frozen type. A capsule has no payload/service getter, mutable owner, public factory, or authority to close a World. It carries lower-held exact family/participant/construction/source/operation stamps and the transferred **actual** ownership graph. Capturing/staging the same ownership twice rejects. Disposal is lower-serialized and idempotent after confirmed release; an incomplete release stays charged/anchored and is not falsely marked complete.

StageRoot transfers a whole prepared capsule to the lower edit, not a callback. Stage-2/7 roots are local working state; stage-8 roots become externally readable only with stage 9. On refusal/failed preparation, the lower ledger aborts independent new ownership in reverse dependency order; on installation it detaches old ownership for retirement. Independent snapshots/recordings have their own reservations and retained children, not a borrowed capsule that becomes invalid on overwrite.

`ReadEngineRoot<TRoot>` requires the actual current `WorldFrameReadLease`, exact permitted reader and actor key, and exact captured output type. It lends immutable content for that read only; extraction copies required data and acquires independently funded snapshot/pin ownership before releasing the lease. Neither a retained class reference nor copied GUIDs extend that borrow.

Frame actors are fresh per-World instances captured once from `FrameActors`, not generated on each change. Animation supplies key 4 for pose slot 0. Particles supply key 5 for enabled 2D PlaneXY output slot 1 and key 6 for 3D output slot 2 (including 3D PlaneXY and CameraFacing3D). Disabled dimensions omit only their actor; a selected empty actor returns a stamped typed empty root. The extended preparation order remains **0,1,4,5,6,7,2,3**, omitting unselected actors; key 7 remains the separately selected LOD owner. Primary diagnostics retain lowest frozen key/stable entry order, not preparation-order precedence.

The frame binding/check constructors are coordinator-only and validate the actual active Check/Prepare operation. Check borrows already-admitted sources and returns nonnegative byte demands; nullable failure entry is absent for success, otherwise a nonnegative stable ordinal. The lower constructor captures the complete Chapter 16 stamp vector; those facts cannot authorize another operation after revocation. Check allocates no replacement cache and acquires no new asset pins. Prepare requires the exact matching check and the complete peak reservation; only then may CaptureRoot take new ownership. No caller-supplied generation is accepted.

This uses Chapter 16's **one** coordinator and existing installation gate: ordered Check, full reservation, Prepare, reverse Abort, callback/allocation-free root swap, detached Retire. Abort/Retire are lower ownership-ledger operations over capsules, not missing participant Install callbacks. They call owned cleanup only outside gates, after actual uses join, attempting independent releases even after a failure. Frame-only work transports valid pose/history and particle derived representations without advancing timeline, age, RNG, counters, canonical spatial positions, or resetting a cache to pass admission. The current source's small frame tuple is not evidence that this required plumbing already exists.

## 3. Standard Branch post-restore join

```csharp
internal sealed class WorldEngineRestoreJoinBinding
{
    internal WorldEngineRestoreJoinBinding(World candidate, object participant,
        long invocationEpoch);
    internal Guid WorldGuid { get; }
    internal long CompletedTick { get; }
    internal SpatialFrame3 Frame3 { get; }
    internal SpatialFrame2? Frame2 { get; }
    internal int GetComponentCount<T>() where T : unmanaged;
    internal T ReadComponent<T>(int row, out WorldEntityIdentity entity)
        where T : unmanaged;
    internal T ReadWorldAttachment<T>() where T : class;
    internal T ReadResourcePartition<T>(Guid resourceId, int partitionIndex)
        where T : unmanaged;
    internal WorldEntityIdentity ResolveLiveEntity(Guid entityId);
    internal WorldEntityIdentity ResolveHistoricalEntity(Guid entityId,
        Guid creationLineageId, Guid creationBranchId);
    internal WorldEngineScenePose ReadScenePose(WorldEntityIdentity entity);
    internal WorldEngineOwnedRoot CaptureRoot<TRoot>(int slot, TRoot value,
        IDisposable ownership) where TRoot : class;
    internal void StageRoot(int slot, WorldEngineOwnedRoot root);
    internal void Revoke();
}
```

Only `World.CompleteM05RestoreJoin` issues these grants, once for Animation then particles, into a complete **unpublished Standard Branch candidate** after Persistent/Derived/resource/system/attachment, Scene-instance/hierarchy, and clock/control restoration. Require the exact candidate, registered instance, checked invocation epoch, owner thread, selected family, restored frames, and no marker. No application Restore hook is called. Revocation/sticky failure/null/default/foreign-operand rules are those of section 2; grants cannot escape or await.

The binding reads only the selected restored families and complete receiving maps. ResolveLiveEntity requires a real live candidate assignment and returns its qualified copied identity, not an Entity handle; ResolveHistoricalEntity additionally permits a matching retired assignment. Empty/mismatched IDs reject. Neither infers creation facts from the active branch or uses a process-global GUID dictionary. ReadResourcePartition checks exact resource family and `[0,4096)`; restored request identities are historical and do not require live players/actors.

There are deliberately **no** attachment/resource/component setters, control/event/result appenders, clock operations, canonical repair, hidden Step, RNG advancement, or binding-to-application permission on this grant. Animation checks the complete component/attachment bijection, exact Scene-instance expanded-key maps, controller/clip/Skeleton/Atlas backing, bindings, exclusive channels/motion actors, and section 5's retained resource joins. It rebuilds only derived binding accelerators/poses and request pin ownership. Restored source times, Seek cursor/latch/fraction/transition bits, and request values are unchanged. Particles validate prepared emitter content and build a fresh empty owner-local Generation=1/NextParticleId=1 pool and discontinuous output; they restore no omitted pool state.

Request-only assets must also work when their player/Scene was destroyed. Do not recover them from a current live Scene or perform asset I/O in JoinRestored. Close the higher-owned preparation object in `TerraFX.Animation`:

```csharp
internal sealed class AnimationPreparedRestoreAssets : IDisposable
{
    private AnimationPreparedRestoreAssets();
    internal static Task<AnimationPreparedRestoreAssets> PrepareAsync(
        AssetStore assets, AssetPackageLease packageLease,
        ReadOnlyMemory<byte> validatedEcsRecord,
        CompiledContentSchemaRegistry schemas,
        CancellationToken cancellationToken);
    internal void TransferTo(World candidate);
    public void Dispose();
}
```

PrepareAsync is called by the actual load preparation over its owned immutable validated target projection and exact retained package. It measures/reserves before resolving all active Animation and request-only controller/Clip dependencies; it validates content joins without a World/owner issuer or live lookup. It takes independent leases before escape, never retains a caller's bare mutable buffer, and returns a nonnull complete owner or the enclosing load's real failure. It is not a new public loader, replay source, or migration. Caller disposal/cancellation cannot free running work; a failed preparation unwinds actual leases before becoming terminal.

The prepared load state owns this object. TransferTo, called once by the actual candidate-construction path before the join, verifies the exact selected candidate/registration and prepared target projection/package relationship and transfers the existing ownership to that candidate's Animation instance. It cannot retarget by GUID. Disposal after transfer releases only the empty wrapper; before transfer it releases its own leases/credits, retaining unreleased cleanup. Candidate failure/World retirement owns transferred cleanup. Use the existing load-plan/source/candidate credits; do not call active-binding `AcquireGuidUse` on an unpublished candidate or borrow Scene command credits.

Recommended production ordering, anchored at `WorldLoadPreparedState.CreateRestoredCandidate` and `WorldReplacementOperation`:

1. Existing complete source/target projection validation and admission; prepare exact package-backed Scene and Animation/request-only assets under the load owner.
2. Construct/initialize the candidate; restore ECS values and all owner state; restore Scene instances/hierarchy; restore clock/controls, as today.
3. Transfer prepared Animation assets; call `CompleteM05RestoreJoin`. Failure cleans the candidate and leaves the old World installed.
4. Perform the existing fresh observational Validate/Count/Write and exact target-projection comparison **after** the join. A join that changes canonical bytes is a protocol failure, not permission to refresh expected bytes.
5. Prepare the coherent CPU cohort/selected empty particle roots with the restored frames, without timeline advancement. Revalidate exact stamps and unchanged canonical revision.
6. Prepare the restored marker/identity remap and the existing source/playback/lifetime handoffs. Under the existing replacement gate, install prepared roots and marker without callbacks, allocation, or further fallible work.
7. Retire only old ownership; report old retirement separately. No notifications, root-motion consumption, particle spawn receipt, or Seek traversal is replayed.

RecordedContinuation with this extension still rejects before candidate publication. StrictLocal/profile/continuation execution and M06/M07 feedback remain separate owner gates; nothing here activates them.

## 4. Typed version-selecting AssetReferences

### 4.1 Value and field-binding declarations

Introduce these **internal Entities-owned copied values**, not an Assets service or new wire record:

```csharp
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct WorldAssetReference
{
    private readonly int _assetId;
    private readonly int _schemaMajor;
    internal WorldAssetReference(int assetId, int schemaMajor);
    internal int AssetId { get; }
    internal int SchemaMajor { get; }
    internal bool IsValid { get; }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct OptionalWorldAssetReference
{
    private readonly byte _present;
    private readonly WorldAssetReference _value;
    internal OptionalWorldAssetReference(WorldAssetReference value);
    internal bool IsPresent { get; }
    internal WorldAssetReference Value { get; }
}

// Added internal members on ComponentFieldBinding<T,TAuthor>:
internal static ComponentFieldBinding<T, TAuthor>
    VersionedAssetReference<TRuntime, TAuthorReader>(
        ComponentFieldDescriptor field,
        ImmutableArray<ComponentStoragePath> storagePaths,
        Guid targetSchemaId, ImmutableArray<int> targetMajors,
        TRuntime runtime, TAuthorReader author)
    where TRuntime : struct, IComponentValueAccessor<T, WorldAssetReference>
    where TAuthorReader : struct, IComponentValueReader<TAuthor, WorldAssetReference>;

internal static ComponentFieldBinding<T, TAuthor>
    OptionalVersionedAssetReference<TRuntime, TAuthorReader>(
        ComponentFieldDescriptor field,
        ImmutableArray<ComponentStoragePath> storagePaths,
        Guid targetSchemaId, ImmutableArray<int> targetMajors,
        TRuntime runtime, TAuthorReader author)
    where TRuntime : struct, IComponentOptionalAccessor<T, WorldAssetReference>
    where TAuthorReader : struct, IComponentOptionalReader<TAuthor, WorldAssetReference>;
```

The first constructor requires both positive signed-32-bit values; no ushort truncation. Default is invalid `(0,0)` and IsValid=false; AssetId/SchemaMajor getters reject an invalid value. A field further restricts majors to its exact captured set. No CLR equality/hash member is required for execution, and neither value owns a lease or disposal authority.

The optional value is exactly nine packed bytes: presence byte plus the eight-byte pair. Default is absent with zero payload. Its constructor accepts only a valid pair, sets presence to one; Value throws when absent. Provider validation rejects presence other than 0/1 and nonzero absent payload. Optional accessors test IsPresent before reading, SetPresent installs both fields, and Clear writes the complete all-zero absent value; zero AssetId is never a present-null sentinel.

Both factories require the exact matching required/optional Record, Reference=Asset, nested `native/asset-reference` 1.0, exact field/registry, initialized unique complete storage paths, nonempty fixed target SchemaId, and initialized nonempty ascending unique positive majors. Capture readers/policy by value. Append private binding kinds VersionedAssetReference=12 and OptionalVersionedAssetReference=13, preserving existing 0–11; map them to Value and OptionalValue storage respectively. Do not reinterpret the existing int-based kinds or expose another public binding API.

The schema ID is a field constant, not stored in every component. Minor is exactly zero in this profile, verified against the actual selected asset tuple. The provider preserves the selected major; no “latest”, int-only projection, opaque Guid, native pointer, or silently chosen default major substitutes for it.

### 4.2 Complete v2 field/provider assignment

Use the rectification's exact layout/factory keys and factory version 1. New types are nested private implementation families of `RenderComponentDefinitions`; the table gives their complete named data. Runtime structs are sequential packed unmanaged storage, constructors assign **every** listed field, and authors are distinct immutable symbolic values with the corresponding complete constructor/get-only data. No other runtime-only component fields are introduced.

| Family | Complete fields, in tag order | Exact field roots |
|---|---|---|
| `MeshComponent2` / `MeshAuthor2` | 1 `WorldAssetReference Mesh`; 2 `ulong LayerMask`; 3 `byte PoseMode` | Tag 1 uses VersionedAssetReference, fixed `S("native/mesh")`, majors `[1,2]`. Tags 2/3 use existing Scalar ulong/byte roots. |
| `SpriteComponent2` / `SpriteAuthor2` | 1 `int AtlasAssetId`; 2 `Guid FrameId`; 3 `Vector2 Pivot`; 4 `Vector2 Size`; 5 `Vector4 Tint`; 6 `byte FlipMask`; 7 `int SortingLayer`; 8 `int SortingOrder`; 9 `OptionalWorldAssetReference Material`; 10 `bool PixelSnap` | Tag 1 retains the existing required int AssetReference to Atlas 1.0. Tag 9 uses OptionalVersionedAssetReference, fixed `S("native/material")`, majors `[1,2]`. All other tags use the unchanged exact scalar roots/kinds/predicates/defaults/annotations. |

The storage inventory is exactly one SchemaValue leaf per field: Mesh `(Value8,Value8,Value1)`; Sprite `(Value sizeof(int), Value sizeof(Guid), Value sizeof(Vector2), Value sizeof(Vector2), Value sizeof(Vector4), Value1, Value4, Value4, OptionalValue9, Value sizeof(bool))`. ComponentStoragePaths name the corresponding backing fields; the AssetReference leaf covers **both** int words, not only AssetId. Leaves are byte-ordered; declarations cover `Unsafe.SizeOf<T>()` without gaps/overlap, with every actual padding interval explicitly declared and initialized. Do not assert an unmeasured platform size by reusing the v1 layout.

The four new asset readers/accessors have these complete interface member sets:

| Concrete struct | Interface and members |
|---|---|
| `Mesh2AssetAccessor` | `IComponentValueAccessor<MeshComponent2,WorldAssetReference>`: `WorldAssetReference Get(in MeshComponent2 value)`; `void Set(ref MeshComponent2 value, in WorldAssetReference fieldValue)`. |
| `Mesh2AssetAuthorReader` | `IComponentValueReader<MeshAuthor2,WorldAssetReference>`: `WorldAssetReference Get(in MeshAuthor2 value)`. |
| `Sprite2MaterialAccessor` | `IComponentOptionalAccessor<SpriteComponent2,WorldAssetReference>`: `bool IsPresent(in SpriteComponent2 value)`; `WorldAssetReference GetPresent(in SpriteComponent2 value)`; `void SetPresent(ref SpriteComponent2 value, in WorldAssetReference fieldValue)`; `void Clear(ref SpriteComponent2 value)`. |
| `Sprite2MaterialAuthorReader` | `IComponentOptionalReader<SpriteAuthor2,WorldAssetReference>`: `bool IsPresent(in SpriteAuthor2 value)`; `WorldAssetReference GetPresent(in SpriteAuthor2 value)`. |

They are stateless structs with valid zero default, no constructors/configuration/disposal/World lookup. They read/write only their named field; field policy validation remains with the captured binding/provider. Remaining field accessors/readers implement the existing concrete typed scalar contracts over the table's exact fields; no generator change or guessed generated member is required.

Each family has one closed factory, default provider, restore provider and `IComponentStateCodec<T,TAuthor>` with the existing exact `Read`, `Validate`, `Count`, `Write`, `Restore`, and `RebuildDerived` signatures. Capture these roots once in `ComponentFactoryBinding<T>.Create<TAuthor>`, with Persistent/NotApplicable, initialized-empty Derived dependencies, exact schema 2.0, and the complete field list in tag order. RebuildDerived is unreachable for these Persistent families and must reject misuse, not issue a default valid component. Defaults still require explicit Mesh, or Atlas/frame, respectively; default initialization cannot invent AssetId 1 or a frame. Existing authoring defaults apply only where declared, never to missing required encoded fields.

Read validates the complete canonical outer record and exact reference record fields before issuing its issuer-bound author. The nested reference **header remains 1.0**. Its body is required tag 1 UInt32 checked in `[1,int.MaxValue]`, tag 2 RFC Guid equal to the field constant, tag 3 UInt32 in `[1,2]`. No fourth field or implicit minor exists. Restore copies the entire pair/optional value; it maps no AssetId through a World identity map. Validate checks the selected major and all unchanged component predicates, including PoseMode `[0,1]` and the real Animation/Skeleton binding for Animated. Count/Write serialize named fields in canonical tag order, not unmanaged memory; Write repeats the selected major exactly. Cancellation/consumption/short-destination/result issuance follow the existing component codec contract.

Actual package preparation switches on this retained major to the distinct Mesh1/Mesh2 or Material1/Material2 codec/CLR value family; each is loaded/pinned from the exact selected immutable package backing. Mesh2 material-slot references and particle-emitter Material references apply the same `(AssetId, fixed SchemaId, selected major, minor0)` rule in their **asset** codecs. This does not turn them into ECS component fields.

Mesh1/Sprite1 providers and tuples remain unchanged. Mesh1 cannot acquire Material2 because a v2 component referenced it; its own asset codec still enforces its original dependencies. Missing, mismatched, unsupported, or invalid **present** references fail; only an actually absent Sprite Material selects the existing default profile.

Distinguishing vector: alternative authored references `(AssetId=37, Mesh major1)` and `(37, major2)` differ in reference tag 3 bytes `01 00 00 00` versus `02 00 00 00`. They cannot collapse to one int on Read→Restore→Count/Write. These are alternative package inputs, not two same-ID assets in one content set. A v1 component reader still rejects the second. An absent optional Material differs from present `(37,2)`; `(0,2)` is invalid, not absence.

## 5. Complete root-motion Resource/State2 family

### 5.1 Resource membership and retained-definition closure

Register exactly the rectification's resource `S("resource/animation/root-motion/v1")`, unmanaged partition type `RootMotionRequest`, PartitionCount=4096, AllowParallelAccess=false. Factory identity is `S("resource/animation/root-motion/factory/v1")`, version 1. `S` uses the rectification/Chapter 10 namespace and exact name-UUID recipe; these keys are assignments, not unspecified IDs.

The ResourceState root remains `animation/root-motion-resource` 1.0 with required tag 1 Values RA(`animation/root-motion-slot` 1.0), **exactly 4096**, in partition order. Auxiliary slot/source roots remain their proposed 1.0 keys. Add **optional tag 13 Controller Asset(native/animation-controller,1)** to the slot, present iff Present=true. Its reference annotation is Asset; other annotations are None. Every slot's tags 2–13 are present iff tag 1 Present=true; otherwise only tag 1 is encoded. Sources is present-initialized-empty, not absent, when a valid identity request has no contributing sources.

Why tag 13 is necessary: stage 2 can produce A's request; stage 7 can destroy A and its player component/attachment row before save. The request legitimately survives with historical Player/Actor and source ClipSlotIds. Neither the live player map nor a bare Clip AssetId identifies the original controller's slot map/order. Retaining Controller in this resource provides that exact content join and its pin obligation, without serializing an Entity handle, retaining a dead player, or guessing a current definition. This is an explicit revision of an unimplemented proposed resource, not a stealth change to maintained/old wire data.

At each stage-2 resource installation, order Extract-mode players by Player EntityId RFC, assign their complete requests to a dense prefix, and write all remaining partitions absent. Ignore-mode players supply no request. This private partition assignment is not persistent entity-slot identity. Present Player IDs are unique/ascending; absent slots are a suffix. Every present Tick equals the executing tick, or the containing completed tick during capture/restore. Stage-7 destruction does not remove a slot. The next tick replaces the entire set before application reads.

Applications can register **Read only**, and access it only during stages 2 after the engine producer through 7, with the current executing tick check. Reject application Write access at Build and any attempted write/earlier/later access at invocation; do not rely solely on AllowParallelAccess=false. Applications use the ordinary declared Resource traversal/range route, never a new scope-free service. The restored last-tick array authorizes no movement callback; next-tick overwrite precedes any next-tick reader.

### 5.2 Complete copied and author data

Preserve the public RootMotionRequest/RootMotionSource members in the rectification. Add no public constructor, setter, Controller property, lease, or Dispose. Close private construction as:

```csharp
// TerraFX.Animation
internal RootMotionSource(Guid clipSlotId, int clipAssetId,
    long playSequence, long loopOrdinal, float weight);

internal RootMotionRequest(WorldEntityIdentity player, WorldEntityIdentity actor,
    long tickIndex, Vector3 translation, Quaternion rotation,
    RootMotionExtractionPolicy extractionPolicy,
    scoped ReadOnlySpan<RootMotionSource> sources, int controllerAssetId);
```

Both values are packed unmanaged copies. Source stores, in order, Guid ClipSlotId, int ClipAssetId, long PlaySequence, long LoopOrdinal, float Weight: 40 bytes. Request stores the following complete private inventory; use scalar float storage and construct TerraFX vectors/quaternions in getters, avoiding any assumption about geometry struct padding:

| Byte offset / extent | Private data and purpose |
|---|---|
| 0 / 1 | `_present`, exactly byte 0 or 1; schema Present Bool reader. |
| 1 / 16 | `_worldGuid`, DeclaredRuntimeOnly portable Guid, zero when absent; otherwise the containing World, never OwnerId. |
| 17,33,49 / 16 each | Player EntityId, CreationLineageId, CreationBranchId. |
| 65,81,97 / 16 each | Actor EntityId, CreationLineageId, CreationBranchId. |
| 113 / 8 | TickIndex. |
| 121,125,129 / 4 each | Translation X/Y/Z. |
| 133,137,141,145 / 4 each | Rotation X/Y/Z/W. |
| 149 / 1 | Extraction policy, exactly 1 for a present slot. |
| 150 / 1 | SourceCount, DeclaredRuntimeOnly bounded array-count storage, `[0,4]`. |
| 151 / 160 | Four inline Source values, each the exact 40-byte sequence above. |
| 311 / 4 | ControllerAssetId for tag 13. |

Request size is therefore 315 packed bytes; this is a **proposed layout**, not a measured runtime result. Declare the complete storage leaves/padding audit and verify `Unsafe.SizeOf` when implemented. No pointer/native/Entity/private-handle leaf or unmanaged raw-memory serialization is permitted. All absent storage and unused source tails are explicitly zeroed, including after reuse; SkipLocalsInit is not initialization.

Constructors require valid same-World qualified identities, nonnegative tick, finite Translation, the owning admitted quaternion predicate, Extract policy, positive ControllerAssetId, and at most four valid ordered source values. Source IDs/ClipSlotIds/PlaySequence are positive/nonempty, LoopOrdinal is signed, weights are finite positive contributors; do not normalize or repair invalid input. The array's stable controller slot order and normalized contributing weights are validated against the retained definition with the rectification's ordered blend arithmetic.

Default request is absent, **not identity motion**. Only IsValid may be queried on an unissued request; body getters, including SourceCount/GetSource, reject absent state. Source body getters reject its all-zero unissued default. GetSource bounds are `[0,SourceCount)`. A valid zero-source request has explicit positive-zero Translation and Quaternion.Identity, including the addendum's Seek tick; Restore preserves admitted float bits and never replaces an invalid quaternion with identity. SourceCount>0 does not imply nonidentity motion.

Use these complete immutable symbolic types, all internal in `TerraFX.Animation`:

| Type | Complete constructor/get-only data |
|---|---|
| `RootMotionResourceAuthor` sealed class | `RootMotionResourceAuthor(ImmutableArray<RootMotionSlotAuthor> values)`; `ImmutableArray<RootMotionSlotAuthor> Values`. Exactly 4096 initialized nonnull slots, no exposed mutable array. |
| `RootMotionSlotAuthor` sealed class | Constructor takes `bool present`, then nullable `Guid? playerId, playerCreationLineageId, playerCreationBranchId, actorId, actorCreationLineageId, actorCreationBranchId`, `long? tickIndex`, `Vector3? translation`, `Quaternion? rotation`, `ImmutableArray<RootMotionSourceAuthor>? sources`, `byte? extractionPolicy`, `WorldAssetReference? controller`, in that order. Corresponding PascalCase get-only properties; all twelve nullable values present iff Present, otherwise all null. |
| `RootMotionSourceAuthor` readonly struct | Constructor/get-only data: `Guid ClipSlotId`, `WorldAssetReference Clip`, `long PlaySequence`, `long LoopOrdinal`, `float Weight`, in that order. All required; Clip uses fixed `native/clip` major1/minor0. Default is invalid, never an omitted source. |

Constructors copy initialized immutable arrays, reject inconsistent optional arms/default arrays/null entries, and assign every field. Slot sources contain 0–4 entries; no sorting, normalization, or live resolution occurs in author construction. These authors own symbolic values only; read/prepare operation storage accounts for them. Actual package pins live in the separately transferred request companion owner.

### 5.3 Narrow typed aggregate field extension

Keep all public FixedArray APIs unchanged. Add only the internal RecordArray family required here; it does not authorize arbitrary resource services or unfinished feedback. `IComponentValueReader`, `IComponentOptionalReader`, and `IComponentArrayReader` below are the existing typed getter contracts, not delegates.

```csharp
// TerraFX.Entities
internal interface IWorldResourceOptionalArrayReader<TOwner, TElement>
{
    bool IsPresent(in TOwner value);
    int GetCount(in TOwner value);
    TElement GetElement(in TOwner value, int index);
}

internal sealed class WorldResourceRecordBinding<T, TAuthor>
    where T : unmanaged where TAuthor : notnull
{
    private WorldResourceRecordBinding();
    internal static WorldResourceRecordBinding<T, TAuthor> Create(
        CompiledContentSchemaRegistry schemas, ContentSchemaReference schema,
        ComponentStorageDeclaration storage,
        ImmutableArray<WorldResourceRecordFieldBinding<T, TAuthor>> fields);
    internal ContentSchemaReference Schema { get; }
    internal ComponentStorageDeclaration Storage { get; }
}

internal sealed class WorldResourceRecordFieldBinding<T, TAuthor>
    where T : unmanaged where TAuthor : notnull
{
    private WorldResourceRecordFieldBinding();
    internal static WorldResourceRecordFieldBinding<T, TAuthor>
        Value<TValue, TRuntime, TAuthorReader>(ContentFieldDescriptor field,
            ImmutableArray<ComponentStoragePath> storagePaths,
            TRuntime runtime, TAuthorReader author)
        where TValue : unmanaged
        where TRuntime : struct, IComponentValueReader<T, TValue>
        where TAuthorReader : struct, IComponentValueReader<TAuthor, TValue>;
    internal static WorldResourceRecordFieldBinding<T, TAuthor>
        Optional<TValue, TRuntime, TAuthorReader>(ContentFieldDescriptor field,
            ContentReferenceKind reference,
            ImmutableArray<ComponentStoragePath> storagePaths,
            TRuntime runtime, TAuthorReader author)
        where TValue : unmanaged
        where TRuntime : struct, IComponentOptionalReader<T, TValue>
        where TAuthorReader : struct, IComponentOptionalReader<TAuthor, TValue>;
    internal static WorldResourceRecordFieldBinding<T, TAuthor>
        AssetReference<TRuntime, TAuthorReader>(ContentFieldDescriptor field,
            ImmutableArray<ComponentStoragePath> storagePaths,
            Guid targetSchemaId, ImmutableArray<int> targetMajors,
            TRuntime runtime, TAuthorReader author)
        where TRuntime : struct, IComponentValueReader<T, WorldAssetReference>
        where TAuthorReader : struct, IComponentValueReader<TAuthor, WorldAssetReference>;
    internal static WorldResourceRecordFieldBinding<T, TAuthor>
        OptionalAssetReference<TRuntime, TAuthorReader>(ContentFieldDescriptor field,
            ImmutableArray<ComponentStoragePath> storagePaths,
            Guid targetSchemaId, ImmutableArray<int> targetMajors,
            TRuntime runtime, TAuthorReader author)
        where TRuntime : struct, IComponentOptionalReader<T, WorldAssetReference>
        where TAuthorReader : struct, IComponentOptionalReader<TAuthor, WorldAssetReference>;
    internal static WorldResourceRecordFieldBinding<T, TAuthor>
        OptionalRecords<TElement, TElementAuthor, TRuntime, TAuthorReader>(
            ContentFieldDescriptor field,
            ImmutableArray<ComponentStoragePath> storagePaths,
            WorldResourceRecordBinding<TElement, TElementAuthor> element,
            TRuntime runtime, TAuthorReader author)
        where TElement : unmanaged where TElementAuthor : notnull
        where TRuntime : struct, IWorldResourceOptionalArrayReader<T, TElement>
        where TAuthorReader : struct,
            IWorldResourceOptionalArrayReader<TAuthor, TElementAuthor>;
}

// Added internal member on WorldResourceFieldBinding<T,TAuthor>:
internal static WorldResourceFieldBinding<T, TAuthor>
    PartitionRecords<TSlotAuthor, TAuthorReader>(
        WorldResourceFieldDescriptor valuesField,
        WorldResourceRecordBinding<T, TSlotAuthor> slot,
        TAuthorReader author)
    where TSlotAuthor : notnull
    where TAuthorReader : struct, IComponentArrayReader<TAuthor, TSlotAuthor>;
```

Create captures the exact registry/schema/typed readers once. Auxiliary record descriptors are exact, finite and acyclic. Every direct field is bound exactly once in ascending tag order, including optional fields and the fixed three-field AssetReference helper. Child types/schema tuples must exactly equal the field's nested tuple. No role-mask search, string field lookup, opaque bytes, callback registration, recursive shape, foreign descriptor, null member, default collection, duplicate/uncovered storage, or inferred Guid reference is accepted.

Value requires required matching primitive geometry/scalar kind with Reference=None. Optional requires an optional matching kind and **exact** supplied reference; for this extension only None, or HistoricalEntity on Guid, is admitted. AssetReference factories require the section 4 reference shape/policy, not a primitive int. OptionalRecords requires an optional exact RecordArray and its bounded child binding. Presence is tested before a getter/count; absent Get/GetCount/GetElement is misuse. Present Sources count zero is legal, and every element index is `[0,count)`. Concrete reader structs are copied by value; no reader owns or resolves a live service.

PartitionRecords binds **one whole partition to one record element**, not an array inside each partition. Author Values count equals the frozen actual PartitionCount. Capture's existing `GetCount(valuesField)` returns 4096; `GetElement<RootMotionRequest>(valuesField,i)` returns that copied partition through the typed record family. `WorldResourceSchema.GetField` need not invent RecordArray-index `ComponentFieldPath` segments: Values is selected at the root, and child bindings use exact direct descriptors within their own record context.

Extend factory collection/validation and captured dispatch explicitly for this internal binding kind: collect the required Values RecordArray as one partition-domain field, then validate the child's entire closure/storage. The old required-FixedArray branch, descriptors, readers, count/range checks and ABI remain. `WorldResourceFactoryDescriptor.Fields` contains the exact root Values descriptor; the child closure stays captured in the same family. Runtime/author deep field traversal uses that child binding, not `ElementKind` pretending a RecordArray is a primitive FixedArray. Existing registration-domain checks apply to Values `[4096,4096]`; they must not compare a nested Sources `[0,4]` count to 4096.

The complete field/source binding set is independently derived from the three schema rows:

| Record / tag | Runtime reader source | Author reader source | Binding and reference |
|---|---|---|---|
| Resource 1 | Whole partition, index i | `Values[i]` | PartitionRecords, slot binding; count4096. |
| Slot 1 | `_present == 1` | Present | Value Bool. Raw presence is additionally checked as 0/1. |
| Slot 2 / 3 / 4 | Player EntityId / CreationLineageId / CreationBranchId storage | PlayerId / PlayerCreationLineageId / PlayerCreationBranchId | Optional Guid: HistoricalEntity / None / None. |
| Slot 5 / 6 / 7 | Actor EntityId / CreationLineageId / CreationBranchId storage | ActorId / ActorCreationLineageId / ActorCreationBranchId | Optional Guid: HistoricalEntity / None / None. |
| Slot 8 | TickIndex | TickIndex.Value | Optional Int64, None. |
| Slot 9 | Translation from three scalar lanes | Translation.Value | Optional Vector3, None, EngineDistance annotation. |
| Slot 10 | Rotation from four scalar lanes | Rotation.Value | Optional Quaternion, None. |
| Slot 11 | SourceCount and first SourceCount inline values | Sources.Value | OptionalRecords, source binding; present iff `_present==1`, count0–4. |
| Slot 12 | policy byte | ExtractionPolicy.Value | Optional UInt8=1, None. |
| Slot 13 | `(ControllerAssetId,major1)` | Controller.Value | OptionalAssetReference, fixed controller SchemaId, majors `[1]`. |
| Source 1 | ClipSlotId | ClipSlotId | Value Guid, None, nonempty. |
| Source 2 | `(ClipAssetId,major1)` | Clip | AssetReference, fixed Clip SchemaId, majors `[1]`. |
| Source 3 | PlaySequence | PlaySequence | Value Int64>=1, None. |
| Source 4 | LoopOrdinal | LoopOrdinal | Value Int64, None. |
| Source 5 | Weight | Weight | Value Binary32, None; metadata `[0,1]`, contextual contributor `>0`. |

All slot optional readers use the **same** Present gate; author nullable arms are checked for consistency, not interpreted independently. The Present gate is a shared read dependency, not multiple ownership claims over its storage byte. Storage claims cover each body leaf exactly once; Sources claims its inline storage with four 40-byte child layouts. WorldGuid and SourceCount are explicitly declared portable runtime-only derivations, not extra canonical fields; unused tails/absent bytes are validated zero. This supplies every required root/slot/source field, not just the two historical Guid leaves.

The concrete private reader roster is closed as `ValuesAuthorReader`, `Slot1Runtime`/`Slot1Author` through `Slot13Runtime`/`Slot13Author`, and `Source1Runtime`/`Source1Author` through `Source5Runtime`/`Source5Author`, nested in RootMotionResourceProvider. All are stateless readonly structs with valid zero default, no configuration constructor, ownership, or disposal. ValuesAuthorReader implements `IComponentArrayReader<RootMotionResourceAuthor,RootMotionSlotAuthor>`. Slot1 implements the corresponding Value reader pair over Bool; Slots2–7,8,9,10,12,13 implement the Optional reader pairs over Guid, long, Vector3, Quaternion, byte, WorldAssetReference respectively. Slot11 implements the OptionalArray reader pair over RootMotionSource/RootMotionSourceAuthor. Source1–5 implement Value reader pairs over Guid, WorldAssetReference, long, long, float. Runtime/author owners are exactly the types in the table. Their complete callable members are the selected existing interfaces' `Get`, or `IsPresent`/`GetPresent`, or `IsPresent`/`GetCount`/`GetElement`; Values has `GetCount`/`GetElement`. Each reads only its table source. No further selector/tag/type choice is left to implementation.

### 5.4 Context, complete provider, and State2 operations

The current resource bindings have no historical-identity/containing-World port. Add a **resource-only, engine-issued** reference binding and an internal aggregate-codec extension:

```csharp
internal sealed class WorldResourceReferenceBinding
{
    internal WorldResourceReferenceBinding(WorldResourceOperationControl operation,
        World world, long? requiredRequestTick);
    internal WorldResourceReferenceBinding(WorldResourceOperationControl operation,
        ValidatedWorldIdentityHistory sourceHistory, long completedTick);
    internal Guid WorldGuid { get; }
    internal long? RequiredRequestTick { get; }
    internal bool TryResolveHistoricalEntity(Guid entityId, Guid creationLineageId,
        Guid creationBranchId, out WorldEntityIdentity identity);
    internal void Revoke();
}

internal interface IWorldResourceRecordCodec<T, TAuthor> :
    IWorldResourceStateCodec<T, TAuthor>
    where T : unmanaged where TAuthor : notnull
{
    ImmutableArray<Diagnostic> ValidateSourceReferences(
        scoped ReadOnlySpan<byte> source, WorldResourceReferenceBinding binding,
        CancellationToken cancellationToken);
}

// Added internal getter on WorldResourceReadBinding<TAuthor>,
// WorldResourceValidationBinding, WorldResourceCaptureBinding,
// and WorldResourceRestoreBinding<T>:
internal WorldResourceReferenceBinding References { get; }
```

These are not public constructors or caller data. The real resource dispatch/source validator issues the reference scope from either the exact protected World ledger or the already validated complete source history. The supplied operation is the same resource/family operation, with its thread/failure/revocation control; source preflight uses a read-only validation operation and issues no World owner. The References getter rejects when that specific scope was not issued; old primitive resources need none. No binding fabricates a default WorldGuid/history, and no codec receives a World or ledger object.

TryResolveHistoricalEntity validates nonempty operands and exact resource operation before lookup. It returns true for a matching live **or retired** Entity assignment and its immutable creation directory facts, constructing the complete copied WorldEntityIdentity with the containing WorldGuid; false/default only for a well-shaped triple absent/mismatched in that history. It exposes no Entity handle, membership permission, active-binding grant, remap, or asset resolver. RequiredRequestTick is absent for fresh pre-tick defaults (which must all be absent), the executing tick for a produced live array, or the completed snapshot tick for capture/source/restore. Revocation happens with the parent binding in `finally`; all failures are checked by the real dispatch, even if a callback caught them.

Capture/restore registration dispatch must thread this exact context through its actual calls rather than constructing a test binding. Close the private call chain with these replacement/additional signatures; public provider interfaces and old wire behavior do not change:

```csharp
// Replacement operation entries on IWorldResourceRegistration:
void InitializeAndValidate(object storage, World world);
void ValidateForExecution(object storage, World world, long? requiredRequestTick);
nint CountStateRecord(object storage, World world, long completedTick,
    CancellationToken cancellationToken);
nint WriteStateRecord(object storage, scoped Span<byte> destination,
    World world, long completedTick, CancellationToken cancellationToken);
void RestoreStateRecord(scoped ReadOnlySpan<byte> source, object storage,
    World candidate, long completedTick, CancellationToken cancellationToken);

// New source entry on that same captured registration:
ImmutableArray<Diagnostic> ValidateSourceReferences(
    scoped ReadOnlySpan<byte> source,
    ValidatedWorldIdentityHistory sourceHistory, long completedTick,
    CancellationToken cancellationToken);

// Added lower-only members on WorldResourceOperationControl:
internal void BindReferences(World world, long? requiredRequestTick);
internal void BindReferences(ValidatedWorldIdentityHistory sourceHistory,
    long completedTick);
internal WorldResourceReferenceBinding References { get; }

// Additional internal constructor on WorldResourceReadBinding<TAuthor>:
internal WorldResourceReadBinding(Guid resourceId, ContentSchemaReference schema,
    int partitionCount, nint sourceByteLength,
    WorldResourceReferenceBinding references);

// Replacement erased Read entry on IWorldResourceCodecDispatch<T> and its implementation:
WorldResourceStateCodecReadOutcome Read(scoped ReadOnlySpan<byte> source,
    Guid resourceId, ContentSchemaReference schema, int partitionCount,
    WorldResourceReferenceBinding? references, CancellationToken cancellationToken);

// Replacement internal World entry; remaining restoration semantics are unchanged:
internal void RestoreEcsRows(ReadOnlyMemory<byte> ecsRecord,
    WorldPersistenceEcsContents contents, long completedTick,
    CancellationToken cancellationToken);
```

The registration creates its existing exact-family operation control, then calls BindReferences **once before any callback** for this aggregate family. BindReferences validates the active owner/thread/family, rejects rebinding, and constructs the reference binding against that control. Read/source preflight use a read-only Validation operation; no new public operation kind is needed. The aggregate Read constructor checks the same resource/schema/count and owns no replacement context. A null reference argument is legal only for the unchanged primitive-family Read branch; this aggregate rejects a missing context, never substitutes a default. Real dispatch revokes both the Read/reference scope and owning operation in `finally`; other bindings' References getters validate their existing operation first.

InitializeAndValidate supplies no required tick and admits only absent defaults. Live validation supplies the actual engine executing tick, or null during fresh initialization; capture supplies the protected committed/restored completed tick. Importantly, ECS/resource Restore currently precedes clock Restore: `WorldLoadPreparedState` passes `checked(Checkpoint.NextTick - 1)` into RestoreEcsRows and onward, rather than reading the candidate's not-yet-restored clock. The later clock restoration/join must agree with that validated source tick. This is a copied validated fact, not a way to issue World authority.

For old primitive families ValidateSourceReferences returns initialized-empty without invoking a new provider. For this aggregate it issues the source-history reference scope and invokes the frozen `IWorldResourceRecordCodec` root. The actual checkpoint target-validation path calls it after complete schema/identity/clock context is known and before candidate/factory/owner issuance, not from an application hook. Malformed source/count/history joins produce the enclosing load's existing rejected diagnostic channel; wrong-family binding, forged result, or provider count/consumption mismatch is protocol failure. This adds no acceptance ID or new generic migration mechanism.

Close the actual provider in `TerraFX.Animation`:

```csharp
internal sealed class RootMotionResourceProvider :
    IWorldResourceFactory<RootMotionRequest>,
    IWorldResourceDefaultProvider<RootMotionRequest>,
    IWorldResourceRecordCodec<RootMotionRequest, RootMotionResourceAuthor>
{
    internal RootMotionResourceProvider(CompiledContentSchemaRegistry schemas);
    WorldResourceFactoryBinding<RootMotionRequest>
        IWorldResourceFactory<RootMotionRequest>.Binding { get; }
    WorldResourceCodecBinding<RootMotionRequest>
        IWorldResourceStateCodec<RootMotionRequest>.Binding { get; }
    public RootMotionRequest Create(int partitionIndex,
        WorldResourceDefaultBinding binding);
    public WorldResourceReadResult<RootMotionResourceAuthor> Read(
        scoped ReadOnlySpan<byte> source,
        WorldResourceReadBinding<RootMotionResourceAuthor> binding,
        CancellationToken cancellationToken);
    public void Restore(in RootMotionResourceAuthor author,
        WorldResourceRestoreBinding<RootMotionRequest> binding,
        CancellationToken cancellationToken);
    public ImmutableArray<Diagnostic> Validate(
        scoped WorldResourceStateView<RootMotionRequest> value,
        WorldResourceValidationBinding binding,
        CancellationToken cancellationToken);
    public nint Count(scoped WorldResourceStateView<RootMotionRequest> value,
        WorldResourceCaptureBinding binding, CancellationToken cancellationToken);
    public nint Write(scoped WorldResourceStateView<RootMotionRequest> value,
        scoped Span<byte> destination, WorldResourceCaptureBinding binding,
        CancellationToken cancellationToken);
    public ImmutableArray<Diagnostic> ValidateSourceReferences(
        scoped ReadOnlySpan<byte> source, WorldResourceReferenceBinding binding,
        CancellationToken cancellationToken);
}
```

The constructor requires nonnull exact compiled root/slot/source/reference/controller/Clip metadata and creates the entire typed field/default/factory/codec family once. Use `WorldResourceFactoryBinding<RootMotionRequest,RootMotionResourceAuthor>.Create` with the internal PartitionRecords field, exact 315-byte storage declaration, and this default provider; pair `WorldResourceCodecBinding.Create` with that **same** family control. Public factory/codec interfaces have distinct explicit Binding properties. Do not return independently rebuilt equal descriptors or lazily change roots.

| Operation | Complete required behavior |
|---|---|
| Defaults | Check issued binding, exact resource/schema/count and index `[0,4096)`. Return an explicitly zero-initialized absent partition. No identity quaternion is written to an absent body; no package load, RNG, task, or callback escapes. Every World receives fresh arrays. |
| Source preflight | Validate complete exact root/slot/source membership, root count4096, presence arms, source count/order/value predicates, dense prefix, tick context and complete historical creation triples. Use existing descriptor reference validation for historical kind membership and the new typed root for the **triple** join; a matching Guid with wrong creation branch is invalid. Prepared exact controller/Clip assets validate slot/Clip membership/order, Extract policy and normalized weights before candidate use. These are complementary checks, not permission for lower code to load assets. |
| Read | Check cancellation, exact canonical bytes/versions/kinds/cardinalities/flags/unknown fields and complete consumption; bounds precede allocation. Build only the immutable symbolic author graph and issue it through the real one-result binding. Malformed schema/value/reference input rejects with existing ResourceRead failure categories and nonempty diagnostics; protocol misuse throws. No live map or package is guessed. |
| Restore | For all 4096 indices, resolve the present Player/Actor triples using References; construct present requests with the containing WorldGuid and exact copied source bits/controller, or a zero absent request. Set each partition exactly once through the actual Restore binding; no missed/duplicate index, Entity resolution, command, movement, event, or timeline step. Cancellation before publication abandons the candidate; partial candidate writes never become visible. Accepted CommitLoad continues with `CancellationToken.None` as today. |
| Validate | Validate raw initialization/unused-tail rules, deep typed field closure, arm/count/order/finite/quaternion/weight predicates, WorldGuid/tick, and complete historical triples. Return initialized diagnostics, empty only on success. A dead player/actor is not an error merely because it is dead. Do not rely on the current empty common ValidateFields implementation to supply these checks. Asset backing joins belong to the prepared owner/join, not a fabricated Resource validation asset service. |
| Count | Read all 4096 records through the exact captured Values/record sources under one scoped operation. Count canonical EBIN headers, optional presence, nested asset/source records, array indexes/alignment and complete overflow-checked size. Include tag 13. No native pointers, padding, runtime WorldGuid or SourceCount helper byte enters the wire. |
| Write | Use the same frozen traversal/presence/order as Count; verify destination size before writing and return exactly Count bytes. Emit one root, exactly4096 slots, all present slot tags1–13 or absent tag1 only, and the exact source records/AssetReferences. Preserve admitted float/quaternion/source order bits; do not sort/normalize/repair. Recheck cancellation at bounded record/slot boundaries and let it unwind the real operation. |
| Join/retirement | Section 3 binds exact prepared controller/Clip pins for every retained request, including historical-only rows. Rebuild no desired delta and mutate no resource field. Stage-2 replacement acquires new companion ownership before detaching old; old resource readers/capture uses and independently retained output pins keep their actual owners. Stop, player destruction, caller task disposal, canceled wait, Scene unload, or World closure is not evidence that a different retained request/output's asset use ended. |

The source preflight and candidate provider use the literal schema-derived membership, not production output as their own expected-value oracle. Constructor validation alone does not certify context, and successful Read alone does not certify package joins. Request companions and prepared authors fit the **existing** Animation 128-MiB/World/domain/coexistence limits, including all arrays, old/current/candidate values, pins, decode/read/restore workspaces, task/cleanup/quarantine graphs and retained readers. The proposed 315-byte partition is not an entitlement to allocate a peak without admission.

MotionFeedback's actual resource/provider/body/Pose Sync producers are **not selected**. Generic support for these specific request records is not feedback closure. Its resource fields/execution stay with the rectification's M06/M07 gate; imported animation and its adapters remain separately deferred.

## 6. Focused evidence to require during implementation

These are recommendations for the already applicable owner observations in the rectification, **not results, new case IDs, or a new qualification program**. Use ordinary owning-project NUnit/VSTest commands in `docs\project\Building.md`, Debug before Release, with nonzero actual selections when implementation occurs.

| Seam | Distinguishing evidence |
|---|---|
| Frozen composition | Old no-argument CPU support is unchanged. Register selected tokens in different permitted builder order, build two Worlds, prove fresh nonaliasing owners and exact inventories. Missing/conflicting prerequisites fail before issuance; a failing staged registration leaves no partial resource/component/attachment. |
| Real phase dispatch | Native-authored package→Scene→controller→Step→Headless path. An Animation system sees this tick's engine-produced request; Begin/Input and Extract request reads reject; application Write registration rejects. A Structural destroy removes the player attachment row but preserves this tick's historical request. No user System.Order is reserved to fake the engine phase. |
| Seek/event preservation | Use the addendum's multi-duration, masked/zero-weight mid-fade broadcast Seek, preserved fade/remainders/cursors, aggregate4096/4097 refusal and identity desired motion. A failure after a local Applied receipt publishes no provisional events/output; no command is redirected to a successor. |
| Cosmetic phase/identity | Real automatic+explicit spawn preparation, failed encompassing tick, recording retention and restore reset. Retain the addendum's exact P1/S/P2 UUID and mixed order; frame rebase never advances particles or resets IDs. An opaque root is not permission to bypass collision/permutation checks. |
| Component references | Literal major1/major2 Read→Restore→Count/Write for Mesh2 and absent/present Material2 in Sprite2; old v1 refuses target major2. Wrong fixed SchemaId, major0/3, UInt32 above signed range, absent nonzero payload, unknown fields, wrong backing and missing exact codec reject without default fallback. |
| Resource byte/field closure | Independent literal absent/present slot/source records, including tag13 and present-empty Sources. Test exact4096 domain, initialized tails, missing/extra tags, inconsistent arms, wrong historical creation branch, stale tick/World, bad quaternion/weights/controller slot order, and provider-supplied author count mismatch as protocol failure. A source count4095/4097 rejects before candidate issuance. |
| Historical-only restore | Sample a request, destroy player/actor/Scene at Structural, save through production Save, prepare/commit Standard Branch, and fresh-compare canonical bytes. Join retains exact request-only controller/Clip backing without a live player/Scene lookup; no motion/event replay. A same-GUID replacement uses its fresh exact construction binding. |
| Ownership/scope | Retain real prior snapshot/resource-capture/recording use through overwrite/close. Verify own operation U/W, complete peaks, reverse cleanup, and retained charge on cleanup failure. Caught active binding misuse is sticky; revoked/default/wrong-thread/foreign family cannot act. Test via actual World/load operations, not public/test-only binding issuers. |
| Restored cohort | Real restored marker/Headless extraction observes one complete frame/pose/particle-root generation; fresh Count/Write detects any illegal canonical join mutation. Rebase refusal cannot discard history. No hidden Step, public SetFrame, callback under installation gate, or unprepared actor may make the check pass. |

Managed behavior, compiled roots, public-only consumers, native/GPU execution, AOT, and full acceptance remain separate. Existing native loss/timeout/physical input observations remain where Chapter 35 already puts them. No such work is run for this proposal.

## 7. Frozen review boundary

Baseline HEAD: `31f156a51ccc462ae9c5cc8d4e87e00c0054dec4`. Freeze date: **2026-11-16 UTC**. The following **85 complete files** are the direct repository input set, including files read only in relevant ranges or exact symbol/field searches. Hashes are raw-file SHA-256. `C`=unchanged at baseline, `M`=modified, `U`=untracked. Modified/untracked inputs are the actual worktree bytes, not substituted HEAD versions.

Both named proposal hashes and their original 73- and 18-input manifests are separately reverified. Those transitive manifests overlap this table; their counts must not be added as distinct direct inputs. Directory/name discovery is context, not a claim to have reviewed every discovered file.

Before writing this output, Git reported **37** dirty/untracked paths. Freeze their complete path/status/content state using this exact canonical digest: obtain `git status --porcelain=v1 -z --untracked-files=all`; convert path separators to backslashes; sort paths by ordinal Unicode code point; concatenate `XY + TAB + path + TAB + uppercase raw-file SHA256 + LF` for every path; hash the UTF-8 bytes with no BOM. The frozen pre-existing dirty-state digest is `5105E639AA334302CA8006D9B1216753DE2BC048A587853D1A4F8E6E1AB25E91`. It includes nonreviewed dirty files only to protect their bytes/state, not disposition their contents.

**Verification limitation:** that original complete dirty-state digest no longer matches. Final bounded checks, excluding only this output, observe 37 paths and digest `60BABE3095AF0EEDA9F47A24B457F0B9AEB5B93B8F5DBF2FB7B48B453900F3A1`. All 85 direct hashes/states, both original proposal hashes/manifests, and HEAD still match. The aggregate alone cannot identify or attribute the changed nonreviewed dirty fact; no such file is edited or dispositioned here. Preserve the original mismatch, do not silently replace its baseline or claim full-worktree preservation/acceptance. The final observed state is reread for stability, with this proposal as the one path written by this task.

The output is also frozen without claiming an impossible recursive raw self-hash: the seal line below is SHA-256 over this complete file after replacing **only its 64 hexadecimal seal digits** with 64 ASCII zeroes. It covers the final manifest and all proposal text. The handoff additionally records and rechecks the ordinary raw-file output SHA-256 and full final dirty-state digest including the output. A later reviewer must verify those exact bytes/states; no acceptance transfers to edited inputs or proposal bytes.

Proposal-seal-sha256: 4FD4AA32826E80A2A507CACEBB24AF9386842DFAC73456CED20859814CD3EA65

| Input | State | SHA-256 |
|---|---|---|
| ``.editorconfig`` | C | `342683DE2B1C262624546F312F7D8E2078F89185ADD39F43F1FB5013376FD553` |
| ``.github\copilot-instructions.md`` | C | `12439DF4BB5E46021891B234BEC1104ABDCA57C11FDF3F610C8F32C5DAF3001E` |
| ``.github\workflows\ci.yml`` | C | `03EFEA672CD52E9EF053E9CE0285E7CA02C6CE6FB0D21268ADE4255ED5A39D58` |
| ``Directory.Build.props`` | C | `770EE4B2A422861A6F23F0D39DD0ECA361A81CB87E64EBAC4E12170F71231D7A` |
| ``Directory.Build.targets`` | C | `AEEC1D08E8037470239035812351C4045124607D67A354D482112F4D642D4105` |
| ``global.json`` | C | `1BCE01871321C2DF82E887DA428CE5C121A6C6541FE65017FE3D249BE47E190C` |
| ``sources\Directory.Build.props`` | C | `D44373AF315523F8AF9F09F36BDED2A688E2B5C1572FCC0732310C7EF590EAD8` |
| ``sources\TerraFX.Runtime\TerraFX.Runtime.csproj`` | C | `E019D4DBAD14FF2D5FF8D7365DDED0D09210575D5228A0D01CED70C99B3C2D33` |
| ``docs\project\Building.md`` | C | `F53AB58B89EEAE7E17B70970075BE9322A936D443BB6D5239C3238E594DEABC6` |
| ``docs\project\Roadmap.md`` | C | `C4FBE82155A6D82FFD2D13AD2F18056D938738C4FA5A66760C4877E24ED001C7` |
| ``docs\design\09-native-assets-and-source-adapters.md`` | C | `16730803987B66677CE6DA6BB829CCFFC435B614D51B507049DF8FD8988AA441` |
| ``docs\design\10-binary-data-and-authoring-formats.md`` | C | `56721136AECAE18C20EB3E9A1BECEB527CC0676DD6B43CB506538AD63DECBB5F` |
| ``docs\design\12-entities-components-and-queries.md`` | M | `174B2465746DA79CEF560C8506216EF4F99F9D1AABAC184F872C9E6CB0B94E0B` |
| ``docs\design\13-jobs-and-frame-pipeline.md`` | C | `0D7806B03A1D019215EB5C3912F10200A078F9B00A71A206FB979E93BBD18CDE` |
| ``docs\design\16-scenes-transforms-and-cameras.md`` | M | `B012F42B23CEDE9C7CDB71203D8357CD19E1626823EA25EF3119961292D3698A` |
| ``docs\design\20-animation-and-deformation.md`` | C | `BBEF0A36E0832C2BD0D54A07E479917DB923671B64F7176369F8F57A8F1618C5` |
| ``docs\design\27-state-serialization-replay-and-recovery.md`` | C | `B9B6324C5B1B1543CA1D2287ECE579597A7E7AA62D885031C4FF1BB629F08777` |
| ``docs\design\29-project-setup-build-and-deployment.md`` | C | `3B55A013EEC327ECD648ACBD5B53717DD218AD288E8047AC21277609EFA2F47B` |
| ``docs\design\30-public-api-review-surface.md`` | M | `15848C82F3858A77A87A0D3EA6FBCC07046EB6E77CA5B21748C72B90EFAB598B` |
| ``docs\design\35-implementation-roadmap.md`` | C | `24EEC6E92B7AD904D552F290EF748FFC8FCE13B6B94A89EBD9F39487D4584D87` |
| ``docs\proposals\m05-owner-contract-closure-rectification.md`` | U | `67134F02B92D316524848ECB8E0C917EF868DA3D2EDA5FC581DBAA25B1825632` |
| ``docs\proposals\m05-owner-contract-closure-rectification-addendum.md`` | U | `C8DE6581D5CB126D18C294FC9EB22A01459B2D6BFC3AD933ECB1652582634F4A` |
| ``sources\TerraFX.Assets\AssetReference.cs`` | C | `69BD4128428BE60A03459535657F01FE8C6321F11EA822760CDBCC471DF3ED3E` |
| ``sources\TerraFX.Assets\AssetReferenceScanner.cs`` | C | `EF0232BCC4F384FE6025A8F98CB11D7C6E70033FF36F31E796994A314AE67BBF` |
| ``sources\TerraFX.Assets\NativeAssetSchemaCatalogue.cs`` | C | `6B9537EA8CEFEBB48B5DC24261A8C03FB059E3314E7DA089F29A639D9E55620A` |
| ``sources\TerraFX.Graphics\RenderingSchemaCatalogue.cs`` | C | `71BD7A427756B16FC4404865A87A9FA711019EF3DD28D0F8DC860E4A9ADDE94C` |
| ``sources\TerraFX.Runtime\Animation\AnimationSupport.cs`` | C | `4A1AEE71A26A0B1214D730F5CB738D68E16D7C8CE1EC257318BD1CA05F124F56` |
| ``sources\TerraFX.Runtime\Entities\ComponentCaptureBinding.cs`` | C | `695EC25CB20835AB63D76650DC60C13BC01D7D1C1F7B33CDAC92A89F6B72C2B4` |
| ``sources\TerraFX.Runtime\Entities\ComponentFactoryBinding`1.cs`` | C | `96191847427574AB6E33A24203569B0958D9BEA8345411125CFC625FDB4D6284` |
| ``sources\TerraFX.Runtime\Entities\ComponentFieldBinding`2.cs`` | C | `939613DF41712DB452376EA0908E29F51CF7A056666E5FB900764DF59602D967` |
| ``sources\TerraFX.Runtime\Entities\ComponentFieldBindingKind.cs`` | C | `40EC3DDA50F26E2E53B1A78EABAF9E4F94088E388BFDD651ED610DA88DD5E717` |
| ``sources\TerraFX.Runtime\Entities\ComponentStorageDeclaration.cs`` | C | `649857BC76C3982A5FFFF47060D61FED9FE7F9F112A563AF52715400C8D9F573` |
| ``sources\TerraFX.Runtime\Entities\ComponentStorageKind.cs`` | C | `43452EE758E2BF06386BE240CB36BC90F5D1321E2DA4827CD8FFC226EF10A361` |
| ``sources\TerraFX.Runtime\Entities\ComponentStoragePurpose.cs`` | C | `16E450434FDC88B3C27EFE5944D179CC0825017BE74C1C624206536281FF9ACA` |
| ``sources\TerraFX.Runtime\Entities\CpuExtractionSupportRegistration.cs`` | C | `A7031ACC5DA182D427A4ED18FE103F0A8EE17747059019C08DBC60E5AC16237A` |
| ``sources\TerraFX.Runtime\Entities\IComponentArrayReader.cs`` | C | `C5E4ED9C5298B917844851BDB7671DDC6CB7EAF826180EA48B9D3A5012963782` |
| ``sources\TerraFX.Runtime\Entities\IComponentOptionalReader.cs`` | C | `087620FD6E4AD6C3B40821CE630D9D3C5F42AE7CC79A7205101CC5403CA00A97` |
| ``sources\TerraFX.Runtime\Entities\IWorldCpuExtractionParticipant.cs`` | C | `0892E55100F00C0884E730C256303B204F06F963CFCA62CFAFC5130BE0EFE622` |
| ``sources\TerraFX.Runtime\Entities\IWorldCpuExtractionSupportRegistration.cs`` | C | `C7000DB44CAC3E8D75F5D69460D613B1BEC030A62AC20D47F8DE3D6BBEAFAE37` |
| ``sources\TerraFX.Runtime\Entities\IWorldResourceFieldAccess`1.cs`` | C | `1262DECE7DA25EDD09C5513783E7FD1CC9459ED926ABD2899B1ED4210330DB2D` |
| ``sources\TerraFX.Runtime\Entities\IWorldResourceRegistration.cs`` | C | `33150C0A51E172E5FFD9CDBB50A281392AEE71B64F3448082078233506DF3841` |
| ``sources\TerraFX.Runtime\Entities\IWorldResourceStateCodec`2.cs`` | C | `AEDDE44A544E36017482582F9BD6EF8EA62A6B409E3682304A6AE94CAD6C5279` |
| ``sources\TerraFX.Runtime\Entities\IWorldSceneParticipant.cs`` | C | `62A76E371DF1A81B631216B5ED16D9BAC7915F430A22D59FCE8F17D3AA4BDFFC` |
| ``sources\TerraFX.Runtime\Entities\ResourceFactoryFamily`2.cs`` | C | `73B98AF20C28E970B4589E7DE0F5B90BB6B1F0345E301E81BEE13247DD15A022` |
| ``sources\TerraFX.Runtime\Entities\World.cs`` | M | `AD884C9DF4F9B48DB4751518237535BC11A67820EA1F96FC66CAE850935A7B31` |
| ``sources\TerraFX.Runtime\Entities\World.Controls.cs`` | C | `4DD4B837C256D0FF5F120CCD4F71EC1BA6A732C41A64484E1375DC1F931402FF` |
| ``sources\TerraFX.Runtime\Entities\World.CpuExtraction.cs`` | C | `971EE49B1CCD9C3C629E3CA33198F26BE610FE3D28B4236E371C220201D768FC` |
| ``sources\TerraFX.Runtime\Entities\World.PersistenceCapture.cs`` | C | `482F45529A56BF60936A1CAF43282828476A1A2D0F52C2DC94EB3646349B0250` |
| ``sources\TerraFX.Runtime\Entities\World.PersistenceClockRestore.cs`` | C | `3E4A73CC5E30078FDA19440998AD16D59EDE4CAA92D69809374CB9137E0BA345` |
| ``sources\TerraFX.Runtime\Entities\World.PersistenceReplacement.cs`` | C | `42F902FCD6D3FB719FAEDF462BC4522CE3F7639EC7D168AA95A2C27629AC9F3A` |
| ``sources\TerraFX.Runtime\Entities\World.PersistenceRestore.cs`` | C | `CB87DC7B8645EB07B58084E6F4A6E772E085A86883D2D7716A82CBAD965CBE0F` |
| ``sources\TerraFX.Runtime\Entities\World.Systems.Access.cs`` | C | `A60B29870B7A399B2C1D1E2C179DFFC9001198546FA99172A82B12A33F3F5E6A` |
| ``sources\TerraFX.Runtime\Entities\World.Ticks.cs`` | C | `5DAAC62FA95D6E2FC665576C5DCFC6D1CDFCFAE0F345E60A5F06098FDBCF7F05` |
| ``sources\TerraFX.Runtime\Entities\WorldAttachmentFieldBinding`2.cs`` | C | `F6B403BF4AB98AAC2CAC24B3D9EC85138C58D12803134B9EB86CF3DE07D60756` |
| ``sources\TerraFX.Runtime\Entities\WorldEntityIdentity.cs`` | C | `09B2A54CC3ADDEE27331A9BB5D4B6486C27CA506B5B3A8F2C2D022BA49AC4A2C` |
| ``sources\TerraFX.Runtime\Entities\WorldFrameReadLease.cs`` | C | `082B507BE6340D31C0B13302F16867EAA431F792052CE06D57712576CB53293F` |
| ``sources\TerraFX.Runtime\Entities\WorldPersistenceAssetIdSetWriter.cs`` | C | `7870193A4552AAE5C94C0D836DEB1362F69916BC18EEE00A5713F79E49655684` |
| ``sources\TerraFX.Runtime\Entities\WorldPersistenceEcsReader.cs`` | C | `8089307A8BF1FCD2CC789FD100DFFC8995BB91200896C99507DAD5221CBA06D8` |
| ``sources\TerraFX.Runtime\Entities\WorldPersistenceRegistrationCaptureCoordinator.cs`` | C | `8BB6D7C5477A317C0A6436D11E2D1384E412FF76B72E8F25B4DFB7DA50F2D1B8` |
| ``sources\TerraFX.Runtime\Entities\WorldRegistrationBuilder.cs`` | C | `BB60686B9C2B2090F01D9B9C1C27F3D22DFE692EB6B5F71D652B662F61BD9CA7` |
| ``sources\TerraFX.Runtime\Entities\WorldResourceCaptureBinding.cs`` | C | `E293A651873145E9EC126ECC566DF31A58EA832904AE57F70C28141235AFB66E` |
| ``sources\TerraFX.Runtime\Entities\WorldResourceCaptureData`1.cs`` | C | `FECDB8B8F90D5BA175F8E7D6C903713778692A81CA0C28C5ADEB32F84189734C` |
| ``sources\TerraFX.Runtime\Entities\WorldResourceCodecBinding`1.cs`` | C | `2CEFB29B7291F43B3A0748C988E094223AD4D94118FF73D29AC12A5EF2EC9CB0` |
| ``sources\TerraFX.Runtime\Entities\WorldResourceCodecDispatch`2.cs`` | C | `3B3C76F7545015E945437AA999AC82D42BA60A11BE5B55DDF0C196A07F177B7D` |
| ``sources\TerraFX.Runtime\Entities\WorldResourceFactoryBinding`2.cs`` | C | `551A85B6B8B0D0A31EC75B526C7C744EF6BFCFA0CCF4535859C3FF780A46034C` |
| ``sources\TerraFX.Runtime\Entities\WorldResourceFactoryDescriptor.cs`` | C | `FBB94742E6180F80291F47967767920DB404FE1DCAD01FF0454C0EF084622CE8` |
| ``sources\TerraFX.Runtime\Entities\WorldResourceFieldBinding`2.cs`` | C | `5AECB7B86050406E5AD70F21D1C355F0B97E559D4729CD4C478675566E109164` |
| ``sources\TerraFX.Runtime\Entities\WorldResourceOperationControl.cs`` | C | `412807B98C8C71AD17E169A53D63DCADE1408BDC138316D8C2D7A8CBE59E261C` |
| ``sources\TerraFX.Runtime\Entities\WorldResourceReadBinding`1.cs`` | C | `3E6FE156C2A28F6FB085B9A98B533E0AA81F8FCEFBDF50DF0E782721F41809C4` |
| ``sources\TerraFX.Runtime\Entities\WorldResourceReadFailureKind.cs`` | C | `8152897AF409403543C49FE0F37413A2D1657195651F5E3B388E030F509BB085` |
| ``sources\TerraFX.Runtime\Entities\WorldResourceRegistration`1.cs`` | C | `3AB22DF1D073B35578982700D55AB8C4C22AF152E668C89236CF1C7880BEC57B` |
| ``sources\TerraFX.Runtime\Entities\WorldResourceRestoreBinding`1.cs`` | C | `956F078BE58B652335AE25F0A577266A6EE116AF9B9AA2D20EED7EC559E82592` |
| ``sources\TerraFX.Runtime\Entities\WorldResourceSchema`1.cs`` | C | `E5D2D8500A02E5D0AE560CF572BE2BEBA57F8DB457771D9AF45CA03A179B3860` |
| ``sources\TerraFX.Runtime\Entities\WorldResourceStateView`1.cs`` | C | `D96B2991D83BDEC634907CEB38461C4148CB91DD73360F7FEA5C992C268B3921` |
| ``sources\TerraFX.Runtime\Entities\WorldResourceValidationBinding.cs`` | C | `C5BED2DFD1FC87F1BDF1AD4C89FAE523798D8925C238D57B0F32D374A9D6FBA9` |
| ``sources\TerraFX.Runtime\Entities\WorldSystemFieldBinding`2.cs`` | C | `5BDE9D007578BDD9214CC52CFD5019EBD7FEA37C4C8695B576AC01DFB63E42D6` |
| ``sources\TerraFX.Runtime\Hosting\WorldPersistence.cs`` | C | `BC2D253ADFF70F8A14301FCE2ACEBA65F20578258BE1C94434B4DE9F558849A6` |
| ``sources\TerraFX.Runtime\Hosting\WorldLoadPreparedState.cs`` | C | `0AAFE467247DCECFF3EDCFD30F36A19DA8070CEBA7FCF83B8B9ACB44B7B59A28` |
| ``sources\TerraFX.Runtime\Hosting\WorldReplacementOperation.cs`` | C | `C6DFA0EE44438C005E115FE9A15FE51643CF49176FB266A59AE5CFE67250EFF5` |
| ``sources\TerraFX.Runtime\Rendering\RenderMeshComponentDefinitions.cs`` | C | `167D1ADF373643E47C6F489A61D35F51FE6F4829349C82FDBC2131B0E772A20F` |
| ``sources\TerraFX.Runtime\Rendering\RenderSpriteComponentDefinitions.cs`` | C | `F29807904A88273C7FB7E28076453A8465BFDF7D3F50D4A3733CA759E6A83CE7` |
| ``sources\TerraFX.Runtime\Rendering\Renderer.CpuExtractionSupport.cs`` | M | `EAF1F94AFF0886B3FA30AF97BD7F83180A38AB744D4241D0CDD275A055EADB55` |
| ``sources\TerraFX.Runtime\Scenes\ISceneExpansionResourcePreparer.cs`` | M | `67AE936843F6329D95D12B784A4686A8B75B2CCEB3932159E56BDD24F21CA423` |
| ``sources\TerraFX.Runtime\Scenes\SceneExpansionPlan.cs`` | M | `8FFCDE62282341B8C5E782D5262C0917002D1B48E35A5098CC9B4B360BEEFAE9` |
| ``sources\TerraFX.Runtime\Scenes\ScenePersistenceRestorer.cs`` | C | `698F36FB6207D09B395AF7A0DA85FF132DC8E7ED549E9B4E804DF5D6728348C9` |

**Verification record:** all 85 direct hashes/states, both named proposal hashes, the 73- and 18-input inherited manifests, and HEAD match. The original complete dirty-state check **fails as recorded above**; the proposal is not an all-worktree freeze/acceptance result. After the final text/seal edit, the normalized seal, raw-file output hash, final observed 37-path non-output digest, and full final dirty-state digest including this output are frozen and independently reread before handoff. Report any further mismatch instead of changing a reviewed input. This is file/hash/state inspection only.
