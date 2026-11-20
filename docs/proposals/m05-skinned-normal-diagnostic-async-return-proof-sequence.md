# M05 diagnostic async-return proof sequence

## 1. Decision

**Tentative evidence/implementation sequence, pending review. There is not yet a demonstrated safe production return-control implementation. The complete 32/16-MiB admission gate remains NO-GO.**

[Async-return ownership](m05-skinned-normal-diagnostic-async-return-ownership-addendum.md) selects the owners correctly, but its qualification-before-handoff instruction does not identify an entry-funded returned Task when admission itself fails. [Task/waiter lifetime](m05-skinned-normal-diagnostic-task-waiter-lifetime-addendum.md) remains authoritative for the additional submission worker and uncertainty frontier. Section 7 records current compiled-return and actual-task evidence without treating it as ownership qualification.

The current-source compiled-return census and focused actual-task probes are recorded in §7. They establish emitted return shapes for the selected methods and two actual return-path retention outcomes, but do not cover every family's failure/suspension behavior or provide a usable retained-owner link. The next dependency is first-return funding and the complete entry-owner design; until that is closed, no production factory/grant or reusable refund is permitted. The 32/16-MiB admission gate remains NO-GO.

This refines the preceding addendum's implementation stop only to permit an unpublished measurement candidate. It does not require the final candidate's compiled proof before that candidate can exist, or treat candidate execution as byte-qualified admission.

The buffer-import index in `b447c4c2` and command-owner handoff in `22b1a36e` do not alter these four method bodies. Their completed components are not reopened. This document selects no public signature change, wrapper-elision optimization, generic async infrastructure, new allowance, request/waiter cap, schema, or Scene producer.

## 2. New source and toolchain findings

These are observations of the frozen files and property evaluations below, **not compiled-return or allocation measurements**.

| Boundary | Consequence for the experiment |
|---|---|
| Graphics `RecordPassAsync` / `RecordPassRequest` | The method is `internal async Task<RecordedPassCommandList>`, not a public Graphics declaration. The request creates a state-less TCS. Queue refusal faults the enclosing operation after `Abandon`; `Abandon` does not complete the private promise. Returning that private promise unchanged would leave this path pending. |
| Graphics `SubmitGraphAsync` / `SubmitGraphRequest` | Also internal. Validation and pre-cancellation can exit before a request exists. Once constructed, admission failure calls `AdmissionFailed`, which releases work but does not complete the private promise. The enclosing `finally` also owns the resource-use transfer cleanup. |
| Both Graphics `Fail(Exception)` methods | Use `TrySetException`, including when the exception is an `OperationCanceledException`. A faulted private promise is not automatically equivalent to the Task returned by an async method awaiting it. Under the standard Task builder, propagation of that exception produces a canceled outer Task. Measure the selected lowering; do not replace this distinction with a direct-promise shortcut. |
| Runtime `RecordPassCallbacksAsync` | Returns `Task<ImmutableArray<RecordedPassCommandList>>`; its builder, earlier successful command lists, per-pass captures and failure cleanup are another closed family. Graphics recording evidence does not cover it. |
| Runtime public `Renderer.SubmitAsync` | Returns `Task<RenderSubmissionReceipt>` after submission, readback, typed-result creation and `finally` cleanup. Its current null/token checks occur inside the async body; token checking precedes later snapshot/graph validation. This is not evidence that Chapter 02's synchronous usage-error and usage-before-pre-cancellation contract is implemented. A front-door change must explicitly reconcile and test this discrepancy, not silently change exception timing during accounting work. |

The cancellation distinction is a concrete discriminator even without native execution: a recording callback can throw `OperationCanceledException`. The private request promise's status and the returned operation's status must both be recorded. An exception-type-only assertion would miss an incorrect faulted-versus-canceled return. No compiled experiment was run for the earlier frozen source review; current-source census and focused behavior results are recorded in §7.

### Effective settings, not an assumed SDK/runtime pair

Ordinary MSBuild **evaluation only**, Debug before Release, succeeded for both production projects:

| Setting | Evaluated value |
|---|---|
| SDK selected in this checkout | `11.0.100-rc.3.26522.148` |
| C# compiler queried from that SDK | `5.12.0-1.26522.148`, commit `5254d8bcf59917edc1e1abc33bdf2442b77b1c70`; language query lists C# `15.0` as default |
| Both projects | `net11.0`, `LangVersion=latest`, `Features=strict`, nullable enabled, `SkipLocalsInit=true` |
| Debug / Release | `Optimize=false` / `true`; checked arithmetic `true` / `false` |
| Deployment settings | `IsAotCompatible=true`, AOT analyzer enabled; library `PublishAot`, RID and `PlatformTarget` unset |
| Dependency direction | Graphics has Core as its product reference; Runtime references Graphics. Analyzer/generator and shader-tool references are build-time references, not authority to introduce a reverse product dependency. |

`Microsoft.CodeAnalysis.CSharp`'s centrally selected **5.9.0 package is not the SDK compiler version**. `Features=strict` does not supply compiled evidence of wrapper allocation, identity, state clearing, or Runtime Async optimization. Capture the actual compiler invocation and emitted methods in the next batch; do not add a compiler flag to obtain a convenient shape.

The local `dotnet --info` reports an **x64 11.0.0 GA host**, while selecting the local RC3 SDK. The compiler runtimeconfig requests RC3 with `rollForward=Major`; existing test runtimeconfigs request RC3. Those requested versions do not prove which runtime a future test process loads. `GraphicsRecordingUseAdmission.IsSupportedProfile` currently requires the exact RC3 framework description. Thus neither the SDK selection nor old RC3 observations qualify a GA diagnostic execution. Capture the test process's actual framework/runtime identity and profile support before a positive diagnostic run; do not silently broaden that predicate. Static output inspection and ordinary recording-return evidence can proceed without claiming a positive diagnostic-profile observation.

## 3. Candidate to prove, and the admission bootstrap

The only candidate carried forward here is the preceding addendum's **stateful, owner-funded promise as the actual returned Task**, with the existing async execution/cleanup retained as an explicitly owned worker where a distinct wrapper is necessary. It is an ownership change, not removal of an await wrapper to save allocations.

For each exact family the proposed strong path is:

```text
actual returned Task
  -> its TCS constructor state
  -> that layer's closed return control and charged interest
  -> any still-required private promise / execution-worker Task
```

The control must detach obsolete payload/callback roots after actual cleanup while keeping its scalar charge and any still-live worker controls. Keeping the original request alive alone is insufficient. Neither a request-to-Task reference nor a completed worker's former continuation establishes the reverse path above. If a distinct compiler-generated outer Task is still returned, the candidate has not solved the blocker.

Use only operation-specific Graphics and Rendering controls for these four concrete results. No general `AsyncReturn<T>`, custom engine method-builder framework, ambient admission context, weak-table attachment, private BCL field mutation, or Runtime callback into Graphics/Core is selected. Existing wrapper bodies and their ownership/finally work must remain represented and funded; no forwarding body may simply disappear.

### Close the first-return problem before an operational handoff

The existing proposals do not establish an already-funded return when the complete grant is unavailable. Before implementing a supported factory, write and verify its scalar entry sequence:

1. Perform the applicable owner/usage checks through the established channel; resolve the public entry discrepancy in section 2 against [Chapter 02](../design/02-dotnet-api-memory-and-ownership.md#async-errors-and-admission).
2. Identify the **previously admitted owner and bytes** for the minimum actual return, including pre-cancellation and contextual capacity failure, before creating its control, TCS, Task, exception or registration.
3. Check both complete envelopes, with the still-retained baseline, before materializing request/work controls or transferring any lower-layer grant. A failed work grant completes only that already-funded return after safe cleanup.
4. If the minimum return cannot itself be funded, stop. Do not solve this by throwing from an async body, adding `Task.FromException`/`Task.FromCanceled`, or charging after allocation. A synchronous capacity throw would also change the established channel.

Preallocating a terminal refusal Task is **not selected as an assumed solution**. It would need an existing owning budget, complete exception/continuation storage and lifetime qualification, unchanged contextual error behavior, and evidence under repeated calls. A single canceled Task cannot preserve arbitrary distinct cancellation tokens. If repeated retained refusal/cancellation returns cannot be funded without changing the existing contract, report that precise contract-level obstruction; do not invent a request cap or an external “API overhead” allowance.

### Exact accounting ownership

These are ledger assignments, not new byte constants or available grants:

| Construction/retention group | Issuer and existing envelope |
|---|---|
| Record request, its private promise, Graphics recording-return control and execution wrapper | Graphics; joint **33,554,432-byte** preparation/recording envelope, funded from Runtime's headroom beside the live plan |
| Submit request, its distinct promise/return wrapper and preclaimed fence-worker/signal group | Graphics; the same joint envelope; worker/quarantine requirements remain under the task/waiter addendum |
| Runtime recording return, builder/backings and captures | Rendering; the same joint envelope, not a second 32 MiB |
| Runtime submission's actual return control/Task, retained execution-control storage, terminal fault/cancellation controls and receipt | Rendering; independent **16,777,216-byte** completion envelope, preclaimed from entry alongside raw readback, typed results and decode controls |
| Preparation payloads reachable from that submission control | Their original joint-envelope owners; a completion-control reference does not move those bytes into the completion allowance |

Any entry/refusal reserve must be an enumerated, already-funded baseline term in the applicable row, not another envelope. Charge each physical allocation once, include construction/transfer coexistence, and retain every live obligation. An interest moving between holders is not fresh headroom. Unknown family/profile/capacity means unavailable; no numeric Task tariff is inferred from this table.

## 4. Smallest measurable sequence and stop gates

### A. Current compiled-return census and targeted task probes

The current metadata census and targeted probes have run using the ordinary owning projects and NUnit harnesses in [Building](../project/Building.md). They establish selected compiled Task types and behavior, not all of section 5's outcomes. Do not infer Task size from a state-machine struct or treat a completed Task as an owner when the test shows it retains no owner state.

Current probes use the existing owning fixtures, not a second implementation:

- `GraphicsPipelineRequestTests`: three `CompletedRecordingTask` tests pass in Debug/Release. The held `RecordPassAsync` Task is an `AsyncStateMachineBox`, is suspended before callback completion, releases the callback capture/device after completion, and preserves callback cancellation status, exception object, and token.
- `EngineHostOptionsTests` in `EngineHostGraphSubmissionTests.cs`: `D3D12GraphSubmissionRetainsTargetUntilFenceCompletion` passes in Debug/Release. The actual `Renderer.SubmitAsync` Task is an `AsyncStateMachineBox` while suspended. After D3D12 completion and cleanup, retaining only that Task retains none of the snapshot, plan, Renderer, or device.
- `EngineHostOptionsTests.CreatesAndShutsDownVulkanGraphicsDeviceUsingHostIssuedIdentity`: passes in Debug/Release. Its real offscreen graph records through an actual `RecordPassCallbacksAsync` Task; the Task is an `AsyncStateMachineBox` while suspended, and after completion/result disposal it does not retain the `RenderGraphPlan`. This is not a diagnostic consumer.
- `SubmitGraphAsyncTaskRetentionTests.CompletedSubmitGraphTaskDoesNotRetainSubmission`: passes 1/1 in Debug/Release on the real Vulkan backend. The actual `SubmitGraphAsync` Task is an `AsyncStateMachineBox` while suspended; after device shutdown it retains no submission, whether the Task is still held or has been dropped.
- `RenderSnapshotMeshGpuTests`: the positive diagnostic-return row remains unsupported behind the fail-closed `PreparedScene` guard. Do not fabricate a `RenderSnapshot` or call unsupported-profile refusal a success.

The exact emitted-method census passes 1/1 in Debug and Release for the methods listed in section 2. It reports `Task<T>` declarations, emitted state-machine/MoveNext lengths and hashes, and the actual Runtime/architecture. `Renderer.SubmitAsync` is a synchronous wrapper returning the Task from `SubmitAsyncCore`; the latter is the async state machine. The complete output is in the §7 TRX paths.

Remaining section 5 observations include queue/refusal and worker-failure paths and an accepted positive diagnostic consumer. The completed RecordPassCallbacks, SubmitGraphAsync, and Renderer Tasks fail to retain their respective plan/submission/resource owners after cleanup; these are negative owner-link results, not positive interest-retention evidence.

**Stop A:** a missing Runtime producer/profile is an unsupported row, not a synthetic substitute. The completed Renderer Task cannot be treated as the owner of the disposed graph resources. Proceed only to entry-design closure; do not assign it a guessed charge or turn on admission.

### B. Close the entry design, then compile one coherent measurement candidate

First identify section 3's minimum-return funding owner, entry ordering and four terminal-state transitions on paper. This is design closure, not an invented numeric certificate: section C measures and qualifies the construction amounts. If even the funding owner/channel cannot be specified without a new allowance or changed contract, stop at that narrower design obstruction.

Then an unpublished local candidate may rehome execution into the stateful-return scheme, preserving the async cleanup workers and ordinary route. Compile and observe the actual changed owner methods through the existing harness, not lookalike test promises. Cover all four boundaries in one integration batch; a Graphics-only TCS state change is not a Runtime return implementation.

This is the bounded implementation that can start before its final compiled-retention proof, **not an operational grant implementation**. Enumerate and measure its actual return/control allocations as candidate construction obligations; do not claim those obligations are already qualified grants. Do not enable a supported complete-admission profile, lend refunds, add a separate fallback fault Task, or publish the candidate as a qualified implementation. No speculative dormant async framework is authorized.

**Stop B:** freeze and inspect the candidate's actual returned Tasks and worker roots again. Repeat the retained-Task witness with its actual intended return-control/interest instance: it must remain reachable while obsolete safe callback/payload roots do not. In particular, prove publication of a fast-completed outward promise cannot precede safe cleanup or expose an unattached still-live worker. A worker fault must be observed and delivered once; no fire-and-forget path may lose the primary failure or its interest. Any change to a reviewed input invalidates the corresponding proof.

### C. Measure the closed construction groups, then implement the grants

Only after the strong paths and terminal protocol pass, enumerate every construction site in each family: carrier, request, TCS/Task, actual compiled worker, captures, continuations, execution-context/registration storage, terminal exception/cancellation holders and their backings. Measure cold and warmed, suspended and fast-complete, success and failure paths on caller, Graphics owner and pool/continuation threads. Use whole-operation allocation attribution and retained-root observations; a caller-thread allocation delta is not a cross-thread construction bound, and allocation traffic is not retained size.

Derive conservative profile-qualified bounds and checked phase maxima for the groups in section 3. Unsupported or variable unbounded storage remains unavailable. Then add only their private scalar plans, one-shot grants and factories, including constructor unwind and exact-fit/one-byte-short complete-group checks. No constructor may run merely to discover its required charge.

**Stop C:** full diagnostic admission remains unavailable until the other existing ledger rows also close. This slice does not qualify channel/shared controls, backend preflight, quarantine publication, binding aliases or the remaining completion storage.

## 5. Required behavioral observations

These are **acceptance requirements, not tests reported as passed by this document**.

| Distinguishing case | Required result/evidence |
|---|---|
| Queue full/contended/closed, failure before request construction, failure during construction | Correct contextual faulted return after safe cleanup; no pending outward promise, accepted work, newly hidden fault Task or spent grant left behind |
| Callback/backend throws an ordinary exception | Original failure through the established operation channel; safe cleanup or the established ownership-retaining uncertain transfer precedes terminal publication; retained native work is not reported released |
| Callback/private promise propagates `OperationCanceledException`, including a token not selected by admission | Record private-promise and outward-operation statuses separately; preserve the validated async cancellation semantics and token, not just the exception's type |
| Valid pre-canceled submission; invalid usage plus cancellation | Usage precedence/channel follows Chapter 02; valid pre-cancellation produces a funded canceled return with no admitted work |
| Cancellation races acceptance; separate `WaitAsync` observer cancels after acceptance | Original accepted work/Task retains ownership through real consumption; observer cancellation does not release the group or withdraw native work |
| Completion wins before the first await observation, and genuinely suspended completion | Same complete result, cleanup, continuation and retention protocol; never infer that fast completion creates no return/control storage |
| Retain only the actual returned Task after completion, result disposal and request removal | The proper charged return interest remains; obsolete safe inputs detach; no refund on `IsCompleted`, empty queue or frame-slot reuse |
| Success through Runtime | Same ordered command-list membership and receipt sequences/epochs; complete typed diagnostics from real readback, never prefix/empty-success substitution |
| Unsupported profile, checked overflow, exact fit and one byte short | Refusal before the affected group's materialization using its already-funded return; no post-allocation accounting or envelope transfer |

Check discovered **and executed** NUnit selections and fresh TRX output, including each terminal/suspension variant; unsupported Runtime rows remain explicit. Managed owner/fake-backend observations do not establish native failure, NativeAOT or public-consumer qualification. Preserve the existing native observation gates instead of adding synthetic substitutes.

## 6. Frozen inputs and present disposition

Review base: **`22b1a36e711595de936ee1180e500e455a02482d`**. The pre-existing workspace had **79 modified/untracked entries**. Their bytes, as well as the current contract/source inputs, were frozen before disposition. HEAD alone is not the reviewed input. No code, existing dirty file, schema or existing proposal is changed by this review.

The local ignored record is `artifacts\log\reviews\m05-async-return-20261117\frozen-inputs.json`, SHA-256 **`985CCAE1D3776AE40AB58DFAA16F3A4428C48B574C176ED0CA61E9C4A0863048`**. Property-evaluation JSON, `dotnet-info.txt` and the compiler language query are beside it. They are per-review evidence, not a new qualification runner or repository build requirement. The queried SDK `csc.dll` SHA-256 is **`A3C6FCD1FB9AE921A985E7D55F79CE921EEE79A5A61A11F6D8CE9776BAF81172`**.

Load-bearing whole-file SHA-256 values:

| Input | SHA-256 |
|---|---|
| `global.json` | `1BCE01871321C2DF82E887DA428CE5C121A6C6541FE65017FE3D249BE47E190C` |
| `Directory.Build.props` | `770EE4B2A422861A6F23F0D39DD0ECA361A81CB87E64EBAC4E12170F71231D7A` |
| `Directory.Build.targets` | `AEEC1D08E8037470239035812351C4045124607D67A354D482112F4D642D4105` |
| `Directory.Packages.props` | `1E0E79719EFC9259390D9CF4E63AC697561725C8CD910D2AB2541D6E5BB95A6D` |
| `sources\Directory.Build.props` | `D44373AF315523F8AF9F09F36BDED2A688E2B5C1572FCC0732310C7EF590EAD8` |
| `sources\Directory.Build.targets` | `4C0527A12E48D07401C69C06CF942F9145D6A8CCF98C4A7F052B11E463E1CE9A` |
| `docs\project\Roadmap.md` | `1C8C3F8E140F47492255534284ABBF67C5776210DC7A68F2C01155D0BE773F46` |
| `docs\project\Building.md` | `F043A352870EFB6C7D4A61998225E2508A8F4370A57E784483A41819D452075F` |
| `docs\design\02-dotnet-api-memory-and-ownership.md` | `40F913A50434209E09941C22625A6D0722202BAB97EBAC0E153C1EA3368E2291` |
| `docs\design\29-project-setup-build-and-deployment.md` | `3B55A013EEC327ECD648ACBD5B53717DD218AD288E8047AC21277609EFA2F47B` |
| `docs\design\35-implementation-roadmap.md` | `24EEC6E92B7AD904D552F290EF748FFC8FCE13B6B94A89EBD9F39487D4584D87` |
| `docs\proposals\m05-skinned-normal-diagnostic-async-return-ownership-addendum.md` | `F5A8A8043A3E8677CEC3F18AAC1F85994A13FC25C6CFA1BA7E3E72E5B5E60991` |
| `docs\proposals\m05-skinned-normal-diagnostic-task-waiter-lifetime-addendum.md` | `7B9D68F0C01D483BAFF0658056E46847B0BA0CCC6847C0292B909EDA06B9CE05` |
| `docs\proposals\m05-skinned-normal-diagnostic-request-control-admission-addendum.md` | `96397B0B835D727098FF3E9314296BA97ACEE0CA7E71E24FC3B6E9F5A3A0B7B0` |
| `docs\proposals\m05-skinned-normal-diagnostic-recording-budget-closure.md` | `5787039DC3BA43A862529CD9F01D82623DDFEC1B273194CC73FC50F079E0C753` |
| `docs\proposals\m05-skinned-normal-diagnostic-command-payload-owner-transfer-addendum.md` | `1A2998993984D62D544AB0920315A34F263A9A45AFF44994D6D874A35F95C6A4` |
| `sources\TerraFX.Graphics\TerraFX.Graphics.csproj` | `E04F4E614E10B07D60A9E56B1C60E69D85B63240339587271235D079E197CCD4` |
| `sources\TerraFX.Graphics\GraphicsDevice.RecordPass.cs` | `264194C27CE8E864685F00A7BA0B644A4525A5816FFAEB8D76C1D358A260C64D` |
| `sources\TerraFX.Graphics\GraphicsDevice.SubmitGraph.cs` | `C68E23B5FFA591C639714101FB23A1F1AE883FAC28F722EEB5B034725C3A97FD` |
| `sources\TerraFX.Graphics\GraphicsRecordingUseAdmission.cs` | `DD1983E585EE56E2BA2E0BA8D66D385C964C1F8C27539413E06BF0DE874B65FE` |
| `sources\TerraFX.Graphics\GraphicsGraphSubmissionResult.cs` | `72D90BE0F3AB75C515C65A1C921DF84035678A56515AF029F7A3F1CA19372023` |
| `sources\TerraFX.Runtime\TerraFX.Runtime.csproj` | `2F1C81E54F0FBF12D01E34D0D840885F5EB0E94A8B8890865EF721B8C6A3C65D` |
| `sources\TerraFX.Runtime\Rendering\RenderGraphPlan.Recording.cs` | `ED5E694C9B698F6EA3535E46F80149A97CC8F9A67F8048240525D6BABFE28316` |
| `sources\TerraFX.Runtime\Rendering\Renderer.Submit.cs` | `8BF0D0662C9D3DE0A1670ED05C89FAB1E6A749D744BF796A9D5E0236BF4DDB79` |
| `sources\TerraFX.Runtime\Rendering\RenderSubmissionReceipt.cs` | `77120676F289AD7D8891875A783BAFA05E64B7F0317BC09BECC2545AC42E5846` |
| `sources\TerraFX.Runtime\Rendering\RenderMeshC0NormalDiagnosticWork.cs` | `DF6C554342A3D0BD023F398AA2A7137FC47DA9481174FCFA809C1C7AFBAD0E1D` |

Recheck frozen hashes before applying a disposition to that review. **The frozen review itself established source boundaries and effective evaluation settings only.** Later execution evidence and its limits are in §7.

## 7. Post-freeze execution evidence

The current-source evidence was captured at HEAD `74e32218043453d24cbdb24c4d42b04727611d07`, using `.NET 11.0.0-rc.3.26522.148` on X64:

| Evidence | Debug | Release | Scope |
|---|---|---|---|
| `AsyncReturnCensusRecordsFourReturnFamilies` | 1/1 passed | 1/1 passed | Records assembly and emitted-body hashes for `RecordPassAsync`, `SubmitGraphAsync`, `RecordPassCallbacksAsync`, `Renderer.SubmitAsync`, and `SubmitAsyncCore`. All declare `Task<T>`; four async methods expose state-machine metadata, while `Renderer.SubmitAsync` is a 25-byte Debug / 20-byte Release synchronous wrapper whose core contains the async body. Actual `AsyncStateMachineBox` instances were observed for `RecordPassAsync`, `SubmitGraphAsync`, `RecordPassCallbacksAsync`, and `SubmitAsyncCore`; retained-root outcomes are in the following rows. |
| `GraphicsPipelineRequestTests` `CompletedRecordingTask` filter | 3/3 passed | 3/3 passed | Real `RecordPassAsync` Task type/suspended status, callback/device detachment after completion, and fault-versus-canceled callback behavior including exception identity and token. |
| `EngineHostOptionsTests.D3D12GraphSubmissionRetainsTargetUntilFenceCompletion` | 1/1 passed | 1/1 passed | Real D3D12 submission Task type and suspended status; the completed caller Task retains none of the snapshot, plan, Renderer, or device after cleanup. This is not a positive diagnostic consumer. |
| `EngineHostOptionsTests.CreatesAndShutsDownVulkanGraphicsDeviceUsingHostIssuedIdentity` | Passed | Passed | Real offscreen Vulkan graph records through `RecordPassCallbacksAsync`; while held after completion, its Task does not retain the `RenderGraphPlan`. The test reported Vulkan API 1.4.359 and `VULKAN_SDK=C:\VulkanSDK\1.4.363.0`, not the user's supplied 1.4.328.1 toolchain; this is not shader-toolchain or diagnostic-consumer qualification. |
| `SubmitGraphAsyncTaskRetentionTests.CompletedSubmitGraphTaskDoesNotRetainSubmission` | 1/1 passed | 1/1 passed | Direct actual Vulkan graph submission. Its returned Task does not retain the `GraphicsGraphSubmission` after completion and device shutdown, whether the Task is held or has been dropped. |

Fresh results are `artifacts\tst\Debug\tagoo_GOODING-ZEN4_2026-11-20_02_04_48_net11.0.trx`, `artifacts\tst\Release\tagoo_GOODING-ZEN4_2026-11-20_02_05_41_net11.0.trx`, `artifacts\tst\Debug\tagoo_GOODING-ZEN4_2026-11-20_02_06_12_net11.0.trx`, `artifacts\tst\Release\tagoo_GOODING-ZEN4_2026-11-20_02_06_17_net11.0.trx`, `artifacts\tst\Debug\tagoo_GOODING-ZEN4_2026-11-20_02_06_44_net11.0.trx`, `artifacts\tst\Release\tagoo_GOODING-ZEN4_2026-11-20_02_06_50_net11.0.trx`, `artifacts\tst\Debug\tagoo_GOODING-ZEN4_2026-11-20_02_12_28_net11.0.trx`, `artifacts\tst\Release\tagoo_GOODING-ZEN4_2026-11-20_02_13_20_net11.0.trx`, `artifacts\tst\Debug\tagoo_GOODING-ZEN4_2026-11-20_02_34_55_net11.0.trx`, and `artifacts\tst\Release\tagoo_GOODING-ZEN4_2026-11-20_02_35_19_net11.0.trx`.

These are observations of existing returns, not adoption of the proposed stateful-promise candidate. The completed Renderer, RecordPassCallbacks, and SubmitGraph Tasks' negative owner-link results show that none is a retained-resource anchor after cleanup. No first-return funding owner, queue/refusal behavior, complete worker-failure/suspension observations, or positive Scene-backed diagnostic observation has been supplied. Preserve the 32/16-MiB NO-GO and the existing public signatures; do not introduce a request cap, fallback Task, or wrapper-elision shortcut.

The older frozen input set in `m05-skinned-normal-diagnostic-public-task-retention-boundary.md` records a previous hash for this proposal. This post-freeze update invalidates that referenced input hash; do not apply that review's disposition without a new freeze.
