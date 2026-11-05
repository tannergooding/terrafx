// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Input;
using TerraFX.Platform;

namespace TerraFX.Hosting;

internal sealed class PlatformInputRoutingHostAdapter : IPlatformInputRoutingHost
{
    private readonly PlatformHostOwner _platformOwner;

    internal PlatformInputRoutingHostAdapter(PlatformHostOwner platformOwner)
    {
        ArgumentNullException.ThrowIfNull(platformOwner);
        _platformOwner = platformOwner;
    }

    public uint OwnerId => _platformOwner.OwnerId;

    public long GetHostTimestamp()
        => _platformOwner.GetHostTimestamp();

    public PlatformNativeWindowBinding GetNativeWindowBinding(WindowHandle window)
        => _platformOwner.GetNativeWindowBinding(window);

    public bool IsActiveGamepadDevice(InputDeviceHandle device)
        => _platformOwner.IsActiveGamepadDevice(device);
}
