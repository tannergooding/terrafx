// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;

namespace TerraFX.Entities;

public sealed partial class World
{
    internal static World CreateHosted(
        WorldOptions options,
        WorldWorkerPool sharedWorkerPool,
        DiagnosticsService sharedDiagnostics,
        WorldAdmissionDomain hostAdmissionDomain)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(sharedWorkerPool);
        ArgumentNullException.ThrowIfNull(sharedDiagnostics);
        ArgumentNullException.ThrowIfNull(hostAdmissionDomain);

        if ((options.CpuWorkerCount is int configuredWorkerCount)
            && (configuredWorkerCount != sharedWorkerPool.WorkerCount))
        {
            throw new ArgumentException("A hosted World worker count must match the host-owned pool.", nameof(options));
        }

        return AdmitWorld(options, sharedWorkerPool, sharedDiagnostics, hostAdmissionDomain);
    }
}
