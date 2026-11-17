# M05 cosmetic-particle admission and pending-lifetime addendum

## Status and next boundary

**Tentative blocker rectification under the requested Sol maximum-reasoning policy; not implementation, owner acceptance or qualification.** This supplements [the pool owner proposal](m05-cosmetic-particle-pool-owner-closure.md) and preserves [the component validation decision](m05-cosmetic-particle-component-validation-addendum.md).

The private emitter component/factory/State2 unit is implemented. Its exact 15-case Runtime selection passed Debug then Release without skipped cases. That establishes real component storage, scope-close validation, canonical capture and the selected component Read/restore behavior—not a live pool, pending spawn owner or particle publication.

The next coherent CPU unit is the **private, construction-bound World cosmetic owner and its admission/settlement lifecycle**, before any public facade. Another numerical helper or an empty stand-in pool would not close that boundary.

Its semantics are not wholly selected. The prior proposals leave constructor/spawn affinity and byte-refusal reporting open; “128 pending” does not say whether freezing or Task completion refunds a slot; and the receipt prose requires a diagnostic without assigning its issuer/code. The proposed 64-KiB operation envelope also lacks an allocation/return proof. This addendum selects the narrow admission policy below rather than implementing guesses.

## 1. Evidence: declarations are not the missing policy

| Existing input | Consequence |
|---|---|
| `m05-owner-contract-closure-rectification.md`, sections 1.2-1.4 and 3.1-3.2 | Support/controller/result/recording projections, count ceilings, whole-request refusal and fixed-tick behavior already exist tentatively. Their absence from production source is implementation work, not by itself a missing declaration. |
| `m05-owner-contract-closure-runtime-ports.md`, section 2 | The proposed factory inventory, fresh exact-World participant, opaque roots, stage-8 grant and callback-free stage-9 installation are specified. The component-only CPU registration must not be repurposed as an unrestricted participant hook. |
| `m05-cosmetic-particle-pool-owner-closure.md`, sections 3-5 and 8 | Affinity, byte/refusal applicability and actual operation/publisher funding remain explicit holdpoints. A count ceiling is not a storage claim. |
| Chapter 02, “Async errors and admission” | Usage errors are synchronous; valid pre-cancellation yields a canceled Task; expected capacity uses the established receipt or contextual faulted-Task channel. A valid capacity refusal is not automatically permission for a new synchronous exception. |
| `Entities\World.cs:1679-1686,2249-2278` | `AcquireGuidUse` checks lifetime/fault and attachment-callback state, but does not establish owner-idle admission. Existing World read/operation boundaries check thread, active invocation and operation state separately. |
| `Entities\WorldGuidUseControl.cs:273-321` | Release refuses active workspace, releases process and domain interests separately, and retains quarantine on failure. Task terminality cannot authorize that release or erase a partially retired debt. |
| `Scenes\PreparedScene.cs:80-82,104-106`; the existing Scene supplier-return proposal | Scene preparation remains deliberately unavailable without its qualified footprint/last-access inputs. Its producer-return policy cannot be borrowed as an already qualified particle publisher. |

Unqualified source paths above are under `sources\TerraFX.Runtime`. No live particle support/controller/participant implementation currently supplies these policies.

## 2. Entry affinity and exact binding

Select **owner-thread, idle, nonreentrant admission** for the initial controller construction/spawn path, as well as counters and recording rotation. `Async` describes the result's eventual completion, not an ordinary-thread ingress promise.

- Validate the actual World/controller lifetime, selected support, issuing thread and phase before target arguments or allocation. Null World remains a constructor argument error. Disposed, faulted, wrong-thread, busy or callback admissions retain the existing conventional channels and any actual containing invocation's existing sticky-fault rules.
- Spawn count remains `[1,131072]`. Default/stale/dead/foreign Entity arguments are usage errors; an otherwise valid live Entity lacking the selected prepared emitter binding is an expected unavailable-emitter outcome.
- Constructor/spawn admission need not invent a successful tick. Counters/rotation require an actual committed/restored pool root; before one exists they throw `InvalidOperationException`, rather than expose an invented completed tick or run a hidden Step.
- Capture the exact World construction binding, qualified target and actual prepared emitter installation/backing at successful admission. Revalidate that installation at stage 8. Removal/rebind cannot retarget the request; movement and Enabled changes do not replace birth resources. An explicit request is not silently canceled merely because automatic emission is disabled.
- Admission reads no live ECS from another thread and performs no asset I/O. It requires the real lower owner boundary and participant's prepared resource map, not `AcquireGuidUse` alone, a fabricated phase grant or a World-GUID lookup.
- Calls from stages 0-9, queries, providers and publication callbacks are not an ingress shortcut. A caller must leave the active operation and use a real owner entry. No `Task.Run`, ThreadPool substitution, implicit Host dispatcher or blocking owner wait is introduced. Hosted/public usage remains dependent on its actual supported owner route.

This selection adds no public signature. Public-facade implementation remains excluded from the current work.

## 3. Nonadmitted replies and refusal applicability

### Narrow ownership correction

After valid usage checks, pre-cancellation returns the existing canceled-Task form before admission. Likewise, an expected refusal known before acceptance may return a completed receipt Task or contextual faulted Task without first pretending an independent World operation was accepted.

**Propose that these nonadmitted replies are caller-owned scalar observations.** They may contain copied identities, counts, limits and a newly constructed conventional exception; they must contain no World/participant reference, use carrier, asset/storage lease, captured context, pending closure or retirement authority. Use a non-`async` ingress and a static scalar reply path so an early reply does not acquire a hoisted controller/World state machine.

This is an explicit proposed ownership partition, not a consequence of `Task.FromResult` and not a general CLR exemption. An allocation or consumer belonging to an accepted operation still requires its real preclaim. No reply may report an admitted request, consume its sequence/slot, or release actual ownership.

The alternative—requiring a new World use before even reporting that no World use is available—has no terminating admission rule. Changing that expected capacity failure to a synchronous throw would also conflict with Chapter 02's ordinary Task-operation channel. Do not solve either problem by inventing another reserve, uncharged publisher or success-shaped fallback.

### Existing result shape; no new diagnostic identity

For ordinary `RejectSpawn`, retain Status, the non-None Rejection, exact Requested, Spawned=0 and the declared TickIndex applicability. Select **Diagnostic=null when no actual owner-issued diagnostic exists**. This narrowly supersedes the earlier tentative prose requiring a nonnull diagnostic for every rejection; the already proposed property's type is `Diagnostic?`, so no member/enum/schema is added.

Typed reason/count/tick data are the expected refusal result, not a fabricated diagnostic. Do not reuse Mesh/component-read/World-fault codes for a pool that has not faulted. A genuine retained fault diagnostic may accompany its actual outcome; this paragraph assigns no pool-local fault code.

| Condition after valid usage/pre-cancellation checks | Selected channel |
|---|---|
| 128 accepted, unsettled requests already exist | Completed `RejectSpawn(QueueCapacity)` observation; no queue append, use, sequence or committed-counter mutation. |
| Valid Entity has no current prepared emitter binding | Completed `RejectSpawn(EmitterUnavailable)` observation with no tick; no load or substitute binding. |
| Sequence domain exhausted, or independent-use/World/domain/operation workspace cannot be admitted | Contextual faulted Task with `InvalidOperationException`, copied limiting facts and no admitted work. Do not allocate an accepted-operation TCS/pin graph first or synchronously relabel this expected capacity failure as usage misuse. |
| Stage-8 whole request exceeds live-particle or non-recording pool-storage capacity | `RejectSpawn(ParticleCapacity)` staged for that tick. Particle capacity includes its actual backing/pin/workspace limit, not just free row count. |
| Stage-8 request exceeds accepted-stimulus, referenced-recording-row or recording-byte capacity | `RejectSpawn(RecordingCapacity)` staged for that tick. Existing particles still age/expire. |
| Captured emitter installation is unavailable at application | Staged `RejectSpawn(EmitterUnavailable)`; never bind to its replacement. |
| Actual committed pool is Faulted | The previously proposed `PoolFaulted` disposition applies only to that actual state. This addendum does not create its diagnostic/failed-output policy or turn an unprepared source into that state. |
| World closes with an already accepted, unevaluated request | Its funded `WorldClosed` disposition, no tick. A new call on a disposed controller binding instead fails receiver validation. |

Preserve full requested/limiting quota facts privately during preflight; do not describe byte exhaustion as a draw-count overflow. No new public byte-reporting surface is selected here.

Only a refusal evaluated by a successfully committed tick contributes to committed RejectedRequests/RejectedParticles. Admission-only replies have no tick and do not mutate that root. No refusal consumes particle IDs, accepted-stimulus records or RNG words.

## 4. One pending credit through actual settlement

Select the inclusive 128 bound over **all accepted but not fully settled request ownership**, not just the ingress ring:

```text
queued + frozen/evaluating + staged-awaiting-marker
       + publishing/cleanup + stopped-but-unreleased = at most 128
```

Each request occupies one cell as it moves between those states; do not double-count the same request or refund it at a state transition.

1. Perform the nonallocating entry/refusal checks first.
2. Before constructing/retaining the accepted operation graph, acquire its own exact binding use and fully admitted workspace. Qualify bootstrap, task/result, pins, publication, cleanup and overlap before their allocations. W=64 KiB remains a proposed ceiling, not the derivation.
3. Keep the unescaped graph under one actual construction guard; transfer it to one anchored operation owner. Before append, revalidate the real binding/installation, quota and checked next sequence. Commit queue cell, pending credit and sequence together or none.
4. Stage 0 freezes the complete accepted prefix. **Freeing/reusing ring positions does not refund pending credits.** A request remains charged while its stage-8 decision or stage-9 result is provisional.
5. After a successful marker and revoked participant grant, the World-owned dispatcher publishes prepared completions outside gates, on the owner. `RunContinuationsAsynchronously` is required but is not a return/last-access witness.
6. Refund pending/use/workspace only after actual request consumers have ended or transferred to independently funded birth/output ownership, publication and its owned helpers have ended, and the actual lower release succeeds. A terminal Task, captured receipt, canceled observer or cleared field is insufficient.

For sequence issuance, storing the last issued positive sequence with initial zero permits checked `last+1` admission, including the final representable value. Refusal consumes none; no failed-enqueue rollback or wrap is needed. This is a separate noncanonical cursor, not the World's canonical control-request cursor.

Under the proposed W, 128 accepted operation reservations alone total `128 * (256 + 65536) = 8421376` bytes. That arithmetic includes neither standing pool/output storage nor proof of the operation's reachable allocations; it grants no extra budget. Ordinary nonadmitted scalar replies are not a hidden 129th admitted request.

### Failure, cancellation and close

Pre-admission cancellation admits nothing. After successful append, cancellation cannot withdraw the request or release any ownership; a caller may cancel a separate wait. Paused/unticked Worlds leave accepted requests pending without a timer/render-driven tick.

An encompassing failed tick faults its accepted uncommitted tasks after safe cleanup and publishes no provisional particle state. An irrevocably committed spawn cannot be retracted by later publication/delivery failure. Transfer birth pins separately before ending command ownership.

Close stops new admission, joins actual stage/producer use and drains accepted unevaluated requests through their protected terminal publication. Retained recordings/snapshots remain independent owners; close must not wait circularly for caller disposal. A failed cleanup/lower release retains the actual remaining graph and pending credit with its real owner/quarantine. Do not clear the retained root first, retry a completed release or claim both lower interests were released atomically.

The exact compiled publisher/helper/return and failed-release reporting paths still require independent evidence. The open Scene supplier-return/continuation policy is not automatically a qualified particle return boundary.

## 5. Execution decision and smallest follow-on

These are semantic selections for a candidate, **not permission to execute a partly funded World owner**.

Before the next executable private-owner batch:

- Close its actual immutable root/queue/operation graph and lower reservation/transfer/retirement issuer. Derive standing construction and old/current + candidate + retained-output + publication/cleanup coexistence from the final types and call graph. Neither 128 MiB nor W=64 KiB is an object-size estimate.
- Close the actual publication/return end facts and any caller/runtime ownership rider needed by that implementation. Do not invent a numeric margin or run an unfunded operation to discover its allowance.
- Preserve the separately tentative pool-local Faulted diagnostic and failed-output holdpoint; do not choose a new diagnostic or silently replace it with a World fault while implementing this ingress.
- Enlist only a complete real factory/participant graph before construction. A helper returning an empty owner, a no-op `PrepareOutput`, invented birth resources or an always-rejecting test producer does not close it.

Once those inputs are ready, the smallest next code unit is the **closed private preconstruction owner/ingress/settlement implementation plus real World registration/freshness/refusal/retirement tests**. Keep the public facade unimplemented. Use actual package/Scene emitter producers for nonempty stage-8/9 observations; registration tests alone cannot certify births.

No Scene preparation executes while its J/K/supplier/receiver/return gates remain open. No frame grant/snapshot may be fabricated, and no existing CPU composition is changed unconditionally.

**Independent-work disposition:** the completed component unit already supplies the available closed World-storage consumer. This analysis identifies no further useful independent production unit that can be added now without a placeholder owner or crossing the remaining funding/producer gates. Accordingly this turn changes only this addendum, adds no tests and reuses the unchanged component's verified Debug/Release evidence rather than rerunning it. No full-M03/M04 exit is imposed on future independently ready work.

## Evidence boundary

Hashes below bind the selected working-tree evidence, not owner acceptance or native/AOT qualification. Reconcile changed bytes before applying a disposition.

| Input | SHA-256 |
|---|---|
| Pool owner proposal | `888D97BA6BC9AC2F06295D725162074F85C275658CDB3D0DDF31B8DA89904A95` |
| Component validation addendum | `D77508892DB87FA53CA66646727E67AA1439AEA207FB31EA730C3A7619DC70C8` |
| Owner rectification | `67134F02B92D316524848ECB8E0C917EF868DA3D2EDA5FC581DBAA25B1825632` |
| Runtime-port proposal | `605CF1D92DC362A8B73EA9BDA3A5467757942C268712F484AF53D9A9159D53E3` |
| Chapter 02 | `40F913A50434209E09941C22625A6D0722202BAB97EBAC0E153C1EA3368E2291` |
| Chapter 04 | `A4D86F3FA373A64548435C53582BF5916E29D77F34650F9D161CCEC90E6F0245` |
| Chapter 13 | `0D7806B03A1D019215EB5C3912F10200A078F9B00A71A206FB979E93BBD18CDE` |
| Chapter 35 | `24EEC6E92B7AD904D552F290EF748FFC8FCE13B6B94A89EBD9F39487D4584D87` |
| `sources\TerraFX.Runtime\Entities\World.cs` | `692F721BEAD29E5EC258BF7B1844EC785E2D1548E9F53DFA9BDB57A22A01F09F` |
| `sources\TerraFX.Runtime\Entities\WorldGuidUseControl.cs` | `EBF8A3F7C1115538CF06379FA65AE339B783117E39F76D1DE61C46C0B5C40466` |
| `sources\TerraFX.Runtime\Scenes\PreparedScene.cs` | `B73DD4314B63084132AFC2F9325ED98EECCCE65A174F02B16F2DED7EC65F688C` |
| Private emitter component source | `3570F93343961D8618F1665655B1E36FFCBF023031EA6569D1441A38F915B776` |
| Private emitter component tests | `7B83A0100699307498FF716A5DA52452CCA8C195BEC2C628C1DE797D004D3647` |
