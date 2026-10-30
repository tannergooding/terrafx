// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace TerraFX.Entities;

/// <summary>Contains one immutable complete resource-read result.</summary>
/// <typeparam name="TAuthor">The symbolic author value type.</typeparam>
public sealed class WorldResourceReadResult<TAuthor>
    where TAuthor : notnull
{
    private readonly TAuthor? _author;
    private readonly bool _hasAuthor;
    private readonly object _issuer;

    private WorldResourceReadResult(
        WorldResourceReadStatus status,
        nint consumedBytes,
        ImmutableArray<Diagnostic> diagnostics,
        TAuthor? author,
        bool hasAuthor,
        object issuer)
    {
        Status = status;
        ConsumedBytes = consumedBytes;
        Diagnostics = diagnostics;
        _author = author;
        _hasAuthor = hasAuthor;
        _issuer = issuer;
    }

    /// <summary>Gets the number of source bytes consumed.</summary>
    public nint ConsumedBytes { get; }

    /// <summary>Gets the immutable read diagnostics.</summary>
    public ImmutableArray<Diagnostic> Diagnostics { get; }

    /// <summary>Gets the read result status.</summary>
    public WorldResourceReadStatus Status { get; }

    internal static WorldResourceReadResult<TAuthor> Read(TAuthor author, nint consumedBytes, object issuer)
        => new(WorldResourceReadStatus.Read, consumedBytes, [], author, hasAuthor: true, issuer);

    internal static WorldResourceReadResult<TAuthor> Rejected(ImmutableArray<Diagnostic> diagnostics, object issuer)
    {
        if (diagnostics.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A rejected resource read must retain at least one diagnostic.", nameof(diagnostics));
        }

        foreach (var diagnostic in diagnostics)
        {
            ArgumentNullException.ThrowIfNull(diagnostic);
        }

        return new WorldResourceReadResult<TAuthor>(
            WorldResourceReadStatus.Rejected,
            0,
            [.. diagnostics],
            default,
            hasAuthor: false,
            issuer);
    }

    /// <summary>Attempts to retrieve the symbolic author value for a successful read.</summary>
    /// <param name="value">The complete author value, or the default value when rejected.</param>
    /// <returns><see langword="true"/> when the record was read successfully.</returns>
    public bool TryGetAuthorValue([MaybeNullWhen(false), NotNullWhen(true)] out TAuthor value)
    {
        if (_hasAuthor && (Status == WorldResourceReadStatus.Read))
        {
            value = _author!;
            return true;
        }

        value = default!;
        return false;
    }

    internal bool IsIssuedBy(object issuer)
        => ReferenceEquals(_issuer, issuer);
}
