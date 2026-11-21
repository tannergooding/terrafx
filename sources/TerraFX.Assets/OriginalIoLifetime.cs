// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Assets;

internal sealed class OriginalIoLifetime
{
    internal const int NativeStateNotAcquired = 0;
    internal const int NativeStateReleased = 2;
    internal const int TerminalStateLive = 0;
    internal const int TerminalStateReturned = 1;

    internal int _blockReleaseState;

    internal nint _event;

    internal int _eventCloseError;

    internal int _eventCloseState;

    internal OriginalPackageIo? _executionState;

    internal AssetPackageBacking? _generationHead;

    internal int _lifecycle;

    internal long _nativeGenerationInterests;

    internal nint _overlapped;

    internal nint _scratch;

    internal AssetStore.OriginalOperation? _serviceReturn;

    internal long _sourceClaim;

    ~OriginalIoLifetime()
    {
        _executionState?.PublishRetirement(this);
    }

    internal static void CompleteTerminalReturn(OriginalIoLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        var executionState = lifetime._executionState
            ?? throw new InvalidOperationException("The terminal execution state has already been detached.");
        var (issuer, sourceBytes) = executionState.DetachTerminalExecutionState(lifetime);
        issuer.ReleaseSource(sourceBytes);
    }
}
