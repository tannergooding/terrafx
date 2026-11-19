# M05 skinned-normal diagnostic telemetry route closure

## 1. Selected decision and scope

**Tentatively approved as implementation guidance per user direction; not a finalized owner contract or qualification evidence.**

Use the existing `Diagnostic` / `DiagnosticsService` route for **positive-count, noncoalescing Warning telemetry**. Do not add a public result type, receipt member, emitter, subscription, completion token, diagnostic identifier, or content schema. A successful `SubmitAsync` joins the required readback, validation, and publication attempts; it does not guarantee telemetry retention or provide a complete per-draw result.

This closes the route decision left open by [public carrier resolution](m05-skinned-normal-diagnostic-public-carrier-resolution.md). The suggested filename already contains the earlier [telemetry owner-closure proposal](m05-skinned-normal-diagnostic-telemetry-owner-closure.md); that file is preserved. For this bounded decision, select **no coalescing**, rather than that proposal's producer-specific coalescing key, and leave `CommandIdentity` null rather than adding its compound string encoding. These are selected replacement recommendations for this producer, not two alternative routes.

The minimum owner changes are:

| Owner | Proposed amendment |
|---|---|
| Chapter 20 §3 | Adopt §3's membership, positive-only record, original identities, and post-readback publication boundary. Preserve the existing normal algorithm and independent presentation observation. |
| Chapter 28 §1 | Adopt §4's explicit noncoalescing exception for this existing code, ordinary bounded retention, and visible occurrence loss. |
| Chapter 29 diagnostics ownership | Authorize §5's borrowed service injection, non-emitting source attribution, and Renderer admission/drain order at startup, shutdown, and device recovery. |

Chapter 30's public declarations remain unchanged. The dirty `RenderSkinnedNormalDiagnostic` and `RenderSubmissionReceipt.SkinnedNormalDiagnostics` bytes are preservation inputs, **not an accepted API baseline**. This document neither edits nor authorizes committing them as supported public API. Other proposals, maintained chapters, code, tests, and milestone status are unchanged by this task.

## 2. Evidence supporting this decision

These are source/contract observations, not executed results. Full input paths and their frozen revisions are in §7.

| Evidence | Relevant fact |
|---|---|
| Chapter 20 §3 | Already names `DegenerateSkinnedNormal`, the finite near-zero predicate, dominant-bone fallback, bounded compute counting, asynchronous post-fence readback, and frame/draw tags. It does not require public typed results, observable zero rows, or lossless delivery. |
| Chapter 30 §7, `RenderSubmissionReceipt`; §10, `Diagnostic`, `DiagnosticContextEntry`, `DiagnosticSnapshot`, `DiagnosticsService` | The normative receipt has six scalar properties. Existing diagnostic fields and copied polling can represent the count and correlation without adding declarations. |
| Chapters 28 §1 and 04's diagnostic limits | The ordinary service has at most 8192 retained records, a fixed 8-MiB host ceiling, and per-World attribution sublimits. Copied polling is nondestructive; loss/coalescing counters are service-wide, not vertex counts or submission-completeness signals. |
| Chapter 29, “Diagnostics service ownership and issuer”; `WorldDiagnosticsCapability.cs` | Core owns the service/clock; Hosting owns the host instance. The existing World capability permits only its specified subscription-capacity diagnostic. Rendering receives no authority from Core friendship or possession of a World GUID. |
| `DiagnosticsService.cs`, `FindCoalescedIndex` and `Write` | Current warning matching uses only code, execution-session ID, and World OwnerId. Neither draw, snapshot, submission sequence, tick, nor count participates. Current aggregate eviction already adds the represented occurrence count; the unresolved producer-specific issue is the key, not permission to sum counts. |
| `Renderer.Extract.cs`, `CreateMeshDrawId`; `Renderer.MeshC0Graph.cs`; `RenderMeshC0NormalDiagnosticWork.cs` | Existing snapshot/draw identities and actual color/shadow selection support one frozen logical-draw table. Preparation already sorts/uniques each selected group's referenced vertex ordinals and deduplicates camera/shadow uses. These facts do not accept the implementation's public result. |
| `RenderMeshC0NormalDiagnosticGraphPass.cs`, `CreateResults`/`DecodeResults`; `Renderer.Submit.cs`, post-submission readback | The current path obtains actual readback bytes after the graph result and validates row extent, frozen mapping, tested count, count range, invalid-evaluation lane, and reserved lane before returning decoded results. Attaching the public array is separable from that private validation work. |
| `Renderer.CpuExtractionSupport.cs`; `EngineHost.cs`; `EngineHost.GraphicsRecovery.cs` | Renderer currently receives only device/residency. Initial construction precedes Diagnostics initialization; Interactive startup and recovery construct further Renderers. Recovery preserves the graphics OwnerId and increments DeviceEpoch. Existing shutdown does not explicitly join Renderer publication continuations before closing Diagnostics. |

**Distinguishing design example, not a consumer observation:** one submitted snapshot contains two distinct skinned draws at one World tick, with validated counts 3 and 9 out of 12 tested vertices each. The current generic warning key can retain the first payload with `OccurrenceCount=2`. Under this proposal there are two independently identified records, if retained. Neither the first count nor the service counters becomes 12. Resubmitting the same immutable snapshot also produces a distinct observation through `SubmissionSequence`, even when its counts agree.

## 3. Chapter 20: exact producer behavior

### Membership and zero behavior

For each distinct skinned Mesh `DrawId` referenced by the compiled built-in color or shadow work, count each distinct vertex ordinal in that draw's exact admitted group index slice once. Require its captured retained palette and valid nonempty group. A draw used by several cameras/cascades/spot shadows contributes once; a vertex shared by different logical DrawIds belongs to each. Exclude unselected draws, unreferenced vertices, Sprite/custom draws, triangle-corner multiplicity, raster invocations, and pixels. This closes the current admitted geometry path; it does not authorize an absent animated or LOD producer.

Use the actual captured packed normal/weight/palette inputs. Preserve Chapter 20's finite `lengthSquared < 1e-12` test before instance inverse transpose/final normalization, dominant-bone fallback and lowest-joint-index tie break. Equality does not increment the count. Singular/nonfinite palette admission and invalid GPU evaluation remain failures, not zero counts.

Validate every GPU row, including zeros, before publishing any warning from that readback. A verified zero emits **no record** and increments neither loss nor coalescing counters. No eligible draws require no diagnostic pass/readback. Absence from polling is not proof of zero, coverage, completion, or successful execution.

### Existing record fields

| Field | Required value |
|---|---|
| `Code` | Exact existing literal `DegenerateSkinnedNormal`. No suffix or new identifier. |
| `Severity` | `DiagnosticSeverity.Warning`: the fallback is recoverable. Do not change severity merely to bypass coalescing. |
| `Operation` | Exact literal `Renderer.SubmitAsync`. |
| `Message` | Literal `Skinned mesh vertices required the dominant-bone normal fallback.` Counts belong in structured context. |
| `MonotonicTimestamp` | Core `DiagnosticClockDomain` timestamp sampled at publication after validation, not raw Stopwatch ticks, GPU time, or extraction time. |
| `HostId` | Actual nonzero host identity supplied by Hosting from `PlatformHostOwner.OwnerId`. Do not assume it equals the graphics owner. |
| `DeviceId`, `DeviceEpoch` | Exact submitting `GraphicsDevice.OwnerId` and original completed graph's DeviceEpoch. Hardware `Capabilities.DeviceId` is not this operational identity. |
| `WorldGuid`, `Tick` | Submitted snapshot's WorldGuid and TickIndex, not the current/replacement World's values. |
| `SurfaceEpoch`, `SwapchainEpoch` | Original graph result's positive values for a surface submission; null for offscreen. Do not copy offscreen receipt zeros into nullable diagnostic epochs, whose constructor rejects present zero values. |
| `SurfaceId` | Null: this count needs no new scalar surface-ID issuer. Device/submission correlation is sufficient; do not substitute a window/handle slot. |
| `SystemId`, `PassId`, `AssetId`, `CommandIdentity`, `NativeResult` | Null. This is a logical draw across passes, not an issued user command, single pass Guid, native error result, or globally identified asset. |

Producer context has exactly these six entries, in order:

| Key | Representation |
|---|---|
| `SnapshotId` | Captured nonempty `snapshot.SnapshotId`, lowercase Guid `"N"` format. This is the diagnostic's render-frame tag, not a new FrameId, Sprite frame, or spatial-frame revision. |
| `DrawId` | Existing nonempty captured DrawId, lowercase Guid `"N"` format. For Mesh groups its existing derivation is NameUuid of the persistent Entity ID and `mesh-group/` plus the eight-digit lowercase hexadecimal group index. |
| `SubmissionSequence` | Actual positive sequence returned by Graphics for this graph submission. |
| `CompletedFence` | Actual positive original graph fence, not the later readback fence. |
| `TestedVertexCount` | Validated positive count of unique referenced ordinals, within the admitted Mesh vertex-count domain. |
| `DegenerateVertexCount` | Validated positive count no greater than `TestedVertexCount`. |

Integers use invariant decimal digits, without sign, grouping, whitespace, or leading zeros. Sequence/fence values retain their positive `long` domain; counts must fit the actual admitted membership, never wrap, clamp, or saturate. Keys are ordinal and unique. Core alone appends reserved `OccurrenceCount=1`; the producer never supplies it.

The stable observation identity is `(DeviceId, DeviceEpoch, SubmissionSequence, SnapshotId, DrawId)`. It distinguishes resubmission and recovery without inventing session IDs or repurposing the existing World/session operands of Core admission. All tags are historical data, not authority to access a live owner.

### Publication point

After graph completion, await the exported buffer's actual readback transfer fence and CPU copy using noncancelable internal completion after graph acceptance. Validate the entire frozen row table first. End borrowed read windows before constructing/publishing records from copied private scalar data; do not expose GPU/Core readback storage through Diagnostics.

Attempt each positive row exactly once, in frozen ascending RFC-byte DrawId order, on the ordinary managed completion path, outside World/host/native recording gates and the render-thread callback. Use bounded operation-owned workspace; do not detach publication, add a producer queue, synchronously stall the render thread, or reread live ECS/palette state.

All publication attempts and their cleanup finish before `SubmitAsync` becomes successfully terminal, while retaining its normal GPU/pin completion obligations. A later cleanup failure does not retract truthful already-published telemetry. Failed readback or row validation, including device loss preventing completed readback, faults the existing task with its existing context and publishes no count prefix; it never becomes a zero or missing-producer success. Ordinary telemetry admission refusal instead follows §4 and does not invalidate a valid GPU submission. Cancellation and the independent presentation observation retain their existing contracts.

## 4. Chapter 28: coalescing, retention, and visible loss

**Make this existing code an explicit noncoalescing Warning exception.** Core must not use the generic World-warning key, sum payloads, replace counts, or merge distinct submissions/draws. Renderer already attempts each positive row once, so a new compound coalescing key and duplicate-history cache provide no required behavior. Other warnings, especially `WorldSubscriptionCapacityExceeded`, retain their existing rules.

Keep the existing service-owned occurrence metadata at one for every retained row of this producer. These rows add nothing to `CoalescedCount`. Each known-positive publication refused by slot/byte/format-workspace admission adds one to saturated `DroppedCount`; each subsequently evicted row adds one. Zero suppression is not loss. Existing aggregate-aware loss accounting for other warning families remains unchanged.

Use ordinary bounded admission, not the independent critical-fault slot. Charge complete retained records, strings, context backing, and accounting controls against the existing host ceiling and exact source-World sublimit; ring-slot reservation alone does not establish that bound. Reuse normal eviction/refusal, with no waiting on another service or quota increase. A byte refusal must be observable without allocating another unbounded diagnostic. Preserve Chapter 28's counter-saturation behavior.

`DroppedCount` and `CoalescedCount` remain cumulative service-wide counters even for filtered queries. They do not identify which draw was lost, count vertices, or establish that a particular submission was fully retained. Query limits/filters can omit retained rows without constituting a drop. Copied snapshots remain nondestructive and immutable; repeated polling may return the same row. No replay cursor, retry guarantee, per-code loss counter, or complete-result claim is added.

## 5. Chapter 29: exact authorization and ownership

### Borrowed publisher and source attribution

Authorize only the Renderer completion producer above to use the host's existing Core service for this code/operation. Extend the private/internal Renderer construction path with the borrowed `DiagnosticsService` and copied host identity, bound to its exact device/residency generation. Renderer cannot close the service or use this grant for arbitrary diagnostics. No Hosting object/delegate, service locator, new friendship, public emitter, or Core/Graphics-to-Runtime dependency is needed. Graphics continues to supply lower-owned bytes/completion only.

**The current World capability and snapshot metadata cannot supply retained-byte attribution after World replacement/disposal.** The smallest additional ownership amendment is a Core-owned, internal, **non-emitting diagnostic attribution account**, separate from `WorldDiagnosticsCapability`:

- The existing admitted World-diagnostics construction path creates one funded account for the configured `MaximumDiagnosticBytes` and binds its actual execution-session ID, World OwnerId, and WorldGuid once the owner is issued. Failed construction releases its acquisitions. Equal semantic WorldGuids never create interchangeable quota accounts.
- At the valid owner/frame-read boundary, extraction retains that exact account with its snapshot; accepted submission acquires its independent interest before its first diagnostic allocation/await. This handoff grants only byte attribution, not World access, emission, service closure, lineage, or World-use authority. Rendering never receives the World diagnostic capability.
- The same account follows source snapshots submitted through another Renderer/host, including snapshots from a standalone World. Its attribution total spans records retained across services. Source World closure/replacement does not reset outstanding charges or require completion to call a disposed World. There is no GUID lookup, fabricated fresh quota, or host-only reclassification to escape a World sublimit.
- Service entries retain the account solely for debit/refund and release that charge on eviction/closure. Public Diagnostic objects and caller-owned snapshots retain no account, World, GPU resource, or publication interest. The account has only bounded Core scalar/accounting state, no owner/service callback or global registry; fund its control and transfer overlap through existing owning budgets. Its acquisition/retirement does not borrow M03-B receiver credits.

This account is a proposed internal amendment, not a claim that the current service already implements it. Together with the already-required full-record admission, it closes the ownership rule without adding public declarations or a separately spendable allowance.

### Startup, recovery, and close order

| Transition | Required ownership rule |
|---|---|
| Native host startup | Create the host Diagnostics service and issue the actual host identity before constructing/publishing its native Renderer binding. Apply this to OffscreenNative as well as Interactive; fixing only the later Interactive construction misses the early Offscreen instance. Startup success requires the actual selected services, not placeholders. |
| Headless | Standalone `Renderer.CreateHeadless()` remains service/device independent and cannot submit GPU diagnostics. A host Headless Renderer needs no GPU publisher; do not create an auxiliary diagnostics service or zero records. |
| Admission/drain | Acquire a bounded diagnostic-operation interest atomically against Renderer stop admission before preparation/first await; hold it through readback, publication attempts, and finally cleanup. Preserve the current nonwaiting refusal of a second diagnostic attempt. Add no waiting producer queue or public lifetime API. |
| Device recovery | Close old Renderer admission and join its admitted CPU publication work as well as the existing residency/native retirement before replacing the device. Valid old results publish only with old captured tags; failed old readbacks fault normally. Construct the replacement Renderer with the same host service/host identity and actual replacement device/residency/new epoch before publication. Never mutate old publisher state into the replacement. Retained rows and service counters survive recovery. |
| Failed replacement/startup | Stop and drain only the newly acquired publisher's work before releasing its resources. Do not close a service borrowed by other live producers or revive the lost Renderer. |
| Host shutdown | Close Renderer admission before stopping its prerequisites; join accepted publication/residency work while Diagnostics/device prerequisites remain usable, then retire native consumers and hosted Worlds before closing Diagnostics last. Use existing host pumping where necessary; publication must not require a callback on an owner blocked joining it. |

A native fence or residency drain alone does not prove the CPU publisher ended. If native work is quarantined but publication has definitely ended, its publication interest may retire independently. Otherwise retain the service/control ownership and report shutdown failure; catching a late `ObjectDisposedException` is not an ordinary telemetry drop. These are lifetime requirements for this route, not a new resource-recovery algorithm.

## 6. Implementation and evidence boundary

The user's tentative approval authorizes proceeding with this telemetry route without waiting for another carrier decision. Implement its private owner-contract amendments and adapt the existing readback/validation path, Core admission policy, source attribution, and host wiring; do not promote the preserved public result candidate or treat the proposal as qualification.

Managed regressions should distinguish two submissions at one tick, zero suppression, exact context/source identities, per-World byte refusal, immutable polling/loss, and startup/recovery/drain races. Such tests establish only the managed behavior they execute. They do not prove GPU production, an animated-palette consumer, complete byte ledgers, NativeAOT, or milestone acceptance.

Chapters 32/34/35 retain their real-producer and native/public-consumer requirements. Current static BindPose extraction is not a live animated palette; the Scene preparation guard and existing diagnostic/whole-graph budget qualification remain separate existing work, not new route alternatives. A positive native observation must use a real committed World, extraction, graph, GPU readback, and public Diagnostics polling. No fabricated snapshot, CPU-produced count, internal injected row, compilation-only sample, or helper test may stand in for that observation. No builds, tests, shader executions, native/AOT runs, or acceptance runs were performed for this proposal.

## 7. Frozen review inputs

The bounded review set is this proposal's reviewed bytes plus the inputs below. `C` means the exact clean blob at commit **`857c60f914ca852c1405d8714f1002d688cce7a3`**; `M`/`U` identify workspace bytes by SHA-256 below, captured on 2026-11-19. This freezes the decision inputs, not unrelated concurrent work. Recheck the named blobs/hashes before applying a disposition; changed relevant bytes require a new freeze. No schema or protected literal fixture is an input or changed here.

| Input | Freeze |
|---|---|
| `.github\copilot-instructions.md` | C |
| `docs\project\Roadmap.md` | C |
| `docs\design\17-graphics-device-and-resources.md` | C |
| `docs\design\20-animation-and-deformation.md` | C |
| `docs\design\21-2d-and-3d-rendering.md` | C |
| `docs\design\28-diagnostics-performance-and-validation.md` | C |
| `docs\design\29-project-setup-build-and-deployment.md` | C |
| `docs\design\35-implementation-roadmap.md` | C |
| `docs\proposals\m05-skinned-normal-diagnostic-public-carrier-resolution.md` | C |
| `docs\proposals\m05-skinned-normal-diagnostic-telemetry-owner-closure.md` | C |
| `sources\TerraFX.Core\Diagnostic.cs` | C |
| `sources\TerraFX.Core\DiagnosticsService.cs` | C |
| `sources\TerraFX.Core\DiagnosticClockDomain.cs` | C |
| `sources\TerraFX.Core\WorldDiagnosticsCapability.cs` | C |
| `sources\TerraFX.Runtime\Hosting\EngineHost.cs` | C |
| `sources\TerraFX.Runtime\Hosting\EngineHost.GraphicsRecovery.cs` | C |
| `sources\TerraFX.Runtime\Rendering\Renderer.CpuExtractionSupport.cs` | C |
| `sources\TerraFX.Runtime\Rendering\Renderer.MeshC0Graph.cs` | C |
| `sources\TerraFX.Runtime\Rendering\Renderer.Submit.cs` | C |
| `sources\TerraFX.Runtime\Rendering\RenderSnapshot.cs` | C |
| `sources\TerraFX.Runtime\Rendering\RenderMeshC0NormalDiagnosticWork.cs` | C |
| `sources\TerraFX.Runtime\Rendering\RenderMeshC0NormalDiagnosticGraphPass.cs` | C |
| `docs\design\04-capacity-and-workload-profiles.md` | M: `A4D86F3FA373A64548435C53582BF5916E29D77F34650F9D161CCEC90E6F0245` |
| `docs\design\30-public-api-review-surface.md` | M: `D4F622D69ED58AB96118C36FBBF4E2D31E062A3CBF3CE3BA1D64309F662FE4ED` |
| `sources\TerraFX.Runtime\Rendering\Renderer.Extract.cs` | M: `274298BFDCEE321141D4138D417807951FCC0EF6BCAC1DFCE2726D5F36829F2D` |
| `sources\TerraFX.Runtime\Rendering\RenderSubmissionReceipt.cs` | M, preservation only: `77120676F289AD7D8891875A783BAFA05E64B7F0317BC09BECC2545AC42E5846` |
| `sources\TerraFX.Runtime\Rendering\RenderSkinnedNormalDiagnostic.cs` | U, preservation only: `E07287C05EA0E4A12F5ADF2FABCDC48B191C2D753B20E2636447CD83F1E2A41E` |
