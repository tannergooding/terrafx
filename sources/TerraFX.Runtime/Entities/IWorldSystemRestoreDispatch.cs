// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX.Entities;

internal interface IWorldSystemRestoreDispatch
{
    object ReadStateRecord(
        scoped ReadOnlySpan<byte> source,
        Guid systemId,
        CancellationToken cancellationToken);

    void RestoreState(
        IWorldSystem system,
        object author,
        WorldStateRestoreBinding binding,
        CancellationToken cancellationToken);
}
