// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using TerraFX.Numerics;

namespace TerraFX.Entities;

internal readonly record struct WorldCameraData(
    byte ProjectionKind,
    float VerticalFieldOfView,
    float OrthographicHeight,
    float Near,
    byte FarMode,
    float? Far,
    float? AspectRatioOverride,
    Vector4 Viewport,
    byte ClearFlags,
    Vector4 ClearColor,
    ulong LayerMask,
    int Order);
