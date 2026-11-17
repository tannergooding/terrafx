# Skinned-normal diagnostic Frame bindings

## 1. Scope and correction

**Proposed correction only; not implementation or qualification.** This addendum applies to `docs\proposals\m05-skinned-normal-diagnostic-delivery.md`, SHA-256 `1D9ED2736D1C2520712D1E85A75AC5F0CF81D58C1EA48158F284B24B07C0DA3D`.

The original section 4 step 6 is incompatible with the frozen native compute path in **two** ways:

1. `GraphicsGraphSubmission.cs:1177–1208` requires every compute resource to use `ShaderBindingGroup.Frame`, not `Pass`.
2. The same check permits compute `StorageBuffer`/`StorageImage` resources, not `UniformBuffer`. Moving the original parameter `b0` to Frame would still fail. Both native compute recorders independently reject non-storage buffer bindings.

Use **four Frame-group storage buffers and no compute uniform buffer**. Preserve the four existing storage roles; prefix the existing membership view with the parameter uint4 instead of adding a fifth descriptor or widening M04 support. This replaces only the compute binding/parameter-location portions of the original section 4 steps 5–7.

The public `RenderSkinnedNormalDiagnostic` and receipt declarations, defaults, count membership, original submission fence identity, asynchronous completion/readback join, cancellation, disposal, independent pins, buffering/backpressure, failure/quarantine, and consumer lifetime remain unchanged. The detection threshold/fallback, raster ABI, real vertex/palette inputs, static-versus-animated evidence distinction, and capability/refusal gates also remain unchanged. This adds no public API, acceptance ID, native feature, or second skinning path.

Only this new proposal file is created. The original proposal, maintained contracts, source, and tests are not edited. No build, test, shader compilation, native/AOT execution, commit, push, or PR is performed.

### Frozen evidence

Full paths and hashes are in section 5.

| Evidence | Consequence |
|---|---|
| `GraphicsGraphSubmission.cs:1177–1208` checks compute group, kind, array count, stage, and storage access. | A Frame-only rename is necessary but insufficient while `b0` remains. Use compute-stage storage signatures, fixed descriptor count one, and the admitted access mode. |
| `D3D12GraphicsBackend.cs:2278–2377` and `VulkanBackendDevice.GraphSubmission.cs:1123–1200` convert compute buffer bindings only to storage descriptors and supply no uniform bindings. | Do not propose a validator bypass or assume general pipeline/reflection construction enables native compute uniforms. |
| `ShaderBindingGroup.cs`; `D3D12DescriptorLayout.GetRegisterSpace`; `VulkanBackendDevice.PipelineMappings.cs:84–107`; Chapter 17 section 4. | Managed `Frame = 1` maps to D3D12 **space 0** and Vulkan **set 0**. Storage class-local bindings 0–3 map to Vulkan 32–35. |
| `BufferBinding.cs`; `BindingSet.cs`; `GraphicsGraphSubmission.cs:1436–1495`; Chapter 19's paired storage declarations. | Bind exact, declared graph versions/ranges, with every reflected buffer supplied once. Native binding numbers are not values for `BufferBinding.Binding`. |
| D3D12 descriptor checks at `D3D12GraphicsBackend.cs:3431–3462` and UAV creation at `4040–4061`; Vulkan storage descriptors at `VulkanBackendDevice.SampledImageDescriptors.cs:210–283`. | The membership packet can use a checked storage-buffer subrange. The descriptor offset becomes the shader view's element zero; obey storage-offset alignment and the structured stride. |
| `EngineHostGraphSubmissionTests.cs:660–675,4117–4162` contains backend-selected storage-subrange and multiple Frame storage-buffer graph paths. `GraphicsPipelineRequestTests.cs:990–1030` records Frame storage bindings and storage barriers. | These are existing source patterns, not tests run here. The same managed request test also records a compute uniform binding earlier; isolated recording is not native graph acceptance. |
| Chapters 18/30 require actual bytecode/reflection/layout agreement; Chapter 35 separates declarations, implementation, native execution, and qualification. | Recompile and validate the corrected producer during later implementation. Relabeling old reflection is not evidence that its bytecode uses the corrected bindings. |

## 2. Exact corrected resource map

Use this compute layout instead of the original Pass/space-3/set-3 layout:

| Resource role | `ShaderBindingGroup` | Kind / access / descriptor count | Class-local binding | D3D12 HLSL register | Vulkan attribute | Structured stride |
|---|---|---|---:|---|---|---:|
| Actual packed Mesh vertices | `Frame` | `StorageBuffer` / `ReadWrite` / 1 | 0 | `register(u0, space0)` | `[[vk::binding(32, 0)]]` | 96 |
| Actual captured bone palette | `Frame` | `StorageBuffer` / `ReadWrite` / 1 | 1 | `register(u1, space0)` | `[[vk::binding(33, 0)]]` | 112 |
| Dispatch parameters plus packed vertex membership | `Frame` | `StorageBuffer` / `ReadWrite` / 1 | 2 | `register(u2, space0)` | `[[vk::binding(34, 0)]]` | 16 |
| Original diagnostic result rows | `Frame` | `StorageBuffer` / `ReadWrite` / 1 | 3 | `register(u3, space0)` | `[[vk::binding(35, 0)]]` | 16 |

There is **no `b0`/constant-buffer signature, binding, descriptor, or compute uniform allocation** in this corrected pipeline. There are no sampled images, storage images, or samplers. The vertex/palette/result byte layouts retain their original meanings. `ReadWrite` remains the current conservative storage signature even for operands the shader only loads; do not switch those operands to read-only `t` registers or invent narrower reflection to avoid existing validation.

The compute reflection's exact resource array is:

```csharp
[
    new ShaderResourceSignature(
        ShaderBindingGroup.Frame, 0, ShaderResourceKind.StorageBuffer,
        arrayCount: 1, access: ShaderResourceAccess.ReadWrite),
    new ShaderResourceSignature(
        ShaderBindingGroup.Frame, 1, ShaderResourceKind.StorageBuffer,
        arrayCount: 1, access: ShaderResourceAccess.ReadWrite),
    new ShaderResourceSignature(
        ShaderBindingGroup.Frame, 2, ShaderResourceKind.StorageBuffer,
        arrayCount: 1, access: ShaderResourceAccess.ReadWrite),
    new ShaderResourceSignature(
        ShaderBindingGroup.Frame, 3, ShaderResourceKind.StorageBuffer,
        arrayCount: 1, access: ShaderResourceAccess.ReadWrite),
]
```

Keep the original compute stage, empty stage I/O, local size `(64,1,1)`, and proposed compute binding-layout version 1. Reflect storage record strides 96/112/16/16 and the actual compiled records; there is no separate 16-byte uniform record. A single descriptor still refers to multiple structured elements; element count is not `ShaderResourceSignature.ArrayCount`. Non-image metadata remains not applicable, as established by the existing signature constructors.

Every corresponding `BufferBinding` uses `ShaderBindingGroup.Frame`, `ShaderResourceKind.StorageBuffer`, and `Binding = 0`, `1`, `2`, or `3`. Do **not** put 32–35 into its class-local `Binding`, use enum zero (`None`) as Frame, or use `space1` merely because the managed Frame enum equals one.

The four `RWStructuredBuffer` declarations use the registers/attributes in the table; guard Vulkan attributes with the existing `TERRAFX_SPIRV` convention. For example, the changed membership/parameter declaration and load are:

```hlsl
#if defined(TERRAFX_SPIRV)
[[vk::binding(34, 0)]]
#endif
RWStructuredBuffer<uint4> DiagnosticWork : register(u2, space0);

// Within the proposed diagnostic entry point:
uint4 parameters = DiagnosticWork[0];
```

`Frame` is the permitted resource group, not a requirement to supply a raster `FrameData` uniform. The compute pipeline's Frame storage layout is independent of raster Frame uniforms and ObjectData palette bindings. Do not change those raster bindings.

## 3. Parameters inside the membership view

For each logical draw, bind one packet from the existing membership storage owner at Frame `u2`. The packet consists of explicit 16-byte uint4 elements:

| View-relative byte offset | Contents |
|---:|---|
| 0 | `(resultRow, membershipFirst, membershipCount, paletteBase)` |
| 16 onward | Ascending unique vertex ordinals, four per uint4; unused lanes in the last element are zero |

The field order and meanings are preserved. For this per-draw view, `membershipFirst` is exactly **zero**: it is an ordinal offset into the packet's membership payload, not a byte offset or a global buffer offset. `membershipCount > 0` is the original tested-vertex count; `resultRow` names the existing frozen result-table row; `paletteBase` is the same captured palette base as before. Validate all values against the original retained geometry/palette/table before submission.

For ordinal position `i` in `[0, membershipCount)`, the proposed compute loop reads the vertex ordinal from:

```text
DiagnosticWork[1 + ((parameters.y + i) / 4)]
              [(parameters.y + i) % 4]
```

The leading **1** skips the parameter element. The shader does not read the header as vertex ordinals, sample parameters from a live World, or reconstruct a new palette. It still writes the unchanged uint4 `(degenerateCount, testedCount, invalidEvaluation, 0)` into the original result buffer at `parameters.x`. The result readback remains exactly `16 * resultRowCount` bytes; parameters are not added to public results or readback rows.

### Exact ranges and construction

For `n = membershipCount`, calculate using checked `long` arithmetic:

```text
packetBytes = 16 * (1 + ((n + 3) / 4))
alignment   = lcm(16, device.Capabilities.Limits.StorageBufferOffsetAlignment)
packetStart = alignUp(previousPacketEnd, alignment)
```

The first `previousPacketEnd` is zero; subsequent values are the checked prior `packetStart + packetBytes`. The packet backing remains DeviceLocal `Storage | CopyDestination`, with `StructuredStride = 16`. Pack the header and membership once from the original frozen draw table using the existing explicit uint packing. Initialize all payload tails and allocation/alignment gaps; no uninitialized stack/native bytes become packet contents.

For a dispatch, Frame `u2` binds the exact range `(packetStart, packetBytes)` within the current graph version. Do not bind the entire multi-packet owner while still indexing element zero: that would silently reuse the first draw's parameters. D3D12's `FirstElement = packetStart / 16` and Vulkan's `VkDescriptorBufferInfo.offset = packetStart` make the selected header view-relative element zero. Range and offset remain checked against actual buffer size, storage limits, and native alignment.

Declare the packet range through the existing `ReadWriteBuffer` rules and bind its returned version. Supply the complete four-buffer binding set after setting the compute pipeline for that dispatch; preserve the existing vertex/palette/result version chains and storage barriers. The membership/header code performs only loads, while its declared access remains the admitted conservative `ReadWrite`. No new descriptor-update or native command mechanism is required.

The old compute-uniform `lcm(256, uniform alignment)` allocation sentence no longer applies to these parameters. This changes neither raster uniform alignment nor any existing raster record.

Header and alignment bytes count against the original preparation/storage budgets before allocation. Keep all original ceilings and refusal channels; do not enlarge a budget, retain another owner, queue a result, or omit a draw to absorb the overhead. The packet uses the original membership buffer's submission pins, resource uses, barriers, and fence-gated retirement. This introduces no new lifetime interest or public disposal contract.

**Distinguishing packing example, not native evidence:** two draw packets have headers `(0,0,3,0)` and `(1,0,1,2)`. Their payloads are respectively `(2,4,9,0)` and `(3,0,0,0)`, for admitted corresponding geometry/palettes. Each packet is 32 bytes; the second starts at `alignUp(32, alignment)`. Binding its own view yields membership `{3}`, result row 1 and palette base 2, not the first packet's `{2,4,9}`, row 0 and base 0. No expected skinning result is derived from this packing example.

## 4. Compatibility and unchanged gates

The corrected shape satisfies the inspected **binding-group/kind** restriction without modifying `GraphicsGraphSubmission`, either backend, a public surface, or maintained contracts. Both native descriptor paths support the required storage subrange form by source inspection. Four storage descriptors and the 64-invocation group remain actual device-limit requirements, not guaranteed by a declaration or the minimum profile. No fifth storage descriptor, compute uniform support, additional descriptor group, vertex atomics, or enabled Vulkan feature is assumed.

The later implementation must update HLSL register space/Vulkan set, actual compiled reflection, pipeline layout, and recorder bindings together. A metadata-only `Pass` → `Frame` substitution is not sufficient. The corrected packet is a private ABI revision of a proposed, unimplemented diagnostic shader; it is not permission to relabel an already built DXIL/SPIR-V artifact.

All original public receipt and lifetime choices remain unchanged, including completion only after real graph/readback/map/copy completion, the original graph `CompletedFence`, post-acceptance cancellation behavior, scalar-only retained results, error/quarantine semantics, and existing host drain. The original shared normal helper, real animated producer requirement, threshold/fallback, static zero-count limitation, and public/native/AOT qualification gates remain.

The existing M04 tests cited above have been **read, not run**. Source/reflection compatibility is not a native observation. Later implementation still needs actual pinned-toolchain shader/reflection agreement and both real backend paths, including distinct packet subranges and original result/frame/draw correspondence. This addendum claims no test pass, GPU count, nonzero animated result, NativeAOT evidence, or milestone acceptance.

## 5. Frozen inputs and verification

HEAD is `31f156a51ccc462ae9c5cc8d4e87e00c0054dec4`; the index matched HEAD. The original diagnostic proposal is frozen as 62,032 bytes at the SHA-256 in section 1 and is not edited. This addendum does not erase its historical concurrent-input warning or accept its other proposed implementation prerequisites.

The request's initial Git status contained 41 entries: 30 tracked `.M` paths and 11 untracked paths. Canonical status SHA-256: `BE7F0786B81498305E9D8939BCAAC70C13DD70F514F9ADD6DEA15E12D67287B8`. Canonicalization is ordinal sorting of complete `git status --porcelain=v2 --untracked-files=all` lines, LF joining including one final LF, UTF-8 without BOM. Exclude only this new addendum at final comparison.

All 41 initial dirty files, including untracked proposals and the original diagnostic proposal, were also byte-frozen. Their canonical dirty-inventory SHA-256 is `1D5B71F591D6EFDDF1F986843511F0E9ADF01198DE85DE00224F14410B42A0A8`. The inventory sorts by repository-relative Windows path, ordinally; each line is `State<TAB>Path<TAB>DecimalByteLength<TAB>UppercaseFileSha256`, with state `M` or `U`, UTF-8 without BOM, LF and one final LF. This freezes surrounding dirty bytes without treating unrelated work as contract authority. Its initial values were independently rechecked at `2026-11-16T03:11:23.1315346Z`.

The 26 direct/pinned M04 and original-proposal inputs are below. SHA-256 covers whole on-disk files, not normalized text, selected excerpts, or HEAD blobs. `C` is tracked clean, `M` is unstaged modified, and `U` is untracked. No other M05 proposal is a contract dependency of this focused correction.

| Path | State | Bytes | SHA-256 |
|---|---|---:|---|
| `docs\proposals\m05-skinned-normal-diagnostic-delivery.md` | U | 62032 | `1D9ED2736D1C2520712D1E85A75AC5F0CF81D58C1EA48158F284B24B07C0DA3D` |
| `docs\design\17-graphics-device-and-resources.md` | C | 40823 | `8157E7DA5472B9AABD891A05E84F2B997487A5C5E3FF67CB589681747D628718` |
| `docs\design\18-shaders-and-material-pipeline.md` | C | 18074 | `AF20911D9D06BFB58F6C771511181F59347C4659129CFD5FC8CF622ED191C0D7` |
| `docs\design\19-render-graph-synchronization-and-presentation.md` | C | 26572 | `AB673DEDBD85B7A161C5D7DC871BF5B486911DC27EC4279AC43827B005241B8B` |
| `docs\design\30-public-api-review-surface.md` | M | 485241 | `15848C82F3858A77A87A0D3EA6FBCC07046EB6E77CA5B21748C72B90EFAB598B` |
| `docs\design\35-implementation-roadmap.md` | C | 30557 | `24EEC6E92B7AD904D552F290EF748FFC8FCE13B6B94A89EBD9F39487D4584D87` |
| `docs\project\Roadmap.md` | C | 7228 | `C4FBE82155A6D82FFD2D13AD2F18056D938738C4FA5A66760C4877E24ED001C7` |
| `sources\TerraFX.Graphics\GraphicsGraphSubmission.cs` | C | 131205 | `E691A06893DCA483C262B1B707519F5176161E33D877C0006F96022F7C7CD378` |
| `sources\TerraFX.Graphics\ShaderBindingGroup.cs` | C | 625 | `248C8D253A68FC1765D5202F93172D562E19B6D601904F6651AA1EB5439B5060` |
| `sources\TerraFX.Graphics\ShaderResourceSignature.cs` | C | 12293 | `ED145BF6509617F3E078871B90D598A1E03CD0C9A731BAAECA15F2C06BC6B2D3` |
| `sources\TerraFX.Graphics\ShaderReflection.cs` | C | 15319 | `5BEFEA6248E665B6DE75DBA935361CAB6C371B477CD42B3B32E652D770AE408D` |
| `sources\TerraFX.Graphics\BindingSet.cs` | C | 5492 | `7389057053DBACE10ADC2B491AFF8B4FD7CED34D6797F206578B26F56252606C` |
| `sources\TerraFX.Graphics\BufferBinding.cs` | C | 2888 | `98BCE317C28F2A14E0065FBF538899319AA2CAB755D884F922CCADBCF26B8513` |
| `sources\TerraFX.Graphics\GraphicsDevice.PipelineRecording.cs` | C | 1962 | `D8D90F02FEF58E08FB55E163286DE9D31DC6A28F40CD8657E34C43C4C1EF7C72` |
| `sources\TerraFX.Graphics.D3D12\D3D12GraphicsBackend.cs` | C | 288148 | `FB74F3CF8097695B0CB08D5C68F4262C253F6751062F2B3E403EE42ABB31977C` |
| `sources\TerraFX.Graphics.D3D12\D3D12DescriptorLayout.cs` | C | 2426 | `F7CB8EB19AA7E889FE2C740904BF6BD020A3CD1FDC3EB7DC8804C3EA0EDFE7FE` |
| `sources\TerraFX.Graphics.Vulkan\VulkanBackendDevice.GraphSubmission.cs` | C | 116897 | `8182B1C554B908812F32926B1E16E3222378816E68A9FE16C28E1C549EDBA19A` |
| `sources\TerraFX.Graphics.Vulkan\VulkanBackendDevice.PipelineMappings.cs` | C | 8266 | `878F24DA80BA4F13C333F55E566AEABFD26E3001F3FCC3F381EEC9B02B0F55D0` |
| `sources\TerraFX.Graphics.Vulkan\VulkanBackendDevice.PipelineDescriptors.cs` | C | 6643 | `0F127E22C479841842199B7053200BD416572B22B7C416CB9C5B772B4D7610A9` |
| `sources\TerraFX.Graphics.Vulkan\VulkanBackendDevice.DescriptorSets.cs` | C | 11071 | `E31327F9A93C2B5F9075915C22DDE3EEDDB768D5D691A5D28D0F2E59478099F0` |
| `sources\TerraFX.Graphics.Vulkan\VulkanBackendDevice.SampledImageDescriptors.cs` | C | 32421 | `4F855F243CE441851DBB6F7C7D13E70F8BFDA57AFBE6D8CCCA504FE0465BF431` |
| `tests\TerraFX.Graphics.Tests\GraphicsPipelineRequestTests.cs` | C | 84398 | `0861AB2F7035E5992AB59C8E8F49C5BB512738D1B08995AF60534E911D3157B3` |
| `tests\TerraFX.Graphics.Tests\GraphicsPipelineDescriptionTests.cs` | C | 23530 | `63F6E0C305F0D765B0144C0B7537C9195A16F84B31744447BA84229788E6B64A` |
| `tests\TerraFX.Graphics.Tests\ShaderReflectionTests.cs` | C | 19998 | `FEF5798BEFD5703E38E5925F08B75A62A548FD790BA7B71460CB7EA2210AA0B3` |
| `tests\TerraFX.Graphics.Tests\BindingSetTests.cs` | C | 6408 | `06165B1630BC3312CEB9EDBD7AE952793083E13489B33F2DECED2D99E15E8F20` |
| `tests\TerraFX.Runtime.Tests\Hosting\EngineHostGraphSubmissionTests.cs` | C | 332181 | `22ED89ACA9E6709710391AB44BBF3D88B10E919D96A347FBACE5ADF93567A63A` |

### Verification disposition

The post-draft comparison verified all 26 file hashes/lengths/states, unchanged original-proposal bytes, unchanged HEAD/index, canonical status, and the complete 41-file dirty-byte inventory, excluding only this new output. All matched; no concurrent-input mismatch was found for this addendum. Only this addendum was created. This verification is byte/state preservation, not build, test, native, or implementation acceptance evidence.
