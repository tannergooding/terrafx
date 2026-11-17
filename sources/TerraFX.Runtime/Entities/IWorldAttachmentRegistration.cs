// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;

namespace TerraFX.Entities;

internal interface IWorldAttachmentRegistration
{
    WorldAttachmentFactoryDescriptor Descriptor { get; }

    AttachmentScope Scope { get; }

    Type SourceAuthorType { get; }

    Type ValueType { get; }

    nint CountStateRecord(
        object value,
        Guid? ownerEntityId,
        WorldAttachmentCaptureBinding binding,
        CancellationToken cancellationToken);

    object ReadStateRecord(
        scoped ReadOnlySpan<byte> source,
        EbinReadLimits limits,
        CancellationToken cancellationToken);

    void Release(object value, WorldAttachmentReleaseBinding binding);

    object RestoreState(object author, WorldAttachmentRestoreBinding binding, CancellationToken cancellationToken);

    bool TryReadWorldSource(
        IWorldRestoreSourceOwner owner,
        out IWorldPreparedAttachmentSource? source,
        out ImmutableArray<Diagnostic> diagnostics,
        CancellationToken cancellationToken);

    ImmutableArray<Diagnostic> Validate(World world, object value, WorldAttachmentMembershipSource membershipSource);

    void ValidateEntityReferences(
        World world,
        object value,
        Entity excludedEntity,
        WorldAttachmentMembershipSource membershipSource);

    void ValidateEntityReferences(
        World world,
        object value,
        IReadOnlySet<Entity> excludedEntities,
        WorldAttachmentMembershipSource membershipSource);

    void ValidateEntityReferences(World world, object value, WorldAttachmentMembershipSource membershipSource);

    nint WriteStateRecord(
        object value,
        Guid? ownerEntityId,
        scoped Span<byte> destination,
        WorldAttachmentCaptureBinding binding,
        CancellationToken cancellationToken);
}
