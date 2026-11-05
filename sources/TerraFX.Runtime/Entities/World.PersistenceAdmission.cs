// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace TerraFX.Entities;

public sealed partial class World
{
    internal void ReleasePersistenceAdmissionBytes(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);

        lock (_persistenceAdmissionGate)
        {
            if (bytes > _activePersistenceAdmissionBytes)
            {
                throw new InvalidOperationException("The released persistence admission bytes exceed the live reservation.");
            }

            _activePersistenceAdmissionBytes -= bytes;
        }
    }

    internal bool TryGrowPersistenceAdmissionBytes(WorldDomainReservation domainReservation, long bytes)
    {
        ArgumentNullException.ThrowIfNull(domainReservation);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);

        lock (_persistenceAdmissionGate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

            if (bytes > _maximumPersistenceAdmissionBytes - _activePersistenceAdmissionBytes)
            {
                return false;
            }

            _activePersistenceAdmissionBytes += bytes;
        }

        if (domainReservation.TryReserveAdditional(bytes))
        {
            return true;
        }

        ReleasePersistenceAdmissionBytes(bytes);
        return false;
    }

    internal bool TryPrepareGuidReplacement(
        out WorldGuidReplacementPreparation? preparation,
        out WorldGuidRefusal refusal)
    {
        lock (_sceneStorageGate)
        {
            lock (_persistenceAdmissionGate)
            {
                lock (_ownerControlGate)
                {
                    if (Volatile.Read(ref _disposed) != 0)
                    {
                        preparation = null;
                        refusal = new WorldGuidRefusal(WorldGuidRefusal.TargetBindingStale);
                        return false;
                    }

                    return _registry.TryPrepareReplacement(_worldGuidGrant, out preparation, out refusal);
                }
            }
        }
    }

    internal bool TryPrepareLoadReplacement(
        Guid replacementWorldGuid,
        long sourceBytes,
        long targetBytes,
        long dataBytes,
        long cleanupBytes,
        out WorldGuidReplacementPreparation? preparation,
        out WorldGuidRefusal bindingRefusal,
        out WorldAdmissionRefusal operationCreditRefusal)
    {
        if (replacementWorldGuid == Guid.Empty)
        {
            throw new ArgumentException("The replacement WorldGuid cannot be empty.", nameof(replacementWorldGuid));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dataBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cleanupBytes);
        _ = checked(sourceBytes + targetBytes + dataBytes + cleanupBytes);

        lock (_sceneStorageGate)
        {
            lock (_persistenceAdmissionGate)
            {
                lock (_ownerControlGate)
                {
                    if (Volatile.Read(ref _disposed) != 0)
                    {
                        preparation = null;
                        bindingRefusal = new WorldGuidRefusal(WorldGuidRefusal.TargetBindingStale);
                        operationCreditRefusal = WorldAdmissionRefusal.None;
                        return false;
                    }

                    if (!_registry.TryReserveReplacement(
                            _worldGuidGrant,
                            replacementWorldGuid,
                            out var replacementGrant,
                            out bindingRefusal))
                    {
                        preparation = null;
                        operationCreditRefusal = WorldAdmissionRefusal.None;
                        return false;
                    }

                    if (!_admissionDomain.TryReserveLoadOperationCredits(
                        sourceBytes,
                        targetBytes,
                        dataBytes,
                        cleanupBytes,
                        out var credits,
                        out operationCreditRefusal))
                    {
                        if (!_registry.TryReleaseFresh(replacementGrant))
                        {
                            throw new InvalidOperationException("The refused load admission could not release its successor binding.");
                        }

                        preparation = null;
                        bindingRefusal = default;
                        return false;
                    }

                    if (credits is null)
                    {
                        if (!_registry.TryReleaseFresh(replacementGrant))
                        {
                            throw new InvalidOperationException("The inconsistent load admission could not release its successor binding.");
                        }

                        throw new InvalidOperationException("The successful load-credit reservation did not produce its bundle.");
                    }

                    try
                    {
                        preparation = new WorldGuidReplacementPreparation(
                            _registry,
                            _worldGuidGrant,
                            replacementGrant,
                            credits);
                        bindingRefusal = default;
                        operationCreditRefusal = WorldAdmissionRefusal.None;
                        return true;
                    }
                    catch (OutOfMemoryException exception)
                    {
                        Exception? cleanupFailure = null;

                        try
                        {
                            credits.Dispose();
                        }
                        catch (InvalidOperationException releaseException)
                        {
                            cleanupFailure = releaseException;
                        }

                        if (!_registry.TryReleaseFresh(replacementGrant))
                        {
                            var bindingReleaseFailure = new InvalidOperationException(
                                "The failed load admission could not release its successor binding.");
                            cleanupFailure = (cleanupFailure is null)
                                ? bindingReleaseFailure
                                : new AggregateException(cleanupFailure, bindingReleaseFailure);
                        }

                        if (cleanupFailure is not null)
                        {
                            throw new AggregateException(exception, cleanupFailure);
                        }

                        throw;
                    }
                }
            }
        }
    }

    internal bool TryPrepareLoadReplacement(
        long sourceBytes,
        long targetBytes,
        long dataBytes,
        long cleanupBytes,
        out WorldGuidReplacementPreparation? preparation,
        out WorldGuidRefusal bindingRefusal,
        out WorldAdmissionRefusal operationCreditRefusal)
        => TryPrepareLoadReplacement(
            _worldGuid,
            sourceBytes,
            targetBytes,
            dataBytes,
            cleanupBytes,
            out preparation,
            out bindingRefusal,
            out operationCreditRefusal);

    internal bool TryReserveLoadOperationCredits(
        long sourceBytes,
        long targetBytes,
        long dataBytes,
        long cleanupBytes,
        out WorldDomainOperationCreditBundle? credits,
        out WorldAdmissionRefusal refusal)
    {
        lock (_sceneStorageGate)
        {
            lock (_persistenceAdmissionGate)
            {
                lock (_ownerControlGate)
                {
                    if (Volatile.Read(ref _disposed) != 0)
                    {
                        credits = null;
                        refusal = WorldAdmissionRefusal.DomainClosed;
                        return false;
                    }

                    return _admissionDomain.TryReserveLoadOperationCredits(
                        sourceBytes,
                        targetBytes,
                        dataBytes,
                        cleanupBytes,
                        out credits,
                        out refusal);
                }
            }
        }
    }

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "A successful result transfers the exact domain reservation to the returned persistence reservation; the finally path releases it when admission fails.")]
    internal bool TryReservePersistenceAdmissionBytes(
        long bytes,
        long maximumAdditionalBytes,
        out WorldPersistenceAdmissionReservation? reservation)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumAdditionalBytes);
        reservation = null;

        lock (_persistenceAdmissionGate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

            if (bytes > _maximumPersistenceAdmissionBytes - _activePersistenceAdmissionBytes)
            {
                return false;
            }

            _activePersistenceAdmissionBytes += bytes;
        }

        WorldDomainReservation? domainReservation = null;
        var reservationCreated = false;

        try
        {
            if (!_admissionDomain.TryReserveWorld(bytes, maximumAdditionalBytes, out domainReservation)
                || (domainReservation is null)
                || (Volatile.Read(ref _disposed) != 0))
            {
                return false;
            }

            reservation = new WorldPersistenceAdmissionReservation(this, domainReservation, bytes);
            domainReservation = null;
            reservationCreated = true;
            return true;
        }
        finally
        {
            if (!reservationCreated)
            {
                domainReservation?.Dispose();
                ReleasePersistenceAdmissionBytes(bytes);
            }
        }
    }

    internal bool TryReservePersistenceAdmissionBytes(long bytes, out WorldPersistenceAdmissionReservation? reservation)
        => TryReservePersistenceAdmissionBytes(bytes, maximumAdditionalBytes: 0, out reservation);
}
