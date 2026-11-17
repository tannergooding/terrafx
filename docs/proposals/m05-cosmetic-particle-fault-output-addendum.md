# M05 cosmetic-particle fault and output addendum

## Status and selected owner decision

**Tentative blocker rectification under the requested Sol maximum-reasoning policy; not implementation or qualification.** This closes only the failure/output holdpoint in [the pool owner proposal](m05-cosmetic-particle-pool-owner-closure.md) and [the admission/lifetime addendum](m05-cosmetic-particle-admission-lifetime-addendum.md).

**For the first private World-bound CPU pool, a non-capacity simulation/prepublication failure follows the existing World precommit fault boundary. Do not commit a separate pool-local Faulted generation.** Expected particle/recording capacity refusals remain `RejectSpawn`; they do not fault the World.

On a fatal pool preparation failure, discard the complete candidate, retain the previous committed pool/output ownership, fault the World through its existing Step channel, and publish no tick output. A later valid `Renderer.Extract` call on that faulted World fails through its existing `InvalidOperationException` guard. An independently retained earlier snapshot remains valid under its original lifetime.

This explicitly supersedes only the **tentative** earlier policy that committed a pool-local Faulted status/diagnostic while allowing the World tick to succeed. It changes no maintained owner chapter or public API. The current component validation policy is unchanged.

## 1. Why this is contract-conformant

| Authority / inspected source | Applicable evidence |
|---|---|
| Chapter 21 sections 1 and 5 | Capacity saturation requires observable `RejectSpawn`/counter and no authoritative Physics writes. Extraction requires a coherent committed/restored World and returns no partial snapshot. It does not specify a pool-local fault-output edition. |
| Chapter 13 sections 2 and 4 | Preparation/validation failure before the marker withholds the tick publication. System/validator/query/barrier faults close the World to further ticks/saves. Previously committed snapshots may remain visible. Earlier ECS or local Physics writes are not promised rollback. |
| Chapter 30 section 7 | `Extracted` requires a snapshot; `CapacityExceeded` requires an actual capacity diagnostic and no snapshot. There is no existing “extracted except for failed particles” or pool-unavailable result to populate. Invalid/unavailable World operation state uses existing exceptions. |
| `Entities\World.Ticks.cs:178-196` | Before publication, the actual dispatcher calls `FaultWorld(..., "WORLD_STEP_FAILED", "World.Step")`, then throws `WorldStepException` with the selected exception as its cause. After publication it does not pretend the tick failed before commit. |
| `Entities\World.CpuExtraction.cs:17-52`; `Rendering\Renderer.Extract.cs:901-925` and the matching 3D entry | Real extraction acquires the World read lease before camera/draw copying. The existing lease path calls `ThrowIfFaulted`; it does not sample a faulted World's storage and manufacture a snapshot. |
| `Rendering\CosmeticParticleKernels.cs:136-210` | Spatial integration, curves and velocity sampling already throw on nonfinite/unrepresentable arithmetic. The pool must apply them to candidate state, not mutate committed state and then catch the exception as a successful update. |
| Earlier rectification section 3.2 and pool proposal section 4 | Pool-local Faulted publication was a proposed extra containment policy, not a normative Chapter 21 requirement or an implemented state. It would require additional output/counter/diagnostic semantics that the existing World fault boundary already supplies without new IDs. |

Unqualified source paths above are under `sources\TerraFX.Runtime`.

**Tradeoff:** a valid finite cosmetic input can still cause an unrepresentable calculation, and this policy then stops World progress. That is a deliberate fail-closed choice, not a claim that the earlier local-fault proposal was implemented or behaviorally equivalent. Particle saturation remains nonfatal. A future requirement to keep authoritative simulation running after numerical cosmetic failure needs its own complete, reviewed containment/output contract.

“Effects do not change authoritative Physics” continues to forbid particle-originated body/solver writes. It does not provide whole-World rollback or require an unhandled stage failure to be reported as a successful tick.

## 2. Failure classification

| Condition | First private pool policy |
|---|---|
| Expected live-particle or non-recording pool-storage capacity refusal | Whole-request `RejectSpawn(ParticleCapacity)` with the already selected counter/tick applicability; no birth IDs, RNG or accepted-stimulus entry for that request. Other valid candidate work may commit with the tick. |
| Expected recording count/byte refusal | Whole-request `RejectSpawn(RecordingCapacity)`; existing particles still advance/expire in an otherwise successful tick. |
| Expected pre-admission refusal or stale emitter at application | Preserve the admission/lifetime addendum's usage, Task or typed-receipt channel. Do not promote an expected refusal to a World fault. |
| Nonfinite/unrepresentable simulation arithmetic; checked particle-ID, generation, age or required-counter exhaustion; invalid selected source/binding; invariant/ownership failure during preparation | Fail the whole tick through the existing World precommit exception channel. Never wrap, clamp, skip the emitter, reset the RNG or turn this into capacity. |
| Complete immutable output/marker preparation cannot satisfy its required validation/reservation/ownership contract | Apply Chapter 13's existing precommit failure policy. Do not publish a partial/empty substitute or call it per-request capacity when the entire publication is unavailable. |
| Failure after the marker, including delivery or detached retirement | Preserve the committed tick, pool facts and actual outcomes. Use the existing independent delivery/cleanup/retirement handling and retain unreleased ownership. Never relabel the tick as failed-before-commit or rerun it. |

An ordinary capacity refusal whose required checked rejection counter cannot be represented is no longer an ordinary successful refusal: the counter failure takes the fatal row. A full pool must not be allowed to wrap or silently omit its required counter.

Malformed component bytes retain the component addendum's actual typed Read rejection. This policy does not reinterpret bad source bytes as an arithmetic execution fault or add another component diagnostic.

## 3. State, partial progress and lifetime

The private state flow is:

```text
last committed pool/output
        -> distinct provisional candidate
        -> successful stage-9 installation
or
        -> abort candidate + existing World fault; old committed roots retained
```

No separate pool-local Faulted output, empty-success billboard root or newly issued Faulted counter snapshot is installed by the failure branch.

1. **Keep the current graph immutable.** Preparation uses separate particle/emitter/PCG, counter, recording and output state. Sampling may advance a candidate RNG before a later arithmetic check throws; that must not advance the committed RNG.
2. **Per-request and per-tick atomicity are different.** Capacity may reject one complete request while prior valid requests remain provisional. A later fatal error discards **all** that tick's provisional particle work, including those earlier successes and prepared refusals/counters.
3. **Issue nothing before the marker.** No `Spawned` receipt, particle ID, new recording segment/output or billboard snapshot becomes externally visible from a failed tick. The operational admission sequence already consumed by an accepted command is not a particle ID and is not retroactively rolled back.
4. **Do not imply World rollback.** Earlier canonical writes, structural playback or a stage-4 local Physics commit may remain. They are not a successful whole-World checkpoint. Preserve Chapter 13's recovery boundary.
5. **Abort only new ownership.** Revoke grants in `finally`, join admitted work and attempt every independent safe candidate release. Preserve the selected original cause and existing cleanup precedence. Confirmed releases alone refund credits; failures retain their actual graph/ledger/quarantine.
6. **Retain old ownership correctly.** The previous committed pool/current-output graph remains with its actual owner until disposal/retirement. Previously returned snapshots/recordings have independent storage/assets and remain charged through actual use; none is released because a newer preparation failed.
7. **Settle accepted requests without false receipts.** Uncommitted accepted work follows the existing failed-World/Step operation channel after actual consumption/cleanup. Do not manufacture `RejectSpawn(PoolFaulted)` or `WorldClosed` to describe a numerical failed tick. Already committed outcomes remain irrevocable.

Reusing the existing World exception/diagnostic channel does **not** pre-fund the new candidate's abort/reporting/pin/operation graph. Its complete protected failure and overlap envelopes still require derivation before execution. If ownership or failure reporting cannot complete, retain the actual debt and fail closed; an unfunded fallback is not the selected policy.

Recovery must use the actual approved checkpoint/new-World recovery path and its real producers. This addendum neither implements recovery nor promises that the current Standard Branch API can operate directly on an already faulted target. There is no in-place pool restart or hidden tick. A genuinely restored replacement still starts its omitted cosmetic state empty, as already proposed.

### Previously projected local-fault members

The earlier proposed `CosmeticParticlePoolStatus.Faulted`, `ParticleSpawnRejection.PoolFaulted`, and “Fault present iff Faulted” counter rule do not define an additional runtime state in this private profile. World fault guards prevent a new current counter/admission observation from a faulted World; earlier immutable counter copies keep their original facts.

Those unimplemented public projections must be reconciled before any public facade is submitted. Do not publish dead enum alternatives, add stubs or invent a Diagnostic solely to preserve an earlier draft. This document adds no public member and does not change any implemented public enum.

## 4. Exact extraction behavior

| Input / output | Behavior |
|---|---|
| Healthy World, matching committed/restored cohort and valid options | Existing complete extraction and exact count/limit rules, including eligible particles when their real consumer exists. |
| World faulted by particle preparation, with otherwise valid registered extraction support/options | Existing `InvalidOperationException` from the World fault guard. No new read lease, camera/draw copy, asset-pin graph or snapshot is admitted. No new extraction status or diagnostic kind. |
| Current publication/root unavailable or incoherent without a valid matching read generation | Existing operational-invariant exception path, not `CapacityExceeded`, empty success, a repaired cache or an automatically unselected particle feature. |
| Independently retained snapshot from before the fault | It keeps its original World/tick/frame/assets. Normal snapshot disposal and device/target/epoch checks still apply; faulting/disposal of the World does not revoke those independent resources. |

The original Step exception carries the selected cause; the World owns its existing fault diagnostic. The pool/Renderer must not emit `WORLD_STEP_FAILED` manually, reuse a component diagnostic, or promise a new extraction exception payload. Message text remains contextual, not a new fixed contract.

Do not return the old snapshot as if a new `Extract` succeeded, retag it to the failed tick/frame, omit particles from a new ordinary snapshot, or make failure depend on camera masks/culling. A faulted World cannot be made extraction-eligible by asking for a dimension or layer that would hide the failure.

## 5. Distinguishing examples and eventual observations

**Fatal arithmetic after earlier provisional success.** Admit two real package-backed emitter requests in stable emitter/sequence order. A's finite velocity range is safe. B uses finite ordered X endpoints `-float.MaxValue` and `float.MaxValue`, with zero Y/Z and otherwise valid authored data. Those endpoints pass the existing emitter predicates; their binary32 difference is nonfinite, and the existing velocity kernel throws.

A's earlier candidate births, B's candidate RNG draws, all provisional counters/recording/output and both uncommitted spawn results are withheld. The World faults without advancing the tick/publication; no pool-local Faulted generation is committed. Old retained output remains old output. Do not change the emitter codec/schema or add a special validation clamp to avoid this example.

**Expected capacity is different.** After expiry leaves 131071 survivors, a request for two returns whole-request `RejectSpawn(ParticleCapacity)` with rejected particles +2 and no RNG/ID consumption. If the rest of the tick succeeds, the World commits normally. This is the existing saturation slice of EN096/AR08, not a numerical-fault case.

The future real World tests must distinguish these cases, observe valid-argument extraction refusal after the fatal case, preserve old output/assets through fault/close, and verify failed cleanup remains owned. Use the actual emitter/Scene/phase consumers and natural numeric stimulus, never a fake emitter, fabricated snapshot/grant or production fault-injection branch. Component-only 15/15 tests do not establish this observation.

## 6. Batch unlocked and remaining gates

This selection removes the need to design or implement a second successful World publication carrying a locally Faulted pool. It unlocks the **bounded private committed/candidate transaction and existing World-fault integration**, as part of one complete preconstruction owner/phase/retirement implementation:

- Capture the real factory/root inventory and reservations before World construction.
- Prepare actual source-bound pool/recording/results at stage 8; install only the complete prepared roots/results with stage 9.
- On fatal preparation, use the existing Step fault path and abort ledger; do not special-case the Renderer to render a partial World.
- Add real World registration/freshness/disposal and transaction/fault/extraction tests at their legal producer boundaries. No public controller/status API is added.

**Still blocked:** concrete standing/old-new/retained-output/operation/publisher/failure footprints and their exact reservation/transfer/return facts; the actual coherent cohort and prepared-emitter resource producer; Scene J/K, supplier/receiver and final-return gates. Compile/inspect a complete private candidate only within those boundaries; do not execute an unqualified owner, add a placeholder participant or run Scene preparation while its gate is open.

GPU output, native/AOT/public-consumer qualification, actual Physics noninterference and milestone acceptance remain separate. Only this proposal is added; no source/tests, maintained chapters or roadmap are edited and no tests/Scene preparation run in this turn.

## Evidence boundary

These are the inspected working-tree inputs, including dirty bytes; hashes are not an acceptance result. Reconcile changes before using this disposition.

| Input | SHA-256 |
|---|---|
| Chapter 21 | `989A6183186592F165FA9614CC19A8FED63826E2A51170CE55FBFFFCA8116ED2` |
| Chapter 13 | `0D7806B03A1D019215EB5C3912F10200A078F9B00A71A206FB979E93BBD18CDE` |
| Chapter 30 | `15848C82F3858A77A87A0D3EA6FBCC07046EB6E77CA5B21748C72B90EFAB598B` |
| Pool owner proposal | `888D97BA6BC9AC2F06295D725162074F85C275658CDB3D0DDF31B8DA89904A95` |
| Admission/lifetime addendum | `762F1E9643AC9EECDF7487AED28727262DF512DA7F51C04E3E194C239A8FC9BE` |
| Owner rectification | `67134F02B92D316524848ECB8E0C917EF868DA3D2EDA5FC581DBAA25B1825632` |
| `sources\TerraFX.Runtime\Entities\World.Ticks.cs` | `5DAAC62FA95D6E2FC665576C5DCFC6D1CDFCFAE0F345E60A5F06098FDBCF7F05` |
| `sources\TerraFX.Runtime\Entities\World.CpuExtraction.cs` | `971EE49B1CCD9C3C629E3CA33198F26BE610FE3D28B4236E371C220201D768FC` |
| `sources\TerraFX.Runtime\Entities\World.cs` | `692F721BEAD29E5EC258BF7B1844EC785E2D1548E9F53DFA9BDB57A22A01F09F` |
| `sources\TerraFX.Runtime\Rendering\Renderer.Extract.cs` | `274298BFDCEE321141D4138D417807951FCC0EF6BCAC1DFCE2726D5F36829F2D` |
| `sources\TerraFX.Runtime\Rendering\CosmeticParticleKernels.cs` | `2921F9D6FE5CF81C110B0A60A04F0AD6361ECAE22BC86F99263B6320E0B2D09E` |
| `sources\TerraFX.Runtime\Rendering\CosmeticParticleEmitterAssetCodec.cs` | `51DF241C9FA7C41684BD15ECAF45B3B4AF139ACFEE2A5C9D4327FCB61B9D7730` |
