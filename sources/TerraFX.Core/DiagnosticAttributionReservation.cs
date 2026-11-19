// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX;

internal ref struct DiagnosticAttributionReservation
{
    private WorldDiagnosticAttributionAccount? _account;
    private DiagnosticsService? _diagnostics;
    private long _diagnosticsReservedBytes;
    private long _reservedBytes;

    internal DiagnosticAttributionReservation(
        WorldDiagnosticAttributionAccount account,
        long reservedBytes)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(reservedBytes);
        _account = account;
        _diagnostics = null;
        _diagnosticsReservedBytes = 0;
        _reservedBytes = reservedBytes;
    }

    internal readonly WorldDiagnosticAttributionAccount Account
        => _account
            ?? throw new ObjectDisposedException(nameof(DiagnosticAttributionReservation));

    internal readonly DiagnosticsService Diagnostics
        => _diagnostics
            ?? throw new ObjectDisposedException(nameof(DiagnosticAttributionReservation));

    internal readonly long DiagnosticsReservedBytes => _diagnosticsReservedBytes;

    internal readonly long ReservedBytes => _reservedBytes;

    public void Dispose()
    {
        var account = _account;
        var diagnostics = _diagnostics;
        var diagnosticsReservedBytes = _diagnosticsReservedBytes;
        var reservedBytes = _reservedBytes;
        _account = null;
        _diagnostics = null;
        _diagnosticsReservedBytes = 0;
        _reservedBytes = 0;

        diagnostics?.ReleasePreclaimedServiceBytes(diagnosticsReservedBytes);
        account?.Release(reservedBytes);
    }

    internal void AttachServiceReservation(DiagnosticsService diagnostics, long reservedBytes)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(reservedBytes);

        if ((_diagnostics is not null) || (reservedBytes > _reservedBytes))
        {
            throw new InvalidOperationException("The diagnostic service reservation does not match the source-account preclaim.");
        }

        _diagnostics = diagnostics;
        _diagnosticsReservedBytes = reservedBytes;
    }

    internal void TransferToService(long retainedBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(retainedBytes);

        if ((retainedBytes > _reservedBytes) || (retainedBytes > _diagnosticsReservedBytes && (_diagnostics is not null)))
        {
            throw new InvalidOperationException("The diagnostic service retained more bytes than were preclaimed.");
        }

        var account = _account
            ?? throw new ObjectDisposedException(nameof(DiagnosticAttributionReservation));
        var diagnostics = _diagnostics;
        var diagnosticsReservedBytes = _diagnosticsReservedBytes;
        var temporaryBytes = _reservedBytes - retainedBytes;

        diagnostics?.CommitPreclaimedServiceBytes(diagnosticsReservedBytes, retainedBytes);

        if (diagnostics is null)
        {
            if (temporaryBytes != 0)
            {
                account.Release(temporaryBytes);
            }

            _account = null;
            _reservedBytes = 0;
        }

        if (diagnostics is not null)
        {
            _reservedBytes = temporaryBytes;
            _diagnosticsReservedBytes = temporaryBytes;

            if (temporaryBytes == 0)
            {
                _account = null;
                _diagnostics = null;
                _diagnosticsReservedBytes = 0;
            }
        }
    }
}
