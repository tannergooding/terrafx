// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Threading;

namespace TerraFX.Graphics;

internal interface IGraphicsBackendFactory
{
    GraphicsBackend Backend { get; }

    IGraphicsBackendDevice Create(
        uint ownerId,
        GraphicsDeviceProfile profile,
        GraphicsPlatformKind platformKind,
        int frameSlotCount,
        CancellationToken cancellationToken);

    IGraphicsBackendDevice Create(
        uint ownerId,
        GraphicsDeviceProfile profile,
        GraphicsPlatformKind platformKind,
        int frameSlotCount,
        GraphicsNativeSurfaceBinding? presentationProbeBinding,
        CancellationToken cancellationToken)
        => Create(ownerId, profile, platformKind, frameSlotCount, cancellationToken);
}
