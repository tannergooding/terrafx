// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

internal interface IWorldRestoreSourceOwner
{
    long CompletedTick { get; }

    ValidatedWorldIdentityHistory IdentityHistory { get; }

    EbinReadLimits Limits { get; }

    WorldRegistration Registration { get; }

    WorldRestoreSourceLease AcquireRead();
}
