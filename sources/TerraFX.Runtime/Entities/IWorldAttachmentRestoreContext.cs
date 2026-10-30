// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

internal interface IWorldAttachmentRestoreContext
{
    Exception? Failure { get; }

    bool IsActive { get; }

    int OwnerThreadId { get; }

    Exception RecordFailure(Exception exception);

    Entity ResolveEntity(WorldAttachmentFieldDescriptor referenceField, Guid targetEntityId);
}
