// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace TerraFX.Graphics;

public sealed partial class GraphicsDevice
{
    private static bool AreTransientBufferDescriptionsCompatible(BufferDescription left, BufferDescription right)
        => (left.ByteLength == right.ByteLength)
            && (left.MemoryKind == GpuMemoryKind.DeviceLocal)
            && (right.MemoryKind == GpuMemoryKind.DeviceLocal)
            && (left.StructuredStride == right.StructuredStride)
            && (left.Usage == right.Usage);

    private static void ReleaseTransientBufferReservations(
        List<GraphicsOwnerRequestQueue.GraphicsRetirementReservation?> reservations)
    {
        foreach (var reservation in reservations)
        {
            reservation?.Dispose();
        }
    }

    internal async Task<GraphicsTransientBufferGroup> CreateTransientBufferGroupAsync(
        ImmutableArray<GraphBufferVersion> versions,
        CancellationToken cancellationToken)
    {
        if (versions.IsDefaultOrEmpty || (versions.Length < 2))
        {
            throw new ArgumentException("A transient alias group requires at least two graph buffers.", nameof(versions));
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (Status != GraphicsDeviceStatus.Ready)
        {
            throw new InvalidOperationException("The GraphicsDevice is not accepting transient buffer groups.");
        }

        var description = versions[0].Description;

        for (var index = 0; index < versions.Length; index++)
        {
            var version = versions[index];

            if ((version.DeviceEpoch != DeviceEpoch)
                || !AreTransientBufferDescriptionsCompatible(description, version.Description))
            {
                throw new ArgumentException("Transient alias groups require compatible same-device buffer descriptions.", nameof(versions));
            }
        }

        List<GraphicsOwnerRequestQueue.GraphicsRetirementReservation?>? reservations = [];

        try
        {
            for (var index = 0; index < versions.Length; index++)
            {
                reservations.Add(await _ownerQueue.ReserveRetirementAsync(cancellationToken).ConfigureAwait(false));
            }

            var request = new CreateTransientBufferGroupRequest(this, versions, reservations, cancellationToken);
            reservations = null;

            try
            {
                await _ownerQueue.WriteRequestAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                request.AdmissionFailed();
                throw;
            }

            return await request.Task.ConfigureAwait(false);
        }
        finally
        {
            if (reservations is not null)
            {
                ReleaseTransientBufferReservations(reservations);
            }
        }
    }

    private GpuBuffer CreateBufferFromAllocationOnOwner(
        BufferDescription description,
        GraphicsResourceAllocationLease allocationLease,
        GraphicsOwnerRequestQueue.GraphicsRetirementReservation retirementReservation)
    {
        var backendDevice = _backendDevice
            ?? throw new InvalidOperationException("The GraphicsDevice has no live backend owner.");

        if (Status != GraphicsDeviceStatus.Ready)
        {
            throw new InvalidOperationException("The GraphicsDevice stopped before the transient buffer request was executed.");
        }

        IGraphicsBackendBuffer? backendBuffer = null;
        GraphicsResourceOwner? resourceOwner = null;

        try
        {
            backendBuffer = backendDevice.CreateBuffer(description, allocationLease.BackendAllocation);
            var handle = new GpuBufferHandle(_ownerId, DeviceEpoch, GetNextBufferIndex(), generation: 1);
            resourceOwner = new GraphicsResourceOwner(
                this,
                allocationInfo: null,
                backendBuffer,
                retirementReservation,
                bufferDescription: description,
                allocationLease: allocationLease);
            var buffer = new GpuBuffer(handle, description, DeviceEpoch, resourceOwner);

            if (!_resources.Add(resourceOwner))
            {
                throw new InvalidOperationException("The newly created transient Graphics buffer already exists in its owner registry.");
            }

            backendBuffer = null;
            resourceOwner = null;
            return buffer;
        }
        catch (Exception exception)
        {
            if (resourceOwner is not null)
            {
                try
                {
                    backendBuffer?.Dispose();

                    if (resourceOwner.ReleaseAllocationLease() is GraphicsResourceAllocationInfo allocationInfo)
                    {
                        ReleaseResourceAllocation(allocationInfo);
                    }

                    resourceOwner.RetirementReservation.Dispose();
                    resourceOwner.MarkRetired();
                }
                catch (Exception cleanupException)
                {
                    QuarantineResource(resourceOwner, cleanupException);
                    throw new AggregateException(exception, cleanupException);
                }
            }
            else if (backendBuffer is not null)
            {
                try
                {
                    backendBuffer.Dispose();
                    retirementReservation.Dispose();
                }
                catch (Exception cleanupException)
                {
                    QuarantineBackendResource(backendBuffer, cleanupException);
                    retirementReservation.Dispose();
                    throw new AggregateException(exception, cleanupException);
                }
            }
            else
            {
                retirementReservation.Dispose();
            }

            throw;
        }
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Transient shared-allocation rollback must unwind every created buffer and quarantine native blocks that cannot be released safely.")]
    private GraphicsTransientBufferGroup CreateTransientBufferGroupOnOwner(
        ImmutableArray<GraphBufferVersion> versions,
        List<GraphicsOwnerRequestQueue.GraphicsRetirementReservation?> reservations)
    {
        if (!IsOwnerThread)
        {
            throw new InvalidOperationException("Transient buffer groups must be created on the graphics owner thread.");
        }

        var backendDevice = _backendDevice
            ?? throw new InvalidOperationException("The GraphicsDevice has no live backend owner.");

        if (Status != GraphicsDeviceStatus.Ready)
        {
            throw new InvalidOperationException("The GraphicsDevice stopped before the transient buffer group was executed.");
        }

        var description = versions[0].Description;
        var allocationInfo = backendDevice.GetBufferAllocationInfo(description);
        ArgumentOutOfRangeException.ThrowIfLessThan(allocationInfo.SizeBytes, description.ByteLength);
        ReserveResourceAllocation(description.MemoryKind, allocationInfo);

        IGraphicsBackendBufferAllocation? backendAllocation = null;
        GraphicsResourceAllocationLease? allocationLease = null;
        var buffers = new List<GpuBuffer>(versions.Length);

        try
        {
            backendAllocation = backendDevice.CreateBufferAllocation(description, allocationInfo);
            allocationLease = new GraphicsResourceAllocationLease(backendAllocation, allocationInfo);
            backendAllocation = null;

            for (var index = 0; index < versions.Length; index++)
            {
                var reservation = reservations[index]
                    ?? throw new InvalidOperationException("A transient buffer group lost a retirement reservation.");
                var buffer = CreateBufferFromAllocationOnOwner(
                    versions[index].Description,
                    allocationLease,
                    reservation);
                buffers.Add(buffer);
                reservations[index] = null;
            }

            return new GraphicsTransientBufferGroup([.. buffers]);
        }
        catch (Exception exception)
        {
            var cleanupFailures = new List<Exception>();

            for (var index = buffers.Count - 1; index >= 0; index--)
            {
                var buffer = buffers[index];

                try
                {
                    RetireResourceOnOwner(buffer.ResourceOwner);
                }
                catch (Exception cleanupException)
                {
                    QuarantineResource(buffer.ResourceOwner, cleanupException);
                    cleanupFailures.Add(cleanupException);
                }
            }

            if ((allocationLease is not null)
                && !allocationLease.IsReleased
                && (allocationLease.ReferenceCount == 0))
            {
                var allocationToQuarantine = allocationLease.BackendAllocation;
                GraphicsResourceAllocationInfo? releasedAllocationInfo = null;

                try
                {
                    releasedAllocationInfo = allocationLease.ReleaseUnreferenced();
                }
                catch (Exception cleanupException)
                {
                    QuarantineBackendResource(allocationToQuarantine, cleanupException);
                    cleanupFailures.Add(cleanupException);
                }

                if (releasedAllocationInfo is GraphicsResourceAllocationInfo allocationToRelease)
                {
                    try
                    {
                        ReleaseResourceAllocation(allocationToRelease);
                    }
                    catch (Exception cleanupException)
                    {
                        QuarantineBackendResource(allocationToQuarantine, cleanupException);
                        cleanupFailures.Add(cleanupException);
                    }
                }
            }
            else if (backendAllocation is not null)
            {
                try
                {
                    backendAllocation.Dispose();
                    ReleaseResourceAllocation(allocationInfo);
                }
                catch (Exception cleanupException)
                {
                    QuarantineBackendResource(backendAllocation, cleanupException);
                    cleanupFailures.Add(cleanupException);
                }
            }
            else if (allocationLease is null && buffers.Count == 0)
            {
                ReleaseResourceAllocation(allocationInfo);
            }

            ReleaseTransientBufferReservations(reservations);

            if (cleanupFailures.Count != 0)
            {
                cleanupFailures.Insert(0, exception);
                throw new AggregateException("Transient buffer group creation and rollback both failed.", cleanupFailures);
            }

            throw;
        }
    }

    private void QuarantineTransientBufferGroup(GraphicsTransientBufferGroup group, Exception exception)
    {
        foreach (var buffer in group.Buffers)
        {
            QuarantineResource(buffer.ResourceOwner, exception);
        }
    }

    private sealed class CreateTransientBufferGroupRequest : IGraphicsDeviceWork
    {
        private readonly CancellationTokenRegistration _cancellationRegistration;
        private readonly CancellationToken _cancellationToken;
        private readonly TaskCompletionSource<GraphicsTransientBufferGroup> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private GraphicsTransientBufferGroup? _createdGroup;
        private readonly GraphicsDevice _device;
        private List<GraphicsOwnerRequestQueue.GraphicsRetirementReservation?>? _reservations;
        private int _state;
        private readonly ImmutableArray<GraphBufferVersion> _versions;

        internal CreateTransientBufferGroupRequest(
            GraphicsDevice device,
            ImmutableArray<GraphBufferVersion> versions,
            List<GraphicsOwnerRequestQueue.GraphicsRetirementReservation?> reservations,
            CancellationToken cancellationToken)
        {
            _device = device;
            _versions = versions;
            _reservations = reservations;
            _cancellationToken = cancellationToken;
            _cancellationRegistration = cancellationToken.Register(
                static state => ((CreateTransientBufferGroupRequest)state!).Cancel(),
                this);
        }

        internal Task<GraphicsTransientBufferGroup> Task => _completion.Task;

        public void Execute(GraphicsDevice device)
        {
            if (Volatile.Read(ref _state) == 1)
            {
                ReleaseReservations();
                _cancellationRegistration.Dispose();
                return;
            }

            try
            {
                var reservations = _reservations
                    ?? throw new InvalidOperationException("The transient buffer group request lost its retirement reservations.");
                var group = device.CreateTransientBufferGroupOnOwner(_versions, reservations);
                _createdGroup = group;
                _reservations = null;

                if (Interlocked.CompareExchange(ref _state, 2, 0) == 0)
                {
                    _ = _completion.TrySetResult(group);
                }
                else
                {
                    group.Dispose();
                }
            }
            finally
            {
                _cancellationRegistration.Dispose();
            }
        }

        public void Fail(Exception exception)
        {
            if (exception is GraphicsDeviceLostException)
            {
                _device.MarkDeviceLost();
            }

            if (_createdGroup is GraphicsTransientBufferGroup group)
            {
                _device.QuarantineTransientBufferGroup(group, exception);
            }

            ReleaseReservations();

            if (Interlocked.CompareExchange(ref _state, 2, 0) == 0)
            {
                _ = _completion.TrySetException(exception);
            }

            _cancellationRegistration.Dispose();
        }

        internal void AdmissionFailed()
        {
            _ = Interlocked.Exchange(ref _state, 2);
            ReleaseReservations();
            _cancellationRegistration.Dispose();
        }

        private void Cancel()
        {
            if (Interlocked.CompareExchange(ref _state, 1, 0) == 0)
            {
                _ = _completion.TrySetCanceled(_cancellationToken);
            }
        }

        private void ReleaseReservations()
        {
            var reservations = Interlocked.Exchange(ref _reservations, null);

            if (reservations is not null)
            {
                ReleaseTransientBufferReservations(reservations);
            }
        }
    }
}
