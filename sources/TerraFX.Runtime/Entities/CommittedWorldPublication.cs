// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using TerraFX;
using TerraFX.Numerics;

namespace TerraFX.Entities;

/// <summary>Contains one immutable publication installed by a successful World tick.</summary>
public sealed class CommittedWorldPublication
{
    internal CommittedWorldPublication(
        Guid executionSessionId,
        uint worldOwnerId,
        long publicationSequence,
        Guid worldGuid,
        long completedTick,
        long nextTickIndex,
        SpatialFrame3 frame3,
        SpatialFrame2? frame2,
        ImmutableArray<CommandReceipt> commandReceipts,
        ImmutableArray<CommittedWorldEvent> events)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(executionSessionId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfZero(worldOwnerId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(publicationSequence);
        ArgumentOutOfRangeException.ThrowIfEqual(worldGuid, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegative(completedTick);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(nextTickIndex);

        if (nextTickIndex != checked(completedTick + 1))
        {
            throw new ArgumentException("The publication next tick must immediately follow its completed tick.", nameof(nextTickIndex));
        }

        if (commandReceipts.IsDefault || events.IsDefault)
        {
            throw new ArgumentException("Publication arrays must be initialized.");
        }

        foreach (var receipt in commandReceipts)
        {
            ArgumentNullException.ThrowIfNull(receipt);
        }

        foreach (var item in events)
        {
            ArgumentNullException.ThrowIfNull(item);
        }

        ExecutionSessionId = executionSessionId;
        WorldOwnerId = worldOwnerId;
        PublicationSequence = publicationSequence;
        WorldGuid = worldGuid;
        CompletedTick = completedTick;
        NextTickIndex = nextTickIndex;
        Frame3 = frame3;
        Frame2 = frame2;
        CommandReceipts = ImmutableArrayCopy.Create(commandReceipts);
        Events = ImmutableArrayCopy.Create(events);
    }

    /// <summary>Gets the immutable command receipts committed with this publication.</summary>
    public ImmutableArray<CommandReceipt> CommandReceipts { get; }

    /// <summary>Gets the completed zero-based tick index.</summary>
    public long CompletedTick { get; }

    /// <summary>Gets the committed gameplay event sequence.</summary>
    public ImmutableArray<CommittedWorldEvent> Events { get; }

    /// <summary>Gets the execution-session identity for operational delivery.</summary>
    public Guid ExecutionSessionId { get; }

    /// <summary>Gets the actual committed 2D spatial frame, when admitted.</summary>
    public SpatialFrame2? Frame2 { get; }

    /// <summary>Gets the actual committed 3D spatial frame.</summary>
    public SpatialFrame3 Frame3 { get; }

    /// <summary>Gets the next zero-based tick index after this publication.</summary>
    public long NextTickIndex { get; }

    /// <summary>Gets the publication sequence within this World owner.</summary>
    public long PublicationSequence { get; }

    /// <summary>Gets the persistent identity of the World.</summary>
    public Guid WorldGuid { get; }

    /// <summary>Gets the stable runtime World owner identity.</summary>
    public uint WorldOwnerId { get; }

    /// <summary>Gets the exact command receipt containing a Create token.</summary>
    /// <param name="token">The Create token to resolve.</param>
    /// <returns>The owning immutable command receipt.</returns>
    /// <exception cref="ArgumentException"><paramref name="token"/> is invalid or absent from this publication.</exception>
    public CommandReceipt GetCommandReceipt(EntityToken token)
    {
        if (!token.IsValid)
        {
            throw new ArgumentException("A valid Create token is required.", nameof(token));
        }

        foreach (var receipt in CommandReceipts)
        {
            if (receipt.Contains(token))
            {
                return receipt;
            }
        }

        throw new ArgumentException("The Create token does not belong to this publication.", nameof(token));
    }
}
