# M05 Diagnostics component evidence and admission sequence

## 1. Disposition

**Tentative evidence and sequencing addendum, not a qualified byte-admission profile or M05 acceptance.** The next production rectification remains a central, all-record retained-graph preclaim/refund barrier in Core Diagnostics, not a smaller slot count or an exemption for non-M05 records. The new measurements narrow its inputs; they do not supply all its charges or resolve mandatory fault retention.

The [retained-byte admission proposal](m05-skinned-normal-diagnostic-retained-byte-admission.md), especially §§3–7, remains the conditional admission/ownership design. The [telemetry owner-closure proposal](m05-skinned-normal-diagnostic-telemetry-owner-closure.md) remains the proposed M05 format, operational key, injection and publication-lifetime authority. Its earlier aggregate-eviction defect description is historical: current `Write` adds the evicted aggregate's entire occurrence count with saturation. This addendum updates only the component-evidence status and next work; it neither replaces those proposals nor accepts their unqualified illustrative CLR sizes.

The user's tentative-approval instruction permits dependency-ready work in this direction; it does not turn unknown footprints or undefined mandatory payloads into funded storage. This task adds only this proposal. No source, tests, maintained design contracts or previous proposals are edited; no build/test, commit or push is performed.

## 2. Exact evidence and limitations

At HEAD **`e64a4f7b5174fbd6cde439a06f881170d2f57672`**, `DiagnosticsServiceTests.cs` matches that commit. Both supplied TRX files report NUnit Adapter 5.2.0.0, `net11.0`, and runtime **`.NET 11.0.0-rc.3.26522.148/X64`**. Debug and Release each discovered/selected **2**, executed **2**, passed **2**, failed **0**. Exact membership, under `TerraFX.Core.Tests.DiagnosticsServiceTests`:

- `MeasuresBoundedWarningRecordAllocationTraffic`
- `MeasuresDiagnosticRootAndContextCopyAllocationTraffic`

These are two framework measurement tests per configuration, not two conformance cases or the whole Diagnostics/Core suite. The artifacts and source are hashed in §6; the TRX files themselves do not attest a test-binary/source hash.

| Identical Debug/Release observation | Supported interpretation |
|---|---|
| Capacity 8192 source reservation **393,536 bytes**; entry stride **48 bytes** | The getter computes `256 + 64 + roundUp16(8192 * 48)`. The test constructs a **capacity-four** service; it does not allocate/inspect a full 8192-entry ring. The stride is derived from the current source accounting formula using its runtime `Unsafe.SizeOf<DiagnosticEntry>()`, not a measurement of array headers or complete service retention. Requalify it after representation changes. |
| Diagnostic construction with preconstructed inputs: empty context **216 bytes**, seven entries **352 bytes**; delta **136 bytes** | Warm per-record allocation-traffic averages over 1024 iterations. These isolate construction much better than the older combined sample. They are candidate evidence for the Diagnostic root and seven-entry copy path, not a retained-graph census, a generic array formula or an independently measured context-entry stride. |
| Separately constructed strings: length **19 → 64**, **32 → 88**, **110 → 248 bytes** | Warm allocation averages for `new string('x', length)` over 1024 iterations at each named length. They do not establish all formatting paths, all lengths, alignment/large-object boundaries, other runtimes or NativeAOT. |
| Producer formatting/construction **1088 bytes**; first Warning `Write` **488 bytes**; combined **1576 bytes** | The existing capacity-four, warmed-service experiment, including six producer entries and first admission with `OccurrenceCount=1`. Still not coalescing, a maximum-width occurrence, saturation, eviction, polling, Close, attribution controls or Renderer publication. |

The new component helper preconstructs its scalar strings and input contexts before counting, keeps only the last constructed object alive explicitly, and logs averages rather than asserting numerical upper bounds. It does not retain 1024 simultaneous graphs, identify all surviving objects, or measure new ownership controls. The 136-byte difference must not become the charge for every context array. The three string samples must not become a general `T_P(n)` law.

Thus the earlier proposal's “no logged component/entry observations” gaps have **partial new evidence**, not qualified values for every target. Do not substitute `216`, `352` or `1576` for the complete retained M05 charge, sum traffic as if all allocations survived, or divide 8 MiB by any of these samples to derive admission capacity. The 256-byte service-control constant is also still a source reservation, not newly measured object extent.

## 3. Why admission and ownership remain blocked

| Direct source / owner authority | Consequence for the rectification |
|---|---|
| `Diagnostic.cs` and `DiagnosticContextEntry.cs` validate content but impose no string-length/context-count byte ceiling; the Diagnostic copies context entries and retains string references. `DiagnosticsService.GetOwnedByteReservation` charges only control/ring backing. | Constructor validity is not a memory grant. Every retained severity/path needs length-dependent graph admission, including all present scalar strings and context keys/values; M05's fixed format cannot bound arbitrary engine diagnostics. Producer construction before `Write` remains producer-funded. |
| `Write` retains a nonwarning directly, assigns `_latestFault` for Critical, and constructs immutable Warning replacements. Coalescing derives its payload from the **first retained record**, not the incoming context. | Put the barrier before every retention/latest-fault assignment and service-owned allocation. Fund old/new coexistence; do not size a replacement from a smaller ignored incoming payload. Preserve the existing World warning key; M05's separate proposed key is not implemented by these tests. |
| `GetSnapshot` and `DiagnosticSnapshot` create overlapping reference-array backing. Close detaches the ring under the gate, then clears its roots outside it. | Fund service-side polling scratch; returned snapshot data is caller-owned. Ring detachment alone cannot refund live detached backing/controls. Ring and latest-fault aliases must not double-charge/refund the same service-owned payload; eviction alone cannot refund a still-fault-retained payload. |
| Chapter 04's diagnostic paragraph, Chapter 28 §1, and Chapter 30's Diagnostics declarations/lifetime prose | Keep the 8192-record cap **and** fixed 8,388,608-byte host ceiling and each exact configured World sublimit. Snapshots do not extend the service charge. Ordinary loss is counted and cannot replace an operation result; the independent durable critical fault and bounded `DiagnosticCounterSaturated` observation are mandatory. No new public emitter or constructor restriction follows from this proposal. |
| Current `WorldDiagnosticsCapability` carries a service and operational identity, but no shared byte account. The retained-byte proposal identifies additional independent World diagnostic roots. | A WorldGuid filter or legacy Write identity is not a quota reservation. Close the proposed non-emitting Core account's issuance, cross-service grants, metadata funding and last-use transfers; do not repurpose the capability as a Rendering publisher or assume M03-B receiver credits fund it. |

**Distinguishing source-level example, not a newly executed test:** an otherwise valid Information Diagnostic with a 4,194,305-character Message has **8,388,610 logical UTF-16 character bytes alone**, already beyond the host ceiling before headers/context/control. Current `Write` can retain it in one slot without changing its slot-derived reservation. A smaller ring or a fixed M05 row charge does not fix that defect.

Applying ordinary refusal indiscriminately is not a safe solution either. The public Diagnostic shape permits an arbitrarily large Critical record, whereas Chapters 28/30 require independent last-fault retention. Saturating the existing counters also currently emits no `DiagnosticCounterSaturated` record. A finite ledger cannot promise arbitrary mandatory payloads, and newly introducing refusal can reach this unresolved saturation path. Neither silently losing/truncating faults nor treating these paths as zero bytes is authorized.

## 4. Next concrete rectification, in dependency order

**There is not yet a supported allocation-capable admission implementation based on these measurements alone.** The first ready batch is the bounded owner/profile closure below, followed by one coherent production barrier change. Do not repeatedly rerun the unchanged two helpers as a substitute.

| Step | Concrete work and exit condition |
|---|---|
| **1. Close mandatory shapes and ownership** | Specify the actual internal mandatory Critical and counter-saturation producers: exact retained code/fields, bounded strings/context, oversize policy, independent caller/World result ownership, and first/replacement/replenishment reservations. Include an old fault that remains ring-rooted and a nonrecursive saturation publication path. Demonstrate fit at applicable exact lower budgets, including the 4096-byte World minimum, together with required backing/workspace. If this requires a bounded projection or changed retention semantics, supply that exact owner amendment; this addendum does **not** invent a summary code, truncation policy or exemption. |
| **2. Instantiate the proposed private controls and target profile** | Specify the retained-byte proposal's minimal private payload-control/account fields and creation/transfer/release sites before measuring them. Bind the account to the actual operational World issuance, with no live World/callback/service-locator dependency; include World, extraction/snapshot, active publication, service and independently retained result roots identified by that proposal. Returned DiagnosticSnapshots carry data, not account/control grants. Establish qualified upper charges for the Diagnostic, actual context/reference-array capacities and managed strides, strings over the admissible length range, service/control/account objects, polling scratch and append/merge temporary backing. Correlate escaping-root observations with retained-graph or qualified target allocation/layout evidence; more aggregate deltas alone are insufficient. Reuse the new samples as checkpoints, not portable constants. |
| **3. Implement all-path preclaim/refund together** | Once steps 1–2 supply complete bounds, apply the prior proposal's checked synchronous design inside the existing service gate. Validate and size without owned allocation; retain old/fault charges; evict permitted local ring roots; preclaim complete candidate plus temporary service storage and the applicable shared World grant; construct; install without further allocation; release only after actual last engine use. A failed World claim refunds the service claim. Preserve old aggregates on replacement failure and count the refused occurrence. Account counters are exact checked arithmetic, not saturating telemetry. Include nonwarnings, latest-fault aliases, polling, actual Close clearing and mandatory reservations; an M05-only branch cannot establish service compliance. |
| **4. Bind M05 publication only after service closure** | Preserve the previous proposal's fixed warning format/key, streaming whole-readback validation, borrowed service injection, non-emitting source-account handoff, and joined publication/stop order. Independently fund producer formatting/record construction and continuation ownership inside the proposed **16-MiB aggregate completion envelope** before graph acceptance. Service bytes, World quotas, GPU storage and the separate 32-MiB preparation envelope are not spare publication credit. The current two Core tests establish none of these Runtime/native paths. |

Steps 1 and the concrete field/root design in step 2 can proceed without enabling new retention. This is bounded closure work for the existing route, not a new diagnostics framework, global object-identity table, separate allocator, public API or repository-wide qualification pipeline. Conservative per-owner/edge reservation from the previous proposal remains permissible once its charges are qualified; it must not be reported as duplicated physical allocation.

**Retry production admission when** the exact mandatory record/replacement policy, control/account root transfers and qualified complete profile/workspace terms are available and fit the applicable ceilings. If a necessary term cannot be bounded, identify that term/path as unsupported and keep the affected integration blocked; do not install a partial ledger under a compliance claim. **Retry live M05 publication when**, additionally, the actual service behavior, Renderer handoff/drain and whole completion peak are demonstrated. This proposal supplies no waiver of those missing inputs.

### Focused validation for that later implementation

Use the existing Core tests and ordinary `dotnet test` workflow from `docs\project\Building.md`, Debug before Release, with nonzero selected/executed membership. Add behavior cases alongside the actual implementation, not dormant test-only producers:

- Exact-fit and one-byte-short above the measured existing baseline, with setup reaching the intended boundary within the legal configured domain; generic oversize strings/context and checked overflow must refuse before service-owned allocation/retention.
- First Warning append and coalesced old/new peak, occurrence-width growth through the 19-digit limit, construction/claim rollback, whole-aggregate eviction and bounded saturation publication.
- Ring/latest-fault alias eviction and replacement, detached-ring clearing, caller snapshots surviving coalescing/Close, and no refund while an engine owner still uses the graph.
- Two services sharing one operational World account, concurrent near-limit claims, one service/World closing while another retains grants, and independent Worlds with equal semantic GUIDs remaining distinct.

The frozen 2/2 selections did **not** execute these admission/ownership cases, including the existing aggregate-eviction regressions elsewhere in the test file. Managed behavior results, target layout/retention qualification, NativeAOT and real Renderer/public-consumer observations remain separate evidence.

## 5. What can proceed

| State | Scope |
|---|---|
| **Ready within existing authority** | The concrete mandatory-shape/control/profile closure above; dependency-ready M05 CPU/layout/adapter work and M03-B preparation/receiver/cleanup fixes that do not depend on this missing admission path or borrow its unqualified credits. Chapter 35 has no blanket M03/M04 prerequisite for all M05 work. |
| **Still blocked** | Claiming complete Diagnostics host/World byte enforcement; switching on this proposed Renderer telemetry route without its service/account/publication closure; treating arbitrary mandatory faults as ordinary discardable telemetry. |
| **Separate, still open gates** | The project roadmap's full 32-/16-MiB M05 ledgers and actual producer/lifetime prerequisites, plus applicable native/AOT/public-consumer and milestone acceptance. The component measurements close none of these gates and do not qualify full M05 or M03-B. |

## 6. Frozen direct inputs

Freeze: **2026-11-18T02:16:46.7039717Z**. HEAD: **`e64a4f7b5174fbd6cde439a06f881170d2f57672`**. Branch: `tannergooding-m05-implementation-plan`. Review scope is this additive proposal plus the **23 full-file byte hashes** below; Chapter 04/30 hashes intentionally include their existing dirty content. Other concurrent dirty work is preserved and is not assessed here. No schema/fixture or test-binary bytes are used as direct inputs.

C = tracked/clean against HEAD; M = pre-existing tracked modification; I = existing ignored TRX. Input-manifest SHA-256: **`B3CCF9A36F7812F3EBBEEB7C26F0A6EA6FC42C386921266FF82A0E8C2D95F813`**. Canonicalization: ordinal Windows-path order; `State<TAB>Path<TAB>UPPERCASE-SHA256<LF>` encoded UTF-8 without BOM. File hashes cover raw bytes without normalization. Recheck all assessment hashes before disposition/implementation; acceptance applies only to those bytes, not historical manifests in the older proposals.

| Direct input | State | SHA-256 |
|---|---|---|
| `.github\copilot-instructions.md` | C | `12439DF4BB5E46021891B234BEC1104ABDCA57C11FDF3F610C8F32C5DAF3001E` |
| `.github\workflows\ci.yml` | C | `03EFEA672CD52E9EF053E9CE0285E7CA02C6CE6FB0D21268ADE4255ED5A39D58` |
| `Directory.Build.props` | C | `770EE4B2A422861A6F23F0D39DD0ECA361A81CB87E64EBAC4E12170F71231D7A` |
| `Directory.Build.targets` | C | `AEEC1D08E8037470239035812351C4045124607D67A354D482112F4D642D4105` |
| `artifacts\tst\Debug\m05-diagnostic-components-final-debug.trx` | I | `90FE4A5A77F281596AFFFD948C18788AD59FCC87A03E3A5D7AEEBC936EB0AA75` |
| `artifacts\tst\Release\m05-diagnostic-components-final-release.trx` | I | `31C0246B2230670BFE19B6FCEAC3C3FF99F3403C5251F3DCBD1CDC179C4062AF` |
| `docs\design\04-capacity-and-workload-profiles.md` | M | `A4D86F3FA373A64548435C53582BF5916E29D77F34650F9D161CCEC90E6F0245` |
| `docs\design\28-diagnostics-performance-and-validation.md` | C | `C9260CD18A905E087DFC47D07AF67BD2477996232DA773B69A0E4C3B7A64D90F` |
| `docs\design\30-public-api-review-surface.md` | M | `15848C82F3858A77A87A0D3EA6FBCC07046EB6E77CA5B21748C72B90EFAB598B` |
| `docs\design\35-implementation-roadmap.md` | C | `24EEC6E92B7AD904D552F290EF748FFC8FCE13B6B94A89EBD9F39487D4584D87` |
| `docs\project\Building.md` | C | `F043A352870EFB6C7D4A61998225E2508A8F4370A57E784483A41819D452075F` |
| `docs\project\Roadmap.md` | C | `1C8C3F8E140F47492255534284ABBF67C5776210DC7A68F2C01155D0BE773F46` |
| `docs\proposals\m05-skinned-normal-diagnostic-retained-byte-admission.md` | C | `A5297E203DB144D38DD1F4F47F1EFCE1601F785DABB3E3A18585EDC117F5DB96` |
| `docs\proposals\m05-skinned-normal-diagnostic-telemetry-owner-closure.md` | C | `DB4ACD6583E5341F347F56E71A4D8118E953A39A373C1CAD31DEFD0170981E78` |
| `global.json` | C | `1BCE01871321C2DF82E887DA428CE5C121A6C6541FE65017FE3D249BE47E190C` |
| `sources\TerraFX.Core\Diagnostic.cs` | C | `B827997B35EE8ACCCCFA069905865C1E155508401FE7256EACBD0B20E2A6B840` |
| `sources\TerraFX.Core\DiagnosticContextEntry.cs` | C | `C2A1E74E0AE9929638C9255CFAA8DD7235CE37509483660B740569B775BEF530` |
| `sources\TerraFX.Core\DiagnosticSnapshot.cs` | C | `916F5FB2AD709C4F0EF12FC83FF8E997619F1C1B9F6B52234655E91D45EFC242` |
| `sources\TerraFX.Core\DiagnosticsService.cs` | C | `6B2606EDC15849EF83CABBE4E561F2ABCB51954505F363785585D447823C705A` |
| `sources\TerraFX.Core\TerraFX.Core.csproj` | C | `BDA8230D77298EE852B28ED8446C66E7ADD9227F6AD9285087E3408F351E0088` |
| `sources\TerraFX.Core\WorldDiagnosticsCapability.cs` | C | `77D3CBC729325420643EAF196C7B30AB6EBD178A883CFB417CC64E7E41644A5E` |
| `tests\TerraFX.Core.Tests\DiagnosticsServiceTests.cs` | C | `B9317C56A2C22BA557CE751A0748D2492E1B5866646D00151DCBD601F9ECF42A` |
| `tests\TerraFX.Core.Tests\TerraFX.Core.Tests.csproj` | C | `C9B400231C8FADC95C9A2EF1E018519E3337B3F1E4177B0E86F049D439928FD3` |
