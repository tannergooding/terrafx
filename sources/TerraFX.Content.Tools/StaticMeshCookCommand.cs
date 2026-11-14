// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Graphics;
using TerraFX.Rendering;
using TerraFX.Scenes;

namespace TerraFX.Content.Tools;

internal static class StaticMeshCookCommand
{
    private const int MaximumMaterialRecordBytes = 268_435_456;
    private const int MaximumProfileRecordBytes = 65_536;
    private static readonly string[] s_usageLines = [
        "Usage:",
        "  TerraFX.Content.Tools cook-static-mesh <glb|obj> --source <path> --profile-record <path> --material-record <path> --mesh-id <id> --material-id <id> --output <package>",
        "  OBJ additionally requires --obj-convention rh-y-up-z-forward-meters",
    ];

    internal static async Task<int> RunCliAsync(string[] arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Length < 13)
        {
            await WriteUsageAsync().ConfigureAwait(false);
            return 2;
        }

        var format = arguments[0] switch {
            "glb" => SourceFormat.Glb,
            "obj" => SourceFormat.Obj,
            _ => throw new ArgumentException("The selected source format must be 'glb' or 'obj'.", nameof(arguments)),
        };
        var options = ParseOptions(arguments.AsSpan(1), format);
        var sourceMaximum = (format is SourceFormat.Glb) ? StaticMeshSourceAdapters.MaximumGlbBytes : StaticMeshSourceAdapters.MaximumObjBytes;
        var source = await ReadBoundedFileAsync(options.SourcePath, sourceMaximum, cancellationToken).ConfigureAwait(false);
        var profile = await ReadBoundedFileAsync(options.ProfilePath, MaximumProfileRecordBytes, cancellationToken).ConfigureAwait(false);
        var material = await ReadBoundedFileAsync(options.MaterialPath, MaximumMaterialRecordBytes, cancellationToken).ConfigureAwait(false);
        var package = format switch {
            SourceFormat.Glb => StaticMeshSourceAdapters.CookGlbPackage(
                source,
                profile,
                material,
                options.MeshAssetId,
                options.MaterialAssetId),
            SourceFormat.Obj => StaticMeshSourceAdapters.CookWavefrontObjPackage(
                source,
                options.ObjConvention,
                profile,
                material,
                options.MeshAssetId,
                options.MaterialAssetId),
            _ => throw new InvalidOperationException("The selected source format is invalid."),
        };
        await ValidatePackageAsync(
            package,
            options.MeshAssetId,
            options.MaterialAssetId,
            cancellationToken).ConfigureAwait(false);
        ContentPackageWriter.WriteAtomically(options.OutputPath, package);
        await Console.Out.WriteLineAsync(
            $"Cooked one static Mesh and its selected Material to '{Path.GetFullPath(options.OutputPath)}'.",
            cancellationToken)
            .ConfigureAwait(false);
        return 0;
    }

    private static StaticMeshCookOptions ParseOptions(
        ReadOnlySpan<string> arguments,
        SourceFormat format)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var index = 0; index < arguments.Length; index += 2)
        {
            var key = arguments[index];

            if (!key.StartsWith("--", StringComparison.Ordinal)
                || (index + 1 >= arguments.Length)
                || !options.TryAdd(key, arguments[index + 1]))
            {
                throw new ArgumentException($"The static Mesh cook option '{key}' is missing a value or is repeated.");
            }
        }

        var expectedCount = (format is SourceFormat.Obj) ? 7 : 6;

        if (options.Count != expectedCount
            || !options.TryGetValue("--source", out var sourcePath)
            || !options.TryGetValue("--profile-record", out var profilePath)
            || !options.TryGetValue("--material-record", out var materialPath)
            || !options.TryGetValue("--mesh-id", out var meshIdText)
            || !options.TryGetValue("--material-id", out var materialIdText)
            || !options.TryGetValue("--output", out var outputPath))
        {
            throw new ArgumentException("The static Mesh cook command has missing or unrecognized options.");
        }

        var objConvention = WavefrontObjSourceConvention.RightHandedYUpForwardZMeters;

        if (format is SourceFormat.Obj)
        {
            if (!options.TryGetValue("--obj-convention", out var convention)
                || !StringComparer.Ordinal.Equals(convention, "rh-y-up-z-forward-meters"))
            {
                throw new ArgumentException("OBJ requires --obj-convention rh-y-up-z-forward-meters.");
            }
        }
        else if (options.ContainsKey("--obj-convention"))
        {
            throw new ArgumentException("--obj-convention is valid only for the OBJ source format.");
        }

        if (!uint.TryParse(meshIdText, NumberStyles.None, CultureInfo.InvariantCulture, out var meshAssetId)
            || (meshAssetId is 0 or > int.MaxValue))
        {
            throw new ArgumentException("The Mesh AssetId must be in [1,2147483647].");
        }

        if (!uint.TryParse(materialIdText, NumberStyles.None, CultureInfo.InvariantCulture, out var materialAssetId)
            || (materialAssetId is 0 or > int.MaxValue))
        {
            throw new ArgumentException("The Material AssetId must be in [1,2147483647].");
        }

        if (meshAssetId == materialAssetId)
        {
            throw new ArgumentException("The Mesh and Material AssetIds must be distinct.");
        }

        var resolvedSourcePath = Path.GetFullPath(sourcePath);
        var resolvedProfilePath = Path.GetFullPath(profilePath);
        var resolvedMaterialPath = Path.GetFullPath(materialPath);
        var resolvedOutputPath = Path.GetFullPath(outputPath);
        var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        if (pathComparer.Equals(resolvedOutputPath, resolvedSourcePath)
            || pathComparer.Equals(resolvedOutputPath, resolvedProfilePath)
            || pathComparer.Equals(resolvedOutputPath, resolvedMaterialPath))
        {
            throw new ArgumentException("The package output must not overwrite one of its declared inputs.");
        }

        return new StaticMeshCookOptions(
            resolvedSourcePath,
            resolvedProfilePath,
            resolvedMaterialPath,
            meshAssetId,
            materialAssetId,
            resolvedOutputPath,
            objConvention);
    }

    private static async Task<byte[]> ReadBoundedFileAsync(
        string path,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 65_536,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using (stream.ConfigureAwait(false))
        {
            var length = stream.Length;

            if ((length <= 0) || (length > maximumBytes) || (length > int.MaxValue))
            {
                throw new InvalidDataException($"The input file '{fullPath}' is empty or exceeds its {maximumBytes}-byte limit.");
            }

            var bytes = new byte[checked((int)length)];
            await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);

            if (stream.Position != stream.Length)
            {
                throw new IOException($"The input file '{fullPath}' changed length while being read.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            return bytes;
        }
    }

    private static async Task ValidatePackageAsync(
        byte[] package,
        uint meshAssetId,
        uint materialAssetId,
        CancellationToken cancellationToken)
    {
        var temporaryPath = Path.Combine(Path.GetTempPath(), string.Concat("terrafx-cook-", Guid.NewGuid().ToString("N"), ".epkg"));

        try
        {
            await File.WriteAllBytesAsync(temporaryPath, package, cancellationToken).ConfigureAwait(false);
            var schemas = new CompiledContentSchemaRegistry([
                .. CoreContentSchemaCatalogue.PrimitiveSchemas,
                .. CoreContentSchemaCatalogue.FoundationSchemas,
                .. NativeAssetSchemaCatalogue.Schemas,
                .. SceneSchemaCatalogue.Schemas,
                .. RenderingSchemaCatalogue.Schemas,
            ]);
            var registrationBuilder = new AssetStoreRegistrationBuilder(schemas);
            _ = Renderer.RegisterAssetCodecs(registrationBuilder);
            var assets = await AssetStore.CreateAsync(
                new AssetStoreOptions(
                    temporaryPath,
                    SHA256.HashData(package),
                    registrationBuilder.Build()),
                cancellationToken).ConfigureAwait(false);
            await using (assets.ConfigureAwait(false))
            {
                using var meshLease = await assets.LoadAsync<RenderMeshAsset>(
                    new TerraFX.Assets.AssetId(checked((int)meshAssetId)),
                    cancellationToken).ConfigureAwait(false);
                using var materialLease = await assets.LoadAsync<RenderMaterialAsset>(
                    new TerraFX.Assets.AssetId(checked((int)materialAssetId)),
                    cancellationToken).ConfigureAwait(false);
                var mesh = meshLease.Value;
                var material = materialLease.Value;

                if ((mesh.MaterialSlots.Length != 1)
                    || (mesh.MaterialSlots[0].Value != checked((int)materialAssetId))
                    || (mesh.Groups.Length != 1)
                    || (mesh.SkeletonAssetId is not null)
                    || material.IsSprite
                    || material.Parameters.SpriteTexture.HasValue
                    || material.Parameters.BaseTexture.HasValue
                    || material.Parameters.PackedMetalRoughOcclusion.HasValue
                    || material.Parameters.NormalTexture.HasValue
                    || material.Parameters.EmissiveTexture.HasValue)
                {
                    throw new InvalidDataException("The cooked Mesh package does not match the bounded single-group, textureless static-Mesh contract.");
                }
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static Task WriteUsageAsync()
        => Console.Out.WriteLineAsync(string.Join(Environment.NewLine, s_usageLines));

    private enum SourceFormat
    {
        Glb = 0,
        Obj = 1,
    }

    private readonly record struct StaticMeshCookOptions(
        string SourcePath,
        string ProfilePath,
        string MaterialPath,
        uint MeshAssetId,
        uint MaterialAssetId,
        string OutputPath,
        WavefrontObjSourceConvention ObjConvention);
}
