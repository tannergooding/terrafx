// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Collections.Immutable;

namespace TerraFX.Entities;

internal interface IComponentValidationContext : IComponentProviderScope
{
    ImmutableArray<Diagnostic> ValidateFields<TValue>(in TValue value)
        where TValue : unmanaged;
}
