// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Content.Tools;
using TerraFX.Graphics;
using TerraFX.Numerics;
using TerraFX.Rendering;

namespace TerraFX.Runtime.Tests.Scenes;

internal sealed partial class SceneAssetStoreTests
{
    private static byte[] CreateGlb(
        bool duplicateAssetVersion = false,
        bool externalBuffer = false,
        bool positionBufferViewTooShort = false)
    {
        const string Json =
            "{\"asset\":{\"version\":\"2.0\"},"
            + "\"buffers\":[{\"byteLength\":102}],"
            + "\"bufferViews\":["
            + "{\"buffer\":0,\"byteOffset\":0,\"byteLength\":36},"
            + "{\"buffer\":0,\"byteOffset\":36,\"byteLength\":36},"
            + "{\"buffer\":0,\"byteOffset\":72,\"byteLength\":24},"
            + "{\"buffer\":0,\"byteOffset\":96,\"byteLength\":6}],"
            + "\"accessors\":["
            + "{\"bufferView\":0,\"componentType\":5126,\"count\":3,\"type\":\"VEC3\"},"
            + "{\"bufferView\":1,\"componentType\":5126,\"count\":3,\"type\":\"VEC3\"},"
            + "{\"bufferView\":2,\"componentType\":5126,\"count\":3,\"type\":\"VEC2\"},"
            + "{\"bufferView\":3,\"componentType\":5123,\"count\":3,\"type\":\"SCALAR\"}],"
            + "\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0,\"NORMAL\":1,\"TEXCOORD_0\":2},\"indices\":3,\"mode\":4}]}]}";
        var jsonText = duplicateAssetVersion
            ? Json.Replace("\"version\":\"2.0\"", "\"version\":\"2.0\",\"version\":\"2.0\"", StringComparison.Ordinal)
            : Json;

        if (externalBuffer)
        {
            jsonText = jsonText.Replace(
                "\"buffers\":[{\"byteLength\":102}]",
                "\"buffers\":[{\"uri\":\"external.bin\",\"byteLength\":102}]",
                StringComparison.Ordinal);
        }

        if (positionBufferViewTooShort)
        {
            jsonText = jsonText.Replace(
                "{\"buffer\":0,\"byteOffset\":0,\"byteLength\":36}",
                "{\"buffer\":0,\"byteOffset\":0,\"byteLength\":24}",
                StringComparison.Ordinal);
        }

        var json = Encoding.UTF8.GetBytes(jsonText);
        var jsonLength = checked((json.Length + 3) & ~3);
        var binary = new byte[104];
        WriteVector3(binary, 0, Vector3.Zero);
        WriteVector3(binary, 12, Vector3.UnitX);
        WriteVector3(binary, 24, Vector3.UnitY);
        WriteVector3(binary, 36, Vector3.UnitZ);
        WriteVector3(binary, 48, Vector3.UnitZ);
        WriteVector3(binary, 60, Vector3.UnitZ);
        WriteSingle(binary, 72, 0.0f);
        WriteSingle(binary, 76, 0.0f);
        WriteSingle(binary, 80, 1.0f);
        WriteSingle(binary, 84, 0.0f);
        WriteSingle(binary, 88, 0.0f);
        WriteSingle(binary, 92, 1.0f);
        BinaryPrimitives.WriteUInt16LittleEndian(binary.AsSpan(96), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(binary.AsSpan(98), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(binary.AsSpan(100), 2);
        var totalLength = checked(12 + 8 + jsonLength + 8 + binary.Length);
        var glb = new byte[totalLength];
        BinaryPrimitives.WriteUInt32LittleEndian(glb, 0x46546C67);
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(8), checked((uint)totalLength));
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(12), checked((uint)jsonLength));
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(16), 0x4E4F534A);
        json.CopyTo(glb.AsSpan(20));
        glb.AsSpan(20 + json.Length, jsonLength - json.Length).Fill((byte)' ');
        var binaryChunkOffset = 20 + jsonLength;
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(binaryChunkOffset), checked((uint)binary.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(binaryChunkOffset + 4), 0x004E4942);
        binary.CopyTo(glb.AsSpan(binaryChunkOffset + 8));
        return glb;
    }

    private static string FindRepositoryRoot()
    {
        var directory = Directory.GetParent(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TerraFX.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("The TerraFX solution root could not be located.");
    }

    private static byte[] GetProfileRecord()
    {
        var fixturePackage = Package.CreatePackage(new Package.Entry(1, []));
        var profileOffset = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(fixturePackage.AsSpan(16)));
        var profileLength = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(fixturePackage.AsSpan(24)));
        return fixturePackage.AsSpan(profileOffset, profileLength).ToArray();
    }

    private static void WriteSingle(Span<byte> destination, int offset, float value)
        => BinaryPrimitives.WriteInt32LittleEndian(
            destination.Slice(offset, sizeof(float)),
            BitConverter.SingleToInt32Bits(value));

    private static void WriteVector3(Span<byte> destination, int offset, Vector3 value)
    {
        WriteSingle(destination, offset, value.X);
        WriteSingle(destination, offset + sizeof(float), value.Y);
        WriteSingle(destination, offset + (2 * sizeof(float)), value.Z);
    }

    [Test]
    public void GlbAdapterRejectsAccessorsOutsideTheirBufferViews()
        => Assert.Throws<InvalidDataException>(
            () => StaticMeshSourceAdapters.CookGlbPackage(
                CreateGlb(positionBufferViewTooShort: true),
                [],
                [],
                meshAssetId: 1,
                materialAssetId: 2));

    [Test]
    public void GlbAdapterRejectsDuplicateJsonProperties()
        => Assert.Throws<InvalidDataException>(
            () => StaticMeshSourceAdapters.CookGlbPackage(
                CreateGlb(duplicateAssetVersion: true),
                [],
                [],
                meshAssetId: 1,
                materialAssetId: 2));

    [Test]
    public void GlbAdapterRejectsExternalBuffers()
        => Assert.Throws<NotSupportedException>(
            () => StaticMeshSourceAdapters.CookGlbPackage(
                CreateGlb(externalBuffer: true),
                [],
                [],
                meshAssetId: 1,
                materialAssetId: 2));

    [Test]
    public async Task GlbAndWavefrontObjAdaptersCookEquivalentNativeMeshPackages()
    {
        const uint MeshAssetId = 1;
        const uint MaterialAssetId = 2;
        var profile = GetProfileRecord();
        var material = CreateRenderMaterialAssetRecord(
            isSprite: false,
            CreateRenderMaterialPbrParametersRecord(),
            alphaMode: 0,
            doubleSided: true,
            depthTest: true,
            depthWrite: true,
            cullMode: 0,
            outputAlpha: 0,
            requiredCapabilities: 0x191);
        var glbPackage = StaticMeshSourceAdapters.CookGlbPackage(
            CreateGlb(),
            profile,
            material,
            MeshAssetId,
            MaterialAssetId);
        var objPackage = StaticMeshSourceAdapters.CookWavefrontObjPackage(
            Encoding.UTF8.GetBytes(
                "v 0 0 0\n"
                + "v 1 0 0\n"
                + "v 0 1 0\n"
                + "vt 0 0\n"
                + "vt 1 0\n"
                + "vt 0 1\n"
                + "vn 0 0 1\n"
                + "f 1/1/1 2/2/1 3/3/1\n"),
            WavefrontObjSourceConvention.RightHandedYUpForwardZMeters,
            profile,
            material,
            MeshAssetId,
            MaterialAssetId);
        var glbPackagePath = Package.WritePackage(glbPackage);
        var objPackagePath = Package.WritePackage(objPackage);

        try
        {
            var assetRegistrationBuilder = new AssetStoreRegistrationBuilder(CreateRendererRegistry());
            _ = Renderer.RegisterAssetCodecs(assetRegistrationBuilder);
            var registration = assetRegistrationBuilder.Build();
            var glbStore = await AssetStore.CreateAsync(
                new AssetStoreOptions(glbPackagePath, SHA256.HashData(glbPackage), registration)).ConfigureAwait(false);
            await using (glbStore.ConfigureAwait(false))
            {
                var objStore = await AssetStore.CreateAsync(
                    new AssetStoreOptions(objPackagePath, SHA256.HashData(objPackage), registration)).ConfigureAwait(false);
                await using (objStore.ConfigureAwait(false))
                {
                    using var glbMeshLease = await glbStore.LoadAsync<RenderMeshAsset>(new AssetId(checked((int)MeshAssetId))).ConfigureAwait(false);
                    using var objMeshLease = await objStore.LoadAsync<RenderMeshAsset>(new AssetId(checked((int)MeshAssetId))).ConfigureAwait(false);
                    var glbMesh = glbMeshLease.Value;
                    var objMesh = objMeshLease.Value;
                    Assert.Multiple(() => {
                        Assert.That(glbMesh.VertexCount, Is.EqualTo(3));
                        Assert.That(objMesh.VertexCount, Is.EqualTo(3));
                        Assert.That(glbMesh.MaterialSlots, Is.EqualTo([new AssetId(checked((int)MaterialAssetId))]));
                        Assert.That(objMesh.MaterialSlots, Is.EqualTo([new AssetId(checked((int)MaterialAssetId))]));
                        Assert.That(glbMesh.Groups, Is.EqualTo([new RenderMeshGroup(0, 3, 0)]));
                        Assert.That(objMesh.Groups, Is.EqualTo([new RenderMeshGroup(0, 3, 0)]));
                        Assert.That(glbMesh.Indices, Is.EqualTo([0u, 2u, 1u]));
                        Assert.That(
                            new[] {
                                glbMesh.Positions[checked((int)glbMesh.Indices[0])],
                                glbMesh.Positions[checked((int)glbMesh.Indices[1])],
                                glbMesh.Positions[checked((int)glbMesh.Indices[2])],
                            },
                            Is.EqualTo([
                                objMesh.Positions[checked((int)objMesh.Indices[0])],
                                objMesh.Positions[checked((int)objMesh.Indices[1])],
                                objMesh.Positions[checked((int)objMesh.Indices[2])],
                            ]));
                        Assert.That(
                            new[] {
                                glbMesh.Uv0[checked((int)glbMesh.Indices[0])],
                                glbMesh.Uv0[checked((int)glbMesh.Indices[1])],
                                glbMesh.Uv0[checked((int)glbMesh.Indices[2])],
                            },
                            Is.EqualTo([
                                objMesh.Uv0[checked((int)objMesh.Indices[0])],
                                objMesh.Uv0[checked((int)objMesh.Indices[1])],
                                objMesh.Uv0[checked((int)objMesh.Indices[2])],
                            ]));
                        Assert.That(glbMesh.Normals, Is.EqualTo(objMesh.Normals));
                        Assert.That(glbMesh.SkeletonAssetId, Is.Null);
                    });
                }
            }
        }
        finally
        {
            File.Delete(glbPackagePath);
            File.Delete(objPackagePath);
        }
    }

    [Test]
    public async Task StaticMeshCookCommandPreservesPreviousPackageOnUnsupportedSource()
    {
        var sourcePath = Path.Combine(Path.GetTempPath(), $"terrafx-{Guid.NewGuid():N}.obj");
        var profilePath = Path.Combine(Path.GetTempPath(), $"terrafx-{Guid.NewGuid():N}.profile.ebin");
        var materialPath = Path.Combine(Path.GetTempPath(), $"terrafx-{Guid.NewGuid():N}.material.ebin");
        var outputPath = Path.Combine(Path.GetTempPath(), $"terrafx-{Guid.NewGuid():N}.epkg");
        var previousPackage = new byte[] { 1, 2, 3, 4 };

        try
        {
            await File.WriteAllTextAsync(sourcePath, "mtllib unsupported.mtl\n", CancellationToken.None).ConfigureAwait(false);
            await File.WriteAllBytesAsync(profilePath, GetProfileRecord(), CancellationToken.None).ConfigureAwait(false);
            await File.WriteAllBytesAsync(
                materialPath,
                CreateRenderMaterialAssetRecord(
                    isSprite: false,
                    CreateRenderMaterialPbrParametersRecord(),
                    alphaMode: 0,
                    doubleSided: true,
                    depthTest: true,
                    depthWrite: true,
                    cullMode: 0,
                    outputAlpha: 0,
                    requiredCapabilities: 0x191),
                CancellationToken.None).ConfigureAwait(false);
            await File.WriteAllBytesAsync(outputPath, previousPackage, CancellationToken.None).ConfigureAwait(false);
            var arguments = new[] {
                "obj",
                "--source", sourcePath,
                "--profile-record", profilePath,
                "--material-record", materialPath,
                "--mesh-id", "1",
                "--material-id", "2",
                "--output", outputPath,
                "--obj-convention", "rh-y-up-z-forward-meters",
            };
            _ = Assert.ThrowsAsync<NotSupportedException>(
                async () => await StaticMeshCookCommand.RunCliAsync(arguments, CancellationToken.None).ConfigureAwait(false));
            Assert.That(
                await File.ReadAllBytesAsync(outputPath, CancellationToken.None).ConfigureAwait(false),
                Is.EqualTo(previousPackage));
            var toolAssemblyPath = Path.Combine(AppContext.BaseDirectory, "TerraFX.Content.Tools.dll");
            string[] processArguments = [
                toolAssemblyPath,
                "cook-static-mesh",
                .. arguments,
            ];
            var cliError = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await ToolProcessRunner.RunAsync(
                    "dotnet",
                    AppContext.BaseDirectory,
                    processArguments,
                    TimeSpan.FromMinutes(2),
                    maximumCapturedOutputBytes: 65_536,
                    CancellationToken.None).ConfigureAwait(false))!;
            Assert.Multiple(() => {
                Assert.That(cliError.Message, Does.Contain("Content.Tools: The OBJ statement 'mtllib'"));
                Assert.That(cliError.Message, Does.Not.Contain("Unhandled exception"));
            });
            Assert.That(
                await File.ReadAllBytesAsync(outputPath, CancellationToken.None).ConfigureAwait(false),
                Is.EqualTo(previousPackage));

            await File.WriteAllTextAsync(
                sourcePath,
                "v 0 0 0\nv 1 0 0\nv 0 1 0\nvn 0 0 1\nf 1//1 2//1 3//1\n",
                CancellationToken.None).ConfigureAwait(false);
            var exitCode = await StaticMeshCookCommand.RunCliAsync(arguments, CancellationToken.None).ConfigureAwait(false);
            Assert.That(exitCode, Is.Zero);
            Assert.That(
                (await File.ReadAllBytesAsync(outputPath, CancellationToken.None).ConfigureAwait(false)).AsSpan(0, 4).ToArray(),
                Is.EqualTo("EPKG"u8.ToArray()));
        }
        finally
        {
            File.Delete(sourcePath);
            File.Delete(profilePath);
            File.Delete(materialPath);
            File.Delete(outputPath);
        }
    }

    [Test]
    public async Task StaticMeshCookingMsBuildTargetTracksSourceAndProfileInputsAndMissingOutputs()
    {
        var targetDefinitionPath = Path.Combine(Path.GetTempPath(), $"terrafx-{Guid.NewGuid():N}.targets");
        var repositoryRoot = FindRepositoryRoot();
        var projectDirectory = Path.Combine(repositoryRoot, "tests", "TerraFX.Runtime.Tests");
        var temporaryDirectory = Path.Combine(projectDirectory, "obj", $"StaticMeshCook-{Guid.NewGuid():N}");
        var sourcePath = Path.Combine(temporaryDirectory, "triangle.obj");
        var profilePath = Path.Combine(temporaryDirectory, "profile.ebin");
        var materialPath = Path.Combine(temporaryDirectory, "material.ebin");
        var outputPath = Path.Combine(temporaryDirectory, "triangle.epkg");
        var contentCookingTargetsPath = Path.Combine(
            repositoryRoot,
            "sources",
            "TerraFX.Content.Tools",
            "ContentCooking.targets");

        try
        {
            _ = Directory.CreateDirectory(temporaryDirectory);
            await File.WriteAllTextAsync(
                sourcePath,
                "v 0 0 0\nv 1 0 0\nv 0 1 0\nvn 0 0 1\nf 1//1 2//1 3//1\n",
                CancellationToken.None).ConfigureAwait(false);
            await File.WriteAllBytesAsync(profilePath, GetProfileRecord(), CancellationToken.None).ConfigureAwait(false);
            await File.WriteAllBytesAsync(
                materialPath,
                CreateRenderMaterialAssetRecord(
                    isSprite: false,
                    CreateRenderMaterialPbrParametersRecord(),
                    alphaMode: 0,
                    doubleSided: true,
                    depthTest: true,
                    depthWrite: true,
                    cullMode: 0,
                    outputAlpha: 0,
                    requiredCapabilities: 0x191),
                CancellationToken.None).ConfigureAwait(false);
            var sourceXml = SecurityElement.Escape(Path.GetRelativePath(projectDirectory, sourcePath));
            var profileXml = SecurityElement.Escape(Path.GetRelativePath(projectDirectory, profilePath));
            var materialXml = SecurityElement.Escape(Path.GetRelativePath(projectDirectory, materialPath));
            var outputXml = SecurityElement.Escape(Path.GetRelativePath(projectDirectory, outputPath));
            var targetXml = SecurityElement.Escape(contentCookingTargetsPath);
            var project = $"""
                <Project>
                  <ItemGroup>
                    <TerraFXStaticMesh Include="{sourceXml}">
                      <Format>obj</Format>
                      <ProfileRecord>{profileXml}</ProfileRecord>
                      <MaterialRecord>{materialXml}</MaterialRecord>
                      <MeshAssetId>1</MeshAssetId>
                      <MaterialAssetId>2</MaterialAssetId>
                      <PackagePath>{outputXml}</PackagePath>
                      <ObjConvention>rh-y-up-z-forward-meters</ObjConvention>
                    </TerraFXStaticMesh>
                  </ItemGroup>
                  <Import Project="{targetXml}" />
                </Project>
                """;
            await File.WriteAllTextAsync(targetDefinitionPath, project, CancellationToken.None).ConfigureAwait(false);

            string[] arguments = [
                "msbuild",
                Path.Combine(repositoryRoot, "tests", "TerraFX.Runtime.Tests", "TerraFX.Runtime.Tests.csproj"),
                "-target:TerraFXCookStaticMeshes",
                $"-property:CustomAfterMicrosoftCommonTargets={targetDefinitionPath}",
                "-property:Configuration=Debug",
                "-property:TargetFramework=net11.0",
            ];
            var result = await ToolProcessRunner.RunAsync(
                "dotnet",
                repositoryRoot,
                arguments,
                TimeSpan.FromMinutes(2),
                maximumCapturedOutputBytes: 65_536,
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(result.ExitCode, Is.Zero);
                Assert.That(File.Exists(outputPath), Is.True);
                Assert.That(result.StandardOutput, Does.Contain("Cooked one static Mesh"));
            });
            var output = await File.ReadAllBytesAsync(outputPath, CancellationToken.None).ConfigureAwait(false);
            Assert.That(output.AsSpan(0, 4).ToArray(), Is.EqualTo("EPKG"u8.ToArray()));

            await File.WriteAllTextAsync(
                sourcePath,
                "v 0 0 0\nv 2 0 0\nv 0 1 0\nvn 0 0 1\nf 1//1 2//1 3//1\n",
                CancellationToken.None).ConfigureAwait(false);
            File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddSeconds(5));
            result = await ToolProcessRunner.RunAsync(
                "dotnet",
                repositoryRoot,
                arguments,
                TimeSpan.FromMinutes(2),
                maximumCapturedOutputBytes: 65_536,
                CancellationToken.None).ConfigureAwait(false);
            var changedOutput = await File.ReadAllBytesAsync(outputPath, CancellationToken.None).ConfigureAwait(false);
            Assert.Multiple(() => {
                Assert.That(result.StandardOutput, Does.Contain("Cooked one static Mesh"));
                Assert.That(changedOutput, Is.Not.EqualTo(output));
            });

            File.SetLastWriteTimeUtc(profilePath, DateTime.UtcNow.AddSeconds(10));
            result = await ToolProcessRunner.RunAsync(
                "dotnet",
                repositoryRoot,
                arguments,
                TimeSpan.FromMinutes(2),
                maximumCapturedOutputBytes: 65_536,
                CancellationToken.None).ConfigureAwait(false);
            var profileRebuildOutput = await File.ReadAllBytesAsync(outputPath, CancellationToken.None).ConfigureAwait(false);
            Assert.Multiple(() => {
                Assert.That(result.StandardOutput, Does.Contain("Cooked one static Mesh"));
                Assert.That(profileRebuildOutput, Is.EqualTo(changedOutput));
            });

            File.Delete(outputPath);
            result = await ToolProcessRunner.RunAsync(
                "dotnet",
                repositoryRoot,
                arguments,
                TimeSpan.FromMinutes(2),
                maximumCapturedOutputBytes: 65_536,
                CancellationToken.None).ConfigureAwait(false);
            Assert.Multiple(() => {
                Assert.That(result.StandardOutput, Does.Contain("Cooked one static Mesh"));
                Assert.That(File.Exists(outputPath), Is.True);
            });
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }

            File.Delete(targetDefinitionPath);
        }
    }

    [Test]
    public void WavefrontObjAdapterRejectsFacesWithPartialUv0()
        => Assert.Throws<InvalidDataException>(
            () => StaticMeshSourceAdapters.CookWavefrontObjPackage(
                Encoding.UTF8.GetBytes(
                    "v 0 0 0\n"
                    + "v 1 0 0\n"
                    + "v 0 1 0\n"
                    + "vt 0 0\n"
                    + "vt 1 0\n"
                    + "vn 0 0 1\n"
                    + "f 1/1/1 2/2/1 3//1\n"),
                WavefrontObjSourceConvention.RightHandedYUpForwardZMeters,
                [],
                [],
                meshAssetId: 1,
                materialAssetId: 2));

    [Test]
    public void WavefrontObjAdapterRejectsOutOfRangeFaceIndices()
        => Assert.Throws<InvalidDataException>(
            () => StaticMeshSourceAdapters.CookWavefrontObjPackage(
                Encoding.UTF8.GetBytes(
                    "v 0 0 0\n"
                    + "v 1 0 0\n"
                    + "v 0 1 0\n"
                    + "vn 0 0 1\n"
                    + "f 1//1 2//1 4//1\n"),
                WavefrontObjSourceConvention.RightHandedYUpForwardZMeters,
                [],
                [],
                meshAssetId: 1,
                materialAssetId: 2));

    [Test]
    public void WavefrontObjAdapterRejectsPolygonsThatRequireTriangulation()
        => Assert.Throws<NotSupportedException>(
            () => StaticMeshSourceAdapters.CookWavefrontObjPackage(
                Encoding.UTF8.GetBytes(
                    "v 0 0 0\n"
                    + "v 1 0 0\n"
                    + "v 1 1 0\n"
                    + "v 0 1 0\n"
                    + "vn 0 0 1\n"
                    + "f 1//1 2//1 3//1 4//1\n"),
                WavefrontObjSourceConvention.RightHandedYUpForwardZMeters,
                [],
                [],
                meshAssetId: 1,
                materialAssetId: 2));
}
