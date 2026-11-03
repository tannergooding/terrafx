# Building the repository

The repository aims to be simple to configure, build, and produce binaries. To achieve this it pulls in all dependencies as NuGet packages and provides a set of build scripts to assist in performing common tasks such as restoring dependencies, building, testing, and produce NuGet packages.

These scripts are:
 * [scripts/build.ps1](../../scripts/build.ps1) on Windows
 * [scripts/build.sh](../../scripts/build.sh) on Linux

Each script performs the requested build steps and expects that the .NET SDK is already on the box. This means that after a clean enlistment, you must select both the `restore` and `build` steps (in that order).

In addition there are [scripts/cibuild.cmd](../../scripts/cibuild.cmd) and [scripts/cibuild.sh](../../scripts/cibuild.sh) scripts which allow you to reproduce everything the CI environment does. This includes automatically acquiring a compatible .NET SDK and running each step in the correct sequence (first restore, then build, then test, then pack). Run Debug before Release.

Specifying `-architecture` or `--architecture` also acquires the published SDK version from [global.json](../../global.json) under `artifacts\dotnet`. CI's SDK setup uses the same file; update its version for RC/GA releases and servicing without changing the scripts or workflow. Installed SDK selection retains the configured roll-forward policy.

## Requirements

.NET 11.0 SDK: https://dotnet.microsoft.com/download/dotnet/11.0

## Available Arguments

The scripts accept any combination of build steps and additionally forward any user specified arguments given.

For example, `scripts/build.ps1 -build -restore` runs both steps. The scripts are aware of command ordering requirements and will run them in the correct order (e.g. `restore` will always run before `build`).

The Windows arguments take a single dash (`-`) as the prefix; Bash arguments use two (`--`).

The available arguments can be seen via the `help` command, but are additionally listed below for reference:
```
Common settings:
  -configuration <value>  Build configuration (Debug, Release)
  -verbosity <value>      MSBuild verbosity (quiet, minimal, normal, detailed, diagnostic)
  -help                   Print help and exit

Actions:
  -restore                Restore dependencies
  -build                  Build solution
  -test                   Run all tests in the solution
  -pack                   Package build artifacts

Advanced settings:
  -solution <value>       Path to solution or project to build
  -ci                     Acquire the SDK and run restore, build, test, pack
  -architecture <value>   SDK architecture (<auto>, amd64, x64, x86, arm64, arm)
  -additionalArgs <array> Forward extra options (PowerShell)

Command line arguments not listed above are passed through to dotnet.
PowerShell arguments can be shortened as much as to be unambiguous (e.g. -co for configuration, -t for test, etc.).
```

Use `-additionalArgs @("...")` for options that conflict with PowerShell parameters. Bash accepts `--` before forwarded arguments.

## Utilizing `dotnet`

Given that the above build scripts all utilize `dotnet` behind the scenes, you can also utilize the same for working with the repository.

That is, all of the existing well known commands "just work". This includes, but is not limited to:
 * `dotnet restore`
 * `dotnet build`
 * `dotnet test`
 * `dotnet pack`

With a compatible SDK installed, `dotnet build` also restores dependencies automatically; no script or SDK acquisition step is required.

Test filtering uses the runner's existing `FullyQualifiedName` support, as in the foundation-codec commands below. For implemented cases, confirm nonzero selected and executed counts and inspect current-run TRX results under `artifacts\tst`. A successful command with no selected tests is not validation.

### Graphics capability contracts

Run the owning-project tests for immutable device/surface capabilities, profile admission, buffer/texture/sampler descriptions, and invalid boundary rejection:

```powershell
dotnet test .\tests\TerraFX.Graphics.Tests\TerraFX.Graphics.Tests.csproj --configuration Debug
dotnet test .\tests\TerraFX.Graphics.Tests\TerraFX.Graphics.Tests.csproj --configuration Release
```

Run the D3D12 descriptor-range allocation and graph resource-state mapping tests (36/36 in Debug and Release):

```powershell
dotnet test .\tests\TerraFX.Graphics.D3D12.Tests\TerraFX.Graphics.D3D12.Tests.csproj --configuration Debug
dotnet test .\tests\TerraFX.Graphics.D3D12.Tests\TerraFX.Graphics.D3D12.Tests.csproj --configuration Release
```

Build the D3D12 package to compile its adapter enumeration and capability reporting path. This does not initialize a device or qualify native startup:

```powershell
dotnet build .\sources\TerraFX.Graphics.D3D12\TerraFX.Graphics.D3D12.csproj --configuration Debug
dotnet build .\sources\TerraFX.Graphics.D3D12\TerraFX.Graphics.D3D12.csproj --configuration Release
```

### D3D12 OffscreenNative startup and descriptor-set probes

The focused Runtime tests exercise D3D12 startup and descriptor writes using a `PlatformHostOwner`-issued identity:

```powershell
$env:VULKAN_SDK = 'C:\VulkanSDK\1.4.328.1'
dotnet .\artifacts\bin\sources\TerraFX.Content.Tools\Debug\net11.0\TerraFX.Content.Tools.dll compile-m04-shader .\artifacts\shader-fixtures\m04-offscreen-triangle-d3d12-witness
$env:TERRAFX_M04_SHADER_FIXTURE_DIRECTORY = (Resolve-Path .\artifacts\shader-fixtures\m04-offscreen-triangle-d3d12-witness).Path
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~CreatesAndShutsDownD3D12GraphicsDeviceUsingHostIssuedIdentity"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~CreatesAndShutsDownD3D12GraphicsDeviceUsingHostIssuedIdentity"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName=TerraFX.Runtime.Tests.Hosting.EngineHostOptionsTests.CreatesD3D12DescriptorSetsAndRetainsNativeOwners"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName=TerraFX.Runtime.Tests.Hosting.EngineHostOptionsTests.CreatesD3D12DescriptorSetsAndRetainsNativeOwners"
```

On the current Windows 11 x64 host, the native tests pass on the AMD Radeon RX 7900 XTX (vendor `1002`, device `744C`, UMD `002000007D0F1B75`). D3D12 admits sampled D32 depth through an `R32_TYPELESS` resource, with `D32_FLOAT` depth and `R32_FLOAT` sampled view formats; the startup witness creates a real depth/sampled texture and verifies an exact upload/readback round trip. It also round-trips a 4x4x2 RGBA8 volume and creates a resource-bearing PSO/root signature from compiled DXIL. The startup witness uploads and reads back exactly 64 MiB through the staging rings, then uploads and reads back 64 MiB + 1 through dedicated staging, verifying bytes at the end of each range. This focused test passes 1/1 in Debug and Release; TRX: `artifacts\\tst\\Debug\\tagoo_GOODING-ZEN4_2026-11-02_16_09_56_net11.0.trx` / `artifacts\\tst\\Release\\tagoo_GOODING-ZEN4_2026-11-02_16_10_47_net11.0.trx`. The descriptor-set test directly creates the D3D12 backend on its test thread using the Core owner ID issued by `PlatformHostOwner`, then writes the `PSSampledFrameStorageMain` Frame CBV/SRV/UAV/sampler tables for a uniform buffer, sampled texture, structured storage buffer, storage image, and sampler. Both native PSO paths now consume typed reflection generated from the same fixture's DXIL dumpbin and SPIRV-Cross outputs. This validates selected fixture bindings and resource shapes, not generic record-layout association or complete bytecode/reflection equivalence. The descriptor test verifies incomplete, misaligned, and invalid-usage/range refusal plus pipeline/resource/backend retention until set disposal. Storage buffers require a declared structured stride; storage textures are single-layer 2D. Neither test binds tables to a command list, executes the shader, or submits a graph, so neither is the public graph/pipeline witness.

The descriptor witness holds 128 live sampler-backed sets to fill the 256-slot sampler heap, verifies that the next sampler admission is refused, then retires one set/sampler and confirms both ranges can be reused by a replacement set. This verifies native descriptor-capacity refusal and recovery, not GPU-completion retirement.

The same native witness intentionally changes the typed sampled-image register from `t0` to `t1` while keeping the compiled DXIL unchanged; D3D12 rejects the mismatched pipeline state. This validates resource-register compatibility for the selected fixture, not complete reflection equivalence.

### Platform owner contracts

Run the owning-project tests for explicit platform selection, the Headless owner/capability path, Windows PMv2/STA startup, and foreground Raw Input registration/admission:

```powershell
dotnet test .\tests\TerraFX.Platform.Tests\TerraFX.Platform.Tests.csproj --configuration Debug
dotnet test .\tests\TerraFX.Platform.Tests\TerraFX.Platform.Tests.csproj --configuration Release
```

On Windows 11 x64, run the hidden-HWND owner witness for live client metrics, PMv2 scale/provenance, native resize refresh and snapshot replacement, pre-surface zero epochs, and stale-handle rejection:

```powershell
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName=TerraFX.Runtime.Tests.Hosting.EngineHostOptionsTests.WindowsPlatformOwnerWitnessesHiddenWindowCreationAndRetirement"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName=TerraFX.Runtime.Tests.Hosting.EngineHostOptionsTests.WindowsPlatformOwnerWitnessesHiddenWindowCreationAndRetirement"
```

### Offline M04 shader fixture

Run the bounded process and toolchain-manifest regressions. On Windows x64, set `VULKAN_SDK` to the pinned 1.4.328.1 SDK to execute the SPIR-V compile/reflection test; when it is unset, that one test is reported as skipped:

```powershell
dotnet test .\tests\TerraFX.Content.Tools.Tests\TerraFX.Content.Tools.Tests.csproj --configuration Debug
dotnet test .\tests\TerraFX.Content.Tools.Tests\TerraFX.Content.Tools.Tests.csproj --configuration Release
```

On Windows x64, build Content.Tools and verify its copied DXC binary digests, license files, and executable version:

```powershell
dotnet build .\sources\TerraFX.Content.Tools\TerraFX.Content.Tools.csproj --configuration Debug
dotnet .\artifacts\bin\sources\TerraFX.Content.Tools\Debug\net11.0\TerraFX.Content.Tools.dll verify-dxc
```

To compile the pinned Windows x64 DXIL fixture and capture `dxc -dumpbin` reflection without the Vulkan SDK:

```powershell
dotnet .\artifacts\bin\sources\TerraFX.Content.Tools\Debug\net11.0\TerraFX.Content.Tools.dll compile-m04-dxil .\artifacts\shader-fixtures\m04-offscreen-triangle-dxil
```

This command emits per-stage `.dxil` files and matching `.dxil-reflection.txt` files. It does not validate SPIR-V or make Content.Tools a player runtime dependency.

To compile the M04 offscreen triangle, set `VULKAN_SDK` to the exact Vulkan SDK 1.4.328.1 root containing `Bin\spirv-cross.exe` and `Bin\spirv-val.exe`, then choose a new output directory:

```powershell
$env:VULKAN_SDK = 'C:\VulkanSDK\1.4.328.1'
dotnet .\artifacts\bin\sources\TerraFX.Content.Tools\Debug\net11.0\TerraFX.Content.Tools.dll compile-m04-shader .\artifacts\shader-fixtures\m04-offscreen-triangle
```

The command emits the vertex, baseline pixel, resource-bearing `pixel-sampled`, combined `pixel-sampled-frame`, and storage-bearing `pixel-sampled-frame-storage` stages as DXIL with DXC reflection dumped from the binary and Vulkan 1.3 SPIR-V with SPIRV-Cross JSON reflection, then validates each SPIR-V module with `spirv-val`. The baseline pixel reflection pins the FrameData UBO to set 0/binding 0; `pixel-sampled` consumes `FrameTexture` and `FrameSampler` at set 0 bindings 16 and 40; `pixel-sampled-frame` reflects the UBO together with both sampled resources; `pixel-sampled-frame-storage` also reflects `FrameStorageBuffer` at binding 32 and `FrameStorageImage` at binding 33 with `r32ui` format. The output directory must not already exist. Microsoft DXC is licensed for Windows-only use; Linux Vulkan consumers use the cooked SPIR-V and do not execute the Linux DXC archive.

The typed reflection converter cross-checks SPIRV-Cross uniform-buffer records against DXIL `Buffer Definitions`: record association and byte length, supported flat field names/types/offsets/counts, fixed-array strides, and explicit row-major `float4x4` layout. Nested records, one-element or multidimensional record arrays, and storage-buffer element-record association remain unsupported.

For sampled and storage images, the converter resolves the descriptor-decorated `OpTypeImage` through SPIR-V pointer/array types and compares its supported dimension and 32-bit component class (`f32`, `i32`, or `u32`) with SPIRV-Cross and DXIL resource reflection. For storage images it also cross-checks the SPIR-V image-format enumerant against SPIRV-Cross and DXIL for `r32f`, `r32i`, and `r32ui`; this does not establish sampled texture API view-format compatibility or cover every image shape/format.

For every resource in the supported SPIRV-Cross descriptor collections, the converter compares raw SPIR-V `OpVariable` storage class/resource kind, descriptor set/binding, and the product of fixed array dimensions with SPIRV-Cross metadata; runtime-sized or specialization-sized descriptor arrays reject, and array-count products beyond `Int32.MaxValue` are reported as invalid data. This verifies reflected array extents only, not public descriptor-array element binding semantics.

`ShaderReflection.RequiredCapabilities` includes the selected nonbaseline SPIR-V mappings for geometry, tessellation, Float16, Float64, GroupNonUniform operations, and descriptor indexing, including core dynamic descriptor-array indexing and extension nonuniform/runtime-array capabilities. Baseline `Matrix`/`Shader` declarations are omitted; unsupported capability declarations (including Float16Buffer) reject rather than disappearing from typed reflection. The current GraphicsDevice/backends reject every nonempty capability requirement until a matching device-feature mapping is qualified, so parsed requirements are not execution support.

### Vulkan OffscreenNative backend tests

After compiling the M04 shader fixture, point the Vulkan tests at its output directory. To enable the installed Vulkan validation layer, set its manifest directory and layer name as shown:

```powershell
$env:TERRAFX_M04_SHADER_FIXTURE_DIRECTORY = (Resolve-Path .\artifacts\shader-fixtures\m04-offscreen-triangle).Path
$env:VK_LAYER_PATH = 'C:\VulkanSDK\1.4.328.1\Bin'
$env:VK_INSTANCE_LAYERS = 'VK_LAYER_KHRONOS_validation'
dotnet test .\tests\TerraFX.Graphics.Vulkan.Tests\TerraFX.Graphics.Vulkan.Tests.csproj --configuration Debug
dotnet test .\tests\TerraFX.Graphics.Vulkan.Tests\TerraFX.Graphics.Vulkan.Tests.csproj --configuration Release
```

Before creating shader modules or pipeline layouts, Vulkan preflight parses the SPIR-V entry point, stage, required Shader and supported Matrix capabilities, descriptor set/register-class bindings, and fixed descriptor-array counts, then compares them to `ShaderReflection`. It rejects decoration-group encodings, multi-entrypoint modules, runtime-sized descriptor arrays, combined image-sampler descriptors, and nonbaseline capabilities explicitly; it checks scalar/vector and row-major float4x4 uniform-block member offsets, types, sizes, fixed-array counts and strides, matrix stride, and record bounds. Column-major matrices and matrix forms other than float4x4 with a 16-byte row stride are rejected. Graphics-stage scalar/vector interfaces are checked against actual BuiltIn/Location and value type; interpolation is checked for user Location interfaces. The baseline maps VertexIndex/InstanceIndex/Position/FragCoord/FragDepth and `SV_Target` output indices. Centroid/sample qualifiers and unsupported interface forms reject. User semantic labels are not independently validated beyond the system built-in/location mapping, and BuiltIn interpolation is not represented by SPIR-V interface decorations. Storage-buffer layout, exact trailing block padding, compute execution modes, descriptor-array element binding semantics, and sampled API view-format compatibility remain unvalidated by Vulkan preflight; offline conversion cross-checks only the implemented image dimensions/component classes and `r32f`, `r32i`, and `r32ui` storage formats. The native fixture regression also rejects a missing Shader capability without creating a device. This guard runs before the driver pipeline call, so a `t0` versus `t1` metadata mismatch returns an `ArgumentException` instead of reaching native pipeline creation.

The fixture-dependent tests create and retire internal graphics pipelines from compiled SPIR-V. The baseline pixel reflection checks FrameData as a 16-byte UBO at set 0/binding 0; the separate resource-bearing pixel reflection checks `FrameTexture` as a sampled image at binding 16 and `FrameSampler` as a sampler at binding 40; `pixel-sampled-frame` combines all three resources; `pixel-sampled-frame-storage` adds a storage buffer and `r32ui` storage image. The sampled descriptor test creates an image view and populates a matching image/sampler descriptor set, rejects mismatched or missing bindings, and proves that texture, sampler, image view, pipeline, and layout stay retained until retirement. The mixed Frame-set test writes the UBO, sampled image, and sampler into one descriptor set; the storage test also writes the storage buffer and storage image and verifies buffer, texture, sampler, view, pipeline, and layout retention. Sampled and storage images require `GENERAL` layout, and sampled contents must be initialized. The UBO descriptor test continues to reject mismatched bindings and out-of-range descriptors and retain its buffer/layout until retirement. Pipeline failure after descriptor-layout creation must clean up the layout and prior shader modules. The suite also covers staging refusal/recovery and the 128-MiB budget boundary. The mismatch regression verifies the actual SPIR-V `t0` binding and an array-count mismatch are rejected before native pipeline creation. Parser-only tests accept the profile's row-major float4x4 UBO and reject column-major, unsupported stride, and nonsquare forms without creating a device. Five fixture-driven parser-only mismatches also reject changed vertex built-in, pixel target location, output type, interpolation, and missing output metadata without creating a device. `M04ShaderFixtureCompiler.ReadTypedReflection` converts pinned DXIL dumpbin and SPIRV-Cross outputs into `ShaderReflection`, rejecting mismatched descriptor resource kind/set/binding/fixed-array counts, omitted or duplicate SPIRV-Cross output locations, and the selected image/UBO/capability forms described above. Native Vulkan tests consume fixture-generated typed reflection with actual compiled SPIR-V and exercise `VulkanBackendDevice` directly, not descriptor-set command binding, graph submission, or presentation. The current native suite passes 80/80 in Debug and Release with `VK_LAYER_KHRONOS_validation` enabled; fresh TRX: `artifacts\\tst\\Debug\\tagoo_GOODING-ZEN4_2026-11-02_15_50_01_net11.0.trx` / `artifacts\\tst\\Release\\tagoo_GOODING-ZEN4_2026-11-02_15_50_18_net11.0.trx`. If the fixture environment variable is unset, the fixture-dependent tests are skipped; a successful run with zero selected/executed cases is not evidence.

### Vulkan GraphicsDevice startup through the host issuer

This focused Runtime test supplies a real `PlatformHostOwner`-issued identity to `GraphicsDevice.CreateAsync`, starts an OffscreenNative device through `VulkanGraphicsBackend.CreateRegistration`, records the reported adapter/driver/API and validation-layer environment in the TRX output, then uses `CreateBufferAsync`, `UploadBufferAsync`, and `ReadbackBufferAsync` to verify an exact 32-byte round trip before shutting down the device and platform owner:

```powershell
$env:VULKAN_SDK = 'C:\VulkanSDK\1.4.328.1'
$env:VK_LAYER_PATH = 'C:\VulkanSDK\1.4.328.1\Bin'
$env:VK_INSTANCE_LAYERS = 'VK_LAYER_KHRONOS_validation'
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~EngineHostOptionsTests"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~EngineHostOptionsTests"
```

This verifies the internal startup port and public buffer-resource request path with the sealed `GraphicsDevice` facade; it does not exercise `EngineHost`, Persistence service initialization, texture/sampler/pipeline requests through the facade, or graph submission. The test explicitly skips when the Vulkan loader or a qualifying device is unavailable.

### Retained AssetLease lifetime

Run the focused retained-handle, lease-capacity, package-replacement, and store-disposal regressions:

```powershell
dotnet test .\tests\TerraFX.Assets.Tests\TerraFX.Assets.Tests.csproj --configuration Debug --filter "FullyQualifiedName~AssetStoreRetain"
dotnet test .\tests\TerraFX.Assets.Tests\TerraFX.Assets.Tests.csproj --configuration Release --filter "FullyQualifiedName~AssetStoreRetain"
```

### Package-bound AssetLease loading

Run the regression proving referenced assets are loaded from the lease's original package after replacement:

```powershell
dotnet test .\tests\TerraFX.Assets.Tests\TerraFX.Assets.Tests.csproj --configuration Debug --filter "FullyQualifiedName~AssetStoreLoadsAssetFromPackageLease"
dotnet test .\tests\TerraFX.Assets.Tests\TerraFX.Assets.Tests.csproj --configuration Release --filter "FullyQualifiedName~AssetStoreLoadsAssetFromPackageLease"
```

### Scene prefab package continuity

Run the Scene expansion regression proving nested Prefabs remain bound to the root Scene lease's package after the AssetStore is replaced:

```powershell
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~ScenePrefabExpansionUsesRootSceneLeaseBackingAfterStoreReplacement"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~ScenePrefabExpansionUsesRootSceneLeaseBackingAfterStoreReplacement"
```

### Rendering texture asset codec

Run the package-backed RGBA8 texture codec regressions for exact BaseOnly/FullChain mips and semantic validation:

```powershell
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~TextureAssetCodec"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~TextureAssetCodec"
```

### Rendering Atlas page loading

Run the package-backed Atlas tests for same-package page resolution across replacement, texture metadata, clamped two-texel borders, and padded-frame overlap:

```powershell
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~AtlasPageSet"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~AtlasPageSet"
```

### Rendering Material profiles and texture dependencies

Run the package-backed Sprite/PBR Material tests for profile tuples, parameter and render-state constraints, typed texture bindings, slot semantics, and same-package dependency loading across replacement:

```powershell
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~MaterialAssetCodec|FullyQualifiedName~MaterialTextureLoader"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~MaterialAssetCodec|FullyQualifiedName~MaterialTextureLoader"
```

### Scene Sprite component source

Run the package-backed Scene activation test for required Atlas references, optional Material references, Sprite value validation, and refusal to synthesize an absent required Atlas/Frame:

```powershell
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~SceneActivationRestoresSpriteAssetReferencesAndRejectsInvalidMutation"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~SceneActivationRestoresSpriteAssetReferencesAndRejectsInvalidMutation"
```

### Prepared Sprite resource ownership

Run the package-backed Sprite resource tests for exact root-Scene package loading, page validation, SceneInstance ownership, and package replacement:

```powershell
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~SpriteAssetPreparationUsesTheRootScenePackageAfterStoreReplacement|FullyQualifiedName~SceneActivationRestoresSpriteAssetReferencesAndRejectsInvalidMutation"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~SpriteAssetPreparationUsesTheRootScenePackageAfterStoreReplacement|FullyQualifiedName~SceneActivationRestoresSpriteAssetReferencesAndRejectsInvalidMutation"
```

### Sprite snapshot extraction and retained pins

Run the 2D/3D extraction regression for stable per-camera Sprite ordering, empty camera-mask selection without asset pins, real committed-frame transforms, and Atlas/Material/page pins that remain valid after Scene unload and release with snapshot disposal:

```powershell
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~SpriteSnapshotsOrderDrawsAndRetainAssetsAfterSceneUnload"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~SpriteSnapshotsOrderDrawsAndRetainAssetsAfterSceneUnload"
```

### Public structural EBIN and schema metadata

Core exposes bounded schema-agnostic EBIN reader/writer and scalar/array payload helpers. These validate wire structure and preserve canonical byte layout; they do not activate or validate an owner schema. `CompiledContentSchemaRegistry` contains immutable schema/profile metadata only. Typed owner codecs and their roots are separately compiled with the selected application registrations.

Run the focused structural, payload, and registry metadata regressions:

```powershell
dotnet test .\tests\TerraFX.Core.Tests\TerraFX.Core.Tests.csproj --configuration Debug --filter "FullyQualifiedName~EbinStructuralReaderTests|FullyQualifiedName~EbinStructuralWriterTests|FullyQualifiedName~EbinPayloadWriterTests|FullyQualifiedName~ContentSchemaRegistryTests"
dotnet test .\tests\TerraFX.Core.Tests\TerraFX.Core.Tests.csproj --configuration Release --filter "FullyQualifiedName~EbinStructuralReaderTests|FullyQualifiedName~EbinStructuralWriterTests|FullyQualifiedName~EbinPayloadWriterTests|FullyQualifiedName~ContentSchemaRegistryTests"
```

These tests establish only the behaviors they exercise; schema-owner semantics, World registration, package loading, persistence, and M03 acceptance remain distinct.

### Core-private foundation codecs

The EBIN1 structural layer and M02 foundation admission layer live in `sources\TerraFX.Core`.
They are internal, BCL-only implementation details, not a public codec API or an Assets dependency.
The literal eight-schema catalogue fixes canonical registry/kernel bytes and digests; schema versions
own their fixed behavior, and incoming manifests cannot replace that catalogue. Profile admission uses
an explicit selection of already registered identities/settings. Optional extensions use the reserved
EBIN extension set with signed 64-bit IDs and inert opaque payloads. Frame I/O additionally requires a private persistence binding
containing the stable world GUID, profile, runtime owner and binding revision.

Reads validate bounded spans before copying an owned staged wire value. They neither allocate a
runtime owner nor register profiles or activate worlds. Projection and capture are explicit operations
against an owner binding; retained optional fields remain inert and survive recapture. Writers validate
input and checked extent before destination capacity, leave the entire destination unchanged on failure,
and report zero written. Structural payload I/O does not substitute for schema/context admission.

Run the focused foundation-codec regressions:

```powershell
dotnet test .\tests\TerraFX.Core.Tests\TerraFX.Core.Tests.csproj --configuration Debug --filter "FullyQualifiedName~M02FoundationCodecTests"
dotnet test .\tests\TerraFX.Core.Tests\TerraFX.Core.Tests.csproj --configuration Release --filter "FullyQualifiedName~M02FoundationCodecTests"
```

The tests construct five canonical EBIN records in C# and check their exact lengths and SHA-256 values.
Focused cases exercise distinct codec, validation, binding and ownership behavior; there is no separate
M02 fixture manifest, materializer, prefix corpus or fixture-package pin to maintain. The test project
also builds the separate unsafe-disabled public spatial consumer.

The spatial consumer can be run separately as JIT or published for a Windows x64 NativeAOT execution:

```powershell
dotnet run --project .\tests\TerraFX.Core.SpatialConsumer\TerraFX.Core.SpatialConsumer.csproj --configuration Release
dotnet publish .\tests\TerraFX.Core.SpatialConsumer\TerraFX.Core.SpatialConsumer.csproj --configuration Release --runtime win-x64 --self-contained true -p:TerraFXPublishAot=true
```

This consumer covers its selected spatial API surface only; it does not complete the M02 NativeAOT numerical profiles or Linux target qualification.

### Focused production behavior tests

The Core suite contains focused literal cases for bounded rounding, spatial conversion/normalization,
Angle, and geometry behavior. Physics primitive tests exercise the production validators and measures;
these selections do not imply unimplemented M03 package, World, native, or AOT behavior.

```powershell
dotnet test .\tests\TerraFX.Core.Tests\TerraFX.Core.Tests.csproj --configuration Debug --filter "FullyQualifiedName~M02FoundationCodecTests|FullyQualifiedName~BoundedRationalTests|FullyQualifiedName~SpatialMathTests|FullyQualifiedName~SpatialValueTests|FullyQualifiedName~AngleTests|FullyQualifiedName~GeometryKernelTests|FullyQualifiedName~GeometryComparisonTests|FullyQualifiedName~ContentSchemaReferenceTests"
dotnet test .\tests\TerraFX.Physics.Tests\TerraFX.Physics.Tests.csproj --configuration Debug --filter "FullyQualifiedName~PrimitiveShapeValidationTests"
```

Repeat the applicable selections in Release after Debug succeeds. Confirm nonzero selected and executed counts in current-run TRX results under `artifacts\tst\<Configuration>`.

### Runtime public-provider consumers

The Runtime test project builds two independent unsafe-disabled executable witnesses: the original handwritten component consumer and the generated-component consumer. The generated consumer references `TerraFX.Generators` as an analyzer and exercises the same public schema registration, World mutation, default, and scoped-query path without changing the handwritten source.

Run each executable separately after building:

```powershell
dotnet run --project .\tests\TerraFX.Runtime.ComponentConsumer\TerraFX.Runtime.ComponentConsumer.csproj --configuration Debug
dotnet run --project .\tests\TerraFX.Runtime.GeneratedComponentConsumer\TerraFX.Runtime.GeneratedComponentConsumer.csproj --configuration Debug
```

The Runtime suite also runs an unsafe-disabled Scene activation witness through the handwritten consumer as a separate process. It covers an encoded optional Entity reference bound explicitly to null and rejects the same null target for a required reference:

```powershell
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~SceneLoaderActivationHandlesExplicitNullEntityReference"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~SceneLoaderActivationHandlesExplicitNullEntityReference"
```

By default, these focused tests run the handwritten consumer through JIT. They do not complete Scene hierarchy transactions or full CPU extraction/native qualification.

The Scene owner-boundary regression also checks that the estimated expansion bytes are reserved before materialization, remain charged after activation, and are released on unload or when a prepared plan is abandoned. The admission-domain case checks the per-World ceiling under concurrent reservation attempts:

```powershell
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~WorldReservationGrowthCannotExceedItsOwnedStateCeiling|FullyQualifiedName~SceneLoaderActivatesAtTheWorldOwnerControlBoundary|FullyQualifiedName~SceneStorageReservationsAreThreadSafeAndRespectTheWorldBudget"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~WorldReservationGrowthCannotExceedItsOwnedStateCeiling|FullyQualifiedName~SceneLoaderActivatesAtTheWorldOwnerControlBoundary|FullyQualifiedName~SceneStorageReservationsAreThreadSafeAndRespectTheWorldBudget"
```

These reservation and activation regressions do not qualify NativeAOT or native execution.

The SceneInstance transaction regressions cover preview and owner-boundary revalidation, same-World cross-instance parents, foreign/stale targets, sequential root transitions, atomic cycle rejection, KeepWorld residual refusal, cancellation, and queue-capacity refusal:

```powershell
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~SceneAssetStoreTests.SceneTransaction"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~SceneAssetStoreTests.SceneTransaction"
```

These managed transaction cases do not qualify CPU extraction, NativeAOT, or native execution.

The headless extraction cases exercise package-backed Scene and nested-Prefab activation, exact committed 2D/3D frame selection, canonical camera ordering, hierarchical pose composition, directional/point/spot light snapshots, camera-layer filtering, stable identities, exact combined light-capacity refusal, copied snapshot lifetime, and stale-revision rejection. They also verify light defaults/validation and that CPU extraction registration requires Scene support and is admitted only once:

```powershell
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~HeadlessRendererExtractsNestedSceneCamerasFromOneCommittedFrame|FullyQualifiedName~CpuExtractionSupportRequiresSceneSupportAndRejectsDuplicateRegistration|FullyQualifiedName~DirectionalAndSpotLightsUseWorldDefaultsAndRejectInvalidMutation|FullyQualifiedName~PointLightUsesWorldDefaultsAndRejectsInvalidMutation"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~HeadlessRendererExtractsNestedSceneCamerasFromOneCommittedFrame|FullyQualifiedName~CpuExtractionSupportRequiresSceneSupportAndRejectsDuplicateRegistration|FullyQualifiedName~DirectionalAndSpotLightsUseWorldDefaultsAndRejectInvalidMutation|FullyQualifiedName~PointLightUsesWorldDefaultsAndRejectsInvalidMutation"
```

This covers point lights only; draw/sprite/mesh/bone sources, directional/spot lights, asset-lease retention, and the complete frame-cohort lifecycle remain unimplemented. These managed tests do not qualify NativeAOT or native rendering.

For Windows NativeAOT qualification, each executable project enables `PublishAot` locally when `TerraFXPublishAot=true`; the generated project's analyzer reference removes that switch. Publish and execute the artifacts separately:

```powershell
dotnet publish .\tests\TerraFX.Runtime.ComponentConsumer\TerraFX.Runtime.ComponentConsumer.csproj --configuration Release --runtime win-x64 --self-contained true -p:TerraFXPublishAot=true
dotnet publish .\tests\TerraFX.Runtime.GeneratedComponentConsumer\TerraFX.Runtime.GeneratedComponentConsumer.csproj --configuration Release --runtime win-x64 --self-contained true -p:TerraFXPublishAot=true
dotnet publish .\tests\TerraFX.Graphics.D3D12.Consumer\TerraFX.Graphics.D3D12.Consumer.csproj --configuration Release --runtime win-x64 --self-contained true -p:TerraFXPublishAot=true
```

The D3D12 consumer enables NativeAOT locally through `TerraFXPublishAot`; do not pass `PublishAot=true` as a global MSBuild property, which would also apply AOT to build-time analyzer/generator projects. The published executable verifies the nonfriend public registration and transfer/pipeline/sampler signatures, constructs canonical `ShaderReflection` resource/layout/capability values, and mutates the caller-owned input arrays and compiler digest to prove the reflected value is independently copied. It does not activate a native device:

```powershell
& .\artifacts\bin\tests\TerraFX.Graphics.D3D12.Consumer\Release\net11.0\win-x64\publish\TerraFX.Graphics.D3D12.Consumer.exe
```

The handwritten Runtime consumer also verifies that `PhysicalInputEvent` and `PhysicalInputValue` expose no public constructors and that event/value/diagnostic/scroll-unit enum values are stable. Its JIT and Windows x64 NativeAOT executions validate the nonfriend public surface only; neither activates native input or creates a platform window.

The encoded optional-null Scene activation witness can run the exact same cases through the published handwritten NativeAOT executable:

```powershell
$env:TERRAFX_SCENE_ACTIVATION_CONSUMER = (Resolve-Path .\artifacts\bin\tests\TerraFX.Runtime.ComponentConsumer\Release\net11.0\win-x64\publish\TerraFX.Runtime.ComponentConsumer.exe).Path
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~SceneLoaderActivationHandlesExplicitNullEntityReference"
Remove-Item Env:\TERRAFX_SCENE_ACTIVATION_CONSUMER
```

When set, the test launches that exact executable and does not fall back to JIT if it fails. Windows x64 NativeAOT does not qualify Linux.

NativeAOT does not support cross-OS compilation; publish Linux artifacts on the required Linux host, then execute them there. JIT or Windows NativeAOT execution does not qualify the Linux target or M02 profiles.
