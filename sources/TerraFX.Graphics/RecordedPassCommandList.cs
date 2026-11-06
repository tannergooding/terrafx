// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Graphics;

internal sealed class RecordedPassCommandList : IDisposable
{
    private ImmutableArray<GraphicsResourceOwner.UseLease> _useLeases;

    internal RecordedPassCommandList(
        RenderPassId passId,
        CommandRecorderPassKind passKind,
        ImmutableArray<RecordedGraphicsCommand> commands,
        ImmutableArray<GraphicsResourceOwner.UseLease> useLeases)
    {
        if (!passId.IsValid)
        {
            throw new ArgumentException("A recorded pass command list requires a valid pass ID.", nameof(passId));
        }

        if (!Enum.IsDefined(passKind) || (passKind == CommandRecorderPassKind.Presentation))
        {
            throw new ArgumentOutOfRangeException(nameof(passKind));
        }

        if (commands.IsDefault || useLeases.IsDefault)
        {
            throw new ArgumentException("Recorded pass command data must be initialized.");
        }

        PassId = passId;
        PassKind = passKind;
        Commands = commands;
        _useLeases = useLeases;
    }

    internal ImmutableArray<RecordedGraphicsCommand> Commands { get; }

    internal RenderPassId PassId { get; }

    internal CommandRecorderPassKind PassKind { get; }

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
