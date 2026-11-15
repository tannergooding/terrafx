// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Content.Tools;
using TerraFX.Numerics;
using TerraFX.Rendering;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Scenes;

internal sealed partial class SceneAssetStoreTests
{
    private static async Task<byte[]> CreatePublicMeshConsumerPackageAsync()
    {
        const uint MeshAssetId = 10;
        const uint MaterialAssetId = 11;
        const int SceneAssetId = 3;
        var cameraScene = SceneAssetReaderTests.Fixtures.CreateScene(
            new AssetId(SceneAssetId),
            additionalEntityComponents: [CreateCameraRecord(order: 0)],
            rootPositionZ: -5.0f);
        var meshScene = SceneAssetReaderTests.Fixtures.CreateScene(
            new AssetId(4),
            additionalEntityComponents: [
                CreateMeshComponentRecord(checked((int)MeshAssetId), layerMask: ulong.MaxValue),
            ]);
        var meshMaterial = CreateRenderMaterialAssetRecord(
            isSprite: false,
            CreateRenderMaterialPbrParametersRecord(emissiveFactor: Vector3.One),
            alphaMode: 0,
            doubleSided: true,
            depthTest: true,
            depthWrite: true,
            cullMode: 0,
            outputAlpha: 0,
            requiredCapabilities: 0x191UL);
        var cookedPackage = StaticMeshSourceAdapters.CookGlbPackage(
            CreateGlb(),
            GetProfileRecord(),
            meshMaterial,
            MeshAssetId,
            MaterialAssetId);
        var cookedPackagePath = Package.WritePackage(cookedPackage);

        try
        {
            var schemas = CreateRendererRegistry();
            var assetRegistrationBuilder = new AssetStoreRegistrationBuilder(schemas);
            _ = Renderer.RegisterAssetCodecs(assetRegistrationBuilder);
            var registration = assetRegistrationBuilder.Build();
            var assets = await AssetStore.CreateAsync(
                new AssetStoreOptions(
                    cookedPackagePath,
                    SHA256.HashData(cookedPackage),
                    registration)).ConfigureAwait(false);
            await using (assets.ConfigureAwait(false))
            {
                using var meshLease = await assets.LoadAsync<RenderMeshAsset>(
                    new AssetId(checked((int)MeshAssetId))).ConfigureAwait(false);
                using var materialLease = await assets.LoadAsync<RenderMaterialAsset>(
                    new AssetId(checked((int)MaterialAssetId))).ConfigureAwait(false);
                var meshRecord = meshLease.Value.EncodedRecord.AsSpan().ToArray();
                var materialRecord = materialLease.Value.EncodedRecord.AsSpan().ToArray();
                return Package.CreatePackage(
                    new Package.Entry(
                        checked((uint)SceneAssetId),
                        Package.CreateEnvelope(checked((uint)SceneAssetId), cameraScene)),
                    new Package.Entry(
                        4,
                        Package.CreateEnvelope(4, meshScene)),
                    new Package.Entry(
                        MeshAssetId,
                        Package.CreateEnvelope(MeshAssetId, meshRecord)),
                    new Package.Entry(
                        MaterialAssetId,
                        Package.CreateEnvelope(MaterialAssetId, materialRecord)));
            }
        }
        finally
        {
            File.Delete(cookedPackagePath);
        }
    }

    private static byte[] CreatePublicSpriteCameraRecord()
    {
        var farBytes = new byte[sizeof(float)];
        BinaryPrimitives.WriteInt32LittleEndian(farBytes, BitConverter.SingleToInt32Bits(10.0f));
        var layerMask = new byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(layerMask, ulong.MaxValue);
        var orderBytes = new byte[sizeof(int)];

        return SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("scene/camera").SchemaId,
            1,
            0,
            new SceneHierarchyCodecTests.FixtureBuilder.FixtureField(
                1,
                1,
                1,
                1,
                [(byte)CameraProjectionKind.Orthographic]),
            SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(2, 10, float.Pi / 3.0f),
            SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(3, 10, 10.0f),
            SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(4, 10, 0.0f),
            new SceneHierarchyCodecTests.FixtureBuilder.FixtureField(
                5,
                1,
                1,
                1,
                [(byte)CameraFarMode.Finite]),
            new SceneHierarchyCodecTests.FixtureBuilder.FixtureField(6, 10, 0, 1, farBytes),
            SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(8, 16, 0.0f, 0.0f, 1.0f, 1.0f),
            new SceneHierarchyCodecTests.FixtureBuilder.FixtureField(
                9,
                1,
                1,
                1,
                [(byte)(CameraClearFlags.Color | CameraClearFlags.Depth)]),
            SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(10, 16, 0.0f, 0.0f, 0.0f, 1.0f),
            new SceneHierarchyCodecTests.FixtureBuilder.FixtureField(11, 4, 1, 1, layerMask),
            new SceneHierarchyCodecTests.FixtureBuilder.FixtureField(12, 7, 1, 1, orderBytes));
    }

    private static byte[] CreatePublicSpriteConsumerPackage()
    {
        const uint TextureAssetId = 12;
        const uint AtlasAssetId = 13;
        const int SceneAssetId = 3;
        var frameId = new Guid("80000000-0000-4000-8000-000000000001");
        var scene = SceneAssetReaderTests.Fixtures.CreateScene(
            new AssetId(SceneAssetId),
            additionalEntityComponents: [
                CreatePublicSpriteCameraRecord(),
                CreateRenderSpriteRecord(
                    checked((int)AtlasAssetId),
                    frameId,
                    materialAssetId: null),
            ]);
        var atlas = CreateRenderAtlasRecord(
            checked((int)TextureAssetId),
            CreateRenderAtlasFrameRecord(frameId, page: 0, x: 4, y: 4, width: 4, height: 4));
        return Package.CreatePackage(
            new Package.Entry(
                SceneAssetId,
                Package.CreateEnvelope(SceneAssetId, scene)),
            new Package.Entry(
                TextureAssetId,
                Package.CreateEnvelope(
                    TextureAssetId,
                    CreateRenderAtlasPageTextureRecord(17))),
            new Package.Entry(
                AtlasAssetId,
                Package.CreateEnvelope(AtlasAssetId, atlas)));
    }

    private static async Task RunPublicRendererConsumerAsync(
        string consumerProjectName,
        string consumerEnvironmentVariable,
        string backendName,
        byte[] packageBytes,
        Guid cameraEntityId,
        string commandMode,
        string renderMode,
        string completionMarker)
    {
        var packagePath = Package.WritePackage(packageBytes);

        try
        {
            var outputDirectory = new DirectoryInfo(AppContext.BaseDirectory);
            var configurationDirectory = outputDirectory.Parent
                ?? throw new InvalidOperationException("The renderer consumer test output has no configuration directory.");
            var testProjectOutputDirectory = configurationDirectory.Parent
                ?? throw new InvalidOperationException("The renderer consumer test output has no project directory.");
            var testsOutputDirectory = testProjectOutputDirectory.Parent
                ?? throw new InvalidOperationException("The renderer consumer test output has no tests directory.");
            var consumerAssembly = Path.Combine(
                testsOutputDirectory.FullName,
                consumerProjectName,
                configurationDirectory.Name,
                outputDirectory.Name,
                consumerProjectName + ".dll");
            Assert.That(File.Exists(consumerAssembly), Is.True, $"The public {backendName} consumer project reference must build the separate witness.");
            var configuredConsumerPath = Environment.GetEnvironmentVariable(consumerEnvironmentVariable);
            var consumerPath = string.IsNullOrWhiteSpace(configuredConsumerPath)
                ? consumerAssembly
                : configuredConsumerPath;

            if (!Path.IsPathFullyQualified(consumerPath) || !File.Exists(consumerPath))
            {
                throw new InvalidOperationException($"{consumerEnvironmentVariable} must name an existing absolute file.");
            }

            var isManagedAssembly = StringComparer.OrdinalIgnoreCase.Equals(Path.GetExtension(consumerPath), ".dll");
            var startInfo = new ProcessStartInfo(isManagedAssembly ? "dotnet" : consumerPath) {
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };

            if (isManagedAssembly)
            {
                startInfo.ArgumentList.Add(consumerPath);
            }

            startInfo.ArgumentList.Add(commandMode);
            startInfo.ArgumentList.Add(renderMode);
            startInfo.ArgumentList.Add(packagePath);
            startInfo.ArgumentList.Add(cameraEntityId.ToString("N"));
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"The public {backendName} renderer consumer did not start.");
            var standardOutputTask = process.StandardOutput.ReadToEndAsync();
            var standardErrorTask = process.StandardError.ReadToEndAsync();

            try
            {
                await process.WaitForExitAsync()
                    .WaitAsync(TimeSpan.FromSeconds(120))
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().ConfigureAwait(false);
                throw new TimeoutException($"The public {backendName} renderer consumer did not exit within two minutes.");
            }

            var output = await Task.WhenAll(standardOutputTask, standardErrorTask).ConfigureAwait(false);
            var processOutput = $"{output[0]}{Environment.NewLine}{output[1]}";
            Assert.That(process.ExitCode, Is.Zero, processOutput);
            Assert.That(
                processOutput,
                Does.Contain(completionMarker));
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

    [Test]
    public async Task PublicD3D12ConsumerRendersPackageBackedMeshAsync()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            Assert.Ignore("The D3D12 consumer path requires Windows 11.");
        }

        await RunPublicRendererConsumerAsync(
            "TerraFX.Graphics.D3D12.Consumer",
            "TERRAFX_D3D12_NATIVEAOT_CONSUMER",
            "D3D12",
            await CreatePublicMeshConsumerPackageAsync().ConfigureAwait(false),
            SceneAssetReaderTests.Fixtures.RootEntityId,
            "offscreen",
            "render-mesh",
            "D3D12 Offscreen built-in render graph readback passed").ConfigureAwait(false);
    }

    [Test]
    public Task PublicD3D12ConsumerRendersPackageBackedSpriteAsync()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            Assert.Ignore("The D3D12 consumer path requires Windows 11.");
            return Task.CompletedTask;
        }

        return RunPublicRendererConsumerAsync(
            "TerraFX.Graphics.D3D12.Consumer",
            "TERRAFX_D3D12_NATIVEAOT_CONSUMER",
            "D3D12",
            CreatePublicSpriteConsumerPackage(),
            SceneAssetReaderTests.Fixtures.RootEntityId,
            "offscreen",
            "render-sprite",
            "D3D12 Offscreen built-in render graph readback passed");
    }

    [Test]
    public async Task PublicHeadlessConsumerExtractsPackageBackedMeshAsync()
        => await RunPublicRendererConsumerAsync(
            "TerraFX.Runtime.Vulkan.Consumer",
            "TERRAFX_VULKAN_NATIVEAOT_CONSUMER",
            "Headless",
            await CreatePublicMeshConsumerPackageAsync().ConfigureAwait(false),
            SceneAssetReaderTests.Fixtures.RootEntityId,
            "headless-content",
            "mesh",
            "Headless package-backed mesh extraction passed.").ConfigureAwait(false);

    [Test]
    public Task PublicHeadlessConsumerExtractsPackageBackedSpriteAsync()
        => RunPublicRendererConsumerAsync(
            "TerraFX.Runtime.Vulkan.Consumer",
            "TERRAFX_VULKAN_NATIVEAOT_CONSUMER",
            "Headless",
            CreatePublicSpriteConsumerPackage(),
            SceneAssetReaderTests.Fixtures.RootEntityId,
            "headless-content",
            "sprite",
            "Headless package-backed sprite extraction passed.");

    [Test]
    public async Task PublicVulkanConsumerRendersPackageBackedMeshAsync()
        => await RunPublicRendererConsumerAsync(
            "TerraFX.Runtime.Vulkan.Consumer",
            "TERRAFX_VULKAN_NATIVEAOT_CONSUMER",
            "Vulkan",
            await CreatePublicMeshConsumerPackageAsync().ConfigureAwait(false),
            SceneAssetReaderTests.Fixtures.RootEntityId,
            "offscreen",
            "render-mesh",
            "Vulkan Offscreen built-in render graph readback passed").ConfigureAwait(false);

    [Test]
    public Task PublicVulkanConsumerRendersPackageBackedSpriteAsync()
        => RunPublicRendererConsumerAsync(
            "TerraFX.Runtime.Vulkan.Consumer",
            "TERRAFX_VULKAN_NATIVEAOT_CONSUMER",
            "Vulkan",
            CreatePublicSpriteConsumerPackage(),
            SceneAssetReaderTests.Fixtures.RootEntityId,
            "offscreen",
            "render-sprite",
            "Vulkan Offscreen built-in render graph readback passed");
}
