// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Runtime.CompilerServices;

namespace TerraFX.Numerics;

/// <summary>Provides exact, independent by-value interchange with the six BCL geometry types.</summary>
public static class SystemNumericsExtensions
{
    /// <summary>Copies six row-ordered components into three engine rows.</summary>
    /// <param name="value">The BCL value to copy.</param>
    /// <returns>An independent matrix with identical component bits and basis.</returns>
    public static Matrix3x2 AsTerraFXMatrix3x2(this System.Numerics.Matrix3x2 value)
    {
        unsafe
        {
            return Unsafe.BitCast<System.Numerics.Matrix3x2, Matrix3x2>(value);
        }
    }

    /// <summary>Copies sixteen row-ordered components without transpose, basis, or depth conversion.</summary>
    /// <param name="value">The BCL value to copy.</param>
    /// <returns>An independent matrix with identical component bits and conventions.</returns>
    public static Matrix4x4 AsTerraFXMatrix4x4(this System.Numerics.Matrix4x4 value)
    {
        unsafe
        {
            return Unsafe.BitCast<System.Numerics.Matrix4x4, Matrix4x4>(value);
        }
    }

    /// <summary>Copies X, Y, Z, W without normalization or sign canonicalization.</summary>
    /// <param name="value">The BCL value to copy.</param>
    /// <returns>An engine quaternion with identical component bits.</returns>
    public static Quaternion AsTerraFXQuaternion(this System.Numerics.Quaternion value)
    {
        unsafe
        {
            return Unsafe.BitCast<System.Numerics.Quaternion, Quaternion>(value);
        }
    }

    /// <summary>Copies two binary32 components without validation or arithmetic.</summary>
    /// <param name="value">The BCL value to copy.</param>
    /// <returns>An engine vector with identical component bits.</returns>
    public static Vector2 AsTerraFXVector2(this System.Numerics.Vector2 value)
    {
        return new Vector2(value);
    }

    /// <summary>Copies exactly three binary32 components, without reading a fourth lane.</summary>
    /// <param name="value">The BCL value to copy.</param>
    /// <returns>An engine vector with identical component bits.</returns>
    public static Vector3 AsTerraFXVector3(this System.Numerics.Vector3 value)
    {
        return new Vector3(value);
    }

    /// <summary>Copies X, Y, Z, W into the engine vector's four direct lanes.</summary>
    /// <param name="value">The BCL value to copy.</param>
    /// <returns>An engine vector with identical component bits.</returns>
    public static Vector4 AsTerraFXVector4(this System.Numerics.Vector4 value)
    {
        unsafe
        {
            return Unsafe.BitCast<System.Numerics.Vector4, Vector4>(value);
        }
    }
}
