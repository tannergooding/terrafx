// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace TerraFX.Entities;

internal sealed class WorldArchetype
{
    private const long ControlBytes = 256;

    private readonly Guid[] _schemaIds;

    internal WorldArchetype(
        uint ownerId,
        int index,
        int maximumEntityCount,
        ImmutableArray<IComponentRegistration> components,
        ImmutableArray<Guid> tags)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        if (components.IsDefault || tags.IsDefault)
        {
            throw new ArgumentException("Archetype membership arrays must be initialized.");
        }

        Index = index;
        Components = components;
        Tags = tags;
        Storage = new EntityArchetypeStorage(ownerId, index, maximumEntityCount, components);
        _schemaIds = new Guid[checked(components.Length + tags.Length)];

        for (var componentIndex = 0; componentIndex < components.Length; componentIndex++)
        {
            _schemaIds[componentIndex] = components[componentIndex].SchemaReference.SchemaId;
        }

        for (var tagIndex = 0; tagIndex < tags.Length; tagIndex++)
        {
            _schemaIds[components.Length + tagIndex] = tags[tagIndex];
        }

        Array.Sort(_schemaIds, FoundationCatalogue.CompareRfc);

        for (var schemaIndex = 1; schemaIndex < _schemaIds.Length; schemaIndex++)
        {
            if (_schemaIds[schemaIndex] == _schemaIds[schemaIndex - 1])
            {
                throw new ArgumentException("Archetype component and tag identities must be unique.", nameof(tags));
            }
        }
    }

    internal ImmutableArray<IComponentRegistration> Components { get; }

    internal int Index { get; }

    internal long MetadataByteReservation
        => GetMetadataByteReservation(Components.Length, Tags.Length);

    internal EntityArchetypeStorage Storage { get; }

    internal ImmutableArray<Guid> Tags { get; }

    internal static long GetMetadataByteReservation(int componentCount, int tagCount)
    {
        var schemaCount = checked(componentCount + tagCount);
        var bytes = ControlBytes;

        if (componentCount != 0)
        {
            bytes = checked(bytes + ArrayBytes(componentCount, IntPtr.Size));
        }

        if (tagCount != 0)
        {
            bytes = checked(bytes + ArrayBytes(tagCount, Unsafe.SizeOf<Guid>()));
        }

        if (schemaCount != 0)
        {
            bytes = checked(bytes + ArrayBytes(schemaCount, Unsafe.SizeOf<Guid>()));
        }

        return bytes;
    }

    private static long ArrayBytes(int capacity, int stride)
    {
        var payloadBytes = checked((long)capacity * stride);
        var roundedBytes = checked((payloadBytes + 7L) & ~7L);
        return checked(64L + roundedBytes);
    }

    internal int CompareKey(WorldArchetype other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var sharedLength = Math.Min(_schemaIds.Length, other._schemaIds.Length);

        for (var index = 0; index < sharedLength; index++)
        {
            var comparison = FoundationCatalogue.CompareRfc(_schemaIds[index], other._schemaIds[index]);

            if (comparison != 0)
            {
                return comparison;
            }
        }

        return _schemaIds.Length.CompareTo(other._schemaIds.Length);
    }

    internal void DiscardUncommittedStorage()
        => Storage.DiscardUncommittedStorage();

    internal int GetComponentIndex(Type componentType)
    {
        ArgumentNullException.ThrowIfNull(componentType);

        for (var index = 0; index < Components.Length; index++)
        {
            if (Components[index].ComponentType == componentType)
            {
                return index;
            }
        }

        return -1;
    }

    internal bool HasSchema(Guid schemaId)
    {
        foreach (var schema in _schemaIds)
        {
            if (schema == schemaId)
            {
                return true;
            }
        }

        return false;
    }

    internal bool HasTag(Guid schemaId)
    {
        foreach (var tag in Tags)
        {
            if (tag == schemaId)
            {
                return true;
            }
        }

        return false;
    }

    internal bool MatchesComponentsAndTags(ImmutableArray<IComponentRegistration> components, ImmutableArray<Guid> tags)
    {
        if ((components.Length != Components.Length) || (tags.Length != Tags.Length))
        {
            return false;
        }

        for (var index = 0; index < components.Length; index++)
        {
            if (components[index].SchemaReference.SchemaId != Components[index].SchemaReference.SchemaId)
            {
                return false;
            }
        }

        for (var index = 0; index < tags.Length; index++)
        {
            if (tags[index] != Tags[index])
            {
                return false;
            }
        }

        return true;
    }

    internal bool MatchesDescription(EntityDescription description)
    {
        if ((description.Components.Length != Components.Length) || (description.Tags.Length != Tags.Length))
        {
            return false;
        }

        foreach (var component in Components)
        {
            var found = false;

            foreach (var initialization in description.Components)
            {
                if (initialization.ComponentType == component.ComponentType)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return false;
            }
        }

        foreach (var tag in Tags)
        {
            var found = false;

            foreach (var requestedTag in description.Tags)
            {
                if (requestedTag == tag)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return false;
            }
        }

        return true;
    }
}
