// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX;

namespace TerraFX.Platform;

/// <summary>Provides owner-thread platform services owned by an engine host.</summary>
public sealed class PlatformHost
{
    private readonly IPlatformBackend _backend;

    /// <summary>Creates one platform owner from a Runtime-issued identity and explicit service policy.</summary>
    /// <param name="ownerId">The nonzero identity issued by the Runtime host owner.</param>
    /// <param name="backend">The explicitly selected platform backend.</param>
    /// <param name="clipboardPolicy">The host's clipboard service admission policy.</param>
    internal PlatformHost(uint ownerId, PlatformBackend backend, ClipboardServicePolicy clipboardPolicy)
    {
        _backend = PlatformBackendFactory.Create(backend, ownerId, clipboardPolicy);
    }

    /// <summary>Gets the owner's clipboard capability state.</summary>
    public ClipboardServiceStatus ClipboardStatus => _backend.ClipboardStatus;

    /// <summary>Gets an immutable owner-thread snapshot of monitors in stable discovery order.</summary>
    public ImmutableArray<MonitorHandle> GetMonitorHandles()
        => _backend.GetMonitorHandles();

    internal void DisposeFromOwner() => _backend.Dispose();

    internal void Pump() => _backend.Pump();
}
