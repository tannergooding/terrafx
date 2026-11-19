# M03-B phased World replacement owner protocol

**Status: tentative internal-contract decision, not implementation or qualification.** This addendum supplies the concrete protocol left open by [owner alignment](m03b-world-restore-operation-owner-alignment.md). It selects an explicit reassignment of the complete execution actor, not a publish-only reinterpretation of that actor. Only this proposal is changed.

## 1. Decision and exact contract delta

Keep the captured `TerraFX.Entities.WorldReplacementOperation` as the sole lower replacement authority. Introduce a non-authoritative, typed `TerraFX.Hosting.WorldLoadExecution` that drives that authority and the existing higher restore/capture operations through ordinary calls and returns.

**Replace, rather than narrow, the Chapter 30 declaration**

```csharp
// Withdraw this complete-execution declaration in the subsequent contract change:
// TerraFX.Entities.WorldReplacementOperation
WorldReplacementOutcome ExecuteAtOwnerBoundary();
```

with **complete execution** at:

```csharp
// New internal TerraFX.Hosting.WorldLoadExecution instance member:
internal WorldLoadReceipt ExecuteAtOwnerBoundary();
```

That Hosting method still performs the entire candidate-construction, restoration, fresh-capture/comparison and publication lifecycle, or produces the existing prepublication refusal/fault. Its call to the lower publisher is only one step. It never grants Hosting direct World-factory or publication permission. The old lower method is not retained as an alias, called repeatedly, made into a coroutine, or described as having performed higher work before its invocation.

This is the one substantive actor-placement decision. Preserving the original complete method on the Entities type literally, while excluding reverse calls/callbacks, has no valid same-owner-thread control flow: higher Scene work must run between its candidate creation and its terminal return. This proposal **explicitly replaces that declaration** under the tentative-contract authorization. It is not a claim that the existing declaration already permits phases.

The accompanying private signature changes make context explicit instead of introducing a lookup:

| Existing Chapter 30 entry | Selected internal replacement |
|---|---|
| `Task<WorldLoadReceipt> WorldPersistence.CommitLoadAtOwnerBoundary(WorldReplacementOperation)` | `WorldLoadReceipt WorldPersistence.CommitLoadAtOwnerBoundary(WorldLoadExecution execution)` |
| `bool EngineHost.TryEnqueueWorldReplacement(WorldReplacementOperation, out Task<WorldLoadReceipt>?)` | `bool EngineHost.TryEnqueueWorldReplacement(WorldLoadExecution execution, out Task<WorldLoadReceipt>? completion)` |
| `void EngineHost.AdoptWorldReplacement(World, World, long)` | `void EngineHost.AdoptWorldReplacement(EngineHost.WorldReplacementAdoptionPlan adoption)` |
| `void WorldInputTimelineAdapter.AdoptWorld(World, World, long, bool)` | The existing concrete `WorldInputTimelineAdapter.WorldReplacementAdoption.Apply(): void`, reached by the prepared host adoption plan, not an unprepared postpublication operation. |

The typed adoption form deliberately carries the preallocated schedule segment and exact adapter edits. It is selected because it avoids postpublication allocation and ambient pending-adoption state, not because current source overrides the contract. The owning Chapters 27/29/30 must record these exact changes together before implementation is accepted. Public `CommitLoadAsync(WorldLoadPlan, CancellationToken)`, receipts, restore intents, schemas and acceptance identifiers do not change.

## 2. Source facts that make this decomposition possible

- `WorldConstructionPlanControl` already owns the exact lower construction plan, target and replacement preparation. `WorldPreparedReplacement` already forwards to it. Capture can issue their preowned operation instead of exposing the current separate credit/handoff outputs.
- `WorldLoadPreparedState.CreateRestoredCandidate` contains separable higher restoration work after its unauthorized direct factory call. Scene preparation/restoration already takes an actual `World` explicitly.
- `WorldPersistenceCaptureCoordinator.Capture` composes actual Scene capture with lower World capture. Keep it in Integration; keep `WorldPersistenceWorldRecordReader` and package/reference orchestration above Entities.
- The current `Hosting.WorldPersistenceRestoreValidator.Validate` accepts only Core/Entities data and performs byte/history comparison. Move that pure comparison, with its algorithm unchanged, to Entities. Do not move the reader, source loading, Scene capture or package preparation with it.
- `ValidatedWorldIdentityHistory.EncodedRecord` permits checking that the supplied decoded fresh history belongs to the actual captured ECS history bytes, rather than trusting a boolean “validated” result.
- `WorldRestoreSourceControl.SourceUse` is already a concrete lower lease retaining the prepared source graph. The existing source owner releases its package, checkpoint and prepared assets through that control's existing cleanup path. Playback need not name those higher types.
- The existing host adoption plan already contains a schedule segment and concrete input-adoption array. It is data, not a delegate, and can be passed explicitly from the complete executor to the typed request.

These are reuse points, not acceptance evidence. The first addendum's frozen source/caller findings remain applicable; this document supersedes only its unresolved protocol disposition.

## 3. Exact lower declarations and actor permissions

All declarations in this section are internal to `TerraFX.Runtime`, namespace `TerraFX.Entities`. Blocks are owning-member declaration projections, not compilable source files. Omitted fields are private implementation storage, not extensibility points. No declaration mentions Hosting, Scenes, Animation, AssetStore or a package/checkpoint wrapper.

### Preparation and capture

```csharp
// Existing types; selected additional/replacement members.
internal sealed class WorldPreparedReplacement
{
    internal bool IsCommitCaptured { get; }
    internal bool IsCapturedOperation(WorldReplacementOperation operation);
    internal void SealInputs(
        WorldRestoreSourceControl.SourceUse sourceUse,
        ReadOnlyMemory<byte> validatedWorldRecord,
        scoped ReadOnlySpan<byte> checkpointDigest,
        scoped ReadOnlySpan<byte> payloadDigest);
    internal bool TryCaptureCommit(
        out WorldReplacementOperation? operation, out WorldGuidRefusal refusal);
    internal void CloseAcquisition();
}

internal sealed class WorldConstructionPlanControl : IWorldGuidRetentionOwner
{
    // Its constructor preowns one operation before any capture transaction.
    internal bool IsCommitCaptured { get; }
    internal bool IsCapturedOperation(WorldReplacementOperation operation);
    internal void SealInputs(
        WorldRestoreSourceControl.SourceUse sourceUse,
        ReadOnlyMemory<byte> validatedWorldRecord,
        scoped ReadOnlySpan<byte> checkpointDigest,
        scoped ReadOnlySpan<byte> payloadDigest);
    internal bool TryCaptureCommit(
        out WorldReplacementOperation? operation, out WorldGuidRefusal refusal);
    internal void CloseAcquisition();
}

// Replacement return type on the existing source-control member:
internal WorldRestoreSourceControl.SourceUse
    WorldRestoreSourceControl.AcquireUse();
```

`WorldConstructionPlanControl` constructs its sole operation outside admission gates from its existing `WorldConstructionPlan`, exact `World` and `WorldGuidReplacementPreparation`. The operation preowns its empty playback control and fixed transition/refusal storage under the existing finite preparation/candidate/cleanup allowance.

Only the new `void Hosting.WorldLoadPreparedState.SealReplacementInputs()` calls `SealInputs`, after source reads, complete target validation, package/reference validation and all preparation consumers have joined, before returning `Prepared`. This is a fixed one-shot sealing operation, not a builder or caller-selected participant set:

- The source use must be from the exact source control already bound by `BindPreparedResourceSourceOwner`; its registration/history/completed tick must match the construction plan.
- It transfers the existing source-use object to the lower operation only on success; failure leaves the caller responsible. No source use is acquired under capture gates.
- The record is the exact retained, validated World projection underlying that source. Check its ECS and Scene record slices against the source control's retained projections; the current State2 World tags are 5 for ECS and 6 for Scenes. Retain the same backing through the transferred source use, not a bare caller buffer.
- Both digests are exactly 32 bytes. Check the payload digest against the complete retained World record; the checkpoint digest comes from the already admitted checkpoint. Copy digest facts into funded fixed operation storage before capture. The selected Branch comparison retains the existing admitted branch/history transformation, not a new whole-record-equality rule.
- Seal once. A failed, foreign, closed, incomplete or rebound source cannot become capture-ready.

The public Hosting `WorldPersistence.CommitLoadAsync` remains the **only direct caller** of `WorldPreparedReplacement.TryCaptureCommit`. Its signature and synchronous acceptance point are unchanged. The control fills the preowned operation with the exact captured grants/interests/handoff and returns that complete object or null, never a partial bundle.

`WorldGuidRefusal` remains the single bounded capture-refusal carrier. To retain the current distinction between binding refusal and admission refusal, add its internal `WorldAdmissionRefusal AdmissionRefusal { get; }` and `static WorldGuidRefusal ForAdmission(WorldAdmissionRefusal refusal)`; only the construction control calls that factory. The factory accepts a defined, non-None existing admission value. `IsNone` requires both the existing registry code and this admission field to be None. No serialized code or public rejection identifier is added.

Only `WorldLoadPlan.Dispose()` calls `CloseAcquisition`. `IsCommitCaptured` and `IsCapturedOperation` report the shared control's state/reference identity; they create no authority. Closing first prevents capture; capture first remains true despite later acquisition closure. Remove the competing higher capture flags/transactions and their direct lower-close calls rather than retaining an alternate admission path.

The prepared capability's selected members forward only to the same-signature control members above; the control is their only state owner. The “only Dispose” restriction names the Hosting caller of the capability, not its lower forwarding edge. Its state-query members are used only by the plan's disposal and execution binding paths.

### Candidate, comparison and terminal publication

```csharp
internal sealed class WorldReplacementOperation : IWorldGuidRetentionOwner
{
    internal WorldReplacementOperation(
        WorldConstructionPlan constructionPlan, World targetWorld,
        WorldGuidReplacementPreparation preparation);

    internal World TargetWorld { get; }
    internal WorldDomainOperationCredit MappingCredit { get; }

    internal bool TryCreateCandidateAtOwnerBoundary(
        out World? candidate, out WorldGuidRefusal refusal);
    internal void PreparePublicationAtOwnerBoundary(
        in WorldPersistenceCaptureLease captureLease,
        ReadOnlyMemory<byte> freshWorldRecord,
        ValidatedWorldIdentityHistory freshIdentityHistory);
    internal WorldReplacementOutcome PublishAtOwnerBoundary();
    internal void AbandonBeforeOwnerBoundary();
    internal void AbortAtOwnerBoundary();
    internal void EndAfterConsumerCleanup();

    private void TransferSourceToPlayback(WorldPlaybackControl playback);
}

internal enum WorldReplacementOutcomeKind
{
    Invalid,
    Published,
    Rejected,
}

internal readonly struct WorldReplacementOutcome
{
    internal WorldReplacementOutcome(WorldReplacementOutcomeKind kind,
        World? replacementWorld, WorldGuidRefusal refusal);
    internal WorldReplacementOutcomeKind Kind { get; }
    internal World? ReplacementWorld { get; }
    internal WorldGuidRefusal Refusal { get; }
}
```

Exact callers and effects:

| Member | Only authorized caller / effect |
|---|---|
| Operation constructor | `WorldConstructionPlanControl` construction, outside gates; no candidate or OwnerId issuance. |
| `TryCreateCandidateAtOwnerBoundary` | `Hosting.WorldLoadExecution.ExecuteAtOwnerBoundary`; accepts only Captured, exact target owner thread. Revalidate the target before issuance. It alone calls `World.CreateRestoreCandidate(WorldConstructionPlan)`, retains the existing candidate source-budget reservation, then uses `World.InitializeRestoreCandidate(World)`. Complete the admitted Entity map before Restore callbacks. `false` means bounded stale-target refusal and null candidate; unexpected construction/initialization failures fault and retain cleanup ownership. |
| `MappingCredit` | Borrowed by that complete executor only after candidate creation, solely for the existing `WorldIdentityRemap.InitializeFromIdentityHistories` retained-use acquisition. It is the captured bundle's existing Data credit, not a new credit or permission for Hosting to end it. |
| `PreparePublicationAtOwnerBoundary` | That complete executor only, after all restoration/join/host-preflight/remap preparation and actual source-using callbacks have joined. The operation performs the final pure comparison, prepares its marker and installs playback as specified below. |
| `TransferSourceToPlayback` | Only the immediately preceding lower member, after its last comparison read of source/target data. The old complete-execute caller edge is explicitly replaced. |
| `PublishAtOwnerBoundary` | That complete executor only, once, after successful preparation. **Sole terminal publication owner** and sole production caller of `World.TryCommitLoadReplacement`. |
| `AbandonBeforeOwnerBoundary` | `WorldLoadExecution.RejectOwnerQueueFull` or `FailBeforeOwnerBoundary`; legal only for a captured operation whose owner execution never started. It invalidates the unused attempt without issuing a candidate. |
| `AbortAtOwnerBoundary` | `WorldLoadExecution.ExecuteAtOwnerBoundary`'s unwind, or `CompleteAfterOwnerBoundary` for a prepublication refusal, after higher candidate users have joined. Dispose only the unpublished candidate and its installed ownership; never dispose a published replacement. |
| `EndAfterConsumerCleanup` | `WorldLoadExecution.CompleteAfterOwnerBoundary` or `FailBeforeOwnerBoundary`, only after actual higher consumer/cleanup quiescence. End remaining captured interests/grants exactly once; failed cleanup retains the existing debt/anchor instead of declaring release. |
| Outcome constructor | Only `PublishAtOwnerBoundary`; a copied result is a fact, never a new candidate/history permission. |

The candidate source-budget reservation must come from the sealed, prepaid candidate allowance transferred at capture, not a new domain admission. Reuse/adapt `RetainPreparedRestoreSourceBudget` only with that meaning: closure after capture cannot make a late expansion necessary. Insufficient original coexistence/control allowance is a preparation-capacity refusal before sealing/returning `Prepared`, not a reason to refund accepted grants, acquire another interest or reject accepted work merely because the external domain closed.

Outcome alternatives are closed: default `Invalid` is not a result; `Published` has the exact active replacement and no refusal; `Rejected` has null replacement and a non-None bounded refusal. There is no intermediate candidate alternative, public receipt, Task, callback, remap or old-retirement owner in this value. Exceptions are not encoded as `Rejected`.

The lower pure comparison retains the existing exact signature, changing only its namespace:

```csharp
// TerraFX.Entities.WorldPersistenceRestoreValidator
internal static void Validate(
    ReadOnlyMemory<byte> sourceWorldRecord,
    ReadOnlyMemory<byte> targetWorldRecord,
    ValidatedWorldIdentityHistory sourceIdentityHistory,
    ValidatedWorldIdentityHistory targetIdentityHistory,
    WorldConstructionPlan constructionPlan,
    scoped ReadOnlySpan<byte> checkpointDigest,
    long nextTick);
```

`PreparePublicationAtOwnerBoundary` borrows the actual fresh record while its capture reservation and live restore-validation capture lease are retained. Require `IsRestoreValidation`, `IsBoundTo` the operation's exact candidate, and successful `ValidateCurrent`. Match the supplied fresh history's complete `EncodedRecord` to the history record in the fresh World/ECS bytes. Then compare using the sealed source projection, exact plan and digest, not a Hosting-supplied success flag. The actual higher capture actor remains responsible for observing Scenes, not substituting saved Scene bytes.

Before returning, prepare the coherent marker, store the capture's canonical mutation revision and perform the one-use playback transfer after the final source read. No record span, capture lease or mutable output buffer escapes this borrowing call. The higher owner releases its fresh record/lease only after return.

Extend the existing lower World publication member with a final stamp parameter:

```csharp
internal bool World.TryCommitLoadReplacement(
    World replacementWorld, CommittedWorldPublication restoredPublication,
    WorldGuidReplacementPreparation preparation,
    WorldDomainOperationCreditBundle operationCredits,
    WorldDefaultDomainHandoff? defaultDomainHandoff,
    long comparedCanonicalMutationRevision,
    out WorldGuidRefusal bindingRefusal);
```

Only the operation's publisher supplies that stamp. Under the existing final binding/publication gates, verify candidate completeness/marker/frame facts and that its canonical revision still equals the compared revision. A changed candidate revision is an invariant fault, not expected stale-target rejection. Stale target is the existing bounded refusal. No canonical write or additional restoration is legal between preparation and publication.

### Playback is a lower data-use owner, not a package orchestrator

```csharp
// Replaces the internal Hosting type; it is not an alias or public API.
internal sealed class WorldPlaybackControl : IDisposable
{
    internal WorldPlaybackControl(WorldAdmissionDomain domain);
    internal void Initialize(
        WorldRestoreSourceControl.SourceUse sourceUse,
        WorldDomainOperationCredit sourceCredit,
        WorldResourceOperationReservation sourceBudget);
    public void Dispose();
}
```

Only the operation constructor creates it; only its private transfer member initializes it. The operation validates the exact sealed source control, candidate budget and source-credit domain before moving anything. Then transfer the existing concrete source use, existing candidate budget and existing `S_c` once, and install the initialized control through the existing `World.AdoptRestoreLifetimeLease(IDisposable)` path. The operation clears its donor ownership and never reads or ends transferred source authority again.

Playback has no operation, old World, construction-plan, Hosting checkpoint or package reference. Its lower source-use control retains the already existing source owner/data/pins. This adds **no new delegate or executable port**: retain the existing source-control cleanup mechanism solely for its existing data release, never to invoke restoration or capture.

Keep `WorldRetainedHistoryControl` in Hosting as the independent `S_o` data owner. Remove its old `_playbackSourceCredit`/`AttachPlaybackSourceCredit` route; `S_c` now belongs directly to the lower playback control. Old-source data ownership does not acquire process-binding authority.

On retirement, release the playback use and budget through their actual cleanup protocol, then end its `S_c` after its own actual consumers/cleanup have finished. Other independently retained source readers may keep `S_o` alive. A failed/uncertain release keeps all remaining playback fields, credits and original failure rooted by the existing candidate/source quarantine owner; it does not clear ownership first, retry uncertain work or refund by Task completion/GC. Prepublication candidate failure uses that same cleanup. A published candidate keeps playback despite old-binding revocation or plan/service disposal.

## 4. Exact higher context and end-to-end caller chain

All types below are internal in `TerraFX.Hosting` except the unchanged public entry. `WorldLoadExecution` is an explicit per-plan object, not a lower-owned opaque executable, service locator or operation-keyed dispatch table.

```csharp
internal sealed class WorldLoadExecution
{
    internal WorldLoadExecution(WorldLoadPlan plan,
        WorldLoadPreparedState state, WorldPreparedReplacement replacement);
    internal Task<WorldLoadReceipt> Completion { get; }
    internal World TargetWorld { get; }
    internal WorldPersistence Persistence { get; }
    internal EngineHost.WorldReplacementAdoptionPlan? Adoption { get; }

    internal void BindCaptured(
        WorldPersistence persistence, WorldReplacementOperation operation);
    internal WorldLoadReceipt ExecuteAtOwnerBoundary();
    internal WorldLoadReceipt RejectOwnerQueueFull();
    internal void CompleteAfterOwnerBoundary(WorldLoadReceipt receipt);
    internal void FailBeforeOwnerBoundary(Exception failure);
}

// Additional immutable references after WorldLoadPlan.Initialize:
internal WorldPreparedReplacement WorldLoadPlan.PreparedReplacement { get; }
internal WorldLoadExecution WorldLoadPlan.Execution { get; }

// Replacements/additions on the existing higher state:
internal void WorldLoadPreparedState.SealReplacementInputs();
internal void WorldLoadPreparedState.RestoreCandidate(
    World candidate, CancellationToken cancellationToken);

// Selected exact routing declarations:
internal WorldLoadReceipt WorldPersistence.CommitLoadAtOwnerBoundary(
    WorldLoadExecution execution);
internal bool EngineHost.TryEnqueueWorldReplacement(
    WorldLoadExecution execution, out Task<WorldLoadReceipt>? completion);
internal void EngineHost.AdoptWorldReplacement(
    EngineHost.WorldReplacementAdoptionPlan adoption);

// Selected members of the existing EngineHost.WorldReplacementAdoptionPlan:
internal sealed class WorldReplacementAdoptionPlan
{
    internal WorldReplacementAdoptionPlan(
        EngineHost host, int scheduleIndex, World previousWorld,
        World replacementWorld, long hostTimestamp,
        long timeScaleSegmentSequenceAtPause,
        SimulationClockSegment scheduleSegment,
        WorldInputTimelineAdapter.WorldReplacementAdoption[] inputAdoptions);
    internal void Apply(EngineHost host);
}
```

`WorldLoadPlan.Initialize` alone constructs its execution context before the plan becomes available, with task/delivery/cleanup control storage funded by the existing finite allowance. It does **not** preselect a committing persistence service: `BindCaptured`, called only by public commit after successful capture, binds the actual committing service and exact returned lower operation. Reference/generation identity must match the prepared capability. Capture-first binding succeeds even if the plan wrapper has since closed; it does not require an open wrapper/domain or allocate another interest.

`WorldLoadPlan.Dispose` directly closes the lower acquisition route, then inspects that shared captured state. If captured, it cannot dispose the execution/state used by accepted work. If uncaptured, it disposes higher data and lower unused ownership outside gates. Remove `WorldLoadPlan.TryCaptureCommit`, `WorldLoadPreparedState.TryCaptureCommit`, the higher `_commitSourceUse` ownership and competing acquisition flags. Higher construction-plan references become borrowed after capture and are cleared after use; higher cleanup no longer releases transferred construction grants.

Closing acquisition itself does not end P/interests or release the sealed source use ahead of higher cleanup. For an uncaptured plan, `WorldLoadPreparedState.Dispose` first joins/disposes its prepared author consumers and closes the source owner; only then does the existing `WorldConstructionPlan.ReleaseUntransferredRestoreResources()` end the preowned unused operation/source use and construction ownership. Extend that existing lower cleanup to own those unused controls, retaining debt on failure. The same ordered unused-plan helper is used during preparation failure before a plan escapes; it cannot end captured ownership. After capture, higher state disposal closes only its own data/author roots, and the operation remains responsible for construction/candidate/commit cleanup. This removes the current early `ReleaseConstructionResources` ordering rather than introducing a lower callback to arrange the unwind.

The two owner routes are:

```text
WorldPersistence.CommitLoadAsync(plan, callerToken)
  entry/mode/owner/service checks; pre-admission cancellation; BeginLoad
  -> plan.PreparedReplacement.TryCaptureCommit(out operation, out refusal)
  -> plan.Execution.BindCaptured(this, operation)

  standalone:
    -> WorldPersistence.CommitLoadAtOwnerBoundary(execution)
       -> execution.ExecuteAtOwnerBoundary()          [entire lifecycle below]
    -> execution.CompleteAfterOwnerBoundary(receipt)
    -> return execution.Completion

  hosted:
    -> EngineHost.TryEnqueueWorldReplacement(execution, out completion)
       -> EngineHost+HostWorldReplacementRequest.Execute(EngineHost)
          -> execution.Persistence.CommitLoadAtOwnerBoundary(execution)
             -> execution.ExecuteAtOwnerBoundary()   [same entire lifecycle]
          -> host.AdoptWorldReplacement(execution.Adoption) iff Published
          -> execution.CompleteAfterOwnerBoundary(receipt)
    -> return the typed request/execution completion
```

The complete executor, called only by the Hosting owner-boundary member, performs:

1. Lower `TryCreateCandidateAtOwnerBoundary`. Retain only a borrowed candidate until publication; the lower operation owns unpublished cleanup.
2. `state.RestoreCandidate(candidate, CancellationToken.None)`: existing Scene preparation, ECS Persistent/Derived/resource/system/attachment restoration, actual Scene registry/hierarchy installation, clock/control restoration and the selected existing M05 derived-only join in their required order. Remove its direct factory call and old higher playback construction. Join preparation tasks and revoke every scoped binding in `finally`.
3. Perform the existing actual Integration capture and higher World-record read under `candidate.AcquireRestoreValidationCaptureLease()`. Keep the fresh record reservation/lease through step 5. No second source Read for cached registered authors and no saved-Scene-byte substitution.
4. Build the independently owned source-bound remap from these validated source/fresh histories using `operation.MappingCredit`. Prepare all receipt/retirement delivery storage. For hosted replacement, obtain the timestamp and call the existing `EngineHost.PrepareWorldReplacementAdoption(World, World, long)` to create the exact typed plan. Finish every source-reading remap/provider operation here.
5. Lower `PreparePublicationAtOwnerBoundary(in lease, freshRecord, freshHistory)`: final comparison, marker and single-use playback transfer. Release fresh capture/record ownership afterward; no subsequent source reads, restoration, canonical edits or fallible host preparation.
6. Lower `PublishAtOwnerBoundary`: final exact-target/stamp validation and atomic publication, or bounded refusal. Format public rejection/receipt facts outside gates. No target, candidate or operation is recovered from GUID equality.

Replace the old `WorldLoadPreparedState.ValidateRestoredCandidate(World)` entry rather than invoke it and then manufacture a validation token: its current implementation disposes the capture lease before returning. Its higher capture/read work belongs inside the complete executor's steps 3-5 so the lower preparation sees the still-live lease.

Hosted adoption is done by the typed request, not the complete executor or Entities. Only the existing host preflight method constructs the data plan, now binding its exact host, old/new World, schedule index, timestamp, prepared segment and adapter edits. Only the typed request calls host adoption; only that method calls `Apply(EngineHost)`, which validates those exact bindings and consumes the plan once before installing the prepared edits. Its existing concrete adapter `WorldReplacementAdoption.Apply()` operations perform no dynamic provider invocation. No other request or simulation tick can intervene in the owner loop.

`CompleteAfterOwnerBoundary` performs separately reported old-World retirement after successful hosted adoption (or immediately for standalone), joins/releases higher prepared state and ends the remaining lower commit ownership only after actual consumers/cleanup quiesce. It completes the public Task outside gates. Failures after publication keep the receipt `Loaded` and fault `OldWorldRetirement`; an impossible host-adoption invariant failure must also stop the affected host owner route, not resume ticking the old schedule or turn publication into rejection.

Queue-full uses `RejectOwnerQueueFull`, with no candidate creation and the existing rejection diagnostic; host shutdown/other pre-execution failures use `FailBeforeOwnerBoundary` and the existing fault channel. Both join actual higher cleanup before ending captured ownership. `BeginLoad`/`EndLoadOperation` service accounting stays in the Hosting context exactly once on every exit.

## 5. State, acceptance, cancellation and ownership

| Lower phase | Entry / permitted next action |
|---|---|
| Prepared-unsealed | Preowned control only; no capture, candidate or owner issuance. Seal the exact source inputs once. |
| Prepared | Public capture or acquisition close. Close-first rejects capture and cleans unused ownership outside gates. |
| Captured | All-or-none independent commit authority; queue, bind the explicit higher context, abandon before execution, or begin on the exact owner. Wrapper closure cannot revoke it. |
| Candidate | One factory result; higher restoration/capture/preflight only. No public visibility. Refusal/fault unwinds after higher use joins. |
| Publication-prepared | Fresh complete comparison/marker ready, source transferred to candidate playback, compared revision fixed. Only terminal publish or abort; no more source reads or canonical writes. |
| Published | Binding/marker/default-domain handoff committed once. No abort or second publication. Higher retirement/delivery remains independently owned. |
| Rejected / Abandoned / Faulted | No replacement published. Invalidate the attempt, join higher users, retire the unpublished candidate if any, then end only actual released ownership. |

Cleanup is a separate ownership condition, not another success phase: remaining storage/consumers may be live, released or failed-retained after a terminal decision. Invalidating a route does not refund its resources.

Public argument/disposal/mode/owner checks and caller cancellation precede capture. Keep process-registry → exact-domain → binding lock order. No allocation, callback, diagnostic/result delivery or Task completion occurs under those admission/publication gates. All accepted preparation/restore/capture/cleanup calls use `CancellationToken.None`; later caller cancellation affects no admitted operation.

External-domain closure before capture returns existing domain-closed rejection with no candidate/owner issuance. Capture first may finish under the prepaid grants; domain closure alone cannot make accepted grants stale. Independent target disposal/revocation remains stale-target, checked before issuance and at publication. Before issuance no owner is consumed; afterward the owner is burned and the unpublished candidate unwinds. Two plans targeting the same old binding never authorize replacing the first plan's same-GUID successor.

Capture moves independent `S_c/T_c/D_c`, process commit authority, candidate/cleanup allowance and applicable H/handoff into the lower operation without a membership gap. Candidate `C_o` and lifetime grants transfer through its factory. `S_c` transfers once into playback after actual source-use quiescence; `S_o` stays with source data, `T_c` covers actual target comparison and `D_c`/associated P cover accepted execution and cleanup. Remap retains only its existing separately charged mapping use. It contains copied identity facts, not source buffers or World authority.

No phase creates a fourth source interest, independent active-binding U on an unpublished candidate, new admission ceiling, caller-configured demand, new queue or hidden worker. Every new control/result and its actual invoker-return suffix must fit the original finite preparation/candidate/cleanup/delivery allowance before allocation. If a selected lane lacks that proof, refuse that lane; do not lower P or borrow Scene command credits. Do not end credit merely because `ExecuteAtOwnerBoundary`, a Task or a wrapper appears terminal: retain it through the actual consuming invoker/cleanup return, or in the existing quarantine if release is unproved.

## 6. Distinguishing end-to-end witness and bounded work

Use the real Standard Branch Save → PrepareLoad → CommitLoad path for a checkpoint containing one restored Scene with A and B, an edited saved hierarchy/reference to B and registered owner state. Prepare two plans against the same target. Candidate and old World keep the same semantic WorldGuid/Entity GUIDs but have different process owners.

Plan 1's public capture succeeds. Dispose its wrapper and close the external domain afterward: the accepted context still reaches the owner. Only its lower operation creates the candidate/map; higher Scene restoration installs the saved edited hierarchy into that candidate's registry. Fresh Integration capture includes the actual restored Scene and registered state, not original package expansion or copied saved Scene bytes. Host preflight/remap preparation completes before lower comparison transfers source use and `S_c`. The lower publisher swaps once; the typed request adopts the exact schedule/input plans before another tick. Old cleanup failure is separately reported without undoing `Loaded`. Playback remains valid after old binding/service closure.

Plan 2 cannot retarget the successor: if captured before closure, it rejects on the now-stale exact binding before candidate issuance; if capture is attempted after external closure, it rejects at that admission boundary instead. A missing restored Scene, mismatched fresh-history bytes, canonical mutation after comparison or foreign operation/source use faults/refuses at its specified boundary and cannot publish.

This is a future real-consumer witness, not evidence that the present guarded Scene composition can run. Preserve `PreparedScene.QualifiedFootprint == null` and the nonempty resource-preparer restore-plan refusal. Existing Scene/Assets funding, cleanup and actual-return proposals retain their gates. Empty-Scene production tests can exercise the lower routing/closure protocol without pretending to qualify the blocked Scene witness.

Implementation batches are serial:

1. Apply this exact internal actor/signature decision to the owning Chapters 27/29/30; enroll every new direct edge above, retaining the already enrolled handle resolver. Do not accept the previous complete lower-execute row and this split as competing authorities.
2. Implement lower sealed-input/preowned capture, operation phases/outcome/stamp and data-only playback; consolidate acquisition state. Move only the pure comparison helper downward.
3. Integrate the typed Hosting context, complete executor and explicit queue/adoption context. Remove the unauthorized higher factory call and duplicate old operation/playback/capture routes.
4. Validate the coherent unit with the smallest owning Runtime Debug selection, then applicable Release: exact production binding/caller inventory; capture/dispose/cancellation/queue/closure races; stale before/after issuance; complete actual restore/fresh capture; wrong history/revision/source refusal; one-use source transfer and prepublication cleanup; independent remap/playback; schedule/input preservation and separately reported old retirement.

Start with `TerraFX.Runtime.Tests.Hosting.WorldPersistenceCommitTests` (file `EngineHostPersistenceCommitTests.cs`), `WorldPreparedRestoreSourceTests`, `WorldLoadPlanTests`, `WorldIdentityRemapTests` and relevant Entities restore/Scene-gate tests. Confirm nonzero selected/executed membership; record unsupported Scene variants as blocked, not passes. Use the SDK and ordinary commands in [Building](../project/Building.md); no new qualification runner or test-only actor. No build/test execution or native/AOT/milestone acceptance is claimed here.

## 7. Exact input freeze

Inspection HEAD is `5e48cffbda10f9458577c385c81f66af2db72107`. Working-tree hashes, including dirty inputs, are authoritative for this proposal; no source or maintained contract was changed. Relevant dirty inputs remain Chapters 04/12/16/30 and the Runtime/test project files. No schema/fixture bytes, wire version or acceptance membership is changed.

For each manifest below, emit one `repository-relative Windows path|uppercase SHA-256` row per file, sort rows using `StringComparer.Ordinal`, join with LF including a final LF, encode UTF-8 without BOM and SHA-256 the resulting bytes. The exact counts freeze membership as well as contents.

| Set | Exact membership | Files | SHA-256 |
|---|---|---:|---|
| Authority | All immediate `docs\design\*.md` plus the fourteen paths below | 52 | `7E66D6089468E46D5997646D0AD52CE25B9539A15B8A09CA51DBA273D510E03E` |
| Runtime | All `*.cs` recursively under `sources\TerraFX.Runtime`'s `Entities`, `Hosting`, `Scenes`, `Integration`, `Animation` folders | 643 | `598C494404A67EC2DCFC6D69175695727A5B6A7F6447AE07631ABCF78DBAA25F` |
| Tests | All `*.cs` recursively under `tests\TerraFX.Runtime.Tests`'s `Entities` and `Hosting`, plus its `Scenes\SceneAssetStoreTests.cs`, `Scenes\SceneAssetStoreTests.Persistence.cs`, `Scenes\SceneRestorePreparationGateTests.cs` | 123 | `223D74C01BB23DE9E565D2C95D7662F8B696AA1B1FBA3EAAF1A8DB861F6E67B8` |
| Proposals | All immediate `docs\proposals\*.md`, excluding this new file | 79 | `1164417EF2F2E95C39C3A65B2AFF7FC8F2162BEDE51169BBD6FD09A69F3CC4ED` |

The fourteen additional Authority files are:

```text
docs\project\Roadmap.md
docs\project\Building.md
.github\copilot-instructions.md
.editorconfig
global.json
Directory.Build.props
Directory.Build.targets
Directory.Packages.props
sources\Directory.Build.props
tests\Directory.Build.props
tests\Directory.Build.targets
sources\TerraFX.Runtime\TerraFX.Runtime.csproj
tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj
.github\workflows\ci.yml
```

Principal individually addressable inputs:

```text
docs\proposals\m03b-world-restore-operation-owner-alignment.md|C555D4B65196A6560828213F6076DD99BF52898219C8A3C0D780482A25E5C2D5
docs\design\04-capacity-and-workload-profiles.md|A4D86F3FA373A64548435C53582BF5916E29D77F34650F9D161CCEC90E6F0245
docs\design\12-entities-components-and-queries.md|174B2465746DA79CEF560C8506216EF4F99F9D1AABAC184F872C9E6CB0B94E0B
docs\design\16-scenes-transforms-and-cameras.md|B012F42B23CEDE9C7CDB71203D8357CD19E1626823EA25EF3119961292D3698A
docs\design\27-state-serialization-replay-and-recovery.md|11DB22E4E1DFCB8798A64CBDD8B036DBBE99BD27D01A975E34C323215E855AD0
docs\design\29-project-setup-build-and-deployment.md|A39A8D7C67F311AC86E9CD604DAF85DF5AD283957A10E0719939C2420A907FAA
docs\design\30-public-api-review-surface.md|6668E34C24AA09C4DA1197C6A6166712B42E102EFB6EFC4407B09B080C943211
sources\TerraFX.Runtime\Entities\WorldConstructionPlanControl.cs|7EBD30AD74D1BF889BD342CD395FB58217CDBF059309C153803EA5D2943D4744
sources\TerraFX.Runtime\Entities\WorldPreparedReplacement.cs|E14363D4707B8F3D3F92AB8241EC4D29B256DD2B9DB4F0D746DCD530BDC3FC23
sources\TerraFX.Runtime\Entities\World.PersistenceReplacement.cs|42F902FCD6D3FB719FAEDF462BC4522CE3F7639EC7D168AA95A2C27629AC9F3A
sources\TerraFX.Runtime\Entities\WorldRestoreSourceControl.cs|C094211BFEC39974407D722516BA6652948E7D5A3B5B0C5B68FC2004AA9DEC2D
sources\TerraFX.Runtime\Entities\WorldPersistenceCaptureLease.cs|A956909FD264B6A15EBE85DF94CDC527710B7AF5AB24CD4624B584F4E34CEA96
sources\TerraFX.Runtime\Entities\ValidatedWorldIdentityHistory.cs|0FD4EDECAD91BE775B039BBE9E9615AF5AA528D829FB70D02FEC9DE8AC234CCD
sources\TerraFX.Runtime\Hosting\WorldLoadPreparedState.cs|533D03EF8FA2292AEE2B0CE9F48A8C5EA19CFF16B4ADA1A8422C2DFC97EDB25A
sources\TerraFX.Runtime\Hosting\WorldLoadPlan.cs|67CA5C8BA0C9666C71657EBAD1184DFD93924D30AD88FA35A5C6E99877801B9A
sources\TerraFX.Runtime\Hosting\WorldPersistence.cs|465614C0943947AD6FDFAD38C5920A0B4736310EB0F6E8118F5518CD76B3264B
sources\TerraFX.Runtime\Hosting\WorldPersistenceRestoreValidator.cs|2793238C45A349F3A0A7796EDA27A0D3D6C0D0F2346CE3C1A33BB65957A2EA13
sources\TerraFX.Runtime\Hosting\WorldPlaybackControl.cs|B863CAE1E03EE1B54CD1FBC9A665237FD4435409B1BDE552793F8041E73EA413
sources\TerraFX.Runtime\Hosting\WorldRetainedHistoryControl.cs|67FA48F8D29AC05C39D2C974F7BB9206EB94B3E8DDC205B0684D5AF320852FC4
sources\TerraFX.Runtime\Hosting\WorldIdentityRemap.cs|08C3735287E515762F3C0609B5660B08F4E7351474F924DF5CAACE69439BA40B
sources\TerraFX.Runtime\Hosting\EngineHost.WorldReplacement.cs|2698F54CB5ABB8E90110E84A7D93C6B4FEFB32285C67403D30DB9D4A95C05647
sources\TerraFX.Runtime\Hosting\EngineHost.cs|A3FB2415E31CB7B717F2AD382E1FACF874B6AC392A469B6E71BA198DF6A11743
sources\TerraFX.Runtime\Integration\WorldPersistenceCaptureCoordinator.cs|4608EA6CB3D8EFE939683048D73D249F4F8FD9FEE57EE37B3AA044894531940C
sources\TerraFX.Runtime\Scenes\ScenePersistenceRestorer.cs|B60C36D55EDD691A6C8442DB22F9A7995470F4FCE006D7578B24088A6496B508
sources\TerraFX.Runtime\TerraFX.Runtime.csproj|2F1C81E54F0FBF12D01E34D0D840885F5EB0E94A8B8890865EF721B8C6A3C65D
tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj|B8DFBC8438BD32A93ACB54DD420AD09A7BF259C2DB5D366D2100229D066CF583
```

Verify HEAD, all four memberships/digests and these direct hashes before disposition. Changed bytes require reassessment; the previous proposal's historical freeze is not a substitute. File/hash verification is not build/test or acceptance evidence.
