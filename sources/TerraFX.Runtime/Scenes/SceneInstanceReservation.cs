// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX.Scenes;

internal sealed class SceneInstanceReservation : IDisposable
{
    private readonly int _occurrenceCount;
    private SceneWorldRegistry? _registry;
    private readonly int _slot;

    internal SceneInstanceReservation(SceneWorldRegistry registry, int slot, int occurrenceCount)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(occurrenceCount);
        _registry = registry;
        _slot = slot;
        _occurrenceCount = occurrenceCount;
    }

    internal int OccurrenceCount => _occurrenceCount;

    internal SceneWorldRegistry? Registry => _registry;

    internal int Slot => _slot;

    public void Dispose()
        => Interlocked.Exchange(ref _registry, null)?.ReleaseReservation(this);

    internal void Commit(SceneInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var registry = _registry
            ?? throw new ObjectDisposedException(nameof(SceneInstanceReservation));
        registry.CommitReservation(this, instance);
        _ = Interlocked.Exchange(ref _registry, null);
    }
}
