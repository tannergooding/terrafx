// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace TerraFX.Content.Tools;

internal static partial class ContentPackageWriter
{
    private static readonly Guid s_atlasSchemaId = new ContentSchemaIdentityAssignment("native/atlas").SchemaId;
    private static readonly Guid s_materialSchemaId = new ContentSchemaIdentityAssignment("native/material").SchemaId;
    private static readonly Guid s_meshSchemaId = new ContentSchemaIdentityAssignment("native/mesh").SchemaId;
    private static readonly Guid s_particleEmitterSchemaId = new ContentSchemaIdentityAssignment("native/cosmetic-particle-emitter").SchemaId;
    private static readonly Guid s_skeletonSchemaId = new ContentSchemaIdentityAssignment("native/skeleton").SchemaId;
    private static readonly Guid s_textureSchemaId = new ContentSchemaIdentityAssignment("native/texture").SchemaId;

    internal static byte[] CreateNativeAssetPackage(
        scoped ReadOnlySpan<byte> profileRecord,
        scoped ReadOnlySpan<ContentPackageAsset> assets,
        CompiledContentSchemaRegistry schemas)
    {
        ArgumentNullException.ThrowIfNull(schemas);
        return CreatePackageCore(profileRecord, assets, schemas);
    }

    internal static (int PackageBytes, long WorkspaceBytes) MeasureNativeAssetPackage(
        long profileLength,
        scoped ReadOnlySpan<long> recordLengths)
    {
        if (profileLength is <= 0 or > 65_536)
        {
            throw new InvalidDataException("The native cook profile exceeds its complete-record limit.");
        }

        ArgumentOutOfRangeException.ThrowIfZero(recordLengths.Length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(recordLengths.Length, 131_072);
        var directoryOffset = Align16(checked((ulong)(PackageHeaderBytes + profileLength)));
        var directoryLength = checked((ulong)DirectoryHeaderBytes + (checked((ulong)recordLengths.Length) * DirectoryEntryBytes));
        var packageBytes = Align16(checked(directoryOffset + directoryLength));
        var inputBytes = profileLength;
        var maximumRecordBytes = 0L;

        foreach (var length in recordLengths)
        {
            if ((length < 64) || (length > s_assetRecordLimits.MaximumRecordBytes - 144))
            {
                throw new InvalidDataException("A native cook input and its complete envelope must fit the asset record byte limit.");
            }

            inputBytes = checked(inputBytes + length);
            maximumRecordBytes = Math.Max(maximumRecordBytes, length);
            packageBytes = Align16(checked(packageBytes + Align16(checked((ulong)length + 144))));
        }

        if (packageBytes > MaximumPackageBytes)
        {
            throw new InvalidDataException("The complete native candidate exceeds the package byte limit.");
        }

        // Inputs coexist with the candidate and one envelope's contiguous payload scratch.
        // The metadata arrays and bounded file buffer are separate from AssetStore's decode/lease budgets.
        var metadataBytes = checked((long)recordLengths.Length
            * (sizeof(long) + (2 * Unsafe.SizeOf<ContentPackageAsset>())));
        var workspaceBytes = checked(inputBytes + (long)packageBytes + maximumRecordBytes + sizeof(uint)
            + metadataBytes + (2 * Unsafe.SizeOf<EbinFieldSource>()) + 65_536);

        if (workspaceBytes > MaximumPackageBytes)
        {
            throw new InvalidDataException("The native cook's simultaneous input, candidate, and encoding workspace exceeds its byte limit.");
        }

        return (checked((int)packageBytes), workspaceBytes);
    }

    internal static async Task WriteValidatedAtomicallyAsync(
        string path,
        ReadOnlyMemory<byte> package,
        Func<string, CancellationToken, Task> validateAsync,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfZero(package.Length);
        ArgumentNullException.ThrowIfNull(validateAsync);
        cancellationToken.ThrowIfCancellationRequested();
        var targetPath = Path.GetFullPath(path);
        var directoryPath = Path.GetDirectoryName(targetPath)
            ?? throw new ArgumentException("The package output requires a parent directory.", nameof(path));
        _ = Directory.CreateDirectory(directoryPath);
        var stagingPath = Path.Combine(directoryPath, string.Concat(".", Path.GetFileName(targetPath), ".", Guid.NewGuid().ToString("N"), ".staging"));

        try
        {
            var stream = new FileStream(
                stagingPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                65_536,
                FileOptions.Asynchronous | FileOptions.WriteThrough);
            await using (stream.ConfigureAwait(false))
            {
                await stream.WriteAsync(package, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                RandomAccess.FlushToDisk(stream.SafeFileHandle);
            }

            await validateAsync(stagingPath, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(stagingPath, targetPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(stagingPath))
            {
                File.Delete(stagingPath);
            }
        }
    }

    private static Guid ValidateNativeAssetRecord(ReadOnlySpan<byte> record, CompiledContentSchemaRegistry schemas)
    {
        var status = EbinStructuralReader.Validate(
            record,
            s_assetRecordLimits,
            EbinReadPolicy.Canonical,
            out var consumed,
            out var diagnostic);

        if ((status is not EbinReadStatus.Complete) || (consumed != record.Length))
        {
            throw new InvalidDataException($"A native cook input is not a complete canonical record: {diagnostic?.Category ?? status.ToString()}.");
        }

        var schemaId = new Guid(record.Slice(16, 16), bigEndian: true);
        var major = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(record[48..]));
        var minor = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(record[52..]));
        var admitted = (minor == 0)
            && (((schemaId == s_atlasSchemaId) && (major == 1))
                || ((schemaId == s_meshSchemaId) && (major == 2))
                || ((schemaId == s_particleEmitterSchemaId) && (major == 1))
                || ((schemaId == s_materialSchemaId) && (major is 1 or 2))
                || (((schemaId == s_skeletonSchemaId) || (schemaId == s_textureSchemaId)) && (major == 1)));

        if (!admitted
            || !schemas.TryGetSchema(new ContentSchemaReference(schemaId, major, minor, ContentSchemaScope.Content), out var schema)
            || (schema.Role != ContentSchemaRole.NativeAsset))
        {
            throw new InvalidDataException("The native Mesh producer does not select this exact compiled NativeAsset tuple.");
        }

        return schemaId;
    }
}
