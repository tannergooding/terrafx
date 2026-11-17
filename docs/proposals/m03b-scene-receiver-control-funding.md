# M03-B Scene receiver-control funding

**Status: tentative rectification proposal for later review. Not an implemented receiver protocol, a qualified H/R footprint, or permission to enable Scene preparation.**

## Decision and limit

Keep the [finite-envelope addendum's](m03b-scene-preparation-finite-envelope-and-return-addendum.md#retained-r-bounded-lower-owner-protocol) exact-parent, retained-R, and original-owner retirement protocol. Recommend one representation refinement: put the logical parent's receiver ledger in its **existing prepaid 128-byte domain interest cell**, rather than extending the currently 64-byte `WorldDomainReservation` object. The reservation remains the exact-generation handle and release identity; this does not create another owner or separately allocated carrier.

This gives a source-derived, conditional **zero incremental backing charge for the shared ledger**. It does not make the separately allocated parent, lifetime, World close carrier, or receiver free. The proposed receiver layout has a **72-byte x64 size derivation**, versus the observed current 48 bytes; that number still requires final compiled-layout/factory confirmation before becoming a charge.

**A complete, numerically qualified safe route cannot yet be established from these inputs.** In particular, the existing 4096-byte World-control grant has no itemized whole-carrier/close-tail coverage proof from which to derive H. Neither measured constructor establishes it. Keep H and the complete producer R/P footprint unresolved rather than installing guessed constants. The representation and ordering decisions below unblock bounded prerequisite work; they do not close the addendum's enablement gates or unrelated M05 producer/native gates.

The existing addendum is unchanged. This proposal introduces no public declaration, schema, acceptance identifier, quota increase, new worker, or normal-path quarantine.

## 1. Evidence and existing funding

Paths abbreviated below are under `sources\TerraFX.Runtime`.

| Input/boundary | Finding |
|---|---|
| Current Debug/Release TRX; `WorldAdmissionDomainTests.MeasuresWorldDomainReservationConstruction` | Each records eight warmed `TryReserveWorld` allocation deltas of **64 bytes**, on `.NET 11.0.0-rc.3.26522.148/X64`. The measurement ends before reservation disposal. It excludes domain construction, cold/growing registry backing, World/lifetime construction, contention, and close. The test prints the deltas; it does not assert a complete control budget. |
| Supplemental Debug/Release TRX; `SceneSupportRegistrationTests.MeasuresSceneStorageReservationConstruction` | Each records **48 bytes** for one warmed use-funded storage-reservation factory call. World, participant, use, registry setup and disposal are outside that interval. |
| `WorldDomainReservation.cs`; `WorldLifetimeControlQuarantineTests` | The current parent has one reference, four `long` fields including `InitialBytes`, and two four-byte identity fields. Its separate uninitialized-object test asserts 64 bytes; that is not the warmed factory test or an H ledger. There are no receiver-head, S/C, or close-progress fields in this object. |
| `WorldAdmissionDomain.cs:160-187, 1873-1947, 3401-3439` | Interest backing is charged using `ArrayCharge(capacity, Unsafe.SizeOf<InterestEntry>())`; `InterestEntry` declares sequential, pack-8, size-128 storage, including ten unused reserved `long`s. Its existing `Reservation` reference can bind the World interest to its exact parent. The factory currently leaves that reference unset for a World interest and allocates the parent after claiming only the requested `bytes`; it adds no separate 64-byte charge. |
| `World.cs:573-607`; `World.PersistenceAdmission.cs:13-84` | Fresh admission explicitly includes `FreshWorldControlBytes = 4096`. Restore includes that same grant and the separate 128-byte restore-context provision. These are real aggregate reservations before World/lifetime allocation, **not** evidence that arbitrary extensions fit them. Raw persistence reservations also use `TryReserveWorld`; they do not automatically receive the World bootstrap allowance. |
| `WorldGuidRegistry.cs` | Process backing/cell charges are separate from domain/World accounting. A reference from a process cell would not pay for a domain-owned object. No process-cell extension is needed for the selected route. `CanAdmitFresh` refuses any still-indexed GUID; retaining the original revoked binding-owner roles already prevents fresh reuse. |
| `World.cs:1795-1825, 1997-2150, 2215-2224` | Scene factories claim/transfer the passed byte amount, then construct the carrier under `_sceneStorageGate`. They do not add a separately identified receiver-control term. The World Scene mirror aggregates storage and initially includes prospective restore hierarchy capacity. `GetWorldOwnedStateBytes` does not independently enumerate the fixed 4096-byte bootstrap or a general pending-construction ledger. It cannot certify an H carve-out. |
| `ScenePrefabExpansionPlanner.GetRequiredByteReservation`; `RenderSceneAssetBundle` charge helpers | The planner ends with `256 * prefabCount + 2048`; bundle retained/scratch helpers contain 256/128/64-byte constants and array terms. None identifies a complete reservation-control provision or proves unused padding for new metadata. Planner totals also mix temporary and retained work. Their local array rules round to eight bytes, unlike Chapter 04's sixteen-byte accounting rule. |
| Current release paths | `World.Dispose` can release the entire parent before its final restore/quarantine cleanup and World-field write. Storage release suppresses `ObjectDisposedException` after World close. `WorldSceneStorageReservation.Dispose`, plan disposal, and bundle disposal still access their own fields after returning storage. These are not last-access acknowledgments. |

The current bundle `Dispose` returns storage **after** its nested child-cleanup `finally` blocks, not from those blocks. Its construction `finally` still returns reservations following fallible cleanup. Do not copy the addendum's older source description over these current bytes.

The active-workspace transfer and the 4096/3072 rollback arithmetic repair already exist. `TryReturnWorldGuidUseWorkspace` does **not** reject `IndependentUseBytes < bytes`; the independent-use lower-bound predicates remain correctly in subtraction/transfer paths. Neither needs to be proposed as missing work.

## 2. Exact shared-ledger representation and accounting

### Use the prepaid interest cell, not apparent object padding

The addendum places parent state with the parent/domain. Refine its physical representation as follows: the exact World interest contains the ledger; the unchanged parent object supplies domain/index/generation identity. Only that parent may address the cell, and every access revalidates the cell's exact `Reservation` reference and generation. This is a representation change for review, not a claim that the fields already exist.

For the measured x64 lane, the following complete field set fits the existing cell:

| Cell contents | Bytes |
|---|---:|
| Existing scalar fields, `UseControl`, `Reservation`, and alignment | 48 |
| S, C, and retained World-control floor H: three `long`s | 24 |
| Close/cleanup/retirement progress and election flags: one 64-bit word | 8 |
| Pending-constructor and live-return-part counts: two four-byte integers | 8 |
| Original `WorldLifetimeControl`, receiver head, and temporary World close-carrier references | 24 |
| Reserved remainder, not assigned to another purpose | 16 |
| **Whole cell** | **128** |

The 64 bytes of new ledger fields replace eight of the ten reserved words. Keep lifecycle/election acknowledgments in the progress word; adding a private state value to the lifetime's existing `_state` does not require another object field. Counts use checked admission before increment; no count may wrap or authorize an unrecorded suffix. Failed receiver graphs remain on their receiver nodes; failed World cleanup retains its existing close/quarantine graph. A reference slot does not fund the referenced graph.

Provided the final compiled cell still has stride 128:

```text
old interest backing = 64 + RoundUp16(capacity * 128)
new interest backing = 64 + RoundUp16(capacity * 128)
incremental backing charge = 0
parent object remains 64 bytes; incremental parent-object charge = 0
```

The complete old-plus-new array peak on growth remains charged by the existing growth ticket. Copy all new references/ledger fields; clear them before a cell becomes free or generation-retired. Never retain a `ref InterestEntry` across gate release or array replacement. Initial raw reservation issuance must bind `interest.Reservation` before the parent escapes; actual World enrollment then supplies the exact lifetime and close state. Unenrolled raw persistence/domain reservations keep their existing non-receiver semantics.

This uses named prepaid **domain registry** backing, not the World 4096 grant, the process registry's spare ceiling, or a Scene use's 256-byte control. The whole 128-byte cell is already charged once in `RegistryCharge`; do not also add its 64-byte ledger payload to H, R, or P. No additional interest cell is issued.

If review requires those fields physically inside `WorldDomainReservation` instead, the zero-growth conclusion does not apply. Merely adding head/lifetime references, S/C totals and a four-byte state to its current fields projects at least **104 bytes** on this x64 layout, before further close/return fields. The 64-byte factory observation cannot authorize that extension. Reprice every raw factory caller as well as fresh/restore World admission; do not silently consume unspecified 4096-byte padding.

### H is still a separate, unresolved obligation

Here H means the addendum's **World-data-side shared control provision**, not Chapter 12's default-domain handoff H interest. The domain interest cell remains in `RegistryCharge`, outside this H.

H must cover whole coexisting carriers still accessed: the separately allocated 64-byte parent; the original lifetime, retirement permit and synchronization state; the whole World close carrier through its last access; and the actual retirement/return failure and publication suffixes attributable to these owners. Include supported lazy state and separately owned backing, but exclude independently funded domain/process registry backing and borrowed/independently owned data. Summing reference widths is not this calculation.

Let `G_fixed` be the complete fixed World-control construction/close peak assigned to the existing 4096-byte grant, and `H_live` its identified still-required whole-carrier subset. Coverage requires an itemized, independently derived `G_fixed <= 4096` and `H_live <= G_fixed`, including overlap with other controls assigned to that grant. **Neither value is established here.** Restore's additional 128 bytes is not general Scene-control headroom.

Construction order also needs proof: `AdmitWorld` creates its default-domain owner before `TryReserveWorld`. Identify an actual earlier issuer for that allocation or preclaim it earlier; a later 4096-byte grant cannot retroactively fund it. Include that owner through its real close/transfer end in the fixed-control ledger.

Until that partition is proved, do not subtract an estimated A at World close. Conservatively retain the unresolved ordinary World reservation as well as C/S/U; return only independently identified, successfully ended segments. This preserves existing credits but is not proof that the original aggregate was sufficient. Once proved, retain an explicit H floor through the last parent/lifetime/close access. If the qualified fixed peak exceeds its existing grant, preclaim the exact additional amount through fresh **and restore** World admission before allocation, under unchanged World/domain ceilings; otherwise refuse. Do not fund a preexisting World carrier retroactively from a later preparation P.

## 3. Per-receiver control belongs in R and, before transfer, P

Select a receiver with these fields and no additional control object or lock:

- Replace `_world` with the exact parent reservation reference.
- Keep `_workspaceUseControl`, immutable settled byte charge, and one lifecycle integer.
- Add intrusive predecessor/successor and one opaque retained-graph reference.

For the measured x64 class-layout model:

```text
5 references * 8 + settled charge 8 + lifecycle 4 = 52 field bytes
object header 16 + 52 = 68; RoundUp8(68) = 72 bytes
equivalently: observed current 48 + 3 added references * 8 = 72
```

This is the selected representation's **layout derivation, not an observed final allocation**. Confirm its complete field set, header/alignment and factory allocation on the final compiled lane. Do not use `Unsafe.SizeOf<class>()` as object size or promote this x64 result to other runtimes/architectures/AOT. Keeping both World and parent references instead would project 80 bytes and would also retain the World unnecessarily; that is not this route.

Initial reservation admission still crosses the existing live World boundary and validates the exact selected participant. Thereafter an issued workspace reservation can settle only against its captured parent/use. Registration/participant identity cannot change while that World remains open; installation validates that original issuance and the parent's open-admission state under the domain pair. It does not select a participant, call back into World, or obtain new authority. This removal of the receiver's World dependency is required for the 72-byte route, not an optional optimization.

Use the following ledger; these are accounting names, not API fields:

```text
Q_i = qualified whole receiver-control extent and any additional receiver-control provision
D_i = identified receiver data/reserved capacity and other receiver-owned graphs, excluding Q_i
R_i = D_i + Q_i
S   = sum of installed, unreleased R_i, including failed-release nodes
C   = World-funded uninstalled/prospective capacity; excludes use-funded X
B   = parent.Bytes = A + H + C + S
U   = parent.IndependentUseBytes
this World's domain data attribution = B + U
```

For the proposed plain carrier, 72 is the candidate value for Q on the measured lane. Additional fields, cleanup holders or lazy work invalidate that value. D and total R for an actual Scene plan/bundle are **unresolved**. The opaque graph reference includes no allowance for that graph's allocations.

Charge Q once, inside R, before constructing the carrier. On a use-funded path it is part of active X inside the unchanged requested P:

```text
P = F + X + T
remaining use charge = 256 + F + X
claim b:           F -= b; X += b
install R=b:       X -= b; T += b; U -= b; B += b; S += b
ended R release:   S -= b; B -= b; domain data charge -= b
```

Installation preserves B+U and domain charge. Disposing R does not replenish P. The 256-byte use-control grant cannot pay Q. A direct World claim increases B/C before construction; installing it changes only C→S. Already admitted restore C likewise moves to S without a second B charge.

Do **not** fix current producer estimates by adding only `72-48=24`: no source ledger proves the old 48 was assigned inside their constants. Replace the mixed estimates with named disjoint terms, include Q once, and keep scratch/producer/publication claims in X. Update plan `RequiredByteReservation` equality, bundle retained terms, measurement/preflight, direct/restore admission, and failed-cleanup ownership together. Existing cap/default/range and Chapter 04 array accounting remain unchanged.

**Distinguishing arithmetic, not an executed Scene:** for independently specified reserved data capacity D=512 and qualified Q=72, R=584. An otherwise isolated P=1024 use starts at U=1280. Claim/install leaves F=440, X=0, T=584, U=696 and B/S higher by 584. Returning R reduces B/S by 584 and leaves U=696. Calling the old 512-byte argument “512 bytes of data plus free control” would underfund this example by 72 bytes. Full Scene bootstrap/coexistence is deliberately absent from this lower-owner example.

## 4. Ordered preclaim, installation and rollback

1. **Establish funding before any extended allocation.** Qualify the cell stride and shared H assignment; qualify Q for the final carrier. Bind the existing interest to the original parent before escape and the original lifetime before World publication. Do not add a captured funding delegate, guard object, collection or per-node lock. The ordinary unescaped constructor caller owns a scalar guard with exact parent/generation, amount, provenance and one consumption state.
2. **Claim the whole construction peak.** Use-backed requests first move newly needed capacity F→X through the original use, or retain an already active claim without claiming it twice. Direct World requests atomically reserve B/C and a pending-constructor count. Restore requests identify a subset of already admitted C and retain a constructor count; they do not charge B again. Any payload/helper overlap outside the settled R must have its own original grant before allocation.
3. **Construct outside all storage/admission gates.** Revalidate scalar inputs before `new`; initialize the receiver completely before exposing it. Pending C or original X/U prevents close from refunding the attempt. The current factory's growth-gate scope and Scene factories' scene-gate scopes must not span allocation. Use captured indices/generations, never array refs, after reacquiring gates.
4. **Install by one nonallocating transaction.** Under `_growthGate` then `_gate`, verify exact issued parent/use, World enrollment/open admission, constructor provenance, receiver state/charge and available original claim. Precompute every checked result and validate link neighbors before mutation. For use funding atomically apply X→R and B/U/S/operation-interest changes; for World funding apply C→S. Link the same initialized receiver and its actual construction/data owner before its materialized capacity loses its previous root. Clear workspace-only authority in that same transaction; allocate nothing afterward to complete the control.
5. **If construction fails or close wins, return only the uninstalled original claim.** Newly claimed, genuinely unconsumed X may return to its original F; a refused transfer of an existing active claim leaves that claim owned. Fresh direct C can be canceled and B reduced after the attempt's accesses end. Restore rollback restores the original prospective C slice; it does not create new free World headroom. Constructor counts end once, after unwind. A legal original return remains legal after World/domain closure and is not new admission.
6. **After installation there is no constructor rollback to P.** Fallible attachment/physical cleanup preserves the linked R and graph until actual success or a legitimate independently funded transfer. A duplicate/foreign return cannot spend another node's bytes. An uncertain partial release records irreversible progress and retains remaining credits/roots; it must not reset the receiver to a retryable state as current `Dispose` does.

No materialized payload may escape solely through an unrooted local. A constructor failure before any object exists is not an installed R; uncertain unspent C/X debt remains on the original rooted owner. Multiple outstanding attempts require independently provable nonescaping guard lifetimes and exact aggregate accounting, not an escaping scalar token that can be replayed.

## 5. World close and final-return states

### Ownership and locks

The strong data root is **domain → occupied exact World interest → parent/lifetime and receiver head → linked receiver → actual opaque graph**. The lifetime retains its original lineage grant, permit and revoked binding. GC, GUID equality, an ended preparation use, and a World callback are not release authorities.

Keep `_sceneStorageGate` → `_growthGate` → `_gate` for live World enrollment and close. Exit the persistence/owner-control gates used for the disposal latch before entering the domain pair. Receiver-only settlement/unlink/original-claim return uses only the domain pair. Drop both before original lifetime/process retirement; retain the current process-before-domain ordering of independent-use release. No domain→scene/lifetime/process acquisition, allocation, physical cleanup, callback or Task delivery is allowed under the pair.

Split exact live-interest validation from permission for new admission. `IsOwnedBy` remains true while data is retained. Growth/new-use/new receiver-install callers require open World admission; original cleanup returns do not. `InitialBytes` is historical bootstrap information, not a closed-state minimum or rollback allowance. Replace the independently spendable World Scene mirror with a snapshot of authoritative S/C, counting prospective restore C and the retained control floor once in live shared headroom.

| Parent/lifetime condition | Required action and retained charge |
|---|---|
| Open | Admit only against the exact enrolled parent, original binding and shared headroom. |
| Admission closed; close driver active | Latch World close and parent admission together. Revoke the original binding outside the domain pair. Retain all C/S/U, close carrier, H and unresolved cleanup charge. Close-first refuses later installation; install-first remains linked. |
| Ordinary cleanup succeeded; close suffix active | Return only an independently proved disjoint A. Otherwise retain it. Keep the whole close carrier through restore-lease disposal, quarantine-capacity release and the final World-field write. R/C/U returning meanwhile cannot finish the parent. |
| Data/use wait after close's last access | Normal deferred success, not cleanup failure or quarantine. Retain the original owner/lineage, parent, H, all C/S/U and required graphs. No owner-thread wait or new worker. |
| Retirement elected | One ordinary eligible caller advances acknowledged phases outside gates. Retain H and the parent interest until lineage, process retirement, use/return suffixes and close acknowledgment have all ended. |
| Actual cleanup/return failure | Preserve graph, exact remaining ledgers and completed irreversible phases. Retain unresolved A as well if no disjoint partition was proved. Normal repeated `Dispose` is not a physical-release acknowledgment. |
| Released | Clear receiver/close/lifetime links and the parent's domain attachment at their last funded access, then refund the remaining H/interest once. No later engine suffix accesses those carriers under the ended grant. |

### Select a conservative original-owner retirement condition

For enrolled Worlds, defer **both** `C_o` retirement and binding-owner permit consumption until:

- ordinary cleanup and the close driver's actual last-access acknowledgment succeeded;
- C=S=U=0, including raw/prospective use admission and outstanding constructor attempts;
- no other return/acquisition suffix or cleanup debt remains.

This deliberately waits for U as well as R/C. It is a conservative specialization of the addendum, avoiding an early `OwnerRetired` root clear while a raw use is not yet attached. Other real history interests may remain; the existing history-count behavior then retains their root after `C_o` ends. No history membership is manufactured.

Represent normal data/use waiting separately from existing `StateRetirementPending`. `WorldGuidRegistry.TryReleaseIndependentUse` interprets that existing condition as permission to call `CompleteDeferredRetirement`; reporting it while waiting for R would turn a normal last-U return into failed-use quarantine. The selected enrolled-World path does not enter that handshake merely because data remains. Preserve it for its genuine existing role. Protect `CompleteRetirement`, retirement-permit eligibility, unpublished/candidate cleanup, and all unwind callers against bypassing the enrolled parent's condition.

In particular, `TryRollbackFreshLineage`/`TryReleaseFreshLineage`, fresh construction's `finally`, restore-plan `ReleaseResources`, and `WorldConstructionPlan.ReleaseUntransferredRestoreResources` must use the same end-request/eligibility protocol. A central refusal alone is insufficient if a caller then disposes the parent or abandons its process owner in `finally`. Record normal deferral distinctly from `lineageRetired=false` failure; do not weaken actual stale/foreign/invariant failures into success.

### Last-return order

1. The actual higher owner ends or validly transfers every R-funded data/cleanup access before handing back its receiver. Move plan/bundle field clearing before their final handback, or identify a separately funded remaining suffix. Logical disposed flags and the reservation's own `Dispose` do not establish this.
2. Under the domain pair, validate the exact parent/generation, immutable charge, lifecycle and intrusive neighbors. Snapshot the ordinary progress target, clear node ownership/graph/link fields at their last funded access, unlink once, then subtract exactly R from S/B/domain bytes. Do not touch U or another segment. The successful receiver suffix cannot then read/write its fields or allocate Scene-owned storage.
3. Last R before U leaves a normal use wait; last U before R leaves a normal data wait. With the conservative condition, neither consumes the original owner permit prematurely. The last-use path must reach a funded terminal handback **after** its process/domain/control suffix is settled; current `WorldGuidUseControl.Release` writes `_anchor`/state after domain refund and is not that handback. R/H cannot serve as its emergency 256-byte use-control allowance.
4. The last eligible ordinary return, constructor unwind, or close caller elects one retirement driver under the domain pair, then drops it. Retire the exact original lineage once; record its successful acknowledgment before advancing. Retire the revoked binding through the original lifetime/permit, never through GUID lookup or `AcquireUse`. A process/domain partial success retains the remaining phase/root; no consumed permit or released U is recreated.
5. After every acknowledgment and return part ends, clear parent/lifetime/close links before the final interest/H refund. The final domain helper uses already captured values and performs no later parent access. Any domain retirement publication must occur after **both** gates are exited and remain funded by the domain's own control tail. Current `using var growthScope` spans later `TrySetResult` in several domain close/release paths; dropping only `_gate` is insufficient.

If the exact close attempt cannot revoke its binding before ordinary cleanup, reopening is legal only if that binding is demonstrably still active and the exclusive attempt remains current. Use current ledgers after concurrent returns, not saved totals. Otherwise retain closed failure.

Do not release the original revoked owner while R remains even when process independent-use count and history count are zero. With an **open external domain**, fresh admission for that GUID must still fail because its old key/owner role remains. Once actual retirement and all other associated roles end, the existing index rules allow fresh reuse. An already authorized exact replacement may have a successor binding under the same key; it never becomes the old receiver's owner or acquires its release/close rights. Only the original untransferred default-domain owner may close its domain; old replacement retirement cannot close the successor's domain.

External **domain** closure may still permit settlement of a previously prepaid X while its exact World admission remains open, as current transfer helpers do. World admission closure refuses installation. Original returns remain legal after either closure.

## 6. Implementation order and focused evidence

Implement later in one bounded owner batch, with Scene preparation still unavailable:

1. Qualify final shared-cell/receiver representation and the fixed World H coverage or explicit refusal described above. Keep the 64-byte parent unchanged on the selected route.
2. Add exact enrollment, authoritative S/C, original-claim preclaim/rollback, nonallocating installation and parent retention. Integrate fresh/restore construction and untransferred unwind; do not leave a four-file pin-only bypass.
3. Integrate World close, original-owner retirement election, terminal last-U/receiver/parent return and funded out-of-gate domain publication. Preserve failed graphs and irreversible progress.
4. Reconcile plan/bundle retained-control and workspace terms without executing their unavailable preparation producer. Reconcile relevant maintained owner descriptions under the later authorized implementation change; do not treat this proposal as silently amending them.

Extend the existing `WorldAdmissionDomainTests`, `WorldLifetimeControlQuarantineTests`, `WorldGuidRegistryTests` and `SceneSupportRegistrationTests` using real Worlds, registered Scene participants, independent uses and lower reservation paths. Use ordinary owning-project `dotnet test` commands from [Building](../project/Building.md), Debug before Release, combining these filters and checking nonzero discovery/execution. No Scene preparation, fabricated plan/snapshot, fake Scene owner, production failure hook or separate qualification runner is needed.

| Focused observation | Distinguishing requirement |
|---|---|
| Layout/allocation and existing growth boundaries | Verify the independently specified complete field membership, compiled cell stride 128 and unchanged full old/new array charge. Recheck warmed parent factory at 64; measure the final receiver against its layout-derived Q. Cover required cold/lazy/contended branches separately before claiming H. Existing uninitialized-object tests alone are insufficient. |
| Exact/one-byte-short claims | Include funded setup and other concurrent grants. Deny a Q-short constructor before allocation. Cover new F→X→R, existing X→R, direct new C, and already prepaid restore C; assert the independently derived B/U/S/C conservation and no header/cell double charge. Preserve the existing 4096/3072 rollback case. |
| Retained receivers after World close | With no preparation use left, release head/middle/last nodes out of order, repeat disposal and close the external domain before/after World close. Each release removes only its own R. Parent/H and domain retirement survive every required close/return suffix. This is a reservation-credit test, not a Scene producer observation. |
| R/U/C/close permutations | Reach each final owner: last R, last U, constructor unwind and close driver. Verify exactly one lineage/process/parent end and no data-wait-induced use quarantine. Include a raw accepted-use acquisition still in progress at close. |
| Original GUID and successor isolation | Keep an external domain open so it is not the refusal reason. Fresh same-GUID admission refuses while old R remains; reuse is possible only after actual old-role retirement. Exact replacement never retargets old release rights or default close authority. |
| Lost installation and physical failure | Exercise reached close-first/install-first outcomes and original-grant restoration. A normal capacity refusal is not constructor OOM, partial process/domain failure or physical cleanup failure. Use a safe real stimulus/external stop at the actual site where available; otherwise mark that interleaving/failure observation unsupported and open. Do not add a throwing allocator or fake supplier. |
| Raw non-World reservations | Existing raw/persistence disposal and domain-close semantics remain valid. They gain no receiver pin, World-control allowance or new role merely because the same reservation class is used. |

### Records inspected, not rerun

| Existing record | Actual scope |
|---|---|
| Current Debug `tagoo_GOODING-ZEN4_2026-11-17_13_36_39_net11.0.trx` | 4 discovered/executed/passed: parent factory measurement, domain old/new peak, and two interest-growth boundary cases. Eight 64-byte warmed deltas. |
| Current Release `tagoo_GOODING-ZEN4_2026-11-17_13_36_43_net11.0.trx` | Same four cases and eight 64-byte warmed deltas; 4/4 passed. |
| Supplemental Debug/Release `m03b-scene-owner-measure-*-20261118.trx` | Each 16/16 executed/passed, including the 48-byte receiver factory measurement and transfer-rollback regression. No retained-R World-close implementation is exercised. |

These are framework-test counts, not executed conformance-case membership. The records do not bind every dirty source byte to the compiled testhost, qualify total H, or prove producer, native, AOT or public-consumer behavior. No tests, builds, Scene execution or allocation experiments were run for this document.

## 7. Remaining blockers and no-go conditions

| Open obligation | Smallest additional evidence/action |
|---|---|
| Complete H and existing fixed-control coverage | An itemized fresh/restore World-control construction/close/retirement lifetime ledger, with whole managed extents, synchronization/lazy state, failure overlap, last accesses and grant assignment. Demonstrate which existing 4096/128 provisions cover each item before carving H or admitting an exact additional reservation. A whole `World.Create` allocation delta is not a substitute. |
| Final 128/64/72 layout/factory correspondence | Compile the selected private representation and run the focused field/stride/factory observations on the actual Debug/Release runtime lane. A changed field set or supported lane requires repricing; the current 48/64 records cannot qualify hypothetical fields. |
| Original-owner and terminal-return closure | A source-complete phase/last-access proof across close, fresh/restore unwind, last U/R/C, permit consumption, default-domain close, and domain publication, followed by reached real lower-owner observations. Keep exact remaining debt when a later phase fails. |
| Actual producer R/P membership | A disjoint plan/bundle retained-versus-workspace ledger including Q and all still-live graph/control provision, preclaimed at real sites. The current constants are not a derived membership set. |
| Other addendum gates | Finite supplier/invoker/context/publication and native fault-capture funding, cleanup ownership, and the preparation/activation cap-description reconciliation remain as scoped there. No new runtime capability is asserted here. |

Do not proceed to final credit refund or Scene enablement if H is guessed, cell stride changes without admission, Q is omitted/self-funded, an actual graph has no original grant/root, C is refunded during construction, a revoked owner is reused, an uninstalled return is mistaken for installed-R rollback, progress phases can execute twice, the last access follows its grant refund, or completion/cleanup runs under a gate. Unreached OOM/native/interleaving stimuli and helper-only passes cannot close those obligations.

## 8. Input identity

HEAD: **`3e78cca23e633af10436db2a091f62736f70095e`**. Scoped raw-file SHA-256 capture began `2026-11-17T21:44:55Z`; the listed working-tree inputs, including pre-existing modified/untracked inputs, are the review basis, not HEAD alone. The addendum's working and HEAD Git blob are both **`455e6266add677d209043b912bfe954287d66b3d`**. Its earlier manifest and supplemental observations are historical identities, not a replacement for this input set.

```text
03efea672cd52e9ef053e9ce0285e7ca02c6ce6fb0d21268ade4255ed5a39d58  .github\workflows\ci.yml
1bce01871321c2df82e887da428ce5c121a6c6541fe65017fe3d249be47e190c  global.json
f043a352870efb6c7d4a61998225e2508a8f4370a57e784483a41819d452075f  docs\project\Building.md
1c8c3f8e140f47492255534284abbf67c5776210dc7a68f2c01155d0be773f46  docs\project\Roadmap.md
40f913a50434209e09941c22625a6d0722202bab97ebac0e153c1ea3368e2291  docs\design\02-dotnet-api-memory-and-ownership.md
a4d86f3fa373a64548435c53582bf5916e29d77f34650f9d161ccec90e6f0245  docs\design\04-capacity-and-workload-profiles.md
174b2465746da79cef560c8506216ef4f99f9d1aabac184f872c9e6cb0b94e0b  docs\design\12-entities-components-and-queries.md
b012f42b23cede9c7cdb71203d8357cd19e1626823ea25ef3119961292d3698a  docs\design\16-scenes-transforms-and-cameras.md
15848c82f3858a77a87a0d3ea6fbcc07046eb6e77ca5b21748c72b90efab598b  docs\design\30-public-api-review-surface.md
24eec6e92b7ad904d552f290ef748ffc8fce13b6b94a89ebd9f39487d4584d87  docs\design\35-implementation-roadmap.md
3eb20e1051c55ee77d008cf734007fde8c28aec24abd9755f4d1788475bd4605  docs\proposals\m03b-scene-preparation-finite-envelope-and-return-addendum.md
9d7280a89c5bb75890adaf65e26f8c80766b8687ee0608623d0f97d519be32c3  docs\proposals\m03b-scene-preparation-workspace-closure.md
3f8e4f0315c6cc2933fc982f64d5e94b9070eb1876a74b0688118bb17aff5a62  docs\proposals\m03b-scene-preparation-budget-accounting.md
692f721bead29e5ec258bf7b1844ec785e2d1548e9f53dfa9bdb57a22a01f09f  sources\TerraFX.Runtime\Entities\World.cs
ac53bbc6892880dcf0fd12541471f7099956fb0704116177cbcd5b1dc6df2a53  sources\TerraFX.Runtime\Entities\World.PersistenceAdmission.cs
2ce1c0b90157ade6f82605409ab599f7415b7d8aa0a63a4bf4327263610248d9  sources\TerraFX.Runtime\Entities\WorldAdmissionDomain.cs
ea012b9b40acedaa820885eafe27dcd11df55f842b68f2849785dab756e8e3bc  sources\TerraFX.Runtime\Entities\WorldDomainReservation.cs
965c6afb05a49b80c9e81c6147734de57f7d01a1aa9dd739e777ea7be4940ade  sources\TerraFX.Runtime\Entities\WorldSceneStorageReservation.cs
1a6dd92185afe246187c09b09789fc9cddfc195e6c71f9eeb57f264e59092e53  sources\TerraFX.Runtime\Entities\WorldGuidRegistry.cs
7f47be8c4943ce6fc2610fd98d3d2b7470b8f83ea00ec5d9c6c4a3f5c46a9beb  sources\TerraFX.Runtime\Entities\WorldLifetimeControl.cs
c6f41de02f0896016fa55845a61661bcd1a4dfe085bf68302767fe9c94da3228  sources\TerraFX.Runtime\Entities\WorldLifetimeControl.CosmeticCredits.cs
1849eae697239482076f7ec3a1ea02dd6b3348094dad6cb63668bf57fdb20529  sources\TerraFX.Runtime\Entities\WorldGuidRetirementPermit.cs
f3345fceb06beea0da4b89e2cf3106b1f60717a71c810ad3c34477762c6964f9  sources\TerraFX.Runtime\Entities\WorldGuidUseControl.cs
a6120452989f29ec2ef9c2b90e7479cc6635b9b17848c6c683063b3f99d36d22  sources\TerraFX.Runtime\Entities\WorldGuidUse.cs
bcf8f830fbb29e652d480608a3bdfc197d89d7e58299622941e87685b4c2aad4  sources\TerraFX.Runtime\Entities\WorldDefaultDomainOwner.cs
3a6e51a8099482a0fca32f7faac3bd55cfc7c53d233fc88b1bd369dedb32c066  sources\TerraFX.Runtime\Entities\WorldConstructionPlan.cs
b73dd4314b63084132afc2f9325ed98eeccce65a174f02b16f2ded7ec65f688c  sources\TerraFX.Runtime\Scenes\PreparedScene.cs
ec26b6cc6da73ad054583ccc653cf58e45c3630662f642d8c79ef70bd7a21ea4  sources\TerraFX.Runtime\Scenes\ScenePrefabExpansionPlanner.cs
68fe3ac1048423917a1eb64327457cb2d2a3eb95c8792c9536f30ac948192581  sources\TerraFX.Runtime\Scenes\SceneExpansionPlan.cs
4ac8757b98fb6602e11fbe7fbfc12212d849bab471592cf8796bdf6b77a9e67b  sources\TerraFX.Runtime\Rendering\RenderSceneAssetBundle.cs
e390b147ce7fd0a62bc9b210249a41ddf93a5cc1db365b69bf293be11dada3ab  tests\TerraFX.Runtime.Tests\Entities\WorldAdmissionDomainTests.cs
f52196a4ea7f99ab47931b436cf7ce9512d7a10f8ada80860d6928da90f552ae  tests\TerraFX.Runtime.Tests\Entities\WorldLifetimeControlQuarantineTests.cs
6c987ab754e5e6fa92480bc35c8f449ed08bed3fa548692f46030cafdfee1420  tests\TerraFX.Runtime.Tests\Entities\WorldGuidRegistryTests.cs
2728f61bcdaf6ebaaaaf670f8d355e5fb3300700294a27cfa9c1e1cb44937d5b  tests\TerraFX.Runtime.Tests\Scenes\SceneSupportRegistrationTests.cs
9d804ef67925074dc4ccf8bdcc413edff3e876b547d55be2b7c9e08d315d291b  artifacts\tst\Debug\tagoo_GOODING-ZEN4_2026-11-17_13_36_39_net11.0.trx
d85b5c0c8cc5df1e4464217a699f5cb3ca44ea7e9f6c25fe712b220d81d2eb12  artifacts\tst\Release\tagoo_GOODING-ZEN4_2026-11-17_13_36_43_net11.0.trx
4a7925cd9acf93e920e4e6f5c6a50ee31fee7cb526f3fa0d5a839cc201bca69d  artifacts\tst\Debug\m03b-scene-owner-measure-debug-20261118.trx
52a6ebba34fbeb7bdcd2350d076ed9289ba97ba7a1ed52922c6e224b0b27ca3a  artifacts\tst\Release\m03b-scene-owner-measure-release-20261118.trx
```

Recheck these hashes and HEAD before applying a disposition; changed inputs require reassessment. This is a scoped proposal-input list, not a transitive build/runtime receipt or repository inventory. Only this new proposal is authored; source/tests, maintained contracts, roadmaps and existing proposals are untouched. No commit, amendment, push or remote publication is part of this task.
