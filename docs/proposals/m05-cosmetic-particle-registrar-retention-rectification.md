# Cosmetic-particle registrar retention rectification

## 1. Decision and scope

**Recommend permitting the original registrar provision R to survive outcome publication, solely to finish its own qualified publication/return work. End all World data W, diagnostic-source ownership E, and ordinary domain registry backing before publishing successful `WorldAdmissionDomain.Retirement`. End R only after the actual publisher returns and the last domain-owned access finishes.**

This is an additive owner-decision proposal, tentatively selected for dependency-ready implementation work under the user's direction. It is not accepted Chapter 30 text, a numerical allocation profile, or qualification evidence. It resolves the planning choice left in [terminal funding rectification §5](m05-cosmetic-particle-terminal-funding-rectification.md); its other gates remain open. It preserves that proposal's W-before-publication progression, not the earlier W-through-publication cadence in [standing-owner funding §4](m05-cosmetic-particle-standing-owner-funding-route.md). No existing proposal is amended.

### Narrow Chapter 30 decision

`docs\design\30-public-api-review-surface.md:3480` says that `Retirement` completes only after all actual domain interests, allocations and registry storage retire, and that cleanup/quarantine failure faults rather than reporting successful release. That sentence does **not explicitly decide** whether its own completion control and original registrar-return protection are included in the allocations that must end before publication.

The required decision is:

> After every retireable World/source allocation, interest and ordinary registrar table has ended, may the same originally admitted registrar provision retain only the domain's completion/publication control and its return/failure protection past visibility of the existing outcome, until their actual owning use ends?

Select **yes**, conditional on a complete original R profile and a proved end-or-retain suffix. Successful Retirement then establishes retirement of the participating data and ordinary registry storage; it does not establish return of this narrowly identified publication control or release of the process binding. This is an explicit proposed exception to the literal all-allocations reading, not an assertion that existing prose already grants it.

The exception permits no live W/E data, lineage tables, operation workspace, arbitrary observer graph, or unpriced framework work after successful publication. It does not rename outstanding data as protection. Cleanup failure still cannot publish successful release. A fault outcome is not a refund.

Use this choice tentatively to pursue the lower ownership/accounting implementation. Obtain the Chapter 30 owner's disposition before activation. If the owner requires R itself to end before the Task's terminal transition, this Task-based route has no demonstrated originally funded return mechanism: funding cannot end before the setter's remaining accesses. A different route would need an explicitly owned, originally funded transfer/acknowledgment; neither a reference in G nor another wrapper/Task supplies it. No such alternative is selected here.

### Unchanged capability boundary

- Preserve `World.cs:525–528`, `WorldConstructionPlan.cs:340–345`, and `WorldCosmeticCreditOwner.cs:37–58`: selected World construction and the standing-owner constructor remain fail-closed.
- Install no active World particle registration, pool, lifetime bridge, spawn/tick actor, Scene participant, extraction producer, or rendering consumer.
- Preserve existing public signatures, schema identities/tags/versions, acceptance IDs, generation rules and count/byte ceilings. Do not select a new cap, byte allowance, public receipt or fallback.
- The initial proof input remains an empty immutable registration selecting only the private credit-storage request, fresh standalone construction with its private default domain, explicit nonempty World/replay identities, `CpuWorkerCount = 0`, otherwise default options, and an owner-thread Create/Dispose path. All 128 cells are unused. The mandatory standalone diagnostic envelope is included.
- No Step/control/subscription mutation, positive diagnostic payload or Retirement observer/continuation is part of that input. Actual internal runtime dispatch still requires its own R bound.
- Hosted/supplied-domain, restore/handoff, Scene, independent-use/history/operation, external diagnostic-use and particle/rendering compositions remain outside this first profile. This is a closed qualification input, not a new restriction on existing public APIs.

Use the SDK selected by `global.json`; the inspected input selects .NET SDK `11.0.100-rc.3.26522.148`, and root props target `net11.0`. Start with Windows x64 Debug, then Release. This names the candidate input, not a qualified runtime/CoreLib/native profile.

## 2. Current evidence

HEAD was verified as **`14f520edaaaaeb03ad08096afdc92c0169a47745`**, including the requested `14f520ed`. These citations describe current working-file bytes, frozen in §7; old proposal hashes and binary identities are not rebound to them.

Unless otherwise specified, source citations below are relative to **`sources\TerraFX.Runtime\Entities`**.

| Current source | Consequence |
|---|---|
| `WorldAdmissionDomain.cs:72–92,170–196` constructs the TCS and registry arrays before assigning `_registryCharge`. | Complete prospective R must exist before the domain header/children allocate. A later W reservation cannot fund this prefix retroactively. |
| `WorldAdmissionDomain.cs:3190–3247,3268–3296` calls `TrySetResult`, then performs terminal-state/gate work. `ReleaseRegistry` retains `RegistrarControlBytes`; anchored Escrowed/Returned/CompletionPending publication retains that charge. | Source already distinguishes ordinary table retirement from registrar publication-return. This supports the recommended default, but does not prove that the 4096-byte row covers the actual graph or that current ordering satisfies Chapter 30. |
| `World.cs:1649–1678,1949–1967` calls `CompleteWorldClose` from the inner Dispose finally, then the enclosing helper resets a World flag. `WorldAdmissionDomain.SceneStorage.cs:731–807` removes all W before returning. | W currently ends before a reached World write. Move the selected final handback to the real returned-body caller; R cannot pay this World suffix. |
| `WorldGuidRegistry.TerminalAnchor.cs:22–51`; domain terminal partial `:94–140`; `WorldGuidRetirementPermit.cs:44–49` | The invoker observes the World helper return, but later acceptance reads the lifetime/permit owner. Copying a permit reference does not eliminate those W-owned accesses. Finish them while W survives; afterward use issuer-established escrow state only. |
| Registry terminal partial `:168–235`; domain terminal partial `:29–79,354–382` | Current completion releases O/owner-U/key roles before a fallible domain acknowledgment. That acknowledgment can zero R inside domain gate scopes before field clearing and gate exits. Prepare domain completion before destroying its root, and place the final R debit after all domain scopes. |
| `World.cs:653–706`; `WorldAdmissionDomain.cs:1768–1799`; `WorldDomainOperationCredit.cs:13–25` | E is one separately debited retained-source envelope before its credit/control/service graph is created. Initial World and standalone baseline uses share it; E is not another W debit or an absent row. |
| `WorldDiagnosticFundingControl.cs:40–70`; `WorldDiagnosticFundingUse.cs:23–28`; `WorldDomainOperationCredit.cs:32–42,65–79`; `WorldDiagnosticsServiceOwner.cs:399–456,511–550` | Source/control/service links can clear before a fallible child returns; the credit writes its released flag and exits its Lock after the domain debit. E's actual return and failed-return retaining edge remain unproved. R retention does not rectify them by itself. |
| `WorldCosmeticCreditOwner.cs:22–58,83–99`; `WorldLifetimeControl.cs:19–73`; cosmetic lifetime partial `:18–23,66–78` | The candidate would create one owner and one fixed cell array. Logical owner handback refunds nothing. The actual lifetime-to-standing-owner retaining edge and its complete W charge are still absent. |
| `WorldAdmissionDomain.TerminalAnchor.cs:434–480` | Existing terminal failure retention preserves an exception/root but does not fault the retirement TCS. The contract-owned fault-publication path must be closed as well as success. |

The source supports retaining original R rather than inventing another close owner. It does **not** supply complete W/R/G/E numerical envelopes, constructor-prefix retention, compiled-return proof or native observations.

Current symbol navigation independently finds nine incoming publication entries (§4.3), one production caller of `InvokeTerminalWorldClose` (`World.Dispose`), and only test references to the domain `Retirement` property. These are source-membership observations, not executed cases or proof that runtime dispatch is allocation-free.

## 3. Exact original owners

W/R/G/E here are ledger names, not new CLR types. R is not Chapter 04's reservation-count variable; G is not its pending-slab-ticket count; E is not its lineage-edge count.

| Provision | Original issuer, retained ownership and return frontier |
|---|---|
| **W — World** | The exact domain reservation/interest and lifetime. Includes the qualified World/common/standing/control/protected construction and close graph, including any future standing record/array. The enrolled interest roots its lifetime; a future standing retaining edge must be installed before visibility. Hold W through the actual World-body return, all standing/lifetime/permit/reservation accesses and their owning gate exits. End it once, through the original domain issuer, before successful Retirement publication. |
| **R — registrar** | The same domain's original `RegistryCharge`: its control and any separately enumerated, originally charged publication/protection rows under Chapter 04. The original process binding roots the actual domain; the domain's terminal fields identify the exact invocation. Retire ordinary tables only after their last use. Hold the remaining original R through setter/dispatch, fault retention, domain acknowledgment, field changes and domain gate exits. Its final debit is the last domain-owned operation. |
| **G — process registry** | Existing `WorldGuidRegistryAccounting1` control, index, directories and retained-high-water slabs. The original exact binding O, owner-U and key roles retain the terminal root until registrar return is prepared and completed. Fresh P/coordinator-U have their own construction-role ends. G pays its own invocation, validation, failure-root and final process-only suffix; it does not pay referenced W/R/E objects. Returning roles does not refund installed slabs. |
| **E — diagnostic source** | The original dedicated source-role credit, funding control/account and actual Runtime service/source users. World and baseline uses share one full envelope; retained uses do not mint another domain credit. E remains held until its real source/control/caller work ends or an explicitly funded original-issuer handoff completes. Preserve a discoverable original credit through fallible return. End its bytes and total/source-subset counts together, with no subsequent E-owned access. |

A successful data end is not GC collection of every disposed wrapper or immutable returned result. Passive copies do not extend operational ownership. Conversely, copying a mutable control or exception reference into another object is not funding, transfer or release.

For E, the required repair is an **original source end-or-retain handoff**, not an R exception: deny new source uses at final return, retain the exact credit and failure authority while the source/service/control callers finish, complete their field/Lock work while E remains charged, then let the original issuer debit using validated scalar facts. The same World invocation may carry existing typed references/scalars in unboxed locals; those locals mint no credit. Any failed preparation retains the original discoverable source edge. No success-shaped null-credit return or debit followed by a source-control write is allowed.

Shared registration, system-quarantine and attachment-registry backing retain their own owners. `WorldSystemInstanceRegistry.cs:112–135` reserves a lifetime count slot even at N=0, but that is not byte funding for the new graph and creates no system-domain retention slot. Do not add a dummy system or assign shared backing to G.

### Conservation and capacity

Apply `docs\design\04-capacity-and-workload-profiles.md:105–153,155–191` unchanged:

- W plus the applicable E attribution fits the exact configured World-state ceiling before allocation. Physically, the domain charges W and E separately, once each.
- R remains inside registry metadata B and the domain aggregate, including the still-held publication provision. Do not count it again beside `RegistryCharge`.
- Growth/construction/protection overlap, pending grants, quarantine and every other actual domain credit remain in the aggregate. A retained R is not reusable headroom.
- G retains the original process roles and backing under existing K/O/P/U/B limits. No extra O/P/U, bootstrap use, particle grant, tail list, slab ticket or second envelope is introduced.
- Source constants such as `FreshWorldControlBytes = 4096 + 4 * IntPtr.Size`, registrar 4096, and the cell-array formula are inputs, not spare allowances. If an actual named cost is uncovered, derive and preadmit it under its existing owner/ceiling before allocation; do not choose a larger margin or spend a released page's allowance.
- Missing/mismatched profiles, checked overflow and reached capacity refusal retain conventional channels and occur before selected allocation/identity visibility. They do not silently unselect storage or become `RejectSpawn`; no spawn operation exists in this input.

No complete value for W, R, G or E's protected return graph is selected here.

## 4. Construction, election and return sequence

Phase names below describe proposed encodings/progression in the existing private owner state, not a public enum, receipt or new transferable capability. Preserve exact reference/issuer/index/generation validation; GUID equality and a caller Boolean are insufficient authority.

### 4.1 Constructor prefix and installation

1. **Profile preflight:** validate the actual closed registration/options/target and derive the complete simultaneous original owner demands. Missing proof refuses at the existing early gate, before fresh identity acquisition or domain/owner allocation.
2. **Original process admission:** use the existing fresh K/O/P/U route and its qualified G construction/unwind provision. Before the domain header/TCS/arrays allocate, preadmit prospective R against that new private domain's frozen metadata/aggregate budgets. Prospective-to-allocated conversion preserves the same claim; it is not a second debit.
3. **Root returned domain:** after the actual constructor returns, retain that domain in the original reserved binding before the next fallible child construction. A construction-pending phase permits only the exact fresh invoker's reservation/enrollment/commit path. Current terminal-prepared admission is not that permission (`WorldAdmissionDomain.cs:1775–1781,2033–2040`).
4. **Original W/E admission:** reserve the complete W and the separate mandatory E before their controls allocate. Bind the actual lifetime/reservation/lineage as they return, and pre-reserve its legitimate quarantine capacity. The original construction caller stays funded through its own return/unwind.
5. **Standing installation:** only after `new World` returns and `unpublishedWorld` is assigned (`World.cs:767–777`), install the fully priced pending/standing retaining state, construct the qualified owner on the actual owner thread, and install its actual lifetime edge before any factory/publication. All cells remain unused. An unreturned constructor has no invented owner; a returned owner is retained as that exact object.
6. **Publication/promotion:** complete normal initialization, exact budget validation and original fresh commits. Promote the same construction anchor to the exact active lifetime before the selected World escapes. Preserve the original fresh-role transfer/end protocol and qualify its caller suffix; acquire no replacement roles. Only the real returned admission body transfers construction ownership to the live World.

The proposed standing edge/common layout remains a separate open prerequisite. Do not add an inert field/record under current charges merely to compile this sequence.

If the domain constructor never returns, there is no domain object or Retirement Task to manufacture. The prospective owner must cover the actual partial construction/unwind and failure propagation. Refund requires a proved returned-body/last-use end; uncertainty retains the original provision. If existing preowned state cannot represent that prefix's retained claim/failure, the missing representation and its derived charge must be closed before this profile executes. A local finally, null domain reference or emergency allocation is not a durable owner.

### 4.2 Successful enrolled close

Use the existing synchronous `WorldGuidRegistry.InvokeTerminalWorldClose` actor, not a delegate, Task continuation, worker or new outer close object.

| Step | Actual work and surviving owner |
|---|---|
| **1. Elect invocation** | Validate the exact original domain/lifetime/reservation/grant, owner thread, binding and absence of unsupported users. Under existing short gate transactions move Prepared to Invoking. G's original root survives; W/R/E remain held. |
| **2. Run World body** | Begin World close/revocation and logical standing close/handback. Run all normal safe cleanup and nested finally work, including structural and terminal-invocation flag resets. The selected child must defer the final `CompleteWorldClose` handback to its real process invoker. Logical owner Returned is not a debit. |
| **3. Observe actual return** | The World helper ordinarily returns to that invoker with W and any not-yet-ended E still held. Existing typed references/out scalars may carry the exact original return facts. A throwing body produces no successful return fact; use §5. |
| **4. Finish E** | Close the original source handoff from §3. Complete service/source/control/Lock and actual caller accesses before the original E debit. The World interest still survives, so this source end cannot become the selected last Retirement publisher. If any payload/source-use/return debt remains, stop success and retain it. |
| **5. Finish W authority** | Accept the actual returned World body and complete standing/lifetime/lineage/permit work while W is charged. Escrow the exact original permit once into issuer-owned binding state. Finish every permit-owner consultation. Clear the domain's owning lifetime/standing links while W survives; selected later acceptance must not require `_terminalLifetime` or consult a spent permit. |
| **6. End W and tables** | Prepare and commit the original W end after its last owning access and gate exit. Complete source/World reciprocal-reference work before debit. Under R, retire the actual ordinary lineage/interest/reservation/index backing only after its final access. No W/E-owned work may follow this cut. |
| **7. Elect sole outcome** | Validate closed admission, no actual data/interests/reservations/growth/raw tickets/return debt, zero total and retained-source credits, ended ordinary tables, absence of cleanup failure, and the exact issuer-established escrow/invocation. Elect one publisher before calling the TCS. Exit **all process, domain and storage gates** before delivery. |
| **8. Publish under R** | Call the existing TCS once. Success may become visible here with R and the original G root still held, but W/E/tables ended. Actual completion/dispatch/context/tracing children must fit the qualified R owner profile; `RunContinuationsAsynchronously` is not that proof. |
| **9. Accept publisher return** | Observe the actual synchronous publisher return to the enclosing registrar/invoker and finish its R-owned bookkeeping. `TrySetResult`'s Boolean and `Task.IsCompleted` alone acknowledge neither another thread's publisher return nor its caller's end. |
| **10. Prepare irreversible end** | While R and the original G root survive, validate all exact generations/counts/index/role effects and complete every fallible domain acknowledgment. Clear reciprocal terminal fields, finish domain work, and exit all domain gate scopes. Capture only process-local grant/scalar release facts; no later lifetime/permit/domain lookup is allowed. |
| **11. End R, then G roles** | With exclusive prepared terminal disposition, the original R debit is the **last domain-owned operation**, outside domain gate scopes. Its enclosing method must have a proved harmless return suffix. After that real domain operation returns, release the original binding/owner-U/key roles with prevalidated nonthrowing process-only operations. Clear the process terminal root only here. |
| **12. Return process-only** | Exit only G's gate and the public forwarding epilogues. Prove no ended W/R/E access, allocation under their ownership, callback, refusing acknowledgment or cleanup in that suffix. Installed process slabs remain charged at high water. |

Do not hold a process gate across World cleanup, TCS publication or dispatch: an enclosing actor is not an enclosing lock scope. Where gates must coexist, preserve process-registry → exact-domain growth/membership order (`docs\design\29-project-setup-build-and-deployment.md:88–100`).

Steps 5–11 are not the current implementation. In particular, `ReleaseBindingCell` currently clears the root, and `ReleaseKeyIfUnowned`/`RemoveIndexEntry` still have throwing validation (`WorldGuidRegistry.cs:1934–1985,2025–2069`). Move those conditions and checked-arithmetic validation before destructive release; do not perform another fallible `Try...` after the owner disappears.

### 4.3 Complete publication-entry membership

The current direct call set was independently derived by source/symbol navigation:

| Entry / publication call site | First-profile disposition |
|---|---|
| `WorldAdmissionDomain.Dispose` / main file `:290` | Default-domain logical close is nonterminal while the World interest survives. A pre-World failure belongs to the actual constructor-prefix owner, not this enrolled success lane. |
| `ReleaseRetainedDiagnosticSourceCredit` / `:476` | Mandatory E end occurs with the World interest still held. A later external-source publisher needs its own actual original caller/profile and is unsupported here. |
| `ReleaseWorld` / `:541` | Unenrolled reservation/constructor rollback. Keep the actual creating caller and prospective original ownership through unwind; do not fabricate a standing World or use enrolled success readiness. |
| `ReleaseHistoryInterest` / `:413`; `ReleaseOperationCredit` / `:443`; `RollbackUnissuedWorldGuidUse` / `:630`; `RollbackWorldGuidUseRaw` / `:696`; `TryReleaseIndependentUse` / `:1255` | No corresponding producer is issued in this profile. Unsupported selection must be excluded before raw/count/control acquisition, not after debt exists. Preserve valid unselected behavior. |
| `FinishWorldRetirement` / SceneStorage partial `:807` | The selected enrolled terminal entry, moved after the actual World-body return and all W authority work. It alone reaches the selected success election. |

Scene/restore/resource/use returns into `EndWorldReturnPart` are additional ingresses into that last entry, not additional TCS setters. They remain unsupported before acquisition. Every relevant election/release path must recognize selected construction/return/retained disposition before mutation.

Do not broaden the first lane by exempting a late E publisher. The existing unselected compatibility lane legitimately releases a binding while E still keeps the domain charged and Retirement pending (`tests\TerraFX.Runtime.Tests\Entities\WorldGuidRegistryTests.cs:502–520`). Preserve it; it neither qualifies selected standing storage nor permits a successful Retirement with E outstanding.

### Distinguishing example

After steps 6–7, W=0, E=0, data interests/ordinary registry tables are ended, and only the original R publication provision survives in this domain. Step 8 makes Retirement successful. A concurrent fresh request for the **same WorldGuid** must still observe the exact retained original binding and fail with the existing `GuidAlreadyRetained`; it may succeed only after step 11 releases those roles.

This separates data-retirement outcome, registrar return and key reuse without a new public receipt. Conversely, if an E use survives, successful Retirement remains unavailable regardless of zero World interests or a logically returned standing owner.

## 5. Cancellation and failures

`World.Create`/`Dispose` introduce no CancellationToken. The existing `TryCancelTerminalAnchor` is preparation cancellation, not cancellation of the domain Task.

The following dispositions govern the proposed selected route; they do not silently replace the existing unselected compatibility protocol.

- **Before ownership starts:** exact preparation cancellation may detach only after proving no selected pending/installed storage, construction borrower, failure or unreturned child depends on it. Preserve ordinary unselected cancellation.
- **After prospective construction or invocation starts:** cancellation must not detach the mandatory selected root, refund W/R/E, undo escrow or revoke accepted work. Current cancellation recognizes only Prepared (`WorldAdmissionDomain.TerminalAnchor.cs:295–352`); a selected owned preparation must not accidentally inherit that detachable state.
- **Domain Dispose:** closes external admission without waiting; it is not another publisher or a debt-clearing escape. Preserve the existing refusal when a retained anchor excludes that caller (`WorldAdmissionDomain.cs:260–290`).
- **Later async compositions:** must specify their own preadmission token channel and real producer. Canceling an observation never ends accepted ownership or native work. No such composition is enabled here.

| Failure frontier | Required outcome and retained ownership |
|---|---|
| Before selected grant/allocation | Preserve conventional validation/refusal; no selected owner, publication or debit is invented. |
| Partial construction, no returned child | Preserve the actual original pending provision and primary exception through its real unwind. There is no invented child owner; prefix representation/retention must be proved before execution. |
| Returned standing child or failed source return | Retain the exact child/credit/pending debt under the original lifetime/domain/source owner before a fallible cleanup or link clear. Attempt only independent safe cleanup. Unresolved W/E remains charged and blocks successful election. |
| World cleanup or prepublication registrar failure | After actual active callers/finally work unwind, establish the original quarantine/terminal root and retain all remaining W/E/R/G claims. Elect the existing Task's **fault** outcome under qualified R, outside gates, with the contractually selected escaping failure. Do not leave success selected or treat fault publication as release. |
| False/competing/throwing setter | Retain R/G and the actual failure. A false result is not a returned-owner witness or permission to refund; do not retry or republish. If the Task is already terminal, preserve that outcome and confirmed partial facts. |
| Failure after visible success | Never rewrite the Task, resurrect ended W/E, recreate a consumed permit/role, or claim full registrar return. Retain remaining R/G and the escaping failure. A reachable postvisibility failure that invalidates retired data is a qualification failure: eliminate/prove it impossible for the supported success lane before activation. |

The fault path must use the **same existing TCS**, with no second promise/observer. Close its actual fault-graph and setter-return funding; the current `_terminalFailure` assignment alone does not implement it. Publish failure only after active cleanup callers have actually returned/unwound and remaining ownership has a discoverable original quarantine root. This reports cleanup/quarantine failure, not successful physical release.

Preserve existing construction/system/attachment/lower-cleanup exception precedence and bare-rethrow identity. Retain required primary and secondary facts in their actual preowned controls; do not manufacture an aggregate or drop a primary because a single slot is convenient. If current fields cannot retain the required graph, that is an explicit representation/funding gate, not a reason to allocate an emergency owner in a catch.

Confirmed subreleases remain confirmed. Retain only **remaining** claims, never pretend a partial release was rolled back. A failed/retained anchor may consume capacity until process shutdown. Repeated Dispose must not retry destructive releases, detach the retained root or return credits; no unbounded retry queue or silent fallback is introduced.

## 6. Implementation and test sequence

| Boundary / owning work | Required action and gate |
|---|---|
| **Tentative owner decision** | Use §1's yes decision for static/accounting work. Chapter 30 disposition is still required before activation. No maintained chapter or public declaration changes in this proposal. |
| **Original funding closure** | Derive disjoint W/R/G/E and shared registration/quarantine/attachment rows from actual types/sites/capacities and simultaneous peaks. Include prospective domain construction, cold/shared initialization, exception/fault graphs, synchronization, dispatch/context/tracing and native children. Establish original end-or-retain owners before adding declarations or allocating. Constants, shallow measurements and warm-zero samples do not complete this gate. |
| **Lower return implementation** | Implement §4's original typed synchronous construction/close coordination, E continuity, exact escrow/election and prepared R/G end together. Reuse existing private controls/state and unboxed locals where sufficient; no generic receipt/observer protocol. Preserve unselected late-source, Scene and factory-failure behavior. Keep all particle selection/owner guards closed. |
| **Standing representation** | Separately close the actual lifetime retaining edge, owner/backing/record and pending/failure membership. Price common deltas on selected and unselected fresh/restore routes before those layouts execute. Do not add an inert field under old charges or open operations from a successful logical handback. |
| **Compiled/runtime proof** | Freeze final candidate source/schema/registration/options and actual compiler/modules/runtime/CoreLib. Inspect Debug then Release IL/EH and actual JIT tiers/AOT code for every World/source/permit/publisher/domain/gate/forwarding suffix. Observe required real allocation, synchronization, fault/dispatch and final-access sites. Neither old IL/MVIDs nor constructor-free shell sizes qualify current returns. |
| **Behavior and admission** | Only after complete original funding, return proof and applicable actual observations exist, qualify the exact real storage-only owner route. Preserve early refusal until then; do not bypass a guard to discover its budget. Operation/pool/World participant/rendering activation remains separately gated. |
| **Real consumers / milestone** | Close the actual M03-B Scene/assets/camera producer and complete particle registration/pool/lifetime/state/extraction contract before positive particle observations. Then separately qualify headless/native/public/AOT consumers under existing Chapter 35 gates. R's policy decision does not close any of these gates. |

The unqualified R observation is the actual `WorldAdmissionDomain.PublishRetirement` success/fault completion and enclosing return graph, including runtime dispatch, synchronization and exception children. Retry when the complete original preclaims, candidate code and exact loaded runtime/module/context inputs exist and the required real stimulus is available; no unchanged blocked probe or synthetic substitute establishes that observation. Native particle observations remain blocked at Chapter 35's M05 particle/World-bound consumer gates by the unfunded standing/pool path and missing qualified M03-B Scene producer; retry only after those actual owners and producers close.

Add focused cases to the existing Runtime fixtures when their implementation boundary is ready:

1. Preserve explicit/duplicate/frozen/staging selection and same-GUID reuse after early missing-profile refusal (`WorldOptionsTests.cs:15–54`). Test exact profile mismatch and unsupported input without selected allocation.
2. Exercise real original-grant/binding/generation validation, source end and returned World-body ordering; distinguish Task visibility, retained R/O/owner-U/key and actual final return. Use actual production sites/external debugger or runtime evidence for intermediate boundaries, not a new test-only delay/callback/publisher.
3. Test independently derived exact-fit and one-byte-short **reached** World/domain/registration endpoints, including R/E/protection overlap and retained capacity. An earlier dominating cap or unknown full demand is unsupported, not the intended endpoint pass.
4. Cover actual available construction/source/cleanup/pre- and post-escrow failures, original fault identity/precedence, remaining-debit retention and no destructive retry. Wrong/default/foreign/stale/replayed handbacks must never refund. Do not add an application Release callback to claim the empty first input's unavailable failure stimulus.
5. Preserve the existing unselected terminal, concurrent/late-source and system-cleanup regressions. They establish their exercised behavior, not selected particle standing-owner or native qualification.

Use the ordinary owning NUnit/VSTest project after a coherent implementation batch, Debug before Release; for example:

```powershell
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~WorldOptionsTests|FullyQualifiedName~WorldGuidRegistryTests|FullyQualifiedName~WorldAdmissionDomainTests|FullyQualifiedName~WorldLifetimeControlQuarantineTests|FullyQualifiedName~WorldSystemFactoryTests"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~WorldOptionsTests|FullyQualifiedName~WorldGuidRegistryTests|FullyQualifiedName~WorldAdmissionDomainTests|FullyQualifiedName~WorldLifetimeControlQuarantineTests|FullyQualifiedName~WorldSystemFactoryTests"
```

Confirm actual nonempty selections/execution and current TRX under `artifacts\tst`; expand only for affected seams. These are future validation instructions, not commands executed for this documentation-only proposal.

The owner boundaries remain:

- Chapter 18 owns shader/layout/pipeline lifetime; Chapter 19 owns immutable snapshot/graph, GPU and independent presentation completion (`18-shaders-and-material-pipeline.md:15–31,87–106`; `19-render-graph-synchronization-and-presentation.md:5–17,89–97`). Registrar return supplies neither.
- Chapter 20 owns canonical animation/static BindPose (`20-animation-and-deformation.md:11–29`); Chapter 21 §5 owns the 131072 particle ceiling, observable `RejectSpawn` and exclusion from authoritative Physics (`21-2d-and-3d-rendering.md:89–97`). This proposal changes none of them.
- Chapter 28 separates source/managed tests, runtime/native/AOT and release evidence; Chapter 29 keeps Entities ownership and process/domain accounting separate (`28-diagnostics-performance-and-validation.md:81–96,143–153`; `29-project-setup-build-and-deployment.md:51–100`).
- Chapter 35's actual producer and completion gates remain mandatory (`35-implementation-roadmap.md:39–43,109–129`). The M03-B preparation guard and Scene-dependent M05 observations remain closed. Continue only other dependency-ready work with its own prerequisites.

## 7. Review input and validation boundary

Decision inputs were frozen at **`2026-11-20T20:52:55.0662337+00:00`**, HEAD **`14f520edaaaaeb03ad08096afdc92c0169a47745`**. The manifest below was computed afresh from raw working-file bytes. C = inspected context; E = bounded source/contract/test-pattern evidence; S = existing schema declarations, unchanged; P = excluded dirty/untracked preservation context at that snapshot, not decision input. P supplies no owner, grant or qualification evidence. The unrelated diagnostic draft changed concurrently afterward; its P hash records the snapshot, not its current bytes. The five dirty Scene/Sprite test hashes remain preserved.

Manifest SHA-256: **`03FAC865A66E1C1F35D5EB4338965C0E6EFEA1AD1F84CE829EC131740084C207`**. It covers the 55 displayed lines, ordinal-sorted, joined by LF with a final LF, UTF-8 without BOM. This proposal is excluded to avoid a circular hash; freeze its final bytes alongside the inputs when reviewing. Verify the relevant input hashes before applying a disposition. Acceptance applies only to reviewed bytes and scope.

```text
C 03EFEA672CD52E9EF053E9CE0285E7CA02C6CE6FB0D21268ADE4255ED5A39D58  .github\workflows\ci.yml
C 12439DF4BB5E46021891B234BEC1104ABDCA57C11FDF3F610C8F32C5DAF3001E  .github\copilot-instructions.md
C 1BCE01871321C2DF82E887DA428CE5C121A6C6541FE65017FE3D249BE47E190C  global.json
C 2F1C81E54F0FBF12D01E34D0D840885F5EB0E94A8B8890865EF721B8C6A3C65D  sources\TerraFX.Runtime\TerraFX.Runtime.csproj
C 770EE4B2A422861A6F23F0D39DD0ECA361A81CB87E64EBAC4E12170F71231D7A  Directory.Build.props
C D44373AF315523F8AF9F09F36BDED2A688E2B5C1572FCC0732310C7EF590EAD8  sources\Directory.Build.props
C F043A352870EFB6C7D4A61998225E2508A8F4370A57E784483A41819D452075F  docs\project\Building.md
C F9598D3F8005EC0E73200F7EC30882914F15F275D238707D1F502F5948015F3E  docs\project\Roadmap.md
E 0605BF47F60A2E22BDC951A2BCCE938BC2594804603F4A5DF7127D58AF1A6329  tests\TerraFX.Runtime.Tests\Entities\WorldLifetimeControlQuarantineTests.cs
E 0BE478CC58C22019F5E337EBA63869BC87765BC975B50B345621053D812A6DF9  sources\TerraFX.Runtime\Entities\WorldDiagnosticFundingUse.cs
E 0E3C895BEE5F76118CE87D208170CB6E02E9A19DE3F286B7E243B28165370D14  docs\design\28-diagnostics-performance-and-validation.md
E 14F767254F119B9DB7ACD92EC26BF94A3F5DAA42F3AA6480496A8D139E69F24F  docs\proposals\m05-cosmetic-particle-terminal-funding-rectification.md
E 1F66B39F8346EDC4663813B409F5016656840F7861423B71D8F42BEACE2A6BD8  sources\TerraFX.Runtime\Entities\WorldAdmissionDomain.SceneStorage.cs
E 24EEC6E92B7AD904D552F290EF748FFC8FCE13B6B94A89EBD9F39487D4584D87  docs\design\35-implementation-roadmap.md
E 31F0775D13AA40ABD3D2DA154D9A9F8A325BBA34B4C6E9067BB516828D1CB227  sources\TerraFX.Runtime\Rendering\Renderer.CosmeticParticleAssets.cs
E 3570F93343961D8618F1665655B1E36FFCBF023031EA6569D1441A38F915B776  sources\TerraFX.Runtime\Rendering\RenderParticleEmitterComponentDefinitions.cs
E 39CC351A3E0B81EC7F703A26AF416829DFB97EC7315D6F669A74D57EE40AD20A  sources\TerraFX.Runtime\Entities\WorldAdmissionDomain.TerminalAnchor.cs
E 45C2A4D3B48F370175485A0D7BC6C13A3F6B8230F976B34032CE2C13D3125C58  sources\TerraFX.Runtime\Entities\WorldGuidRegistry.cs
E 49598161138FEBBF889F2A5176E3504AF8D8BB8481D2157FBCFBBD94D2B46604  docs\design\29-project-setup-build-and-deployment.md
E 4F1FED2EBE3E418EDA6BBE6C7528D6AD327185EAAFD1F4C0286C760ED32FE331  tests\TerraFX.Runtime.Tests\Entities\WorldGuidRegistryTests.cs
E 5A304AAF8B2353E28F6F743D5587E82F12E979DCF0DFEBF55103E075CCAF54F2  sources\TerraFX.Runtime\Entities\WorldLifetimeControl.cs
E 60047DD1EACF26196B0BF23F16798E2AA265FD26CD36B9A0B4DF1B79135730D7  docs\design\04-capacity-and-workload-profiles.md
E 70DCB73D187AA6FC27B099F8C27B6716D3D63EF6C4022662017D34C25F5A4E20  sources\TerraFX.Runtime\Entities\WorldSystemInstanceRegistry.cs
E 7DB9F67F2C35B2DBDCA1DE13D7530F785655C7BF157FCC3D7C5B92408BFE4863  sources\TerraFX.Runtime\Entities\WorldAdmissionDomain.cs
E 7E300E9E1270DC82E27D5C48FDD67A8653BB917E7D001052C3A177522553DECA  sources\TerraFX.Runtime\Entities\WorldDomainOperationCredit.cs
E 7EEFB227D9654552997BF3515E29CA32E3791F0A155FBF1AA37C274508E2CEA6  sources\TerraFX.Runtime\Entities\WorldCosmeticCreditOwner.cs
E 81F87F907579884295E3B441F21B5EA4FE6B2509790735B1577F2B27B16D2BD6  sources\TerraFX.Runtime\Entities\WorldGuidRetirementPermit.cs
E 87DAFCDB1C57F622625BC2538C72A8A7D2EE54090D8C11A79DD75C0A1FFAA2B6  tests\TerraFX.Runtime.Tests\Entities\WorldSystemFactoryTests.cs
E 8F0EF274C39C3E07E691951011AC86C574311912D7F78348EB8D6780555F0C71  sources\TerraFX.Core\ReentrantGate.cs
E 989A6183186592F165FA9614CC19A8FED63826E2A51170CE55FBFFFCA8116ED2  docs\design\21-2d-and-3d-rendering.md
E A2E7D572F5D8BBDBC0E4E40E004479CF9895283E2F0BE6947BD6CF8AF755197C  sources\TerraFX.Runtime\Entities\WorldConstructionPlan.cs
E A5346D4B04FC594C8713C0628A1035F75C97FE664808BA45069898C095CE16CB  sources\TerraFX.Runtime\Rendering\Renderer.M05AssetCodecs.cs
E A8E1D78B13F8B7CC3887E4523BBB7415198B7D7B09647291F6FDCE58DDF9131F  tests\TerraFX.Runtime.Tests\Entities\WorldAdmissionDomainTests.cs
E AB37920CF44BFF419E3DF7B77800E4428F95F8E139A498839ABF3919D3046BC8  docs\proposals\m05-cosmetic-particle-standing-owner-construction-addendum.md
E AB673DEDBD85B7A161C5D7DC871BF5B486911DC27EC4279AC43827B005241B8B  docs\design\19-render-graph-synchronization-and-presentation.md
E AD662321F076BB0BF40E06634C11A36F44127F6E7E773D6E33BA612876918098  sources\TerraFX.Runtime\Rendering\RendererCpuFeatures.cs
E AE075BEBF688FA767B0FE6906495DBA8DCABA6DD4B59E82BCC9658BBA4C6579C  tests\TerraFX.Runtime.Tests\Entities\WorldOptionsTests.cs
E AF20911D9D06BFB58F6C771511181F59347C4659129CFD5FC8CF622ED191C0D7  docs\design\18-shaders-and-material-pipeline.md
E B28F32C5737F6D120C0988885BBC9DA577D1B737A00C46988FE1C3DC281324EA  sources\TerraFX.Runtime\Entities\WorldGuidRegistry.TerminalAnchor.cs
E B5AE58EA0D5821FAB70001A44D59A100CAF789CFB148107776A026355E9C61D1  docs\design\20-animation-and-deformation.md
E C6F41DE02F0896016FA55845A61661BCD1A4DFE085BF68302767FE9C94DA3228  sources\TerraFX.Runtime\Entities\WorldLifetimeControl.CosmeticCredits.cs
E CBB5BDF2B4F769609D7FF091B14710C9E6520AF81B172D7CD8A16D99E9490FB4  sources\TerraFX.Runtime\Entities\WorldDiagnosticsServiceOwner.cs
E D135F424BCBC93274E9B74B523648A7771008D6EA454E79782566E710A8B5693  docs\proposals\m05-cosmetic-particle-standing-owner-funding-route.md
E D4F622D69ED58AB96118C36FBBF4E2D31E062A3CBF3CE3BA1D64309F662FE4ED  docs\design\30-public-api-review-surface.md
E D6CDD6105850B8076D40891B0ED1F583D9FCDE6EE190444A7D570879964403DC  sources\TerraFX.Runtime\Entities\WorldDomainReservation.cs
E E19FE8BD36A7AF9B06ED2BB48DDF32FB89DCD5F93DF6591F9EBB9EF609FA44B5  sources\TerraFX.Runtime\Entities\WorldDiagnosticFundingControl.cs
E EAD70618FAC9EF668FBEBAEFA35678D19DBFBBE944710D1418CC354C306D42F0  sources\TerraFX.Runtime\Entities\World.cs
E F6F88D50FEF6614A6CB0A8C149400F3931AE6129FD131653C9A92266D46424F4  docs\proposals\m05-diagnostics-terminal-anchor-credit-compatibility.md
P 1B2DF8EF680E90E4D33DFDB24B447097274C5798196D9FB198F2CF4FE89BD380  docs\proposals\m05-diagnostic-first-return-owner-rectification.md
P 4C0841A40295A891B68974FDDD8078DFD0181C31B35AB0BCB0B5114FE0FD1039  tests\TerraFX.Runtime.Tests\Scenes\SceneAssetStoreTests.EngineHost.cs
P 8E65B71B5AC1E0EF9D1F47AA56963054D091DF8C229E74DDBB1CAE37BB59AB60  tests\TerraFX.Runtime.Tests\Scenes\RenderSpriteComponentTests.cs
P ABAEEA4D0F6BE941AF73E640B7FCE41EC753D2199654343FA4668D54EC20FC6A  tests\TerraFX.Runtime.Tests\Scenes\RendererExtractionTests.cs
P CAA6610CF55F8FFFA077745FD96BAA45FCECFED2FD051CC763901AEED5500239  tests\TerraFX.Runtime.Tests\Scenes\RenderSpriteCapacityTests.cs
P E342237B7645BE805DD924855B2239907773D31753D22B8B2261BF26C20452F4  tests\TerraFX.Runtime.Tests\Scenes\PreparedSceneAssetRetentionTests.cs
S DE25A8C0E249FF9AB5B592FE848C85429C004FE629694C64EF1AFEF3BA731614  sources\TerraFX.Graphics\RenderingSchemaCatalogue.cs
```

This document's evidence is current source/symbol/contract inspection and bounded hash/reference/whitespace checks. No build/test, allocation probe, selected World/owner, particle consumer, TCS target method or native observation was executed. No complete runtime/binary correspondence is asserted. Only this additive proposal is authored; source, tests, Roadmap, maintained chapters and existing proposals are not edited, and no commit/push/PR is performed.
