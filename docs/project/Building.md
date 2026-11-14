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

## Specialized procedures

- [Graphics and rendering tests](Building.Graphics.md): D3D12/Vulkan setup, grouped rendering, shader fixtures, and optional budget-pressure probes.
- [Platform tests](Building.Platform.md): physical Windows input, isolated X11, and WSLg/Wayland setup.
- [Public consumers and NativeAOT](Building.Consumers.md): standalone consumer projects and target-specific publishing.

Milestone behavior and qualification gates are owned by the [design roadmap](../design/35-implementation-roadmap.md). This guide records commands and setup, not run history or per-run results.
