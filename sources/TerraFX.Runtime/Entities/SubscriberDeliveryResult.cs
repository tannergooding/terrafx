// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

/// <summary>Contains one subscriber's outcome for a committed World publication.</summary>
public sealed class SubscriberDeliveryResult
{
    private SubscriberDeliveryFailure? _failure;
    private readonly CommittedWorldPublication _publication;
    private SubscriberDeliveryStatus _status;
    private readonly long _subscriptionId;

    internal SubscriberDeliveryResult(
        CommittedWorldPublication publication,
        long subscriptionId,
        SubscriberDeliveryStatus status,
        SubscriberDeliveryFailure? failure)
    {
        ArgumentNullException.ThrowIfNull(publication);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(subscriptionId);

        if (status == SubscriberDeliveryStatus.Delivered != (failure is null))
        {
            throw new ArgumentException("The delivery failure must be present exactly for Failed status.", nameof(failure));
        }

        if (status is not SubscriberDeliveryStatus.Delivered and not SubscriberDeliveryStatus.Failed)
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        _publication = publication;
        _subscriptionId = subscriptionId;
        _status = status;
        _failure = failure;
    }

    /// <summary>Gets the execution-session identity for operational delivery.</summary>
    public Guid ExecutionSessionId => _publication.ExecutionSessionId;

    /// <summary>Gets the delivery failure, or null when delivered.</summary>
    public SubscriberDeliveryFailure? Failure => _failure;

    /// <summary>Gets the exact committed publication delivered to the subscriber.</summary>
    public CommittedWorldPublication Publication => _publication;

    /// <summary>Gets the publication sequence.</summary>
    public long PublicationSequence => _publication.PublicationSequence;

    /// <summary>Gets the delivery outcome.</summary>
    public SubscriberDeliveryStatus Status => _status;

    /// <summary>Gets the positive registration identity.</summary>
    public long SubscriptionId => _subscriptionId;

    /// <summary>Gets the runtime World owner identity.</summary>
    public uint WorldOwnerId => _publication.WorldOwnerId;

    internal void Fail(SubscriberDeliveryFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        if ((_status != SubscriberDeliveryStatus.Delivered) || (_failure is not null))
        {
            throw new InvalidOperationException("The subscriber result is not available for a failure transition.");
        }

        _failure = failure;
        _status = SubscriberDeliveryStatus.Failed;
    }
}
