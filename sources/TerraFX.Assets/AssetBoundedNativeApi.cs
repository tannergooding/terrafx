// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Assets;

internal enum AssetBoundedNativeApi : byte
{
    None = 0,
    WindowsCreateFile = 1,
    WindowsQueryFile = 2,
    WindowsReadFile = 3,
    WindowsCloseHandle = 4,
    LinuxOpen = 5,
    LinuxFileStatus = 6,
    LinuxPread = 7,
    LinuxClose = 8,
}
