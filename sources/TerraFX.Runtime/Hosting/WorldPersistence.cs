// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Integration;

namespace TerraFX.Hosting;

/// <summary>Captures and writes CPU-owned State2 checkpoints for one selected asset package.</summary>
public sealed class WorldPersistence : IAsyncDisposable
{
    private const int FileStreamBufferBytes = 65_536;
    private const int FixedArrayHeaderBytes = 32;
    private const ushort FixedArrayKind = 23;
    private const int MaximumQueuedSavesPerPath = 8;
    private const ushort UInt32Kind = 3;
    private static readonly ConcurrentDictionary<string, SavePathState> s_savePaths = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private int _activeSaves;
    private readonly AssetStore _assets;
    private TaskCompletionSource? _disposeCompletion;
    private bool _disposed;
    private readonly Lock _lifetimeGate = new();
    private readonly WorldPersistenceMetadata _metadata;

    /// <summary>Initializes a standalone persistence service for one asset store and explicit metadata profile.</summary>
    /// <param name="assets">The exact asset store whose selected package backs the active World.</param>
    /// <param name="metadata">The caller-supplied application and runtime identity.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public WorldPersistence(AssetStore assets, WorldPersistenceMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(metadata);
        _assets = assets;
        _metadata = metadata;
    }

    private static long Align16(long value)
    {
        if (value > (long.MaxValue - 15))
        {
            throw new InvalidOperationException("The State2 record alignment overflowed.");
        }

        return (value + 15) & ~15L;
    }

    private static Diagnostic CreateDiagnostic(string code, string message, Guid? worldGuid)
        => new(
            code,
            DiagnosticSeverity.Error,
            Stopwatch.GetTimestamp(),
            hostId: null,
            worldGuid: worldGuid,
            deviceId: null,
            surfaceId: null,
            deviceEpoch: null,
            surfaceEpoch: null,
            swapchainEpoch: null,
            tick: null,
            systemId: null,
            passId: null,
            assetId: null,
            commandIdentity: null,
            operation: "SaveAsync",
            nativeResult: null,
            message: message,
            context: []);

    private static SaveReceipt CreateRejected(string code, string message, Guid? worldGuid)
        => new(SaveStatus.Rejected, transactionGeneration: null, diagnostic: CreateDiagnostic(code, message, worldGuid));

    private static AssetId? FindMissingPackageAssetId(
        scoped ReadOnlySpan<byte> worldRecord,
        int assetIdCount,
        AssetPackageLease packageLease)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(assetIdCount);
        ArgumentNullException.ThrowIfNull(packageLease);
        var world = new EbinRecordReadView(worldRecord);

        if (!world.TryGetField(7, out var assetIds)
            || (assetIds.Kind != FixedArrayKind)
            || (assetIds.Count != (ulong)assetIdCount)
            || (assetIds.Payload.Length != checked(FixedArrayHeaderBytes + (assetIdCount * sizeof(uint))))
            || (BinaryPrimitives.ReadUInt16LittleEndian(assetIds.Payload[16..]) != UInt32Kind)
            || (BinaryPrimitives.ReadUInt32LittleEndian(assetIds.Payload[20..]) != sizeof(uint)))
        {
            throw new InvalidOperationException("The captured state/world AssetIds array has an invalid fixed-array extent.");
        }

        for (var index = 0; index < assetIdCount; index++)
        {
            var value = BinaryPrimitives.ReadUInt32LittleEndian(
                assetIds.Payload[(FixedArrayHeaderBytes + (index * sizeof(uint)))..]);
            var assetId = new AssetId(checked((int)value));

            if (!packageLease.ContainsAssetId(assetId))
            {
                return assetId;
            }
        }

        return null;
    }

    private static bool IsSaveRejection(Exception exception)
        => exception is ArgumentException
            or IOException
            or InvalidOperationException
            or NotSupportedException
            or OverflowException
            or UnauthorizedAccessException;

    private static int MeasureRecordBytes(scoped ReadOnlySpan<int> fieldPayloadBytes)
    {
        var cursor = Align16(checked(64L + (fieldPayloadBytes.Length * 32L)));

        for (var index = 0; index < fieldPayloadBytes.Length; index++)
        {
            cursor = checked(Align16(cursor) + fieldPayloadBytes[index]);
        }

        var recordBytes = Align16(cursor);

        if (recordBytes > int.MaxValue)
        {
            throw new InvalidOperationException("A State2 record extent exceeds the supported managed-array length.");
        }

        return checked((int)recordBytes);
    }

    private static long MeasureSaveWorkspaceBytes(
        WorldPersistenceMetadata metadata,
        WorldPersistenceMode mode,
        int worldRecordBytes,
        int foundationProfileBytes,
        long maximumSnapshotBytes,
        EbinReadLimits limits)
    {
        var packageDigestBytes = mode is WorldPersistenceMode.StrictLocal ? 32 : 0;
        Span<int> headerFieldPayloadBytes = stackalloc int[11];
        var headerFieldCount = 0;

        if (packageDigestBytes > 0)
        {
            headerFieldPayloadBytes[headerFieldCount++] = packageDigestBytes;
        }

        headerFieldPayloadBytes[headerFieldCount++] = metadata.ApplicationRecord.Length;
        headerFieldPayloadBytes[headerFieldCount++] = 16;
        headerFieldPayloadBytes[headerFieldCount++] = foundationProfileBytes;
        headerFieldPayloadBytes[headerFieldCount++] = 8;
        headerFieldPayloadBytes[headerFieldCount++] = 1;
        headerFieldPayloadBytes[headerFieldCount++] = 16;
        headerFieldPayloadBytes[headerFieldCount++] = metadata.RuntimeProfileRecord.Length;
        headerFieldPayloadBytes[headerFieldCount++] = 4;
        headerFieldPayloadBytes[headerFieldCount++] = 4;
        headerFieldPayloadBytes[headerFieldCount++] = 1;
        var headerRecordBytes = MeasureRecordBytes(headerFieldPayloadBytes[..headerFieldCount]);
        Span<int> canonicalFieldPayloadBytes = stackalloc int[7];
        var canonicalFieldCount = 0;
        canonicalFieldPayloadBytes[canonicalFieldCount++] = 1;

        if (packageDigestBytes > 0)
        {
            canonicalFieldPayloadBytes[canonicalFieldCount++] = packageDigestBytes;
        }

        canonicalFieldPayloadBytes[canonicalFieldCount++] = metadata.ApplicationRecord.Length;
        canonicalFieldPayloadBytes[canonicalFieldCount++] = 16;
        canonicalFieldPayloadBytes[canonicalFieldCount++] = foundationProfileBytes;
        canonicalFieldPayloadBytes[canonicalFieldCount++] = worldRecordBytes;

        if (mode is WorldPersistenceMode.StrictLocal)
        {
            canonicalFieldPayloadBytes[canonicalFieldCount++] = metadata.RuntimeProfileRecord.Length;
        }

        var canonicalRecordBytes = MeasureRecordBytes(canonicalFieldPayloadBytes[..canonicalFieldCount]);
        Span<int> checkpointFieldPayloadBytes = [headerRecordBytes, worldRecordBytes, 32];
        var checkpointRecordBytes = MeasureRecordBytes(checkpointFieldPayloadBytes);
        Span<int> fileFieldPayloadBytes = [8, checkpointRecordBytes, 32, 32];
        var fileRecordBytes = MeasureRecordBytes(fileFieldPayloadBytes);

        if ((foundationProfileBytes > limits.MaximumRecordBytes)
            || (headerRecordBytes > limits.MaximumRecordBytes)
            || (canonicalRecordBytes > limits.MaximumRecordBytes)
            || (checkpointRecordBytes > limits.MaximumRecordBytes)
            || (fileRecordBytes > limits.MaximumRecordBytes))
        {
            throw new InvalidOperationException("A State2 save record exceeds the World snapshot-byte limit.");
        }

        long headerPayloadBytes = 0;

        for (var index = 0; index < headerFieldCount; index++)
        {
            headerPayloadBytes = checked(headerPayloadBytes + headerFieldPayloadBytes[index]);
        }

        var metadataProjectionBytes = checked(
            (long)metadata.ApplicationRecord.Length
            + foundationProfileBytes
            + metadata.RuntimeProfileRecord.Length);
        var profilePeak = checked(2L * foundationProfileBytes);
        var headerPeak = checked(foundationProfileBytes + headerPayloadBytes + (2L * headerRecordBytes));
        var checkpointPeak = checked(
            foundationProfileBytes
            + (2L * headerRecordBytes)
            + worldRecordBytes
            + (2L * checkpointRecordBytes)
            + metadataProjectionBytes
            + 96);
        var fileRecordPeak = checked(
            foundationProfileBytes
            + headerRecordBytes
            + (2L * checkpointRecordBytes)
            + (2L * fileRecordBytes)
            + 168);
        var existingFileReadPeak = checked(
            foundationProfileBytes
            + headerRecordBytes
            + checkpointRecordBytes
            + metadataProjectionBytes
            + maximumSnapshotBytes
            + FileStreamBufferBytes);
        var maximumPeak = Math.Max(
            Math.Max(profilePeak, headerPeak),
            Math.Max(checkpointPeak, Math.Max(fileRecordPeak, existingFileReadPeak)));
        return checked(maximumPeak + 4096);
    }

    private static async Task<long> ReadLatestCommittedGenerationAsync(
        string targetPath,
        string backupPath,
        long maximumSnapshotBytes,
        EbinReadLimits limits,
        CancellationToken cancellationToken)
    {
        var maximumGeneration = 0L;
        string[] candidates = [targetPath, backupPath];

        for (var index = 0; index < candidates.Length; index++)
        {
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
                    throw new InvalidDataException("A committed State2 candidate exceeds the World snapshot-byte limit.");
                }

                var candidate = new byte[checked((int)candidateLength)];
                await stream.ReadExactlyAsync(candidate, cancellationToken).ConfigureAwait(false);

                if (stream.Position != stream.Length)
                {
                    throw new IOException("A State2 candidate changed length while its transaction generation was being read.");
                }

                var generation = WorldPersistenceCheckpointWriter.ReadTransactionGeneration(candidate, limits);
                maximumGeneration = Math.Max(maximumGeneration, generation);
            }
        }

        return maximumGeneration;
    }

    private static async Task<SaveReceipt> SaveOnDiskAsync(
        SavePathState pathState,
        string targetPath,
        SaveOptions options,
        EbinReadLimits limits,
        long maximumSnapshotBytes,
        Guid worldGuid,
        byte[] checkpointRecord,
        AssetPackageLease packageLease,
        IDisposable? saveUse,
        CancellationToken cancellationToken)
    {
        string? temporaryPath = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var durableReplacementAvailable = SupportsDurableReplacement(targetPath);

            if ((options.DurabilityPolicy is SaveDurabilityPolicy.RequireDurable)
                && !durableReplacementAvailable)
            {
                return new SaveReceipt(
                    SaveStatus.DurabilityUnsupported,
                    transactionGeneration: null,
                    diagnostic: CreateDiagnostic(
                        "save-durability-unsupported",
                        "The destination filesystem cannot provide the requested durable replacement guarantee.",
                        worldGuid));
            }

            cancellationToken.ThrowIfCancellationRequested();
            var directoryPath = Path.GetDirectoryName(targetPath)
                ?? throw new InvalidOperationException("The absolute save path has no parent directory.");
            _ = Directory.CreateDirectory(directoryPath);
            var backupPath = string.Concat(targetPath, ".bak");
            var previousGeneration = await ReadLatestCommittedGenerationAsync(
                targetPath,
                backupPath,
                maximumSnapshotBytes,
                limits,
                cancellationToken).ConfigureAwait(false);
            var transactionGeneration = pathState.NextGeneration(previousGeneration);
            var (fileRecord, _) = WorldPersistenceCheckpointWriter.CreateFileRecord(transactionGeneration, checkpointRecord, limits);
            cancellationToken.ThrowIfCancellationRequested();
            temporaryPath = Path.Combine(
                directoryPath,
                string.Concat(".", Path.GetFileName(targetPath), ".", Guid.NewGuid().ToString("N"), ".tmp"));

            var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                FileStreamBufferBytes,
                FileOptions.Asynchronous | FileOptions.WriteThrough);
            await using (stream.ConfigureAwait(false))
            {
                await stream.WriteAsync(fileRecord, cancellationToken).ConfigureAwait(false);
                await Task.Run(() => stream.Flush(flushToDisk: true), CancellationToken.None).ConfigureAwait(false);

                if (stream.Length != fileRecord.Length)
                {
                    throw new IOException("The flushed State2 temporary file length does not match the encoded record.");
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(targetPath))
            {
                File.Replace(temporaryPath, targetPath, backupPath);
            }
            else
            {
                File.Move(temporaryPath, targetPath);
            }

            temporaryPath = null;
            var status = durableReplacementAvailable
                ? SaveStatus.DurableCommitted
                : SaveStatus.BestEffortCommitted;
            return new SaveReceipt(status, transactionGeneration, diagnostic: null);
        }
        catch (OperationCanceledException)
        {
            if (temporaryPath is not null)
            {
                File.Delete(temporaryPath);
            }

            throw;
        }
        catch (Exception exception) when (IsSaveRejection(exception))
        {
            if (temporaryPath is not null)
            {
                File.Delete(temporaryPath);
            }

            return CreateRejected("save-io-rejected", exception.Message, worldGuid);
        }
        finally
        {
            packageLease.Dispose();
            saveUse?.Dispose();
        }
    }

    private static bool SupportsDurableReplacement(string targetPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var root = Path.GetPathRoot(targetPath);

        if (string.IsNullOrEmpty(root))
        {
            return false;
        }

        var drive = new DriveInfo(root);
        return (drive.DriveType is DriveType.Fixed)
            && (string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase)
                || string.Equals(drive.DriveFormat, "ReFS", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Waits for accepted saves without disposing the caller-owned AssetStore.</summary>
    public ValueTask DisposeAsync()
    {
        lock (_lifetimeGate)
        {
            if (_disposed)
            {
                return _disposeCompletion is null
                    ? ValueTask.CompletedTask
                    : new ValueTask(_disposeCompletion.Task);
            }

            _disposed = true;

            if (_activeSaves == 0)
            {
                return ValueTask.CompletedTask;
            }

            _disposeCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return new ValueTask(_disposeCompletion.Task);
        }
    }

    /// <summary>Captures a committed World synchronously on its owner and atomically writes one checkpoint.</summary>
    /// <param name="world">The World to capture on its owner thread.</param>
    /// <param name="options">The absolute destination and durability policy.</param>
    /// <param name="cancellationToken">The token that cancels work before replacement.</param>
    /// <returns>The terminal save outcome.</returns>
    /// <remarks>The save mode must match <see cref="WorldOptions.Profile"/>; a mismatch returns a rejected receipt before capture or file I/O.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="world"/> or <paramref name="options"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">This persistence service has been disposed.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested before capture was admitted.</exception>
    [SuppressMessage(
        "Reliability",
        "CA2025",
        Justification = "The queued operation owns the package and admission leases and disposes them in SaveOnDiskAsync before its task completes.")]
    public Task<SaveReceipt> SaveAsync(World world, SaveOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        BeginSave();

        AssetPackageLease? packageLease = null;
        WorldPersistenceAdmissionReservation? captureReservation = null;
        IDisposable? captureUse = null;
        WorldPersistenceAdmissionReservation? saveReservation = null;
        IDisposable? saveUse = null;
        var saveHandedOff = false;

        try
        {
            var strictLocalWorld = world.Options.Profile is SimulationProfile.StrictLocal;

            if (strictLocalWorld != (options.Mode is WorldPersistenceMode.StrictLocal))
            {
                return Task.FromResult(CreateRejected(
                    "save-mode-profile-mismatch",
                    "The save mode must match the World simulation profile; StrictLocal saves require a StrictLocal World.",
                    world.WorldGuid));
            }

            packageLease = _assets.AcquirePackageLease();
            var schemas = world.Options.Registration.Schemas;

            if (!ReferenceEquals(schemas, packageLease.Registration.Schemas))
            {
                return Task.FromResult(CreateRejected(
                    "save-schema-registration-mismatch",
                    "The World and selected AssetStore package do not share the exact compiled schema registry.",
                    world.WorldGuid));
            }

            using var captureLease = world.AcquirePersistenceCaptureLease();
            var limits = WorldPersistenceEbinLimits.Create(world.Options);
            var captureStatus = WorldPersistenceCaptureCoordinator.Capture(
                world,
                captureLease,
                schemas,
                out captureReservation,
                out var capturedWorldRecord,
                out var assetIdCount,
                out var captureDiagnostic,
                cancellationToken);

            if (captureStatus is not EbinWriteStatus.Complete)
            {
                return Task.FromResult(CreateRejected(
                    "save-capture-rejected",
                    captureDiagnostic?.Category ?? $"World capture returned {captureStatus}.",
                    world.WorldGuid));
            }

            var worldRecord = capturedWorldRecord
                ?? throw new InvalidOperationException("The successful World capture did not produce its record.");
            var capturedPackageLease = packageLease
                ?? throw new InvalidOperationException("The selected package lease was not retained during capture.");
            var missingPackageAssetId = FindMissingPackageAssetId(
                worldRecord,
                assetIdCount,
                capturedPackageLease);

            if (missingPackageAssetId is { } missingAssetId)
            {
                return Task.FromResult(CreateRejected(
                    "save-package-asset-missing",
                    $"The selected package does not contain referenced asset {missingAssetId}.",
                    world.WorldGuid));
            }

            var retainedCaptureReservation = captureReservation
                ?? throw new InvalidOperationException("The successful World capture did not retain its admission reservation.");
            var foundationMeasureStatus = FoundationProfileWriter.Measure(
                world.SpatialProfileId,
                world.SpatialSettings,
                limits,
                out var foundationProfileBytes,
                out var foundationDiagnostic);

            if (foundationMeasureStatus is not EbinWriteStatus.Complete)
            {
                return Task.FromResult(CreateRejected(
                    "save-profile-measurement-rejected",
                    foundationDiagnostic?.Category ?? $"Foundation profile measurement returned {foundationMeasureStatus}.",
                    world.WorldGuid));
            }

            var workspaceBytes = MeasureSaveWorkspaceBytes(
                _metadata,
                options.Mode,
                worldRecord.Length,
                foundationProfileBytes,
                world.Options.MaximumSnapshotBytes,
                limits);

            if (!captureLease.TryReserveAdmissionBytes(workspaceBytes, out saveReservation))
            {
                return Task.FromResult(CreateRejected(
                    "save-admission-rejected",
                    "The World persistence admission budget cannot cover the save's retained records and file-validation workspace.",
                    world.WorldGuid));
            }

            var retainedSaveReservation = saveReservation
                ?? throw new InvalidOperationException("The save workspace reservation was not created.");
            saveUse = retainedSaveReservation.AcquireRetainedUse(world, workspaceBytes);
            retainedSaveReservation.Dispose();
            saveReservation = null;

            captureUse = retainedCaptureReservation.AcquireRetainedUse(world, worldRecord.Length);
            retainedCaptureReservation.Dispose();
            captureReservation = null;

            var packageSha256 = options.Mode is WorldPersistenceMode.StrictLocal
                ? packageLease.PackageSha256.AsSpan()
                : [];
            var foundationProfileRecord = new byte[foundationProfileBytes];
            var foundationCandidateStorage = new byte[foundationProfileBytes];
            var writeProfileStatus = FoundationProfileWriter.Write(
                world.SpatialProfileId,
                world.SpatialSettings,
                limits,
                foundationCandidateStorage,
                foundationProfileRecord,
                out var profileBytesWritten,
                out foundationDiagnostic);

            if ((writeProfileStatus is not EbinWriteStatus.Complete) || (profileBytesWritten != foundationProfileRecord.Length))
            {
                return Task.FromResult(CreateRejected(
                    "save-profile-write-rejected",
                    foundationDiagnostic?.Category ?? $"Foundation profile writing returned {writeProfileStatus}.",
                    world.WorldGuid));
            }

            var headerRecord = WorldPersistenceHeaderWriter.Create(
                _metadata,
                options.Mode,
                packageSha256,
                captureLease.WorldGuid,
                world.ReplayLineageId,
                captureLease.NextTickIndex,
                checked((byte)world.CpuWorkerCount),
                foundationProfileRecord);
            var (checkpointRecord, _, _) = WorldPersistenceCheckpointWriter.CreateCheckpoint(headerRecord, worldRecord, limits);
            captureUse.Dispose();
            captureUse = null;

            var targetPath = Path.GetFullPath(options.AbsoluteSavePath);
            var pathState = s_savePaths.GetOrAdd(targetPath, static _ => new SavePathState());
            var maximumSnapshotBytes = world.Options.MaximumSnapshotBytes;
            var capturedWorldGuid = world.WorldGuid;
            var retainedPackageLease = packageLease
                ?? throw new InvalidOperationException("The selected package lease was not retained for file I/O.");
            var retainedSaveUse = saveUse
                ?? throw new InvalidOperationException("The save workspace lease was not retained for file I/O.");
            var queuedSave = pathState.TryEnqueue(() => SaveOnDiskAsync(
                pathState,
                targetPath,
                options,
                limits,
                maximumSnapshotBytes,
                capturedWorldGuid,
                checkpointRecord,
                retainedPackageLease,
                retainedSaveUse,
                cancellationToken));

            if (queuedSave is null)
            {
                return Task.FromResult(CreateRejected(
                    "save-queue-full",
                    $"The destination already has {MaximumQueuedSavesPerPath} queued or active saves.",
                    world.WorldGuid));
            }

            packageLease = null;
            saveUse = null;
            saveHandedOff = true;
            return CompleteSaveAsync(queuedSave);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsSaveRejection(exception))
        {
            return Task.FromResult(CreateRejected(
                "save-capture-rejected",
                exception.Message,
                world.WorldGuid));
        }
        finally
        {
            captureUse?.Dispose();
            saveUse?.Dispose();
            captureReservation?.Dispose();
            saveReservation?.Dispose();
            packageLease?.Dispose();

            if (!saveHandedOff)
            {
                EndSave();
            }
        }
    }

    private void BeginSave()
    {
        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _activeSaves = checked(_activeSaves + 1);
        }
    }

    private async Task<SaveReceipt> CompleteSaveAsync(Task<SaveReceipt> saveTask)
    {
        try
        {
            return await saveTask.ConfigureAwait(false);
        }
        finally
        {
            EndSave();
        }
    }

    private void EndSave()
    {
        lock (_lifetimeGate)
        {
            if (_activeSaves <= 0)
            {
                throw new InvalidOperationException("The persistence service has no admitted save to complete.");
            }

            _activeSaves--;

            if (_disposed && (_activeSaves == 0))
            {
                _ = (_disposeCompletion?.TrySetResult());
            }
        }
    }

    private sealed class SavePathState
    {
        private readonly Lock _gate = new();
        private long _highestIssuedGeneration;
        private int _pending;
        private Task _tail = Task.CompletedTask;

        internal long NextGeneration(long highestCommittedGeneration)
        {
            lock (_gate)
            {
                _highestIssuedGeneration = Math.Max(_highestIssuedGeneration, highestCommittedGeneration);
                ArgumentOutOfRangeException.ThrowIfEqual(_highestIssuedGeneration, long.MaxValue);
                _highestIssuedGeneration = checked(_highestIssuedGeneration + 1);
                return _highestIssuedGeneration;
            }
        }

        internal Task<SaveReceipt>? TryEnqueue(Func<Task<SaveReceipt>> operation)
        {
            lock (_gate)
            {
                if (_pending >= MaximumQueuedSavesPerPath)
                {
                    return null;
                }

                _pending++;
                var previous = _tail;
                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _tail = completion.Task;
                return RunAfterAsync(previous, completion, operation);
            }
        }

        private async Task<SaveReceipt> RunAfterAsync(
            Task predecessor,
            TaskCompletionSource completion,
            Func<Task<SaveReceipt>> operation)
        {
            await predecessor.ConfigureAwait(false);

            try
            {
                return await operation().ConfigureAwait(false);
            }
            finally
            {
                lock (_gate)
                {
                    _pending--;
                }

                _ = completion.TrySetResult();
            }
        }
    }
}
