# M05 cosmetic-particle pool owner closure

## Status and decision

**Tentative rectification and implementation guidance under the requested blocker policy; not an accepted owner/API contract, implementation result, or qualification result.**

Deliver one Rendering-owned cosmetic CPU pool belonging to one exact World construction binding. Enlist it before World construction, prepare it at stage 8 from real Scene emitters and the previous committed pool, and install its complete state/output with the successful stage-9 marker. Do not put mutable particle state in `Renderer`, an application system, a caller-owned attachment, or a process dictionary keyed by World GUID.

The blocker is the missing production owner and its integration/lifetime closure, not missing emitter mathematics or an absent content cooker. Reuse those existing implementations. Keep live particles, their RNG, counters, recordings and history outside canonical World state. Their authored emitter components remain persistent content. There is no Physics write authority, implicit checkpoint-resumption mode, or new public API/acceptance identifier in this proposal.

Authority remains with Chapters 21 and 13 for behavior/publication, Chapter 30 for public declarations, Chapters 10/12/16/27 for schemas/World/Scene/state ownership, and Chapter 35 for prerequisites and acceptance. This document changes none of them or `docs\project\Roadmap.md`.

## 1. Current evidence and the actual gap

Evidence below is inspection of the current working tree, whose base HEAD is `31f156a51ccc462ae9c5cc8d4e87e00c0054dec4`; it includes pre-existing dirty implementation files. Test source establishes intended coverage, not a fresh execution result.

| Existing evidence | Consequence |
|---|---|
| `docs\project\Roadmap.md`, M05/Next work; Chapter 21 section 5; Chapter 35 capability sequencing and section 2.3 | The 131072-particle ceiling, fixed ticks, recorded engine RNG, observable `RejectSpawn`, cosmetic hash exclusion and separate actual-consumer prerequisites are existing requirements. They do not construct a pool or supply its lifetime contract. |
| `sources\TerraFX.Graphics\RenderingSchemaCatalogue.cs:48-106`; `Rendering\CosmeticParticleEmitterAsset.cs`; `Rendering\CosmeticParticleEmitterAssetCodec.cs` | M05 metadata already contains emitter/color-key/size-key 1.0 and persistent `render/particle-emitter` 1.0. The private emitter codec validates authored space, rates, lifetime, finite ordered velocity ranges, curves, Atlas/frame, Material major 1/2 and 63-bit stream. Metadata is not an executable component factory or a live emitter. Chapter 10 currently has no particle rows to treat as an approved live extension. |
| `Rendering\Renderer.M05AssetCodecs.cs:16-44`; `sources\TerraFX.Content.Tools\NativeParticleCookCommand.cs:17-83,170-204`; `docs\project\Building.md`, native cosmetic particle cooking | Explicit asset-codec selection and a real native emitter-to-EPKG2 producer exist. The cooker reloads its package through AssetStore and validates the exact reference closure before atomic publication. It does not author/activate a Scene or create a pool. The feature flag selects asset codecs, not World participation. |
| `Rendering\CosmeticParticleEmitterAssetBinding.cs:14-63` | Binding validation retains emitter/Atlas/pages/Material while checking, returns sorted AssetIds, then disposes those leases. Its returned ID list is **not** a retained runtime resource owner. Live births need actual independent ownership of that same immutable backing. |
| `Rendering\CosmeticParticleKernels.cs:11-180,233-275`; `tests\TerraFX.Runtime.Tests\Rendering\CosmeticParticleKernelsTests.cs` | Checked lifetime quantization, exact normalized age, linear curves, finite spatial integration and PCG32/fixed-three-word velocity sampling exist. Symbol references to the kernel class are currently its declaration and this test fixture, not a production pool. |
| `Entities\WorldRegistrationBuilder.cs:33-86,176-252`; `Rendering\Renderer.CpuExtractionSupport.cs:59-94`; `Scenes\SceneLoader.cs:385-416` | The current CPU support registers Mesh/Sprite/three light component families only. Its isolated staging builder explicitly forbids resources, attachments, systems and tags. Scene preparation rejects components without a real registered factory. No particle support token/controller/selected CPU-options implementation currently supplies the missing route. |
| `Entities\World.Ticks.cs:37-196`; `Entities\IWorldCpuExtractionParticipant.cs`; `Entities\IWorldSceneParticipant.cs` | Step executes scheduled systems, structural playback, validation and publication, but no particle engine phase. The CPU participant provides a frame-read lease and disposal, not stage preparation/commit callbacks. Existing idle Scene-resolution ports are not issued stage-8 capabilities. |
| `Entities\World.CpuExtraction.cs:17-70,148-175`; `Entities\World.Controls.cs:846-865`; `Rendering\Renderer.Extract.cs:389-505,774-845,901-1178` | Real committed extraction checks exact participant/World, marker, revision and cohort generation. The current cohort carrier holds frames/generation, not particle-owned roots. Sprite capture and pinning require an active SceneInstance and its prepared bundle; this cannot represent particles surviving emitter/Scene unload by inventing Entity or Scene IDs. |
| `Entities\World.Attachments.cs:331-535`; `Entities\WorldAttachmentTable.cs`; `Entities\World.PersistenceRestore.cs`; `Entities\World.PersistenceReplacement.cs:31-60,120-183` | Transferred registered attachments use immutable State2 families, canonical mutation invalidation and release obligations. Caller-owned attachment payloads do not acquire that ownership or funding. Existing restore publishes only a completed candidate; there is no particle restore/reset join. Neither attachment mode is an implicit cosmetic-pool integration mechanism. |
| `Scenes\SceneLoader.cs:165-204`; `Scenes\PreparedScene.cs:80-82,98-126`; `Scenes\PreparedScene.Preparation.cs` | A precreated preparation root/ordinary driver candidate is now present, unlike the older M03-B proposal snapshots. However, `QualifiedFootprint => null` deliberately refuses live preparation until the J/K catalogue and producer last-access gates close. Current Scene execution is therefore an explicit prerequisite blocker, not evidence that package-to-Scene particle tests can run now. |
| `Entities\World.cs:576-603,1259-1438,1679-1686`; `Entities\WorldGuidUseControl.cs:11,273-327` | World admission/disposal account existing owners; independent operations acquire exact binding uses. A use costs 256 bytes plus its workspace and cannot release active workspace. Failed lower release can leave partially retired obligations; task terminality or clearing the retained owner is not release proof. |

Unqualified source paths in this table are under `sources\TerraFX.Runtime`.

### Relationship to existing proposals

Use the particle sections of [the owner rectification](m05-owner-contract-closure-rectification.md), [its particle identity/order addendum](m05-owner-contract-closure-rectification-addendum.md), and [the private runtime-port closure](m05-owner-contract-closure-runtime-ports.md) as the prior **tentative** declaration projections. Their emitter data/kernels/cooker prerequisites have partly materialized; their live pool, command/result, phase, cohort and retained-output owners have not.

This proposal narrows the next work to that missing owner. It does not reopen Animation, LOD, picking, the skinned-normal diagnostic byte ledger, or source-adapter scope. In particular:

- Reuse the already proposed particle support token, controller, single `SpawnAsync(Entity, int, CancellationToken)` shape and typed receipts/counters/recordings; do not invent alternatives or convenience overloads.
- Reuse the addendum's draw identity and merged Sprite ordering, rather than assigning one Sprite ID to every particle.
- Treat the runtime-port declarations as proposed lower capabilities, not methods that the current interfaces already implement.
- Preserve the M03-B [workspace](m03b-scene-preparation-workspace-closure.md), [handoff](m03b-scene-preparation-handoff-closure.md), [compiled-producer](m03b-scene-preparation-compiled-producer-evidence-addendum.md) and [allocation-evidence](m03b-scene-preparation-allocation-evidence-capability.md) gates. The new compile-only Scene candidate does not qualify its suppliers or remove those gates.

## 2. Prerequisites and sequencing

| Work | What can start | What is required before its consumer executes |
|---|---|---|
| Existing numerical/content work | Accepted M02 spatial/buffer/arithmetic roots and the actual emitter codec/cooker already support bounded kernel/content work. No M04 or blanket M03-B exit is needed. | These tests prove only the operations/content they execute. They cannot be relabeled as pool saturation, World publication, or billboard evidence. |
| Particle owner, component factory and lower plumbing | Accepted M03-A World/factory/lifetime roots permit a bounded, contract-complete owner/registration/accounting candidate. Close the prior projections' reachable declarations and private ownership records first. | Exact component provider/State2 roots, frozen inventories, phase grants, byte claims, cleanup ownership and selected CPU composition must be concrete. Compile/inspect a candidate while live Scene preparation is blocked; do not provide a test emitter or a fabricated stage binding to bypass it. |
| Real CPU emitter-to-billboard path | Does not require Animation, a solver, GPU output, or completion of unrelated M03-B observations. | Requires the actual M03-B package store, native Scene/component restoration, activation, transforms/cameras, measured resource preparers, coherent CPU cohort and their allocation/handoff lifetimes. The current `QualifiedFootprint` refusal must remain until its actual gates close. |
| Save/restore reset | Uses the existing real Standard Branch persistence path, not a new particle movie. | Restored emitter references/resources and the particle reset/cohort join must complete before fresh canonical Count/Write comparison and restored-marker publication. |
| Native billboards | CPU owner implementation remains independently eligible. | Requires genuinely World-produced snapshots plus actual selected M04 device/residency/pipeline/target, submission and readback/presentation paths. Exercise D3D12 and Vulkan separately. |
| Full EN096 noninterference | Canonical exclusion can be observed in a real body-free World after the CPU path is available. | Actual authoritative Physics noninterference needs the real M06/M07 body/Integration consumer. A body-free hash comparison is only that case's canonical-state slice. |

Chapter 35's distinctions apply per row. Do not require all M03-B/M04 work to finish before independent CPU work; equally, do not claim a missing producer exists because a lower assembly or feature enum exists.

## 3. Concrete producer and ownership arrangement

### Registration, construction and Scene preparation

1. Reconcile the existing four particle schema rows and the prior public projections with their owners. Supply the actual typed persistent emitter component factory, validation/default/reference/Count/Write/Restore roots and exact descriptor/layout closure; do not assign new schema identities or widen the existing wire tuples.
2. Freeze one separate particle support registration. Its storage inventory is exactly the emitter component plus the noncanonical pool participant described by the runtime-port proposal: no canonical particle resource, system state, transferred attachment, tag, control payload or RNG registration. Preserve the current CPU-support component-only rule. Validate the complete selected composition atomically, including Scenes prerequisites and any selected versioned render-component prerequisites; a flag must not silently select missing factories or another feature.
3. Before participant allocation, reserve empty-owner, standing queue, candidate/publication and cleanup backing through the real World/domain owner. Construct a fresh participant for every exact World binding, after its Scene prerequisite and before dependent CPU extraction. Capture immutable factory facts once and check admitted versus actual reservation. No package I/O, task escape, native service or hidden tick belongs in construction. Later failure burns an already issued operational owner under the existing contract and unwinds all independently releasable ownership.
4. Compose the actual selected Scene resource preparers over **one** `SceneExpansionPlan`. The current single CPU-preparer lookup is insufficient. Measure and fund particle measurement/sort/load/retention work before invocation, then retain emitter, Atlas, all required pages and optional exact-version Material from that plan's package backing. Use the existing binding predicates; an absent Material selects the existing default Sprite profile, while a failed/incompatible Material load rejects.
5. Activation installs emitter bindings/resources with the Scene or none. It does not emit particles. Content/component edits must either install a completely prepared replacement binding through their real owner or reject; stage 8 must never perform asset I/O or repair an unprepared binding.

The controller borrows the exact World and locates only its already selected participant. It neither creates a second pool nor disposes/rebinds the World. The selected engine phase reads only copied emitter values, qualified membership and Scene transforms through a lower-issued, same-World, nonreentrant grant. Revoke in `finally`; active misuse is sticky under Chapter 13. Public idle World getters and application `System.Order` are not substitutes.

### Storage

Use bounded private particle rows and stable survivor compaction, not one managed object/Entity per birth. Keep the last committed state immutable while preparing a distinct candidate. A row carries its positive particle ID, birth emitter-generation/resource key, normalized `SpatialPosition3`, velocity, age and life-step count; previous/current presentation data belong to derived output. Initialize every exposed/read field, including rented/stack storage under `SkipLocalsInit`.

Emitter rows retain the qualified Entity, exact component/content installation, definition/backing, seed/stream and current PCG state. Use a checked pool-local emitter-incarnation sequence so removal/re-addition or rebind cannot reuse an old birth-resource key. Enable/disable and movement do not reset it. Rebinding content/backing/seed/stream starts a fresh stream; existing particles keep their birth row, sorting/layer fields and assets until their actual last consumer releases them.

Use the previous proposals' envelopes as **tentative ceilings, not derived footprints or additional budget entitlements**:

| Ownership category | Bound |
|---|---|
| Existing Chapter 21 live-particle limit | 131072 total per pool, independent of available render draw capacity |
| Proposed live emitters / pending explicit requests | 1024 / 128 |
| Proposed accepted stimuli per recording segment | 65536; not a particle count |
| Proposed pool/current/candidate/retained storage | 128 MiB, including at most 64 MiB total recording backing and at most four independently retained recording outputs |
| Proposed additional M05 CPU output storage | 128 MiB shared with any other actually selected M05 outputs, not another particle-only entitlement |
| Proposed spawn-operation envelope | W = 64 KiB plus the existing 256-byte independent-use control; still requires a complete derivation |

Count maxima do not promise joint byte fit. World accounting must include the pool, operations, outputs and stopped-but-unreleased ownership under the existing shared 2-GiB World and exact admission-domain limits; do not conceal them in the resource/system allowance or Scene preparation's purse.

Before allocation/admission, reserve the complete simultaneous peak: old/current state, new candidate/growth, scratch and sorting, recording rotation, retained readers/outputs, asset-pin wrappers, task/result/publication machinery and cleanup/quarantine provision. Charge actual capacities, headers/alignment and closed runtime layouts once through their actual owners. A logical row size, the 131072 count, or `Unsafe.SizeOf<class>()` is not that proof. Reused storage is reusable only after its previous actual users end.

Qualification uses independently measured boundary baseline B and independently derived additional peak D: exact fit is B+D, one byte short is B+D-1, with all preceding setup funded. Verify the intended denial is reached. Never lower requested Scene P, assume 64 KiB funds an unexamined Task graph, or allocate first to discover a permitted allowance.

## 4. Fixed-tick, overload and publication semantics

| Boundary | Required operation |
|---|---|
| Stage 0 | Freeze the accepted explicit-request prefix for this tick. Use a separately bounded **noncanonical** queue; ordinary World control admission/cursors must not change merely because cosmetic work was requested. Later requests wait for a later admitted tick. |
| Stages 1-7 | No particle simulation. Structural playback establishes complete next membership and Scene transforms. Destroyed/rebound emitter generations cannot retarget queued requests to a successor. |
| Stage 8, after Extract systems and final authoritative validation | Invoke the actual particle engine participant once, after any selected Animation output and before dependent CPU output preparation. Reconcile real emitter bindings; prepare from the previous committed pool; expire/integrate survivors; evaluate automatic/explicit requests; prepare counters, recording, resource owners, results and target-independent billboard roots together. |
| Stage 9 | Install only fully prepared ownership/values with the one committed publication. No participant/user/native callback, allocation or Task completion occurs inside marker installation. After the marker and revoked phase grants, dispatch prepared result completion outside gates. |

The particle phase writes no authoritative component/attachment/resource, World control, gameplay event, clock, canonical active-AssetId set or authoritative RNG. Retaining cosmetic assets is not canonical asset activation.

Apply the existing kernels without a second implementation:

- `LifeSteps = ceil(LifetimeTicks * TickRate / 10000000)` using widened checked integers, minimum one. Existing particles increment age and expire at `newAge >= LifeSteps` **before** integration. Survivors use the fixed binary32 `1.0f / TickRate` displacement and admitted spatial normalization.
- Generate an automatic request only for a live enabled emitter with nonzero `SpawnPerTick`. Merge by emitter EntityId RFC bytes, then producer sequence: automatic zero, explicit checked positive monotonic admission sequences. Disabled automatic emission does not destroy old particles or implicitly cancel otherwise valid explicit work.
- Recommend World-axis velocity ranges in both spaces: the actual Scene transform supplies birth position; no implicit emitter scale/rotation is applied to the kernel's velocity or size. PlaneXY keeps spawn depth and zero Z velocity. Capture the proposed stimulus rotation as provenance, not an unspecified extra transform. This numerical interpretation must be included in the owner closure before implementation.
- Preflight each whole request's particle/recording/pin/byte/counter demand. Admit all requested particles or none. Refusal issues no particle IDs, slots, accepted-stimulus records or RNG words. New particles start at age zero and first integrate next tick.
- Consume exactly three PCG32 words in X/Y/Z order for each admitted birth, including fixed lanes and PlaneXY Z. Use each emitter's independent stream; never World authoritative RNG, OS randomness, render rate or worker arrival.
- Sample exact keys directly; otherwise linearly interpolate at the existing nearest-even normalized age. The age-one endpoint is defined but an expiring particle is not drawn there.

### Refusal and faults

Use the already proposed typed `RejectSpawn` dispositions, not partial spawning or invisible culling. Particle-capacity and recording-capacity refusal leave accepted particle state/RNG/IDs untouched and increment committed rejected-request/rejected-particle totals by one/requested count. Full recording prevents new accepted spawns, but does not stop existing particles aging/expiring. Queue admission refusal consumes no admitted sequence.

Recommend that committed counters count requests evaluated in a committed tick. Admission-only refusals have no tick and are observed through their receipts; they cannot mutate the committed counter root before a marker. Close the exact byte-refusal/reason and admission-only reporting mapping in the Chapter 30 projection before exposing it; do not invent an enum value or mislabel bytes as a draw-count overflow.

Arithmetic/spatial/age/ID/counter exhaustion must not wrap or masquerade as capacity. Preserve the prior proposed pool-local `Faulted` disposition: discard the entire provisional pool update, retain previous numeric state/ownership, prepare an explicit pool fault and uncommitted request dispositions, and install that fault only if the enclosing World commits. No earlier partial `Spawned` result escapes. A selected faulted particle root cannot become a successful empty billboard output; extraction must report its unavailable required output through the closed error contract. There is no automatic restart.

This local fault boundary is not a catch-all. Invalid engine grants/stamps, unprepared content, ownership/publication failures and unexpected infrastructure exceptions follow the existing World/tick fault channel. If fault reporting itself cannot be prepared safely, withhold the World publication rather than silently disabling particles.

If the encompassing tick fails, discard all provisional particle changes, counters, recordings and results; fault its admitted uncommitted tasks after safe cleanup. Earlier ECS/structural writes are not promised rollback. Conversely, after a successful marker, subsequent subscriber/delivery failure cannot undo the pool or change committed spawn outcomes into rejection.

## 5. Recording and complete lifetime

### Recording

Use the already proposed owned **in-memory** recording, not a new wire schema, importer, input movie or State2 resource. A segment retains initial live particles, referenced/current emitter definitions and PCG states, next particle ID, rate/profile and half-open tick interval, followed by ordered accepted stimuli with captured birth pose, count, RNG before/after and first ID. It describes the accepted cosmetic trajectory, not rejected-request/admission history or canonical replay.

Retain only current/referenced emitter-generation rows. The prior segment bound is independently decomposed as at most 131072 initial-particle generations + 1024 current emitters + 65536 generations introduced by accepted requests = 197632 rows; the byte limit can refuse earlier. Removed generations remain while any initial particle or stimulus references them.

Rotation is an idle-owner transaction: reserve old output + new initial state + pins/scratch/cleanup coexistence, transfer the old committed segment to its independent owner, and start the next segment from the exact committed pool/PCG state. Four retained outputs or byte refusal leave segment/counters/state unchanged. Do not overwrite old records, reset age/RNG/IDs, or refund transferred storage. Recording disposal ends only its own reads/pins/storage; access after disposal throws.

### Admission, cancellation, completion and disposal

- Close controller construction/spawn thread and phase affinity explicitly with the prior public projection. Counters/rotation are owner-idle committed operations. Explicit spawn count is `[1,131072]`; zero is not an implicit no-op or unlimited sentinel. Shape/lifetime misuse remains synchronous; a valid pre-canceled spawn admits no use, queue slot, pin or sequence. Do not add a different overload to evade these rules.
- Before Task/TCS/closure/pin allocation or escape, acquire the request's exact World binding use and fully funded operation owner. Capture actual emitter installation identity under the admitted boundary and revalidate it at stage 8. Removal/rebind yields the proposed unavailable-emitter disposition, never a lookup against a same-GUID successor.
- Queue-full typed results need a funded, bounded refusal publication too. If the binding/use/result envelope cannot be admitted, use the existing synchronous admission-failure channel before creating an unfunded task. The precise precedence/mapping is part of the declaration closure, not permission for success-shaped fallback.
- After acceptance, token cancellation/abandoning a wait does not revoke work or free queue/pool/output memory. No token crosses the synchronous engine phase. Paused/unticked Worlds do not run a timer/render-driven tick to finish a spawn task.
- Complete explicit requests only after their evaluating tick commits, using asynchronously dispatched continuations outside locks and participant callbacks. Keep the operation rooted and funded through actual publication return and every remaining producer access/cleanup tail, not merely Task terminality or `RunContinuationsAsynchronously`. Derive the finite runtime/continuation ownership boundary rather than assuming the unresolved M03-B external-continuation rider is already accepted.
- A scalar receipt/retained completed Task grants no asset/pool/World authority. Transfer caller observation ownership explicitly and sever owner/resource links after actual producer completion. Birth resources receive their own retained ownership; command cleanup cannot release a live particle's borrowed command pin.
- Close stops new admission, joins actual stage/producer use and completes unapplied work with the prior `WorldClosed` disposition. A committed request remains committed; an encompassing failed tick remains a fault. Controller collection/disposal of a caller wait is not World close.
- Scene unload stops future automatic emission and invalidates queued requests for that generation, but surviving particles/recordings/snapshots keep exact birth assets. World close retires pool ownership; independently returned immutable outputs retain their separately funded storage/assets through actual CPU/GPU use. They do not retain active-World authority merely because they contain copied identities, and World close must not wait circularly for their disposal.
- Prepare failure cleanup before ownership transfer, attempt every independent safe release and preserve original failure channels. Never add a disposal call followed by unconditional field clearing that loses a throwing owner's graph. Stop-but-unreleased ownership stays anchored/charged in its actual World/output/use cleanup ledger. `WorldGuidUseControl.Release` can fail after part of lower retirement; preserve the real remaining debts, do not clear its root first, retry a completed release, or manufacture a new owner/grant.

## 6. Extraction, rebase and restore integration

### Genuine billboard extraction

Extend the actual committed CPU cohort with the prior particle root slots and fixed frame actors; do not bolt a separately sampled particle cache onto the current frame tuple. Preserve Chapter 16's exact stamps, Check/full-peak-reservation/Prepare/reverse-Abort/callback-free-install/detached-Retire protocol. An unselected actor is absent; a selected empty actor returns a genuine stamped empty root. No placeholder Animation/LOD actor is needed for particles.

`Renderer.Extract` must acquire one real read lease over matching frame/camera/pool roots, copy output and retain independent resources before ending that borrow. A frame-only rebase transports materialized particle representations and valid history without advancing age/RNG/counters/recording, changing persistent positions or dropping a cache to pass capacity. Repeated extraction never simulates or emits.

PlaneXY participates in the selected 2D and 3D Sprite paths; CameraFacing3D participates only in 3D, using copied camera +X/+Y bases. Preserve centered pivot, Sprite CW quad/UV, straight tint/premultiplied texture color rules and Blend depth behavior. A particle retains birth sorting/layer/material fields after emitter edits/unload.

Count one eligible logical billboard per live particle across selected cameras before culling/batching, together with all ordinary eligible draws. Preserve draw/light/bone precedence and exact full Requested/Limit. A full particle pool plus one eligible Sprite requests 131073 draws and returns no snapshot, not an invisible omitted Sprite or particle. New byte diagnostics require their already proposed owner-reviewed declaration/issuer; do not manufacture a public limit kind.

Carry private billboard provenance/resources without fabricating a live Entity/SceneInstance to fit today's ordinary Sprite carrier. Retain the identity addendum's pool-local Generation=1/positive never-reused ParticleIds and its exact `particle/v1/<Hex16 Generation>/<Hex16 ParticleId>` NameUuid recipe. Across one snapshot validate particle keys and collisions with all logical Sprite/Mesh draw IDs before output/native admission.

For each copied camera, merge ordinary Sprite blocks and particle blocks into one complete captured-set order by signed sorting layer, signed sorting order, descending finite camera-view Z, then RFC DrawId. Neither emitter order, particle age/slot nor a separate particle pass may precede the final key. Batch adjacent compatible draws only. Preserve the addendum's literal P1/S/P2 interleaving witness and invalid-collision failure; cosmetic particles do not acquire entity-picking membership.

Old snapshots own their copied data/pins. The four-entry render mailbox may replace only an unconsumed entry under Chapter 13; it does not cap all externally retained snapshots, replace a snapshot being recorded, or authorize overwriting particle recording storage. CPU command recording, GPU resource/fence retention and interactive presentation completion remain different lifetimes.

### Persistence/reset

Persist only the authored emitter component and its ordinary declared asset references. Do not serialize live pool rows, counters, PCG, recording, operation queue or billboard history through attachments/resources/control records as a shortcut.

The real Standard Branch candidate must restore emitter components/Scene resources/clock first, then execute the closed particle restore join: fresh empty exact-bound pool, owner-local Generation=1/NextParticleId=1, initial streams from restored definitions, and explicit lost-history/discontinuity. It emits no automatic/explicit spawn, receipt, gameplay event or hidden Step. Prepare coherent empty roots before fresh canonical Count/Write validation and the restored marker.

Failure before replacement publication leaves the old World/pool untouched and retains failed candidate cleanup ownership. Successful replacement revokes old controllers/requests without retargeting them; old immutable outputs remain readable against their original backing. Equal numeric generation/IDs or WorldGuid in the successor do not alias the old pool. Old-World retirement remains separately reported. This does not enable RecordedContinuation or checkpoint-resumable cosmetic particles.

## 7. Coherent implementation and evidence batches

| Batch | Deliverable and required observations | Gate |
|---|---|---|
| Owner/declaration and funded construction closure | Reconcile existing metadata/prior projections; implement actual component/factory/registration inventory, scoped lower phase/cohort/reset/retirement ports and disjoint owner ledgers. Compile real closed roots; test duplicate/missing/conflicting registrations, exact schema/provider/default/reference behavior, fresh per-World ownership and construction unwind. Preserve the unselected static path. | Resolve reachable signatures, affinity, refusal/error mapping, actual backing and release authority. A compile-only candidate is not permission to run unfunded Scene or Task producers. |
| Complete CPU producer/lifetime unit | After the exact Scene prerequisites are executable/funded, deliver native-authored emitter + separately native-authored Scene → actual preparation/activation → controller/automatic requests → fixed Step → stage-8/9 pool/recording/results → genuine Headless extraction. Include unload/rebind, full/short budgets, rotation/retained outputs, close, frame transaction and Standard Branch reset in this unit, not deferred ownership fixes. | Actual package/Scene/transforms/cameras/cohort, current Scene J/K/supplier/receiver gates, and independently derived spawn/publication/retention envelopes. Do not substitute direct internal pool/snapshot construction for this path. |
| Native billboard consumer | Extend the existing Sprite C0 resource/order path for genuine particle roots. Exercise PlaneXY/CameraFacing3D, mixed Sprite order, texture/material majors, multiple cameras, unload/World replacement while real GPU use retains assets, actual readback and separate presentation where applicable. | Real selected M04 device, residency, pipelines, target and completion on each backend; existing graph/budget obligations for the chosen path remain. |
| Qualification | Compile/run a public-only particle consumer without friend access; execute applicable managed Debug/Release, target NativeAOT and workload/capacity observations. Complete real Physics noninterference when its M06/M07 producer exists. | These are separately recorded observations, not consequences of helper tests or compilation. Parent cases/milestone stay incomplete until all their required variants execute. |

Focused, independently specified cases for the CPU batch:

- After expiry leaves 131071 survivors, request two: whole-request rejection, rejected-particle total +2, RNG and next ID unchanged. Request one can fill the pool. Distinguish recording/queue/byte refusal and zero automatic rate; test checked counter/ID/incarnation exhaustion.
- At 2 Hz, one-second lifetime, birth x=0/vx=2: draw age0/x0, then age1/x1, then expire before integration; never draw x2. White-to-red/alpha-zero endpoints give `(1,0.5,0.5,0.5)` halfway; size 1→4 gives 2.5. Preserve existing exact-ratio rounding and the six EN123 PCG words, including three-word consumption for fixed lanes.
- Failed encompassing publication leaks no new particles, counter/log state or `Spawned` receipt. A post-marker delivery failure does not retract committed outcomes. Use actual reached validation/reservation/owner behavior, not a test-only participant or production fault-injection branch.
- Vary zero/one/multiple CPU workers and extraction frequency without changing fixed-tick births/RNG/recording. Compare canonical hashes at equal canonical state, not after changing the persistent emitter definition or other authoritative fields.
- Retain recordings/snapshots across definition/backing replacement, emitter removal, Scene unload, rebase, World close and same-GUID replacement. Verify birth resource identity, no reused IDs, no current-Scene lookup and exact disposal/retirement charges.
- Preserve the existing addendum's independently derived UUID/mixed-order vectors. Restore a real save to a new binding: empty/discontinuous particles, unchanged restored canonical bytes, no replayed spawn, and old outputs still valid.

Use ordinary developer commands selected by `global.json`, owning-project filters and Debug before Release. Existing focused fixtures are `CosmeticParticleKernelsTests`, `CosmeticParticleEmitterAssetCodecTests`, `NativeParticleCookCommandTests` and `RenderingSchemaCatalogueTests`; extend the related owning areas and actual public-consumer programs as the feature is delivered. Run only affected content/schema projects when their inputs change. Retain fresh TRX/logs under ignored `artifacts`, record nonzero selected/executed tests and separately enumerate exercised conformance observations. No new runner, full second particle implementation, production-generated expected values, or acceptance ID is proposed.

## 8. Exact stops and unresolved gates

1. **Owner/public closure:** stop the affected exposure if the prior support/controller/result/recording declarations, component family, selected CPU composition, affinity, admission-only counter policy, byte-refusal mapping or faulted-output error contract remains unresolved. Metadata and a tentative proposal are not accepted public declarations. Freeze the complete actual review inputs, including dirty-source/schema hashes, before assigning an owner-review disposition.
2. **Scene execution:** keep the current `QualifiedFootprint` refusal until the final candidate's complete J/K, suppliers/measurement/Rendering workspace, retained receiver and producer/publication last-access proofs are closed. Do not retry the same blocked integrated preparation or replace null with a guessed allowance. Continue only independent contract-complete numerical/content or compile/inspection work.
3. **Pool/operation funding and lifetime:** no live pool/queue/rotation/publication work before every reachable allocation, simultaneous peak, retained path and actual release/transfer has a finite owner. No canceled wait, terminal Task, finalizer, cleared field or blanket quarantine proves a running producer ended. Missing coverage remains a specific NO-GO, not a reason to raise a ceiling.
4. **Stage/cohort/reset closure:** stop World-bound observations if the real dispatcher, post-Structural reads, single marker, immutable root ownership, rebase actors or pre-marker restore join is absent. Never register late, reserve an application system order, forge a grant, fabricate a snapshot, or silently omit selected particle output.
5. **Native/integrated observations:** leave the exact D3D12/Vulkan/NativeAOT/Physics observation open when its genuine producer/capability/stimulus is missing. Record the missing capability and retry condition. Existing M08/M09 physical-input, driver failure, timeout and uncertain-retirement deferrals are unchanged and are not prerequisites for unrelated CPU implementation.

Design acceptance, compiled roots, managed behavior, complete World/public-consumer behavior, real GPU/fence/presentation observations, NativeAOT, measured capacity and milestone acceptance remain distinct. The relevant existing acceptance is the particle slice of AR08 and EN096; EN123 supplies the RNG vector. Scoped cosmetic repeatability is not canonical replay. This documentation task ran no build/tests and claims none of those gates closed.

### Evidence anchors

These hashes identify central inspected working-tree inputs, not a complete future API-review freeze or executable qualification profile. Re-read changed inputs before applying this guidance; a later review must include its complete selected source/schema set.

| Input | SHA-256 |
|---|---|
| `docs\project\Roadmap.md` | `649182860D65DC902A406BCAC482AA1D286465BAFB0B34143452C8B09E51EBDE` |
| Chapter 13 | `0D7806B03A1D019215EB5C3912F10200A078F9B00A71A206FB979E93BBD18CDE` |
| Chapter 21 | `989A6183186592F165FA9614CC19A8FED63826E2A51170CE55FBFFFCA8116ED2` |
| Chapter 30 | `15848C82F3858A77A87A0D3EA6FBCC07046EB6E77CA5B21748C72B90EFAB598B` |
| Chapter 35 | `24EEC6E92B7AD904D552F290EF748FFC8FCE13B6B94A89EBD9F39487D4584D87` |
| Owner rectification | `67134F02B92D316524848ECB8E0C917EF868DA3D2EDA5FC581DBAA25B1825632` |
| Particle identity/order addendum | `C8DE6581D5CB126D18C294FC9EB22A01459B2D6BFC3AD933ECB1652582634F4A` |
| Private runtime-port proposal | `605CF1D92DC362A8B73EA9BDA3A5467757942C268712F484AF53D9A9159D53E3` |
| `sources\TerraFX.Graphics\RenderingSchemaCatalogue.cs` | `DE25A8C0E249FF9AB5B592FE848C85429C004FE629694C64EF1AFEF3BA731614` |
| `sources\TerraFX.Runtime\Scenes\PreparedScene.cs` | `B73DD4314B63084132AFC2F9325ED98EECCCE65A174F02B16F2DED7EC65F688C` |
