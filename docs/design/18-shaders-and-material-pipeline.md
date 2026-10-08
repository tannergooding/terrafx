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

## 3. Reflection and binding validation

The low-level public inputs are `ShaderBytecodeInput` and `ShaderReflection` in `TerraFX.Graphics`, consumed by `GraphicsDevice.CreatePipelineAsync`. Offline tools/cooked-asset adapters construct those inputs from leased bytes and copied metadata; `Graphics` depends on the Core assembly's `TerraFX.Buffers` APIs, not `Assets`. The typed graphics/compute pipeline descriptions select stages, layouts and fixed render state; success returns the owned `GpuPipeline` used by `CommandRecorder.SetPipeline`. Native validation must check actual DXIL/SPIR-V against the supplied reflection, and independent bytecode credits last through actual backend creation even when caller leases close. A digest/cache hit or caller-supplied reflection is not proof of usable pipeline state.

Reflection bundle includes stage I/O location/type/interpolation, resource group/binding/class/type/array count, record offsets/strides, entry/workgroup size, required capabilities and compiler identity. `Extract` DXIL reflection and inspect validated SPIR-V using version-pinned tooling; source declarations alone are not evidence generated layout agrees.

SPIR-V validation against actual Vulkan 1.3 feature profile required at import/build; native DXIL validation likewise. Reject unsupported capabilities, workgroup product beyond device limit, missing descriptors, render target mismatch and unbounded arrays. Baseline compute local size up to 256 invocations with each axis within device limits; declared nonzero.

Shader signatures/material/pipeline layout hash all reference same schema. Changing layout is not a benign shader replacement: create new bindings/packers/pipeline/material versions and transactionally switch a fully validated set.

## 4. Hot reload and failure

`Editor` watcher debounces 100 ms monotonic; coalesces source changes by latest digest, compiles off owner/render thread. Compiled version is validated fully, then activated at frame boundary only when matching material/pipeline versions ready. Old version retained until all CPU snapshots/GPU fences retire.

Compile/validation failure leaves old version intentionally active with named `ReloadFailed` diagnostics; it is not a successful reload. A first required shader failure prevents scene/renderer startup. Renderer error-checkerboard shader can be explicitly selected by editor `ErrorVisualization` policy, never be the default silent player behavior.

Pixel/compute reference scenes assert matrix projection, texture origin/cube face order, binding groups, uniform offsets, normals/negative scale, sRGB/alpha/depth/shadows and format conversion on both application programming interfaces (APIs). These future native tests, not reference scripts, establish parity; tolerances declared per test to allow rasterization/`float` differences, no bit-identical-pixel promise.
