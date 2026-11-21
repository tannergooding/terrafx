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
    private static Lock? s_failedReleaseRootGate;
    private static AssetStore.OriginalIssuer? s_originalAnchorIssuer;
    private static int s_originalAnchorState;
    internal int _linuxFileDescriptor = -1;
    internal int _nativeAcquisitionState;
    internal int _nativeApi;
    internal int _nativeCloseError;
    internal int _nativeCloseState;
    internal int _nativeFileDisposition;
    internal string? _originalReportedPath;
    internal nint _windowsHandle = -1;
    private AssetPackageBackingToken? _backingToken = new();
    private readonly AssetBoundedNativeFile? _boundedNativeFile;
    private AssetStoreBoundedProvision? _boundedProvision;
    private readonly Lock _gate = new();
    private bool _isFailedReleaseRooted;
    private AssetPackageBacking? _nextFailedRelease;
    private bool _ownerReleased;
    private EbinReadLimits? _readLimits;
    private AssetStoreRegistration? _registration;
    private Exception? _releaseFailure;
    private int _releaseState;
    private FileStream? _stream;
    private int _referenceCount = 1;
    private AssetPackageBacking? _nextOriginalGeneration;
    private readonly long _originalGenerationId;
    private AssetStore.OriginalIssuer? _originalIssuer;
    private long _originalKeepSourceBytes;
    private AssetStore.OriginalOperation? _originalOpening;
    private AssetStore.OriginalOperation? _originalRetirement;
    private int _originalSelection;
    private int _originalSourceInterests;

    internal AssetPackageBacking(
        AssetStoreOptions options,
        AssetStore.OriginalIssuer issuer,
        long generationId,
        AssetStore.OriginalOperation opening,
        AssetStore.OriginalOperation retirement)
    {
        _boundedNativeFile = null;
        Entries = [];
        CpuAssetBudgetBytes = options.CpuAssetBudgetBytes;
        ExpectedPackageSha256 = options.ExpectedPackageSha256;
        MaximumConcurrentDecodeBytes = options.MaximumConcurrentDecodeBytes;
        MaximumDecodedAssetBytes = options.MaximumDecodedAssetBytes;
        MaximumLiveAssetLeases = options.MaximumLiveAssetLeases;
        _registration = options.Registration;
        _readLimits = CreateReadLimits(options.MaximumDecodedAssetBytes);
        _originalIssuer = issuer;
        _originalGenerationId = generationId;
        _originalKeepSourceBytes = AssetStore.GetOriginalKeepSourceBytes(AssetStore.OriginalMaximumEntryCount);
        _originalOpening = opening;
        _originalRetirement = retirement;
        retirement.InstallBacking(this);
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

    internal AssetPackageBackingToken BackingToken
    {
        get
        {
            lock (_gate)
            {
                var token = _backingToken
                    ?? throw new InvalidOperationException("The package token source ownership has ended.");
                _originalSourceInterests |= 2;
                return token;
            }
        }
    }

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

    internal ImmutableArray<AssetPackageEntry> Entries { get; private set; }

    internal ImmutableArray<byte> ExpectedPackageSha256 { get; }

    internal bool IsBoundedOpening => _boundedProvision is not null;

    internal int MaximumConcurrentDecodeBytes { get; }

    internal int MaximumDecodedAssetBytes { get; }

    internal int MaximumLiveAssetLeases { get; }

    internal AssetPackageBacking? NextOriginalGeneration
    {
        get
        {
            return _nextOriginalGeneration;
        }

        set
        {
            _nextOriginalGeneration = value;
        }
    }

    internal long OriginalFailedKeepSourceBytes
        => AssetStore.GetOriginalKeepSourceBytes((_releaseState == 2) ? 0 : Entries.Length);

    internal long OriginalKeepSourceBytes => _originalKeepSourceBytes;

    internal Exception? OriginalUnreturnedReleaseFailure
    {
        get
        {
            lock (_gate)
            {
                return (_releaseState == 3) ? _releaseFailure : null;
            }
        }
    }

    internal long PackageByteLength { get; private set; }

    internal SpatialProfileId ProfileId { get; private set; }

    internal EbinReadLimits ReadLimits
        => _readLimits
            ?? throw new InvalidOperationException("The package read-limit source ownership has ended.");

    internal AssetStoreRegistration Registration
        => _registration
            ?? _boundedProvision?.Registration
            ?? throw new InvalidOperationException("The package backing has no installed codec registration.");

    internal SpatialSettings SpatialSettings { get; private set; }

    internal static void CancelOriginalAnchorElection(bool ownsAnchor)
    {
        if (ownsAnchor && (Interlocked.CompareExchange(ref s_originalAnchorState, 0, 1) != 1))
        {
            throw new InvalidOperationException("The original acquisition anchor election changed.");
        }
    }

    internal static void FailOriginalAnchorInstallation(AssetStore.OriginalIssuer? issuer, bool ownsAnchor)
    {
        if (ownsAnchor && (Volatile.Read(ref s_originalAnchorState) != 2))
        {
            s_originalAnchorIssuer = issuer;
            Volatile.Write(ref s_originalAnchorState, 3);
        }
    }

    internal static void InstallOriginalAnchor(AssetStore.OriginalIssuer issuer, bool ownsAnchor)
    {
        if (ownsAnchor)
        {
            s_originalAnchorIssuer = issuer;
            s_failedReleaseRootGate = new Lock();
            Volatile.Write(ref s_originalAnchorState, 2);
        }
        else if ((Volatile.Read(ref s_originalAnchorState) != 2) || (s_originalAnchorIssuer is null))
        {
            throw new InvalidOperationException("The original acquisition anchor is unavailable.");
        }
    }

    internal static bool TryElectOriginalAnchor()
    {
        var spinner = new SpinWait();

        while (true)
        {
            var state = Volatile.Read(ref s_originalAnchorState);

            if (state == 0)
            {
                if (Interlocked.CompareExchange(ref s_originalAnchorState, 1, 0) == 0)
                {
                    return true;
                }

                continue;
            }

            if (state == 2)
            {
                return false;
            }

            if (state != 1)
            {
                throw new InvalidOperationException("The original acquisition anchor is unavailable.");
            }

            spinner.SpinOnce();
        }
    }

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

    internal void CompleteOriginalOpening(long keepSourceBytes, bool retainFailure)
    {
        lock (_gate)
        {
            if ((_originalIssuer is null) || (_originalSelection != 0) || (keepSourceBytes > _originalKeepSourceBytes))
            {
                throw new InvalidOperationException("The original generation opening settlement is inconsistent.");
            }

            _originalKeepSourceBytes = keepSourceBytes;
            _originalSelection = retainFailure ? 4 : 1;

            if (retainFailure && (_releaseState == 2))
            {
                Entries = [];
            }

            if (!retainFailure)
            {
                _originalOpening = null;
            }
        }

        if (retainFailure)
        {
            RetainFailedReleaseRoot();
        }
    }

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

    internal void DisplaceOriginalGeneration()
    {
        if (Interlocked.CompareExchange(ref _originalSelection, 6, 5) != 5)
        {
            throw new InvalidOperationException("The original generation is not the selected backing.");
        }
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

    internal void InstallOriginalMetadata(
        ImmutableArray<AssetPackageEntry> entries,
        SpatialProfileId profileId,
        SpatialSettings spatialSettings,
        long packageByteLength)
    {
        lock (_gate)
        {
            if ((_originalSelection != 0) || (_stream is null) || (_originalGenerationId <= 0))
            {
                throw new InvalidOperationException("The original generation has no acquired opening stream.");
            }

            Entries = entries;
            ProfileId = profileId;
            SpatialSettings = spatialSettings;
            PackageByteLength = packageByteLength;
        }
    }

    internal void InstallOriginalStream(FileStream stream)
    {
        lock (_gate)
        {
            if ((_originalIssuer is null) || (_originalOpening is null) || (_stream is not null))
            {
                throw new InvalidOperationException("The original generation acquisition root is inconsistent.");
            }

            _stream = stream;
            _originalRetirement?.ObserveStreamAcquired();
        }
    }

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

    internal void RecordOriginalOpeningFailure(Exception failure, bool releaseReturned)
    {
        lock (_gate)
        {
            _releaseFailure = failure;
            _releaseState = releaseReturned ? 2 : 3;
            _ownerReleased = true;

            if (releaseReturned)
            {
                _stream = null;
                _referenceCount = 0;
            }
        }
    }

    internal AssetBoundedFailureFact ReleaseBoundedNativeControls()
        => BoundedNativePort.ReleaseControls();

    internal void ReleaseOwner()
        => ReleaseReference(releaseOwner: true, out _);

    internal void ReleaseOwner(out bool sourceReturn)
        => ReleaseReference(releaseOwner: true, out sourceReturn);

    internal void ReleaseReference()
        => ReleaseReference(releaseOwner: false, out _);

    internal void ReleaseReference(out bool sourceReturn)
        => ReleaseReference(releaseOwner: false, out sourceReturn);

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

    internal void ReleaseUnstartedOriginalOpening()
    {
        lock (_gate)
        {
            if ((_originalSelection != 0) || (_stream is not null) || (_referenceCount != 1))
            {
                throw new InvalidOperationException("The original package opening has already acquired a file.");
            }

            _ownerReleased = true;
            _referenceCount = 0;
            _releaseState = 2;
            _originalSourceInterests |= 1;
        }
    }

    internal void RetainOriginalReadLimits()
    {
        lock (_gate)
        {
            _originalSourceInterests |= 4;
        }
    }

    internal long ReturnOriginalSource()
    {
        lock (_gate)
        {
            if ((_originalIssuer is null) || (_releaseState != 2) || ((_originalSourceInterests & 1) == 0))
            {
                throw new InvalidOperationException("The original generation has no returned final source release.");
            }

            _originalSourceInterests &= ~1;
            Entries = [];
            _registration = null;
            _releaseFailure = null;
            var retainedToken = (_originalSourceInterests & 2) != 0;
            var retainedLimits = (_originalSourceInterests & 4) != 0;

            if (!retainedToken)
            {
                _backingToken = null;
            }

            if (!retainedLimits)
            {
                _readLimits = null;
            }

            if (retainedToken || retainedLimits)
            {
                // Descriptors and custom codec contexts have no handback yet.
                // Retain their actual source carriers, not the entry array.
                _originalSelection = 3;
                _originalKeepSourceBytes = checked(
                    AssetStore.OriginalBackingSourceBytes + AssetStore.OriginalOperationSourceBytes
                    + (retainedToken ? AssetStore.OriginalTokenSourceBytes : 0)
                    + (retainedLimits ? AssetStore.OriginalReadLimitsSourceBytes : 0));
            }
            else
            {
                _originalRetirement?.ObserveSourceReturn(retainFailure: false);
                _originalRetirement = null;
                _originalIssuer = null;
                _originalSelection = 2;
                _originalKeepSourceBytes = 0;
            }

            return _originalKeepSourceBytes;
        }
    }

    internal void SelectOriginalGeneration()
    {
        if (Interlocked.CompareExchange(ref _originalSelection, 5, 1) != 1)
        {
            throw new InvalidOperationException("The original generation opening has not returned.");
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

    private void ReleaseReference(bool releaseOwner, out bool sourceReturn)
    {
        sourceReturn = false;
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
                _originalRetirement?.RecordFailure(failure, cleanup: true);
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
            _originalRetirement?.ObserveFileReleaseReturn();
            _originalSourceInterests |= 1;
            sourceReturn = _originalIssuer is not null;
        }
    }

    private void RetainFailedReleaseRoot()
    {
        var anchorGate = s_failedReleaseRootGate
            ?? throw new InvalidOperationException("The package failure has no funded original acquisition anchor.");

        lock (anchorGate)
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
