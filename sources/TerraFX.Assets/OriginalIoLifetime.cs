// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Assets;

internal sealed class OriginalIoLifetime
{
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
}
