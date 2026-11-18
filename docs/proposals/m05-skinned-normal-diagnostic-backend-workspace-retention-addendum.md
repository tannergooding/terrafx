# M05 backend graph-submission workspace and retention boundary

## 1. Decision and scope

**Tentative implementation authorization, pending review; not budget acceptance.** Select an **exact-capacity, submission-owned backend storage batch**, transferred with the existing Graphics request, and **device-owned fixed retention cells** reserved atomically at queue acceptance. Backend mutable state is acquired on the owner thread inside that same request. Eliminate graph-path growth of shared uncertainty lists; do not predict their BCL capacities.

This makes the backend row of the [physical-capacity owner addendum](m05-skinned-normal-diagnostic-preflight-hash-capacity-owner-addendum.md) concrete. Its general physical-footprint and retained-interest requirements remain applicable. The selected mechanism may be implemented tentatively; an unknown footprint or owner still prevents complete diagnostic admission.

The joint **33,554,432-byte preparation/recording** and independent **16,777,216-byte completion** ledgers, and **whole-graph admission, remain NO-GO**. This changes no public API, diagnostic/acceptance identifier, schema version, shader ABI, draw limit, command membership, native budget or cancellation/completion contract. In particular, the dirty diagnostic result carrier is not accepted API, and blocked Scene consumers are not acceptance evidence. M08 release qualification remains separately open.

## 2. Current source evidence and first boundaries

Source locations below refer to the frozen inputs in section 8, based on HEAD `07dfe60d33e7b91061f8eab9ac763ca85b73c4d2`.

### Graphics handoff

- `sources\TerraFX.Graphics\GraphicsDevice.SubmitGraph.cs:122-203`: resource-use transfer and shared validation precede `SubmitGraphRequest` construction. The request constructs its own TCS. Diagnostic submission then calls the nonwaiting `TryWriteDiagnosticRequest`; ordinary work uses the existing waiting route. Failure of diagnostic entry calls `AdmissionFailed`, not the ordinary route.
- The same file, lines 79-115 and 319-365: acquisition builds a resource-owner dictionary; transfer builds a distinct-owner set and a new lease list. These allocations precede backend entry. They are separate Graphics construction groups, not covered by a backend descriptor-reference array.
- `sources\TerraFX.Graphics\GraphicsGraphSubmission.cs:1415-1421`: each validation allocates two import-indexed `ushort[]` flags and pass/access-indexed `byte[]` flags. The seven retained indexes and absence of HashSet/Dictionary construction do not remove these four per-call backings. Both native backends validate again after queue acceptance.
- `sources\TerraFX.Graphics\GraphicsOwnerRequestQueue.cs:11-55, 233-311, 443-485`: requests use a fixed 256-reference ring. Diagnostic entry checks its backing component and tries the existing gate without waiting; dequeue promotes the oldest ordinary writer under that gate. This is not a backend-storage reservation.
- `sources\TerraFX.Graphics\GraphicsDevice.cs:22-25, 55-84, 1102-1129`: frame-slot count is configured within the existing one-to-four range. The pending-request list reserves startup capacity and refuses growth. Neither its physical capacity nor a free semaphore/ring slot funds backend storage.

### Backend allocation inventory

For this table, `B` means every recorded `SetBindings` command, `C` means distinct non-depth attachment ResourceIds in first-occurrence order, `I_b`/`I_t` mean all buffer/texture imports, and `P` means submission passes. These are counts derived from complete frozen membership, not new limits.

| Allocation group and source | Current first construction / capacity source | Boundary that remains unqualified |
|---|---|---|
| D3D12 cold shared controls: `D3D12GraphicsBackend.cs:1907, 4147-4250, 4375-4385` | `GetTransferQueue` can construct the transfer queue and its native/frame controls. Descriptor heap initialization constructs fixed-capacity range allocators, each with a `bool[]`. | Owner-thread lazy initialization can precede even the local descriptor list. Device-owned controls and initialization overlap need funding before diagnostic admission; a warmed run is insufficient. |
| D3D12 per-graph descriptors and uncertainty: same file, `1911-1930, 2720-2808` | Local `List<D3D12BackendDescriptorSet>(B)`; uncertainty lists request their current Count plus B descriptors / one RTV heap. | List controls, descriptor-owner objects and retained arrays are not reference slots. Shared `EnsureCapacity` may allocate before recording; uncertainty later appends to those shared lists. |
| D3D12 color views: same file, `1931-2012` | Growing ID list/set; a dictionary requested at C; native RTV heap with C descriptors. | ID/hash backing, view mapping and heap ownership are distinct groups. These constructions happen after enqueue and before native execution. |
| Vulkan scope tracking: `VulkanBackendDevice.GraphSubmission.cs:266-643` | Four texture dictionaries for color/depth/sampled/storage, a color-then-sampled set, a separate copy-texture dictionary; current-buffer dictionary and graph-buffer tuple list. Requests are mostly `I_t - 1` or `I_b`. | Tuple strides include native-owner references, subresources and state masks, not just handles. Copy tracking includes the output import; `I_t - 1` is not its complete membership bound. Buffer aggregation uses native-buffer reference identity, separately from ResourceId lookup. |
| Vulkan color/store/content tracking: same file, `665-693` | Growing color-ID list/set; color-view dictionary and image-view list at C; color/depth store dictionaries, pass-index dictionary at P, final-content dictionary. Several requests use `I_t - 1`. | Store/content tracking can include the output. Requested dictionary capacities are not physical capacities. Color, copy and sampled roles coexist and cannot be collapsed into one current-layout field. |
| Vulkan descriptor and uncertainty retention: same file, `243-264, 733-736, 1700, 1888-1941, 1967-2061` | Local descriptor list and uncertainty descriptor reserve use G, counted by the distinct nonempty Frame/Material/ObjectData/Pass groups across each binding set. `BindGraphDescriptorSets` appends one descriptor owner for each such group. Image-view uncertainty requests C; target-owner uncertainty requests one. | Group-count under-reservation is fixed, but List/object controls, BCL growth overlap, descriptor-owner backing and retained existing capacity remain owner-thread funding/retention gaps. |
| Standing native-frame holders | D3D12 `815-878` allocates descriptor-list, fence and RTV-heap arrays at the configured slot count. Vulkan `VulkanBackendDevice.cs:126-132, 1545-1556` constructs that many slot objects plus their array; slots retain descriptor/image-view lists and fences. | Existing startup arrays/slot controls, the new admission cells and each batch are separate physical owners. Replacing per-slot Lists must not omit either standing storage or still-retained old backing. |
| Binding translation, both backends | D3D12 `2318-2358, 2488-2574`; Vulkan graph submission `1190-1518, 1967-2061`: typed immutable builders, freeze copies, Vulkan per-group builders, and captured reflected-resource searches. | These are allocations inside recording, not just the top-level maps. Empty-group builder controls and builder/frozen-array overlap must be eliminated or included. |
| Descriptor construction and ownership, both backends | D3D12 `3245-3942`; Vulkan `VulkanBackendDevice.SampledImageDescriptors.cs:140-712`: sorted binding copies, per-kind owner arrays, combined retained arrays, native write/info/table scratch, descriptor-owner objects, native ranges/pools/views. | Descriptor-reference list capacity does not fund these transitive groups. Native resource reference counts and managed backing interests have different release conditions. |
| Depth-view caches | D3D12 `4827-4875`; Vulkan graph submission `49-77, 1320-1328`: first use can create a texture-owned DSV heap / cached depth image view. | Cache presence is mutable owner state. The cache's native and holder ownership cannot be inferred by a producer or refunded with graph-local color views. |
| Failure/publication controls | Vulkan graph submission `1840-1872` constructs a cleanup-exception list and may insert the original exception; both backends may construct aggregates. Graphics submission `226-255` copies leases and appends to quarantine bags. | Failure handling itself can allocate. `GraphicsDevice.cs:1503-1604` additionally drains work, copies owner-wide resource/surface sets and grows quarantine roots; this is not bounded by B or C. |

D3D12 locations in the table are under `sources\TerraFX.Graphics.D3D12`; Vulkan locations are under `sources\TerraFX.Graphics.Vulkan`.

**Concrete distinguishing case:** one valid `SetBindings` containing a Frame uniform binding and a Material uniform binding has B = 1 and G = 2. Commit `b35502bc` changes Vulkan's pre-scan to count the distinct nonempty groups and reserves two owner slots; `GraphDescriptorSetCapacityCountsDistinctNonemptyGroups` covers Frame+Material and repeated same-group bindings. This proves the group-count calculation, not physical owner/list capacity or a native graph observation. D3D12 still uses B because it creates one owner containing multiple tables. Keep separate formulas: **B for D3D12, G for Vulkan**. The existing eight-uniform-buffer/across-groups graph fixture is a relevant integration shape, not proof of storage reservation.

Both backends also invoke their optional graph-submission observer after repeated validation and before local preparation. The inspected Runtime observer constructs builders and retains allocation leases. An opaque observer is not an allocation-free borrow: its controls and escaped references must be qualified, or its presence makes complete diagnostic admission unavailable. Existing observer-based tests remain scoped to their exercised behavior.

### Native acceptance and subsequent retention

- D3D12 `D3D12GraphicsBackend.cs:6282-6317` marks uncertainty immediately before `ExecuteCommandLists`; `Signal` can fail after execution was issued. A missing returned fence is not proof of nonacceptance.
- Vulkan `VulkanBackendDevice.Submission.cs:240-317` can mark uncertainty on command-buffer end failure and marks it before `vkQueueSubmit2`. The timeline value is committed only after success.
- Both graph paths install descriptor/view holders into their selected native frame slot before presentation. Presentation can fail afterward. Normal retirement destroys those holders at confirmed fence completion: D3D12 `1705-1736`, Vulkan graph submission `2367-2402`.
- Graphics submission `410-462, 490-560` records the returned fence, publishes owner states and starts pending fence work. `Task.Run` and its signal continuation are additional post-native controls. A positive submitted fence, or backend-reported uncertainty after backend entry, leads to quarantine on failure.

## 3. Selected physical representation and required data

Use one **private per-attempt storage batch**, not a submission-global scratch cache or a general renderer allocator. These are private ownership requirements, not declarations of currently available types.

The backend publishes an immutable storage recipe through its existing Graphics startup boundary. It names concrete backing element types, exact array-length rules, complete wrapper/control construction groups, and the supported compiled profile. It contains no current list Count/Capacity, descriptor free-space snapshot, native handles or texture layout/cache values. Graphics consumes this lower-layer recipe; neither backend acquires a Runtime/Scene sizing callback.

The nonallocating planning pass needs:

- The exact submission and recording-owner identities, device owner/epoch and backend recipe identity; Surface/swapchain epochs and output identity when applicable. Retain the actual recorded owners and their existing `ReadCommands` protocol. Do not copy the command stream or replace the recipe's exact-backing witness with equal-looking commands.
- Complete import ResourceIds, Graphics resource-owner identities, descriptions, ranges and import options; actual pass order/kinds/attachments/load/store actions; all accesses, transitions and both alias-barrier streams. Native-buffer reference-identity aggregation is resolved on the owner thread; capacity `I_b` does not require predicting that distinct count. Count scans borrow existing immutable data. Any stored planning directory is itself precharged before construction.
- For each binding command: its ordinal, preceding pipeline/layout and complete reflected signatures, each actual kind/group/binding/range, sampled-image arraying, and sampler identity. Derive independently the exact per-kind lengths and nonempty group membership. Reject unsupported metadata through the established validation channel, not by dropping a group.
- Checked physical lengths below, retained Runtime/Graphics baseline and other simultaneous live interests. A repeated count is not evidence that membership, owner or generation remained the same.

| Selected backing | Exact capacity source / contents |
|---|---|
| Shared validation scratch | One attempt-owned buffer-import flag array of `I_b`, texture-import flag array of `I_t`, pass flag array of P and access flag array of `Accesses.Length`. Reset between the producer and backend validation borrows. Concurrent attempts get separate funded scratch; no assumption that all calls on a submission are globally serialized. |
| D3D12 attachment/view directory | Exactly C first-occurrence color entries, each retaining its texture-import ordinal and RTV handle, plus any required import-indexed ordinal directory of `I_t`. Reuse Graphics' owned identity indexes through nonallocating private lookup. No growing color-ID list/set/dictionary. |
| Vulkan texture scope directory | Exactly `I_t` typed entries, including output ordinal zero, with independent presence/state fields for the existing primary color/depth/sampled/storage classification, copy scope, color-then-sampled membership, store actions and last content-writing pass/validity. Preserve current primary-role precedence, selected subresource, output handling and separate copy-state updates. Do not merge distinct role histories. |
| Vulkan buffer/pass directories | Current buffer scope entries of `I_b`; a separate reference-identity aggregate buffer-scope array with capacity `I_b` and an initialized count; pass ordinals of P where existing indexes cannot be borrowed. Preserve native-buffer identity aggregation and final-writer pass ordering. |
| Vulkan graph color views | C first-occurrence color/import ordinals and C handle slots. The import-indexed directory supplies lookup. Graph color views remain separate from descriptor image views and texture-owned cached depth views. |
| Descriptor-owner slots | Exactly B D3D12 slots. Exactly one Vulkan slot for every nonempty group of every binding command, in the existing command/group order. Compute that sum with checked arithmetic; never substitute B or a guessed multiplier/load factor. |
| D3D12 descriptor construction | Per command, exact uniform/storage-buffer, sampled/storage-image and sampler translation lengths. Retained buffers have the sum of the two buffer-kind lengths; retained textures have the two texture-kind lengths; retained samplers have their own length. Table count/range-start/root-index/offset scratch uses the existing `D3D12DescriptorLayout.TableCount`; handle storage uses the actual populated-table count derived from full reflection. Include rounded uniform ranges, sorted-order storage and the real descriptor-owner object. |
| Vulkan descriptor construction | Per emitted group, exact five kind-specific binding lengths; buffer/image info and write arrays at their corresponding kind lengths; combined retained buffer/texture arrays; sampler array; descriptor image-view slots at sampled-plus-storage image count; pool-size records for exactly the nonempty native descriptor kinds; and the real descriptor-owner object. |
| Rollback/retention state | Initialized prefixes, acquired native range/pool/view handles, retained-reference counts, recording/acquired-image flags, selected native frame slot, acceptance/fence state, primary fault and recording-stop/acquired-image-release fault slots, and the already transferred resource-use holder. These fields/holders belong to the batch's qualified construction group, not a later failure allocation. |

Select exact arrays and explicitly sized controls for this graph route. Rework descriptor construction to consume the preclaimed storage while reusing native validation/emission/release logic; leaving the allocating builder/`ToImmutable`/`ToArray`/captured-search helper behind a new caller does not implement the recipe. Sorting and metadata lookup need source-owned nonallocating algorithms or separately qualified controls, not assumed-free LINQ/comparers. Initialize all exposed/read slots and stack records explicitly.

A recipe can reserve all prescribed construction scratch conservatively; a smaller sequential scratch maximum requires proof of nonoverlap. Apply existing qualified array rules only to their supported representations. Struct stride, object controls, zero-length/shared initialization, factory/interest controls and failure-return construction still need their own profile evidence. No byte tariff is supplied here.

## 4. Concrete reservation and handoff

### Standing owner storage

At device startup, establish an owner-funded **retention anchor with an exact N-reference cell array**, where N is the configured graph-frame-slot count, not the request-ring capacity or the pending List's rounded capacity. Register its precreated intrusive anchor in a private strong process-root chain before publishing this device's diagnostic storage capability. Registration and safe-shutdown unlink use one qualified startup/shutdown synchronization group; graph failure only changes state on an already rooted anchor and never needs to publish a new node.

The anchor retains the actual device/backend and occupied batches. Safe shutdown unlinks it only after every cell and other native obligation is safely closed; a quarantined anchor remains rooted. Its object, cell array, process-root synchronization and initialization are explicit unresolved startup funding terms, not free statics or another per-graph 32-MiB allowance. Shared transfer-queue/frame controls and D3D12's fixed descriptor-allocator backings likewise need device-owned initialization/funding before this capability is published. Do not warm them through a diagnostic graph and declare them absent.

All graph requests using that device's frame-slot protocol participate in the cell protocol; ordinary requests cannot mutate an untracked graph-retention lane. This adds no smaller graph-count limit and no M05 byte ceiling to ordinary graphs. An existing frame-slot reservation authorizes at most one occupied cell for its transferred request. Managed cell indexes are not backend command-frame-slot indexes.

### Producer phase: immutable planning and safe construction

1. Preserve existing argument, cancellation, device/frame-reservation identity and epoch precedence. Borrow complete frozen membership and the immutable backend recipe without reading mutable backend/native state or queuing an owner probe. The real recorded-reader/metadata-read protocol needs a qualified standing or construction interest first, including its cold synchronization/contended paths; `ReadCommands` being a ref struct does not establish zero allocation or authorize bypassing its gate.
2. Before constructing the batch, planning directory, scratch, descriptor controls or submit request/TCS, reserve their complete construction group inside the **same still-retained joint envelope**. Include the reservation/carrier's own construction and actual overlap. The [request-control](m05-skinned-normal-diagnostic-request-control-admission-addendum.md) and [return-owner](m05-skinned-normal-diagnostic-async-return-ownership-addendum.md) grants remain separately required; they do not exist merely because this batch's lengths are derivable.
3. Materialize exactly the planned managed backings/controls through pure storage construction; backend handles, live texture scopes, native owner references and native reservations are initialized only on the owner thread. The producer owns safe rollback until enqueue. Never derive admission by querying the constructed collection's capacity. Unknown profile, checked overflow, changed membership or any unqualified adjacent construction refuses the whole diagnostic-bearing graph.
4. At the existing queue gate, in the same nonwaiting acceptance operation, recheck closure/generation and the one-shot frame/request transfer; claim an empty retention cell with the exact batch reference; install its retained interest and request; then append that same work item to the existing FIFO ring and signal it. A failed operation before append restores the cell and leaves ownership with the producer. No allocation, native query, callback, waiter or second queue is permitted in this operation.

Cell availability is owner-synchronized admission state, not a producer reading backend uncertainty counts. Queue close and ordinary-writer promotion keep their current ordering under this gate. A cell cannot become reusable by dequeue: release uses an identity-checked atomic clear after the old batch's actual safe obligations end, so a delayed cleanup cannot clear a replacement batch. Publish fully initialized state before owner wakeup. Signal/publication failure must preserve whichever side already owns accepted work.

### Owner phase: mutable native state in the same request

5. On dequeue, transfer execution authority, not the cell's retained-byte interest. Recheck device/Surface generations, recorded-owner liveness and backend-specific validity. Repeat shared validation using that attempt's reset scratch. Keep actual command-reader lifetimes intact; current owner-backed reads are not permission to retain unaccompanied copied command referents.
6. Only here read completion, choose the actual free native frame slot, load Vulkan texture layouts/last-access masks/content state and depth-cache presence, and inspect live D3D12 descriptor allocation state. Earlier ordinary submissions/uploads/retirements may have changed them since planning. Do not treat a producer's initial-state snapshot as current native state.
7. As descriptor construction reaches each command, reserve D3D12's required contiguous ranges in actual emission order using the existing owner-thread fixed allocators, storing each successful range immediately in its planned rollback entry. Roll back earlier ranges if a later reservation fails. Preserve fragmentation/exhaustion refusal. Vulkan creates the planned per-group pools/sets and descriptor/color views into initialized batch slots, recording each acquired handle and retained-reference prefix immediately. Preserve native construction/WSI ordering rather than hoisting a later native failure ahead of acquisition solely to simplify sizing.
8. Complete every required **managed holder and retention reservation** before command recording/acquired-image operations can require uncertain retention; native entries are filled incrementally in their existing operation order. Queue acceptance commits managed storage/retention, not native capacity: native exhaustion can still fault accepted work safely before execution, but cannot force batch growth. Texture-owned lazy depth views require a qualified standing-owner construction/transfer rule; if absent, that graph shape stays unavailable. No native preparation branch may allocate unplanned managed scratch or use the legacy shared uncertainty lists as overflow storage.
9. Immediately before the backend's possibly accepted execution call, mark the batch accordingly. On success install its fence and the **same batch reference** in the selected native frame slot before presentation, owner-state publication, fence-worker startup or result publication can throw. The existing pending-submission backing remains separately owned; its capacity is not replaced or refunded by this cell. No new owner round trip occurs.

## 5. Rollback, completion and quarantine ordering

| Outcome | Required order and retained interest |
|---|---|
| Construction or queue refusal, including full/contended/closed admission | No native work and no accepted queue reference. Unwind only initialized safe storage/uses, clear any uncommitted cell claim and release the frame reservation through its existing exactly-once protocol. Deliver failure through the already-funded contextual return. Never retry by silently using ordinary queue waiting. |
| Owner validation/native reservation fails before recording | Release successfully acquired native ranges/objects in reverse order, then safe managed holders. Already accepted unrelated resource operations remain accepted. |
| Recording or WSI acquisition fails before execution | First stop/reset recording and release any acquired image through the existing backend protocol. Only confirmed safe cleanup permits descriptor/view/range/use release. Record both original and cleanup failures in planned fault slots; no growing cleanup list is needed to preserve them. |
| Native execution may have been accepted; signal/end/submit/presentation/wait/publication fails; or cleanup is uncertain | Mark the rooted batch/anchor retained **before** formatting/aggregating faults, faulting a Task or releasing a count slot. Keep all applicable descriptor owners/ranges, graph and descriptor views, target/resources, command owners, use leases and storage interests in that same cell. Preserve both backend uncertainty and Graphics device-loss/retirement semantics. |
| Confirmed fence completion | Observe the actual backend fence; destroy descriptor owners and graph-local views/heaps/ranges on their owner thread; finish recorded reads/uses and detach ended roots. Clear the native frame-slot reference and admission cell only after safe release. Cache-owned depth state stays with its texture. A cleanup failure instead retains the batch and closes admission; it does not recycle a partially retired cell. |

Replace graph-path `_uncertainGraphDescriptorSets`, `_uncertainGraphRenderTargetHeaps`, `_uncertainGraphImageViews` and graph-target appends to `_uncertainTextureOwners` with retention of the already rooted batch. Do not resize or flatten its arrays on uncertainty. Give Graphics the already transferred exact lease holder; `QuarantineGraphSubmission` must not create another lease-array copy or append to a growing bag to establish safety.

This also requires closing the **actual owner-wide failure edge**, not just the two graph bags: resource/Surface/device quarantine and `QuarantineOwnerState` must retain the existing device registries through the anchor without unreserved snapshot arrays/bag growth. Preserve quarantine marking, retirement reservations and notifications; do not silently skip them. Those owners' existing construction/retention interests remain charged. Until this path and its controls are qualified, batch-local failure tests cannot certify device-wide quarantine admission.

A released frame semaphore, empty ring/native slot, completed/faulted Task, or cleared original field is not a managed-byte refund. Copied commands/bindings/child arrays, retained requests/returned operations and synchronous cleanup contexts keep their actual interests. Current `GraphicsGraphSubmissionPass` uses `RecordedPassCommandList.ReadCommands`; older raw-command-array inventories are not descriptions of the current getter. The remaining element/reference-alias qualification in the [physical-storage and alias proposal](m05-skinned-normal-diagnostic-physical-storage-and-alias-qualification-addendum.md) still applies.

## 6. Focused evidence gates

This document performs source/fixture inspection only; it reports no fresh test pass, allocation measurement, native/AOT observation or Scene consumer result.

| Required evidence after the owning implementation exists | Existing harness / distinguishing checks |
|---|---|
| Exact shape and physical construction | Extend the owning Graphics/backend fixtures with independently derived literal empty, sparse, permuted and repeated-color cases; Frame-plus-Material in one binding command; multiple binding commands/groups; copy of output and non-output textures; color→sample/copy, depth and final discard/copy-write ordering. Verify the Vulkan emitted-group sum, all typed array lengths/identities and no hidden builder/hash/growth path. Preserve existing validation/error order. |
| Complete component grant before allocation | Extend `GraphicsBufferTransferTests`' exact-capacity/one-byte-short patterns to the **complete selected batch** beside an independently retained baseline, including controls, scratch, zero/shared and cold paths. Cover checked overflow, unknown recipe/profile, changed recording/import membership and stale generations. A reference-array assertion or warmed thread-allocation delta is not a complete object-footprint proof. |
| Atomic queue/cell transfer | Extend `GraphicsOwnerRequestQueueTests`' fixed-backing/no-waiter, contention, full/closed, FIFO and cancellation/close-race cases. Verify one cell per transferred frame reservation, no cell/ring retention on failed admission, no withdrawal after acceptance, no dequeue refund, identity-safe reuse and no fallback/second probe. Exercise all existing configured frame-slot counts with the existing frame-admission fixture. |
| Owner-only state and safe rollback | In the owning managed backend harness, place ordinary work/retirement between planning and execution. Check fresh owner state, descriptor fragmentation/exhaustion, partial range/pool/view acquisition, stop/reset/image-release failure, delayed completion and retirement failure. Verify initialized-prefix cleanup and same-interest retention without collection growth. Tests must exercise real production storage/owner paths, not a second map implementation. |
| Backend integration | Reuse D3D12 range-allocator and native descriptor-owner tests, Vulkan descriptor-owner tests, and graph binding/copy/attachment/fence-retention shapes. Use actual Graphics-created resources/pipelines and command recording at the lower-layer boundary when available. Preserve native descriptor retention/reuse and output/barrier semantics. If a Runtime graph selection reaches the Scene preparation guard first, record it as blocked; do not fabricate a `RenderSnapshot` or claim backend coverage from that failure. |
| Accepted/uncertain terminal paths | Cover missing returned fence after possible execution, positive fence followed by presentation/owner-publication/fence-worker failure, cleanup exceptions, independently retained returned Tasks and copied command referents, and shutdown with retained batches. Existing process-isolated timeout overrides establish only their injected behavior; they do not establish real driver/OS loss, timeout or uncertain-retirement observations. |

Relevant inspected fixtures are `GraphicsOwnerRequestQueueTests`, `GraphicsBufferTransferTests` (`GraphFrameSlotAdmissionWaitsAtCapacityAndRecoversAfterRelease`, index charge/refusal and `SubmissionRecordingRootsDetachOnlyAfterSafeRelease`), `D3D12DescriptorRangeAllocatorTests`, `VulkanBackendDeviceTests`, and Runtime's `EngineHostGraphSubmissionTests` / `CreatesD3D12DescriptorSetsAndRetainsNativeOwners`.

Use the ordinary owning project/filter commands in [Building](../project/Building.md) and [Graphics test procedures](../project/Building.Graphics.md), Debug before Release, confirming nonzero selected/executed cases and recording unsupported variants separately. Qualify actual compiler/runtime/architecture/control layouts: the selected SDK is `11.0.100-rc.3.26522.148`, the shared TFM is `net11.0`, and central interop inputs are Windows `10.0.26100.6` / Vulkan `1.4.328.1`. These input versions are not runtime-footprint or NativeAOT evidence. Keep real native stimulus gaps with their existing Chapter 35/M09 owners; do not repeat blocked probes merely to close this managed-storage row.

## 7. Next action and unresolved prerequisites

**Next action:** close the private Vulkan per-group descriptor batch's complete physical/control recipe and the N-cell anchor's startup ownership, then implement its single-request transfer, rollback and retained-cell tests as one vertical slice. The `b35502bc` group-count correction removes the identified under-reservation; the focused group-count case passes and the Vulkan suite passes 93 with 18 skips in Debug and Release. This is managed/backend-suite evidence, not a native graph or physical-retention qualification, and does not fund transitive descriptor-construction scratch. Apply the same carrier to D3D12's table/range and RTV batch; do not treat correcting B or G alone as a retention fix. The tentative authorization permits this bounded implementation sequencing and independent dependency-ready M05/M03-B work, not a GO disposition.

| Open prerequisite | Required closure |
|---|---|
| Exact backend/control/startup recipes | Establish the concrete typed layouts, supported build/runtime profiles, cold/shared initialization, native lazy-cache ownership and anchor/root synchronization funding. No guessed wrapper tariff, postallocation capacity query or second full allowance. |
| Whole-graph grant and Graphics edges | Supply Runtime's retained baseline and complete phase maxima, actual use-transfer/request/TCS/return/fence-control groups and refusal return. The existing index/flag/ring components are necessary but insufficient. |
| Safe/uncertain managed lifetime | Close descriptor/command referent aliases, actual return/cleanup contexts and owner-wide quarantine publication without unreserved copies/growth. A cell is a retention mechanism, not proof that all managed roots ended. |
| Independent completion ledger | Close the 16-MiB result/receipt/return interests through their real owners; do not borrow Core readback or native-resource allowances for them. |
| M03-B and consumer provenance | Keep Scene preparation fail-closed until its real preparation-root/workspace/receiver/cleanup/producer-publication gates close. Requalify the actual World-produced Scene consumer afterward. This backend proposal does not supply that producer. |
| Public surface and later qualification | Diagnostic result API review remains open. Chapter 35's native/AOT/public-consumer obligations, M08 release gates and separately deferred M09 observations remain distinct; no milestone/case is closed here. |

## 8. Frozen review inputs

The tables identify the complete load-bearing static input set for this backend decision, not a repository inventory or a footprint certificate. No schema/public declaration is proposed. Recheck these whole-file SHA-256 values and the preserved dirty-patch fingerprint before applying a review disposition; HEAD alone is insufficient. Historical freezes in linked proposals do not review these newer source bytes.

At capture, 22 pre-existing dirty files were preserved. Their manifest SHA-256 is `1DAA3D4C53A08934D93B34EA5B855CFF39D79683EBFCA3E02328EF6D08D0B425`. The manifest excludes this new proposal and consists of UTF-8 records `status<TAB>Windows-relative-path<TAB>uppercase-file-SHA256<LF>`, sorted by path, from `git status --porcelain=v1 -z --untracked-files=all`. This fingerprint records preservation, not approval of that patch. The four dirty design/schema-authority files are additionally pinned below and explicitly remain outside any public-contract acceptance by this proposal.

| Input | SHA-256 |
|---|---|
| `global.json` | `1BCE01871321C2DF82E887DA428CE5C121A6C6541FE65017FE3D249BE47E190C` |
| `Directory.Build.props` | `770EE4B2A422861A6F23F0D39DD0ECA361A81CB87E64EBAC4E12170F71231D7A` |
| `Directory.Packages.props` | `1E0E79719EFC9259390D9CF4E63AC697561725C8CD910D2AB2541D6E5BB95A6D` |
| `docs\project\Roadmap.md` | `3086D68823CC53E67519ADBE3BA71EAC41A5B85054CB9FFC07432E387B33AFFE` |
| `docs\project\Building.md` | `F043A352870EFB6C7D4A61998225E2508A8F4370A57E784483A41819D452075F` |
| `docs\project\Building.Graphics.md` | `8227D03FEF2FD5709AB61FF70318EB5F18A717F1D0A8DC5432F6C765D25BADD3` |
| `docs\design\29-project-setup-build-and-deployment.md` | `3B55A013EEC327ECD648ACBD5B53717DD218AD288E8047AC21277609EFA2F47B` |
| `docs\design\35-implementation-roadmap.md` | `24EEC6E92B7AD904D552F290EF748FFC8FCE13B6B94A89EBD9F39487D4584D87` |
| `docs\proposals\m05-skinned-normal-diagnostic-preflight-hash-capacity-owner-addendum.md` | `D6A78B2BAE1C0D688A3F28B122A15CE6ABDE2F765795C83663BAEFFA6B0F5E8D` |
| `docs\proposals\m05-skinned-normal-diagnostic-preflight-alias-workspace-addendum.md` | `AE89901571E050F4B9F29997F7B3559B15021396F4C0BFC360A7FB05CE83BB3B` |
| `docs\proposals\m05-skinned-normal-diagnostic-request-control-admission-addendum.md` | `96397B0B835D727098FF3E9314296BA97ACEE0CA7E71E24FC3B6E9F5A3A0B7B0` |
| `docs\proposals\m05-skinned-normal-diagnostic-async-return-ownership-addendum.md` | `F5A8A8043A3E8677CEC3F18AAC1F85994A13FC25C6CFA1BA7E3E72E5B5E60991` |
| `docs\proposals\m05-skinned-normal-diagnostic-physical-storage-and-alias-qualification-addendum.md` | `FB9182D9FEBD235EC602114EA3944791B92D77D9BC0775584DCEFEDE26FB8D23` |
| `sources\TerraFX.Graphics\GraphicsGraphSubmission.cs` | `E308421BFD21DBC0E02BCF38C059BCDE990153F70764B3893C809541C3354404` |
| `sources\TerraFX.Graphics\GraphicsGraphSubmissionPass.cs` | `D9B4A53701AFA17BDB0E8E21E078AEECDBFE22A4A9B09393AF9719B5007CD315` |
| `sources\TerraFX.Graphics\RecordedPassCommandList.cs` | `0BA9BAD85B70F0745B7AED2ED5A0D86C35902BB8EE18296DBC9BC053DEDAE476` |
| `sources\TerraFX.Graphics\GraphicsOwnerRequestQueue.cs` | `B53D2EB1C14874A544519A46CE19288B334BF50EFFE86B6C807289F0BA743689` |
| `sources\TerraFX.Graphics\GraphicsDevice.SubmitGraph.cs` | `C68E23B5FFA591C639714101FB23A1F1AE883FAC28F722EEB5B034725C3A97FD` |
| `sources\TerraFX.Graphics\GraphicsDevice.cs` | `ED23475055D08BB3631935580912DB5AB30F55E0F5228227AF76BB123CEE5FEC` |
| `sources\TerraFX.Graphics\GraphicsDevice.Surfaces.cs` | `F3F5416E4777836CCAB3746AC4A5875137E07C2B64D307A25ECC7ADF8BC11C96` |
| `sources\TerraFX.Graphics\IGraphicsBackendDevice.cs` | `5A39A41A96528156D774EFED908AEE3661B5DA1A6E8EE3E3A0FE005F39F9BD0E` |
| `sources\TerraFX.Graphics.D3D12\D3D12GraphicsBackend.cs` | `DE8F5BCB77D1EE2F0AC47979F7D62E1D09FCE4761EF99C798CCAD6CB9ADE732F` |
| `sources\TerraFX.Graphics.D3D12\D3D12DescriptorLayout.cs` | `F7CB8EB19AA7E889FE2C740904BF6BD020A3CD1FDC3EB7DC8804C3EA0EDFE7FE` |
| `sources\TerraFX.Graphics.D3D12\D3D12DescriptorRangeAllocator.cs` | `B8F81495D00369E75849DC0EB5EB760AFE6A7FA19FEE78E4DA7340E8BF75265D` |
| `sources\TerraFX.Graphics.Vulkan\VulkanBackendDevice.GraphSubmission.cs` | `0F4A9701CACB291E56DF6D00EE95CB817663D7DD40DDE30C507B0E6D3C54C74D` |
| `sources\TerraFX.Graphics.Vulkan\VulkanBackendDevice.SampledImageDescriptors.cs` | `4F855F243CE441851DBB6F7C7D13E70F8BFDA57AFBE6D8CCCA504FE0465BF431` |
| `sources\TerraFX.Graphics.Vulkan\VulkanBackendDescriptorSet.cs` | `FB3F31180F5AEA6BF87296B6F2E2C866152D4CCC074B590A578EE4D7AAE7AB8F` |
| `sources\TerraFX.Graphics.Vulkan\VulkanBackendDevice.Submission.cs` | `475CB25BFBD807E98D3360CD61D4031347D416A33EA48B444B79C895DA21B4FF` |
| `sources\TerraFX.Graphics.Vulkan\VulkanBackendDevice.cs` | `55D36B5E8B4734EE4EED92553E126DB6BC10E9FEE08865677DD81B0957F8DBA1` |
| `sources\TerraFX.Runtime\Rendering\RenderGraphPlan.Submission.cs` | `4ADE3D4E3DCD71DAADBDB2D63294D8D556A14019074CA8173A1171698FA2B55D` |
| `sources\TerraFX.Runtime\Rendering\Renderer.Submit.cs` | `8BF0D0662C9D3DE0A1670ED05C89FAB1E6A749D744BF796A9D5E0236BF4DDB79` |
| `tests\TerraFX.Graphics.Tests\GraphicsOwnerRequestQueueTests.cs` | `7553591FF07775B9ECE84D23D420CB2214C91822C63C51DBB6B51E4F3D35D4C3` |
| `tests\TerraFX.Graphics.Tests\GraphicsBufferTransferTests.cs` | `69B41522504029816F1F02166C2D3F08C8DFE4BC3EB69746154257C198F79DE8` |
| `tests\TerraFX.Graphics.D3D12.Tests\D3D12DescriptorRangeAllocatorTests.cs` | `570FDF275C79F266AADE79BD39E11EB6C1652B0C1FD21FF5A0718116A3346C33` |
| `tests\TerraFX.Graphics.Vulkan.Tests\VulkanBackendDeviceTests.cs` | `B6F241A38A8FBCAFE921A768A66AFF2F3A2EB1B1DEEB810F5F829793E53B6C16` |
| `tests\TerraFX.Runtime.Tests\Hosting\EngineHostGraphSubmissionTests.cs` | `D23360A025E9AED9A643DBD90AEBAAED2B6810AA3F4373635DBEA3001BCBF1B2` |
| `tests\TerraFX.Runtime.Tests\Hosting\EngineHostOptionsTests.cs` | `DA7B38BA5BBDAF7A27CD5A2C1DF4BEC66DDDC427A5741400A046F76AD6D5FB47` |

Preserved dirty design/schema-authority inputs; these hashes imply no contract approval:

| Input | SHA-256 |
|---|---|
| `docs\design\04-capacity-and-workload-profiles.md` | `A4D86F3FA373A64548435C53582BF5916E29D77F34650F9D161CCEC90E6F0245` |
| `docs\design\12-entities-components-and-queries.md` | `174B2465746DA79CEF560C8506216EF4F99F9D1AABAC184F872C9E6CB0B94E0B` |
| `docs\design\16-scenes-transforms-and-cameras.md` | `B012F42B23CEDE9C7CDB71203D8357CD19E1626823EA25EF3119961292D3698A` |
| `docs\design\30-public-api-review-surface.md` | `15848C82F3858A77A87A0D3EA6FBCC07046EB6E77CA5B21748C72B90EFAB598B` |
