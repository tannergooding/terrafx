// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Integration;
using TerraFX.Scenes;

namespace TerraFX.Hosting;

/// <summary>Captures and writes CPU-owned State2 checkpoints for one selected asset package.</summary>
public sealed class WorldPersistence : IAsyncDisposable
{
    private const int FileStreamBufferBytes = 65_536;
    private const int FixedArrayHeaderBytes = 32;
    private const ushort FixedArrayKind = 23;
    private const int MaximumAssetValidationReadBufferBytes = 65_537;
    private const int MaximumQueuedSavesPerPath = 8;
    private const ushort UInt32Kind = 3;
    private static readonly ConcurrentDictionary<string, SavePathState> s_savePaths = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private int _activeLoads;
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

    private static Diagnostic CreateLoadDiagnostic(string code, string message, Guid? worldGuid)
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
            operation: "PrepareLoadAsync",
            nativeResult: null,
            message: message,
            context: []);

    private static EbinReadLimits CreateLoadLimits(WorldOptions options, long maximumSnapshotBytes)
    {
        var defaults = WorldPersistenceEbinLimits.Create(options);
        return new EbinReadLimits(
            checked((int)maximumSnapshotBytes),
            defaults.MaximumFieldsPerRecord,
            defaults.MaximumDepth,
            defaults.MaximumArrayElements,
            defaults.MaximumUtf8Bytes,
            defaults.MaximumAggregateRecords,
            defaults.MaximumAggregateFields,
            defaults.MaximumAggregateArrayElements);
    }

    private static WorldLoadPreparation CreateLoadRejected(string code, string message, Guid? worldGuid)
        => new(
            WorldLoadPreparationStatus.Rejected,
            plan: null,
            [CreateLoadDiagnostic(code, message, worldGuid)]);

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

    private static bool IsLoadRejection(Exception exception)
        => exception is ArgumentException
            or InvalidDataException
            or NotSupportedException
            or OverflowException;

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
        long assetReferenceBytes,
        long maximumDecodedAssetBytes,
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
        var assetValidationPeak = checked(
            assetReferenceBytes
            + maximumDecodedAssetBytes
            + MaximumAssetValidationReadBufferBytes);
        var maximumPeak = Math.Max(
            Math.Max(profilePeak, headerPeak),
            Math.Max(
                checkpointPeak,
                Math.Max(fileRecordPeak, Math.Max(existingFileReadPeak, assetValidationPeak))));
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
        AssetReference[] assetReferences,
        int assetReferenceCount,
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

            for (var index = 0; index < assetReferenceCount; index++)
            {
                var reference = assetReferences[index];

                if (!await packageLease.MatchesAssetReferenceAsync(reference, cancellationToken).ConfigureAwait(false))
                {
                    return CreateRejected(
                        "save-package-asset-schema-mismatch",
                        $"Asset {reference.AssetId} does not match its saved schema reference.",
                        worldGuid);
                }
            }

            try
            {
                await WorldPersistenceScenesReader.ValidatePackageSourceClosureAsync(
                    checkpointRecord,
                    packageLease,
                    limits,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is ArgumentException
                or InvalidDataException
                or KeyNotFoundException
                or NotSupportedException)
            {
                return CreateRejected(
                    "save-scene-source-mismatch",
                    exception.Message,
                    worldGuid);
            }

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

            if ((_activeSaves == 0) && (_activeLoads == 0))
            {
                return ValueTask.CompletedTask;
            }

            _disposeCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return new ValueTask(_disposeCompletion.Task);
        }
    }

    /// <summary>Validates a bounded checkpoint and prepares a target-bound World replacement plan.</summary>
    /// <param name="targetWorld">The exact active World that will be replaced.</param>
    /// <param name="savePath">The absolute checkpoint path.</param>
    /// <param name="options">The explicit restore intent and source limit.</param>
    /// <param name="cancellationToken">The token that cancels source inspection.</param>
    /// <returns>The terminal preparation outcome.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="savePath"/> is empty, whitespace, or not absolute.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A load option is outside its supported range.</exception>
    /// <exception cref="ObjectDisposedException">This persistence service has been disposed.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested before source inspection was admitted.</exception>
    public Task<WorldLoadPreparation> PrepareLoadAsync(
        World targetWorld,
        string savePath,
        LoadOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetWorld);
        ArgumentNullException.ThrowIfNull(savePath);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(savePath) || !Path.IsPathFullyQualified(savePath))
        {
            throw new ArgumentException("The State2 checkpoint path must be a nonempty absolute path.", nameof(savePath));
        }

        options.ValidateForPrepareLoad(targetWorld.Options.MaximumSnapshotBytes);
        var targetPath = Path.GetFullPath(savePath);
        Guid targetWorldGuid;

        using (var captureLease = targetWorld.AcquirePersistenceCaptureLease())
        {
            targetWorldGuid = captureLease.WorldGuid;
        }

        BeginLoad();

        if ((options.RestoreIntent is not WorldRestoreIntent.Branch)
            || (targetWorld.Options.Profile is not SimulationProfile.Standard))
        {
            EndLoad();
            return Task.FromResult(CreateLoadRejected(
                "load-intent-not-supported",
                "This restore path currently admits only Standard Branch replacements.",
                targetWorldGuid));
        }

        return PrepareLoadCoreAsync(targetWorld, targetWorldGuid, targetPath, options, cancellationToken);
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

            var worldView = new EbinRecordReadView(worldRecord);
            var ecsRecord = worldView.GetRecord(5).EncodedBytes;
            var scenesRecord = worldView.GetRecord(6).EncodedBytes;
            var assetReferenceCount = WorldPersistenceAssetIdSetWriter.MeasureReferenceCount(
                schemas,
                ecsRecord,
                scenesRecord,
                limits);
            var assetReferenceBytes = checked(
                64L + ((checked((long)assetReferenceCount * Unsafe.SizeOf<AssetReference>()) + 7L) & ~7L));
            var maximumDecodedAssetBytes = capturedPackageLease.MaximumDecodedAssetBytes;
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
                assetReferenceBytes,
                maximumDecodedAssetBytes,
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

            var assetReferences = new AssetReference[assetReferenceCount];
            var uniqueAssetReferenceCount = WorldPersistenceAssetIdSetWriter.CopySortedUniqueAssetReferences(
                schemas,
                ecsRecord,
                scenesRecord,
                limits,
                assetReferences);
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
                assetReferences,
                uniqueAssetReferenceCount,
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

    private void BeginLoad()
    {
        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _activeLoads = checked(_activeLoads + 1);
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

    private void EndLoad()
    {
        lock (_lifetimeGate)
        {
            if (_activeLoads <= 0)
            {
                throw new InvalidOperationException("The persistence service has no admitted load to complete.");
            }

            _activeLoads--;

            if (_disposed && (_activeLoads == 0) && (_activeSaves == 0))
            {
                _ = (_disposeCompletion?.TrySetResult());
            }
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

            if (_disposed && (_activeSaves == 0) && (_activeLoads == 0))
            {
                _ = (_disposeCompletion?.TrySetResult());
            }
        }
    }

    private async Task<WorldLoadPreparation> PrepareLoadCoreAsync(
        World targetWorld,
        Guid targetWorldGuid,
        string targetPath,
        LoadOptions options,
        CancellationToken cancellationToken)
    {
        AssetPackageLease? packageLease = null;
        WorldPersistenceAdmissionReservation? sourceAdmission = null;
        WorldPersistenceAdmissionReservation? restoreStateAdmission = null;
        IDisposable? sourcePerWorldUse = null;
        IDisposable? restoreIdentityUse = null;
        IDisposable? recoveryUse = null;
        WorldGuidReplacementPreparation? readPreparation = null;
        WorldGuidReplacementPreparation? replacementPreparation = null;
        WorldConstructionPlan? constructionPlan = null;
        WorldLoadPreparedState? preparedState = null;
        WorldLoadPlan? loadPlan = null;
        WorldPersistenceUseLease? combinedUse = null;
        byte[]? fileRecord = null;
        WorldPersistenceCheckpointContents? checkpointContents = null;
        var sourceMismatch = (string?)null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            packageLease = _assets.AcquirePackageLease();

            if (!ReferenceEquals(targetWorld.Options.Registration.Schemas, packageLease.Registration.Schemas))
            {
                return CreateLoadRejected(
                    "load-schema-registration-mismatch",
                    "The target World and selected AssetStore package do not share the exact compiled schema registry.",
                    targetWorldGuid);
            }

            var limits = CreateLoadLimits(targetWorld.Options, options.MaximumSnapshotBytes);
            var profileMeasureStatus = FoundationProfileWriter.Measure(
                targetWorld.SpatialProfileId,
                targetWorld.SpatialSettings,
                limits,
                out var profileRecordBytes,
                out var profileDiagnostic);

            if (profileMeasureStatus is not EbinWriteStatus.Complete)
            {
                return CreateLoadRejected(
                    "load-profile-measurement-rejected",
                    profileDiagnostic?.Category ?? $"Foundation profile measurement returned {profileMeasureStatus}.",
                    targetWorldGuid);
            }

            var backupPath = string.Concat(targetPath, ".bak");
            var readWorkspaceBytes = WorldPersistenceRecoveryReader.MeasureCandidateReadWorkspaceBytes(
                targetPath,
                backupPath,
                options.MaximumSnapshotBytes);

            if (!targetWorld.TryReservePersistenceAdmissionBytes(readWorkspaceBytes, out sourceAdmission))
            {
                return CreateLoadRejected(
                    "load-admission-rejected",
                    "The target World cannot admit the bounded primary/backup source-read workspace.",
                    targetWorldGuid);
            }

            var retainedSourceAdmission = sourceAdmission
                ?? throw new InvalidOperationException("The successful load source admission did not retain its reservation.");
            sourcePerWorldUse = retainedSourceAdmission.AcquireRetainedUse(targetWorld, readWorkspaceBytes);
            retainedSourceAdmission.Dispose();
            sourceAdmission = null;

            if (!targetWorld.TryPrepareLoadReplacement(
                    targetWorldGuid,
                    sourceBytes: readWorkspaceBytes,
                    targetBytes: 1,
                    dataBytes: 1,
                    cleanupBytes: 1,
                    out readPreparation,
                    out var readBindingRefusal,
                    out var readAdmissionRefusal))
            {
                var message = (readBindingRefusal.Code == WorldGuidRefusal.TargetBindingStale)
                    ? "The exact target World binding became stale before source inspection."
                    : $"The target World refused source-read credits with admission code {readAdmissionRefusal}.";
                return CreateLoadRejected("load-target-admission-rejected", message, targetWorldGuid);
            }

            var expectedMode = WorldPersistenceMode.Standard;

            async ValueTask<WorldPersistenceRecoveryReader.CandidateValidation?> ValidateCandidateAsync(
                ReadOnlyMemory<byte> candidate,
                CancellationToken validationCancellationToken)
            {
                WorldPersistenceLoadWorkspace.Requirements requirements;

                try
                {
                    requirements = WorldPersistenceLoadWorkspace.Measure(candidate, limits);
                }
                catch (Exception exception) when (exception is ArgumentException or OverflowException or NotSupportedException)
                {
                    sourceMismatch = exception.Message;
                    return null;
                }

                if (options.NewReplayLineageId.HasValue)
                {
                    sourceMismatch = "A compatible Standard Branch restore retains its replay lineage and does not accept a replacement lineage.";
                    return null;
                }

                if (requirements.BranchCount >= requirements.Configuration.MaximumIdentityBranches)
                {
                    sourceMismatch = "The source identity history has no capacity for the requested Branch record.";
                    return null;
                }

                var profileWorkspaceBytes = checked((long)profileRecordBytes * 2);
                var validationWorkspaceBytes = checked(requirements.TotalWorkspaceBytes + profileWorkspaceBytes);

                if (!targetWorld.TryReservePersistenceAdmissionBytes(
                        validationWorkspaceBytes,
                        out var validationReservation)
                    || (validationReservation is null))
                {
                    sourceMismatch = "The target World cannot admit the source validation and decoded World data.";
                    return null;
                }

                using (validationReservation)
                {
                    var activeValidationUse = validationReservation.AcquireRetainedUse(
                        targetWorld,
                        validationWorkspaceBytes);
                    WorldPersistenceCheckpointContents? validatedContents = null;

                    try
                    {
                        var profileCandidateStorage = new byte[profileRecordBytes];
                        var profileRecord = new byte[profileRecordBytes];
                        var profileWriteStatus = FoundationProfileWriter.Write(
                            targetWorld.SpatialProfileId,
                            targetWorld.SpatialSettings,
                            limits,
                            profileCandidateStorage,
                            profileRecord,
                            out var profileBytesWritten,
                            out var writeProfileDiagnostic);

                        if ((profileWriteStatus is not EbinWriteStatus.Complete)
                            || (profileBytesWritten != profileRecord.Length))
                        {
                            throw new InvalidOperationException(
                                writeProfileDiagnostic?.Category ?? $"Foundation profile writing returned {profileWriteStatus}.");
                        }

                        var fileView = new EbinRecordReadView(candidate.Span);
                        var checkpointRecordView = fileView.GetRecord(2);
                        var headerRecord = checkpointRecordView.GetRecord(1).EncodedBytes;
                        var headerMismatch = WorldPersistenceCheckpointWriter.ValidateHeaderCompatibility(
                            headerRecord,
                            limits,
                            _metadata,
                            expectedMode,
                            expectedPackageSha256: [],
                            profileRecord);

                        if (headerMismatch is not null)
                        {
                            sourceMismatch = headerMismatch;
                            return null;
                        }

                        var identityScratch = new WorldPersistenceIdentityScratchEntry[requirements.IdentityAssignmentCount];
                        var lineageMembershipScratch = new Guid[requirements.LineageMembershipScratchCapacity];
                        var strictLayoutBuffers = default(WorldPersistenceStrictLayoutReadBuffers);
                        validatedContents = WorldPersistenceCheckpointWriter.ReadContentsWithLineageMembership(
                            candidate.Span,
                            limits,
                            targetWorld.Options.Registration,
                            identityScratch,
                            lineageMembershipScratch,
                            ref strictLayoutBuffers);
                        var history = validatedContents.WorldContents.Ecs.ValidatedIdentityHistory
                            ?? throw new InvalidOperationException("The validated State2 checkpoint omitted its identity history.");

                        for (var index = 0; index < history.Branches.Length; index++)
                        {
                            if (history.Branches[index].BranchId == options.IdentityBranchId)
                            {
                                sourceMismatch = "The selected identity branch is already present in the source history.";
                                return null;
                            }
                        }

                        var missingAssetId = FindMissingPackageAssetId(
                            validatedContents.WorldRecord.Span,
                            validatedContents.WorldContents.AssetIdCount,
                            packageLease
                                ?? throw new InvalidOperationException("The load lost its selected package lease."));

                        if (missingAssetId is { } assetId)
                        {
                            sourceMismatch = $"The selected package does not contain referenced asset {assetId}.";
                            return null;
                        }

                        var checkpointRecord = WorldPersistenceCheckpointWriter.ReadCheckpointRecord(candidate.Span, limits);
                        await WorldPersistenceScenesReader.ValidatePackageSourceClosureAsync(
                            checkpointRecord,
                            packageLease,
                            limits,
                            validationCancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception exception) when (
                        exception is ArgumentException
                            or InvalidDataException
                            or NotSupportedException
                            or OverflowException)
                    {
                        sourceMismatch = exception.Message;
                        return null;
                    }
                    finally
                    {
                        activeValidationUse.Dispose();
                    }

                    validationReservation.Shrink(
                        checked(validationWorkspaceBytes - requirements.RetainedContentsBytes));
                    var retainedUse = validationReservation.AcquireRetainedUse(
                        targetWorld,
                        requirements.RetainedContentsBytes);
                    return new WorldPersistenceRecoveryReader.CandidateValidation(validatedContents!, retainedUse);
                }
            }

            var selected = await WorldPersistenceRecoveryReader.ReadLatestCommittedWithRetentionAsync(
                targetPath,
                backupPath,
                readPreparation
                    ?? throw new InvalidOperationException("The source validation preparation was not retained."),
                options.MaximumSnapshotBytes,
                limits,
                ValidateCandidateAsync,
                cancellationToken).ConfigureAwait(false);
            readPreparation.Dispose();
            readPreparation = null;

            if (selected is null)
            {
                return CreateLoadRejected(
                    "load-checkpoint-rejected",
                    sourceMismatch ?? "The primary and backup files contain no valid committed Standard State2 checkpoint.",
                    targetWorldGuid);
            }

            fileRecord = selected.Value.FileRecord;
            checkpointContents = selected.Value.Contents;
            recoveryUse = selected.Value.SourceUse;
            var identityHistory = checkpointContents.WorldContents.Ecs.ValidatedIdentityHistory
                ?? throw new InvalidOperationException("The validated State2 checkpoint omitted its complete identity history.");
            var identityBranchId = options.IdentityBranchId
                ?? throw new InvalidOperationException("The Branch restore option omitted its identity branch.");
            var restoreStateBytes = WorldRestoreIdentityState.MeasureBranchOwnedByteReservation(
                identityHistory,
                identityBranchId,
                checkpointContents.CheckpointDigest.Span,
                checkpointContents.WorldContents.Configuration);

            if (!targetWorld.TryReservePersistenceAdmissionBytes(restoreStateBytes, out restoreStateAdmission)
                || (restoreStateAdmission is null))
            {
                return CreateLoadRejected(
                    "load-identity-admission-rejected",
                    "The target World cannot admit the restored identity directory and locator maps.",
                    targetWorldGuid);
            }

            var retainedRestoreReservation = restoreStateAdmission
                ?? throw new InvalidOperationException("The identity restoration reservation was not retained.");
            restoreIdentityUse = retainedRestoreReservation.AcquireRetainedUse(targetWorld, restoreStateBytes);
            retainedRestoreReservation.Dispose();
            restoreStateAdmission = null;

            WorldRestoreIdentityState restoreIdentityState;

            try
            {
                restoreIdentityState = WorldRestoreIdentityState.CreateBranch(
                    identityHistory,
                    identityBranchId,
                    checkpointContents.CheckpointDigest.Span,
                    checkpointContents.NextTick,
                    isStrictLocal: false,
                    checkpointContents.WorldContents.Configuration);
            }
            catch (InvalidOperationException exception)
            {
                return CreateLoadRejected("load-identity-rejected", exception.Message, targetWorldGuid);
            }

            var tickRate = checkpointContents.WorldContents.Clock.TickRate;
            var timeScale = checkpointContents.WorldContents.Clock.TimeScale;
            WorldRestoreEcsPlan restoreEcsPlan;

            try
            {
                restoreEcsPlan = WorldPersistenceEcsReader.CreateHomogeneousRestorePlan(
                    checkpointContents.WorldContents.EcsRecord,
                    checkpointContents.WorldContents.Ecs,
                    targetWorld.Options.Registration);
            }
            catch (NotSupportedException exception)
            {
                return CreateLoadRejected("load-state-not-supported", exception.Message, targetWorldGuid);
            }

            long targetBytes;

            try
            {
                targetBytes = targetWorld.MeasureRestoreWorldOwnedBytes(
                    restoreIdentityState,
                    checkpointContents.WorldContents.Configuration,
                    tickRate,
                    timeScale,
                    restoreEcsPlan);
            }
            catch (InvalidOperationException exception)
            {
                return CreateLoadRejected("load-target-budget-rejected", exception.Message, targetWorldGuid);
            }

            var sourceBytes = Math.Max(1L, checked((long)fileRecord.Length + readWorkspaceBytes));
            var dataBytes = Math.Max(
                1L,
                checked(
                    restoreStateBytes
                    + restoreEcsPlan.OwnedByteReservation
                    + checkpointContents.WorldRecord.Length
                    + checkpointContents.HeaderRecord.Length
                    + 512L));
            var cleanupBytes = Math.Max(1L, targetBytes);

            if (!targetWorld.TryPrepareLoadReplacement(
                    checkpointContents.WorldGuid,
                    sourceBytes,
                    targetBytes,
                    dataBytes,
                    cleanupBytes,
                    out replacementPreparation,
                    out var bindingRefusal,
                    out var admissionRefusal))
            {
                var message = (bindingRefusal.Code == WorldGuidRefusal.TargetBindingStale)
                    ? "The exact target World binding became stale during checkpoint preparation."
                    : $"The target World refused replacement credits with admission code {admissionRefusal}.";
                return CreateLoadRejected("load-target-admission-rejected", message, targetWorldGuid);
            }

            if (!targetWorld.TryPrepareRestoreConstructionPlan(
                    restoreIdentityState,
                    checkpointContents.WorldContents.Configuration,
                    replacementPreparation
                        ?? throw new InvalidOperationException("The replacement preparation was not retained."),
                    tickRate,
                    timeScale,
                    out constructionPlan,
                    out bindingRefusal,
                    out admissionRefusal,
                    restoreEcsPlan)
                || (constructionPlan is null))
            {
                var message = (bindingRefusal.Code == WorldGuidRefusal.TargetBindingStale)
                    ? "The exact target World binding became stale before the prepared plan could be returned."
                    : $"The target World refused restore construction admission with code {admissionRefusal}.";
                return CreateLoadRejected("load-construction-admission-rejected", message, targetWorldGuid);
            }

            combinedUse = new WorldPersistenceUseLease(
                recoveryUse,
                sourcePerWorldUse,
                restoreIdentityUse);
            recoveryUse = null;
            sourcePerWorldUse = null;
            restoreIdentityUse = null;
            preparedState = new WorldLoadPreparedState(
                constructionPlan,
                replacementPreparation,
                packageLease,
                combinedUse,
                fileRecord,
                checkpointContents,
                options.RestoreIntent);
            constructionPlan = null;
            replacementPreparation = null;
            packageLease = null;
            combinedUse = null;
            fileRecord = null;
            var payloadDigest = SHA256.HashData(checkpointContents.WorldRecord.Span);
            loadPlan = new WorldLoadPlan(
                targetWorld,
                targetWorldGuid,
                checkpointContents.WorldGuid,
                checkpointContents.WorldGuid,
                checkpointContents.NextTick);
            loadPlan.Initialize(
                preparedState,
                checkpointContents.CheckpointDigest.Span,
                payloadDigest);
            preparedState = null;
            var preparation = new WorldLoadPreparation(
                WorldLoadPreparationStatus.Prepared,
                loadPlan,
                []);
            loadPlan = null;
            return preparation;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsLoadRejection(exception))
        {
            return CreateLoadRejected("load-preparation-rejected", exception.Message, targetWorldGuid);
        }
        finally
        {
            loadPlan?.Dispose();
            preparedState?.Dispose();
            constructionPlan?.ReleaseUntransferredRestoreResources();
            replacementPreparation?.Dispose();
            readPreparation?.Dispose();
            combinedUse?.Dispose();
            recoveryUse?.Dispose();
            sourcePerWorldUse?.Dispose();
            sourceAdmission?.Dispose();
            restoreStateAdmission?.Dispose();
            restoreIdentityUse?.Dispose();
            packageLease?.Dispose();
            EndLoad();
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
