# M05 existing-owner terminal funding rectification

## 1. Decision and limits

**Tentatively approved implementation guidance only; no funded profile, contract acceptance, or execution permission.** Select a field-free **original World provision → original domain terminal provision → process-only suffix** progression, using the existing `WorldGuidRegistry.InvokeTerminalWorldClose`. Each arrow changes which actual work remains; it does **not** transfer another owner's objects or mint credit.

Keep World funding through the complete returned World body and remaining lifetime/permit accesses. End it **before** successful domain-retirement publication. Keep domain funding through the actual publisher, domain bookkeeping and gate exits. End it at the last domain-owned access; only already-funded process-registry work may follow. Do not retain still-used World storage through a successful `Retirement` and call it retired.

This refines [standing-owner funding §4](m05-cosmetic-particle-standing-owner-funding-route.md): its proposed terminal-held W-through-publication cadence is not selected here. Its complete numerical funding, construction, registration, metadata and qualification gates remain open. The [terminal ledger §5](m05-cosmetic-particle-terminal-return-ledger-addendum.md) identifies the required capability, but its old source ordering is not current evidence.

There is **no closed, funded implementable route today**. The existing fields support a bounded synchronous refactoring target; neither their presence nor this proposal establishes finite complete runtime/failure bounds. The public-readiness decision in §5 is also unresolved. If those obligations cannot close without the prohibited additions, retain the guards rather than invent an owner.

No new budget, public API, acceptance identifier, production field/record, padding-based grant, Task, delegate, observer, worker, test-only producer, or operational execution is selected. Preserve:

- `World.AdmitWorld`'s `CosmeticCreditStorageSelected` refusal;
- `WorldConstructionPlan.ValidateForConstruction`'s same refusal;
- `WorldCosmeticCreditOwner.HasQualifiedStandingOwner == false`;
- existing generation, workspace, retirement, quarantine, count and byte guards.

The initial input remains the funding route's fresh standalone/private-default-domain, storage-only empty registration, explicit nonempty identity inputs, zero-worker, owner-thread lane. All 128 cells are unused. Its **mandatory diagnostic source envelope is not excluded**. Hosted/supplied-domain/restore/handoff, Scene, independent-use/history/operation, pool and rendering producers are not admitted by this selection.

## 2. Current evidence and exact owners

`E` below denotes `sources\TerraFX.Runtime\Entities`.

| Frozen current source | Consequence |
|---|---|
| `WorldGuidUseControl.CompleteDomainRelease`, called by domain `TryReleaseIndependentUse`, clears interests/root links/state **before** the use-byte debit. `Release` retains a parent return part and ends it in its final `finally`. | The old post-debit `RetainedOwner`/`Next`/Released-write diagnosis is stale. A real enrolled parent can remain charged through that caller; it is not a new invocation grant. |
| `WorldAdmissionDomain.ReleaseRegistry` leaves `RegistrarControlBytes`; `PublishRetirement` retains it in the anchored Escrowed/Returned/CompletionPending cases. | The old zero-registrar-charge-before-setter diagnosis is stale. The surviving 4096-byte control row is an actual named provision, **not proved complete publisher funding**. |
| `FinishWorldRetirement` removes all original W before returning; afterward `DisposeWithinTerminalInvocation` resets `_isTerminalCloseInvocation`. | W still ends before a reached World write. Merely observing the helper return does not retroactively pay it. |
| `TryAcceptTerminalCallerReturn` reads `lifetime.RetirementPermit`; registry completion calls `permit.IsTerminalEscrowedFor`, which reads the permit's `_owner`. | Copying the permit into a local does not remove post-W owner access. Finish these checks while W lives, then use only the exact registry-owned escrow disposition. |
| `TryCompleteTerminalAnchor` releases O/owner-U/key roles before fallible `CompleteTerminalAnchorBindingRelease`. That domain method can zero `_registryCharge` inside two gate scopes, then clear fields. | The remaining failure root and domain gate/access suffix do not meet a prepared, last-access commit. |
| `WorldDiagnosticFundingControl.ReleaseUse` clears `_credit` before its child returns; credit `ReleaseCharge` writes `_isChargeReleased` and exits its Lock after domain debit. Service close/reconciliation clears retaining links before child return. | Mandatory E still needs its actual control/caller and failed-return ownership proof. A zero payload balance or null link is not that proof. |

The only selected retaining storage is **already present**: domain interest `WorldLifetime`/`WorldCloseCarrier`/return-progress fields; domain `_terminalRegistry`, `_terminalGrant`, `_terminalLifetime`, `_terminalFailure`, `_terminalAnchorState`, `_terminalWorldReturnReady`; original binding `_terminalDomain`, `_terminalFailure`, `_terminalState`; and the existing reservation/World invocation fields. Use caller-local existing types and scalars, not a new receipt/standing record. The lifetime currently has **no installed cosmetic standing-owner edge**; this proposal does not supply one.

| Claim | Actual issuer, retained graph and required end |
|---|---|
| **W: original World reservation** | `WorldAdmissionDomain`, exact reservation interest/generation and `Bytes`; rooted through the enrolled interest and lifetime. Covers qualified World construction/close/control work, not the separately debited E. Hold through the complete World-body return, lifetime/permit fields and Lock exit; debit only after their last access. Current `4096 + 4 * IntPtr.Size` is a source accounting input, not complete B/protection evidence. |
| **R: original registrar/control provision** | The same actual domain's `_registryCharge`, under Chapter 04 metadata B and aggregate domain limit. Retire only ended arrays/membership backing; retain the original control/protected provision through publisher and domain-return work. Its named 4096 floor cannot be supplemented from released page allowances or numeric headroom without an independently derived, originally preadmitted owner charge. No such additional amount is selected. |
| **G: process-registry provision** | `WorldGuidRegistryAccounting1`, including selected control, fixed index/directories and installed high-water slabs. Original exact O/owner-U/K retains the terminal root; fresh P/coordinator-U has its separate construction end. The remaining process-only release/gate/return suffix stays with this owner. Cell release does not refund installed slabs. This G is the funding route's process provision, not a new slab-allocation ticket. |
| **E: retained diagnostic source** | Original dedicated `WorldDomainOperationCredit`, `WorldDiagnosticFundingControl` and Runtime service owner. One full envelope, shared by initial World/service-baseline uses; not another W debit. Return only through its actual last-use/control end or an explicit original-issuer-funded handoff. Neither W, R nor G acquires its control/failure graph by reference copying. |
| **Issued independent use, when separately supported** | Exact process U plus `Anchor.OperationBytes` and its history/operation interests. Original `256 + W_workspace`, before actual transfers, is not the standing provision. No such use is issued in the first input. |

W/E/R coexist and obey their existing combined limits. G and shared registry backing have separate owners. `WorldSystemInstanceRegistry.ReserveQuarantineCapacity(0)` supplies one lifetime **count slot**, not byte funding: its shared Q and attachment-registry coverage remain separate open terms. Historical 10,592 sizing inputs, measured shallow sizes and unused numeric ceilings discharge none of these obligations.

## 3. Narrow source/proof target

These are future owning-source steps, **not edits or operations performed here**.

1. **Move the selected final World handback to its real existing caller.** Make `DisposeWithinTerminalInvocation` return/unwind after every World-reading cleanup and its invocation-flag reset, with W still live. The selected child must not call the final `CompleteWorldClose` from inside its own `finally`. Pass the exact existing reservation and close facts as unboxed caller locals/out scalars; the process invoker performs that existing handback after ordinary return. Preserve the unselected route and cleanup order/exception precedence. A throwing body supplies no success handback.
2. **Finish lifetime authority while W is live.** `CompleteWorldClose`/`FinishWorldRetirement` may finish lineage and `CompleteRetirement`, including the existing exact permit's one-time terminal escrow. Complete every lifetime/permit consultation and Lock exit before W debit. Remove later dependence on `lifetime.RetirementPermit` and `IsTerminalEscrowedFor`'s owner reads; later acceptance uses the exact issuer-validated original grant, generations, domain and existing binding Escrowed state. A caller Boolean or `_terminalWorldReturnReady` alone cannot substitute for that authority or ordinary return.
3. **End W, then publish under R.** Complete World/control/reciprocal-reference work before ending the original interest/data debit. Afterward no publisher, acceptor or forwarding epilogue may access the ended World/lifetime/reservation/owner. Retire actual registry backing only when all data/use/E obligations permit it. Call the existing TCS directly outside owner/storage gates, retaining R and the original process anchor through its actual return and subsequent domain bookkeeping. This does not loan R to still-used W/E storage.
4. **Prepare the domain/process commit before releasing its root.** Revalidate generations, escrow/returned phases, counts, byte totals, index membership and release effects under process → domain order while R/O remain. Complete all fallible domain acknowledgment, terminal-field clearing and **domain gate exits before** R's final debit. That debit must be the last domain-owned operation. Only after this whole domain operation has ordinarily returned may process-local binding/owner-U/key release finish. No later domain/lifetime callback is permitted. Prevalidate `ReleaseKeyIfUnowned`/`RemoveIndexEntry`'s presently throwing conditions and Debug checked arithmetic; another `Try...` after destroying authority is not a prepared commit.
5. **Close the actual prefix and mandatory E seams before construction.** Derive prospective original R before `new WorldAdmissionDomain`/its header, TCS and arrays, and cover a constructor that never returns. The present constructor installs `_registryCharge` after allocating children; later W cannot fund that prefix. Separately close the named E control/service return and failure-root seams above. Any transfer must name an actual funded receiver and conserve the original issuer's bytes/counts before debit. If existing fields/claims cannot represent that exact prefix or retained debt, report the missing capability; do not add a record or invent a grant.

The final R debit is followed only by a proved no-R/W/E-access/no-owned-allocation suffix. G covers its **own** remaining registry instructions and gate exit. The public `Dispose` forwarding return must be harmless too. `NoInlining`, source `finally`, state enums and passive references are not compiled/native proofs.

### Publication and ingress closure

Current symbol incoming calls independently identify **nine entries**, not five/seven/eight conformance cases:

| Entry set | First-input disposition |
|---|---|
| `FinishWorldRetirement` | Enrolled terminal entry, only after the complete returned World body and exact escrow. |
| `Dispose`, `ReleaseRetainedDiagnosticSourceCredit` | Normal default close/source return is nonterminal while the World interest survives. Prove this ordering and E's actual return; do not exempt late source publishers. |
| `ReleaseWorld` | Unenrolled construction/rollback entry. Its real fresh caller/prefix/unwind funding remains required; do not fabricate an enrolled World or standing child. |
| `ReleaseHistoryInterest`, `ReleaseOperationCredit`, `RollbackUnissuedWorldGuidUse`, `RollbackWorldGuidUseRaw`, `TryReleaseIndependentUse` | No corresponding producer is issued in this input. Enforce unsupported selection **before acquisition**, including Scene/restore ingresses into `EndWorldReturnPart`; preserve ordinary unselected behavior. |

The historical last-use-without-an-active-enrolled-parent slice is **not closed** by this World-close actor. Current `TryRetainWorldGuidUseReturnPart` can validly return no parent there. A later admitted particle/use producer needs its original standing/use caller claim and its own lower terminal end; do not borrow this profile or extend O/U/K by fabrication.

Current `Retirement` symbol references are tests, and the default-domain anchor attach helper's callers are tests. That identifies current source membership, not future observer absence, runtime mode or operational qualification. Do not add an observer to manufacture return.

## 4. Failure and end semantics

- Before successful admission/authority capture, preserve conventional usage/refusal channels and all original claims. No foreign/default/stale/replayed handback debits anything.
- After a child throws, finish its existing protected safe cleanup/unwind, retain the **actual escaping exception** by reference in the preowned terminal failure path, and preserve bare-rethrow identity/token and existing mandatory lower-cleanup precedence. The remaining enrolled interest/terminal domain/original binding supplies the root; a count slot or exception reference does not price the reachable graph.
- Preserve confirmed process/domain/receiver subreleases and journal facts. Never recreate a released U, consumed permit, interest or refunded bytes. A failed commit cannot be described as all-or-none after a destructive subrelease.
- Retain all remaining original provision/root on failure; repeated Dispose/Quarantined no-op is not debt settlement or permission to retry. Retention until process shutdown consumes actual capacity. No emergency allocation or global tail list is introduced.
- `TryRetainTerminalFailure` currently retains failure but does not publish `_retirement` fault. Chapter 30 requires cleanup/quarantine **fault, not successful release**: the complete funded existing-TCS fault disposition remains an R gate. If publication already became terminal, preserve its outcome; neither rollback nor republishing is possible. A reachable later failure that would invalidate successful retirement must be eliminated/proved harmless for the supported success lane, not concealed by a returned Boolean.

The success handback is the real enclosing caller's acceptance followed by prepared last-access debits. Task terminality, zero counters, Released/Retired or logical `AcceptOwnerReturn` establishes neither invoker end nor physical funding return.

## 5. Genuine owner decision and finite-route limit

Chapter 30 says `Retirement` completes only after all actual domain interests, allocations and registry storage retire. **Owner review must resolve whether the already charged, solely terminal-publisher control/provision R may survive outcome publication after every retireable data allocation and registry table has ended.** Recommend that narrow last-access interpretation, conditional on the complete R profile and harmless suffix; this is proposed clarification, not a claim that the current sentence already grants it. It exempts no live World/E/backing or arbitrary framework allocation.

If the contract instead requires **R itself, including the completion control, to end before the Task becomes terminal**, there is no finite implementable Task-based route within this request's constraints. The setter still accesses its Task/continuation fields after publishing terminal state. A wrapper, delayed decrement or additional outcome Task cannot acknowledge its own later return. Likewise a literal physical-return requirement for every public caller needs an independently funded outer acknowledgment absent from the existing void/Task interfaces.

No further user decision is needed to write this proposal or pursue static owner/claim closure. The readiness interpretation requires confirmation **before activation**; tentative implementation guidance does not waive that contract gate. Missing numeric/runtime bounds are evidence blockers, not permission to select an arbitrary margin or assert that the runtime is necessarily unbounded.

## 6. Exact remaining gates and evidence levels

| Gate / actual owner | Required closure; current state |
|---|---|
| W and standing construction/close caller | Complete disjoint B/common/selected/protected simultaneous peak, original preadmission and retained attribution; complete returned World/lifetime/permit boundary. The live standing retention edge and any later changed layout remain absent/unqualified. **Open.** |
| R, publication and original prefix | Complete domain header/TCS/Task/gate/cold/failure/context peak; prospective original claim before allocation; unique publisher across the nine entries; fault/root retention; public-readiness disposition and final domain end. 4096 floor retention is not the proof. **Open.** |
| G and final process authority | Complete cold/growth/invocation/retained-failure/gate coverage within existing `WorldGuidRegistryAccounting1`; original exact roles remain through prepared commit; no throwing domain call or spent-owner read afterward. Shared Q/attachment preclaim remains with its respective registry, not G. **Open.** |
| Mandatory E and World-reached publishers/returns | Exact source-control, service/baseline, account and shared-clock ownership; discoverable original return debt; complete control/caller end or funded original-issuer handoff. No successful domain retirement with E live. **Open.** |
| Source/compiled proof | Freeze the final field-free source, registration/options and actual compiler/assemblies; inspect Debug then Release IL/EH and all affected return paths, including gate scopes and forwarding suffixes. Match source to modules. **Current source ordering only; candidate unimplemented.** |
| Runtime/native qualification | Establish actual loaded runtime/CoreLib, native tiers/AOT, cold/synchronization/tracing/context/exception/dispatch children and actual last accesses. Supply unavailable observations' precise owner/site and retry condition. **Not performed or qualified.** |
| Admission and real consumers | After the exact owner's funding/qualification closes, separately close standing representation/construction, operation use, real particle/Scene provenance and applicable M05 observations under Chapter 35. No pool selection/admission from this proposal, shallow tests or static reasoning. Unrelated dependency-ready M03-B/M05 work may continue. **Closed to activation.** |

The retained IL JSON records CoreLib `11.0.0-rc.3.26522.148+5254d8bcf59917edc1e1abc33bdf2442b77b1c70`, SHA-256 `DF53885902E896A2B40988237CBE9CBB9F1BB0743200F05FE9257C56A29C5732`, MVID `4d3da96b-886f-4bbc-ab65-95c08e83c881`. In that recorded module, `Task.TrySetResult` (`0x0600425D`) calls `FinishContinuations` at IL 41 after terminal-state update; `FinishContinuations` (`0x06004261`) exchanges its continuation field at IL 11. TCS `TrySetResult` (`0x0600434B`) can spin on the false path. `RunOrQueueCompletionAction` can invoke an internal action directly or allocate/queue an invoker. These explain why outcome-only handback and asynchronous-continuation options are insufficient.

Those are **retained read-only IL provenance/order observations**, not current loaded-binary correspondence, finite allocation bounds, native return proof, or new execution. No TerraFX/CoreLib target method, constructor, release, TCS setter, build, test or native probe ran for this proposal. Later behavior validation uses the existing Runtime NUnit project/ordinary [Debug then Release workflow](../project/Building.md), nonempty executed selections and real existing producers; unavailable stimuli remain explicit gaps.

## 7. Frozen input identity and validation

Frozen at `2026-11-20T19:23:08.984521Z`, HEAD `55c378c781c03da8e8d2d2787a010f6727ece913`. Hashes are raw **working-file bytes**, not Git blob identities. C = inspected build/workflow/status context; E = bounded source/contract/retained-IL evidence; P = preservation only, not qualification input.

Manifest identity: **`1C27DE757A542900C1FDADE23FDB3DDF4AC9F02A9D349183BFF64A339AC18FE4`**. It is SHA-256 of the 50 displayed manifest lines, ordinal-sorted, joined by LF with one final LF, UTF-8 without BOM; each line is `role SHA256  relative-path`. This proposal is excluded to avoid a circular hash. Revalidate E/C bytes before applying any disposition; acceptance is limited to the reviewed bytes and scope.

The five task-entry dirty Scene/Sprite tests are preserved at their entry hashes. `BoundedStaticMeshAssetValidationTests.cs` became dirty concurrently and is also preservation-only. No schema/fixture/API declaration is changed or supplied by this lower-owner proposal; no executable schema profile is qualified.

```text
C 03EFEA672CD52E9EF053E9CE0285E7CA02C6CE6FB0D21268ADE4255ED5A39D58  .github\workflows\ci.yml
C 12439DF4BB5E46021891B234BEC1104ABDCA57C11FDF3F610C8F32C5DAF3001E  .github\copilot-instructions.md
C 1BCE01871321C2DF82E887DA428CE5C121A6C6541FE65017FE3D249BE47E190C  global.json
C 2F1C81E54F0FBF12D01E34D0D840885F5EB0E94A8B8890865EF721B8C6A3C65D  sources\TerraFX.Runtime\TerraFX.Runtime.csproj
C 770EE4B2A422861A6F23F0D39DD0ECA361A81CB87E64EBAC4E12170F71231D7A  Directory.Build.props
C D44373AF315523F8AF9F09F36BDED2A688E2B5C1572FCC0732310C7EF590EAD8  sources\Directory.Build.props
C F043A352870EFB6C7D4A61998225E2508A8F4370A57E784483A41819D452075F  docs\project\Building.md
C F9598D3F8005EC0E73200F7EC30882914F15F275D238707D1F502F5948015F3E  docs\project\Roadmap.md
E 098DA320E61AA3171B5B09D1C11DAA0CD03B67AFB5B1CA5DA5C75801987744FB  docs\proposals\m05-cosmetic-particle-protected-close-coverage-addendum.md
E 0A317BE624225E5A6873F82D491FDD93B2C01A91DCBAF1CF96153ADFE4659DB8  docs\proposals\m05-cosmetic-particle-terminal-return-ledger-addendum.md
E 0A9AE81EB42D886168D9E05970C7395B33C9EF70FCCADA9ED311CF2E1DC7AD89  docs\proposals\m03b-scene-preparation-owner-aware-async-handback.md
E 174B2465746DA79CEF560C8506216EF4F99F9D1AABAC184F872C9E6CB0B94E0B  docs\design\12-entities-components-and-queries.md
E 1C3FA0C41D8EF5272DEAA0A16A575FD83B7742F3752F0F4D92D93A9980A9AF9A  docs\proposals\m05-cosmetic-particle-domain-terminal-authority-addendum.md
E 1E97D990D2AFF1E2A92061919C4E105EB00E7CFC09C05FAC82F6B3979D0D0386  docs\proposals\m05-cosmetic-particle-synchronous-release-invocation-addendum.md
E 1F66B39F8346EDC4663813B409F5016656840F7861423B71D8F42BEACE2A6BD8  sources\TerraFX.Runtime\Entities\WorldAdmissionDomain.SceneStorage.cs
E 24EEC6E92B7AD904D552F290EF748FFC8FCE13B6B94A89EBD9F39487D4584D87  docs\design\35-implementation-roadmap.md
E 39CC351A3E0B81EC7F703A26AF416829DFB97EC7315D6F669A74D57EE40AD20A  sources\TerraFX.Runtime\Entities\WorldAdmissionDomain.TerminalAnchor.cs
E 3D16317E7B76ACF940EEB86E3B6827F4E8A632AD16B628A8731B853035839EC4  artifacts\qcl\t\corelib-completion-il.json
E 40F913A50434209E09941C22625A6D0722202BAB97EBAC0E153C1EA3368E2291  docs\design\02-dotnet-api-memory-and-ownership.md
E 45C2A4D3B48F370175485A0D7BC6C13A3F6B8230F976B34032CE2C13D3125C58  sources\TerraFX.Runtime\Entities\WorldGuidRegistry.cs
E 49598161138FEBBF889F2A5176E3504AF8D8BB8481D2157FBCFBBD94D2B46604  docs\design\29-project-setup-build-and-deployment.md
E 5A304AAF8B2353E28F6F743D5587E82F12E979DCF0DFEBF55103E075CCAF54F2  sources\TerraFX.Runtime\Entities\WorldLifetimeControl.cs
E 60047DD1EACF26196B0BF23F16798E2AA265FD26CD36B9A0B4DF1B79135730D7  docs\design\04-capacity-and-workload-profiles.md
E 6F997FEFECF9702FD5C182415ED37D0D09C6C1C930C6385F0404824712E2E306  artifacts\qcl\t\pinned-reference-inputs.json
E 70DCB73D187AA6FC27B099F8C27B6716D3D63EF6C4022662017D34C25F5A4E20  sources\TerraFX.Runtime\Entities\WorldSystemInstanceRegistry.cs
E 7DB9F67F2C35B2DBDCA1DE13D7530F785655C7BF157FCC3D7C5B92408BFE4863  sources\TerraFX.Runtime\Entities\WorldAdmissionDomain.cs
E 7E300E9E1270DC82E27D5C48FDD67A8653BB917E7D001052C3A177522553DECA  sources\TerraFX.Runtime\Entities\WorldDomainOperationCredit.cs
E 7EEFB227D9654552997BF3515E29CA32E3791F0A155FBF1AA37C274508E2CEA6  sources\TerraFX.Runtime\Entities\WorldCosmeticCreditOwner.cs
E 81F87F907579884295E3B441F21B5EA4FE6B2509790735B1577F2B27B16D2BD6  sources\TerraFX.Runtime\Entities\WorldGuidRetirementPermit.cs
E 8F0EF274C39C3E07E691951011AC86C574311912D7F78348EB8D6780555F0C71  sources\TerraFX.Core\ReentrantGate.cs
E 989A6183186592F165FA9614CC19A8FED63826E2A51170CE55FBFFFCA8116ED2  docs\design\21-2d-and-3d-rendering.md
E A2E7D572F5D8BBDBC0E4E40E004479CF9895283E2F0BE6947BD6CF8AF755197C  sources\TerraFX.Runtime\Entities\WorldConstructionPlan.cs
E A6120452989F29EC2EF9C2B90E7479CC6635B9B17848C6C683063B3F99D36D22  sources\TerraFX.Runtime\Entities\WorldGuidUse.cs
E B28F32C5737F6D120C0988885BBC9DA577D1B737A00C46988FE1C3DC281324EA  sources\TerraFX.Runtime\Entities\WorldGuidRegistry.TerminalAnchor.cs
E B83381C2308204C8D437687FA1212EC332C557DD79CCA7E1615E0A98C648FAED  docs\proposals\m05-cosmetic-particle-terminal-caller-return-closure-addendum.md
E C6F41DE02F0896016FA55845A61661BCD1A4DFE085BF68302767FE9C94DA3228  sources\TerraFX.Runtime\Entities\WorldLifetimeControl.CosmeticCredits.cs
E CBB5BDF2B4F769609D7FF091B14710C9E6520AF81B172D7CD8A16D99E9490FB4  sources\TerraFX.Runtime\Entities\WorldDiagnosticsServiceOwner.cs
E D135F424BCBC93274E9B74B523648A7771008D6EA454E79782566E710A8B5693  docs\proposals\m05-cosmetic-particle-standing-owner-funding-route.md
E D4F622D69ED58AB96118C36FBBF4E2D31E062A3CBF3CE3BA1D64309F662FE4ED  docs\design\30-public-api-review-surface.md
E D6CDD6105850B8076D40891B0ED1F583D9FCDE6EE190444A7D570879964403DC  sources\TerraFX.Runtime\Entities\WorldDomainReservation.cs
E E19FE8BD36A7AF9B06ED2BB48DDF32FB89DCD5F93DF6591F9EBB9EF609FA44B5  sources\TerraFX.Runtime\Entities\WorldDiagnosticFundingControl.cs
E EAD70618FAC9EF668FBEBAEFA35678D19DBFBBE944710D1418CC354C306D42F0  sources\TerraFX.Runtime\Entities\World.cs
E F00B54B0E8F305F90DF0D63FFDF6C3419FEB8F3D2542E72D76A79AD2D0990946  sources\TerraFX.Runtime\Entities\WorldGuidUseControl.cs
E F6F88D50FEF6614A6CB0A8C149400F3931AE6129FD131653C9A92266D46424F4  docs\proposals\m05-diagnostics-terminal-anchor-credit-compatibility.md
P 4C0841A40295A891B68974FDDD8078DFD0181C31B35AB0BCB0B5114FE0FD1039  tests\TerraFX.Runtime.Tests\Scenes\SceneAssetStoreTests.EngineHost.cs
P 73AB54BFC79FEAD31951E2B94B07E2961D7AE853ED9673BE9A92341F0A2CCFEC  tests\TerraFX.Runtime.Tests\Scenes\BoundedStaticMeshAssetValidationTests.cs
P 8E65B71B5AC1E0EF9D1F47AA56963054D091DF8C229E74DDBB1CAE37BB59AB60  tests\TerraFX.Runtime.Tests\Scenes\RenderSpriteComponentTests.cs
P ABAEEA4D0F6BE941AF73E640B7FCE41EC753D2199654343FA4668D54EC20FC6A  tests\TerraFX.Runtime.Tests\Scenes\RendererExtractionTests.cs
P CAA6610CF55F8FFFA077745FD96BAA45FCECFED2FD051CC763901AEED5500239  tests\TerraFX.Runtime.Tests\Scenes\RenderSpriteCapacityTests.cs
P E342237B7645BE805DD924855B2239907773D31753D22B8B2261BF26C20452F4  tests\TerraFX.Runtime.Tests\Scenes\PreparedSceneAssetRetentionTests.cs
```

Validation for this document: current source/symbol ordering and retained IL metadata inspection; manifest/digest and local-reference checks; preservation of the five entry-dirty test hashes; proposal whitespace/scope checks. No source, maintained chapter, Roadmap, session plan or unrelated file is authored. No commit, push or PR is performed.
