# M05 owner-contract rectification

## Status, authority, and scope

**Proposed owner declarations, tentatively approved for implementation sequencing by the current user direction; retained for later review.** This document supplies concrete choices left open by `docs\proposals\m05-owner-contract-closure.md`. Its declarations are recommendations, not descriptions of members already implemented or amendments to maintained design chapters. The earlier proposal remains useful semantic guidance; use the choices below together with the explicit declaration holdpoints in section 6. Do not interpret semantic closure as evidence that every supporting implementation port already exists.

The deliverable is this proposal only. No maintained contract, production source, test, milestone status, package, commit, or remote publication is changed. No build, test, native execution, or acceptance is claimed. The input manifest at the end freezes the actual reviewed worktree bytes, including modified and untracked inputs.

### Observed boundary

| Observed fact | Owning evidence and consequence |
|---|---|
| M05 is in progress; dynamic Animation, particles, LOD, and picking remain gated. | `docs\project\Roadmap.md`, M05 and Next work. Existing static rendering, GLB/OBJ, shadows, public consumers, and their accepted subsets are preserved. |
| Chapter 35 permits dependency-ready CPU work without an undifferentiated M03/M04 exit. It requires declarations, schema assignments, construction, ordering, and lifetimes before the affected implementation. | `docs\design\35-implementation-roadmap.md`, capability sequencing and unit handoff. A declaration, managed test, actual producer, native observation, AOT execution, and complete case acceptance are different gates. |
| Animation codecs and sampling exist, not a live controller/state/event producer. | `AnimationSupport.cs`, `AnimationClipAsset.cs`, `AnimationClipAssetBinding.cs`, `AnimationClipSamplingKernels.cs`. Clip bounds qualification is presently only zero. |
| The public event base is more specified than implemented. | Chapter 30 declares `CommittedWorldEvent.Schema`, `WorldOwnerId`, `CompletedTick`, and `Sequence`; `CommittedWorldEvent.cs` currently has only an internal empty constructor. Implementing that already-specified base is a producer prerequisite, not another design decision. |
| Current Mesh decoding is exactly 1.0, with one index/group set and major-1 references. | `RenderingSchemaCatalogue.cs`, `RenderMeshAssetCodec.cs`, `RenderMeshAsset.cs`. Adding tags to its existing tuple, or assuming it can load a major-2 Material, is incorrect. |
| Asset codec registrations require both unique exact schema tuples and unique CLR value types. Multiple metadata versions are allowed. | `AssetStoreRegistrationBuilder.cs`, `CompiledContentSchemaRegistry.cs`. New root versions need distinct codec value types, not two registrations for the existing sealed type. |
| Existing World CPU support registers components only and requires Scenes support. | `WorldRegistrationBuilder.cs`, `CpuExtractionSupportRegistration.cs`, `Renderer.CpuExtractionSupport.cs`. It is not a hidden Animation/particle system or resource registration hook. |
| Existing mesh extraction constructs BindPose palettes. Snapshots retain assets, but do not yet capture the proposed exact binding/qualified draw provenance. | `Renderer.Extract.cs`, `RenderSnapshot.cs`, `RenderSnapshotAssetPins.cs`. Picking cannot reconstruct old creation identity from a current bare Entity GUID. |
| The independent-use route exists in dirty implementation inputs. Its existence is not independent-consumer qualification. | Chapters 04/12/16/30, `World.AcquireGuidUse`, `WorldLifetimeControl.AcquireUse`, `WorldGuidUseControl`. Picking must fund its own envelope and execute its real reconciliation path. |
| Focused sampling, BindPose, and public static consumer test sources exist. | The three test inputs in the manifest. They were read, not executed here, and do not establish the four new features. |

### Preserved boundaries

- Keep the thirteen-product DAG and the logical owners in Chapter 29. Core stays BCL-only; Assets and Graphics acquire no Runtime references. Entities retains lower-owned opaque registrations/ports, not Animation, Scenes, or Rendering types. Runtime does not depend on Content.Tools. Integration, not Physics, interprets Entity/root-motion bindings.
- Preserve native Skeleton/Clip 1.0, Scene/Prefab 2.0, Mesh/Material/render-component 1.0, all M02 fixture bytes, the existing static support factory, and the bounded GLB/OBJ adapters. No implicit migration or source-adapter expansion follows.
- Asset identity remains a positive signed-32-bit `AssetId` in one content set. “Clip/Mesh/Material version” below means that ID under its exact retained immutable package backing and schema tuple. It is not a new GUID, per-asset digest, or “latest” service.
- Do not promote cube sky, IBL, morphs, meshlets, GPU compute skinning, authoritative particles, UI picking, or Physics picking. M08/M09 qualification and existing physical-input/loss/timeout deferrals are unchanged.

## 1. Shared declaration and schema closure

### 1.1 Schema notation and version selection

Use Chapter 10's EBIN1 kinds, flags, alignment, cardinalities, numerical rules, and `TerraFXNameUuidSha256V1` recipe. In this proposal, `S(k)` is **the actual assigned identity**, computed from exact ASCII key `k` under namespace `2ed8b750-e1a0-4b46-9000-000000000001` by that frozen recipe. It is not an unassigned placeholder. Every new key below selects `(S(key), 1, 0, Content)` unless its row explicitly says 2.0. No new namespace, wire kind, role, owner enum, extension-ID scheme, or foundation digest is introduced.

Notation: `R(k)` is one complete exact nested record; `RA(k)[a..b]` is a record array; `FA(K)[a..b]` is a fixed array of the named permanent kind. `?` means optional, omitted when absent; otherwise tags are required, including empty arrays. `Asset(k,v)` means the existing `native/asset-reference` 1.0 record, whose ID is valid and whose SchemaId/Major select `k`/`v`; minor selection is exactly zero in this profile. `F`, `G`, `V2`, `V3`, `V4`, `Q`, and `P3` mean Binary32, Guid, Vector2/3/4, Quaternion, and SpatialPosition3. Integers have their stated signedness. All geometry is TerraFX geometry, not BCL numerics.

Unmentioned annotations are Unit=None, Reference=None, Authoring=None. Native roots are `NativeAsset`; helper records are `Auxiliary`; explicitly identified components are Persistent/NotApplicable. Other records are NotComponent/NotApplicable. References inside source assets are source keys, not live World references. The existing codecs and tuples remain unchanged.

| Proposed catalogue addition | Exact membership and executable registration |
|---|---|
| `AnimationSchemaCatalogue.PlaybackSchemas` | Exactly the Animation rows in sections 2.2–2.4, excluding the separately identified feedback rows. Existing `Schemas` retains its eleven current rows. |
| `AnimationSchemaCatalogue.FeedbackSchemas` | Exactly the feedback rows in section 2.6. Metadata may be compiled early; the actual Integration producer is selected only with its Physics prerequisite. |
| `RenderingSchemaCatalogue.M05Schemas` | Exactly Mesh 2.0, Material 2.0, render/mesh 2.0, render/sprite 2.0, the LOD helper, and particle rows below. This is Core-only descriptor metadata in Graphics, not Runtime factories in Graphics. |
| Existing native and continuation catalogues | No row is rewritten. `state/ecs` already admits exact registered AttachmentState/ResourceState payloads; `state/control` admits registered ControlPayload. Their role selections do not activate unimplemented codecs. |

`native/mesh` 2.0 and `native/material` 2.0 get distinct internal immutable roots `RenderMeshAsset2` and `RenderMaterialAsset2` and their own exact codecs. Their 1.0 roots/codecs stay registered where needed. There is no codec alias from 1.0 to 2.0. A World may select only one active component version for a given schema GUID; compiling both descriptors does not permit registering both CLR component families in that World.

### 1.2 Proposed public registration members

These are additions, not existing methods:

```csharp
// TerraFX.Rendering
[Flags]
public enum RendererCpuFeatures
{
    Static = 0,
    Animation = 1,
    CosmeticParticles = 2,
    MeshLod = 4,
    Picking = 8
}

public sealed class RendererCpuExtractionOptions
{
    public RendererCpuExtractionOptions(RendererCpuFeatures features);
    public RendererCpuFeatures Features { get; }
}

// Members on the existing Renderer:
public static CpuExtractionSupportRegistration CreateCpuExtractionSupportRegistration(
    RendererCpuExtractionOptions options);
public static AssetStoreRegistrationBuilder RegisterM05AssetCodecs(
    AssetStoreRegistrationBuilder builder, RendererCpuFeatures features);
public static CosmeticParticleSupportRegistration CreateCosmeticParticleSupportRegistration();

// TerraFX.Animation; members on the existing AnimationSupport:
public static AnimationSupportRegistration CreateWorldSupportRegistration();
public static AssetStoreRegistrationBuilder RegisterPlaybackAssetCodecs(
    AssetStoreRegistrationBuilder builder);

// TerraFX.Entities; sealed opaque tokens, internal constructors only:
public sealed class AnimationSupportRegistration { /* no public members */ }
public sealed class CosmeticParticleSupportRegistration { /* no public members */ }

// Members on the existing WorldRegistrationBuilder:
public WorldRegistrationBuilder RegisterAnimationSupport(AnimationSupportRegistration support);
public WorldRegistrationBuilder RegisterCosmeticParticleSupport(CosmeticParticleSupportRegistration support);
```

The token comments mean no additional public member set; their internal implementations hold the exact lower-owned frozen factory bindings. Null references throw. Unknown feature bits reject. `Static=0` is admitted and is exactly the existing no-argument CPU-support composition; it does not select new component versions. `RegisterM05AssetCodecs` with zero registers nothing. With a nonzero selection it registers Mesh 2.0 and Material 2.0; it additionally registers the emitter root only for CosmeticParticles. It does not duplicate the four codecs registered by the existing `Renderer.RegisterAssetCodecs`. The Animation method registers only the controller-definition root below, not the existing Skeleton/Clip codecs again.

For any nonzero CPU selection, enlist the version-2 Mesh/Sprite component families, the unchanged three light families, and the particle component only when selected. The Animation bit requires the exact Animation support token in the same World; CosmeticParticles requires its exact particle token. MeshLod requires its versioned content/selection producers. Picking requires qualified draw provenance and the Material/picking path, not Animation or LOD flags. Missing descriptors, duplicate/conflicting tokens, incompatible versions, or missing prerequisites reject Build atomically, before World issuance. No feature silently enlists another support token.

The two new builder methods use staged registration like the existing CPU method, but have **their own explicit allowed inventories**:

| Support | Registered storage and work |
|---|---|
| Animation | `animation/player` component; one World-scoped transferred immutable `animation/world-state` attachment family; the read-only-to-applications root-motion resource; closed stage-2, structural-membership, capture/restore-join, cohort, and retirement actors. No application `IWorldSystem` order is reserved or used as a substitute for engine-before-Animation work. |
| Cosmetic particles | `render/particle-emitter` component and one noncanonical per-World pool actor, with stage-8 preparation, stage-9 swap, cohort, reset, and retirement bindings. No canonical particle resource, system state, attachment, or RNG stream is registered. |

The lower registration interface exposes only the existing typed registrations and the named engine phase/cohort lifecycle; it cannot enumerate arbitrary services or call Hosting. New engine phase bindings are private, scoped, same-World/nonreentrant capabilities, issued only by actual World phase dispatch. They permit only the selected participant's prepared storage, declared component/resource access, and exact ownership transfer. They are revoked in `finally`; active misuse is sticky under Chapter 13. No public factory, reflection discovery, arbitrary callback, or test binding constructor is added.

### 1.3 Construction, publication, and cohort order

The proposed construction sequence is:

1. Compile exact old and selected new descriptors, with every nested reference closed. Freeze typed Assets codecs separately from World factories.
2. Register Scenes, the selected Animation/particle tokens, CPU support/options, application systems, resources, and attachments. Build validates the complete graph and read/write permissions.
3. World construction reserves all standing participant/storage/cleanup backing under the existing World/domain limits. Construct empty per-World owners; no package load, hidden player, native service, or tick occurs.
4. Real `SceneLoader.PrepareAsync` expands the actual package-backed Scene. Rendering and Animation prepare their measured immutable bundles from that exact plan/backing. Animation resolves its definition, clips, Skeleton, Atlas/pages, and target map before activation. It reuses the already expanded Scene map, not recursive Scene activation through a Clip's Scene reference.
5. Atomic activation revalidates the complete receiving membership and source map, reserves player/emitter state and pins, and installs all selected participants with the Scene or none. Merely loading a Clip does not instantiate a player.
6. Stage 2 applies admitted Animation commands, advances unaffected players, prepares poses/root requests/events, and installs that local canonical state before application Animation systems. Stage 7 removes destroyed bindings after complete structural projection. Stage 8 prepares cosmetic state and immutable output; stage 9 publishes only prepared roots/values by the existing callback/allocation-free marker.
7. Actual owner-idle `Renderer.Extract` reads one committed/restored cohort and copies the selected pose, particle, LOD, camera, and asset roots. Render/native work never follows live ECS references.

The original four-actor M03 composition remains unchanged when no extension is selected. The extended composition freezes the following preparation dependency order: SceneHierarchy(0), Camera(1), AnimationPose(4, if selected), ParticlePlane(5, if selected), ParticleCameraFacing(6, if selected), LodHistory(7, if selected), CpuExtraction2(2), CpuExtraction3(3). Keys identify actors; this list defines extended preparation order. Within a failure category the lowest frozen key, then stable entry order, still selects the primary diagnostic as Chapter 16 requires. Disabled dimensions omit their unselected actor; selected empty actors prepare stamped empty payloads.

Use Chapter 16's exact stamps, Check/Prepare/reverse-Abort/no-Install-callback/Retire protocol and one lower-issued read lease. A frame-only rebase transports current/previous pose bases, live particle spatial positions' derived representations, and every materialized LOD-history entry without advancing ages/timelines, changing canonical spatial positions, or resetting history. It cannot discard a cache to pass capacity/precision admission. Restoration prepares the same coherent roots before its restored marker, without public `SetFrame`, hidden Step, or replayed notifications.

### 1.4 Finite owner envelopes

These are **proposed ceilings, not measured footprints or added budget entitlements**:

| Owner | Count/byte closure |
|---|---|
| Animation | 4096 players; 8 layers/player; 4 active clips/layer; 32 allowed clips and 64 transitions/definition; 4096 distinct Scene targets plus at most 256 joints/definition; 128 pending commands/World; 4096 events/player/stage and 65536 Animation events/World/tick; 128 MiB total Animation-owned canonical/derived/staging/retained storage. |
| Animation command operation | Fixed W=1 MiB per accepted command, plus the existing 256-byte independent-use control. Includes request/task/receipt, command pin wrappers, validation and cleanup not already charged elsewhere. No escaping child operation. Assets retains its separate decode/lease charge. |
| Cosmetic particles | 1024 live emitters, 131072 live particles, 128 pending explicit spawn requests, 65536 accepted-stimulus records/recording segment; 128 MiB pool/working/output-retention subceiling, of which recording backing is at most 64 MiB. At most four independently retained recording outputs. |
| Particle spawn operation | Fixed W=64 KiB plus 256-byte independent-use control, covering the finite command/task/result/pin/cleanup envelope, not the separately reserved pool backing. |
| LOD | 8 levels/mesh; aggregate 1048576 indices and 65536 groups over all levels; at most 2097152 current camera/instance history slots; 128 MiB history/current/candidate/scratch/retirement subceiling. |
| Extended CPU outputs | 128 MiB additional current/prepared/independently retained M05 output storage, including copied palettes, weighted Sprite instances, billboard records, and per-camera selection arrays. Existing draw/light/bone ceilings still apply. |
| Picking | 4 unreconciled/unreleased operations per exact World; table count at most 131072; fixed W=64 MiB plus 256 bytes per operation. GPU images, native allocation footprints, staging and ordinary submission owners additionally require their existing Graphics budgets. |

All subceilings also fit the exact World’s existing 2-GiB owned-state ceiling, applicable resource/system sublimit, scheduling budgets, and exact admission domain. Count maxima do not promise joint byte fit. Reserve the complete old/current + new + scratch + retained-reader/output + task/result/pin + cleanup/quarantine peak before allocation or callback. Use actual qualified backing/header/alignment sizes; no arbitrary per-object estimate refunds real storage. Independently retained immutable output needs a storage/pin reservation, not active-World authority solely because it contains copied GUIDs.

An independent asynchronous binding operation acquires its own U/`W_u`/`D_u` through the existing exact `World.AcquireGuidUse` route before allocating or escaping. Its charge is `256+W`, not Scene preparation’s cap. Transfers move only actually covered bytes; closure/cancellation never ends a running use. Cleanup failure anchors the remaining graph in that use or its actual storage owner, without quarantining an active binding. Old immutable outputs retain their original backing through CPU recording and actual GPU completion. No receipt drop, finalizer, or canceled wait refunds live storage.

Append these proposed `RenderExtractionLimitKind` values without changing current values DrawInstances=0, Lights=1, BonePaletteEntries=2: `M05OwnedBytes=3`, `LodHistoryEntries=4`. Full-count precedence is existing draws/lights/bones, then LOD entries, then bytes. Byte diagnostics report the complete requested simultaneous charge and the actual limiting byte ceiling. Refusal returns no snapshot and installs no output/history. Invalid source data remains an exception, not capacity or background.

## 2. Dynamic Animation and root motion

### 2.1 Typed public and private storage shape

Retain every existing controller method/signature in Chapter 30, including required `AnimationPlaybackOptions`, explicit Seek policy, and animation-stage receipts. Complete the missing request as:

```csharp
namespace TerraFX.Animation;

public sealed class AnimationTransitionRequest
{
    public AnimationTransitionRequest(Guid transitionId);
    public Guid TransitionId { get; }
}

public enum AnimationDirection : sbyte { Reverse = -1, Forward = 1 }
public enum RootMotionExtractionPolicy : byte { Extract = 1 }

public readonly struct RootMotionSource
{
    // Engine-issued; no public constructor.
    public AssetId Clip { get; }
    public Guid ClipSlotId { get; }
    public long PlaySequence { get; }
    public long LoopOrdinal { get; }
    public float Weight { get; }
}

public readonly struct RootMotionRequest
{
    // Engine-issued; default is an absent resource slot, not identity motion.
    public bool IsValid { get; }
    public WorldEntityIdentity Player { get; }
    public WorldEntityIdentity Actor { get; }
    public long TickIndex { get; }
    public Vector3 Translation { get; }
    public Quaternion Rotation { get; }
    public RootMotionExtractionPolicy ExtractionPolicy { get; }
    public int SourceCount { get; }
    public RootMotionSource GetSource(int index);
}

public static class AnimationResources
{
    public static Guid RootMotionRequestsId { get; }
}
```

`TransitionId` must be nonempty and resolve in the receiver's retained definition at application. The RootMotion structs are unmanaged copied data, with no leases, Entity handles, public setters, or disposal authority. `GetSource` accepts `[0,SourceCount)` only. Valid identity motion explicitly contains zero Translation and `Quaternion.Identity`; a default request has `IsValid=false` and no usable motion body. A valid request may have zero contributing sources and identity motion.

The fixed resource identity is `S("resource/animation/root-motion/v1")`. Animation support registers 4096 fresh World-owned `RootMotionRequest` partitions with `AllowParallelAccess=false`, using the existing Resource kind and typed resource/factory/State2 mechanisms. Applications may declare Read only; Write registration rejects. Application reads are admitted at stage 2 after the engine producer and through stages 3–7, with the current executing tick checked; there is no scope-free `GetRootMotion` service. A movement system also declares membership and actual Integration body-command access as needed.

**Persistence disposition:** this bounded resource stores the last locally prepared tick's copied requests, explicitly included in canonical capture through its ResourceState codec. It is not an uncharged ephemeral service. Its historical player/actor IDs remain legal after stage-7 destruction; its clip pins/active AssetIds survive until the slot is overwritten or actually retired. Restore installs those copied records but issues no request callback and applies no movement. On the next tick the engine overwrites all slots before application reads.

Canonical playback lives in one **private World-scoped transferred immutable attachment family**, `AnimationRuntimeState` / immutable symbolic `AnimationRuntimeAuthor`. Its exact payload is `animation/world-state` below. A caller cannot construct or install that private CLR type. The Animation participant replaces a completely prepared immutable value under a closed engine phase grant; it does not mutate an object returned by `TryGetAttachment`. Its frozen factory supplies Defaults, Read, Validate, Count, Write, Restore, typed reference readers, and Release. Asset pins, binding accelerators, poses, and palettes are separately owned derived storage, never encoded pointer fields.

`AnimationPlayerComponent` and its immutable symbolic author are private typed component families selecting `animation/player`; the component stores the controller-definition AssetId, not a player handle. They use the actual typed component factory/layout/field/codec path, including a complete storage inventory and padding audit. Native Scene authoring is the initial production author; no public mutable author graph or guessed generated component members are required.

### 2.2 Native authoring schemas

All rows in this section have owner Animation. Arrays described as semantic order retain authored order; readers do not sort them to repair input.

| Key / role | Complete proposed fields |
|---|---|
| `native/animation-controller` / NativeAsset | 1 Skeleton? Asset(native/skeleton,1); 2 Scene? Asset(native/scene,2); 3 Clips RA(native/animation-clip-slot)[1..32], semantic order; 4 Layers RA(native/animation-layer)[1..8], ascending unique tag-1 index; 5 Transitions RA(native/animation-transition)[0..64], ascending unique tag-1 RFC ID; 6 RootMotionMode U8{0 Ignore,1 Extract}, author default 0; 7 MotionActorKey? G, source ExpandedEntityKey. |
| `native/animation-clip-slot` | 1 ClipSlotId G, nonempty; 2 Clip Asset(native/clip,1). Slot IDs and AssetIds are independently unique within the definition. |
| `native/animation-layer` | 1 Index U8[0..7]; 2 Mode U8{0 Override,1 Additive}; 3 Weight F[0..1], default 1; 4 Mask FA(F)[0..4352], each [0..1]; 5 ReferencePose RA(native/animation-reference-pose)[0..4352]; 6 InitialClips RA(native/animation-clip-weight)[0..4], clip-slot semantic order; 7 EventPolicy U8{0 Both,1 TargetOnly,2 None}, default 0. |
| `native/animation-reference-pose` | 1 TargetDomain U8{0 Scene,1 Joint}; 2 TargetId G/EntityOrJoint; 3 Pose R(scene/local-transform,1). Ascending unique `(TargetDomain,TargetId RFC)`. |
| `native/animation-clip-weight` | 1 ClipSlotId G; 2 Weight F[0..1]. Unique slots; zero-weight entries remain explicit until a defined retirement step. |
| `native/animation-transition` | 1 TransitionId G; 2 LayerIndex U8[0..7]; 3 SourceClipSlotId? G; 4 TargetClipSlotId G; 5 DurationTicks I64[0..long.MaxValue] @TimeSpanTicks, author default 2000000; 6 Priority I32, default 0; 7 Condition R(one of the three condition rows); 8 InterruptPolicy U8{0 RestartFromCurrentBlend,1 RejectWhileTransitioning}, default 0; 9 Action R(one of the two action rows); 10 EventPolicy U8{0 Both,1 TargetOnly,2 None}, default 0. |
| `native/animation-condition-manual` | No fields. Eligible only through an explicit transition request. |
| `native/animation-condition-completed` | No fields. Fires on a new Completed edge of the named source, or any active source when tag 3 is absent. |
| `native/animation-condition-always` | No fields. Eligible at the next stage-2 evaluation; at most one automatic transition/layer/tick. |
| `native/animation-action-none` | No fields. |
| `native/animation-action-layer-weight` | 1 LayerIndex U8[0..7]; 2 Weight F[0..1]. |
| `animation/player` / Component Persistent | 1 Controller Asset(native/animation-controller,1), Reference=Asset, PrefabEditable. No CLR player ID or implicit optional default. |

The definition's runtime root type is proposed public opaque `AnimationControllerAsset`, with no public constructor or additional public members. Its exact contextual codec is registered by `RegisterPlaybackAssetCodecs`.

Layer indices are contiguous from zero. Resolve all clip slots and transition operands against that exact definition. Source/target equality on an explicitly source-qualified transition rejects. Masks use one independently derived target domain: all exact Skeleton joints plus the union of Scene target keys in the allowed clips, ordered domain then RFC ID. Joint order in the Skeleton itself stays topological. Mask count equals that complete domain's count; a zero-target definition has an empty mask. Empty ReferencePose means bind-local Skeleton poses and the saved Scene baseline; otherwise it covers that domain exactly once. No reference is inferred from a wrapper GUID.

Skeleton is present iff the selected clips require it; every such clip selects the same exact Skeleton/backing. Scene is present iff Scene tracks or MotionActorKey require it; Clip Scene references, when present, equal it and the exact activated root Scene asset/backing. Extract requires a Skeleton with the named parentless unit-scale root, at least one extractable clip, and a nonnull MotionActorKey. Ignore forbids MotionActorKey and leaves visual root tracks intact. The native Clip's existing root policy zero maps explicitly to the proposed enabled extraction mode; enum numeric equality is not assumed.

The receiving player may be a Mesh child while MotionActorKey names its body/root Entity. Resolve both through its exact SceneInstance's complete expanded-key map. The receiver's live provenance selects the instance; bare authored ID, parent guessing, a same-GUID successor, and implicit Clip Scene activation are forbidden. All joint output for a skinned render receiver uses that receiver's exact Skeleton. Two active players cannot own the same Scene target/channel or the same motion actor. Reject the complete activation/rebind, not whichever player happened to register second.

Scene Position tracks on descendants replace parent-local translation. On a Scene root, their values are **displacements from the saved root baseline**: set RootPosition to baseline plus sampled displacement by admitted spatial arithmetic and keep LocalTransform.Position exactly zero. Rotation/Scale use the corresponding baseline fields. This is an explicit playback-binding choice, not reinterpretation of vector bytes as a global position. Reject direct control of a Physics-owned dynamic root. An application moves that root through body commands instead.

Sprite-frame targets use base layer 0 in Override mode; additive Sprite-frame channels reject. Up to four positive-weight clip instances may yield four ordered weighted Sprite subdraws; a two-clip crossfade yields exactly the specified two. Transform/joint channels support all eight layers. Additive references and admitted additive scale keys must be positive; a later sampled invalid ratio/nonfinite result faults rather than divides or repairs it.

### 2.3 Canonical state and controls

These rows are Animation-owned. `animation/world-state` is AttachmentState; `animation/root-motion-resource` is ResourceState; `animation/control` is ControlPayload; other rows are Auxiliary.

| Key | Complete proposed fields |
|---|---|
| `animation/world-state` | 1 NextCommandSequence I64[1..long.MaxValue]; 2 Players RA(animation/player-state)[0..4096], ascending unique ActorEntityId RFC. This array is the complete live player-component set. Pending accepted commands must be empty at capture. |
| `animation/player-state` | 1 ActorEntityId G/Entity; 2 SceneInstanceId G/SceneInstance; 3 Controller Asset(native/animation-controller,1); 4 Speed F[-4..4]; 5 NextPlaySequence I64[1..long.MaxValue]; 6 LastSampleTick? I64[0..long.MaxValue]; 7 Bindings RA(animation/target-binding)[0..4352], domain/key order; 8 Layers RA(animation/layer-state)[1..8], contiguous index order; 9 MotionActorId? G/Entity. |
| `animation/target-binding` | 1 Domain U8{0,1}; 2 SourceKey G; 3 EntityId? G/Entity, present iff domain 0; 4 Baseline R(scene/local-transform,1); 5 RootBaseline? P3 @EngineDistance, present iff the mapped Scene Entity is a root. Joint baseline equals its exact Skeleton bind-local pose. |
| `animation/layer-state` | 1 Index U8[0..7]; 2 Weight F[0..1]; 3 Clips RA(animation/active-clip)[0..4], definition clip-slot order; 4 Transition? R(animation/transition-state). Mode/mask/reference/event policy come from the retained definition, not duplicated mutable settings. |
| `animation/active-clip` | 1 ClipSlotId G; 2 PlaySequence I64[1..long.MaxValue]; 3 LocalTicks I64[0..long.MaxValue]; 4 PreviousTicks I64[0..long.MaxValue]; 5 Fraction B[24..24]; 6 LoopOrdinal I64; 7 PreviousLoopOrdinal I64; 8 Weight F[0..1]; 9 ForwardCursor U32[0..1048576]; 10 ReverseCursor U32[0..1048576]; 11 StartDelivered Bool; 12 Completed Bool; 13 LastDirection I8{-1,1}. |
| `animation/transition-state` | 1 TransitionId G; 2 ElapsedTicks I64[0..long.MaxValue]; 3 Fraction U16[0..999]; 4 SourceWeights FA(F)[1..4], aligned with active clip-slot order and each [0..1]; 5 TargetClipSlotId G. |
| `animation/root-motion-resource` | 1 Values RA(animation/root-motion-slot)[4096..4096], partition order. |
| `animation/root-motion-slot` | 1 Present Bool; 2 PlayerId? G/HistoricalEntity; 3 PlayerCreationLineage? G; 4 PlayerCreationBranch? G; 5 ActorId? G/HistoricalEntity; 6 ActorCreationLineage? G; 7 ActorCreationBranch? G; 8 Tick? I64[0..long.MaxValue]; 9 Translation? V3 @EngineDistance; 10 Rotation? Q; 11 Sources? RA(animation/root-motion-source)[0..4]; 12 Policy? U8=1. Tags 2–12 all present iff Present=true; otherwise all absent. WorldGuid is the containing World, never a stored OwnerId. |
| `animation/root-motion-source` | 1 ClipSlotId G; 2 Clip Asset(native/clip,1); 3 PlaySequence I64[1..long.MaxValue]; 4 LoopOrdinal I64; 5 Weight F[0..1]. Definition order; positive contributing weights normalize under the same ordered blend rule. |
| `animation/control` | 1 Operation U8{0 Play,1 Seek,2 Transition,3 LayerWeight,4 Stop}; 2 PlayerId G/HistoricalEntity; 3 CommandSequence I64[1..long.MaxValue]; 4 Clip? Asset(native/clip,1); 5 Speed? F[-4..4]; 6 PositionTicks? I64[0..long.MaxValue] @TimeSpanTicks; 7 SeekPolicy? U8{0,1}; 8 TransitionId? G; 9 LayerIndex? U8[0..7]; 10 Weight? F[0..1]. Play requires 4/5 only; Seek 6/7 only; Transition 8 only; LayerWeight 9/10 only; Stop none. Unused tags are forbidden. |

No encoded default repairs a missing required field. Active times/cursors/slot membership, transition duration/fraction, exact Scene map, qualified creation facts, and complete component/state bijections are contextually validated before use. `Fraction` is a signed 192-bit two's-complement little-endian integer with the bounds below, not arbitrary opaque state. Transient handles, package pointers, local array indices, GPU bytes, padding, and process owner IDs are excluded.

Capture/restore uses the existing actual State2 attachment/resource envelopes and closed typed reference ports. Asset capture includes the controller definition, its prepared allowed clips/Skeleton/Scene/Atlas/Material dependencies, and request-slot pins still in actual use. Control history is historical, not permission to activate an old clip. The existing global `state/control` wrapper supplies its normal sequence and application-tick facts; preserve all applied controller controls in command order within existing retained-control bounds.

First implement **Standard Branch** save/restore through the production path. Read and validate symbolic state; establish complete receiving Entity/Scene maps; restore Persistent components and the private attachment/resource values; then run a closed Animation-owned **post-restore join** before CPU cohort preparation. Attachment Restore does not acquire an undeclared component-read capability. The join validates/rebinds retained assets and targets and rebuilds CPU poses without advancing timelines or replaying events. Fresh Count/Write must equal the validated target projection before publication.

Animation state belongs in canonical hashes, including exact fractional/cursor/transition bits. This does **not** extend `state/continuation`'s current finite movie. A RecordedContinuation request involving this extension is explicitly unsupported before candidate publication; do not capture an animation control stream and call it executable continuation. StrictLocal snapshot/profile checks, when selected and implemented, are separate from movie execution. This proposal supplies no new Input/Physics/audio continuation permission.

### 2.4 Timeline, transitions, events, and command lifetime

**Timeline arithmetic.** For fixed rate `r`, let `Q = r * 2^149` and `I = exactBinary32Speed * 2^149`, an exact signed integer, including subnormals. Each ordinary advance computes `n = Fraction + 10000000 * I`, `delta = trunc(n/Q)`, and `Fraction = n - delta*Q`. Thus `-Q < Fraction < Q`. The largest intermediate is below 2^176 and fits the specified signed 192-bit representation for `r<=1000` and speed `[-4,4]`. Sampling uses the resulting integral native TimeSpan ticks through the existing kernels; sub-tick time is carried, not repeatedly rounded away or flushed. Preserve speed signed-zero bits, although either zero advances nothing.

Loop arithmetic uses checked quotient/remainder, not per-loop stepping. LoopOrdinal is signed; reverse wraps decrement it. Clamp clips hold their terminal pose and issue Completed once per play. Seek accepts a nonnegative TimeSpan: Loop uses quotient/modulo, Clamp clamps to its interval. Seek resets Fraction to zero; Play resets it and all layer timelines. All overflow/nonfinite motion is explicit failure before installing the affected candidate state.

Play requires the lease's AssetId and exact backing to identify one allowed slot; uniqueness above makes the existing operands sufficient. It resets all layers to their definition's initial states, then replaces base layer 0 with that slot at weight one and the requested global player speed. Stop empties every layer, clears transitions, releases active-playback pins, and samples the configured bind/baseline no-active pose without Completed. Play/Seek establish this stage's sample and suppress ordinary advancement for that player in that stage; a later ordinary tick advances it. Other commands do not invent an extra tick.

Commands apply in accepted positive sequence order at stage 2. Multiple commands have their individual receipts/events; a later command does not erase an earlier occurrence. Start-at-zero markers fire once on explicit Play. Forward intervals are `(previous,current]`; reverse intervals are reversed half-open intervals in reverse traversal order. At a forward wrap, preceding-duration events precede new-zero events. Reverse uses the inverse boundary ordering. Pose-only Seek emits none; explicit crossed-event Seek pre-counts and wholly refuses 4097, while 4096 is admitted if the complete tick envelope also fits.

Pre-count normal advancement and explicit command events using interval/loop arithmetic before materializing them. The 4096/player/stage and 65536/World/tick bounds include markers and Completed, across commands and layers. A command whose own demand cannot fit is Rejected without state/cursor/pin transfer; ordinary advancement exceeding the bound faults the tick before Animation state installation. Reliable events are never truncated. Earlier unrelated tick writes still have Chapter 13's nonrollback semantics.

Override blending uses stable definition clip order, positive-total normalization, linear position/scale, and incremental shortest-arc quaternion blending with `t = nextWeight / (previousTotal + nextWeight)`. Zero total means bind/baseline pose. Apply layers in index order; mask weight zero leaves the previous layer unchanged. Additive rotation/composition/scale ratios and root replacement use Chapter 20 exactly.

Transition conditions are data, not code. Evaluate automatic candidates from the pre-transition state; manual requests for that layer take precedence that tick. Choose greatest signed Priority, then ascending RFC TransitionId. Apply at most one automatic transition/layer/tick, so Always cannot recurse. Completed consumes one new play-completion edge, not a continuously true flag. Actions execute once in layer order after choices are frozen.

RestartFromCurrentBlend records the current ordered normalized clip weights as the new source vector; **source clips continue sampling**, rather than storing a frozen pose or creating a fifth hidden clip. Blend that vector linearly to a single target over simulation elapsed time, independent of clip speed. Reuse an already active target at its current time; a new target starts at zero with a fresh PlaySequence. Refuse before mutation if the union exceeds four active clips. At completion remove zero-weight outgoing clips and release their playback pins. Zero-duration transitions switch immediately. RejectWhileTransitioning returns a rejected command while a transition is active. Source weights, elapsed ticks and remainder are canonical, making mid-interruption restore closed.

Both selects each active source/target occurrence once, including zero-weight boundary occurrences until their defined retirement; TargetOnly selects the target during transition and all active clips outside transition; None suppresses that layer's marker/Completed emissions. The same clip active in two layers is two intentional occurrences.

Complete the event bodies as proposed sealed engine-issued descendants in `TerraFX.Animation`:

| CLR type | Get-only body, in addition to the already-declared `CommittedWorldEvent` base |
|---|---|
| `AnimationMarkerEvent` | `WorldEntityIdentity Player`, `AssetId Clip`, `Guid ClipSlotId`, `byte LayerIndex`, `long PlaySequence`, `long LoopOrdinal`, `AnimationDirection Direction`, `TimeSpan LocalTime`, `Guid EventId`, `string Name`. Name is the copied native marker's bounded unnormalized UTF-8 text. |
| `AnimationCompletedEvent` | Same source fields through LocalTime; no EventId or Name. |

The matching EventPayload schemas are `animation/event-marker` and `animation/event-completed`. Both have tags 1 PlayerId G/HistoricalEntity; 2 CreationLineage G; 3 CreationBranch G; 4 Clip Asset(native/clip,1); 5 ClipSlotId G; 6 LayerIndex U8[0..7]; 7 PlaySequence I64[1..long.MaxValue]; 8 LoopOrdinal I64; 9 Direction I8{-1,1}; 10 LocalTicks I64[0..long.MaxValue] @TimeSpanTicks. Marker additionally requires tag 11 EventId G/Event and tag 12 Marker R(native/event-marker,1). Completed forbids 11/12. WorldGuid comes from the containing publication. No caller-created event, arbitrary payload CLR type, or raw native marker masquerades as an issued event. The selected asset payload set is exactly native/event-marker 1.0; another payload schema is unsupported for this first live producer even if inert asset loading can preserve it.

For this M05 composition the concrete committed producer set is exactly those two Animation types. Order by player EntityId RFC, layer index, clip-slot authored ordinal, then chronological occurrence ordinal for that clip traversal; at a common occurrence put marker records in traversal EventId order and terminal Completed after terminal markers. Preserve loop end-before-start within that traversal. Assign the containing batch's dense `Sequence` only after this merge. No Physics/Integration descendant is activated by that table; a later combined composition must explicitly enlist its real producer and preserve its owner ordering.

The controller borrows the exact World and has no Dispose/World-close authority. Admission validates pure shape first, funds its own operation, retains the Clip before returning Play's task, and captures the exact target binding. Stage-2 application revalidates the live receiver and exact Scene/definition backing. Accepted playback takes a **separate** pin; command cleanup cannot release it. Definition/Scene pins may independently retain the same backing after Stop. Queue-full, history/cursor exhaustion, binding rejection, and byte refusal produce an animation Rejected receipt with an Error diagnostic; they are not Applied. Usage errors throw synchronously. Pre-admission cancellation admits nothing; after acceptance the token does not undo work. Complete tasks asynchronously outside locks/callbacks.

An Applied receipt acknowledges local stage-2 application, not stage-9 commit, rendering, or GPU completion. A later tick failure publishes no provisional events/output and requires existing whole-World recovery; it does not retract that receipt or promise rollback. World close rejects pending unapplied commands, drains actual work, and releases pins only after actual use; no command is redirected to a successor World.

### 2.5 Render producer and distinguishing evidence

`render/mesh` 2.0 (section 4) is the explicit animated-pose opt-in. PoseMode=1 requires the same Entity's admitted player and exact Skeleton. Version 1 remains BindPose even if an unrelated player exists. CPU output copies current bone models, `inverseBind * currentModel`, validated inverse-transpose normal data, current/previous pose provenance, and weighted Sprite subdraws. No previous palette is fabricated. Spawn/Play/Seek/Stop/content change/lost history marks discontinuity; a pure rebase transports valid history.

Count every positive-weight Sprite subdraw and particle billboard before culling/batching, once across cameras. Palette entries still count once per skinned instance, not per group/camera; the ceiling remains 2048. Unknown animated bounds, including current Clip BoundsQualification=0, force conservative drawing and finest LOD with a diagnostic. No new bounds qualification value is selected. A selected singular/nonfinite palette fails CPU extraction before native graph construction; it does not become an identity palette.

Required focused tests/evidence, not results:

- Native literal definition/state records: exact new tuple/tag/role/default/cardinality/order membership; missing tags, duplicate slots, cross-backing binding, ambiguous repeated-prefab authored IDs, wrong Skeleton and conflicting target ownership reject. Two instances of the same Scene bind the same Clip to different qualified World entities through their own expanded maps.
- Retain current sampling regressions. Add a public package→Scene→controller→Step→Headless Extract path, not directly constructed Clip/snapshot proof. Test speed ±4 and adjacent outside binary32 values, signed zero, subnormal carry, 60-Hz integral boundaries, reverse, long quotient seeks, event endpoints, 4096/4097, and ordinary tiny-loop event exhaustion without materialization.
- One-second loop with markers at 0 and 1 s: explicit Play emits zero once; crossing 1 s yields old end then new start; pose-only Seek emits neither. Reverse order is independently literal. Failure after stage 2 yields no external event/output despite the Applied command receipt.
- Interruption example: clips A/B currently weigh 0.75/0.25; restarting toward C gives those same A/B weights at elapsed zero and 0.375/0.125/0.5 at half duration. Attempting a fifth distinct active clip refuses without changing any clock, weight, event cursor, or pin. Test masks, zero total and additive reference/composition separately.
- Save through production Standard Branch mid-transition and after an interruption. Fresh target Count/Write, next poses and subsequent event occurrences match literal expectations without a hidden tick. RecordedContinuation remains explicitly unsupported, not an empty replay pass.
- Real animated Mesh/Sprite snapshots retain old palettes/frames/assets across Stop, unload, package switch and World replacement. Counted subdraw/palette overflow returns no partial snapshot; failed extraction changes no history.
- Existing Chapter 20's diagnostic compute producer remains specified implementation work. Exercise its zero count from a genuine static snapshot; its nonzero case uses a package-authored two-joint identity/Y-half-turn animation, one equal-weight +X-normal vertex, and root-only others. Expect one degenerate blend, lowest-joint +X fallback and a frame/draw-tagged count after the actual compute/readback fence on each backend. No fabricated palette, vertex atomic, or second complete skinning oracle qualifies it.

These select AR02/AR07/AR08, native slices of AS01/AS04/AS07/AS08 and EN056–EN060 as applicable. They do not close imported EN059, blocked-body EN060, strict continuation, or complete Chapter 32.

### 2.6 Root-motion and feedback boundary

For each original root track, retain Chapter 20's `D = M(b) * inverse(M(a))`, chronological loop composition, and `D * actorWorld`. Compute before replacing visual root translation/rotation by bind values. Base-layer contributing deltas blend in the same stable normalized order; higher layers contribute none. MotionActor's world scale must be unit, and Extract forbids scaled/animated ancestors that invalidate that rigid binding. Nonfinite accumulated motion faults; loop powers use checked exponentiation by squaring.

Root requests are packed densely by player RFC ID into resource partitions; unused partitions are explicit absent slots. Request.Actor is the definition's mapped MotionActor, not implicitly the skinned Mesh child. A stage-2 application movement system translates copied desired motion to actual supported body targets/forces/impulses. Animation never invokes Physics, writes a dynamic root, or claims that desired travel occurred.

Close the future producer's data now without pretending it exists:

| Proposed public declaration, `TerraFX.Integration` | Shape |
|---|---|
| `enum MotionDimension : byte` | `TwoD=2`, `ThreeD=3`; other values invalid. |
| `readonly struct MotionFeedback` | Internal issuer; `bool IsValid`, `MotionDimension Dimension`, `WorldEntityIdentity Player`, `WorldEntityIdentity Actor`, `long TickIndex`, `SpatialPosition3 PreviousPosition`, `Quaternion PreviousRotation`, `SpatialPosition3 CurrentPosition`, `Quaternion CurrentRotation`, `Vector3 ActualTranslation`, `Quaternion ActualRotation`, `RootMotionRequest DesiredMotion`, all get-only. Default is absent. |
| `static MotionFeedbackResources` | Get-only `Guid FeedbackId = S("resource/integration/motion-feedback/v1")`. |
| `sealed MotionFeedbackCommittedEvent : CommittedWorldEvent` | Internal issuer; get-only `MotionFeedback Feedback`. |

Integration alone issues feedback from **actual old/new body-origin poses at Pose Sync**, not COM poses, an intended target, or an Animation prediction. ActualTranslation/Rotation are the rigid actor-local delta satisfying new=`delta * old`, computed with admitted nearby spatial displacement, not a global floating-position matrix. TwoD embeds the actual XY body origin at the bound Scene depth and uses Z-axis rotation; reject incompatible out-of-plane desired motion under the explicitly selected application motor policy, rather than inventing a 3D solve.

The proposed feedback catalogue has Animation owner=5 (the existing owner enum; no Integration owner value is invented), with actual CLR issuer Integration:

- `animation/motion-feedback-resource`, ResourceState: tag 1 Values RA(animation/motion-feedback-slot)[4096..4096].
- `animation/motion-feedback-slot`, Auxiliary: tag 1 Present Bool; tag 2 Dimension? U8{2,3}; tag 3 Desired? R(animation/root-motion-slot); tags 4/5 PreviousPosition?/P3 @EngineDistance and PreviousRotation?/Q; tags 6/7 CurrentPosition?/P3 and CurrentRotation?/Q; tags 8/9 ActualTranslation?/V3 @EngineDistance and ActualRotation?/Q. Tags 2–9 all present iff Present; Desired must itself be present/valid and supplies the qualified Player/Actor/tick/source facts.
- `animation/event-motion-feedback`, EventPayload: tag 1 Feedback R(animation/motion-feedback-slot), required valid.

The real Integration registration, when implemented, owns 4096 fresh read-only-to-applications typed resource partitions and their exact State2 factory/codec/pins under the same existing Resource kind. Stage 6 reads see the current actual feedback; next-tick stage-2 policy may read its prior completed tick with that tick explicit. It is not selected by Animation support. External feedback events are provisional until stage 9; when this producer is actually enlisted, append its internally player-RFC ordered batch after the Animation batch and then assign dense publication ordinals. Physics contact/trigger roots still require their own complete enrolled ordering; they are not authorized by this two-producer extension.

M05 CPU root-delta tests need no solver. A literal noncommuting rotation/translation example must distinguish `D * actorWorld` from the reverse, and a full loop must not introduce the end→zero backward jump. Actual feedback, collisions, and the Chapter 32 sphere/wall witness require M06 bodies/commands/solve plus M07 Integration/Pose Sync. The actor center bound `<=0.605001 m` while the walk pose progresses is unchanged. Hand-authored feedback or a moving transform cannot satisfy it. Chapter 33's bound-Entity cases remain Integration evidence, not new dependencies inside standalone Physics.

## 3. Cosmetic CPU particles

### 3.1 Producer declarations and data

Choose **cosmetic only**. Particle positions, ages, PCG state, counters, recordings, and billboard histories are excluded from canonical World hash/checkpoint/continuation. The authored emitter component/asset remains ordinary persistent content; that does not make its live pool authoritative. Restore/replacement starts a fresh empty pool from restored emitter definitions/seeds and reports a new cosmetic generation/discontinuity.

Proposed public surface in `TerraFX.Rendering`:

```csharp
public enum CosmeticParticleSpace : byte { PlaneXY = 0, CameraFacing3D = 1 }
public enum ParticleSpawnStatus : byte { Spawned = 0, RejectSpawn = 1 }
public enum ParticleSpawnRejection : byte
{
    None = 0, ParticleCapacity = 1, RecordingCapacity = 2,
    QueueCapacity = 3, EmitterUnavailable = 4, PoolFaulted = 5, WorldClosed = 6
}
public enum CosmeticParticlePoolStatus : byte { Running = 0, Faulted = 1, Closed = 2 }

public sealed class CosmeticParticleController
{
    public CosmeticParticleController(World world);
    public Task<ParticleSpawnReceipt> SpawnAsync(
        Entity emitter, int count, CancellationToken cancellationToken = default);
    public CosmeticParticleCounters GetCounters();
    public CosmeticParticleRecording RotateRecording();
}
```

The controller borrows a World with the exact frozen support; it creates no second pool and has no Dispose/World-close authority. Spawn count is `[1,131072]`; default Entity, bad count and null World are usage errors. Async requests capture the exact binding and take their own finite use before escape. Queued target membership is revalidated by the real owner. Counters/rotation are owner-idle reads/operations on the committed pool only.

| Proposed output type | Complete get-only data / construction |
|---|---|
| `sealed ParticleSpawnReceipt` | Internal constructor; `ParticleSpawnStatus Status`, `ParticleSpawnRejection Rejection`, `int Requested`, `int Spawned`, `long? TickIndex`, `Diagnostic? Diagnostic`. Spawned means full count, Rejection=None and no diagnostic. RejectSpawn means Spawned=0, a non-None reason and diagnostic; tick is present only if evaluated in a committed tick. |
| `readonly struct CosmeticParticleCounters` | Internal issuer plus `bool IsValid`; `CosmeticParticlePoolStatus Status`, `long Generation`, `long CompletedTick`, `int LiveEmitters`, `int LiveParticles`, `long SpawnedParticles`, `long RejectedParticles`, `long RejectedRequests`, `long ExpiredParticles`, `Diagnostic? Fault`. Default is unissued; Fault is present iff Faulted. |
| `sealed CosmeticParticleRecording : IDisposable` | Internal constructor; `long Generation`, `long StartNextTick`, `long EndNextTick`, `long NextParticleId`, `int TickRate`, `int EmitterCount`, `int InitialParticleCount`, `int StimulusCount`; `GetEmitter(int)`, `GetInitialParticle(int)`, `GetStimulus(int)` return the typed values below; `Dispose()`. Indices use the corresponding half-open count. Reads after disposal throw. |
| `readonly struct CosmeticParticleEmitterRecord` | Engine-issued copied `WorldEntityIdentity Emitter`, `long EmitterGeneration`, `AssetId Definition`, `CosmeticParticleSpace Space`, `ulong Seed`, `ulong Stream`, `ulong InitialState`, `ulong InitialIncrement`, `long NextBirthOrdinal`. Recording privately retains the exact definition/Atlas/Material backing for this row. |
| `readonly struct CosmeticParticleInitialState` | Engine-issued copied `long ParticleId`, `WorldEntityIdentity Emitter`, `long EmitterGeneration`, `SpatialPosition3 Position`, `Vector3 Velocity`, `long AgeSteps`, `long LifeSteps`; definition is the matching emitter-generation row, retained even if the live emitter has since been removed. |
| `readonly struct CosmeticParticleStimulus` | Engine-issued copied `long TickIndex`, `WorldEntityIdentity Emitter`, `long EmitterGeneration`, `long ProducerSequence`, `int Count`, `SpatialPosition3 Position`, `Quaternion Rotation`, `ulong RandomBefore`, `ulong RandomAfter`, `long FirstParticleId`. Count is positive and this is an **accepted** request only. |

Recording is a complete **in-memory owned output**, not a new on-wire movie or State2 schema. There is no replay importer, canonical RNG registration, serialization claim, or “deterministic particles” switch. Its initial pool/emitter states plus ordered accepted stimuli describe its trajectory under the fixed rate/profile. Copies of scalar rows grant no current-package resolution or World authority. Reproduction evidence must use the same actual retained asset definitions; loading equal IDs from a replacement package is not sufficient.

Rendering-owned schema rows:

| Key / role | Complete proposed fields |
|---|---|
| `native/cosmetic-particle-emitter` / NativeAsset | 1 Space U8{0,1}, default 0; 2 SpawnPerTick U32[0..131072], default 0; 3 LifetimeTicks I64[1..long.MaxValue] @TimeSpanTicks; 4 VelocityMinimum V3; 5 VelocityMaximum V3; 6 ColorKeys RA(native/particle-color-key)[2..16]; 7 SizeKeys RA(native/particle-size-key)[2..16]; 8 Atlas Asset(native/atlas,1); 9 FrameId G; 10 Material? Asset(native/material,{1,2}); 11 Seed U64; 12 Stream U64[0..9223372036854775807]; 13 SortingLayer I32, default 0; 14 SortingOrder I32, default 0; 15 LayerMask U64, default all bits. |
| `native/particle-color-key` | 1 Age F[0..1]; 2 Color V4 @LinearColor: finite nonnegative HDR RGB, alpha [0..1]. |
| `native/particle-size-key` | 1 Age F[0..1]; 2 Size V2>0 @EngineDistance. |
| `render/particle-emitter` / Component Persistent | 1 Definition Asset(native/cosmetic-particle-emitter,1), Reference=Asset, PrefabEditable; 2 Enabled Bool, default true, PrefabEditable. |

The root value/codec is private immutable `CosmeticParticleEmitterAsset`. Register it only for the selected particle feature. Seed zero is valid; stream is explicitly 63-bit so `(stream<<1)|1` does not silently alias its high bit. Velocity is engine-distance/second; because the current Unit enum has no velocity value, its field annotation is None and this schema version supplies that meaning. Min<=Max per lane; all values are finite.

Keys strictly ascend by Age, exactly start at positive zero and end at one. Use linear interpolation, no extrapolation, spline or implicit random color/size. FrameId must exist in the exact Atlas. Material, when present, must be the Sprite profile and the exact selected page; absent means the existing default Sprite profile, not missing-load fallback.

PlaneXY uses the existing Scene/Sprite `SpatialPosition3` representation, fixed spawn Z and world X/Y axes; its velocity Z must be zero. It is not a PhysicsWorld2D position or implicit collider. It is eligible in both dimensional Sprite extraction paths, using the captured Frame2/Frame3 pair as appropriate. CameraFacing3D is eligible only in 3D extraction; its right/up axes are the copied camera's +X/+Y rotated basis, not a mutable camera lookup. Both use a centered pivot, existing CW Sprite quad/UV rules, material/tint alpha convention, and no depth write for Blend. Emitters require the actual Scene transform and package-prepared resources; there is no test-only position source.

### 3.2 Tick algorithm, RNG, recording, and lifetime

The pool has one engine PCG32 stream per emitter, independent of all authoritative streams. Use Chapter 27's exact multiplier, odd increment, output rotation and seeding. For each admitted particle consume exactly three words in X/Y/Z order; `u=(word>>8)*2^-24`, then velocity lane=`minimum + (maximum-minimum)*u` with the declared binary32 operations. Consume three words even for a fixed lane or PlaneXY zero Z, so the stream schedule is explicit. No OS randomness, render frequency, worker arrival, or authoritative stream participates.

At each real successful fixed tick:

1. Prepare from the last committed pool. For existing particles increment AgeSteps; expire when `newAge >= LifeSteps`, **before** integration. Survivors move by `position + velocity * FixedDeltaTimeSeconds` using admitted spatial addition and finite binary32 products.
2. Generate each enabled emitter's automatic request from SpawnPerTick, then merge explicit requests. Order by emitter EntityId RFC, then producer sequence; automatic sequence is zero, explicit sequences are checked positive monotonic admission sequences. Revalidate exact live emitter/definition/transform.
3. Preflight each request's complete particle, recording, pin, counter and byte demand. Admit all requested particles or RejectSpawn. A refusal consumes no particle ID, pool slot, recording-stimulus slot or RNG word and increments rejected-request/particle counters by one/requested count.
4. New particles have age zero, captured canonical spawn position and initialized sampled velocity; they first integrate on the next tick. Prepare receipts/counters/recording and immutable output. Install all cosmetic roots only with the successful stage-9 marker.

`LifeSteps = ceil(LifetimeTicks * TickRate / 10000000)` by checked widened integer arithmetic, minimum one. At age `a < LifeSteps`, curve position is binary32 nearest-even `a/LifeSteps`; exact-key values select that key, otherwise interpolate the bracketing pair. The age-one endpoint is defined but a particle expiring at that age is not drawn. No rounded repeated TimeSpan addition determines expiry.

An arithmetic/spatial/counter failure faults **the cosmetic pool**, never wraps a counter or mutates Physics/authoritative RNG. Prepare its Faulted state and diagnostic for the same commit, reject further spawns, and publish no new valid billboard output from that faulted generation. Other World work is not silently failed or rolled back by an optional cosmetic capacity refusal. Conversely, if the encompassing World tick fails, discard all provisional cosmetic changes/receipts/recording; previously committed output remains independently valid.

The accepted-stimulus log is bounded and non-overwriting. Recording exhaustion RejectSpawns new automatic/explicit work observably without RNG consumption; already live particles continue integrating/expiring. It does not silently drop a stimulus. `RotateRecording` atomically transfers the old committed segment to an owned result and starts the next with copied live-particle and per-emitter PCG state. Reserve old output + new initial state + pin/task/cleanup peaks before copying. If four recording outputs or bytes prevent rotation, throw a capacity `InvalidOperationException` with no segment/counter/pool change. Returned recordings keep their own actual byte/pin reservations through Dispose; rotation is not a refund.

A segment records its initial live state, emitter definitions/PCG states, next particle ID, rate and half-open tick interval; gaps between stimuli mean ticks with no accepted spawn. Rejected-request receipts/counters are observable separately; the recording is not claimed to replay rejection history or public command admissions. Removed emitter rows remain in the segment while any initial/live particle or stimulus references them, charged within the same recording bounds. Definition/backing or seed/stream replacement starts a checked positive emitter generation with a fresh stream; old particles retain their original generation, never a lookup against the new definition. At most 197632 emitter-generation rows fit a segment: at most 131072 generations referenced by initial particles, 1024 current emitters, and 65536 generations introduced by accepted requests. Only referenced/current rows are retained; further introduction rejects the spawn as RecordingCapacity, and the byte cap may refuse earlier. Particle IDs are checked positive pool-generation ordinals, allocated contiguously per accepted request without reuse; exhaustion faults the cosmetic pool. Enabling/disabling or moving an emitter changes future automatic request stimuli, not existing particles. Scene unload stops future emissions and rejects queued requests for that generation; existing particles keep their retained definition/pages until expiration or explicit World/pool retirement.

Explicit requests complete only after the tick that evaluated them commits. Failed encompassing ticks fault their admitted tasks through the World failure channel after cleanup, not Spawned/empty success. Close yields RejectSpawn(WorldClosed) for unapplied requests. Cancellation after acceptance cannot free a running request or revoke the pool's resulting particles.

The World-owned pool retires on World close; restore/replacement never resumes its omitted state. It has no body handles or Physics write access. Billboards/recordings already returned keep independent storage and exact assets across unload/close/GPU use. Any unreleased cleanup remains charged in the actual pool/output quarantine; no World close waits circularly for an immutable caller output to become a current-World use.

### 3.3 Focused examples and evidence

- With 131071 live particles, request two: RejectSpawn(ParticleCapacity), rejected particles +=2, RNG and particle IDs unchanged. Request one can fill the pool; the next one rejects. A later recording-capacity refusal is distinguishable from pool capacity.
- At rate 2 Hz and lifetime one second, LifeSteps=2. A new particle at x=0, vx=2 is drawn at age0/x0. Next successful tick draws age1/x1. The next expires before integrating; no x2 particle is drawn. A color curve white/alpha1 at age0 and red/alpha0 at age1 gives `(1,0.5,0.5,0.5)` at normalized age0.5. Size `(2,2)`→`(4,4)` gives `(3,3)`.
- Validate Chapter 34 EN123's six literal PCG words for seed42/stream54 before checking particle velocity lanes. Test fixed-lane consumption, independent emitter order, expiry equality, one-step lifetime, exact curve endpoints, invalid/nonfinite/overflow requests and whole-request byte refusal.
- Use actual native emitter content, Scene activation, fixed World ticks, controller receipts and committed Headless billboards. Repeat with different extraction/render frequencies; tick positions/PCG stimuli agree, canonical World hashes and authoritative RNG do not change because of pool saturation or rendering.
- Fault after cosmetic preparation: no provisional output/counter/recording or Spawned receipt escapes. Test recording rotation with live particles, retained old outputs, full-log refusal, unload, close, restore reset and cleanup retention.
- Real D3D12/Vulkan billboard pixels and actual fence-held retention are separate observations. Actual Physics noninterference requires the real M06/M07 body consumer; unchanged hash in a body-free World is only its canonical-state slice.

These are particle slices of EN096 and AR08, not a new acceptance suite or proof of canonical replay.

## 4. Authored Mesh LOD

### 4.1 Exact data/versioned producer

Propose the following Rendering-owned versions; do not widen their 1.0 tuples:

| Schema | Complete change from the frozen 1.0 row |
|---|---|
| `native/mesh` **2.0** | Tags 1–7 retain their exact kinds/cardinalities/stream/Skeleton semantics. Tags 3/4 are explicitly finest level 0. Tag 5 MaterialSlots permits Material major 1 or 2, checked by the new codec. Append required tag 8 AdditionalLevels RA(native/mesh-lod-level)[0..7] in fine-to-coarse semantic order; append required tag 9 Thresholds FA(F)[0..7], each finite positive, strictly decreasing. Count(levels)=1+tag8.Count and tag9.Count=tag8.Count. |
| `native/mesh-lod-level` 1.0 | Tag 1 Indices R(native/mesh-indices16 or native/mesh-indices32, both 1.0); tag 2 Groups RA(native/mesh-group,1)[1..65536], existing ordered contiguous range rules. |
| `render/mesh` **2.0**, Component Persistent | Tags 1 Mesh Asset(native/mesh,{1,2}), 2 LayerMask U64 default all, 3 PoseMode U8{0 BindPose,1 Animated} default 0. Field annotations otherwise match 1.0. Animated requires the exact same Entity's player/Skeleton and selected Animation support. |
| `render/sprite` **2.0**, Component Persistent | Exact tags/kinds/defaults/annotations of 1.0; only present tag 9 Material references admit Material major 1 or 2. Atlas remains 1.0. This is the new component family, not a reinterpretation of old bytes. |

Use distinct private unmanaged component/immutable author families for the two component versions; the old support composition still selects only 1.0. Use the existing factory/layout NameUuid recipe with exact new keys `render/mesh/layout/v2`, `render/mesh/factory/v2`, `render/sprite/layout/v2`, and `render/sprite/factory/v2`, factory version 1. Their complete typed storage inventories and actual `Unsafe.SizeOf<T>()` are implementation/audit work under already-closed fields, not reflection-inferred declarations.

All levels share the supplied vertices/streams, Skeleton and MaterialSlots. Every level has exactly the same group count and group-index→material-slot mapping as level0. Each index/group set independently passes all existing triangle, range, divisibility, nondegeneracy and skin predicates. Each index format is independently UInt16/UInt32. Aggregate indices<=1048576, aggregate groups<=65536, and the existing complete native-record/decode/lease byte limits apply before allocation. Bounds include **all supplied vertices**, hence every level, including unreferenced vertices; no thinner per-level culling bound is substituted.

First producer is native authored EBIN1→ordinary content/package build→new exact codec→real Scene resources. Stable AssetIds and EPKG2 envelope/checksum rules are unchanged. Existing GLB/OBJ output remains Mesh 1.0 and is accepted through its existing path; it does not begin producing LODs or Material 2.0. No simplifier, source-import option, or per-level asset/version service is selected.

### 4.2 Selection operator and history

Use the all-level local AABB, actual instance transform, copied camera and that camera's independently edge-quantized physical viewport. Compute the eight homogeneous clip corners with the same admitted binary32 row-vector transform/projection contract as rendering. Matrix/source invalidity rejects normally. For a qualified static bound whose perspective corners all lie strictly in front of the homogeneous near plane, define:

```text
nx = clip.X / clip.W; ny = clip.Y / clip.W
d = max((max(nx)-min(nx))*viewportWidth,
        (max(ny)-min(ny))*viewportHeight) / (2*viewportHeight)
```

For selection comparisons, treat the copied finite binary32 clip components as exact dyadics, the divisions as exact rational values, and physical widths/heights as exact integers. Do not first round/clamp `d` to a float, use full-target height, clip the AABB to the viewport, or infer a sphere diameter. Orthographic uses the same formula with its admitted homogeneous W. Bounded exact comparison scratch is charged; it is not a global floating position.

Unqualified animated bounds, a perspective near-plane touch/intersection (`W<=0` or `Z>=W` at any corner), or nonfinite/unrepresentable derived projection selects finest level 0 conservatively with a diagnostic. Invalid source camera/transform/profile is not excused by this fallback. Offscreen/culling is a separate operation and cannot turn uncertainty into an empty draw.

For levels `0..L-1` and boundaries `T[0..L-2]`, initial selection is the first level i with `d>=T[i]`, otherwise L-1. From prior level i:

- Coarsen across boundary i only if `10*d < 9*T[i]`.
- Refine across boundary i-1 only if `10*d > 11*T[i-1]`.
- Equality retains the prior level. Apply adjacent moves repeatedly for a large jump; strictly decreasing thresholds prevent a same-input reversal.

Those are exact rational comparisons of the admitted operands. No decimal 0.9/1.1 approximation or epsilon is introduced.

The **World-owned CPU extraction participant**, not the Renderer service, owns current derived history. Key it by exact World binding, qualified camera identity and qualified Mesh Entity identity. One slot stores its last level plus exact Mesh schema/backing, target extent, quantized viewport and projection parameters. Camera/instance movement updates the measurement without resetting history; changing camera identity, content backing, projection configuration, target/viewport, or explicit teleport/lost-history discontinuity starts with the initial rule. Do not accumulate keys for old viewport configurations.

Repeated extraction of the same committed generation and context returns the already determined selection and does not advance hysteresis. Prepare all requested cameras' selections privately; only a completely successful extraction installs them atomically. Capacity/source refusal installs none. Cameras not requested retain their existing current slots within the fixed bounds; removing a camera/instance retires its slot through the actual owner. A frame-only rebase transports materialized measurement bases and preserves the last level rather than acting like a content/viewport reset.

Add proposed engine-issued `RenderMeshLodSelection` copied values with get-only `Guid CameraId`, `WorldEntityIdentity Entity`, `AssetId Mesh`, and `byte LevelIndex`; default is unissued via `bool IsValid`. Add `ImmutableArray<RenderMeshLodSelection> RenderSnapshot3D.MeshLodSelections`. In the extended composition it contains each eligible camera/Mesh-instance pair once, camera order then EntityId RFC; even a one-level Mesh has level0. It is not multiplied by groups. The old static composition may expose initialized-empty selections and its existing implicit level0. Each per-camera draw-order entry joins to that captured selection; native color/shadow/picking paths consume those actual index/group sets, never recompute from a live camera.

Logical group counts and palette counts are preflighted before LOD, culling or batching. Stable draw GUIDs remain `mesh-group/` plus the original eight-digit group index. LOD cannot change material membership, hide excess draws, alter Physics topology, or mutate canonical state. Current qualification-zero animated bounds always choose finest; useful animated coarsening waits for a separately specified real qualified-bounds producer, not fabricated certification.

### 4.3 Boundary examples and required evidence

- `T=0.5`, sizes `0.50→0.46→0.44→0.54→0.56` produce fine, fine, coarse, coarse, fine.
- Exact equality: `T=0.625`, lower=0.5625, upper=0.6875. A prior fine level stays fine at lower equality; prior coarse stays coarse at upper equality. Adjacent representable values on either side distinguish the strict comparisons. Decimal 0.45/0.55 at T=0.5 are not exact equality witnesses.
- With a copied NDC X span=1 and Y span=0.5, viewport 800×400 gives d=1; viewport 400×400 gives d=0.5, regardless of the larger output texture. Test explicit aspect overrides separately.
- A second camera can simultaneously choose coarse for an instance whose first camera chooses fine. Repeat same-generation extraction, a multi-level jump, rebase, viewport/projection change, content replacement and forced byte refusal; verify no hidden history reset or partial installation.
- Literal native records cover one/eight levels, wrong threshold count/order/NaN, aggregate index/group ceilings, incompatible group/material mapping, bad coarse triangles, optional Skeleton closure and unchanged Mesh 1.0 refusal of added fields.
- Real package→Scene→Headless extraction establishes CPU selection/history and retained geometry. Native fine/coarse silhouettes from the same package on D3D12/Vulkan establish rendering; old snapshots keep their old content/choices through replacement/fences. No fabricated snapshot or automatic source simplification is a witness.

These are AR07/AR08 and Chapter 35's existing spatial/LOD boundary obligations. There is no new LOD acceptance ID.

## 5. Visual picking

### 5.1 Material and request/operation/result declarations

Propose `native/material` **2.0**, Rendering NativeAsset: exact existing tags 1–13 and profile semantics, plus **required tag 14 `PickAlphaCutoff F[0..1]`, author default 0.5**. All fields are finite under their owner predicates, and defaults do not fill missing encoded tags. Mesh 2.0/render-sprite 2.0 are the explicit producers that can reference it. Material 1.0 remains unchanged and has a **derived fixed pick cutoff 0.5**, not a new configurable field in its old tuple. AlphaCutoff tag7 still controls Mask only.

Use one explicit asynchronous owned operation with owner-thread reconciliation, avoiding a hidden worker-thread World lookup or standalone owner-queue deadlock:

```csharp
// TerraFX.Rendering
public sealed class RenderPickRequest
{
    public RenderPickRequest(RenderSnapshot snapshot, Guid cameraId,
        int pixelX, int pixelY, Surface surface);
    public RenderPickRequest(RenderSnapshot snapshot, Guid cameraId,
        int pixelX, int pixelY, GpuTexture offscreenColorTarget);
    public RenderSnapshot Snapshot { get; }
    public Guid CameraId { get; }
    public int PixelX { get; }
    public int PixelY { get; }
    public Surface? Surface { get; }
    public GpuTexture? OffscreenColorTarget { get; }
}

// Member on Renderer; synchronous admission, asynchronous operation:
public RenderPickOperation BeginPick(World world, RenderPickRequest request,
    CancellationToken cancellationToken = default);

public sealed class RenderPickOperation : IDisposable
{
    // Internal issuer only.
    public Task<RenderPickCompletedReceipt> Completion { get; }
    public RenderPickResult Resolve();
    public void Dispose();
}

public enum RenderPickStatus : byte
{
    Selection = 0, NoSelection = 1, StaleWorldBinding = 2, StaleFrame = 3,
    StaleView = 4, StaleTarget = 5, StaleEntity = 6, WorldFaulted = 7
}

public readonly struct RenderPickCompletedReceipt
{
    // Internal issuer; default is unissued.
    public bool IsValid { get; }
    public Guid SnapshotId { get; }
    public Guid CameraId { get; }
    public long TickIndex { get; }
    public RenderSubmissionReceipt Submission { get; }
}

public readonly struct RenderPickResult
{
    // Internal issuer; default is unissued, not a Selection.
    public bool IsValid { get; }
    public RenderPickStatus Status { get; }
    public Guid SnapshotId { get; }
    public Guid CameraId { get; }
    public long TickIndex { get; }
    public WorldEntityIdentity? Entity { get; }
    public Guid? DrawId { get; }
}
```

Null required references, empty camera, negative pixel coordinates, or a pixel outside the snapshot target/selected quantized camera viewport are usage exceptions (`ArgumentNullException`, `ArgumentException`, `ArgumentOutOfRangeException` respectively), not NoSelection. The two typed request constructors select exactly one borrowed target; they acquire no use/pin or trusted epoch. Default structs are unissued (`IsValid=false`); data is usable only when issued. Selection results have both Entity and DrawId; every other status has neither. Receipt Submission is nonnull only for a valid issued receipt.

`BeginPick` requires the exact idle committed World owner, selected Picking CPU support, a genuine snapshot from that exact construction binding, and a native Renderer/device. Headless throws NotSupportedException before native work. Validate receiver/World lifetime and owner/phase, request shape/provenance/camera/target/format, cancellation, then operation-count/U/byte capacity. Capacity failure throws contextual InvalidOperationException before allocating/retaining work; it does not return a background result. Accepted work takes independent snapshot/pin/target/table/use ownership before escape.

**Graph/submission choice:** the first API owns one complete ordinary built-in color graph for that snapshot/target, adds its selection/copy work to that same graph, compiles and submits it once through the real Renderer path. Thus an interactive Pick is also an explicit render submission and may produce a presentation sequence; it is not a readback from an already presented image. The application does not separately Submit that intended frame again. The old BuildGraph/custom-pass/Submit APIs remain unchanged. This first operation does not accept a caller-modified graph or claim custom-pass/UI silhouettes.

Completion means actual graph submission fence **and** selection readback fence/map/CPU copy finished. It does not mean reconciliation or presentation. A Surface presentation sequence, if present, still needs its separate real `WaitForPresentationAsync`; offscreen has none and zero surface/swapchain epochs.

`Resolve` is a synchronous owner-idle operation after successful Completion; premature resolve throws InvalidOperationException. It is single-use, with a second call returning the same cached copied result. No accepted operation is silently reconciled from a graphics/IO continuation. Awaiting Completion alone does not grant World-owner affinity. If the exact binding has closed, Resolve can return StaleWorldBinding from its retained lower control without entering disposed World storage.

### 5.2 Exact ID membership and silhouette

Extend the **actual extraction producer** to capture an immutable per-logical-draw `WorldEntityIdentity` and private exact World construction-binding provenance when the snapshot is created. Existing copied bare EntityId/WorldGuid fields cannot supply this retrospectively. The independent-use route protects later active authority; it does not bless an unqualified old snapshot.

The ID table is exactly all eligible entity-backed Mesh-group and Sprite logical draws in that snapshot, unique across cameras, **before camera frustum culling/batching**. Omit cosmetic particles and UI; neither has this entity-selection contract. Weighted subdraws of one animated Sprite map to the same original logical draw/Entity and get one ID. Mesh groups remain separate logical draws across LODs. Sort the merged set by ascending RFC draw GUID; dense UInt32 IDs are exactly `1..tableCount`, zero reserved.

Each entry copies `(DenseId, WorldEntityIdentity, DrawId)`. Validate duplicate/conflicting identities before native admission. Table cardinality is bounded by the snapshot's counted logical draw instances; unused/camera-ineligible entries are legal table entries, not rendered fragments. The requested camera alone supplies selection visibility; this is camera-qualified picking, not an implicit topmost-camera query over the composed Surface.

Rasterize its same captured geometry, selected LOD, skinning, clipping/culling/parity, pixel snapping and draw order. Opaque/Mask use the ordinary silhouette and Mask alpha cutoff. Blend disables blending on R32UInt, uses the independently selected pick cutoff, and keeps a fragment iff its **ordinary sampled linear output alpha >= PickAlphaCutoff**. Include tint/material/vertex alpha exactly as the color path does. Retain the ordinary depth-test/write policy and reversed-Z comparison; transparent draws do not acquire a depth-write shortcut. Later eligible Sprite draws overwrite IDs by the same draw order. A cutoff equality survives; cutoff zero does not invent an alpha>0 rule.

Selection pipelines have actual uint `SV_Target0`/integer attachment reflection and no color blending. Keep existing color shader ABI records unchanged. Add a separate 16-byte Object-group `b1` uniform, `uint4(DenseId, binary32-cutoff-bits,0,0)`, with pick-specific shader/layout identity `S("material/layout/picking/v1")` and pass identity `S("material/pass/picking/v1")`; native reflection must validate the extra binding for each selected Mesh/Sprite variant. Do not repurpose Sprite metadata's reserved-zero lanes or put a uint ID through float color encoding/tone mapping.

### 5.3 Native resources, exact-binding reconciliation, and retirement

Allocate/import real same-device single-sample 2D R32UInt selection storage with ColorAttachment|CopySource, plus D32 depth. Clear the complete ID image to integer zero and depth to zero before a partial camera RenderArea; this satisfies whole-subresource initialization. Use the selected camera's quantized viewport/scissor for its ID raster pass. The color target remains the ordinary Surface/offscreen output and is not replaced by the integer image.

Copy the requested `(x,y,1,1)` region from the actual selection image into a **real owned 1×1 R32UInt** texture with CopyDestination|CopySource, then use existing full-subresource `ReadbackTextureAsync`. The region equals the destination's complete extent. No new transfer API, native pointer, transient-image materialization, synchronous render-thread mapping, or zero-filled failure fallback is required. Validate actual format transfer/attachment usages and pipeline output types on each backend; enum presence is not support.

The private operation captures exact World/use generation, snapshot/table, qualified camera identity, both relevant frame descriptors/revisions, copied camera view/projection, physical target extent and quantized viewport, actual target/device identity, DeviceEpoch, and SurfaceEpoch/SwapchainEpoch when interactive. For this integral **physical-pixel** API, captured metrics are physical extent plus quantized viewport; no undocumented DPI-to-pixel conversion or invented Platform metrics revision is assumed. A future DIP request helper is not selected.

Native work reads only those captured values. At owner reconciliation, validate in this order:

1. Exact original binding still active; otherwise StaleWorldBinding, including a same-GUID replacement. A faulted active World gives WorldFaulted.
2. Captured dimensional frame descriptors/revisions still match; otherwise StaleFrame. Do not auto-rebase/reproject.
3. Actual target/device/epoch/physical metrics still match and target remains admitted; otherwise StaleTarget.
4. The same qualified camera remains live with the same copied view/projection/viewport bits; otherwise StaleView. An unrelated later tick does not by itself stale the request.
5. Interpret the copied UInt32 only after those checks. Zero gives NoSelection. Nonzero outside the retained table is an InvalidDataException/native-data fault, never background.
6. For a mapped nonzero entry, lookup its **complete** qualified creation identity in that exact World on its owner. Missing/dead identity gives StaleEntity; never look up a recycled slot or substitute its new occupant. Selection returns the original copied identity/DrawId with the old snapshot tick, not an Entity handle or promise of future membership.

The lower-use addition needed by Resolve is a closed Entities-owned exact-binding-active check on the operation's captured `WorldGuidUseControl`/`WorldLifetimeControl`/grant, under their existing ordering. It is not a GUID lookup, generic permission, new lock domain, or permission to close/quarantine a World. All actual live camera/entity reads occur through the ordinary owner/cohort route after that check. This is a concrete independent-use consumer and must be tested as such; Scene preparation credit is not borrowed.

Cancel before admission acquires nothing. After native acceptance, cancellation does not terminate Completion early or free any source/staging/pipeline/target. `Dispose` closes further Resolve/admission to that wrapper; it may precede Completion, but the operation's internal owner continues until actual native/map/CPU-copy consumers stop. A pre-native abandoned operation can cancel safely; an accepted native failure faults Completion with the original contextual Graphics failure after safe cleanup, or retains quarantine when completion is uncertain. It never issues a valid completed receipt/result.

Keep table, snapshot/asset pins, target, ID/depth/1×1 textures, pipelines, copy/readback owners, and W/use credits through their actual consumers. Resolve or Dispose ends active reconciliation interest only after native work has also quiesced. Scalar result copies confer no ownership and may outlive that use; tasks/receipt graphs must sever resource/World authority at terminal cleanup. Dispose of the caller snapshot/target, unload, same-GUID replacement, or a canceled wait cannot revoke already admitted native ownership. Existing presentation-resource retirement remains separately fenced.

### 5.4 Required focused observations

- Compile public-only calls for both typed targets and every result/status/default/nullability path. No fabricated snapshots, public epoch setters, Entity constructors, or unsigned casts of pixel counts.
- Exact mixed Mesh-group/Sprite table order/membership, weighted Sprite deduplication, particle/UI exclusion, camera viewport half-open edges, valid zero versus invalid pixel/stale context, and nonzero out-of-table failure.
- A Blend material with mask cutoff 0.2 and pick cutoff 0.5 rejects sampled alpha 0.4 for picking; equality 0.5 survives. A Material 1.0 uses fixed pick cutoff 0.5. Test actual masked/blended sampled alpha and overlapping Sprite/Mesh draw/depth order with independently authored expected IDs.
- Submit from an old real snapshot, destroy/reuse its entity slot, finish real readback, then Resolve: StaleEntity, never the new occupant. Repeat with same-GUID World replacement, rebase, resize, camera/view change, target disposal and an unchanged-context later tick.
- Hold a real admitted pick through caller snapshot/asset/target disposal and World close. Verify independent-use admission/exact W, queue/count/byte refusal, release after actual copy/map completion, and quarantine without active-binding retirement authority. Synthetic lifetime controls qualify only their controlled failure slice.
- Native R32UInt raster→region copy→full readback on D3D12 and Vulkan is mandatory for execution evidence. Animation/LOD silhouette variants use their real producers; static picking can precede them. A public AOT execution and an interactive presentation wait are separate observations, not inferred from managed/native offscreen readback.

These select EN117, applicable EN069/EN070, and AR02/AR07/AR08 slices. Real loss/timeouts remain their existing M09 observations, not prerequisites for ordinary picking implementation.

## 6. Implementation batches and producer gates

Rows are work descriptions, **not acceptance IDs**. Each implementation batch owns its implementation, directly related documentation, focused tests and affected validation; implementation assignments use these proposed declarations rather than inventing new members/tags during coding.

### Explicit declaration holdpoints

The bounded review did not establish the following supporting declarations as existing or fully closed. They block the named integration step, not independent numerical/content work:

| Holdpoint | Disposition before affected implementation |
|---|---|
| Exact lower engine phase and post-restore-join binding signatures | Section 1 fixes issuer, roster, operations, affinity, ordering and lifetime, but does not publish the complete private C# port/member catalogue. Entities/Animation/Rendering must close those signatures in the first registration batch before live Animation or particle enlistment. No guessed `WorldAnimationStepBinding` or restore callback is authorized by implication. |
| Multi-major AssetReference field bindings in version-2 components | The selected wire alternatives are explicit. The new private runtime/author representation must retain both signed-32-bit AssetId and selected positive schema major (schema ID is fixed by the field; minor zero), with matching typed accessors and canonical codec. Existing major-1/int-only component bindings are not evidence that such a producer exists. Close its exact typed provider surface before the version-2 component batch. |
| Root-request/feedback resource aggregate provider closure | The proposed unmanaged value and RA slot/source shapes are fixed, but this review did not verify that current typed ResourceState field bindings can represent them. Supply the complete frozen field/source/default/reference/Read/Validate/Count/Write/Restore family, or revise that representation explicitly before registration. Do not route it through fabricated bindings or opaque mutable service state. |
| Feedback and imported-animation execution | Actual body/Pose Sync and expanded adapter producers remain absent from the reviewed scope. Their gates are M06/M07 and the separately owned adapter extension, respectively; no additional M08/M09 qualification work is introduced here. |

These are not acceptance results or permission to begin an affected implementation with a missing declaration. The native schema, behavior, lifetime and public operation recommendations remain available for review without further exploration in this proposal task.

| Order / coherent batch | Exact prerequisites and deliverable | Observation boundary |
|---|---|---|
| 1. Versioned native content and frozen registration closure | Existing M02/M03 selected descriptors, native Assets/Scene producers, Chapter 10 recipe and typed factory mechanisms. Add the exact selected new metadata/codecs, native author records, Mesh/Material 2.0 value families, explicit component selection and support-token/phase/cohort plumbing. Keep all old paths. Implement the already-declared event base before event descendants. Do not register callable roots for unfinished features. | Literal bytes/negative readers and public construction/registration tests only; no live feature/native claim from metadata. |
| 2. Live Animation owner, commands, events, root requests and Standard Branch state | Batch 1's Animation selection; real Clip/Skeleton/Scene/Atlas backing, resolved receiver/MotionActor map, all attachment/resource provider roots, exact queue/pin/use accounting and post-restore join. Deliver one real native-authored World/controller/Headless path including complete state/cursors/transitions and persistence. | CPU sampling, root deltas, committed events, state/hash/restore and actual owner lifetimes. No M04, M06, broader import, or recorded-continuation prerequisite for these CPU operations. |
| 3. Animated CPU extraction and actual skin/Sprite consumers | Batch 2's real current/previous pose output plus batch 1's version-2 render components and exact CPU cohort. Deliver palettes, weighted Sprite subdraws, bounds/refusal/history, and the already-specified diagnostic compute producer. | Headless actual extraction first. GPU/palette/degenerate-normal output additionally needs actual M04 device, shaders/reflection, target, graph, residency, submission/readback. No fabricated palette witness. |
| 4. Cosmetic emitter/pool/recording/billboard unit | Batch 1's selected emitter/Material/Atlas/component roots and real Scene transform producer; frozen particle phase/cohort and own operation/storage accounting. Deliver the algorithm, commands/results/counters/recording retention and committed output together. Independent of Animation and solver implementation. | Actual CPU producer/canonical exclusion first; native billboards after actual M04. Physics noninterference remains dependent on its real M06/M07 consumer. |
| 5. Static authored LOD selection/history unit | Batch 1's Mesh 2.0 author/codec/package path, actual static Scene/camera extraction and transactional history storage. Independent of live Animation. Deliver all levels, exact projection/hysteresis, immutable per-camera choices and retained geometry together. | CPU boundary/history evidence first; actual native silhouettes next. Animated coarsening is not claimed while bounds remain qualification zero. |
| 6. Static visual-pick unit, then produced variants | Batch 1's Material 2.0, qualified snapshot provenance and actual static draws; exact independent-use/reconciliation route; actual M04 R32UInt/pipeline/graph/copy/readback. Deliver request/operation/result and native retention/reconciliation together. Does not wait for batches 2–5. | Static native/public path first; add animated/LOD parity only after batches 3/5 actually produce those inputs. Table-only tests do not qualify GPU picking. |
| 7. Scoped M05 native/AOT/public observations | Each completed unit's actual selected roots and current public consumers. Extend the existing public-only consumers and standard publish/test procedures for exactly those implemented features. | Windows D3D12, Windows Vulkan and Linux Vulkan observations remain distinct; publish/execute each required target. No Windows JIT result certifies Linux/AOT. This is not the M08 release matrix. |
| Deferred dependency, not an M05 declaration blocker: actual collision feedback and expanded source import | M06 body/command/solver plus M07 Integration/Pose Sync for real feedback and blocked travel. An actual expanded GLB owner/adapter for imported hierarchy/skin/animation/camera conversion, not the current static pair. | Complete those existing Chapter 32/EN059/EN060/AS03 integrated slices only when their producers exist. Native-authored animation/LOD and CPU desired deltas do not substitute. |

Within the dependency-ready rows, particle, static LOD and static picking work can proceed independently once their named shared inputs are delivered; no blanket M03-B/M04 exit or arbitrary code-size quota imposes another ordering. Shared catalogue/registration edits remain one serial ownership boundary.

### Evidence and acceptance discipline

Use the existing owning projects and NUnit/VSTest commands in `docs\project\Building.md`, Debug before Release, with focused implemented fixtures and fresh nonzero selected/executed counts. Use `Building.Consumers.md` for real public-only/AOT publishing and target-specific execution. Follow the actual `global.json`; historical qualification examples in prose do not replace its developer SDK selection.

Keep independently authored literal expectations. Do not approve output by regenerating expected values from production or maintaining another complete playback/particle/skinning engine. Retain per-run TRX/logs under ignored artifacts when implementation is performed; no generic qualification runner, source inventory, receipt chain, or test-only producer is proposed.

The applicable existing sets are AR01/AR02/AR03/AR07/AR08, AS01/AS03/AS04/AS07/AS08 and EN056–EN060, EN069/EN070, EN095/EN096, EN117, EN121–EN124/EN131–EN133 only for the slices actually exercised. Source conversion, exact-profile replay, Physics integration, native output, AOT consumers and complete case/milestone acceptance remain separately incomplete until their full existing membership runs. No new acceptance ID is allocated.

## 7. Frozen review inputs

Baseline HEAD: `31f156a51ccc462ae9c5cc8d4e87e00c0054dec4`. Review date: **2026-11-16 UTC**. The following **73 complete files** are the repository review-input set, even where only applicable ranges were read. Hashes are raw-file SHA-256, not Git blob hashes or the earlier proposal's copied manifest. `C`=unchanged at baseline, `M`=modified, `U`=untracked. Modified and untracked bytes are intentional inputs, not silently replaced by HEAD.

Filename/directory discovery and Git status are context, not additional byte-reviewed source inputs. Other dirty files and the unrelated M03-B proposal were not reviewed/dispositioned. This sole new proposal is the output and is excluded from a recursive self-hash. These hashes are a local review boundary, not runtime asset identity or new qualification infrastructure.

| Input | State | SHA-256 |
|---|---|---|
| `.editorconfig` | C | `342683DE2B1C262624546F312F7D8E2078F89185ADD39F43F1FB5013376FD553` |
| `.github\workflows\ci.yml` | C | `03EFEA672CD52E9EF053E9CE0285E7CA02C6CE6FB0D21268ADE4255ED5A39D58` |
| `Directory.Build.props` | C | `770EE4B2A422861A6F23F0D39DD0ECA361A81CB87E64EBAC4E12170F71231D7A` |
| `Directory.Build.targets` | C | `AEEC1D08E8037470239035812351C4045124607D67A354D482112F4D642D4105` |
| `global.json` | C | `1BCE01871321C2DF82E887DA428CE5C121A6C6541FE65017FE3D249BE47E190C` |
| `sources\Directory.Build.props` | C | `D44373AF315523F8AF9F09F36BDED2A688E2B5C1572FCC0732310C7EF590EAD8` |
| `tests\Directory.Build.props` | C | `281AAD4237121FEAB1903C2384F1A5F0AED7E218A704C918FE953E9FC8E75764` |
| `sources\TerraFX.Runtime\TerraFX.Runtime.csproj` | C | `E019D4DBAD14FF2D5FF8D7365DDED0D09210575D5228A0D01CED70C99B3C2D33` |
| `tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj` | C | `ECBFD99E4DD1EC2A32472D3187B6F2911D7297D1F74A52F2AFA600EB6AE5F06E` |
| `docs\project\Roadmap.md` | C | `C4FBE82155A6D82FFD2D13AD2F18056D938738C4FA5A66760C4877E24ED001C7` |
| `docs\project\Building.md` | C | `F53AB58B89EEAE7E17B70970075BE9322A936D443BB6D5239C3238E594DEABC6` |
| `docs\project\Building.Consumers.md` | C | `8AF46079A9E11E01578CC9B76B695801B585F58148DF36418EE5506CA4006442` |
| `docs\design\03-shared-conventions-and-clocks.md` | C | `B4208D3037C8C90EED56627F63DC04ACFFA80FF16B5FF3FF6936CD89615C08C5` |
| `docs\design\04-capacity-and-workload-profiles.md` | M | `A4D86F3FA373A64548435C53582BF5916E29D77F34650F9D161CCEC90E6F0245` |
| `docs\design\09-native-assets-and-source-adapters.md` | C | `16730803987B66677CE6DA6BB829CCFFC435B614D51B507049DF8FD8988AA441` |
| `docs\design\10-binary-data-and-authoring-formats.md` | C | `56721136AECAE18C20EB3E9A1BECEB527CC0676DD6B43CB506538AD63DECBB5F` |
| `docs\design\11-assets-import-streaming-and-packages.md` | C | `DE1032A04217B2A08933332AEE7DB22DF849CBFC069E76CF6B2263BF469DF109` |
| `docs\design\12-entities-components-and-queries.md` | M | `174B2465746DA79CEF560C8506216EF4F99F9D1AABAC184F872C9E6CB0B94E0B` |
| `docs\design\13-jobs-and-frame-pipeline.md` | C | `0D7806B03A1D019215EB5C3912F10200A078F9B00A71A206FB979E93BBD18CDE` |
| `docs\design\16-scenes-transforms-and-cameras.md` | M | `B012F42B23CEDE9C7CDB71203D8357CD19E1626823EA25EF3119961292D3698A` |
| `docs\design\17-graphics-device-and-resources.md` | C | `8157E7DA5472B9AABD891A05E84F2B997487A5C5E3FF67CB589681747D628718` |
| `docs\design\18-shaders-and-material-pipeline.md` | C | `AF20911D9D06BFB58F6C771511181F59347C4659129CFD5FC8CF622ED191C0D7` |
| `docs\design\19-render-graph-synchronization-and-presentation.md` | C | `AB673DEDBD85B7A161C5D7DC871BF5B486911DC27EC4279AC43827B005241B8B` |
| `docs\design\20-animation-and-deformation.md` | C | `BBEF0A36E0832C2BD0D54A07E479917DB923671B64F7176369F8F57A8F1618C5` |
| `docs\design\21-2d-and-3d-rendering.md` | C | `989A6183186592F165FA9614CC19A8FED63826E2A51170CE55FBFFFCA8116ED2` |
| `docs\design\27-state-serialization-replay-and-recovery.md` | C | `B9B6324C5B1B1543CA1D2287ECE579597A7E7AA62D885031C4FF1BB629F08777` |
| `docs\design\29-project-setup-build-and-deployment.md` | C | `3B55A013EEC327ECD648ACBD5B53717DD218AD288E8047AC21277609EFA2F47B` |
| `docs\design\30-public-api-review-surface.md` | M | `15848C82F3858A77A87A0D3EA6FBCC07046EB6E77CA5B21748C72B90EFAB598B` |
| `docs\design\32-end-to-end-integration-scenarios.md` | C | `6AF2211CB79FECA449D0F701752F86B8D093162AD75673770B0C0DE0F49BA4FC` |
| `docs\design\33-physics-acceptance-and-reference.md` | C | `288E9469AD6B9998921AA02C67D9272E61EC925B6583DF108BE84AFF33058A9E` |
| `docs\design\34-engine-acceptance-cases.md` | C | `532A68F1717DC05901DFC4537204E65B31B28EE46326F64F5A1CD7834332AA41` |
| `docs\design\35-implementation-roadmap.md` | C | `24EEC6E92B7AD904D552F290EF748FFC8FCE13B6B94A89EBD9F39487D4584D87` |
| `docs\proposals\m05-owner-contract-closure.md` | U | `6FEE16009BF0D1FB529E68C8DF1E3293A852468E71D9A291984D8A64A9A62BC8` |
| `sources\TerraFX.Core\CompiledContentSchemaRegistry.cs` | C | `CFE040B8E51CB811AECCCBB8B0BEC0D2993B7A726A5B179968CE0DB1415EAE08` |
| `sources\TerraFX.Assets\AssetStoreRegistrationBuilder.cs` | C | `EF0B1B098B1619414A633C118E0B98E75523A3BF7219DEE52E20314FB5E7CF04` |
| `sources\TerraFX.Assets\NativeAssetSchemaCatalogue.cs` | C | `6B9537EA8CEFEBB48B5DC24261A8C03FB059E3314E7DA089F29A639D9E55620A` |
| `sources\TerraFX.Graphics\RenderingSchemaCatalogue.cs` | C | `71BD7A427756B16FC4404865A87A9FA711019EF3DD28D0F8DC860E4A9ADDE94C` |
| `sources\TerraFX.Runtime\Animation\AnimationSchemaCatalogue.cs` | C | `8D219E50380D7D0722BF8BA1B84F8B634AE4BB3F026381654A50B60A11E5A64A` |
| `sources\TerraFX.Runtime\Scenes\SceneSchemaCatalogue.cs` | C | `CF0253CF1C207644F0B6C76B0392977E02B0939FF355ED825F38D42705F2B32A` |
| `sources\TerraFX.Runtime\Entities\StandaloneContinuationSchemaCatalogue.cs` | C | `751F3AD4B183208455CDF289E37517E0E5036B4F22CC247E8A8E82B664B73269` |
| `sources\TerraFX.Runtime\Animation\AnimationSupport.cs` | C | `4A1AEE71A26A0B1214D730F5CB738D68E16D7C8CE1EC257318BD1CA05F124F56` |
| `sources\TerraFX.Runtime\Animation\AnimationClipAsset.cs` | C | `AFB62F4EC29D3CEFCFB46D529699BC04FFB19F3E2D79D22BD4353350B91B0539` |
| `sources\TerraFX.Runtime\Animation\AnimationClipAssetBinding.cs` | M | `E09FCA988BA58127B365D8F4D7EB0C22CA3FD17E07E8342879544FB81BBD4DB1` |
| `sources\TerraFX.Runtime\Animation\AnimationClipSamplingKernels.cs` | C | `E2ECEFF51F175038833C1883D7EDF522F679BA0F6D31CEF8EC5F9E62F037AC3D` |
| `sources\TerraFX.Runtime\Animation\SkeletonAsset.cs` | C | `0236A3FEF5AEEDBECF2BFDB9179EC1F9F6F0698E54EC54D5DE4953625D309C04` |
| `sources\TerraFX.Runtime\Entities\World.cs` | M | `AD884C9DF4F9B48DB4751518237535BC11A67820EA1F96FC66CAE850935A7B31` |
| `sources\TerraFX.Runtime\Entities\WorldRegistrationBuilder.cs` | C | `BB60686B9C2B2090F01D9B9C1C27F3D22DFE692EB6B5F71D652B662F61BD9CA7` |
| `sources\TerraFX.Runtime\Entities\CpuExtractionSupportRegistration.cs` | C | `A7031ACC5DA182D427A4ED18FE103F0A8EE17747059019C08DBC60E5AC16237A` |
| `sources\TerraFX.Runtime\Entities\IWorldCpuExtractionParticipant.cs` | C | `0892E55100F00C0884E730C256303B204F06F963CFCA62CFAFC5130BE0EFE622` |
| `sources\TerraFX.Runtime\Entities\World.CpuExtraction.cs` | C | `971EE49B1CCD9C3C629E3CA33198F26BE610FE3D28B4236E371C220201D768FC` |
| `sources\TerraFX.Runtime\Entities\WorldGuidUse.cs` | U | `A6120452989F29EC2EF9C2B90E7479CC6635B9B17848C6C683063B3F99D36D22` |
| `sources\TerraFX.Runtime\Entities\WorldGuidUseControl.cs` | U | `1931CD2B5DFD81A3F0CD8A4E6F3D09C5D9B006B2E29817F5C295C77F78202B68` |
| `sources\TerraFX.Runtime\Entities\WorldLifetimeControl.cs` | M | `B1A9ADD0A1D68CAF4C385973CA4E2602C68E19E46078AA3CB09614848C09A634` |
| `sources\TerraFX.Runtime\Entities\WorldEntityIdentity.cs` | C | `09B2A54CC3ADDEE27331A9BB5D4B6486C27CA506B5B3A8F2C2D022BA49AC4A2C` |
| `sources\TerraFX.Runtime\Entities\CommittedWorldEvent.cs` | C | `F9754E6EF91F442BAD7FCAD2BDFBDC969C6F4352663AB57292679237017E5C0C` |
| `sources\TerraFX.Runtime\Scenes\SceneInstance.cs` | C | `56CC97329BCF128C270873F0B3743944F435E932822E0D3E413F33DF80FCD7A7` |
| `sources\TerraFX.Runtime\Scenes\SceneEntityProvenance.cs` | C | `73F104E23599399ADADCA4146BD1A92C95DA1BC7B2E15FB94076FF9791FE6F36` |
| `sources\TerraFX.Runtime\Rendering\Renderer.CpuExtractionSupport.cs` | M | `EAF1F94AFF0886B3FA30AF97BD7F83180A38AB744D4241D0CDD275A055EADB55` |
| `sources\TerraFX.Runtime\Rendering\Renderer.Extract.cs` | C | `5D3B4C9D739316542629EA7A40B60D8558A54C50B6347441985A77E51F8CA048` |
| `sources\TerraFX.Runtime\Rendering\RenderMeshAsset.cs` | C | `DE4681A7A5EDFC39502FAF9339BC5D97AFD0EF2BBDF024AA0FD67F535829C49E` |
| `sources\TerraFX.Runtime\Rendering\RenderMeshAssetCodec.cs` | C | `F863186F77B07AAD63606D43A3F74CC887D0A6C3A52B3BD1777EDAD7899CEB16` |
| `sources\TerraFX.Runtime\Rendering\RenderMaterialAsset.cs` | C | `F4C183B5B28BC45F570166F3ECF8BB55D65BCBEFBDA11D6E68D40ED8BF36E1D7` |
| `sources\TerraFX.Runtime\Rendering\RenderMeshComponentDefinitions.cs` | C | `167D1ADF373643E47C6F489A61D35F51FE6F4829349C82FDBC2131B0E772A20F` |
| `sources\TerraFX.Runtime\Rendering\RenderSceneAssetBundle.cs` | M | `4FD3F20576AD328BD3E9958E40EF8D475451BDCBFC5122C3BA7E599A0EA8361A` |
| `sources\TerraFX.Runtime\Rendering\RenderSnapshot.cs` | C | `733C8A58053977A349742540639FC55AF0CD299DB33C7394D1DC999A17D30AF0` |
| `sources\TerraFX.Runtime\Rendering\RenderSnapshotAssetPins.cs` | C | `8417756EA5D9DC6363D71C6274B2B6B8849C179BFFDA0D1133A4FFB81FCD0A1C` |
| `sources\TerraFX.Runtime\Rendering\Renderer.BuildGraph.cs` | C | `552A7F939E01CA189DB0EB3F759F7E97ADC7BD2FF0005391CDFD899860A09560` |
| `sources\TerraFX.Runtime\Rendering\Renderer.Submit.cs` | C | `DBBA4C338D7178A0758B50738343C04D70DBD1DA8F8FDF7FD72277765828E680` |
| `sources\TerraFX.Runtime\Rendering\RenderExtractionLimitKind.cs` | C | `101BDA2A5F0C0F34F062515D3057CEA0588484D484E3879AFE89C79792554906` |
| `sources\TerraFX.Runtime\Shaders\MeshC0Vertex.hlsl` | C | `369AA412615FEB6405E38907733AF2DEBEDCFE6366B3BB56D21546C6E829498C` |
| `tests\TerraFX.Runtime.Tests\Animation\AnimationClipSamplingTests.cs` | C | `8BF4E9D3E22CEC58B8D29CBFF5CB3AE159155D7DC59AB54900FF36B1F95A1ECA` |
| `tests\TerraFX.Runtime.Tests\Scenes\RenderMeshSnapshotTests.cs` | C | `B161509CAE92232C57CABD57B2DCE790ACF880FEF4D3AD62D747526C66EDD4C2` |
| `tests\TerraFX.Runtime.Tests\Scenes\RenderPublicConsumerTests.cs` | C | `D08B3435DA4E823553BD53C52BC282FBB7D42699E973D95B630F0D755D58C59E` |

**Verification record:** all 73 listed raw-file SHA-256 values and C/M/U states matched; no input was missing, changed, or unverifiable. Baseline HEAD also matched. Verification is repeated against this same manifest after the final proposal edit. A later disposition must recheck the inputs; acceptance cannot transfer to subsequently edited bytes.
