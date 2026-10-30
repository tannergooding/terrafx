# Jobs, stages, immutable output, and shutdown

[Terminology and reading conventions](01-terminology-and-reading-guide.md).

## 1. Ownership and worker admission

Host's dedicated owner thread pumps platform windows, user interface (UI), world stage barriers and trusted systems. Dedicated render thread owns rendering hardware interface (RHI) recording/submission/presentation; audio processing never accesses entity-component-system (ECS). Bounded asset I/O/import workers use `Task`/`Channels`. Fixed central processing unit (CPU) worker pool executes synchronous engine jobs and never blocks on device/asset/UI work; default worker count is `min(64, max(1, logicalProcessors-2))`. Public host and standalone World options select an explicit integer count in `[0, 64]`; zero uses the exact stable partition/order path inline without creating worker threads, while nonzero creates the requested pool without silent downshift or owner-thread substitution. Hosting owns one shared pool and joins Worlds before disposing it; a standalone World owns and retires its pool. Disposing one host-owned World joins its work but does not close the shared pool.

System registration provides stable `Guid`, unique explicit `Order`, `Stage`, component/resource read and write sets, optional Before/After constraints, and owner-thread or parallel eligibility. `Order` is a signed `int`, including negative values; command keys preserve that exact registered value and signed comparison rather than casting it to an unsigned ordinal. Stable partition ordinals are nonnegative `int`, and local command sequences use checked `long`. A resource is a named world service/state partition, not just a component. Registration freezes before play; incompatible metadata/cyclic constraints reject startup.

`SystemResourceKind.EntityMembership = 4` is the fixed read-only World-wide membership capability with identity `resource/entities/membership/v1` / `5ae0d511-7e5f-81df-bb07-e2b259892443`. Its only valid access is `Read`, with no range; it is engine-defined, not a caller-registerable resource array or component/tag schema. Query declarations remain component/tag-only. The grant permits identity/handle lookup outside a ranged query's partition, but grants no component/resource access, enumeration, write, structural permission or range bypass. Every subsequent data access still requires its ordinary type, mode and range admission.

Compile a stage directed acyclic graph (DAG) from explicit edges plus conflicting accesses. Conflicts are ordered by explicit `Order` then `Guid`, independent of discovery/reflection/dictionary order. Read/read may run concurrently; write/read and write/write serialize unless declared disjoint partitions with checked no-overlap ranges. Hidden managed/native/global writes are forbidden in jobs.

For each `(stage, kind, resource)` group, reserve conflict comparisons before enumerating pairs. With `W` writers and `R` read-only systems, charge `W*(W-1)/2 + W*R`; sum with checked arithmetic across groups. Count all possible comparisons even when ranges later prove disjoint or an edge already exists. Reject the registration before pair enumeration if the configured comparison ceiling would be exceeded. Ranges are nonempty checked half-open intervals; overlap is `a.Start < b.EndExclusive && b.Start < a.EndExclusive`.

Explicit Before/After references resolve against the complete immutable system registration. Unknown/self references, duplicate entries/orders, same-order systems and cycles reject before World activation. Edges cannot move a system across its fixed stage or reorder engine-owned stage work: a forward cross-stage dependency is redundant, while a backward dependency rejects. A cycle diagnostic is bounded and retains its system path. Ready systems use signed Order as the primary deterministic selection key and RFC-order GUID only as the secondary key.

Resource GUIDs and disjoint declared ranges do not prove that mutable storage is independent. The World allocates fresh private backing for every registered resource; callers cannot supply mutable backing arrays or service instances. Resource, component-column, system-instance and cross-World mutable storage cannot alias. Shared immutable configuration/copied data and retained package-backed asset leases are permitted; version 1 has no cross-World shared mutable scheduling domain. `AllowParallelAccess` is eligibility only: a Parallel system with any access to a resource that disallows parallel access is rejected at registration and rechecked at execution admission. It neither proves arbitrary service thread safety nor grants undeclared service access.

One factory-created system instance belongs to one registered system in one World, remains owned across ticks, and is never concurrently executed or reentered. `Execute` runs once when its stage is reached for each admitted tick, even if its queries would match no rows; a prior fault may prevent later systems from starting. OwnerThread systems run on the World owner. Parallel systems run as whole synchronous executions on the selected CPU pool. Public `ForEachChunk` remains ordered and synchronous on the executing thread, borrowing one caller-visible `scoped ref TAction`; its state is not copied for parallel use. There is no public parallel chunk callback or reduction API. Private engine jobs use separate partition-local storage and deterministic folds. Zero-worker mode follows the same DAG, plan, range and fold order inline, without worker creation or fallback to `ThreadPool`.

`SystemDescription` owns scheduling identity/order, stage, accesses, dependencies and thread mode; `WorldSystemFactoryBinding` owns the closed type, source-state declaration, State2 field dispatch and factory callbacks, and does not duplicate scheduling facts. A stateful `Execute` receives write authority only for its own authoritative fields. Invalidate the canonical marker before invocation, validate on normal and exceptional exit, and revoke the authority in `finally`; a stateless system receives no authoritative-state grant. Provider/reader calls use the exact registered family and same-World nonreentrant invocation boundary. Active misuse records its first sticky protocol exception before throwing, and dispatch checks it after return or unwind so catching it cannot produce success. Expired binding use is throw-only, and no lock spans application callbacks. Chapter 30 defines the typed factory, field, codec, and binding declarations.

For each structural-layout epoch, build one global component/tag work plan by canonical archetype schema-GUID tuple, chunk ordinal and physical row. Split each nonempty chunk into blocks of at most 64 rows and assign global nonnegative partition ordinals. Component/tag ranges select these existing ordinals; they are not row, entity, chunk, query-local or worker indices. Filters never renumber partitions. The contextual domain is `[0, MaximumLiveEntities)`; unpopulated reserved ordinals select no callbacks only after schema, permission and range validation succeeds. A capacity-one layout can therefore have 1048576 partitions. Structural playback and idle structural edits rebuild the plan under a checked epoch; no query borrows a stale plan. `Standard` restore may change assignments, while `StrictLocal` preserves the serialized layout/order needed for exact continuation.

Within each physical chunk, plan-block cuts are at row offsets `0, 64, 128, ...`, with one shorter final block when necessary. For `ForEachChunk`, a present `EntityQueryDescription.Range` invokes exactly one callback for each selected populated plan partition whose archetype matches the query, in global plan order; the borrowed view contains exactly that partition's physical-row slice. With Range absent, invoke exactly one callback for each matching nonempty physical chunk, exposing all its populated rows. A present full-domain Range remains partitioned. Do not coalesce adjacent partitions, subdivide a partition at query dispatch, or expose unselected rows; the Entities span and every admitted component span have identical extents and local indices. Required/excluded schema filters apply to archetype membership and do not create row-level holes. Validate the receiver, query schemas, complete contextual range, permissions and invocation before resolving a selection that has no callbacks. For a system-context query, an absent Range requests the whole `[0, MaximumLiveEntities)` domain; do not intersect it with a narrower declared grant or the currently populated/matching plan. Resource-partition traversal retains its separate `[0, PartitionCount)` domain.

Each logical writer prefix has one sequence domain per barrier, shared across repeated queries and calls outside queries. Outside-query writes use partition 0; a ranged callback for plan partition `p` uses `p + 1`; a whole-chunk callback uses its first plan partition plus one. Sequences start at zero once per prefix/barrier, advance with checked `long` arithmetic and never reset for each query or callback. Worker identity and completion races do not contribute to command identity. The public key remains `(stage, exact signed registered Order, writerPartition, localSequence)`; dependencies order execution but do not reorder playback. A token is valid only for its logical prefix and barrier.

Before/After constraints and hazard edges order system execution. They do not change fixed stage boundaries or the stable command-playback key. Resolve ready systems deterministically by signed `Order`; `Guid` is a secondary comparison where needed, not a substitute for unique Order. Duplicate system orders remain invalid. Worker exceptions retain system, partition and tick identity, stop new work, and join all admitted siblings before fault handling.

The EN036 four-worker qualification control captures actual worker entry, held-active state, release and completion separately. It holds all four selected workers before releasing worker 3, then waits for that worker's captured completion before releasing 2, then 1, then 0; the captured release and completion permutations are both `[3,2,1,0]`, and all four joins are observed before Step returns. The zero- and one-worker controls use a closed no-wait path. This control proves actual overlap and completion order; it does not make worker identity, timing or interleaving part of semantic command equality or alter scheduler admission.

`ThreadPool` is not a substitute for these stage/lifetime guarantees; use it for ordinary asynchronous I/O, not solver/frame/audio affinity. `Zero`-worker mode performs the same ordered work inline for diagnosis.

M03's resource/system/attachment state registration binds exact Core descriptors, fresh per-World storage and typed State2 factories/codecs before freeze. Resource partition visits use concrete scoped actions, global partition indices and checked declared read/write ranges; no mutable backing escapes. Scenes enlists its closed registry factory through the lower Entities-owned participant/retirement interface before registration freeze. The separate Rendering CPU-extraction support registration declares its Scenes prerequisites before World construction; neither actor can enlist late. Commit/publication installs only prepared ownership and copied actual frames, without callbacks or allocation. Frame-dependent stage/extraction reads pin one exact frame-cohort generation rather than combining separately sampled frame/cache state. A direct frame commit is an idle owner operation and a Host frame request is applied at the existing owner-control boundary; neither advances the clock, runs a hidden tick or publishes a tick result. These clauses do not alter signed Order, stage/range semantics, zero-worker inline behavior or hosted/standalone worker-pool ownership.

For M03-A, every selected system/resource/attachment provider root is implemented before registration freezes, together with its fresh authoritative storage, declared access, mutation invalidation, reference validation and disposal/fault behavior. A executes only through the real operation that issues each scoped binding; it does not synthesize State2 callbacks to qualify persistence code. B owns public persistence capture/I/O, source/candidate State2 restoration, replacement and continuation execution. Implementation ownership and the legal owner of an observation are distinct.

## 2. One simulation tick

| `Stage` | Work / visibility |
|---|---|
| 0 `Begin` | Drain accepted owner work and freeze control/input/version admission; then run Begin systems |
| 1 `Input` / `Early` | Consume this tick's normalized events and evaluate gameplay actions; then run Input/Early systems |
| 2 `Animation` / Targets | Sample admitted animation/root motion; then run Animation systems; systems enqueue kinematic targets or dynamic forces/impulses, never overwrite dynamic poses |
| 3 `Transforms` | Run Transforms systems; then resolve the dirty hierarchy and validate physics transform ownership |
| 4 `Physics` | Run Physics systems; then admit body commands, solve staged physics state with fixed dt and commit valid body/contact state only |
| 5 Pose Sync | Copy body poses to owned root transforms; run PoseSync systems; then resolve descendants |
| 6 Events / `Late` | Prepare copied sorted contact/trigger/motion-feedback events; run Events/Late systems; no external publication or second hidden physics solve |
| 7 `Structural` | Run Structural systems; join every job/query; preflight/commit all deferred structural commands; resolve newly dirty transforms; new bodies first simulate next tick |
| 8 `Extract` | Run Extract systems; then prepare and validate immutable output. No user callback or public capture occurs here |
| 9 `Commit` | Engine-only marker installation and external delivery. No user `Execute` is registered at this stage |

Membership observation follows those barriers. Owner-thread World identity reads are valid only while idle, nonfaulted and outside a running tick; a synchronous idle query may use its protected membership epoch. During stages 0–7, including Structural Execute, registered contexts observe the complete pre-playback live set and pending tokens are absent. After every job/query has joined, all user contexts are revoked before stage-7 playback. Playback atomically installs the complete next membership/work-plan epoch; fresh stage-8 Extract contexts observe that post-playback set. Stage 9 has no user Execute/context and external output observes only the committed publication.

`WorldSystemContext` is an engine-issued sealed reference type; its language default is `null`, never an engine context. Membership lookup through a context requires the system's explicit membership Read grant and validates invocation, owner/binding, thread, phase and epoch on every call. Empty IDs and default/stale/foreign Entity handles reject; `TryGetEntity` returns false only when a valid stable ID is absent from the admitted live set, with invalid default output. Missing permission, wrong-thread/phase use or revoked/fabricated nonnull contexts throw. Scoped-receiver misuse and command token-argument faults follow the exact receiver rules below; invalid handles or token bits never select a fault target.

`WorldSystemContext.Step` is an engine-issued `SimulationStepContext` containing the zero-based executing `TickIndex`, fixed `TickRate`, and single-precision `FixedDeltaTimeSeconds = 1 / TickRate`. Its default value is not issued. The context exposes no frame, World identity, elapsed-time accumulator, or mutation authority; copied scalar metadata remains ordinary immutable data after the callback.

All authoritative component/resource/system references are revalidated after stage-7 playback against the complete next membership and after Extract's permitted writes before output preparation/publication. A destroyed target must be explicitly cleared or rebound; it is not silently nulled. A post-playback validation fault withholds publication and tick advancement but does not imply rollback of the already committed structural transaction.

Application deferred-command writers admit mutation only in stages 0–7. At stage 8, every writer operation rejects before allocating a sequence, copying payload, issuing a token or enqueueing a command, and records a sticky running-context fault; there is no silent carry to the next tick. Stage 8 direct component/resource writes require their ordinary declared write scopes and validation before immutable preparation. Stage 9 performs only the already-prepared marker installation without allocation or user/native callbacks.

For the four `AddTag`/`RemoveTag` overloads, preserve receiver, invocation/binding, owner-thread, and existing phase precedence. In an otherwise valid live Extract invocation, stage refusal occurs before target, token, or schema inspection and before any command sequence, staging reservation, payload copy, token issuance, or enqueue. Catching that actual refusal in the callback does not clear the sticky fault: `Step` fails with the same exception object as `WorldStepException.InnerException`, and neither publication nor tick advancement occurs. The two token-overload precedence witnesses use a default token, and one uses an unknown schema ID; they establish only phase precedence, not token validity or a new exception type/message contract.

For either deferred `Destroy` overload, validate the active invocation/binding, owner thread, phase, prefix and barrier epoch before validating `mode`; then validate the target arm. An unknown mode throws `ArgumentOutOfRangeException(nameof(mode))` before copying, reservation, command admission or sequence allocation. Prepare the complete append and checked next sequence together; a refused append consumes none. Caught misuse in an active invocation remains sticky, while retained/revoked-writer misuse cannot retroactively fault a committed publication or select another World from invalid target bits. Immediate `World.DestroyEntity` performs idle-owner and mode checks before canonical mutation and creates neither a deferred key nor receipt.

Command/event payloads retain type/schema/owner/tick/sequence. A callback cannot recursively `Step` the world, mutate the current solver or perform structural work outside its writer. Contact-end from stage 7 destruction is delivered at next tick's Events stage with explicit cause, and retained even though the original entity no longer exists; payload carries historical IDs not live refs.

Host pumps UI/input and the Entities owner-control queue each wall iteration, between catch-up ticks and even with zero admitted world ticks or paused worlds. Structural/control requests are applied at this owner boundary without inventing a simulation tick; time-scale, pause/resume, frame changes, scene edits and load replacement remain visible to the next admitted tick. Platform text/UI callbacks precede assignment of remaining gameplay input events. `World`'s fixed rate is not render rate; extraction occurs after every committed tick.

`Input` follows chapter [14](14-custom-input-actions-and-text.md)'s recipient contract: one `HostLiveUI` router per window and at most one attached simulation-timeline router. Native capture occurs once; copied event-time UI dispositions and original event identities enter each recipient's bounded inbox. A world's stage 1 consumes only its own tick-assigned cursor/state, never the current platform/UI mirror or another world's records. Pause/focus/overflow controls affect only their declared recipient; headless timelines use explicit injected/replay input.

Stages 0 through 8, inclusive, prepare output but do not publish it to external event subscribers/render/audio. `Stage` 9 commits only after all tick work succeeds. Tick systems stage audio one-shots/other observable effects in owned records; they cannot call external I/O or the live audio mixer as if whole-world rollback existed. After commit, audio requests follow chapter [24](24-custom-audio-mixing-and-spatialization.md)'s admission/receipt protocol; a delivery failure faults that request and reports `AudioDeliveryFailed`, not retroactively invalidates an already committed gameplay tick. Render/audio latest-value data are deliberately observational.

### Irrevocable commit and external delivery

`CommittedWorldPublication.Events` is the complete immutable, typed gameplay
event sequence for that committed publication. Each captured subscriber is
invoked once with the whole sequence, including when it is empty; callback and
delivery-report cardinality depend on captured subscriptions, not event count.
Prepare and own the complete sequence and all event values before the single
publication marker. Preserve every occurrence. Order values by the complete
ordering required by their actual selected producers, then assign dense
zero-based `Sequence` ordinals matching their indices in `Events`; ordinals are
publication-local, not producer or global event identities.

The current M03-A selected producer roots and concrete event subtypes are
empty, so each successful A publication has an initialized empty `Events`
sequence. This is a bounded owner-derived selection, not an always-empty API
contract, and a failed pre-marker tick produces no successful publication or
success-shaped empty result. No synthetic event producer or emitter is added.
Before any later producer is enrolled, its complete typed body, exact active
EventPayload schema, ownership/lifetime, deterministic producer ordering,
result schema, and any cross-producer merge order must be reviewed. Physics
event ordering and the populated Begin/terminal-End observations remain with
their actual owners; this A selection does not move EN040 from M07.

Before the commit point, stage 9 validates/reserves the complete immutable `CommittedWorldPublication`: completed tick, checked next `TickIndex`/publication sequence, spatial settings/frame descriptor/revision, sorted gameplay events and command results (including fully prepared destruction-identity vectors), extracted snapshots, staged effect requests and the replacement `Integration` episode ledger. Each existing `CommandReceipt` exposes its vector through the command results already returned by `WorldStepReceipt` and committed publication. Snapshot the active external subscriptions in checked monotonic registration order. Reservation/exhaustion or validation failure here follows section 4's pre-commit fault policy; no subscriber runs and no receipt/output becomes externally visible. Receipt vectors are prepared before structural installation; their actual `CMD+D` preapplication and `O+W+D` published footprints are reserved before the commit point, never truncated.

The owner installs one committed publication marker after all these preparations, without allocation or user/native callbacks. Its release-published immutable record is the single visibility point for the completed tick, next `TickIndex`, canonical mutation revision, episode ledger and save/replay capture eligibility; readers acquire that record, not separately mixed generations. Owner state and the ledger are coherent before delivery starts. A checkpoint captured after this marker describes this committed version; older actually captured checkpoints remain earlier committed recovery points. This does not mandate serializing the whole world on every tick. Before the marker, a failure cannot publish this tick. After it, the tick is irrevocably committed: notification/audio/render failure cannot decrement `TickIndex`, suppress canonical events/receipts, remove a ledger entry, rerun the tick or relabel it `FaultedBeforeCommit`.

Any canonical mutation outside successful stage-9 publication (including idle writes, queued structural/control commits, authoritative attachment edits, asset activation and replay-relevant frame/time-scale changes) invalidates eligibility before granting the write. Such a World remains ineligible for save/replay capture and committed extraction until a later successful tick publishes the exact current revision. A restored World may publish a coherent restored marker after validation without a hidden tick.

Dispatch external subscribers synchronously on the host owner after commit and outside native/solver callbacks. Each World and the host-native event channel admit at most 64 subscriptions; `TrySubscribe(callback, out Subscription? subscription)` returns false/`null` only at that bound, with `NotNullWhen(true)`, no list change and an explicit capacity diagnostic. On an otherwise valid call, receiver lifetime/fault state and owner/phase authority are checked first, then a null callback throws `ArgumentNullException`, then exhausted positive subscription-ID space throws `InvalidOperationException`, and only then is the 64-entry bound checked. Thus invalid arguments, disposal, a faulted World, wrong-thread/phase use, and ID exhaustion cannot be reported as capacity refusal. A refusal consumes no ID, retains no callback, changes no registration or canonical World state, and returns false with a null subscription. `TrySubscribe` is owner-thread-only: the standalone World owner or hosted host owner may call it while the World is idle or during postcommit external subscriber delivery; a change made during delivery affects only later captured batches. It is not valid during a system/provider invocation or precommit tick work. The capacity diagnostic is `WorldSubscriptionCapacityExceeded`, severity `Warning`, operation `TerraFX.Entities.World.TrySubscribe`, null tick, the current `WorldGuid`, and the fixed message `World subscription capacity (64) reached; subscription was not admitted.` Its producer context contains exactly `ActiveSubscriptions=64` then `MaximumSubscriptions=64`; chapter [28](28-diagnostics-performance-and-validation.md) appends the reserved `OccurrenceCount=1` entry for the retained warning and increments it on coalescing. It is best-effort bounded telemetry: failure to retain it increments the applicable dropped count but never changes false/`null`, throws, or faults the World. Invalid arguments/disposal/ID exhaustion throw. Subscription IDs are checked monotonically increasing positive `long` values, never reused/wrapped. Each subscription receives the immutable whole event batch once in captured registration order. Operational delivery identity is `(ExecutionSessionId, WorldOwnerId, PublicationSequence, SubscriptionId)`; World GUID, replay lineage and identity branch are semantic metadata, not publication authority. `ExecutionSessionId` is the nonempty UUIDv4 published once by the synchronized Entities execution-domain gate in chapter [12](12-entities-components-and-queries.md); all standalone and hosted Worlds in that admitted Runtime/single-Core domain share it. It survives zero live Worlds, host restarts, replacement and fork, and is never caller-supplied or persisted. This prevents a restored/replaced World that reuses a publication sequence from aliasing delivery in the same execution session. Copy the full tuple into publication/delivery observations before publication.

For `WorldSubscriptionCapacityExceeded`, the raw `Diagnostic` must have
`WorldGuid=Some(the current WorldGuid)`, `HostId=None`, and `Tick=None`.
`AssetId`, `CommandIdentity`, `DeviceEpoch`, `DeviceId`, `NativeResult`,
`PassId`, `SurfaceEpoch`, `SurfaceId`, `SwapchainEpoch`, and `SystemId` must
all be None. The fixed code, message, operation, severity, and context remain
exactly as specified above. `MonotonicTimestamp` is not part of the projected
capacity-warning record; the raw value must satisfy chapter
[03](03-shared-conventions-and-clocks.md)'s complete clock contract for its
loaded Core assembly identity, including source/frequency/origin, widened
subtraction, negative clamping, UInt128 floor conversion, saturation,
nondecreasing/no-wrap issuance and same-domain comparison. Equal timestamps
are valid.

A newly constructed World has no subscriptions and initializes its private last-issued subscription ID to zero. The first successful registration receives ID 1; each later successful registration receives the checked next positive ID. Refusal, any throwing admission, and disposal do not advance or reset this cursor. This World-local issuer is independent of Core OwnerId issuance; replacement constructs a distinct World with its own cursor and never resets an existing World's cursor.

Invoke subscribers individually; at the existing ordinary-exception boundary,
retain the exact thrown exception object and continue to the remaining captured
subscribers and other delivery lanes. The resulting
`SubscriberDeliveryFailed` Diagnostic has a nondefault Context containing
exactly one ordered item keyed
`TerraFX.Entities.SubscriberThrowStack.v1`. Its value is `C:` followed by the
complete qualified rendering of 0-4096 UTF-16 units, `T:` followed by the
exact 4096-unit prefix only after capture observed a 4097th unit, or one of
`U:Unqualified`, `U:Budget`, `U:Refused`, `U:WorkLimit`, or `U:Failed`. No
unavailable outcome contains partial text. No separate public stack property
is added.

For a qualified bounded-capture profile, isolate the ordinary per-subscriber
invoke-and-catch boundary in a dedicated private method marked with the exact
framework `StackTraceHiddenAttribute` at the method level. Preserve the exact
thrown exception instance, registration-order continuation and report
semantics. Mark only that boundary, not its declaring type, so ordinary helper
methods remain visible. This metadata policy is uniform across selected
outcomes and does not shorten or omit frame acquisition. A target profile
earns Complete(0) only from the actual complete sequence with no visible
frames under the fixed rendering rule; an empty writer literal is not a World
observation.

Do not invoke an unbounded public-BCL stack capture and truncate afterward.
Use rich capture only when a target-specific bounded capture/rendering profile
is independently derived and qualified; reserve its complete optional
workspace/output only after mandatory delivery/report/failure/diagnostic
carriers are reserved before the marker. If no bounded profile is qualified,
select `U:Unqualified` without attempting capture. Optional reservation
refusal becomes `U:Budget`; recoverable bounded-resource refusal, ordinary
capture failure, and an actually reached derived traversal/resource extent
become their corresponding U reasons. These outcomes cannot replace the
subscriber exception, skip later callbacks, prevent report sealing, fault the
committed World, or borrow mandatory baseline credits. A failure to reserve
the mandatory baseline follows the existing precommit fault policy. The
64-subscription limit bounds result multiplicity only, not frame count or
capture work. A required selected captured-provenance case that exceeds its
claimed profile fails qualification; U is not a downgrade.

Pre-reserve one result slot per captured subscription; after dispatch, expose
the owned immutable `PublicationDeliveryReport` with `Delivered` or
`Failed(SubscriberException)` for each attempted subscriber through the tick
receipt/current delivery-report view. Retaining a report is ordinary owned
data, not a lease on the mutable World; the current view may advance but
cannot mutate a previously returned report. Failure means the invocation
occurred but the engine cannot know how much user code consumed; never retry
automatically or synthesize rollback/duplicate `Begin`. The immutable
canonical batch and command receipts remain readable independent of callback
success. Subscription changes or `Stop` requests inside a callback affect only
later publications; complete the captured batch first, so a subscriber cannot
erase another's already scheduled notification.

`WorldSubscription.Dispose` remains owner-affine and idempotent. A legal
disposal removes that registration only from future captures; it does not
cancel an invocation already captured for the current publication or mutate a
report being assembled. Disposal during postcommit delivery is valid and
affects later publications only. World disposal retires registrations for
future captures; a later disposal on the original owner is an idempotent
no-op, while wrong-thread disposal still throws the contextual owner-affinity
exception.

A World has a current completed-delivery value independently observable from its own commit/control state for qualification. It is absent before the first completed delivery batch. Publication installation does not replace that value. Completion and sealing of a delivery batch replace it with that batch's immutable report value; a pre-publication failure leaves it unchanged. This requires neither a public getter nor preservation of a report object's reference identity. Disposal may retire the World-owned observation; caller-retained publication, receipt and report contents retain their existing lifetime guarantees.

Callbacks may copy data/enqueue future commands but cannot recursively `Step`, mutate the current committed world, await with a borrowed context or acknowledge fictitious rollback. The engine reports callback failures through owner-thread diagnostics and delivery results; telemetry loss cannot erase their count/status. A subscriber fault alone neither faults the committed world nor disables later delivery. Explicit application `Stop` and unrecoverable host failures occur after this commit, stop future admission and have explicit terminal delivery outcomes; they do not undo already attempted invocations. Normal shutdown/fault closure uses the committed episode ledger, so healthy observers receive one eventual terminal End even if another subscriber threw while receiving `Begin`. Unrecoverable process termination cannot guarantee managed notification delivery and is never claimed to do so.

Pump native/control readiness and accepted owner-boundary transactions between catch-up ticks, not just after all eight. Pause/resume and input overflow recovery are control-boundary work even with no admitted world ticks. FIFO structural intent is revalidated against current canonical values when applied; preview-time transform results are never committed stale.

Verify capture availability, mandatory report refusal, subscriber
continuation and completed-report sealing through the actual owner behavior
and returned diagnostics/results. Keep the existing public C/T/U behavior;
these observations add no delivery API, failure kind or rollback path.

## 3. Output queues and state visibility

Render snapshot queue capacity 4. Producer may replace the oldest not-yet-consumed snapshot, disposing its CPU/asset leases; it never replaces a snapshot already being recorded. This intentional lossy presentation stream is counted, does not lose gameplay events, and does not affect canonical hashes. Interpolation uses the two consecutive committed snapshots retained by renderer; nonconsecutive/missing history uses current pose with a discontinuity marker, not interpolation across a teleport.

Render thread releases CPU snapshot data after recording and retains every graphics processing unit (GPU) resource/descriptor/upload version until its fence. Asset unloading and world disposal cannot free that retained GPU lifetime. `Audio` spatial snapshots have latest-value semantics; Play/`Stop`/parameter commands follow chapter [24](24-custom-audio-mixing-and-spatialization.md)'s reliable/acknowledged protocol, not the render mailbox.

UI reads immutable diagnostic/scene-view snapshots or owner-thread state outside query/solve; it never examines mutable archetype memory while workers write. `Editor` play-world and edit-world are distinct owners. Asset/shader completion becomes active only at a named boundary and is recorded for replay.

## 4. Fault boundaries

System/validator/query/barrier faults close the world to further ticks/saves; join in-flight jobs, do not publish partial events/snapshots. Previously committed snapshot can stay visible with a fault overlay. `Physics`' staged transaction prevents a partial local physics step. A valid stage 4 local physics commit, like earlier ECS component writes, may remain after a later world-stage failure; neither is a committed whole-world checkpoint. `Integration` withholds external physics events until stage 9. Recovery reconstructs every service from the prior committed whole-world checkpoint, not from the newer local physics snapshot.

### Scoped receiver and command argument fault routing

Validate the receiver's captured invocation, World/binding lifetime and generation before processing arguments, invoking callbacks or admitting a command. Validate the active invocation's admitted thread, phase, logical prefix/barrier and permission before append preparation. A live invocation denied by those checks records its first protocol exception before throwing.

A null `WorldSystemContext` receiver does not enter an engine context method and cannot record a protocol fault; its language-level `NullReferenceException` follows ordinary callback-exception handling if uncaught. A fabricated nonnull context with no recognized issued invocation has no fault target and cannot record a sticky fault on an ambient or containing invocation. A context captured from a completed invocation likewise cannot select or fault a successor invocation or another World. If either receiver exception is caught, the containing invocation is not faulted by that receiver attempt; if uncaught, ordinary callback-exception handling applies.

A default, stale, foreign or otherwise invalid `Entity` argument supplied to a valid live context or writer rejects under that member's declared validation and records the fault on the current invocation. Entity fields are checked as arguments only; they never select a fault target.

An expired or revoked writer, view or binding whose captured invocation ended throws without recording a fault against a committed operation, successor invocation, another World or an ambient callback. Do not substitute a currently active invocation for the receiver's ended generation. Receiver-generation checks, first-fault recording and invocation completion linearize against that exact generation. An uncaught exception can still fail the caller through ordinary callback-exception handling; that is not a fault recorded by the expired receiver. An expired `CommandWriter` specifically throws `InvalidOperationException` with contextual, nonfixed message text before inspecting arguments; it records no fault against another or successor invocation. Other expired view/binding exception types remain owned by their declared surfaces.

With a valid live writer, a default/unissued or forged token, foreign World/binding or logical-prefix token, wrong tick/barrier token, token not naming an earlier admitted `Create`, or token canceled by an earlier admitted same-prefix `Destroy` is an invalid argument of the current invocation. Throw `ArgumentException` naming its declared parameter: `owner` or `target` for reference overloads; `entity` for existing token-taking forms. Messages are contextual, not fixed API text. Record the actual exception object in the first-protocol-fault slot before throwing; never fault a purported token owner.

A live `CommandWriter.Create` whose explicit nonempty `EntityDescription.PersistentId` is in the reserved RFC-variant UUIDv8 domain is likewise a first protocol fault: throw `ArgumentException(nameof(description))` and record that actual exception before throwing. Refuse before command-key/sequence allocation, payload preparation/copy, command-staging reservation or token issuance. A caught refusal remains sticky. An idle `World.CreateEntity` applies the same assignment-domain rule before mutation; it does not create an invocation fault or consume an assignment ordinal.

After the first protocol fault, no later command member may append or consume command resources. A later command call throws a contextual `InvalidOperationException` for the already-faulted invocation before argument processing; this later refusal cannot replace the first protocol exception as the callback/system cause. A same-object rethrow from every subsequent member is not required. Dispatch checks the first fault before accepting callback completion.

Refused appends consume no sequence, payload copy, command-staging reservation or token issuance. A reference admitted before later cancellation remains an admitted trace entry and is decided by complete structural preflight; it does not retroactively fault an ended invocation. Preserve existing Destroy mode-before-target precedence and exact `ArgumentOutOfRangeException(nameof(mode))`.

For a system Execute or other callback with sticky protocol-fault state, its first recorded protocol exception remains the selected callback/system cause even if caught and followed by a distinct escaped exception. If no sticky fault exists, use the escaped exception. The ordinary Step channel preserves the selected object as `WorldStepException.InnerException`. Do not aggregate or add a secondary-exception retention API. Preserve original throw provenance when deferring propagation. Cleanup callbacks have independent fault state; freeze the containing primary before cleanup. Existing attachment/R2 carrier precedence and exact cause identities remain unchanged.

### Attachment provider boundary and fault routing

Attachment field-reader and provider invocations use the same-World nonreentrant boundary defined in [12](12-entities-components-and-queries.md). A prohibited nested public admission throws synchronously:

```csharp
new InvalidOperationException(
    "A World operation cannot be admitted during an attachment reader or provider invocation.");
```

After identifying the actual World and active invocation, refusal precedes argument processing that could invoke callbacks or admit work. A nested Step rejects before tick admission and does not itself throw `WorldStepException`. The first denied-call exception is retained as the invocation's sticky protocol fault before it is thrown. Catching it, returning normally, returning diagnostics or supplying another result cannot clear it; repeated denials do not create additional release attempts. Fault routing uses the live containing operation, never a World inferred from invalid Entity bits or an expired binding. Retained-binding misuse outside an invocation remains throw-only. Generation checks and invocation completion linearize so a late call cannot fault a committed or successor operation.

After each reader/provider callback returns or unwinds, dispatch checks the sticky fault before accepting its output. Before commit, a sticky fault stops normal preparation. Immediately before installation commit, the owner also revalidates the actual target lifetime, membership/slot generation, current installation identity and canonical revision against the prepared transaction; stale preparation cannot be installed.

For idle installation validation, unwind unpublished preparation, retain the old installation and caller ownership of the untransferred proposal, and invoke no Release for that value. No write grant occurred, so canonical revision and stored committed-marker generation are unchanged. This is provider protocol misuse: fault the World, set `LastFault` to an Error diagnostic for `AttachmentInvocationFault` identifying the actual operation, and throw the first recorded `InvalidOperationException` object without replacing its cause. Do not return successful installation, false/absence or a release report. Fault inspection and required cleanup/disposal remain available; the unchanged marker does not make a faulted World capture-eligible. Expected reservation refusal and ordinary validation rejection retain their existing, nonfaulting channels.

When idle validation is part of Step, route the original callback fault through the existing tick-fault channel: throw `WorldStepException` with that exception as `InnerException`, update `LastFault`, and do not publish or advance the tick. Do not claim rollback of earlier tick writes.

After storage commit, a Release callback fails if it escapes an exception or returns with a sticky protocol fault. The first sticky exception is its cause when present; otherwise use the escaped exception. Record that failed invocation once for the released installation, so a caught nested Set/Remove/Step/Dispose becomes one `WorldAttachmentReleaseFailure` with the exact recorded exception as `OriginalException`. Normal return cannot certify cleanup after protocol misuse. Committed storage, transfer and mutation revision remain unchanged; continue all safe cleanup exactly once without retry or rollback. Unknown/unreleased obligations remain owned and charged under existing retention/quarantine rules.

Complete-report failures use `WorldAttachmentReleaseException`; finalization failures use the prepared `WorldAttachmentReleaseReportFinalizationException`. Preserve their existing idle/Step channels, cause identity, counts, omission policy and `StorageCommitted` meaning. The admission boundary adds no reporting channel or exception family.

A render device loss fails pending graphics observations/uploads with device epoch/context, disables new submissions and invokes configured `StopHost(default)` or `SuspendPresentation` policy. The latter can keep simulation/audio explicitly running with a visible fault state; no ordinary successful rendered frame is reported. Asset worker failures become typed failed tickets; missing required assets fail scene admission, optional explicitly configured substitutes carry diagnostics.

## 5. Shutdown and scheduling bounds

`Stop` admission/input commands; complete/cancel ordinary I/O; finish or fault the current tick and join CPU work; close snapshots/audio requests; stop audio/device callbacks; stop render production and wait submission plus presentation retirements; destroy windows only after surface teardown; release asset/plugin/native owners last. Application `StopAsync` observes completion or explicit timeout/fault, never a fake clean shutdown.

Frame wait defaults 5 s configurable, timeout is diagnostic/fault rather than destroy-in-use. Workers check cancellation between bounded partitions; canceling a wait cannot free storage still in use. Save flushing is explicit policy: default await already-accepted transactions, no new save on shutdown. Host owner's message pump continues while asynchronous shutdown depends on native dispatch; no blocking circular joins.
