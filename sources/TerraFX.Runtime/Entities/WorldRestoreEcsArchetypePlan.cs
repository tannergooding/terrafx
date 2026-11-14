// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace TerraFX.Entities;

internal sealed class WorldRestoreEcsArchetypePlan
{
    private int _entityCount;

    private WorldRestoreEcsArchetypePlan(
        int signatureHash,
        int persistentComponentCount,
        ImmutableArray<IComponentRegistration> components,
        ImmutableArray<ContentSchemaReference> tags,
        int entityCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(persistentComponentCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(entityCount);

        if (components.IsDefault || tags.IsDefault)
        {
            throw new ArgumentException("The restored ECS archetype signature must be initialized.");
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(persistentComponentCount, components.Length);

        foreach (var tag in tags)
        {
            if ((tag.SchemaId == Guid.Empty)
                || (tag.Major <= 0)
                || (tag.Minor < 0)
                || (tag.Scope != ContentSchemaScope.Content))
            {
                throw new ArgumentException("The restored tag signature contains an invalid schema reference.", nameof(tags));
            }
        }

        for (var index = 0; index < components.Length; index++)
        {
            var component = components[index];
            ArgumentNullException.ThrowIfNull(component);

            if (index < persistentComponentCount)
            {
                if (component.Descriptor.Flags != ComponentStorageFlags.Persistent)
                {
                    throw new ArgumentException("The persistent restore component set contains a non-Persistent registration.", nameof(components));
                }
            }
            else if ((component.Descriptor.Flags != ComponentStorageFlags.Transient)
                || (component.Descriptor.Restore is not ComponentTransientRestore.Reset and not ComponentTransientRestore.Derived))
            {
                throw new ArgumentException("The transient restore component set contains a registration without a transient restore policy.", nameof(components));
            }
        }

        PersistentComponentCount = persistentComponentCount;
        Components = components;
        Tags = tags;
        SignatureHash = signatureHash;
        _entityCount = entityCount;
    }

    internal ImmutableArray<IComponentRegistration> Components { get; }

    internal int EntityCount => _entityCount;

    internal long OwnedByteReservation
        => checked(
            64L
            + ArrayBytes(Components.Length, IntPtr.Size)
            + ArrayBytes(Tags.Length, Unsafe.SizeOf<ContentSchemaReference>()));

    internal int PersistentComponentCount { get; }

    internal int SignatureHash { get; }

    internal ImmutableArray<ContentSchemaReference> Tags { get; }

    internal int TransientComponentCount => Components.Length - PersistentComponentCount;

    internal static WorldRestoreEcsArchetypePlan Create(
        int signatureHash,
        int persistentComponentCount,
        ImmutableArray<IComponentRegistration> components,
        ImmutableArray<ContentSchemaReference> tags)
        => new(signatureHash, persistentComponentCount, components, tags, entityCount: 1);

    internal static WorldRestoreEcsArchetypePlan CreateEmpty(int entityCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(entityCount);
        return new WorldRestoreEcsArchetypePlan(0, 0, [], [], entityCount);
    }

    private static long ArrayBytes(int count, int elementSize)
    {
        if (count == 0)
        {
            return 0;
        }

        var payloadBytes = checked((long)count * elementSize);
        return checked(64L + ((payloadBytes + 15L) & ~15L));
    }

    internal void AddEntity()
    {
        _entityCount = checked(_entityCount + 1);
    }
}
