// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Hosting;

/// <summary>Reports one terminal checkpoint save outcome.</summary>
public sealed class SaveReceipt
{
    /// <summary>Initializes a save receipt with a consistent committed or rejected outcome.</summary>
    /// <param name="status">The terminal save status.</param>
    /// <param name="transactionGeneration">The positive committed generation, or null when no commit occurred.</param>
    /// <param name="diagnostic">The rejection diagnostic, or null for a committed outcome.</param>
    /// <exception cref="ArgumentException">The status, generation, and diagnostic combination is inconsistent.</exception>
    internal SaveReceipt(SaveStatus status, long? transactionGeneration, Diagnostic? diagnostic)
    {
        var committed = status is SaveStatus.DurableCommitted or SaveStatus.BestEffortCommitted;

        if (!Enum.IsDefined(status)
            || (committed && ((transactionGeneration is null) || (transactionGeneration <= 0) || (diagnostic is not null)))
            || (!committed && ((transactionGeneration is not null) || (diagnostic is null))))
        {
            throw new ArgumentException("The save status, transaction generation, and diagnostic are inconsistent.");
        }

        Status = status;
        TransactionGeneration = transactionGeneration;
        Diagnostic = diagnostic;
    }

    /// <summary>Gets the rejection diagnostic, or null when the checkpoint was committed.</summary>
    public Diagnostic? Diagnostic { get; }

    /// <summary>Gets the terminal outcome.</summary>
    public SaveStatus Status { get; }

    /// <summary>Gets the committed transaction generation, or null when no commit occurred.</summary>
    public long? TransactionGeneration { get; }
}
