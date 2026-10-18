# Diagnostics, performance, and implementation acceptance

[Terminology and reading conventions](01-terminology-and-reading-guide.md).

## 1. Observable system

Diagnostic record: code/severity, monotonic timestamp, host/world/device/surface IDs + epochs, tick, system/pass/asset/command identity, operation, native result, message and actionable context. Error paths return/throw their result independently of telemetry. Log structured fields, not giant per-frame dumps; never text-field/password/file-secret payloads by default.

`EventSource`/`ActivitySource`/`Meter` instruments host/tick/jobs/barriers/physics/narrowphase/solver/graph/upload/submit/present/audio/import/save/replay. Counters include active/peak/owned/pooled bytes, native allocator blocks/residency estimates, queue occupancy/highwater/backpressure, dropped PRESENTATION snapshots, audio underruns, input overflow, query/continuous collision detection (CCD) indeterminate, cache/import failures, waits and retirement ages.

Diagnostic queue capacity is 8192 records per standalone service or one shared 8192-record ring per host service; retained storage is additionally bounded by chapter [04](04-capacity-and-workload-profiles.md)'s per-World diagnostic sublimit and fixed 8-MiB host-service ceiling. Critical fault state has an independent durable last-error slot and caller result so queue overflow cannot erase failure. Repeated warnings coalesce by code and operational object identity. A coalescible warning's first retained record carries the reserved context entry `OccurrenceCount=1`, appended after producer context entries; each merged occurrence increments it with saturation, and `CoalescedCount` counts merged occurrences. For `WorldSubscriptionCapacityExceeded`, the producer context order is `ActiveSubscriptions`, then `MaximumSubscriptions`, followed by the service-owned `OccurrenceCount`. The count is a positive invariant-decimal string in `[1, 9223372036854775807]`. `DroppedCount` counts every occurrence no longer retained, including all occurrences represented by an evicted aggregate; the two cumulative counters may overlap when a coalesced record is later evicted. Both counters saturate at `long.MaxValue`.

`DiagnosticsService.GetSnapshot(DiagnosticQuery)` is bounded, non-destructive copied polling, not a public emitting-thread subscription. `DiagnosticQuery(maximumRecords, minimumSeverity, worldGuid, minimumTick)` has explicit inputs: count in `[1, 8192]`, closed severity enum, nullable world GUID filter and nullable lower tick bound. A present `minimumTick` is inclusive and matches only diagnostics with a nonnull `Tick` greater than or equal to it; tickless records are excluded. A present `worldGuid` requires exact equality. Results retain stable admission order, drop/coalesce counts and immutable records; a filtered query does not drain another consumer's telemetry. The editor replaces its bounded copied view. `TryGetLatestFault(out Diagnostic?)` is false only when no critical fault is retained; faults remain independent of the ordinary ring.

For `WorldSubscriptionCapacityExceeded`, the coalescing identity is the operational tuple `(Code, ExecutionSessionId, WorldOwnerId)`, not `WorldGuid` or callback identity. Its visible `WorldGuid` remains a semantic filter field and its `Tick` is null. Queue/byte refusal may drop the warning only while incrementing the applicable saturated dropped counter; it cannot alter the operation's return value. Snapshot arrays copy immutable Diagnostic references in stable admission order; the returned snapshot owns that copied array independently of subsequent ring eviction and service shutdown.

Coalescing replaces the retained aggregate in its original admission position; it never moves the record to the position of a later occurrence. Each copied snapshot and its records remain unchanged when the live aggregate is subsequently coalesced, evicted, or the service is shut down. The M03-A diagnostics-service regression uses the actual internal service-admission path with this fixed sequence: admit `WorldSubscriptionCapacityExceeded` for `(session S0, owner O0, WorldGuid G0)`, snapshot; admit the same code for `(S0,O1,G1)`; admit it again for `(S0,O0,G2)` where `G2 != G0`; then admit it for `(S1,O0,G0)`. The first warning coalesces in place to occurrence 2; distinct owner and session identities remain separate, and the final admission order is the original O0 record followed by O1 and S1. The snapshot taken after the first admission remains occurrence 1 and byte-for-byte unchanged. A direct coalescing-key comparison with the same session/owner tuple and a distinct closed `Code` must be unequal. Test identities are valid internal values supplied through the test boundary; this does not create a public emitter or claim hosted-World behavior.

Tick filters and cumulative observational counts/byte budgets use nonnegative `long`; bounded polling counts use `int`. Dropped/coalesced, clipped-sample, underrun and histogram/replacement telemetry saturates at `long.MaxValue` with a retained `DiagnosticCounterSaturated` diagnostic identifying the affected counter; it cannot wrap or turn negative. Saturation does not erase an independent operation failure or falsely advance an ordered timeline. Counter-saturation recording is preallocated/bounded and never blocks an audio callback; the control owner publishes its diagnostic.

Use private monotonic `TimeProvider`/`Stopwatch` backing for central processing unit (CPU) timing. Public diagnostic/profiling values follow [03's clock domains](03-shared-conventions-and-clocks.md#2-four-clocks) and [02's type boundary](02-dotnet-api-memory-and-ownership.md#public-type-boundary); callers do not supply or retain a stopwatch/provider, and raw timestamp meaning cannot depend on an undocumented backing frequency. Convert graphics processing unit (GPU) query timestamps using the native frequency/valid bits and submission calibration when available; do not subtract CPU and GPU clocks naively. Audio sample/endpoint timing follows [Chapter 24](24-custom-audio-mixing-and-spatialization.md). Debug overlays/render-graph inspectors show current dependencies/states/epoch/fence vectors, physics AABBs/manifolds/joints, input contexts, and audio buses.

.NET 11 Unix in-process crash reporting can be explicitly enabled for qualification/deployment diagnostics; record the actual runtime/configuration/capability and report failure to produce an artifact. It is not an engine recovery mechanism or proof callbacks/resources quiesced. Crash artifacts may contain sensitive runtime/path/state data, so default runtime reporting is not silently enabled, ordinary redacted logs remain separate, storage/access/retention policy is explicit and there is no automatic upload.

## 2. Performance methodology

All below are UNMEASURED implementation targets, not evidence this specification produced a fast engine. Baseline machine: Intel i5-12400, 16 GiB RAM, RTX 3060/12 GiB, SSD; Windows 11 x64 and Ubuntu 24.04 x64, recorded drivers/runtime/power profile. Release NativeAOT runtime/editor/tools, validation/debug overlays off for throughput; separate debug correctness lane.

Primary engine lanes use the qualified.NET 11/C# 15 toolchain; optional matched.NET 10/11 portable harnesses use chapter [29](29-project-setup-build-and-deployment.md)'s distinct targets, not a claim of engine.NET 10 compatibility. Measure AOT cold startup, steady state, constrained-callable dispatch and chosen async lowering on unchanged workloads. Optional matched JIT comparisons additionally record tiering/R2R fallback on admitted v2 hardware. Record instruction set architecture (ISA)/intrinsics/FMA/code-generation settings and content selection; numerical profiles remain distinct even when physical tolerances agree.

Assembly/package/feature-root qualification additionally follows [29](29-project-setup-build-and-deployment.md#feature-roots-and-published-assets). Compare actual AOT published size, metadata/native inventory, startup and selected roots for standalone/headless/backend profiles; assembly consolidation is not an asserted size/inlining/performance win. Record the larger integrated restore closure separately from shipped bytes.

Warmup 120 frames/minimum, then 3000 frames; selected optional JIT comparisons additionally reach recorded steady tiering; report median/p95/p99/max, closest-rank percentile `ceil(p * N)`, allocated garbage collection (GC) bytes, gen collections/pause, GPU memory and queue wait. Run five processes, disclose ranges/outliers; baseline same hardware/profile/content/selection, no isolated "best run" claim. Latency and throughput separate; vsync masks throughput, so offscreen/unthrottled for GPU cost and interactive for input-present latency.

| Workload | Exact scene / target |
|---|---|
| ECS loop | 100000 entities (`Position`/`Velocity`), fixed 60 Hz, no structural churn; position += velocity * dt; owner + worker lanes produce same profile hash, CPU p95 <= 2 ms |
| `Structural` churn | 10000 entities/tick add/remove/destroy/create in preallocated limit; correct receipts/indices and p95 <= 5 ms, zero stale alias |
| 2D renderer | 10000 visible atlas sprites, 100 materials, 1920x1080, alpha/ordered overlap; CPU extract + record p95 <= 4 ms, GPU <= 8 ms |
| 3D renderer | 1000 instances/1 million visible triangles, 128 materials, 32 lights, 4 cascades/4 spot shadows, 20 animated actors with 64 bones each; CPU render <= 4 ms, GPU <= 12 ms |
| `Physics` | 1000 supported dynamic primitives, 10 ten-high stacks, static floor, 100 queries/tick, default solver at 60 Hz; p95 <= 4 ms plus chapter [33](33-physics-acceptance-and-reference.md)'s stability/CCD criteria |
| `Audio` | 64 mono 3D + 32 stereo 2D voices, 4 buses, streamed music, 48000 Hz; process CPU <= 20% of each callback's duration p99, no underruns in 10 minute controlled stress |
| UI/editor | 10000 tree items virtualized to <= 200 visible, Unicode inspector fields, live viewport/import; UI p95 <= 4 ms, no pump starvation > 100 ms in controlled workload |
| `Memory`/leaks | After warmup steady logical scene and 10 resize/load/unload/device cycles, no unbounded resident ownership; postquiescence outstanding leases/resources return expected baseline |

### Permanent identity and scene-history workloads

The `Structural` churn lane retains exact identity history throughout the run. Start with 10000 live entities, then commit 10000 new Entity assignments per tick for the required 120 warmup plus 3000 measured frames:

```text
10000 initial assignments + 10000 × (120 + 3000) = 31210000 assignments
```

Do not reset history, recycle retired IDs, replace the World, or exclude top-level scene-instance assignments during measurement. Additional warmup and any scene-instance creation must be admitted before the run and counted. The 33554432 cumulative ceiling therefore leaves 2344432 assignments after this primary lane; the existing `Structural` p95 target remains <= 5 ms.

Report early, middle and late windows as well as the full interval across five processes: exact-index lookup misses/comparisons, B+ tree page copies and splits, moved leaf bytes, hashing, reservations, full successful and failed preflight cost, formatted-name allocations, allocated GC bytes and collections, and p95/p99/max. The bounded-index design has a conservative 32-full-GUID-comparison lookup bound and an insertion leaf-movement upper bound of 40640000 bytes per 10000-create batch; these are work bounds, not latency claims. `FoundationNameUuid.Create` takes a string: the 79-byte Entity and 87-byte scene-instance hash inputs fit `RentedBuffer`'s 1024-byte inline capacity, but formatting the name may allocate. Do not claim allocation-free identity issuance without measuring the complete NativeAOT path.

Run a separate correctness/capacity lane with 1048576 previously assigned-and-retired Entity identities, one top-level scene instance, 1048576 live scene entities, 1048576 retained occurrences, and the same 31200000 churn assignments:

```text
1048576 + 1 + 1048576 + 31200000 = 33297153 assignments
33554432 − 33297153 = 257279 remaining assignments
```

The retired setup identities must have been committed before destruction. Use the reviewed repeated/nested scene witness and measure actual resident/staged bytes, slot history, registry/provenance, reference positions, candidate coexistence and quarantine. This correctness lane is distinct from the 5-ms churn target; do not substitute the million-entity scene into the primary timing workload.

World lineage-admission qualification measures exact domain-local root/key/edge/interest/reservation counts and charged bytes, including prospective grants, whole page/vector/index capacities, old-plus-new growth, actual consumer retirement and quarantine. Record the runtime/target/allocation profile, reserved versus actual engine-owned memory, peak coexistence, latency and allocations for small fresh admission, deepest legal history/fork projection, many small histories, maximum interest contention, worst qualified hash probing and repeated retire/re-admit without lineage tombstones. Exercise production count and effective byte boundaries with valid inputs and report any dominating cap. Metadata-only values are not roots; consumers retain immutable GUID vectors, not shared-index slots. These are existing World/coexistence observations, not evidence of a performance win or a new acceptance ID.

For `M03StructuralProjection`, measure actual admitted subtree sizes and the simultaneous destruction-vector preparation/transfer/retained peaks against the qualified `CMD+D` and `O+W+D` site profile. Include the 64-byte logical payload per four-GUID identity, array/header/alignment and expansion metadata, success and exact byte-refusal boundaries, and literal public receipt order. Do not infer constant work or capacity from one command, truncate a vector, count only final retained bytes, or treat the receipt as proof of cleanup-dispatch order.

Checkpoint cancellation qualification reports structural-scan duration separately from semantic codec/restore time and cancellation-to-terminal latency. Core's initial recursive EBIN structural scan is noninterruptible; checking cancellation in the visitor does not bound or interrupt that scan, and finite size/count limits make no arbitrary-hardware latency guarantee. A canceled accepted load retains source/candidate ownership until active consumers actually unwind.

Steady-state engine-managed allocations target <= 64 KiB/hostframe for these scenes excluding explicit structural/import/editor edits; audio processing path 0 managed allocation. Pooled/native buffers counted separately, so zero allocations doesn't mean zero memory. Failure to meet target is reported regression/optimization work, not license to drop correctness checks or silently cull workload.

The owned-geometry cases in [Chapter 06](06-owned-geometry-and-numerics-interchange.md), especially NM15, additionally compare the owned kernels and by-value base class library (BCL) interchange against the same BCL operations/data, including `Vector3` tail extents, row matrices, and field-heavy mixed usage. Preserve disassembly/inlining/register/spill/copy evidence for mandatory AOT/application binary interface (ABI)/v2-v3/FMA lanes and selected optional JIT/tier comparisons alongside five-process timing/allocations; wrapper hints are not an empirical universal zero-cost guarantee. Stable representative overhead beyond noise is reported/resolved or explicitly excepted, never hidden by different workloads or fewer validations.

Measure BCL/single instruction, multiple data (SIMD) improvements before changing `SoA` layout or adopting intrinsics; optional Zip/Unzip/Concat or wider-vector acceleration retains scalar reference/bounds and supported-feature guards. New `Half`/decimal/`Complex` Application programming interfaces (APIs) do not change the binary32/chunked physics/rendering profile and bounded exact-predicate contracts. Unexpected/cold failures retain exception-based handling; no generic result layer or allocation of object-backed unions is introduced to optimize events that realistically never occur in normal work.

## 3. Test layers

Native-owner qualification compares direct handle ownership with a justified private `SafeHandle` boundary on matched native-call/creation/disposal workloads. Record wrapper allocations/indirections, marshaling/credit cost, finalizable graph retention, collection/reclamation latency and code size; fewer wrapper objects are not a universal performance guarantee. Tests prove [02 section 4](02-dotnet-api-memory-and-ownership.md#4-native-memory-handles-and-disposal)'s active-use roots, partial-construction cleanup, release ordering/counts, disposal/finalizer races, thread-affine retirement and failure transparency before considering overhead savings.

Callable qualification covers buffer windows/chains, entity chunks and 2D/3D hit visitors with ordinary structs and ref structs. Prove caller-visible `ref` state, scope rejection, exception/stop/disposal cleanup and closed generated registrations. After warmup, engine callback dispatch adds zero managed allocation; compare the same work through a cached delegate, interface-erased value and concrete generic callable. Record constrained dispatch, boxing, inlining, code size, copies and throughput/latency independently rather than attributing every difference to allocation. Coarse notifications/render-pass delegates remain valid and are not mechanically rewritten.

Use the supplied foundation's centrally selected NUnit test packages. When isolated CPU experiments are needed, use tools-only `BenchmarkDotNet` with the version selected in `Directory.Packages.props`; use the normal engine integration harness for frame/native/audio latency. Qualify the selected package versions against the selected .NET 11 SDK before release as specified in [Chapter 29](29-project-setup-build-and-deployment.md), recording their exact identities in the evidence rather than requiring default NuGet lockfiles. No performance library belongs in runtime assemblies.

Repository layering follows the physical project DAG and logical owner boundaries in [29](29-project-setup-build-and-deployment.md#logical-layering-inside-runtime). Keep concrete access, issuer and public-consumer checks with their owners; no separate Runtime architecture-policy analyzer or compiler-proof pipeline is required.

Unit/reference: clock/ranges/handles/schema/queries/directed acyclic graph (DAG)/math/packers/geometry/digital signal processing (DSP)/PCG. Property/fuzz: randomized bounded schemas/scenes/packages/graph accesses/input sequences and geometry degeneracies, stable seeds/minimal repro. `Headless` integration: all canonical simulation and saves/replays with 0/1/manyworkers. Native conformance: Windows D3D12/Vulkan, Linux Vulkan Wayland/X11, offscreen and interactive, real Windows Audio Session API (WASAPI)/`PipeWire`/input/hotplug.

Graphics validation: D3D12 debug layer/GPU-based validation/DRED as available, Vulkan validation + sync validation, exact epochs/no-use-after-free/resize/present retirement. No-error validation on one triangle is insufficient. Run allocator/descriptor stress and deliberately delayed present fences.

`Physics` tests cover stability, limits, inside hits, bounded separating axis theorem (SAT)/feature enumeration, manifolds, continuous collision detection (CCD), joints, and the default/tight budget profiles in [Chapter 33](33-physics-acceptance-and-reference.md); visualize adversarial repros but assert numeric expectations. `Audio` tests use offline deterministic digital signal processing (DSP) sample arrays plus hardware underrun/hotplug tests; silent endpoint/headless tests cannot certify real latency. `Input` tests inject normalized events plus native focus/input method editor (IME)/device tests. File fault injection covers every save phase.

### M03 package bounds and schema tests

Package loading uses the independent whole-package checksum, fixed EPKG2/EIDX1 extents, the 131072-entry directory bound, and the per-package/decode/retained-memory limits in [04](04-capacity-and-workload-profiles.md#4-content-package-and-asset-loading-limits). The production repository does not yet compile/admit M03 content schemas or load EPKG2 packages; current Core tests cover only implemented foundation behavior. Add focused tests with the actual schema registrations and package/store implementation when those features arrive. Ordinary .NET test/build results do not establish unimplemented package/store behavior.
Once tag and storage behavior is implemented, test it through the owning
Runtime NUnit suite after real registration, construction, transition and
cleanup operations. Cover type-budget success/refusal, 257-row tag-only and
Position12-plus-tag growth, the 32756-byte row boundary, absence of tag
payload columns, atomic refusal, migration, pool/reuse and cleanup. Assert
the actual owning state and result; do not add a source/result schema,
terminal observer, retained-reference recorder or isolated Runtime build.

Subscriber-exception stack provenance uses the same derivation rule. A finite
selected-case count establishes invocation/result multiplicity, not arbitrary
callback stack depth, frame-acquisition cost or rendering work. Rich capture
requires an independently qualified target profile covering its input domain,
capture and rendering bounds, actual allocation-site capacities/multiplicities,
runtime/metadata work, pool use and lifetime overlap, plus a safe admission
mechanism that does not first perform the unbounded operation it is meant to
exclude. A profile value may equal a familiar number only with its derivation
and applicability proof; no general frame/work ceiling or elapsed-time target
is implied.

The capture outcome is `C:` only after rendering completion within 4096 UTF-16
units, `T:` only after observing the 4097th unit, or an explicit `U:` reason.
`U:WorkLimit` requires an attempted admitted capture to reach an independently
derived and enforced traversal/resource extent before C/T is established.
Exceeding a profile that claims to cover a required case is a qualification
failure even when the U outcome truthfully reports the boundary. With no
qualified bounded implementation, select `U:Unqualified` without attempting
unbounded public-BCL stack capture. Optional provenance richness is separate
from mandatory report/result accounting: reserve the latter before the
publication marker; optional refusal cannot consume its credits or interrupt
captured delivery.

Rich capture does not request file/PDB information, read exception Message or
Data, traverse the application exception graph, invoke application
`StackTrace`/`ToString` overrides, or upload details. Unavailable processing
reads none of those payloads. This contract does not authorize private
CoreLib-layout assumptions or a native capture adapter; either requires its
own reviewed design and target-specific qualification before use.

Test the diagnostic context and sealed delivery report against actual
subscriber invocations and outcomes. Do not require a separate source-derived
observer membership, import-accounting record or terminal capture receipt.

## 4. Release gates

Every mandatory case from [Chapter 34](34-engine-acceptance-cases.md), [Chapter 33](33-physics-acceptance-and-reference.md), [Chapter 15](15-platform-host-and-devices.md), [Chapter 24](24-custom-audio-mixing-and-spatialization.md), [Chapter 14](14-custom-input-actions-and-text.md), and [Chapter 06](06-owned-geometry-and-numerics-interchange.md) must execute with a fresh test ID/assertion count and expected observation; an unsupported target is a gap, not skipped green. EN143 is an explicitly opt-in application-adapter case, outside core engine qualification; absence of that separate game is declared not applicable, not a skipped core test. Hardware lanes may require dedicated runners with recorded adapter/operating system (OS)/driver; software GPU tests are supplementary, not a substitute. No fabricated speed/stability/fun/accessibility guarantees.

API compatibility/schema migration/layout generation/analyzer/AOT trim warnings are release failures unless an exact reviewed bounded-metadata exception exists; dynamic-code requirements, missing generic instantiations and JIT-only core features are not exceptions. Build manifests/SBOM/native licenses/content digest/test artifacts and performance evidence preserved. The reference scripts establish selected finite arithmetic/integrity only; native engine tests require implementation.

Build and package failures remain release blockers. Concrete ownership and
issuer violations remain covered by their owner contracts and functional
negative cases; the project DAG alone does not prove every logical boundary
within an assembly.

The revised API lane additionally checks `bool` Try/out single-failure behavior, domain status/payload invariants and task fault/cancellation versus protocol rejection, no synthesized positional public contract, Runtime Async wrapper semantics and optional Unsafe Evolution caller propagation. GA qualification is separate from supported RC qualification; benchmark wins cannot waive either safety or compatibility.
