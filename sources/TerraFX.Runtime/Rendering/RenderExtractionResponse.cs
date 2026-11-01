// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX;

namespace TerraFX.Rendering;

/// <summary>Reports a complete CPU extraction or a typed capacity refusal.</summary>
public sealed class RenderExtractionResponse
{
    internal RenderExtractionResponse(
        RenderExtractionStatus status,
        RenderSnapshot? snapshot,
        RenderExtractionLimitDiagnostic? limitDiagnostic,
        ImmutableArray<Diagnostic> diagnostics)
    {
        if (diagnostics.IsDefault)
        {
            throw new ArgumentException("The extraction diagnostics array must be initialized.", nameof(diagnostics));
        }

        if (status is RenderExtractionStatus.Extracted)
        {
            if ((snapshot is null) || limitDiagnostic.HasValue || !diagnostics.IsEmpty)
            {
                throw new ArgumentException("An extracted response requires a snapshot and no capacity diagnostic.");
            }
        }
        else if (status is RenderExtractionStatus.CapacityExceeded)
        {
            if ((snapshot is not null) || !limitDiagnostic.HasValue || !diagnostics.IsEmpty)
            {
                throw new ArgumentException("A capacity refusal requires its typed diagnostic and no partial snapshot.");
            }
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        Status = status;
        Snapshot = snapshot;
        LimitDiagnostic = limitDiagnostic;
        Diagnostics = [.. diagnostics];
    }

    /// <summary>Gets copied extraction warnings, or an initialized empty array.</summary>
    public ImmutableArray<Diagnostic> Diagnostics { get; }

    /// <summary>Gets the exact capacity diagnostic when the extraction was refused.</summary>
    public RenderExtractionLimitDiagnostic? LimitDiagnostic { get; }

    /// <summary>Gets the complete immutable snapshot, or null when capacity was exceeded.</summary>
    public RenderSnapshot? Snapshot { get; }

    /// <summary>Gets the extraction outcome.</summary>
    public RenderExtractionStatus Status { get; }
}
