// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Threading;

namespace TerraFX.Graphics;

internal interface IGraphicsBackendSurface : IDisposable
{
    SurfaceCapabilities Capabilities { get; }

    PixelSize ClientPixelSize { get; }

    TextureFormat ColorFormat { get; }

    SurfaceCapabilities Resize(PixelSize clientPixelSize);

    bool TryGetCompletedPresentation(
        long presentationSequence,
        uint deviceEpoch,
        out PresentationReceipt? receipt);

    PresentationReceipt WaitForPresentation(
        long presentationSequence,
        uint deviceEpoch,
        CancellationToken cancellationToken);

    ImmutableArray<PresentationReceipt> WaitForPresentations(uint deviceEpoch);
}
