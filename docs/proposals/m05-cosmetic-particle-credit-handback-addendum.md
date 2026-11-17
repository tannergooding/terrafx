# M05 cosmetic-particle acquisition debt and credit handback addendum

## Status and decisions

**Tentative private owner-contract closure; not implementation or qualification.** This resolves the two ownership decisions left by [the budget/return addendum](m05-cosmetic-particle-budget-return-addendum.md); it supplies neither a measured construction profile nor a finite Task-publisher envelope.

Select:

1. **An Entities-owned, preowned acquisition journal in the same 128-credit owner.** The actual `WorldLifetimeControl`/domain/registry issuers record original raw-ticket/control ownership before fallible work and terminal lower release at its authoritative site. Exceptions remain exceptions; a caller does not infer debt from their type/message.
2. **A World-lifetime-owned standing credit/release owner, with caller-accepted terminal handbacks.** A releaser prepares a return-ready token; only its actual lower caller, after the releaser successfully returns, consumes that token and refunds the cell. World standing ownership ends through a separate close handback.

No public API, diagnostic ID, additional U/credit allowance or canonical Resource/System/Attachment registration is added. Current source does not yet implement these private capabilities. Until they and their changed standing footprint are real/qualified, construction/ingress stays fail-closed.

## 1. Existing mechanisms and constraints

All source paths in this section are under `sources\TerraFX.Runtime\Entities`.

| Input | Exact consequence |
|---|---|
| `WorldLifetimeControl.AcquireUse` | Raw admission precedes control construction, linking, domain interests, process U and carrier return. Cleanup may throw before a carrier escapes. Both capacity and later invariant failures can escape through conventional exceptions; their type alone is not a no-debt certificate. |
| `WorldAdmissionDomain.LinkWorldGuidUseRaw` | Linking moves the actual control into the lower retained-use list and frees/retires the raw ticket. A journal must record that ownership transfer before the ticket can be reused. |
| `RollbackWorldGuidUseRaw`, `RollbackUnissuedWorldGuidUse`, `TryReleaseIndependentUse` | Actual accounting can end under lower gates before later retirement/publication work returns or throws. Conversely a failed check may retain it. Lower debt state and producer/caller last access are different facts. |
| `WorldGuidUseControl.Release` | Process release precedes domain release. Its catch records Quarantined. It also returns normally when already Quarantined, so **normal void return is not proof of Released**. |
| `WorldResourceOperationControl` | Exact family/operation/thread checks, captured source ownership, revocation and first-fault recording belong to the issuing owner. Revocation does not itself return storage. |
| `WorldResourceOperationReservation`; `WorldPreparedSourceStorage.StorageUse` | Ownership fields survive fallible release; a failed retained-source cleanup stays sticky/rooted. This is the appropriate ownership pattern, not permission to copy their 8192/512/256-byte reservations to a new type. |
| `WorldLifetimeControl.BeginRetirement/CompleteRetirement`; `WorldGuidRegistry.TryReleaseIndependentUse` | Logical revocation and final retirement are distinct. Last-U release can call deferred lifetime completion; premature World retirement could therefore create a cycle with a credit waiting for that release. |
| `WorldSystemQuarantineReservation`; `WorldSystemInstanceRegistry.QuarantineLifetimeControl` | The existing lower World-lifetime quarantine path reserves one lifetime-control anchor even with zero systems. It can retain the actual quarantined lifetime; it does not make the particle owner a System or provide free storage for its child graph. |
| `IWorldGuidRetentionOwner` | This is an empty marker, not a retaining edge, allocator, return witness or budget claim. Implementing it alone solves neither decision. |

Chapter 12/30's scoped family and immutable-source rules and Chapter 29's Entities-owned identity/retention authority remain intact. Rendering must not receive the registrar, reservation-release authority or a raw-ticket constructor.

## 2. Concrete standing owner and stamps

Introduce one **private `WorldCosmeticCreditOwner` in Entities**, logically owned by the exact `WorldLifetimeControl`. It owns the fixed 128-cell backing and the acquisition/return bookkeeping. Its per-World construction binding is the actual lifetime/owner object and captured grants, never WorldGuid equality.

The eventual live selection must install the actual strong lifetime-to-owner edge before any lower acquisition can start. Do not rely on the caller's stack or install that edge only after acquisition succeeds. Installing a new field/child graph remains subject to the changed standing-footprint gate.

Required state, not guessed byte values:

- Shared immutable captured lifetime/domain/reservation/registry binding and owner thread.
- Exactly 128 preowned cells, occupied/prospective count and Open/Closing/ReturnReady/Returned owner state.
- Per-cell owner-local nonzero `uint` generation, lifecycle phase, acquisition facts, exact lower references/stamps, outstanding work/transfer facts and retained failure references.
- Standing construction/rollback/release/drain provision under the actual World/domain issuer, including changed lower-control layout and any still-owned caller suffix.

Cells cover prospective construction, accepted work, return preparation and retained debt. Generation zero/default is unissued; never wrap or recycle a live generation. Retire an exhausted cell rather than silently restore usable capacity. This adds no count beyond the selected 128.

Define three private stamps:

| Stamp | Issuer and data | Authority |
|---|---|---|
| `WorldCosmeticCredit` | Credit owner only: exact owner reference, cell index `[0,128)`, nonzero cell generation | Identifies one reservation; copying it adds no credit or disposal right. |
| `WorldCosmeticCreditHandback` | Credit owner/releaser only, return-ready cell: exact owner/index/generation | A synchronous caller-local handback, not a producer-return proof or a raw-grant release capability. |
| `WorldCosmeticOwnerHandback` | Closed/drained credit owner only: exact owner/lifetime | Permits the World-lifetime caller to finish standing ownership after the closing helper has actually returned. |

Make handbacks readonly ref-struct values with no public constructor, no heap/Task publication and no borrowed payload span. A default, stale, foreign or already consumed handback is invalid. The fixed lower caller uses a local, not an out field visible before return; it validates the complete stamp and ReturnReady state before accepting it. An owner is never reopened/rebound after closing, so no additional close cursor is needed.

This is not a general operation registry. Only the closed cosmetic factory/World owner uses it. No arbitrary callbacks, logger, observer interface or application provider runs in journal/cell updates or under lower gates.

Reserve/acquire/prepare/accept/close remain on the actual World owner thread and the selected nonreentrant entry/dispatcher route. Returning through a captured binding after revocation is cleanup authority, not ordinary-thread admission. Keep the existing registrar gate order; journal stores add no nested owner lock or callback.

## 3. Acquisition journal: authoritative facts, unchanged exceptions

### Required fact set

For each credit, the issuer records these facts. Acquisition stage, lower-debt disposition and standing cell phase are separate; an operational NoLowerDebt fact cannot erase quarantine or prove terminal return.

| Fact | Meaning / actual retained identity |
|---|---|
| Unattempted | No lower acquisition has started. |
| NoLowerDebt | The issuers have confirmed that this attempt owns no remaining raw/process/domain/workspace credit. It says nothing about an acquisition caller/rollback suffix still running. |
| RawHeld | The exact domain/reservation/lifetime and raw-ticket generation that actually reserved bytes/prospective interests. |
| ControlHeld | The actual `WorldGuidUseControl` reference/carrier generation, plus original attempt facts. Its live/ended subclaims remain authoritative in the existing registrars/control; do not reconstruct them from copied anchor fields. |
| Issued | This attempt returned the actual carrier/control to its selected operation. The one release right stays with this credit owner. |
| RetainedDebt | Acquisition/cleanup did not establish complete release/return. Keep the actual raw/control graph and its precise original stamps; do not assert all subclaims are still live or all are gone. |

Retain an acquisition failure and an escaping cleanup failure by reference where both exist. Recording these references creates no aggregate or replacement exception. A cell cannot perform a second acquisition attempt; reuse requires completed handback and a new cell generation.

### Issuer integration

Add **private issuer overloads/common-core support** taking a prevalidated standing journal site. The current legacy `AcquireUse(long)` outward behavior remains unchanged. The journal site is a fixed sealed-owner/cell stamp, not a delegate, mutable caller sink or forged lower grant.

Keep the existing issue order, exact grant/generation checks, carrier-creation validation and cleanup selection. Returning a borrowed control to the private operation does not issue another carrier/U or transfer the standing owner's one release right to that borrower.

Record:

1. Raw reservation success inside its issuer before the attempt proceeds.
2. Actual constructed control in the preowned cell before a fallible link/issue call.
3. Raw-to-control linkage before freeing/reusing the raw ticket.
4. Domain/process issue and actual releases at their own authoritative mutation points.
5. NoLowerDebt only when the original attempt's obligations really ended, including the case where later domain-retirement publication still throws.

Validate the journal's exact lifetime/thread/cell/generation before lower work. Its in-gate recording is preowned field/state assignment: no allocation, formatting, callback, new fallible validation or failure masking. This requires source/compiled verification before activation, not an assumption that all instrumentation is free.

The existing throw selection is preserved. An initial refusal still throws the current exception. If the current acquisition catch's cleanup throws and replaces the acquisition exception, the same cleanup object still escapes; retain the earlier cause privately without substituting `AggregateException`, `Exception.Data`, a new carrier or another public channel. Bare rethrow/original provenance remains. This protocol introduces no cancellation token/check and never converts an existing `OperationCanceledException`, token, fault or cancellation into another outcome.

### Failure/handback matrix

| Actual path | Journal / credit result | Caller result |
|---|---|---|
| Refusal before raw reservation | NoLowerDebt; prospective cell stays occupied until the entry guard's own handback | Same current exception/channel; no accepted command/sequence |
| Constructor/link/issue failure, complete original rollback | NoLowerDebt, with the original attempt facts retained as nonauthority metadata until its suffix ends | Same escaping acquisition exception |
| Failure with raw rollback incomplete | RetainedDebt with the exact still-owned raw ticket and original reservation | Same currently escaping cleanup/acquisition exception |
| Failure after link/partial cleanup without terminal issuer proof | RetainedDebt with the actual control; released subclaims are not retried/recreated | Same currently escaping exception |
| Successful issue | Issued control belongs to this cell; anchoring follows the budget proposal | Same real carrier/control, not another U |

A NoLowerDebt record does **not** make the cell Free. A newly constructed but no-longer-operational control, its local references and rollback/retirement-publication suffix still need the standing rollback-tail claim until their actual end/transfer. Reusing the existing lower accounting must not make those allocations or accesses uncharged.

## 4. Standing releaser and terminal handback

Select the exact **World-lifetime caller** as the terminal acceptor. The credit owner is the standing releaser; neither a Rendering payload nor the method which is still releasing its own inputs can refund a cell by setting a flag.

The private operation projection is:

| Owning type / operation | Result |
|---|---|
| `WorldCosmeticCreditOwner.TryReserve(out WorldCosmeticCredit credit)` | `bool`: reserve one prospective cell; false means no reusable cell |
| `WorldLifetimeControl.AcquireCosmeticUse(WorldCosmeticCreditOwner owner, in WorldCosmeticCredit credit, long workspaceLimit)` | Borrowed actual `WorldGuidUseControl`, or the unchanged acquisition exception with disposition recorded in the cell |
| `WorldCosmeticCreditOwner.TryPrepareCreditReturn(in WorldCosmeticCredit credit, out WorldCosmeticCreditHandback handback)` | `bool`: false means actual ownership is not terminal; no handback |
| `WorldLifetimeControl.AcceptCosmeticCreditReturn(in WorldCosmeticCreditHandback handback)` | `void`: consume the exact ReturnReady cell once |
| `WorldCosmeticCreditOwner.BeginClose()` | `void`: monotonically deny admission |
| `WorldCosmeticCreditOwner.TryPrepareOwnerReturn(out WorldCosmeticOwnerHandback handback)` | `bool`: false means standing close is not terminal; no handback |
| `WorldLifetimeControl.AcceptCosmeticOwnerReturn(in WorldCosmeticOwnerHandback handback)` | `void`: consume the exact owner's ReturnReady handback once |

These are engine-private operations in `TerraFX.Entities`, not public APIs or permission to omit supporting fields/checks. Invalid/default/foreign/stale/replayed stamps remain usage failures, never capacity false. `workspaceLimit` retains the selected 65536-byte request; neither an empty bookkeeping body nor a smaller observed allocation authorizes shrinking it. That request is not a qualified publisher size.

### Per-credit return

1. End/transfer actual body, receiver, pin and workspace obligations under their real issuers. The budget proposal's producer/helper return conditions are separate facts. Task terminality is not one.
2. For a control requiring release, invoke the existing release logic with the exact captured generation. Add an issuer-owned read of its **actual Released versus Retained state**; normal void return, `!IsActive`, cleared fields or a prior Quarantined no-op are insufficient.
3. A thrown release retains the actual cell/control graph and rethrows that same object. A normally returning Quarantined control yields no successful handback. No retry of already completed process/domain destruction is introduced.
4. Only when lower debt is absent and every required owned producer/transfer condition is met, enter ReturnReady and prepare the synchronous handback. Keep the cell counted and its actual standing provision alive while this helper still runs.
5. **After the helper successfully returns through its qualified boundary**, the real World-lifetime caller consumes the local handback, confirms the exact cell is still ReturnReady, clears ended references and returns that one pending capacity credit. Do not decrement from inside the releaser before its caller accepts.

A thrown prepare helper issues no consumable token. An acquisition exception may leave authoritative NoLowerDebt facts; only after that acquisition has wholly unwound may its still-owned entry guard call the return protocol. A saved state flag/out value from an uncompleted call is never a substitute for its return.

The control's existing 256-byte representation need not gain a journal field: the standing cell holds the actual control and methods receive the journal site. Any implementation which changes an existing control's layout must requalify its charge; do not assume spare bytes.

### Standing close return and retention

The World lifetime owns the credit owner through close. `BeginClose` denies new reservations, but cleanup/handback on the captured binding remains legal after public admission is revoked. Do not require an active World read scope to release old ownership. Preserve the selected normal close protocol: join actual stage/producer use and settle accepted unevaluated requests as `WorldClosed` through their funded terminal publisher, without a tick or circular wait for caller-held recordings/observations.

**Do not call final `CompleteRetirement`, roll back/release the lineage, dispose the actual World reservation or drop the standing owner while this owner still owes a credit/tail/close handback—even for a never-ticked World.** In particular, do not enter deferred final lifetime completion and then wait for the last cosmetic U whose release calls that same completion; drain the cosmetic owner while lifetime is revoked but still retained, then allow normal final/deferred retirement of other remaining uses.

After all cells and owned close/releaser work end, `TryPrepareOwnerReturn` produces the separate close handback. Its real caller accepts only after the closing helper returns. Keep the actual standing reservation/root through its fallible release; clear links/refund only on success. A zero pending count alone does not prove that the close caller/tail ended.

If debt remains, retain the actual lifetime/standing owner and its remaining claims through the **existing World-lifetime quarantine anchor**, using its pre-reserved lifetime-control slot. Add the cosmetic retention predicate to every early lineage/reservation/final-retirement release condition. The anchor holds the lifetime, which holds this owner, which holds its cells/debts; it is not an extra System instance, new U or global particle list.

Preserve existing mandatory retirement/quarantine failure precedence. When the retained root is successfully established, retain the current attachment-then-system cleanup reporting precedence; privately retain any additional credit failure rather than replacing those failures with an aggregate. With neither existing failure, throw the original credit release exception, or, for retained-without-throw, use the existing conventional `InvalidOperationException` retirement-failure channel. Attempt other independent safe cleanup first and never report a clean World close with debt. Repeated disposal must not repeat confirmed destructive releases.

This is the selected **private retention capability to implement**, not a claim that current World disposal already includes the new predicate/link. `IWorldGuidRetentionOwner` alone and a reference in an otherwise collectible wrapper are not that capability.

## 5. Funding and proof boundary

The owner/journal/cells, lower-layout changes, rollback and final release/drain suffix have an actual standing World/domain claim, admitted before construction. Keep separate still-needed operation workspace and independent receiver interests. Acceptance of an owner handback cannot refund storage still referenced by its own acceptor/release suffix: prove its end or transfer that remaining interval to the actual lifetime caller's disjoint protected standing claim before refunding the sender. If that caller provision/return cannot be established, retain the original claim. Never borrow the Resource source's fixed control allowance, create a 129th fallback cell or reduce an outstanding World use.

No numeric standing or publisher value is selected. Compile the new unselected private graph before wiring it into a live World/lifetime layout under old charges. Its current qualified creation/work profiles must remain unavailable until the actual F/rollback/release rows are derived. Existing World/Resource paths without this selection keep their existing behavior and reservations.

The stamps and journal establish who may record debt and accept returns; they are **not self-authenticating evidence of runtime quiescence**. The chosen ordinary direct-call caller boundary still needs compiled/source/native proof that the callee and its remaining funded wrappers no longer access/allocate against the returned claim. Inlining requires the equivalent proved no-further-access suffix. If that boundary or the lower retained-root hook cannot be supplied safely, no handback is issued and the affected path remains fail-closed.

The publisher's unbounded observer dimension, provenance partition and actual Task/context/native allocation/return catalogue remain the budget addendum's gates. This design neither declares those allocations free nor supplies a finite W.

## 6. Smallest candidate enabled

Implement only the production-private **128-cell prospective/debt/ReturnReady ledger**, stamped nonescaping handbacks and fixed lower issuer-journal/release-state path:

- New Entities-owned credit/journal/handback types with actual source/layout and no public facade.
- Private common-core/overload support in `Entities\WorldLifetimeControl.cs`, `WorldAdmissionDomain.cs`, `WorldGuidRegistry.cs` and `WorldGuidUseControl.cs`, preserving original throw/cleanup behavior. Do not add a generic observer framework.
- A complete no-work credit lifecycle may express real acquisition, failure ownership and return through those issuers without starting any Task/particle/Scene producer. It qualifies only bookkeeping when funded, never births.
- Keep live construction, operation publication and World retirement integration gated until their actual standing links/claims/return proof close. No no-op participant, invented debt object or fake “Returned” callback.

This supplies the lower contract which a counter-only container lacked. It enables compilation/layout/call-path inspection without a numerical publisher envelope. Owning Runtime Debug build and later genuinely funded real World bookkeeping tests remain distinct; no Scene preparation, pool simulation, native output or public API is enabled here.

Remaining live gates are the actual standing/caller-return profile, finite engine-owned operation/publisher envelope and actual-return proof. Nonempty particle execution additionally needs the real prepared emitter/cohort/Scene producers and their existing J/K, supplier/receiver and final-return gates. None is discharged by this journal or by a bookkeeping test.

Only this proposal is added. No source/tests or maintained chapters are edited; no build/tests/Scene execution occurs this turn.

## Evidence boundary

These hashes identify inspected working-tree evidence, not an accepted implementation profile. Reconcile changes before applying a disposition.

| Input | SHA-256 |
|---|---|
| Budget/return addendum | `1574E6D573BDFE2AF45A3E44EE3BAB5FE52131A02547C8610294F8BBF3C4B119` |
| Chapter 12 | `174B2465746DA79CEF560C8506216EF4F99F9D1AABAC184F872C9E6CB0B94E0B` |
| Chapter 29 | `3B55A013EEC327ECD648ACBD5B53717DD218AD288E8047AC21277609EFA2F47B` |
| Chapter 30 | `15848C82F3858A77A87A0D3EA6FBCC07046EB6E77CA5B21748C72B90EFAB598B` |
| `Entities\WorldLifetimeControl.cs` | `B1A9ADD0A1D68CAF4C385973CA4E2602C68E19E46078AA3CB09614848C09A634` |
| `Entities\WorldGuidUseControl.cs` | `EBF8A3F7C1115538CF06379FA65AE339B783117E39F76D1DE61C46C0B5C40466` |
| `Entities\WorldAdmissionDomain.cs` | `E3FFB70F958230A89387ABFAA730213C574DF3850E617B83AB506A9C90F71691` |
| `Entities\WorldGuidRegistry.cs` | `470EC745500181A57D2E0625B3A93EE5E51A899E046C017B3DD490FDB02BE739` |
| `Entities\WorldResourceOperationControl.cs` | `55E29520F3B5DF8A2DA746CEDA149D5A63F78E2339F3442180D06A4A5FD3CFCF` |
| `Entities\WorldResourceOperationReservation.cs` | `50AFBA21BE95312EC76A7E09C29D7BA9A2AF00F977E722115AB9ABCADA0FB025` |
| `Entities\WorldPreparedSourceStorage.cs` | `B416B0AB992ED34523209D13A6F5648EB1623BDBFD8022CCE841D1CC07C02925` |
| `Entities\WorldSystemInstanceRegistry.cs` | `70DCB73D187AA6FC27B099F8C27B6716D3D63EF6C4022662017D34C25F5A4E20` |
| `Entities\WorldSystemQuarantineReservation.cs` | `F864C7C0E250D4F6DA38B587C964CE0366E54FFFE22509120B84726E4354361F` |
| `Entities\IWorldGuidRetentionOwner.cs` | `2D22B0F24B56DFA9851BE868054FA30C80B5208CE3F405367DBB01A458301B6B` |
| `Entities\World.cs` | `692F721BEAD29E5EC258BF7B1844EC785E2D1548E9F53DFA9BDB57A22A01F09F` |

`Entities` paths above are under `sources\TerraFX.Runtime`.
