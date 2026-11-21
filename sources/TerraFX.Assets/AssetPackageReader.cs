// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace TerraFX.Assets;

internal static class AssetPackageReader
{
    private const int BoundedStaticMeshEntryCount = 2;
    private const int DirectoryEntrySize = 32;
    private const int DirectoryHeaderSize = 64;
    private const ulong MaximumDirectoryBytes = 4_194_368;
    private const uint MaximumEntryCount = 131_072;
    private const long MaximumPackageBytes = 8L * 1024 * 1024 * 1024;
    private const int PackageHeaderSize = 64;

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "The installed original opening retains both operation and lower cleanup failures without replacing the primary fault.")]
    internal static async Task<AssetPackageBacking> OpenAsync(
        AssetStore.OriginalOperation? opening,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(opening);
        var storage = opening.Storage;
        var options = storage.Options;
        var backing = opening.Backing;
        FileStream? stream = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            opening.BeginAcquisition(cancellationToken);
            stream = new FileStream(
                options.AbsolutePackagePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1,
                FileOptions.Asynchronous | FileOptions.RandomAccess);
            storage.Stream = stream;
            backing.InstallOriginalStream(stream);
            opening.ObserveStreamAcquired();
            var packageByteLength = stream.Length;

            if (packageByteLength is < PackageHeaderSize or > MaximumPackageBytes)
            {
                throw new InvalidDataException("The EPKG2 package length is outside its admitted range.");
            }

            storage.ActualDigest = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);

            if (CryptographicOperations.FixedTimeEquals(storage.ActualDigest, options.ExpectedPackageSha256.AsSpan()) is false)
            {
                throw new InvalidDataException("The EPKG2 whole-package SHA-256 does not match the expected digest.");
            }

            storage.Header = new byte[PackageHeaderSize];
            await ReadExactlyAtAsync(stream, 0, storage.Header, cancellationToken).ConfigureAwait(false);

            if (!storage.Header.AsSpan(0, 4).SequenceEqual("EPKG"u8)
                || (BinaryPrimitives.ReadUInt16LittleEndian(storage.Header.AsSpan(4)) != 2)
                || (BinaryPrimitives.ReadUInt16LittleEndian(storage.Header.AsSpan(6)) != 0)
                || (BinaryPrimitives.ReadUInt64LittleEndian(storage.Header.AsSpan(8)) != checked((ulong)packageByteLength))
                || (BinaryPrimitives.ReadUInt32LittleEndian(storage.Header.AsSpan(52)) != 0)
                || !IsZero(storage.Header.AsSpan(56, 8)))
            {
                throw new InvalidDataException("The EPKG2 package header is invalid.");
            }

            var profileOffset = BinaryPrimitives.ReadUInt64LittleEndian(storage.Header.AsSpan(16));
            var profileLength = BinaryPrimitives.ReadUInt64LittleEndian(storage.Header.AsSpan(24));
            var directoryOffset = BinaryPrimitives.ReadUInt64LittleEndian(storage.Header.AsSpan(32));
            var directoryLength = BinaryPrimitives.ReadUInt64LittleEndian(storage.Header.AsSpan(40));
            var entryCount = BinaryPrimitives.ReadUInt32LittleEndian(storage.Header.AsSpan(48));

            if ((profileOffset != PackageHeaderSize)
                || (profileLength < PackageHeaderSize)
                || (profileLength > checked((ulong)EbinReadLimits.Foundation.MaximumRecordBytes))
                || (entryCount > MaximumEntryCount))
            {
                throw new InvalidDataException("The EPKG2 profile extent or entry count is invalid.");
            }

            var profileEnd = checked(profileOffset + profileLength);
            var expectedDirectoryOffset = Align16(profileEnd);
            var expectedDirectoryLength = checked((ulong)DirectoryHeaderSize + (checked((ulong)entryCount) * DirectoryEntrySize));
            var packageByteLengthValue = checked((ulong)packageByteLength);

            if ((directoryOffset != expectedDirectoryOffset)
                || (directoryLength != expectedDirectoryLength)
                || (directoryLength > MaximumDirectoryBytes)
                || (directoryOffset > packageByteLengthValue)
                || (directoryLength > (packageByteLengthValue - directoryOffset)))
            {
                throw new InvalidDataException("The EPKG2 directory extent is invalid.");
            }

            var directoryEnd = directoryOffset + directoryLength;
            storage.Profile = new byte[checked((int)profileLength)];
            await ReadExactlyAtAsync(stream, profileOffset, storage.Profile, cancellationToken).ConfigureAwait(false);
            var profileStatus = FoundationCodec.ReadPackageProfile(
                storage.Profile,
                out var profileId,
                out var spatialSettings,
                out var profileConsumed,
                out var profileDiagnostic);

            if ((profileStatus != EbinStatus.Complete) || (profileConsumed != storage.Profile.Length))
            {
                throw new InvalidDataException(
                    $"The EPKG2 foundation profile is not admitted: {profileStatus} {profileDiagnostic.Category}.");
            }

            storage.Directory = new byte[checked((int)directoryLength)];
            await ReadExactlyAtAsync(stream, directoryOffset, storage.Directory, cancellationToken).ConfigureAwait(false);
            ValidateDirectoryHeader(storage.Directory, entryCount, directoryLength);

            storage.EntryBuilder = ImmutableArray.CreateBuilder<AssetPackageEntry>(checked((int)entryCount));
            var firstPayloadOffset = Align16(directoryEnd);

            if (firstPayloadOffset > packageByteLengthValue)
            {
                throw new InvalidDataException("The EPKG2 package has no room for its aligned payload region.");
            }

            await EnsureZeroPaddingAsync(
                stream,
                directoryEnd,
                firstPayloadOffset - directoryEnd,
                storage.Padding,
                cancellationToken).ConfigureAwait(false);

            var previousAssetId = 0U;
            var previousPayloadEnd = firstPayloadOffset;

            for (var index = 0; index < entryCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entryOffset = checked(DirectoryHeaderSize + (checked((int)index) * DirectoryEntrySize));
                var encodedEntry = storage.Directory.AsSpan(entryOffset, DirectoryEntrySize);
                var assetIdValue = BinaryPrimitives.ReadUInt32LittleEndian(encodedEntry);
                var codec = encodedEntry[4];
                var payloadOffset = BinaryPrimitives.ReadUInt64LittleEndian(encodedEntry[8..]);
                var storedLength = BinaryPrimitives.ReadUInt64LittleEndian(encodedEntry[16..]);
                var decodedLength = BinaryPrimitives.ReadUInt64LittleEndian(encodedEntry[24..]);

                if ((assetIdValue is 0 or > int.MaxValue)
                    || (assetIdValue <= previousAssetId)
                    || (codec > 1)
                    || (encodedEntry[5] != 0)
                    || !IsZero(encodedEntry[6..8])
                    || (storedLength == 0)
                    || (decodedLength == 0)
                    || (decodedLength > checked((ulong)options.MaximumDecodedAssetBytes))
                    || ((codec == 1)
                        && (storedLength > checked((ulong)BrotliEncoder.GetMaxCompressedLength(checked((int)decodedLength)))))
                    || ((codec == 0) && (storedLength != decodedLength)))
                {
                    throw new InvalidDataException($"The EPKG2 entry {index} has an invalid ID, codec, flag, or length.");
                }

                var expectedPayloadOffset = Align16(previousPayloadEnd);

                if ((payloadOffset != expectedPayloadOffset) || ((payloadOffset & 15) != 0))
                {
                    throw new InvalidDataException($"The EPKG2 entry {index} has an invalid payload offset.");
                }

                if ((payloadOffset > packageByteLengthValue)
                    || (storedLength > (packageByteLengthValue - payloadOffset)))
                {
                    throw new InvalidDataException($"The EPKG2 entry {index} exceeds the package bounds.");
                }

                await EnsureZeroPaddingAsync(
                    stream,
                    previousPayloadEnd,
                    payloadOffset - previousPayloadEnd,
                    storage.Padding,
                    cancellationToken).ConfigureAwait(false);

                var payloadEnd = payloadOffset + storedLength;

                storage.EntryBuilder.Add(new AssetPackageEntry(
                    new AssetId(checked((int)assetIdValue)),
                    codec,
                    payloadOffset,
                    storedLength,
                    decodedLength));
                previousAssetId = assetIdValue;
                previousPayloadEnd = payloadEnd;
            }

            var expectedPackageLength = Align16(previousPayloadEnd);

            if (expectedPackageLength != packageByteLengthValue)
            {
                throw new InvalidDataException("The EPKG2 package has trailing or missing bytes.");
            }

            await EnsureZeroPaddingAsync(
                stream,
                previousPayloadEnd,
                expectedPackageLength - previousPayloadEnd,
                storage.Padding,
                cancellationToken).ConfigureAwait(false);

            backing.InstallOriginalMetadata(
                storage.AdoptEntries(),
                profileId,
                spatialSettings,
                packageByteLength);
            return backing;
        }
        catch (Exception failure)
        {
            opening.RecordFailure(failure, cleanup: false);
            var releaseReturned = !opening.FileAcquisitionAttempted;
            Exception? releaseFailure = null;

            if (stream is not null)
            {
                try
                {
                    await stream.DisposeAsync().ConfigureAwait(false);
                    releaseReturned = true;
                    opening.ObserveFileReleaseReturn();
                }
                catch (Exception cleanupFailure)
                {
                    opening.RecordFailure(cleanupFailure, cleanup: true);
                    releaseFailure = cleanupFailure;
                }
            }

            backing.RecordOriginalOpeningFailure(releaseFailure ?? failure, releaseReturned);
            ExceptionDispatchInfo.Capture(failure).Throw();
            throw;
        }
        finally
        {
            ClearOpeningBorrows(ref opening, ref storage, ref options, ref stream);
        }
    }

    internal static AssetBoundedFailureFact OpenBoundedMetadata(
        AssetPackageBacking backing,
        out AssetPackageEntry firstEntry,
        out AssetPackageEntry secondEntry,
        out SpatialProfileId profileId,
        out SpatialSettings spatialSettings,
        out AssetBoundedFailureFact hashProviderRelease,
        out AssetPackageBackingToken? packageBackingToken)
    {
        ArgumentNullException.ThrowIfNull(backing);
        firstEntry = default;
        secondEntry = default;
        profileId = default;
        spatialSettings = default;
        hashProviderRelease = default;
        packageBackingToken = null;

        var file = backing.BoundedNativeFile;

        if (!backing.IsBoundedOpening || !file.IsOpen)
        {
            return CreateBoundedFailure(
                AssetBoundedFailureStage.Header,
                AssetBoundedFailureKind.Invariant,
                file.Disposition);
        }

        Span<byte> actualDigest = stackalloc byte[SHA256.HashSizeInBytes];
        var failure = backing.HashBoundedPackage(actualDigest, out hashProviderRelease);

        if (failure.Kind is not AssetBoundedFailureKind.None)
        {
            return failure;
        }

        if (hashProviderRelease.Kind is not AssetBoundedFailureKind.None)
        {
            return default;
        }

        if (!CryptographicOperations.FixedTimeEquals(actualDigest, backing.BoundedProvision.ExpectedPackageSha256.Span))
        {
            return CreateBoundedFailure(
                AssetBoundedFailureStage.Hash,
                AssetBoundedFailureKind.Digest,
                file.Disposition);
        }

        Span<byte> header = stackalloc byte[PackageHeaderSize];
        failure = backing.ReadBoundedExactlyAt(0, header, AssetBoundedFailureStage.Header);

        if (failure.Kind is not AssetBoundedFailureKind.None)
        {
            return failure;
        }

        failure = GetBoundedPackageSections(
            file.Length,
            header,
            file.Disposition,
            out var profileLength,
            out var directoryOffset,
            out var directoryLength);

        if (failure.Kind is not AssetBoundedFailureKind.None)
        {
            return failure;
        }

        var profileBuffer = backing.BoundedProvision.PackageProfileBuffer;
        var profileBytes = profileBuffer[..checked((int)profileLength)];
        failure = backing.ReadBoundedExactlyAt(
            PackageHeaderSize,
            profileBytes,
            AssetBoundedFailureStage.Profile);

        if (failure.Kind is not AssetBoundedFailureKind.None)
        {
            return failure;
        }

        var directoryBuffer = backing.BoundedProvision.PackageDirectoryBuffer;
        failure = backing.ReadBoundedExactlyAt(
            directoryOffset,
            directoryBuffer,
            AssetBoundedFailureStage.Directory);

        if (failure.Kind is not AssetBoundedFailureKind.None)
        {
            return failure;
        }

        failure = ParseBoundedMetadata(
            file.Length,
            backing.MaximumDecodedAssetBytes,
            header,
            profileBytes,
            directoryBuffer,
            file.Disposition,
            out firstEntry,
            out secondEntry,
            out profileId,
            out spatialSettings);

        if (failure.Kind is not AssetBoundedFailureKind.None)
        {
            return failure;
        }

        var directoryEnd = checked(directoryOffset + directoryLength);
        var firstPayloadEnd = checked(firstEntry.PayloadOffset + firstEntry.StoredLength);
        var paddingFailure = ReadBoundedZeroPadding(
            backing,
            directoryEnd,
            firstEntry.PayloadOffset - directoryEnd);

        if (paddingFailure.Kind is not AssetBoundedFailureKind.None)
        {
            return paddingFailure;
        }

        var secondPayloadEnd = checked(secondEntry.PayloadOffset + secondEntry.StoredLength);
        paddingFailure = ReadBoundedZeroPadding(
            backing,
            firstPayloadEnd,
            secondEntry.PayloadOffset - firstPayloadEnd);

        if (paddingFailure.Kind is not AssetBoundedFailureKind.None)
        {
            return paddingFailure;
        }

        paddingFailure = ReadBoundedZeroPadding(
            backing,
            secondPayloadEnd,
            checked(Align16(secondPayloadEnd) - secondPayloadEnd));

        if (paddingFailure.Kind is AssetBoundedFailureKind.None)
        {
            packageBackingToken = backing.BackingToken;
        }

        return paddingFailure;
    }

    internal static AssetBoundedFailureFact ParseBoundedMetadata(
        long packageByteLength,
        int maximumDecodedAssetBytes,
        scoped ReadOnlySpan<byte> header,
        scoped ReadOnlySpan<byte> profileBytes,
        byte[] directory,
        AssetBoundedFileDisposition fileDisposition,
        out AssetPackageEntry firstEntry,
        out AssetPackageEntry secondEntry,
        out SpatialProfileId profileId,
        out SpatialSettings spatialSettings)
    {
        firstEntry = default;
        secondEntry = default;
        profileId = default;
        spatialSettings = default;
        var failure = GetBoundedPackageSections(
            packageByteLength,
            header,
            fileDisposition,
            out var profileLength,
            out var directoryOffset,
            out var directoryLength);

        if (failure.Kind is not AssetBoundedFailureKind.None)
        {
            return failure;
        }

        if ((profileBytes.Length != checked((int)profileLength))
            || (directory.Length != checked((int)directoryLength)))
        {
            return CreateBoundedFailure(
                AssetBoundedFailureStage.Directory,
                AssetBoundedFailureKind.Layout,
                fileDisposition);
        }

        var profileStatus = FoundationCodec.ReadPackageProfile(
            profileBytes,
            out profileId,
            out spatialSettings,
            out var profileConsumed,
            out _);

        if ((profileStatus != EbinStatus.Complete) || (profileConsumed != profileBytes.Length))
        {
            return CreateBoundedFailure(
                AssetBoundedFailureStage.Profile,
                AssetBoundedFailureKind.Profile,
                fileDisposition);
        }

        try
        {
            ValidateDirectoryHeader(
                directory,
                BoundedStaticMeshEntryCount,
                directoryLength);
        }
        catch (InvalidDataException)
        {
            return CreateBoundedFailure(
                AssetBoundedFailureStage.Directory,
                AssetBoundedFailureKind.Layout,
                fileDisposition);
        }

        var packageByteLengthValue = checked((ulong)packageByteLength);
        var directoryEnd = checked(directoryOffset + directoryLength);
        var firstPayloadOffset = Align16(directoryEnd);

        if (firstPayloadOffset > packageByteLengthValue)
        {
            return CreateBoundedFailure(
                AssetBoundedFailureStage.Directory,
                AssetBoundedFailureKind.Layout,
                fileDisposition);
        }

        var previousAssetId = 0U;
        var previousPayloadEnd = firstPayloadOffset;

        for (var index = 0; index < BoundedStaticMeshEntryCount; index++)
        {
            var entryOffset = checked(DirectoryHeaderSize + (index * DirectoryEntrySize));
            var encodedEntry = directory.AsSpan(entryOffset, DirectoryEntrySize);
            var assetIdValue = BinaryPrimitives.ReadUInt32LittleEndian(encodedEntry);
            var codec = encodedEntry[4];
            var payloadOffset = BinaryPrimitives.ReadUInt64LittleEndian(encodedEntry[8..]);
            var storedLength = BinaryPrimitives.ReadUInt64LittleEndian(encodedEntry[16..]);
            var decodedLength = BinaryPrimitives.ReadUInt64LittleEndian(encodedEntry[24..]);

            if ((assetIdValue is 0 or > int.MaxValue)
                || (assetIdValue <= previousAssetId)
                || (encodedEntry[5] != 0)
                || !IsZero(encodedEntry[6..8])
                || (storedLength == 0)
                || (decodedLength == 0)
                || (decodedLength > checked((ulong)maximumDecodedAssetBytes)))
            {
                return CreateBoundedFailure(
                    AssetBoundedFailureStage.Directory,
                    AssetBoundedFailureKind.Layout,
                    fileDisposition);
            }

            if (codec != 0)
            {
                return CreateBoundedFailure(
                    AssetBoundedFailureStage.Directory,
                    AssetBoundedFailureKind.UnsupportedContent,
                    fileDisposition);
            }

            var expectedPayloadOffset = Align16(previousPayloadEnd);

            if ((storedLength != decodedLength)
                || (payloadOffset != expectedPayloadOffset)
                || ((payloadOffset & 15) != 0)
                || (payloadOffset > packageByteLengthValue)
                || (storedLength > (packageByteLengthValue - payloadOffset)))
            {
                return CreateBoundedFailure(
                    AssetBoundedFailureStage.Directory,
                    AssetBoundedFailureKind.Layout,
                    fileDisposition);
            }

            var entry = new AssetPackageEntry(
                new AssetId(checked((int)assetIdValue)),
                codec,
                payloadOffset,
                storedLength,
                decodedLength);

            if (index == 0)
            {
                firstEntry = entry;
            }
            else
            {
                secondEntry = entry;
            }

            previousAssetId = assetIdValue;
            previousPayloadEnd = checked(payloadOffset + storedLength);
        }

        if (Align16(previousPayloadEnd) != packageByteLengthValue)
        {
            return CreateBoundedFailure(
                AssetBoundedFailureStage.Directory,
                AssetBoundedFailureKind.Layout,
                fileDisposition);
        }

        return default;
    }

    internal static AssetBoundedFailureFact ReadBoundedPayload(
        AssetPackageBacking backing,
        AssetPackageBackingToken? packageBackingToken,
        AssetPackageEntry entry,
        scoped Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(backing);

        if (!ReferenceEquals(packageBackingToken, backing.BackingToken))
        {
            return CreateBoundedFailure(
                AssetBoundedFailureStage.PayloadRead,
                AssetBoundedFailureKind.Invariant,
                AssetBoundedFileDisposition.NotAttempted);
        }

        if (!backing.IsBoundedOpening)
        {
            return CreateBoundedFailure(
                AssetBoundedFailureStage.PayloadRead,
                AssetBoundedFailureKind.Invariant,
                AssetBoundedFileDisposition.NotAttempted);
        }

        var file = backing.BoundedNativeFile;

        if (!file.IsOpen)
        {
            return CreateBoundedFailure(
                AssetBoundedFailureStage.PayloadRead,
                AssetBoundedFailureKind.Invariant,
                file.Disposition);
        }

        var failure = ValidateBoundedPayloadRange(
            entry,
            file.Length,
            backing.MaximumDecodedAssetBytes,
            destination.Length,
            file.Disposition);

        if (failure.Kind is not AssetBoundedFailureKind.None)
        {
            return failure;
        }

        var payloadLength = checked((int)entry.DecodedLength);
        return backing.ReadBoundedExactlyAt(
            entry.PayloadOffset,
            destination[..payloadLength],
            AssetBoundedFailureStage.PayloadRead);
    }

    internal static async ValueTask ReadExactlyAtAsync(
        FileStream stream,
        ulong offset,
        Memory<byte> destination,
        CancellationToken cancellationToken)
    {
        try
        {
            var position = checked((long)offset);
            var read = 0;

            while (read < destination.Length)
            {
                var count = await RandomAccess.ReadAsync(
                    stream.SafeFileHandle,
                    destination[read..],
                    checked(position + read),
                    cancellationToken).ConfigureAwait(false);

                if (count == 0)
                {
                    throw new EndOfStreamException("The EPKG2 package ended before the declared extent.");
                }

                read += count;
            }
        }
        finally
        {
            ClearReadDestination(ref destination);
        }
    }

    internal static AssetBoundedFailureFact ValidateBoundedPayloadRange(
        AssetPackageEntry entry,
        long packageByteLength,
        int maximumDecodedAssetBytes,
        int destinationLength,
        AssetBoundedFileDisposition fileDisposition)
    {
        if ((packageByteLength < PackageHeaderSize)
            || (maximumDecodedAssetBytes < 0)
            || !entry.AssetId.IsValid
            || (entry.Codec > 1)
            || (entry.StoredLength == 0)
            || (entry.DecodedLength == 0)
            || (entry.DecodedLength > checked((ulong)maximumDecodedAssetBytes)))
        {
            return CreateBoundedFailure(
                AssetBoundedFailureStage.PayloadRead,
                AssetBoundedFailureKind.Layout,
                fileDisposition);
        }

        if (entry.Codec != 0)
        {
            return CreateBoundedFailure(
                AssetBoundedFailureStage.PayloadRead,
                AssetBoundedFailureKind.UnsupportedContent,
                fileDisposition);
        }

        var packageByteLengthValue = checked((ulong)packageByteLength);

        if ((entry.StoredLength != entry.DecodedLength)
            || ((entry.PayloadOffset & 15) != 0)
            || (entry.PayloadOffset > packageByteLengthValue)
            || (entry.StoredLength > (packageByteLengthValue - entry.PayloadOffset)))
        {
            return CreateBoundedFailure(
                AssetBoundedFailureStage.PayloadRead,
                AssetBoundedFailureKind.Layout,
                fileDisposition);
        }

        if (entry.DecodedLength > checked((ulong)destinationLength))
        {
            return CreateBoundedFailure(
                AssetBoundedFailureStage.PayloadRead,
                AssetBoundedFailureKind.Invariant,
                fileDisposition);
        }

        return default;
    }

    private static ulong Align16(ulong value) => checked((value + 15) & ~15UL);

    private static void ClearOpeningBorrows(
        ref AssetStore.OriginalOperation? opening,
        ref AssetStore.OriginalOpeningStorage? storage,
        ref AssetStoreOptions? options,
        ref FileStream? stream)
    {
        opening = null;
        storage = null;
        options = null;
        stream = null;
    }

    private static void ClearReadDestination(ref Memory<byte> destination)
    {
        destination = default;
    }

    private static AssetBoundedFailureFact CreateBoundedFailure(
        AssetBoundedFailureStage stage,
        AssetBoundedFailureKind kind,
        AssetBoundedFileDisposition fileDisposition)
        => new(
            stage,
            kind,
            AssetBoundedNativeApi.None,
            0,
            Guid.Empty,
            0,
            fileDisposition);

    private static async ValueTask EnsureZeroPaddingAsync(
        FileStream stream,
        ulong offset,
        ulong length,
        Memory<byte> padding,
        CancellationToken cancellationToken)
    {
        try
        {
            if (length == 0)
            {
                return;
            }

            if (length > 15)
            {
                throw new InvalidDataException("The EPKG2 layout contains an unaligned gap.");
            }

            padding = padding[..checked((int)length)];
            await ReadExactlyAtAsync(stream, offset, padding, cancellationToken).ConfigureAwait(false);

            if (!IsZero(padding.Span))
            {
                throw new InvalidDataException("The EPKG2 layout contains nonzero alignment padding.");
            }
        }
        finally
        {
            ClearReadDestination(ref padding);
        }
    }

    private static AssetBoundedFailureFact GetBoundedPackageSections(
        long packageByteLength,
        scoped ReadOnlySpan<byte> header,
        AssetBoundedFileDisposition fileDisposition,
        out ulong profileLength,
        out ulong directoryOffset,
        out ulong directoryLength)
    {
        profileLength = 0;
        directoryOffset = 0;
        directoryLength = 0;

        if ((packageByteLength is < PackageHeaderSize or > MaximumPackageBytes)
            || (header.Length != PackageHeaderSize)
            || !header[..4].SequenceEqual("EPKG"u8)
            || (BinaryPrimitives.ReadUInt16LittleEndian(header[4..]) != 2)
            || (BinaryPrimitives.ReadUInt16LittleEndian(header[6..]) != 0)
            || (BinaryPrimitives.ReadUInt64LittleEndian(header[8..]) != checked((ulong)packageByteLength))
            || (BinaryPrimitives.ReadUInt32LittleEndian(header[52..]) != 0)
            || !IsZero(header[56..64]))
        {
            return CreateBoundedFailure(
                AssetBoundedFailureStage.Header,
                AssetBoundedFailureKind.Layout,
                fileDisposition);
        }

        var profileOffset = BinaryPrimitives.ReadUInt64LittleEndian(header[16..]);
        profileLength = BinaryPrimitives.ReadUInt64LittleEndian(header[24..]);
        directoryOffset = BinaryPrimitives.ReadUInt64LittleEndian(header[32..]);
        directoryLength = BinaryPrimitives.ReadUInt64LittleEndian(header[40..]);
        var entryCount = BinaryPrimitives.ReadUInt32LittleEndian(header[48..]);

        if ((profileOffset != PackageHeaderSize)
            || (profileLength < PackageHeaderSize)
            || (profileLength > checked((ulong)EbinReadLimits.Foundation.MaximumRecordBytes)))
        {
            return CreateBoundedFailure(
                AssetBoundedFailureStage.Profile,
                AssetBoundedFailureKind.Layout,
                fileDisposition);
        }

        if (entryCount != BoundedStaticMeshEntryCount)
        {
            return CreateBoundedFailure(
                AssetBoundedFailureStage.Directory,
                AssetBoundedFailureKind.UnsupportedContent,
                fileDisposition);
        }

        var profileEnd = checked((ulong)PackageHeaderSize + profileLength);
        var expectedDirectoryOffset = Align16(profileEnd);
        var expectedDirectoryLength = checked(
            (ulong)DirectoryHeaderSize + (checked((ulong)entryCount) * DirectoryEntrySize));
        var packageByteLengthValue = checked((ulong)packageByteLength);

        if ((directoryOffset != expectedDirectoryOffset)
            || (directoryLength != expectedDirectoryLength)
            || (directoryOffset > packageByteLengthValue)
            || (directoryLength > (packageByteLengthValue - directoryOffset)))
        {
            return CreateBoundedFailure(
                AssetBoundedFailureStage.Directory,
                AssetBoundedFailureKind.Layout,
                fileDisposition);
        }

        return default;
    }

    private static bool IsZero(scoped ReadOnlySpan<byte> bytes) => bytes.IndexOfAnyExcept((byte)0) < 0;

    private static AssetBoundedFailureFact ReadBoundedZeroPadding(
        AssetPackageBacking backing,
        ulong offset,
        ulong length)
    {
        if (length == 0)
        {
            return default;
        }

        if (length > 15)
        {
            return CreateBoundedFailure(
                AssetBoundedFailureStage.Directory,
                AssetBoundedFailureKind.Layout,
                backing.BoundedNativeFile.Disposition);
        }

        Span<byte> padding = stackalloc byte[15];
        var paddingBytes = padding[..checked((int)length)];
        var failure = backing.ReadBoundedExactlyAt(
            offset,
            paddingBytes,
            AssetBoundedFailureStage.Directory);

        if (failure.Kind is not AssetBoundedFailureKind.None)
        {
            return failure;
        }

        return IsZero(paddingBytes)
            ? default
            : CreateBoundedFailure(
                AssetBoundedFailureStage.Directory,
                AssetBoundedFailureKind.Layout,
                backing.BoundedNativeFile.Disposition);
    }

    private static void ValidateDirectoryHeader(byte[] directory, uint entryCount, ulong directoryLength)
    {
        var header = directory.AsSpan(0, DirectoryHeaderSize);
        var expectedTableLength = checked((ulong)entryCount * DirectoryEntrySize);

        if (!header[..4].SequenceEqual("EIDX"u8)
            || (BinaryPrimitives.ReadUInt16LittleEndian(header[4..]) != 1)
            || (BinaryPrimitives.ReadUInt16LittleEndian(header[6..]) != 16)
            || (BinaryPrimitives.ReadUInt32LittleEndian(header[8..]) != entryCount)
            || (BinaryPrimitives.ReadUInt32LittleEndian(header[12..]) != 0)
            || (BinaryPrimitives.ReadUInt64LittleEndian(header[16..]) != DirectoryHeaderSize)
            || (BinaryPrimitives.ReadUInt64LittleEndian(header[24..]) != expectedTableLength)
            || (BinaryPrimitives.ReadUInt64LittleEndian(header[32..]) != directoryLength)
            || !IsZero(header[40..]))
        {
            throw new InvalidDataException("The EIDX1 directory header is invalid.");
        }
    }
}
