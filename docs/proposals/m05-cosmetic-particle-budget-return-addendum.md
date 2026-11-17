# M05 cosmetic-particle budget and return boundary addendum

## Status and result

**Tentative owner/qualification proposal under the requested Sol maximum-reasoning policy. No source/tests or Scene execution.** This supplements the [pool owner](m05-cosmetic-particle-pool-owner-closure.md), [admission/lifetime](m05-cosmetic-particle-admission-lifetime-addendum.md), and [fault/output](m05-cosmetic-particle-fault-output-addendum.md) selections.

The selected counts yield the finite logical maxima below. They do **not** establish a numerical construction footprint, a 64-KiB accepted-operation implementation, or a qualified producer-return boundary. Actual private owner/operation/root layouts do not yet exist; unrestricted Task observers are not bounded by the 128 request credits. No larger allowance, free framework category or sampled maximum closes those gaps.

Keep the affected construction/publication path fail-closed until its actual storage catalogue and return/transfer facts close. The component/State2 15/15 Debug and Release evidence remains component evidence only.

## 1. Existing mechanisms and ownership partition

Chapter 04 owns shared-state/domain limits and backing/metadata accounting. Chapters 13/16 own single-marker publication, read generations, complete coexistence reservation and detached retirement. Chapters 21/30 own particle/output behavior; Chapter 35 separates compiled roots, managed behavior, actual consumers and qualification.

Use the existing Entities-owned `WorldDomainReservation`, exact `WorldGuidUseControl`, issuer-mediated workspace reservation/transfer/return and retained ownership controls. Do not give Rendering the registrar, create a global particle allocator, or register the pool as canonical Resource/System/Attachment state.

| Allocation/obligation | Real owner and end condition |
|---|---|
| Factory descriptions and immutable registration metadata | Their existing construction/registration owner; capture once. They are not per-World mutable backing or a source of runtime credits. |
| Fresh World cosmetic control, queue/cell backing, root/cohort bookkeeping and standing release tail | The actual World/domain reservation, charged before construction. Includes any changed World/control layout, lower stamps and failure/retirement provision. |
| Current/candidate/retired particle and emitter state; recording graphs and pin metadata | The World cosmetic storage ledger, including reservations and stopped-but-unreleased ownership. The proposed recording allowance is a subset of this ledger. |
| Accepted/prospective operation root, request pins, promise, result preparation and owned publisher/cleanup work | Its own exact use and derived workspace; retain the physical graph through actual last access or a proved ownership transfer. |
| Returned recording owner or copied M05 snapshot/output/pin graph | An independently retained receiver/storage interest through actual release. It must not depend on active-World authority or keep the entire mutable pool/command queue alive merely to access assets. |
| Decoded definitions, Atlas/frame arrays, textures, material values and immutable package backing | Their actual Assets owner/budgets. World-side lease wrappers, builders, resource-set controls and copies still have their own charge. Shared decoded bytes are not charged a second time per reference. |
| Nonadmitted scalar reply and caller-induced Task observer work | Only the narrowly proposed caller-observation/provenance partition, if accepted and proved at the actual sites. This does not exempt engine-created promise, registration, context, failure or release work. |
| Process U cells, domain interests, raw admission/growth metadata and quarantine | Their existing lower owners/capacity formulas. A particle reference to a control neither duplicates its physical charge nor makes the control free. |

`World.GetWorldOwnedStateBytes` currently sums existing owners, not a particle ledger. A real integration must join its new actual charges to World/domain admission and preserve `WorldDomainReservation.IndependentUseBytes`; adding an unused private counter or relying on a higher-level cap is insufficient.

## 2. Logical maxima derivable now

Use only already selected values:

```text
P = 131072 live particles          E = 1024 current emitters
Q = 128 pending capacity credits  S = 65536 accepted stimuli/segment
R = 4 retained recording outputs  C = 16 selected cameras
```

P/C are existing owner limits. E/Q/S/R remain the earlier selected tentative particle limits; this document adds no new count ceiling. Count maxima never promise joint byte/lease fit.

| Quantity | Derived upper bound and scope |
|---|---|
| Automatic requests in one tick | E, one nonzero request per enabled current emitter |
| Explicit requests evaluated in one tick | At most Q; credits still held by older publishers reduce the actual prefix |
| Total requests / requested particles per tick | `E+Q = 1152` requests; `(E+Q)*P = 150994944` requested particles before refusals |
| Actual new births / expired particles in one tick | At most P each; live capacity is checked after expiry |
| Birth-sampling RNG words | `3*P = 393216`; new-emitter seeding/initialization is a separate bounded row, not hidden in that total |
| Birth-generation membership of one live pool version | `P+E = 132096`: at most one generation per live particle plus current emitter generations |
| Generation membership of one recording segment | `P+E+S = 197632`: initial-particle generations, current emitters and at most one newly introduced generation per accepted stimulus |
| Particle/camera logical references in one extraction | `C*P = 2097152`; this is not a requirement to duplicate complete billboard records per camera |
| Atlas page/reference closure | Native Atlas currently admits at most 16 pages. Definition + Atlas + all pages + optional Sprite Material therefore selects at most 19 AssetId/backing keys per emitter resource closure. It does not bound physical wrapper replication. |

Unreferenced old emitter generations must not accumulate as tombstones. Current/referenced rows remain; a numeric generation cursor is not permission to retain all preceding generations.

### Version/coexistence counts are not global object caps

Four recording-output credits include prospective output ownership and closed-but-unreleased/failed-cleanup outputs until their actual end.

- Ordinary candidate preparation can have four returned recording graphs plus current and candidate active-segment views: at most six such views **before other retained retirements**. At the semantic maximum this is `6*197632 = 1185792` generation-membership positions, not necessarily that many distinct definitions or allocations.
- Rotation reserves its output credit before copying. With at most three earlier outputs, the old active segment is also the prospective fourth output—not an extra physical graph. Old outputs + old active + new active give at most five views. The new segment starts with zero stimuli and at most `P+E` generations, so the corresponding bound is `4*197632 + 132096 = 922624` membership positions.
- Additional old cohort/reader/cleanup graphs remain explicitly charged. Neither the four-entry presentation mailbox nor the recording-output limit bounds every old cohort or caller-held snapshot.

For `n` additional retiring graphs, include their **actual** capacities/children, not a guessed six-graph global limit. A byte ceiling implies a finite graph count only after every nonempty/control-only graph has a qualified positive minimum charge: `n <= floor(availableBytes/minimumCharge)`. No such private particle graph size is presently established.

Snapshots must copy/pin their required immutable billboard/resource content. Keeping root slot 0 merely as a convenient back-reference can also retain all recording and pending-operation state; that implementation needs the entire reachable graph charged and cannot use the recording-output count as its bound.

## 3. Exact admission accounting, not invented footprints

### Limits versus reserved storage

Preserve the proposed 128-MiB cosmetic-storage subceiling, its 64-MiB total recording subset, and the 128-MiB **shared additional M05 output** subceiling. They are quotas, not default preallocations, per-segment allowances or added World entitlements. Recording/retained/retiring bytes count together; other selected M05 outputs consume the same output quota.

Reserve the actual standing graph before construction. Reserve dynamic old/current + new + scratch + retained-reader/output + cleanup/retirement peaks before allocation/Prepare, through the actual lower issuer. Do not reserve a full 128-MiB purse and then charge transferred bytes again as though the destination had no existing reservation.

For disjoint accounting rows:

```text
World simultaneous charge =
    disjoint existing charge Bexisting
  + Pcharged                 # actual cosmetic graphs/reservations; recordings included
  + Xcharged                 # disjoint additional M05 output graphs/reservations
  + sum(256 + Wi - Ti)        # live operation uses; Ti is actually transferred, not yet returned
  + new lower metadata/backing not already included above
```

Here Bexisting excludes every P/X/new-operation/new-metadata row in this partition; do not add this expression to a current total that already includes them. Enforce the exact configured World limit and exact domain headroom at the same admission. Pcharged/Xcharged also obey their subceilings. Assets' decoded/backing charge stays at Assets; wrapper/retention copies belong to their actual P/X/operation row. Lower metadata page capacity/high water remains charged after a live slot is returned.

Expected per-request pool/recording pressure retains its selected whole-request refusal. If required whole-tick candidate/output/failure preparation cannot be reserved, use the selected World precommit failure boundary; do not freeze existing particles, skip output or manufacture a successful partial tick.

For W=65536, each original use reserves `256+W = 65792`; Q uses total **8421376 bytes before transfers**. This is reserved operation capacity, not a measured Task/pin footprint. If the eventual closed graph needs more than W, do not silently raise W; if it uses less, do not secretly request a smaller W to evade the selected admission boundary.

Current `TryReserveWorldGuidUseRaw` also needs two domain-interest positions and one domain operation credit per use, and the process path needs its U. Thus 128 successfully issued uses require capacity for **256 interest positions, 128 domain operation credits and 128 process U cells** in addition to the binding's existing roots/owner. They do not create 128 new World owners or prospective replacement P roles. Existing lower maxima, generations, raw-ticket availability and metadata growth can refuse earlier.

### Backing rules and candidate catalogue

Current `World.ArrayBytes` charges `64 + RoundUp8(checked(capacity*stride))`; the domain registrar's Chapter 04 `ArrayCharge` uses `64 + RoundUp16(...)`. Use the applicable existing owner's rule; do not silently interchange them or call either an observed GC allocation size. For example, on x64 one genuinely allocated 128-reference backing costs 1088 under the World rule; two such backings cost 2176. Neither number funds the operation objects they reference.

The compiled candidate must supply, for every row:

```text
creating site/type; actual capacity or finite multiplicity; full size/alignment derivation;
coexistence interval; exact preclaim/issuer; retained path; actual end/transfer;
all cold/native/runtime children; disposition on failure
```

Standing construction includes new World/control fields, cells/directories, source/stamp/root controls, mandatory abort/publication/retirement provision and any relevant lazy/native synchronization storage. A struct stride excludes separately allocated children; `Unsafe.SizeOf<class>()` is not instance size. Include spare array capacity, builder-to-immutable copies and old/new growth overlap.

For an operation, derive the maximum simultaneous sum of disjoint shared-promise, body/pin, preflight/scratch, staged-result, publisher/runtime and protected-failure/cleanup rows. Do not let body work spend protected failure/publication provision, or charge one live allocation to both W and a receiver. No numerical row is supplied merely because W is 64 KiB.

Exact-fit qualification instead uses the independently established **total already charged at the request boundary**, Bboundary, and only its additional reachable peak D: **Bboundary+D** fits, **Bboundary+D-1** refuses at that same reached boundary. Bboundary already includes current pool/output/operation/high-water rows. Transferring an existing allocation does not add its payload again to D; a genuinely new control/reservation does. Earlier setup must fit both trials. GC traffic, process-memory deltas and unrelated warm baselines are not D.

### Assets do not make retention wrappers free

`AssetLease.Retain` reserves another live lease and constructs a new handle plus `LeaseState`; it shares the decoded lifetime/backing. `RenderAtlasPageSet.Retain` retains Atlas/pages and constructs a builder, immutable page-handle storage and a new set. Its temporary/current copies and exceptions need rows.

The default per-store live-handle limit is 4096. For example, 1024 independently pinned worst-case 19-handle closures would require **19456 handles**, before temporaries/other consumers; E=1024 is not a promise that this fits the default store. Proven immutable resource-owner sharing can reduce handle replication; equal AssetIds alone cannot.

The existing binding validator returns at most 19 distinct IDs and disposes its leases. That list is not a retained birth-resource owner. Current Atlas loading additionally validates up to 1048576 frames with sort/overlap workspace. That supplier work belongs to its actual preparation owner, not to an imaginary constant “one selected frame” cost or the later spawn W.

## 4. Close the pending-credit bootstrap hole

Clarify the **same 128 capacity credits** to cover prospective construction as well as accepted settlement:

```text
prospective/bootstrap + queued/frozen/evaluating + staged/publishing/cleanup
                     + stopped-but-unreleased = at most 128
```

This refines the earlier append wording, not the numerical cap. Atomically accepted state/sequence/queue insertion still occurs only after successful preparation. Reserve a preowned standing capacity cell **without allocation before acquiring/constructing the operation graph**. A failed unescaped constructor returns that prospective credit only after its acquired graph is actually released. If cleanup is retained, the credit remains occupied despite there being no admitted command.

Otherwise repeated failures before append could accumulate uncounted use/TCS/pin graphs while “accepted pending” remains zero. Serial owner affinity bounds simultaneous entry, not accumulated failed cleanup. Do not add a free 129th bootstrap or quarantine allowance.

Expected refusal before operation construction remains the admission addendum's nonadmitted scalar-reply path; it consumes no operation credit/use/sequence. Its actual caller-observation ownership must be established, not inferred from Task terminality.

The lower `AcquireUse` path can itself fail during control/interest/process-cell construction and cleanup. The particle guard needs an exact lower **no-use versus still-retained acquisition-debt disposition** through the existing issuer. An exception type/message alone does not prove that no lower claim remains. Before permitting retry/refunding the prospective cell, prove that disposition or keep the affected ingress fail-closed with the real lower debt retained. This requires no new public API or fabricated grant.

## 5. Original-grant handbacks

| Boundary | Ownership/funding handback required |
|---|---|
| Unescaped entry -> queued operation | One construction guard owns the prospective cell and exact use until the actual complete operation is anchored. Enqueue transfers that role once; aliases do not acquire a second Dispose right. |
| Operation -> prepared birth/output storage | Move only actual covered bytes/ownership through the original issuer. Operation use decreases by transferred x while receiver storage increases by x, preserving the shared total. Retaining a second wrapper is a new allocation, not that transfer. |
| Temporary receiver ends while sender provision is still needed | Return the exact x to that still-live original claim through its proved backlink/issuer; do not release then reacquire headroom. A permanent independent transfer is different: do not recreate an ended use later. |
| Stage-8 prepared ledger -> stage-9 current roots | One callback/allocation-free marker transfer. Old current ownership moves to preowned detached retirement; it is not refunded until actual readers/cleanup end. On failure, abort only new ownership and preserve the selected World fault/cause. |
| Scalar result -> caller observation | No new resource authority. The shared promise remains funded until the engine's actual accesses end; explicitly identify any later caller-only storage transfer. Engine callbacks/continuations must not keep the World/pool alive through a supposedly scalar result. |
| Publisher body -> lower release tail | Propose a **World-owned, preclaimed standing releaser/close owner** for the exact remaining release/handback suffix. Derive its changed control/frame/runtime footprint; it is not free space in the existing 256-byte use control. The original operation stays funded until this actual transfer or its complete last access is proved. |
| Final use/pending-credit return | End/transfer every active workspace/pin/receiver obligation and owned producer/helper provision. Keep the real retained root through fallible lower release. A partial process/domain failure retains the actual remaining ledger; no false all-or-none refund or repeated completed destruction. |
| World close | Deny new admission; join/drain real work and release-tail roles. Actual independent recordings/snapshots keep their own storage interest through release after logical close. Do not retain operational World U solely for copied GUID metadata or wait circularly for caller disposal. |

Current workspace methods make these distinct operations: reservation/release moves between free and active workspace **without ending the prepaid use**; transfer decreases `OperationBytes` and increases receiver storage; return restores the original provision. Ending a subclaim is not refunding all `256+W`.

The proposed standing releaser must stay owned/charged through logical close until its real last use, via the existing World/domain lifetime/retention machinery. Merely moving a reference to a global list, new passive owner or unfunded “orphan tail” is not a handback. Requalify changed lower controls; the fixed charge for one existing type is not a general object allowance.

`WorldSceneStorageReservation` demonstrates issuer-mediated workspace/storage transfer and preserves its fields through failed release, but it is a Scene-owned mechanism with its own open receiver/return gates. Do not authorize particle/output storage by simply passing its borrowed Scene participant, or assume its after-close path qualifies a different receiver.

## 6. Why no complete finite executable envelope is established

1. **No final particle allocation graph.** The private standing/root/queue/operation/recording/output types, growth capacities, indexing replication and retention edges are not implemented. Counts and schema bytes cannot derive their complete transitive size. Missing bytes are an unproved bound, not zero.
2. **Observer multiplicity is not bounded by Q.** One exposed Task can receive N caller continuations, contexts and scheduler registrations. N is unconstrained by this engine profile; Q bounds producer operations, not observer-induced dispatch. If all resulting publication work is World-owned, no W bounded by these selected limits—and in particular no 64-KiB proof—follows.
3. **Ownership policy is necessary but not a size proof.** The narrow external-observer provenance rider can assign caller-induced registration/list/context/scheduler/dispatch storage to that caller. It cannot exempt engine-created TCS/Task base storage, owned callbacks/registrations, context wrappers, contingent/failure state or native copies. If that partition is not accepted, the publication/funding boundary must change before code can claim a finite World envelope; increasing W is not the rectification.
4. **Fault/context inputs need their own owner.** Preserve original foreign failure objects under their actual producer/input ownership. Any new engine-owned copy/capture, including reached exception dispatch/native stack work, needs a finite derivation. An input owner does not automatically pay for a new copy. If the copied dimension has no admitted bound, a revised input/funding/publication owner contract is required.
5. **Task-only readiness is not producer return.** The existing Scene return analysis records a component observation of Task terminality while `TrySetResult` remains in external scheduling. It is not a particle witness, but disproves terminality as a general end signal. No finite reuse deadline follows; timeout/canceled wait cannot free still-used storage.

Minimal required capability: for the actual candidate and chosen SDK/compiler/CoreLib/runtime/target/code-generation lane, identify a production-valid **post-last-access return or exact transfer to the named funded releaser**, covering all engine-owned callers, registration/dispatch/context/native suffixes and the release bookkeeping itself. A source `finally`, counter-zero, method-leave event or another Task cannot signal before an unproved suffix and then refund it.

A synchronous World-owned publisher with no engine subscription to its public Task may expose a simpler ordinary `TrySet*` return boundary than Scene's nested async suppliers. Prove that actual implementation independently; do not inherit either a Scene pass or a blanket permanent Scene dependency. If a remaining invoker has no existing handback/proved no-access suffix, the affected path stays unavailable until a budget-aware owner capability exists.

Use a complete source/compiled/runtime allocation catalogue and actual-return proof, corroborated by the target's verified allocation/native instrumentation. Sampled allocation ticks, warm zero-allocation calls, aggregate GC traffic and heap snapshots cannot supply those proofs. A per-allocation CoreCLR capability is not automatically NativeAOT/native-allocation coverage. Existing [allocation-evidence guidance](m03b-scene-preparation-allocation-evidence-capability.md) specifies that distinction; no new runner, tracing API or synthetic particle producer is proposed.

## 7. Qualification possible without Scene preparation

| Work | What it can establish without Scene execution |
|---|---|
| Compile/inspect complete private storage/credit/releaser candidate | Exact actual types, array capacities, closed roots, allocation sites and caller/return paths. Use ordinary Runtime builds selected by `global.json`, Debug before Release; freeze actual resolved toolchain/source/binary inputs. No invented F/W value is needed to compile unselected code. |
| Existing World/use/domain primitives and a funded complete new storage/credit part | Real issuer charge/transfer/return behavior, all 128 prospective/settling cells, exact-fit/short admission, freshness, failure retention and close. These qualify bookkeeping only; no fake emitter/output/context or no-op participant may turn them into birth evidence. |
| Existing package/codecs/lease retention with real native content | Their supported decoding/binding/lease behavior under actual Assets prerequisites. An isolated child result does not qualify its async producer tail or fund a World Scene supplier. |
| Actual accepted particle Task publication | Only when a real admission source/qualified operation exists. A pre-admission `EmitterUnavailable` reply or a separate TCS component probe is not this producer. |
| Nonempty stage-8/9 simulation, real birth-resource/output retention | Still requires actual prepared emitter/Scene/cohort producers and their J/K/supplier/receiver/return gates. Do not execute `SceneLoader.PrepareAsync` while its qualified profile is unavailable. |

Snapshot/native/AOT/workload/Physics observations retain their separate owners and prerequisites. Empty selected World behavior can be tested only for a complete real owner; an always-empty or always-rejecting stand-in is not that implementation.

## 8. Smallest code batch enabled

The next step is a **production-private storage/128-credit/original-grant handback candidate**, not the whole pool algorithm or public controller:

- Supply the actual closed storage/root/operation inventory, prospective-cell transitions and standing release-tail owner, using the existing lower World/domain issuers and no global allocator.
- Keep particle Task publication and Scene-consuming entry unavailable until their respective actual claim/return profiles close. Do not stub selected lifecycle methods or enlist an incomplete participant.
- Compile/inspect those final private types and call sites; derive the disjoint F/W/receiver/tail catalogue and the lower acquisition-debt disposition. Then run only genuinely funded real World bookkeeping/lifetime cases.

This proposal establishes **no numeric qualified construction or publisher profile**. Full live owner/Task/Scene execution remains NO-GO until the missing facts above exist. The adopted fault policy uses the existing World failure channel; that simplifies state semantics but pays for no allocation or release tail.

Only this addendum is added. No source/tests, maintained chapters or roadmap are edited, and no build/tests/Scene preparation execute this turn.

## Evidence boundary

These are inspected working-tree bytes, not a future candidate's layout or an acceptance profile. Reconcile changed inputs before applying a disposition.

| Input | SHA-256 |
|---|---|
| Chapter 04 | `A4D86F3FA373A64548435C53582BF5916E29D77F34650F9D161CCEC90E6F0245` |
| Chapter 13 | `0D7806B03A1D019215EB5C3912F10200A078F9B00A71A206FB979E93BBD18CDE` |
| Chapter 16 | `B012F42B23CEDE9C7CDB71203D8357CD19E1626823EA25EF3119961292D3698A` |
| Chapter 21 | `989A6183186592F165FA9614CC19A8FED63826E2A51170CE55FBFFFCA8116ED2` |
| Chapter 30 | `15848C82F3858A77A87A0D3EA6FBCC07046EB6E77CA5B21748C72B90EFAB598B` |
| Chapter 35 | `24EEC6E92B7AD904D552F290EF748FFC8FCE13B6B94A89EBD9F39487D4584D87` |
| Pool owner proposal | `888D97BA6BC9AC2F06295D725162074F85C275658CDB3D0DDF31B8DA89904A95` |
| Admission/lifetime addendum | `762F1E9643AC9EECDF7487AED28727262DF512DA7F51C04E3E194C239A8FC9BE` |
| Fault/output addendum | `6F06567667A184A9F1286F3C1DADAE862CCE8E360B8C37B45F8417593EE28C44` |
| Scene footprint proposal | `940BAEF78CCFBD720E292167C3AF9C7C00AABDBC4F448E6E4A00793BB54230DA` |
| Scene supplier-return proposal | `199158C2357AC6B3FE551F568C0DA69B739A0A30AFDFC2372113DD4C6597F8FD` |
| Allocation-evidence proposal | `D130242EEAB65921B9D6FF10D15E117AA0FC551503A0781D705E9856BABA08B9` |
| `global.json` | `1BCE01871321C2DF82E887DA428CE5C121A6C6541FE65017FE3D249BE47E190C` |
| `Entities\World.cs` | `692F721BEAD29E5EC258BF7B1844EC785E2D1548E9F53DFA9BDB57A22A01F09F` |
| `Entities\WorldAdmissionDomain.cs` | `E3FFB70F958230A89387ABFAA730213C574DF3850E617B83AB506A9C90F71691` |
| `Entities\WorldDomainReservation.cs` | `EA012B9B40ACEDAA820885EAFE27DCD11DF55F842B68F2849785DAB756E8E3BC` |
| `Entities\WorldGuidUseControl.cs` | `EBF8A3F7C1115538CF06379FA65AE339B783117E39F76D1DE61C46C0B5C40466` |
| `Entities\WorldLifetimeControl.cs` | `B1A9ADD0A1D68CAF4C385973CA4E2602C68E19E46078AA3CB09614848C09A634` |
| `Entities\WorldSceneStorageReservation.cs` | `965C6AFB05A49B80C9E81C6147734DE57F7D01A1AA9DD739E777EA7BE4940ADE` |
| `Assets\AssetLease.cs` | `A639A5949353F2FB594B2C57569B2491D08988BFBA48F551E666A36A6EA8D3DA` |
| `Assets\AssetLeaseLifetime.cs` | `2749B26E3A42809558558704B6D11C51E886AE40A933E29B7A205A10357881B2` |
| `Assets\AssetStoreOptions.cs` | `BB2123297D3E65309BD38CE5DA0083E14319810139FA233CA0B0EBC4E5404594` |
| `Rendering\RenderAtlasAssetCodec.cs` | `4F095E1DDDD3101C870D30948BEF101D0A9CE32C72E5C9BCF60900F2C78C6EB1` |
| `Rendering\RenderAtlasPageSet.cs` | `ABC653B3150C4C3BDE95A90FE39527D0F4B068C62DAD96577A183E2C681F82BB` |
| `Rendering\RenderAtlasPageLoader.cs` | `C8A80F5A957BA0E1DB6A56D390CCB04A8D807399AD18F4FB233297A10C8411BE` |
| `Rendering\CosmeticParticleEmitterAssetBinding.cs` | `76F8B6E03A575FCBAE18B4135AE3AD53E19FCD84D2F14F35A7E9DA15FAFDEA2B` |

`Entities`/`Rendering` paths are under `sources\TerraFX.Runtime`; `Assets` paths are under `sources\TerraFX.Assets`.
