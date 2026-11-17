# M05 protected close provision and terminal return

## Result

**Tentative capability/evidence selection; no admitted construction profile.** Existing World/domain contracts can reserve a **known complete** amount within configured Chapter 04 headroom, without raising limits. They do not currently establish a complete amount or surviving protected-close claim for this new owner.

The measured 10,592 World-associated sizing inputs are not that complete amount. The separate shared-registration increment remains 8 bytes. The missing terms are not another header estimate: they are protected construction/failure/unwind/close capacity, its actual issuer/recipient, and the end of the last release/publication invoker.

Select **terminal-return grant coverage** as the next evidence gate. Do not run another object-size probe or attempt live construction to discover its allowance. The [standing-data coverage addendum](m05-cosmetic-particle-standing-data-coverage-addendum.md) retains the disjoint data inventory and the [construction addendum](m05-cosmetic-particle-standing-owner-construction-addendum.md) retains optional selection and original-reservation policy.

## 1. Reservation capability is not coverage

`WorldConstructionPlan` carries actual lifetime/domain bindings, not an independent budget or a new caller-tail grant. `WorldDomainReservation` can grow/release actual World bytes through its domain, and tracks independent-use bytes separately. It has no current protected-close loan/end witness.

Future admission must:

- Establish the complete additional simultaneous standing peak, including protected work and any real metadata/receiver overlap, before selected allocation.
- Check the exact configured World-state limit and original domain headroom; refuse the whole request if it does not fit. Count maxima and default 2-GiB/8-GiB ceilings are not entitlements.
- Price the common lifetime increment for every affected World, attribute shared registration metadata once to its actual owner, and include selected-only storage once.
- Reserve through the original World/domain issuer and keep that provision until actual end or an exact funded handoff. No new U, global tail list, raised limit, 4096-byte “spare room,” guessed margin or release/reacquire workaround is selected.

These are capabilities the existing reservation can support **after** its complete bound/end is established. Merely adding 10,592 to `worldOwnedBytes`, or supplying that number to a standing-control constructor, would not cover the protected terms.

## 2. Concrete final-release frontier

The inspected source and existing nonexecuting IL audit establish:

| Boundary | Ended fact | Still-reached work / unproved coverage |
|---|---|---|
| `World.Dispose` calls `_domainReservation.Dispose()` | May end the original World interest/byte claim | Lifetime completion, default-domain close and remaining World disposal/finally work still follow. |
| `WorldAdmissionDomain.ReleaseWorld` | Debits `_worldBytes`, releases the interest, calls `reservation.MarkReleased` | On a closed last-interest path, registry release and `_retirement.TrySetResult` still follow. |
| `WorldDomainReservation.MarkReleased` | Clears its domain binding | Holding the reservation object or its old `Bytes` value is not a surviving grant. |
| `WorldAdmissionDomain.ReleaseRegistry` | Clears registry storage and sets `_registryCharge` to zero | Last-interest/domain-dispose paths publish retirement after this method. The former 4096-byte registrar charge cannot simply be presumed live. |
| `WorldLifetimeControl.CompleteRetirement` / `CloseDefaultDomain` | Can clear reservation/lineage/default-domain references | Reference clearing is not an allocation/last-access certificate for remaining invokers. |

This identifies missing coverage for the proposed profile; it does not declare every current M03 behavior incorrect. Existing functional tests and the shallow observations do not prove the protected allocation/return intervals.

**Distinguishing case:** merely move World-reservation disposal after default-domain close. While its interest remains, domain disposal can return without completing retirement. Releasing that final interest afterward then reaches `ReleaseWorld -> ReleaseRegistry -> TrySetResult`. The last publication frontier moved; it did not disappear. No execution is needed to derive this from the source conditions.

## 3. Minimal required owner capability

Require a **funded terminal-close context** under the actual World/lower issuers. This is a private capability requirement, not a new public token/type, accepted byte value or implemented protocol:

1. The original constructor/standing provision owns all reached construction and failed-construction work until its real enclosing caller ends or accepts a funded handoff.
2. Before releasing an owner/storage claim, identify the actual still-live original or lower-control claim covering the closing helper, retained failures and remaining caller. Preserve the exact grant and failed debt through fallible release.
3. Before retiring the last domain-control charge, identify provision that covers actual retirement publication and its final invoker suffix. **Logical interest retirement, Task terminality and physical publisher return are separate facts.**
4. End/refund that provision only at a proved post-last-access boundary or through a caller-accepted transfer to another named funded claim. A new counter, passive wrapper or copied grant is not the recipient.
5. Account all applicable World/domain byte and metadata/count effects simultaneously, with no second disposal right or hidden capacity increase.

Prefer demonstrating coverage in the existing original/lower-control provisions. If implementation instead must retain or split their protected release phase, preserve public retirement readiness and exact failure/cancellation semantics; review that lower-owner change before activation. In particular, do not casually reorder/refund registry storage around Task completion or invent another operation credit to make the accounting appear balanced.

The current standing record is a retaining data structure, not an issuer of this capability. No amount for the protected provision is selected. If no existing claim covers an interval, the precise missing owner decision is **which actual issuer-backed claim survives that interval and who accepts its terminal handback**.

## 4. Exact next evidence action

Produce a bounded, **nonexecuting terminal-return ledger**, using the already frozen candidate/source and actual target CoreLib/runtime:

- Scope only fresh, standalone, empty-registration/private-default-domain storage construction and its success, unpublished-construction failure, normal close and failed-close branches. No Scene, live request or hypothetical caller is added.
- For each row, record: actual member/IL edge; original grant and byte/count row live before it; exact debit/release point; still-executing callee/caller and reachable backing; real surviving claim or **uncovered**; failure disposition; required return/handoff fact.
- Extend the existing 47-body source/Debug-IL catalogue at the identified final-release edges, not with another broad source inventory. Bind observations to source hashes, module identities and target configuration; review the other configuration before applying a cross-lane disposition.
- Inspect the matching CoreLib `TaskCompletionSource.TrySetResult` path and its reached Task/continuation/context/native boundary **without invoking it**. Establish the actual private-domain observer/engine-registration set; do not infer it from the absence of a cosmetic Task.
- For each remaining cold/runtime/native or failure dimension, either supply a finite derivation with its real owner, or name the exact missing allocation/return observation capability and retry condition. Static absence of `newobj`, aggregate GC traffic and warm zero-allocation calls are not substitutes.

The deliverable is the smallest source/compiled/runtime allocation-and-grant slice that either proves an existing protected row or specifies the necessary private lower-owner handback change. Do not implement or execute selected standing construction while that ledger contains an uncovered interval or unknown required bound.

After that closure, and only then, derive the complete standing total and integrate its original-reservation preflight/retention together. The measured data inputs can be reused where valid; they do not justify selecting protected work as zero. A runtime/native capability that cannot support the required bound leaves the profile unavailable rather than authorizing an exploratory operational owner.

## Inspected boundary

These are inspected working-tree/artifact inputs, not a protected-capacity or return qualification. `Entities` paths are under `sources\TerraFX.Runtime`.

| Input | SHA-256 |
|---|---|
| `Entities\World.cs` | `692F721BEAD29E5EC258BF7B1844EC785E2D1548E9F53DFA9BDB57A22A01F09F` |
| `Entities\WorldConstructionPlan.cs` | `3A6E51A8099482A0FCA32F7FAAC3BD55CFC7C53D233FC88B1BD369DEDB32C066` |
| `Entities\WorldDomainReservation.cs` | `EA012B9B40ACEDAA820885EAFE27DCD11DF55F842B68F2849785DAB756E8E3BC` |
| `Entities\WorldAdmissionDomain.cs` | `2CE1C0B90157ADE6F82605409AB599F7415B7D8AA0A63A4BF4327263610248D9` |
| `Entities\WorldLifetimeControl.cs` | `7F47BE8C4943CE6FC2610FD98D3D2B7470B8F83EA00EC5D9C6C4A3F5C46A9BEB` |
| `Entities\WorldDefaultDomainOwner.cs` | `BCF8F830FBB29E652D480608A3BDFC197D89D7E58299622941E87685B4C2AAD4` |
| `artifacts\qcl\h\results.json` | `D69C5C2D69517FEE0AD42D4B63270A3B01628DEBC9CFC9A159E4918C6A2AA916` |
| `artifacts\qcl\h\coverage-audit.json` | `74A132AF533C7FFBC6445B7B8FB6203149359FE700F8C230239153A2E299876E` |
| `artifacts\qcl\h\compiled-coverage-debug.json` | `3516C4652BBAA4B6F4E6FBCDA89511FBB51529FB809DB2766432A73F70107C38` |
| `docs\proposals\m05-cosmetic-particle-standing-data-coverage-addendum.md` | `8BA1AA18867265CBE0B142AB5158921A65466EFB393F9B5F57D4B2B91484F843` |
| `docs\proposals\m05-cosmetic-particle-standing-owner-construction-addendum.md` | `488BC2E441FE0AD22501ACD7B63796C8FDF47912A78F2F23A3B924FE77053AA7` |
| `docs\design\04-capacity-and-workload-profiles.md` | `A4D86F3FA373A64548435C53582BF5916E29D77F34650F9D161CCEC90E6F0245` |

Only this proposal is added. No source/tests or maintained chapters/roadmap are edited; no build/probe or World/Scene/pool/publisher operation runs.
