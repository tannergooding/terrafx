// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Hosting;

internal readonly struct WorldIdentityRemapEntry
{
    private readonly Guid _sourceId;

    private readonly Guid _targetId;

    internal WorldIdentityRemapEntry(Guid sourceId, Guid targetId)
    {
        _sourceId = sourceId;
        _targetId = targetId;
    }

    internal Guid SourceId => _sourceId;

    internal Guid TargetId => _targetId;
}
