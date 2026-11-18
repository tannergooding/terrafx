// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

internal readonly struct WorldCosmeticCredit
{
    internal WorldCosmeticCredit(WorldCosmeticCreditOwner owner, int index, uint generation)
    {
        Generation = generation;
        Index = index;
        Owner = owner;
    }

    internal uint Generation { get; }

    internal int Index { get; }

    internal WorldCosmeticCreditOwner? Owner { get; }
}
