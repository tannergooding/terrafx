// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX;

namespace TerraFX.Platform;

internal interface IPlatformBackend : IDisposable
{
    PlatformServiceStatus AudioStatus { get; }

    PlatformBackend Backend { get; }

    ClipboardServiceStatus ClipboardStatus { get; }

    PlatformServiceStatus DisplayStatus { get; }

    uint OwnerId { get; }

    PlatformServiceStatus PhysicalInputStatus { get; }

    ImmutableArray<MonitorHandle> GetMonitorHandles();

    ImmutableArray<PlatformMonitorInfo> GetMonitorInfo();

    void Pump();

    void SetMonitorHandles(ImmutableArray<MonitorHandle> monitorHandles);

    bool TakeMonitorTopologyChanged();
}
