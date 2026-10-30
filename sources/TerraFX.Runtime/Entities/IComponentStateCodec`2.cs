// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Threading;

namespace TerraFX.Entities;

/// <summary>Reads, validates, measures, and writes one component's canonical state.</summary>
/// <typeparam name="T">The unmanaged component type.</typeparam>
/// <typeparam name="TAuthor">The symbolic author value type.</typeparam>
public interface IComponentStateCodec<T, TAuthor>
    where T : unmanaged
{
    /// <summary>Counts the exact canonical byte length of one component value.</summary>
    /// <param name="value">The component value to measure.</param>
    /// <param name="binding">The operation-scoped capture binding.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>The exact canonical encoded length.</returns>
    nint Count(in T value, ComponentCaptureBinding binding, CancellationToken cancellationToken);

    /// <summary>Reads one complete author record from the bounded source span.</summary>
    /// <param name="source">The borrowed complete source record.</param>
    /// <param name="binding">The operation-scoped read result binding.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>The one complete read result issued through <paramref name="binding"/>.</returns>
    ComponentReadResult<TAuthor> Read(
        scoped ReadOnlySpan<byte> source,
        ComponentReadBinding<TAuthor> binding,
        CancellationToken cancellationToken);

    /// <summary>Validates the declared field values of one component.</summary>
    /// <param name="value">The component value to validate.</param>
    /// <param name="binding">The operation-scoped field-validation binding.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>The immutable diagnostics for invalid fields.</returns>
    ImmutableArray<Diagnostic> Validate(
        in T value,
        ComponentValidationBinding binding,
        CancellationToken cancellationToken);

    /// <summary>Writes one complete canonical component record.</summary>
    /// <param name="value">The component value to encode.</param>
    /// <param name="destination">The borrowed exclusive destination span.</param>
    /// <param name="binding">The operation-scoped capture binding.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>The exact number of destination bytes written.</returns>
    nint Write(
        in T value,
        scoped Span<byte> destination,
        ComponentCaptureBinding binding,
        CancellationToken cancellationToken);
}
