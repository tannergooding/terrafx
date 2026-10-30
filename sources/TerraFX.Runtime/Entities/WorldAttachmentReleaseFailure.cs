// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

/// <summary>Contains immutable facts from one failed transferred-attachment Release attempt.</summary>
public sealed class WorldAttachmentReleaseFailure
{
    private readonly WorldAttachmentReleaseFailureControl _control;

    internal WorldAttachmentReleaseFailure(WorldAttachmentReleaseFailureControl control)
    {
        ArgumentNullException.ThrowIfNull(control);
        _control = control;
    }

    /// <summary>Gets the exact original Release exception.</summary>
    public Exception OriginalException => _control.OriginalException;

    /// <summary>Gets the stable Entity owner identity, or null for World scope.</summary>
    public Guid? OwnerEntityId => _control.OwnerEntityId;

    /// <summary>Gets the detached installation identity.</summary>
    public ulong ReleasedInstallationId => _control.ReleasedInstallationId;

    /// <summary>Gets the committed replacement installation identity, when present.</summary>
    public ulong? ReplacementInstallationId => _control.ReplacementInstallationId;

    /// <summary>Gets the exact registered AttachmentState schema tuple.</summary>
    public ContentSchemaReference Schema => _control.Schema;

    /// <summary>Gets the attachment owner scope.</summary>
    public AttachmentScope Scope => _control.Scope;

    /// <summary>Gets the World identity whose Release failed.</summary>
    public Guid WorldGuid => _control.WorldGuid;

    internal void Seal(WorldAttachmentReleaseBinding binding, Exception originalException)
        => _control.Seal(binding, originalException);
}
