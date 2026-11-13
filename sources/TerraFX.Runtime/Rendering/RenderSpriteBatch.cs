// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX.Graphics;

namespace TerraFX.Rendering;

internal readonly record struct RenderSpriteBatch(
    Guid CameraId,
    RenderArea Area,
    RenderSpriteBatchKey Key,
    ImmutableArray<int> DrawIndices);
