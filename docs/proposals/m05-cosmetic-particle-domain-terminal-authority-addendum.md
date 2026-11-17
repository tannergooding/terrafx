# M05 domain-terminal invocation authority

## Disposition

**No current-contract implementation candidate is closed, including a meaningful compile-only invoker.** Ordinary synchronous return can establish a child body's return without a Task observer. The missing capability is an **issuer-backed, strongly retained domain-terminal invocation obligation which survives both the last World/use interest and the original World lifetime's retirement authority**. No inspected existing actor supplies that combined capability.

This narrows the [synchronous-release candidate](m05-cosmetic-particle-synchronous-release-invocation-addendum.md), rather than treating its proposed methods/receipts as implemented permissions. In particular, its “five existing domain publication sites” premise is incorrect: the current source has **seven**. Do not implement that five-site projection or add an unreachable wrapper as evidence of ownership closure.

“Nonblocking” here means no wait/join on a producer or retirement Task, no polling, new wait queue or observer. Existing admission gates can contend; this is not a claim that `ReentrantGate` is lock-free. Physical self-return remains distinct from a proved harmless suffix, as explained in the [owner-aware async-handback assessment](m03b-scene-preparation-owner-aware-async-handback.md), sections 4–7.

## 1. Exact current retirement paths

All source paths below are under `sources\TerraFX.Runtime\Entities`, except the named Core gate.

### Clean private-default-domain World close

```text
World.Dispose
  BeginRetirement -> WorldGuidRegistry.TryRevokeForRetirement
  retire fresh lineage; release ordinary owned storage
  WorldDomainReservation.Dispose
    WorldAdmissionDomain.ReleaseWorld
      debit World bytes; release World interest; MarkReleased clears reservation's domain
  WorldLifetimeControl.CompleteRetirement
    WorldGuidRegistry.TryRetire -> consume retirement permit -> TryReleaseFresh
    state Retired; clear lifetime reservation/lineage
  WorldLifetimeControl.CloseDefaultDomain
    clear default-owner field
    WorldDefaultDomainOwner.Close: Active -> Closed before calling domain.Dispose
      WorldAdmissionDomain.Dispose
        mark domain closed; last-interest test; ReleaseRegistry
        _retirement.TrySetResult()
        exit outer growthScope
  World.Dispose finally: restore lease, quarantine capacities, structural-operation flag
```

In the ordinary no-independent-use route, the domain has not yet been closed when `ReleaseWorld` runs; publication occurs in the later `domain.Dispose`. If it was already closed, `ReleaseWorld` itself can publish. Moving either call alone therefore does not remove the frontier.

`ReleaseRegistry` at `WorldAdmissionDomain.cs:3050` clears backing/registrar state and sets `_registryCharge = 0` **before** the publisher. `CompleteRetirement` at `WorldLifetimeControl.cs:175` has already consumed/ended the normal lifetime authority in this clean route. The remaining World `finally` at `World.cs:1470` still accesses World fields after domain publication. None of those ordinary returns transfers a funded tail.

### Last independent use after World close

`WorldGuidUseControl.Release` at line 277 first calls **process** `WorldGuidRegistry.TryReleaseIndependentUse`:

1. Decrement the binding's independent-use count; the last revoked/pending use becomes `UseRoleIndependentRetiring`.
2. Call `lifetime.CompleteDeferredRetirement`; consume the process retirement permit, retire binding/owner roles, set lifetime Retired and clear its reservation/lineage.
3. Resume the process registry and free the retiring independent U.
4. Call **domain** `TryReleaseIndependentUse`; release history/operation interests and operation bytes, unlink the control and retire the last membership/backing.
5. Clear domain interests; leave the domain gates; call `_retirement.TrySetResult`; return.
6. Resume `WorldGuidUseControl.Release`, clear `RetainedOwner`/`Next`, write Released, then return to the carrier/caller.

The process registry does not publish the domain Task. Its binding cell contains scalar identity/count/state fields, not a retaining reference to the lifetime/domain. Its independent U does retain the actual control, but that U is already freed before domain publication. Keeping a numeric O alive alone would not create the missing strong root or fund a domain-owned control.

### Complete publication membership

LSP incoming calls to `ReleaseRegistry` and exact `_retirement.TrySetResult` sites agree on this seven-member set:

| Domain entry / publication line | Actual ingress | Work still outside/beyond publication |
|---|---|---|
| `Dispose` / 264 | Default-domain owner; direct domain owner; failed construction | Outer growth-gate disposal; default owner/lifetime/World caller returns |
| `ReleaseHistoryInterest` / 388 | `WorldHistoryInterest.Dispose` | Outer growth-gate disposal and history carrier return |
| `ReleaseOperationCredit` / 419 | `WorldDomainOperationCredit.ReleaseCharge`, from owner disposal or last retained-use release | Outer growth-gate disposal; `_isChargeReleased = true` and caller lock exit |
| `ReleaseWorld` / 479 | Reservation disposal; failed reservation-object allocation rollback | Outer growth-gate disposal and reservation/construction caller suffix |
| `RollbackUnissuedWorldGuidUse` / 557 | `AcquireUseCore` failed acquisition; may delegate to the next independent-use route when interests were issued | Rollback return, original/cleanup rethrow and acquisition caller/journal suffix |
| `RollbackWorldGuidUseRaw` / 612 | `AcquireUseCore` failed construction/link | Same enclosing acquisition/unwind obligation |
| `TryReleaseIndependentUse` / 1160 | Use release or linked-use rollback | Domain Boolean return, control's final fields/state and enclosing use caller |

The last three exit their nested growth/membership scopes before publication; the first four hold an outer `using var growthScope` across it. `sources\TerraFX.Core\ReentrantGate.cs` shows scope disposal writes owner-embedded gate state. Gate exit cannot remain after that owner's final funding debit and be called a harmless suffix.

`WorldDomainOperationCredit` is a concrete additional caller witness: the domain debit/publication returns **before** the wrapper sets `_isChargeReleased` and exits its `Lock`. A domain-only publisher-return flag would not join those instructions.

The selected storage-only lane need not execute every generic ingress. However, a common domain implementation must preserve each legacy route or establish its precise selected-route refusal/unreachability; it cannot silently assume only World disposal publishes.

## 2. Why existing retained ownership is insufficient

| Existing mechanism | Exact limitation |
|---|---|
| Original `WorldDomainReservation` | `ReleaseWorld` ends both bytes and its I cell; `MarkReleased` clears its issuer link. Keeping the interest alive prevents last-interest publication. It exposes no independent terminal-release loan or returned-body commit. |
| Registrar's 4096-byte control | An actual existing domain accounting row, but current `ReleaseRegistry` ends it before publication. Retaining it longer could preserve a real charge; neither its complete protected coverage nor a post-failure retention/end protocol follows from the number. |
| `WorldDefaultDomainOwner.Close` | A close-right carrier, not a release-invocation grant; it changes state to Closed before calling the domain. Retrying the same close cannot witness or repair the previous publisher's return. |
| World lifetime quarantine | `QuarantineAfterCleanupFailure` requires StateRevoked, nonnull reservation and matching lineage/reservation references, plus an available process permit. Clean `CompleteRetirement` clears those references and consumes that authority before the publication above. |
| Pre-reserved lifetime quarantine slot | `QuarantineLifetimeControl` accepts an already quarantined lifetime. Slot capacity is neither a new domain byte grant nor permission to retain a Retired lifetime as a domain-terminal owner. At zero systems there is no separate system-domain-retention slot. |
| Process O/P/U backing | Separate process accounting; it cannot pay domain bytes. `BindingCell` is not a graph anchor, and the actual independent-U root ends earlier in the traced route. |
| `Task` status / `TrySetResult` Boolean | Outcome information, not publisher/caller return. A false result can also be concurrent with an earlier setter's suffix. No Task observer is added. |

Precisely, `QuarantineAfterCleanupFailure` checks matching references; it does **not** independently prove the reservation's World interest is still live. Delaying `CompleteRetirement` can leave its legacy validation available, but cannot turn an already released reservation into publisher funding. Nor does an arbitrary extra reference fund the referenced control.

The bootstrap case rules out relying solely on a constructed World lifetime. In `World.AdmitWorld`, the domain and default owner are created before `TryReserveWorld`, the lifetime and the quarantine reservation. Failure can reach direct `admissionDomain.Dispose` at line 791 without a lifetime/slot to lend. A valid selected terminal capability must cover that construction/unwind route as well as late close.

## 3. Exact missing lower-owner disposition

The next owner decision is not “allow another helper.” It is:

> Permit the actual Entities domain registrar to carry a single terminal invocation obligation, with an original issuer-backed control/protected claim and a real prepaid strong failure anchor, from before selected construction can fail through zero World/use interests, actual publisher/enclosing-body return, and final accounting. Define its terminal end-or-retain operation independently of the already-consumed World retirement permit.

That disposition must close these inseparable effects:

- **Issuer/amount:** prefer retaining the original actual registrar control provision. Derive its complete retained receiver, cold/failure/runtime/native peak; if it does not fit its named coverage, preadmit separately owned control/protected backing under Chapter 04's existing B and aggregate limits. No guessed “spare 4096,” new entitlement, extra I/U or copied World grant.
- **Anchor:** select the exact existing Entities owner/root slot allowed to hold the actual terminal graph, with capacity reserved before the relevant allocation/fallible release. Existing process binding cells and World-lifetime quarantine do not currently provide that permission. Adding a domain reference/role to a process cell, broadening lifetime quarantine to domain-terminal ownership, or introducing a registrar anchor would each be a real owner/layout change requiring its own defined lifecycle—not an implementation detail already selected here.
- **Issuer commit:** preserve backing retirement, logical interest retirement, public readiness and terminal-control release as separate facts. The final zero-interest publisher needs unique ownership across the seven entries. Accepted body return must precede debit of that body's claim, or an exact funded handoff must cover its remaining fields/locks/caller.
- **Failure/end:** retain actual remaining claims and a valid strong root on every prepublication, publication and later accounting failure; preserve confirmed subreleases and exception/token identity. Already-terminal readiness is not rolled back. No retry through a spent permit, recreated U, new global orphan list or clean-success fallback is allowed.

These are not new public API or acceptance IDs. The existing Chapters 04/29 allow prepaid controls and actual end/transfer in principle; they do not choose this missing root permission, terminal grant or commit. Until that disposition is closed, a new private token/state/receipt would merely name nonexistent authority.

The [owner-aware assessment](m03b-scene-preparation-owner-aware-async-handback.md) already supplies the ordinary-return/harmless-suffix distinction. No blanket CLR-hook impossibility is claimed. A real funded invoker could work; a final refund after its **own physical return** still needs an independently funded actual outer handback, unless the remaining suffix is proved harmless.

## 4. First valid code batch and stops

**There is no dependency-ready live source batch now.** The first valid implementation batch, after selecting the disposition above, is **the lower Entities domain-terminal issuer/anchor**, not particle storage registration or another World invoker wrapper:

| Owning files | One coherent batch |
|---|---|
| `WorldAdmissionDomain.cs` | Separate actual backing retirement from retained terminal control; implement the selected exact claim/phase/unique-publisher/end protocol for the seven ingresses. Keep its profile unavailable. |
| The exact anchor owner selected in section 3 | Install the real preclaimed retaining edge, correct failure authority and final release. Do not implement an unchosen alternative in `WorldGuidRegistry` or `WorldSystemInstanceRegistry`. |
| `WorldDomainReservation.cs`, `WorldHistoryInterest.cs`, `WorldDomainOperationCredit.cs` | Bind real original-issuer facts and ordinary caller-return coverage where needed; preserve legacy disposal, lock, failure and readiness behavior. |
| `WorldGuidUseControl.cs`, `WorldLifetimeControl.cs`, cosmetic partial and retirement permit/registry, only where the selected protocol requires them | Preserve exact partial process/domain release and authority. Do not reset a consumed permit or label current use/control suffixes joined. |

Its first artifact must use exact production declarations/callers in the already selected isolated candidate workflow, with no World hooks or operational execution. Price every changed common layout before live installation. Compilation establishes only type/call-chain coherence; constructor-free measurements establish only their exact layout rows. Do not create another shadow owner or generic receipt/provenance framework.

Only after that issuer/anchor exists can the separately measured standing graph be integrated with original World preclaim, full construction/unwind/close provision and standing retention. The 10,592 World-associated sizing inputs and separate +8 registration increment remain unfunded. CPU storage/layout work, actual return, allocation/failure coverage, public-consumer observations and native/AOT qualification remain distinct.

Stop if any required anchor/claim can be created only after the claim it must protect ends; an unsupported generic publisher is reached; any field/gate/caller suffix remains after its funding ends; the profile requires an unbounded observer or polling/join; or admission would raise limits or fabricate an owner. No target execution is needed or authorized to rediscover those source-derived blockers.

## Reviewed inputs

These working-source hashes, including dirty/untracked declarations, identify the bounded trace; they are not return qualification. `Entities` paths are relative to `sources\TerraFX.Runtime`.

| Input | SHA-256 |
|---|---|
| `Entities\WorldAdmissionDomain.cs` | `2CE1C0B90157ADE6F82605409AB599F7415B7D8AA0A63A4BF4327263610248D9` |
| `Entities\World.cs` | `692F721BEAD29E5EC258BF7B1844EC785E2D1548E9F53DFA9BDB57A22A01F09F` |
| `Entities\WorldDomainReservation.cs` | `EA012B9B40ACEDAA820885EAFE27DCD11DF55F842B68F2849785DAB756E8E3BC` |
| `Entities\WorldDefaultDomainOwner.cs` | `BCF8F830FBB29E652D480608A3BDFC197D89D7E58299622941E87685B4C2AAD4` |
| `Entities\WorldLifetimeControl.cs` | `7F47BE8C4943CE6FC2610FD98D3D2B7470B8F83EA00EC5D9C6C4A3F5C46A9BEB` |
| `Entities\WorldLifetimeControl.CosmeticCredits.cs` | `C6F41DE02F0896016FA55845A61661BCD1A4DFE085BF68302767FE9C94DA3228` |
| `Entities\WorldGuidRegistry.cs` | `9A7339A8ECFD56C60F7C2A5C9251BCBBA2DA73A7A26520AFCDBE533B24BD39E9` |
| `Entities\WorldGuidUseControl.cs` | `F3345FCEB06BEEA0DA4B89E2CF3106B1F60717A71C810AD3C34477762C6964F9` |
| `Entities\WorldGuidRetirementPermit.cs` | `1849EAE697239482076F7EC3A1EA02DD6B3348094DAD6CB63668BF57FDB20529` |
| `Entities\WorldDomainOperationCredit.cs` | `1F005480FE39ACE70B469848F943A5A72433C00A1F9E099075DCCB604B2A0002` |
| `Entities\WorldHistoryInterest.cs` | `1094A7B16FA8D78FB0A8095CB3E3C77AD94417DBB85B34CE7E46AB58BD67BAEA` |
| `Entities\WorldSystemInstanceRegistry.cs` | `70DCB73D187AA6FC27B099F8C27B6716D3D63EF6C4022662017D34C25F5A4E20` |
| `Entities\WorldSystemQuarantineReservation.cs` | `F864C7C0E250D4F6DA38B587C964CE0366E54FFFE22509120B84726E4354361F` |
| `Entities\WorldCosmeticCreditOwner.cs` | `7EEFB227D9654552997BF3515E29CA32E3791F0A155FBF1AA37C274508E2CEA6` |
| `sources\TerraFX.Core\ReentrantGate.cs` | `8F0EF274C39C3E07E691951011AC86C574311912D7F78348EB8D6780555F0C71` |
| `docs\proposals\m05-cosmetic-particle-synchronous-release-invocation-addendum.md` | `1E97D990D2AFF1E2A92061919C4E105EB00E7CFC09C05FAC82F6B3979D0D0386` |
| `docs\proposals\m05-cosmetic-particle-terminal-return-ledger-addendum.md` | `0A317BE624225E5A6873F82D491FDD93B2C01A91DCBAF1CF96153ADFE4659DB8` |
| `docs\proposals\m03b-scene-preparation-owner-aware-async-handback.md` | `0A9AE81EB42D886168D9E05970C7395B33C9EF70FCCADA9ED311CF2E1DC7AD89` |
| `docs\design\04-capacity-and-workload-profiles.md` | `A4D86F3FA373A64548435C53582BF5916E29D77F34650F9D161CCEC90E6F0245` |
| `docs\design\29-project-setup-build-and-deployment.md` | `3B55A013EEC327ECD648ACBD5B53717DD218AD288E8047AC21277609EFA2F47B` |
| `docs\design\35-implementation-roadmap.md` | `24EEC6E92B7AD904D552F290EF748FFC8FCE13B6B94A89EBD9F39487D4584D87` |
| `docs\project\Roadmap.md` | `649182860D65DC902A406BCAC482AA1D286465BAFB0B34143452C8B09E51EBDE` |

Only this addendum is added. No source/tests or maintained chapters/roadmap change; no builds, target workloads, World/Scene/pool or publisher operations run. `WorldCosmeticCreditOwner.HasQualifiedStandingOwner` remains false.
