# M05 diagnostic request-control admission addendum

## 1. Exact remaining decision

**Tentative implementation unblocker, pending review. Request-control admission and the complete 32/16-MiB diagnostic gate remain NO-GO.** This narrows the still-missing control row in [owner-queue admission](m05-skinned-normal-diagnostic-owner-queue-admission-addendum.md); it does not treat the implemented backing check as a complete byte grant.

Select **Graphics-owned, profile-qualified construction grants for the actual record and submit request families**, obtained before either request/TCS is constructed. Runtime funds those subgrants within its still-retained joint baseline and phase maxima. Preserve the joint **33,554,432-byte** and independent completion **16,777,216-byte** ceilings, command/result membership, native completion, cancellation, quarantine and public signatures.

No unmeasured Task, cancellation-registration, synchronization, continuation or shared-control footprint is assigned a guessed fixed overhead. No new diagnostic/rejection ID, World/Scene callback, public budget API or general renderer allowance is proposed.

## 2. What the current queue batch actually establishes

The request-storage owner is now `GraphicsOwnerRequestQueue`:

- The existing **256-request** capacity is backed by one explicit, fixed `IGraphicsDeviceWork?[]`, not a lazily growing channel request buffer. Its known conservative charge is `64 + roundUp16(capacity * referenceStride)`; at x64 this is **2,112 bytes**. This is a persistent shared backing interest, not a new backing per request.
- The private diagnostic entry checks that backing component and uses nonwaiting entry on the existing queue gate. Full, contended, closed or insufficient-backing entry returns refusal without retaining the work, creating a waiter, growing the ring or copying the request.
- Ordinary waiting producers remain outside the accepted ring until their work is promoted. The reader promotes the oldest uncanceled producer into the freed slot **under the gate**, before completing its admission outside the gate; a diagnostic producer cannot overtake that ordering. Cancellation removes only unaccepted work. Close rejects pending producers and permits already accepted entries to drain.
- Recording callbacks throughout a diagnostic-bearing Runtime graph, and its Graphics submission, select the nonwaiting entry. A failed handoff aborts the unsubmitted operation through the existing contextual failure/cleanup path; there is no fallback to ordinary queue waiting.
- Ordinary pending-writer controls detach their work on acceptance/cancellation/close and detach queue/token references after cancellation-registration disposal. This is an ownership mechanism, not a qualified numeric bound for those controls.

These are supported backing/ordering/refusal mechanisms. They do **not** preclaim every control object allocated before reaching the ring:

| Still-unqualified owner | Current gap |
|---|---|
| `GraphicsDevice.RecordPass.cs`, `RecordPassRequest` | The request constructs `TaskCompletionSource<RecordedPassCommandList>`; its Task and the compiled outer async operation/control storage have no complete, owner-issued byte grant. Construction currently precedes the nonwaiting queue entry. |
| `GraphicsDevice.SubmitGraph.cs`, `SubmitGraphRequest` | A distinct request/TCS/task and use-transfer/control graph is constructed after preflight and before queue entry. The recording request's allowance is not authority for this family. |
| `GraphicsOwnerRequestQueue` | The ring's array charge does not include the queue object, existing shared gate/event, retirement-channel controls, ordinary pending-writer/TCS/cancellation machinery or attributable cold initialization. Existing shared storage needs its actual owner/funding disposition, not a zero-cost exclusion. |
| Runtime recording/handoff | Pass builders, recipe/callback controls and handoff copies can already exist before Graphics sees the request. They remain with their Runtime owner until a qualified transfer; the queue cannot retroactively fund them. |

`maximumBackingBytes` at the private entry is specifically a **backing-only subcheck**. Passing the known backing charge back to that check is not a request/control reservation or a full-ledger certificate. Likewise, a free count slot is not a byte credit. Until the following capability exists, request-control qualification remains unavailable and no full diagnostic budget admission/GO may be inferred from successful implementation-level execution.

## 3. Minimum private owner capability

1. **Nonallocating per-family construction plan.** Graphics owns complete plans for `RecordPassRequest` and `SubmitGraphRequest`, including their separately allocated TCS/Task objects, actual compiled async controls, native-owner handoff controls and retained backing. Identify the exact SDK/runtime/architecture/build profile and source/control shapes. Do not use a class reference stride as its object size, assume compiler copy/state elision, or call a sizing probe while constructing the plan.
2. **Grant before factory.** Check the complete request/control amount beside Runtime's retained baseline before constructing the grant/carrier, request or TCS. Any carrier is included in an already funded construction amount. A private factory consumes that one-shot grant and verifies its frozen family, profile, graph/device epoch and capacities before publication. Unknown/excess/checked-overflow construction refuses before queue retention, not after allocation.
3. **One ownership interest through the queue.** Atomic successful entry transfers the same request and construction interest to the queue/owner executor. Refusal leaves no queue reference, waiter or callback root and unwinds safe untransferred interests. No second accepted waiting queue, duplicated payload or new independently lendable allowance is introduced.
4. **Task/control retention is separately owned.** Dequeue/count-slot reuse does not end request/TCS/task storage. Detach obsolete captures and metadata at actual consumption end; keep every remaining control/backing charge with its real retained Task/request/result owner. Disposing a lease, completing/faulting a TCS, source scope exit or a cleared ring slot is not proof that all managed roots ended.
5. **Cancellation and closure have a closed handoff.** For ordinary pending producers, cancellation wins only before acceptance at the queue gate; accepted work cannot be withdrawn. End cancellation registration before releasing its queue/token root. For diagnostic work, preserve usage/identity/version and pre-cancellation precedence before the nonwaiting attempt. If the attempt refuses, perform safe request cleanup before its operation Task becomes terminal.
6. **Uncertainty transfers the same grant.** A native-accepted or uncertain submission retains request/control interests and its preadmitted quarantine carrier until actual release/qualified retained ownership. Neither count-slot/frame-slot release nor an observer's cancellation/fault returns still-live bytes. The separately unqualified quarantine publication/copy row is not solved by request construction.

These are requirements for a real private factory/owner capability, not declarations of currently available types. They preserve the existing Graphics-to-Core dependency direction and require no higher-layer sizing callback.

## 4. Smallest dependent batch and gates

After review, qualify the actual record/submit construction groups and their compiled roots using the existing owning harness. Add only the corresponding private scalar plans, one-shot grants and factories, then carry them through the current ring entry and terminal/refusal paths. Update Runtime's complete retained-baseline/phase accounting at the real construction boundary rather than adding another full ceiling.

Focused checks must include exact-fit/one-byte-short **complete control** grants, unknown-profile refusal, frozen family/epoch/capacity mismatch, failed constructor unwind, actually full/contended/closed admission, canceled-versus-accepted producers, independently retained Tasks and uncertainty transfer. Prove control/backing sizes and roots for the exact compiled implementation; the fixed-ring zero-allocation write test and use-node allocation test do not establish them.

Use ordinary owning Graphics/Runtime Debug tests, then Release, with nonzero selections. Native failure/execution, NativeAOT and public-consumer observations remain separate. No extra control-size/runtime probe is performed by this document.

**Current disposition:** fixed backing and nonwaiting/FIFO mechanics are implemented independently. The complete request/control grant is still missing, as are other preflight/backend scratch, quarantine-carrier, payload-alias and completion-ledger terms. Both unchanged diagnostic envelopes and whole-graph budget qualification remain **NO-GO**.

## 5. Frozen static inputs

Whole-file SHA-256 values identify the narrow dirty-workspace inputs based on HEAD `b09aa49ed077dbe07dec6cdf9019e6a54876bf49`. HEAD alone is not the reviewed input. Recheck before applying a disposition; this is static source evidence, not control-footprint qualification.

| Input | SHA-256 |
|---|---|
| `docs\proposals\m05-skinned-normal-diagnostic-owner-queue-admission-addendum.md` | `A546D16E33EA02F0953C8496A22C6C841B2D7E1EB524E2437237166D50097F72` |
| `sources\TerraFX.Graphics\GraphicsOwnerRequestQueue.cs` | `B53D2EB1C14874A544519A46CE19288B334BF50EFFE86B6C807289F0BA743689` |
| `sources\TerraFX.Graphics\GraphicsDevice.RecordPass.cs` | `264194C27CE8E864685F00A7BA0B644A4525A5816FFAEB8D76C1D358A260C64D` |
| `sources\TerraFX.Graphics\GraphicsDevice.SubmitGraph.cs` | `C68E23B5FFA591C639714101FB23A1F1AE883FAC28F722EEB5B034725C3A97FD` |
| `sources\TerraFX.Graphics\GraphicsGraphSubmission.cs` | `04A17FC4704EB697927EB963FCCBD3593C7CACCE9CD81290A265CF48846C052C` |
| `sources\TerraFX.Runtime\Rendering\RenderGraphPlan.Recording.cs` | `ED5E694C9B698F6EA3535E46F80149A97CC8F9A67F8048240525D6BABFE28316` |
| `sources\TerraFX.Runtime\Rendering\RenderMeshC0NormalDiagnosticWork.cs` | `2A2822D28BC48C76703636CE7C58706E54FA6620593A0CE65CCAB20971E376E6` |
