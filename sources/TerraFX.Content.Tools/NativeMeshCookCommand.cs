// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using TerraFX.Animation;
using TerraFX.Assets;
using TerraFX.Graphics;
using TerraFX.Rendering;
using TerraFX.Scenes;

namespace TerraFX.Content.Tools;

internal static class NativeMeshCookCommand
{
    internal static AssetStoreRegistration CreateRegistration(bool includeParticles = false)
    {
        var schemas = new CompiledContentSchemaRegistry([
            .. CoreContentSchemaCatalogue.PrimitiveSchemas,
            .. CoreContentSchemaCatalogue.FoundationSchemas,
            .. NativeAssetSchemaCatalogue.Schemas,
            .. SceneSchemaCatalogue.Schemas,
            .. RenderingSchemaCatalogue.Schemas,
            .. RenderingSchemaCatalogue.M05Schemas,
            .. AnimationSchemaCatalogue.Schemas,
        ]);
        var builder = new AssetStoreRegistrationBuilder(schemas);
        _ = Renderer.RegisterAssetCodecs(builder);
        var features = RendererCpuFeatures.MeshLod
            | (includeParticles ? RendererCpuFeatures.CosmeticParticles : RendererCpuFeatures.Static);
        _ = Renderer.RegisterM05AssetCodecs(builder, features);

        _ = AnimationSupport.RegisterAssetCodecs(builder);
        return builder.Build();
    }

    internal static uint ParseAssetId(string text)
    {
        if (!uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            || (value is 0 or > int.MaxValue))
        {
            throw new ArgumentException("Native package AssetIds must be in [1,2147483647].");
        }

        return value;
    }

    internal static async Task<byte[]> ReadInputAsync(string path, long expectedLength, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            65_536,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using (stream.ConfigureAwait(false))
        {
            if (stream.Length != expectedLength)
            {
                throw new IOException("A native cook input changed length after workspace admission.");
            }

            var bytes = new byte[checked((int)expectedLength)];
            await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);

            if ((stream.Length != expectedLength) || (stream.Position != expectedLength))
            {
                throw new IOException("A native cook input changed length while being read.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            return bytes;
        }
    }

    internal static async Task<int> RunCliAsync(string[] arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        cancellationToken.ThrowIfCancellationRequested();

        if (arguments.Length < 8)
        {
            await Console.Out.WriteLineAsync(
                "Usage: TerraFX.Content.Tools cook-native-mesh --source <mesh-2.ebin> --profile-record <profile.ebin> --mesh-id <id> [--asset <id>=<record-path>]... --output <package>",
                cancellationToken)
                .ConfigureAwait(false);
            return 2;
        }

        var options = ParseOptions(arguments);
        var profileLength = new FileInfo(options.ProfilePath).Length;
        var lengths = new long[checked(options.Dependencies.Length + 1)];
        lengths[0] = new FileInfo(options.SourcePath).Length;

        for (var index = 0; index < options.Dependencies.Length; index++)
        {
            lengths[index + 1] = new FileInfo(options.Dependencies[index].Path).Length;
        }

        _ = ContentPackageWriter.MeasureNativeAssetPackage(profileLength, lengths);
        var profile = await ReadInputAsync(options.ProfilePath, profileLength, cancellationToken).ConfigureAwait(false);
        var source = await ReadInputAsync(options.SourcePath, lengths[0], cancellationToken).ConfigureAwait(false);
        _ = ContentPackageWriter.ValidateRecord(
            source,
            new ContentSchemaIdentityAssignment("native/mesh").SchemaId,
            2,
            0,
            "native Mesh",
            ContentPackageWriter.AssetRecordLimits);
        var assets = new ContentPackageAsset[lengths.Length];
        var expectedIds = new HashSet<AssetId> { new(checked((int)options.MeshAssetId)) };
        assets[0] = new ContentPackageAsset(options.MeshAssetId, source);

        for (var index = 0; index < options.Dependencies.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dependency = options.Dependencies[index];
            var bytes = await ReadInputAsync(dependency.Path, lengths[index + 1], cancellationToken).ConfigureAwait(false);
            assets[index + 1] = new ContentPackageAsset(dependency.AssetId, bytes);
            _ = expectedIds.Add(new AssetId(checked((int)dependency.AssetId)));
        }

        var registration = CreateRegistration();
        cancellationToken.ThrowIfCancellationRequested();
        var package = ContentPackageWriter.CreateNativeAssetPackage(profile, assets, registration.Schemas);
        var digest = SHA256.HashData(package);
        await ContentPackageWriter.WriteValidatedAtomicallyAsync(
            options.OutputPath,
            package,
            (path, token) => ValidatePackageAsync(path, digest, options.MeshAssetId, expectedIds, registration, token),
            cancellationToken).ConfigureAwait(false);
        await Console.Out.WriteLineAsync(
            $"Cooked one native Mesh 2.0 and its exact reference closure to '{options.OutputPath}'.",
            CancellationToken.None).ConfigureAwait(false);
        return 0;
    }

    private static NativeMeshCookOptions ParseOptions(ReadOnlySpan<string> arguments)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        var dependencies = new List<NativeMeshDependency>();

        for (var index = 0; index < arguments.Length; index += 2)
        {
            var key = arguments[index];

            if ((index + 1 >= arguments.Length) || !key.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException("A native Mesh cook option is missing its value.");
            }

            var value = arguments[index + 1];

            if (StringComparer.Ordinal.Equals(key, "--asset"))
            {
                var separator = value.IndexOf('=', StringComparison.Ordinal);

                if ((separator <= 0) || (separator == value.Length - 1))
                {
                    throw new ArgumentException("--asset requires <positive-id>=<record-path>.");
                }

                if (dependencies.Count >= 131_071)
                {
                    throw new ArgumentException("A native Mesh package cannot contain more than 131072 entries.");
                }

                dependencies.Add(new NativeMeshDependency(
                    ParseAssetId(value[..separator]),
                    Path.GetFullPath(value[(separator + 1)..])));
            }
            else if (!options.TryAdd(key, value))
            {
                throw new ArgumentException($"The native Mesh cook option '{key}' is repeated.");
            }
        }

        if ((options.Count != 4)
            || !options.TryGetValue("--source", out var sourcePath)
            || !options.TryGetValue("--profile-record", out var profilePath)
            || !options.TryGetValue("--mesh-id", out var meshIdText)
            || !options.TryGetValue("--output", out var outputPath))
        {
            throw new ArgumentException("The native Mesh cook command has missing or unrecognized options.");
        }

        var meshAssetId = ParseAssetId(meshIdText);
        var ids = new HashSet<uint> { meshAssetId };
        var resolvedSourcePath = Path.GetFullPath(sourcePath);
        var resolvedProfilePath = Path.GetFullPath(profilePath);
        var resolvedOutputPath = Path.GetFullPath(outputPath);
        var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        if (pathComparer.Equals(resolvedOutputPath, resolvedSourcePath)
            || pathComparer.Equals(resolvedOutputPath, resolvedProfilePath))
        {
            throw new ArgumentException("The native package output must not overwrite one of its declared inputs.");
        }

        foreach (var dependency in dependencies)
        {
            if (!ids.Add(dependency.AssetId))
            {
                throw new ArgumentException("Native package AssetIds must be unique, including the root Mesh.");
            }

            if (pathComparer.Equals(resolvedOutputPath, dependency.Path))
            {
                throw new ArgumentException("The native package output must not overwrite one of its declared inputs.");
            }
        }

        return new NativeMeshCookOptions(
            resolvedSourcePath,
            resolvedProfilePath,
            meshAssetId,
            resolvedOutputPath,
            [.. dependencies]);
    }

    private static async Task ValidatePackageAsync(
        string path,
        byte[] digest,
        uint meshAssetId,
        HashSet<AssetId> expectedIds,
        AssetStoreRegistration registration,
        CancellationToken cancellationToken)
    {
        var store = await AssetStore.CreateAsync(
            new AssetStoreOptions(path, digest, registration),
            cancellationToken).ConfigureAwait(false);
        await using (store.ConfigureAwait(false))
        {
            using var meshLease = await store.LoadAsync<RenderMeshAsset2>(
                new AssetId(checked((int)meshAssetId)),
                cancellationToken).ConfigureAwait(false);
            var actualIds = await RenderMeshAsset2Binding.ValidatePackageBindingsAsync(
                meshLease,
                cancellationToken).ConfigureAwait(false);

            if (actualIds.Length != expectedIds.Count)
            {
                throw new InvalidDataException("Every supplied dependency must belong to the root Mesh's exact reference closure.");
            }

            foreach (var assetId in actualIds)
            {
                if (!expectedIds.Contains(assetId))
                {
                    throw new InvalidDataException("The native Mesh references an undeclared package dependency.");
                }
            }
        }
    }

    private readonly record struct NativeMeshCookOptions(
        string SourcePath,
        string ProfilePath,
        uint MeshAssetId,
        string OutputPath,
        NativeMeshDependency[] Dependencies);

    private readonly record struct NativeMeshDependency(uint AssetId, string Path);
}
