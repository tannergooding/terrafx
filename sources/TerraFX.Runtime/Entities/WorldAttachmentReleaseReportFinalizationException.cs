// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;

namespace TerraFX.Entities;

/// <summary>Preserves the committed attachment release facts when normal report finalization fails.</summary>
[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "This carrier is preconstructed before attachment storage commit and exposes its sealed failure facts.")]
public sealed class WorldAttachmentReleaseReportFinalizationException : InvalidOperationException
{
    private readonly WorldAttachmentReleaseReportFaultControl _control;
    private readonly bool _storageCommitted;

    internal WorldAttachmentReleaseReportFinalizationException(WorldAttachmentReleaseReportFaultControl control)
    {
        ArgumentNullException.ThrowIfNull(control);
        _control = control;
        _storageCommitted = true;
    }

    /// <summary>Gets the committed canonical mutation revision.</summary>
    public long CommittedMutationRevision
    {
        get
        {
            _control.ThrowIfUnsealed();
            return _control.CommittedMutationRevision;
        }
    }

    /// <summary>Gets the exact original Release exception.</summary>
    public Exception OriginalReleaseException => _control.OriginalReleaseException;

    /// <summary>Gets the stable Entity owner identity, or null for World scope.</summary>
    public Guid? OwnerEntityId
    {
        get
        {
            _control.ThrowIfUnsealed();
            return _control.OwnerEntityId;
        }
    }

    /// <summary>Gets the number of failed Release callbacks.</summary>
    public long ReleaseFailureCount
    {
        get
        {
            _control.ThrowIfUnsealed();
            return _control.ReleaseFailureCount;
        }
    }

    /// <summary>Gets the detached installation identity.</summary>
    public ulong ReleasedInstallationId
    {
        get
        {
            _control.ThrowIfUnsealed();
            return _control.ReleasedInstallationId;
        }
    }

    /// <summary>Gets the committed replacement installation identity, when present.</summary>
    public ulong? ReplacementInstallationId
    {
        get
        {
            _control.ThrowIfUnsealed();
            return _control.ReplacementInstallationId;
        }
    }

    /// <summary>Gets the finalization exception from normal report construction.</summary>
    public Exception ReportFinalizationException => _control.ReportFinalizationException;

    /// <summary>Gets the exact registered AttachmentState schema tuple.</summary>
    public ContentSchemaReference Schema
    {
        get
        {
            _control.ThrowIfUnsealed();
            return _control.Schema;
        }
    }

    /// <summary>Gets the attachment owner scope.</summary>
    public AttachmentScope Scope
    {
        get
        {
            _control.ThrowIfUnsealed();
            return _control.Scope;
        }
    }

    /// <summary>Gets whether attachment storage committed before cleanup failure.</summary>
    public bool StorageCommitted => _storageCommitted;

    /// <summary>Gets the sealed World-system cleanup report, when present.</summary>
    public WorldSystemCleanupReport? SystemCleanupReport
    {
        get
        {
            _control.ThrowIfUnsealed();
            return _control.SystemCleanupReport;
        }
    }

    /// <summary>Gets the World identity whose Release callback failed.</summary>
    public Guid WorldGuid
    {
        get
        {
            _control.ThrowIfUnsealed();
            return _control.WorldGuid;
        }
    }
}
