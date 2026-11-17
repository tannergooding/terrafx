# M05 skinned-normal diagnostic storage-hazard scaling

## 1. Disposition and boundary

**Tentative unblocker for review, not an accepted contract, implementation, measurement, or qualification result. Current implementation disposition: NO-GO for closing the recording-scaling gate.**

Add this correction to the [recording-budget proposal](m05-skinned-normal-diagnostic-recording-budget-closure.md), retaining its joint **33,554,432-byte preparation/recording ceiling** and independent **16,777,216-byte completion ceiling**. Pre-sizing pending writes bounds their allocation, not the work spent searching or removing them.

Select a **diagnostic-only, resource-partitioned pending-write tracker**: one pending slot per geometry/palette/result resource, an append-only array retaining every exact work-packet write, and a deterministic sorted geometry index. Validate the closed diagnostic shape before using this tracker. Recording hazard work becomes `O(D log(M + 1) + B)`, with `O(D + M)` retained hazard storage; declaration-index construction costs `O(A log A)`. There is no scan of earlier packets and no list-tail compaction.

Keep one compute pass and one recording-owner request. Preserve all four Frame `StorageBuffer`/`ReadWrite` bindings, every exact `u2` packet view, every draw/result, pipeline rebinds and uses, paired graph versions, barrier command order/ranges/access flags, graph dependencies, original result mapping, and completion/retirement semantics. Do not bind the whole work owner, narrow reflection, erase unordered writes, substitute a result prefix, or create `D` separately owned requests. Ordinary recorders and unrelated graph semantics are outside this correction. No public API, identifier, diagnostic code, shader ABI, or allowance is added.

The parent reported implementation changes **after kickoff and in two subsequent input-state updates**. Earlier snapshots are discarded. Sections 2–8 assess the re-frozen current bytes, including prebuilt bindings, per-draw pipeline rebinds, `MeshBufferCount`, bounded addition checks, and reusable scratch. These are parent implementation under review, not changes made by this proposal. Section 9 discloses the active M04 readback work and temporary untracked instrumentation.

Only this new proposal is edited. This author performs no implementation, test, build, shader tool, commit, push, or PR operation. The separately parent-reported failed build is disclosed below.

## 2. Current source trace and actual diff

Full paths and whole-file hashes are in section 9. Line references refer to those current working files.

| Direct evidence | Consequence |
|---|---|
| `RenderMeshC0NormalDiagnosticWork.cs:51–150,189–278` | Freezes RFC DrawId order, unique indexed-vertex membership, result rows, and monotonically packed, nonempty work packets. Counts selected mesh-buffer slots in `MeshBufferCount`. Its present byte estimate still covers five preparation arrays, not the complete recording envelope. |
| `Renderer.MeshC0Graph.cs:1030–1054,1124–1190`; `GraphResourceRegistry.cs:221–239,432–466,748–751` | Creates a separate graph resource for each mesh vertex buffer and separate palette/work/result resources. The diagnostic declares one whole-range read/write pair for each selected geometry plus palette, work and result. Each pair contributes two access records. Exact packet **bindings** do not require changing these declarations. |
| `RenderMeshC0NormalDiagnosticGraphPass.cs:61–162` | Now prebuilds `D` immutable binding sets. Reuses geometry/palette/result bindings; allocates one exact packet binding per draw. Reports `B = 3D-M-2`, commands `6D-M-2`, pending capacity `D+M+2`, and `D` pipeline uses. Here the cached geometry count counts used mesh-array slots; the inspected caller gives those slots distinct resource identities. An independently supplied aliased array would invalidate that inference. |
| `RenderMeshC0NormalDiagnosticGraphPass.cs:166–177` → `CommandRecorder.cs:54–100` | Each draw now calls `SetPipeline`, `SetBindings`, `Dispatch(1,1,1)` through the existing recorder facade. The previous proposal's one-pipeline callback rejection is no longer the current callback shape. Native acceptance has not been executed here. |
| `RenderGraphPlan.Recording.cs:174–214` → `GraphicsDevice.RecordPass.cs:12–116` | Passes the diagnostic capacity through one request to one owner-thread recorder. Other passes pass `default`. No per-draw request/control is currently needed. |
| `CommandRecorderRecordingCapacity.cs`; `CommandRecorderState.cs:87–112,207–227,462–599,617–660,880–936` | Current changes pre-size/check commands, leases, pending writes and four-binding storage; reuse expected/actual binding and barrier scratch; cache the same pipeline layout; avoid a zero-sampler lease list. These reduce growth/temporary allocation but do not index pending writes or declared accesses. Capacity mode alone is not a verified diagnostic-shape certificate. |
| `CommandRecorderState.cs:370–397,760–877`; `StorageResourceUse.cs:15–78` | Every positive dispatch compares all four current uses against **every** pending write. A hazard is graph resource identity/type, not version identity, plus overlapping bound ranges and a write. Each selected barrier resource then incurs further pending-list scans for flags, representative version and removal. Pipeline rebind clears bound uses, **not pending writes**. |
| `CommandRecorderState.cs:855–865` and its HEAD-to-worktree diff | Replaces capturing `RemoveAll` with reverse `RemoveAt`. This removes that delegate allocation, not the searches or suffix movement. In this diagnostic shape each removed resource has exactly one pending entry; work packets are never removed. Section 4 distinguishes this from the general multiple-match case. |
| `CommandRecorderState.cs:718–736`; `GraphicsGraphSubmission.cs:1520–1553` | Binding validation also scans access arrays. The native read/write-pair search does not stop at the matching pair. Merely fixing pending writes can leave a `D × A` recorder lookup and `D × totalGraphAccesses` native-preflight lookup. Section 6 closes only those diagnostic-specific repeated lookups. |
| `GraphicsGraphSubmission.cs:1123–1208,1420–1431,1720–1755` | Still requires a fresh complete binding set after each pipeline command. Storage barriers retain the graph-version/import range, not a newly invented smaller range. Current source is consistent with the new per-draw rebind schedule. |
| `Renderer.Submit.cs:792–823`; `GraphicsDevice.SubmitGraph.cs:126–166`; `RenderGraphPlan.Submission.cs:81–149`; `RecordedPassCommandList.cs` | Recording and native preflight precede graph queue acceptance. Submission shares the immutable command payload. Mutable/frozen command arrays coexist at recorder completion; releasing leases does not clear the retained `Commands` property. |
| `RenderMeshC0ShaderReflection.cs:32–109`; `SkinnedNormalDiagnostic.hlsl:23–51,105–112` | Four conservative Frame storage operands remain. `u2[0]` supplies the selected packet's parameters; result writes still target the frozen row. Load-only HLSL operands are not permission to stop tracking their reflected writes. |
| `RenderSkinnedNormalDiagnosticPrototypeTests.cs:33–168` | Temporary instrumentation constructs binding plans and prints allocation/stride values. It does **not** call `Record`, `RecordPassAsync`, or native preflight, measure hazard work, or assert the scaling bounds. Its existence is not recording evidence; it was not run here. |

**Parent-reported failed check:** after this review began, the parent ran a Runtime-independent Debug build of the Graphics recording-capacity path. It failed with `TFX0002` member-ordering diagnostics in `CommandRecorderRecordingCapacity.cs`, `CommandRecorderState.cs` and `GraphicsDevice.RecordPass.cs`, plus `IDE0028`/`IDE0007` in `CommandRecorderState.cs`. The parent reported no file changes from that build. The exact invocation/log was not supplied here; this is a disclosed failed check, not an independently reproduced result or evidence that these bytes compile. Source remains untouched by this author.

[Chapter 19 sections 1–2](../design/19-render-graph-synchronization-and-presentation.md#1-graph-records-and-compilation) and [Chapter 30's recorder contract](../design/30-public-api-review-surface.md#7-graphics-and-rendering) own exact returned-version bindings and resource-scoped storage ordering. [Chapter 04 section 1](../design/04-capacity-and-workload-profiles.md#1-selection-and-exceptions) includes metadata, spare capacity, staging and coexistence in admission. Chapters [02](../design/02-dotnet-api-memory-and-ownership.md#3-leases-and-asynchronous-operations) and [17](../design/17-graphics-device-and-resources.md#3-uploads-mappings-and-disposal) retain asynchronous ownership and real-fence protection. Chapter [18](../design/18-shaders-and-material-pipeline.md#3-reflection-and-binding-validation) is unchanged: no narrower metadata substitutes for actual shader/reflection agreement.

## 3. Exact barriers, commands and writes

For positive `D`, define `M` as **distinct selected vertex-buffer resource identities**, not draw count, DrawId count, a set of vertex ordinals, or an unchecked mesh-array length. The inspected construction establishes `1 <= M <= D` and distinct geometry/palette/work/result identities. Let `m_j` count geometries seen through dispatch `j`.

The diagnostic pass has `R = M + 3` resource identities and exactly `A = 2R` declared access records: one initialized prior-version read and one returned-version write per resource. These are pass-local counts, not the total graph access count.

Every dispatch appends four conservative pending writes. Geometry's same whole range overlaps on every revisit; palette and result overlap after their first use. Strictly increasing, disjoint work views do not overlap:

```text
geometry barriers       = D - M
palette barriers        = D - 1
result barriers         = D - 1
work-packet barriers    = 0
B                       = 3D - M - 2

SetPipeline = SetBindings = Dispatch = D
recorded commands N     = 3D + B = 6D - M - 2
pipeline uses           = D
pending writes appended = 4D
pending writes removed  = B

P_0                     = 0
P_j                     = j + m_j + 2, for j >= 1
P_final = high water    = 4D - B = D + M + 2
```

Thus **`B = 3D-M-2` remains correct** for this shape after the parent's changes. Resource-scoped barriers remove the single pending write for that geometry/palette/result, then the dispatch adds its replacement. All `D` packet writes remain pending. A rebind adds a command/use, not an ordering event. For `D = 0`, preparation returns no diagnostic pass; all these counts are zero and the positive-`D` formulas are not evaluated.

Use distinct packet ranges `w1`, `w2`, ... in each schedule below. `A`, `B`, `C` in the schedule column name geometries, not access/barrier counts.

| Geometry schedule | D | M | Geometry / palette / result / work barriers | B | N | Appended / removed writes | Final/high-water pending |
|---|---:|---:|---|---:|---:|---|---:|
| A | 1 | 1 | 0 / 0 / 0 / 0 | 0 | 3 | 4 / 0 | 4 |
| A,A | 2 | 1 | 1 / 1 / 1 / 0 | 3 | 9 | 8 / 3 | 5 |
| A,B | 2 | 2 | 0 / 1 / 1 / 0 | 2 | 8 | 8 / 2 | 6 |
| A,A,A | 3 | 1 | 2 / 2 / 2 / 0 | 6 | 15 | 12 / 6 | 6 |
| A,B,C | 3 | 3 | 0 / 2 / 2 / 0 | 4 | 13 | 12 / 4 | 8 |
| A,B,A | 3 | 2 | 1 / 2 / 2 / 0 | 5 | 14 | 12 / 5 | 7 |

`A,B,A` distinguishes a resource index from tracking only the most recently bound geometry. Before its third dispatch, the original A write is still pending and must produce a geometry barrier. Packet `w3` remains a new exact view; neither A's revisit nor the shared result owner merges result rows.

## 4. Why the current bounded list is still quadratic

The first detection loop alone executes exactly:

```text
Cdetect = 4 * sum(P_k, k = 1 .. D-1)
```

It does not break after discovering an overlapping write. Consequently:

| Schedule family | Exact first-loop `HasHazardWith` calls | Exact suffix elements shifted by current removals |
|---|---|---|
| One geometry repeated D times | `2D(D-1) + 12(D-1)` | `5(D-1)` |
| D different geometries | `4(D-1)(D+2)` | `2(D-1)` |
| `A1,...,AM,A1,...,AM`, `D = 2M` | `14M² + 10M - 8` | `2M² + 5M - 2` |

The first loop already makes **34,361,049,076** calls for one geometry and **68,720,001,016** for all different geometries at `D = 131072`. Further per-barrier scans are additional work. These are derived operation counts, not elapsed-time measurements or promises that the row ceiling fits the byte budget.

Reverse removal is not free: `RemoveAt(i)` shifts the surviving suffix. For one repeated geometry the old geometry/palette/result sit near the tail, so each subsequent dispatch shifts `3 + 2 + 0 = 5` entries. For all-new geometry only palette/result are removed, shifting `2 + 0`. In the two-cycle family, first-cycle removals shift `2(M-1)` entries; each second-cycle geometry removal shifts `2M+1`, followed by two palette shifts. At `M = 65536`, that is **8,590,262,270** shifted `StorageResourceUse` values.

For `A,B,A` specifically, detection performs `4(4+6) = 40` calls and removals shift `2 + 7 = 9` entries. The last A cannot be dropped to avoid these movements.

In this particular shape there is one match per removed resource, so the former `RemoveAll` also had to compact that same suffix. The current edit does **not** newly change this shape's removal asymptotic; it leaves the existing quadratic revisit schedule intact. Reverse `RemoveAt` can be worse than one stable compaction with multiple scattered matches, but that broader recorder case is not the selected diagnostic contract and is not redesigned here.

Fixed four-element validation scratch and pre-sized lists improve allocation behavior. Neither changes these operation counts.

## 5. Selected private recording algorithm

### 5.1 Closed-shape recipe and construction order

Opt in only the built-in diagnostic through the existing internal recording boundary; do **not** infer this shape merely from `IsBounded` or four bindings.

Before queueing that recording request, reserve the section 7 ledger and construct a private immutable recipe from the frozen bindings and pass metadata:

1. Retain `D`, the existing pass/device-epoch identity, and one copied immutable array of the `D` prebuilt `BindingSet` references. This copies references, not binding objects or GPU data. It permits checking the exact set/order without inspecting live World state. Charge the additional reference array.
2. Sort an exactly sized `int[A]` access-index array by resource identity and read/write member. Group once, establishing exactly `R` initialized pairs with `IsStorageReadWritePairOf`, exact version references, ranges, pass and epoch. Retain an immutable resource-entry table: `M` sorted geometry entries plus fixed palette/work/result entries. Each entry contains the existing `ulong` resource identity, expected `GraphBufferVersion` reference, exact `GraphBufferRange`, and two `int` read/write witness offsets into the recipe-retained immutable pass-access array. These offsets are local array positions, not new resource IDs. A deterministic bounded sort/index is required; a hash table's expected constant-time behavior is not the worst-case proof.
3. Independently validate `M` from resource identities. Verify the three fixed roles are mutually distinct and cannot alias any geometry. Check that the input `MeshBufferCount` agrees, not that mesh-slot count alone proves identity.
4. Verify each frozen set has exactly Frame storage bindings 0–3, no textures/samplers, and the expected exact versions. Geometry, palette and result each bind their unchanged full selected range. Every work binding is the original packet range, within its work version and paired declaration, with its checked alignment and extent. Verify the packet sequence has `start_j >= end_(j-1)`; gaps remain gaps, not bound bytes. The Runtime preparation owner checks the original packet/header/row correspondence against its frozen draws/work bytes, without deriving new membership or skinning results. Graphics receives only the immutable Graphics bindings, ranges, counts and access metadata; it acquires no dependency on Runtime draw types, World data or callbacks into the higher owner.
5. On the graphics owner, preserve live-pipeline/epoch/pass/layout checks and verify the actual fixed four-resource `ReadWrite` reflection. Each subsequent pipeline command must be the same admitted pipeline/layout. Keep all existing pipeline use acquisitions and the exact `SetPipeline` → `SetBindings` → positive `Dispatch(1,1,1)` sequence.

The private recipe/witness is not a new public schema or identifier. If the shape, exact sequence, version, range or pair disagrees, abort the **whole unsubmitted recording** through the existing validation/failure channel. A budget failure retains its existing allocation-budget channel. Do not silently enter the quadratic path, narrow access, or switch trackers after recording a prefix. General callbacks continue to use their existing recorder; this correction makes no promise about their complexity.

### 5.2 Pending representation

| Storage | Exact capacity and meaning |
|---|---|
| Immutable resource-entry table | `R = M+3` entries, with the geometry portion sorted by actual resource identity; fixed role offsets are private array positions. |
| Singleton pending uses | `M+2` `StorageResourceUse` values plus `M+2` initialized presence flags, for geometry, palette and result. Each slot contains the last unordered write for that resource. |
| Packet pending uses | Exactly `D` `StorageResourceUse` values, appended in dispatch order; maintain initialized `workCount` and checked `previousPacketEnd`. Every entry retains its **actual bound packet range** and version. |
| Current uses and barrier candidates | The existing four-use bounded scratch and at most three candidate slots. No list of all writes is searched or compacted. |

Do not allocate the old `D+M+2` pending-list backing as well in this private branch. If a prototype retains it for comparison, both representations must be charged and it cannot establish the production memory bound.

### 5.3 Per-dispatch operations

For row `j`, after validating its exact immutable binding set and sequence:

1. While validating `SetBindings`, binary-search the `M` geometry entries once; verify the exact expected version/range and cached pair witness, and retain its private slot ordinal in current-use scratch for the following dispatch. Do not search again in hazard detection. Palette/work/result use their fixed entries. Check the four current uses against their witnesses and current reflection; packet containment remains an actual range check.
2. Test the occupied geometry/palette/result slots with the existing overlap semantics. For work, compare its actual start to `previousPacketEnd` and check its expected frozen range. The validated monotone sequence proves disjointness from **every** earlier exact packet without reading those entries.
3. Emit the same barriers, in canonical current-binding order: geometry if previously pending, then palette if pending, then result if pending. For each, use its retained pending version and that version's existing graph range, with the same source/destination flags. In this shape all four flags are true. Do not replace the barrier range with a packet or inferred smaller range.
4. Only after successfully recording the corresponding barrier, mark that singleton write ordered. After recording all required barriers, install the current geometry/palette/result writes and append the current exact packet write. Preserve the existing ordering of barriers before the dispatch command.
5. Update checked logical pending count/high water and the existing bounded command/use/write checks. After `j` dispatches require `workCount=j` and logical pending `j+m_j+2`. At `Complete`, require exactly `D` sets/dispatches/packet writes, `M` occupied geometry slots, palette/result occupied, `B` barriers, `N` commands, `D` uses, and logical final/high water `D+M+2`. Array capacity is not logical pending count.

Rebinding the pipeline does not reset any of these pending slots or packet entries. All packet writes remain represented through the end of this pass. Ending recorder-local tracking after completing the pass is the same existing pass boundary, not a new ordering event; later graph users still consume the original graph dependencies and native barriers.

### 5.4 Equivalence and work bound

Induct on dispatch count. After the first dispatch, both representations retain the same four writes. Assume correspondence after `j-1`: one write per seen geometry and current palette/result, plus the same `j-1` exact packets. The new packet cannot overlap a previous one. Only the current geometry, palette and result can hazard; each has at most one unordered entry. The tracker therefore emits the identical barrier/version/range/flag sequence, orders exactly those entries, and adds the identical four current writes. Unbound geometry entries and all packet entries remain. This establishes correspondence and the counts in section 3, including separated revisits.

For `h = ceil(log2(M+1))`, ordinary binary search needs at most `h` geometry-key comparisons per dispatch. Across the pass:

```text
geometry-key comparisons         <= D*h
singleton overlap tests          = B
packet-order tests               <= D
pending-use assignments          = 4D
singleton entries ordered        = B
storage-barrier command writes   = B
prior packet entries visited     = 0
pending-list elements shifted    = 0
```

At the logical row ceiling, `h <= 18`: at most **2,359,296** geometry comparisons, **524,288** pending-use assignments and **393,213** barriers, not tens of billions of packet comparisons. These bound hazard bookkeeping, not shader membership loops, all graph compilation, or wall-clock real-time performance.

The retained `D` pipeline acquisitions still allocate/use `D` owner leases and insert `D` entries into the existing owner's `SortedSet<long>` (`GraphicsDevice.PipelineRecording.cs:13–39`, `GraphicsResourceOwner.cs:146–178`). Their total tree work is `O(D log D)` and their nodes/leases remain charged. This proposal neither deduplicates those uses nor multiplies owner requests.

## 6. Do not move the quadratic loop into validation

The current recorder's `HasBufferAccess` performs up to `4D*A` access visits; the current native read/write-pair branch visits all graph accesses for each binding. For `M` growing with `D`, allocation reuse alone does not close either path.

Use the recipe's exact paired-access witnesses for **this diagnostic only**. Building them must group/index the access list once, not call the old linear matcher for each resource or draw. Each per-binding lookup still verifies the same pass, epoch, returned-version reference, storage kind/access, initialized matching pair and range containment. Immutable metadata permits reusing that established witness; counts or `ResourceId` equality alone do not.

At `Complete`, couple the successfully checked recipe with the produced immutable command array, then carry them through the internal `RecordedPassCommandList` → `GraphicsGraphSubmissionPass` handoff. The selected minimum prototype carries the recipe, including its pass-access metadata, rather than constructing a second per-draw plan. Native preflight must:

- Bind the witness to this pass/epoch and the **same immutable command array**, and verify the closed command/set sequence. A capacity tuple is not a certificate.
- Reconcile the witnessed pairs with actual submission accesses once: scan the graph accesses once to select this pass, sort/index the selected `A` records, and verify exact read/write versions, ranges, initialization and pairing. Do not reuse numeric access offsets from a different copied array.
- Use those reconciled entries for the four repeated storage-buffer lookups. Continue all existing pipeline, imported-range, binding, storage-barrier and native capability validation. Do not trust an unchecked Runtime label to bypass submission validation.

This adds only private diagnostic recording metadata; no public marker or new graph rejection ID is required. Other pass validators, state transitions, feedback rules, dependency compilation and aliasing semantics remain unchanged.

The diagnostic's relevant preparation/validation cost is then `O(A log A + D log(M+1) + B)`, plus a **single** `O(totalGraphAccesses)` selection at submission and the existing linear command traversal. Pipeline uses retain their separate `O(D log D)` work. No claim is made that every unrelated graph compiler/validator is linear. Failure to close or account the diagnostic's repeated pair searches leaves this gate NO-GO even if the pending-write loop is fixed.

## 7. Memory, admission and finite-work interpretation

Retain the prior proposal's conservative `Q(c,s) = 64 + roundUp16(checked(c*s))` array charge, increased if target evidence requires it. `s` is the actual compiled managed element stride, not a shader stride. Let `Sentry`, `Sstorage` and `Saccess` be qualified resource-entry, storage-use and access strides; `r` is managed reference width.

A conservative additional hazard/recipe bound, including setup overlap, is:

```text
Q(D,r)                  copied immutable binding-set references
+ Q(M+3,Sentry)         immutable resource/pair index
+ Q(A,4)                access-index construction workspace
+ Q(M+2,Sstorage)       singleton pending values
+ Q(M+2,1)              initialized presence flags
+ Q(D,Sstorage)         all exact packet pending writes
+ H                     qualified recipe/state/control and fixed-role storage
```

The existing pass-access arrays, including any backing newly retained by the recipe, still cost their actual `Q(capacity,Saccess)` in the full ledger. Native reconciliation adds a selected-access/index backing if it cannot share one; charge that storage and any simultaneous index copies at their actual capacities. Do not hide them in `H` or count a struct containing references as unmanaged bytes. Once compiled, replace symbolic strides and `H` with conservative target-profile charges before admitting any recipe.

This is a finite `O(D+M+A)` bound with no growth in the recording loop, no per-packet tree nodes, and no new per-draw owner/TCS/request. It replaces only the former pending-list representation. The full joint envelope still includes:

- Frozen draw/work/result arrays and preparation scratch at the phases they coexist.
- The parent's `D` binding sets, `D+M+2` reused/packet binding objects, retained four-reference arrays, and construction/cache backing at its actual mesh-array capacity.
- Exactly `N` mutable commands **and** the `N`-element immutable completion copy, mutable/frozen lease arrays, `D` lease objects and active-use tree nodes.
- Reflected-binding scratch, access copies, callback/request/task controls, submission/preflight metadata, and the new recipe/index overlap.

Do not release a charge while its plan, request, recorded list, submission or quarantine still roots that storage. `Dispose` releasing leases does not clear command references. The existing independent completion envelope cannot lend space to recording. Native command/descriptor/staging/retirement reservations remain the existing Graphics owners' responsibility; this CPU correction provides no GPU credit.

Claim the full phase-maximum byte charge before queueing this diagnostic recording request and recheck before graph acceptance. On excess, checked overflow, or an unestablished charge, refuse the whole diagnostic-bearing graph; never shrink membership or queue a prefix. Pre-sizing counts is not this byte reservation.

`D = 131072` is an algebraic work ceiling, not an admission promise. Even with `M=D`, the unchanged schedule has `N=655358`; the eight direct references and three direct `long` fields of `RecordedGraphicsCommand` imply at least 88 bytes per command on a 64-bit target. Its **final payload alone** is at least **57,671,504 bytes**, above 32 MiB before a builder, binding or hazard array. That request must be refused under the existing representation/envelope. No replacement hard draw limit can be inferred without the complete qualified ledger.

“Finite acceptable scaling” here means the deterministic section 5 operation bound and unchanged byte admission, not merely termination below a huge count. Practical owner-thread latency still requires measurement on the claimed profile. These contracts supply no universal milliseconds-per-diagnostic promise; this proposal invents none.

## 8. Minimum prototype and go/no-go

After separate implementation authorization, extend the existing owning Graphics/Runtime harness; do not add a runner, public test API, second skinning implementation or shader variant. The temporary allocation-printing file is not that prototype yet.

| Required observation | GO condition for the reviewed implementation |
|---|---|
| Exact schedules | Record the six literal schedules in section 3 with distinct exact packet ranges and conservative four-Frame reflection. Inspect the complete command sequence, source/destination versions/ranges/flags, `D` pipeline uses, exact write retention and table occupancy. Counts match the independently derived table; `A,B,A` retains and orders A. Native preflight accepts the same admitted recorded sequence. |
| Structural work | Count geometry comparisons, singleton overlap tests, packet checks, access-index visits, assignments, barrier writes and shifted elements on the actual recorder. Use increasing admitted `D` with one geometry, all different geometries and two long cycles. Meet section 5's deterministic bounds, zero prior-packet visits and zero shifts; no per-draw pass-access scan remains in recorder or diagnostic native preflight. Sorting/index construction is included, not timed off-book. |
| Complete byte bound | Obtain target-runtime/architecture strides, real object/backing capacities, cold initialization, allocation traffic and phase coexistence using the normal compiled path. Include the recipe, every index, both command payloads, active-use nodes and handoff roots. Peak fits 33,554,432 bytes; completion separately fits 16,777,216 bytes. No uncharged growth or unexplained allocation is acceptable. |
| Boundary/refusal | Exercise last-admitted and first-refused recipes and the logical row-ceiling request. Over-budget requests refuse **before graph acceptance**, without allocating the row-ceiling command payload merely to discover the known excess. All frozen draws remain required for an admitted graph. Do not bypass the envelope to obtain a large synthetic “pass.” |
| Closed-shape failures | Reused/overlapping or backwards packet range, wrong packet/row set, aliased fixed roles, wrong returned version, missing/uninitialized pair, changed pipeline/reflection, wrong sequence/count or epoch must abort the whole recording, unwind safe uses and expose no partial command list/results. A stale/nonmatching native witness cannot bypass the ordinary checks. |
| Actual recording cost | Report cold/warm owner-thread allocation and elapsed recording/preflight time, separately, for largest byte-admitted representatives of all three schedule families. Counters establish the asymptotic bound; timings establish practical cost only for that named machine/runtime/profile. A profile whose applicable latency requirements are not met is NO-GO, not grounds to omit results or raise the envelope. |
| Ownership | Keep the one recording request and actual resource/use ownership. Safe refusal releases acquired uses; accepted/uncertain submissions retain required state/charges through their existing fence/quarantine boundary. Inspect retained completed Tasks/receipts separately from caller-retained plans, as required by the preceding budget proposal. |

Use ordinary owning Debug checks, then Release where applicable, with nonzero selected/executed cases and retained ignored logs. The parent-reported analyzer/style build failure must also be resolved and revalidated by implementation work. **This author runs no such commands for this document.** Acceptance applies to the later frozen implementation/profile, not these proposed data structures by name.

**GO** requires command/ordering equivalence, deterministic work bounds, complete pre-admitted bytes and correct refusal/ownership for the exact reviewed implementation. **NO-GO** includes an unchanged pending scan, `D×A` diagnostic validation, reverse-removal compaction, an unchecked shape/witness, unknown footprint, or required profile evidence missing. Current source establishes the blocker and the parent's allocation-oriented progress, not this gate's closure.

Actual backend execution/readback, real animated nonzero production, public-only consumers and NativeAOT observations remain separate [Chapter 35 capability/observation gates](../design/35-implementation-roadmap.md#m04m05-capability-and-observation-sequencing). This proposal does not accept or qualify the concurrently edited M04 readback seam.

## 9. Frozen inputs and concurrent workspace

### Snapshot identity and timing

The operative inventory was re-frozen **after both parent input-state updates**, at `2026-11-16T04:32:00.0617412Z`. The 47 direct/context files below were then whole-file frozen at `2026-11-16T04:35:15.9907221Z`. No hash captured before the reported writes is used as the current review baseline.

- HEAD: `31f156a51ccc462ae9c5cc8d4e87e00c0054dec4`.
- Branch: `tannergooding-m05-implementation-plan`.
- HEAD tree and index tree: `c0165a07c7568f1c3ce916c3f7f7f96863784a97`; no staged changes.
- Index file: **147501 bytes**, SHA-256 `B37B58603A3DA21437FA7FE20D0AEAEFA6227980D62EBB4E4926853976F63931`.
- Baseline dirty inventory: **77 files = 53 tracked unstaged modifications + 24 untracked files**.
- Canonical status SHA-256: `BD02279512B114445D5EB7A98B11CFE45A28A1CDB7B6413FA9750BECF0BABA6B`.
- Canonical dirty-byte inventory SHA-256: `72799545D54CE8AF10C89FB633CEC2C66C4561EDFE7FCED01D0B2769371A285F`.

Status canonicalization ordinal-sorts complete original `git status --porcelain=v2 --untracked-files=all` lines, joins with LF and one final LF, and encodes UTF-8 without BOM. The dirty-byte inventory ordinal-sorts repository-relative Windows paths; each line is `State<TAB>Path<TAB>DecimalByteLength<TAB>UppercaseWholeFileSha256`, using the same LF/UTF-8 convention. `M` is unstaged modified, `U` untracked, `C` tracked clean. Both final comparisons exclude **only this new proposal**. The dirty digest byte-freezes surrounding work without treating it as contract authority.

**Concurrent M04 boundary:** `GraphicsDevice.cs`, `IGraphicsBackendDevice.cs`, `IGraphicsBackendBufferReadback.cs`, `D3D12GraphicsBackend.cs`, the three listed Vulkan implementation files and the transfer/backend tests contain active parent readback work. Their dirty bytes are included in the inventory digest; this proposal does not review or certify their completion behavior. The Runtime graph/export/submission changes also overlap diagnostic/readback integration. Only the narrow diagnostic recording trace described above is assessed.

**Untracked implementation:** the diagnostic work/pass/pipeline/result types, shaders and proposals are not committed baseline capabilities. `CommandRecorderRecordingCapacity.cs` is new parent implementation. `RenderSkinnedNormalDiagnosticPrototypeTests.cs` is explicitly temporary, untracked allocation instrumentation; it is retained untouched and unexecuted.

### Whole-file direct/context manifest

SHA-256 covers whole on-disk bytes, not normalized text, excerpts or HEAD blobs. Build/convention inputs are context only. Dirty source/proposals are implementation/proposal evidence, not accepted maintained contracts.

| Path | State | Bytes | SHA-256 |
|---|:---:|---:|---|
| `.editorconfig` | C | 26884 | `342683DE2B1C262624546F312F7D8E2078F89185ADD39F43F1FB5013376FD553` |
| `.github\workflows\ci.yml` | C | 5643 | `03EFEA672CD52E9EF053E9CE0285E7CA02C6CE6FB0D21268ADE4255ED5A39D58` |
| `Directory.Build.props` | C | 4236 | `770EE4B2A422861A6F23F0D39DD0ECA361A81CB87E64EBAC4E12170F71231D7A` |
| `Directory.Build.targets` | C | 1877 | `AEEC1D08E8037470239035812351C4045124607D67A354D482112F4D642D4105` |
| `docs\design\02-dotnet-api-memory-and-ownership.md` | C | 37545 | `40F913A50434209E09941C22625A6D0722202BAB97EBAC0E153C1EA3368E2291` |
| `docs\design\04-capacity-and-workload-profiles.md` | M | 27808 | `A4D86F3FA373A64548435C53582BF5916E29D77F34650F9D161CCEC90E6F0245` |
| `docs\design\17-graphics-device-and-resources.md` | C | 40823 | `8157E7DA5472B9AABD891A05E84F2B997487A5C5E3FF67CB589681747D628718` |
| `docs\design\18-shaders-and-material-pipeline.md` | C | 18074 | `AF20911D9D06BFB58F6C771511181F59347C4659129CFD5FC8CF622ED191C0D7` |
| `docs\design\19-render-graph-synchronization-and-presentation.md` | C | 26572 | `AB673DEDBD85B7A161C5D7DC871BF5B486911DC27EC4279AC43827B005241B8B` |
| `docs\design\30-public-api-review-surface.md` | M | 485241 | `15848C82F3858A77A87A0D3EA6FBCC07046EB6E77CA5B21748C72B90EFAB598B` |
| `docs\design\35-implementation-roadmap.md` | C | 30557 | `24EEC6E92B7AD904D552F290EF748FFC8FCE13B6B94A89EBD9F39487D4584D87` |
| `docs\project\Building.md` | C | 4652 | `F53AB58B89EEAE7E17B70970075BE9322A936D443BB6D5239C3238E594DEABC6` |
| `docs\project\Roadmap.md` | C | 7228 | `C4FBE82155A6D82FFD2D13AD2F18056D938738C4FA5A66760C4877E24ED001C7` |
| `docs\proposals\m05-skinned-normal-diagnostic-delivery-frame-bindings-addendum.md` | U | 19999 | `26F0A14AAB3453AA106ACBD5543BC434E4787CF2AFA51E80A542BCE9BF6C33B4` |
| `docs\proposals\m05-skinned-normal-diagnostic-delivery.md` | U | 62032 | `1D9ED2736D1C2520712D1E85A75AC5F0CF81D58C1EA48158F284B24B07C0DA3D` |
| `docs\proposals\m05-skinned-normal-diagnostic-recording-budget-closure.md` | U | 45112 | `5787039DC3BA43A862529CD9F01D82623DDFEC1B273194CC73FC50F079E0C753` |
| `global.json` | C | 179 | `1BCE01871321C2DF82E887DA428CE5C121A6C6541FE65017FE3D249BE47E190C` |
| `sources\Directory.Build.props` | C | 1062 | `D44373AF315523F8AF9F09F36BDED2A688E2B5C1572FCC0732310C7EF590EAD8` |
| `sources\TerraFX.Graphics\BindingSet.cs` | C | 5492 | `7389057053DBACE10ADC2B491AFF8B4FD7CED34D6797F206578B26F56252606C` |
| `sources\TerraFX.Graphics\BufferBinding.cs` | C | 2888 | `98BCE317C28F2A14E0065FBF538899319AA2CAB755D884F922CCADBCF26B8513` |
| `sources\TerraFX.Graphics\CommandRecorder.cs` | C | 4293 | `BEFAC55EA372433CF445CD7AF0D750D34843A3A6ED594D5CB187FDD49601E511` |
| `sources\TerraFX.Graphics\CommandRecorderRecordingCapacity.cs` | U | 1123 | `E8BC9374EE5055551B22E501AE9BA25BC728CE102273C38B6B4D80B28B663D3B` |
| `sources\TerraFX.Graphics\CommandRecorderState.cs` | M | 38855 | `AF2322564CB1355F95BE82213D11798F5122582AC0E3B2047762F9246BEDDF54` |
| `sources\TerraFX.Graphics\GraphBufferRange.cs` | C | 2586 | `1EA927C4F3CB859736F70BA1E2D521154188E318C17FC69B64586AD6B992A729` |
| `sources\TerraFX.Graphics\GraphBufferVersion.cs` | C | 1712 | `FEE9EE352B9C46186FA19BE2F28AFAB9EBC00D441BC23BE596569E5090BAE6BE` |
| `sources\TerraFX.Graphics\GraphResourceAccess.cs` | C | 13437 | `2B7E9BDD2F8E4CCB78645A1F0D3C4B0A37234BC4FDE96D5ED0B9810497D0168E` |
| `sources\TerraFX.Graphics\GraphResourceRegistry.cs` | C | 29126 | `D4803011465D57DD45DE6C30F3E67F19D377BC71FA9D0F7DA8D2088B9068B830` |
| `sources\TerraFX.Graphics\GraphicsDevice.PipelineRecording.cs` | C | 1962 | `D8D90F02FEF58E08FB55E163286DE9D31DC6A28F40CD8657E34C43C4C1EF7C72` |
| `sources\TerraFX.Graphics\GraphicsDevice.RecordPass.cs` | M | 4464 | `5CE023B1D6A9B500E33E2099E312C759CB98059CFFC1070F1E0A3DAFEF5D34CC` |
| `sources\TerraFX.Graphics\GraphicsDevice.SubmitGraph.cs` | C | 18611 | `A85B4857B87F7487E213E23F8C431E9F8D0D16DFE3394E2E97C22CDEB37C6D93` |
| `sources\TerraFX.Graphics\GraphicsGraphSubmission.cs` | C | 131205 | `E691A06893DCA483C262B1B707519F5176161E33D877C0006F96022F7C7CD378` |
| `sources\TerraFX.Graphics\GraphicsGraphSubmissionPass.cs` | C | 484 | `DC9ECEED7C712C467C79B88481C66AFBEDCD82DD55A1BAC6E71E575E93213475` |
| `sources\TerraFX.Graphics\GraphicsResourceOwner.cs` | C | 17503 | `C28BBB51E3FD3140472017DE70F8718B60296335AA8959EEEC54420073BF9846` |
| `sources\TerraFX.Graphics\RecordedGraphicsCommand.cs` | C | 7348 | `346862C8D8FFFCE1A264E7EA970B0B1404CABAB75753CA1C3CA12B140A082A9A` |
| `sources\TerraFX.Graphics\RecordedPassCommandList.cs` | C | 1807 | `6D024FAF75DE8967E61500CF11EB1F2B0424082F63FF87625EFA20D911D21961` |
| `sources\TerraFX.Graphics\StorageResourceUse.cs` | C | 2705 | `2D682BEE85C0088B40D075BE72DA23E92737DC8D98EC68B06537ED0258A0A18D` |
| `sources\TerraFX.Graphics\TerraFX.Graphics.csproj` | C | 817 | `E04F4E614E10B07D60A9E56B1C60E69D85B63240339587271235D079E197CCD4` |
| `sources\TerraFX.Runtime\Rendering\RenderGraph.cs` | M | 45818 | `FC12EF6CF50752FE4C5AE4CDD504A44A68690A58DC8D006A0A708B5B57113586` |
| `sources\TerraFX.Runtime\Rendering\RenderGraphPlan.Recording.cs` | M | 10202 | `00F835F89D24C4119C14551902DA7824E4503F4CF8A600E0BB8598AFA060F939` |
| `sources\TerraFX.Runtime\Rendering\RenderGraphPlan.Submission.cs` | C | 9286 | `8C6FD0D587B42E487141D43F60F54504A19E6F5F6F1C86394CA5C81D73CA3A02` |
| `sources\TerraFX.Runtime\Rendering\RenderMeshC0NormalDiagnosticGraphPass.cs` | U | 9272 | `7B824FE1CE4F9349C5ABF3AC48F70403B09C772F374E5F15F24062C0FF3E18FF` |
| `sources\TerraFX.Runtime\Rendering\RenderMeshC0NormalDiagnosticWork.cs` | U | 12049 | `0E0215007DB31524A91EBA94871B17355559E2009C2122472C9B01B008616E3C` |
| `sources\TerraFX.Runtime\Rendering\RenderMeshC0ShaderReflection.cs` | M | 20230 | `373512B6A20BEE7BBC8BA7A15F8CFE207D33EE0BF96932F1838C6B78749C5402` |
| `sources\TerraFX.Runtime\Rendering\Renderer.MeshC0Graph.cs` | M | 73871 | `79A1FBD0F69B4881AD6FB4359E5A1CEFF600EF654F57D023AC210A717F8BB086` |
| `sources\TerraFX.Runtime\Rendering\Renderer.Submit.cs` | M | 43663 | `D11FC04A346ECC06013D29DA37A6E4B44C12163D5777E9F2963F3AE8AFF8FB26` |
| `sources\TerraFX.Runtime\Shaders\SkinnedNormalDiagnostic.hlsl` | U | 3407 | `D9B7BA4AC63BA1C951C862226C8BA975EB180ADC6E302FA5816C31E0A0AB084E` |
| `tests\TerraFX.Runtime.Tests\Rendering\RenderSkinnedNormalDiagnosticPrototypeTests.cs` | U | 7117 | `70712590F730B08BBDDFF0ACC0582383171B0A8C41EA9ACA8254FBC5386B2692` |

### Complete baseline dirty-path inventory

The following inventory excludes this new output. All 77 files contribute their whole-file length/hash to the dirty-byte digest above.

```text
M docs\design\04-capacity-and-workload-profiles.md
M docs\design\12-entities-components-and-queries.md
M docs\design\16-scenes-transforms-and-cameras.md
M docs\design\30-public-api-review-surface.md
U docs\proposals\m03b-scene-preparation-budget-accounting.md
U docs\proposals\m03b-scene-preparation-footprint-closure.md
U docs\proposals\m03b-scene-preparation-handoff-closure.md
U docs\proposals\m03b-scene-preparation-workspace-closure.md
U docs\proposals\m05-owner-contract-closure-rectification-addendum.md
U docs\proposals\m05-owner-contract-closure-rectification.md
U docs\proposals\m05-owner-contract-closure-runtime-ports-addendum.md
U docs\proposals\m05-owner-contract-closure-runtime-ports.md
U docs\proposals\m05-owner-contract-closure.md
U docs\proposals\m05-skinned-normal-diagnostic-delivery-frame-bindings-addendum.md
U docs\proposals\m05-skinned-normal-diagnostic-delivery.md
U docs\proposals\m05-skinned-normal-diagnostic-recording-budget-closure.md
M sources\TerraFX.Graphics.D3D12\D3D12GraphicsBackend.cs
M sources\TerraFX.Graphics.Vulkan\VulkanBackendDevice.BackendPort.cs
M sources\TerraFX.Graphics.Vulkan\VulkanBackendDevice.Submission.cs
M sources\TerraFX.Graphics.Vulkan\VulkanBackendDevice.cs
U sources\TerraFX.Graphics\CommandRecorderRecordingCapacity.cs
M sources\TerraFX.Graphics\CommandRecorderState.cs
M sources\TerraFX.Graphics\GraphicsDevice.RecordPass.cs
M sources\TerraFX.Graphics\GraphicsDevice.cs
U sources\TerraFX.Graphics\IGraphicsBackendBufferReadback.cs
M sources\TerraFX.Graphics\IGraphicsBackendDevice.cs
M sources\TerraFX.Runtime\Animation\AnimationClipAssetBinding.cs
M sources\TerraFX.Runtime\Entities\World.cs
M sources\TerraFX.Runtime\Entities\WorldAdmissionDomain.cs
M sources\TerraFX.Runtime\Entities\WorldDomainReservation.cs
M sources\TerraFX.Runtime\Entities\WorldGuidRegistry.cs
M sources\TerraFX.Runtime\Entities\WorldGuidRetirementPermit.cs
U sources\TerraFX.Runtime\Entities\WorldGuidUse.cs
U sources\TerraFX.Runtime\Entities\WorldGuidUseControl.cs
M sources\TerraFX.Runtime\Entities\WorldLifetimeControl.cs
M sources\TerraFX.Runtime\Entities\WorldSceneStorageReservation.cs
U sources\TerraFX.Runtime\Rendering\GraphBufferReadbackExport.cs
M sources\TerraFX.Runtime\Rendering\RenderBuiltInSubmissionResources.cs
M sources\TerraFX.Runtime\Rendering\RenderGraph.cs
M sources\TerraFX.Runtime\Rendering\RenderGraphPlan.Recording.cs
M sources\TerraFX.Runtime\Rendering\RenderGraphPlan.cs
U sources\TerraFX.Runtime\Rendering\RenderMeshC0NormalDiagnosticGraphPass.cs
U sources\TerraFX.Runtime\Rendering\RenderMeshC0NormalDiagnosticWork.cs
M sources\TerraFX.Runtime\Rendering\RenderMeshC0ShaderReflection.cs
M sources\TerraFX.Runtime\Rendering\RenderSceneAssetBundle.cs
U sources\TerraFX.Runtime\Rendering\RenderSkinnedNormalDiagnostic.cs
M sources\TerraFX.Runtime\Rendering\RenderSpriteGraphState.cs
M sources\TerraFX.Runtime\Rendering\RenderSubmissionReceipt.cs
M sources\TerraFX.Runtime\Rendering\Renderer.CpuExtractionSupport.cs
M sources\TerraFX.Runtime\Rendering\Renderer.Extract.cs
M sources\TerraFX.Runtime\Rendering\Renderer.MeshC0Graph.cs
U sources\TerraFX.Runtime\Rendering\Renderer.SkinnedNormalDiagnosticPipeline.cs
M sources\TerraFX.Runtime\Rendering\Renderer.Submit.cs
M sources\TerraFX.Runtime\Scenes\ISceneExpansionResourcePreparer.cs
M sources\TerraFX.Runtime\Scenes\PreparedScene.cs
M sources\TerraFX.Runtime\Scenes\SceneActivationRequest.cs
M sources\TerraFX.Runtime\Scenes\SceneExpansionPlan.cs
M sources\TerraFX.Runtime\Scenes\SceneLoadOptions.cs
M sources\TerraFX.Runtime\Scenes\SceneLoadOptionsSnapshot.cs
M sources\TerraFX.Runtime\Scenes\SceneLoader.cs
M sources\TerraFX.Runtime\Scenes\ScenePrefabExpansionPlanner.cs
U sources\TerraFX.Runtime\Shaders\MeshC0Skinning.hlsl
M sources\TerraFX.Runtime\Shaders\MeshC0Vertex.hlsl
U sources\TerraFX.Runtime\Shaders\SkinnedNormalDiagnostic.hlsl
M sources\TerraFX.Runtime\TerraFX.Runtime.csproj
M tests\TerraFX.Graphics.Tests\GraphicsBufferTransferTests.cs
M tests\TerraFX.Graphics.Tests\GraphicsPipelineRequestTests.cs
M tests\TerraFX.Graphics.Vulkan.Tests\VulkanBackendDeviceTests.cs
M tests\TerraFX.Runtime.Tests\Entities\WorldAdmissionDomainTests.cs
M tests\TerraFX.Runtime.Tests\Entities\WorldLifetimeControlQuarantineTests.cs
U tests\TerraFX.Runtime.Tests\Rendering\RenderSkinnedNormalDiagnosticPrototypeTests.cs
M tests\TerraFX.Runtime.Tests\Scenes\PreparedSceneAssetRetentionTests.cs
M tests\TerraFX.Runtime.Tests\Scenes\RenderSpriteCapacityTests.cs
M tests\TerraFX.Runtime.Tests\Scenes\RenderSpriteComponentTests.cs
M tests\TerraFX.Runtime.Tests\Scenes\RendererExtractionTests.cs
M tests\TerraFX.Runtime.Tests\Scenes\SceneLoadOptionsTests.cs
M tests\TerraFX.Runtime.Tests\Scenes\SceneSupportRegistrationTests.cs
```

### Verification disposition

At `2026-11-16T04:40:22.7102967Z`, all **47** direct/context file hashes, lengths and states matched; all **77** baseline dirty files reproduced the frozen dirty-byte digest; canonical status, HEAD, index tree, index byte length and index SHA-256 also matched. Zero mismatches; only this new proposal was added. The parent-reported failed build introduced no observed frozen-input changes. This preservation check is not execution or acceptance evidence.
