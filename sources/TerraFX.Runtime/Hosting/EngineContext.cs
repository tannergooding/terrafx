// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Threading;
using TerraFX.Entities;
using TerraFX.Input;

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

    /// <summary>Binds one simulation input router to a native window and hosted World.</summary>
    /// <param name="window">The exact live host-owned window route.</param>
    /// <param name="world">The World hosted by this EngineHost.</param>
    /// <returns>The binding result and any contextual rejection diagnostic.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="world"/> is null.</exception>
    public InputBindingReceipt BindGameplayInput(WindowHandle window, World world)
    {
        ArgumentNullException.ThrowIfNull(world);
        return GetHost().BindGameplayInputFromContext(window, world);
    }

    /// <summary>Binds a simulation input router to one World in a Headless host.</summary>
    /// <param name="router">The exact simulation router created by this host.</param>
    /// <param name="world">The World hosted by this EngineHost.</param>
    /// <returns>The binding result and any contextual rejection diagnostic.</returns>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public InputBindingReceipt BindHeadlessGameplayInput(InputRouter router, World world)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(world);
        return GetHost().BindHeadlessGameplayInputFromContext(router, world);
    }

    /// <summary>Creates one host-owned input router for exactly one recipient.</summary>
    /// <param name="recipientKind">The host-live UI or simulation timeline recipient.</param>
    /// <param name="window">The exact host window for a HostLiveUI recipient.</param>
    /// <returns>The recipient-local router.</returns>
    /// <exception cref="ArgumentException">The recipient and window selection are inconsistent.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="recipientKind"/> is undefined.</exception>
    /// <exception cref="NotSupportedException">The requested native UI route is not implemented for this host profile.</exception>
    public InputRouter CreateInputRouter(InputRecipientKind recipientKind, WindowHandle? window = null)
        => GetHost().CreateInputRouterFromContext(recipientKind, window);

    /// <summary>Creates a hosted World from an immutable registration and validated options.</summary>
    /// <param name="options">The frozen registration and bounded World capacities.</param>
    /// <returns>The World owned by the current EngineHost.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">The World schema registry does not match the selected AssetStore package.</exception>
    /// <exception cref="InvalidOperationException">The host is not accepting Worlds or a host-owned capacity is unavailable.</exception>
    public World CreateWorld(WorldOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return GetHost().CreateWorldFromContext(options);
    }

    /// <summary>Injects one normalized event to an explicit set of bound Headless timelines.</summary>
    /// <param name="recipients">The nonempty, distinct same-host bound timeline recipients.</param>
    /// <param name="input">The normalized input value copied to each recipient.</param>
    /// <returns>Recipient-ordered routing receipts with one shared capture identity.</returns>
    /// <exception cref="ArgumentException">The recipient set is empty, oversized, duplicated, foreign, or unbound.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="input"/> or a recipient is null.</exception>
    /// <exception cref="InvalidOperationException">The host is not a Headless profile.</exception>
    public ImmutableArray<InputRoutingReceipt> InjectHeadlessInput(
        ImmutableArray<InputRouter> recipients,
        InputInjection input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return GetHost().InjectHeadlessInputFromContext(recipients, input);
    }

    internal void Invalidate()
        => _ = Interlocked.Exchange(ref _host, null);

    private EngineHost GetHost()
    {
        var host = Volatile.Read(ref _host);
        ObjectDisposedException.ThrowIf(host is null, this);
        return host;
    }
}
