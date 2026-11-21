// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace TerraFX.Assets;

public sealed partial class AssetStore
{
    // Q aligns each declared field slot, not the packed CLR shell. O includes two
    // source messages; framework shells and their resource interests are not bytes.
    internal const long OriginalAnchorSourceBytes = 32;
    internal const long OriginalBackingSourceBytes = 328;
    internal const long OriginalIssuerSourceBytes = 64;
    internal const int OriginalMaximumEntryCount = 131_072;
    internal const long OriginalOpeningHolderSourceBytes = 88;
    internal const long OriginalOpeningSourceBytes =
        OriginalOperationSourceBytes + OriginalOpeningHolderSourceBytes
        + 88 + 65_560 + 4_194_392 + 40 + 56 + OriginalProfileSourceBytes;
    internal const long OriginalOperationSourceBytes = OriginalPackageIoSourceCapacity.OperationBytes;
    internal const long OriginalProfileSourceBytes =
        88 + 48 + (3 * 56) + 32 + (64 * 56) + (3 * 536) + 65_536 + (64 * (24 + 7)) + 56;
    internal const long OriginalReadLimitsSourceBytes = 80;
    internal const long OriginalServiceSourceBytes = OriginalPackageIoSourceCapacity.ServiceBytes;
    internal const long OriginalStoreSourceBytes = 152;
    internal const long OriginalTokenSourceBytes = 24;
    private AssetPackageBacking? _generationHead;

    private OriginalOperation? _ioControl;
    private OriginalOperation? _operationHead;
    private readonly OriginalIssuer? _originalIssuer;

    internal long OriginalSourceCapacity
        => _originalIssuer?.SourceCapacity
            ?? throw new InvalidOperationException("The Store has no ordinary original source issuer.");

    internal static long GetOriginalKeepSourceBytes(int entryCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(entryCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(entryCount, OriginalMaximumEntryCount);
        return checked(
            OriginalBackingSourceBytes + OriginalReadLimitsSourceBytes + OriginalTokenSourceBytes
            + OriginalOperationSourceBytes + 24 + ((long)entryCount * 32));
    }

    internal static AssetStore PreclaimOriginalOpening(
        AssetStoreOptions options,
        out OriginalOperation opening)
    {
        if (!OperatingSystem.IsWindows()
            || (RuntimeInformation.ProcessArchitecture is not Architecture.X64)
            || !StringComparer.Ordinal.Equals(
                RuntimeInformation.FrameworkDescription,
                ".NET 11.0.0-rc.3.26522.148"))
        {
            throw new InvalidOperationException("The ordinary Assets source-capacity profile is unavailable.");
        }

        var ownsAnchor = AssetPackageBacking.TryElectOriginalAnchor();
        var sourceBytes = checked(
            OriginalServiceSourceBytes + GetOriginalKeepSourceBytes(OriginalMaximumEntryCount)
            + OriginalOpeningSourceBytes + (ownsAnchor ? OriginalAnchorSourceBytes : 0));

        if (sourceBytes > options.CpuAssetBudgetBytes)
        {
            AssetPackageBacking.CancelOriginalAnchorElection(ownsAnchor);
            throw new InvalidOperationException("The CPU asset budget cannot fund package opening.");
        }

        OriginalIssuer? issuer = null;

        try
        {
            issuer = new OriginalIssuer(options.CpuAssetBudgetBytes, sourceBytes);
            AssetPackageBacking.InstallOriginalAnchor(issuer, ownsAnchor);
            var store = new AssetStore(issuer);
            opening = store.MaterializeOriginalOpening(options, issuer.IssueGenerationId(), issuer.IssueOperationId());
            return store;
        }
        catch
        {
            // An installed anchor keeps its first paying issuer. An unreturned
            // framework construction prefix is not a native consumption receipt.
            AssetPackageBacking.FailOriginalAnchorInstallation(issuer, ownsAnchor);
            throw;
        }
    }

    internal void ReleaseOriginalBackingReference(AssetPackageBacking backing)
    {
        backing.ReleaseReference(out var sourceReturn);

        if (sourceReturn)
        {
            ReturnOriginalBackingSource(backing);
        }
    }

    internal void ReleaseUnstartedOriginalOpening(OriginalOperation opening)
    {
        lock (_gate)
        {
            var backing = opening.Backing;
            backing.ReleaseUnstartedOriginalOpening();
            var returnedOpening = opening;
            CompleteOriginalOpeningSource(ref returnedOpening, succeeded: false, retainFailure: false);
            ReturnOriginalBackingSource(backing);
        }
    }

    internal void ReturnOriginalBackingSource(AssetPackageBacking backing)
    {
        lock (_gate)
        {
            var issuer = _originalIssuer
                ?? throw new InvalidOperationException("The Store has no ordinary original source issuer.");
            var previousClaim = backing.OriginalKeepSourceBytes;
            var remainingClaim = backing.ReturnOriginalSource();

            if (ReferenceEquals(_currentBacking, backing))
            {
                _currentBacking = null;
            }

            if (ReferenceEquals(_pendingReleaseBacking, backing))
            {
                _pendingReleaseBacking = null;
            }

            if (remainingClaim == 0)
            {
                UnlinkOriginalGeneration(backing);
            }

            issuer.ReleaseSource(checked(previousClaim - remainingClaim));
        }
    }

    private void AbandonOriginalStoreSource()
    {
        var issuer = _originalIssuer
            ?? throw new InvalidOperationException("The Store has no ordinary original source issuer.");
        issuer.CloseAdmission();
        EndOriginalServiceProvision();
        issuer.ReleaseSource(OriginalStoreSourceBytes);
        _generationHead = null;
        _operationHead = null;
    }

    private void CompleteOriginalOpeningSource(
        ref OriginalOperation? opening,
        bool succeeded,
        bool retainFailure)
    {
        var returnedOpening = opening
            ?? throw new InvalidOperationException("The original opening source has already been handed back.");
        opening = null;
        var issuer = _originalIssuer
            ?? throw new InvalidOperationException("The Store has no ordinary original source issuer.");
        var backing = returnedOpening.Backing;
        var previousKeep = backing.OriginalKeepSourceBytes;
        var remainingKeep = succeeded
            ? GetOriginalKeepSourceBytes(backing.Entries.Length)
            : backing.OriginalFailedKeepSourceBytes;
        backing.CompleteOriginalOpening(remainingKeep, retainFailure);
        returnedOpening.ObserveSourceReturn(retainFailure);
        issuer.ReleaseSource(checked(previousKeep - remainingKeep));

        if (!retainFailure)
        {
            UnlinkOriginalOperation(returnedOpening);
        }

        issuer.ReleaseSource(checked(
            OriginalOpeningSourceBytes - (retainFailure ? OriginalOperationSourceBytes : 0)));
    }

    private void EndOriginalServiceProvision()
    {
        if (_originalIssuer is { } issuer)
        {
            var serviceReturn = _ioControl
                ?? throw new InvalidOperationException("The original service has no return owner.");
            var lifetime = serviceReturn.IoLifetime
                ?? throw new InvalidOperationException("The original service lifetime is unavailable.");

            if (!ReferenceEquals(serviceReturn.Issuer, issuer))
            {
                throw new InvalidOperationException("The original service issuer changed before handback.");
            }

            var (returnIssuer, sourceBytes) = lifetime.DetachUnstartedService();

            serviceReturn.ObserveSourceReturn(retainFailure: false);
            _ioControl = null;
            returnIssuer.ReleaseSource(sourceBytes);
        }
    }

    private OriginalOperation MaterializeOriginalOpening(
        AssetStoreOptions options,
        long generationId,
        long operationId)
    {
        var issuer = _originalIssuer
            ?? throw new InvalidOperationException("The Store has no ordinary original source issuer.");
        var opening = new OriginalOperation(issuer, operationId, OriginalOpeningSourceBytes) {
            Next = _operationHead
        };
        _operationHead = opening;
        var retirement = new OriginalOperation(issuer, issuer.IssueOperationId(), OriginalOperationSourceBytes);
        var backing = new AssetPackageBacking(options, issuer, generationId, opening, retirement) {
            NextOriginalGeneration = _generationHead
        };
        _generationHead = backing;
        opening.InstallBacking(backing);
        opening.InstallStorage(new OriginalOpeningStorage(options));
        return opening;
    }

    private OriginalOperation PreclaimOriginalCandidate(AssetStoreOptions options)
    {
        var issuer = _originalIssuer
            ?? throw new InvalidOperationException("The Store has no ordinary original source issuer.");

        if (options.CpuAssetBudgetBytes == 0)
        {
            throw new InvalidOperationException("The CPU asset budget cannot fund package opening.");
        }

        var sourceBytes = checked(
            GetOriginalKeepSourceBytes(OriginalMaximumEntryCount) + OriginalOpeningSourceBytes);
        issuer.ClaimSource(sourceBytes, _liveAssetBytes);

        // Constructed prefixes remain in the charged intrusive roots if a
        // constructor fails; they do not receive a completion-based refund.
        return MaterializeOriginalOpening(options, issuer.IssueGenerationId(), issuer.IssueOperationId());
    }

    private void ReleaseOriginalBackingOwner(AssetPackageBacking backing)
    {
        backing.ReleaseOwner(out var sourceReturn);

        if (sourceReturn)
        {
            ReturnOriginalBackingSource(backing);
        }
    }

    private void UnlinkOriginalGeneration(AssetPackageBacking backing)
    {
        AssetPackageBacking? previous = null;
        var current = _generationHead;

        while (!ReferenceEquals(current, backing))
        {
            if (current is null)
            {
                throw new InvalidOperationException("The original generation membership is inconsistent.");
            }

            previous = current;
            current = current.NextOriginalGeneration;
        }

        if (previous is null)
        {
            _generationHead = backing.NextOriginalGeneration;
        }
        else
        {
            previous.NextOriginalGeneration = backing.NextOriginalGeneration;
        }

        backing.NextOriginalGeneration = null;
    }

    private void UnlinkOriginalOperation(OriginalOperation operation)
    {
        OriginalOperation? previous = null;
        var current = _operationHead;

        while (!ReferenceEquals(current, operation))
        {
            if (current is null)
            {
                throw new InvalidOperationException("The original operation membership is inconsistent.");
            }

            previous = current;
            current = current.Next;
        }

        if (previous is null)
        {
            _operationHead = operation.Next;
        }
        else
        {
            previous.Next = operation.Next;
        }

        operation.Next = null;
    }

    internal sealed class OriginalIssuer
    {
        private readonly Lock _gate = new();
        private readonly long _budget;
        private long _nextGeneration;
        private long _nextOperation;
        private long _sourceCapacity;
        private int _state;

        internal OriginalIssuer(long budget, long sourceCapacity)
        {
            _budget = budget;
            _sourceCapacity = sourceCapacity;
        }

        internal long Budget => _budget;

        internal long SourceCapacity
        {
            get
            {
                lock (_gate)
                {
                    return _sourceCapacity;
                }
            }
        }

        internal void ClaimSource(long bytes, long liveWireBytes)
        {
            lock (_gate)
            {
                if ((_nextGeneration == long.MaxValue) || (_nextOperation > long.MaxValue - 2))
                {
                    throw new InvalidOperationException("The original generation or operation issuer is exhausted.");
                }

                if ((_state != 0)
                    || (bytes > _budget)
                    || (liveWireBytes > _budget - bytes)
                    || (_sourceCapacity > _budget - bytes - liveWireBytes))
                {
                    throw new InvalidOperationException("The original CPU asset source-capacity budget is exhausted.");
                }

                _sourceCapacity = checked(_sourceCapacity + bytes);
            }
        }

        internal void CloseAdmission()
        {
            lock (_gate)
            {
                _state = 1;
            }
        }

        internal long IssueGenerationId()
        {
            lock (_gate)
            {
                if ((_state != 0) || (_nextGeneration == long.MaxValue))
                {
                    throw new InvalidOperationException("The original generation issuer is exhausted.");
                }

                _nextGeneration = checked(_nextGeneration + 1);
                return _nextGeneration;
            }
        }

        internal long IssueOperationId()
        {
            lock (_gate)
            {
                if ((_state != 0) || (_nextOperation == long.MaxValue))
                {
                    throw new InvalidOperationException("The original operation issuer is exhausted.");
                }

                _nextOperation = checked(_nextOperation + 1);
                return _nextOperation;
            }
        }

        internal void ReleaseSource(long bytes)
        {
            lock (_gate)
            {
                if ((bytes < 0) || (bytes > _sourceCapacity))
                {
                    throw new InvalidOperationException("The original source-capacity accounting is inconsistent.");
                }

                _sourceCapacity = checked(_sourceCapacity - bytes);
            }
        }
    }

    internal sealed class OriginalOpeningStorage
    {
        private byte[]? _actualDigest;
        private byte[]? _directory;
        private ImmutableArray<AssetPackageEntry> _entries;
        private ImmutableArray<AssetPackageEntry>.Builder? _entryBuilder;
        private byte[]? _header;
        private AssetStoreOptions? _options;
        private byte[]? _padding;
        private byte[]? _profile;
        private FileStream? _stream;

        internal OriginalOpeningStorage(AssetStoreOptions options)
        {
            _options = options;
        }

        internal byte[] ActualDigest
        {
            get
            {
                return _actualDigest ?? throw new InvalidOperationException("The opening digest is unavailable.");
            }

            set
            {
                _actualDigest = value;
            }
        }

        internal byte[] Directory
        {
            get
            {
                return _directory ?? throw new InvalidOperationException("The opening directory is unavailable.");
            }

            set
            {
                _directory = value;
            }
        }

        internal ImmutableArray<AssetPackageEntry>.Builder EntryBuilder
        {
            get
            {
                return _entryBuilder ?? throw new InvalidOperationException("The opening entry builder is unavailable.");
            }

            set
            {
                _entryBuilder = value;
            }
        }

        internal byte[] Header
        {
            get
            {
                return _header ?? throw new InvalidOperationException("The opening header is unavailable.");
            }

            set
            {
                _header = value;
            }
        }

        internal AssetStoreOptions Options
            => _options ?? throw new InvalidOperationException("The opening inputs are unavailable.");

        internal byte[] Padding => _padding ??= new byte[15];

        internal byte[] Profile
        {
            get
            {
                return _profile ?? throw new InvalidOperationException("The opening profile is unavailable.");
            }

            set
            {
                _profile = value;
            }
        }

        internal FileStream? Stream
        {
            get
            {
                return _stream;
            }

            set
            {
                _stream = value;
            }
        }

        internal ImmutableArray<AssetPackageEntry> AdoptEntries()
        {
            _entries = EntryBuilder.MoveToImmutable();
            _entryBuilder = null;
            return _entries;
        }

        internal void Clear()
        {
            _header = null;
            _profile = null;
            _directory = null;
            _padding = null;
            _actualDigest = null;
            _entryBuilder = null;
            _entries = default;
            _stream = null;
            _options = null;
        }
    }

    internal sealed class OriginalOperation
    {
        private AssetPackageBacking? _backing;
        private OriginalOpeningStorage? _inputOwner;
        private OriginalIoLifetime? _ioLifetime;
        private readonly OriginalIssuer _issuer;
        private OriginalOperation? _next;
        private Task? _response;
        private readonly SynchronizationContext? _context = SynchronizationContext.Current;
        private long _additionalFailures;
        private int _caller;
        private long _claim;
        private Exception? _cleanup;
        private int _fileInterest;
        private readonly long _operationId;
        private Exception? _primary;
        private int _returnInterest;
        private int _state;
        private CancellationToken _token;

        internal OriginalOperation(OriginalIssuer issuer, long operationId, long claim)
        {
            _issuer = issuer;
            _operationId = operationId;
            _claim = claim;
        }

        internal AssetPackageBacking Backing
            => _backing
                ?? throw new InvalidOperationException("The original opening has no installed generation.");

        internal bool FileAcquisitionAttempted => _fileInterest != 0;

        internal OriginalIoLifetime? IoLifetime => _ioLifetime;

        internal OriginalIssuer Issuer => _issuer;

        internal OriginalOperation? Next
        {
            get
            {
                return _next;
            }

            set
            {
                _next = value;
            }
        }

        internal OriginalOpeningStorage Storage
            => _inputOwner
                ?? throw new InvalidOperationException("The original opening has no installed storage owner.");

        internal void BeginAcquisition(CancellationToken cancellationToken)
        {
            if ((_state != 0) || (_fileInterest != 0) || (_operationId <= 0) || (_issuer.Budget < _claim))
            {
                throw new InvalidOperationException("The original opening is not in its unstarted state.");
            }

            _token = cancellationToken;
            _fileInterest = 1;
            _returnInterest = 1;
            _state = 1;
        }

        internal void InstallBacking(AssetPackageBacking backing)
            => _backing = backing;

        internal void InstallIoLifetime(OriginalIoLifetime lifetime)
        {
            ArgumentNullException.ThrowIfNull(lifetime);

            if (!ReferenceEquals(lifetime._serviceReturn, this) || (_ioLifetime is not null))
            {
                throw new InvalidOperationException("The original service lifetime installation is inconsistent.");
            }

            _ioLifetime = lifetime;
        }

        internal void InstallResponse(Task response)
            => _response = response;

        internal void InstallStorage(OriginalOpeningStorage storage)
            => _inputOwner = storage;

        internal void ObserveFileReleaseReturn()
            => _fileInterest = 3;

        internal void ObserveSourceReturn(bool retainFailure)
        {
            if ((_caller != 0) || (_state is not (0 or 1)) || ((_state == 1) && (_returnInterest != 1)))
            {
                throw new InvalidOperationException("The original opening source handback is inconsistent.");
            }

            _inputOwner?.Clear();
            _inputOwner = null;
            _response = null;
            _token = default;
            _ioLifetime = null;
            _caller = 1;
            _returnInterest = 0;
            _state = retainFailure ? 3 : 2;
            _claim = retainFailure ? OriginalOperationSourceBytes : 0;

            if (!retainFailure)
            {
                _primary = null;
                _cleanup = null;
                _backing = null;
            }

            GC.KeepAlive(_context);
        }

        internal void ObserveStreamAcquired()
            => _fileInterest = 2;

        internal void RecordFailure(Exception failure, bool cleanup)
        {
            if (cleanup && (_cleanup is null))
            {
                _cleanup = failure;
            }
            else if (!cleanup && (_primary is null))
            {
                _primary = failure;
            }
            else
            {
                _additionalFailures = checked(_additionalFailures + 1);
            }
        }
    }
}
