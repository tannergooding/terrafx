// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Rendering;

internal readonly record struct RenderAtlasFrame(
    Guid FrameId,
    int Page,
    int X,
    int Y,
    int Width,
    int Height,
    int Padding);
