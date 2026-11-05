// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using TerraFX.Input;
using TerraFX.Platform;

namespace TerraFX.Hosting;

internal interface IPlatformInputRoutingHost
{
    uint OwnerId { get; }

    long GetHostTimestamp();

    PlatformNativeWindowBinding GetNativeWindowBinding(WindowHandle window);

    bool IsActiveGamepadDevice(InputDeviceHandle device);

    PlatformInputCaptureBatch RequestGamepadStateReconciliation(InputDeviceHandle device);

    PlatformInputCaptureBatch RequestInputStateReconciliation(WindowHandle? window);
}
