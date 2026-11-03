// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

internal readonly struct WorldPersistenceRegistrationWorkspaceRequirements
{
    internal WorldPersistenceRegistrationWorkspaceRequirements(
        int componentsArrayPayloadBytes,
        int tagsArrayPayloadBytes,
        int systemsArrayPayloadBytes,
        int resourcesArrayPayloadBytes,
        int rootPayloadBytes,
        int recordBytes)
    {
        ComponentsArrayPayloadBytes = componentsArrayPayloadBytes;
        RecordBytes = recordBytes;
        ResourcesArrayPayloadBytes = resourcesArrayPayloadBytes;
        RootPayloadBytes = rootPayloadBytes;
        SystemsArrayPayloadBytes = systemsArrayPayloadBytes;
        TagsArrayPayloadBytes = tagsArrayPayloadBytes;
        WorkspaceBytes = checked(
            (long)componentsArrayPayloadBytes
            + tagsArrayPayloadBytes
            + systemsArrayPayloadBytes
            + resourcesArrayPayloadBytes
            + rootPayloadBytes
            + (2L * recordBytes));
    }

    internal int ComponentsArrayPayloadBytes { get; }

    internal int RecordBytes { get; }

    internal int ResourcesArrayPayloadBytes { get; }

    internal int RootPayloadBytes { get; }

    internal int SystemsArrayPayloadBytes { get; }

    internal int TagsArrayPayloadBytes { get; }

    internal long WorkspaceBytes { get; }

    internal bool Matches(WorldPersistenceRegistrationWorkspaceRequirements other)
        => (ComponentsArrayPayloadBytes == other.ComponentsArrayPayloadBytes)
        && (RecordBytes == other.RecordBytes)
        && (ResourcesArrayPayloadBytes == other.ResourcesArrayPayloadBytes)
        && (RootPayloadBytes == other.RootPayloadBytes)
        && (SystemsArrayPayloadBytes == other.SystemsArrayPayloadBytes)
        && (TagsArrayPayloadBytes == other.TagsArrayPayloadBytes)
        && (WorkspaceBytes == other.WorkspaceBytes);
}
