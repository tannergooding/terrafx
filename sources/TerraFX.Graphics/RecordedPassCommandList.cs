// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Graphics;

internal sealed class RecordedPassCommandList : IDisposable
{
    private ImmutableArray<GraphicsResourceOwner.UseLease> _useLeases;

    internal RecordedPassCommandList(
        ImmutableArray<RecordedGraphicsCommand> commands,
        ImmutableArray<GraphicsResourceOwner.UseLease> useLeases)
    {
        if (commands.IsDefault || useLeases.IsDefault)
        {
            throw new ArgumentException("Recorded pass command data must be initialized.");
        }

        Commands = commands;
        _useLeases = useLeases;
    }

    internal ImmutableArray<RecordedGraphicsCommand> Commands { get; }

    public void Dispose()
    {
        var useLeases = _useLeases;
        _useLeases = [];

        foreach (var useLease in useLeases)
        {
            useLease.Dispose();
        }
    }
}
