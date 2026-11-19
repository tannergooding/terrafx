# M05 standing credit owner construction and retention

## Status and selection

**Tentative private construction/ownership decision; not a qualified footprint or permission to execute it.** Select an explicit, immutable, engine-private **credit-storage-only** World registration request. Fund its complete standing graph in the original World/domain construction reservation, and retain its actual owner through `WorldLifetimeControl`.

This is the lower storage sub-capability of the eventual particle implementation, not `CosmeticParticleSupportRegistration`, a Scene participant, a canonical Resource/System/Attachment, or an empty particle producer. It installs no frame actor, root slot, spawn ingress, Task publisher, tick dispatch or GPU consumer. The existing public-token projection still requires Scenes and a complete particle factory; this private request does not satisfy or bypass that contract.

The [budget/return](m05-cosmetic-particle-budget-return-addendum.md), [credit-handback](m05-cosmetic-particle-credit-handback-addendum.md), and [layout evidence](m05-cosmetic-particle-credit-layout-evidence-addendum.md) addenda retain their other decisions. The measured X64 input is one 128-element array, stride 80, World accounting charge 10,304 bytes—not total standing funding.

## 1. Exact optional selection

Propose only these private registration facts:

- `WorldRegistrationBuilder.RegisterCosmeticCreditStorage()` is internal, takes no application provider/delegate, and returns the builder. Default is unselected; duplicate selection throws `ArgumentException`; frozen-builder use retains `InvalidOperationException`.
- `Build` copies one immutable internal `WorldRegistration.CosmeticCreditStorageSelected` Boolean. Reusing the registration produces distinct per-World owners, never shared cells or debt.
- Selection adds no component/tag/schema/System/resource/attachment identity. Registering `render/particle-emitter`, Scene support, or existing CPU extraction support does not select it implicitly.
- Both existing component-only staging validators must reject this new request if a staged factory attempts to smuggle it into its component inventory. Do not relax those validators.
- The first supported construction route is **fresh standalone World admission with its private default domain**: no supplied `WorldOptions.AdmissionDomain`, host-shared construction, restore/replacement or default-domain handoff. Selected unsupported routes refuse before candidate construction/claim capture; they do not silently unselect storage or substitute another domain. Unselected routes retain their contracts.

The first qualification input is an otherwise ordinary empty World registration with no Scene/CPU support or application factories. This is a bounded input to the real construction route, not a second test constructor. Additional registration compositions need their actual combined profiles; selecting this private request does not certify them.

Resolve the exact closed construction profile once in fresh admission preflight, before provisional WorldGuid issuance. A missing profile produces conventional `InvalidOperationException`, before creating a default domain or allocating storage. Capture its effective amount and target/source binding in the existing construction plan; never represent “unqualified selected” as zero bytes. There is no new public option, diagnostic ID, allocator or mutable switch on an existing World.

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

The private default-domain restriction avoids borrowing a caller-owned domain/publisher boundary; it does **not** prove retirement publication free. `WorldDefaultDomainOwner.Close` calls domain disposal, which can publish its retirement Task. Its actual observer set, runtime allocations and caller suffix remain part of the target's construction/teardown catalogue. No publisher or return qualification is claimed.

## 5. What can proceed and exact stop

The next coherent source candidate is the private immutable selection, captured plan amount, actual lifetime retaining edge/construction-debt state, matching World accounting, and symmetric fresh-failure/disposal retention predicates. Keep the profile unavailable and do not execute selected construction while deriving its new header/base-field and caller-tail rows. If a new common field cannot yet be funded for existing unselected Worlds, keep that physical hook uninstalled rather than silently changing their allocation under old charges. No Scene, public token, phase, persistence schema or live pool implementation is needed to compile that candidate.

Only a complete source/toolchain-bound standing construction/unwind/close profile permits later real World create/dispose observations. The 10,304-byte result proves neither the owner header/new lower fields nor actual claim admission, protected fault/cleanup provision, constructor failure overlap, last access, domain retirement publication or release after close. Existing legacy tests establish neither that new graph nor a finite operation/publisher envelope.

No further particle behavior decision is needed for this narrowly selected fresh storage-only route. **The missing enabling capability remains the qualified standing construction/retention/return profile for its actual implementation.** If the existing lower caller/retirement path cannot supply that profile, keep the request fail-closed and report that exact boundary; do not replace it with a guessed margin or enable the global gate after measuring only the array. Hosted, external-domain, restore and live operation paths remain separately closed.

## Inspected boundary

These are working-tree inputs, not an accepted executable profile. Source paths are under `sources\TerraFX.Runtime` unless otherwise qualified. Reconcile changed bytes before applying a disposition.

| Input | SHA-256 |
|---|---|
| `Entities\WorldRegistrationBuilder.cs` | `40B6C12CF129A56FC1BE0890311AFF5203F805C50376DDE9184EECB42E703487` |
| `Entities\WorldRegistration.cs` | `0FA11E1B9C75FF4D2EB8309EF2E2226C54820D003CE8579CEE2D91D52E311D6E` |
| `Entities\WorldConstructionPlan.cs` | `3A6E51A8099482A0FCA32F7FAAC3BD55CFC7C53D233FC88B1BD369DEDB32C066` |
| `Entities\WorldConstructionPlanControl.cs` | `7EBD30AD74D1BF889BD342CD395FB58217CDBF059309C153803EA5D2943D4744` |
| `Entities\World.cs` | `692F721BEAD29E5EC258BF7B1844EC785E2D1548E9F53DFA9BDB57A22A01F09F` |
| `Entities\WorldLifetimeControl.cs` | `7F47BE8C4943CE6FC2610FD98D3D2B7470B8F83EA00EC5D9C6C4A3F5C46A9BEB` |
| `Entities\WorldDefaultDomainOwner.cs` | `BCF8F830FBB29E652D480608A3BDFC197D89D7E58299622941E87685B4C2AAD4` |
| `Entities\WorldDomainReservation.cs` | `EA012B9B40ACEDAA820885EAFE27DCD11DF55F842B68F2849785DAB756E8E3BC` |
| `Entities\WorldSystemInstanceRegistry.cs` | `70DCB73D187AA6FC27B099F8C27B6716D3D63EF6C4022662017D34C25F5A4E20` |
| `Entities\WorldSystemQuarantineReservation.cs` | `F864C7C0E250D4F6DA38B587C964CE0366E54FFFE22509120B84726E4354361F` |
| `Entities\WorldCosmeticCreditOwner.cs` | `7EEFB227D9654552997BF3515E29CA32E3791F0A155FBF1AA37C274508E2CEA6` |
| `Entities\WorldLifetimeControl.CosmeticCredits.cs` | `C6F41DE02F0896016FA55845A61661BCD1A4DFE085BF68302767FE9C94DA3228` |
| `docs\design\04-capacity-and-workload-profiles.md` | `A4D86F3FA373A64548435C53582BF5916E29D77F34650F9D161CCEC90E6F0245` |
| `docs\proposals\m05-owner-contract-closure-runtime-ports.md` | `605CF1D92DC362A8B73EA9BDA3A5467757942C268712F484AF53D9A9159D53E3` |
| `docs\proposals\m05-cosmetic-particle-budget-return-addendum.md` | `1574E6D573BDFE2AF45A3E44EE3BAB5FE52131A02547C8610294F8BBF3C4B119` |
| `docs\proposals\m05-cosmetic-particle-credit-handback-addendum.md` | `6FF6EA4E6BA175558C492155FC95E08B936C1171B6747CA6E499166A2F8CB19C` |
| `docs\proposals\m05-cosmetic-particle-credit-layout-evidence-addendum.md` | `BFAFB74AA1570308830BB95C92058963101588419D196E4929EC5E8B786FDC50` |
| `tests\TerraFX.Runtime.Tests\Entities\WorldLifetimeControlQuarantineTests.cs` | `F52196A4EA7F99AB47931B436CF7CE9512D7A10F8ADA80860D6928DA90F552AE` |
| `artifacts\log\particle-credit-layout\results.json` | `04EA2D33084973DE93A43EE6565CAEBE35CB17D34AAF674A3458CA0B572046D0` |

Only this proposal is added. No source/tests or maintained chapters/roadmap are edited; no build, test, World/Scene/pool operation or probe executes.
