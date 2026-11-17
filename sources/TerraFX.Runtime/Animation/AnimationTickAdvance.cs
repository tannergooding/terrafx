// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Animation;

internal readonly record struct AnimationTickAdvance(long DeltaTicks, AnimationTickRemainder192 Remainder);
