// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using TerraFX;

namespace TerraFX.Entities;

/// <summary>Reports a frame transaction that failed during preparation or abort before installation.</summary>
[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "Frame transaction failures require their exact World, dimension, primary diagnostic, and cleanup causes.")]
public sealed class WorldFrameTransactionException : InvalidOperationException
{
    internal WorldFrameTransactionException(
        Exception? innerException,
        Diagnostic? primaryDiagnostic,
        Guid worldGuid,
        int dimension,
        ImmutableArray<Exception> cleanupExceptions)
        : base("The World frame transaction failed before installation.", innerException)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(worldGuid, Guid.Empty);

        if (dimension is not 2 and not 3)
        {
            throw new ArgumentOutOfRangeException(nameof(dimension));
        }

        if (cleanupExceptions.IsDefault)
        {
            throw new ArgumentException("Frame transaction cleanup exceptions must be initialized.", nameof(cleanupExceptions));
        }

        PrimaryDiagnostic = primaryDiagnostic;
        WorldGuid = worldGuid;
        Dimension = dimension;
        CleanupExceptions = ImmutableArrayCopy.Create(cleanupExceptions);
    }

    /// <summary>Gets cleanup failures recorded while aborting the transaction.</summary>
    public ImmutableArray<Exception> CleanupExceptions { get; }

    /// <summary>Gets the affected dimensional frame.</summary>
    public int Dimension { get; }

    /// <summary>Gets the primary bounded diagnostic, when preparation produced one.</summary>
    public Diagnostic? PrimaryDiagnostic { get; }

    /// <summary>Gets the exact World identity that owned the operation.</summary>
    public Guid WorldGuid { get; }
}
