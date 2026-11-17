// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;
using TerraFX.Assets;
using TerraFX.Entities;

namespace TerraFX.Animation;

internal sealed class AnimationRestoreAssetSourceLease : IDisposable
{
    private bool _isDisposed;
    private readonly Lock _gate = new();
    private readonly IAnimationRestoreAssetSourceOwner _owner;
    private readonly AssetPackageLease _packageLease;
    private readonly WorldAttachmentSource<AnimationRuntimeAuthor> _playbackState;
    private readonly WorldResourceSource<RootMotionResourceAuthor> _rootMotionRequests;
    private readonly WorldRestoreSourceControl.SourceUse _sourceUse;

    internal AnimationRestoreAssetSourceLease(
        IAnimationRestoreAssetSourceOwner owner,
        WorldAttachmentSource<AnimationRuntimeAuthor> playbackState,
        WorldResourceSource<RootMotionResourceAuthor> rootMotionRequests,
        AssetPackageLease packageLease,
        IDisposable sourceUse)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(playbackState);
        ArgumentNullException.ThrowIfNull(rootMotionRequests);
        ArgumentNullException.ThrowIfNull(packageLease);
        ArgumentNullException.ThrowIfNull(sourceUse);
        _sourceUse = sourceUse as WorldRestoreSourceControl.SourceUse
            ?? throw new ArgumentException("Animation asset preparation requires an actual prepared-load source use.", nameof(sourceUse));

        if (!ReferenceEquals(_sourceUse.Control.Owner, owner)
            || !playbackState.IsFrom(owner) || !rootMotionRequests.IsFrom(owner))
        {
            throw new ArgumentException("Animation asset preparation requires both typed caches from its exact source owner.", nameof(owner));
        }

        _owner = owner;
        _packageLease = packageLease;
        _playbackState = playbackState;
        _rootMotionRequests = rootMotionRequests;
    }

    internal IAnimationRestoreAssetSourceOwner Owner
    {
        get
        {
            ThrowIfFailed();
            return _owner;
        }
    }

    internal AssetPackageLease PackageLease
    {
        get
        {
            ThrowIfFailed();
            return _packageLease;
        }
    }

    internal WorldAttachmentSource<AnimationRuntimeAuthor> PlaybackState
    {
        get
        {
            ThrowIfFailed();
            return _playbackState;
        }
    }

    internal WorldResourceSource<RootMotionResourceAuthor> RootMotionRequests
    {
        get
        {
            ThrowIfFailed();
            return _rootMotionRequests;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            try
            {
                _playbackState.Dispose();
            }
            finally
            {
                try
                {
                    _rootMotionRequests.Dispose();
                }
                finally
                {
                    _sourceUse.Dispose();
                }
            }

            _isDisposed = true;
        }
    }

    internal void ThrowIfFailed()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            _sourceUse.Control.ThrowIfFailed();
        }
    }
}
