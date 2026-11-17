# M05 diagnostic task and waiter lifetime addendum

## 1. Focused decision and status

**Tentative implementation guidance, pending review. Request/TCS/task admission and the complete 32/16-MiB diagnostic gate remain NO-GO.** This supplements [request-control admission](m05-skinned-normal-diagnostic-request-control-admission-addendum.md) with the concrete task/waiter construction and retention frontiers now identified in the current owners. It does not replace [owner-queue admission](m05-skinned-normal-diagnostic-owner-queue-admission-addendum.md).

Select **one Graphics-owned submission-control construction interest that already includes its post-acceptance fence-wait control group before queue/native acceptance**. A request/TCS-only grant is insufficient: the current implementation starts additional task/continuation work after native submission. Keep that same interest with the real request/task owner through completion or a qualified uncertain-work transfer. Do not construct a second grant or quarantine carrier after uncertainty and call the first one released.

The existing diagnostic queue entry has no producer waiter; that narrow incremental row is absent by construction. It does **not** make frame-slot waits, retirement-channel waits, ordinary pending writers, request promises, compiled async state, fence workers or shared queue controls zero-cost.

No request/waiter cap, object-size constant, public API, diagnostic ID, shader change, Scene preparation or new producer is selected. Preserve **33,554,432 bytes** jointly for preparation/recording and **16,777,216 bytes** independently for completion, including their required retained baselines. No unused credit transfers between these envelopes.

## 2. Concrete owner and construction frontiers

| Owner/path | Real storage or retention frontier | Admission consequence |
|---|---|---|
| `GraphicsDevice.RecordPass.cs` | `RecordPassRequest` constructs `TaskCompletionSource<RecordedPassCommandList>` and its Task before the nonwaiting entry. The outer `RecordPassAsync` has its own compiled async control and consumes the private promise. | Qualify the request, promise and compiled operation family together. Known command arrays or an allocation-free queue write do not preclaim these controls. |
| `GraphicsDevice.SubmitGraph.cs` | `SubmitGraphRequest` constructs a distinct TCS/Task. `SubmitGraphAsync` transfers uses and validates preflight before constructing that request. | The factory needs its own preconstruction grant; the recording family cannot fund it. Runtime/preflight allocations that already happened remain with their actual owners. |
| `SubmitGraphRequest.Execute` → `AddPendingGraphSubmission` → `StartFenceWait` | After `backendDevice.SubmitGraph`, the owner stores the request in its existing pending list, calls `Task.Run` with a captured backend/fence, and adds a `ContinueWith` signal Task carrying the Graphics device. The request keeps both task references. | A complete preclaim must include worker/capture, continuation and relevant execution-context/scheduler controls before native acceptance. Counting only the two source-level Task fields is not a footprint. Allocation failure here is already a native-accepted/uncertain path. |
| `GraphicsDevice.AcquireGraphFrameSlotAsync` | A real `SemaphoreSlim.WaitAsync` may suspend; its reservation is a count credit, and the outer async operation has independent control storage. | Neither one free frame slot nor the configured slot count is a byte grant for waiting callers. |
| `GraphicsOwnerRequestQueue.WriteRequestCore` | Ordinary requests construct `PendingRequestWriter` only when the ring is full. That node owns Work, TCS/Task, links, queue/token references and the cancellation-registration/async path. Pending producers are not bounded by the 256 **accepted-entry** count. | Do not multiply a guessed waiter size by 256 or impose a replacement cap. The diagnostic route does not enter this path. Shared/general-owner funding is still unqualified, not silently excluded. |
| `GraphicsOwnerRequestQueue.ReserveRetirementAsync` | The separate bounded reservation channel can suspend a reader; its hidden controls/backing are not the explicit request ring. The retirement-work channel is another distinct owner. | Nonwaiting request entry is not authority for these channel rows or earlier resource-preparation operations that use them. |

Cold/runtime initialization and failure/cancellation controls are not proved by a warmed successful allocation observation. Qualifying compiled async controls also requires their real profile and retained execution-context roots; do not assume `ConfigureAwait(false)` eliminates execution-context capture. This addendum supplies no numeric bound for those groups.

## 3. Exact independent backing row

The pending-submission **array backing**, unlike those object/control groups, has an exposed actual capacity:

- The existing Graphics constructor calls `_pendingGraphSubmissions.EnsureCapacity(frameSlotCount)` before publishing the device. Existing supported logical slot counts are **1 through 4**; this change adds no limit.
- `AddPendingGraphSubmission` checks `Count >= Capacity` before `Add`. No later path grows that list. Removing/completing a request or returning a frame slot does not shrink/release its backing.
- `GraphicsDevice.PendingGraphSubmissionBackingByteCharge` reads the real `List.Capacity` and applies the existing conservative array rule `64 + roundUp16(capacity * IntPtr.Size)`. It does not use `Count`, configured frame-slot count, logical buffer length or a request-object reference stride as an object size.
- Runtime passes this owner-reported scalar to its existing known-minimum diagnostic guard before constructing the diagnostic preparation arrays. The backing appears once in that known-component sum. This is a conservative necessary refusal check, **not** a new complete reservation or certificate for the list object and its controls.

Distinguishing example on the selected x64 implementation: a device configured with **one** frame slot retains a list backing capacity of **four**, charged **96 bytes**. Charging the one logical slot would give **80 bytes** under the same array rule and undercount that owner. The backing remains at 96 bytes after a reservation is released/reused. These are CPU reference-array bytes, not native staging or Core readback bytes.

The pending list and its backing remain owned by the Graphics device for their actual managed lifetime, including retained/quarantined device lifetime. No backing credit is returned on dequeue, task completion or frame-slot reuse. Shared/startup control funding remains an open row; the known-component check does not invent that funding.

## 4. Retention and uncertainty still block a complete grant

The present code has useful, narrower root rules:

1. Ordinary pending-writer acceptance clears `Work` while transferring it into the ring; cancellation/closure clears unaccepted work. Cancellation registration is disposed before clearing the writer's queue/token references.
2. Safe submission completion calls `ReleaseWork` and releases uses, recorded commands and its frame reservation. The pending owner removes the request. That is not evidence that every retained Task/control object has ceased to exist.
3. `QuarantineGraphSubmission` currently retains the **submission payload** and a copied **use-lease array** in separate bags. It does not explicitly transfer the `SubmitGraphRequest`, its promise, fence-worker/signal Tasks or a request-control interest. `QuarantineOwnerState` can then remove the pending request.

Consequently, a future grant cannot be refunded solely because the pending-list entry disappeared or the observer Task faulted. A worker/continuation or caller-retained Task can still have an independent lifetime.

The minimum missing capability is a **private, profile-qualified construction/retention plan for the complete actual request/task group**, with:

- scalar planning before its first allocation, including the grant/carrier's own already-funded storage;
- an enforceable link keeping each control interest with the actual retained promise/outer operation/worker, rather than assuming request or Task completion ends all roots;
- the same interest transferred to a preadmitted uncertain-work owner that also retains the relevant request/task roots before the pending owner removes them;
- qualified quarantine publication capacity/copies and a real release condition, with no count-slot refund standing in for control release.

Whether an existing request can serve as that retained carrier depends on qualifying its full group and quarantine publication; this document does not declare either available. Do not add a weak-table side registry, hidden BCL reflection, placeholder warm-up, guessed continuation overhead or a new callback into Runtime/Core to bypass the missing capability.

## 5. Bounded next implementation and evidence

The independent batch accounts actual pending-list backing and extends the existing owner tests:

- Exercise all four existing frame-slot configurations, full admission/recovery, and retained backing after slot reuse.
- Exercise full/closed/insufficient-backing diagnostic refusal with no accepted-membership change, no pending-writer links and no incremental allocation in the measured warmed queue-entry call. Measure the contended nonwaiting call separately on its caller thread.
- Include the owner-reported backing in independent Runtime known-component expectations; reject negative or checked-overflow charges. Preserve zero-draw behavior.

These observations establish only the stated backing and producer-waiter/refusal components. They do not qualify request/TCS/Task object sizes, cold/suspended/faulted control groups, native execution, NativeAOT, public consumers, or the full budget envelopes.

The next dependent source batch requires the missing construction/retention capability above. Until it is qualified, keep request-control admission unavailable; do not insert a guessed grant into the current factories or declare whole-graph budget admission. Preflight scratch, quarantine carriers/copies, payload aliases and remaining completion terms retain their separate NO-GO rows.

## 6. Frozen inputs

Whole-file SHA-256 values identify the narrow dirty-workspace input set based on HEAD `8ff3c116421a5e50c47a9de1cd96918cc7ecb294`. HEAD alone is not the reviewed input. Recheck before applying a disposition. These inputs are static source/fixture evidence, not task/control-size qualification.

| Input | SHA-256 |
|---|---|
| `global.json` | `1BCE01871321C2DF82E887DA428CE5C121A6C6541FE65017FE3D249BE47E190C` |
| `docs\proposals\m05-skinned-normal-diagnostic-owner-queue-admission-addendum.md` | `A546D16E33EA02F0953C8496A22C6C841B2D7E1EB524E2437237166D50097F72` |
| `docs\proposals\m05-skinned-normal-diagnostic-request-control-admission-addendum.md` | `96397B0B835D727098FF3E9314296BA97ACEE0CA7E71E24FC3B6E9F5A3A0B7B0` |
| `sources\TerraFX.Graphics\GraphicsDevice.cs` | `ED23475055D08BB3631935580912DB5AB30F55E0F5228227AF76BB123CEE5FEC` |
| `sources\TerraFX.Graphics\GraphicsDevice.RecordPass.cs` | `264194C27CE8E864685F00A7BA0B644A4525A5816FFAEB8D76C1D358A260C64D` |
| `sources\TerraFX.Graphics\GraphicsDevice.SubmitGraph.cs` | `C68E23B5FFA591C639714101FB23A1F1AE883FAC28F722EEB5B034725C3A97FD` |
| `sources\TerraFX.Graphics\GraphicsOwnerRequestQueue.cs` | `B53D2EB1C14874A544519A46CE19288B334BF50EFFE86B6C807289F0BA743689` |
| `sources\TerraFX.Runtime\Rendering\RenderMeshC0NormalDiagnosticWork.cs` | `DF6C554342A3D0BD023F398AA2A7137FC47DA9481174FCFA809C1C7AFBAD0E1D` |
| `sources\TerraFX.Runtime\Rendering\Renderer.MeshC0Graph.cs` | `73D1AB54679FDA2A0FD3D9880DD51FC5A9A6DD9A936D10D38D54BE30A0645335` |
| `tests\TerraFX.Graphics.Tests\GraphicsOwnerRequestQueueTests.cs` | `7553591FF07775B9ECE84D23D420CB2214C91822C63C51DBB6B51E4F3D35D4C3` |
| `tests\TerraFX.Graphics.Tests\GraphicsBufferTransferTests.cs` | `E7A07B72A65C5343DEB204A9F1B6DF854C18F573C6710B7CFCB0407EB964B603` |
| `tests\TerraFX.Runtime.Tests\Rendering\RenderMeshC0NormalDiagnosticBudgetTests.cs` | `F86055DEA983DD4E22AA5DF60937C2AFA34C514C85B2BE829D05C8E33ADF8175` |
