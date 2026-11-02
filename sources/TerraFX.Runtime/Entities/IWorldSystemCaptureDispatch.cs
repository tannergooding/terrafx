// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX.Entities;

internal interface IWorldSystemCaptureDispatch
{
    nint Count(
        IWorldSystem system,
        Guid systemId,
        WorldSystemCaptureBinding binding,
        CancellationToken cancellationToken);

    nint Write(
        IWorldSystem system,
        Guid systemId,
        scoped Span<byte> destination,
        WorldSystemCaptureBinding binding,
        CancellationToken cancellationToken);
}
