// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using TerraFX.Numerics;

namespace TerraFX.Rendering;

/// <summary>Contains the copied committed pose, cone and photometric values of one enabled spot light.</summary>
[SuppressMessage(
    "Performance",
    "CA1815:Override equals and comparison operators on value types",
    Justification = "RenderSpotLightSnapshot is an identity-bearing snapshot value; its public contract does not define value equality.")]
public readonly struct RenderSpotLightSnapshot
{
    internal RenderSpotLightSnapshot(
        Guid snapshotId,
        Guid worldGuid,
        Guid lightId,
        Guid entityId,
        SpatialFrame3 frame3,
        SpatialPosition3 position,
        Vector3 direction,
        Vector3 color,
        float luminousFlux,
        float range,
        float innerConeAngle,
        float outerConeAngle,
        ulong layerMask,
        int priority)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(snapshotId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(worldGuid, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(lightId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(entityId, Guid.Empty);
        RenderExtractionValidation.ValidateFrame(frame3, nameof(frame3));

        if (!direction.IsFinite().All())
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }

        var directionLengthSquared = Vector3.Dot(direction, direction);

        if (!float.IsFinite(directionLengthSquared) || (directionLengthSquared <= 1.0e-12f))
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }

        if (!color.IsFinite().All()
            || (color.X < 0.0f)
            || (color.Y < 0.0f)
            || (color.Z < 0.0f))
        {
            throw new ArgumentOutOfRangeException(nameof(color));
        }

        if (!float.IsFinite(luminousFlux) || (luminousFlux < 0.0f))
        {
            throw new ArgumentOutOfRangeException(nameof(luminousFlux));
        }

        if (!float.IsFinite(range) || (range <= 0.0f))
        {
            throw new ArgumentOutOfRangeException(nameof(range));
        }

        if (!float.IsFinite(innerConeAngle)
            || !float.IsFinite(outerConeAngle)
            || (innerConeAngle <= 0.0f)
            || (innerConeAngle > outerConeAngle)
            || (outerConeAngle >= (float.Pi / 2.0f)))
        {
            throw new ArgumentOutOfRangeException(nameof(innerConeAngle));
        }

        SnapshotId = snapshotId;
        WorldGuid = worldGuid;
        LightId = lightId;
        EntityId = entityId;
        Frame3 = frame3;
        Position = SpatialMath.Normalize(position, frame3.Settings);
        Direction = Vector3.Normalize(direction);
        Color = color;
        LuminousFlux = luminousFlux;
        Range = range;
        InnerConeAngle = innerConeAngle;
        OuterConeAngle = outerConeAngle;
        LayerMask = layerMask;
        Priority = priority;
    }

    /// <summary>Gets the copied linear RGB color.</summary>
    public Vector3 Color { get; }

    /// <summary>Gets the normalized world-space light direction.</summary>
    public Vector3 Direction { get; }

    /// <summary>Gets the stable identity of the source Entity.</summary>
    public Guid EntityId { get; }

    /// <summary>Gets the exact 3D frame used to capture this light.</summary>
    public SpatialFrame3 Frame3 { get; }

    /// <summary>Gets the inner cone half angle in radians.</summary>
    public float InnerConeAngle { get; }

    /// <summary>Gets the light's selection mask.</summary>
    public ulong LayerMask { get; }

    /// <summary>Gets the stable light identity derived from the Entity identity and component schema.</summary>
    public Guid LightId { get; }

    /// <summary>Gets the luminous flux in lumens.</summary>
    public float LuminousFlux { get; }

    /// <summary>Gets the outer cone half angle in radians.</summary>
    public float OuterConeAngle { get; }

    /// <summary>Gets the copied canonical spatial position.</summary>
    public SpatialPosition3 Position { get; }

    /// <summary>Gets the shadow/light-selection priority.</summary>
    public int Priority { get; }

    /// <summary>Gets the positive attenuation range in the selected World engine-distance units.</summary>
    public float Range { get; }

    /// <summary>Gets the identity of the render snapshot that issued this value.</summary>
    public Guid SnapshotId { get; }

    /// <summary>Gets the persistent identity of the source World.</summary>
    public Guid WorldGuid { get; }
}
