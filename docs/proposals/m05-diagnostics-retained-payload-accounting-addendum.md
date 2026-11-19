# M05 Diagnostics retained-payload accounting addendum

## 1. Decision

**Tentatively approved as implementation guidance per user direction; not a finalized owner contract or byte-profile qualification result.**

Add one checked Core admission ledger covering complete retained payloads and construction/replacement peaks, using the existing service gate and the [route proposal](m05-skinned-normal-diagnostic-telemetry-route-closure.md)'s exact source-World account. Apply it to every service admission, not only Rendering. Keep positive-only, noncoalescing `DegenerateSkinnedNormal` and existing ordinary warning keys/first-payload semantics.

The existing contracts already require full retained-byte limits; the implementation gap does not justify redefining them as ring-slot limits. **Small Chapter 04/28/29 clarifications are needed** for preconstruction reservations, replacement coexistence, and cross-service attribution, as specified below. Chapter 30 needs no change. Add no public API, diagnostic ID, separately spendable quota, universal string-length limit, or silent truncation.

This is the bounded follow-up to the route decision. The older [retained-byte assessment](m05-skinned-normal-diagnostic-retained-byte-admission.md) remains unchanged; its allocation observations are not retained-size bounds, and its earlier skinned-normal coalescing recommendations are not revived.

## 2. Evidence

| Current input | Finding |
|---|---|
| `DiagnosticsService.GetOwnedByteReservation` / `GetMaximumRecordCount` | Count only service/control and preallocated ring backing. A slot-derived reservation leaves referenced payloads uncharged. |
| `Diagnostic` constructor | Copies context entries but retains their strings. String lengths/context count are not service-byte-limited; constructor validity is not permission to retain a record. |
| `AppendOccurrenceCount`, `CopyDiagnostic`, `UpdateOccurrenceCount` | Allocate immutable replacement objects/context backing. The current constructor copies context again; the old aggregate remains live during replacement. |
| `GetSnapshot`, `DiagnosticSnapshot` constructor, `Close` | Polling currently constructs two arrays of references. Earlier snapshots retain old immutable payloads. Close detaches the ring before clearing its record references; detachment alone is not release. |
| `World.cs` diagnostic admission; `WorldDiagnosticsCapability`; `World.Publications.cs` | World admission funds slot-derived backing, or zero service backing for a hosted World. The capability has no payload ledger. The subscription-capacity producer constructs a Diagnostic before calling Write. |
| Chapters 04/28/29 | Require the fixed 8-MiB host ceiling, exact per-World sublimits, immutable copied observations, visible ordinary loss, and Core-owned service/clock with explicit higher-owner bindings. |

These are source observations, not results from running tests.

## 3. Complete charges, not character counts alone

Maintain checked, nonnegative internal accounting:

```text
service backing + live payload charges + preparing charges
    + unused protected reservations <= existing service limit

source account live + source account preparing
    <= that operational World's configured MaximumDiagnosticBytes

retained ring count <= 8192
```

The host limit remains **8,388,608 bytes**. A standalone service uses its existing admitted World limit. Attribution remains within existing World/shared-state funding, not additional headroom.

For each retained payload, store its immutable reserved charge and source-account reference alongside private service-root bookkeeping. Charge:

- The complete Diagnostic object and private accounting/control storage.
- Actual allocated context-array capacity and managed element stride.
- Every retained Code, Operation, Message, optional CommandIdentity, context-key and context-value string, including object headers, terminator and alignment.
- Service/ring/control backing, including the revised entry layout, plus every temporary builder/copy/formatting backing used before transfer.

Use owner-reviewed conservative allocation-profile bounds for the selected runtime/architecture; checked sizing must reject an unknown profile or overflowing/impossible shape before allocation. `Unsafe.SizeOf<Diagnostic>()` measures a reference, and allocation-traffic deltas are not escaping object sizes. No numeric CLR-layout qualification is claimed here.

Charge aliases conservatively per payload rather than adding a global string/object identity table. Report such charges as reserved upper bounds, not duplicated physical allocations. A shared host service has one physical-owner ledger; a World's attribution is a quota debit against those bytes, not a second physical allocation.

Keep the independently required fault and counter-saturation roots, their mandatory replacement space, and bounded polling-construction space inside the same ceiling. Ordinary payloads cannot consume their protected headroom. Transfer protected reservations into preparing/live ownership when used; do not add the same reservation twice. Independence from the ordinary ring is not a byte exemption or a promise to retain arbitrary-length critical payloads. Mandatory producers require their finite owner-defined representations, not an unrestricted exception-text escape hatch.

Treat the existing reservation helper as **initial backing only**. Derive backing capacity/headroom together under the existing limits; do not allocate 8192 slots merely because the slots alone fit, or allocate an assumed 8-MiB arena.

### Exact source account

Reuse the route's non-emitting Core account, issued/funded by the admitted World-construction path and bound once to the real execution-session/World-owner generation. WorldGuid is semantic metadata, not a quota key or account issuer.

The same account follows snapshots/submission interests and service entries across hosts, including a standalone-World snapshot submitted through a native host. Its live/preparing totals aggregate across services atomically. World replacement, closure, or a matching semantic GUID never resets outstanding debits. Missing/mismatched attribution is rejected before construction, not converted into host-only telemetry.

Account/control and retained funding must remain covered by their existing source-owner credits until actual last engine use; no World callback, GUID lookup, new World-use authority, or M03-B receiver allowance is introduced. Account refunds require no second service gate or cross-service eviction.

## 4. Admission before service-owned construction

Recommend a synchronous, checked internal construction/admission path:

1. **Validate and size borrowed inputs without allocating.** Use closed producer fields, existing immutable string lengths, and bounded formatting descriptors. Compute products and context capacity first; stop when the shape cannot fit. Known formats provide bounds, such as 32 Guid characters or at most 19 positive-long digits. Do not call arbitrary `ToString`, exception getters, user formatters, or callbacks while sizing.
2. **Enter the existing service gate and select the actual path.** For a coalescible warning, find its retained target using the unchanged operational key. Preserve argument/invariant validation; avoid formatting an incoming payload that the first-payload coalescer will not retain. Replacement sizing uses the **old retained payload**, not a smaller incoming message/context.
3. **Reserve the whole simultaneous peak.** Keep the old payload charged. Claim the new retained graph plus all temporary construction storage in the service ledger and, where attributable, the exact source account. Use subtraction-based limit checks before addition. Account admission uses atomic debit, not another service lock; failure rolls back the service claim.
4. **Construct only after both claims succeed.** Core performs only bounded managed construction inside this serialized scope: no await, native wait, World lookup, or external callback. This requires no new producer queue or concurrent-candidate allowance. Build final immutable context once through a private construction path where possible; otherwise explicitly fund the current additional context copies. Public constructor behavior is unchanged.
5. **Commit without allocating.** Install private retention metadata, transfer preparing claims to live ownership, and update successful occurrence/coalescing counters. End temporary/old engine uses and clear their references before refunding. A failed candidate leaves the old aggregate unchanged, unwinds only its own claims, and counts the refused ordinary occurrence once. Argument/invariant failures are not disguised as byte refusal.

Service-only producers, including the current subscription warning, must stop using “construct Diagnostic, then hope Write can afford it.” All existing Write/forwarding paths still pass central payload admission; an unmetered overload is not a valid compatibility path.

An operation may already need a Diagnostic for its independent result. Its construction must already be funded by that operation's owner. Core can size that immutable value without copying and acquire its **independent retention claim before retaining it**. Core's later claim does not retroactively fund the producer or authorize an unbounded formatting allocation.

## 5. Eviction, replacement, and refund

| Path | Required behavior |
|---|---|
| New ordinary record | Reject a shape that cannot fit individually before futile eviction/construction. Otherwise evict eligible oldest local ring roots for slot/service-byte pressure. For World-quota pressure, only local roots for that same account can help; never evict another service or unrelated Worlds solely to free that account. Preserve survivor order. |
| Ordinary warning coalescing | Preserve the target's first payload, timestamp and admission position. Reserve old + complete replacement + temporary peak in both applicable ledgers; net post-swap growth is not the peak. Protect the target while constructing. If it cannot fit, keep the old aggregate, increment DroppedCount once, and do not increment CoalescedCount. On commit append/update only service-owned OccurrenceCount and increment CoalescedCount. |
| `DegenerateSkinnedNormal` | Never coalesce. Each admitted positive row has service-owned OccurrenceCount=1. A verified zero creates no telemetry record, payload reservation, or loss. A refused positive publication adds one dropped occurrence; it does not fail an otherwise valid submission. The route's identities and publication join are unchanged. |
| Eviction or service close | Refund the stored charge exactly once after the last service-owned root/temporary consumer ends. A payload still held by the independent latest-fault root remains charged after ring eviction. Drain construction/publication before close; do not refund when merely detaching a still-populated array. |

Evicting a warning aggregate increments DroppedCount by its full represented OccurrenceCount; refusing an incoming occurrence increments it by one. Already performed evictions remain observable if later construction fails. Observational counters retain their existing saturation rules; accounting balances must never saturate, wrap or become negative.

**Illustrative charge example, not measured object sizes or a new quota:** an aggregate is at occurrence 9, free headroom is 1024 bytes, and its replacement plus temporary work requires 1536 + 256 bytes. Refuse the incoming occurrence even if the final replacement would have no net growth. Keep occurrence 9 and its prior snapshots, add one drop, and leave CoalescedCount unchanged. With the full 1792-byte peak available, commit occurrence 10 before retiring/refunding the old graph.

### Copied snapshots are not service roots

Keep copied polling semantics and stable admission order. Reserve bounded polling-construction workspace before allocating, including both current arrays unless the redundant copy is removed internally. The handed-off immutable array and its Diagnostic references are caller-owned, contain no account/control/service capability, and remain readable after replacement, eviction and shutdown.

An old caller snapshot does not keep a service/account debit alive; refund follows the service's last root, not GC. Conversely, another **engine** owner retaining a Diagnostic for a fault/result remains responsible under its own existing budget—it is not automatically “caller-owned” on service eviction. This bounds admitted engine ownership, not caller retention, unreachable GC garbage, fragmentation, or process RSS.

## 6. Minimum owner amendments and implementation boundary

| Chapter | Required clarification, with existing limits unchanged |
|---|---|
| 04 | Complete rooted payload/control storage is covered; reserve construction/replacement peaks before allocation under the same ceiling. Attribution follows the exact operational account across services; copied caller observations do not prolong its service debit. |
| 28 | Specify preconstruction admission, immutable replacement peak and refusal, protected target/fault roots, actual-release refunds, and one visible drop per refused ordinary occurrence. Preserve ordinary warning keys/first payloads, aggregate eviction counts, copied polling and the route's noncoalescing exception. |
| 29 | Core owns the private ledger/construction path/account. Admitted source owners supply exact funded attribution; all producers use central admission. Host/Renderer drain precedes service closure. This neither broadens the World capability nor adds reverse dependencies. |

This is a concrete internal implementation recommendation, not another public carrier decision. Later focused checks should exercise exact byte boundaries/overflow, cross-service account pressure, old/new replacement failure, aggregate eviction/refund, snapshot independence, and close races on the final implementation. Actual allocation-profile bounds and native/AOT/real-consumer qualification remain distinct evidence; no build, test, allocation probe, or native run was performed for this addendum.

## 7. Frozen review inputs

Review this addendum's exact bytes with the following clean blobs at **`64c1f7326581e77d902a6b52f92157c4c6633243`**, inspected on 2026-11-19. Recheck the named inputs before disposition; changed relevant bytes require a new freeze. No dirty implementation, schema, protected fixture, or numeric allocation profile is accepted by this document.

- `docs\design\04-capacity-and-workload-profiles.md`
- `docs\design\28-diagnostics-performance-and-validation.md`
- `docs\design\29-project-setup-build-and-deployment.md`
- `docs\design\30-public-api-review-surface.md`
- `docs\design\35-implementation-roadmap.md`
- `docs\project\Roadmap.md`
- `docs\proposals\m05-skinned-normal-diagnostic-telemetry-route-closure.md`
- `docs\proposals\m05-skinned-normal-diagnostic-retained-byte-admission.md`
- `sources\TerraFX.Core\Diagnostic.cs`
- `sources\TerraFX.Core\DiagnosticSnapshot.cs`
- `sources\TerraFX.Core\DiagnosticsService.cs`
- `sources\TerraFX.Core\ReentrantGate.cs`
- `sources\TerraFX.Core\WorldDiagnosticsCapability.cs`
- `sources\TerraFX.Runtime\Entities\World.cs`
- `sources\TerraFX.Runtime\Entities\World.Publications.cs`
