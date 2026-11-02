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

    Type ValueType { get; }

    nint CountStateRecord(
        object value,
        Guid? ownerEntityId,
        WorldAttachmentCaptureBinding binding,
        CancellationToken cancellationToken);

    void Release(object value, WorldAttachmentReleaseBinding binding);

    ImmutableArray<Diagnostic> Validate(World world, object value);

    void ValidateEntityReferences(World world, object value, Entity excludedEntity);

    void ValidateEntityReferences(World world, object value, IReadOnlySet<Entity> excludedEntities);

    nint WriteStateRecord(
        object value,
        Guid? ownerEntityId,
        scoped Span<byte> destination,
        WorldAttachmentCaptureBinding binding,
        CancellationToken cancellationToken);
}
