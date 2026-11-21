// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Threading;
using System.Threading.Tasks.Sources;

namespace TerraFX.Assets;

internal sealed class OriginalIoRequest
{
    internal AssetPackageBacking? _backing;

    internal int _cancellationError;

    internal CancellationToken _cancellationToken;

    internal ManualResetValueTaskSourceCore<int> _completion;

    internal int _consumptionUse;

    internal byte[]? _destination;

    internal int _destinationOffset;

    internal int _error;

    internal long _fileOffset;

    internal AssetStore.OriginalIssuer? _issuer;

    internal int _kind;

    internal int _length;

    internal int _lifecycle;

    internal long _nativeAttemptSequence;

    internal int _nativeUse;

    internal AssetStore.OriginalOperation? _operation;

    internal int _publicationUse;

    internal OriginalIoRequest? _queueNext;

    internal int _registrationUse;

    internal long _requestId;

    internal int _result;

    internal OriginalIoRequest? _returnNext;

    internal int _returnUse;

    internal long _sourceClaim;

    internal int _waitError;

    internal OriginalPackageIo? _worker;
}
