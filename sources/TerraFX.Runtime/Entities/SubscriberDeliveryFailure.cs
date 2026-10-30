// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

/// <summary>Contains the exact exception and diagnostic for one failed subscriber delivery.</summary>
public sealed class SubscriberDeliveryFailure
{
    private readonly Diagnostic _diagnostic;
    private readonly SubscriberDeliveryFailureKind _kind;
    private Exception? _originalException;

    internal SubscriberDeliveryFailure(Diagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        _kind = SubscriberDeliveryFailureKind.SubscriberException;
        _diagnostic = diagnostic;
    }

    internal SubscriberDeliveryFailure(SubscriberDeliveryFailureKind kind, Exception originalException, Diagnostic diagnostic)
    {
        if (kind != SubscriberDeliveryFailureKind.SubscriberException)
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        ArgumentNullException.ThrowIfNull(originalException);
        ArgumentNullException.ThrowIfNull(diagnostic);
        _kind = kind;
        _originalException = originalException;
        _diagnostic = diagnostic;
    }

    /// <summary>Gets the bounded diagnostic for this delivery failure.</summary>
    public Diagnostic Diagnostic => _diagnostic;

    /// <summary>Gets the failure category.</summary>
    public SubscriberDeliveryFailureKind Kind => _kind;

    /// <summary>Gets the exact original subscriber exception.</summary>
    public Exception OriginalException
        => _originalException
            ?? throw new InvalidOperationException("The prepared subscriber failure has not been completed.");

    internal void Initialize(Exception originalException)
    {
        ArgumentNullException.ThrowIfNull(originalException);

        if (_originalException is not null)
        {
            throw new InvalidOperationException("The subscriber failure has already been completed.");
        }

        _originalException = originalException;
    }
}
