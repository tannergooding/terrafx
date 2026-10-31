// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX;

namespace TerraFX.Scenes;

/// <summary>Defines per-Scene preparation limits and root external-slot bindings.</summary>
public sealed class SceneLoadOptions
{
    /// <summary>Initializes the default Scene loading options.</summary>
    public SceneLoadOptions()
    {
    }

    /// <summary>Gets the copied input bindings for root Scene external slots.</summary>
    public ImmutableArray<SceneExternalBinding> ExternalBindings { get; init; } = [];

    /// <summary>Gets the maximum expanded Entity count, up to 1048576.</summary>
    public int MaximumExpandedEntities { get; init; } = 1_048_576;

    /// <summary>Gets the maximum retained occurrence count, up to 1048576.</summary>
    public int MaximumExpandedOccurrences { get; init; } = 1_048_576;

    /// <summary>Gets the maximum Entity count in one Prefab, up to 65536.</summary>
    public int MaximumPrefabEntities { get; init; } = 65_536;

    /// <summary>Gets the maximum nested Prefab depth, up to 32.</summary>
    public int MaximumPrefabDepth { get; init; } = 32;

    /// <summary>Gets the maximum hierarchy depth, up to 256.</summary>
    public int MaximumHierarchyDepth { get; init; } = 256;

    /// <summary>Gets the optional requested Scene instance identity.</summary>
    public Guid? SceneInstanceId { get; init; }

    private static int CompareBindings(SceneExternalBinding left, SceneExternalBinding right)
        => FoundationCatalogue.CompareRfc(left.SlotId, right.SlotId);

    private static bool IsUuidV8(Guid value)
        => (value.ToByteArray(bigEndian: true)[6] & 0xF0) == 0x80;

    private static void ValidateLimit(int value, int maximum, string parameterName)
    {
        if ((value <= 0) || (value > maximum))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    internal SceneLoadOptionsSnapshot ValidateAndFreeze()
    {
        ValidateLimit(MaximumExpandedEntities, 1_048_576, nameof(MaximumExpandedEntities));
        ValidateLimit(MaximumExpandedOccurrences, 1_048_576, nameof(MaximumExpandedOccurrences));
        ValidateLimit(MaximumPrefabEntities, 65_536, nameof(MaximumPrefabEntities));
        ValidateLimit(MaximumPrefabDepth, 32, nameof(MaximumPrefabDepth));
        ValidateLimit(MaximumHierarchyDepth, 256, nameof(MaximumHierarchyDepth));

        if (SceneInstanceId is Guid sceneInstanceId
            && ((sceneInstanceId == Guid.Empty) || IsUuidV8(sceneInstanceId)))
        {
            throw new ArgumentException("A requested Scene instance identity must be nonempty and non-v8.", nameof(SceneInstanceId));
        }

        if (ExternalBindings.IsDefault)
        {
            throw new ArgumentException("The external-binding array must be initialized.", nameof(ExternalBindings));
        }

        var externalBindings = ExternalBindings.AsSpan().ToArray();

        for (var index = 0; index < externalBindings.Length; index++)
        {
            if (externalBindings[index] is null)
            {
                throw new ArgumentException("The external-binding array cannot contain null.", nameof(ExternalBindings));
            }
        }

        Array.Sort(externalBindings, CompareBindings);

        for (var index = 1; index < externalBindings.Length; index++)
        {
            if (externalBindings[index - 1].SlotId == externalBindings[index].SlotId)
            {
                throw new ArgumentException("External slot bindings must have unique SlotIds.", nameof(ExternalBindings));
            }
        }

        return new SceneLoadOptionsSnapshot(
            SceneInstanceId,
            [.. externalBindings],
            MaximumExpandedEntities,
            MaximumExpandedOccurrences,
            MaximumPrefabEntities,
            MaximumPrefabDepth,
            MaximumHierarchyDepth);
    }
}
