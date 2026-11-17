// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX.Entities;

namespace TerraFX.Animation;

internal sealed class RootMotionResourceAuthor
{
    internal RootMotionResourceAuthor(ImmutableArray<RootMotionSlotAuthor> values)
    {
        if (values.IsDefault || (values.Length != 4096))
        {
            throw new ArgumentOutOfRangeException(nameof(values), "Root-motion resource authors require exactly 4096 initialized slots.");
        }

        foreach (var value in values)
        {
            ArgumentNullException.ThrowIfNull(value);
        }

        Values = ImmutableArrayCopy.Create(values);
    }

    internal ImmutableArray<RootMotionSlotAuthor> Values { get; }
}
