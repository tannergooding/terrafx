# M05 diagnostic preflight and payload-alias workspace addendum

## 1. Focused decision and unchanged gate

**Tentative implementation guidance, pending review. Complete preflight/backend workspace and payload-retention admission remain NO-GO.** This narrows [recording workspace admission](m05-skinned-normal-diagnostic-recording-workspace-admission-addendum.md) to the current validation collections, repeated backend preparation and immutable payload aliases. The separate [async-return owner](m05-skinned-normal-diagnostic-async-return-ownership-addendum.md) remains unqualified.

Select **one prequeue Graphics/backend-owned workspace plan for complete frozen graph membership**, including repeated validation, and **one shared private payload owner/borrow contract for existing command backings**. A backend-local collection created after queue acceptance cannot retroactively supply the grant. An immutable-array or pass-value copy is not a new independent allowance or evidence that the old backing was released.

Keep the joint **33,554,432-byte** preparation/recording and independent **16,777,216-byte** completion ceilings, current draw ceiling, exact commands/barriers/bindings/results, native staging ownership, Core readback and uncertain-work rules. Do not reduce membership, invent a collection/request cap, duplicate command payloads, change the 280-byte command representation or add a public API/diagnostic ID.

## 2. Two independently removable scratch rows

The bounded source batch eliminates storage rather than assigning unqualified object sizes:

- `HasPriorStorageWriteHazard` no longer constructs/grows `List<GraphResourceAccess>`. It locates the target in `OrderedPasses` and checks prior paired writes against current paired accesses by borrowing the existing immutable data. Buffer-byte and texture-subresource overlap, resource identity, write/pair predicates and actual pass order remain authoritative. Both native backends still call the same query after queue acceptance; there is no replacement managed collection, backing, pool, comparer or payload copy at that call.
- Preflight no longer constructs a per-pass `List<TextureFormat>`. `MatchesColorTargetFormats` compares the pipeline formats directly with non-depth attachments in their existing order, including exact count equality. Existing attachment/device limits and attachment validation still run before this check. There is no new stack/array capacity or smaller color-attachment limit.

These two **managed List/backing terms are absent**, not a qualified zero-byte estimate for the rest of either method. The hazard query rescans frozen accesses instead of retaining a compact prior-write list; no native throughput claim follows from eliminating that allocation. Valid-input zero incremental allocation is observed only at the two measured managed helper calls, not at full preflight, cold initialization, failure controls or backend submission.

## 3. Actual remaining collection owners and capacities

`GraphicsDevice.SubmitGraphAsync` calls `ValidateNativeRasterSubmission` before constructing/enqueueing `SubmitGraphRequest`. **D3D12 and Vulkan call that validator again inside their backend `SubmitGraph`, after owner-queue acceptance.** Moving or funding only the first call would miss the second. Preserve the fresh device/Surface validation; do not skip it to make an allocation test pass.

| Current owner/storage | Capacity fact available now | Missing prequeue authority |
|---|---|---|
| Shared validation import/order maps | Constructor requests are `BufferImports.Length`, `TextureImports.Length` and `OrderedPasses.Length`. Separate usage maps and global/per-pass/per-pipeline sets/dictionaries start without an explicit capacity and grow with actual members. | A requested dictionary capacity is not its bucket/entry backing capacity. No owner-issued nonallocating plan enumerates object controls, actual hash capacities, growth overlap, initialization or simultaneous live groups. |
| Shared validation resource/binding sets | Distinct resource IDs, pass/resource pairs and reflected signatures determine membership; the reflected-resource set is rebuilt per pipeline command. | Four diagnostic shader bindings are not four total graph resources or four total preflight entries. Unknown ordinary callbacks/passes and their aliases do not inherit a diagnostic recipe's footprint. |
| Both backend descriptor-reference lists | `B` is counted from **every** `SetBindings` command in the frozen passes. The positive-capacity `new List<...>(B)` has B reference slots; both descriptor-set element types are classes. Zero capacity uses shared empty storage. | List controls, descriptor creation/retention and native descriptor budgets are separate. Backend uncertainty lists use `EnsureCapacity(current Count + B)`; retained old capacity and resize overlap need their own owner plan, not `B * referenceStride` alone. |
| D3D12 render-target preparation | A growing ID list/set discovers distinct non-depth attachment resources. The view dictionary requests that distinct count. The uncertain render-target-heap list can reserve `Count + 1`. | Distinct membership, actual list/hash capacities, wrapper/control storage and old/new overlap are not exported before backend entry. Native heap ownership is not a managed-scratch allowance. |
| Vulkan buffer/texture scopes | The graph-buffer tuple list requests `BufferImports.Length`. Current-buffer maps request that count; color/depth/sampled/storage/copy texture maps request `TextureImports.Length - 1`. | These are constructor inputs, not qualified dictionary capacities or tuple/control footprints. Overlapping color/sampled roles and their actual live owner groups must be enumerated. |
| Vulkan color-view/store preparation | A growing color-ID list/set computes distinct membership. The image-view list requests that count; view/store/pass/content dictionaries request the distinct count, non-target texture count or pass count. | Actual capacities, struct strides/control bounds and retained/failure cleanup overlap remain unqualified. A reference-width guess cannot size value-type scope entries or native views. |

These facts support future planning inputs, not a complete numeric grant. No allocation probe, native diagnostic workload, bucket-layout reflection or placeholder warming is used to turn the requested counts into object sizes.

## 4. Payload aliases are a separate real owner boundary

Current source has this chain:

1. `RecordedPassCommandList.Commands` exposes its immutable command array.
2. Runtime copies that value into `GraphicsGraphSubmissionPass.Commands`; the pass is a readonly record struct.
3. Submission outer-array construction and later pass/array/value copies keep the same inner command backing. They are not deep command copies.
4. Both backends receive the submission and can invoke their existing submission observer before recording. Any retained pass/array/command alias remains a root independently of its original list field.

Safe disposal correctly clears the original list/submission fields; uncertain disposal correctly retains recording. **Neither revokes a previously copied immutable command array or pass value.** The focused fixture now retains those aliases across safe and quarantined cleanup and verifies that the original binding object remains reachable from them. This is a demonstrated limitation, not a new payload byte grant.

Select a **shared private payload owner around the existing backing**, with its already-funded managed interest retained by each real holder and only scoped borrows where an independent alias is not permitted. Before implementation:

- close the actual holder set: recorded list, Runtime handoff/pass values, submission, backend-local reads/observers, completed operations and quarantine;
- qualify the payload-owner control and every retained outer/container copy before constructing them; retain exactly one backing interest rather than charging the same array anew per reference;
- prevent an unaccompanied raw immutable-array escape from being treated as an ended borrow, or keep admission unavailable for that path;
- detach obsolete callback/recipe roots without claiming that all aliases died, and keep native use/fence ownership distinct from managed payload retention;
- transfer the same live payload/workspace interests through the preadmitted uncertain-work carrier/publication before releasing their old owner.

This is not permission to add a budget field to every command, compact commands, create a third deep command copy, extend native use leases merely to park a CPU charge, add a finalizer/weak-table registry, or assume an observer cannot retain its inputs. No such payload owner/control footprint currently exists.

## 5. Minimum dependent batch and retry gate

The missing capability is a **nonallocating complete prequeue workspace plan plus actual storage/alias ownership transfer**:

1. Runtime freezes full pass/access/import/alias/attachment/command/recipe membership and its still-retained handoff baseline. Graphics and each backend plan their own actual control/backing capacities under the existing dependency direction; no callback into Runtime/Core supplies a hidden size.
2. Before constructor/scratch allocation, graph return or queue acceptance, the same joint envelope funds all needed additional groups, their carriers and simultaneous growth/freeze overlap. Checked overflow, changed identity/capacity or any unknown owner keeps the whole diagnostic-bearing graph unavailable.
3. Repeated backend validation resets/reuses funded private workspace or uses an independently qualified reserved maximum. The native backend cannot allocate fresh unplanned maps after acceptance.
4. The qualified payload owner/borrow path closes raw aliases before any managed refund is possible; the same interests survive uncertainty through already-funded publication capacity.

After review, qualify only those concrete owners and storage shapes, then extend the existing Graphics/Runtime harness for exact-fit/one-byte-short **complete** workspace grants, changed membership/capacity, repeated validation, independently retained aliases and safe/uncertain terminal paths. Existing managed helpers do not establish native/AOT/public-consumer or complete-budget acceptance.

**Current disposition:** only the two List-backed scratch sites are removed. Maps/sets/backend growth, complete prequeue/refusal plans, payload-alias interests and quarantine remain unqualified. Existing request/TCS/async-return and completion gaps remain. The unchanged joint and completion gates remain **NO-GO**.

## 6. Frozen inputs

Whole-file SHA-256 values identify the narrow dirty-workspace input set based on HEAD `2231717f82681c7a3120b62c079b9a9d111333a1`. HEAD alone is not the reviewed input. Recheck before applying a disposition. These are static owner/fixture inputs, not object-footprint or native qualification.

| Input | SHA-256 |
|---|---|
| `global.json` | `1BCE01871321C2DF82E887DA428CE5C121A6C6541FE65017FE3D249BE47E190C` |
| `docs\proposals\m05-skinned-normal-diagnostic-recording-workspace-admission-addendum.md` | `BC52399DE52D48A5425BD36D7ECC6D132B7BA3C9D792040F2FA9ABF8BCB24CE8` |
| `docs\proposals\m05-skinned-normal-diagnostic-async-return-ownership-addendum.md` | `F5A8A8043A3E8677CEC3F18AAC1F85994A13FC25C6CFA1BA7E3E72E5B5E60991` |
| `sources\TerraFX.Graphics\GraphicsGraphSubmission.cs` | `4881313549CD786A3D8FBC91196B168F085ED6245E784976BA5C2C3DB372E0BC` |
| `sources\TerraFX.Graphics\GraphResourceAccess.cs` | `2B7E9BDD2F8E4CCB78645A1F0D3C4B0A37234BC4FDE96D5ED0B9810497D0168E` |
| `sources\TerraFX.Graphics\GraphicsGraphSubmissionPass.cs` | `4BE4B04419FAD2F72C791358A8D7A784367E748D4F4A8037725AD1B35FDB645B` |
| `sources\TerraFX.Graphics\RecordedPassCommandList.cs` | `36C99EEC7846E37DB528F69421B79D957BBDBAC5B11A4A83E6D62F837AC25571` |
| `sources\TerraFX.Graphics\RecordedGraphicsCommand.cs` | `346862C8D8FFFCE1A264E7EA970B0B1404CABAB75753CA1C3CA12B140A082A9A` |
| `sources\TerraFX.Graphics\SkinnedNormalDiagnosticRecordingRecipe.cs` | `FCEA487B4B8E92FC46C11FEEF4C8A4080BD96356A8189A2E9A182BE25F0CCF26` |
| `sources\TerraFX.Graphics\GraphicsDevice.SubmitGraph.cs` | `C68E23B5FFA591C639714101FB23A1F1AE883FAC28F722EEB5B034725C3A97FD` |
| `sources\TerraFX.Graphics.D3D12\D3D12GraphicsBackend.cs` | `3A78D1E2A78ABAEDCD1CFCBA2AC685DD85B5995A1A0808AEDAB78E1EFE702239` |
| `sources\TerraFX.Graphics.Vulkan\VulkanBackendDevice.GraphSubmission.cs` | `8182B1C554B908812F32926B1E16E3222378816E68A9FE16C28E1C549EDBA19A` |
| `sources\TerraFX.Graphics.Vulkan\VulkanBackendDescriptorSet.cs` | `FB3F31180F5AEA6BF87296B6F2E2C866152D4CCC074B590A578EE4D7AAE7AB8F` |
| `sources\TerraFX.Runtime\Rendering\RenderGraphPlan.Submission.cs` | `34AB6687DED10903F40453441F3158F78DC4D8C78D82982863CADE038870525C` |
| `tests\TerraFX.Graphics.Tests\GraphicsBufferTransferTests.cs` | `C304113A8308C9C77BC9B18CD951E41E90F68986FAF332203CFD67C6021F2103` |
