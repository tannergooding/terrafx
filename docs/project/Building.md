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

Build the D3D12 package to compile its adapter enumeration and capability reporting path. This does not initialize a device or qualify native startup:

```powershell
dotnet build .\sources\TerraFX.Graphics.D3D12\TerraFX.Graphics.D3D12.csproj --configuration Debug
dotnet build .\sources\TerraFX.Graphics.D3D12\TerraFX.Graphics.D3D12.csproj --configuration Release
```

### D3D12 OffscreenNative startup probe

The focused Runtime test attempts D3D12 startup through the registration using a `PlatformHostOwner`-issued identity:

```powershell
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~CreatesAndShutsDownD3D12GraphicsDeviceUsingHostIssuedIdentity"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~CreatesAndShutsDownD3D12GraphicsDeviceUsingHostIssuedIdentity"
```

On the current Windows 11 x64 host, D3D12 device creation and the `IDXGIDevice` UMD-version query succeed, but profile admission rejects both AMD adapters because `D32Float` does not report `Sampled` support. The test is explicitly skipped with both adapter identities and UMD versions in the TRX; this is a capability blocker, not a native D3D12 pass. The accepted format contract must be satisfied before the adapter can return a `GraphicsDevice`.

### Platform owner contracts

Run the owning-project tests for explicit platform selection, the Headless owner/capability path, Windows PMv2/STA startup, and foreground Raw Input registration/admission:

```powershell
dotnet test .\tests\TerraFX.Platform.Tests\TerraFX.Platform.Tests.csproj --configuration Debug
dotnet test .\tests\TerraFX.Platform.Tests\TerraFX.Platform.Tests.csproj --configuration Release
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

The command emits per-stage DXIL with DXC reflection dumped from the binary, Vulkan 1.3 SPIR-V with SPIRV-Cross JSON reflection, and validates each SPIR-V module with `spirv-val`. The output directory must not already exist. Microsoft DXC is licensed for Windows-only use; Linux Vulkan consumers use the cooked SPIR-V and do not execute the Linux DXC archive.

### Vulkan OffscreenNative backend tests

After compiling the M04 shader fixture, point the Vulkan tests at its output directory. To enable the installed Vulkan validation layer, set its manifest directory and layer name as shown:

```powershell
$env:TERRAFX_M04_SHADER_FIXTURE_DIRECTORY = (Resolve-Path .\artifacts\shader-fixtures\m04-offscreen-triangle).Path
$env:VK_LAYER_PATH = 'C:\VulkanSDK\1.4.328.1\Bin'
$env:VK_INSTANCE_LAYERS = 'VK_LAYER_KHRONOS_validation'
dotnet test .\tests\TerraFX.Graphics.Vulkan.Tests\TerraFX.Graphics.Vulkan.Tests.csproj --configuration Debug
dotnet test .\tests\TerraFX.Graphics.Vulkan.Tests\TerraFX.Graphics.Vulkan.Tests.csproj --configuration Release
```

The fixture-dependent tests create and retire an internal graphics pipeline with a Frame-group uniform buffer from the compiled SPIR-V. The M04 shader-tool tests check its actual DXIL/SPIRV-Cross output: one `float4` field at offset 0 in a 16-byte block, mapped to Vulkan set 0/binding 0. Pipeline failure after descriptor-layout creation must clean up the layout and prior shader modules. A native descriptor test allocates and populates an immutable Frame-group uniform-buffer set, rejects a mismatched binding and out-of-range descriptor, and proves that the set retains its pipeline/layout and buffer until retirement. The suite checks that a 65-MiB upload is refused while the 64-MiB ring is live, that a follow-up ring upload/readback succeeds, and that the same 65-MiB upload succeeds through dedicated staging on a fresh device. It also admits a 128-MiB dedicated upload at the full staging-budget boundary, verifies bytes at both ends, and rejects a 128-MiB-plus-one-byte upload. A managed test verifies that `VulkanGraphicsBackend.CreateRegistration()` selects Vulkan without creating a device. Native Vulkan tests exercise `VulkanBackendDevice` directly, not descriptor-set command binding, graph submission, or presentation. The current native suite passes 62/62 in Debug and Release with `VK_LAYER_KHRONOS_validation` enabled. If the fixture environment variable is unset, the fixture-dependent tests are skipped; a successful run with zero selected/executed cases is not evidence.

### Vulkan GraphicsDevice startup through the host issuer

This focused Runtime test supplies a real `PlatformHostOwner`-issued identity to `GraphicsDevice.CreateAsync`, starts an OffscreenNative device through `VulkanGraphicsBackend.CreateRegistration`, records the reported adapter/driver/API and validation-layer environment in the TRX output, then shuts down the device and platform owner:

```powershell
$env:VULKAN_SDK = 'C:\VulkanSDK\1.4.328.1'
$env:VK_LAYER_PATH = 'C:\VulkanSDK\1.4.328.1\Bin'
$env:VK_INSTANCE_LAYERS = 'VK_LAYER_KHRONOS_validation'
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~EngineHostOptionsTests"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~EngineHostOptionsTests"
```

This verifies the internal startup port with the sealed `GraphicsDevice` facade; it does not exercise `EngineHost`, Persistence service initialization, resource requests through the facade, or graph submission. The test explicitly skips when the Vulkan loader or a qualifying device is unavailable.

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

The D3D12 consumer enables NativeAOT locally through `TerraFXPublishAot`; do not pass `PublishAot=true` as a global MSBuild property, which would also apply AOT to build-time analyzer/generator projects. The published executable verifies the nonfriend public registration/signature surface without activating a native device:

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
