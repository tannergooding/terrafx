// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using TerraFX;

namespace TerraFX.Graphics;

public sealed partial class GraphicsDevice
{
    internal async Task<Surface> CreateSurfaceAsync(
        GraphicsNativeSurfaceBinding binding,
        PixelSize clientPixelSize,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Status != GraphicsDeviceStatus.Ready)
        {
            throw new InvalidOperationException("The GraphicsDevice is not accepting surface requests.");
        }

        if (Capabilities.Profile != GraphicsDeviceProfile.Interactive)
        {
            throw new InvalidOperationException("OffscreenNative devices do not create presentation surfaces.");
        }

        if (binding.PlatformKind != _platformKind)
        {
            throw new ArgumentException("The native binding does not match the initialized graphics platform.", nameof(binding));
        }

        ValidateSurfaceExtent(clientPixelSize);
        var reservation = await _ownerQueue.ReserveRetirementAsync(cancellationToken).ConfigureAwait(false);
        var request = new CreateSurfaceRequest(
            this,
            binding,
            clientPixelSize,
            reservation,
            cancellationToken);

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

    internal async Task<SurfaceResizeReceipt> ResizeSurfaceAsync(
        Surface surface,
        PixelSize clientPixelSize,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(surface);
        cancellationToken.ThrowIfCancellationRequested();

        if (!ReferenceEquals(surface.Device, this))
        {
            throw new ArgumentException("The Surface belongs to a different GraphicsDevice.", nameof(surface));
        }

        ObjectDisposedException.ThrowIf(!surface.IsLive, surface);

        if (Status != GraphicsDeviceStatus.Ready)
        {
            throw new InvalidOperationException("The GraphicsDevice is not accepting surface resize requests.");
        }

        ValidateSurfaceExtent(clientPixelSize);
        var request = new ResizeSurfaceRequest(surface, clientPixelSize);

        try
        {
            await _ownerQueue.WriteRequestAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            request.AdmissionFailed();
            throw;
        }

        return await request.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    internal Task RetireSurfaceAsync(Surface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if (!ReferenceEquals(surface.Device, this))
        {
            throw new ArgumentException("The Surface belongs to a different GraphicsDevice.", nameof(surface));
        }

        if (!surface.TryInvalidate())
        {
            return surface.RetirementTask;
        }

        var request = new RetireSurfaceRequest(this, surface);

        if (!_ownerQueue.TryEnqueueRetirement(request, surface.RetirementReservation))
        {
            return surface.RetirementTask;
        }

        return request.Task;
    }

    internal async Task<PresentationReceipt> WaitForPresentationAsync(
        Surface surface,
        long presentationSequence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(presentationSequence);
        cancellationToken.ThrowIfCancellationRequested();

        if (!ReferenceEquals(surface.Device, this))
        {
            throw new ArgumentException("The Surface belongs to a different GraphicsDevice.", nameof(surface));
        }

        if (surface.BackendSurface.TryGetCompletedPresentation(
            presentationSequence,
            DeviceEpoch,
            out var completedReceipt))
        {
            return completedReceipt
                ?? throw new InvalidOperationException("The backend presentation history returned no receipt.");
        }

        ObjectDisposedException.ThrowIf(surface.IsRetired || surface.IsQuarantined, surface);

        if (Status != GraphicsDeviceStatus.Ready)
        {
            throw new InvalidOperationException("The GraphicsDevice is not accepting presentation waits.");
        }

        var request = new WaitForPresentationRequest(
            surface,
            presentationSequence,
            cancellationToken);

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

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "A failed backend surface creation must release partially acquired native ownership and preserve both creation and cleanup failures.")]
    private Surface CreateSurfaceOnOwner(
        GraphicsNativeSurfaceBinding binding,
        PixelSize clientPixelSize,
        GraphicsOwnerRequestQueue.GraphicsRetirementReservation retirementReservation)
    {
        var backendDevice = _backendDevice
            ?? throw new InvalidOperationException("The GraphicsDevice has no live backend owner.");

        if (!IsOwnerThread)
        {
            throw new InvalidOperationException("Native surfaces must be created on the graphics owner thread.");
        }

        if (Status != GraphicsDeviceStatus.Ready)
        {
            throw new InvalidOperationException("The GraphicsDevice stopped before the surface request was executed.");
        }

        IGraphicsBackendSurface? backendSurface = null;

        try
        {
            backendSurface = backendDevice.CreateSurface(binding, clientPixelSize)
                ?? throw new InvalidOperationException("The selected backend returned no presentation surface.");
            var capabilities = backendSurface.Capabilities;
            var isSuspended = (clientPixelSize.Width == 0) || (clientPixelSize.Height == 0);

            if ((capabilities.SurfaceEpoch != binding.SurfaceEpoch)
                || (backendSurface.ClientPixelSize != clientPixelSize)
                || (isSuspended ? capabilities.SwapchainEpoch != 0 : capabilities.SwapchainEpoch == 0))
            {
                throw new InvalidOperationException("The backend surface did not publish the requested extent and native epochs.");
            }

            var surface = new Surface(this, backendSurface, retirementReservation);

            if (!_surfaces.Add(surface))
            {
                throw new InvalidOperationException("The GraphicsDevice registered a duplicate Surface owner.");
            }

            backendSurface = null;
            return surface;
        }
        catch (Exception creationException)
        {
            if (backendSurface is null)
            {
                throw;
            }

            try
            {
                backendSurface.Dispose();
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException(creationException, cleanupException);
            }

            throw;
        }
    }

    private void QuarantineSurface(Surface surface, Exception exception)
    {
        if (surface.IsQuarantined || surface.IsRetired)
        {
            return;
        }

        _ = _surfaces.Remove(surface);
        s_quarantinedBackendSurfaces.Add(surface.BackendSurface);
        s_quarantinedGraphicsDevices.Add(this);
        _hasQuarantinedResources = true;
        _retirementFailure ??= exception;
        surface.MarkQuarantined(exception);
        surface.RetirementReservation.Dispose();
    }

    private void RetireSurfaceOnOwner(Surface surface)
    {
        if (!IsOwnerThread)
        {
            throw new InvalidOperationException("Native surfaces must be retired on the graphics owner thread.");
        }

        if (surface.IsRetired || surface.IsQuarantined)
        {
            return;
        }

        try
        {
            WaitForPendingGraphSubmissions();

            _ = surface.BackendSurface.WaitForPresentations(DeviceEpoch);

            surface.BackendSurface.Dispose();
            surface.RetirementReservation.Dispose();
            _ = _surfaces.Remove(surface);
            surface.MarkRetired();
        }
        catch (Exception exception)
        {
            QuarantineSurface(surface, exception);
            throw;
        }
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Shutdown quarantines a failed surface release and continues retiring independent surfaces.")]
    private void RetireSurfacesForShutdown()
    {
        var surfaces = new Surface[_surfaces.Count];
        _surfaces.CopyTo(surfaces);

        foreach (var surface in surfaces)
        {
            _ = surface.TryInvalidate();

            try
            {
                RetireSurfaceOnOwner(surface);
            }
            catch (Exception)
            {
            }
        }
    }

    private void ValidateSurfaceExtent(PixelSize clientPixelSize)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            clientPixelSize.Width,
            Capabilities.Limits.MaximumTextureDimension2D,
            nameof(clientPixelSize));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            clientPixelSize.Height,
            Capabilities.Limits.MaximumTextureDimension2D,
            nameof(clientPixelSize));
    }

    private void WaitForPendingGraphSubmissions()
    {
        if (!IsOwnerThread)
        {
            throw new InvalidOperationException("Surface lifecycle changes are restricted to the graphics owner thread.");
        }

        var maximumSubmittedFence = 0L;

        foreach (var submission in _pendingGraphSubmissions)
        {
            maximumSubmittedFence = Math.Max(maximumSubmittedFence, submission.SubmittedFence);
        }

        if (maximumSubmittedFence > 0)
        {
            var backendDevice = _backendDevice
                ?? throw new InvalidOperationException("The GraphicsDevice has no live backend owner.");
            backendDevice.WaitForGraphFence(maximumSubmittedFence);
        }
    }

    private sealed class CreateSurfaceRequest : IGraphicsDeviceWork
    {
        private readonly GraphicsNativeSurfaceBinding _binding;
        private readonly CancellationTokenRegistration _cancellationRegistration;
        private readonly CancellationToken _cancellationToken;
        private readonly PixelSize _clientPixelSize;
        private readonly TaskCompletionSource<Surface> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Surface? _createdSurface;
        private readonly GraphicsDevice _device;
        private GraphicsOwnerRequestQueue.GraphicsRetirementReservation? _retirementReservation;
        private int _state;

        internal CreateSurfaceRequest(
            GraphicsDevice device,
            GraphicsNativeSurfaceBinding binding,
            PixelSize clientPixelSize,
            GraphicsOwnerRequestQueue.GraphicsRetirementReservation retirementReservation,
            CancellationToken cancellationToken)
        {
            _device = device;
            _binding = binding;
            _clientPixelSize = clientPixelSize;
            _retirementReservation = retirementReservation;
            _cancellationToken = cancellationToken;
            _cancellationRegistration = cancellationToken.Register(
                static state => ((CreateSurfaceRequest)state!).Cancel(),
                this);
        }

        internal Task<Surface> Task => _completion.Task;

        public void Execute(GraphicsDevice device)
        {
            if (Volatile.Read(ref _state) == 1)
            {
                ReleaseReservation();
                _cancellationRegistration.Dispose();
                return;
            }

            try
            {
                var reservation = _retirementReservation
                    ?? throw new InvalidOperationException("The Surface request has no retirement reservation.");
                var surface = device.CreateSurfaceOnOwner(_binding, _clientPixelSize, reservation);
                _createdSurface = surface;
                _retirementReservation = null;

                if (Interlocked.CompareExchange(ref _state, 2, 0) == 0)
                {
                    _ = _completion.TrySetResult(surface);
                }
                else
                {
                    device.RetireSurfaceOnOwner(surface);
                }
            }
            finally
            {
                _cancellationRegistration.Dispose();
            }
        }

        public void Fail(Exception exception)
        {
            if (_createdSurface is not null)
            {
                _device.QuarantineSurface(_createdSurface, exception);
            }

            ReleaseReservation();

            if (Interlocked.CompareExchange(ref _state, 2, 0) == 0)
            {
                _ = _completion.TrySetException(exception);
            }

            _cancellationRegistration.Dispose();
        }

        internal void AdmissionFailed()
        {
            _ = Interlocked.Exchange(ref _state, 2);
            ReleaseReservation();
            _cancellationRegistration.Dispose();
        }

        private void Cancel()
        {
            if (Interlocked.CompareExchange(ref _state, 1, 0) == 0)
            {
                _ = _completion.TrySetCanceled(_cancellationToken);
            }
        }

        private void ReleaseReservation()
        {
            var reservation = Interlocked.Exchange(ref _retirementReservation, null);
            reservation?.Dispose();
        }
    }

    private sealed class ResizeSurfaceRequest : IGraphicsDeviceWork
    {
        private readonly PixelSize _clientPixelSize;
        private readonly TaskCompletionSource<SurfaceResizeReceipt> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Surface _surface;

        internal ResizeSurfaceRequest(Surface surface, PixelSize clientPixelSize)
        {
            _surface = surface;
            _clientPixelSize = clientPixelSize;
        }

        internal Task<SurfaceResizeReceipt> Task => _completion.Task;

        public void Execute(GraphicsDevice device)
        {
            ObjectDisposedException.ThrowIf(!_surface.IsLive, _surface);

            var backendSurface = _surface.BackendSurface;
            SurfaceCapabilities capabilities;

            try
            {
                device.WaitForPendingGraphSubmissions();

                _ = backendSurface.WaitForPresentations(device.DeviceEpoch);

                capabilities = backendSurface.Resize(_clientPixelSize);
            }
            catch
            {
                _surface.PublishBackendState();
                throw;
            }

            var isSuspended = (_clientPixelSize.Width == 0) || (_clientPixelSize.Height == 0);

            if ((capabilities.SurfaceEpoch != _surface.SurfaceEpoch)
                || (backendSurface.ClientPixelSize != _clientPixelSize)
                || (isSuspended ? capabilities.SwapchainEpoch != 0 : capabilities.SwapchainEpoch == 0))
            {
                throw new InvalidOperationException("The backend surface resize did not publish the requested extent and epochs.");
            }

            _surface.PublishBackendState();
            var status = isSuspended ? SurfaceResizeStatus.Suspended : SurfaceResizeStatus.Resized;
            _ = _completion.TrySetResult(new SurfaceResizeReceipt(
                status,
                _clientPixelSize,
                capabilities.SurfaceEpoch,
                capabilities.SwapchainEpoch,
                rejection: null));
        }

        public void Fail(Exception exception)
            => _ = _completion.TrySetException(exception);

        internal void AdmissionFailed()
            => _ = _completion.TrySetException(new InvalidOperationException("The surface resize request was not admitted."));
    }

    private sealed class RetireSurfaceRequest : IGraphicsDeviceWork
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly GraphicsDevice _device;
        private readonly Surface _surface;

        internal RetireSurfaceRequest(GraphicsDevice device, Surface surface)
        {
            _device = device;
            _surface = surface;
        }

        internal Task Task => _completion.Task;

        public void Execute(GraphicsDevice device)
        {
            device.RetireSurfaceOnOwner(_surface);
            _ = _completion.TrySetResult();
        }

        public void Fail(Exception exception)
        {
            _device.QuarantineSurface(_surface, exception);
            _ = _completion.TrySetException(exception);
        }
    }

    private sealed class WaitForPresentationRequest : IGraphicsDeviceWork
    {
        private readonly CancellationToken _cancellationToken;
        private readonly TaskCompletionSource<PresentationReceipt> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly long _presentationSequence;
        private readonly Surface _surface;

        internal WaitForPresentationRequest(
            Surface surface,
            long presentationSequence,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(surface);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(presentationSequence);
            _surface = surface;
            _presentationSequence = presentationSequence;
            _cancellationToken = cancellationToken;
        }

        internal Task<PresentationReceipt> Task => _completion.Task;

        public void Execute(GraphicsDevice device)
        {
            if (_surface.BackendSurface.TryGetCompletedPresentation(
                _presentationSequence,
                device.DeviceEpoch,
                out var completedReceipt))
            {
                var cachedReceipt = completedReceipt
                    ?? throw new InvalidOperationException("The Surface presentation cache returned no receipt.");

                if (cachedReceipt.Status is PresentationStatus.DeviceLost)
                {
                    device.MarkDeviceLost();
                }

                _ = _completion.TrySetResult(cachedReceipt);
                return;
            }

            var receipt = _surface.BackendSurface.WaitForPresentation(
                _presentationSequence,
                device.DeviceEpoch,
                _cancellationToken);

            if (receipt.Status is PresentationStatus.DeviceLost)
            {
                device.MarkDeviceLost();
            }

            _ = _completion.TrySetResult(receipt);
        }

        public void Fail(Exception exception)
        {
            if ((exception is OperationCanceledException) && _cancellationToken.IsCancellationRequested)
            {
                _ = _completion.TrySetCanceled(_cancellationToken);
            }
            else
            {
                _ = _completion.TrySetException(exception);
            }
        }

        internal void AdmissionFailed()
        {
            if (_cancellationToken.IsCancellationRequested)
            {
                _ = _completion.TrySetCanceled(_cancellationToken);
            }
            else
            {
                _ = _completion.TrySetException(new InvalidOperationException("The presentation wait was not admitted."));
            }
        }
    }
}
