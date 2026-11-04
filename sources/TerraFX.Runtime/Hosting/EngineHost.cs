// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Graphics;
using TerraFX.Input;
using TerraFX.Integration;
using TerraFX.Platform;
using TerraFX.Rendering;

namespace TerraFX.Hosting;

/// <summary>Owns CPU, asset, persistence, platform, and optional offscreen graphics services.</summary>
public sealed class EngineHost : IAsyncDisposable
{
    private const long DefaultAdmissionDomainBytes = 8_589_934_592L;
    private const int MaximumInputRecipients = 64;
    private const int MaximumOwnerRequestCount = 512;
    private const int MaximumSimulationTicksPerPump = 8;
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
    private HostEventSequenceLease? _hostEventSequenceLease;
    private readonly HashSet<InputRouter> _inputRouters = [];
    private readonly Dictionary<InputRouter, WorldInputTimelineAdapter> _inputTimelineByRouter = [];
    private readonly EngineHostOptions _options;
    private readonly Lock _ownerRequestGate = new();
    private readonly BlockingCollection<HostActionRequest> _ownerRequests = [with(MaximumOwnerRequestCount)];
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
    private readonly List<HostedWorldSchedule> _worlds = [];

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

    private static Diagnostic CreateInputBindingDiagnostic(string code, string message, Guid? worldGuid)
        => new(
            code,
            DiagnosticSeverity.Error,
            System.Diagnostics.Stopwatch.GetTimestamp(),
            hostId: null,
            worldGuid: worldGuid,
            deviceId: null,
            surfaceId: null,
            deviceEpoch: null,
            surfaceEpoch: null,
            swapchainEpoch: null,
            tick: null,
            systemId: null,
            passId: null,
            assetId: null,
            commandIdentity: null,
            operation: "BindGameplayInput",
            nativeResult: null,
            message: message,
            context: []);

    private static InputBindingReceipt CreateRejectedInputBindingReceipt(World world, string code, string message)
        => new(
            InputBindingStatus.Rejected,
            router: null,
            window: null,
            worldGuid: world.WorldGuid,
            rejection: CreateInputBindingDiagnostic(code, message, world.WorldGuid));

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

    internal InputBindingReceipt BindGameplayInputFromContext(WindowHandle window, World world)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(world);
        ThrowIfStopping();

        var diagnostic = CreateInputBindingDiagnostic(
            "InteractiveInputUnavailable",
            "Interactive window input routing is unavailable until the native UI route is implemented.",
            world.WorldGuid);
        return new InputBindingReceipt(InputBindingStatus.Rejected, null, window, world.WorldGuid, diagnostic);
    }

    internal InputBindingReceipt BindHeadlessGameplayInputFromContext(InputRouter router, World world)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(world);
        ThrowIfStopping();

        if (_options.Profile != HostProfile.Headless)
        {
            return CreateRejectedInputBindingReceipt(
                world,
                "HeadlessGameplayInputRequiresHeadlessHost",
                "Headless gameplay input can be bound only by a Headless host.");
        }

        if (!_inputRouters.Contains(router) || (router.RecipientKind != InputRecipientKind.SimulationTimeline))
        {
            return CreateRejectedInputBindingReceipt(
                world,
                "InputRouterNotOwnedByHost",
                "The router is not a same-host SimulationTimeline recipient.");
        }

        if (!_worlds.Any(schedule => ReferenceEquals(schedule.World, world)))
        {
            return CreateRejectedInputBindingReceipt(
                world,
                "WorldNotOwnedByHost",
                "The World is not owned by this EngineHost.");
        }

        if (_inputTimelineByRouter.ContainsKey(router)
            || _inputTimelineByRouter.Values.Any(adapter => ReferenceEquals(adapter.World, world)))
        {
            return CreateRejectedInputBindingReceipt(
                world,
                "InputRouteAlreadyBound",
                "A router or World can be bound to only one simulation timeline.");
        }

        var sequenceLease = _hostEventSequenceLease
            ?? throw new InvalidOperationException("The EngineHost event-sequence lease is not initialized.");
        var platformOwner = _platformOwner
            ?? throw new InvalidOperationException("The EngineHost platform owner is not initialized.");
        var adapter = new WorldInputTimelineAdapter(world, router, sequenceLease);
        _ = adapter.BeginPumpBoundary(platformOwner.GetHostTimestamp(), isPaused: false);
        _inputTimelineByRouter.Add(router, adapter);
        return new InputBindingReceipt(
            InputBindingStatus.Bound,
            router,
            window: null,
            worldGuid: world.WorldGuid,
            rejection: null);
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "A router whose host registration fails is disposed before the allocation failure is rethrown.")]
    internal InputRouter CreateInputRouterFromContext(InputRecipientKind recipientKind, WindowHandle? window)
    {
        EnsureOwnerThread();
        ThrowIfStopping();

        if (!Enum.IsDefined(recipientKind))
        {
            throw new ArgumentOutOfRangeException(nameof(recipientKind));
        }

        if (recipientKind is InputRecipientKind.HostLiveUI)
        {
            if (window is null)
            {
                throw new ArgumentException("A HostLiveUI recipient requires its exact host window.", nameof(window));
            }

            throw new NotSupportedException("Interactive HostLiveUI recipients require the unfinished native window and UI fanout path.");
        }

        if (window.HasValue)
        {
            throw new ArgumentException("A SimulationTimeline recipient is not constructed with a physical window.", nameof(window));
        }

        var sequenceLease = _hostEventSequenceLease
            ?? throw new InvalidOperationException("The EngineHost event-sequence lease is not initialized.");
        var router = new InputRouter(
            InputRecipientKind.SimulationTimeline,
            OwnerIdAllocator.Allocate(),
            _ownerThreadId,
            sequenceLease);

        try
        {
            _ = _inputRouters.Add(router);
            return router;
        }
        catch (Exception)
        {
            _ = router.DisposeAndGetTransitions();
            throw;
        }
    }

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "A scheduled World transfers to the EngineHost's ordered owner list on success and is disposed if schedule admission fails.")]
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
            var sequenceLease = _hostEventSequenceLease
                ?? throw new InvalidOperationException("The EngineHost event-sequence lease is not initialized.");
            _worlds.Add(new HostedWorldSchedule(world, sequenceLease));
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

    internal ImmutableArray<InputRoutingReceipt> InjectHeadlessInputFromContext(
        ImmutableArray<InputRouter> recipients,
        InputInjection input)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(input);
        ThrowIfStopping();

        if (_options.Profile != HostProfile.Headless)
        {
            throw new InvalidOperationException("Headless input injection requires a Headless host.");
        }

        if (recipients.IsDefaultOrEmpty)
        {
            throw new ArgumentException("At least one bound SimulationTimeline recipient is required.", nameof(recipients));
        }

        if (recipients.Length > MaximumInputRecipients)
        {
            throw new ArgumentOutOfRangeException(
                nameof(recipients),
                $"A Headless input injection supports at most {MaximumInputRecipients} recipients.");
        }

        var uniqueRecipients = new HashSet<InputRouter>();

        foreach (var router in recipients)
        {
            ArgumentNullException.ThrowIfNull(router);

            if (!uniqueRecipients.Add(router))
            {
                throw new ArgumentException("A Headless input recipient may appear only once.", nameof(recipients));
            }

            if (!_inputTimelineByRouter.ContainsKey(router))
            {
                throw new ArgumentException("Every recipient must be a bound SimulationTimeline owned by this host.", nameof(recipients));
            }
        }

        var platformOwner = _platformOwner
            ?? throw new InvalidOperationException("The EngineHost platform owner is not initialized.");
        var sequenceLease = _hostEventSequenceLease
            ?? throw new InvalidOperationException("The EngineHost event-sequence lease is not initialized.");
        var hostTimestamp = platformOwner.GetHostTimestamp();
        var hostSequence = sequenceLease.Allocate();
        var receipts = ImmutableArray.CreateBuilder<InputRoutingReceipt>(recipients.Length);

        foreach (var router in recipients)
        {
            receipts.Add(_inputTimelineByRouter[router].EnqueueCapturedInput(input, hostTimestamp, hostSequence));
        }

        return receipts.MoveToImmutable();
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

    private void RecordSimulationDebt(HostedWorldSchedule schedule, long debtTicks, bool dropped)
    {
        var world = schedule.World;
        var platformOwner = _platformOwner
            ?? throw new InvalidOperationException("The EngineHost platform owner is not initialized.");
        var diagnostics = _diagnostics
            ?? throw new InvalidOperationException("The EngineHost diagnostics service is not initialized.");
        var debtSeconds = debtTicks / (double)world.TickRate;
        var diagnostic = new Diagnostic(
            dropped ? "SimulationDebtDropped" : "SimulationOverrun",
            DiagnosticSeverity.Warning,
            System.Diagnostics.Stopwatch.GetTimestamp(),
            hostId: platformOwner.OwnerId,
            worldGuid: world.WorldGuid,
            deviceId: null,
            surfaceId: null,
            deviceEpoch: null,
            surfaceEpoch: null,
            swapchainEpoch: null,
            tick: world.NextTickIndex,
            systemId: null,
            passId: null,
            assetId: null,
            commandIdentity: null,
            operation: "EngineHost.Scheduler",
            nativeResult: null,
            message: dropped
                ? $"The host discarded {debtTicks} tick(s) of simulation debt ({debtSeconds.ToString("R", CultureInfo.InvariantCulture)} seconds)."
                : $"The host paused simulation with {debtTicks} tick(s) of debt ({debtSeconds.ToString("R", CultureInfo.InvariantCulture)} seconds).",
            context: [new DiagnosticContextEntry("DebtTicks", debtTicks.ToString(CultureInfo.InvariantCulture))]);
        diagnostics.Write(diagnostic, executionSessionId: null, worldOwnerId: null);
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
            _hostEventSequenceLease = new HostEventSequenceLease();
            _platformOwner = PlatformHostOwner.Create(_options, _hostEventSequenceLease);
            _platform = _platformOwner.Platform;
            _ = _startupCompletion.TrySetResult();

            while (!_ownerRequests.IsCompleted)
            {
                if (_ownerRequests.TryTake(out var request, OwnerPumpIntervalMilliseconds))
                {
                    request.Execute(this);
                }

                var capturedInput = _platformOwner.Pump();
                var hostTimestamp = _platformOwner.GetHostTimestamp();

                foreach (var schedule in _worlds)
                {
                    _ = schedule.BeginPumpBoundary(hostTimestamp);
                }

                foreach (var adapter in _inputTimelineByRouter.Values)
                {
                    var schedule = _worlds.First(candidate => ReferenceEquals(candidate.World, adapter.World));

                    if (schedule.DidResumeAtLastBoundary)
                    {
                        _ = adapter.ReconcileHeadlessInputAtBoundary(
                            [],
                            hostTimestamp,
                            out _,
                            out _);
                    }
                    else
                    {
                        _ = adapter.BeginPumpBoundary(hostTimestamp, schedule.IsPaused);
                    }

                    _ = adapter.ServiceHostControlBoundary();
                }

                if (!capturedInput.Events.IsEmpty || (capturedInput.DiscardedSampleCount != 0))
                {
                    throw new InvalidOperationException("The selected non-interactive platform unexpectedly produced physical input.");
                }

                foreach (var schedule in _worlds)
                {
                    var world = schedule.World;
                    world.PumpControlBoundary();
                    var debtTicks = schedule.GetTickDebt(hostTimestamp);

                    if (schedule.IsPaused)
                    {
                        continue;
                    }

                    if (schedule.IsOverrun(debtTicks))
                    {
                        var inputAdapter = _inputTimelineByRouter.Values
                            .FirstOrDefault(adapter => ReferenceEquals(adapter.World, world));

                        if (_options.OverrunPolicy is SimulationOverrunPolicy.Pause)
                        {
                            schedule.PauseAtBoundary(hostTimestamp);
                            _ = (inputAdapter?.PauseHeadlessInput(hostTimestamp, out _, out _));
                            RecordSimulationDebt(schedule, debtTicks, dropped: false);
                            continue;
                        }

                        _ = (inputAdapter?.PauseHeadlessInput(hostTimestamp, out _, out _));
                        schedule.DropDebtAtBoundary(hostTimestamp);
                        _ = (inputAdapter?.ReconcileHeadlessInputAtBoundary([], hostTimestamp, out _, out _));
                        RecordSimulationDebt(schedule, debtTicks, dropped: true);
                        continue;
                    }

                    var tickCount = checked((int)Math.Min(debtTicks, MaximumSimulationTicksPerPump));

                    for (var tickOffset = 0; tickOffset < tickCount; tickOffset++)
                    {
                        var nextTick = world.NextTickIndex;
                        var inputAdapter = _inputTimelineByRouter.Values
                            .FirstOrDefault(adapter => ReferenceEquals(adapter.World, world));

                        if (inputAdapter is not null)
                        {
                            _ = inputAdapter.PublishInputStage(nextTick);
                        }

                        _ = world.ExecuteTick();
                    }
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
        var inputFailures = new List<Exception>();

        foreach (var router in _inputRouters)
        {
            try
            {
                _ = router.DisposeAndGetTransitions();
            }
            catch (Exception exception)
            {
                inputFailures.Add(exception);
            }
        }

        _inputRouters.Clear();
        _inputTimelineByRouter.Clear();
        var worldFailures = new List<Exception>();

        for (var index = _worlds.Count - 1; index >= 0; index--)
        {
            try
            {
                _worlds[index].World.Dispose();
            }
            catch (Exception exception)
            {
                worldFailures.Add(exception);
            }
        }

        _worlds.Clear();

        if (worldFailures.Count != 0)
        {
            worldFailures.InsertRange(0, inputFailures);
            throw new AggregateException("One or more hosted Worlds failed to retire; shared owner services remain retained.", worldFailures);
        }

        var failures = inputFailures;

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

        _hostEventSequenceLease?.Revoke();

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

    private void ThrowIfStopping()
    {
        lock (_ownerRequestGate)
        {
            ObjectDisposedException.ThrowIf(_stopping, this);
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

    private sealed class HostedWorldSchedule
    {
        private const uint TimeSpanTicksPerSecond = 10_000_000;

        private bool _isPaused;
        private bool _isRecoveringOverrun;
        private SimulationClockSegment _segment;
        private readonly HostEventSequenceLease _sequenceLease;
        private long _timeScaleSegmentSequenceAtPause;
        private readonly World _world;

        internal HostedWorldSchedule(World world, HostEventSequenceLease sequenceLease)
        {
            ArgumentNullException.ThrowIfNull(world);
            ArgumentNullException.ThrowIfNull(sequenceLease);
            _world = world;
            _sequenceLease = sequenceLease;
        }

        internal bool DidResumeAtLastBoundary { get; private set; }

        internal bool IsPaused => _isPaused;

        internal World World => _world;

        internal bool BeginPumpBoundary(long hostTimestamp)
        {
            var wasPaused = _isPaused;

            if (_isPaused && (_world.TimeScaleSegmentSequence != _timeScaleSegmentSequenceAtPause))
            {
                _isPaused = false;
                _isRecoveringOverrun = true;
            }

            DidResumeAtLastBoundary = wasPaused && !_isPaused;
            BeginSegment(hostTimestamp, _isPaused);
            return DidResumeAtLastBoundary;
        }

        internal void DropDebtAtBoundary(long hostTimestamp)
        {
            var nextOpenTick = _world.NextTickIndex;
            var scaledTimeSpanTicks = (UInt128)(ulong)nextOpenTick * TimeSpanTicksPerSecond;
            var wholeElapsedTicks = scaledTimeSpanTicks / (uint)_world.TickRate;

            if (wholeElapsedTicks > long.MaxValue)
            {
                throw new OverflowException("The dropped-debt clock position exceeds the signed TimeSpan range.");
            }

            var remainder = scaledTimeSpanTicks % (uint)_world.TickRate;
            _isPaused = false;
            _isRecoveringOverrun = false;
            DidResumeAtLastBoundary = false;
            _segment = new SimulationClockSegment(
                _segment.IsValid ? checked(_segment.Revision + 1) : 1,
                hostTimestamp,
                _sequenceLease.Allocate(),
                (long)wholeElapsedTicks,
                (double)remainder / _world.TickRate,
                _world.TickRate,
                nextOpenTick,
                _world.CurrentTimeScale,
                isPaused: false);
        }

        internal long GetTickDebt(long hostTimestamp)
        {
            if (_isPaused || !_segment.IsValid || (_segment.TimeScale == 0.0f))
            {
                return 0;
            }

            if (!_segment.TryGetTickIndexForHostEvent(
                hostTimestamp,
                _segment.HostSequenceStart,
                out var latestTick,
                out _))
            {
                return 0;
            }

            var nextOpenTick = _world.NextTickIndex;
            return (latestTick > nextOpenTick) ? (latestTick - nextOpenTick) : 0;
        }

        internal bool IsOverrun(long debtTicks)
        {
            var exceedsThreshold = (debtTicks > 0) && (((UInt128)(ulong)debtTicks * 4) > (uint)_world.TickRate);

            if (_isRecoveringOverrun)
            {
                _isRecoveringOverrun = exceedsThreshold;
                return false;
            }

            return exceedsThreshold;
        }

        internal void PauseAtBoundary(long hostTimestamp)
        {
            _isPaused = true;
            _isRecoveringOverrun = false;
            _timeScaleSegmentSequenceAtPause = _world.TimeScaleSegmentSequence;
            BeginSegment(hostTimestamp, isPaused: true);
        }

        private void BeginSegment(long hostTimestamp, bool isPaused)
        {
            var hostSequenceStart = _sequenceLease.Allocate();
            var elapsedTicks = 0L;
            var fractionalTicks = 0.0;

            if (_segment.IsValid)
            {
                if (_segment.IsPaused || (_segment.TimeScale == 0.0f))
                {
                    elapsedTicks = _segment.SimulationElapsedStartTicks;
                    fractionalTicks = _segment.FractionalSimulationTicks;
                }
                else if (!_segment.TryGetSimulationTimeAtHostEvent(
                    hostTimestamp,
                    hostSequenceStart,
                    out elapsedTicks,
                    out fractionalTicks))
                {
                    throw new InvalidOperationException("The active host clock segment could not be advanced to this pump boundary.");
                }
            }

            _segment = new SimulationClockSegment(
                _segment.IsValid ? checked(_segment.Revision + 1) : 1,
                hostTimestamp,
                hostSequenceStart,
                elapsedTicks,
                fractionalTicks,
                _world.TickRate,
                _world.NextTickIndex,
                _world.CurrentTimeScale,
                isPaused);
        }
    }
}
