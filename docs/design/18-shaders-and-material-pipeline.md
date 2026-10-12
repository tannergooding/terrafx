# Shader compilation, reflection, and layouts

[Terminology and reading conventions](01-terminology-and-reading-guide.md).

## 1. Sources and toolchain

High-Level Shading Language (HLSL) is source-of-truth; offline DirectX Shader Compiler (DXC) creates DirectX Intermediate Language (DXIL) and SPIR-V from the same portable source. Pin release v1.8.2505.1, binary/`NuGet` version 1.8.2505.32, with artifact SHA256 and upstream licenses in build manifest. These version strings differ intentionally. Target VS/PS/CS6.0 for core, Vulkan 1.3 SPIR-V environment; no preview shader model, platform-specific authoring language or runtime compiler in release player.

Compile key: source/transitive include bytes + paths, entry/stage/target, sorted defines, compiler binary digest/version, backend flags, layout schema, numerical/coordinate profile `LeftHandedYUpForwardZ1`. Include resolution is restricted to declared asset roots and normalized stable logical names; include cycles, depth > 64, or aggregate source > 4 MiB reject. Offline tools use a configurable 30-second timeout and [11](11-assets-import-streaming-and-packages.md#3-runtime-tickets-and-leases)'s common bounded process-output/termination protocol, including its aggregate raw-byte limit and `ToolOutputLimitExceeded` failure.

Default optimized release and separate debug artifacts; IEEE-sensitive algorithms disable contraction/reassociation in Strict shaders explicitly where supported. Graphics processing unit (GPU) shader numerical results are not canonical physics state. Successful process exit without required binary/reflection output is a build failure.

Vulkan uses explicit `[[vk::binding]]` mapping from [Chapter 17](17-graphics-device-and-resources.md) and stable `[[vk::location]]` for user stage I/O. Do not rely on register classes collapsing to identical Vulkan binding numbers. DXIL registers remain b/t/u/s per space; duplicate/cross-stage binding/type mismatches fail pipeline import.

## 2. Portable data layout

Uniform/storage records use 16-byte slots of `float4`/`uint4`/`int4` and `row_major` float4x4; matrices use four 16-byte rows. No `bool`, native pointer, implicit `Vector3` padding, or Common Language Runtime (CLR) struct-layout dependency is allowed. Scalar metadata may share one explicitly packed `uint4`/`float4` slot. Array strides are 16 bytes or a larger 16-byte-aligned value; nested structs flatten to named slots. Vertex buffers have a separate declared format/byte-offset schema and may use packed `float3`, half, or normalized bytes supported by format limits.

Central processing unit (CPU) semantic geometry is chapter [06](06-owned-geometry-and-numerics-interchange.md)'s `TerraFX.Numerics` family. Generated packers consume copied rows/components, not mutable `Mij` fields or an arbitrary whole-struct bitcast. `EngineGeometryBinary32Rows2` joins the layout/build key independently of `LeftHandedYUpForwardZ1`. `Matrix4x4`'s 64 bytes happen to match four `float4` rows; `Vector3`'s 12-byte stride does not match a uniform `float4` slot. Base class library (BCL) interchange is not shader-layout conversion.

The required `ObjectGpuData` layout places `World` at byte offsets 0 through 63 inclusive, `PreviousWorld` at 64 through 127 inclusive, and three `NormalRows` at 128, 144, and 160. `Metadata` is a `uint4` at 176; data size is 192 bytes. A semantic `Vector3` expands to a declared `float4`, with W = 0 for a direction and W = 1 for a position. Uniform allocation stride is `alignUp(192, lcm(256, VulkanAlignment))`; it does not change data size.

Source generator emits explicit packers/field offsets, not unsafe cast of arbitrary public struct. Use `BinaryPrimitives` for serialized data and native-endian `float` bit packing into owned upload bytes; baseline native platforms little endian. Shader reflection must agree DXIL/SPIR-V on every offset/stride/matrix-major/type/count. Vulkan standard layout mode (DXC gl-layout) plus 16-byte slot subset avoids requiring scalar-block-layout/DX-layout extensions.

Material parameter defaults are typed schema values; a missing required field fails import, optional field takes documented default with schema version. A `float3` material parameter occupies a declared `float4` slot; edits create immutable material version at boundary, not overwrite submitted uniform data.

#### M03 7.1 Complete material profile operands

Profile keys and slot assignments (all semantic version 1):

```text
Sprite profile: material/profile/sprite-premul/v1
  Shader: material/shader/sprite-unlit/v1
  Pass: material/pass/sprite/v1
  Layout: material/layout/sprite/v1
  Parameters: native/material-sprite-parameters
PBR profile: material/profile/pbr-metallic-roughness/v1
  Shader: material/shader/pbr-metallic-roughness/v1
  Pass: material/pass/pbr/v1
  Layout: material/layout/pbr/v1
  Parameters: native/material-pbr-parameters
```

Every assignment uses S(key)/NameRecipe with namespace/key/code present and LiteralAssignmentDigest absent. Bindings are slots 1/2/3 ascending. Unknown/mixed profile/shader/pass/layout tuples refuse even if values look equivalent.

```text
Vertex attributes: Position=1 Normal=2 Tangent=4 UV0=8 UV1=16
                   Color=32 Joints=64 Weights=128
Capabilities: Texture2D=1 VertexColor=2 AlphaMask=4 PremultipliedBlend=8
              ReversedZDepth=16 FourWeightSkin=32 NormalMap=64
              MetallicRoughness=128 ForwardLights=256
Alpha mode: Opaque=0 Mask=1 Blend=2; AlphaBit(mode)=1<<mode
Cull: None=0 Back=1
OutputAlpha: Straight=0 Premultiplied=1
```

Profile rows:

| Profile | Base capabilities | Default alpha | Depth test | Depth-write alpha mask | Cull | Required attributes | Allowed alpha mask |
|---|---:|---:|---|---:|---:|---:|---:|
| Sprite | 0x001 | 2 | false | 0 | 0 | 0x009 | 7 |
| PBR | 0x191 | 0 | true | 3 | 1 | 0x003 | 7 |

Actual material RequiredCapabilities equals base OR AlphaMask for Mask OR PremultipliedBlend for Blend OR NormalMap when the PBR normal texture is present OR ReversedZDepth when explicit DepthTest/DepthWrite is true. Mesh pairing additionally checks selected UV sets, tangent for normal texture, and both skin attributes for skin. Its derived pipeline requirement also ORs VertexColor when the color stream is present and FourWeightSkin when skinned; these do not rewrite the stored material mask. PBR admits all those combinations; Sprite admits capabilities 0x01F and no skin/normal-map/PBR-light features. These are data/ABI capability bits, not a GPU/shader implementation claim.

Default depth write is profile mask membership; Blend always forbids depth write and any DepthWrite requires DepthTest. DoubleSided forces Cull None. Blend uses Premultiplied output, other modes Straight. Explicit supported depth-test/cull choices are retained, not reset, subject to those relationships; DP Capabilities evaluates after the admitted explicit/default depth state. AlphaCutoff is always encoded/range-admitted even when inactive.

Texture format RGBA8=0; ColorSpace Linear=0/sRGB=1; Alpha Straight=0/Premultiplied=1; Semantic Color=0/Normal=1/Data=2; MipPolicy BaseOnly=0/FullChain=1. Pixels are tightly packed top-left row-major RGBA8. BaseOnly has level0; FullChain levels 0..floor(log2(max(W,H))), floor-halved each dimension clamped to1, last 1x1. Every Pixels count=4*W*H. Normal/Data is Linear/Straight. Color texture slots use owner-required color spaces; base/emissive sRGB, packed/normal Linear. Validate supplied native bytes; no M03 decoder/mip-generator claim.

Sampler Min/Mag Nearest0/Linear1, Mip None0/Nearest1/Linear2, Address Clamp0/Repeat1. Atlas pages are square <=4096, premultiplied BaseOnly; frames have exact two-texel clamped-edge border, disjoint padded rectangles and actual page bounds. Atlas page order is semantic; frame map is RFC-sorted. Mip sampling cannot request missing levels.

Texture references select `native/texture` 1.0, atlas references `native/atlas` 1.0, mesh/skeleton/clip/material references their corresponding roots, and collision references the selected primitive dimension. PBR texture slots require Semantic=Color/Normal/Data/Color respectively, their stated color space and Straight alpha; Sprite texture is Color/Premultiplied with explicitly retained Linear or sRGB. Premultiplication is in linear space before storage encoding, as chapter 11 requires. A sprite material's optional texture, when paired with an atlas frame, must be that frame's exact page version; omission means the declared per-draw atlas page, not a failed-load substitute. An absent sprite Material selects the fixed C0 Sprite profile with explicit white parameters/default state; missing or invalid *present* material never selects that default. Native supplied mips/borders are validated but not secretly generated or recomputed.

The fixed Sprite layout exposes UV0 only, so a Sprite texture binding requires UVSet0. PBR UVSet0/1 requires the corresponding supplied mesh stream at pairing. This is a known layout/binding predicate, not an unsupported-enum fallback or a guessed duplicate UV stream.

RR29 additionally requires each spot's `0 < InnerHalfAngle <= OuterHalfAngle < pi32/2`; equality is the admitted hard-cutoff branch. Colors/intensity/flux/range/direction retain their exact finite/nonnegative/positive/unit predicates above, never inferred clamps. Shadow request/priority is copied inert extraction data at M03, not an implemented shadow-pass certificate.

## 3. Reflection and binding validation

The low-level public inputs are `ShaderBytecodeInput` and `ShaderReflection` in `TerraFX.Graphics`, consumed by `GraphicsDevice.CreatePipelineAsync`. Offline tools/cooked-asset adapters construct those inputs from leased bytes and copied metadata; `Graphics` depends on the Core assembly's `TerraFX.Buffers` APIs, not `Assets`. The typed graphics/compute pipeline descriptions select stages, layouts and fixed render state; success returns the owned `GpuPipeline` used by `CommandRecorder.SetPipeline`. Native validation must check actual DXIL/SPIR-V against the supplied reflection, and independent bytecode credits last through actual backend creation even when caller leases close. A digest/cache hit or caller-supplied reflection is not proof of usable pipeline state.

Reflection bundle includes stage I/O location/type/interpolation, resource group/binding/class/type/array count, record offsets/strides, entry/workgroup size, required capabilities and compiler identity. `Extract` DXIL reflection and inspect validated SPIR-V using version-pinned tooling; source declarations alone are not evidence generated layout agrees.

SPIR-V validation against actual Vulkan 1.3 feature profile required at import/build; native DXIL validation likewise. Reject unsupported capabilities, workgroup product beyond device limit, missing descriptors, render target mismatch and unbounded arrays. Baseline compute local size up to 256 invocations with each axis within device limits; declared nonzero.

Shader signatures/material/pipeline layout hash all reference same schema. Changing layout is not a benign shader replacement: create new bindings/packers/pipeline/material versions and transactionally switch a fully validated set.

## 4. Hot reload and failure

`Editor` watcher debounces 100 ms monotonic; coalesces source changes by latest digest, compiles off owner/render thread. Compiled version is validated fully, then activated at frame boundary only when matching material/pipeline versions ready. Old version retained until all CPU snapshots/GPU fences retire.

Compile/validation failure leaves old version intentionally active with named `ReloadFailed` diagnostics; it is not a successful reload. A first required shader failure prevents scene/renderer startup. Renderer error-checkerboard shader can be explicitly selected by editor `ErrorVisualization` policy, never be the default silent player behavior.

Pixel/compute reference scenes assert matrix projection, texture origin/cube face order, binding groups, uniform offsets, normals/negative scale, sRGB/alpha/depth/shadows and format conversion on both application programming interfaces (APIs). These future native tests, not reference scripts, establish parity; tolerances declared per test to allow rasterization/`float` differences, no bit-identical-pixel promise.
