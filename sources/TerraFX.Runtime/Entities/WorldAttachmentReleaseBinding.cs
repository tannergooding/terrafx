// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

/// <summary>Describes one committed transferred-attachment release obligation.</summary>
public sealed class WorldAttachmentReleaseBinding
{
    private readonly Guid? _ownerEntityId;
    private readonly ulong _releasedInstallationId;
    private readonly ulong? _replacementInstallationId;
    private readonly ContentSchemaReference _schema;
    private readonly AttachmentScope _scope;
    private readonly Guid _worldGuid;

    internal WorldAttachmentReleaseBinding(
        Guid worldGuid,
        AttachmentScope scope,
        ContentSchemaReference schema,
        Guid? ownerEntityId,
        ulong releasedInstallationId,
        ulong? replacementInstallationId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(worldGuid, Guid.Empty);

        if (scope is not AttachmentScope.World and not AttachmentScope.Entity)
        {
            throw new ArgumentOutOfRangeException(nameof(scope));
        }

        if ((schema.Scope != ContentSchemaScope.Content)
            || (schema.SchemaId == Guid.Empty)
            || (schema.Major <= 0)
            || (schema.Minor < 0))
        {
            throw new ArgumentException("An attachment release requires an exact Content schema tuple.", nameof(schema));
        }

        var requiresEntityOwner = scope == AttachmentScope.Entity;

        if (requiresEntityOwner != ownerEntityId.HasValue)
        {
            throw new ArgumentException("The Entity owner identity must be present exactly for Entity-scoped attachments.", nameof(ownerEntityId));
        }

        if (ownerEntityId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(ownerEntityId));
        }

        ArgumentOutOfRangeException.ThrowIfZero(releasedInstallationId);

        if (replacementInstallationId is { } replacement)
        {
            ArgumentOutOfRangeException.ThrowIfZero(replacement);
            ArgumentOutOfRangeException.ThrowIfEqual(replacement, releasedInstallationId, nameof(replacementInstallationId));
        }

        _worldGuid = worldGuid;
        _scope = scope;
        _schema = schema;
        _ownerEntityId = ownerEntityId;
        _releasedInstallationId = releasedInstallationId;
        _replacementInstallationId = replacementInstallationId;
    }

    /// <summary>Gets the stable Entity owner identity, or null for World scope.</summary>
    public Guid? OwnerEntityId => _ownerEntityId;

    /// <summary>Gets the detached installation identity.</summary>
    public ulong ReleasedInstallationId => _releasedInstallationId;

    /// <summary>Gets the committed replacement installation identity, when present.</summary>
    public ulong? ReplacementInstallationId => _replacementInstallationId;

    /// <summary>Gets the exact AttachmentState schema tuple.</summary>
    public ContentSchemaReference Schema => _schema;

    /// <summary>Gets the scope of the detached attachment.</summary>
    public AttachmentScope Scope => _scope;

    /// <summary>Gets the persistent identity of the World that committed the detachment.</summary>
    public Guid WorldGuid => _worldGuid;
}
