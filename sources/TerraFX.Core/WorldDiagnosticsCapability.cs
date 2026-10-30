// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX;

internal sealed class WorldDiagnosticsCapability
{
    private int _closed;
    private readonly DiagnosticsService _diagnostics;
    private readonly Guid _executionSessionId;
    private readonly Guid _worldGuid;
    private long _worldOwnerId;

    internal WorldDiagnosticsCapability(DiagnosticsService diagnostics, Guid worldGuid, Guid executionSessionId)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentOutOfRangeException.ThrowIfEqual(worldGuid, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(executionSessionId, Guid.Empty);

        _diagnostics = diagnostics;
        _worldGuid = worldGuid;
        _executionSessionId = executionSessionId;
    }

    internal DiagnosticsService Diagnostics => _diagnostics;

    internal Guid ExecutionSessionId => _executionSessionId;

    internal Guid WorldGuid => _worldGuid;

    internal void BindOwner(uint worldOwnerId)
    {
        ArgumentOutOfRangeException.ThrowIfZero(worldOwnerId);

        if (Interlocked.CompareExchange(ref _worldOwnerId, worldOwnerId, 0) != 0)
        {
            throw new InvalidOperationException("The diagnostics capability is already bound to a World owner.");
        }
    }

    internal void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) == 0)
        {
            _diagnostics.Close();
        }
    }

    internal long GetTimestamp()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _closed) != 0, this);
        return DiagnosticClockDomain.GetElapsedTimestamp();
    }

    internal void RecordDropped()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _closed) != 0, this);
        _diagnostics.RecordDropped();
    }

    internal void Write(Diagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _closed) != 0, this);

        if (diagnostic.WorldGuid != _worldGuid)
        {
            throw new ArgumentException("The diagnostic WorldGuid does not match this capability.", nameof(diagnostic));
        }

        var worldOwnerId = Volatile.Read(ref _worldOwnerId);

        if (worldOwnerId == 0)
        {
            throw new InvalidOperationException("The diagnostics capability is not bound to a World owner.");
        }

        _diagnostics.Write(diagnostic, _executionSessionId, checked((uint)worldOwnerId));
    }
}
