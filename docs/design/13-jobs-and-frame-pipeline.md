# Jobs, stages, immutable output, and shutdown

[Terminology and reading conventions](01-terminology-and-reading-guide.md).

## 1. Ownership and worker admission

Host's dedicated owner thread pumps platform windows, user interface (UI), world stage barriers and trusted systems. Dedicated render thread owns rendering hardware interface (RHI) recording/submission/presentation; audio processing never accesses entity-component-system (ECS). Bounded asset I/O/import workers use `Task`/`Channels`. Fixed central processing unit (CPU) worker pool executes synchronous engine jobs and never blocks on device/asset/UI work; defaults `min(64, max(1, logicalProcessors-2))`, configurable integer counts in `[1, 64]`.

System registration provides stable `Guid`, unique explicit `Order`, `Stage`, component/resource read and write sets, optional Before/After constraints, and owner-thread or parallel eligibility. `Order` is a signed `int`, including negative values; command keys preserve that exact registered value and signed comparison rather than casting it to an unsigned ordinal. Stable partition ordinals are nonnegative `int`, and local command sequences use checked `long`. A resource is a named world service/state partition, not just a component. Registration freezes before play; incompatible metadata/cyclic constraints reject startup.

Compile a stage directed acyclic graph (DAG) from explicit edges plus conflicting accesses. Conflicts are ordered by explicit `Order` then `Guid`, independent of discovery/reflection/dictionary order. Read/read may run concurrently; write/read and write/write serialize unless declared disjoint partitions with checked no-overlap ranges. Hidden managed/native/global writes are forbidden in jobs.

Each parallel query splits ordered chunk rows into 64-row partitions, stable ordinal; independent jobs may steal execution, not identity. Deterministic reductions write partition-local results then fold in partition order with a specified operator/precision. No shared atomic `float` sum is claimed deterministic. Worker exceptions are captured with system/partition/tick and all siblings are joined before fault handling.

`ThreadPool` is not a substitute for these stage/lifetime guarantees; use it for ordinary asynchronous I/O, not solver/frame/audio affinity. `Zero`-worker mode performs the same ordered work inline for diagnosis.

## 2. One simulation tick

| `Stage` | Work / visibility |
|---|---|
| 0 `Begin` | Drain accepted external commands/completions, apply prior external structural transaction, freeze input/time-scale segment and asset/version admission |
| 1 `Input` / `Early` | Consume this tick's normalized events; evaluate gameplay actions; `Early` gameplay systems enqueue changes/forces/requests |
| 2 `Animation` / Targets | Sample clips/root motion; application systems consume motion requests and enqueue kinematic targets or dynamic forces/impulses; no direct dynamic-pose overwrite |
| 3 `Transforms` | Resolve current dirty hierarchy; validate physics transform ownership before native/custom solve |
| 4 `Physics` | Admit body commands; solve staged physics state with fixed dt; commit valid body/contact state only |
| 5 Pose Sync | Copy body poses to owned root transforms; resolve descendants |
| 6 Events / `Late` | Make copied sorted contact/trigger/motion-feedback events available to tick-local `Late` systems; no external publication yet and no second hidden physics solve |
| 7 `Structural` | Join every job/query; preflight/commit all deferred structural commands; resolve newly dirty transforms; new bodies first simulate next tick |
| 8 `Extract` | Produce immutable render snapshot, animation palette version and audio transform snapshot; acknowledged audio commands have separate reliable delivery |
| 9 `Commit` | Irrevocably install the committed publication/episode ledger and advance `TickIndex`; enable save/replay capture, then dispatch external observations |

Command/event payloads retain type/schema/owner/tick/sequence. A callback cannot recursively `Step` the world, mutate the current solver or perform structural work outside its writer. Contact-end from stage 7 destruction is delivered at next tick's Events stage with explicit cause, and retained even though the original entity no longer exists; payload carries historical IDs not live refs.

Host pumps UI/input each wall iteration even with zero admitted world ticks or paused worlds. Platform text/UI callbacks precede assignment of remaining gameplay input events. `World`'s fixed rate is not render rate; extraction occurs after every committed tick, rendering may select the latest completed pair.

`Input` follows chapter [14](14-custom-input-actions-and-text.md)'s recipient contract: one `HostLiveUI` router per window and at most one attached simulation-timeline router. Native capture occurs once; copied event-time UI dispositions and original event identities enter each recipient's bounded inbox. A world's stage 1 consumes only its own tick-assigned cursor/state, never the current platform/UI mirror or another world's records. Pause/focus/overflow controls affect only their declared recipient; headless timelines use explicit injected/replay input.

Stages 0 through 8, inclusive, prepare output but do not publish it to external event subscribers/render/audio. `Stage` 9 commits only after all tick work succeeds. Tick systems stage audio one-shots/other observable effects in owned records; they cannot call external I/O or the live audio mixer as if whole-world rollback existed. After commit, audio requests follow chapter [24](24-custom-audio-mixing-and-spatialization.md)'s admission/receipt protocol; a delivery failure faults that request and reports `AudioDeliveryFailed`, not retroactively invalidates an already committed gameplay tick. Render/audio latest-value data are deliberately observational.

### Irrevocable commit and external delivery

Before the commit point, stage 9 validates/reserves the complete immutable `CommittedWorldPublication`: completed tick, checked next `TickIndex`/publication sequence, spatial settings/frame descriptor/revision, sorted gameplay events and command results, extracted snapshots, staged effect requests and the replacement `Integration` episode ledger. Snapshot the active external subscriptions in checked monotonic registration order. Reservation/exhaustion or validation failure here follows section 4's pre-commit fault policy; no subscriber runs and no receipt/output becomes externally visible.

The owner installs one committed publication marker after all these preparations, without allocation or user/native callbacks. Its release-published immutable record is the single visibility point for the completed tick, next `TickIndex`, episode ledger and save/replay capture eligibility; readers acquire that record, not separately mixed generations. Owner state and the ledger are coherent before delivery starts. A checkpoint captured after this marker describes this committed version; older actually captured checkpoints remain earlier committed recovery points. This does not mandate serializing the whole world on every tick. Before the marker, a failure cannot publish this tick. After it, the tick is irrevocably committed: notification/audio/render failure cannot decrement `TickIndex`, suppress canonical events/receipts, remove a ledger entry, rerun the tick or relabel it `FaultedBeforeCommit`.

Dispatch external subscribers synchronously on the host owner after commit and outside native/solver callbacks. Each world and the host-native event channel admit at most 64 subscriptions; `TrySubscribe(callback, out Subscription? subscription)` returns false/`null` only at that bound, with `NotNullWhen(true)`, no list change and an explicit capacity diagnostic. Invalid arguments/disposal/ID exhaustion throw. Subscription IDs are checked monotonically increasing positive `long` values, never reused/wrapped. Each subscription receives the immutable whole event batch once in captured registration order, with identity `(WorldGuid,PublicationSequence,SubscriptionId)` for world publications. Invoke subscribers individually; catch ordinary subscriber exceptions only at that invocation boundary, retain the exception/stack in `SubscriberDeliveryFailed`, and continue to the remaining captured subscribers and other delivery lanes. Pre-reserve one result slot per captured subscription; after dispatch, expose the owned immutable `PublicationDeliveryReport` with `Delivered` or `Failed(SubscriberException)` for each attempted subscriber through the tick receipt/current delivery-report view. Retaining a report is ordinary owned data, not a lease on the mutable world; the current view may advance but cannot mutate a previously returned report. Failure means the invocation occurred but the engine cannot know how much user code consumed; never retry automatically or synthesize rollback/duplicate `Begin`. The immutable canonical batch and command receipts remain readable independent of callback success. Subscription changes or `Stop` requests inside a callback affect only later publications; complete the captured batch first, so a subscriber cannot erase another's already scheduled notification.

Callbacks may copy data/enqueue future commands but cannot recursively `Step`, mutate the current committed world, await with a borrowed context or acknowledge fictitious rollback. The engine reports callback failures through owner-thread diagnostics and delivery results; telemetry loss cannot erase their count/status. A subscriber fault alone neither faults the committed world nor disables later delivery. Explicit application `Stop` and unrecoverable host failures occur after this commit, stop future admission and have explicit terminal delivery outcomes; they do not undo already attempted invocations. Normal shutdown/fault closure uses the committed episode ledger, so healthy observers receive one eventual terminal End even if another subscriber threw while receiving `Begin`. Unrecoverable process termination cannot guarantee managed notification delivery and is never claimed to do so.

Pump native/control readiness between catch-up ticks, not just after all eight. Pause/resume and input overflow recovery are control-boundary work even with no admitted world ticks.

## 3. Output queues and state visibility

Render snapshot queue capacity 4. Producer may replace the oldest not-yet-consumed snapshot, disposing its CPU/asset leases; it never replaces a snapshot already being recorded. This intentional lossy presentation stream is counted, does not lose gameplay events, and does not affect canonical hashes. Interpolation uses the two consecutive committed snapshots retained by renderer; nonconsecutive/missing history uses current pose with a discontinuity marker, not interpolation across a teleport.

Render thread releases CPU snapshot data after recording and retains every graphics processing unit (GPU) resource/descriptor/upload version until its fence. Asset unloading and world disposal cannot free that retained GPU lifetime. `Audio` spatial snapshots have latest-value semantics; Play/`Stop`/parameter commands follow chapter [24](24-custom-audio-mixing-and-spatialization.md)'s reliable/acknowledged protocol, not the render mailbox.

UI reads immutable diagnostic/scene-view snapshots or owner-thread state outside query/solve; it never examines mutable archetype memory while workers write. `Editor` play-world and edit-world are distinct owners. Asset/shader completion becomes active only at a named boundary and is recorded for replay.

## 4. Fault boundaries

System/validator/query/barrier faults close the world to further ticks/saves; join in-flight jobs, do not publish partial events/snapshots. Previously committed snapshot can stay visible with a fault overlay. `Physics`' staged transaction prevents a partial local physics step. A valid stage 4 local physics commit, like earlier ECS component writes, may remain after a later world-stage failure; neither is a committed whole-world checkpoint. `Integration` withholds external physics events until stage 9. Recovery reconstructs every service from the prior committed whole-world checkpoint, not from the newer local physics snapshot.

A render device loss fails pending graphics observations/uploads with device epoch/context, disables new submissions and invokes configured `StopHost(default)` or `SuspendPresentation` policy. The latter can keep simulation/audio explicitly running with a visible fault state; no ordinary successful rendered frame is reported. Asset worker failures become typed failed tickets; missing required assets fail scene admission, optional explicitly configured substitutes carry diagnostics.

## 5. Shutdown and scheduling bounds

`Stop` admission/input commands; complete/cancel ordinary I/O; finish or fault the current tick and join CPU work; close snapshots/audio requests; stop audio/device callbacks; stop render production and wait submission plus presentation retirements; destroy windows only after surface teardown; release asset/plugin/native owners last. Application `StopAsync` observes completion or explicit timeout/fault, never a fake clean shutdown.

Frame wait defaults 5 s configurable, timeout is diagnostic/fault rather than destroy-in-use. Workers check cancellation between bounded partitions; canceling a wait cannot free storage still in use. Save flushing is explicit policy: default await already-accepted transactions, no new save on shutdown. Host owner's message pump continues while asynchronous shutdown depends on native dispatch; no blocking circular joins.
