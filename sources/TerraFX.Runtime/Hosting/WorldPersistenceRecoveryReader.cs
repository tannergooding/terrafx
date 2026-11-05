// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Hosting;

internal static class WorldPersistenceRecoveryReader
{
    private const int FileStreamBufferBytes = 65_536;
    private const long RecoveryControlBytes = 4096;

    internal static long MeasureCandidateReadWorkspaceBytes(
        string targetPath,
        string backupPath,
        long maximumSnapshotBytes)
    {
        ArgumentNullException.ThrowIfNull(targetPath);
        ArgumentNullException.ThrowIfNull(backupPath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumSnapshotBytes);

        return checked((2 * maximumSnapshotBytes) + FileStreamBufferBytes + RecoveryControlBytes);
    }

    internal static async Task<(string Path, long Generation, byte[] FileRecord, IDisposable SourceUse)?> ReadLatestCommittedAsync(
        string targetPath,
        string backupPath,
        WorldGuidReplacementPreparation preparation,
        long maximumSnapshotBytes,
        EbinReadLimits limits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(targetPath);
        ArgumentNullException.ThrowIfNull(backupPath);
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumSnapshotBytes);
        cancellationToken.ThrowIfCancellationRequested();

        IDisposable? sourceUse = null;

        try
        {
            sourceUse = preparation.AcquireRetainedUse(
                MeasureCandidateReadWorkspaceBytes(targetPath, backupPath, maximumSnapshotBytes),
                0,
                0,
                0);
            var targetLength = ReadCandidateLength(targetPath, maximumSnapshotBytes);
            var backupLength = ReadCandidateLength(backupPath, maximumSnapshotBytes);
            (string Path, long Generation, byte[] FileRecord)? latest = null;
            latest = await ReadCandidateAsync(
                targetPath,
                targetLength,
                maximumSnapshotBytes,
                limits,
                latest,
                cancellationToken).ConfigureAwait(false);
            latest = await ReadCandidateAsync(
                backupPath,
                backupLength,
                maximumSnapshotBytes,
                limits,
                latest,
                cancellationToken).ConfigureAwait(false);

            if (latest is null)
            {
                return null;
            }

            var result = (
                latest.Value.Path,
                latest.Value.Generation,
                latest.Value.FileRecord,
                sourceUse);
            sourceUse = null;
            return result;
        }
        finally
        {
            sourceUse?.Dispose();
        }
    }

    private static async Task<(string Path, long Generation, byte[] FileRecord)?> ReadCandidateAsync(
        string candidatePath,
        long expectedLength,
        long maximumSnapshotBytes,
        EbinReadLimits limits,
        (string Path, long Generation, byte[] FileRecord)? latest,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (expectedLength == 0)
        {
            return latest;
        }

        try
        {
            var stream = new FileStream(
                candidatePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                FileStreamBufferBytes,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            await using (stream.ConfigureAwait(false))
            {
                var candidateLength = stream.Length;

                if (candidateLength != expectedLength)
                {
                    throw new IOException("A State2 candidate changed length after load admission was measured.");
                }

                if ((candidateLength > maximumSnapshotBytes) || (candidateLength > int.MaxValue))
                {
                    throw new IOException("A State2 candidate exceeded its admitted source-read limit.");
                }

                var candidate = new byte[checked((int)candidateLength)];
                await stream.ReadExactlyAsync(candidate, cancellationToken).ConfigureAwait(false);

                if (stream.Position != stream.Length)
                {
                    throw new IOException("A State2 candidate changed length while its committed generation was being read.");
                }

                long generation;

                try
                {
                    generation = WorldPersistenceCheckpointWriter.ReadTransactionGeneration(candidate, limits);
                }
                catch (ArgumentException)
                {
                    return latest;
                }

                return (latest is null) || (generation > latest.Value.Generation)
                    ? (candidatePath, generation, candidate)
                    : latest;
            }
        }
        catch (FileNotFoundException)
        {
            return latest;
        }
        catch (DirectoryNotFoundException)
        {
            return latest;
        }
    }

    private static long ReadCandidateLength(string candidatePath, long maximumSnapshotBytes)
    {
        try
        {
            var stream = new FileStream(
                candidatePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1,
                FileOptions.None);

            using (stream)
            {
                var candidateLength = stream.Length;
                return (candidateLength > maximumSnapshotBytes) || (candidateLength > int.MaxValue)
                    ? 0
                    : candidateLength;
            }
        }
        catch (FileNotFoundException)
        {
            return 0;
        }
        catch (DirectoryNotFoundException)
        {
            return 0;
        }
    }
}
