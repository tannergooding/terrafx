// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX.Entities;

internal sealed class WorldPersistenceAdmissionReservation : IDisposable
{
    private readonly long _bytes;
    private readonly Lock _disposeGate = new();
    private readonly WorldDomainReservation _domainReservation;
    private bool _isDisposed;
    private readonly World _world;

    internal WorldPersistenceAdmissionReservation(
        World world,
        WorldDomainReservation domainReservation,
        long bytes)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(domainReservation);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);
        _bytes = bytes;
        _domainReservation = domainReservation;
        _world = world;
    }

    public void Dispose()
    {
        lock (_disposeGate)
        {
            if (_isDisposed)
            {
                return;
            }

            _domainReservation.Dispose();
            _world.ReleasePersistenceAdmissionBytes(_bytes);
            _isDisposed = true;
        }
    }

    internal EbinWriteStatus WriteConfigurationRecord(
        World world,
        WorldOptions options,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(options);

        lock (_disposeGate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            if (!ReferenceEquals(_world, world) || !ReferenceEquals(world.Options, options))
            {
                throw new ArgumentException("The persistence reservation and options must belong to this exact World.", nameof(world));
            }

            if (checked((long)candidateStorage.Length + destination.Length) > _bytes)
            {
                throw new ArgumentException("The persistence reservation does not cover both configuration-record workspaces.", nameof(candidateStorage));
            }

            return WorldPersistenceConfigurationWriter.Write(
                options,
                candidateStorage,
                destination,
                out bytesWritten,
                out diagnostic);
        }
    }

    internal EbinWriteStatus WriteIdentityBranchRecord(
        World world,
        in World.IdentityBranchCaptureEnumerator branch,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(world);

        lock (_disposeGate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            if (!ReferenceEquals(_world, world) || !branch.IsBoundTo(world))
            {
                throw new ArgumentException("The identity-branch capture and reservation must belong to this exact World.", nameof(world));
            }

            if (checked((long)candidateStorage.Length + destination.Length) > _bytes)
            {
                throw new ArgumentException("The persistence reservation does not cover both identity-branch workspaces.", nameof(candidateStorage));
            }

            return WorldPersistenceIdentityBranchWriter.Write(
                in branch,
                candidateStorage,
                destination,
                out bytesWritten,
                out diagnostic);
        }
    }
}
