// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace TerraFX.Entities;

/// <summary>Contains one complete or rejected attachment state read result.</summary>
/// <typeparam name="TAuthor">The immutable symbolic author type.</typeparam>
public sealed class WorldAttachmentReadResult<TAuthor>
    where TAuthor : notnull
{
    private readonly TAuthor? _author;
    private readonly nint _consumedBytes;
    private readonly ImmutableArray<Diagnostic> _diagnostics;
    private readonly bool _hasAuthor;
    private readonly WorldAttachmentReadStatus _status;

    internal WorldAttachmentReadResult(
        WorldAttachmentReadStatus status,
        TAuthor? author,
        nint consumedBytes,
        ImmutableArray<Diagnostic> diagnostics)
    {
        if (status is not WorldAttachmentReadStatus.Read and not WorldAttachmentReadStatus.Rejected)
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(consumedBytes);

        if (diagnostics.IsDefault)
        {
            throw new ArgumentException("Attachment read diagnostics must be initialized.", nameof(diagnostics));
        }

        if ((status == WorldAttachmentReadStatus.Read)
            ? (author is null || !diagnostics.IsEmpty)
            : (author is not null || (consumedBytes != 0) || diagnostics.IsEmpty))
        {
            throw new ArgumentException("The attachment read result does not match its selected status.");
        }

        foreach (var diagnostic in diagnostics)
        {
            ArgumentNullException.ThrowIfNull(diagnostic);
        }

        _status = status;
        _author = author;
        _hasAuthor = status == WorldAttachmentReadStatus.Read;
        _consumedBytes = consumedBytes;
        _diagnostics = ImmutableArrayCopy.Create(diagnostics);
    }

    /// <summary>Gets the exact number of source bytes consumed by a complete read.</summary>
    public nint ConsumedBytes => _consumedBytes;

    /// <summary>Gets the immutable read diagnostics.</summary>
    public ImmutableArray<Diagnostic> Diagnostics => _diagnostics;

    /// <summary>Gets the selected read status.</summary>
    public WorldAttachmentReadStatus Status => _status;

    /// <summary>Gets the complete author value when this result represents a successful read.</summary>
    /// <param name="value">The decoded immutable author value, or default on rejection.</param>
    /// <returns><see langword="true"/> when a complete author value is available.</returns>
    public bool TryGetAuthorValue([MaybeNullWhen(false), NotNullWhen(true)] out TAuthor value)
    {
        value = _hasAuthor ? _author! : default!;
        return _hasAuthor;
    }
}
