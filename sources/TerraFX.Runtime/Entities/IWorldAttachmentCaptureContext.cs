// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

internal interface IWorldAttachmentCaptureContext
{
    Exception? Failure { get; }

    bool IsActive { get; }

    int OwnerThreadId { get; }

    Guid GetEntityId(WorldAttachmentFieldDescriptor referenceField, Entity entity);

    Exception RecordFailure(Exception exception);
}
