# M05 diagnostic first-return owner rectification

## 1. Decision and scope

**Tentative proposal for review, not an accepted contract, available construction grant, qualified implementation, or milestone disposition. The complete 32-MiB preparation/recording and 16-MiB completion ledgers remain NO-GO.**

An all-outcome, original-owner-funded first Task **cannot be guaranteed for every valid invocation under the current contracts**. An operation-specific, stateful promise can anchor an already-funded actual return and preserve the cleanup worker's Task semantics. It cannot provide that funding when the minimum return grant itself is unavailable. The existing owners do not supply a previously admitted, inexhaustible refusal/cancellation return.

**Recommend an explicit pre-Task admission decision:** failure to acquire the complete first-return bootstrap must be reportable as an allocation-free, scalar **no-operation** outcome, with no Task output and no accepted work. Only after that bootstrap succeeds does the existing Task cancellation/execution/refusal channel begin. This is a required error-channel/precedence decision for review, not an interpretation already permitted by `Renderer.SubmitAsync`.

The current public signature cannot express that outcome. Chapters 02 and 30 must select its legal caller-visible admission form before a production diagnostic entry can use it. This proposal assigns no public member/type/enum name, numeric code, or acceptance ID. A private scalar handoff alone cannot silently repair the public boundary.

The requested tentative-approval policy can guide the bounded private owner/measurement work below; it does not establish public API acceptance or authorize full-ledger admission. This document changes no source, tests, roadmap, design chapter, existing proposal, SDK setting, budget, or native behavior.

## 2. Current-source findings

The basis is HEAD `14f520edaaaaeb03ad08096afdc92c0169a47745` and the on-disk hashes in section 8, not the older proposals' manifests. Paths are repository-relative; line ranges refer to these bytes.

| Current input | Fact and consequence |
|---|---|
| `sources\TerraFX.Graphics\GraphicsDevice.RecordPass.cs:41–98,100–170` | `RecordPassAsync` is an internal async operation. It validates, constructs `RecordPassRequest` and its stateless TCS, then attempts diagnostic queue entry. It awaits the private promise and returns a different enclosing operation. Refusal runs `Abandon`/input cleanup; `Abandon` does not complete the private promise. Directly returning it would leave that refusal pending. |
| `sources\TerraFX.Graphics\GraphicsDevice.SubmitGraph.cs:143–235,548–582` | Submission validation, pre-cancellation, use transfer, preflight and diagnostic backend-storage construction can precede the request. The request has another stateless TCS. On failed queue admission, the enclosing `finally` disposes the use wrapper, transferred uses, recorded lists, backend storage and frame reservation. There is **no current `AdmissionFailed` method** to substitute for that outer cleanup. |
| The same Graphics partials, `RecordPass.cs:162–166`; `SubmitGraph.cs:633–650` | Request `Fail` uses `TrySetException`, including for `OperationCanceledException`. Awaiting that faulted private promise through the standard async worker can produce a **canceled outer Task**. Copying the private promise's status would change the caller's observation. Cleanup can itself throw before private completion; the methods are not demonstrated exception-total publishers. |
| `sources\TerraFX.Runtime\Rendering\RenderGraphPlan.Recording.cs:13–43,83–84,205–240` | Runtime owns a separate async return, outer/per-pass builders, callback captures and previous successful command lists. A failing current list is disposed in `finally`; the outer catch disposes earlier lists. A Graphics request interest alone covers none of this enclosing family. |
| `sources\TerraFX.Runtime\Rendering\Renderer.Submit.cs:33–60,884–963` | The public entry is now a **synchronous wrapper** calling `ValidateSubmitInputs` before `SubmitAsyncCore`. The core checks pre-cancellation before submission admission and retained diagnostic-source acquisition. Thus the older claim that all public validation remains inside the async body is stale. Additional closed-admission/retain checks still occur in the core; this is not a claim that every precedence edge is qualified. |
| The same Runtime partial, `:743–815,817–881`; `Renderer.SubmissionAdmission.cs:17–85` | Success follows recording, submission, readback, typed-row creation and Diagnostics publication. Receipt construction precedes the resource `finally` and the enclosing using-declaration disposals. The existing active diagnostic lane and shutdown drain count operations, not historical returned Tasks or bytes. A relay must follow the **entire completed core worker**, not receipt construction, the graph fence, or an inner `finally`. |
| `sources\TerraFX.Runtime\Rendering\RenderSnapshot.cs:332–383`; `sources\TerraFX.Runtime\Entities\WorldDiagnosticFundingControl.cs:9–94`; `WorldDiagnosticFundingUse.cs:7–31` in the same Entities folder | The snapshot retains an exact non-emitting source funding use; submission acquires another use under the snapshot's retain/dispose gate. The funding control retains the original domain source envelope until its final use and zero attributed payload balance. Its retain counter and `EnvelopeBytes` are **not** M05 Task construction/suballocation grants. |
| `sources\TerraFX.Graphics\GraphicsOwnerRequestQueue.cs:13–56,268–310,318–344,473–482` | The accepted request backing is now a fixed array, with nonwaiting diagnostic entry and ordinary-writer promotion. Diagnostic entry can refuse for backing, contention, closure, full storage or unavailable graph-retention cells. Its backing-only check does not fund requests, Tasks, exception controls or preceding Runtime allocations. The existing 256 accepted slots do not bound refused calls or caller-held returns. |
| `sources\TerraFX.Graphics\GraphicsDevice.cs:78–85,1102–1129,1502–1520`; `GraphicsDevice.SubmitGraph.cs:14–15,37–54,281–315,504–546,633–650,682–706,743–777` | Graphics now has a process-rooted device retention anchor containing whole `SubmitGraphRequest` cells, claimed during queue acceptance. Uncertain failure marks the cell quarantined; pending-list removal does not erase that root. This corrects the older claim that only copied payload/use bags retain uncertain graph work. It still supplies no qualified first-return grant or complete fence-worker/signal footprint. |
| `sources\TerraFX.Runtime\Rendering\RenderMeshC0NormalDiagnosticWork.cs:18,93–155,269–287` | The preparation guard now includes several known recording, binding, recipe/state, queue and pending-backing terms, not only the original five arrays. It remains a **known-minimum refusal check**, not a complete simultaneous-storage ledger or return-control reservation. |
| `sources\TerraFX.Runtime\Rendering\Renderer.Submit.cs:120–128,781–815`; `RenderMeshC0NormalDiagnosticGraphPass.cs:202–218,231–275` in the same folder; `sources\TerraFX.Graphics\GraphicsBufferReadbackStorage.cs:37–70` | The Core destination is created with a capped readback allowance; typed decoding allocates a separate result backing, publication adds formatting/record controls, and a scalar receipt is constructed. Capping the Core destination at 16 MiB does not reserve their joint peak or the public return/worker group. |
| `sources\TerraFX.Runtime\Rendering\RenderSubmissionReceipt.cs:8–58`; `docs\design\30-public-api-review-surface.md:5273–5294` | The current receipt contains the six submission/fence/epoch properties. It does **not** expose the old proposed typed-diagnostic array. Current positive rows go through Diagnostics. Do not use the earlier receipt proposal as an implemented return contract. |
| `sources\TerraFX.Runtime\Scenes\PreparedScene.cs:86–145`; `SceneLoader.cs:170–211` in the same folder | `QualifiedFootprint` is null. A non-pre-canceled public Scene preparation reaches the fail-closed guard before constructing the candidate owner. Scene-backed positive diagnostic consumers therefore remain blocked, independently of first-return design. |

The [return-proof sequence](m05-skinned-normal-diagnostic-async-return-proof-sequence.md), section 7, records earlier compiled/retained-Task observations at another revision. In current tests, `GraphicsPipelineRequestTests.cs:334–429`, `SubmitGraphAsyncTaskRetentionTests.cs:183–217`, and `EngineHostGraphSubmissionTests.cs:454–466` explicitly test missing post-completion owner links and cancellation behavior. Their source is relevant; their earlier results are not a fresh execution or footprint certificate for this revision.

The current-source inspection also establishes a useful BCL option. The selected SDK's `.dotnet\packs\Microsoft.NETCore.App.Ref\11.0.0-rc.3.26522.148\ref\net11.0\System.Runtime.xml:71680–71689` declares `TaskCompletionSource<TResult>.TrySetFromTask(Task<TResult>)`, copying result, exception and cancellation information from a completed Task. The [public API documentation](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.taskcompletionsource-1.trysetfromtask?view=net-11.0) agrees. This is an available semantic candidate, **not an executed proof** of a changed TerraFX return or a Task-size bound.

## 3. The obstruction and the precise review decision

### Why another funded promise is insufficient

Take a valid snapshot/plan while the original owner's available headroom is smaller than the complete minimum return group. That can arise from still-live work/quarantine or previously retained, charged returns; it does not require a full request ring. The current ordinary cancellable Task contract still requires a canceled Task for valid pre-cancellation, or a contextual faulted Task for expected admission refusal (`docs\design\02-dotnet-api-memory-and-ownership.md:99–111`).

The proposed return/TCS/control/terminal-storage group has a positive construction obligation. No measured byte value is needed to see that an unavailable grant cannot fund it. An async throw, `Task.FromException`, `Task.FromCanceled`, or a separately cached fault Task does not create an original-owner grant.

A finite preallocated return pool merely moves the same obstruction to its exhaustion. A single terminal Task cannot preserve arbitrarily distinct cancellation tokens, success receipts or contextual failures. The active diagnostic lane does not limit sequential retained canceled/refused returns, and the request-ring limit applies only to accepted entries. Introducing either as a new return/request cap would change the contract rather than solve the byte problem.

The [public-Task retention proposal](m05-skinned-normal-diagnostic-public-task-retention-boundary.md) identifies this cardinality problem and proposes making nonadmitted observations caller-owned from construction. **That is a different funding decision, not an all-outcome original-owner-funded implementation.** It is not adopted here as an implicit Task-overhead exemption. A later proved caller-observation transfer may end an engine obligation, but transfer after completion cannot manufacture missing entry headroom during a concurrent refusal.

### Selected minimum change for review

Separate **first-return admission** from **work admission**:

1. Validate usage/owner/target identity through the established synchronous channel.
2. Before an ordinary Task operation exists, the diagnostic entry may report scalar unavailability if its entire first-return bootstrap cannot be admitted. No Task, newly constructed refusal exception, waiter, work retention or graph submission is produced on that branch. The snapshot/plan remains reusable.
3. A successfully constructed and attached first-return group establishes the operation's sole Task channel. Valid pre-cancellation then completes that funded channel as canceled, with the original token, before work admission. Later work-capacity/queue refusal faults **that same Task** after cleanup.

The review must explicitly permit scalar bootstrap refusal **even when the supplied submission token is already canceled**: without a funded Task channel there is no canceled Task to deliver. Bootstrap reservation is for return/cleanup construction, not acceptance of cancellable work. After bootstrap success, usage-before-pre-cancellation and cancellation-before-work rules remain.

This changes the existing public delivery/precedence contract at precisely the “no minimum return available” branch. Merely hoisting `OutOfMemoryException` into the current wrapper is not selected: it changes the channel and leaves its own exception/throw construction obligation unresolved. Nor may the current Task wrapper convert the scalar outcome into an unfunded replacement Task.

The necessary public shape is a caller-visible, nonallocating admission result with a Task output valid only when first-return admission succeeds. **Its declaration remains a Chapter 30 review gate; none is fabricated here.** If that decision is rejected, retain the existing Task API and the first-return NO-GO. More Task-size measurements cannot resolve the contradiction.

Minimum-return constructor failure is another pre-operation boundary. An expected unavailable profile/headroom decision must precede construction. **Propose synchronous propagation of the original exception for unexpected bootstrap construction failure**, after safe unescaped cleanup or attachment of unreleased partial controls/failure debt to the preowned root. Do not construct a replacement exception or Task on that exit. This explicit pre-operation channel, including its throw/dispatch construction obligation, must be reviewed and qualified with the bootstrap; it cannot promise delivery through a TCS that was never constructed. The requested guarantee is a logical admission/ownership guarantee, not guaranteed CLR allocation success.

## 4. Concrete private owner design after that decision

These are proposed responsibilities, not currently available types or byte grants.

### Owners and strong paths

Rendering owns its two exact families: `Task<ImmutableArray<RecordedPassCommandList>>` and `Task<RenderSubmissionReceipt>`. Graphics owns `Task<RecordedPassCommandList>` and `Task<GraphicsGraphSubmissionResult>`, including their requests and native completion controls. Use operation-specific controls, not a generic async framework.

The existing Renderer/GraphicsDevice lifetimes must each have a **preconstructed, owner-funded entry/retention root** before exposing the relevant diagnostic capability. Its full construction, synchronization, backing and failure-retention slots belong to the appropriate retained baseline. It may not be lazily created by a refused call. Current startup fields and source-account credit are not proof that this root has been funded. Until those exact baseline terms are qualified, the capability remains unavailable.

Each admitted attempt has one closed return control and original construction interest:

```text
actual returned promise Task
  -> its public TCS constructor state / AsyncState
  -> this operation's return control
  -> original charged scalar construction interest

original layer's preowned operational root
  -> the same control
  -> gated cleanup worker, relay and their last-return obligations

Graphics' existing retention anchor
  -> accepted/quarantined request
  -> that same Graphics construction interest and live worker obligations
```

The Task must be the actual promise Task; no further compiler-generated outer Task is returned around it. Existing async bodies remain owned **execution/cleanup workers**, not discarded wrappers. The scalar funding holder must not retain a Renderer, World, snapshot, plan, callback or obsolete device merely to preserve its debit. Active operational references are separate and detach only after safe consumption or a qualified ownership-retaining handoff.

Rendering checks and keeps its parent reservation before requesting Graphics-owned scalar plans/grants. Graphics receives only its own private, exact-family/epoch/capacity capability; it receives no Entities funding use, Runtime control, upward sizing callback or release callback. A parent charge remains held until the lower-owned obligation's explicit disposition is observed. Transferring an interest changes its holder, not its original payer or available headroom.

### Required preallocation and entry order

1. **Plan without allocation.** Determine the supported compiler/runtime/build profile, exact return family, frozen identities/capacities, retained baseline and complete first-return construction group. Include the control/grant itself, outward TCS/Task, start gate, actual compiled worker, relay/notification, required execution-context/continuation storage, and terminal exception/cancellation/cleanup holders. Unknown or overflowing amounts mean scalar bootstrap unavailability, not a zero charge.
2. **Reserve before factory.** The original layer atomically reserves that complete group under its existing envelope and installs its unescaped-construction role in the already funded root. No request, promise, registration or exception is allocated to discover whether it fits. Partial construction and failed cleanup retain their actual remaining debt in that root.
3. **Construct before execution.** Construct the stateful outward promise with `RunContinuationsAsynchronously`. Create the operation-specific worker behind a preconstructed start gate, so it cannot begin recording, native work, publication or input borrowing while bootstrap assembly is incomplete. Attach the real worker and its funded completion relay to the operational root before releasing that gate. The gate/relay are internal construction obligations, not fallback outward returns.
4. **Establish the sole channel.** Only a complete, linked group yields a valid Task output. Then release the start gate outside locks. The worker performs pre-cancellation before work admission. First-return construction failure is handled before this boundary; work rejection after it has an existing promise and cleanup owner.
5. **Admit the complete work peaks.** Before the first operational wait/allocation, check both ledgers beside their retained baselines and bootstrap charges. Reserve lower request/return, recording, preflight and fence-worker/signal groups before their materialization/acceptance. Runtime recording derives its child construction obligations from the real ordered pass/callback set, not 256 guessed requests. A child bootstrap refusal is delivered through the already-funded parent operation; it does not create a child fallback Task.
6. **Preserve worker semantics.** After the entire owning cleanup worker completes, use `TrySetFromTask` on the outward promise to copy that worker's terminal state. In Graphics this means the completed outer execution/cleanup worker, **not** the private request TCS. Required cancellation evidence includes the original exception object and token in `GraphicsPipelineRequestTests.CompletedRecordingTaskPreservesCallbackCancellation`; replacing it with `TrySetCanceled(token)` alone is not presumed equivalent.
7. **Retain publication tails independently.** The operational root retains the relay's actual Task and entry/publisher suffix interests even if the outward promise becomes terminal immediately. Attach-before-start also covers completion before the first await or before the entry call returns. Release cannot be inferred from assignment of the worker field, a relay's own `finally`, `TrySetFromTask`, or `RunContinuationsAsynchronously`.

An external owning join/disposition must establish the actual worker/relay return before removing those interests. For Graphics, the device's existing owner processing/retention machinery supplies the owning location; Rendering's submission/drain integration must explicitly include the corresponding workers. A self-join or a completion notification still executing its own tail is not that witness. If the exact Runtime caller/publisher-return handshake is unavailable, keep its charge/root and the reuse gate closed; do not invent a poller, generic scheduler or GC refund.

## 5. Terminal transitions and failure ownership

The following transition names are descriptive private protocol states, not new public status identifiers. Every transition after first-return admission refers to the **same actual outward promise and original grants**.

| Event / transition | Task and ownership disposition |
|---|---|
| Bootstrap unavailable, before operation creation | Scalar no-operation outcome; no Task output or work acceptance. Existing input and owner obligations remain unchanged. No cancellation/fault Task is allocated as a fallback. |
| Bootstrap construction fails before the channel is established | No successful operation admission. Synchronously propagate the original construction exception through the proposed pre-operation channel, after safe cleanup or rooted retention. The preowned root retains partially materialized controls and failed-cleanup debt; no refund just because construction threw. |
| Linked → pre-canceled | The funded worker produces cancellation before work admission; its completed state is copied to the sole promise. Task state retains the original return interest; entry/relay tails remain independently rooted and charged. |
| Linked/working → work or queue refused | No graph acceptance. Record recording abandonment or submission transfer cleanup on the original worker. Earlier independently accepted uploads/resources keep their own obligations. Complete the same outward promise only after safe cleanup or a qualified retained-failure handoff; never return the private, still-pending request promise. |
| Working → success | Preserve ordered command-list membership and submission/fence/epoch results. Runtime additionally finishes real readback, complete row validation, Diagnostics publication and all enclosing cleanup/using scopes. Copy the completed owning worker's result; no partial/empty-success substitution. |
| Working → ordinary execution fault | Preserve the worker's original failure/context and safe cleanup outcome. Error dispatch/container storage belongs to the prepaid group. The outward Task and operational root retain the remaining scalar/control debt; an exception reference is not proof that obsolete operational roots detached. |
| Private request faults with `OperationCanceledException` | Keep the private request fault observation distinct from the canceled enclosing worker. Copy the latter's completed state, preserving the required cancellation information/identity; do not blindly `TrySetException` or manufacture a new cancellation exception on the outward promise. |
| Cleanup fails or native ownership becomes uncertain | First transfer every unreleased obligation and the same grant to a preadmitted retaining owner. Graphics keeps the existing quarantined request cell; Rendering keeps its own source/publication controls and any necessary source use without giving Graphics Runtime-owned controls. Preserve primary failure plus required cleanup context using qualified storage. If the handoff cannot be proved, retain the roots/charges and do not claim release or a safe terminal transition. |
| Cancellation races acceptance / an observer cancels `WaitAsync` | Preserve each owner's acceptance boundary. Accepted graph work and its original Task are not withdrawn by the submission token or an observer cancellation. Native consumption, fence/signal workers and uncertainty debt remain funded. |
| Outward Task terminal → retained observation | Its state still roots the return control/interest after request removal and safe input detachment. No reusable credit follows from terminality, an empty queue, lane/frame-slot reuse, result cleanup or GC. Any later caller-observation transfer requires its own reviewed ownership and actual-last-access proof. |

Current `ReleaseWork` and Runtime cleanup contain sequential operations that can throw before later releases (`GraphicsDevice.SubmitGraph.cs:743–757`; `Renderer.Submit.cs:817–881`). A new owner protocol must attempt independent safe cleanup and retain each unsuccessful release before reporting terminal failure. The existing source is not evidence that all those obligations already close. Do not add an unbounded AggregateException chain, lose the primary failure, or complete a promise while a still-owning producer accesses its inputs.

This proposal selects **no reusable managed-return refund**. Retained original-owner interests may legitimately exhaust finite headroom; the proposed scalar pre-Task channel reports that condition without inventing a retention cap. The [public-Task boundary](m05-skinned-normal-diagnostic-public-task-retention-boundary.md)'s separate post-detachment ownership decision remains necessary for a reusable caller-observation transfer or complete owner retirement. Such a transfer would end an engine obligation, not assert physical free or retroactively make first construction caller-funded.

## 6. Ledger placement and original funding

Keep the unchanged joint **33,554,432-byte preparation/recording** envelope and independent **16,777,216-byte completion** envelope. These are not interchangeable credits or certificates of current availability.

| Group | Original owner / existing envelope |
|---|---|
| Graphics record request, private promise, actual diagnostic return control, gated execution/relay and terminal holders | Graphics-owned; funded within Rendering's joint preparation/recording reservation beside the retained plan. |
| Graphics submit request/return/cleanup group, preclaimed fence worker/signal, native-uncertainty retention | Graphics-owned; the same joint envelope, not a second 32 MiB or the Core readback grant. |
| Runtime recording return, builders/captures, child-handoff and cleanup controls | Rendering-owned; the same joint envelope, including actual capacity and simultaneous copies. |
| Runtime public submission return/control, execution/relay/terminal group, raw-plus-typed decode/publication and scalar receipt | Rendering-owned; the independent completion envelope, reserved from entry beside all retained completion obligations. Core-owned raw storage retains its specific existing subgrant. |
| Persistent entry roots/shared controls and unescaped-construction/failure retention | Explicit retained baseline in the corresponding owner/envelope, funded before capability publication. Never a free static, an “API overhead” allowance, or uncharged first-use initialization. |
| Plan, command, binding and native payloads referenced by a return control | Stay with their original owners/ledgers. A completion-control reference does not move preparation bytes into completion, or return native-resource credit. |
| Diagnostics-service/source attribution and retained World source envelope | Keep the existing Core/service and Entities source-account accounting from the protected route. Account identity, a retained use, or unused `MaximumDiagnosticBytes` is not authority to pay M05 Task storage. |

For each phase, add the **still-retained original baseline**, unspent construction obligations and actually materialized simultaneous storage once. A grant and the allocation consuming it are one obligation, not two. Include bootstrap/work overlap, worker/relay coexistence, growth and failed-cleanup/quarantine. Unknown footprints stay unavailable; actual lower-owner return/disposition is required before changing the parent's debit. No logical count, reference stride, current-thread allocation delta or guessed Task tariff closes a row.

A necessary-only preparation check or the current capped readback creation cannot be promoted to either complete inequality. The separate request/channel, physical preflight/backend capacity, alias, quarantine-publication, service allocation-profile and completion-result gates remain open at their own owners.

## 7. Validation and implementation gates

These are required future observations, **not results of this document**.

| Gate | Required closure |
|---|---|
| Caller-visible bootstrap decision | Review the scalar no-operation/error channel, Task-output validity and pre-canceled precedence against Chapters 02/30. Keep the current public diagnostic entry unqualified until its legal shape is selected. Separately disposition any public-observation transfer; do not assume caller funding from construction. |
| Preowned roots and closed construction | Qualify each original owner baseline and exact four-family bootstrap/work group, including constructor failure, error holders, startup gate, relay and their backings. Use the selected SDK's actual compiler/lowering and each claimed runtime/architecture/Debug/Release profile. The RC3 recording profile is not silently widened to GA or NativeAOT. |
| Funding refusal | Exercise complete exact-fit/one-byte-short, unsupported profile and checked overflow, with nonzero retained baselines. Demonstrate **zero Task/work construction on bootstrap refusal** and completion of the sole funded Task on later work refusal. Retain successive returns with different cancellation tokens; verify scalar exhaustion rather than an invented cap or fallback Task. |
| Semantic relay | Use the real owner methods. Exercise ordinary faults, callback OCE identity/token, pre-cancellation, invalid usage plus cancellation and queue full/contended/closed/retention-cell refusal. Record private versus worker versus outward Task status. Prove `TrySetFromTask` matches the completed owning worker on the actual target runtime. |
| Actual entry/worker/publisher return | Freeze the candidate's compiled inputs and inspect pending and fast-complete paths, worker/relay roots and dropped/retained outward Tasks. Coordinate completion before attachment/first observation and during publication/cleanup. Verify the stateful actual return retains its intended interest, obsolete safe inputs detach, and a running tail cannot refund itself. |
| Cleanup, uncertainty and drain | Exercise constructor unwind, every owned release-failure frontier and accepted/uncertain work. Join real worker/signal/publication tasks; verify failed/quarantined interests survive pending/lane removal and host recovery/shutdown. Managed/fake failures cover only their exercised protocol, not real driver loss/timeout. |
| Whole ledgers | Instantiate both complete, conservative phase maxima and original-owner transfers for the final implementation/profile. Backing-only, scheduling or semantic Task evidence closes neither full ledger. |
| Real producers and consumer qualification | The M03-B Scene preparation root/full numerical funding and producer last-access gates must close before the real Scene/camera/World `Extract` → graph → diagnostic submission witness. No fabricated snapshot, injected palette, unsupported refusal or skipped test substitutes. The animated nonzero witness additionally requires its real reviewed producer. Native/backend, NativeAOT, public consumer and complete milestone acceptance remain distinct Chapter 35 gates. |

After the review decision, an unpublished, non-admitting candidate can cover the four exact families in one bounded integration batch. Do not first add a request-only TCS state link and describe Runtime as solved. Run the existing owning Graphics/Runtime NUnit selections through ordinary `dotnet` commands, Debug before Release, checking nonzero selected/executed cases and fresh TRX; use `docs\project\Building.md`. No generic acceptance runner or new test-only producer is needed.

**Current disposition: first-return owner/channel design requires the explicit review decision above; production construction grants, reusable return credit and the full 32/16-MiB diagnostic gate remain NO-GO.** Independent dependency-ready M05/M03-B work can continue without treating blocked diagnostic consumers as accepted.

## 8. Current frozen basis and preservation

Capture: `2026-11-20T20:48:28.2884689Z`; HEAD `14f520edaaaaeb03ad08096afdc92c0169a47745`. The index matched HEAD. There were five pre-existing unstaged test modifications, preserved below as **dirty-only context**, not diagnostic evidence. This new proposal was absent and is excluded from input/status digests.

The 52 load-bearing source/contract/proposal/tool-reference inputs below and five dirty-only files are frozen by **whole on-disk SHA-256**, not normalized excerpts or old proposal hashes. No schema bytes are changed; Chapter 30's existing declaration closure is a frozen input. Recheck the input set before applying a review disposition. Earlier manifests remain historical and unchanged.

The pre-draft `git status --porcelain=v2 --untracked-files=all` digest was `A3DC2C26AE78F378D5EFBEF8EF0BE95D3F583C5E604507C6387B39FD6BC42A87`. Canonicalization ordinal-sorts complete original Git lines and hashes UTF-8 without BOM, LF joins and one final LF. The existing 101 `docs\proposals\*.md` files have combined digest `33A115C59C2B9141EBD7B18BAB4C525261F3A15EE8DCAB27F8F35BF33C160903`: ordinal-sort `filename=whole-file-SHA256` lines, use the same UTF-8/LF rule, and exclude only this new file.

No build, test, allocation measurement or consumer/native/AOT execution was run for this documentation-only proposal. SDK inspection selected `11.0.100-rc.3.26522.148`; the pinned reference documentation was inspected for the BCL relay API. Neither SDK selection nor the PowerShell host's GA runtime observation qualifies the production diagnostic profile.

### Load-bearing inputs

| Path | SHA-256 |
|---|---|
| `.dotnet\packs\Microsoft.NETCore.App.Ref\11.0.0-rc.3.26522.148\ref\net11.0\System.Runtime.xml` | `A6708171773D23000BF45673B97CF0DAAA8D34566C6581DF6833C3A4A14D411A` |
| `Directory.Build.props` | `770EE4B2A422861A6F23F0D39DD0ECA361A81CB87E64EBAC4E12170F71231D7A` |
| `Directory.Build.targets` | `AEEC1D08E8037470239035812351C4045124607D67A354D482112F4D642D4105` |
| `Directory.Packages.props` | `1E0E79719EFC9259390D9CF4E63AC697561725C8CD910D2AB2541D6E5BB95A6D` |
| `docs\design\02-dotnet-api-memory-and-ownership.md` | `40F913A50434209E09941C22625A6D0722202BAB97EBAC0E153C1EA3368E2291` |
| `docs\design\19-render-graph-synchronization-and-presentation.md` | `AB673DEDBD85B7A161C5D7DC871BF5B486911DC27EC4279AC43827B005241B8B` |
| `docs\design\29-project-setup-build-and-deployment.md` | `49598161138FEBBF889F2A5176E3504AF8D8BB8481D2157FBCFBBD94D2B46604` |
| `docs\design\30-public-api-review-surface.md` | `D4F622D69ED58AB96118C36FBBF4E2D31E062A3CBF3CE3BA1D64309F662FE4ED` |
| `docs\design\35-implementation-roadmap.md` | `24EEC6E92B7AD904D552F290EF748FFC8FCE13B6B94A89EBD9F39487D4584D87` |
| `docs\project\Building.md` | `F043A352870EFB6C7D4A61998225E2508A8F4370A57E784483A41819D452075F` |
| `docs\project\Roadmap.md` | `F9598D3F8005EC0E73200F7EC30882914F15F275D238707D1F502F5948015F3E` |
| `docs\proposals\m05-diagnostics-protected-route.md` | `DC28F94EFCE9DC4D4024B3E452296867E6095E7D9F20264DFC9698218D94D321` |
| `docs\proposals\m05-diagnostics-terminal-anchor-credit-compatibility.md` | `F6F88D50FEF6614A6CB0A8C149400F3931AE6129FD131653C9A92266D46424F4` |
| `docs\proposals\m05-skinned-normal-diagnostic-async-return-ownership-addendum.md` | `F5A8A8043A3E8677CEC3F18AAC1F85994A13FC25C6CFA1BA7E3E72E5B5E60991` |
| `docs\proposals\m05-skinned-normal-diagnostic-async-return-proof-sequence.md` | `F484E085A0EE3C3DCF5F80FA7D97BBE77220691C532D0F838582497860945385` |
| `docs\proposals\m05-skinned-normal-diagnostic-budget-evidence-rectification.md` | `A8DBD9EE4EA443245391A3B8AE17EDE03E3D71DA3F824E9128641E1C94F4DB36` |
| `docs\proposals\m05-skinned-normal-diagnostic-delivery.md` | `1D9ED2736D1C2520712D1E85A75AC5F0CF81D58C1EA48158F284B24B07C0DA3D` |
| `docs\proposals\m05-skinned-normal-diagnostic-owner-queue-admission-addendum.md` | `A546D16E33EA02F0953C8496A22C6C841B2D7E1EB524E2437237166D50097F72` |
| `docs\proposals\m05-skinned-normal-diagnostic-public-task-retention-boundary.md` | `E75AB4F2DFE2C3B05EAE86DB220BFA115B40CF01291071D084F5E4F8C7BC2E74` |
| `docs\proposals\m05-skinned-normal-diagnostic-recording-budget-closure.md` | `5787039DC3BA43A862529CD9F01D82623DDFEC1B273194CC73FC50F079E0C753` |
| `docs\proposals\m05-skinned-normal-diagnostic-request-control-admission-addendum.md` | `96397B0B835D727098FF3E9314296BA97ACEE0CA7E71E24FC3B6E9F5A3A0B7B0` |
| `docs\proposals\m05-skinned-normal-diagnostic-task-waiter-lifetime-addendum.md` | `7B9D68F0C01D483BAFF0658056E46847B0BA0CCC6847C0292B909EDA06B9CE05` |
| `global.json` | `1BCE01871321C2DF82E887DA428CE5C121A6C6541FE65017FE3D249BE47E190C` |
| `sources\Directory.Build.props` | `D44373AF315523F8AF9F09F36BDED2A688E2B5C1572FCC0732310C7EF590EAD8` |
| `sources\Directory.Build.targets` | `4C0527A12E48D07401C69C06CF942F9145D6A8CCF98C4A7F052B11E463E1CE9A` |
| `sources\TerraFX.Graphics\GraphicsBufferReadbackStorage.cs` | `24E6B8286437CE3B38D42F7D8469C9845EDA16D4E981A0976812DE038A232B7D` |
| `sources\TerraFX.Graphics\GraphicsDevice.cs` | `F349F2768BB51651AC1A8CFFE5BEF76BE13471599BA31545F8F15C7552C4C65A` |
| `sources\TerraFX.Graphics\GraphicsDevice.RecordPass.cs` | `264194C27CE8E864685F00A7BA0B644A4525A5816FFAEB8D76C1D358A260C64D` |
| `sources\TerraFX.Graphics\GraphicsDevice.SubmitGraph.cs` | `96DCAD360688B13AC4549371B2FE91DC75D2A3CAC287C776C13804B1175018FC` |
| `sources\TerraFX.Graphics\GraphicsGraphSubmissionResult.cs` | `72D90BE0F3AB75C515C65A1C921DF84035678A56515AF029F7A3F1CA19372023` |
| `sources\TerraFX.Graphics\GraphicsOwnerRequestQueue.cs` | `0053FD8CFBE7AD125030A5DF75D05665F96C615FF4E80CA002352AFD29FF1EF1` |
| `sources\TerraFX.Graphics\GraphicsRecordingUseAdmission.cs` | `DD1983E585EE56E2BA2E0BA8D66D385C964C1F8C27539413E06BF0DE874B65FE` |
| `sources\TerraFX.Graphics\TerraFX.Graphics.csproj` | `E04F4E614E10B07D60A9E56B1C60E69D85B63240339587271235D079E197CCD4` |
| `sources\TerraFX.Runtime\Entities\WorldDiagnosticFundingControl.cs` | `E19FE8BD36A7AF9B06ED2BB48DDF32FB89DCD5F93DF6591F9EBB9EF609FA44B5` |
| `sources\TerraFX.Runtime\Entities\WorldDiagnosticFundingUse.cs` | `0BE478CC58C22019F5E337EBA63869BC87765BC975B50B345621053D812A6DF9` |
| `sources\TerraFX.Runtime\Rendering\Renderer.SkinnedNormalDiagnosticPipeline.cs` | `23E9E1FFBE5DE83A262D0297517696E8EF66499CAEBC6080CC1D105BB42545A2` |
| `sources\TerraFX.Runtime\Rendering\Renderer.SubmissionAdmission.cs` | `8B456AF38EED738E7A02BD233FFEB83438CBD114B59CA3967BA6103A1E082D14` |
| `sources\TerraFX.Runtime\Rendering\Renderer.Submit.cs` | `63F35194EF4BD50246D1AC8969289E38C2529CAAC9E29912B69740C97B950589` |
| `sources\TerraFX.Runtime\Rendering\RenderGraphPlan.cs` | `CF206DBB501A547170ED5CEAF8797BBB18AA1881EDFE2FB8B70DD5B2CD29F0D7` |
| `sources\TerraFX.Runtime\Rendering\RenderGraphPlan.Recording.cs` | `ED5E694C9B698F6EA3535E46F80149A97CC8F9A67F8048240525D6BABFE28316` |
| `sources\TerraFX.Runtime\Rendering\RenderMeshC0NormalDiagnosticGraphPass.cs` | `554750E946DCADFD826C2B2348550B5ECE7C78B5BBC93084CB4F1BFCFD3FBB9B` |
| `sources\TerraFX.Runtime\Rendering\RenderMeshC0NormalDiagnosticWork.cs` | `4F8D5DC6529155612D7654D2F7736276D2C683965BA4A60BED499B850F56F767` |
| `sources\TerraFX.Runtime\Rendering\RenderSnapshot.cs` | `0172B307DF892EE13EF60F7CD210517CB95E4428017247627DB0EA851FEAD028` |
| `sources\TerraFX.Runtime\Rendering\RenderSubmissionReceipt.cs` | `BDFA23FB56EC678171E7B2A6BDDFB35C0A5C2FA21DC5ABECE7A7839A80FBC2D2` |
| `sources\TerraFX.Runtime\Scenes\PreparedScene.cs` | `8A822C991792D18BBCF3D58718C7C420833C45A261F406487C7A6DD2E2EF3DF0` |
| `sources\TerraFX.Runtime\Scenes\SceneLoader.cs` | `4903A86FC323ED906A0045288BEBAB68B37C5B6BEEFF6312EA0440A9D05D453C` |
| `sources\TerraFX.Runtime\TerraFX.Runtime.csproj` | `2F1C81E54F0FBF12D01E34D0D840885F5EB0E94A8B8890865EF721B8C6A3C65D` |
| `tests\TerraFX.Graphics.Tests\GraphicsPipelineRequestTests.cs` | `C833BF14A066480AF7A7212F7E19168569346623D3E456025DA5B6CC3CCB6019` |
| `tests\TerraFX.Runtime.Tests\Hosting\EngineHostGraphSubmissionTests.cs` | `3ECB45866CDC0C429536A7C5722E3D660AC9FBE3E9A9D2A616B13A91C6F1F195` |
| `tests\TerraFX.Runtime.Tests\Hosting\EngineHostOptionsTests.cs` | `DA7B38BA5BBDAF7A27CD5A2C1DF4BEC66DDDC427A5741400A046F76AD6D5FB47` |
| `tests\TerraFX.Runtime.Tests\Rendering\RendererAsyncValidationTests.cs` | `C0717DA8BD133B2720B2DB918F21E70DC64429B02241260BA46F10C4A404DF30` |
| `tests\TerraFX.Runtime.Tests\Rendering\SubmitGraphAsyncTaskRetentionTests.cs` | `C0E8263D3A838C4C50FE866240710A7767EBACF656DD119195276F1206ED3A24` |

### Preserved dirty-only context

| Path | SHA-256 |
|---|---|
| `tests\TerraFX.Runtime.Tests\Scenes\PreparedSceneAssetRetentionTests.cs` | `E342237B7645BE805DD924855B2239907773D31753D22B8B2261BF26C20452F4` |
| `tests\TerraFX.Runtime.Tests\Scenes\RenderSpriteCapacityTests.cs` | `CAA6610CF55F8FFFA077745FD96BAA45FCECFED2FD051CC763901AEED5500239` |
| `tests\TerraFX.Runtime.Tests\Scenes\RenderSpriteComponentTests.cs` | `8E65B71B5AC1E0EF9D1F47AA56963054D091DF8C229E74DDBB1CAE37BB59AB60` |
| `tests\TerraFX.Runtime.Tests\Scenes\RendererExtractionTests.cs` | `ABAEEA4D0F6BE941AF73E640B7FCE41EC753D2199654343FA4668D54EC20FC6A` |
| `tests\TerraFX.Runtime.Tests\Scenes\SceneAssetStoreTests.EngineHost.cs` | `4C0841A40295A891B68974FDDD8078DFD0181C31B35AB0BCB0B5114FE0FD1039` |
