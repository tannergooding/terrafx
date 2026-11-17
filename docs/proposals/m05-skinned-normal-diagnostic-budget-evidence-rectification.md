# M05 skinned-normal diagnostic budget-evidence rectification

## 1. Decision and scope

**Tentative implementation unblocker, pending review; not a contract amendment or acceptance decision. The budget gate remains NO-GO.** Retain the joint **33,554,432-byte preparation/recording ceiling** and independent **16,777,216-byte completion ceiling** from [recording-budget closure](m05-skinned-normal-diagnostic-recording-budget-closure.md), sections 1 and 4–7. Neither lends credit to the other.

Recommend **conservatively reducing diagnostic admission through a complete, profile-qualified byte ledger while retaining the current command representation**. Refuse the whole diagnostic-bearing graph when its complete frozen workload does not fit. Do not change the renderer's shared 131,072-draw logical ceiling, truncate diagnostics, or promise admission at that ceiling. Representation compaction is a later option if an actual required workload is excluded, not a prerequisite for implementing safe refusal.

Preserve the exact packet views, four conservative Frame storage bindings, paired graph versions, pipeline acquisitions, barrier order/flags, complete result membership, cancellation, real completion and quarantine behavior. This direction may tentatively unblock in-scope M05/M03-B implementation; it grants no new World/Scene allowance, public API, schema, diagnostic identifier, shader ABI, latency promise, or milestone acceptance.

## 2. Current evidence and its limits

Line references below identify the dirty workspace based on HEAD `31f156a51ccc462ae9c5cc8d4e87e00c0054dec4`, not just committed files. Existing proposals describe earlier snapshots; their historical scan/build failures are not assertions about today's implementation.

The supplied `artifacts\tst\Debug\tagoo_GOODING-ZEN4_2026-11-15_21_16_31_net11.0.trx:8–19,37` records **one selected/executed/passed Runtime test**, `RenderSkinnedNormalDiagnosticPrototypeTests.MeasuresDiagnosticBindingPlanAllocation`, on **.NET 11.0.0-rc.3.26522.148/X64**:

| Observed quantity | Bytes |
|---|---:|
| `Unsafe.SizeOf<RecordedGraphicsCommand>()` | 280 |
| `Unsafe.SizeOf<StorageResourceUse>()` | 72 |
| `Unsafe.SizeOf<BufferBinding>()` — reference width, not object size | 8 |
| Isolated `BufferBinding` allocation | 56 |
| Isolated `BindingSet + copied arrays` allocation | 440 |
| Four-element `BufferBinding[]` allocation | 56 |
| Graph-pass construction, D=2 / M=1 | 1,376 |
| Graph-pass construction, D=1000 / M=1 | 560,256 |
| Graph-pass construction, D=1000 / M=1000 | 632,184 |

TRX SHA-256: `13FEE83EA6AE0A6E637716E863AC415EFEE399EC17F66A4EBA6E17072F2FAB62`.

These are warmed, isolated current-thread allocation deltas and managed strides. The temporary test prepares draw/version inputs before the construction measurement and never records commands (`tests\TerraFX.Runtime.Tests\Rendering\RenderSkinnedNormalDiagnosticPrototypeTests.cs:34–129,132–175`). **440 is allocation traffic for that constructor call, not the retained size of a BindingSet.** Do not add its copied arrays again to that same traffic total or extrapolate the three pass samples into a qualified bound. They measure neither complete recording nor simultaneous live storage, native backing, async roots, completion, cold initialization, other architectures, or NativeAOT.

Current progress is real but narrower:

- The closed recipe copies binding references, indexes exact access-pair witnesses and validates the completed command array (`sources\TerraFX.Graphics\SkinnedNormalDiagnosticRecordingRecipe.cs:64–145,269–316,641–700`). Its singleton-plus-packet tracker retains exact writes without the previous pending-list scan (`sources\TerraFX.Graphics\SkinnedNormalDiagnosticRecordingState.cs:29–37,96–157`).
- The existing focused Graphics TRX `artifacts\tst\Debug\tagoo_GOODING-ZEN4_2026-11-15_21_14_17_net11.0.trx` records one passed framework test covering A, AA, AB, AAA, ABC and ABA, including direct native-access-witness validation. The test uses a fake backend (`tests\TerraFX.Graphics.Tests\GraphicsPipelineRequestTests.cs:1702–1730,1864–1973`); six schedules are not six native qualification cases.
- A later full Graphics Debug run (`artifacts\tst\Debug\tagoo_GOODING-ZEN4_2026-11-15_21_28_25_net11.0.trx`) passed 223 tests after adding overlapping-packet recipe refusal. It remains managed/fake-backend validation.
- At proposal freeze the estimator charged only five preparation arrays (`sources\TerraFX.Runtime\Rendering\RenderMeshC0NormalDiagnosticWork.cs:138–155`). A later implementation adds a necessary-only floor for those arrays plus two command arrays using the worst-case `Q(6D-3, 280)` before the D-sized preparation arrays are allocated (`RenderMeshC0NormalDiagnosticWork.CalculateKnownMinimumCpuBytesForWorstCaseGeometry`). It refuses requests when that floor exceeds 32 MiB, but does not instantiate the complete ledger. `RenderMeshC0NormalDiagnosticBudgetTests` verifies the row-ceiling arithmetic only; it does not exercise end-to-end `Work.Create`, graph refusal, or owner-queue admission. The budget gate remains NO-GO.
- `RenderMeshC0NormalDiagnosticBudgetTests` passed two scalar tests (`artifacts\tst\Debug\tagoo_GOODING-ZEN4_2026-11-15_21_34_17_net11.0.trx`); these verify the lower-bound calculation but do not exercise end-to-end refusal.
- The later full Runtime Debug suite passed 1,059 tests with 128 skipped (`artifacts\tst\Debug\tagoo_GOODING-ZEN4_2026-11-15_21_35_06_net11.0.trx`); the four static-skinning GPU cases assert one positive, nondegenerate diagnostic row on D3D12 and Vulkan. These are behavior/consumer observations, not budget observations. Scheduling/native-witness success does not close the budget gate.

## 3. Corrected command arithmetic

Retain the independently derived schedule in [storage-hazard scaling](m05-skinned-normal-diagnostic-storage-hazard-scaling.md), sections 3 and 7. For positive D, M is the number of distinct selected geometry resource identities, with `1 <= M <= D`:

```text
barriers B                 = 3D - M - 2
commands N                 = 6D - M - 2
pipeline uses              = D
logical pending high water = D + M + 2
resource entries R         = M + 3
pass-local access count A  = 2R
```

The current pass supplies these counts and rebinds the pipeline per draw (`sources\TerraFX.Runtime\Rendering\RenderMeshC0NormalDiagnosticGraphPass.cs:153–167,214–225`). D=0 creates no diagnostic pass; do not evaluate the positive-D formulas for it.

At the earlier algebraic row ceiling, D=131,072:

| Geometry schedule | N | Final command payload, `280N` | Mutable + frozen payload, at least `560N` |
|---|---:|---:|---:|
| All distinct, M=D — minimum command count | 655,358 | **183,500,240** | **367,000,480** |
| One repeated geometry, M=1 | 786,429 | **220,200,120** | **440,400,240** |

The former 88-byte figure was a valid source-derived **lower bound**, not a CLR stride measurement. For this measured profile, replace it with 280 in accounting; its earlier minimum final-payload bound of 57,671,504 bytes becomes 183,500,240 bytes. This is derived arithmetic using the measured stride, **not an allocation of the row-ceiling array or a measured peak**.

`CommandRecorderState` retains its mutable list while `Complete` constructs the immutable array (`sources\TerraFX.Graphics\CommandRecorderState.cs:90–118,237–275`). With actual mutable capacity C, charge `Q(C,280) + Q(N,280)`, not merely `280N`. Even if C=N, the conservative array rule below gives **367,000,608** and **440,400,384** bytes respectively for those two arrays. Growth can cost more. Submission shares the frozen command payload; it is not a third deep copy (`sources\TerraFX.Runtime\Rendering\RenderGraphPlan.Submission.cs:113–125,142–149`).

As an early necessary-only rejection, those two arrays require `N <= 59,918`; this implies at most D=9,986 for M=1 or D=11,984 for M=D **before every other charge**. These are not safe admitted draw limits. Even AA needs nine commands: 2,520 final payload bytes and at least 5,040 during freeze, none included in the present five-array estimate.

## 4. Narrow rectification choices

| Option | Benefit and obligation | Disposition |
|---|---|---|
| Compact representation only | A private diagnostic command stream could avoid storing unused general-command fields. It must preserve managed-reference rooting, immutable ownership, complete validation and both backend traversals. Re-expanding it to the old array restores the cost; deferring unreserved expansion until after graph acceptance is invalid. All noncommand charges still apply. | Not the narrowest unblocker; no evidence yet establishes its stride or sufficient savings. |
| Conservative byte-derived diagnostic admission with the current representation | Close every ledger term, reserve phase maxima, and admit all D draws or refuse the graph. Use a private effective limit from the qualified ledger, not a changed shared renderer ceiling or an arbitrary smaller constant. | **Recommended now.** Narrowest change that can enforce the unchanged envelopes without redesigning command consumers. |
| Both | Establish safe admission first; compact only when an identified excluded workload warrants it, then remeasure and recompute admission. | Optional follow-on, not presumed savings or authority to raise either ceiling. |

Compaction that retains two equal-sized command backings would need a raw stride of at most 25 bytes for the all-distinct row-ceiling schedule, or 21 for the repeated-geometry schedule, **even if every other charge were zero**. Removing a copy alone still leaves the current final array above budget. Neither optimization by itself promises row-ceiling fit.

## 5. Owners and admission sequence

The following are proposed implementation changes, not changes made by this document. Graphics owns its representation/collection/object footprint terms; Runtime consumes private scalar bounds without Graphics depending on Runtime types or World callbacks.

| Exact owner and current evidence | Required responsibility |
|---|---|
| `sources\TerraFX.Runtime\Rendering\RenderMeshC0NormalDiagnosticWork.cs:115–158,189–204,267–303`; `Renderer.MeshC0Graph.cs:1056–1062,1124–1191` in the same folder | Extend the current necessary-only lower bound to the complete joint phase estimate before diagnostic arrays/sets are allocated or a graph is returned. Count D/I/G/allocated W first. Use conservative M bounds, or precharge the mesh-flag counting workspace before finding exact M; never allocate an uncharged discovery set. Preserve the allocated work upper bound, not just occupied packets. |
| `sources\TerraFX.Runtime\Rendering\RenderMeshC0NormalDiagnosticGraphPass.cs:61–144,181–212`; `RenderGraph.cs:738–749,799–827`; `RenderSpriteGraphState.cs:54–68` in the same folder | Carry the admitted private footprint/capacities with the frozen plan; claim bindings/cache/geometry arrays before construction. Preserve one complete set and exact packet binding per draw. BuildGraph's local construction must either return the complete graph or fail, not return a graph with diagnostics removed (`Renderer.BuildGraph.cs:60–93,127–156`). |
| `sources\TerraFX.Runtime\Rendering\Renderer.Submit.cs:170–192,792–838,916–919`; `RenderGraphPlan.Recording.cs:83–114,196–235`; `RenderGraphPlan.Submission.cs:81–149,210–228` | The existing lane is a count guard, not a byte claim. Before the first diagnostic wait/materialization, claim the additional headroom beside still-retained plan bytes and the independent completion requirement. Charge access builders, recipe creation, callback/request controls and handoff copies before allocation; propagate the reservation through the existing private boundaries. |
| `sources\TerraFX.Graphics\CommandRecorderRecordingCapacity.cs`; `GraphicsDevice.RecordPass.cs:12–41,45–133`; `CommandRecorderState.cs:90–127,227–275,669–713` in the same folder | Carry a private byte claim alongside count capacities. Pre-admit actual list capacities and both completion copies, fixed scratch and request/TCS controls. Refuse unreserved growth; verify final counts/capacities before publishing a completed list. Construction/callback/freeze failures unwind safe acquisitions. |
| `sources\TerraFX.Graphics\SkinnedNormalDiagnosticRecordingRecipe.cs:64–145,226–234,269–316`; `SkinnedNormalDiagnosticRecordingState.cs:10–37` in the same folder | Charge every recipe/index/scratch/pending allocation and its lifetime. Reuse the existing A-entry index during submission reconciliation; do not introduce a second uncharged index. Preserve exact witness/command-array checks and the no-general-pending-backing branch. |
| `sources\TerraFX.Graphics\GraphicsDevice.PipelineRecording.cs:12–39`; `GraphicsResourceOwner.cs:13,146–178,432–463` in the same folder | Supply/enforce qualified charges for D pipeline-use objects and D active-use tree nodes, plus distinct materialized-resource use ownership. Rebinding the same pipeline still acquires D uses; do not assume deduplication to reduce bytes. |
| `sources\TerraFX.Graphics\RecordedPassCommandList.cs:44–67`; `GraphicsGraphSubmission.cs:99–107,943–975,1169`; `GraphicsGraphSubmissionPass.cs:7–13`; `GraphicsDevice.SubmitGraph.cs:158–172,213–220,442–476,518–527` in the same folder | Carry/reconcile the claim across list → submission → request → fence/quarantine. Charge outer/access copies and native-preflight managed collections before graph queue acceptance. Detach safe obsolete command/request roots, or keep their charge; disposing leases alone does not clear `Commands`. Uncertain work retains its reservation with quarantine. |

Admission is staged, but never retroactive:

1. Establish a conservative whole-recipe bound before D-sized diagnostic construction. If exact M is not yet known, use `N <= 6D-3` and `M <= min(D,G)` for the other capacities. Any preliminary counting scratch has its own preclaim.
2. Before returning a diagnostic-bearing graph, its preparation and retained-plan construction fit the joint bound. Before the first submit wait, claim the remaining joint headroom and the independent completion peak for this attempt; a retained plan still costs bytes on resubmission.
3. Before `RecordPassAsync` queues work, all recipe, recording, freeze and handoff storage has a qualified charge. At `SubmitGraphAsync`, reconcile actual capacities and complete preflight **before** `_ownerQueue.WriteRequestAsync`. Earlier independently accepted uploads/resources retain their own cleanup/completion rules; graph refusal is not rollback of those operations.
4. On excess, checked arithmetic failure or an unestablished footprint, admit no graph and expose no prefix/empty-success diagnostics. Use the contextual allocation-budget `OutOfMemoryException` proposed in recording-budget closure section 5; separate it from the current `InvalidOperationException` capacity branch in Work and preserve argument/identity/version validation precedence. Do not add a graph-compile rejection identifier.
5. Spend a reservation by converting its grant to owned storage, not charging both. End it only at actual ownership release. Cancellation, task failure, lane reset or lease disposal cannot lend still-rooted/quarantined storage to another attempt. Preserve the existing device-loss admission closure.

## 6. Required ledger and phase maxima

For each claimed runtime/architecture/build profile, use checked arithmetic and actual allocated capacities:

```text
Q(c,s) = 64 + roundUp16(checked(c*s))
```

This existing conservative array charge (`sources\TerraFX.Runtime\Rendering\RenderMeshC0NormalDiagnosticWork.cs:302–303`) includes a header/alignment allowance; it is **not** a measured CLR header rule. Increase it if target evidence requires. For the measured profile use reference stride 8, command stride 280 and storage-use stride 72; qualify every other stride. Small List capacities can exceed requested counts, including the first allocation. Dictionary/hash-set bucket/entry capacities are not their logical counts.

Every separately allocated class/control/node needs its own qualified header + fields + padding/alignment charge, with its separate backing charged separately. Do not use `Unsafe.SizeOf<class>()` as that charge. Do not hide unknown allocations in an unexplained fixed overhead constant.

Here G is mesh-array capacity, I is maximum selected index count, W is allocated work-byte length, A=2(M+3), and `Sdraw/Sentry/Saccess/Sresult` are qualified managed strides.

| Ledger entry | Exact contents and retention |
|---|---|
| Preparation and plan | `Q(D,Sdraw) + Q(G,1) + Q(I,4) + Q(W,1) + Q(16D,1)`, plus construction controls/sorting initialization. Scratch may end after preparation; draw/work/initialized-result arrays survive in the plan. Graph upload owners share payloads rather than deep-copying them. Do not confuse initialized result-upload bytes with later raw readback. |
| Binding plan | D BindingSet objects; D+M+2 BufferBinding objects; D retained four-reference arrays; `Q(D,8)` set references; `Q(M,8)` selected geometry references. During construction include the `Q(G,8)` vertex-binding cache, current four-reference input/sort/final-array overlap and comparer/delegate allocations (`RenderMeshC0NormalDiagnosticGraphPass.cs:65–140`; `sources\TerraFX.Graphics\BindingSet.cs:31–148`). Charge only actually shared empty backings once. |
| Recipe/witness | Additional `Q(D,8)` copied set references, `Q(M+3,Sentry)`, retained `Q(A,4)` access index, `Q(M,1)` geometry-seen scratch; recipe and index-comparer objects. The pass-local immutable access backing is shared with the request/diagnostic recorder, but still charged once for its whole retention. `_verifiedCommands` adds a root, not another command payload. |
| Hazard/recorder state | `Q(M+2,72) + Q(M+2,1) + Q(D,72) + Q(4,4)` for singleton values/presence, exact packet writes and bound-entry indices; `Q(4,72)` current uses. Include recorder/state/list/dictionary/hash-set objects and actual reflected-binding/barrier-scratch backing. The general pending-list object still exists; its D-sized backing is not allocated in this branch (`CommandRecorderState.cs:12–35,114–127`). |
| Commands and uses | Mutable `Q(Ccommand,280)` plus frozen `Q(N,280)` at Complete; mutable/frozen lease-reference arrays at actual capacities; D use objects and D tree nodes. Include old-plus-new growth if any. Freezing copies references to bindings/owners, not those objects themselves. |
| Graph construction/handoff/preflight | Added graph version/description/pass/upload/export controls, dependency arrays, labels, access/transition/import records; pass-local access builder and frozen backing; outer command-list/pass/import arrays and dictionaries; submission's copied access/outer arrays; native-preflight maps/sets, per-pipeline reflected-resource sets and comparer objects. Enumerate actual allocation sites and capacities, including shared-container copies/growth caused by the diagnostic and managed backend-preparation storage. A=pass-local accesses is not total graph accesses. |
| Root/ownership controls | Callback captures, recipe/state, recorded-list/submission/request objects, Task/TCS/continuations, queue/fence controls, use-transfer and quarantine carriers. Trace plan, completed operation Task, receipt and quarantine roots independently. Referenced backing remains charged through its actual owner/lifetime; multiple references do not duplicate physical payload, but an extra root can extend retention. Existing shared assets/resources retain their owning budget, not free diagnostic-created backing. |
| Independent completion | Actual raw CPU readback backing capacity/alignment/owners, leases, D typed results at `Sresult`, decode builder/action/control and receipt/task storage. Current decode uses `MoveToImmutable`, so it transfers the exactly filled result backing rather than making another D-result copy (`RenderMeshC0NormalDiagnosticGraphPass.cs:228–282`). Qualify that ownership; do not assume an unmeasured raw `byte[]` or charge native staging twice. |

Assign each allocation a phase interval, owner, capacity, stride/object charge, root and release condition. Include attributable shared-container growth under the real owners' budgets, not only its new logical elements. The ledger must instantiate all symbolic terms before admission; the table is a checklist, not a completed numeric proof.

| Phase | Simultaneous charge to bound |
|---|---|
| Preparation | Five-array preparation peak and setup/sorting controls. |
| Binding-plan construction | Still-live preparation/plan bytes + all completed sets/bindings + cache + current construction copies. |
| Recipe and recording | Retained plan/bindings + access builders/copies + recipe/indexes + recorder/pending arrays + growing command/use backing and live uses/nodes. |
| Complete/freeze | All still-rooted preceding storage + mutable and frozen command/use backings + completed-list controls. Do not discard the mutable charge merely because recording ended. |
| Submission/preflight | Retained plan + frozen recording + any still-rooted recorder backing + outer/access copies + preflight/control storage; no third deep command copy. |
| Submitted/uncertain | Plan/command/binding/request/use roots until their actual release; quarantined ownership does not become reusable headroom. |
| Readback/decode/retained result | Remaining recording/plan bytes stay on the joint ledger while raw readback + typed result + decode/control peak independently stays on completion. Retained receipt/result backing stays charged for its owned lifetime. |

Require `max(joint phase charges) <= 33,554,432` and, independently, `max(completion phase charges) <= 16,777,216`. Sum allocations that coexist, not mutually exclusive phase maxima; do not subtract a backing on logical Clear/Dispose without proving its roots ended. Caller-retained plans keep their storage charged. These are owned-storage admission bounds, **not total-process/RSS or no-GC guarantees**. Report allocation traffic separately from live/retained storage; forced GC is not a reservation mechanism.

## 7. Focused implementation evidence and gates

Extend the existing owning Graphics/Runtime harness after implementation; no new qualification runner or row-ceiling command allocation is needed.

| Check | Required evidence |
|---|---|
| Scalar estimator and necessary refusals | Independently assert D=0, AA counts/payloads, both row-ceiling arithmetic rows, checked overflow and alignment rounding without constructing N commands. The new scalar test covers only the row-ceiling minimum; still prove the actual request refuses before command-list allocation/recording enqueue. Test command-only first-failing D=9,987 for M=1 and D=11,985 for M=D, explicitly distinguishing those necessary guards from the lower full-ledger admission boundary. |
| Full admission boundary | For fixed qualified repeated/all-distinct/two-cycle workload families, derive the last fitting and first refused complete recipes from the ledger. Cover exact-fit and one-byte-short private reservation headroom with a nonzero retained-plan baseline; do not add a public budget knob. Include large G with small M, index scratch, storage-offset alignment/gaps and allocated W. A valid admitted recipe keeps every draw; the next one exposes no graph/result prefix. |
| Actual managed footprint | Measure cold/warm construction, owner-thread recording, Complete, submission/preflight and decode separately; capture actual capacities, all remaining strides/object/node charges, allocation traffic and allocation/root traces at phase boundaries. Explain constructor traffic versus retained objects, including the observed 440-byte sample. An isolated current-thread delta does not observe owner-thread allocations or prove a peak. Unknown charges remain refusal conditions. |
| Coexistence and no growth | Assert command/use capacities including small minimum-capacity cases, both freeze copies, recipe/index arrays, singleton/packet storage and no general pending backing. Observe shared command/access backings by identity and all incremental graph-container copies. No uncharged expansion or copy elision may be assumed. |
| Whole-graph refusal/cleanup | Exercise budget refusal before construction/enqueue and injected allocation/failure at recipe, recording, freeze, handoff and preflight. Existing fake-owner counters must show no graph backend submission on refusal, safe uses/reservations unwound, original graph/snapshot inputs unchanged, and no partial result. Earlier accepted resource work is cleaned up through its own completion rules. |
| Scheduling and invalid witnesses | Retain all six schedules and direct native-witness checks. Add wrong sequence/capacity/version/epoch/pair, changed/overlapping packet and mismatched immutable command-array cases where missing; fail the whole unsubmitted recording without entering the general scan path. Keep deterministic work-bound checks distinct from elapsed-time observations. |
| Root lifetime | Retain completed Tasks/receipts separately from plans; prove no obsolete recording roots survive a claimed release. Controlled fence/fault tests prove safe transfer, cancellation and quarantine accounting only; uncertain work retains charge. A real-native failure observation is not supplied by that control harness. |

Use the `global.json` SDK and ordinary owning Debug builds/tests, then Release where applicable; confirm nonzero selected/executed tests and retain per-run evidence under ignored `artifacts` (`docs\project\Building.md:3–31`). Freeze/hash the eventual implementation inputs before reviewing its numeric ledger. A changed representation/profile invalidates affected size/root evidence.

**GO for this budget gate requires a fully instantiated conservative ledger, pre-admitted phase maxima, whole-graph refusal and ownership checks for the exact implementation/profile. The current state remains NO-GO despite the passing scheduling test.**

Managed layout/accounting, fake-backend recording, actual Vulkan/D3D12 reservation/execution/readback, native failure/retirement, NativeAOT and public-only consumer observations are separate evidence classes. Neither this proposal nor a future managed pass qualifies GPU normal counts or supplies the missing live animated-palette producer. Native command/descriptor/staging/retirement budgets stay with their existing Graphics owners; managed preparation attributable to them is not omitted from the ledger.

Continue dependency-ready M05/M03-B work without declaring those observations complete. `docs\design\35-implementation-roadmap.md:28–56,84–99` owns their capability/observation gates; M08 retains the complete release measurement/package/profile obligations, and the separately deferred M09 native-failure observations do not become this budget gate. `docs\project\Roadmap.md:11,14,19–21` retains M05 in progress and release qualification separate. No maintained contract, prior proposal or acceptance status is amended here.
