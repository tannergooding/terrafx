// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

internal readonly struct WorldPersistenceEntityWorkspaceRequirements
{
    internal WorldPersistenceEntityWorkspaceRequirements(
        int componentCount,
        int tagCount,
        int transientSchemaCount,
        bool hasSceneInstance,
        int componentStorageBytes,
        int tagStorageBytes,
        int transientSchemaStorageBytes,
        int componentArrayPayloadBytes,
        int tagArrayPayloadBytes,
        int transientSchemaArrayPayloadBytes,
        int childCandidateBytes,
        int payloadStorageBytes,
        int recordBytes)
    {
        ChildCandidateBytes = childCandidateBytes;
        ComponentArrayPayloadBytes = componentArrayPayloadBytes;
        ComponentCount = componentCount;
        ComponentStorageBytes = componentStorageBytes;
        HasSceneInstance = hasSceneInstance;
        PayloadStorageBytes = payloadStorageBytes;
        RecordBytes = recordBytes;
        TagArrayPayloadBytes = tagArrayPayloadBytes;
        TagCount = tagCount;
        TagStorageBytes = tagStorageBytes;
        TransientSchemaArrayPayloadBytes = transientSchemaArrayPayloadBytes;
        TransientSchemaCount = transientSchemaCount;
        TransientSchemaStorageBytes = transientSchemaStorageBytes;
    }

    internal int ChildCandidateBytes { get; }

    internal int ComponentArrayPayloadBytes { get; }

    internal int ComponentCount { get; }

    internal int ComponentStorageBytes { get; }

    internal bool HasSceneInstance { get; }

    internal int PayloadStorageBytes { get; }

    internal int RecordBytes { get; }

    internal int TagArrayPayloadBytes { get; }

    internal int TagCount { get; }

    internal int TagStorageBytes { get; }

    internal int TransientSchemaArrayPayloadBytes { get; }

    internal int TransientSchemaCount { get; }

    internal int TransientSchemaStorageBytes { get; }
}
