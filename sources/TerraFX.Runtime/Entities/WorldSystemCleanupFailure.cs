// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

/// <summary>Retains one bounded World-system Release failure.</summary>
public sealed class WorldSystemCleanupFailure
{
    private readonly WorldSystemCleanupFailureControl _control;

    internal WorldSystemCleanupFailure(WorldSystemCleanupFailureControl control)
    {
        ArgumentNullException.ThrowIfNull(control);
        _control = control;
    }

    /// <summary>Gets the stable factory identity.</summary>
    public Guid FactoryId => _control.FactoryId;

    /// <summary>Gets the positive factory version.</summary>
    public int FactoryVersion => _control.FactoryVersion;

    /// <summary>Gets the exact original Release exception.</summary>
    public Exception OriginalException => _control.OriginalException;

    /// <summary>Gets the zero-based release-attempt index, including omitted failures.</summary>
    public int ReleaseAttemptIndex => _control.ReleaseAttemptIndex;

    /// <summary>Gets the system identity whose Release failed.</summary>
    public Guid SystemId => _control.SystemId;

    internal void Seal(
        Guid systemId,
        Guid factoryId,
        int factoryVersion,
        int releaseAttemptIndex,
        Exception originalException)
        => _control.Seal(systemId, factoryId, factoryVersion, releaseAttemptIndex, originalException);
}
