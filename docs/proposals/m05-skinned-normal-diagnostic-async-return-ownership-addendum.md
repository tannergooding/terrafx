# M05 diagnostic async-return ownership addendum

## 1. Narrow decision

**Tentative implementation guidance, pending review. Request/TCS/async-return admission and the complete 32/16-MiB diagnostic gate remain NO-GO.** This resolves the next return-owner decision in [task/waiter lifetime](m05-skinned-normal-diagnostic-task-waiter-lifetime-addendum.md), not the separate fence-worker, channel or quarantine qualification.

Keep **Graphics request/promise controls Graphics-owned and Runtime return controls Runtime-owned**. Do not expand Core's buffer-completion allowance into a general Task tariff. The next missing capability is an owner-qualified construction and managed-retention interest for the **actual returned async operation**, including its failure/cancellation return, not merely the private request or its TCS.

For a future private diagnostic handoff, select a **funded promise return whose managed interest is co-retained by that actual returned Task**, rather than assuming a request-held interest survives every returned Task. The existing TCS constructor's `state` provides a possible strong retention link for its own Task; it supplies neither an object-footprint bound nor a link to a different outer return. Do not implement that handoff until both its full construction group and the enclosing Runtime return owner are qualified.

Preserve the **33,554,432-byte** joint preparation/recording and independent **16,777,216-byte** completion ceilings, complete diagnostic results, command schedule, cancellation boundary and existing Core readback handoff. This selects no public API, request/waiter cap, object-size constant, compiler flag, Scene producer or generic engine async abstraction.

## 2. First unqualified return boundary

| Existing owner | Actual source boundary | What is not established |
|---|---|---|
| Core `BufferCompletionFootprint` / `BufferCompletionAdmission` | The selected buffer group names allocation/options controls, fixed registration backing, writer/reader/finalized-owner wrappers, admission and native allocator storage. | It does not enumerate `TaskCompletionSource`, Task, async-return, continuation or cancellation controls. Its profile and byte allowance cannot be reused for these different shapes or charged through logical buffer length. |
| Graphics `RecordPassRequest` | Constructs `TaskCompletionSource<RecordedPassCommandList>` and exposes its private `Task`. `RecordPassAsync` awaits it and returns its own async operation. | A request-only interest, or a state link on the private promise alone, is not a qualified owner for the actual outer return's complete storage/lifetime. |
| Graphics `SubmitGraphRequest` | Uses the different `TaskCompletionSource<GraphicsGraphSubmissionResult>` family; `SubmitGraphAsync` awaits that promise and has its own cleanup/finally protocol. | The recording family's result/layout and retention evidence are not authority for this family. Post-native worker/signal Tasks remain additional groups under the preceding addendum. |
| Runtime `RenderGraphPlan.RecordPassCallbacksAsync` | Returns `Task<ImmutableArray<RecordedPassCommandList>>` around the sequential Graphics calls and builder/failure cleanup. | Its compiled control and retained builder/input/result roots have no complete construction/return interest. |
| Runtime `Renderer.SubmitAsync` | Returns public `Task<RenderSubmissionReceipt>` around admission, resource ownership, lower-layer awaits and completion cleanup. | Lower-layer promise grants, a frame-slot credit or Core readback storage do not fund this separate Runtime return/control family. |

These are source-level ownership boundaries, **not counts of physical Task allocations**. [Chapter 02 section 3](../design/02-dotnet-api-memory-and-ownership.md#3-leases-and-asynchronous-operations) explicitly forbids mechanical wrapper removal and preserves error, continuation, affinity and lifetime semantics. Runtime Async may optimize wrappers. [Chapter 29](../design/29-project-setup-build-and-deployment.md) keeps the engine Runtime Async flag optional; the in-tree default `Features` is `strict`. Qualify the actual compiler/runtime/build variant instead of assuming either two allocations per wrapper or zero.

Distinguishing case: retain only the operation returned by a suspended recording call through completion, after the queue has dequeued the request and its safe input roots have detached. Counting only the request/private promise, or refunding on dequeue/completion, does not establish a charge for still-retained returned-operation storage. Whether the compiled return retains, clears or shares particular controls must be proved; successful command/result disposal is not that proof.

## 3. Selected private handoff and missing capability

The smallest ownership-complete handoff needs all of the following before source implementation:

1. **Closed construction plans.** Identify the exact closed return families above, effective compiler flags, architecture/runtime and Debug/Release lowering. Include actual promise, returned-operation, state/capture, exception/cancellation and continuation storage with their backing capacities. Count retained payload/backing separately and once; neither `sizeof(TResult)` nor a Task/reference count is the allocation charge.
2. **Scalar admission before materialization.** A nonallocating private plan must check the complete group beside the still-live joint baseline before constructing the interest, request, TCS or return control. The interest's own storage belongs to an already funded construction amount. Unknown profile/shape/capacity remains unavailable.
3. **Returned-operation retention.** For a TCS-owned diagnostic return, the existing BCL `state` constructor can retain the same scalar/control interest with its actual Task on success, fault and cancellation. The interest must not retain obsolete callback/payload roots merely to keep its charge alive. If a distinct compiler-produced outer return remains, it needs its own qualified owner/link; the private promise's state does not silently cover it.
4. **No wrapper shortcut.** Returning the funded promise directly is an ownership option only after relocating/proving all existing finally/abandonment cleanup and preserving Chapter 02's usage/admission/error/cancellation channels and asynchronous continuations. Do not remove the current wrapper as a standalone optimization or hoist a contextual capacity fault into a new synchronous failure.
5. **The refusal return is not free.** Throwing inside an async body, `Task.FromException` or `Task.FromCanceled` can materialize return/fault controls. A valid-request budget refusal must use its already-funded owning return channel; it cannot escape the accounting by constructing an unplanned faulted Task. Do not return an empty successful diagnostic or change the public Task contract to a scalar Try result.
6. **Independent Runtime owner.** Runtime must fund its own compiled/returned operation and retained baseline before relying on a lower Graphics grant. Graphics and Core must not acquire a Runtime sizing callback or retain a Runtime control registry to bypass the dependency direction.

A complete group remains charged while its real request, return, worker or qualified quarantine owner retains it. No reusable managed-byte refund is established merely by `IsCompleted`, callback cleanup, disposed results or an empty queue/pending-list slot. Native/uncertain ownership must transfer the same applicable interest before releasing the old owner, through the separately qualified quarantine publication path.

The current owners supply no such complete return construction/retention capability. The buffer completion grant and pipeline-use cohort remain valid only for their own enumerated groups. No guessed field-slot extension, weak-table registry, hidden BCL layout read, result-length substitute or unqualified promise carrier is introduced.

## 4. Smallest next dependent batch

After qualification, implement only the private Graphics diagnostic promise/return factory and its funded Runtime handoff, with the existing ordinary route and public signatures preserved. First close the concrete returned-operation owner; do not add a request-only grant and present it as a complete return/control phase.

Required owning evidence includes exact-fit/one-byte-short complete grants, unsupported-profile refusal before materialization, safe constructor unwind, actual pending/fast-complete/faulted/canceled return paths, independently retained returned Tasks, and exact cleanup/continuation ordering. Any native-accepted transfer still needs the preceding addendum's fence-worker/quarantine proof. Use the real owning Graphics/Runtime harnesses; no fabricated snapshots, placeholder promises/results or new Scene preparation may substitute for an unavailable Runtime consumer.

**Current disposition:** the examined request/return slice has no further independently qualified numeric allocation or transfer term. The fixed request-ring and actual pending-submission backing rows remain implemented; they are not new work here. Source/tests remain unchanged, and no new tests or allocation probes are run for this documentation-only blocker. The complete request/control, waiter/channel, preflight, quarantine, alias and completion gates remain **NO-GO**.

## 5. Frozen inputs

Whole-file SHA-256 values identify the narrow input set based on HEAD `9a8cccee4c577e3d6c3eb4279a26102a8e95c2fe`. HEAD alone is not the reviewed input. Recheck before applying a disposition. These are static contract/source inputs, not compiled-return footprint qualification.

| Input | SHA-256 |
|---|---|
| `global.json` | `1BCE01871321C2DF82E887DA428CE5C121A6C6541FE65017FE3D249BE47E190C` |
| `docs\proposals\m05-skinned-normal-diagnostic-request-control-admission-addendum.md` | `96397B0B835D727098FF3E9314296BA97ACEE0CA7E71E24FC3B6E9F5A3A0B7B0` |
| `docs\proposals\m05-skinned-normal-diagnostic-task-waiter-lifetime-addendum.md` | `7B9D68F0C01D483BAFF0658056E46847B0BA0CCC6847C0292B909EDA06B9CE05` |
| `sources\TerraFX.Core\Buffers\BufferCompletionFootprint.cs` | `2554C968EC3EA4272177F538A2B36C01EAE6A630D012667FBE99CF0913A116D7` |
| `sources\TerraFX.Core\Buffers\BufferCompletionAdmission.cs` | `3983535DF65E53C70235B310A33B26CFDEAF4BC621981CFD485D266B490CA810` |
| `sources\TerraFX.Graphics\GraphicsRecordingUseAdmission.cs` | `DD1983E585EE56E2BA2E0BA8D66D385C964C1F8C27539413E06BF0DE874B65FE` |
| `sources\TerraFX.Graphics\GraphicsDevice.RecordPass.cs` | `264194C27CE8E864685F00A7BA0B644A4525A5816FFAEB8D76C1D358A260C64D` |
| `sources\TerraFX.Graphics\GraphicsDevice.SubmitGraph.cs` | `C68E23B5FFA591C639714101FB23A1F1AE883FAC28F722EEB5B034725C3A97FD` |
| `sources\TerraFX.Graphics\GraphicsOwnerRequestQueue.cs` | `B53D2EB1C14874A544519A46CE19288B334BF50EFFE86B6C807289F0BA743689` |
| `sources\TerraFX.Runtime\Rendering\RenderGraphPlan.Recording.cs` | `ED5E694C9B698F6EA3535E46F80149A97CC8F9A67F8048240525D6BABFE28316` |
| `sources\TerraFX.Runtime\Rendering\Renderer.Submit.cs` | `8BF0D0662C9D3DE0A1670ED05C89FAB1E6A749D744BF796A9D5E0236BF4DDB79` |
| `docs\design\02-dotnet-api-memory-and-ownership.md` | `40F913A50434209E09941C22625A6D0722202BAB97EBAC0E153C1EA3368E2291` |
| `docs\design\29-project-setup-build-and-deployment.md` | `3B55A013EEC327ECD648ACBD5B53717DD218AD288E8047AC21277609EFA2F47B` |
| `Directory.Build.props` | `770EE4B2A422861A6F23F0D39DD0ECA361A81CB87E64EBAC4E12170F71231D7A` |
| `Directory.Build.targets` | `AEEC1D08E8037470239035812351C4045124607D67A354D482112F4D642D4105` |
| `sources\Directory.Build.props` | `D44373AF315523F8AF9F09F36BDED2A688E2B5C1572FCC0732310C7EF590EAD8` |
