# M05 skinned-normal diagnostic retained-byte admission

## 1. Disposition and bounded scope

**Tentative budget/ownership addendum, not an accepted contract or a numeric admission profile. Retained-byte admission remains open.**

Recommend central checked admission for **every** record retained by Core Diagnostics, with separately funded producer formatting, immutable replacement workspace, and operational World attribution. Keep the existing public `Diagnostic` / `DiagnosticsService` surface. Do not equate allocation traffic with retained size, allocate an assumed 8-MiB arena, exempt non-M05 records, or obtain Rendering emission authority from the World capability.

The [telemetry owner-closure proposal](m05-skinned-normal-diagnostic-telemetry-owner-closure.md) remains unchanged. Its proposed code, format, positive-only Warning behavior, submission/draw key, host injection and publication lifetime remain tentative. Its earlier aggregate-eviction finding is corrected by **`667cb5710c6b99c4face24587442f45b9753886e`**; its byte-accounting and counter-saturation observations remain unresolved. This addendum supplies a more precise admission design and identifies what must still be measured or amended before allocation-capable implementation.

No source, test, maintained contract, prior proposal or protected fixture is changed. No build/test or new allocation probe is run. Existing TRX files are read as evidence, not regenerated or treated as complete native/AOT/consumer or diagnostic-budget qualification.

## 2. Current evidence

### What the new measurement establishes

`0d9349e6f31317c5c8767ed2b03fd99e6a13f2b7` adds only `DiagnosticsServiceTests.MeasuresBoundedWarningRecordAllocationTraffic`. The frozen focused Debug and Release TRX files ending **`14_19_57`** and **`14_19_59`** each record **one selected/executed/passed framework test**, reporting:

| Runtime / path | Producer formatting + Diagnostic construction | Warning Write admission | Combined allocation traffic |
|---|---:|---:|---:|
| .NET 11.0.0-rc.3.26522.148 / X64, Debug | 1088 bytes | 488 bytes | 1576 bytes |
| Same runtime/architecture, Release | 1088 bytes | 488 bytes | 1576 bytes |

The earlier Debug/Release TRX files ending `14_19_05` / `14_19_12` contain ten executed DiagnosticsService tests each and the same helper output. They corroborate the observation, not an upper bound or full Core-suite result. All four artifacts are frozen in §8.

The helper (`tests\TerraFX.Core.Tests\DiagnosticsServiceTests.cs:171–240`) creates a capacity-four service and warms another warning **before** its counters start. It formats two Guids, two maximum signed-long values, two six-digit counts and a 110-character `string.Concat` identity; creates six context entries and a Diagnostic; then measures one first admission of that distinct warning. It passes test session/owner identities to the current Write path.

Consequently:

- The first delta combines strings, any selected Concat/lowering temporaries, context construction/copying and the source Diagnostic. It does not isolate a Diagnostic object.
- The second delta covers the current first-warning append/copy path with **OccurrenceCount=1**, not a 19-digit occurrence, coalescing, eviction, snapshot copying, a critical fault, service construction, attribution controls or Renderer integration.
- No retained graph, maximum simultaneous roots, generic long-string/context domain, cold initialization bound, contention peak or new internal control layout is measured. Equal Debug/Release deltas do not qualify another architecture, AOT or every runtime lowering.
- **Neither 1088, 488 nor 1576 is D_P, R_P or W_P. Dividing 8 MiB by 1576 is not a capacity derivation.**

### Current source and owner boundaries

| Frozen input | Exact consequence |
|---|---|
| `sources\TerraFX.Core\DiagnosticsService.cs:14–85,143–163,274–380` | Reservation still covers `256 + ArrayCharge(capacity, Unsafe.SizeOf<DiagnosticEntry>())`, not referenced Diagnostic/context/string graphs. Warning identity remains the existing code/session/owner comparison. Eviction now adds the complete aggregate occurrence count with saturation. Neither saturation helper publishes `DiagnosticCounterSaturated`. |
| `Diagnostic.cs:33–141`; `DiagnosticContextEntry.cs:17–30` | Valid Code, Operation, Message, CommandIdentity and context strings have no length ceiling; context count has no service-byte ceiling. Diagnostic copies context entries but retains string references. There is no public emitter, so constructor validity is not service admission or a memory grant. |
| `DiagnosticsService.cs:71–110,145–163` | First-warning append creates an extended context and CopyDiagnostic invokes the public constructor, which copies again. Coalescing creates a builder/backing, replaces the occurrence string, moves that backing, then copies context into a new Diagnostic. The old immutable record remains needed during construction. |
| `DiagnosticsService.cs:166–251`; `DiagnosticSnapshot.cs:22–47` | Polling builds an array of existing Diagnostic references, then snapshot construction copies those references again. Earlier snapshots retain old payloads after service replacement/eviction. Close detaches the ring under the gate but clears the detached array **after** releasing it; detachment is not yet release of that array's record roots. |
| `ReentrantGate.cs` | Core already serializes synchronous service operations using an embedded reentrant spin gate and a ref-struct scope. This is not a byte claim or a latency guarantee. Preserve the no-copy-after-use invariant; no additional waiting publication queue is needed. |
| Chapter 04, diagnostic-budget paragraph; Chapter 28 §1 | The host ceiling is **8388608 bytes**, not a preallocation. Actual retained World-attributable diagnostics obey the exact configured World sublimit; snapshots are caller-owned observations. Per-World attribution is not a second physical allocation. Byte and count limits both apply. |
| `World.cs:556–611`; `WorldSchedulingLimits.cs`; `WorldConstructionPlan.cs`; `WorldDiagnosticsCapability.cs` | The World limit is in `[4096,8388608]`; current admission computes only a slot-derived reservation, with zero diagnostic-service physical reservation for a shared service. No cross-service retained-byte account is transferred. The capability has no byte ledger and may not be repurposed as a Rendering publisher. |
| `Renderer.CpuExtractionSupport.cs:16–28,53–56`; `RenderSnapshot.cs`; `Renderer.Submit.cs:164–195,829–856` | Renderer has no diagnostics/account input, snapshot has no operational quota account, and the diagnostic path still constructs the unapproved typed results. The 16-MiB readback argument is not a complete completion/publication ledger. No current source implements this proposed telemetry admission path. |

Chapter 28 §3 and Chapter 35 keep target allocation/lifetime observations distinct from unit tests. Chapter 29 keeps Core BCL-only and requires explicit higher-owner bindings; friendship is not issuer authority. The namespace discrepancy between Chapter 30's Diagnostics declarations and current Core namespaces is unchanged and outside this addendum.

### Existing Write callers cannot be omitted

The current Core Write references include `WorldDiagnosticsCapability` and Hosting's `EngineHost`, `EngineHost.GraphicsRecovery` and `PlatformInputRoutingOwner`. These all need central retained-graph charging, not just a branch for `DegenerateSkinnedNormal`.

- `EngineHost.cs:728–750,853–916` constructs World-tagged scheduler warnings and forwards startup/mode/captured-input Diagnostics. Scheduler messages include formatted debt; forwarding does not prove a fixed payload bound.
- `EngineHost.GraphicsRecovery.cs:35–60` retains adapter-name context. `PlatformInputRoutingOwner.cs:828–860` passes `exception.Message`; its later helpers accept message/context/reason data and construct variable context arrays. Their lengths must be measured from the actual immutable values or bounded by their owners, not assumed equal to M05's format.
- The World capability's **actual source callers** include capacity/control warnings, subscriber-delivery diagnostics and `World.cs:2279–2314`'s Error fault using a validation message or `exception.Message`. `World._lastFault`, control receipts and delivery reports can retain Diagnostics independently of the Core ring. Their owner budgets/results cannot be erased by telemetry refusal or automatically classified as caller-only storage.
- Chapter 29's stated World-capability permission is narrower than those current source uses. This source observation grants no additional authority; resolving that pre-existing issuer discrepancy is not part of this byte-admission implementation authorization.

## 3. Record charges: exact shape, unqualified layouts

Use the previous proposal's fixed M05 format, including `1 <= TestedVertexCount <= min(Mesh.VertexCount, group.IndexCount) <= 262144`. No new threshold, count membership or public payload is introduced.

The retained warning has one Diagnostic and one seven-entry context backing. Its bounded string-reference edges are:

```text
context key lengths:   10,  6, 18, 14, 17, 21, 15
context value maxima:  32, 32, 19, 19,  6,  6, 19
other string maxima:   Code 23, Operation 20, Message 65, CommandIdentity 110
```

That is **18 string-reference edges and at most 452 UTF-16 code units**: 904 logical character bytes, not a managed-object size. Ten edges are fixed literals and eight are variable-format values, including the service's occurrence value. Equal values, literals, cached small-number strings or shared references can reduce actual distinct objects; no cache/sharing credit is assumed in the conservative charge.

For qualified allocation profile P:

| Term | Required meaning | What is numerically established now |
|---|---|---|
| D_P | Complete escaping Diagnostic object extent, excluding separately charged referents | Open. `Unsafe.SizeOf<Diagnostic>()` is reference width; 1088/488 do not isolate this object. |
| T_P(n) | Checked complete string-object upper charge for length n, including applicable header/terminator/alignment | Open. The measured helper covers only selected strings/paths and a one-digit occurrence. It proves no generic length/large-object formula. |
| Q_P(c,s) | Complete managed array charge at **allocated capacity** c and actual managed stride s | Open as a qualified physical upper bound. Source's `64 + roundUp16(c*s)` is a conservative accounting rule, not evidence for every CLR array layout. |
| s_context,P | Actual managed `DiagnosticContextEntry` stride | Two references are visible in source; the corresponding numeric managed stride was not logged. |
| E_P | Actual managed stride of the service's complete ring entry | Source uses `Unsafe.SizeOf<DiagnosticEntry>()`, but these TRX files print no result. Do not sum nullable/native field sizes or use Marshal.SizeOf as a substitute. Recheck after internal representation changes. |
| A_P | Proposed per-payload private retention control plus conservatively covered attribution-control ownership not charged elsewhere | Open. Neither control exists in the measured path; zero allocation there is not A_P=0 for the proposed implementation. |
| R_P | Complete reserved ownership for one retained M05 payload, below | Open pending these terms; not 1576 bytes. |

Retain the previous proposal's fixed worst-case reservation policy:

```text
R_P = D_P + Q_P(7,s_context,P)
      + sum(T_P(n) over the 18 bounded edges) + A_P
```

Reserve the 19-digit occurrence extent from the first admission; do not lower R_P for short values and later grow without a claim. This is deliberately conservative reserved headroom, not a report of duplicated physical string storage.

For generic records, compute the same kind of **length-dependent** graph charge. Include all four scalar string fields when present and both strings in every context entry. For a Warning, use the retained context length `n+1`, the reserved key and up-to-19-digit occurrence string; coalescing derives its replacement shape from the **first retained payload**, not a smaller incoming context that the current coalescer ignores. Nonwarnings do not gain occurrence metadata.

Count aliases conservatively per retention owner rather than introducing an unbudgeted global object-identity table. Distinguish reservations from actual unique physical backing when reporting ownership. The same Diagnostic can be shared with a caller/World result or another service; repeating a conservative reservation does not establish that the object was copied or physically allocated twice.

The prior proposal's illustrative **2384 context/string-envelope bytes** relied on unqualified header/alignment assumptions. The new traffic observation neither validates nor replaces that envelope. No numeric R_P, E_P, maximum retained-row count, or minimum one-record fit is established here.

## 4. Concrete proposed preclaim/refund mechanism

The following is a **conditional implementation design**: its arithmetic and state transitions are defined, but no allocation is authorized until the profile terms and attribution lifetime in §§5–7 are closed.

### Service ledger and private retention ownership

Keep the existing synchronous Core gate. A service owns:

```text
F_P       = qualified service/control backing + Q_P(N,E_P)
            + reserved polling workspace + bounded scalar saturation/fault controls
live      = sum(R(node) for distinct service-owned retained payload controls)
preparing = complete in-progress candidate and temporary construction claims
mandatoryUnused = unspent reservations for reviewed mandatory fault/saturation payloads
0 <= F_P + live + preparing + mandatoryUnused <= service limit
N <= 8192; host service limit = 8388608
```

Ordinary telemetry cannot consume mandatoryUnused. A reviewed mandatory producer transfers its existing reservation into preparing/live; it does not add the same grant twice. Its required old/new headroom and replenishment policy remain part of the unclosed fault/saturation profile in §7, not a guessed slot size. For a standalone service, use its exact lower admitted limit; select backing capacity before allocation. Include record/workspace headroom when deriving capacity. The current slot-only `GetMaximumRecordCount` is insufficient. Neither the host ceiling nor this ledger requests an 8-MiB byte array.

Propose one **private Core retention control per admitted payload**, containing the Diagnostic reference, its immutable reserved charge, optional attribution-account reference and bounded service-root state. Ring entries reference that control. The independent latest-fault slot may reference the same control: ring eviction then removes only the ring root, not its charge while the fault root remains. A control belongs to one service; another service creates its own funded control. No global deduplication index is needed.

This adds no public field/member/type. Public Diagnostics and DiagnosticSnapshots contain data, **not** the control/account or a service/World reference. The control's charge is part of A_P; the actual entry representation and E_P must be qualified after this change, even if a reference merely replaces the current Record field.

### Checked synchronous sequence

1. **Validate and size without allocation.** Preserve argument/reserved-key precedence. Inspect only immutable Diagnostic fields, lengths and entry values, not exception getters or arbitrary objects' ToString methods. Calculate a lower array/object extent first; reject an already impossible context count without scanning/allocating a second collection. Accumulate every term with checked arithmetic and early budget comparison. Check `n+1` before narrowing to an array capacity. Overflow/unknown profile is not a zero charge.
2. **Determine the complete peak.** Under the service gate, locate the operational coalescing target and keep it charged. Set `candidate = R(new retained payload)` and `temporary = W_path(shape)`. Do not subtract the old payload or assume ring eviction will free an independently fault-retained payload. M05's proposed key and generic/World key rules remain the previous proposal's separate review item; the measurement did not implement them.
3. **Make service room without borrowing other budgets.** For an individually admissible new payload, evict permitted oldest ring roots until slot/byte requirements fit. On World-quota pressure, only local roots for that same account can free its quota; do not call/evict another service. Preserve the coalescing target while building its replacement and independent fault roots throughout. Recompute actual released reservations, not merely removed slot counts.
4. **Preclaim both ledgers before owned allocation.** For nonnegative charge b, test `b <= limit - used` after validating `0 <= used <= limit`; only then add. Under the service gate, reserve `candidate + temporary`. For an attributed payload, atomically reserve the complete candidate charge against the shared account using the same subtraction test and CompareExchange. A failed account reservation rolls back the service claim. CAS retries require constant storage, not another queue or a second service gate; no fairness/latency bound is inferred.
5. **Construct synchronously within that serialized scope.** Append/copy/coalesce only after those claims exist. No await, callback, World/Hosting lookup or native wait occurs under the Core gate. This keeps the old retained record stable without adding an out-of-gate pin/history/generation protocol. Producers waiting to enter still own/fund their input Diagnostics; their memory is not silently assigned to Core's one active workspace.
6. **Commit without allocating.** After full construction/validation, install the new control and transfer its candidate service/account claim into retained ownership exactly once. Replace a warning in its original admission position; update occurrence/coalesced counts only on successful replacement. Add the fault root when applicable before releasing the previous one. The local claim consists of scalar ownership flags/amounts; no heap-allocated reservation object precedes its own funding.
7. **Release after actual last engine use.** Clear old controls/data/account references and end every temporary consumer before refunding their grants. A coalesced old record held by a caller snapshot is caller-owned data, not a retained Core control. If a candidate fails before commit, discard its roots, refund only claims still locally owned, retain the previous aggregate and count the refused ordinary occurrence. Preserve producer argument/invariant/operation failures; do not disguise them as successful empty output.

All additions, subtraction, capacity products and refunds are checked. Accounting counters cannot saturate or wrap; their invariants are not observational telemetry. Ordinary refusal increments the existing saturated DroppedCount without altering the independent operation result. Eviction uses the now-correct entire aggregate OccurrenceCount; coalesced/dropped saturation still needs its separate bounded producer closure.

No claim may be released because its wrapper was disposed while an engine continuation still consumes its data. Conversely, explicit detachment/end-of-use, not a GC callback, ends engine retained ownership. This bounds admitted rooted ownership, **not** GC heap occupancy, allocator fragmentation, CLR metadata, outstanding garbage or process RSS.

## 5. W_P and old/new/snapshot coexistence

W_P is not the measured 488-byte Write traffic and not an unexplained fixed padding constant. The current source establishes the following **allocation/retention sites**, whose complete target-specific capacities must be qualified:

| Path | Coexistence that the claim must cover |
|---|---|
| New warning | The producer Diagnostic/context/format strings remain producer-funded. Core constructs the extended context, then the retained Diagnostic and its copied context. Candidate R covers the final graph/control; temporary W covers the extended input backing and any additional proven lowering/runtime scratch. Shared strings are not deep-copied by CopyDiagnostic. |
| Coalescing | Old Diagnostic/context/occurrence and the new complete immutable graph coexist. Keep old R while reserving new R. W additionally covers the builder object, its allocated backing moved as temporary input, constructor-copy overlap and actual extra lowering scratch. Only the occurrence value changes; first-payload strings are shared, not multiplied allocation traffic. |
| Generic Warning | Size the temporary and final arrays from the retained/incoming shape appropriate to the path, not six/seven entries. No assumption about M05 maxima may bound generic messages, keys, values or context capacity. |
| Nonwarning / critical admission | Current Write retains the supplied Diagnostic rather than cloning it. Full graph ownership still needs a candidate claim. The proposed private control and independent fault-root bookkeeping also need funding. |
| GetSnapshot | The builder/reference backing and the snapshot constructor's copied reference backing coexist. Reserve the temporary builder/backing service workspace for the maximum admitted query before consuming the service's remaining space; the returned snapshot object/final array are caller-owned output. Shared Diagnostic payloads are not copied again. |
| Close / retirement | `_records = null` does not release the detached array still held by Close. Preserve record/control/account and ring-backing charges through actual clearing/end-of-use. Refund each final retained node once; clearing a second fault/ring alias must not double-refund it. |

For the source-level first-warning and merge paths, necessary workspace terms include:

```text
W_append(n) >= Q_P(n+1,s_context,P)
W_merge(k)  >= qualified builder-object charge + Q_P(k,s_context,P)
```

These are **necessary terms, not complete upper bounds**. The final Diagnostic/context and occurrence string belong to candidate R; source-context and builder/construction copies cannot also disappear from the peak. Any extra compiler/BCL lowering backing must be enumerated rather than assigned zero because the aggregate sample happened to fit.

Keep a qualified maximum polling-scratch reservation within F_P so an otherwise full ring does not prevent its normal bounded copied observation. Admission/coalescing use a dynamically checked `preparing` claim, not a preallocated maximum-size buffer. Because the service operations are serialized, their active workspace need not be summed as if all ran concurrently; simultaneous producer-owned work and other services remain separately funded.

Caller snapshots may retain arbitrarily many historical versions across calls. No finite host-service budget can bound that permitted caller retention. During coalescing, charge old/new **engine** coexistence; after detachment, preserve the snapshot's data without continuing to debit the service. Engine-retained results such as World._lastFault/reports are not automatically caller snapshots and need their own owner claims.

M05 producer formatting is a separate `PublicationWorkspace_P` term in the proposed 16-MiB aggregate completion ledger. Claim it before graph acceptance, beside actual raw readback/leases/tasks and all other completion roots. Stream one positive row after complete raw-row validation. Do not hold service retention claims across the GPU await, construct a D-sized Diagnostic array, or fund formatting from unused host-ring/World/GPU/preparation credits. The current isolated helper does not establish this publication peak or its compiler/async roots.

## 6. Per-World quota across services

**No current account/API or measured control proves this boundary.** Supplying the current Write session/owner operands, or keeping a semantic WorldGuid in a snapshot, neither reserves bytes nor shares an atomic quota. A dictionary keyed by WorldGuid would also confuse distinct operational generations and add unbounded metadata/lifetime obligations.

The smallest recommended change remains a **Core-owned internal non-emitting account**, created/bound by the existing World construction path to its actual operational owner/session and exact configured diagnostic limit. It exposes only bounded debit/refund operations. It contains no live World, service locator, higher-layer callback or authority to emit/close services. Rendering receives it only through the explicitly reviewed extraction/snapshot/submission handoff, never through `WorldDiagnosticsCapability`.

Its numeric invariant is:

```text
U_world = sum(unrefunded attributed retained grants + preparing grants
              + unspent attributed mandatory reservations
              across every service and any other admitted engine diagnostic owner)
0 <= U_world <= exact MaximumDiagnosticBytes
```

Use the same account for a historical snapshot submitted through another host, including a standalone-World snapshot submitted through a native Renderer. Bind operational identity at issuance; do not infer a new account from Diagnostic.WorldGuid or use that semantic filter as the coalescing key. Two operational Worlds with the same semantic GUID have independent accounts. World disposal/replacement or closure of one service cannot reset grants retained by another.

For example, if two services share an account, simultaneous claims that would jointly exceed its limit cannot both succeed. Closing service A refunds only A's actually released grants; B's remain. A copied DiagnosticSnapshot may keep data but must not keep an account/control grant alive. A Submit already admitted before render-snapshot disposal retains its independently funded account ownership through publication/cleanup.

Account metadata itself is not free:

- Qualify the fixed account/control charge K_P and its source-World, render-snapshot, active-submit and retained-control roots. Each receiving owner must reserve its metadata coverage **before** taking a reference; retain old coverage until that handoff completes. Conservative full-account coverage may be included in A_P for each retained owner rather than introducing a global alias registry; report that as reserved headroom, not repeated physical allocations.
- World/snapshot/submit owners fund their own account roots under their existing budgets. A Core control must not retain an Entities domain reservation/callback to accomplish that handoff. Root closure needs explicit release/transfer, not GC discovery.
- Every existing World-attributable producer must supply the actual account through its owning boundary. Scheduler warnings currently pass null legacy identities; some input-route warnings pass only WorldOwnerId. Neither is permission to omit their quota. Host-only records still spend the service ledger. Missing attribution is an unresolved producer port, not a fresh quota or silently cleared WorldGuid.
- The World capability's real fault/control/subscriber callers and World._lastFault/report retention require an explicit attribution/owner disposition as well. Service refusal must not prevent the independently required World fault/result from being retained; its mandatory bounded shape/funding cannot be borrowed from an ordinary ring grant.

The scalar CAS algorithm is derivable; **K_P, complete root funding, construction/handoff ports and generic independent-owner grants are not implemented or qualified**. Review those exact internal declarations/lifetimes and measure their actual controls before claiming cross-service compliance. This addendum does not create an operational World-use/receiver credit or consume concurrent M03-B allowances.

## 7. Remaining owner decisions and smallest additional evidence

The strongest supported result is a checked, all-callers admission design with **uninstantiated numeric terms**, not a completed byte ledger.

| Open item | Smallest required next measurement/change; not performed here |
|---|---|
| D_P / T_P / Q_P | Extend the existing isolated helper with escaping, rooted component measurements: a Diagnostic with preconstructed inputs; actual context arrays/copies; dynamic string extents and relevant padding/large-object boundaries. Separate retained roots from dead input arrays/Concat temporaries. Correlate with a retained-graph inspection or qualified runtime allocation/layout evidence for the pinned target; further aggregate GC deltas alone do not prove physical retained size. No private CoreLib-layout guess or `Unsafe.SizeOf<class>()` charge. |
| E_P | Log the existing `GetOwnedByteReservation(8192)` result; with its current source formula, `(result - 256 - 64) / 8192` yields the runtime-selected entry stride because the payload is a multiple of 16. This observes that stride, **not** a qualified array header. Repeat after changing the entry/control representation; no public inspection API is necessary. |
| A_P / K_P | Specify the minimal private retention/account fields and exact issuer/root transfer first, then measure escaping controls and whole allocated backing. The current helper has neither. Close each World/snapshot/submit/service root and independent-result budget before creating it. |
| W_P / generic domain | Observe first append and actual coalescing with retained old snapshots; 1→2 and 18→19 occurrence-digit transitions and saturation; context growth/copying and large admissible generic payloads. Confirm selected compiler/BCL lowering/capacities and actual last roots. Qualify maximum polling copy scratch. No allocation inside a measurement may be excluded merely because it is temporary. |
| Generic retained admission | Add the central checked length/capacity-derived barrier before **any** Write retention, warning replacement or latest-fault assignment, while leaving the public constructor's validity contract unchanged. Existing callers already construct Diagnostics before Write: their pre-Write formatting/construction stays under their producer owners, not retroactively within Core's retention budget. |
| Critical and saturation retention | The existing arbitrary Diagnostic shape cannot guarantee an independently durable critical payload of arbitrary size inside 8 MiB. Owners must close bounded mandatory fault and `DiagnosticCounterSaturated` producer formats plus old/new reserved capacity, or review an exact alternate retention contract. Do not silently drop/truncate a critical fault, invent a replacement code/size, or assign an unmeasured fixed slot. Correct arithmetic in `667cb571` does not close this requirement. |
| Admission/refund behavior | Later tests must exercise production preclaim failure before allocation, exact-fit/one-byte-short **above the existing baseline**, coalesced old/new peak, fault/ring aliases, generic oversized strings/context, construction failure, account contention across two services, actual Close clearing, and caller-snapshot survival. Arithmetic helpers or a nominal ring count are not these observations. |

The reviewed owner amendment must explicitly authorize the private ledger/control/account handoffs and conservative reservation policy, fund polling/transition workspace within the existing ceiling, and close mandatory fault/saturation behavior. If a supported generic/runtime extent cannot be qualified, that extent remains unsupported for admission until a reviewed bounded representation/profile exists; it is not silently admitted as zero bytes or exempted. No arbitrary smaller public constructor limit or new public API is recommended.

The 8-MiB retained ceiling and proposed 32-/16-MiB diagnostic envelopes do not rise. Ordinary telemetry can be visibly refused; mandatory compute/readback/completion workspace cannot be omitted to obtain a successful graph. Native/AOT/public-consumer, full-operation memory and milestone acceptance remain separate open gates under Chapter 35.

## 8. Fresh input freeze and preservation

Freeze: **2026-11-17T22:36:02.2872914Z**. Current HEAD: **`1ace85441823cc3f99359102840d54080afe31db`**, branch `tannergooding-m05-implementation-plan`. This includes the two supplied commits and the concurrent `1ace8544` World preclaim-order correction. The frozen World source below includes that correction; this task makes no source change.

The previous telemetry proposal remains byte-for-byte **`DB4ACD6583E5341F347F56E71A4D8118E953A39A373C1CAD31DEFD0170981E78`**. Its old implementation manifest is historical; this addendum freezes current inputs afresh rather than claiming the changed Diagnostics source still matches that old manifest.

The bounded review input is this addendum plus the **44 full files** below. C = tracked/clean, M = tracked/modified, I = existing ignored TRX artifact. Input-manifest SHA-256: **`4300588A67B17C8733829E827834B3307B02EDF0970AE90A4277CAEFF7024BE5`**. Canonicalization: ordinal Windows-path order; `State<TAB>Path<TAB>UPPERCASE-SHA256<LF>` encoded UTF-8 without BOM; each file digest covers raw bytes without normalization.

At the input freeze, dirty safety state was **79 paths, 60 tracked modifications and 19 untracked files**, no staged changes. Fingerprint: **`BD2C18EC77F241B367F4A4B06C6BA7C601B1CC013D88C0D3470F75929BE5ECB5`**. Canonicalization: all those dirty files in ordinal Windows-path order, `two-character git porcelain-v1 status<TAB>Path<TAB>UPPERCASE-SHA256<LF>`, UTF-8 without BOM. This records unrelated work without making it review input.

Concurrent Scene-storage work subsequently changed three pre-existing dirty files and added `WorldAdmissionDomain.SceneStorage.cs`. These are **outside the 44 assessment inputs** and were not edited or used as authority by this task. At **2026-11-17T22:43:31.9133322Z**, excluding only this addendum, the dirty snapshot was **80 paths, 60 tracked modifications and 20 untracked files**, fingerprint **`E30A062363A45EF63A59499C67FFCE3E92493EC9A9FFA1E7E7AECFB94FE9BDD3`** under the same canonicalization. The observed concurrent-file hashes were:

| Concurrent non-input path | Observed SHA-256 |
|---|---|
| `sources\TerraFX.Runtime\Entities\WorldAdmissionDomain.cs` | `BFFC521906A0A902081AE9CB92FD23F3787B1241D515EECEBF8F6A1084B072A4` |
| `sources\TerraFX.Runtime\Entities\WorldDomainReservation.cs` | `6CB07C538151E25728D0A1A316B9CC4EAE231EA849C719FBF44112DEE00B9422` |
| `sources\TerraFX.Runtime\Entities\WorldSceneStorageReservation.cs` | `CE475A3B02912FC8AA35FB5123B2398F36B6A7CC48027D489EEDEC1008177604` |
| `sources\TerraFX.Runtime\Entities\WorldAdmissionDomain.SceneStorage.cs` | `43ECCCE2919BCB2A4B47406077C0A9F98BDD4937DEB8F637E8C5C2BEB63D4246` |

Recheck HEAD and every assessment-input hash before disposition. Preserve concurrent work rather than restoring an earlier workspace fingerprint; the two safety snapshots are point-in-time records, not a claim that external writers stopped. No scratch/evidence copy or extra manifest is written. Relevant input changes invalidate this freeze; unrelated changes do not become accepted review inputs. This task's only file delta is this new addendum, whose own digest is reported separately after final verification.

| Input | State | SHA-256 |
|---|---|---|
| `.github\copilot-instructions.md` | C | `12439DF4BB5E46021891B234BEC1104ABDCA57C11FDF3F610C8F32C5DAF3001E` |
| `Directory.Build.props` | C | `770EE4B2A422861A6F23F0D39DD0ECA361A81CB87E64EBAC4E12170F71231D7A` |
| `artifacts\tst\Debug\tagoo_GOODING-ZEN4_2026-11-17_14_19_05_net11.0.trx` | I | `230BDBAB108C701B8321A7C5810809A9ED310B4164BEF83ECC8564A8316F02B7` |
| `artifacts\tst\Debug\tagoo_GOODING-ZEN4_2026-11-17_14_19_57_net11.0.trx` | I | `BB1F35236C66657828E2202B476DC9EBC12458C1D9DE56ED3C9B1C2C84921780` |
| `artifacts\tst\Release\tagoo_GOODING-ZEN4_2026-11-17_14_19_12_net11.0.trx` | I | `23F37AB38A0F6CA2CB9083CC87575AF7FAB857AFF8AFA677AC9F16019D876B52` |
| `artifacts\tst\Release\tagoo_GOODING-ZEN4_2026-11-17_14_19_59_net11.0.trx` | I | `01913BDB446A5872B7A16AE8B2694167C687FD5D90424B185582F68681DF1736` |
| `docs\design\04-capacity-and-workload-profiles.md` | M | `A4D86F3FA373A64548435C53582BF5916E29D77F34650F9D161CCEC90E6F0245` |
| `docs\design\10-binary-data-and-authoring-formats.md` | C | `56721136AECAE18C20EB3E9A1BECEB527CC0676DD6B43CB506538AD63DECBB5F` |
| `docs\design\20-animation-and-deformation.md` | C | `BBEF0A36E0832C2BD0D54A07E479917DB923671B64F7176369F8F57A8F1618C5` |
| `docs\design\28-diagnostics-performance-and-validation.md` | C | `C9260CD18A905E087DFC47D07AF67BD2477996232DA773B69A0E4C3B7A64D90F` |
| `docs\design\29-project-setup-build-and-deployment.md` | C | `3B55A013EEC327ECD648ACBD5B53717DD218AD288E8047AC21277609EFA2F47B` |
| `docs\design\30-public-api-review-surface.md` | M | `15848C82F3858A77A87A0D3EA6FBCC07046EB6E77CA5B21748C72B90EFAB598B` |
| `docs\design\35-implementation-roadmap.md` | C | `24EEC6E92B7AD904D552F290EF748FFC8FCE13B6B94A89EBD9F39487D4584D87` |
| `docs\project\Building.md` | C | `F043A352870EFB6C7D4A61998225E2508A8F4370A57E784483A41819D452075F` |
| `docs\project\Roadmap.md` | C | `1C8C3F8E140F47492255534284ABBF67C5776210DC7A68F2C01155D0BE773F46` |
| `docs\proposals\m05-skinned-normal-diagnostic-budget-evidence-rectification.md` | C | `A8DBD9EE4EA443245391A3B8AE17EDE03E3D71DA3F824E9128641E1C94F4DB36` |
| `docs\proposals\m05-skinned-normal-diagnostic-telemetry-owner-closure.md` | C | `DB4ACD6583E5341F347F56E71A4D8118E953A39A373C1CAD31DEFD0170981E78` |
| `global.json` | C | `1BCE01871321C2DF82E887DA428CE5C121A6C6541FE65017FE3D249BE47E190C` |
| `sources\TerraFX.Core\Diagnostic.cs` | C | `B827997B35EE8ACCCCFA069905865C1E155508401FE7256EACBD0B20E2A6B840` |
| `sources\TerraFX.Core\DiagnosticClockDomain.cs` | C | `D194906D28D774DF01A83DC7C554E0AF82645DE90FA9931D415DBD02134BD680` |
| `sources\TerraFX.Core\DiagnosticContextEntry.cs` | C | `C2A1E74E0AE9929638C9255CFAA8DD7235CE37509483660B740569B775BEF530` |
| `sources\TerraFX.Core\DiagnosticQuery.cs` | C | `E8574D8DA53A59F71557EAB8FF101F8EE56A9CB126AC26614CE4D7F6795F6DE2` |
| `sources\TerraFX.Core\DiagnosticSnapshot.cs` | C | `916F5FB2AD709C4F0EF12FC83FF8E997619F1C1B9F6B52234655E91D45EFC242` |
| `sources\TerraFX.Core\DiagnosticsService.cs` | C | `6B2606EDC15849EF83CABBE4E561F2ABCB51954505F363785585D447823C705A` |
| `sources\TerraFX.Core\ReentrantGate.cs` | C | `8F0EF274C39C3E07E691951011AC86C574311912D7F78348EB8D6780555F0C71` |
| `sources\TerraFX.Core\TerraFX.Core.csproj` | C | `BDA8230D77298EE852B28ED8446C66E7ADD9227F6AD9285087E3408F351E0088` |
| `sources\TerraFX.Core\WorldDiagnosticsCapability.cs` | C | `77D3CBC729325420643EAF196C7B30AB6EBD178A883CFB417CC64E7E41644A5E` |
| `sources\TerraFX.Graphics\RenderingSchemaCatalogue.cs` | C | `DE25A8C0E249FF9AB5B592FE848C85429C004FE629694C64EF1AFEF3BA731614` |
| `sources\TerraFX.Runtime\Entities\World.Controls.cs` | C | `4DD4B837C256D0FF5F120CCD4F71EC1BA6A732C41A64484E1375DC1F931402FF` |
| `sources\TerraFX.Runtime\Entities\World.OwnerControlAdmission.cs` | C | `FD7F0B0F15181CE6D94EE9CB1903B94AED8C7FFD3C96A60A16FE459D1D892003` |
| `sources\TerraFX.Runtime\Entities\World.OwnerControlApply.cs` | C | `50A932F1DC1657B8BAC458580B28F9E8856FB39A80027DA7BF38A15449157DF9` |
| `sources\TerraFX.Runtime\Entities\World.OwnerControlRequests.cs` | C | `DF45B48E8A7E570616B7BF511E0E4D72CC509A54AA79A823B79310B9D08FD9BB` |
| `sources\TerraFX.Runtime\Entities\World.Publications.cs` | C | `8673BECCDC1CAB9A1B65C5B3936C2E5E9E98C0C0937EB2EA750AF7F2D766A21B` |
| `sources\TerraFX.Runtime\Entities\World.cs` | M | `52935AF5D9494BA8B7C5C650B2C52DC5849F3CBD5EAEE43524E5543C1F87F8AD` |
| `sources\TerraFX.Runtime\Entities\WorldConstructionPlan.cs` | C | `3A6E51A8099482A0FCA32F7FAAC3BD55CFC7C53D233FC88B1BD369DEDB32C066` |
| `sources\TerraFX.Runtime\Entities\WorldSchedulingLimits.cs` | C | `497B007892C2F5E2C7B87665DF15656ED0788DC810C4FB37D2213719F22DDF61` |
| `sources\TerraFX.Runtime\Hosting\EngineHost.GraphicsRecovery.cs` | C | `CC03DB8E1296B8A1C4CD1FAAC37F33BF4455D424C3EE66C6EC5DFE481B89B8D6` |
| `sources\TerraFX.Runtime\Hosting\EngineHost.cs` | C | `A3FB2415E31CB7B717F2AD382E1FACF874B6AC392A469B6E71BA198DF6A11743` |
| `sources\TerraFX.Runtime\Hosting\PlatformInputRoutingOwner.cs` | C | `524BC0FADDC5FE75BF7536B5DD78F6ADB43EBA471EE05D603FABCA53976E02B3` |
| `sources\TerraFX.Runtime\Rendering\RenderSnapshot.cs` | C | `733C8A58053977A349742540639FC55AF0CD299DB33C7394D1DC999A17D30AF0` |
| `sources\TerraFX.Runtime\Rendering\Renderer.CpuExtractionSupport.cs` | M | `EAF1F94AFF0886B3FA30AF97BD7F83180A38AB744D4241D0CDD275A055EADB55` |
| `sources\TerraFX.Runtime\Rendering\Renderer.Extract.cs` | M | `274298BFDCEE321141D4138D417807951FCC0EF6BCAC1DFCE2726D5F36829F2D` |
| `sources\TerraFX.Runtime\Rendering\Renderer.Submit.cs` | C | `8BF0D0662C9D3DE0A1670ED05C89FAB1E6A749D744BF796A9D5E0236BF4DDB79` |
| `tests\TerraFX.Core.Tests\DiagnosticsServiceTests.cs` | C | `EE7D1A804BBBD5D4B9E80E069F46204C2F16F21EF63107E90DEEA6DFAD9DBFE9` |

Chapter 10 and the Rendering catalogue freeze the bounded Mesh-schema context; no schema/fixture version is changed or accepted. The TRX files remain the only execution evidence examined. Proposal formatting/hash checks are not additional test runs or acceptance.
