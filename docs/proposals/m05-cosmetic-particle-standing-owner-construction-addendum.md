# M05 standing credit owner construction and retention

## Status and selection

**Tentative private construction/ownership decision; not a qualified footprint or permission to execute it.** The explicit immutable engine-private **credit-storage-only** World registration request and early fail-closed selection gate are implemented. The complete standing graph is not constructed, funded or retained through `WorldLifetimeControl`.

This is the lower storage sub-capability of the eventual particle implementation, not `CosmeticParticleSupportRegistration`, a Scene participant, a canonical Resource/System/Attachment, or an empty particle producer. It installs no frame actor, root slot, spawn ingress, Task publisher, tick dispatch or GPU consumer. The existing public-token projection still requires Scenes and a complete particle factory; this private request does not satisfy or bypass that contract.

The [budget/return](m05-cosmetic-particle-budget-return-addendum.md), [credit-handback](m05-cosmetic-particle-credit-handback-addendum.md), and [layout evidence](m05-cosmetic-particle-credit-layout-evidence-addendum.md) addenda retain their other decisions. The measured X64 input is one 128-element array, stride 80, World accounting charge 10,304 bytes—not total standing funding.

**Funding result: no complete incremental maximum is established.** The current source identifies the owner's fixed constructor graph, but neither a closed charge for its header/common retaining state nor a funded final caller suffix. The existing 4,096-byte World row is already charged, not an available allowance; the separate domain registrar row does not pay for this new World child. Keep both construction guards and `HasQualifiedStandingOwner` closed.

## 1. Exact optional selection

Propose only these private registration facts:

- `WorldRegistrationBuilder.RegisterCosmeticCreditStorage()` is internal, takes no application provider/delegate, and returns the builder. Default is unselected; duplicate selection throws `ArgumentException`; frozen-builder use retains `InvalidOperationException`. Implemented in `WorldRegistrationBuilder.cs`.
- `Build` copies one immutable internal `WorldRegistration.CosmeticCreditStorageSelected` Boolean. Reusing the registration produces distinct per-World owners, never shared cells or debt. Implemented in `WorldRegistration.cs`.
- Selection adds no component/tag/schema/System/resource/attachment identity. Registering `render/particle-emitter`, Scene support, or existing CPU extraction support does not select it implicitly.
- Both existing component-only staging builders disable the selection request and reject attempts to smuggle it into their component inventory. Focused regressions cover both CPU-extraction and Scene staging paths; do not relax those validators.
- The first supported construction route is **fresh standalone World admission with its private default domain**: no supplied `WorldOptions.AdmissionDomain`, host-shared construction, restore/replacement or default-domain handoff. Selected unsupported routes refuse before candidate construction/claim capture; they do not silently unselect storage or substitute another domain. Unselected routes retain their contracts.

The first qualification input is an otherwise ordinary empty World registration with no Scene/CPU support or application factories. This is a bounded input to the real construction route, not a second test constructor. Additional registration compositions need their actual combined profiles; selecting this private request does not certify them.

Resolve the exact closed construction profile once in fresh admission preflight, before provisional WorldGuid issuance. A missing profile produces conventional `InvalidOperationException`, before creating a default domain or allocating storage. Capture its effective amount and target/source binding in the existing construction plan; never represent “unqualified selected” as zero bytes. There is no new public option, diagnostic ID, allocator or mutable switch on an existing World.

The current fail-closed implementation checks the immutable request immediately after options validation and before generating or reserving a WorldGuid, creating a default domain, or allocating World/owner storage. `WorldConstructionPlan.ValidateForConstruction` repeats the guard before restore-candidate construction. The complete profile is absent, so every selected World still throws the recorded `InvalidOperationException`; no selected World is created. The same WorldGuid can subsequently be admitted by an unselected registration.

The eventual full particle factory may request this same lower capability through its atomic registration path. It must still supply its complete Scene/resource/phase producer closure. Do not implement that factory to deliver this batch.

## 2. Original grant and charge transfer

Use the actual `World.AdmitWorld` path:

1. Establish the complete additional per-World standing catalogue before allocation: owner header, the measured cell backing, new World/plan/lifetime fields, protected construction/unwind/close provision, retained failure references, actual runtime children and any newly required lower metadata. Separately account for request/registration metadata under its existing registration owner and registration-byte budget; do not charge a shared immutable registration object again for every World. A reference does not pay for its referent.
2. Admit the combined configured Chapter 04 World-state peak and the existing domain headroom: the exact `MaximumOwnedStateBytes` up to 2 GiB and aggregate domain accounting up to 8 GiB remain unchanged. Include the selected standing reservation in `worldOwnedBytes` before `WorldAdmissionDomain.TryReserveWorld`; reject before allocating the selected graph if the exact World limit does not fit. Do not rely on the later `ConfigureOwnedStateBudget` clamp.
3. Keep the existing fresh World identity/domain/lineage grants. The standing storage creates **no additional process O/P/U, independent-use reservation, domain operation credit or bootstrap pending cell**. The 128 cells are backing capacity, not 128 acquired uses.
4. `WorldConstructionPlan` carries the captured request/amount with its existing exact lifetime/domain reservation. It borrows those bindings; it is not another owner of the charge. `WorldConstructionPlanControl` is currently restore-specific and must not be manufactured as a fresh-construction handback.
5. On installation, the construction provision becomes the actual World-lifetime-owned storage/protected provision **under the same original reservation**. Do not refund/reacquire headroom, add another physical charge, or borrow operation workspace to bootstrap standing storage.

Join the actual selected reservation to `World.GetWorldOwnedStateBytes` and `ConfigureOwnedStateBudget`, including stopped-but-unreleased ownership. Initial preadmission and later live attribution are two views of the same funding, not summands to charge twice. Keep `IndependentUseBytes` unchanged. The reservation remains held through the owner's real final handback; do not zero attribution when its logical close flag changes.

Use the existing Chapter 04 shared World/control and domain accounting. The dedicated cosmetic graph also belongs to the cosmetic standing ledger's applicable subceiling; that quota attribution is not another physical grant. New shared representation fields affect even unselected instances: verify the revised base registration/World/plan/lifetime footprint under each actual owner, rather than charging only selected Worlds for an unconditional layout change. `FreshWorldControlBytes = 4096` is not proof of spare room for the new graph. Price or prove that common representation change before either selected or unselected construction executes with it; leaving only the selected profile unavailable does not protect an unselected instance whose physical layout changed.

Select **no early refund of this standing provision while its immutable selection exists**. Protected construction/unwind capacity remains reserved for its admitted lifetime/close role. Actual byte release occurs through the original World/domain issuer after the required return/retention boundaries, not merely because construction succeeded. No numeric total is selected here.

### 2.1 Additional graph actually supported by source

Paths in the following source tables are relative to `sources\TerraFX.Runtime\Entities`. The first qualification input remains the empty, fresh, private-default-domain route in section 1.

| Row | Actual membership and missing bound |
|---|---|
| Owner allocation | `WorldCosmeticCreditOwner.cs:12-34` has five direct reference fields (`_cells`, `_domain`, `_lifetime`, `_registry`, `_reservation`), one copied `WorldGuidGrant`, two `int` fields and one `OwnerState` enum. `WorldGuidGrant.cs` itself contains ten 32-bit fields, a `Guid` and a registry reference. The constructor copies existing bindings and allocates one fixed cell array; it does not construct those referents or acquire another grant. Its class header, managed field placement and rounded shallow allocation have **not** been measured. `Unsafe.SizeOf<class>()` would measure a reference, not this allocation. |
| Cell backing | Exactly one `Cell[128]`, with the already observed 80-byte element stride and 10,304-byte **World accounting charge**. This charge already includes the World array-header allowance; do not add another array header. It is not a measured GC allocation size. |
| Cell referents in this route | All cells start unused. No storage-only operation may populate `Control`, `AcquisitionFailure` or `CleanupFailure`. Their reference slots are in the stride; legitimate initial referent multiplicity is zero, not 128 prepaid controls plus 256 prepaid exceptions. An occupied cell is retained failure/debt, not a way to enter live operations under this profile. |
| Already implemented registration metadata | `WorldRegistrationBuilder.cs:12-22` adds `_allowCosmeticCreditStorage` and `_cosmeticCreditStorageSelected`; `WorldRegistration.cs:16-25` adds the immutable selection field. These are fields of existing objects, not a new registration object per selected World. They affect unselected and staging layouts too. No shallow-allocation delta, including a possible zero delta from padding, is proved merely by counting Booleans. Their construction/retention belongs to the actual builder/shared registration owner. |
| Not-yet-implemented common retaining state | `WorldConstructionPlan` has an existing lifetime/reservation binding, but no captured standing amount/protection record. `WorldLifetimeControl` has no owner field, pending-construction debt, retained construction/close failures or standing return/protection record. `World` has no corresponding standing attribution row. Their exact added fields/encoding and affected allocation sizes are not defined by the present source. An owner-to-lifetime reference cannot replace the missing lifetime-to-owner edge. |
| Close handbacks | `WorldCosmeticOwnerHandback` and `WorldCosmeticCreditHandback` are readonly ref structs; the current ordinary success helpers update existing state and return caller-local values. They are not separately allocated close owners, protected byte claims or evidence that the acceptor's suffix has returned. `AcceptOwnerReturn` changes `_state`, without refunding or dropping the backing. |
| Protection, failure and lower retention | New mandatory construction/unwind/close provision and retained failure closure need an actual owner, finite membership and a protected simultaneous peak. Existing lifetime locks, retirement permit/default-domain owner and `WorldSystemCleanupPreparation` are baseline allocations, not unspent allowances for these additions. `WorldSystemInstanceRegistry.ReserveQuarantineCapacity(0)` ensures one lifetime-list slot and allocates a reservation control; it reserves no system-domain slot and performs no byte admission for a new cosmetic graph. Actual list capacity/growth overlap and retained high-water backing remain with that registry's real owner, not one duplicate full list charge per World. |

This closes a useful source distinction: a successful candidate constructor would add **one owner instance plus one backing array**, not another domain, lifetime, registry or 128 uses. It does not close the larger construction/retention profile required to make that constructor reachable. There is no source-defined protected construction/failure/close graph whose missing rows can safely be entered as zero.

### 2.2 Existing debits and their actual intervals

The following distinguishes observable accounting from a qualified physical footprint. None of these named amounts proves spare capacity for an unnamed object.

| Existing mechanism | What the source charges/retains | What it does not supply |
|---|---|---|
| `World.AdmitWorld`, `FreshWorldControlBytes` | Adds 4,096 once to initial `worldOwnedBytes`, alongside separately calculated directories, owner-control queue, diagnostics, resource/system, Scene/CPU support and subscription rows (`World.cs:572-615`). A successful `TryReserveWorld` puts that total into domain `_worldBytes` and `WorldDomainReservation.InitialBytes`. It remains part of that interest until real reservation release, including a retained close. | No allocation catalogue partitions the 4,096 into occupied and unused bytes for this change. It is not a per-child allowance, registration grant, operation workspace or proof of pre-reservation caller funding. The array alone is larger than the entire row, before the existing base graph or new owner header. |
| World array rule | `World.ArrayBytes` charges `64 + RoundUp8(checked(capacity * elementSize))` (`World.cs:878-883`). It prices one actual backing at its actual capacity under its owner. | Calling the arithmetic helper neither reserves bytes nor attaches an allocation to a lifetime. It excludes separately allocated referents. The 10,304 result has not been added to any selected World reservation. |
| Domain registrar and metadata | `WorldAdmissionDomain.cs:71-91,170-196` constructs the domain's TCS and registry arrays, then records `RegistrarControlBytes = 4096` plus its own `ArrayCharge` rows. The domain rule rounds payload to 16, not 8. `ReleaseRegistry` retains the 4,096 registrar charge for publication; ordinary `PublishRetirement` clears it after `TrySetResult`, while the terminal-anchor route retains it until its outer handoff. | This is a different owner/debit from the World's 4,096. Neither the registrar control constant nor a process/domain cell's reference grants storage for the cosmetic owner, its array or unpriced caller/failure work. Source accounting is not a runtime TCS/lock/dispatch footprint or preallocation proof. |
| Registration budget | `WorldSchedulingLimits.MaximumRegistrationBytes` names a validated 16-MiB maximum. `Build` constructs one shared registration and compiles its schedule with default limits. `SystemExecutionSchedule.Compile/ValidateLimits` check graph/conflict budgets; the inspected path contains no registration-byte debit for the added builder/registration fields. | The named maximum does not demonstrate that those physical deltas were admitted. The existing shared registration must not be charged again per World, but its actual construction owner still needs the representation change accounted/proved. |
| Shared World/domain ceilings | `MaximumOwnedStateBytes` is an exact configured ceiling up to 2,147,483,648 bytes. The domain ceiling is up to 8,589,934,592 bytes, and `TryReserveWorld` checks remaining bytes after registry charge, pending growth and all existing World/operation bytes. | Neither 2/8 GiB nor Chapter 04's remaining shared credits/control reservation, nor the tentative cosmetic subceiling, is an allocation entitlement. Passing the domain check does not imply fitting the exact World limit. |
| Independent-use/workspace and identity grants | `IndependentUseBytes`, `WorldGuidUseControl.ControlByteCharge = 256` and the candidate's 65,536-byte workspace request belong to actually acquired uses. Copied identity grants keep their existing issuers/roles. | Storage-only construction acquires no such use or operation credit. There is no `128 * (256 + 65536)` standing purse, extra process role, bootstrap cell or permissible borrowing from operation workspace. |

The fresh source currently checks its initial sum against domain headroom, not a complete combined World-state peak before allocation. `GetWorldOwnedStateBytes` returns `GetOtherWorldOwnedStateBytes` plus Scene construction/storage; neither summation includes a cosmetic row. `ConfigureWorldOwnedStateBudget` later computes `Math.Max(0, maximumOwnedStateBytes - otherOwnedStateBytes - sceneBytes)` (`WorldAdmissionDomain.SceneStorage.cs:145-160`): an over-limit base can yield zero future headroom rather than preconstruction refusal. Do not treat that clamp, or a zero remaining-use allowance, as funding the new standing graph.

A future selected profile must reserve its complete disjoint peak in initial admission **and** include its still-owned attribution in these World-state calculations. This is one original physical reservation viewed at preflight and during ownership, not two grants. For a reached boundary with already funded baseline `B`, `B + 10304` is only an array-inclusive lower bound, not an exact-fit standing profile. Header, common-layout changes and protected/retained overlap must first be closed; no `4096 - guessedBaseSize` residual is admissible evidence.

## 3. Concrete construction order and retaining edge

Before selected storage allocation, the actual lifetime, domain reservation, lineage and pre-reserved lifetime-quarantine capacity must exist. `ReserveQuarantineCapacity(N)` already reserves one lifetime anchor even when N is zero; reserve it before this new constructor. Do not add a dummy system to obtain a slot.

In the existing `new World(constructionPlan)` / initialization boundary:

1. The lifetime records the selected standing amount and **construction-in-progress ownership** before invoking the owner constructor. The original construction caller remains funded through its own return/unwind; the pending record is not a last-access witness.
2. After the World object has established its real OwnerId/thread, invoke the private storage initializer on that owner thread, before application factories and before publication. Bind the exact plan/lifetime/reservation/registration and actual World owner thread—not WorldGuid equality or a caller-provided replacement family.
3. The current constructor allocates only its header/cell backing and copies bindings. It must acquire no U, reserve no operation credit, perform no asset I/O, publish no Task and issue no operational borrow during construction.
4. After its successful ordinary return, install the **actual strong `WorldLifetimeControl -> WorldCosmeticCreditOwner` edge** before any possible acquisition, publication or transfer. `WorldConstructionPlan -> lifetime` and then the existing `World -> lifetime` edge preserve the same ownership across construction. There is no new global particle list.
5. Only after that installation and the rest of normal fresh initialization may the existing lineage/WorldGuid commits and `PublishFresh` occur.

Verify the installed storage/ledger uses the captured original claim, fixed capacity and exact admitted amount before publication. A mismatch fails construction with its real ownership retained for unwind; it is not repaired by silently changing the reservation.

This does not require exposing a half-constructed owner. On constructor failure before a reference returns, the lifetime retains the exact pending construction claim/failures; there is no issued cosmetic operation and no invented replacement owner. A successful owner reference followed by later initialization failure remains strongly retained until its actual cleanup/handback.

Separate **storage-construction qualification from operation authority**. Installing this owner must not make its current reserve/acquire bridges operational merely because an object now exists. Keep operation/profile entry unavailable and expose no borrowed owner getter to Rendering or application code. Storage-only construction starts with all cells unused; it is not a no-body operation producer.

## 4. Unwind, disposal and failed close

Apply the credit-handback protocol at the actual World caller sites. For this storage-only selection, close stops construction/entry and requires no outstanding credit/body/publisher ownership. An unexpected occupied or retained cell is an invariant/cleanup debt, not permission to pump a tick, fabricate settlement or discard it.

Attempt the selected owner's close before releasing the lineage, reservation or final lifetime. Keep the actual owner/amount through the closing helper's return and the caller's acceptance, and keep the acceptor's remaining suffix protected. An owner handback is logical; it does not itself refund World storage. Do not enter `CompleteRetirement`/deferred retirement while this owner still requires that same lifetime, creating a last-U/close cycle.

For either failed construction or disposal:

- Extend the existing **actual cleanup-retains-reservation predicate** with selected construction/owner/return debt. Check it before every fresh-lineage rollback/release, World reservation disposal, attribution clear and final `CompleteRetirement`. This applies even to a never-published/never-ticked lineage; `HasCommittedHistory` alone is insufficient.
- Revoke through `BeginRetirement`, keep the actual reservation/lineage and owner or pending-construction debt in the lifetime, then use `QuarantineAfterCleanupFailure` and the existing pre-reserved `QuarantineLifetimeControl` anchor. At N=0, the reservation has a lifetime slot but no independent system-domain-retention slot: retain through the actual lifetime graph, not `RetainDomainReservation()` on a nonexistent system capacity.
- Do not clear the stored owner or dispose the reservation first. A normal void release on a Quarantined control, a zero pending count, Task terminality or a caught exception is not terminal handback proof. Preserve partial lower release facts; repeat no confirmed destructive cleanup.
- Preserve the original construction exception/cancellation as primary through ordinary selected-storage unwind. Retain an additional storage-cleanup failure privately without creating a System failure or an aggregate. Existing mandatory lower retirement/quarantine failures retain their conventional precedence. On disposal, preserve existing attachment/system precedence; with no such earlier failure, rethrow the actual storage-close exception, or use conventional `InvalidOperationException` for retained-without-throw. Never return clean success with debt.

The ordinary M03 prefix before a lifetime/quarantine binding exists must allocate no selected owner. Its original construction grants still need their existing cleanup/return proof; this proposal does not invent an orphan debt owner for that prefix.

### Default-domain publication and enclosing caller

The private default-domain restriction avoids borrowing a caller-owned domain/publisher boundary; it does **not** prove retirement publication free. The actual source sequence matters:

1. `WorldLifetimeControl.CloseDefaultDomain` clears its owner field under its gate and calls the captured owner's `Close` outside the gate (`WorldLifetimeControl.cs:152-174`). `WorldDefaultDomainOwner.Close` marks itself closed **before** calling `WorldAdmissionDomain.Dispose`. Neither flag change proves publication or caller return.
2. With the ordinary enrolled World interest still present, domain disposal sets `_isDisposed` but does not complete retirement yet (`WorldAdmissionDomain.cs:259-290`). It can publish immediately in an earlier no-interest failure prefix; selected owner allocation remains forbidden in that prefix.
3. `World.Dispose` still has its nested reservation/quarantine cleanup finally suffix. Its last `CompleteWorldClose` call records close success/return and calls `EndWorldReturnPart` (`World.cs:1494-1554`; `WorldAdmissionDomain.SceneStorage.cs:108-140,676-729`). Retirement election checks Scene construction/storage, return-part counts and independent-use bytes, but knows nothing about a cosmetic owner or its pending debt.
4. `FinishWorldRetirement` calls `lifetime.CompleteRetirement`, releases the interest and subtracts **all** its World bytes, then calls `PublishRetirement` when it is the closed domain's last interest (`WorldAdmissionDomain.SceneStorage.cs:731-793`). That publisher calls `_retirement.TrySetResult`, then reacquires the domain gate and clears the ordinary registrar charge (`WorldAdmissionDomain.cs:3122-3146`). The World reservation has already ended while publication and enclosing callers still execute. Unenrolled unwind has the corresponding reservation-disposal/`ReleaseWorld` suffix; it is not exempt.

For the closed empty/private-domain Create-to-Dispose stimulus, the inspected production source installs **no observer of this domain's `Retirement` Task**: its property is not read by a production consumer, and the selected owner constructor installs no Task/callback. Thus an arbitrary particle-Task observer fan-out is not a necessary obstacle to this storage-only input. The existing domain still constructs a promise and executes its completion path. Zero installed observers, a preexisting TCS and `RunContinuationsAsynchronously` are not a measured cold/runtime/failure or enclosing-return profile. Exposing/observing the Task or admitting another consumer would reopen that membership bound.

There is an existing, but **not automatically selected**, terminal-anchor route. `World.TryAttachTerminalAnchorForDefaultDomain` has no production caller in this input. When explicitly attached, `World.Dispose` enters `WorldGuidRegistry.InvokeTerminalWorldClose`; that wrapper waits for `DisposeWithinTerminalInvocation` to return, then calls `TryAcceptTerminalCallerReturn` and `TryCompleteTerminalAnchor`. Domain publication leaves its registrar charge and process-rooted anchor retained until that handoff (`WorldAdmissionDomain.TerminalAnchor.cs:40-80,289-367`; `WorldGuidRegistry.TerminalAnchor.cs:22-52`). This supplies a concrete retention mechanism to evaluate, not automatic attachment, new owner fields, a qualified 4,096-byte footprint or a budget for its own remaining outer suffix.

**Remaining lower-owner decision:** identify the actual preclaimed owner of the storage acceptor, retirement publisher and enclosing return/unwind suffix after logical owner handback. Either keep the original World provision through that boundary, or prove an exact transfer to the existing lower domain/terminal provision with its complete disjoint footprint and real end. Merely keeping a reference after the World debit ends, or moving the charge to the registrar's named 4,096, supplies neither. No change to this lower path or publisher/return qualification is claimed here.

## 5. What can proceed and exact stop

The private immutable selection and early fail-closed admission guard are implemented at `6cce8f1e7b039f364b8609ec082f1c013564dc13`; they add builder/registration fields and preflight checks, not a World/plan/lifetime storage field or selected owner construction. Their passing tests are not a funding proof for those common registration layouts.

| Gate evidence at that revision | Debug | Release | Scope |
|---|---:|---:|---|
| Focused selection and staging | 2/2 | 2/2 | Explicit/frozen selection, both component-only staging paths and early refusal |
| WorldCreation / WorldHostedConstruction / WorldOptions regression batch | 56/56 | 56/56 | Reported gate-batch results; not a new execution in this inspection |

The focused retained TRX files confirm nonzero selected/executed/passed counts. The broader counts are carried forward from the gate handoff; no matching broader World-batch TRX is part of this freeze. The selected request still refuses before WorldGuid/domain/World storage admission; no selected owner or World was constructed.

The next safe action is to close the following bounded profile inputs, **not install the common lifetime hook under old charges**:

| Open prerequisite | Required closure |
|---|---|
| Actual representation | Fix the exact captured amount, lifetime-to-owner reference, pending-construction/close/debt/failure representation and attribution fields. Derive their actual Windows x64 RC3 shallow/allocation deltas, including unselected builder/registration/World/plan/lifetime instances; do not guess padding savings. |
| Protected simultaneous peak | Enumerate construction success/failure, unescaped constructor overlap, cleanup/retained failure graphs, lower registry growth/high water and cold/runtime children. Assign each disjoint row to its actual preclaim and coexistence interval; reused existing referents are not charged twice. |
| Final suffix owner and end | Resolve the lower-owner decision above through actual fresh success, failed construction and disposal callers. A local close handback, quarantine count slot or process anchor alone does not close its byte/return row. |
| Admission/retention integration | Only with that profile, capture its amount, add the real lifetime edge and symmetric debt predicates, and enforce both exact World and aggregate domain headroom before allocation. Preserve original-issuer retention through stopped-but-unreleased ownership. |

Only a complete source/toolchain-bound standing construction/unwind/close profile permits later real World create/dispose observations. The 10,304-byte result proves neither these rows nor actual claim admission, last access, domain publication or release after close. Existing tests establish neither the new graph nor a finite operation/publisher envelope. No Scene, public token, phase, persistence schema or live pool implementation is a prerequisite to closing this smaller storage profile.

**The remaining enabling prerequisite is the complete, originally funded standing construction/retention/return profile, including its lower caller suffix.** No conservative numerical maximum follows from the presently undefined representation/membership and unassigned suffix; that is an unproved bound, not evidence that a finite implementation is impossible. Keep `HasQualifiedStandingOwner` false and both selection guards unchanged. Hosted, external-domain, restore and live operation paths remain separately closed.

## Inspected boundary

This expanded source inspection integrates the selection-gate update at HEAD `63b15172cbb4229636ddb5ba6d5f2944b213b58e`, whose gate implementation is `6cce8f1e7b039f364b8609ec082f1c013564dc13`. The following working-tree hashes were refreshed on 2026-11-19, not copied from the earlier proposal freeze. They are not an accepted executable profile. Source paths are under `sources\TerraFX.Runtime` unless otherwise qualified.

The repository-local `.\.dotnet\dotnet.exe --info` identifies SDK `11.0.100-rc.3.26522.148`, host `11.0.0-rc.3.26522.148` at runtime commit `5254d8bcf5`, Windows `10.0.26300`, x64. The 80-byte stride observation remains the earlier Debug/Release managed RC3 result linked above. This update ran the full Core suite and focused Runtime LOD selection suite after the bounded-rational change, but no owner/layout allocation probe or selected World/Scene/pool operation. No portable, native or AOT claim follows.

The current Runtime and Runtime.Tests binaries were rehashed after the Core, LOD-selection, and World-subscription validations. The Runtime test binaries were executed only for the focused LOD-selection and World-subscription suites, not for the retained cosmetic-credit gate/layout results; these hashes do not identify the modules behind those earlier observations. The layout addendum retains its original module identities and binary hashes. The focused gate TRX files are retained gate evidence, not proof about these binaries. `results.json` is the earlier layout-batch record, not a replacement for the `*-current-*` layout TRX files. Reconcile changed inputs before applying any disposition.

| Input | SHA-256 |
|---|---|
| `Entities\WorldRegistrationBuilder.cs` | `63B526D8A01A8795BB54D8522F25F33E5C8DE492B8DFFD7A7F731FA2A290EC84` |
| `Entities\WorldRegistration.cs` | `05368A6952DE904A07DA3782D11837C82FF2A37C515D2A11A669B5219A79ADB7` |
| `Entities\WorldConstructionPlan.cs` | `B2EE768A7CFD14753882452CE019FFFF3DF274969B3368572E3EF26DDFFBFB06` |
| `Entities\WorldConstructionPlanControl.cs` | `D052B6C1B276601E5F7D75D546D9373AE82A3350140886299CD21D61577F2BE7` |
| `Entities\World.cs` | `99DF71A9956B22E072FDF5314056A6854880BBDE83654D09E5EAB4A197B92FE8` |
| `Entities\WorldLifetimeControl.cs` | `5A304AAF8B2353E28F6F743D5587E82F12E979DCF0DFEBF55103E075CCAF54F2` |
| `Entities\WorldDefaultDomainOwner.cs` | `BCF8F830FBB29E652D480608A3BDFC197D89D7E58299622941E87685B4C2AAD4` |
| `Entities\WorldDomainReservation.cs` | `D6CDD6105850B8076D40891B0ED1F583D9FCDE6EE190444A7D570879964403DC` |
| `Entities\WorldSystemInstanceRegistry.cs` | `70DCB73D187AA6FC27B099F8C27B6716D3D63EF6C4022662017D34C25F5A4E20` |
| `Entities\WorldSystemQuarantineReservation.cs` | `F864C7C0E250D4F6DA38B587C964CE0366E54FFFE22509120B84726E4354361F` |
| `Entities\WorldSystemCleanupPreparation.cs` | `AC0CA1A32B96702985F1286D923142BC4B42B014CD4B5CA82AC531D5FCECB723` |
| `Entities\WorldCosmeticCreditOwner.cs` | `7EEFB227D9654552997BF3515E29CA32E3791F0A155FBF1AA37C274508E2CEA6` |
| `Entities\WorldCosmeticCredit.cs` | `33B7E51F1B36F7BB2860F6BB48426375523398C4AB1E0BE7E692E363780B6707` |
| `Entities\WorldCosmeticCreditHandback.cs` | `DAD1757D41E9F419B3AB18F5A137C0484F4C368363422ECAFFCD137380A0AC55` |
| `Entities\WorldCosmeticOwnerHandback.cs` | `D779373743112441F594CF0325E23F8FEE3615317A7D47E35EB0599606079BE5` |
| `Entities\WorldLifetimeControl.CosmeticCredits.cs` | `C6F41DE02F0896016FA55845A61661BCD1A4DFE085BF68302767FE9C94DA3228` |
| `Entities\WorldAdmissionDomain.cs` | `0AFBC247E5CE649EDBD6F26A01F67BD291D7F23CD1529DD5CD735E89DFFD70F7` |
| `Entities\WorldAdmissionDomain.SceneStorage.cs` | `C8595AF75DCDB5B7128AB6C180A0C07C53E5D8109BBE481F96B4DA3634375608` |
| `Entities\WorldAdmissionDomain.TerminalAnchor.cs` | `63092FD413CF296E188116A3910C1E3031AE42D6C86E8BBB80820B8A62E7970B` |
| `Entities\WorldGuidRegistry.TerminalAnchor.cs` | `EF6E0389A5C303B3EE7F0DB37D66A70EC5FAEB921670CBCB0A65BEA2D91843D2` |
| `Entities\WorldGuidGrant.cs` | `A371A0AFD89C3D987815FA57A866518D39B664E1561F2050346285DB5FFA3399` |
| `Entities\WorldGuidUseControl.cs` | `F00B54B0E8F305F90DF0D63FFDF6C3419FEB8F3D2542E72D76A79AD2D0990946` |
| `Entities\WorldOptions.cs` | `1DACC0CCF24E4E3FF5459E53BC6CF5F562F565A3B7520C24A25451BD12BA4C54` |
| `Entities\WorldSchedulingLimits.cs` | `497B007892C2F5E2C7B87665DF15656ED0788DC810C4FB37D2213719F22DDF61` |
| `Entities\SystemExecutionSchedule.cs` | `280F58958805CB67F8E4220299785055E7C2A969A4DE81934DA45A4E1C2DAAB2` |
| `docs\design\04-capacity-and-workload-profiles.md` | `60047DD1EACF26196B0BF23F16798E2AA265FD26CD36B9A0B4DF1B79135730D7` |
| `docs\design\35-implementation-roadmap.md` | `24EEC6E92B7AD904D552F290EF748FFC8FCE13B6B94A89EBD9F39487D4584D87` |
| `docs\project\Roadmap.md` | `FD84BBDF11FEF04C15FC09489DD663864664EFADEBF1E294748C83E8A73B1BF8` |
| `docs\proposals\m05-owner-contract-closure-runtime-ports.md` | `605CF1D92DC362A8B73EA9BDA3A5467757942C268712F484AF53D9A9159D53E3` |
| `docs\proposals\m05-cosmetic-particle-budget-return-addendum.md` | `1574E6D573BDFE2AF45A3E44EE3BAB5FE52131A02547C8610294F8BBF3C4B119` |
| `docs\proposals\m05-cosmetic-particle-credit-handback-addendum.md` | `6FF6EA4E6BA175558C492155FC95E08B936C1171B6747CA6E499166A2F8CB19C` |
| `docs\proposals\m05-cosmetic-particle-credit-layout-evidence-addendum.md` | `BFAFB74AA1570308830BB95C92058963101588419D196E4929EC5E8B786FDC50` |
| `global.json` | `1BCE01871321C2DF82E887DA428CE5C121A6C6541FE65017FE3D249BE47E190C` |
| `Directory.Build.props` | `770EE4B2A422861A6F23F0D39DD0ECA361A81CB87E64EBAC4E12170F71231D7A` |
| `sources\Directory.Build.props` | `D44373AF315523F8AF9F09F36BDED2A688E2B5C1572FCC0732310C7EF590EAD8` |
| `sources\TerraFX.Runtime\TerraFX.Runtime.csproj` | `2F1C81E54F0FBF12D01E34D0D840885F5EB0E94A8B8890865EF721B8C6A3C65D` |
| `tests\TerraFX.Runtime.Tests\Entities\WorldLifetimeControlQuarantineTests.cs` | `F52196A4EA7F99AB47931B436CF7CE9512D7A10F8ADA80860D6928DA90F552AE` |
| `tests\TerraFX.Runtime.Tests\Entities\WorldOptionsTests.cs` | `AE075BEBF688FA767B0FE6906495DBA8DCABA6DD4B59E82BCC9658BBA4C6579C` |
| `.dotnet\shared\Microsoft.NETCore.App\11.0.0-rc.3.26522.148\System.Private.CoreLib.dll` | `DF53885902E896A2B40988237CBE9CBB9F1BB0743200F05FE9257C56A29C5732` |
| `.dotnet\shared\Microsoft.NETCore.App\11.0.0-rc.3.26522.148\coreclr.dll` | `46A09D97AD8DD30D400A86C90AF707BEE7593B585D3CCB8586C4FFFCA404D1E2` |
| `artifacts\bin\sources\TerraFX.Runtime\Debug\net11.0\TerraFX.Runtime.dll` | `542BCF0B4DE22AEE185C6DE2B75BC60CB413F13B5D554F2AD1D2A396D160DA1D` |
| `artifacts\bin\sources\TerraFX.Runtime\Release\net11.0\TerraFX.Runtime.dll` | `2CA525D91AF312997F5D113B789EC3C2D6EB432636B9061CBEABF1F14D4E8DC1` |
| `artifacts\bin\tests\TerraFX.Runtime.Tests\Debug\net11.0\TerraFX.Runtime.Tests.dll` | `352D102C9C9EC7F1C46AC015E36E77B2DBCCDDC32F7D12439B136EA2CC9EB718` |
| `artifacts\bin\tests\TerraFX.Runtime.Tests\Release\net11.0\TerraFX.Runtime.Tests.dll` | `EBC04CF033E7B3796F73E0BC34DB0D9C374363179889150590BAD573DBC98A79` |
| `artifacts\tst\Debug\cosmetic-credit-selection-debug.trx` | `F48F5091AA3AA35D75947EA8E95AC4E411089BF194CEF567A76A3B2772D28FE2` |
| `artifacts\tst\Release\cosmetic-credit-selection-release.trx` | `FC6DD7E49D015F946BB30AB3EB655455929BF8E1A48D3302E18464A9CF9E4CD9` |
| `artifacts\tst\Debug\particle-credit-cell-layout-current-debug.trx` | `952D95D0C2A7D598B200DF007CF8275BADE42C03AED4414EB8099F5937B8247C` |
| `artifacts\tst\Release\particle-credit-cell-layout-current-release.trx` | `90667EE196ECD22634E086FA1D88EADE5230B97F0F38F3CCAE2B2CDB21F51F46` |
| `artifacts\log\particle-credit-layout\results.json` | `04EA2D33084973DE93A43EE6565CAEBE35CB17D34AAF674A3458CA0B572046D0` |

The following dirty files were already present at the integration boundary. Their bytes are recorded only to distinguish the shared worktree state; they were not edited, reviewed or used as standing-owner qualification inputs. No schema/fixture changes are in this evidence scope.

| Preexisting dirty file | SHA-256 |
|---|---|
| `tests\TerraFX.Runtime.Tests\Scenes\PreparedSceneAssetRetentionTests.cs` | `E342237B7645BE805DD924855B2239907773D31753D22B8B2261BF26C20452F4` |
| `tests\TerraFX.Runtime.Tests\Scenes\RenderSpriteCapacityTests.cs` | `CAA6610CF55F8FFFA077745FD96BAA45FCECFED2FD051CC763901AEED5500239` |
| `tests\TerraFX.Runtime.Tests\Scenes\RenderSpriteComponentTests.cs` | `8E65B71B5AC1E0EF9D1F47AA56963054D091DF8C229E74DDBB1CAE37BB59AB60` |
| `tests\TerraFX.Runtime.Tests\Scenes\RendererExtractionTests.cs` | `ABAEEA4D0F6BE941AF73E640B7FCE41EC753D2199654343FA4668D54EC20FC6A` |
| `tests\TerraFX.Runtime.Tests\Scenes\SceneAssetStoreTests.EngineHost.cs` | `4C0841A40295A891B68974FDDD8078DFD0181C31B35AB0BCB0B5114FE0FD1039` |

This update preserves the private selection gate record and adds source-backed funding/retention analysis only. No source/tests or maintained design/roadmap contracts were changed. No owner, World storage, pool operation, Scene preparation, native consumer, owner funding profile or milestone was enabled or qualified.
