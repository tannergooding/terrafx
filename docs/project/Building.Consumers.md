# Consumer and NativeAOT test procedures

Consumer projects exercise public APIs independently from the NUnit test process:

```powershell
dotnet run --project .\tests\TerraFX.Runtime.ComponentConsumer\TerraFX.Runtime.ComponentConsumer.csproj --configuration Debug
dotnet run --project .\tests\TerraFX.Runtime.GeneratedComponentConsumer\TerraFX.Runtime.GeneratedComponentConsumer.csproj --configuration Debug
```

## First Sprite/Mesh native consumers

These focused tests launch the separate public consumers with package-backed Scene content. Mesh cases load their native Mesh and Material records from an EPKG2 package produced by the GLB source adapter, then combine those records with test Scene fixtures. Headless cases prove `EngineHost` loading and `Renderer.Extract`; D3D12/Vulkan offscreen cases run `Extract` -> `BuildGraph` -> `Compile` -> `SubmitAsync` and verify non-clear pixels from each built-in Sprite/Mesh path:

```powershell
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~PublicHeadlessConsumerExtractsPackageBackedMeshAsync|FullyQualifiedName~PublicHeadlessConsumerExtractsPackageBackedSpriteAsync|FullyQualifiedName~PublicD3D12ConsumerRendersPackageBackedMeshAsync|FullyQualifiedName~PublicD3D12ConsumerRendersPackageBackedSpriteAsync|FullyQualifiedName~PublicVulkanConsumerRendersPackageBackedMeshAsync|FullyQualifiedName~PublicVulkanConsumerRendersPackageBackedSpriteAsync"
```

Managed-process witnesses do not establish NativeAOT behavior; publish and execute each target-specific consumer separately below. To run the same cases through published consumers, set `TERRAFX_D3D12_NATIVEAOT_CONSUMER` and/or `TERRAFX_VULKAN_NATIVEAOT_CONSUMER` to their absolute executable paths before running the filter. A `.dll` path is launched through `dotnet`; each run qualifies only the selected host/backend.

## NativeAOT

Publish for the target operating system; .NET NativeAOT does not cross-compile between Windows and Linux. `TerraFXPublishAot` is consumed by the owning consumer project; do not set `PublishAot` globally, because that would apply it to build-time analyzers and generators.

Windows x64 examples:

```powershell
dotnet publish .\tests\TerraFX.Runtime.ComponentConsumer\TerraFX.Runtime.ComponentConsumer.csproj --configuration Release --runtime win-x64 --self-contained true -p:TerraFXPublishAot=true
dotnet publish .\tests\TerraFX.Runtime.GeneratedComponentConsumer\TerraFX.Runtime.GeneratedComponentConsumer.csproj --configuration Release --runtime win-x64 --self-contained true -p:TerraFXPublishAot=true
dotnet publish .\tests\TerraFX.Graphics.D3D12.Consumer\TerraFX.Graphics.D3D12.Consumer.csproj --configuration Release --runtime win-x64 --self-contained true -p:TerraFXPublishAot=true
dotnet publish .\tests\TerraFX.Runtime.Vulkan.Consumer\TerraFX.Runtime.Vulkan.Consumer.csproj --configuration Release --runtime win-x64 --self-contained true -p:TerraFXPublishAot=true
```

For Linux x64, publish the corresponding consumer with `--runtime linux-x64` on a Linux host with its native toolchain. In WSLg, pass `-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/` and use the Linux SDK/environment from [Platform test procedures](Building.Platform.md).

Consumer publishing does not establish a different OS, backend, or hardware profile. Follow [Chapter 35](../design/35-implementation-roadmap.md) for required roots and qualification gates.
