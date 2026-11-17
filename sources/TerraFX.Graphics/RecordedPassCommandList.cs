// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Graphics;

internal sealed class RecordedPassCommandList : IDisposable
{
    private SkinnedNormalDiagnosticRecordingRecipe? _diagnosticRecipe;
    private bool _isDisposed;
    private bool _isQuarantined;
    private ImmutableArray<GraphicsResourceOwner.UseLease> _useLeases;

    internal RecordedPassCommandList(
        RenderPassId passId,
        CommandRecorderPassKind passKind,
        ImmutableArray<RecordedGraphicsCommand> commands,
        ImmutableArray<GraphicsResourceOwner.UseLease> useLeases,
        SkinnedNormalDiagnosticRecordingRecipe? diagnosticRecipe = null)
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

        if ((diagnosticRecipe is not null) && (diagnosticRecipe.PassId != passId))
        {
            throw new ArgumentException("A diagnostic recording recipe must belong to this pass.", nameof(diagnosticRecipe));
        }

        if (diagnosticRecipe is not null)
        {
            foreach (var lease in useLeases)
            {
                if (!lease.IsCoveredBy(diagnosticRecipe.RecordingUseAdmission))
                {
                    throw new InvalidOperationException("A diagnostic pipeline use is not covered by the frozen recording cohort.");
                }
            }
        }

        PassId = passId;
        PassKind = passKind;
        Commands = commands;
        _diagnosticRecipe = diagnosticRecipe;
        _useLeases = useLeases;
    }

    internal ImmutableArray<RecordedGraphicsCommand> Commands { get; private set; }

    internal SkinnedNormalDiagnosticRecordingRecipe? DiagnosticRecipe => _diagnosticRecipe;

    internal RenderPassId PassId { get; }

    internal CommandRecorderPassKind PassKind { get; }

    public void Dispose()
    {
        if (_isDisposed || _isQuarantined)
        {
            return;
        }

        _isDisposed = true;
        Commands = [];
        var useLeases = _useLeases;
        _useLeases = [];
        var diagnosticRecipe = _diagnosticRecipe;
        _diagnosticRecipe = null;
        diagnosticRecipe?.Release();

        foreach (var useLease in useLeases)
        {
            useLease.Dispose();
        }
    }

    internal void RecordSubmittedQueueFence(long fenceValue)
    {
        ObjectDisposedException.ThrowIf(_isDisposed || _isQuarantined, this);

        foreach (var useLease in _useLeases)
        {
            useLease.RecordSubmittedQueueFence(fenceValue);
        }
    }

    internal void RetainForUncertainSubmission()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _isQuarantined = true;
    }
}
