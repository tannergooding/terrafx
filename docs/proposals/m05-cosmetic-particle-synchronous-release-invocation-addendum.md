# M05 synchronous release invocation candidate

## Decision: ordinary return is usable, self-return is not

**Tentative private lower-owner mechanism; not implemented or qualified.**

The current caller **as written cannot** cover the [terminal ledger's](m05-cosmetic-particle-terminal-return-ledger-addendum.md) two uncovered tails: it has neither a surviving invocation grant nor an owner-aware terminal debit. Wrapping the same debit in another method does not create one.

A concrete synchronous mechanism is nevertheless possible **under the existing owner-aware rule**: an actual funded enclosing invoker observes its child body and publisher return, then performs final accounting with a separately proved harmless suffix. It needs no Task observer, async builder, background releaser or caller acknowledgment.

There is a necessary precision about “caller return”:

| Required boundary | Disposition |
|---|---|
| An ordinary release/publication body actually returns/unwinds to its funded enclosing caller | Direct synchronous call ownership can establish it. This proposal names those bodies/callers below. |
| Credits end only after the final public `Dispose` wrapper itself has physically returned | The same wrapper cannot execute that later debit. This stronger requirement needs an independently funded actual outer invoker/caller/runtime handback; a finite chain of nested wrappers does not solve self-return. |
| Final debit precedes only a proved no-grant-access/no-owned-allocation epilogue | Permitted in principle by the existing [M03-B owner-aware assessment](m03b-scene-preparation-owner-aware-async-handback.md), sections 4 and 7. An ordinary call or `NoInlining` alone does not prove that epilogue. |

Select the third interpretation for the **candidate**, not a claim that the current emitted suffix is safe. If the literal all-public-callers-physically-returned condition is mandatory, stop at the second row: the current public void interface supplies no such acknowledgment.

The candidate returns the **terminal invocation's** provision only after the actual publisher/core return. It does not claim that every earlier World/use credit waits for publication: those can end after their own last funded body, while a separately preclaimed lower-domain provision owns the remaining publisher. Nor is a returned 128-cell logical capacity credit a byte refund. If the requirement instead keeps the **same original World reservation** through domain publication, its current World-interest coupling prevents that sequence; an explicit issuer-owned retained-tail handoff would be required and is not selected here.

## 1. Two actual funding tiers

Use the already selected fresh standalone/private-default-domain lane only. Existing unselected/hosted/external-domain/restore and generic independent-use routes are not silently changed or qualified.

| Real owner | Prepaid provision / end |
|---|---|
| Actual World lifetime/standing control | Original World reservation funds selected storage, construction/failure and ordinary use/World-close body invocations. Keep its World interest/standing amount while those actual bodies are live. |
| Actual `WorldAdmissionDomain` terminal control | Its own qualified domain-control provision funds the enclosing terminal invocation, retirement publisher, retained terminal failures and final accounting after World/use claims end. Keep this actual charge through their returns. |

This makes explicit the missing **lower-domain terminal provision**. Domain-owned Task/control work is not reassigned to a shared registration object, a released World grant or a process-registry slab. The domain provision must be proved covered by the existing named control row, or separately preadmitted as actual domain-owned control/protected storage under Chapter 04's existing metadata and aggregate limits. No extra quota, U or operation/interest count is created.

This is a lower-owner refinement, **not** a claim that the present 4096-byte registrar control has spare room. Do not construct the invoker/control until that complete profile is available. If retaining/representing this provision changes common domain fields, price those actual unselected-layout changes too before using them.

No whole-World charge is refunded/reacquired to bootstrap this invoker. World-owned work stays under its original World claim; domain-owned terminal work has one actual domain claim. If a desired implementation instead loans World bytes into a domain row, that needs a separately reviewed original-issuer transfer conserving both physical charge and all applicable quotas; it is not supplied by copying `WorldDomainReservation`.

Use the domain's actual control as the terminal owner, not a newly allocated per-release wrapper/closure. Its candidate representation must retain the exact pending issuer identities, phase and failure/root obligation. These are additions to the representation/coverage audit, not free fields inside the current fixed reservation.

## 2. Concrete private call chain

The following are proposed **fixed Entities-owned operations**, not existing methods or a new general callback interface. Bodies execute on the selected owner thread/nonreentrant route. They accept only exact captured lifetime/domain/reservation/generation and closed profile; no application delegate runs the invoker.

### A. End an owned use without releasing its caller's funding

1. `WorldLifetimeControl.ReleaseOwnedUseInvocation` enters under the actual World standing invocation provision. Logical World closure may already deny admission, but the original World standing claim remains held. Do not enter final/deferred lifetime retirement while this standing owner still needs it.
2. It calls ordinary `WorldGuidUseControl.ReleaseBodyCore`. Process-release facts can advance as required, but the domain-use bytes/interests and actual control root stay held while this body runs. Active workspace/refusal and partial failures retain their exact existing channels.
3. The domain's private prepare operation validates/captures the exact release identities without the current early byte refund. The core returns an issuer-stamped ready receipt; that receipt itself owns no bytes.
4. **After the core has actually returned**, its real caller invokes `domain.CommitReturnedUseBody`. While the real use claim is still live, this operation ends the control's remaining fields/aliases and unlinks it with a surviving standing root. Only then does the exact issuer debit/free its use bytes/interests.
5. The standing invocation caller accepts the actual ordinary commit return, ends its body obligations and returns the pending credit. Its remaining work is still standing-owned, not charged to the ended use.

For this selected lane, the standing World interest is retained until all owned use invocations drain. Therefore an individual use release cannot unexpectedly become final default-domain publication. A release still callable after an already-retired World with no standing invocation owner remains outside this profile; do not pretend this makes every existing `WorldGuidUse.Dispose` caller funded.

### B. Close the World, publish retirement, then end terminal funding

Choose the **domain** as the real enclosing terminal invoker, because its provision outlives the World data claim:

```text
public World.Dispose selected forwarding path
  -> domain.RunOwnedWorldCloseInvocation [domain terminal claim held]
       -> lifetime.CloseWorldBodyCore [original World standing claim held]
            -> actual World close/cleanup body, including every World-reading finally
            -> all owned uses/body obligations ended or retained on failure
            -> returns exact WorldBodyReturned receipt
       -> domain.CommitReturnedWorldBodyAndPublishCore
            -> accepts only after CloseWorldBodyCore actually returned
            -> ends the original World data interest/claim at its real issuer
            -> retires only actually ended registrar backing, KEEPING terminal control charge
            -> exits publication-relevant gates/scopes
            -> invokes the existing retirement TrySetResult directly
            -> TrySetResult actually returns; all publisher/gate scopes end
            -> returns exact DomainPublicationReturned receipt
       -> enclosing domain caller accepts that ordinary return
       -> performs remaining prevalidated terminal accounting under the terminal claim
       -> final debit leaf, then ONLY the independently proved harmless suffix
  -> public wrapper returns with no further World/control access or owned allocation
```

In particular:

- The existing `ReleaseRegistry` cannot zero the terminal provision before publication. Split backing retirement from terminal-control retirement; keep actual unspent/protected provision charged, rather than inventing an ambient owner after `_registryCharge = 0`.
- Refactor every public-wrapper World access, cleanup/finally and pending field write into the protected body **before** its returned receipt. No remaining `_isStructuralOperationActive` reset, quarantine-capacity access or borrowed-control write may hide after the final debit.
- Finish the lifetime/process-retirement field work before ending its World-owned coverage, or keep that exact provision held. The domain publication core must not read/write an ended World/lifetime/standing control through the receipt. Any retained receiver/header still needed on failure belongs in a named surviving row; merely holding a reference is not a storage transfer.
- Make the two core calls ordinary non-inlined private boundaries in the candidate so their caller can observe actual return/unwind. This is a compilation constraint for evidence, not a return certificate.
- A ready receipt is a readonly ref-struct value, private-issuer constructed, caller-local and nonpublishable. It binds exact invocation/phase/generation/original claim; default/foreign/replayed receipts fail before debit. The fixed caller uses it only after successful return. A throwing core issues no consumable success token.
- Public retirement readiness remains after domain closure and retirement of actual World interests. A still-funded **domain-control publisher** is not another live World interest. Do not alter Task identity/outcome, continuation options or public readiness to use its completion as the ownership acknowledgment.

All five existing domain publication sites must participate in one exact selected terminal-publisher arbitration. The fixed invoker owns that publication exclusively; a competing/already-terminal setter is not a return certificate. The selected private-default-domain profile adds no engine observer and must not expose that Task to new work. Any reached engine observer/queued producer requires its own funded join or remains unsupported; the call's ordinary return joins the synchronous publisher, not detached work.

The actual final debit must have no later owner access, gate release, context restore, fallible publication or new owned allocation. A candidate may isolate it as a non-inlined, prevalidated accounting leaf; its caller and native epilogue still require proof. If that cannot be established, **do not refund there**: this candidate then needs the independent outer invoker capability described in the decision table.

## 3. Failure retention is part of the invoker

Preclaim the failure/root capacity before any release starts. Keep the actual domain terminal invocation and original binding/retirement authority through every fallible body/publication/accounting step; do not retire their last failure anchor early.

The candidate must have a prepared terminal commit which can end the remaining actual process/metadata roles only after all fallible work and return checks succeed. Do not assume the current fallible `TryRetire`/cleanup sequence already supplies that commit. Validate its exact generations and count/byte effects under the proper issuer gates while funding/root capacity is still held.

On failure:

- Preserve confirmed process/domain/receiver releases; never recreate a U or repeat completed destruction.
- Store the original body/cleanup failure references in the preowned real record, retain every remaining terminal charge/root and propagate the same selected primary exception/token.
- Use the already pre-reserved lifetime quarantine anchor only while its actual authority/root remains valid. Where the original World interest/lineage has ended, a new private terminal-retention validation must bind the **actual surviving domain terminal grant**, not falsely describe the old World reservation as still active.
- If retirement's Task has already become terminal, do not roll it back or report its outcome as clean resource settlement. World close must not return clean success with retained debt.

No new global particle/domain tail list or fake System is introduced. If a valid preclaimed terminal failure anchor/prepared final commit cannot be supplied under the actual existing issuers, that is an explicit missing owner capability and the entire invocation profile stays unavailable. A constructor-free header result does not close it.

## 4. Budget and evidence sequence

No byte value for either invocation provision is selected. Keep the World original-grant terms, common lifetime increment, shared-registration owner, selected data rows and lower-domain terminal rows disjoint. Both World and domain configured limits, domain metadata B and process/domain count ceilings remain unchanged; existing consumed roles are retained longer where required, not duplicated.

The next bounded work is:

1. Freeze this private call-chain candidate's exact fields, receivers, ready/returned receipt shapes, gate order, process/metadata prepared commit and terminal-failure root before implementation review. Include actual construction of its domain terminal provision before any escape.
2. Compile/inspect that source candidate in the established isolated production-artifact lane, with profiles unavailable and no World operation. Measure any new representation rows and inspect the actual core boundaries, IL exception regions, inlining constraints and native/JIT/runtime-tier suffixes. No shadow owner or completion-only test is used.
3. Derive complete simultaneous World-body and domain-terminal cold/normal/failure/runtime/native catalogues. Explicitly classify any reached observer/context/dispatch work; synchronous `TrySetResult` return is necessary for the publisher body but does not automatically join detached engine work. Do not declare framework overhead free or borrow unused numeric caps.
4. Prove the final debit's harmless suffix and failure commit/root under exact frozen lane inputs. Keep the last actual provision held until that proof. A Task status, no-allocation warm run, source `finally`, `NoInlining` attribute or another wrapper does not discharge it.
5. Only after complete provision/admission/retention/return proof, integrate the original World preclaim and domain terminal control row together and qualify real selected create/close paths, Debug then Release. Those operations are not authorized by this proposal.

The [owner-aware async-handback proposal](m03b-scene-preparation-owner-aware-async-handback.md) already explains outcome/producer separation and the valid harmless-suffix alternative. This candidate replaces only **ordinary lower release invocation ownership**; it does not select a new Task/ValueTask type, a builder decorator or a private async framework, and does not close Scene supplier return.

The owning future edits are `Entities\World.cs` (forwarding/body extraction), `WorldLifetimeControl.cs` and its cosmetic partial (standing invocation/drain), `WorldGuidUseControl.cs` and `WorldGuidRegistry.cs` (prepared process/use transitions), `WorldAdmissionDomain.cs` and `WorldDomainReservation.cs` (issuer split, surviving terminal charge and unique publisher), and the existing lifetime-quarantine reservation/registry (actual terminal-root validation). All are under `sources\TerraFX.Runtime`; no Rendering, Scene or public declaration change is a prerequisite. This is an ownership map, not authorization to edit them now or install hooks before their profile closes.

## 5. Exact capability still absent

Thus **the ordinary-call mechanism is possible in principle**, but the current synchronous caller cannot simply take the measured data allowance and refund it after `TrySetResult`. Required new private capabilities are:

- An actual domain-funded enclosing terminal invocation surviving World/use debit.
- An issuer-owned prepare/returned-body/commit split, not current early debit plus a flag.
- A preclaimed failure anchor/prepared terminal commit and a proved final no-owned-access/no-owned-allocation suffix.

If physical return of the final public caller is required instead of that suffix, a budget-aware actual outer caller/runtime invoker must acknowledge it. No current Task/public void interface exposes that fact. Keep admission unavailable in that interpretation rather than attach another observer.

The new 10,592 World-associated sizing inputs, separate registration increment, protected byte bounds and operational/hosted/external-domain/restore/Scene/pool gates remain unfunded or unqualified. No live-use readiness is claimed.

## Inspected boundary

`Entities` paths are under `sources\TerraFX.Runtime`. These identify inputs, not an implemented mechanism.

| Input | SHA-256 |
|---|---|
| `Entities\World.cs` | `692F721BEAD29E5EC258BF7B1844EC785E2D1548E9F53DFA9BDB57A22A01F09F` |
| `Entities\WorldLifetimeControl.cs` | `7F47BE8C4943CE6FC2610FD98D3D2B7470B8F83EA00EC5D9C6C4A3F5C46A9BEB` |
| `Entities\WorldLifetimeControl.CosmeticCredits.cs` | `C6F41DE02F0896016FA55845A61661BCD1A4DFE085BF68302767FE9C94DA3228` |
| `Entities\WorldGuidUse.cs` | `A6120452989F29EC2EF9C2B90E7479CC6635B9B17848C6C683063B3F99D36D22` |
| `Entities\WorldGuidUseControl.cs` | `F3345FCEB06BEEA0DA4B89E2CF3106B1F60717A71C810AD3C34477762C6964F9` |
| `Entities\WorldGuidRegistry.cs` | `9A7339A8ECFD56C60F7C2A5C9251BCBBA2DA73A7A26520AFCDBE533B24BD39E9` |
| `Entities\WorldAdmissionDomain.cs` | `2CE1C0B90157ADE6F82605409AB599F7415B7D8AA0A63A4BF4327263610248D9` |
| `Entities\WorldDomainReservation.cs` | `EA012B9B40ACEDAA820885EAFE27DCD11DF55F842B68F2849785DAB756E8E3BC` |
| `Entities\WorldSystemInstanceRegistry.cs` | `70DCB73D187AA6FC27B099F8C27B6716D3D63EF6C4022662017D34C25F5A4E20` |
| `docs\proposals\m05-cosmetic-particle-terminal-return-ledger-addendum.md` | `0A317BE624225E5A6873F82D491FDD93B2C01A91DCBAF1CF96153ADFE4659DB8` |
| `docs\proposals\m03b-scene-preparation-owner-aware-async-handback.md` | `0A9AE81EB42D886168D9E05970C7395B33C9EF70FCCADA9ED311CF2E1DC7AD89` |
| `docs\design\04-capacity-and-workload-profiles.md` | `A4D86F3FA373A64548435C53582BF5916E29D77F34650F9D161CCEC90E6F0245` |
| `docs\design\29-project-setup-build-and-deployment.md` | `3B55A013EEC327ECD648ACBD5B53717DD218AD288E8047AC21277609EFA2F47B` |

Only this proposal is added. No source/tests or maintained chapters/roadmap are edited; no build/probe or release, World/Scene/pool/Task-publisher operation runs.
