// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Entities;

internal interface IWorldSystemValidationContext
{
    Exception? Failure { get; }

    bool IsActive { get; }

    int OwnerThreadId { get; }

    ContentSchemaReference Schema { get; }

    Guid SystemId { get; }

    Exception RecordFailure(Exception exception);

    ImmutableArray<Diagnostic> ValidateFields<TSystem>(TSystem system)
        where TSystem : class, IWorldSystem;
}
