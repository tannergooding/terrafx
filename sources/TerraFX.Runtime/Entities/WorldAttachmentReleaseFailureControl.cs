// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

internal sealed class WorldAttachmentReleaseFailureControl
{
    private bool _isSealed;
    private Exception? _originalException;
    private Guid? _ownerEntityId;
    private ulong _releasedInstallationId;
    private ulong? _replacementInstallationId;
    private ContentSchemaReference _schema;
    private AttachmentScope _scope;
    private Guid _worldGuid;

    internal Exception OriginalException
    {
        get
        {
            ThrowIfUnsealed();
            return _originalException
                ?? throw new InvalidOperationException("The attachment release failure has no original exception.");
        }
    }

    internal Guid? OwnerEntityId
    {
        get
        {
            ThrowIfUnsealed();
            return _ownerEntityId;
        }
    }

    internal ulong ReleasedInstallationId
    {
        get
        {
            ThrowIfUnsealed();
            return _releasedInstallationId;
        }
    }

    internal ulong? ReplacementInstallationId
    {
        get
        {
            ThrowIfUnsealed();
            return _replacementInstallationId;
        }
    }

    internal ContentSchemaReference Schema
    {
        get
        {
            ThrowIfUnsealed();
            return _schema;
        }
    }

    internal AttachmentScope Scope
    {
        get
        {
            ThrowIfUnsealed();
            return _scope;
        }
    }

    internal Guid WorldGuid
    {
        get
        {
            ThrowIfUnsealed();
            return _worldGuid;
        }
    }

    internal void Seal(WorldAttachmentReleaseBinding binding, Exception originalException)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(originalException);

        if (_isSealed)
        {
            throw new InvalidOperationException("The prepared attachment release failure is already sealed.");
        }

        _worldGuid = binding.WorldGuid;
        _scope = binding.Scope;
        _schema = binding.Schema;
        _ownerEntityId = binding.OwnerEntityId;
        _releasedInstallationId = binding.ReleasedInstallationId;
        _replacementInstallationId = binding.ReplacementInstallationId;
        _originalException = originalException;
        _isSealed = true;
    }

    private void ThrowIfUnsealed()
    {
        if (!_isSealed)
        {
            throw new InvalidOperationException("The attachment release failure has not been sealed.");
        }
    }
}
