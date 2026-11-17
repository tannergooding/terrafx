// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using TerraFX.Assets;
using TerraFX.Graphics;
using TerraFX.Rendering;

namespace TerraFX.Content.Tools;

internal static class NativeParticleCookCommand
{
    internal static async Task<int> RunCliAsync(string[] arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        cancellationToken.ThrowIfCancellationRequested();

        if (arguments.Length < 8)
        {
            await Console.Out.WriteLineAsync(
                "Usage: TerraFX.Content.Tools cook-native-particle-emitter --source <emitter-1.ebin> --profile-record <profile.ebin> --emitter-id <id> [--asset <id>=<record-path>]... --output <package>",
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
        var profile = await NativeMeshCookCommand.ReadInputAsync(
            options.ProfilePath,
            profileLength,
            cancellationToken).ConfigureAwait(false);
        var source = await NativeMeshCookCommand.ReadInputAsync(
            options.SourcePath,
            lengths[0],
            cancellationToken).ConfigureAwait(false);
        _ = ContentPackageWriter.ValidateRecord(
            source,
            new ContentSchemaIdentityAssignment("native/cosmetic-particle-emitter").SchemaId,
            1,
            0,
            "native Cosmetic Particle Emitter",
            ContentPackageWriter.AssetRecordLimits);
        var assets = new ContentPackageAsset[lengths.Length];
        var expectedIds = new HashSet<AssetId> { new(checked((int)options.EmitterAssetId)) };
        assets[0] = new ContentPackageAsset(options.EmitterAssetId, source);

        for (var index = 0; index < options.Dependencies.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dependency = options.Dependencies[index];
            var bytes = await NativeMeshCookCommand.ReadInputAsync(
                dependency.Path,
                lengths[index + 1],
                cancellationToken).ConfigureAwait(false);
            assets[index + 1] = new ContentPackageAsset(dependency.AssetId, bytes);
            _ = expectedIds.Add(new AssetId(checked((int)dependency.AssetId)));
        }

        var registration = NativeMeshCookCommand.CreateRegistration(includeParticles: true);
        cancellationToken.ThrowIfCancellationRequested();
        var package = ContentPackageWriter.CreateNativeAssetPackage(profile, assets, registration.Schemas);
        var digest = SHA256.HashData(package);
        await ContentPackageWriter.WriteValidatedAtomicallyAsync(
            options.OutputPath,
            package,
            (path, token) => ValidatePackageAsync(path, digest, options.EmitterAssetId, expectedIds, registration, token),
            cancellationToken).ConfigureAwait(false);
        await Console.Out.WriteLineAsync(
            $"Cooked one native Cosmetic Particle Emitter 1.0 and its exact reference closure to '{options.OutputPath}'.",
            CancellationToken.None).ConfigureAwait(false);
        return 0;
    }

    private static NativeParticleCookOptions ParseOptions(ReadOnlySpan<string> arguments)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        var dependencies = new List<NativeParticleDependency>();

        for (var index = 0; index < arguments.Length; index += 2)
        {
            var key = arguments[index];

            if ((index + 1 >= arguments.Length) || !key.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException("A native particle cook option is missing its value.");
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
                    throw new ArgumentException("A native particle package cannot contain more than 131072 entries.");
                }

                dependencies.Add(new NativeParticleDependency(
                    NativeMeshCookCommand.ParseAssetId(value[..separator]),
                    Path.GetFullPath(value[(separator + 1)..])));
            }
            else if (!options.TryAdd(key, value))
            {
                throw new ArgumentException($"The native particle cook option '{key}' is repeated.");
            }
        }

        if ((options.Count != 4)
            || !options.TryGetValue("--source", out var sourcePath)
            || !options.TryGetValue("--profile-record", out var profilePath)
            || !options.TryGetValue("--emitter-id", out var emitterIdText)
            || !options.TryGetValue("--output", out var outputPath))
        {
            throw new ArgumentException("The native particle cook command has missing or unrecognized options.");
        }

        var emitterAssetId = NativeMeshCookCommand.ParseAssetId(emitterIdText);
        var ids = new HashSet<uint> { emitterAssetId };
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
                throw new ArgumentException("Native package AssetIds must be unique, including the root Emitter.");
            }

            if (pathComparer.Equals(resolvedOutputPath, dependency.Path))
            {
                throw new ArgumentException("The native package output must not overwrite one of its declared inputs.");
            }
        }

        return new NativeParticleCookOptions(
            resolvedSourcePath,
            resolvedProfilePath,
            emitterAssetId,
            resolvedOutputPath,
            [.. dependencies]);
    }

    private static async Task ValidatePackageAsync(
        string path,
        byte[] digest,
        uint emitterAssetId,
        HashSet<AssetId> expectedIds,
        AssetStoreRegistration registration,
        CancellationToken cancellationToken)
    {
        var store = await AssetStore.CreateAsync(
            new AssetStoreOptions(path, digest, registration),
            cancellationToken).ConfigureAwait(false);
        await using (store.ConfigureAwait(false))
        {
            using var emitterLease = await store.LoadAsync<CosmeticParticleEmitterAsset>(
                new AssetId(checked((int)emitterAssetId)),
                cancellationToken).ConfigureAwait(false);
            var actualIds = await CosmeticParticleEmitterAssetBinding.ValidatePackageBindingsAsync(
                emitterLease,
                cancellationToken).ConfigureAwait(false);

            if (actualIds.Length != expectedIds.Count)
            {
                throw new InvalidDataException("Every supplied dependency must belong to the emitter's exact reference closure.");
            }

            foreach (var assetId in actualIds)
            {
                if (!expectedIds.Contains(assetId))
                {
                    throw new InvalidDataException("The cosmetic emitter references an undeclared package dependency.");
                }
            }
        }
    }

    private readonly record struct NativeParticleCookOptions(
        string SourcePath,
        string ProfilePath,
        uint EmitterAssetId,
        string OutputPath,
        NativeParticleDependency[] Dependencies);

    private readonly record struct NativeParticleDependency(uint AssetId, string Path);
}
