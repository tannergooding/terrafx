// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX.Assets;
using TerraFX.Rendering;

namespace TerraFX.Content.Tools.Tests;

[TestFixture]
internal sealed class NativeMeshCookCommandTests
{
    private string _directory = string.Empty;

    private static async Task AssertUsablePackageAsync(string path, int meshId = 100)
    {
        var store = await NativeMeshTestData.OpenStoreAsync(path).ConfigureAwait(false);
        await using var storeLifetime = store.ConfigureAwait(false);
        using var mesh = await store.LoadAsync<RenderMeshAsset2>(new AssetId(meshId)).ConfigureAwait(false);
        _ = await RenderMeshAsset2Binding.ValidatePackageBindingsAsync(mesh).ConfigureAwait(false);
    }

    private static async Task RunBuildAsync(string projectPath, bool expectCook)
    {
        var result = await ToolProcessRunner.RunAsync(
            "dotnet",
            NativeMeshTestData.FindRepositoryRoot(),
            new[] { "msbuild", projectPath, "-target:Build", "-verbosity:minimal", "-nologo" },
            TimeSpan.FromMinutes(2),
            65_536).ConfigureAwait(false);
        Assert.That(result.ExitCode, Is.Zero);
        Assert.That(result.StandardOutput.Contains("Cooked one native Mesh", StringComparison.Ordinal), Is.EqualTo(expectCook));
    }

    [Test]
    public async Task CancellationBeforeAdmissionAndAfterRealStagedValidationPreservesPreviousOutput()
    {
        await NativeMeshTestData.WriteInputsAsync(_directory).ConfigureAwait(false);
        Assert.That(await NativeMeshCookCommand.RunCliAsync(CookArguments(), CancellationToken.None).ConfigureAwait(false), Is.Zero);
        var outputPath = Path.Combine(_directory, "mesh.epkg");
        var previous = await File.ReadAllBytesAsync(outputPath).ConfigureAwait(false);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync().ConfigureAwait(false);
        _ = Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await NativeMeshCookCommand.RunCliAsync(CookArguments(), cancellation.Token).ConfigureAwait(false));
        Assert.That(await File.ReadAllBytesAsync(outputPath).ConfigureAwait(false), Is.EqualTo(previous));

        using var stagedCancellation = new CancellationTokenSource();
        var registration = NativeMeshTestData.CreateRegistration();
        var candidate = ContentPackageWriter.CreateNativeAssetPackage(
            NativeMeshTestData.GetProfileRecord(),
            [
                new ContentPackageAsset(100, NativeMeshTestData.CreateMesh(1)),
                new ContentPackageAsset(101, NativeMeshTestData.CreateMaterial()),
            ],
            registration.Schemas);
        var validated = false;
        _ = Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await ContentPackageWriter.WriteValidatedAtomicallyAsync(
                outputPath,
                candidate,
                async (path, token) => {
                    var store = await AssetStore.CreateAsync(new AssetStoreOptions(
                        path,
                        SHA256.HashData(candidate),
                        registration), token).ConfigureAwait(false);
                    await using (store.ConfigureAwait(false))
                    {
                        using var mesh = await store.LoadAsync<RenderMeshAsset2>(new AssetId(100), token).ConfigureAwait(false);
                        _ = await RenderMeshAsset2Binding.ValidatePackageBindingsAsync(mesh, token).ConfigureAwait(false);
                    }

                    validated = true;
                    await stagedCancellation.CancelAsync().ConfigureAwait(false);
                },
                stagedCancellation.Token).ConfigureAwait(false));

        Assert.That(validated, Is.True);
        Assert.That(await File.ReadAllBytesAsync(outputPath).ConfigureAwait(false), Is.EqualTo(previous));
        Assert.That(Directory.GetFiles(_directory, "*.staging"), Is.Empty);
        await AssertUsablePackageAsync(outputPath).ConfigureAwait(false);
    }

    [Test]
    public async Task DefaultWriterRetainsOnePointZeroPolicyAndNativeSelectionIsNotVersionPassThrough()
    {
        await NativeMeshTestData.WriteInputsAsync(_directory).ConfigureAwait(false);
        _ = await NativeMeshCookCommand.RunCliAsync(CookArguments(), CancellationToken.None).ConfigureAwait(false);
        var profile = NativeMeshTestData.GetProfileRecord();
        var schemas = NativeMeshTestData.CreateRegistration().Schemas;

        _ = Assert.Throws<InvalidDataException>(() => ContentPackageWriter.CreatePackage(
            profile,
            [new ContentPackageAsset(100, NativeMeshTestData.CreateMesh())]));
        _ = Assert.Throws<InvalidDataException>(() => ContentPackageWriter.CreatePackage(
            profile,
            [new ContentPackageAsset(101, NativeMeshTestData.CreateMaterial(2))]));
        _ = Assert.Throws<InvalidDataException>(() => ContentPackageWriter.CreateNativeAssetPackage(
            profile,
            [new ContentPackageAsset(100, NativeMeshTestData.Rewrite(NativeMeshTestData.CreateMesh(), minor: 1))],
            schemas));
        _ = Assert.Throws<InvalidDataException>(() => ContentPackageWriter.CreateNativeAssetPackage(
            profile,
            [new ContentPackageAsset(101, NativeMeshTestData.Rewrite(NativeMeshTestData.CreateMaterial(), major: 3))],
            schemas));
        _ = Assert.Throws<InvalidDataException>(() => ContentPackageWriter.CreateNativeAssetPackage(
            profile,
            [new ContentPackageAsset(100, NativeMeshTestData.CreateBounds())],
            schemas));
        await AssertUsablePackageAsync(Path.Combine(_directory, "mesh.epkg")).ConfigureAwait(false);
    }

    [Test]
    public async Task MsBuildAssociatesDependenciesWithExactlyTheirResolvedRoot()
    {
        await NativeMeshTestData.WriteInputsAsync(_directory).ConfigureAwait(false);
        var otherMesh = NativeMeshTestData.Rewrite(
            NativeMeshTestData.CreateMesh(1),
            5,
            new ContentRecordField(5, 24, 1, ContentPackageWriter.EncodeRecordArray([
                NativeMeshTestData.CreateAssetReference(201, "native/material"),
            ])));
        await File.WriteAllBytesAsync(Path.Combine(_directory, "other.ebin"), otherMesh).ConfigureAwait(false);
        var project = await WriteBuildProjectAsync("""
            <TerraFXNativeMesh Include="other.ebin" ProfileRecord="profile.ebin" MeshAssetId="200" PackagePath="other.epkg" />
            <TerraFXNativeMeshAsset Include="material.ebin" AssetId="201" MeshSource="other.ebin" />
            """).ConfigureAwait(false);
        await RunBuildAsync(project, expectCook: true).ConfigureAwait(false);
        await AssertUsablePackageAsync(Path.Combine(_directory, "mesh.epkg")).ConfigureAwait(false);
        await AssertUsablePackageAsync(Path.Combine(_directory, "other.epkg"), 200).ConfigureAwait(false);
    }

    [Test]
    public async Task MsBuildTracksDeclaredInputsOptionsAndMissingOutputs()
    {
        await NativeMeshTestData.WriteInputsAsync(_directory).ConfigureAwait(false);
        var project = await WriteBuildProjectAsync().ConfigureAwait(false);
        var outputPath = Path.Combine(_directory, "mesh.epkg");
        await RunBuildAsync(project, expectCook: true).ConfigureAwait(false);
        var original = await File.ReadAllBytesAsync(outputPath).ConfigureAwait(false);
        await AssertUsablePackageAsync(outputPath).ConfigureAwait(false);
        await RunBuildAsync(project, expectCook: false).ConfigureAwait(false);

        var mesh = NativeMeshTestData.Rewrite(
            NativeMeshTestData.CreateMesh(),
            9,
            new ContentRecordField(9, 23, 1, NativeMeshTestData.EncodeThresholds([0.5f])));
        await File.WriteAllBytesAsync(Path.Combine(_directory, "mesh.ebin"), mesh).ConfigureAwait(false);
        File.SetLastWriteTimeUtc(outputPath, DateTime.UtcNow.AddSeconds(-5));
        await RunBuildAsync(project, expectCook: true).ConfigureAwait(false);
        var changed = await File.ReadAllBytesAsync(outputPath).ConfigureAwait(false);
        Assert.That(changed, Is.Not.EqualTo(original));

        var material = NativeMeshTestData.Rewrite(
            NativeMeshTestData.CreateMaterial(),
            7,
            new ContentRecordField(7, 10, 1, ContentPackageWriter.EncodeSingle(0.25f)));
        await File.WriteAllBytesAsync(Path.Combine(_directory, "material.ebin"), material).ConfigureAwait(false);
        File.SetLastWriteTimeUtc(outputPath, DateTime.UtcNow.AddSeconds(-5));
        await RunBuildAsync(project, expectCook: true).ConfigureAwait(false);
        Assert.That(await File.ReadAllBytesAsync(outputPath).ConfigureAwait(false), Is.Not.EqualTo(changed));

        foreach (var input in new[] { "profile.ebin", "ContentCooking.targets" })
        {
            File.SetLastWriteTimeUtc(outputPath, DateTime.UtcNow.AddSeconds(-5));
            File.SetLastWriteTimeUtc(Path.Combine(_directory, input), DateTime.UtcNow);
            await RunBuildAsync(project, expectCook: true).ConfigureAwait(false);
        }

        var xml = await File.ReadAllTextAsync(project).ConfigureAwait(false);
        await File.WriteAllTextAsync(project, xml.Replace("MeshAssetId=\"100\"", "MeshAssetId=\"105\"", StringComparison.Ordinal)).ConfigureAwait(false);
        File.SetLastWriteTimeUtc(outputPath, DateTime.UtcNow.AddSeconds(-5));
        await RunBuildAsync(project, expectCook: true).ConfigureAwait(false);
        await AssertUsablePackageAsync(outputPath, 105).ConfigureAwait(false);
        File.Delete(outputPath);
        await RunBuildAsync(project, expectCook: true).ConfigureAwait(false);
        await AssertUsablePackageAsync(outputPath, 105).ConfigureAwait(false);
    }

    [TestCase("association")]
    [TestCase("duplicate-source")]
    [TestCase("duplicate-output")]
    [TestCase("duplicate-static-output")]
    [TestCase("output-alias")]
    public async Task MsBuildValidatesAssociationsAndOutputsEvenAfterSuccessfulCooking(string failure)
    {
        await NativeMeshTestData.WriteInputsAsync(_directory).ConfigureAwait(false);
        var project = await WriteBuildProjectAsync().ConfigureAwait(false);
        await RunBuildAsync(project, expectCook: true).ConfigureAwait(false);
        var previous = await File.ReadAllBytesAsync(Path.Combine(_directory, "mesh.epkg")).ConfigureAwait(false);
        await File.WriteAllBytesAsync(Path.Combine(_directory, "other.ebin"), NativeMeshTestData.CreateMesh()).ConfigureAwait(false);
        await File.WriteAllTextAsync(
            Path.Combine(_directory, "static.obj"),
            "v 0 0 0\nv 1 0 0\nv 0 1 0\nvn 0 0 1\nf 1//1 2//1 3//1\n").ConfigureAwait(false);
        var extra = failure switch {
            "association" => """<TerraFXNativeMeshAsset Include="material.ebin" AssetId="102" MeshSource="missing.ebin" />""",
            "duplicate-source" => """<TerraFXNativeMesh Include=".\mesh.ebin" ProfileRecord="profile.ebin" MeshAssetId="200" PackagePath="other.epkg" />""",
            "duplicate-output" => """<TerraFXNativeMesh Include="other.ebin" ProfileRecord="profile.ebin" MeshAssetId="200" PackagePath=".\mesh.epkg" />""",
            "duplicate-static-output" => """<TerraFXStaticMesh Include="static.obj" Format="obj" ProfileRecord="profile.ebin" MaterialRecord="material.ebin" MeshAssetId="200" MaterialAssetId="201" PackagePath="mesh.epkg" ObjConvention="rh-y-up-z-forward-meters" />""",
            "output-alias" => """<TerraFXNativeMesh Include="mesh.epkg" ProfileRecord="profile.ebin" MeshAssetId="200" PackagePath="other.epkg" />""",
            _ => throw new ArgumentOutOfRangeException(nameof(failure)),
        };
        project = await WriteBuildProjectAsync(extra).ConfigureAwait(false);
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await RunBuildAsync(project, expectCook: false).ConfigureAwait(false));
        var expectedMessage = failure switch {
            "association" => "MeshSource must match exactly one",
            "duplicate-source" => "source paths must be unique",
            "output-alias" => "must not overwrite any declared input",
            _ => "must not share a package output path",
        };
        Assert.That(exception?.Message, Does.Contain(expectedMessage));
        Assert.That(await File.ReadAllBytesAsync(Path.Combine(_directory, "mesh.epkg")).ConfigureAwait(false), Is.EqualTo(previous));
        await AssertUsablePackageAsync(Path.Combine(_directory, "mesh.epkg")).ConfigureAwait(false);
    }

    [TestCase(1, false)]
    [TestCase(2, true)]
    public async Task NativeCliProducesDeterministicPackagesWithExactVersionedReferenceClosure(int materialMajor, bool includeSkinAndTexture)
    {
        await NativeMeshTestData.WriteInputsAsync(
            _directory,
            NativeMeshTestData.CreateMesh(materialMajor: materialMajor, includeUv: includeSkinAndTexture, skinned: includeSkinAndTexture),
            NativeMeshTestData.CreateMaterial(materialMajor, includeSkinAndTexture ? NativeMeshTestData.TextureId : null)).ConfigureAwait(false);
        var materialPath = Path.Combine(_directory, "material=selected.ebin");
        File.Copy(Path.Combine(_directory, "material.ebin"), materialPath);
        var dependencies = new List<string> { string.Concat("101=", materialPath) };

        if (includeSkinAndTexture)
        {
            var skeletonPath = Path.Combine(_directory, "skeleton.ebin");
            var texturePath = Path.Combine(_directory, "texture.ebin");
            await File.WriteAllBytesAsync(skeletonPath, NativeMeshTestData.CreateSkeleton()).ConfigureAwait(false);
            await File.WriteAllBytesAsync(texturePath, NativeMeshTestData.CreateTexture()).ConfigureAwait(false);
            dependencies.Add(string.Concat("102=", skeletonPath));
            dependencies.Add(string.Concat("103=", texturePath));
        }

        var arguments = NativeMeshTestData.CookArguments(_directory, [.. dependencies]);
        string[] cliArguments = [typeof(NativeMeshCookCommand).Assembly.Location, "cook-native-mesh", .. arguments];
        var result = await ToolProcessRunner.RunAsync(
            "dotnet",
            _directory,
            cliArguments,
            TimeSpan.FromMinutes(2),
            65_536).ConfigureAwait(false);
        Assert.That(result.ExitCode, Is.Zero);
        var outputPath = Path.Combine(_directory, "mesh.epkg");
        var first = await File.ReadAllBytesAsync(outputPath).ConfigureAwait(false);
        dependencies.Reverse();
        _ = await NativeMeshCookCommand.RunCliAsync(
            NativeMeshTestData.CookArguments(_directory, [.. dependencies]),
            CancellationToken.None).ConfigureAwait(false);
        Assert.That(await File.ReadAllBytesAsync(outputPath).ConfigureAwait(false), Is.EqualTo(first));

        var store = await NativeMeshTestData.OpenStoreAsync(outputPath).ConfigureAwait(false);
        await using var storeLifetime = store.ConfigureAwait(false);
        using var mesh = await store.LoadAsync<RenderMeshAsset2>(new AssetId(100)).ConfigureAwait(false);
        var membership = await RenderMeshAsset2Binding.ValidatePackageBindingsAsync(mesh).ConfigureAwait(false);
        Assert.That(mesh.Value.Levels, Has.Length.EqualTo(2));
        Assert.That(mesh.Value.Thresholds, Is.EqualTo([0.625f]));
        Assert.That(mesh.Value.MaterialSlots[0].SchemaMajor, Is.EqualTo(materialMajor));
        int[] expectedIds = includeSkinAndTexture ? [100, 101, 102, 103] : [100, 101];
        Assert.That(membership.Select(static id => id.Value), Is.EqualTo(expectedIds));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(first.AsSpan(4)), Is.EqualTo(2));
        var directoryOffset = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(first.AsSpan(32)));
        var envelopeOffset = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(first.AsSpan(directoryOffset + 64 + 8)));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(first.AsSpan(envelopeOffset + 48)), Is.EqualTo(2));
    }

    [TestCase("coarse-data")]
    [TestCase("missing-material")]
    [TestCase("material-version")]
    [TestCase("missing-texture")]
    [TestCase("missing-skeleton")]
    [TestCase("unused-dependency")]
    [TestCase("duplicate-id")]
    [TestCase("root-id-collision")]
    [TestCase("unknown-option")]
    [TestCase("output-alias")]
    [TestCase("record-bytes")]
    [TestCase("workspace-bytes")]
    [TestCase("aggregate-indices")]
    public async Task RefusalPreservesThePreviouslyUsablePackage(string failure)
    {
        await NativeMeshTestData.WriteInputsAsync(_directory).ConfigureAwait(false);
        _ = await NativeMeshCookCommand.RunCliAsync(CookArguments(), CancellationToken.None).ConfigureAwait(false);
        var outputPath = Path.Combine(_directory, "mesh.epkg");
        var previous = await File.ReadAllBytesAsync(outputPath).ConfigureAwait(false);
        var arguments = CookArguments();

        switch (failure)
        {
            case "coarse-data":
            {
                var invalid = NativeMeshTestData.Rewrite(
                    NativeMeshTestData.CreateMesh(),
                    8,
                    new ContentRecordField(8, 24, 1, ContentPackageWriter.EncodeRecordArray([NativeMeshTestData.CreateLevel([0, 0, 1])])));
                await File.WriteAllBytesAsync(Path.Combine(_directory, "mesh.ebin"), invalid).ConfigureAwait(false);
                break;
            }

            case "missing-material":
            {
                arguments = NativeMeshTestData.CookArguments(_directory);
                break;
            }

            case "material-version":
            {
                await File.WriteAllBytesAsync(Path.Combine(_directory, "material.ebin"), NativeMeshTestData.CreateMaterial(2)).ConfigureAwait(false);
                break;
            }

            case "missing-texture":
            {
                await NativeMeshTestData.WriteInputsAsync(
                    _directory,
                    NativeMeshTestData.CreateMesh(includeUv: true),
                    NativeMeshTestData.CreateMaterial(textureId: 103)).ConfigureAwait(false);
                break;
            }

            case "missing-skeleton":
            {
                await File.WriteAllBytesAsync(Path.Combine(_directory, "mesh.ebin"), NativeMeshTestData.CreateMesh(skinned: true)).ConfigureAwait(false);
                break;
            }

            case "unused-dependency":
            {
                var texturePath = Path.Combine(_directory, "texture.ebin");
                await File.WriteAllBytesAsync(texturePath, NativeMeshTestData.CreateTexture()).ConfigureAwait(false);
                arguments = CookArguments(string.Concat("103=", texturePath));
                break;
            }

            case "duplicate-id":
            {
                arguments = CookArguments(string.Concat("101=", Path.Combine(_directory, "material.ebin")));
                break;
            }

            case "root-id-collision":
            {
                arguments = CookArguments(string.Concat("100=", Path.Combine(_directory, "material.ebin")));
                break;
            }

            case "unknown-option":
            {
                arguments = [.. arguments, "--unknown", "value"];
                break;
            }

            case "output-alias":
            {
                arguments[^1] = Path.Combine(_directory, ".", "material.ebin");
                break;
            }

            case "record-bytes":
            case "workspace-bytes":
            {
                var largePath = Path.Combine(_directory, "large.ebin");
                using (var stream = File.Create(largePath))
                {
                    stream.SetLength(failure == "record-bytes" ? 268_435_457 : 134_217_728);
                }

                arguments = CookArguments(failure == "record-bytes"
                    ? [string.Concat("104=", largePath)]
                    : [.. Enumerable.Range(104, 8).Select(id => string.Concat(id.ToString(CultureInfo.InvariantCulture), "=", largePath))]);
                break;
            }

            case "aggregate-indices":
            {
                var indices = NativeMeshTestData.RepeatTriangle(174_763);
                var mesh = NativeMeshTestData.Rewrite(
                    NativeMeshTestData.CreateMesh(),
                    3,
                    new ContentRecordField(3, 22, 1, NativeMeshTestData.CreateIndices(indices, wide: false)));
                mesh = NativeMeshTestData.Rewrite(mesh, 4, new ContentRecordField(
                    4, 24, 1, NativeMeshTestData.CreateGroups(indexCount: checked((uint)indices.Length))));
                mesh = NativeMeshTestData.Rewrite(mesh, 8, new ContentRecordField(
                    8, 24, 1, ContentPackageWriter.EncodeRecordArray([NativeMeshTestData.CreateLevel(indices)])));
                await File.WriteAllBytesAsync(Path.Combine(_directory, "mesh.ebin"), mesh).ConfigureAwait(false);
                break;
            }

            default:
            {
                throw new ArgumentOutOfRangeException(nameof(failure));
            }
        }

        var exception = Assert.CatchAsync<Exception>(async () =>
            await NativeMeshCookCommand.RunCliAsync(arguments, CancellationToken.None).ConfigureAwait(false));
        Assert.That(exception, Is.InstanceOf<ArgumentException>()
            .Or.InstanceOf<InvalidDataException>()
            .Or.InstanceOf<KeyNotFoundException>()
            .Or.InstanceOf<NotSupportedException>());
        Assert.That(await File.ReadAllBytesAsync(outputPath).ConfigureAwait(false), Is.EqualTo(previous));
        Assert.That(Directory.GetFiles(_directory, "*.staging"), Is.Empty);
        await AssertUsablePackageAsync(outputPath).ConfigureAwait(false);
    }

    [SetUp]
    public void SetUp() => _directory = NativeMeshTestData.CreateDirectory();

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    private string[] CookArguments(params string[] additionalDependencies)
        => NativeMeshTestData.CookArguments(
            _directory,
            [string.Concat("101=", Path.Combine(_directory, "material.ebin")), .. additionalDependencies]);

    private async Task<string> WriteBuildProjectAsync(string extraItems = "")
    {
        var targetsPath = Path.Combine(_directory, "ContentCooking.targets");
        File.Copy(
            Path.Combine(NativeMeshTestData.FindRepositoryRoot(), "sources", "TerraFX.Content.Tools", "ContentCooking.targets"),
            targetsPath,
            overwrite: true);
        var toolPath = SecurityElement.Escape(typeof(NativeMeshCookCommand).Assembly.Location);
        var projectPath = Path.Combine(_directory, "cook.proj");
        await File.WriteAllTextAsync(projectPath, $"""
            <Project>
              <PropertyGroup>
                <TerraFXContentToolsAssembly>{toolPath}</TerraFXContentToolsAssembly>
                <MSBuildAllProjects>$(MSBuildProjectFullPath)</MSBuildAllProjects>
              </PropertyGroup>
              <ItemGroup>
                <TerraFXNativeMesh Include="mesh.ebin" ProfileRecord="profile.ebin" MeshAssetId="100" PackagePath="mesh.epkg" />
                <TerraFXNativeMeshAsset Include="material.ebin" AssetId="101" MeshSource=".\mesh.ebin" />
                {extraItems}
              </ItemGroup>
              <Import Project="ContentCooking.targets" />
              <Target Name="Build" />
            </Project>
            """).ConfigureAwait(false);
        return projectPath;
    }
}
