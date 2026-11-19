// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;
using TerraFX;

namespace TerraFX.Entities;

internal sealed class WorldDiagnosticFundingControl
{
    internal const long OwnedByteReservation = 136;

    private readonly WorldDiagnosticAttributionAccount _account;
    private WorldDomainOperationCredit? _credit;
    private int _useCount = 1;

    internal WorldDiagnosticFundingControl(
        WorldDiagnosticAttributionAccount account,
        WorldDomainOperationCredit credit)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(credit);

        if (!credit.IsRetainedDiagnosticSource)
        {
            throw new ArgumentException("The diagnostic funding control requires a retained-source credit.", nameof(credit));
        }

        _account = account;
        _credit = credit;
    }

    internal WorldDiagnosticAttributionAccount Account => _account;

    internal long EnvelopeBytes
        => _credit is null
            ? throw new ObjectDisposedException(nameof(WorldDiagnosticFundingControl))
            : _credit.ReservedBytes;

    internal void ReleaseUse()
    {
        while (true)
        {
            var observed = Volatile.Read(ref _useCount);

            if (observed <= 0)
            {
                throw new InvalidOperationException("The diagnostic funding control released more uses than it acquired.");
            }

            if (observed > 1)
            {
                if (Interlocked.CompareExchange(ref _useCount, observed - 1, observed) == observed)
                {
                    return;
                }

                continue;
            }

            if (_account.ReservedBytes != 0)
            {
                throw new InvalidOperationException("The final diagnostic funding use cannot return while source payload remains reserved.");
            }

            if (Interlocked.CompareExchange(ref _useCount, 0, 1) == 1)
            {
                Interlocked.Exchange(ref _credit, null)?.Dispose();
                return;
            }
        }
    }

    internal void RetainUse()
    {
        var observed = Volatile.Read(ref _useCount);

        while (observed > 0)
        {
            if (observed == int.MaxValue)
            {
                throw new InvalidOperationException("The diagnostic funding use count is exhausted.");
            }

            var previous = Interlocked.CompareExchange(ref _useCount, observed + 1, observed);

            if (previous == observed)
            {
                return;
            }

            observed = previous;
        }

        throw new ObjectDisposedException(nameof(WorldDiagnosticFundingControl));
    }
}
