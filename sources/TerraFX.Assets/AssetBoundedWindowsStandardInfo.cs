// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Runtime.InteropServices;

namespace TerraFX.Assets;

[StructLayout(LayoutKind.Sequential)]
internal struct AssetBoundedWindowsStandardInfo
{
    internal long AllocationSize;
    internal long EndOfFile;
    internal uint NumberOfLinks;
    internal byte DeletePending;
    internal byte Directory;
}
