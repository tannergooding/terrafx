# M05 diagnostic owner-queue admission addendum

## 1. Tentative decision

**Tentative implementation unblocker, pending review. Queue byte admission and the complete 32/16-MiB diagnostic gate remain NO-GO.** This resolves only the queue-route choice left open by [recording workspace admission](m05-skinned-normal-diagnostic-recording-workspace-admission-addendum.md), section 4. It does not declare the remaining workspace available.

Select a **private, nonwaiting diagnostic handoff into the existing Graphics owner request ordering**, backed by qualified queue-owned storage and a preclaimed request/control grant. A diagnostic request that cannot enter immediately without unreserved allocation refuses the whole diagnostic-bearing graph. Do not silently fall back to `Channel.Writer.WriteAsync`, add another accepted waiting queue, change general request waiting semantics, or introduce a public Try/budget API.

Preserve the **33,554,432-byte joint preparation/recording ceiling**, independent **16,777,216-byte completion ceiling**, draw ceiling, exact command/barrier schedule, four Frame bindings, packet ranges, complete membership, native completion, cancellation and quarantine contracts. Preserve the implemented [Core completion handoff](m05-skinned-normal-diagnostic-core-completion-handoff-addendum.md); neither its byte grant nor a free frame/count slot funds queue controls.

The current use-owner batch is independent: its profile-bound `GraphicsRecordingUseAdmission` covers the lease/node cohort; recording acquisition uses nonwaiting resource-gate entry, and the active-use tree retains the actual lease/admission until release. This does not qualify owner-queue storage or request/TCS/task construction.

## 2. Source facts and the missing capability

| Current owner | Established fact / unresolved row |
|---|---|
| `GraphicsOwnerRequestQueue` | `_requests` is a bounded channel with capacity 256 and `FullMode.Wait`. `WriteRequestAsync` awaits its writer, then signals the owner. The capacity is a count bound, not an exported managed backing/capacity profile or producer-waiter byte grant. |
| `GraphicsDevice.RecordPass.cs` | Pass metadata and known array minima are checked before request construction. The request constructs a TCS, and the async operation can subsequently wait on the channel. Root cleanup and array/use subgrants do not establish those request/write-control allocation charges. |
| `GraphicsDevice.SubmitGraph.cs` | Submission has a distinct request, resource-use transfer and frame-slot protocol. Its queue admission must independently consume the correct joint-workspace subgrant; recording admission is not submission admission. |
| Native/uncertain terminal paths | A faulted/canceled observer or released count slot does not end a still-retained request, submission or quarantine interest. Existing quarantine container growth/reference copies are separately unqualified, not free consequences of queue acceptance. |

The missing capability is **queue-owner-authoritative preallocation/admission and actual ownership transfer**, including the exact shared queue backing affected by an enqueue. Public `Channel<T>` operations do not currently expose that private storage contract to Runtime or Graphics callers. Measuring one successful write or assuming that a 256-entry bound means a preallocated 256-reference array does not supply it.

The queue owner must either expose and enforce a qualified implementation of that existing storage/order contract or keep diagnostic queue admission unavailable. Do not size hidden BCL controls from reference width, fabricate a fixed overhead, use reflective access to channel internals in production, or force capacity by enqueueing placeholder work.

## 3. Minimum private owner contract

1. **Queue-owned backing is funded before use.** Identify its actual allocated capacities, wrappers, synchronization/signal controls, initialization, retention and any growth overlap. The queue owns persistent/shared storage; a request owns only its separately identified control grant and attributable incremental storage. Existing already-funded backing is not charged twice. Conversely, an unestablished startup/shared-storage owner is not a zero-cost exclusion or authority to borrow GPU/resource or Core completion allowances.
2. **Diagnostic entry performs no waiting allocation.** A private enqueue consumes a real preclaimed control/storage interest and either atomically accepts that exact work item or returns refusal without retaining it. No waiter, continuation, copied request or replacement backing may be allocated after the check without already reserved capacity. A contended queue-admission gate also refuses if its wait cannot be funded; do not hide a second waiting lane in a lock or semaphore.
3. **Preserve one accepted-work ordering.** Accepted diagnostic requests participate in the same owner execution order as uploads, pipeline/resource work and ordinary graph requests. Existing ordinary writer/retirement behavior, shutdown wakeups and close races remain correct. A second queue or a semaphore/ring rewrite is not presumed order-equivalent; the owner implementation must prove that equivalence before selection.
4. **Request controls have their own qualified subgrant.** Enumerate concrete record/submit request objects, TCS/Task and compiled async controls, callback captures, cancellation registration and any handoff storage for the exact profile. Reserve them before constructing them; allocating a request and only then asking whether it fits is retroactive accounting. Existing array and use-node components remain separately identified parts of the same joint envelope, not extra 32-MiB allowances.
5. **No speculative full-ledger certificate.** Runtime supplies its still-retained joint baseline and the graph's complete qualified recipes/workspace membership before graph return/compile/submission acceptance. Graphics consumes scalar/private grants under the existing dependency direction. An unknown callback, queue control, capacity, copy or retained root refuses the whole diagnostic-bearing graph; it does not omit diagnostics, shrink D or return empty success.

This is a private owner-capability requirement, not an implemented interface declaration. It adds no public signature, diagnostic/rejection ID, general renderer budget, World/Scene callback, new native allowance or arbitrary replacement draw limit. If persistent/shared queue funding cannot be represented under the reviewed ownership scope, resolve that exact scope first; do not invent one inside this batch.

## 4. Exact handoff outcomes

| Outcome | Required ownership and charge |
|---|---|
| Pre-enqueue refusal: full, contended, closed, stale or unqualified | No queue retention or owner execution. Keep established argument/identity/version and cancellation precedence; release safe untransferred interests at actual release. Budget/footprint excess uses the existing contextual failure channel, not a result prefix or new acceptance ID. |
| Atomic enqueue succeeds | Transfer the same work item/control interest to the queue. Do not create a second payload or leave a caller-owned disposal path able to reclaim queued storage. Queue capacity and byte ownership are different credits. |
| Owner dequeues | Transfer the work/control interest to the existing owner execution/request boundary. Reusing an empty ring/count slot does not refund task/request/submission storage still live elsewhere. |
| Recording or preflight safely fails | Unwind safe uses and detach obsolete roots before reporting release. Caller-retained immutable aliases, completed Tasks or graph plans keep their remaining interests; dropping a queue reference is not proof of their release. |
| Native acceptance or uncertain completion | Transfer the applicable grant with the submission and its preadmitted quarantine carrier. Do not credit bytes back on frame-slot release, observer cancellation, task fault or device-loss notification while their actual owner remains. |

Earlier independently accepted resource work is not rolled back by a later diagnostic graph refusal. The nonwaiting route must be available and fully funded before the diagnostic attempt needs it; a post-recording enqueue failure may safely abort an unsubmitted graph but does not excuse missing preparation/return admission.

## 5. Smallest subsequent batch and validation

Implement only the real `GraphicsOwnerRequestQueue` capability and its record/submit handoffs after qualification: persistent backing/control ownership, the private nonwaiting diagnostic entry, request/control subgrants and close/transfer rules. Do not compact commands or redesign unrelated rendering.

Extend the existing owner-queue and pipeline/submission harnesses. Cover an actually blocked/full owner, exact-fit/one-byte-short control grants, no enqueue/waiter/allocation on refusal, accepted FIFO ordering beside ordinary work, concurrent close, safe request cleanup, independently retained Tasks and uncertainty transfer. Confirm actual allocated capacities and compiled control/root behavior; a successful count-capacity or use-node test is not a queue-byte observation.

Run ordinary owning Graphics/Runtime Debug selections, then Release, with nonzero selected/executed cases. Native execution/failure, NativeAOT and public-consumer qualification remain separate. This addendum supplies no queue object sizes or numeric grant and performs no additional queue probe.

**Current disposition:** the use-node component can be implemented/observed independently, but the queue/request-control row, preflight/backend scratch, quarantine carriers, payload aliases and remaining completion ledger still prevent GO. After the current bounded use-owner batch and its validation, pause for review; do not stage, commit or start the queue implementation under this proposal.

## 6. Frozen inputs

Whole-file SHA-256 values below identify this narrow dirty-workspace input set based on HEAD `31f156a51ccc462ae9c5cc8d4e87e00c0054dec4`; HEAD alone is not the reviewed input. Recheck before applying a disposition. These are static source inputs, not queue-size or native qualification.

| Input | SHA-256 |
|---|---|
| `docs\proposals\m05-skinned-normal-diagnostic-recording-workspace-admission-addendum.md` | `BC52399DE52D48A5425BD36D7ECC6D132B7BA3C9D792040F2FA9ABF8BCB24CE8` |
| `docs\proposals\m05-skinned-normal-diagnostic-core-completion-handoff-addendum.md` | `958C9AAC244915B8981A6E7014DDFB948A3F5E6F058E07A84CC4609E93C386FF` |
| `sources\TerraFX.Graphics\GraphicsOwnerRequestQueue.cs` | `E26DD7962CF13A7471128A8D27A2BD6DA9F06C75A4F397A3A6D56291A8511143` |
| `sources\TerraFX.Graphics\GraphicsDevice.RecordPass.cs` | `31B2FC61A39D15EC2CD917E631E8FD5B829AC6BC1D89639A440043D6372387E7` |
| `sources\TerraFX.Graphics\GraphicsDevice.SubmitGraph.cs` | `5C8C30700F7F5CDDEDFA97C38B5B4AE9C48D56C3DA82E21523A8DBD5B9BB9A94` |
| `sources\TerraFX.Graphics\GraphicsRecordingUseAdmission.cs` | `DD1983E585EE56E2BA2E0BA8D66D385C964C1F8C27539413E06BF0DE874B65FE` |
| `sources\TerraFX.Graphics\GraphicsResourceOwner.cs` | `02015FB85F3AC6867C891BE8BDE530D075952D28FC9BBFAAD1341ABBFA482ADB` |
| `sources\TerraFX.Graphics\GraphicsDevice.PipelineRecording.cs` | `9C56F246EEBEF4723DE185A2B984D602D989B4B95868033231D24701C7798FB4` |
| `sources\TerraFX.Graphics\SkinnedNormalDiagnosticRecordingRecipe.cs` | `FCEA487B4B8E92FC46C11FEEF4C8A4080BD96356A8189A2E9A182BE25F0CCF26` |
