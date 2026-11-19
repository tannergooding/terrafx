// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

public sealed partial class World
{
    private WorldDiagnosticFundingControl GetDiagnosticFundingControl()
        => _diagnosticFundingControl
            ?? throw new InvalidOperationException("The World has no diagnostic source-funding control.");

    private WorldDiagnosticsServiceOwner GetDiagnosticsServiceOwner()
        => _diagnosticsServiceOwner
            ?? throw new InvalidOperationException("The World has no diagnostic service owner.");

    private void RecordDiagnosticDropped()
        => GetDiagnosticsServiceOwner().RecordDropped(_diagnosticsCapability);

    private void WriteDiagnostic(Diagnostic diagnostic)
        => GetDiagnosticsServiceOwner().WriteAttributed(
            _diagnosticsCapability,
            GetDiagnosticFundingControl(),
            diagnostic);

    private void WriteSubscriptionCapacityWarning()
        => GetDiagnosticsServiceOwner().WriteSubscriptionCapacityWarning(
            _diagnosticsCapability,
            GetDiagnosticFundingControl());
}
