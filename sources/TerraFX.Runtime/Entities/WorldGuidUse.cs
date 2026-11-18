// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

internal readonly struct WorldGuidUse : IDisposable
{
    private readonly WorldGuidUseControl? _control;
    private readonly uint _generation;

    internal WorldGuidUse(WorldGuidUseControl control, uint generation)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentOutOfRangeException.ThrowIfZero(generation);
        _control = control;
        _generation = generation;
    }

    internal WorldGuidUseControl Control
        => _control ?? throw new InvalidOperationException("The WorldGuid use is invalid.");

    internal bool IsValid => (_control is not null) && (_generation != 0);

    public void Dispose()
        => _control?.Release(_generation);
}
