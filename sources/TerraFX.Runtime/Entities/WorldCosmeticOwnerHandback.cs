// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

internal readonly ref struct WorldCosmeticOwnerHandback
{
    internal WorldCosmeticOwnerHandback(WorldCosmeticCreditOwner owner, WorldLifetimeControl lifetime)
    {
        Lifetime = lifetime;
        Owner = owner;
    }

    internal WorldLifetimeControl? Lifetime { get; }

    internal WorldCosmeticCreditOwner? Owner { get; }
}
