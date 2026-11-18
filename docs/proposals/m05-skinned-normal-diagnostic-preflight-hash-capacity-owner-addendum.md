# M05 diagnostic physical hash-capacity owner addendum

## 1. Focused decision and unchanged gate

**Tentative implementation guidance, pending review. Physical map/set workspace and command-payload alias admission remain NO-GO.** This narrows the next owner capability in [preflight/alias workspace](m05-skinned-normal-diagnostic-preflight-alias-workspace-addendum.md); it does not turn its constructor counts into byte grants.

Select **owner-issued physical storage recipes, enforced before allocation and queue acceptance**, for remaining preflight/backend collections. A recipe must identify actual backing capacities and control groups, not merely requested element counts or a `Capacity` observation obtained after allocating them. Mutable backend-owned retention storage additionally needs an owner-synchronized reservation/transfer; a producer-thread reading of current Count/Capacity is not that handoff.

One independent set can instead be removed: enumerate distinct signatures by borrowing the existing pipeline reflection arrays. This supplies no new positive allowance, collection cap, command/payload copy, public API, diagnostic ID or native workload. Keep the **33,554,432-byte** joint and **16,777,216-byte** independent completion ceilings and the whole-graph budget gate **NO-GO**.

## 2. Exact supported metadata borrow

The current owners already provide the complete source data:

- `GpuPipeline` retains `PipelineBindingLayout`.
- The layout retains its immutable `ShaderReflections` array. Each `ShaderReflection` owns immutable, validated resource metadata and rejects repeated register identities within that reflection.
- `ShaderResourceSignature` equality includes group, binding, kind, array count, access, storage-image format, image dimension and image arraying. A shared group/binding key alone is not equality. `GraphicsPipelineDescription` already rejects incompatible metadata at a shared cross-stage register before layout construction.

The bounded batch replaces only preflight's per-`SetPipeline` `HashSet<ShaderResourceSignature>` with `PipelineBindingLayout.EnumerateDistinctResources`:

1. A stack-only ref-struct enumerator holds a `ReadOnlySpan<ShaderReflection>` over the real layout array; it allocates no union array, set, comparer, wrapper or new persistent owner.
2. Resources are yielded in reflection/resource first-occurrence order. For each candidate, earlier sorted reflections are searched using the owner's `CompareResourceIdentity`, then checked with full signature equality. Their validated per-reflection uniqueness closes the remaining duplicate case. Empty reflection arrays/stages are valid.
3. Enumeration copies only `ShaderResourceSignature` values. Those values have no managed payload-reference fields. This is a scoped metadata borrow, not a way to export or fund a command/access backing.
4. Preflight still validates all reflected resources before populating its existing binding collections. It does not merge conflicting nonidentical signatures, remove commands, alter binding membership, change the existing error checks or skip repeated backend validation.

The owner identity comparison uses the enum's underlying integer comparison rather than `Enum.CompareTo(object)`, avoiding boxing at this borrowed lookup. `ShaderBindingGroup` has explicit integer identities 0 through 4; ordering and identity are unchanged. This removes an observed allocation source instead of assigning it an object tariff.

The removed reflected-resource set/control/backing term is **absent**, not sized from the number of shader resources. The view handles the actual reflection arrays without assuming four resources or a fixed shader count. It trades a temporary hash table for owner-defined identity lookup and equality checks; no native throughput claim follows from the managed allocation observation.

This does not qualify the existing layout/reflection construction controls or all managed allocations in full preflight. It also does not replace the independently implemented request/pending-submission backing rows or Core readback ownership.

## 3. First remaining physical-capacity boundary

The inventory remains in the parent addendum. The exact unresolved distinction is:

| Existing operation | What it establishes | What it does not establish |
|---|---|---|
| `new Dictionary(... requestedCount)` or an unsized `HashSet`/dictionary | A constructor request or a growing logical membership. | Actual bucket/entry capacities, object controls, comparer/initialization storage, old/new resize overlap or their complete phase peak before the allocation. |
| Querying actual Capacity/`EnsureCapacity(0)` after construction | At most the current storage state of that already-created collection. | A preconstruction reservation; it cannot retroactively fund controls or a backing allocated earlier. |
| Backend-local positive-capacity List construction | Its explicit constructor capacity can identify that list's backing slots. | The List object, element object/value layout, later shared retention growth or the complete backend workspace grant. Do not equate references with descriptor/view/scope objects. |
| `EnsureCapacity(current Count + incoming)` on backend uncertainty storage | A growth request against mutable owner state. | A race-free producer-side reservation, old/new capacity overlap or the release/transfer of existing retained entries. |

Shared Graphics validation runs prequeue, but both native backends validate again inside backend submission. Per-call maps and owner-retained uncertainty storage therefore have different planning/retention frontiers. Native owner affinity cannot be bypassed by reading mutable backend state from the submitting thread. Do not add a second unbudgeted owner-queue probe/wait simply to discover it.

The minimum missing private capability is an **owner-issued nonallocating construction/retention recipe** that:

- freezes complete actual graph/resource/pass/binding membership and the relevant owner generation;
- enumerates each physical backing and object/control group, including initialization, growth/freeze overlap and failure/quarantine storage;
- reserves those groups within the same still-retained joint envelope before their first allocation and before queue/native acceptance;
- either reuses already-funded immutable/fixed workspace for repeated validation or funds the actual repeated construction;
- synchronizes any mutable owner-retained capacity reservation and transfers the same interest with its actual entries through release/quarantine.

If existing BCL collections cannot supply such a recipe, a separately qualified private storage owner/representation is required before that row can proceed. Do not port private BCL bucket rules as an unexplained sizing formula, reflect hidden layout in production, warm placeholder entries, invent a load factor/cap, or guess an object overhead. No such plan/reservation capability is available in this batch.

## 4. Metadata borrowing does not close command-payload aliases

The new view borrows pipeline metadata synchronously. It neither owns nor revokes `RecordedPassCommandList.Commands`, copied `GraphicsGraphSubmissionPass` values, their immutable command arrays or backend observer aliases.

The existing alias fixture demonstrates that those aliases still retain their binding object after safe original-field detachment; uncertain cleanup retains the original recording too. No credit can be refunded because a metadata enumeration ended, a list detached, a Task completed or a command payload reference became unreachable from one particular owner.

The parent addendum's **shared private payload owner/borrow handoff around the existing backing** is still required. Its control footprint, retained holder set, raw-array escapes, Runtime handoff and preadmitted quarantine publication remain unqualified. Do not substitute reference width or `OwnedBuffer.ByteLength` for owned backing, add a per-command receipt field, deep-copy the command stream, or extend native resource uses merely to park a CPU charge.

## 5. Bounded next batch and qualification

After review, qualify one concrete remaining map/set storage group and its actual owner recipe before changing its construction. Do not present an exact metadata view or one known array as the complete preflight plan.

Owning managed evidence for the independent view covers real Graphics-created supported compute/graphics pipelines, cross-stage duplicates, first-occurrence order, empty stages, existing shared-metadata refusal before borrowing and zero incremental allocation at the measured warmed enumeration. These are not native/Scene, NativeAOT, public-consumer or full-ledger observations.

Dependent workspace tests still require exact-fit/one-byte-short complete grants with a retained baseline, actual physical capacity/overlap, changed membership/generation, repeated validation and safe/uncertain interest transfer. Payload alias qualification requires real shared ownership, not merely another root-clearing assertion.

**Current disposition:** seven exact-capacity sorted arrays index buffer/texture imports, ordered passes, alias-after resources, pass validation, and `(PassId, ResourceId)` accesses. Per-call flags replace four pass-ID sets, twelve imported-resource-use sets, and six binding-pair sets. Color/depth attachment membership uses the existing pass attachment records, and duplicate color detection scans prior attachments within the fixed attachment limit, avoiding three additional sets. Buffer and texture import classification replace all six dictionaries of each type with two import-indexed `ushort[]` arrays; category bits preserve duplicate-key refusal. Pipeline-reflection metadata is borrowed through `PipelineBindingLayout.EnumerateDistinctResources`, and `GraphicsGraphSubmission` no longer constructs HashSet/Dictionary tables. The constructor workspace precheck charges all seven indexes and four per-call flag arrays from access/import/pass counts. Exact-fit/one-byte-short admission, duplicate copy-access use, and duplicate nontransient texture-map refusal regressions pass; the full `TerraFX.Graphics.Tests` suite passes 301/301 in Debug and Release. D3D12 passes 43/43 and Vulkan 92 passed / 18 skipped in both configurations. This bounds only the named Graphics preflight backings; it does not provide the complete validation workspace or whole-graph grant. Remaining physical hash/control plans, backend shared-capacity reservations, complete prequeue refusal, command-payload alias ownership, quarantine, and previous request/return/completion gaps remain **NO-GO**.

## 6. Frozen inputs

Whole-file SHA-256 values identify the narrow dirty-workspace inputs based on HEAD `5cc23de57fa25de0b5c7c1dae387762a21a71f8f`. HEAD alone is not the reviewed input. Recheck before applying a disposition. These are static owner/fixture inputs, not hash-object/native qualification.

| Input | SHA-256 |
|---|---|
| `global.json` | `1BCE01871321C2DF82E887DA428CE5C121A6C6541FE65017FE3D249BE47E190C` |
| `docs\proposals\m05-skinned-normal-diagnostic-preflight-alias-workspace-addendum.md` | `AE89901571E050F4B9F29997F7B3559B15021396F4C0BFC360A7FB05CE83BB3B` |
| `sources\TerraFX.Graphics\PipelineBindingLayout.cs` | `6638EAAB807C576F4A77C00849056298B5EA4D9ACE51B85F8DDBA79C088BA33C` |
| `sources\TerraFX.Graphics\GpuPipeline.cs` | `63485A46600569093637FD7753FA7D4D9D351EC502DB08A05F02018880C2189C` |
| `sources\TerraFX.Graphics\ShaderReflection.cs` | `8862ADEC313B312FBE11F8A17E2405632693703494BEB30AE05C130AC16C77D2` |
| `sources\TerraFX.Graphics\ShaderResourceSignature.cs` | `ED145BF6509617F3E078871B90D598A1E03CD0C9A731BAAECA15F2C06BC6B2D3` |
| `sources\TerraFX.Graphics\GraphicsGraphSubmission.cs` | `7C9AC1EDE5C4FD825A806D6BA9829BBA92BF69265EF463CC76EAC3F99FCD3CE3` |
| `sources\TerraFX.Graphics\GraphicsDevice.SubmitGraph.cs` | `C68E23B5FFA591C639714101FB23A1F1AE883FAC28F722EEB5B034725C3A97FD` |
| `sources\TerraFX.Graphics\RecordedPassCommandList.cs` | `36C99EEC7846E37DB528F69421B79D957BBDBAC5B11A4A83E6D62F837AC25571` |
| `sources\TerraFX.Graphics\GraphicsGraphSubmissionPass.cs` | `4BE4B04419FAD2F72C791358A8D7A784367E748D4F4A8037725AD1B35FDB645B` |
| `sources\TerraFX.Graphics.D3D12\D3D12GraphicsBackend.cs` | `3A78D1E2A78ABAEDCD1CFCBA2AC685DD85B5995A1A0808AEDAB78E1EFE702239` |
| `sources\TerraFX.Graphics.Vulkan\VulkanBackendDevice.GraphSubmission.cs` | `8182B1C554B908812F32926B1E16E3222378816E68A9FE16C28E1C549EDBA19A` |
| `sources\TerraFX.Runtime\Rendering\RenderGraphPlan.Submission.cs` | `34AB6687DED10903F40453441F3158F78DC4D8C78D82982863CADE038870525C` |
| `tests\TerraFX.Graphics.Tests\GraphicsPipelineRequestTests.cs` | `098FB1C79989C9356E8F1EAB175EDF6139D9BC95347E672E95411294EC20D3CC` |
| `sources\TerraFX.Graphics\GraphicsPipelineDescription.cs` | `FBB9388C6E7C89CD4BE3FA0AB915DDFAA38DCA8659ADDC74A32CCE788C859A29` |
| `sources\TerraFX.Graphics\ShaderBindingGroup.cs` | `248C8D253A68FC1765D5202F93172D562E19B6D601904F6651AA1EB5439B5060` |

## 7. Implementation delta after the frozen assessment

Commit `45cf8551` adds exact-capacity sorted indexes for buffer imports, texture imports, and ordered passes, replacing `textureImportsByResourceId` and `orderedPassIndices`. Commit `d0abb56b` adds exact-capacity indexes for buffer and texture alias-barrier after-resource IDs, replacing two per-validation `HashSet<ulong>` instances. The five arrays use one checked preallocation charge and retain first-original-ordinal behavior; diagnostic-bearing submissions keep the existing 32-MiB component ceiling.

The full `TerraFX.Graphics.Tests` suite passed 299/299 in Debug and Release; focused index tests passed 17/17 in both. The D3D12 backend suite passed 43/43 and the Vulkan backend suite passed 92 tests with 18 skips in Debug and Release. The component tests cover exact-fit/one-byte-short charge, sparse and unsorted identities, zero-allocation lookup, duplicate refusal and safe/uncertain index retention. These checks do not qualify the whole-graph budget or a diagnostic consumer/native observation. Section 6 remains the historical freeze for the original owner decision; its hashes do not review these later implementation bytes. Remaining maps/sets, backend-retained capacity, owner controls, payload aliases, quarantine, and completion storage remain NO-GO.
