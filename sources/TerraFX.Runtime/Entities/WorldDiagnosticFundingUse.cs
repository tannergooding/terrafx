// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

internal struct WorldDiagnosticFundingUse : IDisposable
{
    private WorldDiagnosticFundingControl? _control;

    internal WorldDiagnosticFundingUse(WorldDiagnosticFundingControl control)
    {
        ArgumentNullException.ThrowIfNull(control);
        control.RetainUse();
        _control = control;
    }

    internal readonly WorldDiagnosticAttributionAccount Account
        => Control.Account;

    internal readonly WorldDiagnosticFundingControl Control
        => _control
            ?? throw new ObjectDisposedException(nameof(WorldDiagnosticFundingUse));

    public void Dispose()
    {
        var control = _control;
        _control = null;
        control?.ReleaseUse();
    }
}
