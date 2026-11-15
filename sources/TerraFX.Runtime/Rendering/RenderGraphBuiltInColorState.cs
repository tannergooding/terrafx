// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using TerraFX.Graphics;

namespace TerraFX.Rendering;

internal sealed class RenderGraphBuiltInColorState
{
    private readonly HashSet<Guid> _camerasWithColorWrites = [];

    internal RenderGraphBuiltInColorState(GraphTextureVersion colorTarget)
    {
        ArgumentNullException.ThrowIfNull(colorTarget);
        ColorTarget = colorTarget;
        CurrentColorVersion = colorTarget;
    }

    internal GraphTextureVersion ColorTarget { get; }

    internal GraphTextureVersion CurrentColorVersion { get; set; }

    internal bool HasColorContents { get; set; }

    internal bool HasPreviousPass { get; set; }

    internal int NextPassIndex { get; set; }

    internal RenderPassId PreviousPass { get; set; }

    internal bool HasColorWrite(Guid cameraId)
        => _camerasWithColorWrites.Contains(cameraId);

    internal void MarkColorWrite(Guid cameraId)
        => _ = _camerasWithColorWrites.Add(cameraId);
}
