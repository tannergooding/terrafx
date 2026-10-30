// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Entities;

internal interface IWorldAttachmentValidationContext
{
    Exception? Failure { get; }

    bool IsActive { get; }

    int OwnerThreadId { get; }

    Exception RecordFailure(Exception exception);

    void ValidateEntity(Entity entity);

    ImmutableArray<Diagnostic> ValidateFields<T>(T value)
        where T : class;
}
