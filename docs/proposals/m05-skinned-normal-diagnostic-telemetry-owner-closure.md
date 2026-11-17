# M05 skinned-normal diagnostic telemetry owner closure

## 1. Recommendation and authority

**Tentative owner-contract rectification, not an accepted contract, implementation, or qualification result. Recommend the existing `Diagnostic` / `DiagnosticsService` route, with the exact behavioral and budget amendments below, and no new public API. The existing route is not implementation-ready without those amendments.**

Publish a recoverable `DegenerateSkinnedNormal` warning for each completed, positive-count logical draw. Put the GPU count and its tested membership in fixed structured context, distinguish every actual submission, and retain the existing best-effort copied polling model. Do not promise a complete result array, observable zero rows, or lossless delivery. Successful submission means the prescribed readback, validation, and publication attempts finished, not that every warning survived retention.

This closes the decisions deliberately left open by [public carrier resolution](m05-skinned-normal-diagnostic-public-carrier-resolution.md). That proposal remains unchanged. The older [typed delivery proposal](m05-skinned-normal-diagnostic-delivery.md) is comparative design input, not authority for its public additions or stronger delivery guarantee. In particular, the pending `RenderSkinnedNormalDiagnostic` and `RenderSubmissionReceipt.SkinnedNormalDiagnostics` are **not** normative inputs. Their bytes are preserved and hashed only to identify the unapproved implementation dependency.

### Smallest required owner amendments, before implementation

| Owner | Exact proposed disposition |
|---|---|
| Chapter 20 §3, Animation/Rendering | Adopt §§3–5 below: logical-vertex membership, positive-only Warning telemetry, exact frame/draw/submission correlation, post-readback publication attempts joined by `SubmitAsync`, and explicit ordinary-telemetry loss. A readback/row-integrity failure faults submission; retention refusal does not. This is not a change to the normal algorithm. |
| Chapter 28 §1, Diagnostics | Adopt this code's fixed format and operational key, not the current World-warning key. Preserve the existing World warning key. Require full-record byte admission, immutable replacement, aggregate-aware eviction accounting, and the loss rules in §5. No public emitter, subscription, or new code is introduced. |
| Chapter 29, Core/Entities/Hosting/Rendering | Authorize only the direct borrowed service injection, non-emitting source-quota handoff, and close/drain order in §4. Renderer may publish only this closed diagnostic through that route and cannot close the host service. Do not extend or transfer `WorldDiagnosticsCapability`. |
| Chapter 04 diagnostic budgets; Chapter 28 retention | Confirm that strings, context backing, records, attribution controls, independent fault retention, and service admission/coalescing workspace are charged, not merely ring slots. Preserve the fixed host 8-MiB ceiling and exact per-World sublimits. Adopt the allocation/admission obligations in §6; there is no new retained-memory allowance or World-quota exemption. |
| M05 completion-budget owner | Include streaming publication/formatting and lifetime controls in the proposed **16,777,216-byte aggregate completion envelope**, beside actual raw readback and all other completion roots. The separate proposed **33,554,432-byte preparation/recording envelope** cannot fund it. These envelopes remain tentative and unqualified; their existing budget-evidence proposal remains NO-GO. |
| Chapter 30 public API owner | Confirm reuse of existing declarations. No new public declaration or receipt member is recommended. Its existing receipt fields keep their meanings; a GPU fence is not a presentation fence. Namespace/API conformance is not resolved by this proposal. |

These are proposed amendments, not edits made to the owner chapters. Tentative implementation authorization is limited to this recommended direction **after its missing owner semantics and funding are reviewed**; it does not waive those prerequisites. This task stops at the proposal.

## 2. Frozen evidence and what it establishes

Paths in this section are repository-relative. Line ranges refer to the workspace bytes frozen in §8, including modified and untracked inputs.

| Evidence | Finding and consequence |
|---|---|
| `docs\design\20-animation-and-deformation.md`, §3, especially lines 49–55 | The existing code name is `DegenerateSkinnedNormal`. A bounded compute pass counts the finite near-zero blended-normal condition; asynchronous counter readback follows its fence and carries frame/draw tags. Raster fallback has no storage writes. Neither a public result type nor lossless/zero-row delivery is specified. |
| Chapter 28 §§1/3; Chapter 04 lines 44–60 | Ordinary telemetry may coalesce/drop, with visible cumulative counters. The 8192-slot limit is additional to actual retained-byte limits. Snapshots are copied, nondestructive observations. §3 distinguishes owner/unit tests, native conformance, and target-qualified allocation/lifetime evidence; finite case counts are not arbitrary-work or allocation proofs. Its historical package-status prose is not evidence that current package code is absent. |
| Chapter 29 lines 15–41 and logical-layering rules | Core is BCL-only. Hosting owns its service; borrowed World capability authority is restricted to World subscription-capacity diagnostics. Same-assembly reachability and Core friendship are not producer authorization. Hosted Worlds retire before service closure. Already returned diagnostic snapshots outlive either owner. |
| Chapter 30 §7, lines 5220–5241; §10, lines 7696–7763 and following polling/lifetime prose | Renderer returns `Task<RenderSubmissionReceipt>`; the normative receipt has six scalar properties and an internal six-argument constructor, no diagnostic payload. Existing `Diagnostic` has string/context fields and `CommandIdentity`; `DiagnosticSnapshot.Records` supplies observation. Chapter 30 places Diagnostics in `TerraFX.Diagnostics`; inspected Core implementations currently use `TerraFX`. That discrepancy does not authorize a namespace refactor here. |
| `sources\TerraFX.Core\Diagnostic.cs:33–141`; `DiagnosticContextEntry.cs:17–30` | Constructor validation admits arbitrary nonempty Code/Operation/key lengths, arbitrary message/value lengths, and an initialized context array of arbitrary length. Context is copied, but strings are referenced. Constructor validity therefore does **not** establish bounded service retention. |
| `DiagnosticsService.cs:14–85,112–160,163–209,271–363` | The reservation is `256 + ArrayCharge(capacity, sizeof(DiagnosticEntry))`, not a charge for the referenced record graph. Warning lookup compares only `(Code, executionSessionId, worldOwnerId)`; Context, CommandIdentity, world/tick, device, and submission do not participate. Coalescing keeps the first payload/timestamp in place and replaces only service-owned occurrence metadata. **Current full-ring eviction increments DroppedCount by one even for an aggregate**, contrary to Chapter 28's all-occurrences rule. Its saturating increment also does not produce Chapter 28's required `DiagnosticCounterSaturated` observation. |
| `DiagnosticsService.cs:163–248`; `DiagnosticSnapshot.cs:22–47` | Polling selects the newest matching rows, returns them in stable admission order, and does not drain the ring. It copies Diagnostic references into immutable arrays. Replacements cannot mutate earlier snapshots; snapshot construction itself has array-copy coexistence. Closing clears service roots, not already returned observations. |
| `DiagnosticClockDomain.cs:10–60`; Chapter 03 line 70 | The diagnostic clock is shared Core-relative elapsed 100-ns ticks, frequency 10,000,000, nondecreasing and saturated. Use it, not raw `Stopwatch.GetTimestamp()` copied from unrelated host diagnostics. |
| `WorldDiagnosticsCapability.cs:71–90`; `TerraFX.Core.csproj:10–18`; LSP references of `DiagnosticsService.Write` | The capability checks its bound World and forwards the actual execution-session/owner tuple. Current production Write callers are the World capability and Hosting/input-routing paths, not Rendering. Runtime already has Core friendship; no new friend is needed or justified. |
| `sources\TerraFX.Runtime\Entities\World.cs:560–607`; `WorldConstructionPlan.cs:10–12,48–56,161–165` | Current World setup uses slot-derived diagnostic reservation and creates an owned or borrowed capability. The inspected path supplies no retained-record byte-attribution account that an extracted snapshot can safely carry across World disposal/replacement. This is an existing budget-enforcement gap, not permission to recover a live World from a GUID. |
| `Hosting\EngineHost.cs:66–80,138–173,760–845,1099–1260,1287–1305` | The initial Renderer is created **before** the owner-loop diagnostics service. Interactive startup creates another Renderer, whereas OffscreenNative retains the initial instance. Shutdown closes Diagnostics after World/device cleanup, and a separate stop path drains residency. There is no explicit Renderer CPU publication join before `Diagnostics.Close`. Binding only the Interactive construction site would miss OffscreenNative. |
| `Hosting\PlatformHostOwner.cs:71,86–111`; `EngineHost.GraphicsRecovery.cs:169–197,242–278,303–306,387–440` | Platform owner issuance supplies the host identity used by existing host diagnostics. Recovery drains old residency, creates a new device with the same graphics OwnerId and incremented DeviceEpoch, and replaces Renderer. The same host Diagnostics service must persist; old completion tags must not be relabeled. Existing host-side graphics waits keep pumping Platform where required. |
| `Rendering\Renderer.CpuExtractionSupport.cs:16–28,36,53–56`; `GpuResidencyService.cs:108–132,172–187` | Renderer currently receives only device/residency. Residency tracks its submission lease and has StopAsync, but that is not proof every Renderer CPU continuation has joined. `Renderer.Submit.cs` allocates diagnostic storage before acquiring that lease. |
| `Renderer.Submit.cs:35–107,164–195,798–856,858–932` | Submission validates the exact snapshot/graph and target epochs. The diagnostic lane has one-active-attempt exclusion. After the actual graph result, it awaits noncancelable readback, then decodes; the pending implementation attaches a typed array. Cleanup and the lane reset run in finally. This is useful execution infrastructure, not authorization for the public array or a complete byte/lifetime proof. |
| `sources\TerraFX.Graphics\GraphicsDevice.SubmitGraph.cs:216–223,410–442,471–488`; `GraphicsDevice.cs:93–101` | Graphics owns the positive submission sequence, refuses exhaustion, and returns it with the original submitted fence after completion. `GraphicsDevice.OwnerId` is the engine operational identity, **not** the hardware `Capabilities.DeviceId`. Recovery's epoch distinguishes sequence reuse on a replacement device. |
| Chapter 21 §1; `Renderer.Extract.cs:200–285,769–772`; `RenderMeshSnapshot.cs:9–26`; `RenderSnapshot.cs:29–44,244–315` | SnapshotId is the captured render-frame identity; TickIndex is not unique per render submission and FrameRevision is spatial-frame revision. DrawId is the existing NameUuid of the persistent Entity ID and `mesh-group/` plus invariant lowercase eight-digit hexadecimal group index. Snapshot currently contains no operational World owner/session/quota carrier. |
| `Renderer.MeshC0Graph.cs:561–580,655–735,1056–1063,1125–1193`; `RenderMeshC0NormalDiagnosticWork.cs:170–225,267–401` | Selected color/shadow draws are deduplicated, skinned membership is selected by palette presence, rows sort by RFC DrawId, and each group's exact index slice becomes an ascending unique vertex-ordinal packet. Camera/cascade/spot repetition does not multiply the row. The graph exports the real storage result buffer for readback. |
| `RenderMeshC0NormalDiagnosticGraphPass.cs:153–158,202–275`; `Shaders\SkinnedNormalDiagnostic.hlsl`; `Shaders\MeshC0Skinning.hlsl`; `Shaders\MeshC0Vertex.hlsl:173–192` | Each row is 16 little-endian bytes: degenerate, tested, invalid-evaluation, reserved U32 lanes. Shared HLSL applies the finite near-zero predicate before instance transformation; reduction counts packet members. The decoder verifies row extent/mapping, tested equality, degenerates <= tested, invalid=0, reserved=0. The current public-result construction is not required for those checks. |
| Chapter 10 lines 653–664,693; `Graphics\RenderingSchemaCatalogue.cs:159–201`; `RenderMeshAssetCodec.cs:343–354`; `RenderMeshAsset2Codec.cs:29–40` | Native Mesh vertex membership is bounded by **262144** vertices, not merely `int.MaxValue` or UInt32 range. Both inspected codec versions enforce that bound. Existing built-in static extraction is not evidence of an integrated LOD or animated-palette producer. |
| `tests\TerraFX.Core.Tests\DiagnosticsServiceTests.cs`; `tests\TerraFX.Runtime.Tests\Scenes\RenderSnapshotMeshGpuTests.cs:850–888` | Existing tests cover basic coalescing/immutability and pending typed zero-count results. They do not establish this new key, aggregate eviction, full byte admission, or a positive GPU warning through public Diagnostics polling. No tests were executed in this assessment. |

## 3. Exact recommended record contract

All rules in this section are proposed producer semantics for the **existing** code and carrier, not a new binary/content schema or an extension to arbitrary custom passes.

### Count membership

For each distinct captured skinned Mesh DrawId actually referenced by the compiled built-in color or shadow work, test each distinct vertex ordinal in that draw's exact admitted group's index slice once. Require a retained captured palette, valid nonempty group, and valid indices. Deduplicate camera/cascade/spot uses of that same draw. A vertex shared by different logical DrawIds belongs to both counts. Exclude unselected draws, unreferenced vertices, Sprite/custom draws, raster invocation multiplicity, triangle corners, and pixels.

Use the exact packed palette, authored normals and admitted normalized weights already used by raster. Count only a finite blended normal with `lengthSquared < 1.0e-12f`, before instance inverse transpose/final normalization. Equality does not increment the count. Preserve dominant-bone fallback, lowest-joint-index tie breaking, and singular/nonfinite palette refusal. Do not compute a CPU skinning oracle or change raster storage access.

For the currently bounded native-Mesh domain:

```text
1 <= TestedVertexCount <= min(Mesh.VertexCount, group.IndexCount) <= 262144
0 <= DegenerateVertexCount <= TestedVertexCount
```

Diagnostic preparation sorts and uniques the group's index slice and rejects only `uniqueCount <= 0`; the decoder likewise requires only `TestedVertexCount > 0`. Repeated-index slices `[0,0,0]` and `[0,1,1]` therefore have one and two tested vertices respectively. Widen validated GPU U32 lanes to nonnegative `long` observational values; reject out-of-domain values, do not wrap/clamp/saturate a vertex count. All rows, including zero degenerate counts, must pass complete extent/membership/integrity validation before any warning from that readback is attempted.

**Zero suppresses the warning.** A verified zero row is not a retained telemetry record and increments neither cumulative counter. No eligible draws means no diagnostic pass/readback/publication. No record found by polling proves neither zero nor coverage: the query may filter it, there may have been no eligible draw, work may have failed, or ordinary telemetry may have been coalesced/dropped. This route deliberately offers no positive zero/completeness acknowledgment.

### Fields

| Existing field | Exact producer value |
|---|---|
| `Code` | Ordinal literal `DegenerateSkinnedNormal`; no suffix, version, draw ID, or sequence appended. |
| `Severity` | `DiagnosticSeverity.Warning`: a recoverable fallback, not an operation error or critical fault. Do not switch to Information merely to bypass warning coalescing. |
| `Operation` | Ordinal literal `Renderer.SubmitAsync`. |
| `Message` | Literal `Skinned mesh vertices required the dominant-bone normal fallback.` Counts belong in Context, not formatted prose. |
| `MonotonicTimestamp` | Core diagnostic-clock timestamp sampled for this publication attempt after readback/validation; not extraction time, GPU timestamp, or raw native fence time. A coalesced record retains its first timestamp. |
| `HostId` | Captured `PlatformHostOwner.OwnerId` supplied by Hosting at Renderer construction. This is available in all host profiles after platform-owner creation; do not replace it with an invented GUID or allocate a second host identity. |
| `DeviceId`, `DeviceEpoch` | Captured exact submitting `GraphicsDevice.OwnerId` and completed graph result's DeviceEpoch. Verify they belong to the Renderer/plan captured at admission. Never use PCI/vendor device identity or the replacement Renderer. |
| `WorldGuid`, `Tick` | The exact submitted snapshot's WorldGuid and TickIndex. They are semantic filter metadata, not permission to query, resurrect, or rebind a World. |
| `SurfaceEpoch`, `SwapchainEpoch` | The original result's positive values for a surface submission; **null**, not zero, for offscreen. Diagnostic rejects present zero epochs even though offscreen receipt epochs are zero. |
| `SurfaceId` | Null. This producer has no reviewed unique scalar surface-ID mapping; do not substitute a handle slot/window ID. Captured epochs, device, and submission identity remain available. |
| `SystemId`, `PassId`, `AssetId`, `NativeResult` | Null. The graph pass identifier is not a Guid; this is one logical-draw count across passes, not a fabricated pass/entity/asset/native-status identity. |
| `CommandIdentity` | Exact grammar below, constructed from actual Graphics submission sequence and captured snapshot/draw IDs. |

```text
positive-decimal = [1-9][0-9]*, no leading zero, sign, whitespace or grouping
guid32           = [0-9a-f]{32}, actual nonempty Guid in lowercase "N" form
CommandIdentity  = "submission/" positive-decimal "/snapshot/" guid32 "/draw/" guid32
```

The sequence is in `[1, 9223372036854775807]`; CommandIdentity therefore has at most **110 UTF-16 code units**. Identity strings are ordinal, culture-independent values. They are not minted session/World identities, and command identity never implies a live command handle.

Producer context has **exactly six entries**, in this order; no optional payload, extra label, or user-controlled text:

| Key | Value grammar / source | Maximum value length |
|---|---|---:|
| `SnapshotId` | `guid32`, exact submitted `snapshot.SnapshotId` | 32 |
| `DrawId` | `guid32`, exact frozen diagnostic draw's DrawId | 32 |
| `SubmissionSequence` | `positive-decimal`, actual graph submission result | 19 |
| `CompletedFence` | `positive-decimal`, original graph's actual completed fence, **not** the later readback fence | 19 |
| `TestedVertexCount` | `positive-decimal`, validated tested count in `[1,262144]` | 6 |
| `DegenerateVertexCount` | `positive-decimal`, validated positive count <= tested count | 6 |

Format integers with invariant decimal formatting and Guids with `"N"`; never use current culture. Core alone appends seventh entry `OccurrenceCount`, positive invariant decimal of at most 19 units, initially `1`. Producer code cannot supply that reserved key. The first six entries and command identity agree exactly and are unchanged by coalescing.

## 4. Construction, attribution, and lifetime

### Direct borrowed service, not a capability escape

Recommend extending only Renderer’s **private/internal** construction route with the explicit borrowed `DiagnosticsService` reference and copied nonzero host ID. Do not add a public constructor, Diagnostics getter, emitter, callback, service locator, registry lookup, or friend assembly.

1. Keep Core as the BCL-only owner of the clock, ring, byte accounting and non-emitting attribution control. Graphics returns only its existing bytes/completion/scalar identities; it never receives Diagnostics publication duties or Rendering records.
2. Move host Renderer construction out of the early `EngineHost` constructor and into completed owner-loop service assembly: create Diagnostics; create Platform owner and capture its issued host ID; select/create device and residency; then create the fully bound Renderer for **Headless, OffscreenNative, and Interactive** before startup success. Private pre-start nullability/getter guarding is sufficient; do not create a placeholder Renderer or late-bind one already exposed to callers. Preserve startup rollback of every acquired service.
3. Keep standalone `Renderer.CreateHeadless()` free of a host service/native device. It cannot submit a GPU diagnostic. A host's Headless Renderer may receive its borrowed service but produces no GPU records.
4. On recovery, close old Renderer submission admission and join its admitted CPU publication work, in addition to the existing residency/native retirement. Create replacement Renderer with the **same host service and host ID**, the real replacement device/residency, and the new epoch. Never retarget old publisher state. Failed replacement construction/rollback retains or releases only its own acquisitions.
5. Restrict the Rendering admission call semantically to this code/operation/format. Core's existing internal Write can gain a closed internal overload/key branch and attribution input; its physical friend visibility is not permission for Rendering to publish arbitrary diagnostics or close the service.

The parameter types are existing lower-owned Core service and scalar values. No Hosting type/delegate enters Renderer or Core; no Core/Graphics-to-Runtime edge is added.

### Preserve the per-World quota without retaining a World

WorldGuid/Tick metadata cannot enforce Chapter 04's operational per-World byte quota, especially after replacement or when equal semantic GUIDs exist in distinct owners. **Do not silently classify these World-attributable records as host-only to escape that quota.**

Recommend one **Core-owned internal non-emitting diagnostic attribution account per operational World generation**, independently of `WorldDiagnosticsCapability`. Its closed purpose is retained-byte debit/refund; it confers no World access, diagnostic emission, service closure, lineage, or allocator authority.

- The existing World construction/admission path creates its account from the exact configured `MaximumDiagnosticBytes` before publication and binds the real execution-session ID, World OwnerId and WorldGuid once the owner is issued. Account construction/control storage must itself be funded; failed World construction abandons it. No caller supplies a claimed World identity at publication time.
- At the valid owner/frame-read boundary, `Renderer.Extract` captures the exact account reference with the immutable snapshot. The internal handoff is explicitly authorized by the Chapter 29 amendment; it is **not** the World diagnostic capability, live World reference, World-use credit, callback, or permission recovered from readable GUIDs.
- The same account follows render snapshots used by another Renderer or host. Its retained-byte total spans every service retaining records for that operational World. This covers a standalone-World snapshot submitted through a native host Renderer without weakening either quota or adding a global World locator. Snapshot disposal drops only that snapshot's ownership; an admitted Submit acquires its independent account ownership before its first await.
- Internal service ring entries retain the account/byte charge solely for accounting; eviction, replacement and service closure refund the actual charged bytes. Public Diagnostic objects and returned DiagnosticSnapshots do **not** retain the account, World, or publication credit. Their caller-owned lifetime cannot delay that refund. World close/replacement does not reset outstanding service charges or revoke already extracted historical render data. A replacement has its own operational account even if its WorldGuid matches. A foreign/missing/mismatched account is not silently replaced with a fresh quota.
- The account contains only Core scalar/accounting state and no back-reference to World, Hosting, a service, or callbacks. Atomic debit/refund and service-local serialization avoid holding two service gates or calling another owner during eviction. Ring/pending references are bounded by their admission limits; there is no permanent account dictionary/tombstone history. Account/control roots are charged through their real World/snapshot/service ownership and any transfer overlap; GC is not a quota-refund event.

This account is a necessary proposed **internal budget mechanism**, not a claim that one already exists or that the concurrent M03-B receiver-control design supplies it. Do not borrow its World-use/receiver allowances or edit that workstream. Unknown account footprint/lifetime charges remain explicit budget blockers.

### Join publication, not just the native fence

Before the first diagnostic allocation or await, atomically acquire the Renderer’s one-active-diagnostic operation credit against its stop gate. Preserve nonwaiting refusal of a second diagnostic attempt; do not add a waiting producer queue. Hold that credit through actual readback, validation, publication attempts and operation cleanup. A bounded internal stop/drain completion may join it; it cannot complete while a CPU continuation can still call Diagnostics.

Host shutdown must close Renderer admission, drain those admitted publications and residency work, retire native consumers, join hosted Worlds, and **only then** close Diagnostics. Use the existing host-side pumping wait where Platform requires it; do not wait on the graphics owner for a continuation that needs that owner. Apply the same rule to startup rollback and old-Renderer recovery. A residency drain or `_activeSkinnedNormalDiagnosticSubmission = 0` alone is not the proof.

On uncertain native work, preserve the normal quarantine/charges. If the CPU publication consumer has definitely ended, its publication credit may end while native quarantine remains; otherwise retain the credit/service and report shutdown failure. Do not close the service and catch `ObjectDisposedException` later as an ordinary visible drop.

## 5. Publication, coalescing, polling, and visible loss

### Publication boundary

After actual graph completion, await the exported result buffer's real readback transfer fence and CPU ownership/copy, using noncancelable internal completion after graph acceptance. Validate **the whole readback** before publishing a prefix. Then stream positive rows in frozen ascending RFC DrawId order through the host service, using one bounded formatting/record workspace, while the submission/publication credit remains held.

Publication attempts finish before the original `SubmitAsync` becomes successfully terminal, with its normal pin/cleanup obligations preserved. Do not detach a worker or put publication on the graphics command-recording/native pump thread. No synchronous GPU wait or live ECS access is introduced. Cleanup can still fault after a valid GPU count was published; such telemetry describes that historical GPU evaluation, not a promise that the submission receipt subsequently succeeded.

- Admission cancellation produces no accepted count. Accepted graph/readback work and its consumer ownership are not canceled by a later caller wait token.
- Missing compute capability, incomplete/malformed row, invalid evaluation, or transfer/device failure uses the existing contextual failure channel; no zero result, missing-producer success, partial diagnostic prefix, or new error code substitutes.
- Ordinary telemetry retention/format-budget refusal is separate: increment visible loss and preserve the independently valid submission result. No record is required when the verified count is zero.

### Operational identity and coalescing

For `DegenerateSkinnedNormal` Warning only, use this complete service-local operational key:

```text
(Code, HostId, DeviceId, DeviceEpoch, CommandIdentity)
```

CommandIdentity already contains actual SubmissionSequence, SnapshotId and DrawId. All required components are present and validated. Surface/tick/WorldGuid do not replace this key. Other code paths retain their existing key rules, notably `WorldSubscriptionCapacityExceeded`'s `(Code, ExecutionSessionId, WorldOwnerId)`. Rendering must **not** stuff submission sequence bits into either World identity operand of the existing Write call.

Different submits of one immutable snapshot are distinct warnings. A duplicate occurrence for the identical retained key can coalesce only with the same validated producer payload (all fields/context except the attempt timestamp and service-owned OccurrenceCount): keep the first timestamp/context, increment only OccurrenceCount and CoalescedCount with saturation, and retain its original admission position. A conflicting count/membership under that same key is an invariant/producer failure, not a request to sum counts or replace the first payload. Renderer attempts each positive row once and performs no publication retries/history cache; duplicate-key service tests exercise the coalescer, not a required duplicate-producing Renderer.

There is no deduplication history beyond retained records. Re-admission after eviction is a new occurrence-1 record. `OccurrenceCount`, `CoalescedCount` and `DroppedCount` are **warning-occurrence counters, never sums of vertices**.

### Admission and loss policy

Perform checked full-record byte preflight before owned allocation/retention. For a valid new record that can fit individually, evict the oldest retained rows as needed for slot/host-byte capacity. To satisfy its World quota, evict the oldest rows for that **same attribution account in this service** as needed; preserve relative order of survivors. Do not call another service or evict unrelated Worlds solely to free this account's quota. If charges retained in another service or concurrent claims still prevent its debit, refuse this new occurrence. A record too large for its individual available ceiling is refused without futile eviction.

Each eviction adds the evicted warning aggregate's full represented OccurrenceCount, or one for a nonaggregate, to saturated DroppedCount. Each refused positive publication adds one. Successful coalescing adds one to CoalescedCount; if a bounded immutable replacement cannot be prepared, leave the prior aggregate unchanged and count that incoming occurrence as dropped instead. Every path releases or transfers its reservation once. Already performed telemetry evictions are observable and do not roll back an independently successful graph.

All changes/counter observations are serialized by the service. Preallocate/bound counter-saturation reporting as Chapter 28 requires; a saturated counter cannot wrap or recursively allocate an unbounded diagnostic. Preserve the independent critical fault/result channel. The existing aggregate-eviction `+1` implementation must be corrected as part of this coupled admission work, not explained away in the producer.

Snapshots remain newest-matching selection in stable admission order, including unchanged World/minimum-tick filters. Counters are **service-wide cumulative**, even on a filtered query; they do not identify a missing draw, promise per-code loss totals, or provide a recoverable sequence cursor. Earlier copied records survive coalescing, eviction and service shutdown unchanged. Core owns the ring; consumers own their copied snapshot arrays, not an indefinitely retained service result stream.

## 6. Memory closure: covered contract, missing enforcement and workspace

**The existing retained-memory contract covers variable records/contexts. It does not merely cover 8192 references. No additional 8 MiB may be assumed.** Conversely, it does not specify a separately spendable producer-formatting allowance. The current implementation proves neither the retained bound nor a publication peak.

An accepted arbitrary `Diagnostic` has no constant maximum object graph: message/context/value lengths and context count are unrestricted. A single record can exceed 8 MiB. `GetMaximumRecordCount` and `OwnedByteReservation` currently count only ring/control backing, so lowering its slot count is not full-byte enforcement. Snapshots also deliberately permit caller-owned retention outside service lifetime. A bounded ring cannot bound arbitrary caller retention.

### Finite format bound, not an invented CLR size

For this proposed warning, the seven retained key/value pairs have lengths:

```text
key lengths:   10,  6, 18, 14, 17, 21, 15
value maxima:  32, 32, 19, 19,  6,  6, 19
other strings: Code 23, Operation 20, Message 65, CommandIdentity <= 110
```

There are at most **18 string references**, **452 UTF-16 code units / 904 bytes of character payload**, one seven-element context backing and one Diagnostic, plus accounted attribution ownership. Charging shared literals once per row is a conservative reservation, not a claim that they are physically duplicated.

For a qualified allocation profile P, let `D_P` be the complete Diagnostic object charge; `p_P` the reference stride; `T_P(n)` a qualified complete string charge for at most n code units including terminator/header/alignment; `Q_P(c,s)` a qualified complete array charge; and `A_P` any attributable account/control charge not already charged at its real owner. A finite per-record reservation is:

```text
R_P = D_P + Q_P(7, 2*p_P) + sum(T_P(length) for the 18 bounded strings) + A_P
```

Reserve this fixed worst-case R_P for each retained warning of this code, including after coalescing; do not lower it for short numeric values. It is conservative reserved headroom, not a measured physical size. This fixed reservation is the policy used in the ring-count bound below. Do not use `Unsafe.SizeOf<Diagnostic>()` as D_P: it measures a reference. A count string growing during coalescing still requires old/new Diagnostic, context and string coexistence, and earlier caller snapshots retain their own previous record graph.

For comparison only, applying the existing diagnostic-work proposal's **unqualified** conservative rule `Q(c,s) = 64 + roundUp16(c*s)` and a separately proposed string envelope `T(n) = 64 + roundUp16(2*(n+1))`, with p=8, gives **176 context bytes + 2208 string bytes = 2384 bytes**, before D_P/A_P/ring entries/control/workspace. This arithmetic neither establishes CLR headers/string layout nor qualifies x64, ARM64, Debug, Release or AOT. It demonstrates why “8192 records × a reference” is not the relevant bound.

Let C_P include the service/control/independent bounded fault and saturation storage, E_P be the expanded entry stride, and W_P the bounded service-side admission/coalescing workspace including simultaneous old/new roots. The recommended stronger service admission invariant is:

```text
C_P + Q_P(8192,E_P) + sum(retained complete record charges) + W_P <= 8388608
per operational World: sum(attributable retained charges across services) <= its exact MaximumDiagnosticBytes
retained record count <= 8192
```

An all-this-code ring therefore admits at most:

```text
min(8192, floor((8388608 - C_P - Q_P(8192,E_P) - W_P) / R_P))
```

Byte pressure can dominate before 8192. Standalone services also honor their exact lower configured ceiling and associated attribution account; their backing capacity may be reduced before allocation. Reserve account bookkeeping before World creation; no unbounded service dictionary is hidden in C_P. Generic ordinary records need length/capacity-derived admission, not this code's R_P. Independent critical/saturation storage needs its own closed, bounded producer profile inside C_P; the inspected unbounded public Diagnostic shape does not supply that profile. Its missing qualification remains a Core Diagnostics budget prerequisite, not a new permission to truncate faults or lose caller failures.

### Additional owner funding required for publication

Adopt an explicit `PublicationWorkspace_P` term in the M05 completion ledger: one streamed producer record, invariant-number/Guid/command formatting, constructor and service-append context copies, decode traversal/control, active-publication/drain controls, and ownership-transfer/coalescing overlap not charged elsewhere. Reserve it **before graph acceptance**, beside the actual `16*D` raw rows (at most 2 MiB at the logical draw ceiling), readback owners/leases/tasks and all other completion roots.

This term must fit the proposed aggregate 16-MiB completion envelope; it is not taken from the host ring, a World quota, unused GPU budget, or the 32-MiB preparation envelope. Service-owned copies are charged at Core; producer-owned copies at Rendering; transfer does not mint credit or double-count physical backing. Stream after a first validation pass over immutable raw bytes, rather than allocate D Diagnostics, D context arrays, or the unapproved D-result public array. Quarantined/still-rooted storage keeps its existing charge.

**No qualified values for D_P/T_P/A_P/C_P/E_P/W_P or this full workspace are established here.** Before allocation-capable implementation, the owner must close and review their concrete allocation sites/capacities/target profiles, enforce checked pre-admission and actual-release accounting, and prove fit. If the implementation cannot fit, refuse the whole diagnostic-bearing graph before GPU acceptance where its mandatory completion workspace is unavailable; ordinary later ring pressure still follows §5. Do not silently disable the compute pass or invent a new numeric allowance. Any allowance outside these proposed envelopes requires a separate exact owner-budget amendment.

## 7. Distinguishing case and required tests

Use an ordinary internal service test with capacity two and valid fixed literal identities. The production scenario is the same immutable snapshot S / draw A with tested=12, degenerate=3 on both real submissions; do **not** manufacture a changed palette/count between resubmissions of S. A second draw B in that snapshot may independently have tested=12, degenerate=9. The Core test supplies literal completed-readback metadata through the internal admission boundary; it neither fabricates a public submission receipt nor claims that real GPU submissions executed.

| Step | Admission | Required live order / counters |
|---|---|---|
| 1 | Submit sequence 41: S/A, count 3; take snapshot P | `[41/A(3, occurrence 1)]`; dropped=0, coalesced=0 |
| 2 | Submit sequence 42: same S/A, count 3 | `[41/A(3,1), 42/A(3,1)]`; counters unchanged. Equal world/tick/snapshot/draw does not coalesce different submissions. |
| 3 | Inject an identical duplicate of retained 41/A through the actual closed internal admission path | `[41/A(3,2), 42/A(3,1)]`; dropped=0, coalesced=1. It stays first. The vertex count is still 3, not 6 or 2. P remains occurrence 1. |
| 4 | Admit 43/B, count 9 | Evict aggregate 41/A: `[42/A(3,1), 43/B(9,1)]`; **dropped=2**, coalesced=1. The source's current eviction `+1` fails this requirement. |
| 5 | Re-admit 41/A after eviction | `[43/B(9,1), 41/A(3,1)]`; dropped=3, coalesced=1. No permanent deduplication cache; P remains readable and unchanged after service close. |

The current null-session/null-owner warning route instead matches these records by Code alone and retains the first count. Adding context entries alone does not fix that result.

Required focused implementation tests, **not performed by this document**:

| Owner / existing test area | Observations |
|---|---|
| Core `DiagnosticsServiceTests` | Execute the exact five-step sequence; vary actual device owner/epoch and CommandIdentity components independently; same-key conflicting payload refuses; retain the existing World-warning session/owner semantics, query filters, stable admission position, newest selection and earlier-snapshot immutability. Warning telemetry does not replace latest critical fault. |
| Core byte admission | Exercise exact byte fit and one-over, full context/string/account backing rather than slot counts, ordinary oversized refusal, FIFO byte eviction, aggregate occurrence loss, immutable coalescing growth/old-new peak, cross-service account pressure, saturated counters, Close refunds and copied observations. Confirm nonzero tests actually execute; do not test only accounting helper arithmetic. |
| Rendering decoder/membership | Literal 16-byte rows with tested counts 1, 2 and max 262144, zero/positive degenerate counts, tested mismatch, count>tested, invalid/reserved lanes, truncated/extra rows and a late invalid row. No publication before whole validation. Indices `[0,0,0]` test one unique vertex, `[0,1,1]` test two, and `[0,1,2,2,3,0]` test four, not six; repeated cameras/shadows do not multiply a draw. Use selected owner-normal fixtures, not production output or a second skinning implementation as the expectation oracle. |
| Rendering format/publication | Exact six-entry order and service seventh entry; invariant decimal/lowercase N under non-invariant cultures; correct null offscreen epochs; actual graph sequence/fence rather than readback values; complete key on repeat Submit; no zero warning or zero-induced dropped count. Force bounded retention refusal while the independent successful submission remains successful. |
| Core/Entities/Rendering attribution | Two operational owners with the same semantic WorldGuid remain distinct quotas. Capture during valid extraction, then close/replace the World while historical submission completes without a live World access. Exercise standalone-World snapshot to host Renderer, account sharing across services, account/control retention/refund, and construction failure. Do not use the World emitter capability or M03-B credits as substitutes. |
| Hosting lifecycle | All three startup profiles and startup rollback bind/create/close exactly the intended service; OffscreenNative is not missed. Stop and recovery close admission before joining a delayed CPU publication/readback. Old Renderer cannot emit after service closure or relabel results with replacement epoch; failure/quarantine does not falsely complete its drain. |
| Public consumers / native observation | After real producer closure, existing public submission plus `EngineHost.Diagnostics.GetSnapshot` observes a real positive degenerate GPU count on D3D12 and Vulkan, with the captured IDs. Static BindPose zero-result tests are not this witness; the real degenerate animated-palette producer remains a prerequisite. Separate managed/internal behavior from native, NativeAOT, public-only consumer and allocation/budget qualification. |

Use existing NUnit projects and ordinary commands in `docs\project\Building.md`, Debug before Release where both apply, with explicit nonempty selections. Chapters 32/34/35 retain their actual producers and acceptance gates. No synthetic palette, skipped capability, stale TRX, source inspection or proposal hash closes a native/AOT/consumer or diagnostic-budget gate.

## 8. Input freeze and preservation record

Freeze: **2026-11-17T21:49:00.5632853Z**. HEAD: **`3e78cca23e633af10436db2a091f62736f70095e`** (`Verify World admission capacity accounting`). Branch: `tannergooding-m05-implementation-plan`.

The complete bounded assessment input set is the **61 full files** below, not merely the cited ranges. C = tracked/clean, M = tracked/modified, U = untracked at freeze. Dirty rows identify workspace bytes, not HEAD's version. In particular, modified Chapter 30 is not silently replaced with its committed baseline, and unapproved source additions are not promoted to authority.

Canonical input-manifest SHA-256: **`854FAA346763236C5ABA6E4CBBA820ACED0130501F8C26D7AA1F9FF227275BB2`**. Definition: sort the listed Windows-style paths with `StringComparer.Ordinal`; concatenate `State<TAB>Path<TAB>UPPERCASE-SHA256<LF>`; hash UTF-8 bytes without BOM. File hashes cover raw bytes, without newline normalization.

Pre-existing workspace state: **79 dirty paths: 60 tracked modifications, 19 untracked files**; no staged changes. Safety fingerprint over **all 79** pre-existing dirty files, including unrelated concurrent work: **`7A1BDF13B9EC292BF99CC547C4414DE65CD5F8DFC604F539D016129F53EA9FCE`**. Its canonical form is ordinal Windows path order, `two-character git porcelain-v1 status<TAB>Path<TAB>UPPERCASE-SHA256<LF>`, UTF-8 without BOM. This fingerprint is preservation evidence, not review scope or acceptance of those other files.

During completion checking, concurrent work added `docs\proposals\m03b-scene-receiver-control-funding.md`. Its observed SHA-256 at **2026-11-17T21:56:22.7924095Z** was `17671F087293FBFB4C454432B7C016076AB612508AA3EDA85F13ACF54D3CB6EC`. It is not an input, authority, or edit of this assessment. The original 79-file safety fingerprint still matched after separating that addition; none of the 61 frozen inputs changed. Observed workspace total was consequently 81 dirty paths, including that concurrent proposal and this proposal.

No evidence copies, scratch files or manifests are added elsewhere by this task. The hash freeze identifies the exact live input bytes; it does not lock out another writer. Recheck every input hash, HEAD, and the original-dirty fingerprint before any disposition. Exclude these two explicitly identified new proposals when checking that original 79-file fingerprint; check the concurrent addition separately. This task's only file delta is this new proposal. Any relevant concurrent byte change invalidates the input freeze and requires a new assessment rather than silent rebasing.

| Input | State | SHA-256 |
|---|---|---|
| `.editorconfig` | C | `342683DE2B1C262624546F312F7D8E2078F89185ADD39F43F1FB5013376FD553` |
| `.github\copilot-instructions.md` | C | `12439DF4BB5E46021891B234BEC1104ABDCA57C11FDF3F610C8F32C5DAF3001E` |
| `.github\workflows\ci.yml` | C | `03EFEA672CD52E9EF053E9CE0285E7CA02C6CE6FB0D21268ADE4255ED5A39D58` |
| `Directory.Build.props` | C | `770EE4B2A422861A6F23F0D39DD0ECA361A81CB87E64EBAC4E12170F71231D7A` |
| `Directory.Build.targets` | C | `AEEC1D08E8037470239035812351C4045124607D67A354D482112F4D642D4105` |
| `docs\design\03-shared-conventions-and-clocks.md` | C | `B4208D3037C8C90EED56627F63DC04ACFFA80FF16B5FF3FF6936CD89615C08C5` |
| `docs\design\04-capacity-and-workload-profiles.md` | M | `A4D86F3FA373A64548435C53582BF5916E29D77F34650F9D161CCEC90E6F0245` |
| `docs\design\10-binary-data-and-authoring-formats.md` | C | `56721136AECAE18C20EB3E9A1BECEB527CC0676DD6B43CB506538AD63DECBB5F` |
| `docs\design\17-graphics-device-and-resources.md` | C | `8157E7DA5472B9AABD891A05E84F2B997487A5C5E3FF67CB589681747D628718` |
| `docs\design\20-animation-and-deformation.md` | C | `BBEF0A36E0832C2BD0D54A07E479917DB923671B64F7176369F8F57A8F1618C5` |
| `docs\design\21-2d-and-3d-rendering.md` | C | `989A6183186592F165FA9614CC19A8FED63826E2A51170CE55FBFFFCA8116ED2` |
| `docs\design\28-diagnostics-performance-and-validation.md` | C | `C9260CD18A905E087DFC47D07AF67BD2477996232DA773B69A0E4C3B7A64D90F` |
| `docs\design\29-project-setup-build-and-deployment.md` | C | `3B55A013EEC327ECD648ACBD5B53717DD218AD288E8047AC21277609EFA2F47B` |
| `docs\design\30-public-api-review-surface.md` | M | `15848C82F3858A77A87A0D3EA6FBCC07046EB6E77CA5B21748C72B90EFAB598B` |
| `docs\design\32-end-to-end-integration-scenarios.md` | C | `6AF2211CB79FECA449D0F701752F86B8D093162AD75673770B0C0DE0F49BA4FC` |
| `docs\design\34-engine-acceptance-cases.md` | C | `532A68F1717DC05901DFC4537204E65B31B28EE46326F64F5A1CD7834332AA41` |
| `docs\design\35-implementation-roadmap.md` | C | `24EEC6E92B7AD904D552F290EF748FFC8FCE13B6B94A89EBD9F39487D4584D87` |
| `docs\project\Building.md` | C | `F043A352870EFB6C7D4A61998225E2508A8F4370A57E784483A41819D452075F` |
| `docs\project\Roadmap.md` | C | `1C8C3F8E140F47492255534284ABBF67C5776210DC7A68F2C01155D0BE773F46` |
| `docs\proposals\m05-skinned-normal-diagnostic-budget-evidence-rectification.md` | C | `A8DBD9EE4EA443245391A3B8AE17EDE03E3D71DA3F824E9128641E1C94F4DB36` |
| `docs\proposals\m05-skinned-normal-diagnostic-delivery.md` | C | `1D9ED2736D1C2520712D1E85A75AC5F0CF81D58C1EA48158F284B24B07C0DA3D` |
| `docs\proposals\m05-skinned-normal-diagnostic-public-carrier-resolution.md` | C | `AC8271D5AFF2A16BAA1B7DD02F2FD50FF3A77EF09B56EF079CEB2E1DE804F0F1` |
| `global.json` | C | `1BCE01871321C2DF82E887DA428CE5C121A6C6541FE65017FE3D249BE47E190C` |
| `sources\TerraFX.Core\Diagnostic.cs` | C | `B827997B35EE8ACCCCFA069905865C1E155508401FE7256EACBD0B20E2A6B840` |
| `sources\TerraFX.Core\DiagnosticClockDomain.cs` | C | `D194906D28D774DF01A83DC7C554E0AF82645DE90FA9931D415DBD02134BD680` |
| `sources\TerraFX.Core\DiagnosticContextEntry.cs` | C | `C2A1E74E0AE9929638C9255CFAA8DD7235CE37509483660B740569B775BEF530` |
| `sources\TerraFX.Core\DiagnosticQuery.cs` | C | `E8574D8DA53A59F71557EAB8FF101F8EE56A9CB126AC26614CE4D7F6795F6DE2` |
| `sources\TerraFX.Core\DiagnosticSeverity.cs` | C | `E90303D70CECFD9B83EC4D9D5716697230442896D997059CF29A24EB49AE2327` |
| `sources\TerraFX.Core\DiagnosticSnapshot.cs` | C | `916F5FB2AD709C4F0EF12FC83FF8E997619F1C1B9F6B52234655E91D45EFC242` |
| `sources\TerraFX.Core\DiagnosticsService.cs` | C | `004575041F3702786FD85DB0F7A5479CEB5C8C2717374D7B3FB325183E6B7F71` |
| `sources\TerraFX.Core\TerraFX.Core.csproj` | C | `BDA8230D77298EE852B28ED8446C66E7ADD9227F6AD9285087E3408F351E0088` |
| `sources\TerraFX.Core\WorldDiagnosticsCapability.cs` | C | `77D3CBC729325420643EAF196C7B30AB6EBD178A883CFB417CC64E7E41644A5E` |
| `sources\TerraFX.Graphics\GraphicsDevice.SubmitGraph.cs` | C | `C68E23B5FFA591C639714101FB23A1F1AE883FAC28F722EEB5B034725C3A97FD` |
| `sources\TerraFX.Graphics\GraphicsDevice.cs` | C | `ED23475055D08BB3631935580912DB5AB30F55E0F5228227AF76BB123CEE5FEC` |
| `sources\TerraFX.Graphics\RenderingSchemaCatalogue.cs` | C | `DE25A8C0E249FF9AB5B592FE848C85429C004FE629694C64EF1AFEF3BA731614` |
| `sources\TerraFX.Runtime\Entities\World.cs` | M | `692F721BEAD29E5EC258BF7B1844EC785E2D1548E9F53DFA9BDB57A22A01F09F` |
| `sources\TerraFX.Runtime\Entities\WorldConstructionPlan.cs` | C | `3A6E51A8099482A0FCA32F7FAAC3BD55CFC7C53D233FC88B1BD369DEDB32C066` |
| `sources\TerraFX.Runtime\Hosting\EngineHost.GraphicsRecovery.cs` | C | `CC03DB8E1296B8A1C4CD1FAAC37F33BF4455D424C3EE66C6EC5DFE481B89B8D6` |
| `sources\TerraFX.Runtime\Hosting\EngineHost.cs` | C | `A3FB2415E31CB7B717F2AD382E1FACF874B6AC392A469B6E71BA198DF6A11743` |
| `sources\TerraFX.Runtime\Hosting\PlatformHostOwner.cs` | C | `FE2A8A57C24637CFE8534BD3AEC9143CD10A28A5017BA8C975D7C4BEEFA8C97D` |
| `sources\TerraFX.Runtime\Rendering\GpuResidencyService.cs` | C | `7CE97B4E5711B451C1E7EE190C6BF7EC6AC15420625A82EC1E78C0AA9479777C` |
| `sources\TerraFX.Runtime\Rendering\RenderGraphPlan.cs` | M | `CF206DBB501A547170ED5CEAF8797BBB18AA1881EDFE2FB8B70DD5B2CD29F0D7` |
| `sources\TerraFX.Runtime\Rendering\RenderMeshAsset2Codec.cs` | C | `8107151DEB628B6E8D0A4155D2D808ED407B508566DD73BB97A9BA5E9F7C2760` |
| `sources\TerraFX.Runtime\Rendering\RenderMeshAssetCodec.cs` | C | `B07A023942EC548FDFFC0F1DDBDBFD4185FE50F52094DA8220D3617C70E0D424` |
| `sources\TerraFX.Runtime\Rendering\RenderMeshC0NormalDiagnosticDraw.cs` | U | `A6F9B850B3E05D48FDDA56F91B6769A81BC6A3F0878CA294D35DD38B43999758` |
| `sources\TerraFX.Runtime\Rendering\RenderMeshC0NormalDiagnosticGraphPass.cs` | C | `554750E946DCADFD826C2B2348550B5ECE7C78B5BBC93084CB4F1BFCFD3FBB9B` |
| `sources\TerraFX.Runtime\Rendering\RenderMeshC0NormalDiagnosticWork.cs` | C | `934BD260B82EAF9DC3E0312B74ACCCF2DD33FEF22E3309563E583E3E856B8CE6` |
| `sources\TerraFX.Runtime\Rendering\RenderMeshSnapshot.cs` | C | `9003FA81C790BB8CF395CD327063C44EC1B25A14D99875E64AA148BDF0663D05` |
| `sources\TerraFX.Runtime\Rendering\RenderSkinnedNormalDiagnostic.cs` | U | `E07287C05EA0E4A12F5ADF2FABCDC48B191C2D753B20E2636447CD83F1E2A41E` |
| `sources\TerraFX.Runtime\Rendering\RenderSnapshot.cs` | C | `733C8A58053977A349742540639FC55AF0CD299DB33C7394D1DC999A17D30AF0` |
| `sources\TerraFX.Runtime\Rendering\RenderSubmissionReceipt.cs` | M | `77120676F289AD7D8891875A783BAFA05E64B7F0317BC09BECC2545AC42E5846` |
| `sources\TerraFX.Runtime\Rendering\Renderer.CpuExtractionSupport.cs` | M | `EAF1F94AFF0886B3FA30AF97BD7F83180A38AB744D4241D0CDD275A055EADB55` |
| `sources\TerraFX.Runtime\Rendering\Renderer.Extract.cs` | M | `274298BFDCEE321141D4138D417807951FCC0EF6BCAC1DFCE2726D5F36829F2D` |
| `sources\TerraFX.Runtime\Rendering\Renderer.MeshC0Graph.cs` | C | `73D1AB54679FDA2A0FD3D9880DD51FC5A9A6DD9A936D10D38D54BE30A0645335` |
| `sources\TerraFX.Runtime\Rendering\Renderer.Submit.cs` | C | `8BF0D0662C9D3DE0A1670ED05C89FAB1E6A749D744BF796A9D5E0236BF4DDB79` |
| `sources\TerraFX.Runtime\Shaders\MeshC0Skinning.hlsl` | U | `ECBD9366EE9EE222C5B438F35643065CFE30162B59A0B9B6ECAA93BE54909E25` |
| `sources\TerraFX.Runtime\Shaders\MeshC0Vertex.hlsl` | M | `0DB0EB1F49C06B82534F6A767C4FDE91996D31B75CB9775319C70A0B96C8999D` |
| `sources\TerraFX.Runtime\Shaders\SkinnedNormalDiagnostic.hlsl` | U | `D9B7BA4AC63BA1C951C862226C8BA975EB180ADC6E302FA5816C31E0A0AB084E` |
| `sources\TerraFX.Runtime\TerraFX.Runtime.csproj` | M | `2F1C81E54F0FBF12D01E34D0D840885F5EB0E94A8B8890865EF721B8C6A3C65D` |
| `tests\TerraFX.Core.Tests\DiagnosticsServiceTests.cs` | C | `E1E6720346A9DA83C6975EA936C55DD4FAFE2FB993578971A6B0693CE694D496` |
| `tests\TerraFX.Runtime.Tests\Scenes\RenderSnapshotMeshGpuTests.cs` | M | `1F1708D37B38F20760FB4B659844C1815C6EE2EFD500BDD1E796654D3CCDBD7D` |

Chapter 10 and `RenderingSchemaCatalogue.cs` identify the inspected Mesh schema bytes; the codecs identify the enforced vertex bounds. No content-schema version, protected M02 fixture, numerical threshold or shader ABI is changed or accepted. The review set is this proposal plus the 61 frozen inputs. Its own hash is reported separately after final verification, avoiding a self-referential digest. No build, test, native/AOT/consumer, allocation qualification, commit, push or PR is part of this proposal-only task.
