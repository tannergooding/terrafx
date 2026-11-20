// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Runtime.InteropServices;

namespace TerraFX.Assets;

[StructLayout(LayoutKind.Sequential)]
internal struct AssetBoundedWindowsOverlapped
{
    internal nuint Internal;
    internal nuint InternalHigh;
    internal uint Offset;
    internal uint OffsetHigh;
    internal nint EventHandle;
}
