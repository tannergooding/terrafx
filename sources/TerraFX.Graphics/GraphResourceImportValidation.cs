// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

internal readonly struct GraphResourceImportValidation
{
    internal GraphResourceImportValidation(
        bool stateMatches,
        bool contentsMatch,
        bool isIdle,
        long lastUseSequence,
        GraphImportCompletionPolicy completionPolicy,
        bool hasOverlappingImport)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(lastUseSequence);

        if (!Enum.IsDefined(completionPolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(completionPolicy));
        }

        StateMatches = stateMatches;
        ContentsMatch = contentsMatch;
        IsIdle = isIdle;
        LastUseSequence = lastUseSequence;
        CompletionPolicy = completionPolicy;
        HasOverlappingImport = hasOverlappingImport;
    }

    internal GraphImportCompletionPolicy CompletionPolicy { get; }

    internal bool ContentsMatch { get; }

    internal bool HasOverlappingImport { get; }

    internal bool IsAdmissible
        => StateMatches
            && ContentsMatch
            && !HasOverlappingImport
            && ((CompletionPolicy != GraphImportCompletionPolicy.RequireIdle) || IsIdle);

    internal bool IsIdle { get; }

    internal long LastUseSequence { get; }

    internal bool StateMatches { get; }
}
