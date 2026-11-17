# M05 diagnostic recording and preflight workspace admission

## 1. Decision and unchanged gate

**Tentative implementation unblocker, pending review. The complete diagnostic budget gate remains NO-GO.** This supplements [budget-evidence rectification](m05-skinned-normal-diagnostic-budget-evidence-rectification.md) and [recording-budget closure](m05-skinned-normal-diagnostic-recording-budget-closure.md); it does not amend their ceilings or claim implementation acceptance.

Select one **Graphics-owned, preclaimed recording/submission workspace** for the diagnostic-bearing graph, with Runtime funding its qualified phase maxima beside the still-retained plan. A command-count guard, a passed command-array floor, or detached references is not that workspace grant. Refuse the whole diagnostic-bearing graph when any participating phase has no qualified footprint, including an opaque recording callback. Do not execute a callback merely to discover an unreserved bound.

Keep the joint **33,554,432-byte preparation/recording ceiling**, independent **16,777,216-byte completion ceiling**, shared 131,072-draw ceiling, exact four Frame bindings and packet ranges, complete membership, executable command/barrier schedule, pipeline acquisitions, shader ABI, and real-fence/quarantine semantics. Preserve the implemented [Core completion handoff](m05-skinned-normal-diagnostic-core-completion-handoff-addendum.md); its destination grant is not recording headroom. The [palette-bound addendum](m05-skinned-normal-diagnostic-budget-evidence-palette-bound-addendum.md) still owns the production count correction.

## 2. Bounded progress is not complete accounting

Current dirty-source changes establish the following narrower mechanisms:

- `CommandRecorderRecordingCapacity` now derives the actual small-list command/use-reference backing capacities and charges mutable and frozen arrays separately. `CommandRecorderState` enforces those capacities before publishing a completed list. `RenderMeshC0NormalDiagnosticWork` includes the component in its conservative known-component preconstruction guard, not a measured phase peak.
- A known command/use-array minimum above 32 MiB refuses recording before owner-queue waiting or recorder-array construction. Passing that check does not reserve request objects, pipeline-use nodes, scratch collections, preflight or handoff storage.
- Recording requests detach callback, access, color-format and recipe references. The recorder frame ends and its mutable command/use backings detach before task publication. Safe recorded-list/submission cleanup drops the command payload and outer pass/list roots.
- Uncertain submissions explicitly retain recorded lists, recipes, commands and uses against ordinary disposal. These are root/ownership protections, not numeric quarantine-carrier qualification.

For positive D, retain `N = 6D - M - 2`, D pipeline uses and the parent proposal's Q rule. The current mutable lists need `Ccommand = max(4,N)` and `Cuse = max(4,D)`, even for one draw:

```text
known command/use-array component
  = Q(Ccommand, Scommand) + Q(N, Scommand)
  + Q(Cuse, referenceStride) + Q(D, referenceStride)
```

For A, D=M=1, N=3: at the previously observed x64 strides 280 and 8, this component is **2,272 bytes**, not two equal three-command arrays. For AA, D=2/M=1/N=9, it is **5,360 bytes**. These are derived component charges, not fitting whole-graph examples. D=0 has no diagnostic recording component. Do not replace these actual capacities with logical counts or present the component as the complete recording peak.

Dropping an owner's fields ends those particular roots. An independently retained immutable command/access array or pass value still owns its backing; no byte refund follows merely from `Dispose`, source scope exit, a completed Task or a collection's logical Count becoming zero.

## 3. Exact remaining owner-capability gaps

The following are static source facts, not allocation/root measurements. Paths are relative to `sources\` unless stated otherwise.

| Owner | Missing preadmission capability and consequence |
|---|---|
| `TerraFX.Graphics\GraphicsDevice.RecordPass.cs`; `GraphicsOwnerRequestQueue.cs` | `RecordPassRequest`, TCS/task and asynchronous write controls still have no supplied full byte grant. The 256-entry bounded channel uses waiting writers; channel capacity does not bound or prepay all producer-waiter/shared backing growth. A request can retain its prepared graph while waiting. Clearing it later does not fund its creation or queue wait. |
| `TerraFX.Graphics\GraphicsDevice.PipelineRecording.cs`; `GraphicsResourceOwner.cs` | Each pipeline command acquires one `UseLease` and inserts one `SortedSet<long>` active-use node. There is no qualified recording-use control/node subgrant checked before those allocations. Repeated pipeline identity is not deduplication. Use-reference arrays do not include these objects, tree nodes, initialization or release-related retained controls. |
| `TerraFX.Graphics\CommandRecorderState.cs` | Recorder/list/hash-set/dictionary objects, reflected-binding buckets/entries, barrier scratch and specialized hazard/recipe state still need their own qualified bounds. The known command/use-array check deliberately does not pretend otherwise. |
| `TerraFX.Runtime\Rendering\RenderGraphPlan.Recording.cs` and `.Submission.cs` | Pass-local builders, callback controls, recipe construction, outer command-list/pass arrays, command-list/pass dictionaries, imports, attachments and transition copies are constructed before Graphics queue acceptance. Graphics cannot retroactively reserve those Runtime allocations. A pass-local access count is not total graph membership. |
| `TerraFX.Graphics\GraphicsGraphSubmission.cs`, constructor and `ValidateNativeRasterSubmission` | The constructor clones outer/access/import arrays. Preflight builds resource maps, binding-pair/resource sets, per-pass attachment/layout scratch and a reflected-resource hash set per pipeline command. Actual capacities and simultaneous peaks are not exported by a qualified preconstruction plan. Four shader bindings do not imply four total graph resources or four preflight entries. |
| The same owner, `HasPriorStorageWriteHazard` | It constructs and grows `List<GraphResourceAccess>`. Both native backends call it while preparing a submitted graph. Thus attributable managed preparation can still occur **after** graph owner-queue acceptance, outside the new command-array floor. Reserving only `ValidateNativeRasterSubmission` would miss this site. |
| `TerraFX.Graphics\GraphicsDevice.SubmitGraph.cs`, `QuarantineGraphSubmission` | Existing quarantine publication adds to concurrent bags and copies the use-reference list into an immutable backing after uncertainty is known. Marking recording storage retained protects its ownership, but does not prepay that carrier/copy or shared-container growth. A byte grant cannot be returned because a frame slot or TCS completed. |
| `TerraFX.Runtime\Rendering\RenderGraph.cs` | Public pass-registration overloads accept arbitrary callbacks; non-diagnostic recording remains dynamically sized. A diagnostic-bearing graph may therefore contain a callback whose command/use/storage shape cannot be derived from its pass declaration. Existing signatures provide no finite recipe for that callback. Guessing one or executing it before reservation is not admission. |

These are capabilities the next batch must add under the real owners, not grounds for inserting an unexplained fixed overhead, a placeholder owner, a test-only graph producer, a public budget knob, or a smaller arbitrary draw limit.

## 4. Minimum private workspace contract

The selected workspace has the following responsibilities. They are requirements for a future private implementation, not declarations of currently available types.

1. **Freeze complete input membership before construction.** Runtime supplies the full ordered graph shape: exact access/version pairs, imports, aliases, attachments, pass kinds and finite recording recipes, plus actual retained-plan/builder capacities and their lifetime intervals. Graphics owns its representation, pipeline-use, queue, preflight, backend-preparation and quarantine footprint terms. No Graphics/Core dependency on World, Scene, Runtime types or a higher-owner sizing callback is introduced.
2. **Reject opaque callback participation.** A diagnostic-bearing graph is inadmissible if any required pass has no complete qualified footprint/recipe. Initially qualify only the real built-in producers whose schedule is closed; do not infer a bound from an arbitrary public callback. This is diagnostic admission, not a new public callback API or a change to unrelated non-diagnostic graphs. An otherwise valid graph must not be returned/submitted with the diagnostic removed.
3. **Preclaim the maxima, not each phase's full ceiling.** Before diagnostic construction/graph return, establish the plan and all required additional phase charges. Before the first asynchronous diagnostic wait, claim the additional recording/workspace headroom within `33,554,432 - retained joint charge` and the separate qualified completion requirement. Allocate the grant/carrier itself only from an already funded construction allowance. A scalar plan is not a reservation.
4. **Fund queue waiting or refuse before it.** `GraphicsOwnerRequestQueue` must supply an owner-enforced bound for the diagnostic request's controls, waiter and attributable channel/backing growth before `WriteRequestAsync` can create them. If that cannot be qualified, do not enqueue/wait using an unestablished allowance. A separately reviewed diagnostic-only nonwaiting/preallocated route could instead refuse before growth; this proposal does not silently change existing wait semantics or introduce another accepted waiting queue.
5. **Convert the grant into exact managed storage.** Pre-size command/use arrays, recipe/access indices, pending-write arrays and fixed scratch at qualified capacities. Pre-admit use objects/tree nodes before `AcquirePipelineForRecording`/`AcquireUse`, not after allocation. Never count a class reference width as its object size. Shared empty arrays, immutable command payloads and shared access backings are charged once for their complete retention; actual overlapping freeze/outer-container copies remain charged separately.
6. **Preflight owns one bounded workspace.** Materialize Runtime handoff containers and Graphics submission/preflight scratch under their respective subgrants before queue acceptance. Reconcile exact versions/counts/capacities and immutable command identity there. Reuse prepared bounded hazard/access scratch for subsequent backend graph preparation, or retain an equally qualified already reserved maximum; `HasPriorStorageWriteHazard` must not grow an unreserved managed list after acceptance. Native GPU command/descriptor/staging/retirement budgets stay with their existing owners.
7. **Pre-admit uncertainty transfer.** Reserve the concrete quarantine carrier, reference-copy capacity and any attributable shared-container growth before native work is accepted. Safe cleanup detaches obsolete private roots before publishing release. On uncertainty, move the same live grant with the submission, recipe, uses and quarantine carrier; no extra uncharged backing or transient ownership gap is allowed.
8. **Make payload/root ownership explicit.** Bind the command/access payload's accounting interest to every surviving private owner/alias through submission and quarantine. Do not return credit just because the original recorded list or outer pass array detached. If private immutable-array escapes prevent a provable release, use a real shared payload owner/borrow contract with the existing command representation, or keep admission unavailable until the retained-root contract is closed. No compaction, third deep command copy, forced-GC reuse rule or Graphics-calling finalizer is selected.

`BuildGraph` returns a mutable graph, not authority to execute arbitrary later additions under an earlier estimate. Any mutation affecting membership, capacities or recipes invalidates that provisional footprint. Reconcile the complete current graph again before returning a compiled plan and before submission acceptance; an opaque later callback must not inherit the original built-in plan's grant. Preserve existing compilation failure channels without adding a rejection identifier.

Checked footprint overflow and an unavailable term refuse the whole diagnostic-bearing graph through the existing contextual allocation-budget failure, preserving usage/identity/version and cancellation precedence. Earlier independently accepted resource work is not rolled back by graph refusal. A mismatch or allocation failure never produces a prefix command list, fewer result rows, or empty-success diagnostics.

## 5. Smallest next dependent batch and evidence

| Batch | Owning implementation and evidence |
|---|---|
| Recording-use/queue subgrant | Extend the existing Graphics recording recipe/request boundary and real resource owner with qualified controls, actual BCL capacities, use-node footprints and an enforceable queue-storage admission. Use existing pipeline/queue harnesses for blocked-owner admission, refusal before allocation/enqueue, failure unwind and exact pipeline-use counts. Do not qualify unknown nodes or async controls from the four array charges. |
| Whole-graph workspace and return refusal | Complete Runtime's retained-plan/handoff terms and pass-recipe membership; implement the Graphics constructor/preflight/backend-hazard workspace and preadmitted quarantine transfer. Reject unknown callback/owner membership before returning the diagnostic-bearing graph or accepting its submission. Keep exact witnesses and schedules. |
| Retention reconciliation | Carry the same grant through shared payload owners and safe/uncertain terminal paths. Retain Tasks, plans, receipts and immutable aliases independently in the existing harness. Prove what remains charged and what actually detached; do not infer full graph-root release from the current callback/list/pass-root tests. |

Use the ordinary owning Graphics/Runtime Debug selections, then Release, with nonzero selected/executed cases and final-byte evidence under ignored artifacts. Validate exact-fit/one-byte-short complete subgrants with a nonzero retained baseline, all six diagnostic schedules, actual capacities, safe rejection before queue acceptance, and uncertainty retention. Current managed array/root behavior is useful independent evidence, not the complete numeric workspace or a native/AOT/public-consumer observation.

**Retry condition:** every workspace term, growth path, participating callback and lifetime transfer above has a qualified owner and conservative bound for the exact implementation/profile. Until then the joint and completion ledgers and whole-graph admission gate remain **NO-GO**. No Scene, picking, particle, shader ABI, public API, acceptance or release-qualification work is added.

## 6. Frozen static input set

The following whole-file SHA-256 values identify the narrow dirty-workspace input set based on HEAD `31f156a51ccc462ae9c5cc8d4e87e00c0054dec4`; HEAD alone does not identify these bytes. This is static contract evidence, not footprint qualification. Recheck before applying a review disposition. Source edits invalidate the affected rows.

| Input | SHA-256 |
|---|---|
| `docs\proposals\m05-skinned-normal-diagnostic-budget-evidence-rectification.md` | `A8DBD9EE4EA443245391A3B8AE17EDE03E3D71DA3F824E9128641E1C94F4DB36` |
| `docs\proposals\m05-skinned-normal-diagnostic-recording-budget-closure.md` | `5787039DC3BA43A862529CD9F01D82623DDFEC1B273194CC73FC50F079E0C753` |
| `docs\proposals\m05-skinned-normal-diagnostic-core-completion-handoff-addendum.md` | `958C9AAC244915B8981A6E7014DDFB948A3F5E6F058E07A84CC4609E93C386FF` |
| `docs\proposals\m05-skinned-normal-diagnostic-budget-evidence-palette-bound-addendum.md` | `89749E17E8F9624B08577B61CB40808A18F91E63D7BEC04C6992A10833301163` |
| `sources\TerraFX.Graphics\CommandRecorderRecordingCapacity.cs` | `65C17390A621EE2565BF1C84C44B12F6D2C165F5BECD96EEE9D3A2CFAEC3BD31` |
| `sources\TerraFX.Graphics\CommandRecorderState.cs` | `E6479CC51837161E722A27748D34A98CB9BBEEF4AE6EBB4B2AB2D10F95E363EE` |
| `sources\TerraFX.Graphics\GraphicsDevice.RecordPass.cs` | `31B2FC61A39D15EC2CD917E631E8FD5B829AC6BC1D89639A440043D6372387E7` |
| `sources\TerraFX.Graphics\RecordedPassCommandList.cs` | `AB6515E07F2D7DD9F341A763E38AF938D53FBF7E732AC96937D5C3338C5CB3BD` |
| `sources\TerraFX.Graphics\GraphicsGraphSubmission.cs` | `9F3FAA383EEE488EA4821BA2F06BA7F9BD90CB81617AB9A02BFA2E3B3D8484D2` |
| `sources\TerraFX.Graphics\GraphicsDevice.SubmitGraph.cs` | `5C8C30700F7F5CDDEDFA97C38B5B4AE9C48D56C3DA82E21523A8DBD5B9BB9A94` |
| `sources\TerraFX.Graphics\GraphicsOwnerRequestQueue.cs` | `E26DD7962CF13A7471128A8D27A2BD6DA9F06C75A4F397A3A6D56291A8511143` |
| `sources\TerraFX.Graphics\GraphicsResourceOwner.cs` | `C28BBB51E3FD3140472017DE70F8718B60296335AA8959EEEC54420073BF9846` |
| `sources\TerraFX.Graphics\GraphicsDevice.PipelineRecording.cs` | `D8D90F02FEF58E08FB55E163286DE9D31DC6A28F40CD8657E34C43C4C1EF7C72` |
| `sources\TerraFX.Runtime\Rendering\RenderMeshC0NormalDiagnosticWork.cs` | `7C652802C09CDB57C350F5DCBB85F7CE4B0BBA9E6874A504CD0601B34DA3C22B` |
| `sources\TerraFX.Runtime\Rendering\RenderGraph.cs` | `47BFA4EF2FB3C0A374CC1C102A73BCD2F5874715346775DA6097F2696B7D3A78` |
| `sources\TerraFX.Runtime\Rendering\RenderGraphPlan.Recording.cs` | `884A863DAF4E39BD02CFA5348304B0AA5844EB550C32888A4E175EA29A846F8F` |
| `sources\TerraFX.Runtime\Rendering\RenderGraphPlan.Submission.cs` | `34AB6687DED10903F40453441F3158F78DC4D8C78D82982863CADE038870525C` |
| `sources\TerraFX.Graphics.D3D12\D3D12GraphicsBackend.cs` | `3A78D1E2A78ABAEDCD1CFCBA2AC685DD85B5995A1A0808AEDAB78E1EFE702239` |
| `sources\TerraFX.Graphics.Vulkan\VulkanBackendDevice.GraphSubmission.cs` | `8182B1C554B908812F32926B1E16E3222378816E68A9FE16C28E1C549EDBA19A` |
