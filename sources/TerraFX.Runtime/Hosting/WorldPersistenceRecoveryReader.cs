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

        var targetLength = ReadCandidateLength(targetPath, maximumSnapshotBytes);
        var backupLength = ReadCandidateLength(backupPath, maximumSnapshotBytes);
        var candidateBytes = checked(targetLength + backupLength);
        var largestValidationCopy = Math.Max(targetLength, backupLength);
        return checked(candidateBytes + largestValidationCopy + FileStreamBufferBytes + RecoveryControlBytes);
    }

    internal static Task<(
        string Path,
        long Generation,
        byte[] FileRecord,
        WorldPersistenceCheckpointContents Contents,
        IDisposable SourceUse)?> ReadLatestCommittedAsync(
        string targetPath,
        string backupPath,
        WorldGuidReplacementPreparation preparation,
        long maximumSnapshotBytes,
        EbinReadLimits limits,
        Func<ReadOnlyMemory<byte>, WorldPersistenceCheckpointContents?> validateCandidate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(validateCandidate);
        return ReadLatestCommittedWithRetentionAsync(
            targetPath,
            backupPath,
            preparation,
            maximumSnapshotBytes,
            limits,
            (candidate, _) => {
                var contents = validateCandidate(candidate);
                return ValueTask.FromResult((contents is null)
                    ? null
                    : new CandidateValidation(contents, EmptyDisposable.Instance));
            },
            cancellationToken);
    }

    internal static async Task<(
        string Path,
        long Generation,
        byte[] FileRecord,
        WorldPersistenceCheckpointContents Contents,
        IDisposable SourceUse)?> ReadLatestCommittedWithRetentionAsync(
        string targetPath,
        string backupPath,
        WorldGuidReplacementPreparation preparation,
        long maximumSnapshotBytes,
        EbinReadLimits limits,
        Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask<CandidateValidation?>> validateCandidate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(targetPath);
        ArgumentNullException.ThrowIfNull(backupPath);
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(validateCandidate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumSnapshotBytes);
        cancellationToken.ThrowIfCancellationRequested();

        IDisposable? sourceUse = null;
        (string Path, long Generation, byte[] FileRecord, CandidateValidation Validation)? latest = null;

        try
        {
            sourceUse = preparation.AcquireRetainedUse(
                MeasureCandidateReadWorkspaceBytes(targetPath, backupPath, maximumSnapshotBytes),
                0,
                0,
                0);
            var targetLength = ReadCandidateLength(targetPath, maximumSnapshotBytes);
            var backupLength = ReadCandidateLength(backupPath, maximumSnapshotBytes);
            latest = await ReadCandidateAsync(
                targetPath,
                targetLength,
                maximumSnapshotBytes,
                limits,
                latest,
                validateCandidate,
                cancellationToken).ConfigureAwait(false);
            latest = await ReadCandidateAsync(
                backupPath,
                backupLength,
                maximumSnapshotBytes,
                limits,
                latest,
                validateCandidate,
                cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            if (latest is null)
            {
                return null;
            }

            var validationUse = latest.Value.Validation.RetainedUse;
            var retainedUse = new CompositeDisposable(sourceUse, validationUse);
            sourceUse = null;
            latest.Value.Validation.RetainedUse = EmptyDisposable.Instance;
            var result = (
                latest.Value.Path,
                latest.Value.Generation,
                latest.Value.FileRecord,
                latest.Value.Validation.Contents,
                retainedUse);
            latest = null;
            return result;
        }
        finally
        {
            sourceUse?.Dispose();
            latest?.Validation.RetainedUse.Dispose();
        }
    }

    private static async Task<(
        string Path,
        long Generation,
        byte[] FileRecord,
        CandidateValidation Validation)?> ReadCandidateAsync(
        string candidatePath,
        long expectedLength,
        long maximumSnapshotBytes,
        EbinReadLimits limits,
        (string Path, long Generation, byte[] FileRecord, CandidateValidation Validation)? latest,
        Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask<CandidateValidation?>> validateCandidate,
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

                cancellationToken.ThrowIfCancellationRequested();
                long generation;

                try
                {
                    generation = WorldPersistenceCheckpointWriter.ReadTransactionGeneration(candidate, limits);
                }
                catch (ArgumentException)
                {
                    return latest;
                }

                if ((latest is not null) && (generation <= latest.Value.Generation))
                {
                    return latest;
                }

                var validation = await validateCandidate(candidate, cancellationToken).ConfigureAwait(false);

                if (validation is null)
                {
                    return latest;
                }

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    latest?.Validation.RetainedUse.Dispose();
                    return (candidatePath, generation, candidate, validation);
                }
                catch
                {
                    validation.RetainedUse.Dispose();
                    throw;
                }
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

    internal sealed class CandidateValidation
    {
        internal CandidateValidation(
            WorldPersistenceCheckpointContents contents,
            IDisposable retainedUse)
        {
            ArgumentNullException.ThrowIfNull(contents);
            ArgumentNullException.ThrowIfNull(retainedUse);
            Contents = contents;
            RetainedUse = retainedUse;
        }

        internal WorldPersistenceCheckpointContents Contents { get; }

        internal IDisposable RetainedUse { get; set; }
    }

    private sealed class CompositeDisposable : IDisposable
    {
        private readonly IDisposable _first;
        private int _isDisposed;
        private readonly IDisposable _second;

        internal CompositeDisposable(IDisposable first, IDisposable second)
        {
            _first = first;
            _second = second;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
            {
                return;
            }

            try
            {
                _second.Dispose();
            }
            finally
            {
                _first.Dispose();
            }
        }
    }

    private sealed class EmptyDisposable : IDisposable
    {
        internal static EmptyDisposable Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
