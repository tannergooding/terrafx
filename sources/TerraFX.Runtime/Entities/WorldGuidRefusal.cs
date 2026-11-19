// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

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
    internal const int TargetBindingStale = 8;
    internal const int UseCapacity = 5;

    private readonly WorldAdmissionRefusal _admissionRefusal;
    private readonly int _code;

    internal WorldGuidRefusal(int code)
    {
        _code = code;
        _admissionRefusal = WorldAdmissionRefusal.None;
    }

    private WorldGuidRefusal(int code, WorldAdmissionRefusal admissionRefusal)
    {
        _code = code;
        _admissionRefusal = admissionRefusal;
    }

    internal WorldAdmissionRefusal AdmissionRefusal => _admissionRefusal;

    internal int Code => _code;

    internal bool IsNone => (_code == None) && (_admissionRefusal == WorldAdmissionRefusal.None);

    internal static WorldGuidRefusal ForAdmission(WorldAdmissionRefusal refusal)
        => refusal switch {
            WorldAdmissionRefusal.DomainClosed => new WorldGuidRefusal(None, refusal),
            WorldAdmissionRefusal.LineageAlreadyRetained => new WorldGuidRefusal(None, refusal),
            WorldAdmissionRefusal.LineageCapacity => new WorldGuidRefusal(None, refusal),
            WorldAdmissionRefusal.LineageHistoryNotRetained => new WorldGuidRefusal(None, refusal),
            WorldAdmissionRefusal.OperationCapacity => new WorldGuidRefusal(None, refusal),
            _ => throw new ArgumentOutOfRangeException(nameof(refusal)),
        };
}
