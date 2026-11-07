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
    private static bool AreTransientTextureDescriptionsCompatible(TextureDescription left, TextureDescription right)
        => GraphTextureAliasingBarrier.AreCompatible(left, right);

    private static void ReleaseTransientTextureReservations(
        List<GraphicsOwnerRequestQueue.GraphicsRetirementReservation?> reservations)
    {
        foreach (var reservation in reservations)
        {
            reservation?.Dispose();
        }
    }

    internal async Task<GraphicsTransientTextureGroup> CreateTransientTextureGroupAsync(
        ImmutableArray<GraphTextureVersion> versions,
        CancellationToken cancellationToken)
    {
        if (versions.IsDefaultOrEmpty || (versions.Length < 2))
        {
            throw new ArgumentException("A transient alias group requires at least two graph textures.", nameof(versions));
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (Status != GraphicsDeviceStatus.Ready)
        {
            throw new InvalidOperationException("The GraphicsDevice is not accepting transient texture groups.");
        }

        var description = versions[0].Description;

        for (var index = 0; index < versions.Length; index++)
        {
            var version = versions[index];

            if ((version.DeviceEpoch != DeviceEpoch)
                || !AreTransientTextureDescriptionsCompatible(description, version.Description))
            {
                throw new ArgumentException("Transient alias groups require compatible same-device texture descriptions.", nameof(versions));
            }
        }

        List<GraphicsOwnerRequestQueue.GraphicsRetirementReservation?>? reservations = [];

        try
        {
            for (var index = 0; index < versions.Length; index++)
            {
                reservations.Add(await _ownerQueue.ReserveRetirementAsync(cancellationToken).ConfigureAwait(false));
            }

            var request = new CreateTransientTextureGroupRequest(this, versions, reservations, cancellationToken);
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
                ReleaseTransientTextureReservations(reservations);
            }
        }
    }

    private GpuTexture CreateTextureFromAllocationOnOwner(
        TextureDescription description,
        GraphicsResourceAllocationLease allocationLease,
        GraphicsOwnerRequestQueue.GraphicsRetirementReservation retirementReservation)
    {
        var backendDevice = _backendDevice
            ?? throw new InvalidOperationException("The GraphicsDevice has no live backend owner.");

        if (Status != GraphicsDeviceStatus.Ready)
        {
            throw new InvalidOperationException("The GraphicsDevice stopped before the transient texture request was executed.");
        }

        IGraphicsBackendTexture? backendTexture = null;
        GraphicsResourceOwner? resourceOwner = null;

        try
        {
            var allocation = allocationLease.BackendAllocation as IGraphicsBackendTextureAllocation
                ?? throw new InvalidOperationException("The shared Graphics allocation is not a texture allocation.");
            backendTexture = backendDevice.CreateTexture(description, allocation);
            var handle = new GpuTextureHandle(_ownerId, DeviceEpoch, GetNextTextureIndex(), generation: 1);
            resourceOwner = new GraphicsResourceOwner(
                this,
                allocationInfo: null,
                backendTexture,
                retirementReservation,
                textureDescription: description,
                allocationLease: allocationLease);
            var texture = new GpuTexture(handle, description, DeviceEpoch, resourceOwner);

            if (!_resources.Add(resourceOwner))
            {
                throw new InvalidOperationException("The newly created transient Graphics texture already exists in its owner registry.");
            }

            backendTexture = null;
            resourceOwner = null;
            return texture;
        }
        catch (Exception exception)
        {
            if (resourceOwner is not null)
            {
                try
                {
                    backendTexture?.Dispose();

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
            else if (backendTexture is not null)
            {
                try
                {
                    backendTexture.Dispose();
                    retirementReservation.Dispose();
                }
                catch (Exception cleanupException)
                {
                    QuarantineBackendResource(backendTexture, cleanupException);
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
        Justification = "Transient shared-allocation rollback must unwind every created texture and quarantine native blocks that cannot be released safely.")]
    private GraphicsTransientTextureGroup CreateTransientTextureGroupOnOwner(
        ImmutableArray<GraphTextureVersion> versions,
        List<GraphicsOwnerRequestQueue.GraphicsRetirementReservation?> reservations)
    {
        if (!IsOwnerThread)
        {
            throw new InvalidOperationException("Transient texture groups must be created on the graphics owner thread.");
        }

        var backendDevice = _backendDevice
            ?? throw new InvalidOperationException("The GraphicsDevice has no live backend owner.");

        if (Status != GraphicsDeviceStatus.Ready)
        {
            throw new InvalidOperationException("The GraphicsDevice stopped before the transient texture group was executed.");
        }

        var description = versions[0].Description;
        var allocationInfo = backendDevice.GetTransientTextureAllocationInfo(description);
        ReserveResourceAllocation(GpuMemoryKind.DeviceLocal, allocationInfo);

        IGraphicsBackendTextureAllocation? backendAllocation = null;
        GraphicsResourceAllocationLease? allocationLease = null;
        var textures = new List<GpuTexture>(versions.Length);

        try
        {
            backendAllocation = backendDevice.CreateTextureAllocation(description, allocationInfo);
            allocationLease = new GraphicsResourceAllocationLease(backendAllocation, allocationInfo);
            backendAllocation = null;

            for (var index = 0; index < versions.Length; index++)
            {
                var reservation = reservations[index]
                    ?? throw new InvalidOperationException("A transient texture group lost a retirement reservation.");
                var texture = CreateTextureFromAllocationOnOwner(
                    versions[index].Description,
                    allocationLease,
                    reservation);
                textures.Add(texture);
                reservations[index] = null;
            }

            return new GraphicsTransientTextureGroup([.. textures]);
        }
        catch (Exception exception)
        {
            var cleanupFailures = new List<Exception>();

            for (var index = textures.Count - 1; index >= 0; index--)
            {
                var texture = textures[index];

                try
                {
                    RetireResourceOnOwner(texture.ResourceOwner);
                }
                catch (Exception cleanupException)
                {
                    QuarantineResource(texture.ResourceOwner, cleanupException);
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
            else if (allocationLease is null && textures.Count == 0)
            {
                ReleaseResourceAllocation(allocationInfo);
            }

            ReleaseTransientTextureReservations(reservations);

            if (cleanupFailures.Count != 0)
            {
                cleanupFailures.Insert(0, exception);
                throw new AggregateException("Transient texture group creation and rollback both failed.", cleanupFailures);
            }

            throw;
        }
    }

    private void QuarantineTransientTextureGroup(GraphicsTransientTextureGroup group, Exception exception)
    {
        foreach (var texture in group.Textures)
        {
            QuarantineResource(texture.ResourceOwner, exception);
        }
    }

    private sealed class CreateTransientTextureGroupRequest : IGraphicsDeviceWork
    {
        private readonly CancellationTokenRegistration _cancellationRegistration;
        private readonly CancellationToken _cancellationToken;
        private readonly TaskCompletionSource<GraphicsTransientTextureGroup> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private GraphicsTransientTextureGroup? _createdGroup;
        private readonly GraphicsDevice _device;
        private List<GraphicsOwnerRequestQueue.GraphicsRetirementReservation?>? _reservations;
        private int _state;
        private readonly ImmutableArray<GraphTextureVersion> _versions;

        internal CreateTransientTextureGroupRequest(
            GraphicsDevice device,
            ImmutableArray<GraphTextureVersion> versions,
            List<GraphicsOwnerRequestQueue.GraphicsRetirementReservation?> reservations,
            CancellationToken cancellationToken)
        {
            _device = device;
            _versions = versions;
            _reservations = reservations;
            _cancellationToken = cancellationToken;
            _cancellationRegistration = cancellationToken.Register(
                static state => ((CreateTransientTextureGroupRequest)state!).Cancel(),
                this);
        }

        internal Task<GraphicsTransientTextureGroup> Task => _completion.Task;

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
                    ?? throw new InvalidOperationException("The transient texture group request lost its retirement reservations.");
                var group = device.CreateTransientTextureGroupOnOwner(_versions, reservations);
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

            if (_createdGroup is GraphicsTransientTextureGroup group)
            {
                _device.QuarantineTransientTextureGroup(group, exception);
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
                ReleaseTransientTextureReservations(reservations);
            }
        }
    }
}
