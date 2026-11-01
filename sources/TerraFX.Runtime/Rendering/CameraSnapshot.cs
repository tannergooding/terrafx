// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using TerraFX;
using TerraFX.Numerics;
using TerraFX.Scenes;

namespace TerraFX.Rendering;

/// <summary>Contains the copied committed pose and projection inputs of one live Scene camera.</summary>
[SuppressMessage(
    "Performance",
    "CA1815:Override equals and comparison operators on value types",
    Justification = "CameraSnapshot is an identity-bearing snapshot value; its public contract does not define value equality.")]
public readonly struct CameraSnapshot
{
    internal CameraSnapshot(
        Guid snapshotId,
        Guid worldGuid,
        Guid cameraId,
        int order,
        SpatialFrame3 frame3,
        PixelSize targetExtent,
        SpatialPosition3 position,
        Quaternion rotation,
        CameraProjectionKind projectionKind,
        float verticalFieldOfView,
        float orthographicHeight,
        float near,
        CameraFarMode farMode,
        float? far,
        float? aspectRatioOverride,
        Vector4 viewport,
        CameraClearFlags clearFlags,
        Vector4 clearColor,
        ulong layerMask)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(snapshotId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(worldGuid, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(cameraId, Guid.Empty);
        RenderExtractionValidation.ValidateFrame(frame3, nameof(frame3));
        RenderExtractionValidation.ValidateTargetExtent(targetExtent, nameof(targetExtent));

        if (!Enum.IsDefined(projectionKind)
            || !Enum.IsDefined(farMode)
            || !float.IsFinite(verticalFieldOfView)
            || (verticalFieldOfView <= 0.0f)
            || (verticalFieldOfView >= float.Pi)
            || !float.IsFinite(orthographicHeight)
            || (orthographicHeight <= 0.0f)
            || !float.IsFinite(near)
            || (near <= 0.0f)
            || !viewport.IsFinite().All()
            || (viewport.X < 0.0f)
            || (viewport.Y < 0.0f)
            || (viewport.Z <= 0.0f)
            || (viewport.W <= 0.0f)
            || (viewport.X + viewport.Z > 1.0f)
            || (viewport.Y + viewport.W > 1.0f)
            || !clearColor.IsFinite().All()
            || (((byte)clearFlags & ~((byte)CameraClearFlags.Color | (byte)CameraClearFlags.Depth)) != 0))
        {
            throw new ArgumentException("The copied camera projection or output state is invalid.");
        }

        if (aspectRatioOverride.HasValue
            && (!float.IsFinite(aspectRatioOverride.Value) || (aspectRatioOverride.Value <= 0.0f)))
        {
            throw new ArgumentOutOfRangeException(nameof(aspectRatioOverride));
        }

        if (farMode is CameraFarMode.Finite)
        {
            if (!far.HasValue || !float.IsFinite(far.Value) || (far.Value <= near))
            {
                throw new ArgumentOutOfRangeException(nameof(far));
            }
        }
        else if (far.HasValue || (projectionKind is CameraProjectionKind.Orthographic))
        {
            throw new ArgumentException("Only a perspective camera admits an absent infinite far plane.", nameof(far));
        }

        var rotationLengthSquared = Quaternion.Dot(rotation, rotation);

        if (!rotation.IsFinite().All()
            || !float.IsFinite(rotationLengthSquared)
            || (rotationLengthSquared <= 1.0e-12f))
        {
            throw new ArgumentException("The copied camera rotation must be finite and nonzero.", nameof(rotation));
        }

        SnapshotId = snapshotId;
        WorldGuid = worldGuid;
        CameraId = cameraId;
        Order = order;
        Frame3 = frame3;
        TargetExtent = targetExtent;
        Position = SpatialMath.Normalize(position, frame3.Settings);
        Rotation = Quaternion.Normalize(rotation);
        ProjectionKind = projectionKind;
        VerticalFieldOfView = verticalFieldOfView;
        OrthographicHeight = orthographicHeight;
        Near = near;
        FarMode = farMode;
        Far = far;
        AspectRatioOverride = aspectRatioOverride;
        Viewport = viewport;
        ClearFlags = clearFlags;
        ClearColor = clearColor;
        LayerMask = layerMask;
    }

    /// <summary>Gets the optional explicit aspect ratio.</summary>
    public float? AspectRatioOverride { get; }

    /// <summary>Gets the stable camera Entity identity.</summary>
    public Guid CameraId { get; }

    /// <summary>Gets the linear clear color.</summary>
    public Vector4 ClearColor { get; }

    /// <summary>Gets the selected color/depth clear channels.</summary>
    public CameraClearFlags ClearFlags { get; }

    /// <summary>Gets the optional far distance for a finite projection.</summary>
    public float? Far { get; }

    /// <summary>Gets the finite or infinite far-plane policy.</summary>
    public CameraFarMode FarMode { get; }

    /// <summary>Gets the exact 3D spatial frame used to capture this camera.</summary>
    public SpatialFrame3 Frame3 { get; }

    /// <summary>Gets the camera's layer-selection mask.</summary>
    public ulong LayerMask { get; }

    /// <summary>Gets the near distance.</summary>
    public float Near { get; }

    /// <summary>Gets the signed camera ordering value.</summary>
    public int Order { get; }

    /// <summary>Gets the orthographic physical height.</summary>
    public float OrthographicHeight { get; }

    /// <summary>Gets the copied canonical spatial position.</summary>
    public SpatialPosition3 Position { get; }

    /// <summary>Gets the projection kind.</summary>
    public CameraProjectionKind ProjectionKind { get; }

    /// <summary>Gets the copied rigid camera rotation.</summary>
    public Quaternion Rotation { get; }

    /// <summary>Gets the identity of the render snapshot that issued this value.</summary>
    public Guid SnapshotId { get; }

    /// <summary>Gets the physical target extent used by extraction.</summary>
    public PixelSize TargetExtent { get; }

    /// <summary>Gets the vertical perspective field of view in radians.</summary>
    public float VerticalFieldOfView { get; }

    /// <summary>Gets the normalized target viewport.</summary>
    public Vector4 Viewport { get; }

    /// <summary>Gets the persistent identity of the source World.</summary>
    public Guid WorldGuid { get; }
}
