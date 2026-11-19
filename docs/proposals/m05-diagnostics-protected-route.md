# M05 Diagnostics protected-state and funding route

## Decision and boundary

**Tentative implementation guidance, not an accepted contract or allocation profile. P1/P2, numeric eligibility, and the complete 32-/16-MiB Renderer ledgers remain open.**

Select **complete immutable records, admission before the operation that may require them, and prepaid source funding with independently retained lifetimes**. Do not select a text arena, truncated fault, replacement public result, new emitter/receipt API, diagnostic ID, quota, or Core-to-Runtime callback. Ordinary telemetry may lose admission earlier to protect mandatory state; the proposed observable differences are explicit below and require owner disposition before activation.

There are two different impediments:

1. **Protected-input decision:** Chapter 28 must bind its Critical guarantee to actual producer operations and their complete pre-admissible fault shapes. No such production catalog is established by the current source. Public constructor validity cannot supply a finite maximum, and an empty catalog is not acceptance. Section 3 states the exact decision needed rather than manufacturing a Critical producer.
2. **Lifetime funding evidence:** use the concrete source-envelope/retained-use route in section 5. The shared clock additionally needs a demonstrated common lifetime payer. The examined paths do not demonstrate one; that is not proof that no existing payer can exist. If none can be demonstrated, its private-storage/funding disposition returns to the owner, preserving Chapter 03's shared clock.

The dependency-ready next implementation batch is source escrow and standalone/cross-service composition, not a purported complete protected-profile implementation. Section 8 specifies its changes and regression witnesses. Mandatory producer activation waits for its own remaining prerequisites.

## 1. Authority and current evidence

The applicable authorities are [Chapter 04, sections 2–3](../design/04-capacity-and-workload-profiles.md#2-binding-defaults), [Chapter 28, section 1](../design/28-diagnostics-performance-and-validation.md#1-observable-system), [Chapter 29's diagnostics ownership](../design/29-project-setup-build-and-deployment.md#diagnostics-service-ownership-and-issuer), [Chapter 30's public diagnostics surface](../design/30-public-api-review-surface.md#10-platform-ui-editor-modules-persistence-diagnostics), and [Chapter 35's prerequisites and completion gates](../design/35-implementation-roadmap.md#4-completion-gates). In particular:

- The host service's **8,388,608-byte ceiling** includes control/ring backing, complete retained graphs, protected fault/counter roots, and simultaneous construction/replacement storage.
- Exact `MaximumDiagnosticBytes` is an attribution sublimit inside existing World funding, not additional storage. Standalone service ownership and cross-service use must compose within it.
- Queue loss cannot erase Critical last-fault state or change the independent operation result. Returned Diagnostics and copied snapshots remain immutable.
- Ordinary oversized records may be refused without truncation, with the applicable dropped count. This does not authorize applying ordinary loss to the independent Critical guarantee.
- Polling returns at most the newest matching records in stable admission order, without draining/resetting another consumer. Caller-owned observations do not prolong service/account debits.

The [allocation-profile proposal](m05-diagnostics-allocation-profile-closure.md) remains the frozen `4069a0a706feabf4e66a3e6affafde503160b836` analysis, not a current construction trace. Current source includes **`0be05aac7f55809ef766068a12843c01b0198d54`**:

| Current fact | Consequence |
|---|---|
| Public `Diagnostic(...)` still defensively copies producer context. The new private copy constructor and `WithOwnedContext` take service-created immutable backing. | Append/update no longer make the old second context-array copy. This does not restrict public string/context lengths or change immutable observations. |
| `AppendOccurrenceCount` creates its final appended context; `UpdateOccurrenceCount` transfers an exactly filled Builder backing into the replacement. | Re-derive these construction paths before reducing charges. The frozen proposal's additional final constructor copy is stale here; current workspace helpers still reserve their older conservative terms. |
| `WriteCore` still performs ordinary account/service/ring admission before installing `_latestFault`. `_protectedSnapshotBytes` is not protected fault/counter storage. | The context-copy optimization does not repair P1. A previously retained Critical graph can prevent the next full graph from fitting. |
| Saturating helpers update numeric counts but no production source constructs `DiagnosticCounterSaturated`. | Numeric saturation tests alone do not satisfy Chapter 28's retained-observation requirement. |
| Account reservation is a Core CAS over an attribution balance; fresh admission pays account control and standalone backing, not the complete variable envelope. | A quota is not a World/domain funding grant. Restore needs its own funding correction rather than copying the fresh-path claim. |

Existing TRX files were read, not rerun. Both final Core runs report **441 selected/executed/passed**, and both World-subscription selections report **3 selected/executed/passed**, with zero failures. Their allocation output identifies `.NET 11.0.0-rc.3.26522.148/X64`:

| Path-specific managed traffic | Baseline | After context-copy change |
|---|---:|---:|
| Preconstructed Warning admission | 536 B | 400 B |
| Coalesced update, two producer context entries | 470 B/call | 398 B/call |

These are not full retained-size, owner-lifetime, portable CLR, native, or NativeAOT bounds. No new build, test, allocation probe, or native execution was performed for this document. The frozen runtime-layout derivation is supporting evidence only within its stated lane; changed method lowering and complete funding still require review.

## 2. Producer membership: actual inputs versus possible public values

The current `sources` scan finds `DiagnosticSeverity.Critical` only in `Diagnostic`/`DiagnosticQuery` validation and `DiagnosticsService` validation/retention. It finds **no production Critical constructor use**. The production input boundary is nevertheless larger than the two well-bounded Warning formats:

| Current route | Actual membership / limitation |
|---|---|
| Core subscription warning | `WorldSubscriptionCapacityExceeded`, Warning; fixed operation/message, two `"64"` producer entries, then service `OccurrenceCount`. A genuine current coalescible producer. |
| Renderer publication | `DegenerateSkinnedNormal`, noncoalescing Warning; six validated ordered entries, fixed literals, bounded canonical numbers/identities and matching command identity. Zero rows emit nothing; each refused validated positive occurrence is one drop. |
| `World.FaultWorld` | **Error**, not Critical; code/message may come from a validation Diagnostic, otherwise an exception message supplies the message. The service and `World.LastFault` are distinct retaining owners. Do not promote this route or bound arbitrary exception text by its default literals. |
| World publication delivery | `SubscriberDeliveryFailed`, Error; fixed message and current `U:Unqualified` provenance. The report's retained original exception/result is separate from the service Diagnostic. |
| World frame/time-scale/owner-queue refusals | Warning producers in `World.Controls`, `World.OwnerControlApply`, `World.OwnerControlAdmission`, and `World.OwnerControlRequests`. Include their coalescible aggregates and producer/result construction, not only the subscription warning. |
| Host/Input forwarding | `EngineHost` and `PlatformInputRoutingOwner` construct/forward Warning/Error records, including scheduler, platform, text, routing and overflow paths. Some retain supplied messages. These are service-only construction/retention owners; a visible WorldGuid alone does not establish exact source-account charging. |
| Public `Diagnostic(...)` | Accepts arbitrary nonempty code/operation, arbitrary message/command text and initialized valid context, including Critical severity. It copies context entries, not referenced strings. There is **no public service emitter**. Constructor legality is not evidence that these values are current mandatory producers or that every legal value fits retention. |
| Core Critical test fixtures | Exercise internal admission and retained-fault behavior. They are not production producer membership, a full-result bound, or a native witness. |

The mandatory counter membership already known for this service is **not empty**:

- One service-lifetime `DroppedCount`.
- One service-lifetime `CoalescedCount`.
- One `OccurrenceCount` for each admitted **coalescible aggregate**. Noncoalescing skinned-normal rows remain occurrence 1, so that path does not create incrementing aggregate counters.

Reference counts, quota balances, timestamps, owner sequences and budget limits are not automatically additional telemetry counters merely because their representation is integral. Conversely, Chapter 28's clipped-sample, underrun, histogram/replacement and other owners' observations are not waived: each active producer must supply its real counter/control-owner membership before that feature is qualified. This bounded service batch does not claim their implementation or an empty-set pass.

## 3. Smallest protected-input decision

**Recommended owner disposition:** preserve the complete Diagnostic and independent result for every admitted mandatory Critical producer; require its full worst-case construction/retention obligation to be funded **before accepting the operation**. Keep the public constructor and severity domain unchanged. Do not substitute “only if ordinary `Write` happens to fit.”

The missing owner input is one closed producer table, with a row for each actual mandatory Critical operation: existing code/severity/result contract, issuer, complete field/context domain, maximum lengths/multiplicities or checked input-dependent extent, construction workspace, concurrent accepted-operation ownership, exact source-account assignment, and the existing before-work refusal boundary. Unknown future producers are not eligible. The table must explain the current absence of production Critical emitters; it must not invent one to obtain a nonzero test count. Selecting which existing failure contract really requires Critical publication is an owner decision, not a Core implementation inference.

Why this decision cannot be replaced by a larger constant:

- A constructor-valid Critical with `new string('x', 4_194_305)` as its message has **8,388,610 bytes of character payload alone**, exceeding the host-service ceiling before the Diagnostic, headers, other strings or control storage. Retaining a pre-existing string by reference still retains that graph.
- Two constructor-valid Critical messages of `2_097_153` characters each already total **8,388,612 character-payload bytes**. Each fits below 8 MiB individually, but their old/new coexistence cannot fit, even with no ordinary records.

These are counterexamples over the public value domain, not proposed producers. If Chapter 28 instead intends unconditional service retention of *every* constructor-valid Critical value, its finite ceiling and complete immutable retention cannot both be implemented. Owner clarification/revision is unavoidable; this proposal chooses neither truncation nor ordinary Critical refusal to conceal the conflict.

For a producer with genuinely unbounded post-failure text, refusal after the fault occurred is too late. Its independent result must remain truthful and complete. Either the actual owner supplies an already accepted bounded record/result mapping or that producer's numeric/mandatory eligibility remains blocked. The existing Error path is not a secretly accepted bounded Critical mapping.

## 4. Protected transitions and ordinary overlap

For the eventual approved producer table, let `R(f)` cover the complete retained graph and its holding control, and `W(f)` cover simultaneous producer/service construction not already in that graph. These are derived functions of approved shapes, not selected allowances.

For one replacement, the minimum fault transition is:

```text
old latest graph + new complete graph + new construction workspace
= R(old) + R(new) + W(new)
```

With concurrent accepted operations, add **all outstanding preclaims**, not just the largest pair. Service and exact source-account admission reserve those claims before accepting each operation; the existing byte ceilings may refuse another operation before work. Serial publication under the service gate does not eliminate graphs/claims already owned outside it.

Use the existing retention-node reference mechanism with explicit credit classification:

1. Keep the old latest-fault interest and its charge while preparing the complete new record. Ordinary eviction cannot spend this claim. Failed preparation leaves the previous fault and every prior observation unchanged; it does not turn a failed operation into success.
2. Install the complete new immutable Diagnostic as latest fault independently of ordinary-ring admission. A full ring may lose its ordinary copy, with one applicable dropped occurrence, but not the new latest-fault/result. `TryGetLatestFault` must not return false because ordinary capacity was exhausted.
3. If the old fault still has a ring interest, either transfer its **same live payload claim** into funded ordinary retention or evict that ordinary interest under the normal loss rules. Do not release/reuse old protected credit while an unfunded ring interest still retains the graph.
4. Ring/latest aliases to one node have one payload charge, not two physical graphs. Old and new nodes/graphs genuinely coexist; distinct source accounts keep their respective charges. Clear the last engine reference before returning its credit.
5. A previously returned latest Diagnostic or snapshot remains readable unchanged after replacement/eviction/Close. It contains no account/service capability. Its caller lifetime does not hold a service claim; another engine-owned result retaining that graph needs its own payer.

The preclaimed full-ring correction, post-compaction coalescing-index reacquisition, and payload-reference clearing already present in Core remain in place. Do not use this work to reopen those fixes or the public defensive-copy boundary.

## 5. Selected funding route

### Source envelope and composition

Prepay **one exact `E_i = MaximumDiagnosticBytes_i` source envelope** from the source World's existing shared-state/domain credits at fresh **and restore** admission. This is conservative reservation of an existing sublimit, not preallocation of that many bytes, a raised limit, or another quota.

Use an existing Entities-owned `WorldDomainOperationCredit` for the independently retiring envelope, issued before capability/account and any newly owned standalone-service construction. Its control, Lock, retained-use objects and any new private holder bookkeeping must be included in the derived envelope/control charge before they are allocated. Its mere existence is not proof that today's charge covers them. Reserve ordinary World construction separately, without leaving the same diagnostics bytes in both initial grants. Unwind both grants on refusal; transfer ownership through `WorldConstructionPlan`.

While the World is active, include the prepaid envelope exactly once in `GetOtherWorldOwnedStateBytes`/owned-state headroom. It must remain in the shared-state equation even though its independently retiring domain grant is separate:

```text
other live/preparing World state + E_i + other independent-use credits
    <= exact MaximumOwnedStateBytes_i

domain registry + all outstanding World/operation/source grants
    <= exact domain.MaximumBytes
```

For attribution across services:

```text
source account/control ownership
  + standalone-owned fixed/unattributed service obligations, when applicable
  + all-service attributed live + preparing + unused protected claims
    <= E_i
```

The standalone baseline includes its owned service/ring, polling reserve, service-global counter obligations and other non-attributed protected storage. Subtract that baseline from spendable source-account headroom **before any service uses the account**. Merely limiting its own service to `M_i - 128` while another service can spend `M_i - 128` through the same account does not compose. Hosted accounts do not each acquire the shared host's backing charge.

Restore currently creates a fresh account through `CreateBorrowedWorldCapability` on the target's service. Therefore account generation, physical service owner and service close authority are separate facts: preserve the old service's funded fixed ownership until actual release or an authorized construction-plan transfer. Do not copy its backing charge into each successor account or return its funding merely because the old World retires. This accounting carrier grants no new service-close authority; a restore observation requiring an unproved ownership transfer remains blocked.

Within the owned standalone service, attributed payload claims are the same claims counted by the source account; do not add them again as a second standalone physical payload. Reservations transfer between unused/preparing/live states. Host service accounting is the physical storage constraint; the source envelope is its World/domain funding and attribution constraint. Do not add a payload again on top of a prepaid envelope when totaling domain grants.

For each service, checked accounting remains:

```text
fixed service/ring backing
  + polling unused/preparing
  + ordinary live/preparing
  + fault live/preparing/unused
  + counter live/preparing/unused
    <= existing service ceiling
```

No term may be inferred from traffic, “spare” bytes in 4096/256, or a second full implementation. Derive complete old/new peaks and each control cohort from the matched allocation profile. The fresh account has an explicit initial claim today; restore lacks the fresh `diagnosticBytes` term. Correct restore explicitly rather than calling its `RestoreContextControlBytes` free space. Prove the whole fresh/control cohort before assigning capability bytes to it.

The host service's own fixed/protected construction is admitted by its host owner before Renderer startup, not funded retrospectively by the first World. Keep that same service across Renderer recovery; stop/drain accepted old-device publication before replacement, and join all producers/Worlds before host-service Close. `Renderer.CreateHeadless` stays service-independent. Service-only host/input formatting and supplied-result retention need actual existing host/operation claims before construction/retention. For a real World source, propagate its exact account/funding use through the owning producer path; do not infer it from a visible WorldGuid or create a general emitter. This envelope batch does not by itself close every existing host producer's preconstruction ledger.

### Escaping uses and final return without a reverse dependency

Keep all domain/operation-credit ownership in Runtime. Core's account continues to contain only exact identity and bounded scalar accounting: **no World/domain/service pointer, Runtime interface, callback, closure, binding authority or GUID lookup**.

The Runtime side needs a private, pre-funded retained-use holder alongside the existing account reference:

- World construction owns the initial use. An extracted `RenderSnapshot` acquires its own use before publication; its explicit Dispose releases it while preserving copied values.
- `SubmitAsync` acquires an independent use under the snapshot's existing retain/dispose synchronization, before the snapshot can release its use. Keep it through awaits, positive-row publication, rejection, cancellation unwind and actual cleanup, including retained/quarantined work.
- Each participating Runtime service owner registers an exact-account funding attachment **before its first retain/preclaim**. Charge its metadata before allocation. The attachment survives producer/World close while that account can remain retained in Core; a raw managed account reference is not this receipt.
- After every internal service mutation/preclaim return/Close that can refund an account, its Runtime owner reconciles its bounded, funded attachments. Reconcile after leaving Core gates; use exact account references and scalar balances, not GUID lookup or Core callbacks. Include all World capability writes, host/input forwarding and Renderer publication, not just the positive-row path.
- Release the envelope only after World production has ended, all snapshot/submission/other producer uses have ended, no new use can be acquired, every service preclaim/retained/protected account balance is zero, and standalone-owned storage has ended. The “no new use” state makes observing zero stable; a zero sampled while a snapshot can still submit is insufficient.

Keep standing escrow/attachment metadata in a separately accounted source-control baseline, not in the service-payload balance whose zero triggers final return. Reserve baseline growth before creating an attachment, with the same exact source limit. After producer closure and zero external payload/preparation/protected uses, clear/detach the remaining holders and return that baseline and envelope exactly once. Otherwise a holder waiting for its own debit to become zero would pin its funding forever. Independently allocated retained-use leases also need positive metadata claims before acquisition; a zero workspace argument does not make `WorldDomainOperationCredit.AcquireRetainedUse` allocation-free.

The existing domain operation-credit release path can complete retirement after domain closure. A final service eviction/Close must drive reconciliation; do not wait for GC, another poll, a timer, or a future domain admission to return credit. The attachment collection is not an unbounded global registry: every entry/use consumes its own already funded metadata under existing limits and is removed/cleared on final return. No new thread, queue, public lease, or independent World-access authority is required.

This keeps a real diagnostic funding claim alive without unnecessarily keeping the old World binding/history owner alive. Domain retirement may remain pending while actual hosted-service records or accepted submissions retain the claim. World disposal must not synchronously wait for a borrowed host service to close. Source closure never cancels already accepted publication.

The independent World fault, subscriber report/exception, and other operation results are **not** made free by service refusal or caller handoff. Their owning operation/World claims must cover construction and engine retention separately. The source envelope is not proof that arbitrary exception/result graphs fit; their unproved observations remain outside qualification.

### Shared clock and other statics

The [clock-owner evidence correction](m05-diagnostics-clock-owner-correction.md) supports a **40-byte Core-owned `ClockState` box on its identified x64 CoreCLR lane**, distinct from the 24-byte BCL provider and from static cells/loader bookkeeping. Struct syntax or lack of IL `box` does not remove this storage. Its lifetime follows the loaded clock type, not a particular service.

Preserve Chapter 03's once-captured provider/frequency/origin and shared nondecreasing 100-ns domain across close/reopen. Do not reset per service, charge it once per World, claim that BCL ownership of `TimeProvider.System` pays Core's box, or keep an unrelated World domain alive forever.

Before profile selection, identify an **existing common process/control reservation**, its complete cohort, the claim before clock initialization, and ownership through the interval with no live services (and the applicable loader lifetime). Service `Close` currently makes no such transfer. The 256-byte live service term cannot establish that lifetime by arithmetic slack. If this trace cannot be supplied, the smallest additional owner disposition is the private persistent-storage/funding choice under existing ceilings; no clock allowance is selected here. The funding batch below can proceed without asserting this independent subcase closed.

Apply the same distinction to literals and runtime-global formatting caches: shared BCL infrastructure, engine-owned static roots, producer construction and service-retained string uses are different ownership facts. Neither charge every runtime singleton per record nor silently exempt Core-specific roots.

## 6. Concrete counter route proposed for owner disposition

Select **one immutable terminal `DiagnosticCounterSaturated` record per affected counter instance**, not a mutable public record or a two-global mask that omits `OccurrenceCount`. Use the existing code with proposed `Error` severity so it neither Warning-coalesces nor replaces a Critical latest fault. No new counter identity is issued:

| Record scope | Proposed complete identifying fields |
|---|---|
| Service `DroppedCount` / `CoalescedCount` | Operation `DiagnosticsService`; fixed message `"A diagnostic counter reached long.MaxValue."`; context `Counter` equal to the exact existing counter name, then `Value=9223372036854775807`; World/tick fields null. |
| Aggregate `OccurrenceCount` | Same code/severity/operation/message and first two context entries; additionally `SourceCode`, `ExecutionSessionId`, `WorldOwnerId`, and `SourceMonotonicTimestamp` from the first aggregate's existing identity/observation. Absent operational identities use empty context values, not invented IDs. Visible WorldGuid is the aggregate's first WorldGuid; Tick is null. |

These are counter observations, not summaries replacing a fault, operation result, or the fields of a retained Warning.

The aggregate scope is the service's existing coalescing key/account binding and first admission; the timestamp is an observed field, not a new globally unique ID. Preserve the entire source code even when long. Its length is available at initial admission and must be charged, not truncated. Duplicate-looking reports from distinct retired aggregate instances are legal separate records in admission order.

Retain a published terminal record until service Close. This conservative choice requires no unapproved forgetting window and cannot erase a prior saturated counter when another saturates. To keep the finite ring and graph bounded:

1. Reserve byte construction/retention claims and **two of the existing ring-slot credits** for the two service counters at construction.
2. Before admitting a first coalescible Warning, reserve its potential terminal-report bytes/workspace and one additional existing ring-slot credit. Derive the report shape from that actual first input and operational identity. Refuse the ordinary warning, visibly counting a drop, if this complete prospective obligation cannot fit.
3. Before saturation, eviction of an aggregate ends its unused report claim/slot. On the first successful transition to `long.MaxValue`, convert that claim to one immutable report. The report keeps its funded slot/account charge even if the ordinary aggregate is later evicted. Subsequent saturated increments produce no more reports for that instance.
4. Under the existing service gate, enforce `ordinary live slots + published terminal slots + unused terminal slot claims <= admitted ring capacity`. Published terminal rows are not ordinary eviction candidates. Exhaustion can eventually refuse all further ordinary admissions; it must still preserve protected faults and the independent operation results.
5. Construct/publish reports with their prepaid control claims, not through recursive ordinary `Write`. Reporting does not increment its own dropped/coalesced/occurrence counters. Ordinary records evicted for genuine admissions still count their represented occurrences. Claims cover simultaneous global and aggregate saturation, not merely one event in isolation.

For each report descriptor `k`, derive `R_counter(k)` plus construction workspace from the specified literals, actual source-code length, bounded GUID/decimal formatting and the chosen profile. Prepay that complete peak at descriptor admission. There is no guessed counter allowance or separate count quota. Known global membership is two; aggregate multiplicity is admitted through the existing byte and ring limits.

Saturate on first reaching the limit, including an exact final addition. Failed coalescing retains the old aggregate, increments dropped count, and does not advance occurrence/coalesced state. Overflowing eviction adds all represented occurrences with saturation. Once terminal, numeric counters never wrap or become negative.

For current gated Core producers, publish terminal observations before releasing the mutation gate, so polling cannot observe the terminal numeric transition without its retained report. `GetSnapshot` remains copied and nondestructive; it does not flush latches by evicting records. Apply ordinary severity/World/tick filters and newest-matching selection uniformly. A short query need not include an older terminal row; an unfiltered full-capacity query can see every live terminal row in admission order. Polling does not acknowledge/reset them. `TryGetLatestFault` remains Critical-only.

Future real-time owners may only update preallocated atomic latches in their callback; their actual control owner publishes/drains with prepaid credits. Core's gated `RecordDropped` is not callback-safe. Those owners remain unqualified until the real producer/control/lifetime path exists; a synthetic latch test is not an Audio or native observation.

## 7. Deliberate observable admission changes

These options preserve full Critical/result data and immutable observations, but **do not claim unchanged ordinary admission**:

- With 8192 ring slots, two future global counter claims leave at most **8190 ordinary noncoalescible rows before any saturation**. Coalescible first rows each need their own row plus one possible terminal row: at most **4095** such aggregates fit by slots before saturation, potentially fewer by bytes. Current `ConfiguredCapacityRetainsOnlyItsAdmittedRecords`/full-ring expectations cannot be reused unchanged. Total slots never exceed the existing 8192; no capacity is rounded upward.
- For a hosted source with `MaximumDiagnosticBytes=8_388_608`, let `B` be independently established non-diagnostic setup/metadata cost. With exactly `B + 8_388_607` bytes available at its admission boundary, the selected full-envelope route refuses before capability/factory/owner issuance, even though today's account-control-only initial reservation can fit. It requires `B + 8_388_608`, not a guessed smaller envelope. Final boundary tests must include all separately funded controls and registry growth in `B`.
- A first Warning that fits as a current ordinary row but cannot also fund its terminal counter report is now dropped before service-only construction. Its operation's return value is unchanged. The public `Diagnostic` constructor still accepts the same values.
- A mandatory Critical-capable operation whose complete fault preclaim cannot coexist with the retained old fault is refused **before work**, using that operation's existing failure boundary. An already occurred failure is never relabeled a successful refusal, and full independent results are not discarded.

Chapter 28/owning operation owners must accept these exact effects before activation. In particular, if its queue-capacity promise requires 8192 ordinary admissions despite mandatory reservations, the counter-slot option needs owner revision; implementation cannot silently reinterpret that promise. This proposal selects the conservative reservation route for disposition, not an implicit design amendment.

## 8. Next batch and acceptance observations

**Implement next: exact source escrow, standalone baseline debit, and independently retained funding through actual account holders.** Own the batch in Core accounting plus Entities/Rendering/Hosting private lifetime plumbing. Keep `Diagnostic`/query/result public declarations unchanged. Do not create a general allocator, provenance pipeline, new acceptance ID, or a fabricated positive/Critical producer.

| Work | Bounded deliverable / required witness |
|---|---|
| Initial and restore funding | Use the existing domain operation-credit path for exact source envelopes before diagnostics construction. Account for all credit/lease/control backing from a derived cohort. Include the envelope once in World headroom; unwind preconstruction refusal and failed construction. Restore preserves old-plus-candidate ownership and gets its own fresh account/envelope. |
| Core composition | Incorporate the standalone fixed/global baseline into all-service account admission. Preserve source identity validation, old/new Warning peaks, preclaim transfer and last-reference clearing. No hidden Runtime reference or callback enters Core. |
| Escaped funding | Carry paid independent uses through real extraction/snapshot Dispose and submission retain/unwind/drain. Register/reconcile service attachments at every actual retaining/refunding endpoint. Final return clears Runtime account/domain links before releasing the last grant; copied Diagnostic observations hold none. |
| Scope of completion | These changes establish their executed funding behavior, not P1 or an eligible numeric profile. Do not add a zero-Critical eligible profile. Reuse unchanged layout facts only; derive new controls/arrays/peaks before selecting their charges. Keep clock and unbounded result-owner evidence explicitly open. |

Focused regressions, added to the existing Core/Runtime owner test areas:

1. **Admission:** exact full-envelope fit/one-byte-short using independently derived baseline and request peak; hosted and standalone, fresh and restore, lowest legal configured limits and checked overflow. Assert refusal before capability construction/factories/owner issuance and no leaked grant. If a legal limit cannot fit required minima, report unsupported rather than round it up.
2. **Cross-service composition:** standalone backing/global claims plus simultaneous reservations in its own and another service use one source envelope. Another World cannot be evicted to satisfy a source-only quota. Concurrent preclaim/refund and failed attachment admission cannot mint or double-return credit.
3. **Lifetime:** World closes with a live snapshot, an accepted submission, and a retained service record; dispose them in different orders and race snapshot retain/Dispose. Domain retirement stays pending for actual uses, then completes on the **final service mutation/Close**, without another domain operation or GC. Retained caller Diagnostic snapshots remain readable and do not hold the envelope.
4. **Failure/drain:** preparation failure, canceled/unwound submission, recovery Renderer replacement and host shutdown return each claim exactly once after actual use; failed cleanup remains funded. No Core-to-Runtime object/callback exists in the account graph.

After the protected-input and counter/admission dispositions, the next Core batch implements sections 4 and 6. Its focused witnesses must include:

- Two distinct **approved full Critical shapes**, ordinary slot/byte exhaustion, old latest also held by the ring, cross-account/cross-service pressure, pre-existing oversized strings, failed preparation, immutable old latest/snapshots and unchanged independent operation results. Internal test fixtures validate mechanics only until real producer observations execute.
- Every one of the three known counter scopes at `MaxValue-1`, exact reach, over-limit addition, simultaneous transitions, failed coalescing and aggregate eviction; terminal publication cannot recursively count itself or replace a Critical fault. Retain prior snapshots across each transition and across Close.
- Reserved terminal-slot/byte boundaries, full-capacity and short newest-matching queries, severity/World/inclusive-tick filtering including tickless exclusion, eventual ordinary refusal with all protected slots spent, and construction/poll replacement coexistence. Expected quantities come from the reviewed shape/profile, not solely from production charge helpers.
- Clock close/reopen continuity and the real common-payer lifetime trace; per-service slack and allocation deltas are not acceptance evidence.

Use ordinary `dotnet test` on the owning project with combined related filters, Debug before Release, following [Building](../project/Building.md); verify nonempty selected/executed cases and current TRX. No tests are run merely for this proposal. Genuine positive Renderer publication still requires its real World/Scene/extraction/native/readback path; the current Scene preparation guard remains a producer blocker. Full 32-MiB preparation/recording and 16-MiB completion ledgers, native/AOT, and public-consumer qualification remain distinct Chapter 35 gates.

## 9. Frozen review inputs

Source snapshot: **`cef69a291536591d8a32a48076f814083647773f`**, observed 2026-11-19; initial hash collection at `2026-11-19T20:24:14Z`. The Core optimization is `0be05aac7f55809ef766068a12843c01b0198d54`; the older profile remains frozen at `4069a0a706feabf4e66a3e6affafde503160b836`. The subsequently inspected clock correction is included below, not silently folded into that older proposal.

All listed tracked inputs were clean against this snapshot. No schema or protected fixture is changed or used as a new byte oracle. Unrelated dirty World-quarantine/Scene test changes and the Assets opening-lane proposal are excluded and preserved; they are not review inputs or evidence for these claims. The six TRX files are ignored local evidence. No current PE/PDB inspection or current image-layout qualification is claimed here.

Review this proposal's exact bytes together with these inputs; recheck hashes before disposition. A later unrelated HEAD does not update this scope, and a changed relevant hash requires refreezing/review.

### Contracts and preceding evidence

| Input | SHA-256 |
|---|---|
| `global.json` | `1bce01871321c2df82e887da428ce5c121a6c6541fe65017fe3d249be47e190c` |
| `docs\proposals\m05-diagnostics-allocation-profile-closure.md` | `f43381af98c8c02d587a95bf02f467b4f7f32d23f9ceaf421b69cedc54d56924` |
| `docs\proposals\m05-diagnostics-clock-owner-correction.md` | `0b781a8ac8cade7d48941719b67eb5cbe92331001c50276b354ca9782474e912` |
| `docs\design\03-shared-conventions-and-clocks.md` | `b4208d3037c8c90eed56627f63dc04acffa80ff16b5ff3ff6936cd89615c08c5` |
| `docs\design\04-capacity-and-workload-profiles.md` | `60047dd1eacf26196b0bf23f16798e2aa265fd26cd36b9a0b4df1b79135730d7` |
| `docs\design\28-diagnostics-performance-and-validation.md` | `0e3c895bee5f76118ce87d208170cb6e02e9a19de3f286b7e243b28165370d14` |
| `docs\design\29-project-setup-build-and-deployment.md` | `49598161138febbf889f2a5176e3504af8d8bb8481d2157fbcfbbd94d2b46604` |
| `docs\design\30-public-api-review-surface.md` | `d4f622d69ed58ab96118c36fbbf4e2d31e062a3cbf3ce3ba1d64309f662fe4ed` |
| `docs\design\35-implementation-roadmap.md` | `24eec6e92b7ad904d552f290ef748ffc8fce13b6b94a89ebd9f39487d4584d87` |
| `docs\project\Roadmap.md` | `fd84bbdf11fef04c15fc09489dd663864664efadebf1e294748c83e8a73b1bf8` |
| `docs\project\Building.md` | `f043a352870efb6c7d4a61998225e2508a8f4370a57e784483a41819d452075f` |

### Core and Runtime

| Input | SHA-256 |
|---|---|
| `sources\TerraFX.Core\Diagnostic.cs` | `91d9214b033f298dc0aaa90d18f866178e7e1ad31d23a846c9663f9c4e72a0a3` |
| `sources\TerraFX.Core\DiagnosticContextEntry.cs` | `c2a1e74e0ae9929638c9255cfaa8dd7235ce37509483660b740569b775bef530` |
| `sources\TerraFX.Core\DiagnosticQuery.cs` | `e8574d8da53a59f71557eab8ff101f8ee56a9cb126ac26614ce4d7f6795f6de2` |
| `sources\TerraFX.Core\DiagnosticSeverity.cs` | `e90303d70cecfd9b83ec4d9d5716697230442896d997059cf29a24eb49ae2327` |
| `sources\TerraFX.Core\DiagnosticSnapshot.cs` | `d474d194cd990ccd71c6d919ccbb75166d1e15891cfb56ca473c86a4388a28e8` |
| `sources\TerraFX.Core\DiagnosticClockDomain.cs` | `d194906d28d774df01a83dc7c554e0af82645de90fa9931d415dbd02134bd680` |
| `sources\TerraFX.Core\DiagnosticsService.cs` | `92002ce11c2a5817f0525a68b6f4074a35eeef3b53d8efd62ce8748a5f9108d1` |
| `sources\TerraFX.Core\DiagnosticAttributionReservation.cs` | `9bcc73583910112962119bb82d283e7b3a722338daf93705bac2dcf64d1b9ddf` |
| `sources\TerraFX.Core\WorldDiagnosticAttributionAccount.cs` | `75b41f64fcb6af40bfdb7d0c1203596dedd3f8826ea8aa006af9dabe81e25e5b` |
| `sources\TerraFX.Core\WorldDiagnosticsCapability.cs` | `7feb090087103d651e7674ed75d90d103bb7aa2ea039e273e7b41b67d2a2806d` |
| `sources\TerraFX.Runtime\Entities\World.cs` | `99df71a9956b22e072fdf5314056a6854880bbde83654d09e5eab4a197b92fe8` |
| `sources\TerraFX.Runtime\Entities\World.PersistenceAdmission.cs` | `1dbac6f5d3da19a6e616bdd03e8f4a9f885cf74cc0fd40d4f3e34adac5da62b4` |
| `sources\TerraFX.Runtime\Entities\World.Publications.cs` | `ec1e3c92ae6e0e3f30a8dde7aa90ce4cc11141741286eccd753185c648a6d6fe` |
| `sources\TerraFX.Runtime\Entities\World.OwnerControlApply.cs` | `50a932f1dc1657b8bac458580b28f9e8856fb39a80027da7bf38a15449157df9` |
| `sources\TerraFX.Runtime\Entities\World.Controls.cs` | `4dd4b837c256d0ff5f120ccd4f71ec1ba6a732c41a64484e1375dc1f931402ff` |
| `sources\TerraFX.Runtime\Entities\World.OwnerControlRequests.cs` | `df45b48e8a7e570616b7bf511e0e4d72cc509a54aa79a823b79310b9d08fd9bb` |
| `sources\TerraFX.Runtime\Entities\World.OwnerControlAdmission.cs` | `fd7f0b0f15181ce6d94ee9cb1903b94aed8c7ffd3c96a60a16fe459d1d892003` |
| `sources\TerraFX.Runtime\Entities\WorldAdmissionDomain.cs` | `0afbc247e5ce649edbd6f26a01f67bd291d7f23cd1529dd5cd735e89dffd70f7` |
| `sources\TerraFX.Runtime\Entities\WorldAdmissionDomain.SceneStorage.cs` | `c8595af75dcdb5b7128ab6c180a0c07c53e5d8109bbe481f96b4da3634375608` |
| `sources\TerraFX.Runtime\Entities\WorldDomainReservation.cs` | `d6cdd6105850b8076d40891b0ed1f583d9fcde6ee190444a7d570879964403dc` |
| `sources\TerraFX.Runtime\Entities\WorldDomainOperationCredit.cs` | `1f005480fe39ace70b469848f943a5a72433c00a1f9e099075dccb604b2a0002` |
| `sources\TerraFX.Runtime\Entities\WorldLifetimeControl.cs` | `5a304aaf8b2353e28f6f743d5587e82f12e979dcf0dfebf55103e075ccaf54f2` |
| `sources\TerraFX.Runtime\Entities\WorldConstructionPlan.cs` | `b2ee768a7cfd14753882452ce019ffff3df274969b3368572e3ef26ddffbfb06` |
| `sources\TerraFX.Runtime\Entities\WorldFrameReadLease.cs` | `d2516ee9b4845210bdf97ab6c52200213a18b5c70c8ac14d6350ec863f3d1af6` |
| `sources\TerraFX.Runtime\Entities\World.CpuExtraction.cs` | `eb717590a5cc31d05fa576e985f2d2e7fc20c029f561df4fcac1167b5cb96840` |
| `sources\TerraFX.Runtime\Hosting\EngineHost.cs` | `2fd506f286e9d69e1f67b144cd113c4d023edb8ad9f8bfdfd079d4db417c6fa2` |
| `sources\TerraFX.Runtime\Hosting\PlatformInputRoutingOwner.cs` | `524bc0faddc5fe75bf7536b5dd78f6adb43eba471ee05d603fabca53976e02b3` |
| `sources\TerraFX.Runtime\Rendering\RenderSnapshot.cs` | `8b75010ceef75e39ce2a5c35ced6da33ebfcac4385513772282581c68bb6fe16` |
| `sources\TerraFX.Runtime\Rendering\Renderer.CpuExtractionSupport.cs` | `93b0874fe891a5bd5ef32d7dee09605c6431dddfa78f19705f2fef84e6572259` |
| `sources\TerraFX.Runtime\Rendering\Renderer.Extract.cs` | `b74534ab4e8c9b35c46e2c70b640979e6c6b73220747c69f15dcda64264f2b2b` |
| `sources\TerraFX.Runtime\Rendering\Renderer.Submit.cs` | `3f4b232e7c777fcf458c7e17bfc8bec028716e912f68e40eea0993371441c9c3` |
| `sources\TerraFX.Runtime\Rendering\Renderer.SubmissionAdmission.cs` | `8b456af38eed738e7a02bd233ffeb83438cbd114b59ca3967ba6103a1e082d14` |
| `sources\TerraFX.Runtime\Rendering\Renderer.SkinnedNormalDiagnosticPipeline.cs` | `1eeb8c96a3f89f74f232a881ceea2ac3113f70db0a37b35c60beffbedc6159d2` |

### Tests and inspected local results

| Input | SHA-256 |
|---|---|
| `tests\TerraFX.Core.Tests\DiagnosticTests.cs` | `34d8f5d2aa6165c8b67934ddba51841fb0e4e85ba98463c855e75c6585e13322` |
| `tests\TerraFX.Core.Tests\DiagnosticsServiceTests.cs` | `2dea8ed53f8d3bf144125a0cff35f37fbc8e9a7f60baa2d86235f88999db3299` |
| `tests\TerraFX.Runtime.Tests\Entities\WorldSubscriptionTests.cs` | `bc7290c0da7efd5f590f2c3fccdefa4b0473457c60352ce5fd49c0da83b4f3ca` |
| `tests\TerraFX.Runtime.Tests\Rendering\RenderMeshC0NormalDiagnosticBudgetTests.cs` | `d613aba7ef92822e0542d55fd6d84c84a2560b159d5ae06b7e684bf9e1a0887b` |
| `tests\TerraFX.Runtime.Tests\Rendering\RenderSkinnedNormalDiagnosticPrototypeTests.cs` | `3629fc3348fca247e26a5e754e8ac6a0753f04ec3f00de5e813441ded296b6e0` |
| `artifacts\tst\Debug\m05-diagnostics-coalescing-baseline-debug.trx` | `d3e58bc5b22e821afead5a3ab4f44c5f34368744bdb2656ad0e0f8103a28e45a` |
| `artifacts\tst\Release\m05-diagnostics-coalescing-baseline-release.trx` | `641760798e35994c2f898fd9516128a2f37dc9e12e3cb0d094a2dd3ce43b85e4` |
| `artifacts\tst\Debug\m05-diagnostics-context-final-debug.trx` | `f7b52a7296910e0943c1461b35bf158dde5a9a540388e30739d53e75d37d433c` |
| `artifacts\tst\Release\m05-diagnostics-context-final-release.trx` | `a5820237ee239141ae041c14a5e62e0b0d851f4eb228e0f4c9194238c369b43f` |
| `artifacts\tst\Debug\m05-diagnostics-world-subscription-debug.trx` | `58069b0718c13d2016f750a31a360818ec447708ef06be7b4efa571b45537bb2` |
| `artifacts\tst\Release\m05-diagnostics-world-subscription-release.trx` | `ad4896b3ae603ef452da2d2c7e451745549e1fefe5ddfdbb22a22e1980d5191d` |
