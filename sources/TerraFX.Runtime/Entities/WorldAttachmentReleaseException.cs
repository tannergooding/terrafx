// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace TerraFX.Entities;

/// <summary>Reports failures releasing detached transferred attachments after committed storage changes.</summary>
[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "Attachment release exceptions are engine-issued and require their immutable failure report.")]
public sealed class WorldAttachmentReleaseException : InvalidOperationException
{
    private readonly bool _storageCommitted;

    internal WorldAttachmentReleaseException(
        WorldAttachmentReleaseFailure primaryFailure,
        ImmutableArray<WorldAttachmentReleaseFailure> additionalFailures,
        long omittedFailureCount,
        WorldSystemCleanupReport? systemCleanupReport)
        : base(
            "One or more transferred attachments failed to release.",
            primaryFailure is null
                ? throw new ArgumentNullException(nameof(primaryFailure))
                : primaryFailure.OriginalException)
    {
        ArgumentNullException.ThrowIfNull(primaryFailure);
        ArgumentOutOfRangeException.ThrowIfNegative(omittedFailureCount);

        if (additionalFailures.IsDefault)
        {
            throw new ArgumentException("Additional attachment release failures must be initialized.", nameof(additionalFailures));
        }

        foreach (var failure in additionalFailures)
        {
            ArgumentNullException.ThrowIfNull(failure);
        }

        PrimaryFailure = primaryFailure;
        AdditionalFailures = ImmutableArrayCopy.Create(additionalFailures);
        OmittedFailureCount = omittedFailureCount;
        SystemCleanupReport = systemCleanupReport;
        FailureCount = checked(1L + AdditionalFailures.Length + omittedFailureCount);
        _storageCommitted = true;
    }

    /// <summary>Gets the bounded additional attachment release failures after the primary failure.</summary>
    public ImmutableArray<WorldAttachmentReleaseFailure> AdditionalFailures { get; }

    /// <summary>Gets the total number of failed Release callbacks.</summary>
    public long FailureCount { get; }

    /// <summary>Gets the number of failed Release callbacks omitted from the bounded details.</summary>
    public long OmittedFailureCount { get; }

    /// <summary>Gets the stable Entity owner identity, or null for World scope.</summary>
    public Guid? OwnerEntityId => PrimaryFailure.OwnerEntityId;

    /// <summary>Gets the primary failed Release attempt.</summary>
    public WorldAttachmentReleaseFailure PrimaryFailure { get; }

    /// <summary>Gets the detached installation identity.</summary>
    public ulong ReleasedInstallationId => PrimaryFailure.ReleasedInstallationId;

    /// <summary>Gets the committed replacement installation identity, when present.</summary>
    public ulong? ReplacementInstallationId => PrimaryFailure.ReplacementInstallationId;

    /// <summary>Gets the exact registered AttachmentState schema tuple.</summary>
    public ContentSchemaReference Schema => PrimaryFailure.Schema;

    /// <summary>Gets the attachment owner scope.</summary>
    public AttachmentScope Scope => PrimaryFailure.Scope;

    /// <summary>Gets whether the storage detachment committed before Release failed.</summary>
    public bool StorageCommitted => _storageCommitted;

    /// <summary>Gets the sealed World-system cleanup report when disposal also released systems.</summary>
    public WorldSystemCleanupReport? SystemCleanupReport { get; }

    /// <summary>Gets the World identity whose Release callback failed.</summary>
    public Guid WorldGuid => PrimaryFailure.WorldGuid;
}
