// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using TerraFX.Numerics;

namespace TerraFX.Rendering;

/// <summary>Contains the copied committed direction and photometric values of one enabled directional light.</summary>
[SuppressMessage(
    "Performance",
    "CA1815:Override equals and comparison operators on value types",
    Justification = "RenderDirectionalLightSnapshot is an identity-bearing snapshot value; its public contract does not define value equality.")]
public readonly struct RenderDirectionalLightSnapshot
{
    internal RenderDirectionalLightSnapshot(
        Guid snapshotId,
        Guid worldGuid,
        Guid lightId,
        Guid entityId,
        SpatialFrame3 frame3,
        Vector3 direction,
        Vector3 color,
        float illuminance,
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

        if (!float.IsFinite(illuminance) || (illuminance < 0.0f))
        {
            throw new ArgumentOutOfRangeException(nameof(illuminance));
        }

        SnapshotId = snapshotId;
        WorldGuid = worldGuid;
        LightId = lightId;
        EntityId = entityId;
        Frame3 = frame3;
        Direction = Vector3.Normalize(direction);
        Color = color;
        Illuminance = illuminance;
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

    /// <summary>Gets the illuminance in lux.</summary>
    public float Illuminance { get; }

    /// <summary>Gets the light's selection mask.</summary>
    public ulong LayerMask { get; }

    /// <summary>Gets the stable light identity derived from the Entity identity and component schema.</summary>
    public Guid LightId { get; }

    /// <summary>Gets the shadow/light-selection priority.</summary>
    public int Priority { get; }

    /// <summary>Gets the identity of the render snapshot that issued this value.</summary>
    public Guid SnapshotId { get; }

    /// <summary>Gets the persistent identity of the source World.</summary>
    public Guid WorldGuid { get; }
}
