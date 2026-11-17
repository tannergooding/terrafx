# M05 diagnostic public-Task retention and completion-budget scope

## 1. Decision

**Tentative contract clarification for review; not an implemented ownership boundary or footprint qualification. The complete 32/16-MiB diagnostic gate remains NO-GO.**

A single aggregate **16,777,216-byte** envelope cannot include every caller-retained successful, faulted and canceled public submission Task for an unbounded number of calls. This is a lifetime/cardinality obstruction, not a missing Task-size measurement. Keeping the current API and unrestricted caller retention requires distinguishing a bounded operation/engine obligation from caller-owned observations, or explicitly limiting the guarantee to each invocation rather than their aggregate.

Recommend preserving the original **per-active-batch** completion envelope, including return/result construction and all still-engine-owned completion roots, with an explicit transfer of detached public observations to caller ownership. Completed receipts already have that boundary in the original proposal; actual returned Tasks do not yet have a sufficiently specified or demonstrated equivalent. Nonadmitted public replies also need an explicit caller-observation boundary to avoid requiring an unavailable engine grant merely to report admission refusal.

This is the narrow interpretation proposed here, **not an existing general exemption for Task/API overhead**. Human review must confirm the ownership/scope distinction in sections 4–5. Under the requested tentative-approval policy it can guide bounded design and evidence work; it does not qualify today's async boxes, enable production grants, or close either ledger. No signature, caller-retention cap, new allowance, compiler flag, code, existing proposal or dirty M03-B/M05 file is changed.

## 2. Current evidence and existing authority

References identify the whole-file bytes frozen in section 8.

| Input | Exact fact and consequence |
|---|---|
| `docs\design\02-dotnet-api-memory-and-ownership.md:87–111` | Public operations use ordinary `Task`/`Task<T>`. Terminality follows actual consumption; a canceled `WaitAsync` observation does not release the original operation. Valid pre-cancellation uses a canceled Task; expected admission failure keeps its established channel. These rules do not supply a caller Task-return protocol. |
| `docs\design\30-public-api-review-surface.md:5221–5242,5298` | Declares `Task<RenderSubmissionReceipt> SubmitAsync(...)` and an operation-produced scalar receipt. Completion confirms GPU work/pin retirement, not surrender of the Task/result by its caller. No Task-retention ceiling or receipt disposal member is declared. |
| `sources\TerraFX.Runtime\Rendering\Renderer.Submit.cs:35–42,169–185,849–930` | The actual entry is `async Task<RenderSubmissionReceipt>`. It constructs a new receipt on success. Its interlocked diagnostic lane is reset in `finally`; that is an active-work guard, not release of the returned object or a count of retained historical Tasks. |
| `sources\TerraFX.Runtime\Rendering\RenderSubmissionReceipt.cs:9–68`; `RenderSkinnedNormalDiagnostic.cs:8–54` in the same folder | The receipt is a sealed reference type with an immutable diagnostic-array property and no disposal protocol. Diagnostic rows contain GUIDs, integer counts and a tick, not resource ownership. The array can escape independently of both receipt and Task. |
| `sources\TerraFX.Runtime\Rendering\RenderMeshC0NormalDiagnosticGraphPass.cs:232–275`; `Renderer.Submit.cs:831–856` | Decode builds the complete typed array and transfers its backing with `MoveToImmutable`; raw owner/lease disposal is separate. Neither an immutable-array copy nor Task/result retention provides an engine release notification. |
| [Diagnostic delivery](m05-skinned-normal-diagnostic-delivery.md), section 3, consumer-lifetime row; section 6, completion/receipt rows | Explicitly specifies **16 MiB per active batch**, indefinitely readable caller-owned copied receipts, and **no continuing batch charge after detachment**. It does not promise a 16-MiB total across caller histories, or explicitly classify the returned async-operation object. |
| [Recording-budget closure](m05-skinned-normal-diagnostic-recording-budget-closure.md), sections 4–5 | Requires both unchanged ceilings and compiled detachment of obsolete recording roots. It says these are logical owned-storage/admission bounds, not process working-set/no-GC guarantees. |
| [Async-return ownership](m05-skinned-normal-diagnostic-async-return-ownership-addendum.md), section 3; [return-proof sequence](m05-skinned-normal-diagnostic-async-return-proof-sequence.md), section 3 and retained-Task observation | Extends construction/retention responsibility to the **actual returned Task**, including fault/cancellation returns. Assigns Runtime's submission return/receipt to completion, rejects unqualified refunds, and explicitly stops if repeated retained refusal returns cannot be funded. It does not close the public caller-ownership transition or provide finite aggregate capacity for those returns. |
| [Core completion handoff](m05-skinned-normal-diagnostic-core-completion-handoff-addendum.md), sections 3–5 | Distinguishes native free, managed controls and escaped result backing. Ownership transfer is possible; disposal, terminality and collection are not interchangeable release facts. Its buffer capability is not a Task tariff or a public-output lifetime solution. |

Chapter 04 section 2 already distinguishes retained service diagnostics from caller-owned copied query snapshots. That is a useful ownership precedent, **not authority to silently exclude submission Tasks**. Scene/particle proposals likewise do not amend this Rendering boundary.

### What the compiled census establishes

The supplied ignored record is `artifacts\logs\command-payload-owner\async-return-census-20261118.json`:

| Renderer lowering reported by that record | State-machine kind | Instance fields | `MoveNext` IL bytes |
|---|---|---:|---:|
| Debug | Reference class | 212 | 15,877 |
| Release | Value type | 104 | 12,494 |

The observed returned operations are `AsyncStateMachineBox` instances. The D3D12 pending-completion test keeps the actual public Tasks while fences are held, observes successful completion, and separately checks canceled status/token for a frame-slot waiter (`tests\TerraFX.Runtime.Tests\Hosting\EngineHostGraphSubmissionTests.cs:5292–5375`). This is not a post-completion retained-root or allocation-size observation.

The saved Graphics selections executed/passed 115 tests per configuration; Runtime executed/passed 98 of 213 discovered per configuration. The separate static-skinning Debug selection executed two cases and **failed both before rendering**. `sources\TerraFX.Runtime\Scenes\PreparedScene.cs:80–104` still returns no qualified J/K footprint and refuses entry. There is no positive diagnostic return/result observation from that selection.

Field counts, IL length and value-type lowering establish neither Task/object bytes nor the allocation/retention of an async box, captures, execution context, exceptions or workers. The census explicitly leaves those unmeasured. Its four method-source hashes match the frozen current files. Current product/test-copy DLL hashes do **not** match its recorded assembly hashes; the manifest records that distinction. Treat its binary observations as evidence of the recorded run, not qualification of today's output DLLs.

## 3. Why the literal aggregate interpretation cannot work

Consider otherwise admissible diagnostic submissions made **sequentially**, each through real World-produced inputs. Await each operation and keep its returned Task indefinitely. The one-active-batch rule is never exceeded. Each successful submission returns its own receipt with its submission identity; one mutable/recycled Task cannot supply different immutable results to earlier observers.

For any positive footprint of those distinct retained objects, their aggregate eventually exceeds any fixed finite envelope. No numeric Task size is needed for this conclusion. Independently retaining the copied diagnostic arrays produces the same obstruction even if every obsolete worker/capture is perfectly detached.

Fault/cancellation returns do not repair it:

- Repeated valid refusals still require the established faulted-operation channel. An async throw or `Task.FromException` does not eliminate return storage.
- Distinct observable exceptions/context or cancellation tokens cannot generally be represented by one recycled terminal Task. A cached refusal Task is not evidence that all required terminal families and caller continuations are bounded.
- A minimum-return reserve whose charge follows every retained public Task is finite too. Permanently retaining that charge eventually prevents reporting the next refusal through the required channel.

Awaiting, inspecting `IsCompleted`, emptying the queue, resetting the lane, releasing a frame slot, or disposing an independently owned result is not caller relinquishment of the Task. BCL `Task.Dispose` is not a TerraFX storage-credit return protocol. GC, finalizers, weak tables or assumed prompt collection cannot provide reusable admission credit.

Thus a construction interest co-retained by the returned Task can prove that a charge is not lost, but cannot by itself prove a finite reusable aggregate or continued admission. Permanently consuming grants is not lifetime closure. Restricting only *successful* admission would also leave the refusal/pre-cancellation return problem.

## 4. Compatible choices and human decision

| Interpretation | Compatibility and guarantee |
|---|---|
| **Recommended: per-active-batch engine envelope plus explicit public-observation transfer** | Keeps the Task API and unlimited historical caller retention. Return/result construction contributes to the batch's qualified peak; actual engine workers, retained controls and quarantine remain funded. Detached Task/receipt/array observations become caller-owned, not continuing batch liabilities. Pure nonadmitted replies are caller-owned from construction. Aggregate caller memory is explicitly unbounded. Requires the precise ownership clarification below and an implementation proof. |
| **Per-invocation footprint certificate only** | A 16,777,216-byte bound may include that invocation's retained Task/results without being a shared reusable quota. An unbounded number of such certificates does not bound their sum. This preserves the public API, but is a weaker claim than bounding retained engine obligations across attempts; it cannot establish safe quota refunds, quarantine ownership or engine drain. Use this description if only a per-call construction bound is intended. |
| **One aggregate 16,777,216-byte bound including every caller-retained return** | Incompatible with the stated constraints. It requires changing retention/cardinality/release requirements or abandoning a finite aggregate claim. Those changes require a separate human decision; no signature/cap change is selected here. Raising the ceiling to another finite number would not solve it. |

**Decision for review:** confirm that the completion ceiling bounds the active batch and still-engine-owned completion obligations, including construction before public handoff, **not the sum of all historical caller-owned Tasks/results**; explicitly accept pure nonadmitted public observations as caller-owned. This follows the original delivery scope and requires no new public member. It narrowly corrects the later return-interest prose where read as an indefinite shared charge on detached public observations.

If that distinction is rejected, the literal aggregate requirement remains blocked. More object-size measurements or a stateful TCS cannot resolve the contradiction.

## 5. Exact proposed budget and lifetime boundaries

Keep **33,554,432 bytes** jointly for preparation/recording and **16,777,216 bytes** independently for completion. There is no third API-overhead allowance, transfer of headroom, new count cap, or additional 16 MiB per returned Task. The existing diagnostic lane remains one active batch per Renderer/device epoch; ordinary/non-diagnostic submission behavior is unchanged.

For diagnostic admission, retain the existing required baseline and phase accounting:

```text
retained engine completion baseline
  + current batch's additional simultaneous completion/construction peak
  <= 16,777,216 bytes

retained preparation/recording baseline
  + current batch's additional simultaneous preparation/recording peak
  <= 33,554,432 bytes
```

An unspent grant and the storage materialized from it are one obligation, not two. A shared physical allocation is charged once; another reference may extend its lifetime. This is not a process-wide bound across arbitrary Renderers, epochs or caller memory.

| Group | Proposed boundary and mandatory disposition |
|---|---|
| Actual public returned Task and its observation-only controls | Enumerate and qualify their construction, including overlap with workers/results. While the same physical allocation also contains an engine worker, charge the whole allocation once; do not invent separately sized “Task shell” fields. Transfer to caller ownership only after the actual producer/worker last-access and detachment proof permits it. A terminal status alone proves neither condition. |
| Success receipt and copied typed-result backing | Include full construction and raw-plus-typed coexistence in completion. After complete validation, ended private accesses and ownership handoff, caller retention is unrestricted and no continuing batch debit follows those data-only observations. The same applies when only `SkinnedNormalDiagnostics` escapes. No engine result cache/history or result-revoking disposal is introduced. Transfer is not a claim that the managed bytes were physically freed. |
| Nonadmitted public pre-cancellation/refusal observations | Explicitly designate the closed reply family as caller-owned from its first construction, with no accepted work or engine interest to return. Qualify its per-call construction rather than demanding a new aggregate engine grant before it can report that no grant is available. It must not capture Renderer, World, graph/snapshot, request, use, resource, worker or quarantine ownership. This is the proposed bootstrap correction, **not an existing capability** or permission for an unplanned fallback Task. |
| Fault/cancellation metadata | Preserve original exception/channel/status/token semantics. Copied observation data and pre-existing caller exception/token graphs do not become engine storage merely by being observable through a Task. Conversely, an engine-created exception/control retaining engine-owned work cannot be excluded by calling it an observation. There is no claim to bound arbitrary caller continuations, `AsyncLocal` values or the complete transitive graph of a caller-supplied exception. Engine-attributable construction/context storage still needs its own qualified bound. |
| Runtime execution workers, private promises, requests, captures, registrations and publication tails | Keep their real owning interests from before materialization through deterministic last access/cleanup or a qualified ownership-retaining transfer. A public promise may already be terminal while its publisher is still returning; that tail needs an independent live owner. `RunContinuationsAsynchronously`, `SetResult` or a worker's former continuation is not the last-access witness. |
| Graphics/internal recording and submission return families | Remain owned and funded by their existing layers/envelopes. A public caller-observation distinction does not exempt every internal Task, pending writer, channel, fence worker or signal continuation. The four-family proof and separate task/waiter requirements still apply. |
| Core raw CPU destination and managed wrappers/backing | Native payload credit ends only on Core's actual-free fact. Disposed-but-retained controls keep their existing ownership until qualified detachment/transfer; they are not public scalar results. Do not expand the Core buffer allowance into Task funding. |
| Plans, commands, bindings, aliases and preflight storage | Remain on their preparation/recording owners and the joint envelope. A public Task reference does not move them into completion or make them caller-observation overhead. Independently retained plans preserve their existing per-plan ownership. |
| Failed cleanup and uncertain/native-accepted work | Preserve each original charge, roots and preadmitted carrier with the actual retained/quarantine owner. No lane/epoch reset, returned fault, canceled observer or public-result handoff releases these obligations. A later attempt cannot borrow their headroom. |

This clarification ends an **engine** obligation by an explicit, proved ownership handoff, not by collection. Unknown or inseparable engine roots stay charged. It does not authorize relabeling the current `AsyncStateMachineBox` or its whole reachable graph as caller data.

The recommended return shape may use the existing operation-specific promise/worker candidate, but only if its state/control stops retaining engine interests at the proved boundary. Keeping an engine-budget anchor forever in public Task state is not the selected solution. No custom method builder, generic async framework, hidden BCL field mutation or upward Core/Graphics callback is authorized.

The current entry's checks are inside an async body, including token checking before later graph validation. The return-proof sequence already identifies its discrepancy with Chapter 02's usage-error timing/precedence. Any future front-door implementation must reconcile that explicitly; this proposal does not silently change exception timing or implement that separate correction.

## 6. Acceptance evidence and remaining stops

| Required evidence | Acceptance condition |
|---|---|
| Scope disposition | Record which section 4 interpretation is accepted for the frozen input set. Neither a per-call footprint certificate nor an output transfer is described as a finite aggregate of all retained public objects. |
| Exact compiled families | Inspect the actual owning methods/candidate using ordinary builds, Debug before Release, recording compiler/runtime/architecture and exact DLL hashes. Qualify each claimed JIT/NativeAOT profile separately. Rebind the saved census before using different current binaries as its evidence. |
| Construction and coexistence | Measure/attribute actual Task/TCS/worker/control/backing construction on every participating thread, cold and warmed, suspended and fast-complete, success/fault/cancellation. Derive conservative closed-family bounds; field counts, IL size, reference width and current-thread allocation traffic alone are insufficient. Include the return's own construction and failure-construction paths. |
| Retain only public observations | Keep actual returned Tasks after safe cleanup and separately keep only receipt/array results. Prove the intended worker/interest detaches while public data, exception identities and cancellation tokens remain correct. Include completion before first observation and dropped-Task cases. GC-assisted root tests may supply evidence, never the release/admission mechanism. |
| Last producer access | Exercise publication/return races, not just await completion. All worker/publication tails retain a real funded owner until their last access. No early refund, lost worker failure, unobserved fire-and-forget path or dependence on a continuation needing the releasing thread. |
| Repeated calls and refusal bootstrap | Repeatedly retain successful, faulted and distinctly canceled public Tasks without increasing **engine** retained obligations after proved transfer. Caller-observation allocation is reported separately, not called allocation-free or constant total memory. Validate full/contended/closed admission, pre-cancellation, failed construction and unsupported profiles without an unfunded replacement return. Finite tests support the protocol proof; they do not prove an unbounded aggregate fits. |
| Complete ledgers | Derive exact-fit/one-byte-short phase checks with nonzero retained baselines and simultaneous construction. Unknown footprints, growth, aliases or failed release remain unavailable/charged. Whole-graph refusal and complete result membership remain mandatory. |
| Real diagnostic consumer | Retry only after the genuine Scene J/K footprint/producer last-access gates and all applicable diagnostic profile/owner gates are satisfied. Use a World-produced snapshot and real submission/readback. The current two pre-render failures are unsupported positive observations, not diagnostic-return passes. |

No test, build, allocation probe or native experiment is run by this documentation change. Existing behavioral selections and the saved census do not close these stops. Full diagnostic admission, native/public-consumer qualification and AR08/M05 acceptance remain distinct.

## 7. Work that can continue without this decision

- Contract-complete CPU kernels/layout and shader/content-adapter work with their actual available inputs, under Chapter 35's per-capability sequencing. Their evidence does not establish a public diagnostic return.
- Independent preparation/recording/preflight/alias and quarantine ledger work whose real owners/contracts are already closed. Do not enable a complete-admission profile or take public-return refunds from this proposal.
- Read-only compiled-family inspection and existing owning behavioral tests, with no guessed tariff or new production grant. A bounded candidate must still respect the return-proof sequence's entry-design and qualification stops.
- M03-B J/K catalogue, supplier-return, last-access and actual cleanup/receiver work under its own owner proposals. This Rendering distinction neither funds Scene preparation nor bypasses its fail-closed guard.

Live animation/animated palettes, particle/LOD/picking integration and native/public consumer observations retain their own real producer prerequisites. Do not manufacture a snapshot or synthetic diagnostic result to work around this decision or the Scene gate. No unrelated ready M05/M03-B unit needs to wait for an impossible aggregate-Task proof.

## 8. Frozen review inputs

Review HEAD: **`3670e8341e5c1e26ceadfdf8213c929cd8fcb5af`** (`Verify async return task behavior`), after return-proof sequence commit `ace80e10`. HEAD alone is not the reviewed input: the initial **79 modified/untracked files** were separately hashed, including dirty design/source/schema-bearing inputs. No schema or fixture is changed, and no new serialized contract is proposed.

Ignored local evidence:

- `artifacts\logs\command-payload-owner\task-retention-contract-review-inputs-20261118.json`: full initial changed-file inventory, whole-file byte lengths and hashes; SHA-256 **`A7EFEDC8821DCF696C87030840E63C97B92CCFEC0EBF6831EF19B214998DC15E`**.
- `artifacts\logs\command-payload-owner\task-retention-contract-review-evidence-20261118.json`: **52** inspected contract/context/source/evidence inputs, including the saved census, five TRX files, current product/test-copy DLLs and their census mismatch records; SHA-256 **`170D2B8F194BDAE8226DE56E0E97D041CAB75D448395102C7E0782129FCEAEA6`**.
- Exact `git ls-files --stage -z` byte-stream SHA-256: **`088E7C19040627CFB7379C2EE212F089BF93B5FD6C1476CD7FF57B8D4DB946E3`**.

Load-bearing whole-file hashes, not normalized excerpts or HEAD blobs:

| Input | SHA-256 |
|---|---|
| `docs\design\02-dotnet-api-memory-and-ownership.md` | `40F913A50434209E09941C22625A6D0722202BAB97EBAC0E153C1EA3368E2291` |
| `docs\design\04-capacity-and-workload-profiles.md` | `A4D86F3FA373A64548435C53582BF5916E29D77F34650F9D161CCEC90E6F0245` |
| `docs\design\30-public-api-review-surface.md` | `15848C82F3858A77A87A0D3EA6FBCC07046EB6E77CA5B21748C72B90EFAB598B` |
| `docs\design\35-implementation-roadmap.md` | `24EEC6E92B7AD904D552F290EF748FFC8FCE13B6B94A89EBD9F39487D4584D87` |
| `docs\proposals\m05-skinned-normal-diagnostic-delivery.md` | `1D9ED2736D1C2520712D1E85A75AC5F0CF81D58C1EA48158F284B24B07C0DA3D` |
| `docs\proposals\m05-skinned-normal-diagnostic-core-completion-handoff-addendum.md` | `958C9AAC244915B8981A6E7014DDFB948A3F5E6F058E07A84CC4609E93C386FF` |
| `docs\proposals\m05-skinned-normal-diagnostic-task-waiter-lifetime-addendum.md` | `7B9D68F0C01D483BAFF0658056E46847B0BA0CCC6847C0292B909EDA06B9CE05` |
| `docs\proposals\m05-skinned-normal-diagnostic-async-return-ownership-addendum.md` | `F5A8A8043A3E8677CEC3F18AAC1F85994A13FC25C6CFA1BA7E3E72E5B5E60991` |
| `docs\proposals\m05-skinned-normal-diagnostic-async-return-proof-sequence.md` | `8D9FFF9D9E67E32DC9F4C9F01C735E444AC99E1A8A353FBED918450FBA54965D` |
| `sources\TerraFX.Runtime\Rendering\Renderer.Submit.cs` | `8BF0D0662C9D3DE0A1670ED05C89FAB1E6A749D744BF796A9D5E0236BF4DDB79` |
| `sources\TerraFX.Runtime\Rendering\RenderSubmissionReceipt.cs` | `77120676F289AD7D8891875A783BAFA05E64B7F0317BC09BECC2545AC42E5846` |
| `sources\TerraFX.Runtime\Rendering\RenderSkinnedNormalDiagnostic.cs` | `E07287C05EA0E4A12F5ADF2FABCDC48B191C2D753B20E2636447CD83F1E2A41E` |
| `sources\TerraFX.Runtime\Rendering\RenderMeshC0NormalDiagnosticGraphPass.cs` | `554750E946DCADFD826C2B2348550B5ECE7C78B5BBC93084CB4F1BFCFD3FBB9B` |
| `sources\TerraFX.Runtime\Scenes\PreparedScene.cs` | `B73DD4314B63084132AFC2F9325ED98EECCCE65A174F02B16F2DED7EC65F688C` |
| `tests\TerraFX.Runtime.Tests\Hosting\EngineHostGraphSubmissionTests.cs` | `B907CC9885DB0407B744F0ED4DA9514A152932EB3A1ABBDE525CC96B194C91F9` |
| `artifacts\logs\command-payload-owner\async-return-census-20261118.json` | `8C17BF8600E76C90A8EAB8C1BD4A3D10D608C39DE46974E6969B416C8ED4C081` |

Recheck HEAD, index, input hashes and initial changed-file inventory before applying a disposition. Acceptance applies only to these bytes and this public-return budget scope, not unmeasured construction sizes, unseen compiled roots or another revision.
