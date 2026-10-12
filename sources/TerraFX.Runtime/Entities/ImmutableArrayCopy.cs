// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Collections.Immutable;

namespace TerraFX.Entities;

internal static class ImmutableArrayCopy
{
    internal static ImmutableArray<T> Create<T>(ImmutableArray<T> source) => [.. source];
}
