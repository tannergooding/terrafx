// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

internal interface IGraphicsBackendSurface : IDisposable
{
    SurfaceCapabilities Capabilities { get; }

    PixelSize ClientPixelSize { get; }

    TextureFormat ColorFormat { get; }

    SurfaceCapabilities Resize(PixelSize clientPixelSize);
}
