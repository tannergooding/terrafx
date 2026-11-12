# Graphics test procedures

Use the SDK selected by `global.json`. Native D3D12 tests require Windows 11; Vulkan shader and device tests use Vulkan SDK 1.4.328.1.

## Owning projects

```powershell
dotnet test .\tests\TerraFX.Graphics.Tests\TerraFX.Graphics.Tests.csproj --configuration Debug
dotnet test .\tests\TerraFX.Graphics.Tests\TerraFX.Graphics.Tests.csproj --configuration Release
dotnet test .\tests\TerraFX.Graphics.Vulkan.Tests\TerraFX.Graphics.Vulkan.Tests.csproj --configuration Debug
dotnet test .\tests\TerraFX.Graphics.Vulkan.Tests\TerraFX.Graphics.Vulkan.Tests.csproj --configuration Release
```

Runtime graph, transfer, and rendering tests are in `TerraFX.Runtime.Tests`.

## M05 grouped rendering

The selection creates ObjectGpuData pipelines and verifies grouped custom-pass and package-backed Sprite output on D3D12 and Vulkan:

```powershell
$filter = 'FullyQualifiedName~CreatesD3D12PipelineFromCompiledObjectGpuDataShaders|FullyQualifiedName~CreatesVulkanPipelineFromCompiledObjectGpuDataShaders|FullyQualifiedName~SubmitsSnapshotBoundRasterPassWithSemanticGroupsAndReadsBackPixels|FullyQualifiedName~SnapshotPinnedAtlasPageSubmitsObjectAndMaterialGroupsAsync'
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter $filter
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter $filter
```

These are explicit graph consumers, not the built-in Renderer Sprite C0/batching path. See [Chapter 21](../design/21-2d-and-3d-rendering.md) for renderer behavior and [Chapter 35](../design/35-implementation-roadmap.md) for qualification gates.

## M04 shader fixtures

Compile the fixture to a new output directory before running Runtime tests that require it:

```powershell
$env:VULKAN_SDK = 'C:\VulkanSDK\1.4.328.1'
$fixture = Join-Path .\artifacts\shader-fixtures ("m04-" + [guid]::NewGuid().ToString('N'))
dotnet .\artifacts\bin\sources\TerraFX.Content.Tools\Debug\net11.0\TerraFX.Content.Tools.dll compile-m04-shader $fixture
$env:TERRAFX_M04_SHADER_FIXTURE_DIRECTORY = (Resolve-Path $fixture).Path
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~SubmitsOffscreenClearGraphAndReadsBackAsync"
Remove-Item Env:\TERRAFX_M04_SHADER_FIXTURE_DIRECTORY
```

For Vulkan tests outside Windows, set `VULKAN_SDK` and pass `-p:BaseArtifactsPath=<path>` as described in [Platform test procedures](Building.Platform.md).

## Optional D3D12 budget-pressure probe

Run only on Windows with sufficient memory headroom. The bounded probe may allocate up to 512 MiB:

```powershell
$env:TERRAFX_RUN_D3D12_BUDGET_PRESSURE = '1'
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName=TerraFX.Runtime.Tests.Hosting.EngineHostOptionsTests.ObservesActualDxgiBudgetPressureAndUsesDedicatedUploadStaging"
Remove-Item Env:\TERRAFX_RUN_D3D12_BUDGET_PRESSURE
```
