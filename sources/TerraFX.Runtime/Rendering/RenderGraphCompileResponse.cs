// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX;

namespace TerraFX.Rendering;

/// <summary>Reports graph compilation without exposing a partial plan.</summary>
public sealed class RenderGraphCompileResponse
{
    internal RenderGraphCompileResponse(
        RenderGraphCompileStatus status,
        RenderGraphPlan? plan,
        ImmutableArray<Diagnostic> diagnostics)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        if (diagnostics.IsDefault)
        {
            throw new ArgumentException("Graph compilation diagnostics must be initialized.", nameof(diagnostics));
        }

        if ((status == RenderGraphCompileStatus.Compiled)
            ? (plan is null) || !diagnostics.IsEmpty
            : (plan is not null) || diagnostics.IsEmpty)
        {
            throw new ArgumentException("Compiled results require only a plan; rejected results require only diagnostics.");
        }

        Diagnostics = [.. diagnostics];
        Plan = plan;
        Status = status;
    }

    /// <summary>Gets immutable diagnostics for a rejected compilation.</summary>
    public ImmutableArray<Diagnostic> Diagnostics { get; }

    /// <summary>Gets the immutable plan when compilation succeeds.</summary>
    public RenderGraphPlan? Plan { get; }

    /// <summary>Gets the compilation outcome.</summary>
    public RenderGraphCompileStatus Status { get; }
}
