// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX.Assets;
using TerraFX.Content.Tools;
using TerraFX.Rendering;

namespace TerraFX.Content.Tools.Tests;

[TestFixture]
internal sealed class NativeParticleCookCommandTests
{
    private string _directory = string.Empty;

    private static async Task AssertUsablePackageAsync(string path)
    {
        var registration = NativeMeshTestData.CreateRegistration(includeParticles: true);
        var store = await NativeMeshTestData.OpenStoreAsync(path, registration).ConfigureAwait(false);
        await using var storeLifetime = store.ConfigureAwait(false);
        using var emitter = await store.LoadAsync<CosmeticParticleEmitterAsset>(
            new AssetId(checked((int)NativeMeshTestData.ParticleEmitterId))).ConfigureAwait(false);
        _ = await CosmeticParticleEmitterAssetBinding.ValidatePackageBindingsAsync(emitter).ConfigureAwait(false);
    }

    private static async Task RunBuildAsync(string projectPath, bool expectCook)
    {
        var result = await ToolProcessRunner.RunAsync(
            "dotnet",
            Path.GetDirectoryName(projectPath)!,
            new[] { "msbuild", projectPath, "-target:Build", "-verbosity:minimal", "-nologo" },
            TimeSpan.FromMinutes(2),
            65_536).ConfigureAwait(false);
        Assert.That(result.ExitCode, Is.Zero);
        Assert.That(result.StandardOutput.Contains("Cooked one native Cosmetic Particle Emitter", StringComparison.Ordinal), Is.EqualTo(expectCook));
    }

    private static async Task RunBuildFailureAsync(string projectPath, string expectedMessage)
    {
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await ToolProcessRunner.RunAsync(
                "dotnet",
                Path.GetDirectoryName(projectPath)!,
                new[] { "msbuild", projectPath, "-target:Build", "-verbosity:minimal", "-nologo" },
                TimeSpan.FromMinutes(2),
                65_536).ConfigureAwait(false));
        Assert.That(exception?.Message, Does.Contain(expectedMessage));
    }

    [TestCase(1)]
    [TestCase(2)]
    public async Task CliPublishesARealDeterministicPackageForMaterialOneAndTwo(int materialMajor)
    {
        await WriteInputsAsync(materialMajor).ConfigureAwait(false);
        var dependencies = new List<string> {
            string.Concat(NativeMeshTestData.ParticleAtlasId, "=", Path.Combine(_directory, "atlas.ebin")),
            string.Concat(NativeMeshTestData.ParticlePageId, "=", Path.Combine(_directory, "page.ebin")),
            string.Concat(NativeMeshTestData.ParticleMaterialId, "=", Path.Combine(_directory, "material.ebin")),
        };
        string[] cliArguments = [
            typeof(NativeParticleCookCommand).Assembly.Location,
            "cook-native-particle-emitter",
            .. CookArguments([.. dependencies]),
        ];
        var result = await ToolProcessRunner.RunAsync(
            "dotnet",
            _directory,
            cliArguments,
            TimeSpan.FromMinutes(2),
            65_536).ConfigureAwait(false);

        Assert.That(result.ExitCode, Is.Zero);
        Assert.That(result.StandardOutput, Does.Contain("Cooked one native Cosmetic Particle Emitter"));
        var outputPath = Path.Combine(_directory, "particles.epkg");
        var first = await File.ReadAllBytesAsync(outputPath).ConfigureAwait(false);
        dependencies.Reverse();
        _ = await NativeParticleCookCommand.RunCliAsync(
            CookArguments([.. dependencies]),
            CancellationToken.None).ConfigureAwait(false);
        Assert.That(await File.ReadAllBytesAsync(outputPath).ConfigureAwait(false), Is.EqualTo(first));

        var registration = NativeMeshTestData.CreateRegistration(includeParticles: true);
        var store = await NativeMeshTestData.OpenStoreAsync(outputPath, registration).ConfigureAwait(false);
        await using var storeLifetime = store.ConfigureAwait(false);
        using var emitter = await store.LoadAsync<CosmeticParticleEmitterAsset>(
            new AssetId(checked((int)NativeMeshTestData.ParticleEmitterId))).ConfigureAwait(false);
        var membership = await CosmeticParticleEmitterAssetBinding.ValidatePackageBindingsAsync(emitter).ConfigureAwait(false);

        Assert.That(emitter.Value.Material?.SchemaMajor, Is.EqualTo(materialMajor));
        Assert.That(membership.Select(static id => id.Value), Is.EqualTo([
            NativeMeshTestData.ParticleEmitterId,
            NativeMeshTestData.ParticleAtlasId,
            NativeMeshTestData.ParticlePageId,
            NativeMeshTestData.ParticleMaterialId,
        ]));
    }

    [Test]
    public async Task CliReportsAnOutOfRangeSchemaVersionAsAnInputError()
    {
        await WriteInputsAsync(includeMaterial: false).ConfigureAwait(false);
        var emitter = NativeMeshTestData.CreateParticleEmitterRecord();
        BinaryPrimitives.WriteUInt32LittleEndian(emitter.AsSpan(48), (uint)int.MaxValue + 1);
        await File.WriteAllBytesAsync(Path.Combine(_directory, "emitter.ebin"), emitter).ConfigureAwait(false);
        string[] cliArguments = [
            typeof(NativeParticleCookCommand).Assembly.Location,
            "cook-native-particle-emitter",
            .. CookArguments([
                string.Concat(NativeMeshTestData.ParticleAtlasId, "=", Path.Combine(_directory, "atlas.ebin")),
                string.Concat(NativeMeshTestData.ParticlePageId, "=", Path.Combine(_directory, "page.ebin")),
            ]),
        ];

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await ToolProcessRunner.RunAsync(
                "dotnet",
                _directory,
                cliArguments,
                TimeSpan.FromMinutes(2),
                65_536).ConfigureAwait(false));

        Assert.That(exception?.Message, Does.Contain("Content.Tools:"));
        Assert.That(File.Exists(Path.Combine(_directory, "particles.epkg")), Is.False);
    }

    [Test]
    public async Task InvalidFramePreservesThePreviouslyUsableOutput()
    {
        await WriteInputsAsync(includeMaterial: false).ConfigureAwait(false);
        var arguments = CookArguments([
            string.Concat(NativeMeshTestData.ParticleAtlasId, "=", Path.Combine(_directory, "atlas.ebin")),
            string.Concat(NativeMeshTestData.ParticlePageId, "=", Path.Combine(_directory, "page.ebin")),
        ]);
        _ = await NativeParticleCookCommand.RunCliAsync(arguments, CancellationToken.None).ConfigureAwait(false);
        var outputPath = Path.Combine(_directory, "particles.epkg");
        var previous = await File.ReadAllBytesAsync(outputPath).ConfigureAwait(false);
        await File.WriteAllBytesAsync(
            Path.Combine(_directory, "emitter.ebin"),
            NativeMeshTestData.CreateParticleEmitterRecord(frameId: new Guid("80000000-0000-4000-8000-000000000002"))).ConfigureAwait(false);

        _ = Assert.ThrowsAsync<InvalidDataException>(async () =>
            await NativeParticleCookCommand.RunCliAsync(arguments, CancellationToken.None).ConfigureAwait(false));

        Assert.That(await File.ReadAllBytesAsync(outputPath).ConfigureAwait(false), Is.EqualTo(previous));
        Assert.That(Directory.GetFiles(_directory, "*.staging"), Is.Empty);
        await AssertUsablePackageAsync(outputPath).ConfigureAwait(false);
    }

    [Test]
    public async Task MissingMaterialUsesTheDefaultProfileAndOnlyTheExactAtlasClosure()
    {
        await WriteInputsAsync(includeMaterial: false).ConfigureAwait(false);
        _ = await NativeParticleCookCommand.RunCliAsync(
            CookArguments([
                string.Concat(NativeMeshTestData.ParticleAtlasId, "=", Path.Combine(_directory, "atlas.ebin")),
                string.Concat(NativeMeshTestData.ParticlePageId, "=", Path.Combine(_directory, "page.ebin")),
            ]),
            CancellationToken.None).ConfigureAwait(false);
        var outputPath = Path.Combine(_directory, "particles.epkg");
        var store = await NativeMeshTestData.OpenStoreAsync(
            outputPath,
            NativeMeshTestData.CreateRegistration(includeParticles: true)).ConfigureAwait(false);
        await using var storeLifetime = store.ConfigureAwait(false);
        using var emitter = await store.LoadAsync<CosmeticParticleEmitterAsset>(
            new AssetId(checked((int)NativeMeshTestData.ParticleEmitterId))).ConfigureAwait(false);
        var membership = await CosmeticParticleEmitterAssetBinding.ValidatePackageBindingsAsync(emitter).ConfigureAwait(false);

        Assert.That(emitter.Value.Material, Is.Null);
        Assert.That(membership.Select(static id => id.Value), Is.EqualTo([
            NativeMeshTestData.ParticleEmitterId,
            NativeMeshTestData.ParticleAtlasId,
            NativeMeshTestData.ParticlePageId,
        ]));
    }

    [TestCase("association")]
    [TestCase("output-alias")]
    public async Task MsBuildRejectsParticleAssociationAndOutputAlias(string failure)
    {
        await WriteInputsAsync(includeMaterial: false).ConfigureAwait(false);
        var project = await WriteBuildProjectAsync(includeMaterial: false).ConfigureAwait(false);
        await RunBuildAsync(project, expectCook: true).ConfigureAwait(false);
        var outputPath = Path.Combine(_directory, "particles.epkg");
        var previous = await File.ReadAllBytesAsync(outputPath).ConfigureAwait(false);
        var extra = failure switch {
            "association" => """<TerraFXNativeParticleEmitterAsset Include="atlas.ebin" AssetId="304" EmitterSource="missing.emitter.ebin" />""",
            "output-alias" => """<TerraFXNativeParticleEmitter Include="other.emitter.ebin" ProfileRecord="profile.ebin" EmitterAssetId="400" PackagePath="page.ebin" />""",
            _ => throw new ArgumentOutOfRangeException(nameof(failure)),
        };

        if (failure == "output-alias")
        {
            await File.WriteAllBytesAsync(
                Path.Combine(_directory, "other.emitter.ebin"),
                NativeMeshTestData.CreateParticleEmitterRecord()).ConfigureAwait(false);
        }

        project = await WriteBuildProjectAsync(extra, includeMaterial: false).ConfigureAwait(false);
        await RunBuildFailureAsync(
            project,
            failure switch {
                "association" => "EmitterSource must match exactly one",
                _ => "must not overwrite any declared input",
            }).ConfigureAwait(false);
        Assert.That(await File.ReadAllBytesAsync(outputPath).ConfigureAwait(false), Is.EqualTo(previous));
        await AssertUsablePackageAsync(outputPath).ConfigureAwait(false);
    }

    [Test]
    public async Task MsBuildTracksParticleInputsAndMissingOutputs()
    {
        await WriteInputsAsync(2).ConfigureAwait(false);
        var project = await WriteBuildProjectAsync().ConfigureAwait(false);
        var outputPath = Path.Combine(_directory, "particles.epkg");
        await RunBuildAsync(project, expectCook: true).ConfigureAwait(false);
        var original = await File.ReadAllBytesAsync(outputPath).ConfigureAwait(false);
        await AssertUsablePackageAsync(outputPath).ConfigureAwait(false);
        await RunBuildAsync(project, expectCook: false).ConfigureAwait(false);

        await File.WriteAllBytesAsync(
            Path.Combine(_directory, "emitter.ebin"),
            NativeMeshTestData.CreateParticleEmitterRecord(
                materialId: checked((int)NativeMeshTestData.ParticleMaterialId),
                materialMajor: 2,
                spawnPerTick: 3)).ConfigureAwait(false);
        File.SetLastWriteTimeUtc(outputPath, DateTime.UtcNow.AddSeconds(-5));
        await RunBuildAsync(project, expectCook: true).ConfigureAwait(false);
        var changed = await File.ReadAllBytesAsync(outputPath).ConfigureAwait(false);
        Assert.That(changed, Is.Not.EqualTo(original));

        await File.WriteAllBytesAsync(
            Path.Combine(_directory, "page.ebin"),
            NativeMeshTestData.CreateParticleAtlasPageTexture(254)).ConfigureAwait(false);
        File.SetLastWriteTimeUtc(outputPath, DateTime.UtcNow.AddSeconds(-5));
        await RunBuildAsync(project, expectCook: true).ConfigureAwait(false);
        Assert.That(await File.ReadAllBytesAsync(outputPath).ConfigureAwait(false), Is.Not.EqualTo(changed));

        File.Delete(outputPath);
        await RunBuildAsync(project, expectCook: true).ConfigureAwait(false);
        await AssertUsablePackageAsync(outputPath).ConfigureAwait(false);
    }

    [SetUp]
    public void SetUp()
    {
        _directory = NativeMeshTestData.CreateDirectory();
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_directory, recursive: true);
    }

    private string[] CookArguments(string[] dependencies)
        => [
            "--source", Path.Combine(_directory, "emitter.ebin"),
            "--profile-record", Path.Combine(_directory, "profile.ebin"),
            "--emitter-id", NativeMeshTestData.ParticleEmitterId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            .. dependencies.SelectMany(static value => new[] { "--asset", value }),
            "--output", Path.Combine(_directory, "particles.epkg"),
        ];

    private async Task<string> WriteBuildProjectAsync(string extraItems = "", bool includeMaterial = true)
    {
        var targetsPath = Path.Combine(_directory, "ContentCooking.targets");
        File.Copy(
            Path.Combine(NativeMeshTestData.FindRepositoryRoot(), "sources", "TerraFX.Content.Tools", "ContentCooking.targets"),
            targetsPath,
            overwrite: true);
        var toolPath = SecurityElement.Escape(typeof(NativeParticleCookCommand).Assembly.Location);
        var materialItem = includeMaterial
            ? """<TerraFXNativeParticleEmitterAsset Include="material.ebin" AssetId="303" EmitterSource="emitter.ebin" />"""
            : string.Empty;
        var projectPath = Path.Combine(_directory, "cook.proj");
        await File.WriteAllTextAsync(projectPath, $"""
            <Project>
              <PropertyGroup>
                <TerraFXContentToolsAssembly>{toolPath}</TerraFXContentToolsAssembly>
                <MSBuildAllProjects>$(MSBuildProjectFullPath)</MSBuildAllProjects>
              </PropertyGroup>
              <ItemGroup>
                <TerraFXNativeParticleEmitter Include="emitter.ebin" ProfileRecord="profile.ebin" EmitterAssetId="300" PackagePath="particles.epkg" />
                <TerraFXNativeParticleEmitterAsset Include="atlas.ebin" AssetId="301" EmitterSource=".\emitter.ebin" />
                <TerraFXNativeParticleEmitterAsset Include="page.ebin" AssetId="302" EmitterSource="emitter.ebin" />
                {materialItem}
                {extraItems}
              </ItemGroup>
              <Import Project="ContentCooking.targets" />
              <Target Name="Build" />
            </Project>
            """).ConfigureAwait(false);
        return projectPath;
    }

    private async Task WriteInputsAsync(int materialMajor = 1, bool includeMaterial = true, Guid? frameId = null)
    {
        await File.WriteAllBytesAsync(
            Path.Combine(_directory, "profile.ebin"),
            NativeMeshTestData.GetProfileRecord()).ConfigureAwait(false);
        await File.WriteAllBytesAsync(
            Path.Combine(_directory, "emitter.ebin"),
            NativeMeshTestData.CreateParticleEmitterRecord(
                materialId: includeMaterial ? checked((int)NativeMeshTestData.ParticleMaterialId) : null,
                materialMajor: materialMajor,
                frameId: frameId)).ConfigureAwait(false);
        await File.WriteAllBytesAsync(
            Path.Combine(_directory, "atlas.ebin"),
            NativeMeshTestData.CreateParticleAtlasRecord()).ConfigureAwait(false);
        await File.WriteAllBytesAsync(
            Path.Combine(_directory, "page.ebin"),
            NativeMeshTestData.CreateParticleAtlasPageTexture()).ConfigureAwait(false);

        if (includeMaterial)
        {
            await File.WriteAllBytesAsync(
                Path.Combine(_directory, "material.ebin"),
                NativeMeshTestData.CreateParticleMaterialRecord(materialMajor)).ConfigureAwait(false);
        }
    }
}
