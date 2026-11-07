// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Platform;

internal readonly record struct LinuxEvdevGamepadDevice(string SysPath, string DeviceNode);
