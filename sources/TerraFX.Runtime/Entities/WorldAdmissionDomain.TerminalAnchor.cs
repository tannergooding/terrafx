// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;

namespace TerraFX.Entities;

public sealed partial class WorldAdmissionDomain
{
    private const int TerminalAnchorStateBinding = 1;
    private const int TerminalAnchorStateDetaching = 3;
    private const int TerminalAnchorStateNone = 0;
    private const int TerminalAnchorStatePrepared = 2;
    private const int TerminalAnchorStateRetained = 4;

    private int _terminalAnchorState;
    private Exception? _terminalFailure;
    private WorldGuidGrant _terminalGrant;
    private WorldGuidRegistry? _terminalRegistry;

    internal bool TryAttachTerminalAnchor(WorldGuidRegistry registry, in WorldGuidGrant grant)
    {
        ArgumentNullException.ThrowIfNull(registry);

        using (ReentrantGate.EnterScope(ref _gate))
        {
            if (_isDisposed || (_terminalAnchorState != TerminalAnchorStateNone))
            {
                return false;
            }

            _terminalRegistry = registry;
            _terminalGrant = grant;
            _terminalAnchorState = TerminalAnchorStateBinding;
        }

        if (!registry.TryAttachTerminalAnchor(grant, this))
        {
            using var scope = ReentrantGate.EnterScope(ref _gate);
            _terminalRegistry = null;
            _terminalGrant = default;
            _terminalAnchorState = TerminalAnchorStateNone;
            return false;
        }

        using (ReentrantGate.EnterScope(ref _gate))
        {
            if (_terminalAnchorState != TerminalAnchorStateBinding)
            {
                throw new InvalidOperationException("The terminal anchor changed during its binding transaction.");
            }

            _terminalAnchorState = TerminalAnchorStatePrepared;
        }

        return true;
    }

    internal bool TryCancelTerminalAnchor()
    {
        WorldGuidRegistry registry;
        WorldGuidGrant grant;

        using (ReentrantGate.EnterScope(ref _gate))
        {
            if (_terminalAnchorState != TerminalAnchorStatePrepared)
            {
                return false;
            }

            registry = _terminalRegistry
                ?? throw new InvalidOperationException("The prepared terminal anchor lost its WorldGuid registry.");
            grant = _terminalGrant;
            _terminalAnchorState = TerminalAnchorStateDetaching;
        }

        if (!registry.TryDetachTerminalAnchor(grant, this))
        {
            using var scope = ReentrantGate.EnterScope(ref _gate);

            if (_terminalAnchorState == TerminalAnchorStateDetaching)
            {
                _terminalAnchorState = TerminalAnchorStatePrepared;
            }

            return false;
        }

        using (ReentrantGate.EnterScope(ref _gate))
        {
            if ((_terminalAnchorState != TerminalAnchorStateDetaching)
                || !ReferenceEquals(_terminalRegistry, registry)
                || !_terminalGrant.Equals(grant))
            {
                throw new InvalidOperationException("The terminal anchor changed during its cancellation.");
            }

            _terminalRegistry = null;
            _terminalGrant = default;
            _terminalAnchorState = TerminalAnchorStateNone;
        }

        return true;
    }

    internal bool TryRetainTerminalFailure(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        WorldGuidRegistry registry;
        WorldGuidGrant grant;

        using (ReentrantGate.EnterScope(ref _gate))
        {
            if (_terminalAnchorState != TerminalAnchorStatePrepared)
            {
                return false;
            }

            registry = _terminalRegistry
                ?? throw new InvalidOperationException("The prepared terminal anchor lost its WorldGuid registry.");
            grant = _terminalGrant;
            _terminalAnchorState = TerminalAnchorStateDetaching;
        }

        if (!registry.TryRetainTerminalFailure(grant, this, failure))
        {
            using var scope = ReentrantGate.EnterScope(ref _gate);

            if (_terminalAnchorState == TerminalAnchorStateDetaching)
            {
                _terminalAnchorState = TerminalAnchorStatePrepared;
            }

            return false;
        }

        using (ReentrantGate.EnterScope(ref _gate))
        {
            if ((_terminalAnchorState != TerminalAnchorStateDetaching)
                || !ReferenceEquals(_terminalRegistry, registry)
                || !_terminalGrant.Equals(grant))
            {
                throw new InvalidOperationException("The terminal anchor changed while retaining its failure.");
            }

            _terminalFailure = failure;
            _terminalAnchorState = TerminalAnchorStateRetained;
        }

        return true;
    }
}
