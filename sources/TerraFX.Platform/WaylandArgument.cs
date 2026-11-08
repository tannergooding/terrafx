// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Runtime.InteropServices;

namespace TerraFX.Platform;

[StructLayout(LayoutKind.Explicit)]
internal struct WaylandArgument
{
    [FieldOffset(0)]
    internal nint Pointer;

    [FieldOffset(0)]
    internal uint Unsigned;
}
