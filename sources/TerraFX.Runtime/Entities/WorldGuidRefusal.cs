// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

internal readonly struct WorldGuidRefusal
{
    internal const int BindingCapacity = 3;
    internal const int ByteCapacity = 7;
    internal const int GuidAlreadyRetained = 1;
    internal const int KeyCapacity = 2;
    internal const int None = 0;
    internal const int PendingSlabCapacity = 6;
    internal const int PreparationCapacity = 4;
    internal const int UseCapacity = 5;

    private readonly int _code;

    internal WorldGuidRefusal(int code)
    {
        _code = code;
    }

    internal int Code => _code;

    internal bool IsNone => _code == None;
}
