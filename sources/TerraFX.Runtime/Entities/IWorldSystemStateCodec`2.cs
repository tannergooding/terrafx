// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Threading;

namespace TerraFX.Entities;

/// <summary>Defines the typed codec for one stateful World system.</summary>
/// <typeparam name="TSystem">The exact closed World system type.</typeparam>
/// <typeparam name="TAuthor">The immutable symbolic author type.</typeparam>
public interface IWorldSystemStateCodec<TSystem, TAuthor>
    where TSystem : class, IWorldSystem
    where TAuthor : notnull
{
    /// <summary>Counts the exact canonical bytes required to capture one system.</summary>
    /// <param name="system">The system whose authoritative state is captured.</param>
    /// <param name="binding">The operation-scoped capture binding.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>The exact required byte count.</returns>
    nint Count(TSystem system, WorldSystemCaptureBinding binding, CancellationToken cancellationToken);

    /// <summary>Reads one immutable author value from canonical system-state bytes.</summary>
    /// <param name="source">The scoped canonical input bytes.</param>
    /// <param name="binding">The operation-scoped read result binding.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>The complete or rejected read result issued by the binding.</returns>
    WorldSystemReadResult<TAuthor> Read(
        scoped ReadOnlySpan<byte> source,
        WorldSystemReadBinding<TAuthor> binding,
        CancellationToken cancellationToken);

    /// <summary>Restores one immutable author value into its fresh system instance.</summary>
    /// <param name="system">The fresh receiving system instance.</param>
    /// <param name="author">The exact decoded immutable author value.</param>
    /// <param name="binding">The exact candidate-map restore binding.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    void Restore(
        TSystem system,
        in TAuthor author,
        WorldStateRestoreBinding binding,
        CancellationToken cancellationToken);

    /// <summary>Validates one initialized system instance.</summary>
    /// <param name="system">The system to validate.</param>
    /// <param name="binding">The operation-scoped validation binding.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>The initialized immutable validation diagnostics.</returns>
    ImmutableArray<Diagnostic> Validate(
        TSystem system,
        WorldSystemValidationBinding binding,
        CancellationToken cancellationToken);

    /// <summary>Writes the exact canonical system-state bytes.</summary>
    /// <param name="system">The system whose authoritative state is captured.</param>
    /// <param name="destination">The scoped canonical output bytes.</param>
    /// <param name="binding">The operation-scoped capture binding.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>The exact number of bytes written.</returns>
    nint Write(
        TSystem system,
        scoped Span<byte> destination,
        WorldSystemCaptureBinding binding,
        CancellationToken cancellationToken);
}
