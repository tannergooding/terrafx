// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX.Assets;

internal sealed class OriginalPackageIo
{
    internal OriginalIoRequest? _activeRequest;

    internal AssetStore.OriginalIssuer? _issuer;

    internal int _joinState;

    internal int _loopReturnObservation;

    internal int _nativeUse;

    internal long _nextRequestSequence;

    internal int _publicationUse;

    internal int _queueAttemptState;

    internal Lock? _queueGate;

    internal OriginalIoRequest? _queueHead;

    internal OriginalIoRequest? _queueTail;

    internal int _queuedCount;

    internal Action<object?>? _retirementCallback;

    internal OriginalIoLifetime? _retiringOwner;

    internal OriginalIoRequest? _returningHead;

    internal int _startPermit;

    internal int _stopRequest;

    internal Thread? _thread;

    internal bool IsStopRequested
        => Volatile.Read(ref _stopRequest) is not 0;

    internal OriginalIoLifetime? RetiringOwner
        => Volatile.Read(ref _retiringOwner);

    internal void PublishRetirement(OriginalIoLifetime lifetime)
    {
        var current = Interlocked.CompareExchange(ref _retiringOwner, lifetime, null);

        if ((current is null) || ReferenceEquals(current, lifetime))
        {
            Volatile.Write(ref _stopRequest, 1);
        }
    }
}
