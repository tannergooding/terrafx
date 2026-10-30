// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Collections.Immutable;

namespace TerraFX.Entities;

internal sealed class WorldResourceStateCodecReadOutcome
{
    private WorldResourceStateCodecReadOutcome(
        WorldResourceReadStatus status,
        object? author,
        nint consumedBytes,
        ImmutableArray<Diagnostic> diagnostics)
    {
        Status = status;
        Author = author;
        ConsumedBytes = consumedBytes;
        Diagnostics = ImmutableArrayCopy.Create(diagnostics);
    }

    internal object? Author { get; }

    internal nint ConsumedBytes { get; }

    internal ImmutableArray<Diagnostic> Diagnostics { get; }

    internal WorldResourceReadStatus Status { get; }

    internal static WorldResourceStateCodecReadOutcome Create<TAuthor>(WorldResourceReadResult<TAuthor> result)
        where TAuthor : notnull
        => new(result.Status, result.TryGetAuthorValue(out var author) ? author : null, result.ConsumedBytes, result.Diagnostics);
}
