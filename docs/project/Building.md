# Building the repository

Use the .NET SDK selected by [`global.json`](../../global.json). Dependencies are restored by ordinary `dotnet` commands; package versions are centralized in `Directory.Packages.props`.

## Standard commands

Run Debug before Release:

```powershell
dotnet restore TerraFX.slnx
dotnet build TerraFX.slnx --configuration Debug
dotnet test TerraFX.slnx --configuration Debug
dotnet build TerraFX.slnx --configuration Release
dotnet test TerraFX.slnx --configuration Release
```

The Windows and Linux build scripts are `scripts\build.ps1` and `scripts\build.sh`. CI reproduction scripts are `scripts\cibuild.cmd` and `scripts\cibuild.sh`.

For focused work, run the owning project rather than the full solution. Examples:

```powershell
dotnet test .\tests\TerraFX.Core.Tests\TerraFX.Core.Tests.csproj --configuration Debug
dotnet test .\tests\TerraFX.Assets.Tests\TerraFX.Assets.Tests.csproj --configuration Debug
dotnet test .\tests\TerraFX.Content.Tools.Tests\TerraFX.Content.Tools.Tests.csproj --configuration Debug
dotnet test .\tests\TerraFX.Generators.Tests\TerraFX.Generators.Tests.csproj --configuration Debug
dotnet test .\tests\TerraFX.Graphics.Tests\TerraFX.Graphics.Tests.csproj --configuration Debug
dotnet test .\tests\TerraFX.Platform.Tests\TerraFX.Platform.Tests.csproj --configuration Debug
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug
```

Use the corresponding Release configuration when qualifying Release behavior. Test results are written under `artifacts\tst\<Configuration>`; verify filtered runs select and execute tests.

## Shader compilation

Projects that build HLSL import `tools\TerraFX.ShaderCompiler\ShaderCompilation.targets` and declare shader inputs with `TerraFXShaderSource`. List transitive includes as `TerraFXShaderInclude` items. Generated DXIL/SPIR-V and reflection files are intermediate outputs.

Windows resolves the pinned DXC package automatically and uses Vulkan SDK 1.4.328.1 for SPIR-V validation/reflection; set `TerraFXVulkanSdkPath` when the SDK is installed outside the configured default. The Linux DXC tarball is not an allowed compiler input; Linux Vulkan consumers use SPIR-V cooked from the same HLSL on Windows. To run the compiler tests or invoke the incremental target directly:

```powershell
$env:VULKAN_SDK = 'C:\VulkanSDK\1.4.328.1'
dotnet test .\tests\TerraFX.Content.Tools.Tests\TerraFX.Content.Tools.Tests.csproj --configuration Debug
dotnet msbuild .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj -target:TerraFXCompileShaders -property:TerraFXVulkanSdkPath=$env:VULKAN_SDK
```

`TerraFXSkipShaderCompilation=true` explicitly skips shader compilation and is not shader validation.

## Static Mesh content cooking

Import `sources\TerraFX.Content.Tools\ContentCooking.targets` after declaring `TerraFXStaticMesh` items. Each item names one `.glb` or `.obj` source, a canonical Foundation profile record, a textureless native PBR Material record, stable Mesh/Material AssetIds, a `PackagePath`, and the format. OBJ items also declare `ObjConvention`:

```xml
<ItemGroup>
  <ProjectReference Include="..\..\sources\TerraFX.Content.Tools\TerraFX.Content.Tools.csproj" ReferenceOutputAssembly="false" />
  <TerraFXStaticMesh Include="Assets\triangle.glb">
    <Format>glb</Format>
    <ProfileRecord>Assets\profile.ebin</ProfileRecord>
    <MaterialRecord>Assets\default-pbr.ebin</MaterialRecord>
    <MeshAssetId>100</MeshAssetId>
    <MaterialAssetId>101</MaterialAssetId>
    <PackagePath>$(IntermediateOutputPath)Content\triangle.epkg</PackagePath>
  </TerraFXStaticMesh>
</ItemGroup>
<Import Project="..\..\sources\TerraFX.Content.Tools\ContentCooking.targets" />
```

`TerraFXCookStaticMeshes` runs before `Build`, declares the source, profile, material, project/target files, and tool assembly as incremental inputs, and publishes only after the package has been decoded through the native Mesh/Material codecs. Set `TerraFXSkipContentCooking=true` only when intentionally skipping content cooking.

## Native Mesh LOD content cooking

`cook-native-mesh` accepts a complete canonical `native/mesh` 2.0 EBIN1 record already authored in the selected Foundation profile. It does not convert coordinates, simplify geometry, or import GLB/OBJ LODs:

```powershell
dotnet .\artifacts\bin\sources\TerraFX.Content.Tools\Debug\net11.0\TerraFX.Content.Tools.dll cook-native-mesh --source Assets\lod-mesh.ebin --profile-record Assets\profile.ebin --mesh-id 100 --asset 101=Assets\material.ebin --output artifacts\content\lod.epkg
```

Repeat `--asset <id>=<record-path>` for the exact same-package reference closure: Material 1.0/2.0, optional Skeleton 1.0, and referenced Texture 1.0 records. IDs must be distinct positive signed-32-bit values. Missing, unused, or wrong-version dependencies reject; selected Material majors are preserved. Mesh 2.0 requires both additional-level and threshold arrays, even when empty. All levels share streams, material membership, group mapping, Skeleton, and conservative all-position bounds.

Use the same tool project reference and `ContentCooking.targets` import as static cooking:

```xml
<ItemGroup>
  <TerraFXNativeMesh Include="Assets\lod-mesh.ebin">
    <ProfileRecord>Assets\profile.ebin</ProfileRecord>
    <MeshAssetId>100</MeshAssetId>
    <PackagePath>$(IntermediateOutputPath)Content\lod.epkg</PackagePath>
  </TerraFXNativeMesh>
  <TerraFXNativeMeshAsset Include="Assets\material.ebin">
    <MeshSource>Assets\lod-mesh.ebin</MeshSource>
    <AssetId>101</AssetId>
  </TerraFXNativeMeshAsset>
</ItemGroup>
```

`TerraFXCookNativeMeshes` resolves paths from the invoking project directory, rejects ambiguous dependency associations and duplicate output paths, and tracks roots, profiles, dependencies, project/imported item options, the target, and the tool assembly as inputs. Missing outputs rebuild. `TerraFXSkipContentCooking` also applies to this target.

The producer checks complete record/envelope extents and candidate layout before bulk copies. The candidate package and simultaneous input/candidate/encoding buffers each fit the writer's 2,147,483,584-byte ceiling; AssetStore validation retains its separate decode and lease budgets. The staged EPKG2 is opened through a real AssetStore and the exact codecs, including Material/texture/Skeleton bindings, before atomic replacement. Refusal or cancellation before publication preserves the previous output. The default package writer and `cook-static-mesh` still select 1.0 assets only.

This standalone content path does not activate version-2 render components, Scene preparation, per-camera LOD selection/history, or native LOD rendering.

## Native cosmetic particle content cooking

`cook-native-particle-emitter` accepts one canonical `native/cosmetic-particle-emitter` 1.0 EBIN record authored in the selected Foundation profile:

```powershell
dotnet .\artifacts\bin\sources\TerraFX.Content.Tools\Debug\net11.0\TerraFX.Content.Tools.dll cook-native-particle-emitter --source Assets\emitter.ebin --profile-record Assets\profile.ebin --emitter-id 300 --asset 301=Assets\atlas.ebin --asset 302=Assets\atlas-page.ebin --asset 303=Assets\sprite-material.ebin --output artifacts\content\particles.epkg
```

Repeat `--asset <id>=<record-path>` for the exact same-package closure: the Atlas, all referenced Texture pages, and an optional Sprite Material 1.0/2.0. IDs must be distinct positive signed-32-bit values. The cooker validates the selected frame, retained page assets, Material profile and page binding before atomic publication; without a Material, the existing default Sprite profile is used.

Use `RenderingSchemaCatalogue.M05Schemas` in the compiled schema registry and call `Renderer.RegisterAssetCodecs` followed by `Renderer.RegisterM05AssetCodecs(builder, RendererCpuFeatures.MeshLod | RendererCpuFeatures.CosmeticParticles)` when the runtime store must decode this package.

The `ContentCooking.targets` import also accepts `TerraFXNativeParticleEmitter` items with `ProfileRecord`, `EmitterAssetId`, and `PackagePath` metadata. Associate each dependency with the root's resolved path through a `TerraFXNativeParticleEmitterAsset` item using `EmitterSource` and `AssetId`:

```xml
<ItemGroup>
  <TerraFXNativeParticleEmitter Include="Assets\emitter.ebin">
    <ProfileRecord>Assets\profile.ebin</ProfileRecord>
    <EmitterAssetId>300</EmitterAssetId>
    <PackagePath>$(IntermediateOutputPath)Content\particles.epkg</PackagePath>
  </TerraFXNativeParticleEmitter>
  <TerraFXNativeParticleEmitterAsset Include="Assets\atlas.ebin">
    <EmitterSource>Assets\emitter.ebin</EmitterSource>
    <AssetId>301</AssetId>
  </TerraFXNativeParticleEmitterAsset>
  <TerraFXNativeParticleEmitterAsset Include="Assets\atlas-page.ebin">
    <EmitterSource>Assets\emitter.ebin</EmitterSource>
    <AssetId>302</AssetId>
  </TerraFXNativeParticleEmitterAsset>
</ItemGroup>
```

`TerraFXCookNativeParticleEmitters` tracks these inputs and rebuilds missing outputs; it honors `TerraFXSkipContentCooking`. This produces authored content only, not a live World particle pool, Scene consumer, or rendering qualification.

## Specialized procedures

- [Graphics and rendering tests](Building.Graphics.md): D3D12/Vulkan setup, grouped rendering, shader fixtures, and optional budget-pressure probes.
- [Platform tests](Building.Platform.md): physical Windows input, isolated X11, and WSLg/Wayland setup.
- [Public consumers and NativeAOT](Building.Consumers.md): standalone consumer projects and target-specific publishing.

Milestone behavior and qualification gates are owned by the [design roadmap](../design/35-implementation-roadmap.md). This guide records commands and setup, not run history or per-run results.
