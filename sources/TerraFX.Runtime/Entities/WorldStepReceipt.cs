// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX.Numerics;

namespace TerraFX.Entities;

/// <summary>Confirms one successfully committed fixed World tick and its delivery report.</summary>
public sealed class WorldStepReceipt
{
    internal WorldStepReceipt(CommittedWorldPublication publication, PublicationDeliveryReport deliveryReport)
    {
        ArgumentNullException.ThrowIfNull(publication);
        ArgumentNullException.ThrowIfNull(deliveryReport);

        if (!ReferenceEquals(deliveryReport.Publication, publication))
        {
            throw new ArgumentException("The delivery report must refer to the exact committed publication.", nameof(deliveryReport));
        }

        Publication = publication;
        DeliveryReport = deliveryReport;
    }

    /// <summary>Gets the command receipts committed by this tick.</summary>
    public ImmutableArray<CommandReceipt> CommandReceipts => Publication.CommandReceipts;

    /// <summary>Gets the completed zero-based tick index.</summary>
    public long CompletedTick => Publication.CompletedTick;

    /// <summary>Gets the independent subscriber-delivery results for this publication.</summary>
    public PublicationDeliveryReport DeliveryReport { get; }

    /// <summary>Gets the actual committed 2D spatial frame, when admitted.</summary>
    public SpatialFrame2? Frame2 => Publication.Frame2;

    /// <summary>Gets the actual committed 3D spatial frame.</summary>
    public SpatialFrame3 Frame3 => Publication.Frame3;

    /// <summary>Gets the next zero-based tick index.</summary>
    public long NextTickIndex => Publication.NextTickIndex;

    /// <summary>Gets the exact immutable publication committed by this tick.</summary>
    public CommittedWorldPublication Publication { get; }

    /// <summary>Gets the World identity.</summary>
    public Guid WorldGuid => Publication.WorldGuid;
}
