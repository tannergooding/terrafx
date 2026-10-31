// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

namespace TerraFX.Assets;

internal static class AssetPayloadReader
{
    private const int BrotliInputBufferSize = 65_536;

    internal static async Task<byte[]> ReadAsync(
        AssetPackageBacking backing,
        AssetPackageEntry entry,
        CancellationToken cancellationToken)
    {
        var decodedLength = checked((int)entry.DecodedLength);
        var decodedBytes = new byte[decodedLength];

        if (entry.Codec == 0)
        {
            await backing.ReadExactlyAtAsync(entry.PayloadOffset, decodedBytes, cancellationToken).ConfigureAwait(false);
            return decodedBytes;
        }

        if (entry.Codec == 1)
        {
            await ReadBrotliAsync(backing, entry, decodedBytes, cancellationToken).ConfigureAwait(false);
            return decodedBytes;
        }

        throw new InvalidDataException("The selected asset uses an unsupported package codec.");
    }

    private static async Task ReadBrotliAsync(
        AssetPackageBacking backing,
        AssetPackageEntry entry,
        byte[] destination,
        CancellationToken cancellationToken)
    {
        var input = ArrayPool<byte>.Shared.Rent(BrotliInputBufferSize);
        var overflow = new byte[1];
        var inputOffset = 0;
        var inputLength = 0;
        var destinationOffset = 0;
        var packageOffset = entry.PayloadOffset;
        var remainingStoredBytes = entry.StoredLength;

        try
        {
            using var decoder = new BrotliDecoder();

            while (true)
            {
                if ((inputOffset == inputLength) && (remainingStoredBytes != 0))
                {
                    var readLength = checked((int)Math.Min((ulong)input.Length, remainingStoredBytes));
                    await backing.ReadExactlyAtAsync(
                        packageOffset,
                        input.AsMemory(0, readLength),
                        cancellationToken).ConfigureAwait(false);
                    packageOffset += checked((ulong)readLength);
                    remainingStoredBytes -= checked((ulong)readLength);
                    inputOffset = 0;
                    inputLength = readLength;
                }

                var inputSpan = input.AsSpan(inputOffset, inputLength - inputOffset);
                var status = destinationOffset < destination.Length
                    ? decoder.Decompress(inputSpan, destination.AsSpan(destinationOffset), out var bytesConsumed, out var bytesWritten)
                    : decoder.Decompress(inputSpan, overflow, out bytesConsumed, out bytesWritten);
                inputOffset += bytesConsumed;
                destinationOffset += bytesWritten;

                if ((destinationOffset > destination.Length)
                    || ((destinationOffset == destination.Length) && (status == OperationStatus.DestinationTooSmall)))
                {
                    throw new InvalidDataException("The Brotli asset exceeds its declared decoded length.");
                }

                if (status == OperationStatus.Done)
                {
                    if ((destinationOffset != destination.Length)
                        || (inputOffset != inputLength)
                        || (remainingStoredBytes != 0))
                    {
                        throw new InvalidDataException("The Brotli asset length or trailing input is invalid.");
                    }

                    return;
                }

                if (status == OperationStatus.InvalidData)
                {
                    throw new InvalidDataException("The Brotli asset stream is invalid.");
                }

                if (status == OperationStatus.NeedMoreData)
                {
                    if (inputOffset != inputLength)
                    {
                        var remainingInput = inputLength - inputOffset;

                        if (remainingInput == input.Length)
                        {
                            throw new InvalidDataException("The Brotli asset stream made no progress.");
                        }

                        input.AsSpan(inputOffset, remainingInput).CopyTo(input);
                        inputOffset = 0;
                        inputLength = remainingInput;
                    }
                    else
                    {
                        inputOffset = 0;
                        inputLength = 0;
                    }

                    if ((remainingStoredBytes == 0) && (inputLength == 0))
                    {
                        throw new InvalidDataException("The Brotli asset stream is truncated.");
                    }

                    continue;
                }

                if ((status != OperationStatus.DestinationTooSmall)
                    || ((bytesConsumed == 0) && (bytesWritten == 0) && (destinationOffset < destination.Length)))
                {
                    throw new InvalidDataException("The Brotli asset stream made no progress.");
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(input, clearArray: true);
        }
    }
}
