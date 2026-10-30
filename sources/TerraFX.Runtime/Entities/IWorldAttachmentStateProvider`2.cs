// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Threading;

namespace TerraFX.Entities;

/// <summary>Defines the typed state codec and release provider for one attachment family.</summary>
/// <typeparam name="T">The immutable attachment CLR type.</typeparam>
/// <typeparam name="TAuthor">The immutable symbolic author type.</typeparam>
public interface IWorldAttachmentStateProvider<T, TAuthor>
    where T : class
    where TAuthor : notnull
{
    /// <summary>Counts the exact canonical bytes required to capture one attachment.</summary>
    /// <param name="value">The immutable attachment value.</param>
    /// <param name="binding">The operation-scoped capture binding.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>The exact required byte count.</returns>
    nint Count(T value, WorldAttachmentCaptureBinding binding, CancellationToken cancellationToken);

    /// <summary>Reads one immutable author value from canonical attachment bytes.</summary>
    /// <param name="source">The scoped canonical input bytes.</param>
    /// <param name="binding">The operation-scoped read result binding.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>The complete or rejected read result issued by the binding.</returns>
    WorldAttachmentReadResult<TAuthor> Read(
        scoped ReadOnlySpan<byte> source,
        WorldAttachmentReadBinding<TAuthor> binding,
        CancellationToken cancellationToken);

    /// <summary>Releases one detached transferred installation after its storage commit.</summary>
    /// <param name="value">The immutable value whose installation was detached.</param>
    /// <param name="binding">The exact installation release facts.</param>
    void Release(T value, WorldAttachmentReleaseBinding binding);

    /// <summary>Creates one fresh immutable attachment value from its decoded author representation.</summary>
    /// <param name="author">The exact decoded immutable author value.</param>
    /// <param name="binding">The exact candidate-map restore binding.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>The fresh immutable attachment value.</returns>
    T Restore(in TAuthor author, WorldAttachmentRestoreBinding binding, CancellationToken cancellationToken);

    /// <summary>Validates one immutable attachment value.</summary>
    /// <param name="value">The value to validate.</param>
    /// <param name="binding">The operation-scoped validation binding.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>The initialized immutable validation diagnostics.</returns>
    ImmutableArray<Diagnostic> Validate(
        T value,
        WorldAttachmentValidationBinding binding,
        CancellationToken cancellationToken);

    /// <summary>Writes the exact canonical attachment bytes.</summary>
    /// <param name="value">The immutable attachment value.</param>
    /// <param name="destination">The scoped canonical output bytes.</param>
    /// <param name="binding">The operation-scoped capture binding.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>The exact number of bytes written.</returns>
    nint Write(
        T value,
        scoped Span<byte> destination,
        WorldAttachmentCaptureBinding binding,
        CancellationToken cancellationToken);
}
