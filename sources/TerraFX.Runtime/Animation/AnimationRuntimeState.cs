// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Animation;

internal sealed class AnimationRuntimeState
{
    private readonly AnimationRuntimeAuthor _value;

    internal AnimationRuntimeState(AnimationRuntimeAuthor value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _value = value;
    }

    internal AnimationRuntimeAuthor Value => _value;
}
