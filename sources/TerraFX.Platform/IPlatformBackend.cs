// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Platform;

internal interface IPlatformBackend : IDisposable
{
    PlatformServiceStatus AudioStatus { get; }

    PlatformBackend Backend { get; }

    PlatformServiceStatus ClipboardStatus { get; }

    PlatformServiceStatus DisplayStatus { get; }

    uint OwnerId { get; }

    PlatformServiceStatus PhysicalInputStatus { get; }

    void Pump();
}
