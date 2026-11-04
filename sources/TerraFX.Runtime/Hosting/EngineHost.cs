// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Graphics;
using TerraFX.Platform;
using TerraFX.Rendering;

namespace TerraFX.Hosting;

/// <summary>Owns CPU, asset, persistence, platform, and optional offscreen graphics services.</summary>
public sealed class EngineHost : IAsyncDisposable
{
    private const long DefaultAdmissionDomainBytes = 8_589_934_592L;
    private const int MaximumOwnerRequestCount = 512;
    private const int OwnerPumpIntervalMilliseconds = 16;

    [SuppressMessage(
        "Usage",
        "CA2213:Disposable fields should be disposed",
        Justification = "The admission domain is disposed by ShutdownOwnerServices on the dedicated owner thread.")]
    private WorldAdmissionDomain? _admissionDomain;
    private readonly AssetStore _assets;
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private DiagnosticsService? _diagnostics;
    private readonly GraphicsDevice? _graphicsDevice;
    private readonly Lock _ownerRequestGate = new();
    private readonly BlockingCollection<HostActionRequest> _ownerRequests = [with(MaximumOwnerRequestCount)];
    private readonly EngineHostOptions _options;
    private readonly Thread _ownerThread;
    private int _ownerThreadId;
    private readonly WorldPersistence _persistence;
    private PlatformHost? _platform;
    private PlatformHostOwner? _platformOwner;
    private readonly Renderer _renderer;
    private readonly TaskCompletionSource _startupCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _stopTask;
    private bool _stopping;

    [SuppressMessage(
        "Usage",
        "CA2213:Disposable fields should be disposed",
        Justification = "The worker pool is disposed by ShutdownOwnerServices on the dedicated owner thread.")]
    private WorldWorkerPool? _workerPool;
    private readonly List<World> _worlds = [];

    private EngineHost(
        EngineHostOptions options,
        AssetStore assets,
        GraphicsDevice? graphicsDevice)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(assets);
        _options = options;
        _assets = assets;
        _graphicsDevice = graphicsDevice;
        _persistence = new WorldPersistence(assets, options.PersistenceMetadata);
        _renderer = Renderer.CreateHeadless();
        _ownerThread = new Thread(RunOwnerLoop) {
            IsBackground = true,
            Name = "TerraFX.EngineHost",
        };
    }

    /// <summary>Gets the host-owned selected AssetStore.</summary>
    public AssetStore Assets => _assets;

    /// <summary>Gets the task that completes when the owner loop exits.</summary>
    public Task Completion => _completion.Task;

    /// <summary>Gets the host's fixed CPU worker count.</summary>
    public int CpuWorkerCount => _options.CpuWorkerCount;

    /// <summary>Gets the host-owned diagnostic service while the host is running.</summary>
    /// <exception cref="InvalidOperationException">Host startup has not completed.</exception>
    public DiagnosticsService Diagnostics
        => _diagnostics ?? throw new InvalidOperationException("EngineHost startup has not completed.");

    /// <summary>Gets the selected offscreen GraphicsDevice, or null for Headless.</summary>
    public GraphicsDevice? GraphicsDevice => _graphicsDevice;

    /// <summary>Gets the host-owned Persistence service.</summary>
    public WorldPersistence Persistence => _persistence;

    /// <summary>Gets the host-owned platform service after startup.</summary>
    /// <exception cref="InvalidOperationException">Host startup has not completed.</exception>
    public PlatformHost Platform
        => _platform ?? throw new InvalidOperationException("EngineHost startup has not completed.");

    /// <summary>Gets the CPU render-snapshot extraction service.</summary>
    public Renderer Renderer => _renderer;

    /// <summary>Starts a Headless or OffscreenNative host with its explicitly selected services.</summary>
    /// <param name="options">The immutable host profile and service selections.</param>
    /// <param name="cancellationToken">The token that cancels startup before the owner thread is admitted.</param>
    /// <returns>A running host whose owner thread and selected services are initialized.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="options"/> contains invalid scalar settings.</exception>
    /// <exception cref="NotSupportedException">Interactive startup is not yet available because Surface/WSI ownership is not implemented.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested before the host owner thread was admitted.</exception>
    /// <exception cref="System.IO.InvalidDataException">The selected package fails AssetStore admission.</exception>
    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Startup rollback must release every service already acquired and aggregate cleanup failures with the original startup exception.")]
    public static async Task<EngineHost> StartAsync(
        EngineHostOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.PersistenceMetadata);
        cancellationToken.ThrowIfCancellationRequested();

        if (options.Profile is HostProfile.Interactive)
        {
            throw new NotSupportedException("Interactive EngineHost startup requires the unfinished Surface/WSI and live-input owner path.");
        }

        AssetStore? assets = null;
        GraphicsDevice? graphicsDevice = null;
        EngineHost? host = null;

        try
        {
            assets = await AssetStore.CreateAsync(options.AssetStoreOptions, cancellationToken).ConfigureAwait(false);

            if (options.Profile is HostProfile.OffscreenNative)
            {
                var registration = options.GraphicsBackendRegistration
                    ?? throw new InvalidOperationException("OffscreenNative startup has no admitted graphics registration.");
                var ownerId = OwnerIdAllocator.Allocate();
                graphicsDevice = await GraphicsDevice.CreateAsync(
                    ownerId,
                    registration,
                    GraphicsDeviceProfile.OffscreenNative,
                    GraphicsPlatformKind.None,
                    cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            host = new EngineHost(options, assets, graphicsDevice);
            assets = null;
            graphicsDevice = null;
            host.StartOwnerThread();
            await host._startupCompletion.Task.ConfigureAwait(false);
            return host;
        }
        catch (Exception startupException)
        {
            var cleanupFailures = new List<Exception>();

            if (host is not null)
            {
                try
                {
                    await host.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception cleanupException)
                {
                    cleanupFailures.Add(cleanupException);
                }
            }
            else
            {
                if (graphicsDevice is not null)
                {
                    try
                    {
                        await graphicsDevice.ShutdownAsync().ConfigureAwait(false);
                    }
                    catch (Exception cleanupException)
                    {
                        cleanupFailures.Add(cleanupException);
                    }
                }

                if (assets is not null)
                {
                    try
                    {
                        await assets.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception cleanupException)
                    {
                        cleanupFailures.Add(cleanupException);
                    }
                }
            }

            if (cleanupFailures.Count != 0)
            {
                cleanupFailures.Insert(0, startupException);
                throw new AggregateException("EngineHost startup and rollback both failed.", cleanupFailures);
            }

            throw;
        }
    }

    /// <summary>Waits for accepted owner callbacks and then disposes all host-owned services.</summary>
    public ValueTask DisposeAsync()
        => new(StopAsync(CancellationToken.None));

    /// <summary>Queues one bounded synchronous callback on the owner thread.</summary>
    /// <param name="action">The callback that receives its invocation-scoped EngineContext.</param>
    /// <param name="cancellationToken">The token that cancels the callback before it executes.</param>
    /// <returns>A task completed after the callback returns or faults.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">The host is stopping or has stopped.</exception>
    /// <exception cref="InvalidOperationException">The bounded owner mailbox is full.</exception>
    public Task InvokeAsync(Action<EngineContext> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        var request = new HostActionRequest(action, cancellationToken);

        if (!TryEnqueue(request))
        {
            request.Fail(new InvalidOperationException($"The EngineHost owner mailbox is full (capacity {MaximumOwnerRequestCount})."));
        }

        return request.Completion;
    }

    /// <summary>Stops owner-thread admission, joins all Worlds, and disposes host-owned services.</summary>
    /// <param name="cancellationToken">The token that cancels only this wait, not accepted shutdown.</param>
    /// <returns>The shared shutdown task, or a canceled wait while shutdown continues.</returns>
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task stopTask;

        lock (_ownerRequestGate)
        {
            if (!_stopping)
            {
                _stopping = true;
                _ownerRequests.CompleteAdding();
            }

            _stopTask ??= StopAndDisposeAsync();
            stopTask = _stopTask;
        }

        return stopTask.WaitAsync(_options.ShutdownTimeout, cancellationToken);
    }

    /// <summary>Attempts to queue one bounded synchronous callback on the owner thread.</summary>
    /// <param name="action">The callback that receives its invocation-scoped EngineContext.</param>
    /// <param name="cancellationToken">The token checked before mailbox admission.</param>
    /// <param name="completion">The callback completion task, or null when the mailbox is full.</param>
    /// <returns>False only when the bounded owner mailbox is full.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is null.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested before admission.</exception>
    /// <exception cref="ObjectDisposedException">The host is stopping or has stopped.</exception>
    public bool TryInvoke(
        Action<EngineContext> action,
        CancellationToken cancellationToken,
        [NotNullWhen(true)] out Task? completion)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        var request = new HostActionRequest(action, cancellationToken);

        if (!TryEnqueue(request))
        {
            completion = null;
            return false;
        }

        completion = request.Completion;
        return true;
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "A failed hosted World registration is disposed before its admission exception is rethrown.")]
    internal World CreateWorldFromContext(WorldOptions options)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(options);

        lock (_ownerRequestGate)
        {
            ObjectDisposedException.ThrowIf(_stopping, this);
        }

        var workerPool = _workerPool
            ?? throw new InvalidOperationException("The EngineHost worker pool is not initialized.");
        var diagnostics = _diagnostics
            ?? throw new InvalidOperationException("The EngineHost diagnostics service is not initialized.");
        var admissionDomain = _admissionDomain
            ?? throw new InvalidOperationException("The EngineHost admission domain is not initialized.");
        using var packageLease = _assets.AcquirePackageLease();

        if (!ReferenceEquals(options.Registration.Schemas, packageLease.Registration.Schemas))
        {
            throw new ArgumentException("The World registration must use the exact compiled schema registry of the selected package.", nameof(options));
        }

        var world = World.CreateHosted(options, workerPool, diagnostics, admissionDomain);

        try
        {
            _worlds.Add(world);
            return world;
        }
        catch (Exception)
        {
            world.Dispose();
            throw;
        }
    }

    internal void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException("EngineHost owner operations are restricted to its dedicated thread.");
        }
    }

    private void FailPendingRequests(Exception exception)
    {
        lock (_ownerRequestGate)
        {
            _stopping = true;

            if (!_ownerRequests.IsAddingCompleted)
            {
                _ownerRequests.CompleteAdding();
            }
        }

        while (_ownerRequests.TryTake(out var request))
        {
            request.Fail(exception);
        }
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "An owner-loop failure is retained on startup/completion tasks after pending requests and owner services unwind.")]
    private void RunOwnerLoop()
    {
        Exception? failure = null;

        try
        {
            _ownerThreadId = Environment.CurrentManagedThreadId;
            _diagnostics = new DiagnosticsService();
            _workerPool = new WorldWorkerPool(_options.CpuWorkerCount);
            _admissionDomain = new WorldAdmissionDomain(DefaultAdmissionDomainBytes);
            _platformOwner = PlatformHostOwner.Create(_options);
            _platform = _platformOwner.Platform;
            _ = _startupCompletion.TrySetResult();

            while (!_ownerRequests.IsCompleted)
            {
                if (_ownerRequests.TryTake(out var request, OwnerPumpIntervalMilliseconds))
                {
                    request.Execute(this);
                }

                var capturedInput = _platformOwner.Pump();

                if (!capturedInput.Events.IsEmpty || (capturedInput.DiscardedSampleCount != 0))
                {
                    throw new InvalidOperationException("The selected non-interactive platform unexpectedly produced physical input.");
                }
            }
        }
        catch (Exception exception)
        {
            failure = exception;
            _ = _startupCompletion.TrySetException(exception);
            FailPendingRequests(exception);
        }
        finally
        {
            try
            {
                ShutdownOwnerServices();
            }
            catch (Exception cleanupException)
            {
                failure = (failure is null)
                    ? cleanupException
                    : new AggregateException(failure, cleanupException);
            }

            if (failure is null)
            {
                _ = _completion.TrySetResult();
            }
            else
            {
                _ = _completion.TrySetException(failure);
            }
        }
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Host-owned services are independent; shutdown continues after one retirement failure and aggregates all failures.")]
    private void ShutdownOwnerServices()
    {
        var worldFailures = new List<Exception>();

        for (var index = _worlds.Count - 1; index >= 0; index--)
        {
            try
            {
                _worlds[index].Dispose();
            }
            catch (Exception exception)
            {
                worldFailures.Add(exception);
            }
        }

        _worlds.Clear();

        if (worldFailures.Count != 0)
        {
            throw new AggregateException("One or more hosted Worlds failed to retire; shared owner services remain retained.", worldFailures);
        }

        var failures = new List<Exception>();

        if (_platformOwner is not null)
        {
            try
            {
                _platformOwner.DisposeFromOwner();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        if (_workerPool is not null)
        {
            try
            {
                _workerPool.Dispose();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        try
        {
            _admissionDomain?.Dispose();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            _diagnostics?.Close();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        if (failures.Count != 0)
        {
            throw new AggregateException("EngineHost owner-service retirement failed.", failures);
        }
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "A failed Thread.Start must complete startup and owner-loop tasks so startup rollback cannot wait forever.")]
    private void StartOwnerThread()
    {
        try
        {
            _ownerThread.Start();
        }
        catch (Exception exception)
        {
            _ = _startupCompletion.TrySetException(exception);
            _ = _completion.TrySetException(exception);
            throw;
        }
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Shutdown must continue through independent service owners and report every retirement failure.")]
    private async Task StopAndDisposeAsync()
    {
        var failures = new List<Exception>();

        try
        {
            await _completion.Task.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            await _persistence.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        if (_graphicsDevice is not null)
        {
            try
            {
                await _graphicsDevice.ShutdownAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        try
        {
            await _assets.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            _ownerRequests.Dispose();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        if (failures.Count != 0)
        {
            throw new AggregateException("EngineHost shutdown failed.", failures);
        }
    }

    private bool TryEnqueue(HostActionRequest request)
    {
        lock (_ownerRequestGate)
        {
            ObjectDisposedException.ThrowIf(_stopping, this);
            return _ownerRequests.TryAdd(request);
        }
    }

    private sealed class HostActionRequest(Action<EngineContext> action, CancellationToken cancellationToken)
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task Completion => _completion.Task;

        [SuppressMessage(
            "Usage",
            "CA1031:Do not catch general exception types",
            Justification = "A user callback failure is delivered through that request's task and must not terminate the host owner loop.")]
        internal void Execute(EngineHost host)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _ = _completion.TrySetCanceled(cancellationToken);
                return;
            }

            var context = new EngineContext(host);

            try
            {
                action(context);
                _ = _completion.TrySetResult();
            }
            catch (Exception exception)
            {
                _ = _completion.TrySetException(exception);
            }
            finally
            {
                context.Invalidate();
            }
        }

        internal void Fail(Exception exception)
            => _completion.TrySetException(exception);
    }
}
