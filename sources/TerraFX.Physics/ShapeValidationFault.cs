// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Physics;

/// <summary>Describes the first deterministic primitive geometry rejection.</summary>
public sealed class ShapeValidationFault
{
    internal ShapeValidationFault(ShapeValidationFaultCode code, ShapeValidationField field, float observedValue, float limit)
    {
        Code = code;
        Field = field;
        ObservedValue = observedValue;
        Limit = limit;
    }

    /// <summary>Gets the rejection category.</summary>
    public ShapeValidationFaultCode Code { get; }

    /// <summary>Gets the field or predicate that failed.</summary>
    public ShapeValidationField Field { get; }

    /// <summary>Gets the applicable inclusive limit.</summary>
    public float Limit { get; }

    /// <summary>Gets the value observed by the failing check.</summary>
    public float ObservedValue { get; }
}
