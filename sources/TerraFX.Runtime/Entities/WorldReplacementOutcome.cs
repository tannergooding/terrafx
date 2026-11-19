// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

internal readonly struct WorldReplacementOutcome
{
    internal WorldReplacementOutcome(
        WorldReplacementOutcomeKind kind,
        World? replacementWorld,
        WorldGuidRefusal refusal)
    {
        if ((kind == WorldReplacementOutcomeKind.Published)
            && ((replacementWorld is null) || !refusal.IsNone))
        {
            throw new ArgumentException("A published World replacement requires its exact World and no refusal.");
        }

        if ((kind == WorldReplacementOutcomeKind.Rejected)
            && ((replacementWorld is not null) || refusal.IsNone))
        {
            throw new ArgumentException("A rejected World replacement requires a bounded refusal and no World.");
        }

        if (kind is not (WorldReplacementOutcomeKind.Published or WorldReplacementOutcomeKind.Rejected))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        Kind = kind;
        ReplacementWorld = replacementWorld;
        Refusal = refusal;
    }

    internal WorldReplacementOutcomeKind Kind { get; }

    internal WorldGuidRefusal Refusal { get; }

    internal World? ReplacementWorld { get; }
}
