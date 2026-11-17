# M05 picking owner-contract closure

## Status and bounded decision

**Tentative proposal under the requested blocker policy; not an approved owner/API contract, implemented feature, or qualification result.** It may guide separately authorized implementation, subject to the declaration, producer, lifetime, and evidence gates below. This task adds only this proposal.

Select **one physical pixel in one explicitly selected camera of a genuine static RenderSnapshot**, using Chapter 21's separate render-ID pass. Initially admit the existing Sprite 1.0 and **unskinned** Mesh 1.0 C0 paths with Material 1.0. Preserve their current supported material, texture, transform, sorting, and pixel-snap subsets. Do not add a competing CPU ray/triangle picker, Physics query service, UI hit tester, all-hits API, rectangle selection, custom-pass silhouette, or topmost-composited-camera policy.

The useful dependency-ready CPU unit is the bounded ID-membership builder, pixel/context validation, and copied-ID lookup logic. It does **not** answer which geometry covers a pixel without raster output. Scene/camera/qualified extraction production, asynchronous owner reconciliation, and actual native raster/copy/readback are separate integration steps, not manufactured inputs to make that CPU unit appear complete.

Chapters 18–21 own layout/rendering behavior; Chapter 16 owns cameras/coherent reads; Chapters 12/29 own identity authority/layering; Chapter 30 owns public declarations; Chapters 32–34 own observations; Chapter 35 owns sequencing and acceptance. None is amended here. Existing proposal language about tentative approval is not independent approval of this proposal or evidence that its proposed members exist.

## 1. Evidence and the remaining gap

Paths are repository-relative. Source/test inspection is evidence about the frozen inputs in section 9, **not a fresh test execution**.

| Verified input | Consequence |
|---|---|
| `docs\project\Roadmap.md`, M05/Next work; Chapter 35 capability sequencing and sections 2.2–4 | Picking owner data/API closure is still open. Contract-complete CPU work, actual producers, native output, public consumers, NativeAOT, and milestone acceptance are different boundaries. |
| Chapter 21 section 6 | R32UInt, zero background, snapshot-stable dense IDs, retained side tables, silhouette parity, physical-pixel copy, fence completion, and stale identity/context handling are existing requirements. Physics picking is a different policy. |
| Chapter 30 sections 5/7; source search for `RenderPick`, `BeginPick`, `CameraMath`, and `ScreenToRay` in `sources`/`tests` | The public baseline declares camera conversion, extraction, graph/submission, and transfers, but no complete picking request/operation/result. No implementation of those picking operations or camera conversion methods was found. A declared `ScreenToRay` is neither an implemented producer nor a visual picker. |
| `Rendering\Renderer.Extract.cs:148–286,389–548,901–1026`; `RenderMeshSnapshot.cs`; `RenderSpriteSnapshot.cs`; `CameraSnapshot.cs` | Real extraction copies static draws, cameras, and per-camera order. Draws currently retain bare EntityId, not complete creation identity. Mesh groups and Sprites have separate RFC-sorted draw arrays; neither array alone is the mixed ID membership set. |
| `Entities\World.cs:1518–1522,1659–1686`; `World.CpuExtraction.cs:17–70,148–175`; `WorldFrameReadLease.cs` | Qualified identities and exact committed/cohort-checked owner reads already have real issuers. Capture them while the original Entity is live; later lookup by a bare GUID cannot reconstruct the old creation facts. |
| `Rendering\RendererCpuFeatures.cs`; `Renderer.M05AssetCodecs.cs:16–44`; `Renderer.CpuExtractionSupport.cs:32–94` | `Picking=8` currently selects an asset-codec family. It is not picking support, a World registration extension, or an implemented `RendererCpuExtractionOptions` overload. The actual static registration remains the no-argument factory. |
| `sources\TerraFX.Graphics\RenderingSchemaCatalogue.cs:48–106`; `Rendering\RenderMaterialAsset2.cs`; `RenderMaterialAsset2Codec.cs:120–138`; `tests\TerraFX.Runtime.Tests\Rendering\RenderMeshAsset2CodecTests.cs:225–343` | Material 2.0 already has required tag 14, finite `[0,1]` PickAlphaCutoff, author default 0.5, exact codec/version checks, and test source for missing/invalid fields and retained backing. This is not a version-2 Scene/component/extraction producer. Material 1.0 must not acquire tag 14. |
| `Rendering\Renderer.BuildGraph.cs`; `Renderer.SpriteC0Graph.cs:20–77`; `Renderer.MeshC0Graph.cs:380–424,1214–1285,1610–1638`; `Shaders\SpriteC0Pixel.hlsl:23–32`; `MeshC0TexturedPixel.hlsl:220–236` | C0 has real, restricted Sprite/Mesh material paths. Mesh alpha includes material, vertex, and optional sampled alpha; Sprite alpha includes Atlas/tint/material tint. Picking cannot replace these with bounds or an unrelated mask test. |
| Chapters 18/19; `sources\TerraFX.Graphics\GraphicsPipelineDescription.cs:202–240`; `tests\TerraFX.Runtime.Tests\Hosting\EngineHostGraphSubmissionTests.cs:1072–1087,3844–3865,6170–6225` | Unsigned pixel-output validation, an R32UInt raster/readback test, and region-copy test machinery exist. These lower observations do not test a picking table, real picked Entity identity, or stale reconciliation. |
| `Scenes\SceneLoader.cs:165–204`; `Scenes\PreparedScene.cs:80–106`; [compiled-producer proposal](m03b-scene-preparation-compiled-producer-evidence-addendum.md), section 5 | Current public Scene preparation deliberately refuses: `QualifiedFootprint => null`. Its real J/K allocation catalogue and producer last-access gates must close before a package→Scene→Extract picking observation. Existing extraction/consumer test source is not proof that this current guarded path executes. |
| `RenderSnapshotAssetPins.cs`; `Entities\WorldGuidUseControl.cs`; `WorldLifetimeControl.cs`; `WorldGuidRegistry.cs:293–303,1757–1785` | Independent package retention and exact-grant binding checks have concrete owners. `WorldGuidUseControl.IsActive` means the **use carrier** is active, not that its original World binding is still active. Neither copied GUIDs nor retained asset pins confer reconciliation authority. |

Unqualified source paths in this table are under `sources\TerraFX.Runtime`.

### Relationship to prior proposals

Use the picking sections of [the original closure](m05-owner-contract-closure.md), [M05/M03-B closure](m05-m03b-owner-contract-closure.md), and [rectification](m05-owner-contract-closure-rectification.md) as tentative predecessors, not maintained authority. Retain the rectification's camera-qualified policy, complete table, explicit native submission, and strict reconciliation. Retain its addendum's exclusion of cosmetic particles; the [particle owner proposal](m05-cosmetic-particle-pool-owner-closure.md) does not give particles Entity-selection membership.

This narrower first slice makes three explicit refinements to those unimplemented picking recommendations:

1. Use the existing static registration and **derived fixed pick cutoff 0.5 for Material 1.0**. Do not require the proposed version-2 component/registration path merely to pick existing static content. This fixed value is proposed picking behavior, not a configurable field already present in Material 1.0.
2. Defer all skinning, including existing static BindPose, from the first picking raster variants. BindPose is not live Animation, but still needs its exact skin/palette/diagnostic-bearing graph and budget evidence. Do not bypass the separate [diagnostic recording/completion gates](m05-skinned-normal-diagnostic-recording-budget-closure.md).
3. Propose `Resolve(World world)`, not the earlier no-argument `Resolve()`. Supply the original World explicitly for synchronous owner reconciliation; the asynchronous operation need not retain the entire World facade, find a World by GUID, or queue a hidden owner callback.

Material 2.0 custom cutoffs, [authored LOD](m05-mesh-lod-native-authoring-closure.md), animated palettes/Sprites, and their [proposed runtime ports](m05-owner-contract-closure-runtime-ports.md) remain later produced variants. Loading their codecs does not enable them. Their presence in a requested path must refuse explicitly, not downgrade to v1/base geometry/bind pose.

## 2. Owner boundary and exact input data

Rendering owns the query, immutable ID table, selection shader/layout, graph additions, and operation. Entities owns qualified identity, exact binding/use issuance, and owner/cohort access. Scenes supplies actual camera/pose data through the existing lower-issued read lease. Assets owns immutable package backing; Graphics owns native resources, recording, transfers, fences, and uncertain retirement. Keep Core BCL-only, Graphics/Assets independent of Runtime, Entities independent of higher namespace types, and Runtime independent of Content.Tools.

**Proposed extraction amendment, not an existing snapshot guarantee:** while the actual `Renderer.Extract` holds its one `WorldFrameReadLease`, capture:

| Data | Exact source and meaning |
|---|---|
| Snapshot header | Existing SnapshotId, WorldGuid, CompletedTick, dimension, selected camera order, and TargetExtent; privately copy the actual source `World.OwnerId`. It identifies this construction only, is not caller-supplied, and allocates no new owner identity. |
| Spatial context | The complete existing dimensional frame, including profile/settings/origin/extent/revision/owner; a 2D result additionally retains the camera's Frame3. Never combine independent later frame getters with earlier draw roots. |
| Camera provenance | `World.GetEntityIdentity` for the actual selected camera Entity, plus its existing copied pose/projection/near/far/aspect/normalized viewport/layer mask and quantized physical RenderArea. CameraSnapshot's current bare CameraId alone is not the proposed qualified provenance. |
| Logical draw provenance | For every counted static Mesh group/Sprite, copy `World.GetEntityIdentity` while capturing that draw, and the existing stable DrawId. Preserve the current recipes (`mesh-group/` + eight lowercase hexadecimal group digits; `sprite/0`). |
| Silhouette inputs | The existing exact Mesh group/index/vertex data, current transform/parity, or Sprite quad/Atlas frame/flip/tint/pixelSnap; actual per-camera ordering/layers; exact Material, sampler, texture, and package pins. No bounding proxy, live ECS pointer, guessed material, or latest-version lookup. |

These are private immutable output data, not public Entity constructors, public Mesh accessors, a new schema, a canonical component, or another cohort actor. The existing static participant must produce them through real extraction; its additional snapshot allocation/retention must be funded before copying. Old snapshots without this provenance are not retroactively made pickable.

### Complete dense-ID membership

Let N be the full eligible entity-backed logical Mesh-group/Sprite draw count already admitted into that snapshot, unique across its selected cameras and **before frustum culling or batching**. A 2D snapshot contributes its Sprites; a 3D snapshot contributes its Sprites and Mesh groups. Retain the union-of-camera-layer admission used by extraction. Lights, UI, cosmetic particles, shadow-only work, and custom passes are not members.

Merge the two already RFC-byte-sorted draw arrays into one strictly increasing DrawId sequence. Each immutable entry is `(WorldEntityIdentity, DrawId)`; its dense UInt32 ID is its zero-based position plus one. Zero is reserved. The table is camera-independent; the requested camera determines which members actually rasterize. A member selected only by another camera retains its table position but produces no fragment for this query.

Validate all entries before publishing a table: complete valid qualified identities for this source World, nonempty DrawIds, strict merged uniqueness, exact total count, and complete draw-index→dense-ID maps. Several groups/Sprite draws may legitimately share one Entity identity; duplicate DrawId is invalid even if the Entity matches. Do not deduplicate groups, concatenate kind-local tables, number by camera arrival, or renumber after culling. Empty membership is valid, but it does not authorize fabricating a successful zero GPU readback.

## 3. Query semantics, ordering, and refusal

- **Address:** `(pixelX,pixelY)` are signed integer texel indices in top-left-origin physical target coordinates. The selected sample is that pixel's single sample, geometrically at `(x+0.5,y+0.5)`. Require both target bounds and the selected camera's half-open quantized RenderArea. Use existing `RenderCameraViewport.CreateArea`: independently round exact normalized edges to nearest-even pixels, then subtract edges. Never clamp, floor a DIP, apply DPI twice, enlarge an empty viewport, or switch cameras.
- **Meaning:** query only the requested camera's own world rendering, freshly initialized for this selection pass. It is not a readback of a previously presented frame or of the final multi-camera composition. Other cameras' clear/depth history does not select or occlude within this camera-qualified query. Physical metrics are the captured target extent plus normalized/quantized viewport; no new Platform metrics epoch or DIP conversion API is assumed.
- **Geometry:** use the admitted static silhouette, clipping, front-face/mirrored parity, culling, selected UVs/sampling and Sprite pixel snapping. Opaque fragments are not rejected by alpha. Mask keeps ordinary linear alpha **greater than or equal to** its existing AlphaCutoff. Blend keeps ordinary output alpha **greater than or equal to 0.5**, independently of mask cutoff. Do not infer alpha from RGB, premultiplication, bounds, or tone-mapped color.
- **Order/depth:** replay the selected camera's existing C0 stage/order: opaque/masked Mesh draws in stable DrawId order, blended Mesh draws by descending camera-view Z then RFC DrawId, then ordered blended Sprites by `(sortingLayer,sortingOrder,-cameraViewZ,DrawId)`. Mesh uses its admitted reversed-Z `GreaterEqual` test, writing depth for opaque/masked but not Blend; current Sprite C0 has depth disabled and no depth write. Disable color blending for the integer attachment. At exactly equal passing depth, the later executed ID write wins. Thus this is not nearest-distance sorting, lowest dense ID wins, or most-opaque selection. Preserve adjacent-only batching and primitive order.
- **Ties:** stable RFC DrawId breaks equal Mesh depth and equal Sprite layer/order/depth ties. Finite `+0`/`-0` view depths compare equal; their stored bits are not an ordering discriminator. NaN is invalid, not a sortable depth. A later Sprite can overwrite a nearer Mesh because that Sprite path has no depth test. No tolerance-based GUID tie or worker/insertion/hash order is introduced. Existing hardware raster edge rules apply; tests claiming exact interior IDs must not rely on an unspecified cross-backend shared-edge tie.
- **One result:** no hit array, visitor, `Required/Written` protocol, nearest-hit truncation, or partial prefix. A valid zero is `NoSelection`; a valid nonzero maps through the retained table only. A nonzero value greater than N is invalid readback data, not a miss.

**Distinguishing witnesses:** an opaque Mesh-A, blended Mesh-B, and Sprite can have RFC DrawIds ordered Mesh-B/Sprite/Mesh-A while rendering Mesh-A/Mesh-B/Sprite; IDs must be 1/2/3 by the former order, and the last passing Sprite writes ID 2. A Blend alpha of 0.4 fails picking even when a separately authored mask cutoff is 0.2; alpha exactly 0.5 survives. A nearer low-order Sprite does not defeat a later high-order Sprite merely by distance.

Invalid/default/unissued data, negative/outside pixels, unknown camera, nonfinite source data, singular/inadmissible transforms, wrong schema/version, missing pins, incompatible target, and unsupported silhouettes are failures, never background. Keep existing typed argument/owner/capability failures; do not silently fall back to a ray, Physics, another backend, another LOD, or a previous frame. Checked count/range/byte arithmetic must reject before allocation, narrowing, native recording, or publication; IDs never wrap.

Chapter 33 PH17–PH21 defines a different Physics contract (inside policies, coplanar rays, nearest-first hits, handle ties, destination/visitor failures). This proposal implements none of it and does not claim those cases from rendering tests.

## 4. Proposed public operation and result closure

**Every declaration in this block is proposed, not an existing Chapter 30 member.** It refines the earlier picking projection only for this bounded slice. Existing `World`, `RenderSnapshot`, `Surface`, `GpuTexture`, `WorldEntityIdentity`, and `RenderSubmissionReceipt` retain their actual owners.

```csharp
// TerraFX.Rendering; System, System.Threading, System.Threading.Tasks,
// TerraFX.Entities, and TerraFX.Graphics supply the referenced existing types.
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

// Addition to the existing Renderer:
public RenderPickOperation BeginPick(World world, RenderPickRequest request,
    CancellationToken cancellationToken = default);

public sealed class RenderPickOperation : IDisposable
{
    // Internal issuer only; no public constructor.
    public Task<RenderPickCompletedReceipt> Completion { get; }
    public RenderPickResult Resolve(World world);
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
    public RenderSubmissionReceipt? Submission { get; }
}

public readonly struct RenderPickResult
{
    // Internal issuer; default is unissued.
    public bool IsValid { get; }
    public RenderPickStatus Status { get; }
    public Guid SnapshotId { get; }
    public Guid CameraId { get; }
    public long TickIndex { get; }
    public WorldEntityIdentity? Entity { get; }
    public Guid? DrawId { get; }
}
```

Both request constructors borrow exactly one nonnull target and a nonnull snapshot; they acquire no credits or trusted epochs. Null references throw `ArgumentNullException`, empty/unselected camera throws `ArgumentException`, and negative/outside pixel throws `ArgumentOutOfRangeException`. Mutable target readiness/identity is checked again at BeginPick. Requests may be reused, but each accepted BeginPick has independent ownership.

BeginPick runs synchronously on the exact idle, committed/restored World owner. Validate references, World owner/phase/liveness, genuine snapshot source-owner/frame/camera, query shape and target, then cancellation, then count/use/byte admission. A headless Renderer throws `NotSupportedException` before native admission. A pre-canceled token throws `OperationCanceledException` at that admission step and creates no operation. The token is ignored after successful operation admission, matching an admission-only cancellation policy; a caller may separately cancel its observation with `Task.WaitAsync` without changing the operation. Usage/capacity refusal is synchronous; after acceptance, pipeline/graph/native/transfer failure faults Completion with contextual cause and no valid completed receipt.

The operation owns **one ordinary built-in color graph** for the supplied snapshot/target, augments it with selection/copy work, compiles, and submits it once. It therefore also renders a frame; an interactive request may present that frame. The application must not separately submit the same intended frame expecting BeginPick merely to sample it. No caller-modified graph is accepted by this first API. Existing BuildGraph/custom-pass/Submit APIs are unchanged.

Completion succeeds only after actual graph GPU completion **and** selection transfer/map/CPU copy completion. An issued receipt has `IsValid=true`, copied original identifiers/tick, and nonnull Submission. Default is unissued with null Submission; default status zero never denotes an issued Selection. Completion does not reconcile identity or establish visible presentation. Interactive presentation still requires the separate real `Surface.WaitForPresentationAsync`; offscreen receipts have no presentation sequence and zero surface/swapchain epochs.

Resolve requires successful Completion; premature/failed-completion calls throw `InvalidOperationException`, a null World throws `ArgumentNullException`, and a disposed operation throws `ObjectDisposedException`. Validate the supplied World's immutable source OwnerId against the captured one; a different construction, including a same-GUID successor, throws `ArgumentException`. For a live nonfaulted binding, first resolution requires its owner-idle committed/restored boundary; report an active faulted World before attempting the committed read lease. The passed World is borrowed only for this call and is not retained by the asynchronous operation. Once resolved, return the cached copied result on repeated calls with the original World argument; do not perform a second lookup against newer state.

### Reconciliation precedence

1. Check the original exact binding through the retained lower grant, without entering disposed World storage. Revocation/closure yields `StaleWorldBinding`; an active faulted World yields `WorldFaulted`.
2. Under one real `WorldFrameReadLease`, compare all relevant complete captured frame descriptors/revisions. A change yields `StaleFrame`; no implicit rebase, reproject, or remap.
3. Compare the actual target/device identity, epoch, extent/readiness, and interactive Surface/Swapchain epochs. A change yields `StaleTarget`, even if resized back to the same dimensions.
4. Resolve the original **qualified** camera identity and compare the same derived pose/projection/layer/viewport facts and quantized area used at capture. Removal/replacement/change yields `StaleView`. Reuse the actual camera capture interpretation; do not build a second camera math implementation.
5. Only now interpret zero as `NoSelection`. Nonzero outside the retained table throws `InvalidDataException`; cache that terminal failure, release only safely ended ownership, and issue no result.
6. Map a valid nonzero entry and use `World.TryGetEntity(WorldEntityIdentity, out ...)` in this exact World. Missing identity yields `StaleEntity`; otherwise return `Selection` with the original qualified identity/DrawId.

Every issued non-Selection result has null Entity/DrawId. Selection has both; none returns an ephemeral Entity handle. An unrelated later committed tick alone is not stale. A selected Entity that moved or lost its render component may still yield its live qualified identity: this is a query of the **old snapshot**, not proof of current pose/draw membership. Its old SnapshotId/TickIndex are never relabeled.

**Canonical disposition:** picking changes no canonical state, identity cursor, replay hash, or committed World event stream. No new event schema, sequence, automatic selection component, or stage-9 callback is introduced. An application that acts on a result must separately validate/admit its ordinary command/input; GPU completion order is not canonical gameplay order.

## 5. Bounded ownership, storage, and work

Retain the prior tentative ceilings: **N <= 131072**, **four unreconciled/unreleased picks per exact World**, and **W = 67108864 bytes per accepted operation**, plus the existing 256-byte independent-use control (`B_op = 67109120`). These are proposed bounds, not measured footprints or extra headroom. All concurrent uses also fit the exact World's configured shared ceiling and admission domain. The four-entry pending snapshot queue is unrelated and cannot drop an accepted pick.

The concrete per-World issuer is the existing Rendering `CpuExtractionWorldParticipant`, constructed through the existing static support token. Add its bounded pick-slot accounting there, not to an application system, global WorldGuid dictionary, Graphics, or a new phase/cohort participant. A retained slot control must contain no World/Scene/participant back-reference. Its qualified standing storage remains owned until its actual users stop, or transfers to already funded operation storage before World storage is refunded. A missing concrete storage handoff blocks this integration; a counter in an uncharged object is not closure.

Admission and construction use this order; no operation-owned allocation precedes its funding, and no borrowed mutable data escapes:

- Claim the slot and the exact `World.AcquireGuidUse(W)` route; atomically roll back unescaped claims on failure. Anchor the concrete operation in that use before work escapes.
- Prepay the complete simultaneous table/maps/header/validation scratch, graph-building/recording coexistence, request/task/continuation/publication tails, retained wrappers, copied scalar result, and cleanup/failure ownership not already funded by another actual owner. Snapshot provenance/output storage needs its own real reservation before extraction allocation; no new `RenderExtractionLimitKind` is assumed.
- Acquire independent ownership of the original immutable snapshot data/package pins and selected target uses; build and validate the owned table before BeginPick returns. Later recording must consume those admitted captures, not reacquire a disposed caller facade. The proposed private admitted-use path stays inside the existing Renderer/RenderSnapshot owners, preserves the original producer identity, and constructs no replacement RenderSnapshot. Public disposed-input guards remain intact. Resource retention is not a promise of success after a later device/Surface epoch change: native admission still revalidates epochs and can fail safely.

Storage/work bounds for that construction are:

- A table entry contains four GUIDs of qualified identity plus DrawId: **80 logical payload bytes**, or 10485760 bytes at maximum N. Draw-index ID maps add at most `4*N = 524288` logical bytes. These are lower bounds, not CLR/array/header/alignment or full-operation charges. The already sorted input arrays permit an O(N) merge and O(1) dense lookup, with bounded O(N) output/maps; do not add an unbounded sort/cache/BVH.
- Use checked `int` counts/indices/pixels, `long` byte arithmetic, and checked UInt32 ID conversion. Preflight the complete eligible count and bytes, not a prefix. A fifth slot or unfundable W throws contextual `InvalidOperationException` before escape; a funded cap is not permission to allocate beyond its proven remaining envelope.

For exact-binding observation, propose one closed lower accessor **`WorldGuidUseControl.IsBindingActive()`**, implemented using that issued control's actual registry/grant and generation checks already present in `WorldGuidRegistry.IsActive(in WorldGuidGrant)`. It cannot find a binding by GUID, issue another use, invoke user code, retire/quarantine an active World, or grant ECS access. It must not be implemented as the current carrier `IsActive` property. Revocation and same-GUID replacement remain serialized by the existing registry/domain/binding protocol.

Dispose is idempotent and may run on an ordinary thread. It closes future Resolve calls; it does not synchronously wait for or cancel admitted GPU work. A concurrent first Resolve/Dispose has one operation-gate winner; no double release or partially published result. Completion remains an actual completion observation even if the wrapper was disposed. After native consumers stop, Resolve or Dispose ends reconciliation interest and the slot/use; a cached copied result owns neither World authority nor native resources. An execution failure also closes reconciliation interest: its driver must finish safe cleanup/release without requiring a user Resolve call, retaining any unresolved cleanup/quarantine debt.

Keep snapshot/table/package pins, pipeline/target/selection/depth/copy owners, staging/map credits, and the operation anchor through their actual consumers. Do not retain a whole World across asynchronous work. World closure invalidates reconciliation, not accepted native ownership; the lower retained grant permits stale reporting without an owner callback into disposed storage. A retained unresolved operation may continue consuming its bounded credits until Resolve/Dispose; closure must not require that callback to make GPU work complete.

Attempt every independent cleanup; refund only confirmed releases. Uncertain native completion follows Graphics quarantine. Failed use/control cleanup retains its exact remaining graph/charge, not a successful empty result or active-binding quarantine. Completed Tasks/receipts must sever live resource/authority links after the corresponding producer tails truly end; Task terminality, cancellation, finalizers, delay, or GC reachability is not last-access proof. **Unknown allocation multiplicity, retained graph, footprint, or release edge keeps the affected operation profile unavailable.**

## 6. Native work is a separate producer gate

Use real same-device single-sample 2D R32UInt ColorAttachment|CopySource storage and D32 depth. Clear the **complete** ID/depth subresources to zero before the selected camera's partial RenderArea; then load them for selection. This satisfies Chapter 19's subresource validity rule without changing the ordinary color graph's own clear/validity requirements.

Reuse static geometry/transforms/UV/sampling/alpha calculations. Propose the rectification's separate 16-byte Object-group `b1` selection uniform `uint4(DenseId, binary32-cutoff-bits,0,0)`, with its proposed `material/layout/picking/v1` and `material/pass/picking/v1` identities and BindingLayoutVersion 1. Resolve those exact keys by the rectification section 1.1's existing Chapter 10 name-UUID recipe; do not allocate another namespace. These are proposed shader-layout/pass keys, **not new EBIN schemas or approved catalogue assignments**. Existing ObjectGpuData/Sprite reserved lanes stay unchanged. Actual DXIL/SPIR-V reflection must expose a scalar unsigned `SV_Target0`, the exact added binding, and the retained stage interface; never encode an ID through float color or tone mapping.

Declare selection writes, copy reads/writes, dependencies and side effects in the real graph. Copy `(x,y,1,1)` to the origin of a real owned **1×1 R32UInt** CopyDestination|CopySource texture, then use existing full-subresource `ReadbackTextureAsync`. The region equals the whole destination mip. Decode the first UInt32 little-endian for the supported little-endian desktop profiles from the owned pitched result only after its fence/map/CPU copy; validate format/subresource/extent/pitches. No transient texture materialization, new transfer API, synchronous render-thread mapping, or synthetic zero-filled failure is selected.

Budget actual native allocation requirements/alignment, images, descriptors, uploads, command/graph storage, staging, readback, and retirement through their real owners before acceptance. `8*width*height` is only the ID+depth texel-payload lower bound, not an allocation estimate. A one-pixel request still rasterizes admitted geometry over its camera area; it does not bound fragment work to one pixel. Geometry work is bounded by the existing admitted asset streams/draw counts; target dimensions by actual admitted Graphics limits; pass/recording/storage must additionally fit the ordinary graph/device budgets. Do not implement unbounded per-request image/pipeline caches.

Fail the whole picking operation if the complete augmented graph cannot be admitted; do not remove ordinary diagnostics/work, shrink resolution, skip draws, or borrow the skinned diagnostic's separate 32-MiB/16-MiB envelopes. A declared format, existing R32UInt test, compiled graph, or CPU ID lookup does not prove this actual selection pipeline works on D3D12/Vulkan.

## 7. Implementation batches and required evidence

Rows are work descriptions, **not new acceptance IDs**. Each later implementation batch includes its own tests, directly related documentation, and affected validation; none is performed by this proposal.

| Batch | Exact predecessor and deliverable | Evidence boundary |
|---|---|---|
| CPU membership/query data | Review the selected data/order/refusal declarations; implement bounded merge/maps, pixel validation, and copied-ID lookup with no native/World callback. Use ordinary value inputs; obtain any trusted qualified identities from real World issuance. | Literal mixed-kind order, duplicate/conflict, zero/maximum/out-of-range ID, half-open pixels, checked counts/byte overflow, and unchanged sentinel/no-partial output tests. This is helper evidence, not a snapshot or visual query. |
| Real static provenance and lifetime | For the ordinary producer, close the named Scene J/K/supplier last-access gates. A real restored-World producer may independently satisfy the snapshot prerequisite only after its own package/Scene/cohort/funding gates qualify; it does not close ordinary preparation. Add qualified camera/draw provenance, source-owner stamping, funded snapshot storage, pick slots/use anchoring, and concrete retained-control handoff. Preserve old static content/registration. | Extend existing Mesh/Sprite extraction tests through the actual selected public Prepare→Activate→Step→Extract or qualified restore→Extract route. Independently derive exact membership, including multiple groups, two cameras, layer filtering, and retained package replacement/unload. No internal snapshot/camera/use-control fabrication or test-only preparer. |
| Complete static native operation | Previous batch plus actual M04 device/target, qualified operation envelope/tails, real compiled/reflected selection variants, full graph budget and copy/readback. Implement both proposed target overloads, Completion, Resolve, and Dispose together. | D3D12 and Vulkan exact interior IDs, background, Mask/Blend cutoff equality, texture/vertex/tint alpha, mirrored cull, rotated-camera ties, pixel snapping, mixed Sprite/Mesh order, partial viewport, and empty membership through real raster/readback. |
| Reconciliation/failure ownership | Same real operation and exact lower grant; ordinary owner/cohort reads. | Retain old real snapshot/table, destroy/reuse Entity storage, finish readback, then resolve: StaleEntity, never the occupant. Repeat same-GUID replacement, camera replacement/change, rebase, resize/back-to-same-size, disposed target/World, and unchanged-context later tick. Check precedence before zero and original historical tick/identity. |
| Public and target qualification | Completed static owner path and actual public-only caller roots. | Nullable/default/constructor/affinity compilation; managed Debug then Release with nonzero selected/executed NUnit counts; independent native and published NativeAOT consumer execution for Windows D3D12, Windows Vulkan, Linux Vulkan as applicable. Interactive presentation wait is separate from offscreen readback. |
| Later silhouette variants | Real versioned component/Scene/package bindings for Material 2.0; actual per-camera LOD selection/history; actual skin/palette producers and applicable diagnostic budgets. Animation needs its reviewed preconstruction/state/extraction extension. | Add their own native parity/retention observations only after those producers exist. Do not infer them from the first static pass, codec tests, a BindPose helper, or the standalone LOD kernel. |

Add focused lifetime/refusal tests for four retained operations versus the fifth, exact cap refusal before allocation, pre-cancellation/no admission, disposal/cancellation during real consumption, readback padding, cleanup debt, and task/result severance. Controlled backend/scheduler tests qualify only their controlled slice, not real native loss/timeout. Do not make a second complete CPU rasterizer/skinner the expected-value source.

Select the existing **EN117**, applicable **EN069/EN070**, **EN055/EN085/EN088/EN095**, and **AR01/AR02/AR07/AR08** observations actually exercised. Chapters 32 sections 2–5 retain their complete integrated witnesses; Chapter 33 Physics observations remain separate. A parent case stays incomplete until its complete required membership/variants execute.

Use the existing owning Runtime/Graphics test projects and `docs\project\Building.md`; choose focused filters and verify fresh executed counts/TRX under `artifacts\tst\<Configuration>`. Use `Building.Consumers.md` for public/NativeAOT execution and `global.json` for SDK selection. No new runner, source inventory pipeline, duplicated SDK pin, or acceptance identifier is proposed.

## 8. Explicit no-go and retry conditions

| No-go condition | Required change before retry |
|---|---|
| Public Scene preparation still has unavailable QualifiedFootprint, incomplete supplier workspace/receiver ownership, or no production-observable final access/transfer | Close that exact M03-B producer boundary with its real allocation/compiled/runtime evidence before the ordinary preparation consumer runs. Do not bypass it with fabricated Scenes/private snapshots, unqualified restore outputs, guessed J/K, fake preparers, or old green test results. A separately qualified restore route remains its own producer, not proof of ordinary preparation. |
| Missing qualified draw/camera identity or exact construction provenance | Amend the actual extraction producer under its real cohort and storage ownership. A current GUID/Entity lookup cannot repair an older unqualified snapshot. |
| Unclosed pick-slot/storage handoff, operation/task/failure envelope, binding check, or release tail | Establish concrete finite funding and last-access ownership before enabling the affected path. Do not borrow the Scene cap, assume carrier activity means binding activity, retain an uncharged World, or add passive wrappers to simulate acknowledgment. |
| Unproduced Material 2.0/LOD/skin/Animation/custom/UI/particle silhouette requested | Refuse that unsupported scope; complete its real owner producer and query declaration before adding a variant. Never silently omit it or select another policy. |
| Unsupported native usage/pipeline, budget/checked arithmetic refusal, graph rejection, failed/uncertain fence/map/readback | Fail/refuse with original context and preserve required ownership/quarantine. No valid background, partial table/graph, smaller output, old frame, or backend switch. Retry only after capability, admission, or safe retirement evidence changes. |
| Missing target-specific public/native/AOT or mandatory failure observation | Leave that observation unqualified. M09's real driver/OS loss, submitted-work/presentation-timeout and uncertain-retirement stimuli retain their current owners and safe-stimulus gates; do not repeat unchanged blocked probes or invent substitutes. They are not blanket prerequisites for ordinary dependency-ready picking implementation. |
| Review inputs change or transitive declaration closure is unresolved | Re-freeze/review the affected bytes and owner decisions before disposition/dependent work. Tentative guidance is not portable acceptance. |

## 9. Frozen direct review inputs

Base HEAD: `31f156a51ccc462ae9c5cc8d4e87e00c0054dec4`. Freeze completed: **2026-11-17 03:48:48 UTC**. The following **70 complete files** are the direct contract/proposal/source/test/build inputs, even where only applicable ranges were read. Hashes are raw-file SHA-256; C/M/U mean unchanged/modified/untracked against that base. Dirty implementation/schema bytes are inputs, not work performed here.

Repository-wide filename/symbol/search discovery and out-of-scope exploration do not disposition other files. This is a feature-specific review boundary, not a whole-worktree inventory, runtime provenance mechanism, or qualification manifest. The new proposal is the output and is excluded from a recursive self-hash.

| Input | State | SHA-256 |
|---|---|---|
| `docs\project\Roadmap.md` | M | `649182860D65DC902A406BCAC482AA1D286465BAFB0B34143452C8B09E51EBDE` |
| `docs\project\Building.md` | M | `F043A352870EFB6C7D4A61998225E2508A8F4370A57E784483A41819D452075F` |
| `docs\project\Building.Consumers.md` | C | `8AF46079A9E11E01578CC9B76B695801B585F58148DF36418EE5506CA4006442` |
| `docs\design\04-capacity-and-workload-profiles.md` | M | `A4D86F3FA373A64548435C53582BF5916E29D77F34650F9D161CCEC90E6F0245` |
| `docs\design\10-binary-data-and-authoring-formats.md` | C | `56721136AECAE18C20EB3E9A1BECEB527CC0676DD6B43CB506538AD63DECBB5F` |
| `docs\design\12-entities-components-and-queries.md` | M | `174B2465746DA79CEF560C8506216EF4F99F9D1AABAC184F872C9E6CB0B94E0B` |
| `docs\design\16-scenes-transforms-and-cameras.md` | M | `B012F42B23CEDE9C7CDB71203D8357CD19E1626823EA25EF3119961292D3698A` |
| `docs\design\17-graphics-device-and-resources.md` | C | `8157E7DA5472B9AABD891A05E84F2B997487A5C5E3FF67CB589681747D628718` |
| `docs\design\18-shaders-and-material-pipeline.md` | C | `AF20911D9D06BFB58F6C771511181F59347C4659129CFD5FC8CF622ED191C0D7` |
| `docs\design\19-render-graph-synchronization-and-presentation.md` | C | `AB673DEDBD85B7A161C5D7DC871BF5B486911DC27EC4279AC43827B005241B8B` |
| `docs\design\20-animation-and-deformation.md` | C | `BBEF0A36E0832C2BD0D54A07E479917DB923671B64F7176369F8F57A8F1618C5` |
| `docs\design\21-2d-and-3d-rendering.md` | C | `989A6183186592F165FA9614CC19A8FED63826E2A51170CE55FBFFFCA8116ED2` |
| `docs\design\29-project-setup-build-and-deployment.md` | C | `3B55A013EEC327ECD648ACBD5B53717DD218AD288E8047AC21277609EFA2F47B` |
| `docs\design\30-public-api-review-surface.md` | M | `15848C82F3858A77A87A0D3EA6FBCC07046EB6E77CA5B21748C72B90EFAB598B` |
| `docs\design\32-end-to-end-integration-scenarios.md` | C | `6AF2211CB79FECA449D0F701752F86B8D093162AD75673770B0C0DE0F49BA4FC` |
| `docs\design\33-physics-acceptance-and-reference.md` | C | `288E9469AD6B9998921AA02C67D9272E61EC925B6583DF108BE84AFF33058A9E` |
| `docs\design\34-engine-acceptance-cases.md` | C | `532A68F1717DC05901DFC4537204E65B31B28EE46326F64F5A1CD7834332AA41` |
| `docs\design\35-implementation-roadmap.md` | C | `24EEC6E92B7AD904D552F290EF748FFC8FCE13B6B94A89EBD9F39487D4584D87` |
| `docs\proposals\m05-m03b-owner-contract-closure.md` | C | `FC290975441F950EFD4621A0A2DCB32E71A022228C48211CDDDF5F5FB6A70585` |
| `docs\proposals\m05-owner-contract-closure.md` | U | `6FEE16009BF0D1FB529E68C8DF1E3293A852468E71D9A291984D8A64A9A62BC8` |
| `docs\proposals\m05-owner-contract-closure-rectification.md` | U | `67134F02B92D316524848ECB8E0C917EF868DA3D2EDA5FC581DBAA25B1825632` |
| `docs\proposals\m05-owner-contract-closure-rectification-addendum.md` | U | `C8DE6581D5CB126D18C294FC9EB22A01459B2D6BFC3AD933ECB1652582634F4A` |
| `docs\proposals\m05-owner-contract-closure-runtime-ports.md` | U | `605CF1D92DC362A8B73EA9BDA3A5467757942C268712F484AF53D9A9159D53E3` |
| `docs\proposals\m05-cosmetic-particle-pool-owner-closure.md` | U | `888D97BA6BC9AC2F06295D725162074F85C275658CDB3D0DDF31B8DA89904A95` |
| `docs\proposals\m05-mesh-lod-native-authoring-closure.md` | U | `C9381998FC1A1B13800845FD87702417BC4EEC9BE2309B9264A46B6E47BEBCAB` |
| `docs\proposals\m05-skinned-normal-diagnostic-recording-budget-closure.md` | U | `5787039DC3BA43A862529CD9F01D82623DDFEC1B273194CC73FC50F079E0C753` |
| `docs\proposals\m03b-scene-preparation-compiled-producer-evidence-addendum.md` | U | `ECCC174D5DF86E195731EB3BE74FBE5A349D0214C8F94DE35769503D4418595C` |
| `sources\TerraFX.Graphics\RenderingSchemaCatalogue.cs` | M | `DE25A8C0E249FF9AB5B592FE848C85429C004FE629694C64EF1AFEF3BA731614` |
| `sources\TerraFX.Graphics\GraphicsPipelineDescription.cs` | C | `FBB9388C6E7C89CD4BE3FA0AB915DDFAA38DCA8659ADDC74A32CCE788C859A29` |
| `sources\TerraFX.Runtime\Rendering\RendererCpuFeatures.cs` | U | `AD662321F076BB0BF40E06634C11A36F44127F6E7E773D6E33BA612876918098` |
| `sources\TerraFX.Runtime\Rendering\Renderer.M05AssetCodecs.cs` | U | `A5346D4B04FC594C8713C0628A1035F75C97FE664808BA45069898C095CE16CB` |
| `sources\TerraFX.Runtime\Rendering\Renderer.CpuExtractionSupport.cs` | M | `EAF1F94AFF0886B3FA30AF97BD7F83180A38AB744D4241D0CDD275A055EADB55` |
| `sources\TerraFX.Runtime\Rendering\Renderer.Extract.cs` | M | `274298BFDCEE321141D4138D417807951FCC0EF6BCAC1DFCE2726D5F36829F2D` |
| `sources\TerraFX.Runtime\Rendering\RenderSnapshot.cs` | C | `733C8A58053977A349742540639FC55AF0CD299DB33C7394D1DC999A17D30AF0` |
| `sources\TerraFX.Runtime\Rendering\RenderMeshSnapshot.cs` | C | `9003FA81C790BB8CF395CD327063C44EC1B25A14D99875E64AA148BDF0663D05` |
| `sources\TerraFX.Runtime\Rendering\RenderSpriteSnapshot.cs` | C | `28DA4B8894D2F2A49EAFAEE91243C295ED7F01498CD9EB920535A6FD6D365B26` |
| `sources\TerraFX.Runtime\Rendering\CameraSnapshot.cs` | C | `E801A48425A7BAAB220329C3818B3A5D60669CC696308FF694A7E0C528894E70` |
| `sources\TerraFX.Runtime\Rendering\RenderSnapshotAssetPins.cs` | C | `8417756EA5D9DC6363D71C6274B2B6B8849C179BFFDA0D1133A4FFB81FCD0A1C` |
| `sources\TerraFX.Runtime\Rendering\RenderCameraViewport.cs` | C | `DBA62E282FC85FF2BBBC4FF2F13E8B5CFE920B421CBF750F61452F589C80E421` |
| `sources\TerraFX.Runtime\Rendering\RenderMaterialAsset2.cs` | U | `0208747BC207815D07E2584AC262ECE0B8D95B3270B4FDF3164C9C01498E4493` |
| `sources\TerraFX.Runtime\Rendering\RenderMaterialAsset2Codec.cs` | U | `925442FF7BC487C7415E320CA36480F0195CFFA32C68527B6F0E0C5F7648CF9F` |
| `sources\TerraFX.Runtime\Rendering\Renderer.BuildGraph.cs` | C | `552A7F939E01CA189DB0EB3F759F7E97ADC7BD2FF0005391CDFD899860A09560` |
| `sources\TerraFX.Runtime\Rendering\Renderer.SpriteC0Graph.cs` | C | `FF96D6C0CF922CDD80ACFB87F5DAAFEA0F0A99A452BDEAA54B33223534C3F430` |
| `sources\TerraFX.Runtime\Rendering\Renderer.MeshC0Graph.cs` | M | `C1D61709BC3B9AD7325B2B4C47B702C02E8A11A279C6AE3290554EEA4B142E20` |
| `sources\TerraFX.Runtime\Rendering\Renderer.SpriteC0Pipeline.cs` | C | `6849A2F941C4998A88DC8E72E97FD564AF9C913E02D5CD62620AEBA42159CBF3` |
| `sources\TerraFX.Runtime\Entities\World.cs` | M | `692F721BEAD29E5EC258BF7B1844EC785E2D1548E9F53DFA9BDB57A22A01F09F` |
| `sources\TerraFX.Runtime\Entities\World.CpuExtraction.cs` | C | `971EE49B1CCD9C3C629E3CA33198F26BE610FE3D28B4236E371C220201D768FC` |
| `sources\TerraFX.Runtime\Entities\WorldFrameReadLease.cs` | C | `082B507BE6340D31C0B13302F16867EAA431F792052CE06D57712576CB53293F` |
| `sources\TerraFX.Runtime\Entities\WorldGuidUse.cs` | U | `A6120452989F29EC2EF9C2B90E7479CC6635B9B17848C6C683063B3F99D36D22` |
| `sources\TerraFX.Runtime\Entities\WorldGuidUseControl.cs` | U | `EBF8A3F7C1115538CF06379FA65AE339B783117E39F76D1DE61C46C0B5C40466` |
| `sources\TerraFX.Runtime\Entities\WorldLifetimeControl.cs` | M | `B1A9ADD0A1D68CAF4C385973CA4E2602C68E19E46078AA3CB09614848C09A634` |
| `sources\TerraFX.Runtime\Entities\WorldGuidRegistry.cs` | M | `470EC745500181A57D2E0625B3A93EE5E51A899E046C017B3DD490FDB02BE739` |
| `sources\TerraFX.Runtime\Scenes\SceneLoader.cs` | M | `D53823C6C673E881AAD32A3E94508EC5B252CC39D366A99458A6C487E0FE4886` |
| `sources\TerraFX.Runtime\Scenes\PreparedScene.cs` | M | `B73DD4314B63084132AFC2F9325ED98EECCCE65A174F02B16F2DED7EC65F688C` |
| `sources\TerraFX.Runtime\Scenes\PreparedScene.Preparation.cs` | U | `5D210AE9266EFFE8C074DD0CADB8E6C8027A0155424A17F2E3CAB47DA479AA13` |
| `sources\TerraFX.Runtime\Shaders\SpriteC0Pixel.hlsl` | C | `7B49701A09CC8F56103CC7F8AE4F7FF7FC7EC00B0B7C6EABB2E6BA6C41AB10B0` |
| `sources\TerraFX.Runtime\Shaders\MeshC0Pixel.hlsl` | C | `B2BEBCBD46970D567A03099BC4B9EEF21629D2ACC2E094E3814C2D415FE4F951` |
| `sources\TerraFX.Runtime\Shaders\MeshC0TexturedPixel.hlsl` | C | `B9AED847C3169409C0C53A239F66D04C651246B99F6C5578CF968921D5093B67` |
| `sources\TerraFX.Runtime\Shaders\MeshC0Vertex.hlsl` | M | `0DB0EB1F49C06B82534F6A767C4FDE91996D31B75CB9775319C70A0B96C8999D` |
| `tests\TerraFX.Runtime.Tests\Rendering\RenderMeshAsset2CodecTests.cs` | U | `85AB2DC586CF99D33B4D7B5AD6FA4297C6177AFB31EB6D95FF872170B257F302` |
| `tests\TerraFX.Runtime.Tests\Scenes\RenderMeshSnapshotTests.cs` | C | `B161509CAE92232C57CABD57B2DCE790ACF880FEF4D3AD62D747526C66EDD4C2` |
| `tests\TerraFX.Runtime.Tests\Scenes\RenderSpriteSnapshotTests.cs` | C | `959EA83C702256C7095D1AB89E4839D7B4199D8F82D3D4AA54AC66B9D85A111A` |
| `tests\TerraFX.Runtime.Tests\Scenes\RenderPublicConsumerTests.cs` | C | `D08B3435DA4E823553BD53C52BC282FBB7D42699E973D95B630F0D755D58C59E` |
| `tests\TerraFX.Runtime.Tests\Hosting\EngineHostGraphSubmissionTests.cs` | C | `22ED89ACA9E6709710391AB44BBF3D88B10E919D96A347FBACE5ADF93567A63A` |
| `global.json` | C | `1BCE01871321C2DF82E887DA428CE5C121A6C6541FE65017FE3D249BE47E190C` |
| `Directory.Build.props` | C | `770EE4B2A422861A6F23F0D39DD0ECA361A81CB87E64EBAC4E12170F71231D7A` |
| `Directory.Build.targets` | C | `AEEC1D08E8037470239035812351C4045124607D67A354D482112F4D642D4105` |
| `sources\TerraFX.Runtime\TerraFX.Runtime.csproj` | M | `2F1C81E54F0FBF12D01E34D0D840885F5EB0E94A8B8890865EF721B8C6A3C65D` |
| `.github\workflows\ci.yml` | C | `03EFEA672CD52E9EF053E9CE0285E7CA02C6CE6FB0D21268ADE4255ED5A39D58` |
| `.gitattributes` | C | `7B39C552574173F401723BD69A13C1C98C2780D3C10ABAF4A1D8AB214D72EF38` |

Handoff verification matched all 70 raw-file hashes/states and the base; local proposal links, LF line endings, and trailing-whitespace checks were clean. Only this proposal was written. Recheck the inputs before applying any later disposition; acceptance cannot transfer to changed inputs. No build, test, shader compilation, native execution, or acceptance observation is claimed by this documentation-only task.
