# M03-B Scene preparation allocation-evidence capability

**Status: tentative blocker proposal, usable as guidance for separately authorized candidate/evidence work. Not an implementation, numerical J/K allowance, maintained-contract amendment, qualification result, or milestone acceptance. The producer gate remains NO-GO.**

## 1. Decision and current evidence

Keep the [compiled-producer addendum](m03b-scene-preparation-compiled-producer-evidence-addendum.md), section 5, as the production-change sequence: compile/inspect the concrete private candidate, independently derive its complete catalogue and last-access boundaries, install derived preclaims, then execute only when the actual supplier prerequisites are also satisfied. This proposal adds a specific **diagnostic-capability gate**, not another root/driver design or permission to run the currently incompletely funded producer.

For allocation-complete empirical corroboration, require a verified **per-allocation CLR profiler or equivalently complete target/runtime instrumentation**, with separate coverage of reachable native allocation/capacity paths. Ordinary sampled tracing is not a fallback for that capability. Even this stronger recorder proves only the covered executions; universal finite J/K and safe release still require the source/compiled/runtime proof below.

The reported `dotnet-trace collect --profile gc-verbose` attempt yielded six `GCAllocationTick` events, all large test-platform/runtime objects, when parsed with TraceEvent 3.1.23. This is supplied evidence, not reproduced here; the removed trace cannot now be checked for process identity, boundaries or loss. Six ticks are not six allocations. Neither a different parser nor a larger trace buffer reconstructs unsampled small allocations. Do not repeat that unchanged probe as a gate-closing attempt. Separately verify the executing testhost PID rather than assuming a process named `dotnet.exe` is the producer; the report alone does not establish that the wrong process was selected either.

Current source observations, bound by section 7:

| Input | Consequence for the proposed evidence |
|---|---|
| `SceneLoader.PrepareAsync`, `PrepareCoreAsync`, `PreparePlanCoreAsync` | Two binding backings are preclaimed before freeze. The public result is still compiler-completed, and `PreparedScene` is constructed after planning/resource preparation. Loader/lease/builder/plan retention is not the proposed precreated whole-operation root. |
| `PreparedScene.Dispose` / `DetachForActivation` | Disposal now keeps the plan through fallible disposal, keeps the option debt until credit return, and keeps the use through release. Admission transfers the retained owner to the request before clearing the wrapper. These changed bytes invalidate older carrier/retention observations; they do not supply a producer/body join. |
| `SceneActivationRequest` | Cleanup restores a failed plan into the request, preserves failed option-credit debt, and anchors activation publication failures. Its separate activation use survives successful publication until the following release path. Those paths, not an earlier request layout or its existing 256/512 constants, are the relevant inspection inputs. |
| `SceneComponentRestoreContext.Revoke`; `SceneActivationExecutor.BuildEntityDescriptions` | Revocation now clears the option snapshot in the callback's `finally`. This severs that alias; it is not proof that every stack alias, retained context field or producer has stopped. |
| `PreparedAssetLeaseRetainsBundleAcrossActivationUnloadAndDispose` | One test executes several preparation attempts: underfunded bindings, invalid bindings, a full-World-cap refusal, then two successful preparations. The earlier attempts can warm the later ones. Its blocking scheduler witnesses **activation**, not preparation, publication. A test name, Scene AssetId, or whole-test allocation total cannot identify one preparation interval. |

The local PATH inspection found `dotnet-trace` and `dotnet-dump`; neither establishes the required recorder capability. The coordinating session used `dotnet-trace` and a temporary TraceEvent parser for sampled/aggregate diagnostics, but verified no startup per-allocation CLR profiler, native allocation recorder, or actual-return observer. The temporary parser files were removed after use.

**Subsequent aggregate observation, outside the section 7 snapshot:** a temporary focused Runtime test measured **20,176 bytes** from `GC.GetTotalAllocatedBytes(precise: true)` around one real package-backed `SceneLoader.PrepareAsync` call on `.NET 11.0.0-rc.3.26522.148/X64`. Package, AssetStore and World setup preceded the counter; the interval ended when the public preparation Task returned and excluded PreparedScene disposal. The process-wide counter includes any concurrent test/runtime allocation, gives no owner/type attribution, and proves neither retained/live storage nor native capacity or a producer tail after Task completion. TRX: `artifacts\tst\Debug\tagoo_GOODING-ZEN4_2026-11-15_23_01_42_net11.0.trx`, SHA-256 `300BB82A79E903AC6BBF9A54BC9D3865D5FCAD71AD3F6AEAD826B007DC1E341F`; the temporary probe source was removed after capture, SHA-256 `2E8F7E82859134ABE93EA69208E27200F648CFE53A26202004F69B8AEB98BC2B`. This single process-wide traffic sample is not a J/K charge and does not change the NO-GO disposition.

## 2. Measure different quantities separately

| Quantity | Meaning and limit |
|---|---|
| Managed allocation traffic | Sum of newly allocated managed object extents during a stated interval, across **all** participating threads. It includes short-lived objects even when collected before the next snapshot. It excludes preexisting objects and native storage. This is not a live-storage peak or a subclaim ledger. |
| GC-live / heap-resident storage | Reachable objects at a particular GC/heap observation, or allocated heap storage not yet reclaimed. These differ from each other and from contractual ownership. A snapshot misses already collected transient peaks; reachability does not prove a future consumer cannot access an object. |
| Scene-owned simultaneous capacity | Sum of complete, disjoint accounting rows over their proved ownership intervals, including protected unused provision, replacement overlap, native/lazy state and stopped-but-unreleased debt. This is what the purse must cover. Transfer changes the owner without making the storage disappear. |
| Retained result / receiver storage | Storage still owned by Available/Admitted bodies, installed plans/bundles, or independent R receivers. Completed Tasks and observer-owned graphs have their stated owners. Ending the preparation producer does not automatically end these obligations. |

Traffic can exceed P when genuinely ended scratch claims are reused. Conversely, reserved capacity can exceed materialized managed bytes. A complete traffic total bounds new managed allocation residency only for that particular recorded interval; it excludes earlier live storage/native capacity and establishes neither the accounting charge nor a universal bound.

Keep `J_shared`, `J_producer`, `K_body` and `K_publication` disjoint as specified by [footprint closure](m03b-scene-preparation-footprint-closure.md), section 4. The whole carrier belongs to its shared row until the joined end, even after fields are cleared. Supplier X and retained R remain with [workspace closure](m03b-scene-preparation-workspace-closure.md); do not absorb their variable allocations into a guessed fixed J/K.

For every allocation row, retain creating site/type, maximum multiplicity or input-derived capacity, independent byte derivation, coexistence interval, named grant/issuer, retained path, and actual end/transfer. Record both runtime-reported bytes and the accounting charge. Chapter 04's `64 + RoundUp16(capacity * stride)` array charge is not a promise that a profiler reports that many physical bytes. Verify what a size API includes before adding headers/alignment; do not count them twice.

### Ordinary .NET tooling: useful evidence, not the missing proof

| Tool/API | Can establish | Cannot establish |
|---|---|---|
| `dotnet-trace` GC allocation ticks / sampled allocation views | Sampled allocation sites, approximate traffic, GC/runtime scheduling evidence under the selected providers. | Every small allocation, exact per-operation object membership, complete live peak, or last access. Zero transport loss does not remove sampling. |
| `GC.GetAllocatedBytesForCurrentThread` | Managed allocation traffic on that thread; useful for a genuinely synchronous isolated component. | An async operation that migrates threads or has worker allocations; retained/native storage or last access. Subtracting counters from different threads is not an operation measurement. |
| `GC.GetTotalAllocatedBytes(precise: true)` | A process-wide managed allocation counter under the selected runtime's counter contract. | Call-site/owner attribution, simultaneous storage or native work. A VSTest interval includes other work; subtracting a different baseline run does not isolate Scene allocations. |
| Heap dump, `gcdump`, SOS heap/root inspection, GC heap-size counters | Storage/root paths at the observation and useful corroboration of retained graphs. | Transient allocation traffic or an all-schedules release proof. Forced GC changes timing; object disappearance is not a production acknowledgment. |
| Reflection, IL and native-code inspection | Layout/control-flow inputs to an independent derivation, including compiler/runtime tails. | Actual branch execution or complete transitive size from fields alone. `Unsafe.SizeOf<class>()` is not instance size. Breakpoints on a few allocation helpers miss inlined fast paths unless completeness is separately proved. |

## 3. Required recorder capability

Select an existing, reviewable capability before collecting another purportedly complete record. Do not add a generic runner, production tracing API, managed allocation interceptor, per-operation `AsyncLocal`, or fake Scene producer to obtain correlation.

1. **Start before the relevant cold work.** For a CoreCLR lane, a concrete candidate mechanism is a startup CLR profiler using `ICorProfilerCallback.ObjectAllocated`, with `COR_PRF_ENABLE_OBJECT_ALLOCATED` and `COR_PRF_MONITOR_OBJECT_ALLOCATED` enabled during profiler initialization, and a supported size query such as `GetObjectSize2`. Verify those APIs' coverage and restrictions against the **actual selected CLR implementation**, not just their names. Require coverage of all relevant managed allocation categories, including runtime-created holders and applicable small/large/pinned-heap allocations. An attaching profiler enabled after first Scene work cannot establish cold coverage. A sampled profiler mode is not this mechanism.
2. **Capture the actual process and all threads.** Identify the executing testhost/consumer by PID, command line, loaded Runtime assembly hash/MVID and actual test selection. Record native/managed thread identities, creating method/native code version and offset, closed type, reported size and a GC-aware allocation identity. Include entry-thread work before the first suspension, worker/I/O completions, registration/context/dispatch work and late tails. Do not filter by assembly name: BCL/runtime allocations can be Scene-owned.
3. **Make identity and loss explicit.** Managed addresses can move and be reused. Correlate allocation/GC movement/survival using the profiler's documented protocol; a raw `ObjectID` address is not a durable identity. Do not root/pin every observed object to simplify tracking. Use tool-owned native recording buffers and supported callback operations, with visible overflow/dropped-record/truncation counters. Unhandled reentrancy, a missing stack/site map, unsupported allocation category or any lost record makes the claimed coverage incomplete. Matching a component's expected count is a sanity check, not proof that the recorder covers the runtime closure.
4. **Reconstruct async causality, not just thread time.** Use existing public Task, state-machine/box, pending child Task and exact `WorldGuidUseControl` identities, with safe inspection of their real links and compiled sites. Allocation callbacks precede constructor completion; do not assume initialized fields are available there. Correlate every preparation attempt separately, including attempts which fail before a public Task exists. TPL/Activity events may assist but do not by themselves guarantee complete causality. If an event cannot be assigned safely, keep it unresolved or conservatively included with a derived bound; do not subtract it as test-platform noise.
5. **Cover native/lazy work independently.** `ObjectAllocated` is not a native allocator recorder. Follow all reached runtime helpers, including fault dispatch-state capture such as `GetFrozenStackTrace`, synchronization inflation, native capacity growth and cold metadata. For each, prove allocation-free behavior, derive bounded native capacity/lifetime from the pinned implementation, or provide complete instrumentation of its actual allocating paths. Process working-set deltas or one OS heap hook do not cover pooled suballocation/direct virtual allocation automatically. An arbitrary foreign exception/context graph remains a named input-owner obligation; new Scene-created copies/capture holders still require rows. An unbounded native/input-derived copy fails the finite catalogue, regardless of observed sizes.
6. **Keep the observation distinct from production.** Record profiler/debugger options, JIT tiers, ReadyToRun/inlining/optimization effects, Task tracing and context modes. Profiling/ReJIT/enter-leave hooks can change generated code and allocations. Map observations back to the final uninstrumented source/native paths; an instrumented maximum alone does not qualify them. A method-leave notification can precede actual return. NativeAOT requires its own compatible mechanism and target proof; CoreCLR profiler availability is not AOT support.

This is a capability specification, **not a claim that those interfaces alone provide complete managed/native attribution or a universal result on this runtime**. Missing coverage remains an explicit blocker. Tool/observer allocations have their own owner; that exclusion does not make all framework allocations free.

## 4. Safe collection and reconciliation

The sequence remains compile/derive/fund/observe, avoiding the circular procedure “run unfunded preparation to discover the constant that would have permitted that run.”

1. **Compile-only inspection first, under separate authorization.** Follow the existing addendum's bounded `PreparedScene`/ordinary-driver/retention candidate. Freeze its direct and transitive proof inputs, actual SDK/compiler/CoreLib/runtime identities, options, binaries/MVIDs and generated/native code versions. The current source hashes below are not that future candidate. `global.json` selects the SDK with roll-forward; do not add another SDK pin.
2. **Derive before integration.** Resolve every potential J/K allocation and native capacity row from the compiled candidate/runtime closure, including constructors, cold/lazy state, fault/cancel and cleanup overlap. Isolated component observations have harness-owned funding and qualify only those components. Install only independently derived claims through the real issuer. The stable root, actual supplier grants and release/transfer prerequisites must be complete before an integrated call; a profiler does not waive them.
3. **Record the real producer.** Use ordinary owning-project build/test outputs and existing package-backed behavior, not a second implementation. Enable the verified recorder before startup, and retain its readiness, process/code identity and loss information. The capture interval starts before the first relevant allocation and ends after the proved producer/body joins or the documented stopped-but-unreleased disposition—not at Task terminality or the test's final assertion.
4. **Reconcile, do not fit.** Map each observed allocation/native capacity to a catalogue row and one owner. Compare its actual coexistence with the independently derived interval. Additional Scene-owned storage, an unsupported native branch, allocation before preclaim, or an unexplained late access fails that row. A smaller observed peak never reduces the derivation. Reuse an existing owner's proof rather than counting its storage twice.

Apply the existing footprint/behavior matrix with these capture requirements:

| Dimension | Evidence required |
|---|---|
| Cold / warm; synchronous / suspended | A fresh process with no earlier relevant Scene/type/generic/cache initialization, and separately identified warm calls. Record already initialized host/runtime state with its owner; do not silently exclude first-use work. Verify actual suspension and worker participation rather than inferring them from `async`. The existing multi-attempt fixture is not a cold successful-load observation by itself. |
| Success / fault / cancellation | Real no-resource and real-resource routes; synchronous validation/freeze/refusal separately from admitted execution faults; pre-cancellation separately from selected-token cancellation at reached supplier stages. Include fault publication and execution-plus-cleanup failure composition, not just successful TCS completion. Retain original exception/token identities. |
| Cleanup overlap / failure | Available disposal and admitted-body completion while publication or registration is held, plus protected cleanup/publication coexistence. Inspect retained roots and exact remaining debts on actual cleanup/credit-return/publication failures. If no safe real stimulus exists, leave that behavior observation open; capacity refusal is not a cleanup-failure substitute. Do not inject artificial OOM or a throwing fake allocator/preparer. |
| Caller registrations | Zero, one and several raw Action/await/scheduler registrations; registration before, racing with and after completion; context-flow variants. Identify observer wait registrations too: blocking `GetResult` on a pending Task is not a zero-observer path. Use external observation for a genuinely zero-registration lane. Distinguish an already-completed Task from a pending public Task on which registrations can be installed. |
| Exact reached claims | Independently derived exact/one-byte-short claims with all preceding setup funded. Verify the denied stage was reached and denial preceded its allocation. A bootstrap refusal says nothing about a later publication/cache/resource peak. |

These are finite witnesses, not exhaustive continuation/schedule/foreign-input enumeration. Preserve the tentative [handoff](m03b-scene-preparation-handoff-closure.md), section 7, **external-continuation provenance rider** as an unresolved policy dependency: caller-induced registration/list/context/scheduler/dispatch storage follows that caller, even inside completion. Scene-created registrations and its own Task/fault/cancel bookkeeping do not.

For the runtime dispatch path identified by that handoff, additional caller registrations can induce additional completion-time allocations with no cardinality bound tied to P. Zero/one/many measurements cannot turn that dimension into a Scene-owned constant. If the rider is rejected, the all-dispatch constant remains blocked on a separately reviewed boundary/funding design—not a larger K. If it is accepted, classify by provenance rather than allocating thread, and still retain Scene provision while external dispatch holds its publisher.

## 5. Last access is a separate proof

Build the footprint proposal's producer table from **final** source/native code: entry/freeze; ordinary driver parts including registration return; public publication; Scene-created dispatch/context wrappers; child planner/resource producers; body cleanup; and final lower release. For each, identify its grant, last potentially allocating/accessing instruction, all enclosing suffixes, synchronization edge, and the **production-valid** return/transfer fact that makes release legal.

Use external debugger/profiler stops at actual call-return sites and the existing legal caller-scheduler pattern to corroborate those facts. Task terminal timestamps, callback return, method-leave notifications, GC/root snapshots and a trace containing no late access are insufficient. Diagnostic synchronization is evidence, not an acknowledgment the production implementation lacks. Inspect exception paths and code versions as well as normal returns; any post-signal Scene-owned work remains funded through a valid later boundary.

**Distinguishing witness:** hold real preparation completion dispatch after result visibility. A different caller disposes the Available result, or admits and completes its body. The body-only claim may end/transfer, but the whole carrier and remaining producer/publication provision stay charged. Returning from the public publisher is still insufficient if a registering part, child producer or context-restoration suffix has Scene-owned work left. Only the complete joined proof permits the relevant refund, once. The existing activation fixture provides the scheduler stimulus, not an executed preparation witness.

If a compiler/runtime/supplier tail has no observable production end/transfer, report the exact method/native site, object/access, still-held grant and missing acknowledgment. The existing compile-only publisher candidate exposes one useful boundary; it does not manufacture child/runtime acknowledgment. Neither more allocation samples, another observer Task, an `async finally`, normal-path quarantine nor a delayed refund closes that gap. Keep the specific boundary NO-GO.

## 6. Concrete next action and remaining gates

**Next evidence action:** identify a compatible startup per-allocation profiler/equivalent mechanism and document its selected-CLR coverage, loss handling, GC-aware identity/async attribution, native closure coverage and actual-return observation capability. Resolve this before promising allocation-complete collection. No such capability was verified in this task; acquisition or implementation requires separate authorization. If it remains unavailable, record that blocker instead of retrying sampled tracing.

The existing compile-only candidate is still useful and does not depend on first obtaining profiler measurements; it remains the next production-code step under its separate authorization. It must not be executed integrally until the independent finite catalogue, real preclaims, stable rooting and supplier prerequisites exist. The recorder can then corroborate the derived candidate, not define its expected bound.

Keep per-run catalogues, disassembly, allocation/native records and fresh nonzero selected/executed test results under the existing ignored artifacts layout, using `docs\project\Building.md`; Debug precedes Release. Do not introduce a qualification runner or edit ordinary workflows for this purpose. Managed evidence, Release, enabled diagnostic/Runtime Async modes, NativeAOT, actual public consumers and Chapter 35 milestone acceptance remain distinct obligations.

This task adds only this proposal. No source, test, dependency, maintained-design, roadmap or existing-proposal change; no build/test, allocation experiment, external service, install, commit, push or PR was performed.

## 7. Narrow current-input manifest

Raw file SHA-256 snapshot at `2026-11-16T06:52:42Z`, HEAD `31f156a51ccc462ae9c5cc8d4e87e00c0054dec4`. Paths are repository-relative and include the actual dirty/untracked bytes, not reconstructed HEAD files. This is a new bounded evidence-planning input set, not a refresh/acceptance of any prior review. Recheck before using its source-specific dispositions; a mismatch invalidates the affected observation rather than silently updating the expected hash.

| Input | SHA-256 |
|---|---|
| `docs\proposals\m03b-scene-preparation-budget-accounting.md` | `3F8E4F0315C6CC2933FC982F64D5E94B9070EB1876A74B0688118BB17AFF5A62` |
| `docs\proposals\m03b-scene-preparation-workspace-closure.md` | `9D7280A89C5BB75890ADAF65E26F8C80766B8687EE0608623D0F97D519BE32C3` |
| `docs\proposals\m03b-scene-preparation-handoff-closure.md` | `628B09039114AACAEA65116DD584B2F94B554011306AB600414C5A2A809A7FBA` |
| `docs\proposals\m03b-scene-preparation-footprint-closure.md` | `940BAEF78CCFBD720E292167C3AF9C7C00AABDBC4F448E6E4A00793BB54230DA` |
| `docs\proposals\m03b-scene-preparation-compiled-producer-evidence-addendum.md` | `ECCC174D5DF86E195731EB3BE74FBE5A349D0214C8F94DE35769503D4418595C` |
| `docs\project\Roadmap.md` | `508F59CCCD75C74F3BF70EC3DCC01F107EC4F0B9AF52A1C78DA260E6E57A1BE4` |
| `docs\project\Building.md` | `F53AB58B89EEAE7E17B70970075BE9322A936D443BB6D5239C3238E594DEABC6` |
| `docs\design\02-dotnet-api-memory-and-ownership.md` | `40F913A50434209E09941C22625A6D0722202BAB97EBAC0E153C1EA3368E2291` |
| `docs\design\04-capacity-and-workload-profiles.md` | `A4D86F3FA373A64548435C53582BF5916E29D77F34650F9D161CCEC90E6F0245` |
| `docs\design\16-scenes-transforms-and-cameras.md` | `B012F42B23CEDE9C7CDB71203D8357CD19E1626823EA25EF3119961292D3698A` |
| `docs\design\35-implementation-roadmap.md` | `24EEC6E92B7AD904D552F290EF748FFC8FCE13B6B94A89EBD9F39487D4584D87` |
| `global.json` | `1BCE01871321C2DF82E887DA428CE5C121A6C6541FE65017FE3D249BE47E190C` |
| `Directory.Build.props` | `770EE4B2A422861A6F23F0D39DD0ECA361A81CB87E64EBAC4E12170F71231D7A` |
| `sources\Directory.Build.props` | `D44373AF315523F8AF9F09F36BDED2A688E2B5C1572FCC0732310C7EF590EAD8` |
| `sources\TerraFX.Runtime\Scenes\SceneLoader.cs` | `2E3F6C347AA01B5914980064CDBF7A2A0414DCD74C31A5223365F4DD4D15FEBC` |
| `sources\TerraFX.Runtime\Scenes\PreparedScene.cs` | `C84695329A76A794B0CF82093A87DE6DFBE9D2C114F6817A0D61470864973CF3` |
| `sources\TerraFX.Runtime\Scenes\SceneActivationRequest.cs` | `5C6FFD87036807A2863EAFCDFA0DAFE8D3EEB5D9AB093E5804A4965EC7F0B460` |
| `sources\TerraFX.Runtime\Scenes\SceneComponentRestoreContext.cs` | `78CE617A899B1DFC6BAF8E329E301A745B4A9886B65A5D59930023714A73A5F5` |
| `sources\TerraFX.Runtime\Scenes\SceneActivationExecutor.cs` | `7D12EB728B0563ED673A86A1E1846AAF0D3FC37A07C139CADECF8F34C646EC69` |
| `sources\TerraFX.Runtime\Scenes\SceneLoadOptions.cs` | `E95382B5F26B7DD398C34BA0B4F20A6A0E76BF9F392B5B3B4BAD88AD1F898A80` |
| `sources\TerraFX.Runtime\Scenes\ScenePrefabExpansionPlanner.cs` | `D8D807E1925A5CF2538A41076CA3606A7FE3F31005B452429F20C99C5612CA27` |
| `sources\TerraFX.Runtime\Entities\WorldGuidUseControl.cs` | `1931CD2B5DFD81A3F0CD8A4E6F3D09C5D9B006B2E29817F5C295C77F78202B68` |
| `tests\TerraFX.Runtime.Tests\Scenes\PreparedSceneAssetRetentionTests.cs` | `E342237B7645BE805DD924855B2239907773D31753D22B8B2261BF26C20452F4` |

This manifest covers only the direct proposal/contract/source/witness inputs to these conclusions. It is not a repository-wide freeze or a transitive allocation/schema/fixture review. No new schema/API is proposed. Earlier proposals' imported CLR observations remain imported, not independently reverified upstream source; the current producer has not been compiled or executed here. No binary, actual loaded runtime, native-code version or removed trace is claimed to match these hashes. The subsequent candidate must establish those identities and its complete proof closure separately.
