// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX.Entities;
using TerraFX.Numerics;

namespace TerraFX.Animation;

internal static class AnimationRuntimeAuthorValidation
{
    internal static ImmutableArray<T> CopyRequired<T>(
        ImmutableArray<T> values,
        int minimumCount,
        int maximumCount,
        string parameterName)
        where T : class
    {
        if (values.IsDefault)
        {
            throw new ArgumentException("Animation author arrays must be initialized.", parameterName);
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(values.Length, minimumCount, parameterName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(values.Length, maximumCount, parameterName);

        for (var index = 0; index < values.Length; index++)
        {
            if (values[index] is null)
            {
                throw new ArgumentException("Animation author arrays cannot contain null entries.", parameterName);
            }
        }

        return ImmutableArrayCopy.Create(values);
    }

    internal static void ValidateLocalPose(Vector3 position, Quaternion rotation, Vector3 scale)
    {
        if (!position.IsFinite().All())
        {
            throw new ArgumentException("An Animation local-pose position must be finite.", nameof(position));
        }

        var rotationLengthSquared = Quaternion.Dot(rotation, rotation);

        if (!rotation.IsFinite().All()
            || !float.IsFinite(rotationLengthSquared)
            || (rotationLengthSquared <= 1.0e-12f))
        {
            throw new ArgumentException("An Animation local-pose rotation must have a finite nonzero norm.", nameof(rotation));
        }

        if (!scale.IsFinite().All()
            || (float.Abs(scale.X) < 0.0001f)
            || (float.Abs(scale.X) > 1000.0f)
            || (float.Abs(scale.Y) < 0.0001f)
            || (float.Abs(scale.Y) > 1000.0f)
            || (float.Abs(scale.Z) < 0.0001f)
            || (float.Abs(scale.Z) > 1000.0f))
        {
            throw new ArgumentException("An Animation local-pose scale must be finite with component magnitudes in [0.0001, 1000].", nameof(scale));
        }
    }
}
