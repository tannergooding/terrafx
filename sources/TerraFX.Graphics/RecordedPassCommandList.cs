// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Threading;

namespace TerraFX.Graphics;

internal sealed class RecordedPassCommandList : IDisposable
{
    private int _activeReadCount;
    private ImmutableArray<RecordedGraphicsCommand> _commands;
    private SkinnedNormalDiagnosticRecordingRecipe? _diagnosticRecipe;
    private readonly Lock _gate = new();
    private bool _isDisposed;
    private bool _isQuarantined;
    private ImmutableArray<GraphicsResourceOwner.UseLease> _useLeases;
    private ImmutableArray<RecordedGraphicsCommand> _verifiedCommands;

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
        _commands = commands;
        _diagnosticRecipe = diagnosticRecipe;
        _verifiedCommands = (diagnosticRecipe is null) ? [] : commands;
        _useLeases = useLeases;
        diagnosticRecipe?.BindRecordedOwner(this);
    }

    internal bool HasDiagnosticRecipe
    {
        get
        {
            lock (_gate)
            {
                return _diagnosticRecipe is not null;
            }
        }
    }

    internal RenderPassId PassId { get; }

    internal CommandRecorderPassKind PassKind { get; }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_isDisposed || _isQuarantined)
            {
                return;
            }

            _isDisposed = true;
        }

        ReleaseClosedRecording();
    }

    internal CommandRead ReadCommands(ref int readState)
    {
        if (Interlocked.CompareExchange(ref readState, 1, 0) != 0)
        {
            throw new InvalidOperationException("A command-read lease state cannot be reused.");
        }

        var acquired = false;

        try
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_isDisposed || _isQuarantined, this);
                _activeReadCount = checked(_activeReadCount + 1);
                acquired = true;
            }
        }
        finally
        {
            if (!acquired)
            {
                Volatile.Write(ref readState, 2);
            }
        }

        return new CommandRead(this, ref readState);
    }

    internal void RecordSubmittedQueueFence(long fenceValue)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed || _isQuarantined, this);

            foreach (var useLease in _useLeases)
            {
                useLease.RecordSubmittedQueueFence(fenceValue);
            }
        }
    }

    internal void RetainForUncertainSubmission()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            _isQuarantined = true;
        }
    }

    private void EndRead()
    {
        lock (_gate)
        {
            _activeReadCount--;
        }

        ReleaseClosedRecording();
    }

    private void ReleaseClosedRecording()
    {
        SkinnedNormalDiagnosticRecordingRecipe? diagnosticRecipe;
        ImmutableArray<GraphicsResourceOwner.UseLease> useLeases;

        lock (_gate)
        {
            if (!_isDisposed || (_activeReadCount != 0))
            {
                return;
            }

            _commands = [];
            _verifiedCommands = [];
            useLeases = _useLeases;
            _useLeases = [];
            diagnosticRecipe = _diagnosticRecipe;
            _diagnosticRecipe = null;
        }

        diagnosticRecipe?.Release(this);

        foreach (var useLease in useLeases)
        {
            useLease.Dispose();
        }
    }

    internal ref struct CommandRead
    {
        private readonly RecordedPassCommandList? _owner;
        private ref int _readState;

        internal CommandRead(RecordedPassCommandList? owner, ref int readState)
        {
            _owner = owner;
            _readState = ref readState;
        }

        internal ReadOnlySpan<RecordedGraphicsCommand> Commands
        {
            get
            {
                ThrowIfInactive();
                return (_owner is null) ? [] : _owner._commands.AsSpan();
            }
        }

        internal SkinnedNormalDiagnosticRecordingRecipe? DiagnosticRecipe
        {
            get
            {
                ThrowIfInactive();
                return _owner?._diagnosticRecipe;
            }
        }

        internal static CommandRead CreateEmpty(ref int readState)
        {
            if (Interlocked.CompareExchange(ref readState, 1, 0) != 0)
            {
                throw new InvalidOperationException("A command-read lease state cannot be reused.");
            }

            return new CommandRead(null, ref readState);
        }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _readState, 2, 1) == 1)
            {
                _owner?.EndRead();
            }
        }

        internal bool Matches(
            RecordedPassCommandList? owner,
            ImmutableArray<RecordedGraphicsCommand> backing)
        {
            ThrowIfInactive();
            return (_owner is not null)
                && ReferenceEquals(_owner, owner)
                && (_owner._commands == backing);
        }

        internal bool MatchesVerifiedOwner(RecordedPassCommandList? owner)
        {
            ThrowIfInactive();
            return (_owner is not null)
                && ReferenceEquals(_owner, owner)
                && !_owner._verifiedCommands.IsDefaultOrEmpty
                && (_owner._commands == _owner._verifiedCommands);
        }

        private void ThrowIfInactive()
        {
            if (Volatile.Read(ref _readState) != 1)
            {
                throw new ObjectDisposedException(nameof(CommandRead));
            }
        }
    }
}
