// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

internal readonly ref struct WorldCosmeticCreditHandback
{
    internal WorldCosmeticCreditHandback(WorldCosmeticCredit credit)
    {
        Credit = credit;
    }

    internal WorldCosmeticCredit Credit { get; }
}
