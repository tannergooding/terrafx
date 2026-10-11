// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using TerraFX.Numerics;

namespace TerraFX;

internal static class ThrowHelper
{
    // Rounded lengths within 2e-6 span 1 - 33*2^-24 through 1 + 16*2^-23.
    // Squaring their sqrt-rounding midpoints admits binary32 inputs 0x3F7FFFBE..0x3F800021,
    // equivalently an absolute squared-length residual of at most 33*2^-23.
    private const float UnitAxisSquaredLengthTolerance = 3.9339066e-6f;

    [DoesNotReturn]
    internal static void ThrowAggregateException(Exception innerException, Exception secondInnerException)
        => throw new AggregateException(innerException, secondInnerException);

    [DoesNotReturn]
    internal static void ThrowAggregateException(IEnumerable<Exception> innerExceptions)
        => throw new AggregateException(innerExceptions);
    [DoesNotReturn]
    internal static void ThrowArgumentException(string message, string? paramName = null)
        => throw new ArgumentException(message, paramName);

    [DoesNotReturn]
    internal static void ThrowArgumentOutOfRangeException(string? paramName)
        => throw new ArgumentOutOfRangeException(paramName);

    [DoesNotReturn]
    internal static T ThrowArgumentOutOfRangeException<T>(string? paramName)
        => throw new ArgumentOutOfRangeException(paramName);

    [DoesNotReturn]
    internal static void ThrowArgumentOutOfRangeException(string? paramName, object? actualValue, string? message)
        => throw new ArgumentOutOfRangeException(paramName, actualValue, message);

    [DoesNotReturn]
    internal static void ThrowArgumentOutOfRangeException(string? paramName, string? message)
        => throw new ArgumentOutOfRangeException(paramName, message);

    [DoesNotReturn]
    internal static void ThrowFormatException(string message)
        => throw new FormatException(message);

    internal static void ThrowIfNegative(float value, string message, [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (float.IsNegative(value))
        {
            ThrowArgumentOutOfRangeException(paramName, value, message);
        }
    }

    internal static void ThrowIfNotFinite(Angle value, [CallerArgumentExpression(nameof(value))] string? parameter = null)
    {
        if (!float.IsFinite(value.Value))
        {
            ThrowArgumentException("The value must be finite.", parameter);
        }
    }

    internal static void ThrowIfNotFinite(Matrix3x2 value, [CallerArgumentExpression(nameof(value))] string? parameter = null)
    {
        if (!value.IsFinite().All())
        {
            ThrowArgumentException("Every component must be finite.", parameter);
        }
    }

    internal static void ThrowIfNotFinite(Matrix4x4 value, [CallerArgumentExpression(nameof(value))] string? parameter = null)
    {
        if (!value.IsFinite().All())
        {
            ThrowArgumentException("Every component must be finite.", parameter);
        }
    }

    internal static void ThrowIfNotFinite(Quaternion value, [CallerArgumentExpression(nameof(value))] string? parameter = null)
    {
        if (!value.IsFinite().All())
        {
            ThrowArgumentException("Every component must be finite.", parameter);
        }
    }

    internal static void ThrowIfNotFinite(Vector2 value, [CallerArgumentExpression(nameof(value))] string? parameter = null)
    {
        if (!value.IsFinite().All())
        {
            ThrowArgumentException("Every component must be finite.", parameter);
        }
    }

    internal static void ThrowIfNotFinite(Vector3 value, [CallerArgumentExpression(nameof(value))] string? parameter = null)
    {
        if (!value.IsFinite().All())
        {
            ThrowArgumentException("Every component must be finite.", parameter);
        }
    }

    internal static void ThrowIfNotFinite(Vector4 value, [CallerArgumentExpression(nameof(value))] string? parameter = null)
    {
        if (!value.IsFinite().All())
        {
            ThrowArgumentException("Every component must be finite.", parameter);
        }
    }

    internal static void ThrowIfNotFinite(float value, [CallerArgumentExpression(nameof(value))] string? parameter = null)
    {
        if (!float.IsFinite(value))
        {
            ThrowArgumentException("The value must be finite.", parameter);
        }
    }

    /// <summary>Validates the approximate binary32 unit-length tolerance without rounding a square root.</summary>
    internal static void ThrowIfNotUnitAxis(Vector3 value, [CallerArgumentExpression(nameof(value))] string? parameter = null)
    {
        if (!(float.Abs(Vector3.LengthSquared(value) - 1.0f) <= UnitAxisSquaredLengthTolerance))
        {
            ThrowIfNotFinite(value, parameter);
            ThrowArgumentException("The axis must have unit length within 2e-6.", parameter);
        }
    }

    [DoesNotReturn]
    internal static void ThrowInvalidOperationException(string message)
        => throw new InvalidOperationException(message);

    [DoesNotReturn]
    internal static T ThrowInvalidOperationException<T>(string message)
        => throw new InvalidOperationException(message);

    [DoesNotReturn]
    [SuppressMessage("Reliability", "CA2201", Justification = "Native allocator failure is specified to surface as OutOfMemoryException.")]
    internal static void ThrowOutOfMemoryException()
        => throw new OutOfMemoryException();

    [DoesNotReturn]
    internal static void ThrowOverflowException(string message)
        => throw new OverflowException(message);

    [DoesNotReturn]
    internal static void ThrowSynchronizationLockException()
        => throw new SynchronizationLockException();
}
