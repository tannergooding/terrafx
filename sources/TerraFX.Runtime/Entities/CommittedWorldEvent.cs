// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;

namespace TerraFX.Entities;

/// <summary>Base type for one immutable gameplay event in a committed World publication.</summary>
public abstract class CommittedWorldEvent
{
    private protected CommittedWorldEvent(
        ContentSchemaReference schema,
        uint worldOwnerId,
        long completedTick,
        long sequence)
    {
        if ((schema.SchemaId == Guid.Empty)
            || (schema.Major <= 0)
            || (schema.Minor < 0)
            || (schema.Scope != ContentSchemaScope.Content))
        {
            throw new ArgumentException("A committed event requires one valid content schema reference.", nameof(schema));
        }

        ArgumentOutOfRangeException.ThrowIfZero(worldOwnerId);
        ArgumentOutOfRangeException.ThrowIfNegative(completedTick);
        ArgumentOutOfRangeException.ThrowIfNegative(sequence);

        CompletedTick = completedTick;
        Schema = schema;
        Sequence = sequence;
        WorldOwnerId = worldOwnerId;
    }

    /// <summary>Gets the completed zero-based tick that emitted this event.</summary>
    public long CompletedTick { get; }

    /// <summary>Gets the exact EventPayload schema that defines this event.</summary>
    public ContentSchemaReference Schema { get; }

    /// <summary>Gets the event's dense sequence within its committed publication.</summary>
    public long Sequence { get; }

    /// <summary>Gets the stable runtime World owner that emitted this event.</summary>
    public uint WorldOwnerId { get; }
}
