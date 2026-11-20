// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Runtime.InteropServices;

namespace TerraFX.Assets;

[StructLayout(LayoutKind.Explicit, Size = 144)]
internal struct AssetBoundedLinuxStatX64
{
    [FieldOffset(24)]
    internal uint Mode;

    [FieldOffset(48)]
    internal long SizeBytes;
}
