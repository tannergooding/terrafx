# M05 skinned-normal diagnostic Core completion-handoff addendum

## 1. Decision and scope

**Tentative implementation guidance, pending review; not an accepted contract amendment, implemented capability, numeric footprint qualification, or admission guarantee. The budget gate remains NO-GO.**

Select a **Graphics-owned, preclaimed CPU completion destination whose allocation and lease footprint is enforced by Core**. The backend copies completed staging bytes into that destination; it no longer chooses or allocates an opaque Core result inside parameterless `IGraphicsBackendBufferReadback.CopyCompleted()`. Graphics owns readback admission and request/quarantine transfer. Core owns the allocation extent, control/backing capacities, storage credits, and actual-release accounting facts. Runtime owns the diagnostic batch's aggregate completion ledger and typed-result handoff.

This resolves the owner and copy-direction decision in [budget-evidence rectification](m05-skinned-normal-diagnostic-budget-evidence-rectification.md), sections 5–7, and [recording-budget closure](m05-skinned-normal-diagnostic-recording-budget-closure.md), sections 4–7. It does **not** fill their remaining numeric terms. Existing Core ownership rules support the payload transfer and scoped copy, but do not currently supply the complete footprint/admission capability specified below.

Keep the joint **33,554,432-byte preparation/recording ceiling**, independent **16,777,216-byte completion ceiling**, whole-graph refusal, complete result membership, shared 131,072-draw ceiling, exact four Frame bindings and packet views, command schedule, shader ABI, cancellation, and native completion semantics. Neither ceiling lends credit to the other. The [palette-bound addendum](m05-skinned-normal-diagnostic-budget-evidence-palette-bound-addendum.md) remains the separate production-count correction. No public API, diagnostic ID, schema, generic public Core quota domain, Scene producer, or qualification claim is added.

The minimum prerequisite includes a narrow **Core implementation change**, not merely more arithmetic in Runtime or Graphics. It was absent from the preceding Runtime/Graphics-only implementation scope. This proposal authorizes no source changes itself.

## 2. Source-established handoff gap

Paths are repository-relative; section 8 freezes their whole-file bytes.

| Existing owner | Fact and consequence |
|---|---|
| `sources\TerraFX.Graphics\IGraphicsBackendBufferReadback.cs` | `CopyCompleted()` returns `OwnedBuffer<byte>` without a prebound destination or footprint/admission input. The returned type establishes ownership, not a complete preallocation byte bound. |
| `sources\TerraFX.Graphics\GraphicsDevice.cs`, `ReadbackBufferRequest.CompleteOnOwner` | Calls that factory after the native fence, checks logical `ByteLength`, retires backend staging, then publishes through a TCS. Its completion-slot semaphore is a count guard, not a CPU byte reservation. `Fail` can quarantine the request and still release that slot. |
| `sources\TerraFX.Graphics.D3D12\D3D12GraphicsBackend.cs`, `CopyMappedStorageToOwnedBuffer` and `D3D12BufferReadback.CopyCompleted`; `sources\TerraFX.Graphics.Vulkan\VulkanBackendDevice.Submission.cs`, `ReadMappedBuffer` and `VulkanBufferReadback.CopyCompleted` | Both allocate a new Core native CPU buffer with requested length and alignment 1, copy into it, and transfer it to an `OwnedBuffer<byte>`. That CPU destination is distinct from native readback staging. The allocation happens behind the current handoff. |
| `sources\TerraFX.Core\Buffers\BufferAllocationOptions.cs`; ``sources\TerraFX.Core\Buffers\UnmanagedBuffer`1.cs``, `Allocate` | The option caps the alignment-rounded native allocation extent. It does not cap the owner object, registration-list object/backing, wrappers, registrations, or other managed controls. `ByteLength` remains the logical count-times-stride product. |
| The same Core owner, `BeginCopyOperation`, `AcquireOwnedReadLease`, `BeginLeaseWindow`, `ReleaseLease`, `TryFree` | `CopyFrom` allocates an operation registration and can grow `_registrations`; acquiring a read lease allocates a registration and wrapper. A window on an existing lease reuses its registration while taking an operation credit. Physical free waits for owner, finalized-owner, lease, and operation credits to end. |
| ``sources\TerraFX.Core\Buffers\OwnedBuffer`1.cs`` and ``BufferReadLease`1.cs`` | Disposal releases credits but leaves readonly references to storage/registrations. A disposed wrapper retained by a completed Task can therefore retain managed controls/backing after native payload free. Native free and managed-root release are different facts. |
| `sources\TerraFX.Runtime\Rendering\Renderer.Submit.cs`; `RenderMeshC0NormalDiagnosticGraphPass.cs` | Runtime currently awaits an owned readback, acquires one read lease, decodes through one synchronous window, and disposes both. Decode transfers the exactly filled typed-result builder backing with `MoveToImmutable`; it does not transfer the Core raw allocation. |
| `tests\TerraFX.Graphics.Tests\GraphicsBufferTransferTests.cs`, `FakeBackendBufferReadback` | The fake already holds an `OwnedBuffer<byte>` before `CopyCompleted`. Its fence/ownership tests do not establish when real backend Core allocations are admitted or their footprint. |

[Chapter 05](../design/05-owned-buffers-and-internal-collections.md), sections 2–3, already requires allocation/accounting before publication, transfer without payload copying, independent lease credits, and actual-use-delayed free. [Chapter 02](../design/02-dotnet-api-memory-and-ownership.md), sections 3–4, preserves callback-free, allocation-free valid Core release and prohibits cancellation or Task termination from standing in for consumption ending. Those rules do not provide the missing metadata size/capacity table or a managed-root retirement signal.

## 3. Admission authority and the minimum missing Core capability

| Owner | Required responsibility |
|---|---|
| Runtime diagnostic batch, carried from graph planning into `Renderer.SubmitAsync` | Obtain a qualified scalar completion footprint before returning a diagnostic-bearing graph; include it in the independent completion peak. Claim the full completion requirement before the first diagnostic wait/materialization/recording request. Keep retained plan/recording bytes on the separate joint ledger. |
| Graphics completion admission and `ReadbackBufferRequest` | Own the concrete CPU-destination reservation and its request/task/continuation overhead. Materialize the destination under that reservation before diagnostic graph queue acceptance. Later bind the actual source buffer/range under the existing usage/epoch checks. Transfer, do not duplicate, the destination grant into the readback request and its existing quarantine carrier. |
| Core buffer owner and its private admission state | Supply and enforce the complete qualified footprint, actual capacities, allocation identity, reserved future wrapper/lease operations, and payload/control release facts. Keep the relevant debit attached to storage and all surviving accounting roots through transfer/disposal. Graphics must not infer these facts from public lengths or inspect private collection fields as a production protocol. |
| D3D12/Vulkan readback owners | Retain their existing source/fence/staging ownership. After real fence completion, synchronously copy into the exact admitted destination lease. Do not allocate a replacement CPU result, retain the borrowed destination, publish it, or release its admission. |
| Runtime decode and returned result | Consume the finalized raw owner through the reserved read lease/window. Own the separately charged typed-result backing and receipt/control storage after handoff. A typed result does not inherit or free native staging. |

The **minimum missing Core capability** is an internal, closed buffer-allocation admission with all of these properties:

1. **Nonallocating plan.** For a validated byte extent/alignment and the closed operation sequence below, return the actual rounded native capacity, managed control/backing charges, enforced collection capacities, profile identity, and phase/lifetime ownership. Unknown profile, stride, object, backing, or root charge means *unavailable*, not zero. The query performs no runtime sizing experiment, buffer allocation, comparer initialization, or discovery allocation.
2. **Preclaimed construction and no unreserved growth.** Convert an existing grant into the real Core owner/backing and initial write lease; reserve later finalized-owner/read-lease construction before those objects are allocated. Verify the planned identity/capacities before publication. Refuse before any allocation that lacks a charge, including a registration-list expansion. Constructor failure attempts all safe releases and preserves the primary failure.
3. **Storage-owned lifetime accounting.** Move one allocation's debit from mutable owner to finalized owner without ending/reacquiring it. Independent leases and active windows keep the applicable debit alive. Report native release only after the actual matching free succeeds. Retained managed controls/backing have their own debit; native free, list `Remove`/`Clear`, wrapper disposal, and Task completion cannot refund that debit while roots remain.
4. **No upward callback or public protocol.** Keep this state and its accounting operations Core-owned/BCL-only; release must remain idempotent, nonthrowing, allocation-free and callback-free for valid built-in credits. Do not register Graphics/Runtime callbacks or World/domain tickets in Core. A neutral Core-owned admission state may be consumed downward by Graphics and Runtime without giving Core an engine dependency.

These are private capability requirements, not invented public signatures or a completed numeric API review. `TerraFX.Core.csproj` currently grants Runtime friend access but not Graphics. If Graphics must consume the new internal capability directly, the narrow strong-name-qualified Graphics friend is part of the prerequisite; a Runtime callback into Core is not an alternative dependency route. Keep [Chapter 29](../design/29-project-setup-build-and-deployment.md)'s existing assembly directions.

**Managed-root limitation:** the current Core wrappers provide no deterministic “last managed root ended” notification. This proposal does not invent one. The implementation must establish explicit detachment/ownership transfer for obsolete private roots and keep any remaining control debit with the retained storage/wrapper/Task owner. An accounting anchor, if needed, is itself charged and must not disappear before any charged backing it represents. No budget-owning finalizer that calls back into Graphics, forced GC, assumed prompt collection, or weak-reference observation grants reusable headroom. If the required retained-control ownership/release cannot be established within the private Core capability, that is the exact remaining prerequisite and admission stays NO-GO; permanently leaking grants is not a completed implementation.

## 4. Closed destination recipe and charge contents

For positive diagnostic draw count `D`, the raw result extent is `L = checked(16 * D)`. `D = 0` creates no diagnostic destination, readback, or new completion admission; preserve initialized-empty results only for that genuine no-draw case.

Use the existing Core storage family with this selected sequence:

```text
qualified plan / grant
  -> UnmanagedBuffer<byte> + one full-range write lease
  -> backend synchronous copy through that lease's scoped window
  -> write window ends; write lease closes
  -> TransferToOwnedBuffer on the same native allocation
  -> one full-range read lease; one synchronous diagnostic decode window
  -> read lease and finalized owner close
  -> actual native free, with retained managed controls accounted separately
```

The destination and write lease remain private; neither escapes to a public renderer consumer. There is no `Retain`, second reader, second writer, or `CopyFrom` operation-registration allocation in this closed diagnostic route. Keep ordinary public buffer lease behavior unchanged. The renderer draw ceiling makes `L` at most 2,097,152 bytes, so this diagnostic copy/decode fits one `int`-length window; it does not impose that restriction on general public readback.

The Core plan must enumerate, not hide in a fixed “overhead,” at least:

| Charge | Capacity/lifetime obligation |
|---|---|
| Raw CPU native allocation | Actual allocator-requested capacity after Core alignment/padding, not logical `L`. Use the selected allocator/alignment consistently through free. Any pooled or larger backing must report and charge its actual capacity instead. |
| Core allocation and registration storage | One `UnmanagedBuffer<byte>` object, its embedded `ReentrantGate` fields, one registration-list object, and the actual reference-backing capacity required for the sequence. A single live registration does not prove capacity 1: the chosen construction must qualify and enforce its actual capacity. No growth is needed merely to replace the closed writer by the reader. |
| Writer/finalization/reader controls | One write-lease wrapper and its registration, one `OwnedBuffer<byte>` wrapper, and one read-lease wrapper and its registration. Both registrations/wrappers can coexist as managed roots even after the writer is logically removed. Transfer shares the original allocation owner; it does not allocate another raw payload. |
| Admission/setup controls | The actual Core admission/anchor objects and separate backing, allocation options if constructed, and attributable cold initialization. `ReentrantGate` is embedded value state, not a separately allocated lock object. Shared singleton initialization is funded once by its real owner or included at first use, never assumed warm. |
| Graphics completion controls | Destination carrier, readback request/TCS/Task, cancellation registration, fence-wait captures/tasks/continuation, backend readback managed controls, and attributable collection growth. Reserving 256 count slots does not fund these bytes. |
| Runtime completion output | Raw owner/lease/control storage still retained during decode, the D-element typed-result backing at its qualified managed stride, builder/decode/receipt/task controls, and the result's subsequent retention. `MoveToImmutable` shares/transfers that filled backing; do not charge a nonexistent second typed-result copy. |

Use the parent proposal's conservative `Q(capacity, stride)` for separately allocated managed arrays, actual enforced capacities, and qualified object charges. Do not apply a managed-array header rule to the raw native allocation or use `Unsafe.SizeOf<class>()` as an object charge. Actual requested native extent is an owned-allocation bound, not a promise about total process allocator metadata/RSS.

The completion peak is the maximum of preallocation, copy/finalization, raw-plus-typed decode, retained-output, and uncertain-work phases, including every backing/control that still coexists. It must fit **16,777,216 bytes** before admission. Reconcile plan/capacities before allocation and before publishing the handoff; checked charge overflow or an unavailable qualification refuses the whole diagnostic-bearing graph using the existing contextual allocation-budget failure, preserving existing validation/cancellation precedence.

**No staging substitution or double charge:** native GPU readback staging, ring slack, command/descriptor storage, and fence retirement remain charged to their existing Graphics/backend owners. The Core CPU allocation is a second, real allocation charged to the completion ledger. They can coexist and therefore both need their respective grants, but staging is not added again as raw CPU destination storage. Readback-specific managed completion controls belong in completion; still-rooted graph recording/preflight storage remains on the joint preparation/recording ledger rather than being relabeled.

For example, `D = 2` needs 32 logical raw bytes. The current backends' alignment-1 request does not turn 32 into the full completion charge: Core owner/list/backing/lease objects, Graphics controls, and two typed results still contribute. The selected route copies those 32 bytes once from staging into the admitted Core allocation, then transfers its ownership without another raw copy. It preserves both rows; failure to fund any control refuses the graph, not the second row.

## 5. Handoff and release protocol

Replace the **internal backend buffer-readback** factory seam with a synchronous copy-into-admitted-write-lease operation. Graphics, not the backend, performs Core allocation/finalization. Validate exact destination extent/identity and completed-fence/owner-thread state; borrow scoped windows only for the copy, including all mapping/unmapping. The backend retains no destination lease/window after return, even on failure. There is no parameterless-factory fallback for diagnostic work. Public `ReadbackBufferAsync` retains its existing public signature, result ownership, error channels and actual-completion behavior; its ordinary path is not a substitute for the diagnostic's earlier private admission.

At graph planning, refuse an unavailable complete footprint before diagnostic-sized construction/graph return. At submission, acquire the completion grant before the first diagnostic wait and fund its own carrier before constructing that carrier. Materialize and verify the Core destination/write lease before diagnostic graph `_ownerQueue.WriteRequestAsync`; discovering that the CPU result cannot be funded only after graph acceptance is too late. Earlier independently accepted uploads/resources keep their own cleanup rules.

Later, move the same destination/remaining grant into `ReadbackBufferRequest` before readback queue acceptance. That move grants neither extra bytes nor a second allocation. After the real readback fence, copy into the destination, end the write window, dispose the write lease, and finalize through Core's existing writable-credit checks. Allocate the reserved read-only wrapper/read lease and decode only against the already admitted capacities.

| Event | Required ownership/accounting disposition |
|---|---|
| Safe refusal before graph/readback acceptance | End unused grants and safely owned storage at their actual release boundaries. Drop/transfer obsolete roots. Do not claim that a disposed-but-retained control backing is already gone, or that earlier accepted work was rolled back. No graph/result prefix is published. |
| Successful copy/finalization | Move the raw allocation debit to the finalized Core owner. The request detaches transferred destination/writer references before task publication. The output owner/read lease carries the surviving storage liability, not a second reservation for the same payload. |
| Decode while raw storage is live | Keep raw payload/control and typed-result backing charged simultaneously. Complete all D rows or fail; decode/builder failure still closes safe raw ownership without returning an empty-success diagnostic. |
| Owner or lease disposed during an active Core window | Disposal closes admission/releases its credit only. Keep the payload charge until the window's `finally` and actual Core last-use free. A disposed parent cannot revoke an admitted reader/window. |
| Successful raw cleanup after decode | Return the raw native portion only after Core's actual-free fact. Keep managed wrapper/list/backing charges with any remaining roots. Typed-result/receipt charges transfer to their real retained output owner; an escaped immutable result backing must not lose its charge merely because a receipt is collected. |
| Retained completed Task or disposed wrapper | No automatic byte refund. Clear obsolete request/async roots where legal, or retain/transfer the control debit alongside the surviving root. Source-level scope exit is not compiled-root evidence. Tasks need not wait for GC to become terminal once actual consumption and required ownership transfer have ended. |
| Post-admission cancellation or fence wait failure/uncertainty | Preserve existing completion/device-loss rules. The already-rooted quarantine request retains the destination grant and controls still owned/consumed. A demonstrably independent unused CPU destination may release only portions whose actual safe release is established; that does not release uncertain native staging or request roots. |
| Backend staging retirement or cleanup failure | Attempt independent safe CPU cleanup; keep every unreleased portion with its request/quarantine owner. Neither releasing a completion slot, failing a TCS, resetting the Runtime lane nor disposing a GPU use lease returns these bytes. No staging retirement is inferred from Core payload release. |

A grant is converted into storage, not charged both as an unspent grant and as that same storage. A transfer changes the responsible owner without a moment of unaccounted storage. Grants for genuinely unallocated future controls remain held until no longer needed. No replay/resubmission may borrow headroom from an earlier still-retained or quarantined attempt.

## 6. Smallest coherent code/test batch

Implement one dependent **Core → Graphics/backends → Runtime** batch; do not build a parallel allocation framework or a test-only buffer producer.

| Area | Smallest required change and owning checks |
|---|---|
| Core | Add the private qualified footprint/admission and actual-release capability to the existing buffer implementation; enforce registration backing capacity and bind control/storage accounting through finalization and leases. Touch `UnmanagedBuffer`, `OwnedBuffer`, read/write lease controls and the Core friend declaration only as required. Extend `tests\TerraFX.Core.Tests\BufferTests.cs` and `NativeBufferLifetimeTests.cs` for exact-fit/one-byte-short full grants, rounded native capacity distinct from logical length, no-growth enforcement, transfer without payload duplication, construction failure, and disposal while an actual Core window remains active. Existing allocator fakes verify release boundaries; they do not supply invented production object charges. |
| Graphics and both backend implementations | Update `IGraphicsBackendBufferReadback`, `GraphicsDevice` readback admission/request/completion/quarantine, and only the buffer-completion paths in D3D12/Vulkan. Preserve shared texture/readback helpers unless the buffer path actually needs them changed. Update the existing fake backend to copy into the real admitted Core destination rather than returning an uncharged prepared result. Extend `GraphicsBufferTransferTests.cs` for pre-queue refusal, exact destination identity/range, safe unwind, fence-delayed transfer, cancellation and uncertain-work debit retention, independently retained Tasks, and no completion-slot-as-byte-credit assumption. Adapt internal backend test callers, including `CompleteBufferReadbackOnTestThread`, so all projects compile; do not execute or claim native qualification as part of this batch. |
| Runtime | Carry the qualified completion plan/grant through diagnostic graph planning and `Renderer.SubmitAsync`, use the private preadmitted readback path, and retain separate typed-result/receipt charges. Extend `RenderMeshC0NormalDiagnosticBudgetTests.cs` with completion boundary/refusal/overflow and nonzero retained-baseline cases; use existing real owning consumers for end-to-end whole-graph tests, never fabricated snapshots or a new Scene preparation workstream. Preserve the Graphics diagnostic schedule/witness regressions. |

After implementation, use ordinary owning `dotnet test` with `global.json`: Core's two buffer fixtures, Graphics' readback/diagnostic recording selection, and Runtime's diagnostic-budget selection, **Debug before Release**. Confirm nonzero selected/executed cases and keep per-run evidence in ignored artifacts. Qualified Core/control sizes and actual capacities, cold/warm allocation traffic, shared backing identities, compiled retained-root behavior, and refusal before graph/owner-queue acceptance are required observations of the implemented path; a scalar test alone does not close them.

This document performs no such implementation, test, build, allocation trace, root inspection, or additional runtime probe. General readback compatibility is required, but native execution/failure, NativeAOT, public-consumer and milestone qualification remain their existing separate gates.

## 7. Disposition and retry condition

| Gate | Current disposition / required next fact |
|---|---|
| Who preclaims Core completion storage? | Decision proposed: Graphics owns the preclaimed destination/request grant inside Runtime's independent completion envelope; Core supplies/enforces its private full-footprint and lifetime contract. Backends only copy into it. |
| Can current public buffer options and `CopyCompleted()` establish that contract? | **No.** Options bound rounded native payload only; the seam can allocate opaque controls/backing; no qualified retained-control admission/release capability is supplied. |
| Minimum implementation unblocker | The four Core capability requirements in section 3, including a qualified managed-root ownership disposition, plus the private copy-into handoff. Until supplied, do not fabricate size constants, infer credit from `ByteLength`, or admit a diagnostic-bearing graph as budget-qualified. |
| Complete M05 diagnostic budget | **NO-GO.** This addendum supplies no Core object sizes or numeric phase bound and closes neither the remaining joint recording/preflight/root ledger nor the independent typed-result/receipt retention proof. Reconsider only after the exact implementation/profile supplies all required charges, preclaims and release/quarantine observations. |

## 8. Frozen static inputs

Based on HEAD `31f156a51ccc462ae9c5cc8d4e87e00c0054dec4`; HEAD alone does not identify dirty input bytes. The following **33 whole-file SHA-256 values** were captured at `2026-11-17T12:21:39Z`. `C` = tracked clean, `M` = tracked modified, `U` = untracked. This is the narrow static input set, not a compiled profile, runtime measurement manifest or whole-worktree stability claim. Recheck it before applying a review disposition; acceptance applies only to the reviewed bytes/scope.

| Input | State | SHA-256 |
|---|:---:|---|
| `docs\proposals\m05-skinned-normal-diagnostic-budget-evidence-rectification.md` | U | `A8DBD9EE4EA443245391A3B8AE17EDE03E3D71DA3F824E9128641E1C94F4DB36` |
| `docs\proposals\m05-skinned-normal-diagnostic-recording-budget-closure.md` | U | `5787039DC3BA43A862529CD9F01D82623DDFEC1B273194CC73FC50F079E0C753` |
| `docs\proposals\m05-skinned-normal-diagnostic-budget-evidence-palette-bound-addendum.md` | U | `89749E17E8F9624B08577B61CB40808A18F91E63D7BEC04C6992A10833301163` |
| `docs\design\02-dotnet-api-memory-and-ownership.md` | C | `40F913A50434209E09941C22625A6D0722202BAB97EBAC0E153C1EA3368E2291` |
| `docs\design\04-capacity-and-workload-profiles.md` | M | `A4D86F3FA373A64548435C53582BF5916E29D77F34650F9D161CCEC90E6F0245` |
| `docs\design\05-owned-buffers-and-internal-collections.md` | C | `5BD7DDFAAC50534C369572126241EFC39D2661B890F6723965AA4C1B3EA2B3BD` |
| `docs\design\29-project-setup-build-and-deployment.md` | C | `3B55A013EEC327ECD648ACBD5B53717DD218AD288E8047AC21277609EFA2F47B` |
| `docs\design\35-implementation-roadmap.md` | C | `24EEC6E92B7AD904D552F290EF748FFC8FCE13B6B94A89EBD9F39487D4584D87` |
| `docs\project\Roadmap.md` | M | `649182860D65DC902A406BCAC482AA1D286465BAFB0B34143452C8B09E51EBDE` |
| `docs\project\Building.md` | M | `F043A352870EFB6C7D4A61998225E2508A8F4370A57E784483A41819D452075F` |
| `global.json` | C | `1BCE01871321C2DF82E887DA428CE5C121A6C6541FE65017FE3D249BE47E190C` |
| `sources\TerraFX.Core\TerraFX.Core.csproj` | C | `B03B74FB5A268F0273AB8845374CFE9CA61ED16BFD2F5AA837328076BF9A2DBF` |
| ``sources\TerraFX.Core\Buffers\UnmanagedBuffer`1.cs`` | C | `6839374F3FD955EF2996E56A8F62D018DC7B2836435CFA88117EEE85DCF7622F` |
| ``sources\TerraFX.Core\Buffers\OwnedBuffer`1.cs`` | C | `CE9F79F2FB7B7FE5694EAC1AD6A3832A0E5E83577D2A38F72D1194262E33EFE2` |
| ``sources\TerraFX.Core\Buffers\BufferReadLease`1.cs`` | C | `6A21D4A80FEB3466412CE3C312FC5F5444339322ACEC4426A24665649BCE7406` |
| ``sources\TerraFX.Core\Buffers\BufferWriteLease`1.cs`` | C | `6A48B8D8CFB7AA2C3E7940183069F53D7B7DCB66AB24D2AA5515DD8DE61C9085` |
| `sources\TerraFX.Core\Buffers\BufferAllocationOptions.cs` | C | `5DBFBEB2457348E2CBC0270D26E5F5B45A336C62E8AD0A1570FDAEC04D9DD218` |
| `sources\TerraFX.Core\Buffers\NativeMemoryOperations.cs` | C | `87B040D3A9CAD2F5BA5023270C4A1652450E17C05D954C3C27B8A82CFC612424` |
| `sources\TerraFX.Core\ReentrantGate.cs` | C | `8F0EF274C39C3E07E691951011AC86C574311912D7F78348EB8D6780555F0C71` |
| `sources\TerraFX.Graphics\IGraphicsBackendBufferReadback.cs` | U | `33BA726530AA82B3EA5C3C35C2497643DC8880CD018F42B3EFD396E0D9EC597A` |
| `sources\TerraFX.Graphics\GraphicsDevice.cs` | M | `22F6F0732240FDF490F5C82DB8005D0386AAF166883A2F80842448C9F2BDE279` |
| `sources\TerraFX.Graphics\GraphicsDevice.SubmitGraph.cs` | C | `A85B4857B87F7487E213E23F8C431E9F8D0D16DFE3394E2E97C22CDEB37C6D93` |
| `sources\TerraFX.Runtime\Rendering\RenderMeshC0NormalDiagnosticGraphPass.cs` | U | `14E3198F43B96725BE5509EA5611EE1E9D1204E5111AB7327E7C2B61B5DAA380` |
| `sources\TerraFX.Runtime\Rendering\RenderMeshC0NormalDiagnosticWork.cs` | U | `C2EFD9415C0DD8864B8CDF740793D2D88F6D8FA1594CB2FAD6297B25D2B37A09` |
| `sources\TerraFX.Runtime\Rendering\Renderer.Submit.cs` | M | `AE6A5CE9671CA1D5FCF50216D769E27CD5F416F08FAAD087F614DAA8F96A3E5D` |
| `sources\TerraFX.Runtime\Rendering\Renderer.BuildGraph.cs` | C | `552A7F939E01CA189DB0EB3F759F7E97ADC7BD2FF0005391CDFD899860A09560` |
| `sources\TerraFX.Runtime\Rendering\RenderSubmissionReceipt.cs` | M | `77120676F289AD7D8891875A783BAFA05E64B7F0317BC09BECC2545AC42E5846` |
| `sources\TerraFX.Graphics.D3D12\D3D12GraphicsBackend.cs` | M | `B2A87F4F1D28136A9DA209884DA5B63BB0FF571A80C167E7220A23F18438DA58` |
| `sources\TerraFX.Graphics.Vulkan\VulkanBackendDevice.Submission.cs` | M | `68DE5F7AAB5113D8159FAC9055235FFE901E386281C7A7441947B016BE56EAE9` |
| `tests\TerraFX.Core.Tests\BufferTests.cs` | C | `D76544B77F49E07661A23005B48633A6F2AA753ED7E21DD6B44B14A064F29CF1` |
| `tests\TerraFX.Core.Tests\NativeBufferLifetimeTests.cs` | C | `32E386E78D657CB2080F5DFF96EF499DB540310C76EEC94E5B06E3D8027C7B66` |
| `tests\TerraFX.Graphics.Tests\GraphicsBufferTransferTests.cs` | M | `221FCFF220F2AA415B580E44D80DFDE3676AAE7456F52F747A6756A142357776` |
| `tests\TerraFX.Graphics.Vulkan.Tests\VulkanBackendDeviceTests.cs` | M | `9D2F034FF0BE205AE434D94AC294C069FD5DCAC8B0D8E72CD51D594EC483E194` |
