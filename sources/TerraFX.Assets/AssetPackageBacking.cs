// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace TerraFX.Assets;

internal sealed class AssetPackageBacking
{
    private static AssetPackageBacking? s_failedReleaseRoot;
    private static readonly Lock s_failedReleaseRootGate = new();
    private readonly AssetPackageBackingToken _backingToken = new();
    private readonly AssetBoundedNativeFile? _boundedNativeFile;
    private AssetStoreBoundedProvision? _boundedProvision;
    private readonly Lock _gate = new();
    private bool _isFailedReleaseRooted;
    private AssetPackageBacking? _nextFailedRelease;
    private bool _ownerReleased;
    private readonly EbinReadLimits _readLimits;
    private AssetStoreRegistration? _registration;
    private Exception? _releaseFailure;
    private int _releaseState;
    private FileStream? _stream;
    private int _referenceCount = 1;

    internal AssetPackageBacking(
        FileStream stream,
        ImmutableArray<AssetPackageEntry> entries,
        SpatialProfileId profileId,
        SpatialSettings spatialSettings,
        AssetStoreOptions options,
        long packageByteLength)
    {
        _stream = stream;
        _boundedNativeFile = null;
        Entries = entries;
        PackageByteLength = packageByteLength;
        ProfileId = profileId;
        CpuAssetBudgetBytes = options.CpuAssetBudgetBytes;
        ExpectedPackageSha256 = options.ExpectedPackageSha256;
        MaximumConcurrentDecodeBytes = options.MaximumConcurrentDecodeBytes;
        MaximumDecodedAssetBytes = options.MaximumDecodedAssetBytes;
        MaximumLiveAssetLeases = options.MaximumLiveAssetLeases;
        _registration = options.Registration;
        _readLimits = CreateReadLimits(options.MaximumDecodedAssetBytes);
        SpatialSettings = spatialSettings;
    }

    internal AssetPackageBacking(AssetStoreBoundedProvision boundedProvision)
    {
        ArgumentNullException.ThrowIfNull(boundedProvision);
        _boundedNativeFile = new AssetBoundedNativeFile();
        _boundedProvision = boundedProvision;
        Entries = [];
        PackageByteLength = 0;
        ProfileId = default;
        CpuAssetBudgetBytes = boundedProvision.CpuAssetBudgetBytes;
        ExpectedPackageSha256 = [];
        MaximumConcurrentDecodeBytes = boundedProvision.MaximumConcurrentDecodeBytes;
        MaximumDecodedAssetBytes = boundedProvision.MaximumDecodedAssetBytes;
        MaximumLiveAssetLeases = boundedProvision.MaximumLiveAssetLeases;
        _readLimits = CreateReadLimits(boundedProvision.MaximumDecodedAssetBytes);
        SpatialSettings = default;
    }

    internal AssetPackageBackingToken BackingToken => _backingToken;

    internal AssetBoundedNativeFile BoundedNativeFile
        => _boundedNativeFile
            ?? throw new InvalidOperationException("The package backing has no bounded native-file state.");

    internal AssetBoundedNativePort BoundedNativePort
        => BoundedProvision.NativePort;

    internal AssetStoreBoundedProvision BoundedProvision
        => _boundedProvision
            ?? throw new InvalidOperationException("The package backing has no bounded opening provision.");

    internal long BoundedReservedBytes => _boundedProvision?.ReservedBytes ?? 0;

    internal long CpuAssetBudgetBytes { get; }

    internal ImmutableArray<AssetPackageEntry> Entries { get; }

    internal ImmutableArray<byte> ExpectedPackageSha256 { get; }

    internal bool IsBoundedOpening => _boundedProvision is not null;

    internal int MaximumConcurrentDecodeBytes { get; }

    internal int MaximumDecodedAssetBytes { get; }

    internal int MaximumLiveAssetLeases { get; }

    internal long PackageByteLength { get; }

    internal SpatialProfileId ProfileId { get; }

    internal EbinReadLimits ReadLimits => _readLimits;

    internal AssetStoreRegistration Registration
        => _registration
            ?? _boundedProvision?.Registration
            ?? throw new InvalidOperationException("The package backing has no installed codec registration.");

    internal SpatialSettings SpatialSettings { get; }

    private static EbinReadLimits CreateReadLimits(int maximumDecodedAssetBytes)
    {
        var maximumRecordBytes = Math.Max(64, maximumDecodedAssetBytes);
        var maximumUtf8Bytes = Math.Max(0, maximumDecodedAssetBytes);
        return new EbinReadLimits(
            maximumRecordBytes,
            64,
            64,
            1_048_576,
            maximumUtf8Bytes,
            1_048_576,
            1_048_576,
            1_048_576);
    }

    internal AssetBoundedFailureFact CloseBoundedNativeFile()
        => BoundedNativePort.CloseFile(BoundedNativeFile);

    internal AssetOwnedReadDescriptor DescribeOwnedRead<T>(
        AssetStore store,
        AssetId assetId,
        object profileOrigin)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(profileOrigin);

        if (!TryGetEntry(assetId, out var entry))
        {
            throw new KeyNotFoundException($"Asset {assetId} is absent from the selected content set.");
        }

        if (!Registration.TryGetCodec<T>(out _))
        {
            throw new NotSupportedException($"No asset codec is registered for {typeof(T)}.");
        }

        if (!Registration.TryGetProfiledCodecReceipt<T>(profileOrigin, out var codecReceipt))
        {
            throw new NotSupportedException($"No profiled asset codec is registered for {typeof(T)}.");
        }

        if (codecReceipt.RequiredPackageCodec is AssetPackagePayloadCodec requiredCodec
            && (entry.Codec != (byte)requiredCodec))
        {
            throw new NotSupportedException("The selected asset uses a package codec outside its read profile.");
        }

        return new AssetOwnedReadDescriptor(store, this, entry, codecReceipt);
    }

    internal void FailBoundedInitialization(AssetBoundedFailureFact primary)
    {
        var boundedProvision = _boundedProvision
            ?? throw new InvalidOperationException("The package backing has no bounded opening provision.");
        boundedProvision.FailInitialization(primary);
    }

    internal AssetBoundedFailureFact HashBoundedPackage(
        scoped Span<byte> destination,
        out AssetBoundedFailureFact hashProviderRelease)
        => BoundedNativePort.ComputeSha256(BoundedNativeFile, destination, out hashProviderRelease);

    internal AssetBoundedRefusal OpenBoundedNativeFile(
        out AssetBoundedFailureFact primary,
        out AssetBoundedFailureFact fileRelease)
    {
        var boundedProvision = BoundedProvision;
        return boundedProvision.NativePort.OpenFile(
            boundedProvision.PackagePath,
            BoundedNativeFile,
            out primary,
            out fileRelease);
    }

    internal AssetBoundedFailureFact ReadBoundedExactlyAt(
        ulong offset,
        scoped Span<byte> destination,
        AssetBoundedFailureStage failureStage = AssetBoundedFailureStage.PayloadRead)
        => BoundedNativePort.ReadExactly(BoundedNativeFile, offset, destination, failureStage);

    internal ValueTask ReadExactlyAtAsync(ulong offset, Memory<byte> destination, CancellationToken cancellationToken)
    {
        var stream = Volatile.Read(ref _stream);
        ObjectDisposedException.ThrowIf(stream is null, this);
        return AssetPackageReader.ReadExactlyAtAsync(stream, offset, destination, cancellationToken);
    }

    internal AssetBoundedFailureFact ReleaseBoundedNativeControls()
        => BoundedNativePort.ReleaseControls();

    internal void ReleaseOwner()
        => ReleaseReference(releaseOwner: true);

    internal void ReleaseReference()
        => ReleaseReference(releaseOwner: false);

    internal AssetBoundedPreclaimRelease ReleaseUnstartedBoundedPreclaim()
    {
        lock (_gate)
        {
            var boundedProvision = _boundedProvision
                ?? throw new InvalidOperationException("The package backing has no bounded opening provision.");

            if ((_stream is not null) || (_referenceCount != 1) || (_releaseState != 0))
            {
                throw new InvalidOperationException("The bounded package opening is not in its unstarted owner state.");
            }

            var release = boundedProvision.ReleaseUnstartedPreclaim();
            _boundedProvision = null;
            _registration = null;
            _referenceCount = 0;
            _releaseState = 2;
            _ownerReleased = true;
            return release;
        }
    }

    internal bool TryAcquireReference()
    {
        lock (_gate)
        {
            if ((_boundedProvision is not null) || (_releaseState is not 0) || (_referenceCount == 0))
            {
                return false;
            }

            if (_referenceCount == int.MaxValue)
            {
                throw new InvalidOperationException("The package backing has too many live references.");
            }

            _referenceCount++;
            return true;
        }
    }

    internal bool TryBindBoundedStaticMeshRegistration(AssetStoreRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        lock (_gate)
        {
            if ((_boundedProvision is not { } boundedProvision)
                || (_stream is not null)
                || (_releaseState != 0)
                || !boundedProvision.TryBindRegistration(registration))
            {
                return false;
            }

            _registration = registration;
            return true;
        }
    }

    internal bool TryGetEntry(AssetId assetId, out AssetPackageEntry entry)
    {
        var low = 0;
        var high = Entries.Length - 1;

        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var candidate = Entries[middle];
            var comparison = candidate.AssetId.Value.CompareTo(assetId.Value);

            if (comparison == 0)
            {
                entry = candidate;
                return true;
            }

            if (comparison < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        entry = default;
        return false;
    }

    private void ReleaseReference(bool releaseOwner)
    {
        FileStream streamToClose;

        lock (_gate)
        {
            if (_boundedProvision is not null)
            {
                throw new InvalidOperationException("The bounded package opening must end through its original Store delivery.");
            }

            if (releaseOwner)
            {
                if (_ownerReleased)
                {
                    if (_releaseState is 1)
                    {
                        throw new InvalidOperationException("The package backing owner release is still in progress.");
                    }

                    if (_releaseState is 3)
                    {
                        RethrowReleaseFailure();
                    }

                    return;
                }

                _ownerReleased = true;
            }

            if (_releaseState is 3)
            {
                RethrowReleaseFailure();
            }

            if (_releaseState is not 0)
            {
                throw new InvalidOperationException("The package backing release is already in progress or complete.");
            }

            if (_referenceCount <= 0)
            {
                throw new InvalidOperationException("The package backing reference count became negative.");
            }

            if (_referenceCount > 1)
            {
                _referenceCount--;
                return;
            }

            streamToClose = _stream
                ?? throw new InvalidOperationException("The final package backing reference has no retained stream.");
            _releaseState = 1;
        }

        try
        {
            streamToClose.Dispose();
        }
        catch (Exception failure)
        {
            lock (_gate)
            {
                _releaseFailure = failure;
                _releaseState = 3;
            }

            RetainFailedReleaseRoot();
            throw;
        }

        lock (_gate)
        {
            if ((_releaseState is not 1)
                || (_referenceCount != 1)
                || !ReferenceEquals(_stream, streamToClose))
            {
                throw new InvalidOperationException("The package backing changed during final stream release.");
            }

            _stream = null;
            _referenceCount = 0;
            _releaseState = 2;
        }
    }

    private void RetainFailedReleaseRoot()
    {
        lock (s_failedReleaseRootGate)
        {
            if (_isFailedReleaseRooted)
            {
                return;
            }

            _nextFailedRelease = s_failedReleaseRoot;
            s_failedReleaseRoot = this;
            _isFailedReleaseRooted = true;
        }
    }

    private void RethrowReleaseFailure()
    {
        var failure = _releaseFailure
            ?? throw new InvalidOperationException("The package backing has no recorded release failure.");
        ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
