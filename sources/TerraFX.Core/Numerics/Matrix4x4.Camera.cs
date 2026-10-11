// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Numerics;

public readonly partial struct Matrix4x4
{
    /// <summary>Creates the explicit infinite-far left-handed reversed-depth perspective.</summary>
    /// <param name="fieldOfView">The finite vertical field of view in (0, pi) radians.</param>
    /// <param name="aspectRatio">The positive finite width-to-height ratio.</param>
    /// <param name="nearPlane">The positive finite near distance.</param>
    /// <exception cref="ArgumentException">An argument is nonfinite or the derived matrix/inverse is not admitted.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An argument is outside its scalar range.</exception>
    public static Matrix4x4 CreateInfinitePerspectiveFieldOfView(float fieldOfView, float aspectRatio, float nearPlane)
    {
        var scale = PerspectiveScale(fieldOfView, aspectRatio, nearPlane);
        var result = new Matrix4x4(new Vector4(scale.X, 0.0f, 0.0f, 0.0f), new Vector4(0.0f, scale.Y, 0.0f, 0.0f), new Vector4(0.0f, 0.0f, 0.0f, 1.0f), new Vector4(0.0f, 0.0f, nearPlane, 0.0f));
        ValidateProjection(result, nameof(nearPlane));
        return result;
    }

    /// <summary>Creates a view from the exact original target-position difference, before binary32 rounding.</summary>
    /// <exception cref="ArgumentException">An input is nonfinite or no finite representable frame exists.</exception>
    public static Matrix4x4 CreateLookAt(Vector3 position, Vector3 target, Vector3 up)
    {
        if (!TryCreateLookAtCore(position, target, up, out var view, out var failureParameter))
        {
            ThrowHelper.ThrowArgumentException("The sources cannot form a representable view.", failureParameter);
        }

        return view;
    }

    /// <summary>Creates a finite proper left-handed view using exact source degeneracy predicates.</summary>
    /// <exception cref="ArgumentException">An input is nonfinite or no finite representable frame exists.</exception>
    public static Matrix4x4 CreateLookTo(Vector3 position, Vector3 direction, Vector3 up)
    {
        if (!TryCreateLookToCore(position, direction, up, out var view, out var failureParameter))
        {
            ThrowHelper.ThrowArgumentException("The sources cannot form a representable view.", failureParameter);
        }

        return view;
    }

    /// <summary>Creates left-handed reversed-depth orthographic projection with signed ordered near/far planes.</summary>
    /// <param name="width">The positive finite width.</param>
    /// <param name="height">The positive finite height.</param>
    /// <param name="nearPlane">The finite signed near plane.</param>
    /// <param name="farPlane">The finite signed far plane greater than the near plane.</param>
    /// <exception cref="ArgumentException">An argument is nonfinite or the derived matrix/inverse is not admitted.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An argument is outside its scalar range.</exception>
    public static Matrix4x4 CreateOrthographic(float width, float height, float nearPlane, float farPlane)
    {
        ThrowHelper.ThrowIfNotFinite(width);
        ThrowHelper.ThrowIfNotFinite(height);
        ThrowHelper.ThrowIfNotFinite(nearPlane);
        ThrowHelper.ThrowIfNotFinite(farPlane);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(farPlane, nearPlane);

        if (!GeometryKernel.TryOrthographicDepth(nearPlane, farPlane, out var depth, out var offset))
        {
            throw new ArgumentException("The projection depth is not representable.", nameof(farPlane));
        }

        var xScale = 2.0f / width;
        var yScale = 2.0f / height;

        if (!float.IsFinite(xScale))
        {
            ThrowHelper.ThrowArgumentException("The projection scale must be finite.", nameof(width));
        }

        if (!float.IsFinite(yScale))
        {
            ThrowHelper.ThrowArgumentException("The projection scale must be finite.", nameof(height));
        }

        var result = new Matrix4x4(new Vector4(xScale, 0.0f, 0.0f, 0.0f), new Vector4(0.0f, yScale, 0.0f, 0.0f), new Vector4(0.0f, 0.0f, depth, 0.0f), new Vector4(0.0f, 0.0f, offset, 1.0f));
        ValidateProjection(result, nameof(width));
        return result;
    }

    /// <summary>Creates a finite-far left-handed perspective with reversed depth in [0, 1].</summary>
    /// <param name="fieldOfView">The finite vertical field of view in (0, pi) radians.</param>
    /// <param name="aspectRatio">The positive finite width-to-height ratio.</param>
    /// <param name="nearPlane">The positive finite near distance.</param>
    /// <param name="farPlane">The finite far distance greater than the near distance.</param>
    /// <exception cref="ArgumentException">An argument is nonfinite or the derived matrix/inverse is not admitted.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An argument is outside its scalar range.</exception>
    public static Matrix4x4 CreatePerspectiveFieldOfView(float fieldOfView, float aspectRatio, float nearPlane, float farPlane)
    {
        var scale = PerspectiveScale(fieldOfView, aspectRatio, nearPlane);
        ThrowHelper.ThrowIfNotFinite(farPlane);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(farPlane, nearPlane);

        if (!GeometryKernel.TryPerspectiveDepth(nearPlane, farPlane, out var depth, out var offset))
        {
            throw new ArgumentException("The projection depth is not representable.", nameof(farPlane));
        }

        var result = new Matrix4x4(new Vector4(scale.X, 0.0f, 0.0f, 0.0f), new Vector4(0.0f, scale.Y, 0.0f, 0.0f), new Vector4(0.0f, 0.0f, depth, 1.0f), new Vector4(0.0f, 0.0f, offset, 0.0f));
        ValidateProjection(result, nameof(farPlane));
        return result;
    }

    /// <summary>Tests exact source degeneracy before rounding the target-position direction.</summary>
    /// <returns>False with default output only when no representable proper view exists.</returns>
    /// <exception cref="ArgumentException">An input is nonfinite.</exception>
    public static bool TryCreateLookAt(Vector3 position, Vector3 target, Vector3 up, out Matrix4x4 view)
    {
        return TryCreateLookAtCore(position, target, up, out view, out _);
    }

    /// <summary>Returns false with default output only when finite sources cannot form a representable proper view.</summary>
    /// <exception cref="ArgumentException">An input is nonfinite.</exception>
    public static bool TryCreateLookTo(Vector3 position, Vector3 direction, Vector3 up, out Matrix4x4 view)
    {
        return TryCreateLookToCore(position, direction, up, out view, out _);
    }

    private static Vector2 PerspectiveScale(float fieldOfView, float aspectRatio, float nearPlane)
    {
        ThrowHelper.ThrowIfNotFinite(fieldOfView);
        ThrowHelper.ThrowIfNotFinite(aspectRatio);
        ThrowHelper.ThrowIfNotFinite(nearPlane);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fieldOfView);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(fieldOfView, float.Pi);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(aspectRatio);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(nearPlane);

        var tangent = float.Tan(fieldOfView * 0.5f);

        if (!float.IsFinite(tangent) || tangent <= 0.0f)
        {
            ThrowHelper.ThrowArgumentException("The field of view cannot produce representable projection scales.", nameof(fieldOfView));
        }

        if (!GeometryKernel.TryPerspectiveScale(aspectRatio, tangent, out var scale))
        {
            throw new ArgumentException("The projection scale is not representable.", nameof(aspectRatio));
        }

        return scale;
    }

    private static bool TryCreateLookAtCore(Vector3 position, Vector3 target, Vector3 up, out Matrix4x4 view, out string failureParameter)
    {
        ThrowHelper.ThrowIfNotFinite(position);
        ThrowHelper.ThrowIfNotFinite(target);
        ThrowHelper.ThrowIfNotFinite(up);

        return GeometryKernel.TryLookAt(position, target, up, out view, out failureParameter);
    }

    private static bool TryCreateLookToCore(Vector3 position, Vector3 direction, Vector3 up, out Matrix4x4 view, out string failureParameter)
    {
        ThrowHelper.ThrowIfNotFinite(position);
        ThrowHelper.ThrowIfNotFinite(direction);
        ThrowHelper.ThrowIfNotFinite(up);

        return GeometryKernel.TryLookTo(position, direction, up, out view, out failureParameter);
    }

    private static void ValidateProjection(Matrix4x4 projection, string parameter)
    {
        if (!projection.IsFinite().All() || !TryInvert(projection, out _))
        {
            ThrowHelper.ThrowArgumentException("The derived projection has no admitted finite inverse.", parameter);
        }
    }
}
