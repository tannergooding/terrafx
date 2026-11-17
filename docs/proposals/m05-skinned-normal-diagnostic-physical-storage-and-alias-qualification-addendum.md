# M05 diagnostic physical-storage and element-alias qualification

## 1. Two-row disposition

**Tentative Sol owner/qualification decision, pending review. Neither remaining row is qualified for a production reservation or lifetime change. The full 32/16-MiB diagnostic gate remains NO-GO.** This makes the next qualification target concrete under [physical hash-capacity ownership](m05-skinned-normal-diagnostic-preflight-hash-capacity-owner-addendum.md).

| Row | Derivable from current production contracts? | Exact blocker / selected next owner |
|---|---|---|
| Physical preflight map/set storage | **No complete preconstruction recipe.** Constructor membership requests are known; actual hash backing/control/growth footprints are not supplied before allocation. | Start with the real `bufferImportsByResourceId` lookup. Select a Graphics-owned explicit import-index workspace, not a guessed BCL capacity formula or a profile table inferred from sampled logical counts. |
| Payload-alias ownership | **No complete transfer/retention contract.** Original list/submission roots detach safely, but independent array/pass/element/reference aliases remain. | Use the real recorded-command owner and close its complete holder/borrow protocol, including copied command referents and binding-array aliases, before claiming any backing release or refund. A span alone is insufficient. |

Preserve the **33,554,432-byte** joint preparation/recording and **16,777,216-byte** independent completion ceilings, complete membership, exact command/barrier schedule, four diagnostic bindings, command representation, Core readback and native/quarantine ownership. No new cardinality cap, public API, acceptance ID or Scene/native consumer is selected.

## 2. First physical-storage owner decision

The first target is the existing `Dictionary<ulong, GraphBufferImportRecord>(BufferImports.Length)` named `bufferImportsByResourceId`, not all validator/backend tables at once.

For the future private replacement, select an **explicit index backing over the frozen `BufferImports`**, owned by the Graphics preflight workspace:

- The concrete factory would explicitly allocate an `int[]` with one slot per import and publish its actual physical length, retaining the existing import records rather than copying commands or native payloads.
- Sort/search identity remains the actual `ulong ResourceId`; duplicate-import refusal, missing-resource behavior and alias-lifetime validation must retain their existing channels and order.
- An allocation written as `new int[n]` establishes that new representation's actual capacity. It says **nothing** about the current Dictionary's bucket/entry capacity. Its future array allowance uses the existing Q rule and actual int stride, not `n * referenceStride` or `OwnedBuffer.ByteLength`.
- The factory/control/grant, sorting/comparison controls, immutable input retention, construction overlap and reused/reset state must themselves be qualified before implementation. This document declares no existing factory or numeric total for them.

This is a storage-owner choice for review, not authority to replace the current map with an unmeasured quadratic scan or to charge an index-array estimate while still constructing the old Dictionary. Other usage maps/sets and backend scope/descriptor/view groups remain separate.

Both backends repeat validation after owner-queue acceptance. The future workspace must therefore be retained and safely reset/reused, or its real repeated construction must be funded before acceptance. Mutable backend uncertainty lists additionally need an owner-synchronized reservation; reading Count/Capacity on the submitting thread is not sufficient.

## 3. Concrete physical-storage qualification record

Use the existing owning Graphics harness and the **real production workspace factory after its owner contract is closed**, not a second map implementation, test-only producer, placeholder warm-up or native workload.

For each construction/growth/transfer point record:

| Required observation | Purpose |
|---|---|
| Frozen owner/device generation and complete key/member identities | Independently establishes the exact lookup input; a count alone is not membership. |
| Planned backing type/capacity, actual backing identity and physical array length/stride | Proves the factory constructed the planned storage, not merely a logical Count. |
| Complete source-owned control/interest group and exact target/compiler/runtime/configuration | Supplies the separately qualified wrapper/control bound; no guessed object overhead. |
| Simultaneously retained input, old/new, freeze and handoff backings | Prevents a resize/reset or copied outer container from hiding overlap. |
| Current owner, transfer recipient and actual safe/uncertain terminal condition | Distinguishes count-slot release from storage release. |
| Cold initialization and warmed construction allocation observations separately | Tests the derived bound; warmed deltas do not erase cold controls. |

Begin with literal empty/single/import-permutation/duplicate/sparse-resource-ID inputs through the owning graph-resource path. Verify the independently expected keys and refusal cases. Exercise repeated validation against the same frozen inputs and invalidation on changed membership/generation.

`GC.GetAllocatedBytesForCurrentThread` may verify an isolated real construction group's allocation bound; it is not the expected-value source and does not size other threads, retained aliases or native work. Public Capacity/`EnsureCapacity(0)` observations can help characterize existing BCL storage **after construction**, but cannot authorize a production preclaim. If the owner-controlled group's complete bounds cannot be established, leave it unavailable.

No qualification measurement is performed by this addendum. The current factories do not expose the selected workspace/retention capability, so adding a positive byte claim now would be retroactive or incomplete.

## 4. The element-alias boundary a span cannot close

The successful shader-metadata borrow copies `ShaderResourceSignature` values with no managed payload-reference fields. The command payload differs:

- `RecordedGraphicsCommand` contains references including `BindingSet`, pipeline owners and graph versions.
- A caller can copy a command value out of a scoped span and retain its `Bindings` reference. Scoped span safety does not revoke that ordinary managed reference; [Chapter 02](../design/02-dotnet-api-memory-and-ownership.md) explicitly distinguishes these lifetimes.
- `BindingSet` exposes immutable buffer/texture/sampler arrays. A retained array value can outlive the particular `BindingSet` reference that originally exposed it, and its elements retain their own objects.
- `SkinnedNormalDiagnosticRecordingRecipe` separately stores `_verifiedCommands` and requires **that exact completed command backing** at native validation. Replacing array access with an owner reference must preserve this witness, not silently accept an equal-looking copy.

Thus replacing a raw command getter with `ReadOnlySpan<RecordedGraphicsCommand>` does not by itself establish the complete payload holder set or a reusable byte credit. Reference width does not size the retained object/backing graph.

The selected next ownership target is the **existing `RecordedPassCommandList` as the command-backing owner**, subject to qualification of its real controls and any additional borrow state. Retained submission/pass holders must retain that owner/interest, rather than only an unaccompanied raw array. Each longer-lived binding/child-array/reference alias must likewise have an established owner interest or remain inadmissible for this diagnostic path. Do not assume one parent wrapper dominates all child lifetimes.

The required publication order is:

1. Pre-admit the complete owner/control/backing group, including constructor/freeze overlap.
2. Freeze and verify the actual command sequence once.
3. Bind its exact backing/owner identity to the recipe and recorded owner before publication.
4. Transfer existing owner interests into Runtime pass/submission holders; any synchronous borrow must keep its actual owner alive and charged through the borrow.
5. On safe cleanup, detach only the roots whose consumption has ended. On uncertainty, retain/transfer the same backing, owner, uses and interests before releasing the old holder, through preadmitted quarantine publication.

Current code does not implement that sequence for all aliases. Existing public immutable binding-array behavior cannot be treated as revoked. Opaque backend observers must supply a qualified retained-owner handoff or remain an unqualified participant; merely returning from their callback does not prove that they retained nothing.

## 5. Alias qualification matrix and stop condition

Extend the existing owning alias fixture with real nonempty bindings from its graph-resource owners, not fabricated snapshots or a placeholder payload:

| Retained witness | Required result |
|---|---|
| Recorded owner, outer pass array, copied pass value and raw command-array value retained independently | One physical command backing; no refund from original-field detachment alone. |
| Copied command value / `BindingSet` reference | Referent lifetimes remain accounted even if the command-array holder disappears. |
| Binding array retained independently of its exposing parent | Its actual backing and element owners remain accounted; logical length/reference count is not release evidence. |
| Active scoped borrow during safe cleanup | Owner/storage interest lasts through actual consumption; span syntax alone is not the ownership proof. |
| Each retained witness under uncertain-work retention | Same payload/control interests remain with the established quarantine owner; no new uncharged carrier/copy or count-slot refund. |
| Recipe/native handoff | Exact original completed backing identity, bindings and schedule remain required; no third command copy or result truncation. |

Compare actual backing identities and physical lengths in the owning harness; derive expected membership and byte bounds independently from the qualified owner contract. Do not use forced GC as a production reuse rule, add per-command receipt fields, expand the Core buffer allowance, change shader ABI, or suppress tests to manufacture acceptance.

**Current implementation disposition:** no source/test change is supported by a qualified complete row yet, and no additional independent reservation/borrow fix is established in this examined slice. This is a proposal-only blocker. Once a dependent implementation is supported, run focused owning Graphics/Runtime Debug then Release with nonzero executions and exact final-input evidence. Existing metadata-borrow results are not fresh physical-storage or payload-lifetime qualification.

Physical map/set plans, alias/control transfer, backend retained capacity, quarantine publication, request/return controls and completion terms all remain **NO-GO**.

## 6. Frozen inputs

Whole-file SHA-256 values identify the narrow dirty-workspace inputs based on HEAD `e41ea6c5cde3352b7136488816eb9bae1fab23e0`. HEAD alone is not the reviewed input. Recheck before applying a disposition. These are static owner/contract inputs, not new allocation or native measurements.

| Input | SHA-256 |
|---|---|
| `global.json` | `1BCE01871321C2DF82E887DA428CE5C121A6C6541FE65017FE3D249BE47E190C` |
| `docs\design\02-dotnet-api-memory-and-ownership.md` | `40F913A50434209E09941C22625A6D0722202BAB97EBAC0E153C1EA3368E2291` |
| `docs\proposals\m05-skinned-normal-diagnostic-preflight-hash-capacity-owner-addendum.md` | `B8EB3763F8788E539D86A57431025354AA87A014C1A2B241B0582D8E49BBC3E7` |
| `sources\TerraFX.Graphics\GraphicsGraphSubmission.cs` | `7C9AC1EDE5C4FD825A806D6BA9829BBA92BF69265EF463CC76EAC3F99FCD3CE3` |
| `sources\TerraFX.Graphics\RecordedPassCommandList.cs` | `36C99EEC7846E37DB528F69421B79D957BBDBAC5B11A4A83E6D62F837AC25571` |
| `sources\TerraFX.Graphics\RecordedGraphicsCommand.cs` | `346862C8D8FFFCE1A264E7EA970B0B1404CABAB75753CA1C3CA12B140A082A9A` |
| `sources\TerraFX.Graphics\BindingSet.cs` | `7389057053DBACE10ADC2B491AFF8B4FD7CED34D6797F206578B26F56252606C` |
| `sources\TerraFX.Graphics\CommandRecorderState.cs` | `8154B152867064FF89D9B74D6F0639EF593A9F475A6D2C783B8E34F9C4287AFA` |
| `sources\TerraFX.Graphics\SkinnedNormalDiagnosticRecordingRecipe.cs` | `FCEA487B4B8E92FC46C11FEEF4C8A4080BD96356A8189A2E9A182BE25F0CCF26` |
| `sources\TerraFX.Graphics\GraphicsGraphSubmissionPass.cs` | `4BE4B04419FAD2F72C791358A8D7A784367E748D4F4A8037725AD1B35FDB645B` |
| `sources\TerraFX.Graphics\GraphicsDevice.SubmitGraph.cs` | `C68E23B5FFA591C639714101FB23A1F1AE883FAC28F722EEB5B034725C3A97FD` |
| `sources\TerraFX.Graphics.D3D12\D3D12GraphicsBackend.cs` | `3A78D1E2A78ABAEDCD1CFCBA2AC685DD85B5995A1A0808AEDAB78E1EFE702239` |
| `sources\TerraFX.Graphics.Vulkan\VulkanBackendDevice.GraphSubmission.cs` | `8182B1C554B908812F32926B1E16E3222378816E68A9FE16C28E1C549EDBA19A` |
| `sources\TerraFX.Runtime\Rendering\RenderGraphPlan.Submission.cs` | `34AB6687DED10903F40453441F3158F78DC4D8C78D82982863CADE038870525C` |
| `tests\TerraFX.Graphics.Tests\GraphicsBufferTransferTests.cs` | `C304113A8308C9C77BC9B18CD951E41E90F68986FAF332203CFD67C6021F2103` |
