// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace TerraFX.Graphics;

/// <summary>Owns one host-created native graphics device on its dedicated owner thread.</summary>
[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The event is released by the dedicated graphics owner thread after shutdown; callers control lifetime through ShutdownAsync.")]
public sealed class GraphicsDevice
{
    // Keep an owner rooted if native disposal cannot confirm that its resources were released.
    private static readonly ConcurrentBag<IGraphicsBackendDevice> s_quarantinedBackendDevices = [];

    private IGraphicsBackendDevice? _backendDevice;
    private readonly GraphicsCapabilities _capabilities;
    private readonly uint _ownerId;
    private readonly Thread _ownerThread;
    private readonly TaskCompletionSource _shutdownCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly AutoResetEvent _shutdownRequested = new(initialState: false);
    private readonly Task _shutdownTask;
    private int _status;

    private GraphicsDevice(uint ownerId, IGraphicsBackendDevice backendDevice)
    {
        _ownerId = ownerId;
        _backendDevice = backendDevice;
        _capabilities = backendDevice.Capabilities;
        _ownerThread = Thread.CurrentThread;
        DeviceEpoch = 1;
        _status = (int)GraphicsDeviceStatus.Ready;
        _shutdownTask = WaitForShutdownAndOwnerExitAsync(_shutdownCompletion.Task, _ownerThread);
    }

    /// <summary>Gets the immutable capabilities reported by the selected backend.</summary>
    public GraphicsCapabilities Capabilities => _capabilities;

    /// <summary>Gets the device epoch, initially one for this host-issued owner.</summary>
    public uint DeviceEpoch { get; }

    /// <summary>Gets the current lifecycle state.</summary>
    public GraphicsDeviceStatus Status
        => (GraphicsDeviceStatus)Volatile.Read(ref _status);

    /// <summary>Gets the host-issued owner identity used by device resource handles.</summary>
    internal uint OwnerId => _ownerId;

    internal static Task<GraphicsDevice> CreateAsync(
        uint ownerId,
        GraphicsBackendRegistration registration,
        GraphicsDeviceProfile profile,
        GraphicsPlatformKind platformKind,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfZero(ownerId);
        ArgumentNullException.ThrowIfNull(registration);
        ValidateStartupProfile(registration.Backend, profile, platformKind);

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<GraphicsDevice>(cancellationToken);
        }

        return new GraphicsDeviceStartup(
            ownerId,
            registration,
            profile,
            platformKind,
            cancellationToken).Start();
    }

    internal static void ValidateStartupProfile(
        GraphicsBackend backend,
        GraphicsDeviceProfile profile,
        GraphicsPlatformKind platformKind)
    {
        if ((backend == GraphicsBackend.None) || !Enum.IsDefined(backend))
        {
            throw new ArgumentOutOfRangeException(nameof(backend));
        }

        if ((profile == GraphicsDeviceProfile.None) || !Enum.IsDefined(profile))
        {
            throw new ArgumentOutOfRangeException(nameof(profile));
        }

        if (!Enum.IsDefined(platformKind))
        {
            throw new ArgumentOutOfRangeException(nameof(platformKind));
        }

        if (profile == GraphicsDeviceProfile.OffscreenNative)
        {
            if (platformKind != GraphicsPlatformKind.None)
            {
                throw new ArgumentException("OffscreenNative device creation must not select a native window platform.", nameof(platformKind));
            }

            return;
        }

        if (platformKind == GraphicsPlatformKind.None)
        {
            throw new ArgumentException("Interactive device creation requires an initialized native window platform.", nameof(platformKind));
        }

        if ((backend == GraphicsBackend.D3D12) && (platformKind != GraphicsPlatformKind.Win32))
        {
            throw new PlatformNotSupportedException("Interactive D3D12 device creation requires Win32.");
        }
    }

    private static async Task WaitForShutdownAndOwnerExitAsync(Task shutdownTask, Thread ownerThread)
    {
        try
        {
            await shutdownTask.ConfigureAwait(false);
        }
        finally
        {
            await Task.Run(ownerThread.Join).ConfigureAwait(false);
        }
    }

    internal Task ShutdownAsync()
    {
        while (true)
        {
            var currentStatus = Volatile.Read(ref _status);

            if (currentStatus is (int)GraphicsDeviceStatus.Stopping or (int)GraphicsDeviceStatus.Disposed)
            {
                return _shutdownTask;
            }

            if ((currentStatus == (int)GraphicsDeviceStatus.None)
                || !Enum.IsDefined((GraphicsDeviceStatus)currentStatus))
            {
                throw new InvalidOperationException("The GraphicsDevice has an invalid lifecycle state.");
            }

            if (Interlocked.CompareExchange(ref _status, (int)GraphicsDeviceStatus.Stopping, currentStatus) == currentStatus)
            {
                _ = _shutdownRequested.Set();
                return _shutdownTask;
            }
        }
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "The owner thread must relay initialization and native-release failures through its startup or shutdown task.")]
    private void RunOwnerLoop()
    {
        try
        {
            _ = _shutdownRequested.WaitOne();
        }
        catch (Exception exception)
        {
            Volatile.Write(ref _status, (int)GraphicsDeviceStatus.Stopping);

            if (_backendDevice is not null)
            {
                s_quarantinedBackendDevices.Add(_backendDevice);
                _backendDevice = null;
            }

            _ = _shutdownCompletion.TrySetException(exception);
            _shutdownRequested.Dispose();
            return;
        }

        try
        {
            var backendDevice = _backendDevice
                ?? throw new InvalidOperationException("A ready GraphicsDevice must own its backend device.");
            backendDevice.Dispose();
            _backendDevice = null;
            Volatile.Write(ref _status, (int)GraphicsDeviceStatus.Disposed);
            _ = _shutdownCompletion.TrySetResult();
        }
        catch (Exception exception)
        {
            var backendDevice = _backendDevice;

            if (backendDevice is not null)
            {
                s_quarantinedBackendDevices.Add(backendDevice);
                _backendDevice = null;
            }

            _ = _shutdownCompletion.TrySetException(exception);
        }
        finally
        {
            _shutdownRequested.Dispose();
        }
    }

    private sealed class GraphicsDeviceStartup
    {
        private readonly CancellationToken _cancellationToken;
        private readonly TaskCompletionSource<GraphicsDevice> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly uint _ownerId;
        private readonly GraphicsPlatformKind _platformKind;
        private readonly GraphicsDeviceProfile _profile;
        private readonly GraphicsBackendRegistration _registration;

        internal GraphicsDeviceStartup(
            uint ownerId,
            GraphicsBackendRegistration registration,
            GraphicsDeviceProfile profile,
            GraphicsPlatformKind platformKind,
            CancellationToken cancellationToken)
        {
            _ownerId = ownerId;
            _registration = registration;
            _profile = profile;
            _platformKind = platformKind;
            _cancellationToken = cancellationToken;
        }

        internal Task<GraphicsDevice> Start()
        {
            var ownerThread = new Thread(RunOwner) {
                IsBackground = true,
                Name = "TerraFX.Graphics.Owner",
            };
            ownerThread.Start();
            return _completion.Task;
        }

        [SuppressMessage(
            "Usage",
            "CA1031:Do not catch general exception types",
            Justification = "The startup task must receive factory, admission, cancellation, and unwind failures from the dedicated owner thread.")]
        private void RunOwner()
        {
            IGraphicsBackendDevice? backendDevice = null;

            try
            {
                _cancellationToken.ThrowIfCancellationRequested();
                backendDevice = _registration.Factory.Create(
                    _ownerId,
                    _profile,
                    _platformKind,
                    _cancellationToken);
                ArgumentNullException.ThrowIfNull(backendDevice);
                _cancellationToken.ThrowIfCancellationRequested();

                var capabilities = backendDevice.Capabilities;

                if (capabilities.Backend != _registration.Backend)
                {
                    throw new InvalidOperationException("The registered factory created a device for a different backend.");
                }

                var missingRequirements = GraphicsCapabilityAdmission.GetMissingRequirements(capabilities, _profile);

                if (!missingRequirements.IsEmpty)
                {
                    throw new NotSupportedException(
                        $"The selected adapter does not satisfy the {_profile} profile: {string.Join("; ", missingRequirements)}.");
                }

                _cancellationToken.ThrowIfCancellationRequested();

                var device = new GraphicsDevice(_ownerId, backendDevice);
                backendDevice = null;
                _completion.SetResult(device);
                device.RunOwnerLoop();
            }
            catch (Exception exception)
            {
                if (backendDevice is not null)
                {
                    try
                    {
                        backendDevice.Dispose();
                    }
                    catch (Exception cleanupException)
                    {
                        s_quarantinedBackendDevices.Add(backendDevice);
                        exception = new AggregateException(exception, cleanupException);
                    }
                }

                if (_cancellationToken.IsCancellationRequested && (exception is OperationCanceledException))
                {
                    _ = _completion.TrySetCanceled(_cancellationToken);
                }
                else
                {
                    _ = _completion.TrySetException(exception);
                }
            }
        }
    }
}
