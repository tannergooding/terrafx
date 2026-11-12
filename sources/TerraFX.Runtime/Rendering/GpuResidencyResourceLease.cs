// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX.Rendering;

internal sealed class GpuResidencyResourceLease : IDisposable
{
    private GpuResidencyResource? _resource;

    internal GpuResidencyResourceLease(GpuResidencyResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        _resource = resource;
    }

    public void Dispose()
        => Interlocked.Exchange(ref _resource, null)?.ReleaseLease();
}
