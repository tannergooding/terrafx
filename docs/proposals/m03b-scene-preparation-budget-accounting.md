# M03-B Scene preparation and independent-use accounting

**Status: user-authorized tentative unblocker, submitted for review; not an approved contract or implementation acceptance.** This document changes no implementation, maintained design contract, test, global limit, schema, or milestone status.

## 1. Proposed disposition

Separate the requested Scene preparation cap from the target World's **total** owned-state ceiling. Propose one explicit, fixed-default `SceneLoadOptions.MaximumPreparationBytes` setting, exact full-cap admission, and unchanged World/domain/process ceilings. Preserve separately admitted activation, funded ownership transfers, and actual-use cleanup. Do not recover default loading by silently reducing the requested cap.

The reported interest-array growth defect is a separate implementation conformance issue within this same owner closure: reserving only the final size difference underfunds coexistence. It needs no contract exception. The frozen dirty implementation already contains the full-new-array calculation, but the focused regression and retirement/failure evidence below remain required.

This revises only the recommendation in the earlier **tentative** `docs\proposals\m05-m03b-owner-contract-closure.md:69-75` that no public option is needed and the existing World-sized cap can simply be prepaid. That proposal is supporting rationale, not competing normative authority. Current milestone status remains owned by `docs\project\Roadmap.md:9-19`.

## 2. Existing facts and the contradiction

All locations refer to the frozen working-tree inputs in section 7, not just HEAD.

| Existing authority or implementation | Fact |
|---|---|
| `docs\design\04-capacity-and-workload-profiles.md:44-64` | The default shared World-state limit is 2 GiB. `MaximumOwnedStateBytes` includes identity history/indexes, entity storage, Scene provenance, resource/system state, control metadata and admitted transition peaks. Configured values are exact, not rounded upward. Count ceilings do not promise joint byte fit. |
| `docs\design\04-capacity-and-workload-profiles.md:66-93` | The qualification reservations include 128 MiB for Scenes/provenance and leave 320 MiB of shared credits after the listed subtotal. These are not measured object footprints, an independent preparation budget, or simultaneous-capacity guarantees. |
| `docs\design\16-scenes-transforms-and-cameras.md:126-128` | Preparation must acquire its independent use before allocating option copies, reading the root asset, invoking async preparation, growing builders/caches or calling Rendering. It prepays `B_op = 256 + W`; W is the whole-preparation cap selected from `SceneExpansionLimits.MaximumExpandedBytes`, identified as the target World's `MaximumOwnedStateBytes`. Insufficient capacity refuses, without a smaller fallback. Assets backing is separately charged. R/C ownership transfer must be atomic and grounded in actual ownership. |
| `docs\design\12-entities-components-and-queries.md:35-53` | An independent use owns one U and distinct `W_u`/`D_u` interests. Its control/anchor costs 256 bytes plus its finite W. Copies alias ownership. Actual cleanup, not wrapper disposal or cancellation alone, ends required credits; a use may quarantine only itself. |
| `docs\design\30-public-api-review-surface.md:3256-3278,3444-3454,3628-3629` | There is currently no public Scene byte-cap option. Preparation transfers its same use to `PreparedScene`; activation separately admits another use. Queue refusal preserves the prepared result. After admission the request owns both uses; assets/storage transfer to the live instance before operation uses end. |
| `sources\TerraFX.Runtime\Entities\WorldSchedulingLimits.cs:14,39-58` | 2,147,483,648 bytes is both the default and supported owned-state ceiling, not merely a suggested initial size. |
| `sources\TerraFX.Runtime\Entities\World.cs:1789-1819,2181-2189` | `GetWorldOwnedStateBytes()` includes existing owned storage and Scene reservations. The current headroom-setting method is named `ConfigureOwnedStateBudget`, not `RefreshMaximumAdditionalBytes`: it sets additional capacity using `MaximumOwnedStateBytes - GetWorldOwnedStateBytes()`. |
| `sources\TerraFX.Runtime\Scenes\SceneLoader.cs:101-135,214-228`; `sources\TerraFX.Runtime\Scenes\SceneExpansionLimits.cs:31-49` | Public preparation requests `_world.MaximumOwnedStateBytes`. The planner limit is subsequently taken from the use's mutable remaining `WorkspaceLimit`; the internal default expansion limit is also the full default World ceiling. |
| `sources\TerraFX.Runtime\Entities\WorldGuidUseControl.cs:11,49-55,93-142`; `sources\TerraFX.Runtime\Entities\WorldLifetimeControl.cs:94-166` | The control charges exactly `256 + workspaceLimit`. Subclaims reduce available workspace; transferring capacity reduces the use's operation charge. The lifetime issuer passes raw admission's returned cap into the control. |
| `sources\TerraFX.Runtime\Entities\WorldAdmissionDomain.cs:1899-1947` | **The frozen implementation no longer clamps W.** It checks both remaining World and domain bytes against `256 + workspaceLimit` and returns the original workspace limit on success. The prompt's clamping description must not be presented as the current frozen code. Exact refusal exposes, rather than resolves, the contract conflict. |
| `sources\TerraFX.Runtime\Scenes\SceneLoader.cs:47-84`; `sources\TerraFX.Runtime\Scenes\SceneActivationRequest.cs:13-64`; `sources\TerraFX.Runtime\Scenes\SceneWorldRegistry.cs:448-468` | Activation requests a separate use with W=0, then allocates the request/task state. Queue capacity is checked before `Admit()` detaches the prepared plan/use. Thus its independent control alone requires another 256 bytes; the request/task allocations still need their own funded coverage. |

Let M be the configured total World-state ceiling and B the already charged/reserved World-state baseline, counting every grant once. Even with no other use, exact preparation requires `B + 256 + W <= M`. Selecting `W = M` requires `B + 256 <= 0`, which cannot hold. Nonzero baseline is not the only problem: the 256-byte control alone makes full-M prepayment impossible. Concurrent preparation and activation require both controls to coexist, plus any additional structural activation peak.

This is a static accounting contradiction, not a claim that tests were run. Increasing only domain bytes does not create per-World headroom. Increasing M while continuing to choose `W = M` preserves the contradiction. Fixing W at 2 GiB and raising M instead would change the hard profile and dependent qualification; it is not a harmless local adjustment.

### Concrete compatibility consequence

The user reproduced `SceneAssetStoreTests.PreparedAssetLeaseRetainsBundleAcrossActivationUnloadAndDispose` failing in `SceneLoader.PrepareAsync` with **“The World admission domain cannot reserve an independent active-use credit.”** This is user-supplied run evidence, not an independently rerun result here; no run log or selected/executed count was supplied with it.

The existing witness is `tests\TerraFX.Runtime.Tests\Scenes\PreparedSceneAssetRetentionTests.cs:22-106`: it constructs a real texture/Atlas/Sprite Scene package, registers Scene and CPU extraction support, creates a World with default `WorldOptions`, and calls preparation with default `SceneLoadOptions` at lines 55-58. The reported failure matches the full-M request at `SceneLoader.cs:120` and exact-credit refusal at `WorldLifetimeControl.cs:116-124`. It prevents the test from reaching the retention behavior it is intended to prove.

Under the proposed contract, **keep this fixture's package, registrations, default World quota and default Scene options unchanged**. Its default request becomes exactly the proposed 64-MiB preparation cap, not the 2-GiB World ceiling. It must prepare and independently activate, preserve pixel value 17 through unload while the retained bundle lease lives, and preserve the second bundle through `PreparedScene.Dispose`; each last asset lease must then end that bundle's actual ownership. Do not repair the test by increasing World/domain limits, giving only this fixture a smaller explicit cap, removing CPU extraction, replacing its content with an empty Scene, or dropping its lifetime assertions. If a complete qualified inventory shows the proposed operation default is insufficient, revisit that explicit operation-default policy in review; do not silently shrink requests or alter global quotas. Separate constrained-quota cases should deliberately refuse before preparation work and leave existing retained leases valid.

### Additional aggregate-accounting obligation

The raw path compares a request with the reservation's remaining additional bytes, then increments domain-wide `_worldBytes`; it does not add that use's charge to `WorldDomainReservation.Bytes`. Ordinary reservation growth checks that reservation's own additional bytes (`WorldAdmissionDomain.cs:769-804,1912-1945`; `WorldDomainReservation.cs:29-37,66-75`). Therefore those checks alone do not establish the World-total invariant across multiple retained uses and concurrent World growth. A cap-selection repair must also prove common attribution/enforcement, not merely make one request fit.

For example, with 4096 bytes of remaining World headroom, two independent requests of W=2048 each individually pass the current 2304-byte comparison but together require 4608 bytes. An ample shared domain does not reject the second request on the World's behalf. Likewise, if a preparation consumes the exact remaining `256+W`, the current unchanged per-reservation comparison can still admit the separate 256-byte activation control despite no World headroom remaining. These are hypothetical boundary values, not measurements. The full-M preparation refusal currently masks this issue on the ordinary public route; selecting a usable W without repairing attribution would expose it.

## 3. Smallest safe contract revision proposed for review

### Explicit selection, not adaptive fallback

Propose the following addition to the existing `TerraFX.Scenes.SceneLoadOptions` class:

```csharp
public long MaximumPreparationBytes { get; init; } = 67_108_864;
```

- The proposed default is exactly **64 MiB**, independent of available World/domain bytes. Valid values are `[1, 2_147_483_648]` bytes, with no null/zero/automatic mode. Out-of-range values are synchronous argument errors before admission or allocating freeze. A valid value that cannot be funded is a capacity refusal, not an invalid option.
- This is a proposed reservation policy, **not an existing default, measured footprint, throughput claim, or guarantee that any named maximum-size Scene fits**. It is intentionally below the World ceiling and leaves room for other ownership when admission permits. The 64-MiB choice requires the real default-consumer evidence in section 6; the Chapter 04 reservation table does not prove it suitable.
- A caller requiring a larger envelope requests it explicitly. Lowered World limits, concurrent uses, retained results, other Worlds in the domain, or quarantines may refuse even the default. Do not apply `min(request, remaining)`, subtract current baseline to select W, retry smaller values, round up, borrow activation credits, or silently change the caller's option.
- Capture the exact scalar before allocating option copies. Add it to the internal `SceneLoadOptionsSnapshot` (`sources\TerraFX.Runtime\Scenes\SceneLoadOptionsSnapshot.cs:8-15`). For ordinary public preparation select immutable `W = MaximumPreparationBytes`, pass that exact W to `World.AcquireGuidUse(long)` / `WorldLifetimeControl.AcquireUse(long)`, and carry it into `SceneExpansionLimits.MaximumExpandedBytes`. Do not substitute the control's later remaining allowance for the selected cap.
- The cap funds the complete Scene-owned preparation/result/handoff envelope, including input copies, cache/traversal/bootstrap/task storage, old/new growth overlap, callback scratch, and cleanup, except storage already funded by another valid owner. Retained Scene storage is transferred from that funding, not an additional free allowance. Count/depth limits remain unchanged and subordinate to bytes.
- No new public budget object, Entities-to-Scenes callback, global allocator, schema field, diagnostic ID, or acceptance ID is proposed. The existing public `PrepareAsync`/`ActivateAsync` signatures remain. Chapter 30's parameterless internal-issuer descriptions must be reconciled with the actual `long workspaceLimit` signatures when promoting the change.

This is preferable to a hidden internal smaller cap: callers can state the requested envelope and understand refusal. It is preferable to incremental increases of D_u for this unblocker: those require a separately closed finite-growth/closure contract and do not preserve full-cap prepayment. Reserving only estimated output after unreserved counting is not an alternative.

### World-total and domain-total invariants

For each exact World lifetime, define `Q_W = B_W + U_W`, where:

- `B_W` includes all other shared-owned-state allocations and outstanding reservations/transition peaks, including transferred Scene R grants, counted once.
- `U_W` is the sum of still-owned independent-use operation charges, including prospective raw tickets, retained preparation/results, activation and quarantined uses. This is attribution to that exact lifetime, not a GUID lookup.

Require `Q_W <= M` at every admission, ordinary World growth, transfer, rollback and release. A transfer changes attribution, not headroom. Domain charging separately includes all of its actual owners and metadata, not just Q_W. Preserve `RegistryCharge + pending growth + DataCharge <= domain.MaximumBytes` and the existing registry metadata sublimit.

### Minimal fixed-accounting solution

Propose one additional `long` counter on the existing `WorldDomainReservation`, tentatively named `_independentUseBytes`. It owns the sum of prospective raw and issued `D_u.Bytes` attributed to that exact reservation, including controls, unspent prepaid workspace and quarantine. It is not a second physical/domain byte charge, not merely active workspace, and not a new interest/cell/collection. Current reservation storage is at `sources\TerraFX.Runtime\Entities\WorldDomainReservation.cs:8-37`. Qualify the added value field within its existing charged control footprint; if it does not fit, identify and prepay the actual control delta before allocation rather than assuming free padding.

Under the existing domain accounting gate, use the common check:

```text
additionalOwned = reservation.Bytes - reservation.InitialBytes
remainingWorld = reservation.MaximumAdditionalBytes
                 - additionalOwned - reservation.IndependentUseBytes
```

Independent admission must compare the exact `256+W` with this remaining value **and reserve that amount in the counter before releasing the gate**. `TryGrowWorldReservation` must subtract the same counter before admitting ordinary World/Scene growth. Domain metadata/byte and count checks still apply separately. No atomic increment outside the checked transaction suffices. Checked arithmetic, generation/ownership checks and invariant failure handling must prevent wraparound, negative counters or silently reset quotas.

| Transition | Per-World attribution rule |
|---|---|
| Raw admission | Validate exact lifetime/reservation ownership; add `256+W` to the counter and domain data charge atomically with the raw ticket/count/interest-position reservation. A concurrent operation or World growth now sees less remaining headroom. |
| Raw→control→issued D_u/W_u/U | Move ownership of the same charge through existing tickets/controls; do not increment the counter or `_worldBytes` again. The control's existing exact lifetime must retain/resolve this exact accounting reservation until its final release. If that association cannot be retained through retirement, owner closure is blocked; do not reconstruct it from a Guid. |
| Scratch claim/return | Move available versus active workspace inside the already funded cap. Neither counter nor domain data charge changes. |
| D_u→retained R | Decrease the counter by C while increasing the target reservation by R in one transaction. Joint headroom changes only by `R-C`; a fully funded transfer consumes no additional quota. Require the exact attributed reservation/lifetime, not merely the same domain. |
| Receiver-construction rollback | Reverse the exact R/C transfer atomically. Do not return funding to a different use or World, or increase the original cap. Ordinary later disposal of R releases R's World charge; it does not automatically replenish the use's counter/allowance. |
| Raw refusal or final successful use cleanup | Subtract only that raw ticket's or D_u's exact still-owned operation bytes, once. Prevalidate the complete bundle before ending its counter/domain/process ownership; keep the exact reservation available through this transaction. Clearing a wrapper is not release. |
| Cleanup failure, revocation, World disposal | Keep the remaining counter charge and its existing retention graph until actual cleanup/transfer. Do not reset the counter during World teardown, `ConfigureOwnedStateBudget`, domain closure, or a same-Guid replacement. The successor's exact lifetime is a different attribution target; the domain continues to count both owners. |

This adds bounded value-state accounting to an existing owner, not a per-Guid dictionary or new global quota. `MaximumAdditionalBytes` remains the World-owned headroom allowance established from baseline; it must not be increased to compensate for the counter. `GetWorldOwnedStateBytes()` is not assumed to include D_u automatically. Prove that all affected shared-state growth reaches this common admission check and preserve attribution across retained/quarantined states.

**Implementation blocker:** W selection/default activation cannot be accepted independently of this aggregate fix and its race/transfer tests. If an affected World-growth route bypasses common admission, the exact accounting reservation cannot survive independent-use retirement, or the existing control envelope cannot fund the counter, keep that owner closure open. Do not ship the smaller W as a standalone quota repair.

## 4. Admission, transfer and activation sequence

1. **Nonallocating entry checks.** Validate shape, the explicit cap and cancellation. Capture exact active lifetime/binding/domain/root generations. Do not determine W by reading assets or running counting/measurement. Preserve synchronous usage errors and the existing canceled-task entry behavior.
2. **Exact prospective funding.** Before control allocation or operation escape, reserve exactly `256 + W`, one operation-count credit, and two prospective interest positions. Check the World-total invariant and domain bytes including any required metadata-growth peak. No partial carrier is usable. Allocate the qualified control/anchor and any required backing outside admission gates; use the existing fixed tickets, with process → domain → binding ordering for joint validation. Revalidate exact authority after gates are dropped, install distinct `D_u` and `W_u`, then issue one independent U. Roll back the exact grants in reverse on refusal; installed backing remains charged through its actual lifetime.
3. **Funded preparation.** Only after complete admission may input freeze, root/package reads, builders, asynchronous frames and callbacks start. Each allocation or bounded workspace subclaim must be funded before it occurs. The selected W remains immutable; remaining allowance is a different counter. Cache hits do not mint new credits; misses must reserve pending-lease/task and full cache-resize overlap before decode/growth. Assets' decoded/backing charge stays with Assets, without charging the same backing again.
4. **Receiver-owned retained storage.** For a new R-byte `WorldSceneStorageReservation`, identify the exact already-funded C bytes/capacity it will own. Under the accounting transaction, move `D_u.Bytes -= C`, receiver-owned bytes `+= R`; domain and World-total net change is `R-C`, not `+R` followed by a refund. Keep unrelated active scratch/result/cleanup on D_u. For the initial ordinary preparation route, use fully funded `C=R` transfers from the selected envelope; do not use partial coverage as an implicit increase beyond W. Constructor failure returns the exact transfer before anyone reuses its funding. Existing transfer points are `World.cs:2021-2068` and `WorldAdmissionDomain.cs:2206-2258,2378-2438`; current workspace subclaims are `World.cs:2072-2113` and `WorldGuidUseControl.cs:93-142`.
5. **Prepared ownership.** Successful preparation transfers the same use, plan, leases and residual funded result/handoff allowance to `PreparedScene`; it does not end or reissue the preparation credit. Reserve qualified result/handoff capacity before transferring the rest to R so the result does not consume its own necessary handoff/cleanup allowance. Disposing an unadmitted result joins its actual consumers and releases resources before releasing the use. Existing ownership points are `SceneLoader.cs:159-202` and `PreparedScene.cs:22-85,100-139`.
6. **Independent activation admission.** Preparation does not guarantee activation. Before allocating a request or queueing, admit a new exact activation use: current selection is `W_a=0`, hence 256 bytes for its control. Do not first release the prepared use, reduce its selected cap, or consume its U to make this fit. Request/TCS/receipt and other activation bootstrap/handoff allocations must be preclaimed from the retained preparation/handoff allowance before allocation; World structural/control storage retains its separately admitted owners. The 256-byte control envelope does not fund these objects. If the qualified inventory cannot establish that coverage, activation remains blocked on owner closure rather than treating W_a=0 as proof.
7. **Queue/admission boundary.** Credit/byte refusal leaves the prepared result unconsumed and reusable. Entry credit refusal retains the current synchronous `InvalidOperationException` channel; queue-full refusal returns the existing Scene-specific `Rejected` receipt and releases only the unadmitted activation ownership/temporary claims. `SceneWorldRegistry.TryEnqueue` must continue to check capacity before the single atomic detach. Admission-first transfers the plan and prepared use to the request; later wrapper disposal cannot revoke them.
8. **Owner-boundary application and cleanup.** Revalidate exact binding and sequential membership, reserve complete structural/control/identity/instance transition peaks, and commit all or none. Transfer actual plan/assets/storage into `SceneInstance` before ending either operation use (`SceneActivationExecutor.cs:71-172`; `SceneActivationRequest.cs:77-141,151-211`). Refusal/cancellation before application publishes no partial Scene or identity assignment. Complete tasks only after required consumption/cleanup; unexpected admitted failures fault tasks. Failed actual release retains the exact graph and still-required charges in that use's preowned quarantine anchor, never quarantining an active binding or silently applying to a same-GUID successor.

Closure before admission refuses new work. Closure after completed admission does not refund/revoke that prepaid finite use; it authorizes neither a new activation admission nor extra workspace growth. Binding revocation still prevents application to a successor. These distinctions follow `docs\design\12-entities-components-and-queries.md:49-53,100-104` and `docs\design\27-state-serialization-replay-and-recovery.md:69-81`.

**Distinguishing accounting example, not a measured World:** choose M=2,147,483,648, baseline B=1,610,612,736 and proposed W=67,108,864. Preparation charges 67,109,120, so Q becomes 1,677,721,856. Transferring 8,388,608 fully funded bytes to R increases B by that amount and decreases D_u by the same amount; Q stays unchanged. A separately admitted W_a=0 activation raises Q to 1,677,722,112, before additional structural charges. The old W=M request refuses. No cap-sized array allocation or guaranteed activation follows from any of these reservations.

The restore path remains distinct: candidate Scene preparation consumes its admitted construction/persistence grants, not an independent use against a not-yet-active candidate (`SceneLoader.cs:137-156`; `ScenePersistenceRestorer.cs:66-142`). Do not accidentally apply the public 64-MiB default to restored Scenes or grant candidates active-binding authority. This proposal adds no wire field; native/EBIN schema versions and protected fixtures remain unchanged. Any later requirement to serialize this operational option needs a separately closed schema input/review.

## 5. Interest-array growth defect: disposition and focused witness

**Disposition: implementation conformance defect in the reported delta-only form; no relaxation of the owner contract.** Chapter 12 requires complete growth pre-admission and actual-release retention (`docs\design\12-entities-components-and-queries.md:100-102`). Chapter 04 explicitly requires complete old-plus-new peak (`docs\design\04-capacity-and-workload-profiles.md:113-151`). The earlier tentative proposal states the exact interest-array transition (`docs\proposals\m05-m03b-owner-contract-closure.md:49`).

For nonempty owned interest backing, `A(n)=64+128*n`; shared empty backing has charge zero. Let O=A(m), N=A(n), G be the existing registry charge already including O, and P_g other pending growth. Reserving only `delta=N-O` makes the allocation check `G+P_g+delta` instead of `G+P_g+N`: it undercounts **O**. Adding O to pending retirement after installation cannot retroactively admit that allocation peak.

The frozen dirty `WorldAdmissionDomain.cs:2629-2768` now uses full N for `newArrayCharge`, separate `newRegistryCharge=N-O`, and clears `oldInterests` before ending the retirement charge. Thus the reported delta-only calculation is **not still present in the frozen bytes**. This observation is not acceptance of allocation-failure, invalidation, gate/reader, membership-accounting or retirement behavior.

Required transitions:

| Transition | Required accounting |
|---|---|
| Before allocation | Keep O in G; check both registry and domain peak with full N, other pending growth, existing World/data bytes and this request's additional bytes. Ticket and pending growth receive N. |
| Install | Increase G by `N-O`, not N. Replace pending N with pending O atomically: `pending += O-N`. Count existing membership/control terms once. Copy current entries/install under the domain gate after exact ticket/backing revalidation. |
| Actual retirement | Retain O until the old backing/readers no longer belong to the operation, then clear the retention reference and end O. Generation exhaustion cannot permit ticket reuse. |
| Allocation failure / invalidation | Return only proven-unspent/released grants. A returned candidate array and old backing remain owned until actual release; preserve old entries and exact live interests. No early refund merely because installation was refused. |

### Focused nonzero-old-backing regression

Add a data-driven case to the existing `tests\TerraFX.Runtime.Tests\Entities\WorldAdmissionDomainTests.cs`, using its real internal reservation APIs, not a new test-only production switch:

1. Create a domain of **16,384** bytes. Current initialization selects one root, one interest, one reservation, a one-GUID vector and sixteen index buckets (`WorldAdmissionDomain.cs:69-83,147-189`).
2. Independently derive its initial registry charge: `G = 4096 + 320 + 192 + 320 + 80 + 1088 = 6096`. The 128/256/64-byte declared strides are at `WorldAdmissionDomain.cs:3272-3274,3328-3330,3356-3358,3372-3374`.
3. Fill the existing interest with `TryReserveWorld(residentBytes, maximumAdditionalBytes: 0, ...)`. Use rows **8175 / expected next admission true** and **8176 / expected next admission false**. Assert setup succeeds in both rows.
4. Request one more byte with `TryReserveWorld(1, maximumAdditionalBytes: 0, ...)`. This must exercise **interest-only 1→16 growth**, without a lineage/index/root growth or membership allocation. `O=192`, `N=2112`, `delta=1920`.
5. Exact peak: `6096 + 8175 + 2112 + 1 = 16384`; the short row requires 16,385 and must refuse **before new-array allocation**. The delta-only implementation incorrectly admits the short row because its check is only 16,193.
6. Verify the first reservation/bytes remain valid after refusal, the failed output is null, accepted outputs release once, and domain retirement waits for the actual outstanding reservation releases. Do not assert that releasing reservations frees installed high-water backing.

The existing `DomainGrowthRequiresTheCompleteOldAndNewPeak` at `WorldAdmissionDomainTests.cs:13-29` starts with an 8304-byte domain and empty initial backing; it does not distinguish this nonzero-old-interest-array defect. The new two-row witness must execute, not merely inherit that test's name. It establishes aggregate admission behavior; separate actual-allocation/retirement evidence is still needed before claiming every intermediate lifetime transition verified.

## 6. Review, implementation and evidence gates

These are proposed observations within existing M03 ownership/Scene-option obligations, not new acceptance IDs. Design acceptance, behavior tests, real public consumers and native/AOT qualification remain distinct (`docs\design\35-implementation-roadmap.md:23-27,56,102-122`).

| Gate | Required distinguishing evidence |
|---|---|
| Owner/declaration approval | Approve or reject the explicit property, exact 64-MiB policy and range, total-World attribution, immutable selected-cap versus remaining-counter distinction, activation bootstrap owner, and restore separation. Promote agreed wording together into Chapters 12/16/30, with Chapter 04's per-operation policy entry and consumer guidance as needed. Do not implement from an unresolved default/lifetime decision. Global ceilings stay unchanged. |
| Exact admission/no fallback | Extend `SceneSupportRegistrationTests.cs:107-237` and existing domain tests. Hold other budgets/capacities fixed; demonstrate full `256+W` at exact World and domain fit and one byte short, including nonzero baseline and metadata growth. Requests return the original W or refuse without work; a World-sized request refuses. Existing helper admission/transfer tests alone do not exercise public preparation. |
| Aggregate World enforcement | Exercise the 4096-headroom/two-2304-byte-request example: with ample domain bytes, one use succeeds and the other refuses, serially and concurrently. Race independent admission with ordinary World/Scene growth using the same fixed counter; at most the jointly fitting set succeeds. Hold a prepared use that leaves exactly 256 bytes and then 255 bytes for activation: activation succeeds/refuses respectively without releasing the prepared use to manufacture margin. Repeat retained/quarantined and revocation states. Transfers and constructor rollback conserve Q_W/domain totals; no duplicate increments on raw→issued handoff, cross-World counter transfer, underflow, refresh or teardown reset. |
| Public defaults and explicit limits | First rerun the reported `PreparedAssetLeaseRetainsBundleAcrossActivationUnloadAndDispose` unchanged as specified in section 2; verify its retention assertions execute, not just its preparation. Also run a caller-selected larger/smaller cap against real package-backed Scenes, not fabricated plans. Invalid 0/negative/hard+1 values fail before work. A too-small funded cap refuses its next required bootstrap/copy/decode/callback claim, never silently retries. Show the selected cap remains unchanged as subclaims/transfers consume remaining allowance. |
| Complete envelope inventory | Close and qualify input-copy, builder/cache resize, async/result/handoff and callback ownership before claiming the cap enforced. Current counting precedes retained-plan reservation (`ScenePrefabExpansionPlanner.cs:639-705,1191-1222`); unique-Prefab decode/cache insertion precedes the final `256*prefabCount+2048` estimate (`:1928-2024`). Rendering's measurement itself allocates/loads (`RenderSceneAssetBundle.cs:50-112`), while ordinary preparation has scratch and later retained-reservation phases (`:119-172,242-265`). Do not treat final output measurement or that allowance formula as proof of the prior peak. Exercise repeated-Prefab hits, next-unique-Prefab refusal and real package-backed Sprite/Mesh callbacks. |
| Transfer and cleanup | Exercise scratch return separately from retained R transfer, receiver-construction failure, residual builder/callback/result workspace, decode/cancel/cleanup failure and retained consumers. Same bytes have one owner continuously. Failed cleanup retains its actual graph/credits; task terminality or a Dispose call alone is not release evidence. |
| Separate activation and reuse | Extend `SceneAssetStoreTests.cs:1641-1773,2257-2317` with exact 256-byte independent activation fit/shortage while the prepared use remains live. Verify byte/use refusal and queue refusal do not detach the result or consume identities; a later independently funded attempt can succeed. Cover disposal-versus-admission, exact-binding revocation, same-GUID replacement and external-domain closure ordering. Assert request/task/handoff allocations are funded, not assumed covered by the control's 256 bytes. |
| Interest growth | Execute both literal 8175/8176 rows in section 5. Separately cover the independent-use two-interest path with one free slot, allocation failure/invalidation, installed-old backing retirement and ticket generation exhaustion. The single-interest witness is not evidence that pair issuance is atomic. |
| Managed and public-consumer validation | After an approved implementation, use the SDK from `global.json` and ordinary Runtime project tests: combine the existing `WorldAdmissionDomainTests`, `SceneSupportRegistrationTests`, `SceneAssetStoreTests` and `RenderSpriteComponentTests` filters in one Debug run, then the corresponding Release run. Confirm fresh nonzero selected/executed counts and that the new boundary rows/public witnesses actually ran. Retain TRX/logs under the existing artifacts layout; use `docs\project\Building.md:3-35`, not a separate qualification runner. Compile/run the affected public nonfriend Scene consumer. No managed result implies NativeAOT/backend or complete M03 acceptance. |

Until these dispositions are approved and the affected implementation is validated, retain exact refusal. Do not bypass the conflict by weakening quota checks, changing global defaults, skipping the independent-use path, using W=0 for preparation, or claiming the existing helper tests establish default public loading.

**Evidence produced for this proposal:** source/contract inspection and independently derived accounting examples only. No build, test, native/AOT run, commit, push or PR was performed for this document.

## 7. Frozen review inputs

Baseline HEAD: `31f156a51ccc462ae9c5cc8d4e87e00c0054dec4`; snapshot UTC: `2026-11-15T23:54:50Z`. HEAD alone does not identify this review: it includes the pre-existing dirty implementation and two untracked independent-use source files. Recheck the following SHA-256 values before applying any disposition. A changed input requires renewed review of affected conclusions. Unchanged repository command/configuration conventions are pinned by HEAD; the table additionally freezes the cited contract/implementation/test inputs and every pre-existing dirty/untracked source/test file. Hashing a preservation-only file is not a claim that its unrelated behavior was reviewed. This proposal does not alter any of these inputs.

No native/EBIN schema or protected literal fixture is a changed/proposed review input; this proposal assigns no schema identities, tags or versions.

| Input | SHA-256 |
|---|---|
| `docs\design\04-capacity-and-workload-profiles.md` | `f0dc8dee55f2c09b410874287030f675769546ee8e8828d350c83aadef241af5` |
| `docs\design\12-entities-components-and-queries.md` | `44eca223bb3767b113ff9be720503b8656476ca1eda9dbb5f2cc5678c272d3c8` |
| `docs\design\16-scenes-transforms-and-cameras.md` | `064a80720e3d07ea86f51849d5fb9b926d58f171102f722c8db75a44c863ca0b` |
| `docs\design\27-state-serialization-replay-and-recovery.md` | `b9b6324c5b1b1543ca1d2287ece579597a7e7aa62d885031c4ff1bb629f08777` |
| `docs\design\30-public-api-review-surface.md` | `fc0d39b429f0599783b3fe4c44aae1884e1e66559472d53cdbb70e1f13e1dc44` |
| `docs\design\35-implementation-roadmap.md` | `24eec6e92b7ad904d552f290ef748ffc8fce13b6b94a89ebd9f39487d4584d87` |
| `docs\project\Building.md` | `f53ab58b89eeae7e17b70970075be9322a936d443bb6d5239c3238e594deabc6` |
| `docs\project\Roadmap.md` | `c4fbe82155a6d82ffd2d13ad2f18056d938738c4fa5a66760c4877e24ed001c7` |
| `docs\proposals\m05-m03b-owner-contract-closure.md` | `fc290975441f950efd4621a0a2dcb32e71a022228c48211cdddf5f5fb6a70585` |
| `sources\TerraFX.Runtime\Animation\AnimationClipAssetBinding.cs` | `e09fca988ba58127b365d8f4d7eb0c22ca3fd17e07e8342879544fb81bbd4db1` |
| `sources\TerraFX.Runtime\Entities\World.cs` | `00442ed9b448d1e252454e8ebf85b4aa1473b383510b93a7c84d225a57f188fc` |
| `sources\TerraFX.Runtime\Entities\WorldAdmissionDomain.cs` | `71edb20065c857bbff0704d521b75a1064f051336116b07243c3ec733b3d2335` |
| `sources\TerraFX.Runtime\Entities\WorldDomainReservation.cs` | `b7fea2fe4bfb8ec569fea39d2384e00e76cc8a913aea565968df166772244ccb` |
| `sources\TerraFX.Runtime\Entities\WorldGuidRegistry.cs` | `470ec745500181a57d2e0625b3a93ee5e51a899e046c017b3dd490fdb02be739` |
| `sources\TerraFX.Runtime\Entities\WorldGuidRetirementPermit.cs` | `1849eae697239482076f7ec3a1ea02dd6b3348094dad6cb63668bf57fdb20529` |
| `sources\TerraFX.Runtime\Entities\WorldGuidUse.cs` | `a6120452989f29ec2ef9c2b90e7479cc6635b9b17848c6c683063b3f99d36d22` |
| `sources\TerraFX.Runtime\Entities\WorldGuidUseControl.cs` | `0e8832f20d6d8a3f6c9e12142b9a0f343643cedcaaf1743a281fccc8c6117b87` |
| `sources\TerraFX.Runtime\Entities\WorldLifetimeControl.cs` | `fed48718cb127463029dfe453c490fa6ce9482c62dac721e0d7240690b4e8d76` |
| `sources\TerraFX.Runtime\Entities\WorldSceneStorageReservation.cs` | `a6a487abae56a5512976cefb5d342c37b414bb4f8615b1a124692c9696c673e5` |
| `sources\TerraFX.Runtime\Entities\WorldSchedulingLimits.cs` | `497b007892c2f5e2c7b87665df15656ed0788dc810c4fb37d2213719f22ddf61` |
| `sources\TerraFX.Runtime\Rendering\Renderer.CpuExtractionSupport.cs` | `eaf1f94aff0886b3fa30af97bd7f83180a38ab744d4241d0cdd275a055eadb55` |
| `sources\TerraFX.Runtime\Rendering\RenderSceneAssetBundle.cs` | `4fd3f20576ad328bd3e9958e40ef8d475451bdcbfc5122c3ba7e599a0ea8361a` |
| `sources\TerraFX.Runtime\Scenes\ISceneExpansionResourcePreparer.cs` | `67ae936843f6329d95d12b784a4686a8b75b2cceb3932159e56bdd24f21ca423` |
| `sources\TerraFX.Runtime\Scenes\PreparedScene.cs` | `f53de75edfe25950b5e45762eb6d9be5c14a5546d77106f722e23e3c256fb7fe` |
| `sources\TerraFX.Runtime\Scenes\SceneActivationExecutor.cs` | `7d12eb728b0563ed673a86a1e1846aaf0d3fc37a07c139cadecf8f34c646ec69` |
| `sources\TerraFX.Runtime\Scenes\SceneActivationRequest.cs` | `55df1eb1ea5dab4c47b82b69509549a5efcd97da8d5cb2d0d6cd82a3566a32b5` |
| `sources\TerraFX.Runtime\Scenes\SceneExpansionLimits.cs` | `941b5a09b7cd60ea5704030ace2af2f3499ebe2843e14138182faf609acc4173` |
| `sources\TerraFX.Runtime\Scenes\SceneExpansionPlan.cs` | `8ffcde62282341b8c5e782d5262c0917002d1b48e35a5098cc9b4b360beefae9` |
| `sources\TerraFX.Runtime\Scenes\SceneLoader.cs` | `313e11e62374ce6f1fa13c2c08ef73d92110c27ca579585fde7228d67831010d` |
| `sources\TerraFX.Runtime\Scenes\SceneLoadOptions.cs` | `97417a04c1992c1fea74bc0b3ad6ee0bbc3c6e91fba7cacea4cef1b1bdf2d04d` |
| `sources\TerraFX.Runtime\Scenes\SceneLoadOptionsSnapshot.cs` | `3f7c382bb2670686f06d53f660b0b2de1a79eb285538ed3ff47fd0b623c55b1f` |
| `sources\TerraFX.Runtime\Scenes\ScenePersistenceRestorer.cs` | `698f36fb6207d09b395af7a0da85ff132dc8e7ed549e9b4e804df5d6728348c9` |
| `sources\TerraFX.Runtime\Scenes\ScenePrefabExpansionPlanner.cs` | `d8d807e1925a5cf2538a41076ca3606a7fe3f31005b452429f20c99c5612ca27` |
| `sources\TerraFX.Runtime\Scenes\SceneWorldRegistry.cs` | `667e8bf4d3091be93a206a152c9aecbb071e5e9c42d1ecc49f17f1a37c46aeff` |
| `tests\TerraFX.Runtime.Tests\Entities\WorldAdmissionDomainTests.cs` | `1f76b04a26b1ca50cada0a563d6ecd3043d2229f4005d13ddc886dd01549a5ca` |
| `tests\TerraFX.Runtime.Tests\Scenes\PreparedSceneAssetRetentionTests.cs` | `c7a95a1e6c4d90763d0d7ec11bc1a6e1e9da23154631c6ad5a976525cb721f9e` |
| `tests\TerraFX.Runtime.Tests\Scenes\RenderSpriteComponentTests.cs` | `8e65b71b5ac1e0ef9d1f47aa56963054d091df8c229e74ddbb1cae37bb59ab60` |
| `tests\TerraFX.Runtime.Tests\Scenes\SceneAssetStoreTests.cs` | `1784651e891e29d88e84508dc7c78ad105f42f85acf1ae125772f170fd22a587` |
| `tests\TerraFX.Runtime.Tests\Scenes\SceneSupportRegistrationTests.cs` | `da50865017245ea283ae52c22a2962a3a4b5e60d06c818d49dec7bee8c88d70a` |

## 8. Tentative activation-envelope disposition

### 8.1 Decision and scope

**Tentative funding decision:** each activation attempt should prepay its own finite, nonzero request/task/receipt envelope A through its separately admitted activation use: `W_a=A`, `B_activation=256+A`. Do not borrow the control's 256 bytes, reduce preparation W, or assume unused preparation capacity will fund a later attempt. This replaces section 4.6's tentative preparation-funded bootstrap suggestion; it changes no current contract or implementation.

**Not yet an implementation unblocker:** current contracts/code do not establish an exact A, a safe default A, or the completion-publication ownership transition below. No numerical charge is assigned here. In particular, neither one page nor the 64-MiB preparation default is evidence for an activation envelope. This remains a specific owner-closure blocker, not permission to ship W_a=0 or introduce a new fallback/status.

Sections 1-7 retain their original snapshot and scope. This addendum uses the later inputs in section 8.6; it does not reaccept intervening worktree changes.

### 8.2 Allocation and completion facts

- `sources\TerraFX.Runtime\Scenes\SceneLoader.cs:47-84` still acquires W_a=0 before `new SceneActivationRequest`. Its queue-refusal diagnostic is allocated before `request.Reject`, and its catch disposes an alias of the activation use. An exception after request construction therefore needs request-owned unwinding, not a second release by the entry guard.
- `SceneActivationRequest.cs:13-42,52` initializes a TCS with asynchronous continuations during construction and exposes its Task. Funding at `SceneActivationRequest.Admit()`, queue installation or result publication would be too late. `SceneWorldRegistry.cs:448-468` refuses capacity before detachment, while `:203-218,575-582` dequeues before execution: leaving the queue is not the end of request consumption.
- `SceneActivationRequest.cs:123-139` ends use ownership before constructing a rejected receipt and its diagnostic array. `:65-70,100-120` also ends uses before TCS fault/cancel/success publication. Adding a live workspace claim without changing these paths would additionally conflict with the active-workspace release guard at `WorldGuidUseControl.cs:282-287`.
- `SceneActivationReceipt.cs:12-52` validates result shape and constructs its diagnostic interchange value; it supplies no aggregate diagnostic-byte bound. Root validation builds potentially multiple diagnostics (`SceneActivationExecutor.cs:339-398`). Component refusal also passes diagnostics through `ComponentRecordRestoreResult.cs:12-61`, `SceneActivationRejectedException.cs:20-28` and `SceneActivationExecutor.cs:285-291,153-159`. Constructor shape checks do not qualify all payload/copy costs.

### 8.3 Claim owner and required transitions

A means the peak **engine-owned** request object, inline claim state, TCS/Task and deferred completion metadata, receipt/diagnostic carriers, owned payload/copy backing, delivery/cleanup bookkeeping and any required passive handoff control. Existing Scene/Assets/World structural grants keep their actual payloads; a reference from a receipt does not duplicate that payload charge.

| Phase | Tentative owner and required behavior |
|---|---|
| Before construction | Select A from a reviewed profile/bounded-input rule using nonallocating facts. Admit exactly `256+A` against the common World/domain checks while the prepared use remains live. Reserve A through the existing allocation-free `WorldGuidUseControl.TryReserveWorkspace` path before constructing the request, its TCS or Task. The control's anchor records the active claim; the synchronous entry owns its carrier until construction succeeds. No separately allocated guard/claim wrapper is needed. Its proposed inline amount/state must itself be included in the qualified request footprint. |
| Construction succeeds/fails | Transfer the same carrier/claim once to the request and root that request through the activation control's existing retained-owner reference. Before transfer, failure unwinds the entry's claim only after proving no request/task consumer escaped. After transfer, every failure uses request-owned cleanup, including diagnostic creation or enqueue failure; an entry alias cannot independently refund it. Returned/partially acquired ownership is not refunded merely because construction threw. |
| Queue refuses | The request retains A while constructing the diagnostic, receipt and terminal delivery. The prepared plan/use never detach and remain reusable. Clear the attempt's borrowed prepared/options references when consumption stops; retaining a refused attempt must not consume the prepared result. End only this attempt's activation ownership/claim through the completion handoff below. |
| Queue admits / dequeues | `Admit()` transfers the prepared plan/use exactly once; request ownership of A does not change. The queue holds a reference, not a new byte grant. Dequeue, `PreparedScene.Dispose`, observation cancellation and domain closure do not return A. Execution retains A through receipt/fault construction, actual plan/resource cleanup and delivery; structural/control allocations still require their separate admitted owners. |
| Completion publication | Assemble the complete outcome under funding, end all operation borrows/cleanup, and publish outside gates. Still-used TCS/Task/outcome bytes cannot be refunded immediately before `TrySet*`. The preferred closure is a **prepaid passive completion-handoff owner**, receiving those exact bytes by transfer before operational uses end, retaining them through the last engine producer/publication access, then transferring the immutable completed observation to the caller. It must be provisioned before request/task escape, survive queue refusal and revocation, and add no late admission after domain closure. Its exact root, control footprint, transfer/release authority and byte partition are **not defined by current Scene contracts** and remain blocked, not asserted to exist. An ordinary World Scene reservation must not be presumed suitable through World retirement. |
| Failed actual cleanup | Preserve required A, exact uses and the still-owned graph in the existing preowned failure anchor; preserve the failure outcome. Do not return active claims, quarantine a live binding, or use quarantine as the normal completion-handoff mechanism. |

An explicit handoff/linearization is necessary at the completion boundary. Chapter 02 requires actual-consumption completion, asynchronous continuations and delivery outside gates (`docs\design\02-dotnet-api-memory-and-ownership.md:82-106`); EN020 expressly requires operation-credit cleanup for its buffer/chain-sublease case (`docs\design\34-engine-acceptance-cases.md:35`). Whether a Scene's still-used completion-envelope claim is operational consumption or passive result/publication ownership is not closed by the cited Scene clauses. Do not assume metadata is exempt, that keeping operational U/D_u after `TrySet*` satisfies every cleanup gate, or that ending A first proves consumption stopped. The preferred passive handoff above, or a proved equivalent using existing actors, needs an explicit owner disposition without silently changing terminality semantics.

For a reviewed completion partition `0 <= A_completion <= A`, transfer its active funding to that owner atomically: D_u's data claim decreases by `A_completion` while passive owned bytes increase by the same amount, preserving exact World/domain attribution. Release the body partition only after its consumers stop. Do not first release the full active claim to make the existing use-release guard pass and then try to reserve the completion bytes again. No existing Scene helper is asserted to provide this active-claim/passive-owner transfer; its absence is part of the stated blocker.

After a reviewed handoff, caller retention of the completed Task/receipt must not require a new public Dispose, GC/finalizer observation or an indefinitely retained World use. Any runtime work still attributable to the engine remains funded until its actual quiescence. Caller-added continuation graphs must have an explicit ownership boundary, not an assumed unlimited engine allowance.

### 8.4 What can bound A, and what is missing

Use the peak of simultaneous unique obligations, not a sum of unrelated outcomes or production output as the sole oracle:

```text
A >= max_t(request + TCS/Task + completion/failure metadata
           + receipt/owned diagnostics and copy overlap
           + engine delivery/cleanup + passive-handoff ownership)
```

Required evidence is:

1. **Selected compiled profile:** qualify actual object headers/alignment, the final request/options fields, framework Task/TCS and deferred fault/cancel/continuation bookkeeping, and compiler-lowered collection-expression backing against the SDK selected by `global.json` and `net11.0`. Source field counts, reference-sized `Unsafe.SizeOf` results and one successful allocation measurement do not establish this bound. Qualify JIT/AOT separately wherever claimed; no SDK change is proposed.
2. **Independently bounded outcomes:** fixed queue-full output has one diagnostic; Activated has empty diagnostics. Root refusal has an input-derived upper bound `sum(max(1, requiredComponentCount(slot)))` over supplied nonnull bindings: each stale target produces one diagnostic, otherwise each missing required component can produce one. This follows the loop above and covers only root validation, not component callbacks or all failures. Include builder spare/old-new capacity and actual copy coexistence from the compiled lowering.
3. **Payload/foreign-owner closure:** `Diagnostic.cs:33-123` accepts strings and context and creates context interchange, but does not impose an aggregate Scene diagnostic byte ceiling. The component result and receipt validators likewise supply no whole-envelope ceiling. Identify admitted bounds or separately funded ownership for component diagnostics, formatted EBIN errors, original exception graphs, cancellation/fault nodes and runtime delivery bookkeeping. Preserve original exception identity and existing diagnostic content; do not make A finite by truncating required data or replacing failures with a small synthetic receipt.
4. **No double charging:** retained Assets backing, installed Scene state and existing structural/control workspace stay with their valid owners. Newly allocated carriers/copies and any previously uncharged retention are included. Every excluded graph must name its real funded owner and transfer/release boundary; catching an exception or copying an immutable reference does not establish that boundary.

**Remaining decisions:** a finite rule/default and supported profile for A, complete payload/foreign-owner bounds, and the passive completion-handoff owner/publication transition. Until these are closed together, no exact/default activation envelope is derived, and no implementation based solely on `AcquireGuidUse(A)` is accepted.

### 8.5 Exact regression cases after those decisions close

Use existing Scene fixture/provider and reservation facilities; no production test-only allocation switch. Keep unsupported failure stimuli explicitly open.

| Case | Required observations |
|---|---|
| Exact/short admission | With nonzero measured/derived baseline and all other setup/metadata already fitting, leave exactly `256+A` World bytes, then `255+A`; repeat varying only domain bytes. Exact admits the complete envelope; short refuses before request/TCS/Task/receipt construction, queue mutation or prepared detachment. Preserve existing error channels. The earlier 256/255 examples are control-only slices, not full activation-envelope witnesses. |
| Aggregate race | With room for only one `256+A` attempt, race two activations of distinct prepared results and ordinary World growth. Only the jointly fitting set succeeds. Unused prepared results remain reusable; no double debit/refund occurs on entry-to-request or completion-owner transfer. |
| Queue-full reuse | Extend `SceneAssetStoreTests.SceneLoaderRejectsOwnerQueueFullWithoutConsumingPreparedScene` (`SceneAssetStoreTests.cs:2257-2317`): one queued request, then three refused attempts against the same second result while retaining their returned Tasks/receipts. Each has one queue diagnostic and no identity consumption; after each producer/handoff quiesces its attempt charge ends despite caller-retained immutable observations. Drain and activate the original second result successfully. |
| Accepted lifetime | After enqueue, dispose the prepared wrapper and cancel a separate wait observation. Keep the original operation pending until its legal owner boundary/cleanup; A remains funded through dequeue and delivery, then ends/transfers once. Preserve the existing package/Atlas lease assertions in `PreparedSceneAssetRetentionTests.cs:22-106`. |
| Receipt multiplicity | Exercise Activated/empty, queue-full/one diagnostic, two stale nonnull root bindings/two diagnostics, and two live targets each missing two required components/four diagnostics. Include the approved maximum diagnostic/payload shape and its first refused extension without truncation. Cover component-provider refusal separately; root-only counts cannot qualify it. |
| All terminal paths | Success, boundary rejection, admitted cancellation, callback fault with original exception identity, and registry disposal must all retain funded publication metadata. Extend the existing cancellation case (`SceneAssetStoreTests.cs:2098-2144`). Exercise domain closure before admission versus after accepted enqueue: no late completion admission is needed. Verify required consumer/operation cleanup precedes terminal observation, no completion under gates, and no same-Guid retarget. |
| Failure/unwind boundaries | Use available legal stimuli for request/TCS construction, diagnostic/receipt creation and cleanup failure. Before ownership transfer, only the entry unwinds; after transfer only the request/handoff owner does. Actual cleanup failure retains its exact graph and charge. Where a construction/runtime failure cannot be stimulated or observed safely, that variant remains unqualified rather than presumed covered by quota refusal. |

These are M03 behavior and ownership observations, not new acceptance IDs or completed results. No test/build was run for this append.

### 8.6 Addendum input freeze

HEAD remains `31f156a51ccc462ae9c5cc8d4e87e00c0054dec4`; snapshot UTC `2026-11-16T00:07:51Z`. The **first 41,269 bytes** of this document are preserved verbatim from the previous proposal, SHA-256 `a893e9f99ea2b7d23c0e3342a5a4789914d0f7c5ae491d16aa0de1abe20d61cc`. Section 7's earlier hashes are not claims about the intervening dirty worktree. The following hashes freeze this narrowly scoped addendum's inputs; recheck them before disposition.

| Addendum input | SHA-256 |
|---|---|
| `global.json` | `1bce01871321c2df82e887da428ce5c121a6c6541fe65017fe3d249be47e190c` |
| `Directory.Build.props` | `770ee4b2a422861a6f23f0d39dd0eca361a81cb87e64ebac4e12170f71231d7a` |
| `docs\design\02-dotnet-api-memory-and-ownership.md` | `40f913a50434209e09941c22625a6d0722202bab97ebac0e153c1ea3368e2291` |
| `docs\design\04-capacity-and-workload-profiles.md` | `a4d86f3fa373a64548435c53582bf5916e29d77f34650f9d161ccec90e6f0245` |
| `docs\design\12-entities-components-and-queries.md` | `174b2465746da79cef560c8506216ef4f99f9d1aabac184f872c9e6cb0b94e0b` |
| `docs\design\16-scenes-transforms-and-cameras.md` | `b012f42b23cede9c7cdb71203d8357cd19e1626823ea25ef3119961292d3698a` |
| `docs\design\30-public-api-review-surface.md` | `15848c82f3858a77a87a0d3ea6fbcc07046eb6e77ca5b21748c72b90efab598b` |
| `docs\design\34-engine-acceptance-cases.md` | `532a68f1717dc05901dfc4537204e65b31b28ee46326f64f5a1cd7834332aa41` |
| `sources\TerraFX.Core\Diagnostic.cs` | `b827997b35ee8accccfa069905865c1e155508401fe7256eacbd0b20e2a6b840` |
| `sources\TerraFX.Runtime\Entities\ComponentRecordRestoreResult.cs` | `9aa175baa34bb55750e0193efd326a338359f9de1be1bc11472fc821b493c2be` |
| `sources\TerraFX.Runtime\Entities\World.cs` | `00442ed9b448d1e252454e8ebf85b4aa1473b383510b93a7c84d225a57f188fc` |
| `sources\TerraFX.Runtime\Entities\WorldGuidUseControl.cs` | `1931cd2b5dfd81a3f0cd8a4e6f3d09c5d9b006b2e29817f5c295c77f78202b68` |
| `sources\TerraFX.Runtime\Scenes\PreparedScene.cs` | `f53de75edfe25950b5e45762eb6d9be5c14a5546d77106f722e23e3c256fb7fe` |
| `sources\TerraFX.Runtime\Scenes\SceneActivationExecutor.cs` | `7d12eb728b0563ed673a86a1e1846aaf0d3fc37a07c139cadecf8f34c646ec69` |
| `sources\TerraFX.Runtime\Scenes\SceneActivationReceipt.cs` | `d6e028d5f3478d3ebd4c0da8c4f6738ba8de5e0e951404b32ed67f48c886eec6` |
| `sources\TerraFX.Runtime\Scenes\SceneActivationRejectedException.cs` | `9c17a263e8b33153b7ba258b4087de5c5861da223e8abe49915576ac91843b6d` |
| `sources\TerraFX.Runtime\Scenes\SceneActivationRequest.cs` | `55df1eb1ea5dab4c47b82b69509549a5efcd97da8d5cb2d0d6cd82a3566a32b5` |
| `sources\TerraFX.Runtime\Scenes\SceneLoader.cs` | `cfa4adaeb6f485fac9f1baa808e00b9e4e3bce48672d36a2847e0b829ab9c945` |
| `sources\TerraFX.Runtime\Scenes\SceneLoadOptionsSnapshot.cs` | `004b203dc84037b76f854dde1b47c78d570a8085b6a9b48ee6423e27496223e9` |
| `sources\TerraFX.Runtime\Scenes\SceneWorldRegistry.cs` | `667e8bf4d3091be93a206a152c9aecbb071e5e9c42d1ecc49f17f1a37c46aeff` |
| `tests\TerraFX.Runtime.Tests\Scenes\PreparedSceneAssetRetentionTests.cs` | `c7a95a1e6c4d90763d0d7ec11bc1a6e1e9da23154631c6ad5a976525cb721f9e` |
| `tests\TerraFX.Runtime.Tests\Scenes\SceneAssetStoreTests.cs` | `1784651e891e29d88e84508dc7c78ad105f42f85acf1ae125772f170fd22a587` |

## 9. Concrete tentative rectification: captured cap and funded delivery tail

### 9.1 Choice

**Recommend tentative adoption of this implementable contract:** let P be the exact `MaximumPreparationBytes` frozen into the prepared result. Every activation attempt independently selects **`W_a=P`**, prepays **`256+P`**, and retains that activation use through its last completion-publication access. The existing use becomes the owner of a strictly data-only delivery tail after application/cleanup; no additional completion-handoff owner is required.

This supersedes section 8's unspecified-A/default and new-passive-owner blockers. An explicit cap is admission policy, not a claim that the request will allocate the cap or that every input fits. Subclaims may refuse before their corresponding allocation, within the already prepaid cap. Implementing and qualifying those subclaims is required work, not another user decision about an unknown A.

| Choice | Assessment |
|---|---|
| **Reuse captured P for each activation — selected** | No new public setting or numerical default. Preparation and each activation remain separately admitted/charged. Conservative: a large preparation cap can make activation refuse even when its actual carriers would be small. That cost is explicit, never replaced with current headroom or unused preparation allowance. |
| Separate explicit activation-byte cap | Supports independent tuning but adds a public declaration/default/range, snapshot and compatibility decision. It does not remove per-allocation accounting or completion-lifetime requirements. Not needed for this unblocker. |
| Preowned request/completion-handoff design | One precreated TCS/Task cannot represent arbitrarily repeated queue refusals while callers retain old Tasks. A replenishable design still needs per-attempt admission; a separate passive owner additionally needs rooting and release machinery. Protected completion capacity is useful, but a new owner/control is unnecessary with the delivery-tail rule below. |

**No new numerical cap selection is requested from the user.** The current explicit P already supplies the policy; its existing default remains 67,108,864 bytes. Document that it also selects each activation attempt's envelope. Independent tuning can be a later explicit API change, not a hidden optimization in this route.

For fully funded preparation-to-R transfers, preparation plus one activation reserve `2*P+512` above the pre-preparation baseline before other activation storage peaks. At the existing default this is **134,218,240 bytes**, not a new global allowance. Transferred R is counted once, not added again to that same funded capacity. The default retention fixture must still pass unchanged under the existing World/domain limits; count maxima and arbitrary concurrent attempts are not promised.

### 9.2 Source and layout evidence

P is already present in `SceneLoadOptions.cs:36-37` and `SceneLoadOptionsSnapshot.cs:8-16`. The current `SceneLoader.cs:58` instead selects zero. `SceneActivationRequest.cs:13-42,123-139` shows why a new attempt needs its own request/TCS/Task and why use release cannot precede rejected-receipt construction.

Selected-runtime in-memory probes used the repository SDK, .NET `11.0.0-rc.3.26522.148`, Windows x64, with 2000 constructor/carrier warmups and 1000 measured samples per reported case. They created no source/project files. These are **layout/allocation observations, not whole-operation allowances**:

| Observed carrier/path | Managed bytes, min=max |
|---|---:|
| Current request carrier, constructor skipped | 144 |
| Current receipt carrier, constructor skipped | 56 |
| Current independent-use control, constructor skipped | 256 |
| TCS for a receipt reference | 24 |
| Task for a receipt reference | 72 |
| Direct TCS+Task construction, asynchronous continuations | 96 |
| `TrySetResult`, no registered continuations | 0 additional |
| `TrySetCanceled`, no registered continuations | 80 additional |
| `TrySetException` with one pre-existing exception, no registered continuations | 248 additional |

The final carrier probe loaded a cap-bearing snapshot of managed size 56; Runtime MVID `e870d6f2-d7cb-4d17-8f90-9fca881010cc`, DLL SHA-256 `fbe3b89e21e8589bdd5ecd2f559aa5e75b869ffd4029e8f73ca14ffcd8ca8572`. Its binary hash was stable across the probe. The framework completion measurements used the same pinned CLR and receipt-reference generic shape. Request/TCS/Task source fields are not an empty 256-byte activation control; cancel/fault completion can allocate after construction.

A separate controllable caller-TaskScheduler probe observed `Task.IsCompleted == true` while the `TrySetResult` producer had not returned because continuation dispatch was held in `QueueTask`. Releasing dispatch let the producer and continuation finish; the probe exited zero. This confirms the proposed tail-observation stimulus on the pinned CLR, not World/accounting integration.

Do not install these observations as unexplained constants. Derive and qualify the **final** fixed bootstrap and protected completion reservations from the compiled object graph, headers/alignment, the Chapter 04 array rule, and all relevant outcome/copy overlaps. Any added request fields change that inventory. The probes exclude constructors for uninitialized carriers, original exception creation/graphs, diagnostic payloads and continuation registration/dispatch; they do not qualify AOT. None of those exclusions becomes free engine-owned storage.

### 9.3 Implementable sequence

1. **Copy only the scalar policy before admission.** Add a nonallocating PreparedScene admission-header read that validates exact World/AssetStore/result state and copies P while holding its existing gate. Do not copy/borrow `ExternalBindings` or the whole options snapshot at this stage. Release the gate before admission or allocation. Locked failure paths return fixed value facts; construct usage exceptions outside gates. Preserve standard entry/pre-cancellation channels; an immediate canceled observation creates no retained activation request.
2. **Admit the exact attempt.** Acquire one activation use with W_a=P, through the common World/domain attribution checks. `WorldAdmissionDomain.cs:1912-1972` and `WorldDomainReservation.cs:97-111` already provide exact per-World operation attribution; ordinary growth checks it at `WorldAdmissionDomain.cs:776-811`. Debit the full `256+P` once, with the prepared use still live. Neither min-with-headroom nor a failed larger-then-smaller retry is allowed.
3. **Fund bootstrap and protect completion before allocation.** From this use's P, preclaim the qualified request/TCS/Task bootstrap and a fixed protected completion allowance before constructing them. The protected allowance covers the largest supported terminal-carrier/fixed-capacity-refusal/fault-publication coexistence, including cleanup-fault overlap, not just success. Body growth cannot spend it. If P cannot fund this minimum, refuse before request/TCS construction and leave the prepared result unconsumed. Use existing allocation-free workspace claims; the 256-byte control is separately earmarked, never borrowed.
4. **Transfer entry ownership once.** After construction, the request's existing activation carrier and retained-owner anchor own all attempt claims; the entry guard becomes inert. Capture `request.Task` into the return local while this ownership is live, **before** enqueue/refusal can finish synchronously. Do not read the TCS/request again after its delivery tail ends. Post-construction exceptions unwind through the request, not the entry alias.
5. **Make queue/detach admission value-only.** Capacity refusal precedes detachment as now. On success, the existing PreparedScene detach transaction transfers the plan, prepared use **and options snapshot together**, revalidating the exact result and selected P. Remove the constructor's unsynchronized `_options = preparedScene.Options` read (`SceneActivationRequest.cs:38`): it can otherwise borrow options whose funding concurrent disposal ends. No request/TCS, array, diagnostic, exception construction or Task completion occurs under request/prepared/domain gates. Race/disposal/admission failures must leave the gates as bounded status facts and be delivered outside them, including the current `ThrowIf` paths in `SceneWorldRegistry.cs:448-468`.
6. **Charge variable work before growth/copy.** Grow diagnostic backing or create Scene-owned carriers only after their checked capacity/byte claim succeeds. The existing root-validation count bound in section 8.4 is usable input evidence, not a substitute for backing/copy accounting. Route callback-result copying through the owning Entities/Scene budget boundary before copying; do not discover its cost by making the copy first. Keep already funded World structural state and Assets backing with their actual owners. Provider/caller-owned immutable input objects and caller-authored continuation graphs are not new Scene allocations; new engine-created wrappers, copies, diagnostic records and per-attempt publication metadata are. Borrowing such input conveys no World/resource-release authority and cannot excuse an uncharged engine-owned retention graph.
7. **Make exhaustion an explicit atomic refusal.** Reserve capacity for the existing Scene-specific shared-budget refusal (`SceneActivation.ControlCapacityExceeded`, with explicit activation-byte-cap context) before optional work consumes the purse. If a complete required diagnostic/copy cannot fit, refuse the complete activation before mutation; do not truncate the diagnostic set, choose a smaller cap, publish a partial result or allocate an unfunded replacement. Faults retain their original exception identity. All postcommit receipt/publication capacity must be protected before commit so cap exhaustion cannot turn an already activated Scene into `Rejected`. Actual allocation failure remains an unexpected failure, not a capacity guarantee supplied by prepayment.
8. **Separate body cleanup from activation-use release.** After application or refusal, transfer installed assets/storage to the instance or finish required cleanup/quarantine, end the prepared use as applicable, and clear the request's plan, prepared/options references and borrowed ports. Only then enter the data-only delivery tail. Body claims must be released/transferred or anchored in actual cleanup failure; do not zero outstanding claims merely to satisfy the use-release guard.
9. **Publish, then end the funded delivery tail.** Keep the same activation use and its bootstrap/protected completion claims through receipt construction and the complete `TrySetResult`, `TrySetException` or `TrySetCanceled` call, outside every gate. In the single producer's `finally`, after its last TCS/receipt access, transfer the completed immutable observation to caller ownership, end the remaining claims, clear the existing retention root and release that use once. Domain closure/revocation alone cannot invalidate this cleanup authority. A use quarantined for actual cleanup failure retains its still-required charges; the finally must not refund that graph. A violated internal release invariant retains the preowned anchor and reports engine failure rather than manufacturing a refund or changing an already committed outcome.

The fixed bootstrap/protected-completion costs are private profile qualifications to derive during this implementation. They are **not a second caller-selected cap** and do not require guessing a worst-case A for every input: variable claims can refuse against P while protected terminal delivery stays funded. A qualification failure blocks the affected implementation/profile as a concrete failed gate, not the selection of this contract.

### 9.4 Explicit completion clarification

**Proposed Scene-specific contract:** terminal activation means application/refusal is fixed and all operation-scoped input/storage consumption, callbacks and required cleanup have ended or transferred to their proper installed/quarantine owners. It is **not a receipt that promise-publication capacity has already been refunded**. The existing activation use may remain as a data-only delivery tail until the producer returns from its final publication access.

That tail may only publish/clear immutable outcome and promise metadata and end its own credits. It cannot reopen Scene work, invoke resource providers, retain a mutable borrow, inspect/mutate World state, reserve a new independent operation, or retarget another binding. Its existing U/interests remain conservatively charged; they convey no new authority. This is the chosen owner disposition missing from section 8, not a claim that the current text already closes it.

Consequently, a continuation can observe a terminal Task while that same attempt's tail still occupies its exact quota. A new request may explicitly refuse until the tail quiesces; do not credit capacity early. For synchronous queue refusal, the tail finishes before `ActivateAsync` returns its captured Task, preserving repeated refusal/retry without retaining the prepared result or waiting for caller GC.

Chapter 02's retained-input/storage rule and EN020's buffer/chain consumption cleanup are not weakened. No actual buffer/native/asset use may hide in this tail. Caller continuation registrations, arbitrary caller continuation code and its allocations remain caller-owned; engine-owned publication work remains funded through actual last access. Completed caller-retained Tasks/receipts require no new public disposal or World-use grant. No additional H interest, process U, passive-owner allocation, normal-path quarantine or finalizer mechanism is introduced.

### 9.5 Regression and implementation boundary

| Case | Exact requirement |
|---|---|
| Captured policy | Use two different explicit P values that both support the fixture; each attempt debits exactly `256+P`. Mutating/dropping the original options object cannot change the captured value. One-byte-short World/domain trials use the full attempt amount and a live prepared use; no allocation-driven cap adjustment is permitted. |
| Repeat refusal | Keep one request queued, then make three queue-full attempts with the same second PreparedScene while retaining every returned Task/receipt. Each refusal returns its own complete queue receipt, consumes no identity and leaves the second result reusable; its tail has ended before the call returns. Drain and activate that original result. No TCS/Task is reset/reused. |
| Dispose/admit race | Race scalar-cap inspection against result disposal and another admission. Only one detach can win. An unadmitted request never borrows the options array after disposal; all losing-attempt claims end once. Run the default package/Atlas retention witness unchanged. |
| Protected refusal | Choose a cap that funds bootstrap/completion but not the next known diagnostic-array/copy growth. Refuse before that allocation/commit, publish the complete reserved capacity refusal, and leave all existing leases valid. Test root two-/four-diagnostic witnesses, component refusal and source/destination backing overlap; no partial diagnostics or semantic publication. |
| All completion shapes | Activated, queue-full, boundary rejection, admitted cancellation, callback fault preserving exception identity, and registry disposal all retain claims through `TrySet*`. Cleanup-fault overlap is included. Pre-cancellation admits no request. No allocation, callback, diagnostic formatting or completion runs under admission/storage gates. |
| Observable delivery tail | Register a caller-owned controllable TaskScheduler continuation. Hold its `QueueTask` during completion publication to observe the terminal flag before the producer exits publication. At that point body/borrow cleanup is complete, the exact activation `256+P` remains attributed, and a competing request cannot spend it. Release dispatch and verify one refund; repeat external-domain closure while held. The scheduler is a test caller, not production instrumentation or an engine-owned unbounded continuation queue. |
| Aggregate accounting | Race two distinct attempts plus ordinary World growth with room for only the jointly fitting set. Keep tail, retained and quarantine states in the sum; successful result transfer changes attribution once. Tail release works after closure/revocation and never refunds another attempt or successor. |
| Profile qualification | Rebuild and qualify the final bootstrap/protected-completion graph on the selected runtime, including all owned arrays/headers/deferred fault/cancel nodes and changed fields. Use focused Debug then Release Runtime tests and actual public default loading; qualify AOT separately where claimed. A layout probe is not managed behavior, public-consumer or milestone acceptance. |

The coherent implementation touches the existing loader/request/PreparedScene admission handshake, activation allocation/copy sites and their owning budget helpers, plus related contracts and tests when separately authorized. It needs no public activation-cap declaration and no new completion-owner subsystem. This turn appends only this proposal.

### 9.6 Rectification input freeze

HEAD remains `31f156a51ccc462ae9c5cc8d4e87e00c0054dec4`; snapshot UTC `2026-11-16T00:36:22Z`. The preceding **59,002 bytes** are preserved verbatim, SHA-256 `7f545d749ddebbd4b011440e5a883f8da1658178c18d8bde7e8fb659dbf35e5e`. Earlier snapshots are not reaccepted. Source inputs below were read-only. The layout probe's ignored binary is identified in section 9.2 as measurement evidence, not an immutable build output or acceptance of the final proposed implementation.

| Rectification input | SHA-256 |
|---|---|
| `global.json` | `1bce01871321c2df82e887da428ce5c121a6c6541fe65017fe3d249be47e190c` |
| `Directory.Build.props` | `770ee4b2a422861a6f23f0d39dd0eca361a81cb87e64ebac4e12170f71231d7a` |
| `docs\design\02-dotnet-api-memory-and-ownership.md` | `40f913a50434209e09941c22625a6d0722202bab97ebac0e153c1ea3368e2291` |
| `docs\design\04-capacity-and-workload-profiles.md` | `a4d86f3fa373a64548435c53582bf5916e29d77f34650f9d161ccec90e6f0245` |
| `docs\design\12-entities-components-and-queries.md` | `174b2465746da79cef560c8506216ef4f99f9d1aabac184f872c9e6cb0b94e0b` |
| `docs\design\16-scenes-transforms-and-cameras.md` | `b012f42b23cede9c7cdb71203d8357cd19e1626823ea25ef3119961292d3698a` |
| `docs\design\30-public-api-review-surface.md` | `15848c82f3858a77a87a0d3ea6fbcc07046eb6e77ca5b21748c72b90efab598b` |
| `docs\design\34-engine-acceptance-cases.md` | `532a68f1717dc05901dfc4537204e65b31b28ee46326f64f5a1cd7834332aa41` |
| `sources\TerraFX.Core\Diagnostic.cs` | `b827997b35ee8accccfa069905865c1e155508401fe7256eacbd0b20e2a6b840` |
| `sources\TerraFX.Runtime\Entities\ComponentRecordRestoreResult.cs` | `9aa175baa34bb55750e0193efd326a338359f9de1be1bc11472fc821b493c2be` |
| `sources\TerraFX.Runtime\Entities\WorldAdmissionDomain.cs` | `55b61437fe42a622bbb534f2224d569ee3076f30f8fdb50691135ae23ee8d8ee` |
| `sources\TerraFX.Runtime\Entities\WorldDomainReservation.cs` | `ea012b9b40acedaa820885eafe27dcd11df55f842b68f2849785dab756e8e3bc` |
| `sources\TerraFX.Runtime\Entities\WorldGuidUseControl.cs` | `1931cd2b5dfd81a3f0cd8a4e6f3d09c5d9b006b2e29817f5c295c77f78202b68` |
| `sources\TerraFX.Runtime\Scenes\PreparedScene.cs` | `f53de75edfe25950b5e45762eb6d9be5c14a5546d77106f722e23e3c256fb7fe` |
| `sources\TerraFX.Runtime\Scenes\SceneActivationExecutor.cs` | `7d12eb728b0563ed673a86a1e1846aaf0d3fc37a07c139cadecf8f34c646ec69` |
| `sources\TerraFX.Runtime\Scenes\SceneActivationReceipt.cs` | `d6e028d5f3478d3ebd4c0da8c4f6738ba8de5e0e951404b32ed67f48c886eec6` |
| `sources\TerraFX.Runtime\Scenes\SceneActivationRejectedException.cs` | `9c17a263e8b33153b7ba258b4087de5c5861da223e8abe49915576ac91843b6d` |
| `sources\TerraFX.Runtime\Scenes\SceneActivationRequest.cs` | `55df1eb1ea5dab4c47b82b69509549a5efcd97da8d5cb2d0d6cd82a3566a32b5` |
| `sources\TerraFX.Runtime\Scenes\SceneLoader.cs` | `cfa4adaeb6f485fac9f1baa808e00b9e4e3bce48672d36a2847e0b829ab9c945` |
| `sources\TerraFX.Runtime\Scenes\SceneLoadOptions.cs` | `8f272e8e7fbe53bb0b0fa9b52a6149314a6b5efb97676cc0daadde6bc3c59368` |
| `sources\TerraFX.Runtime\Scenes\SceneLoadOptionsSnapshot.cs` | `004b203dc84037b76f854dde1b47c78d570a8085b6a9b48ee6423e27496223e9` |
| `sources\TerraFX.Runtime\Scenes\SceneWorldRegistry.cs` | `667e8bf4d3091be93a206a152c9aecbb071e5e9c42d1ecc49f17f1a37c46aeff` |
| `tests\TerraFX.Runtime.Tests\Scenes\PreparedSceneAssetRetentionTests.cs` | `bee1329312f39372ae9fe9e210c1dfab8e0cc43558d1967c637f1859dc8b02dd` |
| `tests\TerraFX.Runtime.Tests\Scenes\SceneAssetStoreTests.cs` | `1784651e891e29d88e84508dc7c78ad105f42f85acf1ae125772f170fd22a587` |
