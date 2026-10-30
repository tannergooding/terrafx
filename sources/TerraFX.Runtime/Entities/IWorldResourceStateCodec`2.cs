// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Threading;

namespace TerraFX.Entities;

/// <summary>Implements typed resource-state read, restore, validation, and capture operations.</summary>
/// <typeparam name="T">The unmanaged resource partition type.</typeparam>
/// <typeparam name="TAuthor">The immutable symbolic author type.</typeparam>
public interface IWorldResourceStateCodec<T, TAuthor> : IWorldResourceStateCodec<T>
    where T : unmanaged
    where TAuthor : notnull
{
    /// <summary>Counts the canonical encoded resource-state bytes.</summary>
    /// <param name="value">The scoped read-only resource partitions.</param>
    /// <param name="binding">The operation-scoped capture field binding.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>The exact encoded byte count.</returns>
    nint Count(scoped WorldResourceStateView<T> value, WorldResourceCaptureBinding binding, CancellationToken cancellationToken);

    /// <summary>Reads one complete immutable resource-state author value.</summary>
    /// <param name="source">The scoped encoded record.</param>
    /// <param name="binding">The one-result read binding.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>The complete read result.</returns>
    WorldResourceReadResult<TAuthor> Read(
        scoped ReadOnlySpan<byte> source,
        WorldResourceReadBinding<TAuthor> binding,
        CancellationToken cancellationToken);

    /// <summary>Restores every resource partition from the complete author value.</summary>
    /// <param name="author">The immutable resource-state author value.</param>
    /// <param name="binding">The operation-scoped partition writer.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    void Restore(in TAuthor author, WorldResourceRestoreBinding<T> binding, CancellationToken cancellationToken);

    /// <summary>Validates the current resource-state partitions.</summary>
    /// <param name="value">The scoped read-only resource partitions.</param>
    /// <param name="binding">The operation-scoped validation binding.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>The initialized immutable diagnostics.</returns>
    ImmutableArray<Diagnostic> Validate(
        scoped WorldResourceStateView<T> value,
        WorldResourceValidationBinding binding,
        CancellationToken cancellationToken);

    /// <summary>Writes the canonical encoded resource-state bytes.</summary>
    /// <param name="value">The scoped read-only resource partitions.</param>
    /// <param name="destination">The scoped destination span.</param>
    /// <param name="binding">The operation-scoped capture field binding.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>The number of bytes written.</returns>
    nint Write(
        scoped WorldResourceStateView<T> value,
        scoped Span<byte> destination,
        WorldResourceCaptureBinding binding,
        CancellationToken cancellationToken);
}
