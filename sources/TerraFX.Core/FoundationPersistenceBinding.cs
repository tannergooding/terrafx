// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Numerics;

namespace TerraFX;

internal sealed class FoundationPersistenceBinding
{
    private readonly Func<Vector2, Vector2, bool>? _extentPolicy2;
    private readonly Func<Vector3, Vector3, bool>? _extentPolicy3;

    internal FoundationPersistenceBinding(Guid worldGuid, FoundationProfileBinding profile, uint ownerId, long bindingRevision, Func<Vector2, Vector2, bool>? extentPolicy2 = null, Func<Vector3, Vector3, bool>? extentPolicy3 = null)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (worldGuid == default)
        {
            ThrowHelper.ThrowArgumentException("Persistence requires the enclosing stable world GUID.", nameof(worldGuid));
        }

        ArgumentOutOfRangeException.ThrowIfZero(ownerId);
        ArgumentOutOfRangeException.ThrowIfNegative(bindingRevision);
        WorldGuid = worldGuid;
        Profile = profile;
        OwnerId = ownerId;
        BindingRevision = bindingRevision;
        _extentPolicy2 = extentPolicy2;
        _extentPolicy3 = extentPolicy3;
    }

    internal long BindingRevision { get; }

    internal uint OwnerId { get; }

    internal FoundationProfileBinding Profile { get; }

    internal Guid WorldGuid { get; }

    internal bool Admits(Vector2 minimum, Vector2 maximum) => _extentPolicy2?.Invoke(minimum, maximum) ?? true;
    internal bool Admits(Vector3 minimum, Vector3 maximum) => _extentPolicy3?.Invoke(minimum, maximum) ?? true;

    internal void ValidateCapture(uint frameOwnerId, uint currentOwnerId, long currentBindingRevision)
    {
        if (frameOwnerId != currentOwnerId || OwnerId != currentOwnerId)
        {
            ThrowHelper.ThrowInvalidOperationException("The captured frame belongs to a foreign runtime owner.");
        }

        if (BindingRevision != currentBindingRevision)
        {
            ThrowHelper.ThrowInvalidOperationException("The captured persistence binding is stale.");
        }
    }
}
