// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

/// <summary>Owns one World publication callback registration.</summary>
public sealed class WorldSubscription : IDisposable
{
    private readonly long _subscriptionId;
    private readonly World _world;

    internal WorldSubscription(World world, long subscriptionId)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(subscriptionId);
        _world = world;
        _subscriptionId = subscriptionId;
    }

    /// <summary>Gets the positive, never-reused subscription identity.</summary>
    public long SubscriptionId => _subscriptionId;

    /// <summary>Unregisters this callback from future publication captures.</summary>
    public void Dispose()
        => _world.DisposeSubscription(this);

    internal bool BelongsTo(World world)
        => ReferenceEquals(_world, world);
}
