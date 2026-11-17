# M05 skinned-normal diagnostic recording-budget closure

## 1. Decision and scope

**Tentative unblocker for review, not an accepted contract, implementation, or qualification result.** Apply this focused correction to [diagnostic delivery](m05-skinned-normal-diagnostic-delivery.md) section 6 and its [Frame-binding addendum](m05-skinned-normal-diagnostic-delivery-frame-bindings-addendum.md). The frozen versions of both are identified in section 8.

Use the existing **32-MiB preparation ceiling as one joint diagnostic preparation/recording envelope**: retained diagnostic plan storage plus its additional binding objects, recording storage, validation workspace, handoff metadata, and simultaneous copies must fit that same ceiling. This explicitly tightens the original table's incomplete metadata/membership/scratch accounting; it does not add a second 32-MiB allowance. Keep the independent **16-MiB CPU completion ceiling** for raw readback, typed results, decode workspace, and their overhead. Neither envelope lends unused credit to the other.

Preclaim a finite recording recipe from the frozen draw schedule before queuing diagnostic recording; materialize and validate all managed command storage before graph-submission acceptance. Refuse the **whole diagnostic-bearing graph** if the complete reservation cannot fit. The 131,072 logical-draw ceiling is not a promise that every such graph fits 32 MiB.

Preserve four complete Frame storage bindings per dispatch, the exact `u2` packet subrange, parameter/header semantics, paired graph accesses, storage ordering, numerical behavior, result membership, submission/readback completion, cancellation, and quarantine. Do not bind the whole work owner, change shader indexing, omit draws, or replace a missing result with an empty array.

This proposes only private Runtime/Graphics accounting and admission work. It adds no public API, acceptance ID, shader ABI, general renderer budget, or M03-B allowance. Only this proposal is written; source, tests, builds, shaders, commits, and remote publication are excluded.

**Concurrent-work boundary:** `GraphicsDevice.cs` and every backend readback file are outside the frozen direct-input set, as requested. The M04 readback task is actively editing that seam. Its bytes and completion behavior are not reviewed here. The narrow `GraphicsDevice.RecordPass.cs`, `.PipelineRecording.cs`, and `.SubmitGraph.cs` partials provide the recording and graph-acceptance evidence needed below without reading that seam.

## 2. Source-established facts

Basenames identify the full paths in section 8. These are source observations, not executed tests.

| Owner | Observed behavior and budget consequence |
|---|---|
| `RenderMeshC0NormalDiagnosticWork.cs:115–150,189–204,267–278` | Checks five array charges against 32 MiB: draw records, maximum index-sort scratch, mesh-use flags, allocated work bytes, and initialized result bytes. It reserves no `BindingSet`, recording list, validation collection, or pipeline-use storage. The work allocation uses the index-count/alignment upper bound, not only the ultimately occupied unique-membership bytes. |
| `Renderer.MeshC0Graph.cs:1056–1062,1124–1190` | Retains the draw table and graph versions, creates the work/result uploads, and adds one diagnostic compute pass. Exact packet ranges are subsequently selected by the recorder. |
| `RenderGraph.cs:699–718,799–827`; `RenderSpriteGraphBufferUpload.cs`; `RenderSpriteGraphState.cs` | Initialized upload records retain the original `byte[]`; they do not deep-copy its payload. The graph and compiled state can share those bytes. Charge the allocation once, but retain its charge while either owner needs it. |
| `RenderMeshC0NormalDiagnosticGraphPass.cs:78–125` | Currently sets the pipeline once, then constructs **four new `BufferBinding` objects and one `BindingSet` per draw**, followed by `SetBindings` and `Dispatch(1,1,1)`. The work range changes for each draw. |
| `BufferBinding.cs`; `BindingSet.cs:31–148` | Bindings are immutable reference objects. The set clones the input buffer array for sorting and constructs its retained immutable array. Budget the input, sort, and retained four-reference backings during construction unless compiled evidence proves a backing absent/shared. This is copying references, not copying the four bindings or GPU buffers again. Empty texture/sampler inputs are not four-element arrays. |
| `ShaderResourceSignature.cs:8` | The signature constructed for validation by `BufferBinding` is a value type, not another independently retained signature object per binding. |
| `CommandRecorderState.cs:419–530` | Every `SetBindings` creates an expected-binding dictionary, actual-binding hash set, storage-use builder, and zero-sampler lease-list object. It freezes four `StorageResourceUse` values and appends a command retaining the `BindingSet`. Those temporary validation allocations are not covered by the binding-set array alone. |
| `CommandRecorderState.cs:682–786`; `StorageResourceUse.cs` | Hazard detection uses the **bound range**, not merely resource identity. Disjoint work packets remain in `_pendingStorageWrites`; recurring palette/result accesses and recurring geometry produce storage-barrier commands. Pending storage is not bounded by the four currently bound resources. |
| `CommandRecorderState.cs:174–185,548–584`; `GraphicsDevice.PipelineRecording.cs:13–39`; `GraphicsResourceOwner.cs:146–178,432–463` | `Complete` copies the mutable command and use-lease lists into immutable arrays. Each `SetPipeline` acquires a new use lease and inserts an active-use sequence into the pipeline owner's `SortedSet<long>`. It does not deduplicate repeated use of the same handle. |
| `GraphicsGraphSubmission.cs:1307–1308,1420–1431,1835–1863` | Native preflight accepts one complete binding set after a resource-bearing pipeline. `Dispatch` does **not** reset that flag; `SetPipeline` does. Consequently the current diagnostic callback's second `SetBindings` rejects for more than one draw. A source-compatible baseline must rebind the same pipeline before each draw's binding set and budget those extra commands/leases. This is an implementation restriction, not a new public contract. |
| `RecordedGraphicsCommand.cs`; `RecordedPassCommandList.cs` | Commands are a large reference-bearing value type, not one small object or opcode each. The immutable command array retains binding sets and pipeline owners. `Dispose` releases/clears the use-lease array, **not `Commands`**. Lease release is not proof that the command/binding CPU graph is no longer rooted. |
| `RenderGraphPlan.Recording.cs:38–40,83–114,197–233`; `GraphicsDevice.RecordPass.cs` | Recording creates pass-metadata builders, an owner request/TCS and callback capture, the recorder, and an outer command-list builder plus immutable result. Recording completes before `Renderer.SubmitAsync` constructs its graph submission. |
| `RenderGraphPlan.Submission.cs:81–149,210–228`; `GraphicsGraphSubmissionPass.cs`; `GraphicsGraphSubmission.cs:99–107` | A submission pass stores the same immutable command array as the recorded list. Copying outer pass/list arrays does **not** deep-copy each pass's command payload or each `BindingSet`. There is no third full command-array payload copy at this handoff. Outer arrays, dictionaries, and newly copied access/import metadata still cost storage. |
| `Renderer.Submit.cs:170–196,792–837,916–919`; `GraphicsDevice.SubmitGraph.cs:126–200,342–386,411–527` | The diagnostic lane is acquired before the first wait. Recording, submission construction, and native preflight precede `_ownerQueue.WriteRequestAsync` for the graph. The request retains the submission until completion; `ReleaseWork` releases leases before task success. Uncertain submission roots the submission in quarantine. Runtime keeps its lane through the later readback and cleanup. The lane is a count reservation, not a CPU byte reservation. |

Chapter 02 sections 3–5 require independent asynchronous ownership and measured allocation policy. Chapter 04 section 1 includes spare capacity, metadata, staging, and in-flight work in reservations. Chapters 17 sections 3–5 and 19 sections 1–3 preserve exact bindings, native-owner affinity, pre-admitted retirement, and real-fence protection. Chapter 18's explicit storage layout and Chapter 30's copying/recording/submission contracts remain unchanged. Chapter 35 separates this design correction, managed implementation, native execution, public-consumer observations, and milestone acceptance.

## 3. Exact schedule counts

For the inspected single-pass shape, let:

- `D` be eligible distinct logical draws after the original selection/deduplication, in frozen RFC DrawId order.
- `M` be distinct vertex-buffer resource identities used by those draws. For `D > 0`, `1 <= M <= D`.
- `G` be the mesh-array capacity used by preparation; `I` the maximum selected group index count; `W` the actual allocated work-byte length; and `A` the diagnostic pass's actual declared-access count.
- `Scommand`, `Sstorage`, and other element sizes be the compiled managed strides, **not shader record sizes**.

For `D = 0`, there is no diagnostic pass, binding table, recording reservation, or completion batch. The initialized-empty result retains its original meaning.

For positive `D`, all four bindings have conservative `ReadWrite` reflection. Work packets are disjoint; vertex owners are distinct from palette, work, and result owners:

```text
palette barriers       = D - 1
result barriers        = D - 1
vertex barriers        = D - M
work-packet barriers   = 0
B                       = 3D - M - 2

current callback:
  SetPipeline = 1; SetBindings = D; Dispatch = D
  Ncurrent    = 1 + 2D + B = 5D - M - 1

baseline executable with the current native validator:
  SetPipeline = D; SetBindings = D; Dispatch = D
  N           = 3D + B = 6D - M - 2
  pipeline use leases = D
```

After dispatch `j`, pending writes contain `j` disjoint work packets, one entry for each geometry seen so far, and the current palette/result entries. The exact final/high-water logical count is **`D + M + 2`**. A pipeline rebind clears `_boundStorageUses`, not `_pendingStorageWrites`, so it does not remove this cost or these barriers.

| D | M | Barrier commands | Current commands | Executable-baseline commands | Pending-write high water |
|---:|---:|---:|---:|---:|---:|
| 1 | 1 | 0 | 3 | 3 | 4 |
| 2 | 1 | 3 | 8 | 9 | 5 |
| 2 | 2 | 2 | 7 | 8 | 6 |
| 3 | 2 | 5 | 12 | 14 | 7 |

The two-draw/same-geometry case is a concrete distinguishing example: two exact `u2` packet views require two different immutable binding sets. Under the current native validator it needs two pipeline uses, nine commands, three barriers, and five pending-write entries—not one binding set, four pending entries, or just four dispatch-related commands. The second packet must still read its own header and membership, as in the Frame addendum's packing example.

At `D = 131072`, even the smallest executable command count, with `M = D`, is **655,358**. On a 64-bit managed target, eight direct reference fields and three direct `long` fields alone give `Scommand >= 88`, before its many other fields and padding. Thus the **final command payload alone is at least 57,671,504 bytes**, already above 33,554,432 bytes. With one shared geometry, `N = 786429` and that lower bound becomes 69,205,752 bytes. These are source-derived lower bounds, not measured CLR sizes; the builder, bindings, leases, pending writes, and frozen plan make the actual peak larger.

It is therefore incorrect to promise admission at the row ceiling. Do not invent a replacement draw-count limit from these lower bounds. The admitted count depends on the complete qualified byte charge and this graph's membership/alignment/capacities.

## 4. Storage ledger and coexistence

The existing preparation helper uses `ArrayCharge(c,s) = 64 + roundUp16(checked(c*s))`. Retain that conservative charge for separately allocated arrays and increase it wherever target-runtime evidence requires more. It is not a CLR header/layout guarantee or permission to apply a World-domain cell charge to unrelated Graphics objects. Separately allocated object/control/node charges need their own qualified bounds. `Unsafe.SizeOf<BufferBinding>()` would measure a reference, **not** the binding object's allocation.

| Storage | Logical count versus actual capacity | Lifetime/peak that must be charged |
|---|---|---|
| Existing diagnostic preparation | Draw array `D`; flags `G`; index scratch `I`; byte arrays `W` and `16D` | Include the existing preparation peak. Draw/work/result arrays survive into the graph/plan; flags and sorting scratch are preparation-only unless separately retained. Count shared upload payload once. Do not replace allocated `W` by the last occupied packet end. |
| Binding sets and bindings | Current path: `D` sets, `4D` binding objects, and `D` retained four-reference buffer arrays | All earlier sets remain reachable through their commands while later draws record. During one construction also allow its input/sort backing and sorting helpers. Empty-array sharing, collection-expression lowering, and first-use comparer/delegate allocations require the prototype below. |
| Per-binding validation | Dictionary/hash-set logical membership four; storage-use builder count four and immutable result length four; zero-sampler lease-list count zero | Charge their real buckets/entries, builder capacity, wrappers, copies, and any overlapping previous `_boundStorageUses` array. The source supplies no explicit capacities for these collections. Four entries is not a capacity measurement. |
| Pending storage writes | Count high water `D + M + 2`; current `List` capacity at least that value | Include actual spare capacity and old-plus-new backing at growth. The per-dispatch barrier-resource list has at most three identities here, but its list object, backing, and `RemoveAll` capture/delegate allocations still count. |
| Command recording and freezing | Executable-baseline mutable capacity `Ccommand >= N`; immutable final length `N`. The uncorrected callback instead records `Ncurrent`. | At `Complete`, mutable backing and immutable payload coexist. At earlier growth, old and replacement mutable backings coexist. The peak is the maximum of those stages, not `N * sizeof(opcode)`. An exact pre-sized implementation can make `Ccommand = N`; it still needs **two `N`-element payloads at freezing** unless an independently proved ownership-preserving implementation removes a copy. |
| Pipeline recording uses | Executable baseline: `D` use-lease objects, `D` active `SortedSet<long>` nodes, mutable lease capacity `Clease >= D`, and frozen lease length `D` | Both lease arrays coexist at `Complete`; active-use nodes persist until the actual use is released. The zero-sampler path creates no per-draw sampler leases. Buffer resource-use leases belong to distinct materialized graph owners, not four new buffer leases per dispatch. |
| Recording/handoff metadata | Actual access-builder capacity and immutable access copies; command-list builder and outer immutable list; pass arrays, import/access maps, callback/request/TCS/state objects | Charge diagnostic-attributable storage and any shared-container growth it causes. `A` comes from the actual admitted declaration set; do not hardcode a whole-owner access assumption to save bytes. Count copied outer containers, not duplicate descendant command/binding payloads. |
| Graph native preflight | Added pass/resource tracking plus a new reflected-resource hash set per pipeline command (`GraphicsGraphSubmission.cs:1159`) | It runs before graph queue acceptance but after recording; recorded arrays and frozen plan coexist with it. Its incremental maps, sets, capacities, and transient allocations are part of preparation/recording, not the 16-MiB result allowance. |
| Submitted/uncertain state | Frozen commands, binding graph, lease controls, and submission/request references | Keep their charges through actual use and ownership handoff. `RecordedPassCommandList.Dispose` alone does not remove command-array references; a quarantined submission explicitly retains them. |

At a phase with all sets constructed, the current binding storage alone includes:

```text
D * (BindingSetObjectCharge + ArrayCharge(4, referenceBytes))
  + 4D * BufferBindingObjectCharge
```

The executable baseline additionally retains `D` pipeline-use objects/nodes. A recording/freeze estimate must add `ArrayCharge(Ccommand,Scommand)`, the final `ArrayCharge(N,Scommand)` when allocated, pending-write capacity, mutable/frozen lease arrays, validation/handoff workspace, fixed controls, and any old replacement backing still live at that phase. Do not sum unrelated phase maxima as if they necessarily coexist, or subtract a collection merely because its logical `Count` became zero.

Use these two independent admission inequalities, with checked arithmetic throughout:

```text
max(
    diagnostic preparation peak,
    max over recording/submission phases(
        still-retained diagnostic plan charge
        + diagnostic recording/validation/handoff charge at that phase))
    <= 33,554,432 bytes

CPU raw readback + typed result + decode workspace/control peak
    <= 16,777,216 bytes
```

Preparation scratch may cease contributing only when its consumer/ownership has ended. Plan storage does not disappear merely because submission starts. If recording storage remains rooted during readback/decode, its 32-MiB charge and the independent completion charge coexist; this is not authority to relabel recording bytes as completion workspace. Conversely, completed scalar receipts do not retain a continuing recording reservation.

These are logical owned-storage/admission bounds, not a total-process working-set or no-GC promise. Record allocation traffic as well as reachable-storage peaks: repeated short-lived dictionaries/builders may generate substantial garbage. Do not treat an unmeasured compiler optimization, prompt collection, or a forced GC as a reservation mechanism.

GPU allocations, native command/descriptor backing, staging, and retirement retain the original Graphics budgets and preacceptance requirements. Four reflected descriptors is a **layout** count, not proof that all per-dispatch native descriptor/command storage costs only four descriptors once. No native page/heap byte size is inferred here, and no new GPU allowance is created. Any additional managed preparation of that backing belongs in the ledger above.

## 5. Proposed admission and ownership sequence

1. **Freeze the recipe with the plan.** Retain the original identities, exact packet ranges and graph versions, membership, `D`, `M`, and actual allocated capacities. Compute the executable schedule, including barriers and the current native validator's pipeline rebinds. If distinct geometry cannot yet be counted without allocation, use conservative `N <= 6D - 3` and pending count `<= 2D + 2` while reserving the counting workspace; do not allocate an uncharged deduplication set. The recipe is private data, not a live recorder, pipeline lease, or callback into World state.
2. **Close the joint 32-MiB estimate before publishing an admissible diagnostic plan.** Include preparation, retained storage, recording/freeze peaks, preflight, controls, and growth. Budget excess uses the existing contextual allocation-budget failure (`OutOfMemoryException`), with no partial graph or prefix result. Checked reservation overflow is non-admissible, never a wrapped smaller charge; preserve existing argument/identity/extent validation precedence and failure channels rather than converting every invalid input to budget exhaustion. Do not add a sixth graph-compile rejection code or change its established result protocol. A compiled logical plan is not native graph acceptance or proof of a later resource reservation.
3. **Claim the existing one-batch lane and byte headroom before asynchronous diagnostic work.** Preserve usage/identity/pre-cancellation checks. Before the first diagnostic wait/staging/recording request, claim the additional recording allowance within `32 MiB - still-retained plan charge` and preclaim the independently derived completion requirement within 16 MiB. The existing interlocked lane prevents concurrent diagnostic submissions for this Renderer/epoch; it does not preallocate any bytes. No accepted waiting queue or multiplying allowance by frame-slot/channel count is introduced.
4. **Materialize recording storage under that claim.** Carry the private counts/capacities through the existing internal recording request/state boundary; no public constructor or callback signature changes. A simple implementation pre-sizes commands to `N`, pipeline leases to `D`, and pending writes to `D + M + 2`, with explicitly bounded four-binding validation workspace. Reserve the immutable `Complete` copies as well. If the current allocating validation implementation is retained, charge its qualified actual capacity/temporary peaks; reusable scratch is an optimization, not a prerequisite to correctness. Allocation failure while recording still aborts before graph acceptance and unwinds acquired uses.
5. **Finish all preparation before accepting the graph.** The current order already provides the necessary boundary: `RecordPassCallbacksAsync` → `CreateGraphicsSubmission` → `SubmitGraphAsync`'s `ValidateNativeRasterSubmission` → graph `_ownerQueue.WriteRequestAsync`. Check the recorded count, actual capacities, full reservation, and existing native resource/descriptor/retirement availability before that final admission. Detecting an overrun only after submission is too late. Earlier accepted resource creations/uploads retain their own completion/cleanup rules; refusing the graph does not claim to undo those operations.
6. **Keep the charge with its actual owner.** Recording requests own their storage until completion/handoff, then the submission owns its command/binding storage. On an unsubmitted failure, release safe uses and discard the whole record. On submission uncertainty, transfer the recording charge with the already rooted quarantined submission; do not recycle headroom because a wait/task faulted. Preserve the existing device-loss admission closure. On success, end native uses at their actual fence and detach unnecessary CPU command references before declaring their reservation released. Do not infer this detachment from `Dispose` alone.
7. **Complete without retention surprises.** A retained completed operation Task as well as a retained receipt must not keep an otherwise finished diagnostic recording graph alive through an async-state/request reference. If compiled root inspection shows such retention, explicitly end those private references at the proper owner boundary before task completion. Keep plan-owned bytes charged for the caller-retained plan. Observation cancellation does not detach still-consuming storage, release the lane, or cancel accepted graph/readback work.

Steps 1–5 are implementable through existing internal friend boundaries. They do **not** authorize bypassing `BindingSet` validation/copying, mutating an already recorded set, using unmanaged storage for reference-bearing commands, changing owner-thread recording, or weakening exact packet/version validation.

## 6. Contract-compliant simplifications, separate from requirements

The confirmed requirements are one immutable complete binding set and exact work view per draw, actual storage ordering, finite preclaims, and original completion semantics. They do not require four **new** binding objects per draw.

**Supported simplification candidate:** once the exact diagnostic graph versions/ranges are frozen, reuse the immutable vertex binding per distinct selected vertex version, and reuse the immutable palette and result bindings. Allocate only the packet binding per draw. For the inspected shape this reduces binding-object count from `4D` to **`D + M + 2`**, while retaining `D` binding sets and `D` four-reference arrays. Charge the cache's real capacity/control storage too. Key reuse by exact graph-version/range/binding identity, not merely a mesh ID or a GPU owner that could name a different version. No mutable World, later palette sample, or shared mutable binding set is involved.

Preparing those sets before the owner callback can simplify preclaim enforcement; it relocates their construction, not their retained cost. Exact command-list pre-sizing removes growth peaks, not the final freeze copy or barrier commands.

Moving the current pipeline call **inside** the draw loop is the simplest sequence compatible with the inspected native validator; section 3 budgets that baseline. Hoisting it once could become an optimization only after a separately verified internal rebind path in native preflight and both native recorders. It is **not selected or assumed** by this proposal. Likewise, deduplicating repeated pipeline leases, changing `BindingSet` internals, or transferring command backing without copying requires its own implementation proof before reducing charges.

Do not absorb the cost by combining draws into one dispatch, binding the entire packet owner with different indexing, narrowing conservative reflection, clearing pending writes without their barriers, adding compute uniforms, or retaining only a result prefix.

## 7. Smallest remaining prototype and go/no-go gate

**Current state: NO-GO for claiming this recording-budget/admission gate closed.** Source establishes the missing storage and the native multi-draw sequence rejection, but not exact target-runtime object sizes, default collection capacities, collection-expression lowering, or async-root retention. No compile, instrumentation, build, test, shader tool, or native execution was performed for this proposal.

After separate implementation authorization, use **one focused recording/handoff prototype in the existing Graphics/Runtime test harness**, not a new framework, public budget API, alternative renderer, or second skinning implementation. The existing friend access permits inspecting actual recorded lists and submission passes. Keep per-run output under the existing ignored `artifacts` locations and use the SDK selected by `global.json`.

| Observation | Minimum experiment | Exact go/no-go condition |
|---|---|---|
| Managed layout/allocation closure | Compile the actual proposed path; obtain `Unsafe.SizeOf` for `RecordedGraphicsCommand`, `StorageResourceUse`, diagnostic draw/result values, and access/pass value types. Inspect emitted lowering and actual allocations for class instances, reference arrays, list/builder/dictionary/hash-set backing, `SortedSet<long>` nodes, callbacks, requests, Tasks/TCS, and cold comparer/delegate initialization. Use owner-thread allocation deltas plus a managed allocation/object graph trace; allocation totals alone are not reachable peaks. | Every independently allocated object/backing is assigned a conservative charge covering the selected runtime/architecture; no reference-size-as-object-size estimate or unexplained allocation remains. Measure cold and warm paths. Default BCL growth or copy elision is not assumed from source syntax. |
| Small distinguishing schedules | Record `(D,M) = (1,1), (2,1), (2,2), (3,2)`, with the last geometry sequence revisiting its first geometry. Use the actual four-storage reflection and exact distinct packet views. Inspect commands, binding identities/ranges, barriers, pending high water, and pipeline uses. Run native preflight on the same recorded two-draw shape. | Counts match section 3; second packet retains its own header view. The current one-pipeline callback must expose the preflight refusal; the selected corrected sequence must pass that check. Managed recorder success alone is not native admission evidence. |
| Capacity and coexistence | Observe actual capacities at each growth, last binding construction, `Complete`, outer-list freeze, submission construction, and native preflight. Include earlier recorded passes and still-retained plan/upload data. Confirm submission passes share the recorded command payload rather than charging a nonexistent deep copy. | A phase-by-phase ledger covers every live backing, old-plus-new growth, and overlapping copy before allocating. Exact pre-sizing, if selected, really admits `N`, `D`, and `D+M+2` without later growth. Any other capacity is charged at its actual value. |
| Byte admission/refusal | Choose real recipes immediately below/at/above the derived byte boundary, including packet/alignment-heavy input and the row-ceiling request. Exercise the independently computed completion charge too. | Joint preparation/recording peak is at most 33,554,432 bytes and completion peak at most 16,777,216 bytes. An excess or checked-arithmetic failure admits **no graph**, not fewer draws. Do not claim that the row ceiling fits; section 3 proves otherwise for this representation. |
| Ownership and refusal cleanup | Inject preparation/recording failure before queue acceptance, and use the existing controlled completion/failure harness for an admitted graph. Inspect use counts and command roots before/after safe cleanup; retain the completed Task/receipt and separately retain the plan. | No use/recording charge leaks on safe refusal; no charge ends while a submission/quarantine still owns it; a completed Task/receipt does not retain freed recording storage. A retained plan legitimately retains its own storage. Synthetic fence/fault control proves only this managed ownership behavior. |

Run the smallest owning Debug build/tests, then Release where applicable, with nonzero selected/executed cases. The evidence must identify the compiled revision, runtime, architecture and actual capacity/size values. A JIT profile does not establish NativeAOT layout/root behavior; qualify each claimed deployment profile separately. Instrumentation is limited to this implemented recording path and consumes ordinary developer outputs, not new milestone infrastructure.

**Go:** the qualified implementation uses the same exact packet views and complete membership, native preflight accepts its chosen multi-draw sequence, the complete ledger fits both unchanged ceilings before graph acceptance, and ownership/refusal checks pass for those exact bytes/profile. **No-go:** any unknown footprint/capacity/root, budget overrun, unreserved growth, preflight failure, or required capability/reservation not established. Retry after the narrow prototype or owner fix supplies the missing evidence, not by enlarging ceilings or using a smaller result prefix.

Actual backend descriptor/command reservation and real graph/readback fences remain the existing M04 execution gate; this proposal does not certify the concurrently edited readback seam. Actual diagnostic GPU counts, animated nonzero production, public-only consumers, NativeAOT, and complete AR08/M05 acceptance retain their existing Chapter 35 gates. The managed prototype must not manufacture those observations.

## 8. Frozen review inputs and workspace inventory

### Revision and scope

- HEAD: `31f156a51ccc462ae9c5cc8d4e87e00c0054dec4`.
- Branch: `tannergooding-m05-implementation-plan`.
- Index matched HEAD (`git diff --cached --quiet`, exit 0). The complete `git ls-files --stage` manifest had **1,773 entries / 189,960 UTF-8 bytes**, SHA-256 `581fa5651b5491223fa5017d49a94b6be2d5521135a085414855199620c7729c`. Canonicalization preserves Git output order, joins lines with LF plus one final LF, and uses UTF-8 without BOM; this is the logical index manifest, not a claim about index-file metadata.
- Initial inventory: **70 dirty paths: 49 tracked unstaged modifications and 21 untracked files; no staged changes.** Canonical `git status --porcelain=v2 --untracked-files=all` SHA-256: `8083da127891129b75e29541c4e462d6a543e5e4ed16697f4bf4109cf45143ab`. Canonicalization ordinal-sorts complete original Git lines, then uses UTF-8 without BOM, LF joins, and one final LF. Exclude only this new proposal in the post-draft comparison.
- Initial 25 direct/context inputs were frozen at `2026-11-16T03:51:53.7548855Z`; 16 narrow supporting inputs at `03:53:18.4424242Z`; five handoff/completion inputs at `03:55:04.5191181Z`. All **46** hashes and byte lengths were rechecked at `03:58:08.2906494Z`, with zero mismatches; HEAD/index remained unchanged.

SHA-256 below covers each **whole on-disk file's bytes**, not normalized text, selected excerpts, or HEAD blobs. `C` = tracked clean, `M` = tracked unstaged modified, `U` = untracked. Dirty source is evidence of the active implementation, not an accepted contract or executed result.

### Direct/context input manifest

| Path | State | Bytes | SHA-256 |
|---|:---:|---:|---|
| `docs\proposals\m05-skinned-normal-diagnostic-delivery.md` | U | 62032 | `1d9ed2736d1c2520712d1e85a75ac5f0cf81d58c1ea48158f284b24b07c0da3d` |
| `docs\proposals\m05-skinned-normal-diagnostic-delivery-frame-bindings-addendum.md` | U | 19999 | `26f0a14aab3453aa106acbd5543bc434e4787cf2afa51e80a542bce9bf6c33b4` |
| `sources\TerraFX.Runtime\Rendering\RenderMeshC0NormalDiagnosticWork.cs` | U | 11699 | `8771db209a7dbc2818d456d27cf83cb925a30faf390b12311fe09ecd44f1110f` |
| `sources\TerraFX.Runtime\Rendering\RenderMeshC0NormalDiagnosticGraphPass.cs` | U | 6587 | `ee0032ace88777e14f76de9dc3ec77d728f5e4264b6aa5b827ade8857602a446` |
| `sources\TerraFX.Runtime\Rendering\Renderer.MeshC0Graph.cs` | M | 73823 | `2c9b291d507740c9548edebd10e421cef777e8d3f10e29e693f033625b8d56d4` |
| `sources\TerraFX.Runtime\Rendering\Renderer.Submit.cs` | M | 43668 | `8899879ab4416653bc3e6c9d0841d88ab53b9a47a411db8ce0115873fc147321` |
| `sources\TerraFX.Runtime\Rendering\RenderGraphPlan.Recording.cs` | M | 9995 | `c6542336938f5ca231040899cc76620dc24c5aa96146cfd700b9b737ba53729c` |
| `sources\TerraFX.Graphics\BindingSet.cs` | C | 5492 | `7389057053dbace10adc2b491aff8b4fd7ced34d6797f206578b26f56252606c` |
| `sources\TerraFX.Graphics\BufferBinding.cs` | C | 2888 | `98bce317c28f2a14e0065fbf538899319aa2cab755d884f922ccadbcf26b8513` |
| `sources\TerraFX.Graphics\CommandRecorderState.cs` | C | 31917 | `54c985d58c9a047306e0ed41b015455301701f1dde4ec8a61b366fbd94b072f8` |
| `sources\TerraFX.Graphics\RecordedGraphicsCommand.cs` | C | 7348 | `346862c8d8fffce1a264e7ea970b0b1404cabab75753ca1c3ca12b140a082a9a` |
| `sources\TerraFX.Graphics\RecordedPassCommandList.cs` | C | 1807 | `6d024faf75de8967e61500cf11eb1f2b0424082f63ff87625efa20d911d21961` |
| `sources\TerraFX.Graphics\StorageResourceUse.cs` | C | 2705 | `2d682bee85c0088b40d075be72da23e92737dc8d98ec68b06537ed0258a0a18d` |
| `sources\TerraFX.Graphics\GraphicsDevice.RecordPass.cs` | C | 4133 | `796b8c1a92101a42d082cafd3c8a2f3851a6b06791636cadcbf56e4ed11cc44e` |
| `sources\TerraFX.Graphics\GraphicsDevice.PipelineRecording.cs` | C | 1962 | `d8d90f02fef58e08fb55e163286de9d31dc6a28f40cd8657e34c43c4c1ef7c72` |
| `sources\TerraFX.Graphics\GraphicsDevice.SubmitGraph.cs` | C | 18611 | `a85b4857b87f7487e213e23f8c431e9f8d0d16dfe3394e2e97c22cdeb37c6d93` |
| `sources\TerraFX.Graphics\GraphicsGraphSubmission.cs` | C | 131205 | `e691a06893dca483c262b1b707519f5176161e33d877c0006f96022f7c7cd378` |
| `sources\TerraFX.Graphics\GraphicsGraphSubmissionPass.cs` | C | 484 | `dc9eceed7c712c467c79b88481c66afbedcd82dd55a1bac6e71e575e93213475` |
| `sources\TerraFX.Graphics\GraphicsResourceOwner.cs` | C | 17503 | `c28bbb51e3fd3140472017de70f8718b60296335aa8959eeec54420073bf9846` |
| `sources\TerraFX.Graphics\CommandRecorder.cs` | C | 4293 | `befac55ea372433cf445cd7af0d750d34843a3a6ed594d5cb187fdd49601e511` |
| `sources\TerraFX.Graphics\GraphResourceAccess.cs` | C | 13437 | `2b7e9bdd2f8e4ccb78645a1f0d3c4b0a37234bc4fde96d5ed0b9810497d0168e` |
| `sources\TerraFX.Graphics\GraphBufferRange.cs` | C | 2586 | `1ea927c4f3cb859736f70ba1e2d521154188e318c17fc69b64586ad6b992a729` |
| `sources\TerraFX.Graphics\ShaderResourceSignature.cs` | C | 12293 | `ed145bf6509617f3e078871b90d598a1e03cd0c9a731baaeca15f2c06bc6b2d3` |
| `sources\TerraFX.Graphics\TerraFX.Graphics.csproj` | C | 817 | `e04f4e614e10b07d60a9e56b1c60e69d85b63240339587271235d079e197ccd4` |
| `sources\TerraFX.Runtime\Rendering\RenderGraphPlan.cs` | M | 17430 | `a81c5a8a6163d0d3efc198ee9581f0eced119728c3d27e84f063abd901c42b04` |
| `sources\TerraFX.Runtime\Rendering\RenderGraphPlan.Submission.cs` | C | 9286 | `8c6fd0d587b42e487141d43f60f54504a19e6f5f6f1c86394ca5c81d73ca3a02` |
| `sources\TerraFX.Runtime\Rendering\RenderGraph.cs` | M | 45818 | `fc12ef6cf50752fe4c5ae4cdd504a44a68690a58dc8d006a0a708b5b57113586` |
| `sources\TerraFX.Runtime\Rendering\RenderSpriteGraphState.cs` | M | 3519 | `268a1d4dc312b398411b5536672050006b1ee34e1102e4a8d662449121649872` |
| `sources\TerraFX.Runtime\Rendering\RenderSpriteGraphBufferUpload.cs` | C | 308 | `2e4d4efd0ceded2300ecb4f16a3c4eda5799781afe132af18b218bdc1a052256` |
| `sources\TerraFX.Runtime\Rendering\RenderBuiltInSubmissionResources.cs` | M | 4132 | `c675a2f000938ffad0d1ddb11e9aa786da6cfbae88001edadebf8a3747542927` |
| `sources\TerraFX.Runtime\Rendering\RenderSubmissionReceipt.cs` | M | 2851 | `77120676f289ad7d8891875a783bafa05e64b7f0317bc09becc2545ac42e5846` |
| `sources\TerraFX.Runtime\TerraFX.Runtime.csproj` | M | 14924 | `1ae7732f0333e949128268b9d3c0496f4259c322311550dab1a2b564920c0fe4` |
| `docs\design\02-dotnet-api-memory-and-ownership.md` | C | 37545 | `40f913a50434209e09941c22625a6d0722202bab97ebac0e153c1ea3368e2291` |
| `docs\design\04-capacity-and-workload-profiles.md` | M | 27808 | `a4d86f3fa373a64548435c53582bf5916e29d77f34650f9d161ccec90e6f0245` |
| `docs\design\17-graphics-device-and-resources.md` | C | 40823 | `8157e7da5472b9aabd891a05e84f2b997487a5c5e3ff67cb589681747d628718` |
| `docs\design\18-shaders-and-material-pipeline.md` | C | 18074 | `af20911d9d06bfb58f6c771511181f59347c4659129cfd5fc8cf622ed191c0d7` |
| `docs\design\19-render-graph-synchronization-and-presentation.md` | C | 26572 | `ab673dedbd85b7a161c5d7dc871bf5b486911dc27ec4279ac43827b005241b8b` |
| `docs\design\30-public-api-review-surface.md` | M | 485241 | `15848c82f3858a77a87a0d3ea6fbcc07046eb6e77ca5b21748c72b90efab598b` |
| `docs\design\35-implementation-roadmap.md` | C | 30557 | `24eec6e92b7ad904d552f290ef748ffc8fce13b6b94a89ebd9f39487d4584d87` |
| `docs\project\Roadmap.md` | C | 7228 | `c4fbe82155a6d82ffd2d13ad2f18056d938738c4fa5a66760c4877e24ed001c7` |
| `docs\project\Building.md` | C | 4652 | `f53ab58b89eeae7e17b70970075be9322a936d443bb6d5239c3238e594deabc6` |
| `global.json` | C | 179 | `1bce01871321c2df82e887da428ce5c121a6c6541fe65017fe3d249be47e190c` |
| `Directory.Build.props` | C | 4236 | `770ee4b2a422861a6f23f0d39dd0eca361a81cb87e64ebac4e12170f71231d7a` |
| `Directory.Build.targets` | C | 1877 | `aeec1d08e8037470239035812351c4045124607d67a354d482112f4d642d4105` |
| `.editorconfig` | C | 26884 | `342683de2b1c262624546f312f7d8e2078f89185add39f43f1fb5013376fd553` |
| `.github\workflows\ci.yml` | C | 5643 | `03efea672cd52e9ef053e9ce0285e7ca02c6ce6fb0d21268ade4255ed5a39d58` |

### Dirty-path inventory

The following is the complete initial status inventory, rendered with Windows separators. Outside the direct manifest it records **status, not a byte freeze or contract dependency**. In particular the M04 readback interfaces/backend files and `GraphicsDevice.cs` are listed only to disclose surrounding work; their contents were not read or hashed for this review.

```text
M docs\design\04-capacity-and-workload-profiles.md
M docs\design\12-entities-components-and-queries.md
M docs\design\16-scenes-transforms-and-cameras.md
M docs\design\30-public-api-review-surface.md
M sources\TerraFX.Graphics.D3D12\D3D12GraphicsBackend.cs
M sources\TerraFX.Graphics.Vulkan\VulkanBackendDevice.BackendPort.cs
M sources\TerraFX.Graphics.Vulkan\VulkanBackendDevice.Submission.cs
M sources\TerraFX.Graphics\GraphicsDevice.cs
M sources\TerraFX.Graphics\IGraphicsBackendDevice.cs
M sources\TerraFX.Runtime\Animation\AnimationClipAssetBinding.cs
M sources\TerraFX.Runtime\Entities\World.cs
M sources\TerraFX.Runtime\Entities\WorldAdmissionDomain.cs
M sources\TerraFX.Runtime\Entities\WorldDomainReservation.cs
M sources\TerraFX.Runtime\Entities\WorldGuidRegistry.cs
M sources\TerraFX.Runtime\Entities\WorldGuidRetirementPermit.cs
M sources\TerraFX.Runtime\Entities\WorldLifetimeControl.cs
M sources\TerraFX.Runtime\Entities\WorldSceneStorageReservation.cs
M sources\TerraFX.Runtime\Rendering\RenderBuiltInSubmissionResources.cs
M sources\TerraFX.Runtime\Rendering\RenderGraph.cs
M sources\TerraFX.Runtime\Rendering\RenderGraphPlan.Recording.cs
M sources\TerraFX.Runtime\Rendering\RenderGraphPlan.cs
M sources\TerraFX.Runtime\Rendering\RenderMeshC0ShaderReflection.cs
M sources\TerraFX.Runtime\Rendering\RenderSceneAssetBundle.cs
M sources\TerraFX.Runtime\Rendering\RenderSpriteGraphState.cs
M sources\TerraFX.Runtime\Rendering\RenderSubmissionReceipt.cs
M sources\TerraFX.Runtime\Rendering\Renderer.CpuExtractionSupport.cs
M sources\TerraFX.Runtime\Rendering\Renderer.Extract.cs
M sources\TerraFX.Runtime\Rendering\Renderer.MeshC0Graph.cs
M sources\TerraFX.Runtime\Rendering\Renderer.Submit.cs
M sources\TerraFX.Runtime\Scenes\ISceneExpansionResourcePreparer.cs
M sources\TerraFX.Runtime\Scenes\PreparedScene.cs
M sources\TerraFX.Runtime\Scenes\SceneActivationRequest.cs
M sources\TerraFX.Runtime\Scenes\SceneExpansionPlan.cs
M sources\TerraFX.Runtime\Scenes\SceneLoadOptions.cs
M sources\TerraFX.Runtime\Scenes\SceneLoadOptionsSnapshot.cs
M sources\TerraFX.Runtime\Scenes\SceneLoader.cs
M sources\TerraFX.Runtime\Scenes\ScenePrefabExpansionPlanner.cs
M sources\TerraFX.Runtime\Shaders\MeshC0Vertex.hlsl
M sources\TerraFX.Runtime\TerraFX.Runtime.csproj
M tests\TerraFX.Graphics.Tests\GraphicsBufferTransferTests.cs
M tests\TerraFX.Graphics.Tests\GraphicsPipelineRequestTests.cs
M tests\TerraFX.Runtime.Tests\Entities\WorldAdmissionDomainTests.cs
M tests\TerraFX.Runtime.Tests\Entities\WorldLifetimeControlQuarantineTests.cs
M tests\TerraFX.Runtime.Tests\Scenes\PreparedSceneAssetRetentionTests.cs
M tests\TerraFX.Runtime.Tests\Scenes\RenderSpriteCapacityTests.cs
M tests\TerraFX.Runtime.Tests\Scenes\RenderSpriteComponentTests.cs
M tests\TerraFX.Runtime.Tests\Scenes\RendererExtractionTests.cs
M tests\TerraFX.Runtime.Tests\Scenes\SceneLoadOptionsTests.cs
M tests\TerraFX.Runtime.Tests\Scenes\SceneSupportRegistrationTests.cs
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
U sources\TerraFX.Graphics\IGraphicsBackendBufferReadback.cs
U sources\TerraFX.Runtime\Entities\WorldGuidUse.cs
U sources\TerraFX.Runtime\Entities\WorldGuidUseControl.cs
U sources\TerraFX.Runtime\Rendering\GraphBufferReadbackExport.cs
U sources\TerraFX.Runtime\Rendering\RenderMeshC0NormalDiagnosticGraphPass.cs
U sources\TerraFX.Runtime\Rendering\RenderMeshC0NormalDiagnosticWork.cs
U sources\TerraFX.Runtime\Rendering\RenderSkinnedNormalDiagnostic.cs
U sources\TerraFX.Runtime\Rendering\Renderer.SkinnedNormalDiagnosticPipeline.cs
U sources\TerraFX.Runtime\Shaders\MeshC0Skinning.hlsl
U sources\TerraFX.Runtime\Shaders\SkinnedNormalDiagnostic.hlsl
```

### Verification disposition

The post-draft check at `2026-11-16T04:05:17.4704335Z` verified all **46 direct-input byte lengths, hashes, and states with zero mismatches**, unchanged HEAD/index, and no staged changes. It detected one additional modified path outside the direct-input set:

```text
M tests\TerraFX.Graphics.Vulkan.Tests\VulkanBackendDeviceTests.cs
```

That concurrent test edit was not made by this proposal task; its bytes were neither read nor frozen and no reasoning here depends on them. The current surrounding inventory, excluding only this new proposal, is therefore **71 paths: 50 tracked unstaged modifications and 21 untracked files**. Its canonical status SHA-256 is `097cf1398f591843ce02290dd7e0b52cfe43d1dae7c3eace674568f6fb43a189`; it is exactly the initial inventory plus the disclosed path. The original two diagnostic proposal files remain byte-for-byte unchanged.

Only this new proposal was written. UTF-8/LF and trailing-whitespace checks were clean. The ongoing excluded M04 readback edits are deliberately not covered by a whole-worktree byte-stability claim. This verification establishes input preservation/status disclosure, not the unexecuted footprint, admission, native, or milestone gates in section 7.
