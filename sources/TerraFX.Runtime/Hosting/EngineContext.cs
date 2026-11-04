// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;
using TerraFX.Entities;

namespace TerraFX.Hosting;

/// <summary>Provides owner-thread World creation during one EngineHost callback.</summary>
public sealed class EngineContext
{
    private EngineHost? _host;

    internal EngineContext(EngineHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
    }

    /// <summary>Creates a hosted World from an immutable registration and validated options.</summary>
    /// <param name="options">The frozen registration and bounded World capacities.</param>
    /// <returns>The World owned by the current EngineHost.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">The World schema registry does not match the selected AssetStore package.</exception>
    /// <exception cref="InvalidOperationException">The host is not accepting Worlds or a host-owned capacity is unavailable.</exception>
    public World CreateWorld(WorldOptions options)
    {
        var host = Volatile.Read(ref _host);
        ObjectDisposedException.ThrowIf(host is null, this);
        return host.CreateWorldFromContext(options);
    }

    internal void Invalidate()
        => _ = Interlocked.Exchange(ref _host, null);
}
