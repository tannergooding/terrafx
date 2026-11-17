# M05 skinned-normal diagnostic delivery

## 1. Decision and scope

**Tentative unblocker for later user review, not an accepted contract, implementation, or qualification result.** Add a copied, typed `SkinnedNormalDiagnostics` array to the existing completed `RenderSubmissionReceipt`. Complete the existing `Renderer.SubmitAsync` task only after the graph fence, diagnostic readback fence, CPU copy, and operation-owned cleanup. Do not introduce a subscription, another public asynchronous operation, a disposable result, or a render-thread wait.

Chapter 20 closes the detection algorithm and requires a bounded compute count, asynchronous readback, and frame/draw tags. It does **not** close the application-visible result, its count membership, or its delivery lifetime. The “specified execution work, not a contract blocker” classification in the original M05 proposal and `m05-m03b-owner-contract-closure.md` section 2.7 is therefore incomplete. This proposal corrects only that delivery/producer boundary; their animation, particles, LOD, picking, binding-use, and deferred-scope decisions remain separate.

Only this new proposal is an edit target. Maintained chapters, existing proposals, production code, tests, and milestone status are unchanged. No build, test, shader-tool execution, native/AOT observation, commit, push, or PR is part of this work.

**Freeze limitation:** two other proposal files appeared and continued changing during analysis. Section 8 records the original and extended input sets and the concurrent mismatches. Do not treat this draft as acceptance of a stable whole-worktree review set.

### Evidence for the missing boundary

Source basenames below identify the complete paths frozen in section 8. These are source-reading observations, not results from executing the tests.

| Observed fact | Evidence and consequence |
|---|---|
| Near-zero detection, dominant-bone fallback, compute counting, and post-fence frame/draw diagnostics are already required. | Chapter 20 section 3, especially lines 49–55; Chapter 17's skinned-normal paragraph. No numerical redesign is needed. |
| Current extraction produces actual static BindPose palettes, not live animation. | `Renderer.Extract.cs:209–285,725–773`; `RenderMeshPalette.cs`; Chapter 20's M03 static-skin boundary. The rectification section 2.5 proposes the later real animated producer; runtime-port proposals do not constitute its implementation. |
| Built-in Mesh preparation selects camera draws and additional directional/spot shadow casters, retains group/palette identity, and shares geometry across passes. | `Renderer.MeshC0Graph.cs:552–711,778–919,1230–1400`; `RenderMeshSnapshot.cs`; `RenderSnapshot3D.cs:74–198`. Pass ordinals and upload-array positions are not stable draw identities. |
| Mesh C0 has real skinned raster variants, but no diagnostic compute producer. | `TerraFX.Runtime.csproj:21–44`; `Renderer.SpriteC0Pipeline.cs:385–477`; `RenderMeshC0ShaderReflection.cs:195–306`; `RenderBuiltInSubmissionResources.cs`. Naming a compute pass without its compiled shader, reflection, resource versions, and native pipeline would not close the producer. |
| The public receipt exposes only submission/fence/presentation sequences and epochs. | Chapter 30 section 7, `RenderSubmissionReceipt.cs`; `Renderer.Submit.cs:777–792`. There is no existing count/result member or request for this output. |
| Existing readback APIs return caller-owned bytes after real transfer completion. | Chapters 17/30; `GraphicsDevice.cs:771–856,1551–1585`; `TextureReadback.cs`; `TextureReadbackTests.cs`. They can carry the **private bytes**, but do not identify or publish a Renderer diagnostic. |
| An asynchronous public method name does not prove a non-stalling Graphics owner. | `ReadbackBufferRequest.Execute` calls the synchronous backend on the Graphics owner (`GraphicsDevice.cs:3051–3138`). D3D12 `ReadbackBuffer` calls `SubmitAndWait` (`D3D12GraphicsBackend.cs:1610–1730`); Vulkan does likewise (`VulkanBackendDevice.Submission.cs:92–145`). Section 5 closes this actual execution seam. |
| General diagnostics can transport text/context, but not a reliable completed count result. | Chapters 28/30 declare `Diagnostic.Context` and copied polling, an 8,192-record ring, coalescing, dropping, and independent error results. A context string could carry a count, but polling cannot distinguish zero, not-yet-produced, coalesced, and evicted per-draw results. It has no operation-completion join. Chapter 29's existing World diagnostic capability is not a general Rendering emitter grant. |
| Public-only rendering and native test patterns already exist. | `RenderPublicConsumerTests.cs:165–249`; both separate consumer projects; Vulkan consumer `Program.cs:620–710`. `RenderSnapshotMeshGpuTests.cs:821–866` checks real static skinning and explicitly refuses/skips the unsupported Vulkan vertex-storage shape. Preserve that refusal; its existence is not a successful diagnostic observation. |

**Conclusion:** the declared `DiagnosticsService` can carry best-effort telemetry, and Graphics readback can carry private bytes. Neither currently closes typed, operation-correlated application delivery. Extending the existing post-completion receipt is smaller than adding a service, task-valued receipt property, callback, or separately retained readback ticket. The receipt's completion can become later because it now joins the required diagnostic transfer; it must never become earlier than its existing GPU/pin boundary.

## 2. Preserved numerical and shader contracts

Preserve Chapter 20 without changing its threshold or substituting a second skinning implementation:

- The skin matrix is `inverseBind * currentBoneModel`; position uses the existing four-weight row-vector blend followed by `instanceWorld`.
- CPU extraction inverts every admitted bone linear transform. A singular/nonfinite palette fails extraction; diagnostics do not turn it into identity, an empty draw, or a zero-count success.
- Blend the four **per-bone inverse-transpose-transformed authored normals**, using the admitted normalized weights, before the instance inverse transpose and final normalization. Do not normalize the individual bone normals first or test the post-instance normal.
- For a finite blended normal, detection is exactly `dot(blendedNormal, blendedNormal) < 1.0e-12f`, not `<=` and not a configurable tolerance. At equality the near-zero count does not increment.
- The fallback is the highest-weight valid bone normal, ties by **lowest joint index**, not influence-slot order. Then apply the existing instance inverse transpose and normalize; do not emit NaN.
- Keep positive-sum weight normalization and in-range joint admission at their current producers. The compute consumer uses the already packed normalized weights; it does not normalize them again.

`MeshC0Vertex.hlsl` also has a defensive nonfinite-blend fallback branch. Preserve it; it does not redefine the finite near-zero threshold. The diagnostic pass records nonfinite evaluation separately as an invalid-result flag and faults delivery rather than calling an invalid evaluation a successful zero near-zero count. This is not permission to admit a nonfinite CPU palette or replace the raster fallback.

Reuse a shared HLSL normal-blend/predicate helper between raster and diagnostic compilation, preserving expression order and the existing raster interface. The diagnostic computes only the required normal predicate; it produces no positions, tangents, deformed vertex buffer, or CPU skinning oracle. Raster stages gain no counter writes or atomics.

### Existing bytes and bindings that must remain unchanged

| Existing operand | Exact retained meaning |
|---|---|
| Mesh vertex record | 96-byte stride: Position at 0, Normal at 12, UV0 at 24, Tangent at 32, Color at 48, float4 joint indices at 64, normalized float4 weights at 80 (`RenderMeshC0VertexBufferBuilder.cs`). Integer-valued joint components keep the existing shader cast. |
| Bone palette entry | 112 bytes: four skin rows at 0/16/32/48 and three normal rows at 64/80/96 (`RenderMeshC0BoneBufferBuilder.cs`). Use the exact captured palette upload, not a later live animation read or a separately recomputed inverse. |
| Object uniform | Existing 192-byte `ObjectGpuData`, with `Metadata` at 176 and `Metadata.x = paletteBase + 1`; zero remains no skin. Do not repurpose its remaining lanes or alter World/PreviousWorld/normal-row offsets. |
| Raster palette binding | ObjectData `b0` and storage `u0, space2` / Vulkan set 2 binding 32; existing reflection is `ReadWrite`. Preserve the existing graph `ReadWriteBuffer` version chain and actual bytecode validation, even though raster code performs no storage writes. |
| Toolchain/layout | Chapter 18's pinned Windows DXC, VS/PS/CS 6.0 and Vulkan 1.3 targets, compiler identity, explicit row packing, coordinate/numerical identities, reflected access, descriptor limits, and backend validation still apply. New compute artifacts are not evidence until actually built and consumed. |

No Vulkan `vertexPipelineStoresAndAtomics` enablement, source-format expansion, compute-produced geometry, sampled-cube promotion, or widening of current PBR/normal-map/material support follows. In particular, adding this diagnostic must not remove the current Vulkan rejection of an unsupported writable vertex-storage signature.

## 3. Smallest public delivery amendment

The following are **proposed declaration additions**, owned by `TerraFX.Rendering` in Runtime, not members that already exist. `System`, `System.Collections.Immutable`, and the existing `RenderSubmissionReceipt` retain their ordinary meanings.

```csharp
public readonly struct RenderSkinnedNormalDiagnostic
{
    internal RenderSkinnedNormalDiagnostic(
        Guid snapshotId,
        Guid worldGuid,
        long tickIndex,
        Guid drawId,
        int testedVertexCount,
        int degenerateVertexCount);

    public int DegenerateVertexCount { get; }
    public Guid DrawId { get; }
    public Guid SnapshotId { get; }
    public int TestedVertexCount { get; }
    public long TickIndex { get; }
    public Guid WorldGuid { get; }
}

// Added get-only member on the existing sealed RenderSubmissionReceipt:
public ImmutableArray<RenderSkinnedNormalDiagnostic> SkinnedNormalDiagnostics { get; }
```

Append an initialized `ImmutableArray<RenderSkinnedNormalDiagnostic>` parameter to the receipt's existing **internal** six-operand constructor. The other six public properties and the public `SubmitAsync` signature remain unchanged. No public factory, options flag, event, generic diagnostic payload, or new public enumeration is needed: the sole result kind is Chapter 20's `DegenerateSkinnedNormal`.

### Issuance, defaults, and membership

- Only Renderer submission completion issues these records and the containing receipt. Graphics returns lower-owned transfer data, never a Rendering result. Internal accessibility does not grant another subsystem semantic issuance authority.
- All three GUIDs are nonempty; `TickIndex >= 0`; `TestedVertexCount > 0`; and `0 <= DegenerateVertexCount <= TestedVertexCount`. The all-zero default struct is invalid and is never published. There are no nullable result fields.
- The array is always initialized. Its records are unique and ascending by RFC-byte `DrawId`; each has the exact submitted snapshot/world/tick. A zero **count inside a record** means a real test of that draw completed.
- Initialized-empty means the submitted plan contains no eligible renderer-owned skinned Mesh draws. It is not a missing/disabled/unimplemented-producer status. Raw custom passes are not implicitly instrumented, even if they use skin-like resources or a built-in-looking name. Extending a `BuildGraph` graph does not exempt its retained built-in draws.
- There is one record for each distinct captured skinned Mesh `DrawId` actually referenced by the compiled built-in Mesh color or shadow passes, with a retained palette, `GroupIndex >= 0`, and `IndexCount > 0`. Deduplicate camera/cascade/spot repetitions. Count neither unselected snapshot draws nor Sprite/custom draws.
- For that draw, test each distinct vertex ordinal referenced by its exact admitted Mesh group's index slice once. Repeated indices do not inflate the count; a vertex shared by two **different logical draw IDs** belongs to both results. This explicitly selects a logical-vertex diagnostic, not triangle-corner, post-transform-cache-invocation, pixel, or visibility/occlusion counting.
- Derive membership independently from the retained group `FirstIndex`/`IndexCount`, selected index format, and admitted vertex count. An ascending unique-ordinal list is sufficient; it performs no skinning. The renderer's existing `DrawId` is `NameUuid(entityPersistentId, "mesh-group/" + groupIndex.ToString("x8", invariant))`, not `DrawIndex`, pass name, material slot, or a newly issued identity.

This closes the current single-geometry Mesh path and its proposed animated palette input without claiming LOD implementation. If a later actual producer supplies multiple geometry selections for one logical draw, its membership must be the union of referenced vertex inputs distinguished by exact geometry/palette generation, not accidentally merged equal ordinals from different streams. That producer remains subject to its existing reviewed LOD contract; this proposal creates no LOD data or capability.

The containing receipt supplies `SubmissionSequence`, original graph `CompletedFence`, and device/surface/swapchain epochs. Together with `SnapshotId` and `DrawId`, this distinguishes resubmission of the same snapshot. Tick alone is not a frame identity. Do not relabel an old result using the latest frame or device. These historical scalar tags confer no live Entity, World-binding, or reconciliation authority.

### Completion, cancellation, failure, and consumer lifetime

| Event | Required contract |
|---|---|
| Successful `SubmitAsync` | Graph work, diagnostic compute, transfer fence, map/CPU copy, row validation, and all operation-scoped accesses have ended. Submission/resource owners have been released or transferred into their normal completion-gated retirement. The receipt and complete copied array are returned together. |
| Original graph fence completes first | This is not yet a completed diagnostic receipt. Keep the task pending through the diagnostic transfer and CPU copy. `CompletedFence` still identifies the original graph fence; the private transfer fence is separately checked, not substituted for it. |
| No eligible built-in skinned draws | Keep the existing submission completion boundary and return initialized-empty; allocate no diagnostic GPU/readback batch. |
| Cancellation before acceptance | Preserve the existing admission token and guard behavior; no accepted diagnostic or graph work survives an ordinary canceled admission. Already accepted lower resource/upload operations still follow their own consumption rules. |
| Cancellation after graph acceptance | Preserve Chapter 30: ignore the submission token for accepted work. Use noncancelable internal completion/cleanup. `operation.WaitAsync(waitToken)` may cancel only that separate observation; it does not make the original task terminal or release storage. |
| Missing shader, unsupported capability, invalid data, allocation/recording failure | Existing contextual exception/fault channel, no partial graph or successful empty diagnostic array. Resource budgets and actual pipeline support are checked before graph acceptance. No test-only counter, CPU fallback, or disabled diagnostic substitutes. |
| Transfer/device failure after graph submission | Fault the original `SubmitAsync`, retaining original backend/native context and actual submission identity. Do not report rollback, zero counts, or a successful submission receipt with missing diagnostics. The image may already have been submitted/presented; failure of result delivery does not undo it. |
| Timeout or uncertain native ownership | Transfer resources, source uses, staging, device, and charges into the existing quarantine/retirement path. A fault is not proof of safe reuse or successful shutdown. |
| Caller disposes snapshot/target, unloads Scene, changes package, or replaces World | Already admitted independent pins and resource uses remain valid through their real consumers. Do not query the replacement World to decode counts. Snapshot disposal does not cancel accepted work. |
| Caller retains receipt indefinitely | The receipt retains only copied scalar records and its immutable array, no snapshot, World/use credit, package pin, pipeline, GPU buffer, native staging, callback, or owner-task closure. It remains readable on any ordinary thread after World/host shutdown. No `Dispose` is required or permitted for this value result. |

An application uses the existing asynchronous submission path and reads the new property; no friend access or diagnostic polling is involved:

```csharp
var operation = renderer.SubmitAsync(snapshot, plan, admissionToken);
var receipt = await operation.WaitAsync(observationToken);

foreach (var value in receipt.SkinnedNormalDiagnostics)
{
    Console.WriteLine(
        $"{value.SnapshotId:D}/{value.DrawId:D}: " +
        $"{value.DegenerateVertexCount}/{value.TestedVertexCount}");
}
```

Here `renderer`, `snapshot`, and `plan` are produced by the real public host/World/extraction/graph path. If the separate observation is canceled, the caller retains/joins the original operation as needed; it must not infer native retirement from the canceled wait. Interactive presentation still requires the existing separate `Surface.WaitForPresentationAsync` sequence observation. Offscreen receipts still have null presentation sequence and zero surface/swapchain epochs.

## 4. Producer and graph path

1. **Capture real inputs.** Use the exact retained snapshot and selected package Mesh/group, packed vertex bytes, normalized weights/joints, palette bytes, palette base, and stable identities already prepared by Mesh C0. BindPose stays BindPose. The later animated path supplies its actual committed current palette through the reviewed preconstruction Animation/cohort extension; neither this pass nor Renderer manufactures animated state.
2. **Prepare the finite draw table and membership.** Filter the actual compiled built-in Mesh pass set, deduplicate draws and referenced vertex ordinals, order records by RFC draw ID, and freeze the row-to-frame/draw table before asynchronous work. Validate all index/palette/checked extent relationships. Map rows only through that retained table after readback.
3. **Compile a real diagnostic shader.** Add one ordinary Runtime HLSL compute input and its transitive include to the existing shader build. Load backend-specific embedded DXIL/SPIR-V through the existing bytecode/independent-lease path, supply actual matching compute reflection, and call `GraphicsDevice.CreatePipelineAsync`. Extend the private built-in submission resource set and recording route; merely invoking a test compute shader is not integration.
4. **Declare exact graph accesses.** For skinned geometry, add Storage usage and `StructuredStride = 96` to its existing DeviceLocal Vertex/CopyDestination buffer, preserving all raster bytes and its 96-byte vertex binding. Interpret the diagnostic structured view as six explicit float4 slots; Normal is `(slot0.w, slot1.x, slot1.y)`, joints are slot4 and weights slot5. Reuse the actual 112-byte palette buffer. No new CPU normal/palette implementation or independently sampled upload is allowed.
5. **Initialize and dispatch.** Use an initialized DeviceLocal Storage/CopySource/CopyDestination result buffer, 16 bytes per draw, plus initialized membership/parameter data. Use one 64-invocation workgroup per draw, each lane striding its unique-ordinal list, followed by bounded group-shared reduction and one row write. It uses no global or vertex atomics. Each dispatch's loop bound is its admitted vertex count, not a mutable GPU append count.
6. **Keep the new compute ABI separate.** The selected four-storage-binding layout is Pass `u0` vertices, `u1` palette, `u2` packed membership, `u3` result rows, and Pass `b0` one uint4 `(resultRow, membershipFirst, membershipCount, paletteBase)`. Native spaces/sets are 3; Vulkan bindings are 32/33/34/35 and 0 respectively. Membership packs four ordinals per uint4 with zero tail lanes; `membershipFirst` is an ordinal-element offset, so element `i` uses packed row `(membershipFirst + i) / 4` and lane `(membershipFirst + i) % 4`. Result row is uint4 `(degenerateCount, testedCount, invalidEvaluation, 0)`. Strides are 96/112/16/16; the 16-byte uniform range has allocation offsets/stride aligned to `lcm(256, device uniform alignment)`. Check actual device limits, all fixed array counts are one, local size is `(64,1,1)`, and compilation/reflection must agree. These are proposed compute-only operands, not changes to the existing raster ABI.
7. **Honor conservative access and ordering.** The current D3D12 storage profile requires `ReadWrite` reflection; declare paired `ReadWriteBuffer` versions for every such initialized binding and bind the returned versions. Although the vertex/palette/membership shader operands are load-only in code, do not lie in reflection to bypass that profile. Chain the resulting vertex/palette versions into later raster passes. The executor supplies actual storage/UAV dependencies, including same-state hazards. Four descriptors and the workgroup are actual admission requirements, not inferred from the minimum profile or HLSL declarations.
8. **Export the result truthfully.** Add an internal readback-export entry consisting of the exact result `GraphBufferVersion` and range to the graph/plan. It is a terminal external Readback side effect, not a public graph API: it keeps the producer alive under culling and excludes the result allocation from alias reuse until transfer completion. This metadata does not currently exist and must be implemented with the producer. Do not assume a materialized transient survives `SubmitAsync` or that a usage flag alone exports it.
9. **Read back inside the existing submission owner.** After actual graph completion, use the materialization map to identify that exact same-epoch result `GpuBuffer`, then await the non-stalling buffer-readback path in section 5 **inside** `Renderer.SubmitAsync`'s existing try/finally. Do not return first or attach a detached continuation. Keep the relevant transient allocation/group and resource use alive through the transfer; the current finally would otherwise dispose them.
10. **Validate and detach.** Require the exact `16 * drawCount` byte extent; checked-decode unsigned shader counts into `int`; verify each tested count against its frozen membership, degenerate count against that count, reserved zero, and no invalid-evaluation flag. An initialized zero row is not a result for a positive-size draw. Construct the typed array, dispose CPU readback/leases, release native/source/plan accesses and the batch reservation, then return the receipt. Only scalar copies escape.

The result row's invalid flag separates a nonfinite evaluation from an ordinary finite near-zero blend. A nonzero invalid flag faults the operation; do not change Chapter 20's threshold to hide it. Shader input validation, bytecode validation, allocation failure, mapping failure, and a mismatched/incomplete row similarly cannot become a zero-filled successful result.

## 5. Required Graphics readback execution seam

Calling the present `ReadbackBufferAsync` after a graph fence would preserve source lifetime, but would still execute `SubmitAndWait` for its **new transfer** on the Graphics owner. Offloading that existing backend method wholesale to `Task.Run` is also wrong: command recording, staging allocation, mapping, and destruction remain owner-affine.

Keep the existing public `GraphicsDevice.ReadbackBufferAsync` signature and owned-buffer result. Replace its internal blocking backend step with the following narrow Graphics-owned split; this is proposed M04 implementation work required by the diagnostic, not a claim that the current backend already supports it:

```csharp
internal interface IGraphicsBackendBufferReadback : IDisposable
{
    long CompletionFence { get; }
    OwnedBuffer<byte> CopyCompleted();
}

// Replaces the blocking buffer-readback operation on IGraphicsBackendDevice:
IGraphicsBackendBufferReadback BeginBufferReadback(
    IGraphicsBackendBuffer source,
    GraphResourceState sourceState,
    long sourceOffset,
    nint byteLength);
```

All types remain in `TerraFX.Graphics`; `OwnedBuffer<byte>` is the existing Core owner. No Graphics signature references Renderer, World, Scene, or Runtime. Concrete objects and constructors remain private to the selected backend.

- `BeginBufferReadback` runs only on the Graphics owner, validates the exact native source, reserves staging/retirement first, records the actual copy/barriers, submits without waiting, and returns a nonnull owner with a positive fence on that same identified device queue timeline. A default/null owner or zero fence is never success. Partial unsubmitted construction unwinds; uncertain submission retains/quarantines.
- Graphics keeps the independent source-use lease and this backend owner in its admitted request. Reserve at most 256 outstanding buffer-readback completion entries before ordinary request acceptance, matching the existing ordinary-channel capacity; keep that reservation through terminal cleanup, not just dequeue. Full completion capacity applies cancellable pre-admission backpressure, not an unbounded pending list. The Renderer lane below admits only one such batch itself.
- Reuse the existing graph-completion pattern: an independently rooted, bounded fence wait may use the backend's established wait-only path off the Graphics owner; notification returns to that owner. Check a fresh native completed value and native errors, not just `Task.IsCompleted`. Do not wait, spin, map early, or run user continuations inside the owner loop.
- `CopyCompleted` is owner-thread-only, one successful call, and only after Graphics has freshly verified `completed >= CompletionFence` for that exact backend/device. It performs required map/invalidate and CPU copy, returning a nonnull, exact owned byte range. It never submits or waits for GPU work. Premature/repeated calls are misuse, not zero-content results.
- `Dispose` is owner-thread-only, closes further use, and releases staging only after actual completion or a proven unsubmitted abort. If completion/release is uncertain, it transfers the still-charged owner into the existing rooted quarantine rather than freeing it. It is idempotent after release/handoff. Copy failure still owns cleanup; no new public disposal authority is introduced.
- Update source last-use/state and native staging-range retirement with the actual transfer fence. Source and staging credits survive observation cancellation, device loss, and failure exactly as Chapters 17/30 require. Complete the public Task only after actual consumption stops or ownership has safely transferred to fault quarantine, with asynchronous continuations outside locks.

This changes only the necessary private buffer-transfer execution path, not public transfer signatures, texture formats/pitches, device creation, queue topology, or presentation. Both native backends must actually implement and qualify it. A synchronous transfer invoked behind an asynchronous façade does not meet the new producer requirement.

## 6. Bounded ownership and backpressure

Use the existing built-in submission/residency lifetime rather than a new persistent service. Mesh graphs already travel through `BuiltInSpriteState`; `Renderer.Submit.cs:148–156,793–854` holds its submission lease through finally. `GpuResidencyService.StopAsync` joins those active leases; normal stop and recovery use that drain (`EngineHost.cs:1287–1305`; `EngineHost.GraphicsRecovery.cs:172–198`).

Add one private diagnostic-batch reservation per native Renderer/device epoch, acquired after identity/usage validation and pre-cancellation but **before** its first asynchronous wait/diagnostic staging allocation. It has no accepted waiting queue: a second diagnostic-bearing submission while occupied faults before graph acceptance with contextual `InvalidOperationException`; the caller may await the earlier original operation and retry. Non-diagnostic submissions keep their current behavior. This is an explicit proposed admission-capacity outcome, not a silent frame drop or new public Try method.

The current Sprite submission tail serializes active work but can retain multiple waiting tasks; do not cite serialization as proof of bounded diagnostic buffering. The additional reservation bounds only this newly introduced diagnostic lane and is held through readback/copy/cleanup. Keep its ownership inside the existing active submission lease so host drain includes it; recovery cannot rebind it to a new device.

| Storage/interest | Bound and end of ownership |
|---|---|
| Frozen per-plan diagnostic metadata/membership/scratch | At most 131,072 result rows, the existing hard logical-draw ceiling. Propose a private 32-MiB additional CPU preparation ceiling per compiled diagnostic plan, accounting actual array/list capacity, membership construction scratch, and coexistence before allocating. It bounds metadata plus index-derived membership independently of an asset's possible size; excess refuses the whole diagnostic-bearing graph, not a prefix. Immutable plan storage follows the existing caller-retained graph/plan lifetime. |
| Admitted native batch | One per Renderer/epoch, no completed-result queue or detached producer. Reserve all dispatch/resource/retirement requirements before graph acceptance. Palette limit remains 2,048 entries, not multiplied by cameras or diagnostic passes. |
| GPU result and CPU raw readback | Exactly 16 bytes per row, at most 2 MiB each. Native staging uses queried alignment/footprint and actual budget charges, not the logical byte count as an allocation-size claim. Result/export lifetimes cannot alias while native work can read/write them. |
| Incremental CPU completion storage | Propose a private 16-MiB ceiling per active batch for the typed array, raw CPU readback, decode workspace, and actual object/array overhead. The six scalar fields represent 64 bytes per row before runtime overhead; the maximum nominal array payload is 8 MiB, plus at most 2 MiB raw rows. Charge actual sizes/coexistence; refuse before submit if that implementation cannot fit the ceiling. This is not a shader/CLR-layout promise. |
| GPU input/membership/counter/scratch owners | Charge normal existing device allocation, transient subbudget, heap availability, and pre-reserved retirement capacity. The above CPU ceilings add no GPU allowance. Default 512-MiB owned / 128-MiB transient limits and actual native allocation-info checks remain. |
| Snapshot/package/current palette pins | Existing independent submission pins last through their actual consumers. No async escape borrows live ECS or a current-animation getter. Release the submission's pins before returning a completed receipt. |
| Completed receipt | At most one immutable record per eligible draw; caller-owned CPU value retention only. No engine replay/cache/history of results and no continuing batch charge after detachment. Caller-retained receipts/plans are not an unbounded owner queue. |

The private CPU ceilings are proposed finite allocation/admission policies, not measured performance claims or new user-configurable budgets. They bound the extra diagnostic path without changing World diagnostic-ring allowances or M03-B use/accounting envelopes. Capacity/byte failure uses the existing contextual failure channel (`OutOfMemoryException` for unavailable allocation budget), retains previously completed output only with the existing explicit failed-frame outcome, and publishes no partial counts.

On any path, release independent safe owners even if another cleanup fails, retain uncertain native owners and charges, and preserve the original failure plus cleanup context. Do not release the batch slot and continue admitting into a still-consuming untracked operation. After a successful quarantine handoff, device-loss/retirement state must still prevent unsafe new admission. A host timeout, frame-slot wrap, canceled wait, Scene unload, or receipt collection is not a GPU fence.

No `WorldGuidUse` or live-binding authority is required merely to retain these historical scalar results. That is different from the proposed picking operation's later live-identity reconciliation. If another consumer independently retains active World authority, its existing M03-B use contract still applies; this diagnostic is not an exemption or substitute consumer.

## 7. Implementation and evidence boundaries

These are required observations for later implementation, not acceptance IDs, test results, or permission to start dependent native work now.

| Unit | Producer prerequisites and evidence boundary |
|---|---|
| M05 delivery/producer implementation | After owner review of this exact output contract, implement the typed receipt addition, frozen membership, finite reservations, real compute shader/reflection/recording/export, completion join, and cleanup together. Managed tests can establish issuance/default/empty-versus-zero, identity/membership, budgets, refusal, cancellation, and lifetime logic only. Existing raster ABI and BindPose behavior remain. |
| M04 graph/device/readback execution | Actual device/profile, compiled backend compute pipeline, resource usages/version barriers, non-aliasing export, non-stalling split readback, real fences/map/copy and failure retention on D3D12 and Vulkan. A declaration, fake native port, or managed buffer test is not this observation. Existing unsupported capability results remain explicit gaps, not passes. |
| Static zero-count path | A real package-backed static skinned Scene, committed World, `Extract` → built-in graph → compile → submit → new receipt array. Expect a present tested draw with count zero, not an empty array. This can establish the zero producer/transfer path once implemented, without live animation; it cannot establish a nonzero diagnostic or dominant-bone choice. Nothing in today's source establishes that this new path has run. |
| Animated nonzero path | Requires the actual reviewed Animation state/registration/cohort/asset producer and committed animated extraction. Use the distinguishing fixture below through the same ordinary built-in graph and readback on each supported backend. No fabricated snapshot, injected palette, CPU count, or alternative skinning implementation qualifies it. |
| Public consumer | Extend the existing separate, public-only consumer pattern to read the typed result after awaiting `SubmitAsync`. It must load package-backed Scene/Mesh/Skeleton/Clip content and use real public World/controller/Step/Extract roots; no internal draw-array or shader-test access. The current static GLB/OBJ adapters do not produce animated content. Native-authored fixtures do not certify imported animation. |
| NativeAOT/public qualification | Publish and execute the real consumer separately per required OS/backend/target under Chapters 29/35 and `Building.Consumers.md`. Managed consumer execution, source compatibility, shader compilation, and static pixel readback are distinct evidence. AR01/AR02/AR08/AR12 obligations retain their existing scope; EN060's normal/palette slice does not close its M06/M07 blocked-body observation or complete Chapter 32. |

**Literal distinguishing input:** an admitted native package has two joints with identity bind transforms/inverse binds. Joint 0 remains identity; an actual Clip supplies joint 1's Y half-turn quaternion `(0,1,0,0)` at an interior Step sample, avoiding a looping-duration wrap. Use no extracted root-motion replacement for that rotation. A selected three-vertex group has +X authored normals: vertex 0 has joint lanes `(1,0,0,0)` and weights `(0.5,0.5,0,0)`; vertices 1/2 are root-only. The first weighted normal is zero, while the other two are +X. Expect `TestedVertexCount = 3`, `DegenerateVertexCount = 1`, the original snapshot/draw tags, and joint 0's +X fallback despite joint 1 being the first influence lane. Lighting must distinguish +X from an incorrect -X fallback; finite/non-clear pixels alone do not prove the tie rule. Derive these expectations from the literal transforms, not production output.

Focused variants retain the same owner path: repeated triangle indices count a shared vertex once; two logical groups receive separate rows; camera/shadow repetitions do not multiply a row; zero versus initialized-empty differs; and a later frame/device cannot overwrite an older result's identity. Exercise cancellation of only a separate wait, snapshot/target disposal after admission, package/World replacement, budget/concurrent-capacity refusal, missing compute capability, incomplete/invalid readback rows, native transfer failure, and shutdown/recovery with pending work. Synthetic lifetime/failure checks qualify only the behavior exercised; do not label them real driver-loss, native timeout, or NativeAOT evidence.

Chapter 35's sequencing remains unchanged: contract-complete M05 implementation, actual M03-B/Animation input producers, M04 native execution, public consumer execution, AOT qualification, and complete milestone acceptance are separate gates. Missing animated production blocks the nonzero witness, not unrelated dependency-ready M05 work. M08/M09 physical-input, real loss/timeout/uncertain-retirement and release gates are not discharged here.

## 8. Frozen input set and verification

The repository root is the current worktree. Initial HEAD is `31f156a51ccc462ae9c5cc8d4e87e00c0054dec4`; the index initially matched HEAD. File hashes are SHA-256 of exact on-disk bytes, including modified and untracked files, not hashes of HEAD or normalized text. The table includes direct reads and explicitly pinned owner/build context; `dirty-only` rows preserve the surrounding dirty state and are not reviewed/accepted implementation evidence.

Initial status had 38 entries: 30 tracked, unstaged modifications and eight untracked files. Its canonical status SHA-256 is `77A8CCFBC66B7892BAF0C29A15CF6B7FFBBAA21F9F1F60602F838250548D02E1`. Canonicalization means ordinally sort complete `git status --porcelain=v2 --untracked-files=all` lines, join with LF including one final LF, encode UTF-8 without BOM, then hash. Git's porcelain paths remain unmodified in that digest.

The extended capture at `2026-11-16T02:51:57.2506361Z` contains 122 files: 92 direct/pinned inputs and 30 dirty-only files. Nine additional code-intelligence/search-context files were subsequently hash-frozen and added below, making the final manifest 131 files: 101 direct/pinned inputs and 30 dirty-only files. The extended status has 40 entries and SHA-256 `77E407CCCC8642A6B992F690F736AB3160A0C502B040BEE3CD0BF0DF2B579C35`. Two concurrent additions explain the original status mismatch:

- `docs\proposals\m05-owner-contract-closure-runtime-ports-addendum.md`: inspected as additional context; it concerns typed restore-source/asset joins, not this GPU output. It is not substituted for an accepted animated producer.
- `docs\proposals\m03b-scene-preparation-handoff-closure.md`: unrelated dirty-only context, not a dependency of this proposal.

Both additions subsequently changed bytes; preserve the extended-capture hashes below rather than silently advancing them and claiming the initial review set was stable. Original owner/source inputs and the pre-existing M05 proposal dependencies are independently checked against their frozen bytes.

`C` = tracked clean; `M` = tracked `.M` with index equal to HEAD; `U` = untracked. This new output file was absent initially and is excluded from all input/status digests. No generated binaries, tool outputs, previous TRX, or prior native/AOT result is an input to an execution claim.

| Path | State | Bytes | SHA-256 | Scope |
|---|---|---:|---|---|
| `.editorconfig` | C | 26884 | `342683DE2B1C262624546F312F7D8E2078F89185ADD39F43F1FB5013376FD553` | input |
| `.github\workflows\ci.yml` | C | 5643 | `03EFEA672CD52E9EF053E9CE0285E7CA02C6CE6FB0D21268ADE4255ED5A39D58` | input |
| `Directory.Build.props` | C | 4236 | `770EE4B2A422861A6F23F0D39DD0ECA361A81CB87E64EBAC4E12170F71231D7A` | input |
| `Directory.Build.targets` | C | 1877 | `AEEC1D08E8037470239035812351C4045124607D67A354D482112F4D642D4105` | input |
| `Directory.Packages.props` | C | 1585 | `A5066BF0F3B0EFD28552B484A01A39726C2CE47584B3FBC8C077790C4F4AD57A` | input |
| `docs\design\02-dotnet-api-memory-and-ownership.md` | C | 37545 | `40F913A50434209E09941C22625A6D0722202BAB97EBAC0E153C1EA3368E2291` | input |
| `docs\design\03-shared-conventions-and-clocks.md` | C | 25302 | `B4208D3037C8C90EED56627F63DC04ACFFA80FF16B5FF3FF6936CD89615C08C5` | input |
| `docs\design\04-capacity-and-workload-profiles.md` | M | 27808 | `A4D86F3FA373A64548435C53582BF5916E29D77F34650F9D161CCEC90E6F0245` | input |
| `docs\design\12-entities-components-and-queries.md` | M | 72195 | `174B2465746DA79CEF560C8506216EF4F99F9D1AABAC184F872C9E6CB0B94E0B` | dirty-only |
| `docs\design\13-jobs-and-frame-pipeline.md` | C | 46691 | `0D7806B03A1D019215EB5C3912F10200A078F9B00A71A206FB979E93BBD18CDE` | input |
| `docs\design\16-scenes-transforms-and-cameras.md` | M | 49280 | `B012F42B23CEDE9C7CDB71203D8357CD19E1626823EA25EF3119961292D3698A` | input |
| `docs\design\17-graphics-device-and-resources.md` | C | 40823 | `8157E7DA5472B9AABD891A05E84F2B997487A5C5E3FF67CB589681747D628718` | input |
| `docs\design\18-shaders-and-material-pipeline.md` | C | 18074 | `AF20911D9D06BFB58F6C771511181F59347C4659129CFD5FC8CF622ED191C0D7` | input |
| `docs\design\19-render-graph-synchronization-and-presentation.md` | C | 26572 | `AB673DEDBD85B7A161C5D7DC871BF5B486911DC27EC4279AC43827B005241B8B` | input |
| `docs\design\20-animation-and-deformation.md` | C | 12950 | `BBEF0A36E0832C2BD0D54A07E479917DB923671B64F7176369F8F57A8F1618C5` | input |
| `docs\design\21-2d-and-3d-rendering.md` | C | 25589 | `989A6183186592F165FA9614CC19A8FED63826E2A51170CE55FBFFFCA8116ED2` | input |
| `docs\design\28-diagnostics-performance-and-validation.md` | C | 25387 | `C9260CD18A905E087DFC47D07AF67BD2477996232DA773B69A0E4C3B7A64D90F` | input |
| `docs\design\29-project-setup-build-and-deployment.md` | C | 53322 | `3B55A013EEC327ECD648ACBD5B53717DD218AD288E8047AC21277609EFA2F47B` | input |
| `docs\design\30-public-api-review-surface.md` | M | 485241 | `15848C82F3858A77A87A0D3EA6FBCC07046EB6E77CA5B21748C72B90EFAB598B` | input |
| `docs\design\31-public-api-consumer-guide.md` | C | 20402 | `EB725CB14D32754611B15B87870B85758600C42100510B25B13B730AE61A209B` | input |
| `docs\design\32-end-to-end-integration-scenarios.md` | C | 21241 | `6AF2211CB79FECA449D0F701752F86B8D093162AD75673770B0C0DE0F49BA4FC` | input |
| `docs\design\34-engine-acceptance-cases.md` | C | 69900 | `532A68F1717DC05901DFC4537204E65B31B28EE46326F64F5A1CD7834332AA41` | input |
| `docs\design\35-implementation-roadmap.md` | C | 30557 | `24EEC6E92B7AD904D552F290EF748FFC8FCE13B6B94A89EBD9F39487D4584D87` | input |
| `docs\project\Building.Consumers.md` | C | 3455 | `8AF46079A9E11E01578CC9B76B695801B585F58148DF36418EE5506CA4006442` | input |
| `docs\project\Building.Graphics.md` | C | 2997 | `8227D03FEF2FD5709AB61FF70318EB5F18A717F1D0A8DC5432F6C765D25BADD3` | input |
| `docs\project\Building.md` | C | 4652 | `F53AB58B89EEAE7E17B70970075BE9322A936D443BB6D5239C3238E594DEABC6` | input |
| `docs\project\Roadmap.md` | C | 7228 | `C4FBE82155A6D82FFD2D13AD2F18056D938738C4FA5A66760C4877E24ED001C7` | input |
| `docs\proposals\m03b-scene-preparation-budget-accounting.md` | U | 79820 | `3F8E4F0315C6CC2933FC982F64D5E94B9070EB1876A74B0688118BB17AFF5A62` | dirty-only |
| `docs\proposals\m03b-scene-preparation-handoff-closure.md` | U | 58662 | `9325D77A1113A160A1CF3799E9EBCB834C1AB1C946965F1D53B3A358F41A43AB` | dirty-only |
| `docs\proposals\m03b-scene-preparation-workspace-closure.md` | U | 65878 | `9D7280A89C5BB75890ADAF65E26F8C80766B8687EE0608623D0F97D519BE32C3` | dirty-only |
| `docs\proposals\m05-m03b-owner-contract-closure.md` | C | 57319 | `FC290975441F950EFD4621A0A2DCB32E71A022228C48211CDDDF5F5FB6A70585` | input |
| `docs\proposals\m05-owner-contract-closure-rectification-addendum.md` | U | 31680 | `C8DE6581D5CB126D18C294FC9EB22A01459B2D6BFC3AD933ECB1652582634F4A` | input |
| `docs\proposals\m05-owner-contract-closure-rectification.md` | U | 109036 | `67134F02B92D316524848ECB8E0C917EF868DA3D2EDA5FC581DBAA25B1825632` | input |
| `docs\proposals\m05-owner-contract-closure-runtime-ports-addendum.md` | U | 32549 | `71DED88B6EF5FFDF08C24EEE90E64E9CDFC82F05D1433B86B06CECA328BA6B22` | input |
| `docs\proposals\m05-owner-contract-closure-runtime-ports.md` | U | 97516 | `605CF1D92DC362A8B73EA9BDA3A5467757942C268712F484AF53D9A9159D53E3` | input |
| `docs\proposals\m05-owner-contract-closure.md` | U | 48299 | `6FEE16009BF0D1FB529E68C8DF1E3293A852468E71D9A291984D8A64A9A62BC8` | input |
| `global.json` | C | 179 | `1BCE01871321C2DF82E887DA428CE5C121A6C6541FE65017FE3D249BE47E190C` | input |
| `sources\TerraFX.Graphics.D3D12\D3D12GraphicsBackend.cs` | C | 288148 | `FB74F3CF8097695B0CB08D5C68F4262C253F6751062F2B3E403EE42ABB31977C` | input |
| `sources\TerraFX.Graphics.Vulkan\VulkanBackendDevice.cs` | C | 64085 | `3DCF4AAE7F0F346EDC6C1AA0339C8E063EC53CE6FB285F978522EF904DB90278` | input |
| `sources\TerraFX.Graphics.Vulkan\VulkanBackendDevice.BackendPort.cs` | C | 6605 | `39749314ED013E0462E77F1FAA0961E0BBEDE9E8802F061FB9BDAF17A200D1BC` | input |
| `sources\TerraFX.Graphics.Vulkan\VulkanBackendDevice.Buffers.cs` | C | 19646 | `EB28B97A19EA0292A1B3BE6C61DD8BC0DB4ED7E383712B7B9D0A76D91C14FA46` | input |
| `sources\TerraFX.Graphics.Vulkan\VulkanBackendDevice.ImageTransfers.cs` | C | 15405 | `48550705B22A33C5E6E151683D2A5EC75192D9304231422BE975E367068726D4` | input |
| `sources\TerraFX.Graphics.Vulkan\VulkanBackendDevice.Submission.cs` | C | 29505 | `969152814B34BF22A3C12C275A843E07FBBFDEC97C254EBFBE0B18A701881926` | input |
| `sources\TerraFX.Graphics.Vulkan\VulkanBackendDevice.Surfaces.cs` | C | 3555 | `8296FA3564E5F6DC2EA59EAC088B3E68E1323494415A7854382304DC9450D132` | input |
| `sources\TerraFX.Graphics.Vulkan\VulkanBackendSurface.cs` | C | 58718 | `8DE9769E90327B595B08E4A5686D851F382020DFD0FB37E379DB20E849C07BFF` | input |
| `sources\TerraFX.Graphics.Vulkan\VulkanGraphicsBackend.cs` | C | 5946 | `20A059C984885A43395DD898A4BA0729F8F28D54A966820177050C49C828A432` | input |
| `sources\TerraFX.Graphics\GraphicsDevice.cs` | C | 145392 | `5D919B27A1BB8477E77236E9D0670B3687820720267FCE547249B550C0BECECA` | input |
| `sources\TerraFX.Graphics\GraphicsDevice.SubmitGraph.cs` | C | 18611 | `A85B4857B87F7487E213E23F8C431E9F8D0D16DFE3394E2E97C22CDEB37C6D93` | input |
| `sources\TerraFX.Graphics\GraphicsGraphSubmission.cs` | C | 131205 | `E691A06893DCA483C262B1B707519F5176161E33D877C0006F96022F7C7CD378` | input |
| `sources\TerraFX.Graphics\GraphicsGraphSubmissionResult.cs` | C | 397 | `72D90BE0F3AB75C515C65A1C921DF84035678A56515AF029F7A3F1CA19372023` | input |
| `sources\TerraFX.Graphics\GraphicsOwnerRequestQueue.cs` | C | 7826 | `E26DD7962CF13A7471128A8D27A2BD6DA9F06C75A4F397A3A6D56291A8511143` | input |
| `sources\TerraFX.Graphics\GraphicsResourceOwner.cs` | C | 17503 | `C28BBB51E3FD3140472017DE70F8718B60296335AA8959EEEC54420073BF9846` | input |
| `sources\TerraFX.Graphics\IGraphicsBackendDevice.cs` | C | 3842 | `737EB0C1981227C8A242C91396864D49FB2587329C70867A5D8961ED92A75B26` | input |
| `sources\TerraFX.Graphics\TextureReadback.cs` | C | 4691 | `21CBBCE2836D524A27E9494348435D78E7AB11547FE3B4A308C6FB34F1F5A8BE` | input |
| `sources\TerraFX.Runtime\Animation\AnimationClipAssetBinding.cs` | M | 8090 | `E09FCA988BA58127B365D8F4D7EB0C22CA3FD17E07E8342879544FB81BBD4DB1` | dirty-only |
| `sources\TerraFX.Runtime\Entities\World.cs` | M | 109302 | `AD884C9DF4F9B48DB4751518237535BC11A67820EA1F96FC66CAE850935A7B31` | dirty-only |
| `sources\TerraFX.Runtime\Entities\WorldAdmissionDomain.cs` | M | 127319 | `1682BCDF2B478EAE6F94DCA331FB7E05ED5B5357A81B36D06796E247D75886D6` | dirty-only |
| `sources\TerraFX.Runtime\Entities\WorldDomainReservation.cs` | M | 3708 | `EA012B9B40ACEDAA820885EAFE27DCD11DF55F842B68F2849785DAB756E8E3BC` | dirty-only |
| `sources\TerraFX.Runtime\Entities\WorldGuidRegistry.cs` | M | 80738 | `470EC745500181A57D2E0625B3A93EE5E51A899E046C017B3DD490FDB02BE739` | dirty-only |
| `sources\TerraFX.Runtime\Entities\WorldGuidRetirementPermit.cs` | M | 3501 | `1849EAE697239482076F7EC3A1EA02DD6B3348094DAD6CB63668BF57FDB20529` | dirty-only |
| `sources\TerraFX.Runtime\Entities\WorldGuidUse.cs` | U | 880 | `A6120452989F29EC2EF9C2B90E7479CC6635B9B17848C6C683063B3F99D36D22` | dirty-only |
| `sources\TerraFX.Runtime\Entities\WorldGuidUseControl.cs` | U | 13753 | `1931CD2B5DFD81A3F0CD8A4E6F3D09C5D9B006B2E29817F5C295C77F78202B68` | dirty-only |
| `sources\TerraFX.Runtime\Entities\WorldLifetimeControl.cs` | M | 15251 | `B1A9ADD0A1D68CAF4C385973CA4E2602C68E19E46078AA3CB09614848C09A634` | dirty-only |
| `sources\TerraFX.Runtime\Entities\WorldSceneStorageReservation.cs` | M | 1673 | `A6A487ABAE56A5512976CEFB5D342C37B414BB4F8615B1A124692C9696C673E5` | dirty-only |
| `sources\TerraFX.Runtime\Hosting\EngineHost.cs` | C | 68222 | `A3FB2415E31CB7B717F2AD382E1FACF874B6AC392A469B6E71BA198DF6A11743` | input |
| `sources\TerraFX.Runtime\Hosting\EngineHost.GraphicsRecovery.cs` | C | 17454 | `CC03DB8E1296B8A1C4CD1FAAC37F33BF4455D424C3EE66C6EC5DFE481B89B8D6` | input |
| `sources\TerraFX.Runtime\Hosting\EngineHost.Windows.cs` | C | 11461 | `775E310059F9D0D1E334CB3A324DB0DBAF6CECBF94A29B59C0627EB14EB9EE55` | input |
| `sources\TerraFX.Runtime\Hosting\EngineHostOptions.cs` | C | 9311 | `603457D0A16ED2163600849FCA88E20A098D07CAE962DD152F5A737BF6D3A87B` | input |
| `sources\TerraFX.Runtime\Rendering\GpuResidencyService.cs` | C | 22541 | `7CE97B4E5711B451C1E7EE190C6BF7EC6AC15420625A82EC1E78C0AA9479777C` | input |
| `sources\TerraFX.Runtime\Rendering\RenderBuiltInSubmissionResources.cs` | C | 3907 | `E03E3DA4EA526DC08EBBA190B715D91A44EE21A4CA8BDD2D3A55962A7316B9CC` | input |
| `sources\TerraFX.Runtime\Rendering\Renderer.BuildGraph.cs` | C | 6634 | `552A7F939E01CA189DB0EB3F759F7E97ADC7BD2FF0005391CDFD899860A09560` | input |
| `sources\TerraFX.Runtime\Rendering\Renderer.CpuExtractionSupport.cs` | M | 8065 | `EAF1F94AFF0886B3FA30AF97BD7F83180A38AB744D4241D0CDD275A055EADB55` | input |
| `sources\TerraFX.Runtime\Rendering\Renderer.Extract.cs` | C | 47125 | `5D3B4C9D739316542629EA7A40B60D8558A54C50B6347441985A77E51F8CA048` | input |
| `sources\TerraFX.Runtime\Rendering\Renderer.GpuResidency.cs` | C | 5820 | `3C0D8B8DA3B24ADB7100B3FFAFE0C5AA88EE6F1FF39BC8EC451B5770699374FE` | input |
| `sources\TerraFX.Runtime\Rendering\Renderer.MeshC0Graph.cs` | C | 69921 | `CFC945F6071785777A6728286DD8E488993EAC3884E2FCFB2959C1004272C8EE` | input |
| `sources\TerraFX.Runtime\Rendering\Renderer.SpriteC0Pipeline.cs` | C | 22867 | `6849A2F941C4998A88DC8E72E97FD564AF9C913E02D5CD62620AEBA42159CBF3` | input |
| `sources\TerraFX.Runtime\Rendering\Renderer.Submit.cs` | C | 40287 | `DBBA4C338D7178A0758B50738343C04D70DBD1DA8F8FDF7FD72277765828E680` | input |
| `sources\TerraFX.Runtime\Rendering\RenderGraphPlan.Submission.cs` | C | 9286 | `8C6FD0D587B42E487141D43F60F54504A19E6F5F6F1C86394CA5C81D73CA3A02` | input |
| `sources\TerraFX.Runtime\Rendering\RenderMeshC0BoneBufferBuilder.cs` | C | 4519 | `12BCD89A4FE984AD0C691E70FD4352103A26734C3FD4267815D795683B52D72C` | input |
| `sources\TerraFX.Runtime\Rendering\RenderMeshC0GraphPass.cs` | C | 14441 | `45E4998AB15215A9D740DE4D1C33172136E54A6AB442E257B44825506FEC1499` | input |
| `sources\TerraFX.Runtime\Rendering\RenderMeshC0ShaderReflection.cs` | C | 16499 | `08BD0D4448639AB2BCC64C85ED3EAD35A8CF3F481520ABD74FC4D1DF89D7318D` | input |
| `sources\TerraFX.Runtime\Rendering\RenderMeshC0ShadowGraphPass.cs` | C | 7304 | `88BF5564F8017F1CEA693D13AD1D4B85D201B8E27968E53E2A6A8C5E0CEAA4D2` | input |
| `sources\TerraFX.Runtime\Rendering\RenderMeshC0ShadowShaderReflection.cs` | C | 7199 | `1CA3B366D3D213E0D539A4B634F579F4B5FF284EC167D96EEFB2B879F77A9133` | input |
| `sources\TerraFX.Runtime\Rendering\RenderMeshC0VertexBufferBuilder.cs` | C | 3835 | `8F0B65D1F7DBFFFF80638C114FA7FA3879CDCC5C8683F2371DEA69F16B89B6EA` | input |
| `sources\TerraFX.Runtime\Rendering\RenderMeshGroup.cs` | C | 290 | `4CA21EB075506DD48AD42A04E90AB455F50DCB1AB28635C5F7CBCE3F6A89034A` | input |
| `sources\TerraFX.Runtime\Rendering\RenderMeshPalette.cs` | C | 414 | `131D84C52999E8E7902832172F57955E0EDD8BD5B76F63F1DF56C9276C3722A6` | input |
| `sources\TerraFX.Runtime\Rendering\RenderMeshSnapshot.cs` | C | 697 | `9003FA81C790BB8CF395CD327063C44EC1B25A14D99875E64AA148BDF0663D05` | input |
| `sources\TerraFX.Runtime\Rendering\RenderSceneAssetBundle.cs` | M | 34084 | `4FD3F20576AD328BD3E9958E40EF8D475451BDCBFC5122C3BA7E599A0EA8361A` | input |
| `sources\TerraFX.Runtime\Rendering\RenderSnapshot.cs` | C | 14726 | `733C8A58053977A349742540639FC55AF0CD299DB33C7394D1DC999A17D30AF0` | input |
| `sources\TerraFX.Runtime\Rendering\RenderSnapshot3D.cs` | C | 10111 | `5C6044F77B45C89A6C6C00F2B4D393C12FB426B7CEDAA019D449B0E45D5191DC` | input |
| `sources\TerraFX.Runtime\Rendering\RenderSubmissionReceipt.cs` | C | 2275 | `BDFA23FB56EC678171E7B2A6BDDFB35C0A5C2FA21DC5ABECE7A7839A80FBC2D2` | input |
| `sources\TerraFX.Runtime\Scenes\ISceneExpansionResourcePreparer.cs` | M | 704 | `67AE936843F6329D95D12B784A4686A8B75B2CCEB3932159E56BDD24F21CA423` | dirty-only |
| `sources\TerraFX.Runtime\Scenes\PreparedScene.cs` | M | 5574 | `E4837CEB4F9E8A62A3ABB6A50F6668207AB431D7FDA5896E92BA0FD22A5BFB29` | dirty-only |
| `sources\TerraFX.Runtime\Scenes\SceneActivationRequest.cs` | M | 11606 | `7EA293C13FB20992E04B3BC4FB7484A2708337ABDE759F86CDA609DAE030C16D` | dirty-only |
| `sources\TerraFX.Runtime\Scenes\SceneExpansionPlan.cs` | M | 20836 | `8FFCDE62282341B8C5E782D5262C0917002D1B48E35A5098CC9B4B360BEEFAE9` | dirty-only |
| `sources\TerraFX.Runtime\Scenes\SceneLoader.cs` | M | 19174 | `2E3F6C347AA01B5914980064CDBF7A2A0414DCD74C31A5223365F4DD4D15FEBC` | dirty-only |
| `sources\TerraFX.Runtime\Scenes\SceneLoadOptions.cs` | M | 5009 | `E95382B5F26B7DD398C34BA0B4F20A6A0E76BF9F392B5B3B4BAD88AD1F898A80` | dirty-only |
| `sources\TerraFX.Runtime\Scenes\SceneLoadOptionsSnapshot.cs` | M | 564 | `004B203DC84037B76F854DDE1B47C78D570A8085B6A9B48EE6423E27496223E9` | dirty-only |
| `sources\TerraFX.Runtime\Scenes\ScenePrefabExpansionPlanner.cs` | M | 100383 | `D8D807E1925A5CF2538A41076CA3606A7FE3F31005B452429F20C99C5612CA27` | dirty-only |
| `sources\TerraFX.Runtime\Shaders\MeshC0MaskPixel.hlsl` | C | 57 | `BCCAB11746C2E5FD87C0FE388554F55967A9E3793BE94FDC45CC755A564F763E` | input |
| `sources\TerraFX.Runtime\Shaders\MeshC0Pixel.hlsl` | C | 15585 | `B2BEBCBD46970D567A03099BC4B9EEF21629D2ACC2E094E3814C2D415FE4F951` | input |
| `sources\TerraFX.Runtime\Shaders\MeshC0ShadowPixel.hlsl` | C | 555 | `57F9ECD207DAA0E0B79F8745EA6B77021A617BFD08A3D9DE00AF40DBF2530C60` | input |
| `sources\TerraFX.Runtime\Shaders\MeshC0ShadowVertex.hlsl` | C | 4576 | `ED2DF6F940C5A3634ACE18EAFCB3C5946F619EF27334FFF5084852C32A54B531` | input |
| `sources\TerraFX.Runtime\Shaders\MeshC0SkinnedShadowVertex.hlsl` | C | 61 | `350FAE3A4B17F77379975AFAAA3C2C415BEAAEE4BA48833F9283F99C70B82423` | input |
| `sources\TerraFX.Runtime\Shaders\MeshC0SkinnedTexturedShadowVertex.hlsl` | C | 88 | `8E9A4ABB755C54C2118DF50749636E5FB33F230E7CA7FBA32336DF57BE8D6CB3` | input |
| `sources\TerraFX.Runtime\Shaders\MeshC0SkinnedTexturedVertex.hlsl` | C | 82 | `E3D74FE5FB70A20C7291057F3F81B23203A34BD949AC245FCF925BE82F27CDFE` | input |
| `sources\TerraFX.Runtime\Shaders\MeshC0SkinnedVertex.hlsl` | C | 55 | `6D9BA2A5287D55AA42E11234874775B95BE8E72A8B8D780FCCC004BE166DEFF4` | input |
| `sources\TerraFX.Runtime\Shaders\MeshC0TexturedMaskPixel.hlsl` | C | 65 | `1D26185A889621F6535898A7F7D89FB615046A2306520A87FC41DA5420F9E1A2` | input |
| `sources\TerraFX.Runtime\Shaders\MeshC0TexturedPixel.hlsl` | C | 17811 | `B9AED847C3169409C0C53A239F66D04C651246B99F6C5578CF968921D5093B67` | input |
| `sources\TerraFX.Runtime\Shaders\MeshC0TexturedShadowPixel.hlsl` | C | 951 | `134CB1586F2ED49943BE7BB430E72974F0A7148C55F00381F50FB635BAA27636` | input |
| `sources\TerraFX.Runtime\Shaders\MeshC0TexturedShadowVertex.hlsl` | C | 62 | `FE9107BDEB7EBCD0B6E6C62993B2015F2A24E96A0EEAA045A7A359C6ABF598E7` | input |
| `sources\TerraFX.Runtime\Shaders\MeshC0TexturedVertex.hlsl` | C | 56 | `B432DA990424A65F6FC7013A80308F1E07DD2A93F6E52A9E1433651E8FD4819B` | input |
| `sources\TerraFX.Runtime\Shaders\MeshC0Vertex.hlsl` | C | 7387 | `369AA412615FEB6405E38907733AF2DEBEDCFE6366B3BB56D21546C6E829498C` | input |
| `sources\TerraFX.Runtime\TerraFX.Runtime.csproj` | C | 14094 | `E019D4DBAD14FF2D5FF8D7365DDED0D09210575D5228A0D01CED70C99B3C2D33` | input |
| `tests\TerraFX.Graphics.D3D12.Consumer\Program.cs` | C | 11027 | `28648F4D9E9475BEFD2186BA900E1BA1EA2AEAF68BBB513562EDC1E67CCF507A` | input |
| `tests\TerraFX.Graphics.D3D12.Consumer\TerraFX.Graphics.D3D12.Consumer.csproj` | C | 1281 | `C3A5489A9D808F3A7FAA0E21079C344D14E73C5640598BC8360C03131C19DEE1` | input |
| `tests\TerraFX.Graphics.Tests\TextureReadbackTests.cs` | C | 3567 | `1EEE25584A8D76C30193D824A4BC898AF5F67E1D21668CCB7234D66E17560B25` | input |
| `tests\TerraFX.Runtime.Tests\Entities\WorldAdmissionDomainTests.cs` | M | 31338 | `F01C956A1DF54BBB048BD40BE073A0190E843255EB71C0AF618FD3A345793429` | dirty-only |
| `tests\TerraFX.Runtime.Tests\Entities\WorldLifetimeControlQuarantineTests.cs` | M | 6758 | `B4E0683405ADAB476B5B4CF9108E8C7B1E30E33B6CD48EC55A28AECA57EDFFBB` | dirty-only |
| `tests\TerraFX.Runtime.Tests\Rendering\RenderMeshC0BoneBufferBuilderTests.cs` | C | 2602 | `1C3E20C46F62758BC791DE7A208B59E5345FBB278A3EA8EA56962EF550C9B1AA` | input |
| `tests\TerraFX.Runtime.Tests\Scenes\PreparedSceneAssetRetentionTests.cs` | M | 10883 | `E342237B7645BE805DD924855B2239907773D31753D22B8B2261BF26C20452F4` | dirty-only |
| `tests\TerraFX.Runtime.Tests\Scenes\RendererExtractionTests.cs` | M | 32152 | `ABAEEA4D0F6BE941AF73E640B7FCE41EC753D2199654343FA4668D54EC20FC6A` | dirty-only |
| `tests\TerraFX.Runtime.Tests\Scenes\RenderPublicConsumerTests.cs` | C | 14494 | `D08B3435DA4E823553BD53C52BC282FBB7D42699E973D95B630F0D755D58C59E` | input |
| `tests\TerraFX.Runtime.Tests\Scenes\RenderSnapshotMeshGpuTests.cs` | C | 65162 | `448E37540009FB486899253E256CD81D38D14CCF64ED930DCA496867EBB26396` | input |
| `tests\TerraFX.Runtime.Tests\Scenes\RenderSpriteCapacityTests.cs` | M | 13711 | `CAA6610CF55F8FFFA077745FD96BAA45FCECFED2FD051CC763901AEED5500239` | dirty-only |
| `tests\TerraFX.Runtime.Tests\Scenes\RenderSpriteComponentTests.cs` | M | 22417 | `8E65B71B5AC1E0EF9D1F47AA56963054D091DF8C229E74DDBB1CAE37BB59AB60` | dirty-only |
| `tests\TerraFX.Runtime.Tests\Scenes\SceneAssetStoreTests.EngineHost.cs` | C | 14327 | `056EA64B9546BD83DC016EBDD482067C049645EB1443D1D5B54EA6CBFBD7E763` | input |
| `tests\TerraFX.Runtime.Tests\Scenes\SceneLoadOptionsTests.cs` | M | 5938 | `D077505A70E96EA9FB530D1D4B5A030CBF2C6BF599A867DC79256A5A99B10DE8` | dirty-only |
| `tests\TerraFX.Runtime.Tests\Scenes\SceneSupportRegistrationTests.cs` | M | 20208 | `D474CBB730BA7ECA38F5B42886B43E417962241AA88DAD1202BF41DC9CF21E76` | dirty-only |
| `tests\TerraFX.Runtime.Vulkan.Consumer\Program.cs` | C | 35566 | `C9679EC993145AAA83024A225D20F0F4E2EC4EFA494ABAF6E7055956BC07488C` | input |
| `tests\TerraFX.Runtime.Vulkan.Consumer\TerraFX.Runtime.Vulkan.Consumer.csproj` | C | 1447 | `DBEFE41A8ECD0AB9C602B964EFE88222427DC74623DAB888C2DC6B9B646EB782` | input |

### Final verification disposition

The post-draft comparison checked every frozen hash and byte length, HEAD/index, and canonical status excluding this new output. The original 38 dirty paths and all original owner/source/pre-existing proposal bytes matched; HEAD and the index were unchanged. The two concurrently introduced proposals failed the extended byte freeze and were observed changing repeatedly. A later observation at `2026-11-16T03:02:59.9572648Z` was:

| Concurrent path | Frozen bytes / SHA-256 | Later observed bytes / SHA-256 |
|---|---|---|
| `docs\proposals\m05-owner-contract-closure-runtime-ports-addendum.md` | 32549 / `71DED88B6EF5FFDF08C24EEE90E64E9CDFC82F05D1433B86B06CECA328BA6B22` | 43575 / `1D826CF76883A990B28F03CD6604832113AC8723474D72E81C0C288D82EECBA3` |
| `docs\proposals\m03b-scene-preparation-handoff-closure.md` | 58662 / `9325D77A1113A160A1CF3799E9EBCB834C1AB1C946965F1D53B3A358F41A43AB` | 70556 / `628B09039114AACAEA65116DD584B2F94B554011306AB600414C5A2A809A7FBA` |

These are timestamped mismatch observations, not assertions that the moving files have stopped changing. The runtime-port addendum was read only as additional producer-gate context; no delivery decision relies on its moving declarations. Its latest scope still concerns restore-source/resource/attachment/asset joins, not this output. The other concurrent file was not used for a contract decision.

The original canonical status is reproducible only after excluding both external additions and this output; the extended 40-entry status is reproducible after excluding this output alone. Consequently a stable **whole** review-input freeze could not be established during these concurrent writes. Re-freeze the two moving proposals after their owners finish before accepting that larger set. Do not repair this mismatch by editing another author's files, by silently replacing the frozen hashes, or by claiming qualification. Only this proposal was created by this work.
