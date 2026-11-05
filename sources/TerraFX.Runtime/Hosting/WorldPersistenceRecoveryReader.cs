// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TerraFX;

namespace TerraFX.Hosting;

internal static class WorldPersistenceRecoveryReader
{
    private const int FileStreamBufferBytes = 65_536;

    internal static async Task<(string Path, long Generation, byte[] FileRecord)?> ReadLatestCommittedAsync(
        string targetPath,
        string backupPath,
        long maximumSnapshotBytes,
        EbinReadLimits limits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(targetPath);
        ArgumentNullException.ThrowIfNull(backupPath);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumSnapshotBytes);

        (string Path, long Generation, byte[] FileRecord)? latest = null;
        string[] candidates = [targetPath, backupPath];

        for (var index = 0; index < candidates.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidatePath = candidates[index];

            if (!File.Exists(candidatePath))
            {
                continue;
            }

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

                if ((candidateLength > maximumSnapshotBytes) || (candidateLength > int.MaxValue))
                {
                    continue;
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
                    continue;
                }

                if ((latest is null) || (generation > latest.Value.Generation))
                {
                    latest = (candidatePath, generation, candidate);
                }
            }
        }

        return latest;
    }
}
