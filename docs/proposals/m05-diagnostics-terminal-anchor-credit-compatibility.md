# Diagnostics source credit and terminal-anchor compatibility

**Tentative implementation guidance for the frozen candidate below, not an accepted contract, implementation, allocation profile, or milestone disposition.** This addresses the newly exposed terminal-anchor blocker in the [protected route](m05-diagnostics-protected-route.md) and its [reconciliation-owner correction](m05-diagnostics-reconciliation-owner-correction.md). Those proposals' other gates remain open.

## 1. Finding and disposition

Select an **immutable retained-diagnostic-source classification within the existing Entities-owned domain-credit mechanism**, retaining its full debit and existing aggregate credit limit. Distinguish that accounting-only ownership from operations that prevent terminal anchoring. Also separate the **private terminal World/binding-return handshake** from **complete domain retirement**. Changing the attachment predicate alone is insufficient.

Do not complete `WorldAdmissionDomain.Retirement` while an envelope remains outstanding. Chapter 30 explicitly requires all actual domain interests, allocations and registry storage to retire; Chapter 27 explicitly says domain retirement is not a prerequisite for local World retirement. A source credit may return after domain **closure** and World/binding retirement, but a legitimate nonzero credit cannot remain after **successful completion of the domain's Retirement task**. Treating those events as interchangeable would change the public contract.

The reported failures were not rerun for this document. The frozen source independently establishes their immediate cause:

| Frozen implementation | Consequence |
|---|---|
| `World.cs:659-667` reserves the exact diagnostic envelope with `TryReserveOperationCredit` before diagnostics construction. Restore does the same at `World.PersistenceAdmission.cs:832-840`. | Even an idle World with no diagnostic payload owns one outstanding operation-credit count. |
| `WorldAdmissionDomain.cs:1701-1732` adds both the grant's bytes to `_worldBytes` and one to `_activeOperationCreditCount`. | The count currently conflates active operations with the proposed long-lived source escrow. |
| `WorldDiagnosticFundingControl` holds that credit until its last World, baseline-service, snapshot, submission or service-attachment use returns. | This is deliberate lifetime funding, not an accidentally undisposed temporary. A zero payload balance does not end the World or snapshot's ability to produce later diagnostics. |
| `WorldAdmissionDomain.TerminalAnchor.cs:122-187` requires `_activeOperationCreditCount == 0`, besides exact lifetime/reservation ownership and the other idle predicates. | `World.TryAttachTerminalAnchorForDefaultDomain()` returns false for the otherwise eligible fresh default-domain Worlds in both named tests. |
| `WorldGuidRegistryTests.TerminalAnchorReleasesExactBindingAfterEnclosingWorldDisposeReturns` and `WorldSystemFactoryTests.WorldDisposeRetainsTerminalAnchorAfterSystemReleaseFailure` first assert successful attachment. | Both failures precede the disposal behavior those tests are intended to exercise. Removing the assertions would conceal a production ownership incompatibility. |
| Domain `PublishRetirement` is currently the only transition from terminal `Escrowed` to `Published`. Domain terminal completion additionally requires zero operation credits, a 4096-byte registrar-only charge and a completed Retirement task. | Merely exempting the envelope during attachment still makes a valid close with an escaped source use fail its caller-return handoff. |
| `ReleaseRegistry` resets operation-credit count; `PublishRetirement` or terminal completion eventually clears registrar charge. | Allowing those paths to run while an untracked envelope survives would erase release accounting or its control payer. |
| `WorldDomainReservation.IndependentUseBytes`, `WorldGuidUseControl` interests and process binding uses participate in actual World/history retirement. | They are not interchangeable with a non-emitting diagnostic account, which contains no binding or history authority. |

The candidate already contains the correction's Core local-retention observation, `DiagnosticsService.RetainsAttributionAccount`. `WorldDiagnosticsServiceOwner` holds exact-account attachments, pending-preclaim counts and a standalone baseline use. The earlier correction's statement that this seam was absent describes its older snapshot, not these frozen bytes. This proposal requires no additional Core seam or Core-to-Runtime edge.

## 2. Smallest selected credit change

Reuse `WorldDomainOperationCredit`'s owned grant and single-return machinery rather than adding another allocated carrier. Add an immutable private role, provisionally `Operation` versus `RetainedDiagnosticSource`, and a narrowly named Entities admission path for the latter. These are implementation names, not public declarations or a new quota.

Keep the existing counter as the **total outstanding credit count** and add a retained-source subset count:

```text
T = total outstanding domain credits, including raw-use operation credits
R = outstanding retained-diagnostic-source credits
A = T - R

0 <= R <= T <= existing MaximumReservations (4096)

domain charge =
    existing registry and pending-growth charge
  + existing World/operation bytes
  + each outstanding E_i exactly once
  <= exact domain.MaximumBytes
```

In particular:

1. Issue the source role before account/service construction at both fresh and restore admission. Atomically reserve the full `E_i = MaximumDiagnosticBytes_i`, increment T and R, and perform the existing domain-closed, terminal-bound, count and byte checks. Allocation failure returns that same role's grant. Do not first reserve a second envelope or release/reacquire the first.
2. The normal admission method, load bundle, handoff and raw World-use paths retain the operation role. Their outstanding work remains in A, including retained workspace after the credit wrapper is disposed. A positive generic retained-use count is not evidence that an operation is an accounting-only source.
3. The funding control accepts only the dedicated source role. Keep it inside the existing source-control path; do not make a caller-selectable exemption or let arbitrary workspace operations obtain that role. The role never changes during attachment, disposal or a failed operation.
4. On source return, subtract its exact bytes and decrement T and R together under the existing domain accounting gates. Ordinary operation return decrements T only and must not consume the retained subset. Keep checked invariants for the existing raw-use and bundle return paths.
5. Complete domain retirement still requires **T == 0**, not A == 0. Existing aggregate admission limits do not gain another 4096-credit allowance. No per-use T/R increment is needed: the source control's retained uses share one already-debited envelope.

The source claim is independent of `WorldDomainReservation.Bytes`: keep the envelope once in `GetOtherWorldOwnedStateBytes` and the World shared-state admission equation, but do not add it again to that reservation or refund it with World storage. Its post-World owner is still the exact funding control and original domain, not a WorldGuid lookup.

Keep the fixed 4096-byte registrar charge required by Chapter 04. A focused allocation regression verifies the post-change fixed domain-control allocation remains within that charge; the funding-control allocation regression also continues to verify the existing 136-byte per-World source-control charge. No new limit, guessed allowance or duplicate physical payload charge is selected here.

## 3. Terminal return without early domain retirement

### Eligibility

For the lifetime/reservation overload, replace only the operation-count interpretation: require **A == 0**. Preserve all other existing checks: one active World interest, no reservation/growth/prospective/raw-use work, exact current default-domain close owner and lifetime, zero close/return progress, no Scene construction/storage and zero reservation independent-use bytes. Preserve the process registry's independent-binding-use refusal too.

Retained source credits may belong to the current World or an already retired predecessor in the same domain. Neither has active-binding authority. Exempting only the current account would break otherwise-idle restored successors whose service baseline still retains a predecessor.

Binding the anchor continues to close new domain admissions. Retaining an already funded source use does not mint another domain grant or reopen admission. A snapshot acquired before World close can retain a submission use afterward without acquiring a World/history/process-binding use.

### Private handshake

Keep the exact registry grant, retirement permit and enclosing-call return checks. Add a private terminal-readiness transition at the successful **World final-return boundary** in `WorldAdmissionDomain.SceneStorage.FinishWorldRetirement`, after the World reservation/history actually releases:

- The domain is closed, no World/history interests or genuine operations remain, and all existing construction/return/pending/failure predicates are satisfied.
- The exact lifetime has escrowed its retirement permit, and its successful inner close has reached that boundary.
- R may still be positive. Mark only **terminal World-return readiness**, not complete domain retirement.

`TryAcceptTerminalCallerReturn` and `TryCommitTerminalAnchorCompletion` then use that readiness plus the original exact caller-return/permit protocol. They must no longer demand the entire domain Retirement task when only source credits remain. `WorldGuidRegistry.InvokeTerminalWorldClose` still performs the binding release only after `DisposeWithinTerminalInvocation` has returned. A source/service return never retires a process binding or obtains a retirement permit.

The Registry holds its admission gate through a two-step domain handoff: the domain first enters completion-pending, the Registry releases the exact binding/owner-use/key cells, and the Registry then acknowledges that release to the domain. This post-release acknowledgement prevents a concurrent final source refund from completing domain Retirement before the process binding has actually detached.

**Keep the existing registry backing and its entire current charge while retained source credits remain.** Do not introduce an early registry teardown or transfer the registrar's payer into E. This conservatively extends already admitted storage lifetime and is smaller than a second registry-teardown protocol. In the retained-credit case, terminal completion clears only the anchor's registry/grant/lifetime/failure links; it does not set `_registryCharge` to zero, reset T/R, change `_worldBytes`, or discard arrays.

Preserve the current no-retained-credit fast path, including its preowned 4096-byte terminal-publisher escrow. The private domain progress logic must independently track/elect:

- terminal World-return readiness;
- full registry release and Retirement publication once all credits have returned;
- final release of the registrar control charge after any terminal caller-return/publication ownership ends.

These are scalar transitions in the already funded domain control, not new tasks, callbacks or queues. `PublishRetirement` currently rejects terminal states other than `Escrowed` and `None`; it must support full retirement arriving after terminal readiness/return without regressing or replaying that handshake. Perform the relevant state elections under the domain gates and task publication outside them. Do not clear the control payer while either publisher still has an actual return suffix.

| Ordering | Required outcome |
|---|---|
| Source returns before World final return | Existing full-retirement/terminal path; no retained exemption is needed at completion. |
| World final return and enclosing caller return occur while R > 0 | Terminal binding releases successfully; original domain stays closed, fully charged and not retired. |
| Last source return races terminal readiness or caller-return acceptance | Exactly one full-retirement publisher; readiness/return never becomes a failure merely because the source publisher ran first or last. Keep registrar charge until both actual owners have finished. |
| Last source returns after terminal binding has released | Original closed domain refunds it directly, then releases registry backing and completes Retirement if otherwise empty. No World, schedule, registry lookup, second Dispose or poll is required. |
| World/system/attachment cleanup fails | No successful terminal-readiness transition. Existing terminal failure and quarantine owners remain; an independent successful diagnostic refund cannot turn that failure into successful World/domain retirement. |

Do not call `ReleaseRegistry` with R > 0: its present reset would destroy the outstanding-credit ledger. When full retirement is finally possible, the existing table release can run because T and R are both zero. Preserve process-registry → domain ordering where both are needed. Reconciliation must still release source uses outside service/Core gates; no new domain → service/registry callback is introduced.

### Meaning of a late return

`WorldDefaultDomainOwner.Close`/external `domain.Dispose()` closes admission; it does not invalidate existing credits. The source return path must remain legal after that close and use only its original domain, role and byte grant.

A nonzero return **after successful `domain.Retirement` completion is not an allowed new lane**: it proves retirement was published too early or the grant was returned twice. Repeated idempotent credit Dispose after a successful return is harmless and must not call the domain again. A faulted/failed retirement is not a refund either. Do not weaken the public task contract to accommodate erased counts.

## 4. Funding/lifetime sequence and route closure

The classification changes domain interpretation, not the retained-use owner selected by the preceding proposals:

```text
admit one exact E_i
  -> local construction root
  -> funding control / construction plan
  -> World's initial use
  -> independently retained snapshot, submission and service attachment uses
  -> final actual use returns the one original domain credit
```

Acquiring those uses does not charge E again. Exact service/account payload/preparation balances stay inside E; shared physical service storage retains its single service-owner charge. After terminal detachment, the strong root is `snapshot/submission/service owner -> funding control -> source credit -> original domain`; Core holds no reverse link. Reusing the visible WorldGuid cannot retarget the old account's exact session/owner generation or its debit.

1. **Before construction:** reserve the ordinary World state and E separately without overlapping their byte terms. On refusal, return every proven-unspent grant. Failed cleanup retains its actual debt and owner; it does not gain a successful terminal return.
2. **Before escape:** the frame lease carries the funding control; successful 2D/3D snapshot construction acquires its independent use before publication. Submission retains under the snapshot's disposal synchronization before any await. Snapshot disposal cannot refund an accepted submission's use.
3. **Before service retention:** the exact Runtime service owner attaches the source before Core reservation/retention. Pending identity survives until reservation disposal and completion. Reconcile all affected attachment cells after success, refusal, exception, eviction and completed Close, releasing detached uses outside service/Core gates.
4. **World close:** disable World production, close its capability, finish actual service/World cleanup, and surrender only the World use. Outstanding snapshots, submissions, local service records/preclaims and a still-owned standalone baseline retain their existing uses.
5. **Last return:** after future acquisition is excluded by the source control's last-use transition, require zero account payload/preparation balance and no outstanding source-owned service baseline. Return the original role/bytes exactly once. Preserve a discoverable return-debt owner if an invariant/accounting return fails; do not erase the last credit link and call the failure a successful refund.

| Route | Required ownership/accounting |
|---|---|
| Fresh standalone, engine-default domain | The candidate Runtime service owner owns service Close and retains its baseline funding control. Its World and E live in the same default domain. With a snapshot or foreign-service row still live, terminal binding release and domain Retirement now separate as above. |
| Fresh standalone, caller domain | Same service/baseline funding; no default close owner or terminal route is manufactured. Caller closes admission; E survives to its actual last use and delays complete retirement. |
| Hosted | E is charged to the exact host domain/World; host service fixed backing is not charged to each World. Hosted capability/World close does not close the service. An external snapshot may outlive host shutdown; the retained source credit still has its original domain return path. |
| Restore preparation and untransferred plan | Reserve a fresh source envelope/account in the exact target domain before new diagnostic construction. Old and candidate envelopes coexist. Existing load/commit/handoff credits remain genuine operations in A. Both enrolled and unenrolled plan unwind return the candidate's actual uses/grants exactly once, without closing a borrowed service or the target domain. |
| Standalone replacement | The shared standalone Runtime service owner retains its original baseline payer until actual service Close; do not migrate that charge to each new account. Its retained World-use count keeps the service alive across predecessor retirement. Transfer only the existing default-domain close authority with the existing handoff; old E and new E are independent source grants. After the operation/handoff and predecessor World interests end, their remaining accounting-only source grants do not prevent successor anchoring. |
| Hosted replacement/recovery/shutdown | Service owner is independently rooted by EngineHost, not its mutable schedule. Preserve the same owner across replacement Renderers and drain accepted old Renderer work first. Shutdown may close the domain before service Close; attachment reconciliation afterward must still refund E. Clearing schedules does not clear attachment ownership. Failed drain/cleanup retains its actual claims and reports failure. |

The frozen candidate has one relevant integration gap beyond the terminal predicate: `EngineHost.GraphicsRecovery.cs:60` still calls Core `DiagnosticsService.Write` directly. Such an unattributed write can evict attributed records without the Runtime owner's immediate reconciliation. Route that existing mutation through the already selected owner when integrating the authorized escrow batch; otherwise deterministic last-service-mutation return is not established. This is not a reason to create a callback or another owner.

The candidate fresh standalone path currently uses a borrowed Core capability with an owning Runtime service owner; restore borrows that same owner and retains another World use. Preserve the actual standalone/hosted **service-close authority**, baseline payer and last-close semantics; the Core capability flag alone does not describe this candidate's lifetime. This document does not accept the rest of the dirty implementation.

## 5. Alternatives rejected

| Alternative | Reason |
|---|---|
| Decrement `_activeOperationCreditCount` when installing the funding control | Leaves no classified return/capacity ownership; current release and retirement paths either reject the late refund or publish retirement with live debt. |
| Remove the operation-count check for all credits | Admits genuine persistence/handoff/raw-use/workspace operations into a protocol deliberately requiring quiescence. |
| Exempt generic credits once they have a retained lease | Those leases can still own operation work/history/cleanup. Retention is not the selected non-binding diagnostic-source role. |
| Add a retained count but keep the completed-Retirement requirement in terminal completion | Attachment succeeds, but a valid World close with an escaped use still throws at caller-return handoff. |
| Publish complete Retirement while R remains, or zero its bytes during registry release | Violates Chapters 04/30, loses the release owner/control payer and hides underfunding. |
| Wait for snapshots/services in World Dispose or terminal completion | Changes the nonwaiting ownership contract and can deadlock on an externally retained snapshot or foreign host service. |
| Fold E into ordinary World reservation / `IndependentUseBytes` / a second World reservation | Either refunds at World release or retains a World-interest predicate that still prevents the required independent terminal return. |
| Use a WorldGuid/history-use claim | Adds authority/interests that the account expressly lacks and still blocks process binding release; not the source-escrow obligation. |
| Return/reacquire, transfer to another domain, or refund via GC/poll/timer | Creates a funding gap/new admission after closure, violates exact-domain ownership, or lacks deterministic actual-use return. |
| Early teardown of registry backing while source credits remain | Possible only with more teardown/control-payer proof. Retaining its existing admitted charge avoids that unnecessary protocol here. |

## 6. Minimal distinguishing regression witnesses

These are implementation witnesses in existing owning NUnit areas, **not new acceptance IDs or claims that tests ran**:

| Witness | Required distinction |
|---|---|
| The two existing terminal-anchor tests named in section 1, unchanged | Otherwise-idle attachment succeeds; successful enclosing Dispose releases the exact binding; system Release failure retains it and does not invoke Release again on repeat Dispose. |
| Active-operation refusal | With the source envelope plus an ordinary credit, load/handoff credit or independent World use outstanding, attachment still refuses. Disposing an ordinary wrapper while its retained work lease remains does not make it eligible. After actual operation return, a remaining source-only credit is exempt. |
| Retained source across terminal return | Hold a funded source use, anchor and dispose its otherwise-idle default World. The old GUID can be freshly admitted after enclosing Dispose, but its original closed domain's Retirement is pending and E remains debited. Dispose the last use: exact refund and retirement occur without another domain operation. Repeat Dispose does not refund again. |
| Actual snapshot/submission ownership | Use real World → committed Step → Renderer.Extract snapshots for both dimensions, not fabricated RenderSnapshot constructors. Keep a snapshot across World close and retain/dispose a submission use in both orders, including retain/Dispose races and constructor/unwind failure. Assert the original E survives, not just a readable account reference. If the required Scene/camera producer is blocked, report that observation unexecuted; an internal funding-use test does not replace it. |
| Last host-service record | Extend `WorldDiagnosticsFundingTests.CrossServiceEvictionReleasesOnlyTheLocalSourceAttachment` with source World/domain closure **before** the foreign service's final row eviction/Close. Leave that attachment as final payer; its last unattributed eviction must immediately refund E. Exercise two services sharing one account so an idle local attachment detaches despite another service's positive balance. Copied Diagnostic snapshots remain readable and do not pin E. Internal service fixtures establish only these mechanics, not positive native publication. |
| Ordering race | Coordinate final retained-source return before, during and after terminal caller-return handoff. Require one binding release and one credit refund, no `PublishRetirement` state exception, no lost task completion, no early registry/control refund and no new acquisition after source finalization. Use test-side coordination, not production test hooks. |
| Restore and failure retention | Reuse default-domain handoff/restore owner tests; add old-plus-new envelopes, original standalone baseline survival, untransferred plan unwind, publication refusal and hosted borrowing. Verify active handoff still blocks anchoring and predecessor retirement never closes the successor domain. A real existing system-cleanup failure keeps its World/quarantine/terminal debt even if diagnostic uses independently release. |
| Aggregate accounting boundary | Derive the baseline/complete additional peak independently under the qualified profile, then exercise exact fit/one byte short, source plus ordinary-credit aggregate count exhaustion, allocation unwind and checked overflow. Source classification must not create another quota or hide registry high-water backing. |

After implementation, combine these owning Runtime filters in ordinary project tests, Debug before Release, following `docs\project\Building.md`; verify fresh nonzero selected/executed counts and the distinguishing rows. This documentation-only task runs no build, test, allocation probe or native experiment.

## 7. Remaining gates

- **Numeric eligibility:** the focused Runtime regressions confirm the source-control cohort remains 136 bytes and the fixed domain control remains within its 4096-byte registrar charge. Complete service-attachment backing, constructor/teardown overlap and retained registry high water on the selected runtime/target remain open.
- **Protected diagnostics:** the preceding proposals' P1/P2 disposition, real mandatory Critical producer shapes, durable fault/counter claims and old/new overlap, shared-clock lifetime payer, complete independent-result ownership and service-only producer preconstruction remain open.
- **Owner closure:** route every retaining/refunding mutation through the selected reconciliation owner, including the recovery bypass above; verify failed close/drain and final-return debt retention. This proposal is not acceptance of current source-use tokens or the full dirty call graph.
- **Renderer/producer evidence:** complete 32-MiB preparation/recording and 16-MiB completion ledgers remain NO-GO. Scene-dependent public snapshot/positive diagnostic observations require the actual qualified Scene preparation/camera/extraction producer; native skinned-normal paths additionally require the real animated-palette producer where applicable.
- **Qualification:** Chapter 35's M05 capability/observation sequencing and section 4 remain controlling. Managed mechanics do not establish real D3D12/Vulkan submission/readback/drain, NativeAOT, public-only consumers or integrated milestone acceptance. Required real driver/OS loss and submitted-work/presentation-timeout observations remain with their current M09 owners and real stimuli; no synthetic substitute is introduced.

## 8. Frozen inputs and verification

HEAD at refreeze: **`476c334362616c0732a35db436277e06a4ea6338`**. Primary evidence-byte freeze: **2026-11-19T22:33:52Z**; quarantine-owner inputs were added immediately afterward. All E hashes remained unchanged across a concurrent Core commit; the preservation-only Core test hash/status and Core source status below reflect that commit. These are SHA-256 of **worktree file bytes**, not Git blobs. The two prior proposals remain tentative inputs, not competing normative contracts.

The manifest contains **80 inputs: 61 evidence/convention inputs (E), plus 19 preservation-only inputs (P)**. It includes **all 42 pre-existing dirty files** at refreeze. P rows preserve the surrounding work but are excluded from this proposal's technical disposition. C means clean, M modified and ?? untracked at refreeze. No schema or protected fixture is changed or used as a new oracle.

These hashes identify the pre-implementation source inputs used to apply this route; implementation changes since then intentionally make some entries differ. Recheck every hash and the dirty-path set before any further disposition. A changed E input requires rereading/refreezing the affected finding; an unrelated P change is not acceptance evidence. The proposal's own final SHA-256 is reported separately to avoid a self-referential hash. This proposal does not amend maintained design contracts.

| Scope | Status | Input | SHA-256 |
|---|---|---|---|
| E | C | `.github\copilot-instructions.md` | `12439df4bb5e46021891b234bec1104abdca57c11fdf3f610c8f32c5daf3001e` |
| E | C | `.github\workflows\ci.yml` | `03efea672cd52e9ef053e9ce0285e7ca02c6ce6fb0d21268ade4255ed5a39d58` |
| E | C | `Directory.Build.props` | `770ee4b2a422861a6f23f0d39dd0eca361a81cb87e64ebac4e12170f71231d7a` |
| E | C | `Directory.Build.targets` | `aeec1d08e8037470239035812351c4045124607d67a354d482112f4d642d4105` |
| E | C | `docs\design\04-capacity-and-workload-profiles.md` | `60047dd1eacf26196b0bf23f16798e2aa265fd26cd36b9a0b4df1b79135730d7` |
| E | C | `docs\design\12-entities-components-and-queries.md` | `174b2465746da79cef560c8506216ef4f99f9d1aabac184f872c9e6cb0b94e0b` |
| E | C | `docs\design\27-state-serialization-replay-and-recovery.md` | `b0bfa5771f1029bdfb21a0e01556467d3169953f71cbfdf18a06381a11cee461` |
| E | C | `docs\design\28-diagnostics-performance-and-validation.md` | `0e3c895bee5f76118ce87d208170cb6e02e9a19de3f286b7e243b28165370d14` |
| E | C | `docs\design\29-project-setup-build-and-deployment.md` | `49598161138febbf889f2a5176e3504af8d8bb8481d2157fbcfbbd94d2b46604` |
| E | C | `docs\design\30-public-api-review-surface.md` | `d4f622d69ed58ab96118c36fbbf4e2d31e062a3cbf3ce3ba1d64309f662fe4ed` |
| E | C | `docs\design\35-implementation-roadmap.md` | `24eec6e92b7ad904d552f290ef748ffc8fce13b6b94a89ebd9f39487d4584d87` |
| E | C | `docs\project\Building.md` | `f043a352870efb6c7d4a61998225e2508a8f4370a57e784483a41819d452075f` |
| E | C | `docs\project\Roadmap.md` | `fd84bbdf11fef04c15fc09489dd663864664efadebf1e294748c83e8a73b1bf8` |
| E | C | `docs\proposals\m05-diagnostics-protected-route.md` | `dc28f94efce9dc4d4024b3e452296867e6095e7d9f20264dfc9698218d94d321` |
| E | C | `docs\proposals\m05-diagnostics-reconciliation-owner-correction.md` | `b026d3de0d55b6f5a7923110aff4fe0c987ebe08b13a75c79515f4eabcd95baf` |
| E | C | `global.json` | `1bce01871321c2df82e887da428ce5c121a6c6541fe65017fe3d249be47e190c` |
| E | C | `sources\TerraFX.Core\DiagnosticsService.cs` | `ef940c1d1ea11dee91519ef594f57efa61e37197ca89e5132cedb6cd3849fd52` |
| E | C | `sources\TerraFX.Core\WorldDiagnosticAttributionAccount.cs` | `75b41f64fcb6af40bfdb7d0c1203596dedd3f8826ea8aa006af9dabe81e25e5b` |
| E | C | `sources\TerraFX.Core\WorldDiagnosticsCapability.cs` | `7feb090087103d651e7674ed75d90d103bb7aa2ea039e273e7b41b67d2a2806d` |
| P | M | `sources\TerraFX.Runtime\Entities\World.Controls.cs` | `748bc950148dd210434c6de2db4bafba949769b34a4bec781a78b1b08ce3d28c` |
| E | M | `sources\TerraFX.Runtime\Entities\World.CpuExtraction.cs` | `37a8357bff0ea02192754366526a17f55bdf7bcacdd170b95695d56e963336d4` |
| E | M | `sources\TerraFX.Runtime\Entities\World.cs` | `451c8254f65d634caca5fd8a7e5374fa0942ba287fbef96fe920247d40632103` |
| E | ?? | `sources\TerraFX.Runtime\Entities\World.DiagnosticsFunding.cs` | `23d4bdf77e5a147ecfd3f284e2ee005135f77dff0d483dc6177dc28e16f5ade1` |
| E | M | `sources\TerraFX.Runtime\Entities\World.HostedConstruction.cs` | `8969d87abadf2aa00fb1024d413543da72ce815e0ff472f4e7402fc7e92b70b8` |
| P | M | `sources\TerraFX.Runtime\Entities\World.OwnerControlAdmission.cs` | `1f110f0d351aca37284a5e86695cf29101a4809efed9dff797f0ddbb3dc8c3b8` |
| P | M | `sources\TerraFX.Runtime\Entities\World.OwnerControlApply.cs` | `e4d3178adeeb5a1bf5551a6b3f83ebfda176b1f5e25033a2e4f78f7969494903` |
| P | M | `sources\TerraFX.Runtime\Entities\World.OwnerControlRequests.cs` | `b2668e55b7145acc7c3a6f404a94b566574175bd3bad8cf55d618de79d75f3f9` |
| E | M | `sources\TerraFX.Runtime\Entities\World.PersistenceAdmission.cs` | `dd390452433ff8dee88652d8b65cf6fa0eddd0f60bd2e31ca49f22e45d2a4cdb` |
| P | M | `sources\TerraFX.Runtime\Entities\World.Publications.cs` | `6b995f79fd5d89b8288a5334d70a051062f8c4ccde1e238f0e49df70822695c3` |
| E | C | `sources\TerraFX.Runtime\Entities\WorldAdmissionDomain.cs` | `0afbc247e5ce649edbd6f26a01f67bd291d7f23cd1529dd5cd735e89dffd70f7` |
| E | C | `sources\TerraFX.Runtime\Entities\WorldAdmissionDomain.SceneStorage.cs` | `c8595af75dcdb5b7128ab6c180a0c07c53e5d8109bbe481f96b4da3634375608` |
| E | C | `sources\TerraFX.Runtime\Entities\WorldAdmissionDomain.TerminalAnchor.cs` | `63092fd413cf296e188116a3910c1e3031ae42d6c86e8bbb80820b8a62e7970b` |
| E | M | `sources\TerraFX.Runtime\Entities\WorldConstructionPlan.cs` | `a2e7d572f5d8bbdbc0e4e40e004479cf9895283e2f0be6947bd6cf8af755197c` |
| E | C | `sources\TerraFX.Runtime\Entities\WorldDefaultDomainHandoff.cs` | `f2a192c7da7115f8bf8d585a4bae6721554e0d89e616b40d90ed2c4cfa10648d` |
| E | C | `sources\TerraFX.Runtime\Entities\WorldDefaultDomainOwner.cs` | `bcf8f830fbb29e652d480608a3bdfc197d89d7e58299622941e87685b4c2aad4` |
| E | ?? | `sources\TerraFX.Runtime\Entities\WorldDiagnosticFundingControl.cs` | `c4cb6284cc8c1f5187d4052e79b91333c8aec23db5ce981cc843971ab4e40534` |
| E | ?? | `sources\TerraFX.Runtime\Entities\WorldDiagnosticFundingUse.cs` | `0be478cc58c22019f5e337eba63869bc87765bc975b50b345621053d812a6df9` |
| E | ?? | `sources\TerraFX.Runtime\Entities\WorldDiagnosticsServiceOwner.cs` | `cbb5bdf2b4f769609d7ff091b14710c9e6520af81b172d7cd8a16d99e9490fb4` |
| E | M | `sources\TerraFX.Runtime\Entities\WorldDomainOperationCredit.cs` | `6749696d780d54cda79415284aac80d79b370b49932b548b6db4daee3b1c4407` |
| E | C | `sources\TerraFX.Runtime\Entities\WorldDomainOperationCreditBundle.cs` | `9673f149eb8a91c2bc990b382b93e68c659736a6c14f6938a6842edd4ab9ca00` |
| E | C | `sources\TerraFX.Runtime\Entities\WorldDomainReservation.cs` | `d6cdd6105850b8076d40891b0ed1f583d9fcde6ee190444a7d570879964403dc` |
| E | M | `sources\TerraFX.Runtime\Entities\WorldFrameReadLease.cs` | `96422c279cc5966e358eb0af9c36eeddb0e37276d4aff24f8cef2b93be0c0f41` |
| E | C | `sources\TerraFX.Runtime\Entities\WorldGuidRegistry.TerminalAnchor.cs` | `ef6e0389a5c303b3ee7f0db37d66a70ec5faeb921670cbcb0a65bea2d91843d2` |
| E | C | `sources\TerraFX.Runtime\Entities\WorldGuidRetirementPermit.cs` | `81f87f907579884295e3b441f21b5ea4fe6b2509790735b1577f2b27b16d2bd6` |
| E | C | `sources\TerraFX.Runtime\Entities\WorldLifetimeControl.cs` | `5a304aaf8b2353e28f6f743d5587e82f12e979dcf0dfebf55103e075ccaf54f2` |
| E | C | `sources\TerraFX.Runtime\Entities\WorldSystemInstanceRegistry.cs` | `70dcb73d187aa6fc27b099f8c27b6716d3d63ef6c4022662017d34c25f5a4e20` |
| E | C | `sources\TerraFX.Runtime\Entities\WorldSystemQuarantineReservation.cs` | `f864c7c0e250d4f6da38b587c964ce0366e54fffe22509120b84726e4354361f` |
| E | M | `sources\TerraFX.Runtime\Hosting\EngineHost.cs` | `e21df760d8fc43fc0aa4b10aa2c8cd3618caf1e876f9924e8390884419279a70` |
| E | M | `sources\TerraFX.Runtime\Hosting\EngineHost.GraphicsRecovery.cs` | `8411aa72230b15e1d1daf94bd10d0f7f665ddf86f12dd2c6f68f43fb93df3c81` |
| P | M | `sources\TerraFX.Runtime\Hosting\PlatformInputRoutingOwner.cs` | `90ae96face586ce17672f4be2434bb3c2a13db3696f0402898903fc3d04a2f61` |
| E | M | `sources\TerraFX.Runtime\Rendering\Renderer.CpuExtractionSupport.cs` | `8b981bb23dc134c1f2ec208778037a099d3ddd26d0cfcaeb49162dec94cc72ca` |
| E | M | `sources\TerraFX.Runtime\Rendering\Renderer.Extract.cs` | `026a50597c4a6ddf50cdbeeaf2e761392f8933955b454d92a73416e3855d8124` |
| E | M | `sources\TerraFX.Runtime\Rendering\Renderer.SkinnedNormalDiagnosticPipeline.cs` | `23e9e1ffbe5de83a262d0297517696e8ef66499caebc6080cc1d105bb42545a2` |
| E | M | `sources\TerraFX.Runtime\Rendering\Renderer.Submit.cs` | `ca57beb6b04356885a2e63275c1edc70edfe827e52411ad522191f6990f362bf` |
| E | M | `sources\TerraFX.Runtime\Rendering\RenderSnapshot.cs` | `0172b307df892ee13ef60f7cd210517cb95e4428017247627db0ea851fead028` |
| E | M | `sources\TerraFX.Runtime\Rendering\RenderSnapshot2D.cs` | `bc6d2cf06f960163ef416208d2b1ac0eff3297527a8ca80765026a8c28762da8` |
| E | M | `sources\TerraFX.Runtime\Rendering\RenderSnapshot3D.cs` | `37944f5bdd213af05974e09abad7d75cb77db858d367030e5f5b9d361d2554d2` |
| E | C | `sources\TerraFX.Runtime\TerraFX.Runtime.csproj` | `2f1c81e54f0fbf12d01e34d0d840885f5eb0e94a8b8890865ef721b8c6a3c65d` |
| P | C | `tests\TerraFX.Core.Tests\DiagnosticsServiceTests.cs` | `1e50d2ea4cefb47c6e2eec5517f291a958ffbf43beebf82192ee5a46091cc529` |
| E | ?? | `tests\TerraFX.Runtime.Tests\DiagnosticsServiceTestHelper.cs` | `732204b4f323d7319fb9d82d78af30a28e2807a668106882138cf1694b9a1b7f` |
| E | C | `tests\TerraFX.Runtime.Tests\Entities\WorldAdmissionDomainTests.cs` | `9f884c2185cb3372914457dc72ec760666ede1bd2388a387a08b64f3e17b894c` |
| P | M | `tests\TerraFX.Runtime.Tests\Entities\WorldAttachmentRecordBindingTests.cs` | `4f93909760ba073e8a1e0575477ad5f540ee85f1e861b376f6cde0ba1092cfdf` |
| P | M | `tests\TerraFX.Runtime.Tests\Entities\WorldAttachmentTests.cs` | `a1f73845302cd857dbd082d922b1547c9734f68b23698b4a5a968e36099bf971` |
| P | M | `tests\TerraFX.Runtime.Tests\Entities\WorldCreationTests.cs` | `a8ac951d42e6d8bff6f115bae15d66f6f337154401ed24c37639de6236851c00` |
| E | C | `tests\TerraFX.Runtime.Tests\Entities\WorldDefaultDomainOwnershipTests.cs` | `d044b061eeeb2a1b03b4f73ed8d640e4b6138ece9993f6446d9686d0e72b74eb` |
| E | ?? | `tests\TerraFX.Runtime.Tests\Entities\WorldDiagnosticsFundingTests.cs` | `3f077a0db5b3b03dc8a74ad498adb637777b0eb17b8abc50d7d33a3980a2b39f` |
| E | C | `tests\TerraFX.Runtime.Tests\Entities\WorldGuidRegistryTests.cs` | `37a57bba342b8454c399ce60cd9a02aa9cf29009d9a97abff2a0c562417a2105` |
| E | M | `tests\TerraFX.Runtime.Tests\Entities\WorldHostedConstructionTests.cs` | `220a244daf8642230234171d8b4f73a5e86983f535f02c73f5cf1b152e05d0ec` |
| E | C | `tests\TerraFX.Runtime.Tests\Entities\WorldLifetimeControlQuarantineTests.cs` | `0605bf47f60a2e22bdc951a2bcce938bc2594804603f4a5df7127d58af1a6329` |
| P | M | `tests\TerraFX.Runtime.Tests\Entities\WorldPersistenceIdentityHistoryReaderTests.cs` | `1997fe07be739492a97305b9f51bd6aae105e77b973c8c33b2da0eea993d5bab` |
| P | M | `tests\TerraFX.Runtime.Tests\Entities\WorldPersistenceRegistrationCaptureTests.cs` | `d73d7a9d13b7074b2b2585dfde8960d4432eca1496d6469a0f0bd7a2f7ce698f` |
| E | C | `tests\TerraFX.Runtime.Tests\Entities\WorldSystemFactoryTests.cs` | `87dafcdb1c57f622625bc2538c72a8a7d2ee54090d8c11a79dd75c0a1ffaa2b6` |
| P | M | `tests\TerraFX.Runtime.Tests\Hosting\EngineHostOptionsTests.ShaderPipeline.cs` | `7b663e0e6fb6c3d116a0a4946cd5839abebc50b63fd5d6d720fe7a60e2a87fec` |
| P | M | `tests\TerraFX.Runtime.Tests\Hosting\PlatformInputRoutingOwnerTests.cs` | `3a170fdb764ad674e0467fa4ec9410d30aae03c6955171ab9846ef287b531998` |
| P | M | `tests\TerraFX.Runtime.Tests\Hosting\VulkanBackendSurfaceTests.cs` | `468c17c966e38af3317ca3dcf5b61697939d826bdbcf689b4e7260b43f67dc33` |
| P | M | `tests\TerraFX.Runtime.Tests\Scenes\PreparedSceneAssetRetentionTests.cs` | `e342237b7645be805dd924855b2239907773d31753d22b8b2261bf26c20452f4` |
| E | M | `tests\TerraFX.Runtime.Tests\Scenes\RendererExtractionTests.cs` | `abaeea4d0f6be941af73e640b7fce41ec753d2199654343fa4668d54ec20fc6a` |
| P | M | `tests\TerraFX.Runtime.Tests\Scenes\RenderSpriteCapacityTests.cs` | `caa6610cf55f8fffa077745fd96baa45fcecfed2fd051cc763901aeed5500239` |
| P | M | `tests\TerraFX.Runtime.Tests\Scenes\RenderSpriteComponentTests.cs` | `8e65b71b5ac1e0ef9d1f47aa56963054d091df8c229e74ddbb1cae37bb59ab60` |
| P | M | `tests\TerraFX.Runtime.Tests\Scenes\SceneAssetStoreTests.EngineHost.cs` | `4c0841a40295a891b68974fddd8078dfd0181c31b35ab0bcb0b5114fe0fd1039` |
