# M05 terminal anchor and failure disposition

## Stop and proposed rectification

**The implementation stop condition applies.** Section 3 of the [domain-terminal authority addendum](m05-cosmetic-particle-domain-terminal-authority-addendum.md) explicitly leaves the anchor owner/slot and terminal end-or-retain operation unselected. Its section 4 code batch is conditional on that selection. Implementing any one of its alternatives would choose a new ownership disposition, not merely fill in a private helper.

Select the following **tentative lower-owner rectification**, before another implementation attempt:

- Use the **original process binding O cell** as the bounded strong anchor for a selected private-default-domain terminal obligation. Do not allocate a new U, global root list or synthetic World lifetime.
- Keep domain bytes under the **actual domain registrar's original claim**, not under the process metadata budget.
- Make the **actual process-registry caller** the enclosing invoker: it retains that original O while directly invoking the domain body outside its gates, then accepts the body's ordinary return. Its remaining process-cell/gate cleanup stays under actual process-registry backing/control funding.
- On a terminal body/publication failure, retain the original anchor/remaining charges and propagate the **same exception**. Do not retry publication or repurpose the spent World retirement permit.
- Use a separate private, explicit terminal-retention result; do not make existing `TryRetire`/`TryReleaseFresh` success silently mean “O was retained.”

This proposes a concrete choice among the earlier alternatives. It is **not permission already present in the process binding protocol**, an admitted profile or authorization to change maintained contracts. In particular, holding O/key retention longer has observable WorldGuid reuse consequences. Those consequences must be accepted for the selected lane, never applied implicitly to ordinary Worlds.

## 1. Why existing release cannot implement this choice

The inspected `WorldGuidRegistry.BindingCell` has scalar fields only. An O count currently does not retain a domain graph. The existing independent U's control reference is not a substitute: its actual process release precedes the final domain publication in the traced last-use route.

There is a second concrete boundary at `WorldGuidRegistry.TryRetire`:

```text
validate binding under the process gate
leave that gate
permit.TryConsume: Available -> Retired
TryReleaseFresh: separately validate and release the process roles
```

Consequently, a consumed permit is not an issuer-prepared terminal commit or a fallback quarantine right. `WorldGuidRetirementPermit.TryGetQuarantineGrant` requires Available; `WorldLifetimeControl.CompleteRetirement` normally clears its reservation/lineage after successful retirement. A new lower protocol cannot call these operations and then assume the same permit can anchor a later domain failure.

`WorldSystemInstanceRegistry.QuarantineLifetimeControl` instead requires an already quarantined lifetime and consumes its reserved lifetime slot. It does not accept a domain-terminal obligation. Broadening that contract is **not** the choice selected here.

## 2. Bounded anchor selection

Initial supported scope is an explicitly selected **fresh engine-private default domain** paired with one real freshly issued `WorldGuidGrant`. Public/external/hosted domains, restore/replacement, and general World use are not automatically enrolled.

The proposed representation is:

| Actual owner | Proposed state |
|---|---|
| Existing `WorldGuidRegistry.BindingCell` | One nullable `WorldAdmissionDomain` reference, one nullable escaping-terminal `Exception` reference, and one private terminal-phase value. These retain actual objects, not identities masquerading as owners. |
| Actual `WorldAdmissionDomain` | One private anchor value containing the exact registry reference and original `WorldGuidGrant`. Default means unbound; no GUID lookup or ambient TLS/AsyncLocal binding. |
| Caller-local terminal handback | A readonly ref-struct value binding exact registry/grant/domain, terminal phase and original held charge; issuer-created, nonpublishable and single-use through issuer validation. It creates no credit. |

O is already reserved by real fresh binding admission. Attaching the terminal obligation consumes no additional K/O/P/U. While it remains, that **same O** and its necessary key role remain charged/nonreusable; roles whose actual obligations ended are not recreated or falsely retained as operational consumers. The process cell pays only for its own representation. Domain control, Task, receivers and protected work remain domain charges.

The fixed 128-byte cell declaration is not evidence that these references are free. Compile the exact proposed cell/GC layout and domain declaration in the established isolated baseline/candidate workflow before live field installation. Re-derive actual shared representation and process/domain control coverage; preserve all existing ceilings.

### Construction and terminal sequence

1. Prepare the anchor against the real, original fresh-binding generation **before** selected domain construction can require terminal cleanup. No root is acquired after a fallible publisher has started.
2. The selected private construction route must bind the actual constructing domain before its fallible Task/backing construction can escape a retained obligation. No fabricated fully constructed domain or placeholder lifetime covers constructor failure. This requires a precise prospective domain-control grant and constructor binding, not another World hook.
3. Keep the actual domain charge held when backing/interests logically retire. None of the seven publishers may zero that claim first or bypass the selected obligation.
4. The actual enclosing caller accepts the direct publisher/body return, ends all covered fields/locks/caller obligations, and prepares the terminal handback.
5. Final issuer commit validates the exact retained O/domain/phase. After all domain-body work and domain-gate scopes have actually returned, end the domain claim at its own issuer as the final domain-owned access. Then clear the process anchor and end its actual O/key role through the real process issuer. The remaining process-cell/gate suffix stays process-owned; it must not access the ended domain or allocate domain-owned state. No fallible domain publication follows its debit.
6. On failure, transition to Retained without freeing that O/key or refunding the actual domain claim. This first disposition has **no automatic recovery/retry**; recovery would need separately defined authority.

The root phases must distinguish prepared construction, attached ownership, publication in flight, actual returned body, return-ready, retained failure and ended ownership. Task terminality cannot perform a phase transition that means actual return. Process/domain gate order remains the existing Entities order; no publication, allocation or callback occurs under those gates.

If prolonged O/key retention changes required WorldGuid behavior even in this selected lane, or no prospective construction grant can be issued before the required allocation, **this selection is rejected and the lane stays closed**. Do not silently substitute a new global registrar list or World-quarantine overload.

## 3. Exact failure channel proposed

All existing unselected release paths retain their current signatures, seven publication sites, exception precedence and cancellation provenance. Selected terminal handling must use the following private disposition:

| Event | Retained fact / outward channel |
|---|---|
| Invalid/default/foreign/stale/replayed anchor | Usage refusal before release mutation; use existing argument/invariant exception conventions. No new public diagnostic/refusal ID. |
| No complete finite profile or insufficient original domain/process headroom | Refuse before selected construction/registration allocation. No partially admitted terminal owner and no guessed allowance. |
| Original release/body/publication throws | Record that escaping exception by reference in the preowned anchor; retain exact remaining claims; bare rethrow. No replacement aggregate, exception wrapping or Task outcome rewriting. |
| Existing acquisition/cleanup selects a later cleanup exception | Preserve that existing precedence; the terminal machinery must not introduce another cleanup failure that masks it. Existing acquisition journals continue to retain their own earlier cause. |
| `TrySetResult` returns false | Not a supported selected execution or return acknowledgment. Prove it unreachable for valid selected use from sole-publisher ownership across the seven entries and the actual private TCS setter set; otherwise keep the profile unavailable. Legacy Boolean handling is unchanged. |
| Task became terminal before a later failure | Keep its existing outcome; retain debt and propagate the selected actual failure. Never turn readiness into ownership success or attempt `TrySetException` to undo it. |
| Retention/final-commit bookkeeping could fail after destroying its last authority | **Unsupported.** Prevalidation and a proved nonallocating/nonthrowing valid-path commit are prerequisites; no secondary fallback owner is invented. |
| Repeated legacy close is a no-op | Preserve that behavior. A no-op is not a terminal handback and cannot clear retained debt. |

The false-result rule is deliberate: the inspected CoreLib `TaskCompletionSource.TrySetResult` can spin on its false path **before returning the Boolean**, as recorded in the [terminal-return ledger](m05-cosmetic-particle-terminal-return-ledger-addendum.md#3-actual-corelib-completion-suffix). Throwing after observing false would not make that invocation nonblocking. Unique-publisher/never-already-completed proof is therefore a prerequisite, not something replaced by an `IsCompleted` check or an observer. Do not reinterpret an unproved branch as an existing capacity code from `WorldGuidRefusal`.

The no-observer synchronous invoker remains possible in principle under the [owner-aware harmless-suffix rule](m03b-scene-preparation-owner-aware-async-handback.md). This proposal does not supply the compiled/native suffix proof.

## 4. Funding and first valid batch

Retaining the whole actual original registrar claim is a conservative accounting candidate, not a proof that its protected work fits. Derive construction, partial-construction retention, Task/control/receiver storage, failure recording and final invocation costs before the selected route can allocate. Separately preadmitted control/protected capacity may use Chapter 04's existing B/aggregate headroom only through its actual issuer. Do not borrow the process cell's padding to pay domain objects or treat 4096 as spare money.

Likewise, the process-registry caller's actual control/backing claim is a surviving owner, not unlimited invocation capacity. Derive its new process-owned bookkeeping/cold/native suffix within the existing declared process profile; no undeclared allocation is authorized by the process B headroom.

The next code batch is therefore **isolated declaration/issuer preparation**, after this new anchor permission and the terminal result/channel are accepted:

1. `WorldGuidRegistry.cs`: exact proposed O-cell representation and private prepared/retained/end protocol. Keep all existing success meanings; retain precise actual counts.
2. `WorldAdmissionDomain.cs` plus the private anchor/handback declarations: exact reciprocal binding, prospective construction funding, unavailable profile, and seven-entry membership. No publisher invocation is enabled.
3. Measure those actual declarations against their exact baseline before live common fields/hooks. No constructor, fake producer, Task observer or publisher workload is a measurement subject.
4. Only when that storage/registration subset has a complete finite provision, add focused non-Scene tests through real lower registration, binding, use/release and abandonment. Do not fabricate publisher return, force private state, inject a fake completion producer or claim a skipped terminal case passed. The full terminal commit/publisher and World-storage integration remain separate gated work.

No World/pool registration/construction hooks, Scene preparation, public API or maintained-document change is part of that batch. The 10,592 World-associated sizing inputs and separate +8 registration increment remain unfunded; neither they nor normal managed tests qualify actual publisher/caller return.

**This turn stops with the proposal.** The current task's conditional stop overrides proceeding under a newly drafted disposition in the same turn. No source/tests are changed, no build/test selection is run, and the existing standing-owner execution gate remains false.

## Input boundary

The [authority addendum](m05-cosmetic-particle-domain-terminal-authority-addendum.md) remains the exact seven-site/caller ledger. Its hash is `1C3FA0C41D8EF5272DEAA0A16A575FD83B7742F3752F0F4D92D93A9980A9AF9A`; its inspected owner-source hashes remain unchanged. The additional refusal declaration inspected here, `sources\TerraFX.Runtime\Entities\WorldGuidRefusal.cs`, has SHA-256 `4850019D639A00986B83BBCEC31A624FAEDB4D1FC48C6F2FCC954D49787E3ADF`.

No operational stimulus or return qualification is inferred from this source-only disposition.
