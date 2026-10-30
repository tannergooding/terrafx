// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

internal sealed class WorldSystemCleanupFailureControl
{
    private bool _isSealed;
    private Exception? _originalException;

    internal Guid FactoryId { get; private set; }

    internal int FactoryVersion { get; private set; }

    internal Exception OriginalException
        => _originalException ?? throw new InvalidOperationException("The prepared cleanup failure has not been sealed.");

    internal int ReleaseAttemptIndex { get; private set; }

    internal Guid SystemId { get; private set; }

    internal void Seal(
        Guid systemId,
        Guid factoryId,
        int factoryVersion,
        int releaseAttemptIndex,
        Exception originalException)
    {
        if (_isSealed)
        {
            throw new InvalidOperationException("A system cleanup failure cannot be sealed more than once.");
        }

        ArgumentOutOfRangeException.ThrowIfEqual(systemId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(factoryId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(factoryVersion);
        ArgumentOutOfRangeException.ThrowIfNegative(releaseAttemptIndex);
        ArgumentNullException.ThrowIfNull(originalException);
        SystemId = systemId;
        FactoryId = factoryId;
        FactoryVersion = factoryVersion;
        ReleaseAttemptIndex = releaseAttemptIndex;
        _originalException = originalException;
        _isSealed = true;
    }
}
