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
using TerraFX.Numerics;
using TerraFX.Platform;
using TerraFX.Rendering;

namespace TerraFX.Hosting;

/// <summary>Owns CPU, asset, persistence, platform, and optional graphics services.</summary>
public sealed partial class EngineHost : IAsyncDisposable
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
    private WorldDiagnosticsServiceOwner? _diagnosticsServiceOwner;
    private GpuResidencyService? _gpuResidency;
    private GraphicsDevice? _graphicsDevice;
    private HostEventSequenceLease? _hostEventSequenceLease;
    private uint _hostId;
    private readonly HashSet<InputRouter> _inputRouters = [];
    private readonly Dictionary<InputRouter, WorldInputTimelineAdapter> _inputTimelineByRouter = [];
    private readonly EngineHostOptions _options;
    private readonly Lock _ownerRequestGate = new();
    private readonly BlockingCollection<HostActionRequest> _ownerRequests = [with(MaximumOwnerRequestCount)];
    private readonly Thread _ownerThread;
    private int _ownerThreadId;
    private readonly WorldPersistence _persistence;
    private PlatformHost? _platform;
    private PlatformInputRoutingOwner? _platformInputRouting;
    private PlatformHostOwner? _platformOwner;
    private Renderer? _renderer;
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
        graphicsDevice?.SetDeviceLostCallback(HandleGraphicsDeviceLost);
        _gpuResidency = (graphicsDevice is null) ? null : new GpuResidencyService(graphicsDevice);
        _persistence = new WorldPersistence(assets, options.PersistenceMetadata, this);
        _ownerThread = new Thread(RunOwnerLoop) {
            IsBackground = true,
            Name = "TerraFX.EngineHost",
        };

        if (OperatingSystem.IsWindows()
            && (options.Profile == HostProfile.Interactive)
            && (options.Platform == PlatformBackend.WindowsNative))
        {
            _ownerThread.SetApartmentState(ApartmentState.STA);
        }
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

    /// <summary>Gets the host-owned GPU texture residency service, or null for Headless.</summary>
    public GpuResidencyService? GpuResidency => _gpuResidency;

    /// <summary>Gets the selected native GraphicsDevice, or null for Headless.</summary>
    public GraphicsDevice? GraphicsDevice => _graphicsDevice;

    /// <summary>Gets the host-owned Persistence service.</summary>
    public WorldPersistence Persistence => _persistence;

    /// <summary>Gets the host-owned platform service after startup.</summary>
    /// <exception cref="InvalidOperationException">Host startup has not completed.</exception>
    public PlatformHost Platform
        => _platform ?? throw new InvalidOperationException("EngineHost startup has not completed.");

    /// <summary>Gets the CPU render-snapshot extraction service.</summary>
    public Renderer Renderer
        => _renderer ?? throw new InvalidOperationException("EngineHost startup has not completed.");

    /// <summary>Starts a host with its explicitly selected services.</summary>
    /// <param name="options">The immutable host profile and service selections.</param>
    /// <param name="cancellationToken">The token that cancels startup before the owner thread is admitted.</param>
    /// <returns>A running host whose owner thread and selected services are initialized.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="options"/> contains invalid scalar settings.</exception>
    /// <exception cref="NotSupportedException">The selected platform or graphics backend cannot satisfy the requested host profile.</exception>
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
                    options.GraphicsFrameSlotCount,
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

        var request = new HostCallbackActionRequest(action, cancellationToken);

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
        var request = new HostCallbackActionRequest(action, cancellationToken);

        if (!TryEnqueue(request))
        {
            completion = null;
            return false;
        }

        completion = request.Completion;
        return true;
    }

    internal void AdoptWorldReplacement(WorldReplacementAdoptionPlan adoption)
    {
        EnsureOwnerThread();
        adoption.Apply(this);
    }

    internal InputBindingReceipt BindGameplayInputFromContext(WindowHandle window, World world)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(world);
        ThrowIfStopping();

        var worldIsHosted = _worlds.Any(schedule => ReferenceEquals(schedule.World, world));
        var routingOwner = _platformInputRouting
            ?? throw new InvalidOperationException("The EngineHost platform input routing owner is not initialized.");
        return routingOwner.BindGameplayInput(window, world, worldIsHosted);
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

    internal InputRouter CreateInputRouterFromContext(InputRecipientKind recipientKind, WindowHandle? window)
    {
        EnsureOwnerThread();
        ThrowIfStopping();
        var routingOwner = _platformInputRouting
            ?? throw new InvalidOperationException("The EngineHost platform input routing owner is not initialized.");
        return routingOwner.CreateInputRouter(recipientKind, window);
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
        var diagnosticsServiceOwner = _diagnosticsServiceOwner
            ?? throw new InvalidOperationException("The EngineHost diagnostic service owner is not initialized.");
        var admissionDomain = _admissionDomain
            ?? throw new InvalidOperationException("The EngineHost admission domain is not initialized.");
        using var packageLease = _assets.AcquirePackageLease();

        if (!ReferenceEquals(options.Registration.Schemas, packageLease.Registration.Schemas))
        {
            throw new ArgumentException("The World registration must use the exact compiled schema registry of the selected package.", nameof(options));
        }

        var world = World.CreateHosted(options, workerPool, diagnosticsServiceOwner, admissionDomain);

        try
        {
            var sequenceLease = _hostEventSequenceLease
                ?? throw new InvalidOperationException("The EngineHost event-sequence lease is not initialized.");

            lock (_ownerRequestGate)
            {
                ObjectDisposedException.ThrowIf(_stopping, this);
                _worlds.Add(new HostedWorldSchedule(world, sequenceLease));
            }

            return world;
        }
        catch (Exception)
        {
            world.Dispose();
            throw;
        }
    }

    internal ImmutableArray<InputActionTransition> DisposeInputRouterFromContext(InputRouter router)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(router);
        ThrowIfStopping();

        var routingOwner = _platformInputRouting
            ?? throw new InvalidOperationException("The EngineHost platform input routing owner is not initialized.");
        return routingOwner.DisposeInputRouter(router);
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

    internal Task<TResult> InvokeOwnerRequestAsync<TResult>(
        Func<EngineContext, TResult> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<TResult>(cancellationToken);
        }

        var request = new HostResultActionRequest<TResult>(action, cancellationToken);

        if (!TryEnqueue(request))
        {
            request.Fail(new InvalidOperationException($"The EngineHost owner mailbox is full (capacity {MaximumOwnerRequestCount})."));
        }

        return request.Result;
    }

    private void CompletePhysicalInputReconciliationAtBoundary()
    {
        var hostTimestamp = _platformOwner?.GetHostTimestamp()
            ?? throw new InvalidOperationException("The EngineHost platform owner is not initialized.");

        foreach (var adapter in _inputTimelineByRouter.Values)
        {
            if (!adapter.IsAwaitingPhysicalInputReconciliation
                || !adapter.CompletePhysicalInputReconciliationAtBoundary(hostTimestamp))
            {
                continue;
            }

            var schedule = _worlds.First(candidate => ReferenceEquals(candidate.World, adapter.World));
            schedule.CompleteInputReconciliationWait(hostTimestamp);
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

    private void HandleGraphicsDeviceLost(int? nativeResult)
    {
        var graphicsDevice = Volatile.Read(ref _graphicsDevice);

        if (graphicsDevice is not null)
        {
            HandleGraphicsDeviceLost(graphicsDevice, nativeResult);
        }
    }

    private bool HandleSimulationOverrun(
        HostedWorldSchedule schedule,
        long debtTicks,
        long hostTimestamp)
    {
        if (!schedule.IsOverrun(debtTicks))
        {
            return false;
        }

        var inputAdapter = _inputTimelineByRouter.Values
            .FirstOrDefault(adapter => ReferenceEquals(adapter.World, schedule.World));

        if (_options.OverrunPolicy is SimulationOverrunPolicy.Pause)
        {
            schedule.PauseAtBoundary(hostTimestamp);
            _ = (inputAdapter?.PauseHeadlessInput(hostTimestamp, out _, out _));
            RecordSimulationDebt(schedule, debtTicks, dropped: false);
        }
        else
        {
            _ = (inputAdapter?.PauseHeadlessInput(hostTimestamp, out _, out _));
            schedule.DropDebtAtBoundary(hostTimestamp);

            if (inputAdapter is not null)
            {
                var inputRoutingOwner = _platformInputRouting
                    ?? throw new InvalidOperationException("The EngineHost platform input routing owner is not initialized.");

                if (inputRoutingOwner.BeginPhysicalInputReconciliationAtBoundary(inputAdapter, hostTimestamp))
                {
                    schedule.BeginInputReconciliationWait(hostTimestamp);
                    inputRoutingOwner.ServiceSimulationInputRoutes(inputAdapter);
                    CompletePhysicalInputReconciliationAtBoundary();
                }
                else
                {
                    _ = inputAdapter.ReconcileHeadlessInputAtBoundary([], hostTimestamp, out _, out _);
                }
            }

            RecordSimulationDebt(schedule, debtTicks, dropped: true);
        }

        return true;
    }

    private Task<WindowCommandReceipt> InvokeWindowCommandAsync(
        WindowCommandKind operation,
        WindowHandle window,
        Func<WindowCommandReceipt> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<WindowCommandReceipt>(cancellationToken);
        }

        var request = new HostWindowCommandRequest(action, cancellationToken);

        if (!TryEnqueue(request))
        {
            request.CompleteQueueFull(new WindowCommandReceipt(
                operation,
                window,
                WindowCommandStatus.QueueFull,
                ownerSequence: 0,
                diagnostic: null));
        }

        return request.Result;
    }

    private Task<WindowCommandReceipt> InvokeWindowModeCommandAsync(
        WindowHandle window,
        WindowMode mode,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<WindowCommandReceipt>(cancellationToken);
        }

        var request = new HostWindowModeCommandRequest(
            complete => (_platformOwner
                ?? throw new InvalidOperationException("The EngineHost platform owner is not initialized."))
                .SetModeCommand(window, mode, complete),
            cancellationToken);

        if (!TryEnqueue(request))
        {
            request.CompleteQueueFull(new WindowCommandReceipt(
                WindowCommandKind.SetMode,
                window,
                WindowCommandStatus.QueueFull,
                ownerSequence: 0,
                diagnostic: null));
        }

        return request.Result;
    }

    private void RecordSimulationDebt(HostedWorldSchedule schedule, long debtTicks, bool dropped)
    {
        var world = schedule.World;
        var platformOwner = _platformOwner
            ?? throw new InvalidOperationException("The EngineHost platform owner is not initialized.");
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
        (_diagnosticsServiceOwner
            ?? throw new InvalidOperationException("The EngineHost diagnostic service owner is not initialized."))
            .WriteUnattributed(diagnostic, executionSessionId: null, worldOwnerId: null);
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
            var diagnosticsConfiguration = WorldDiagnosticsServiceOwner.GetConfiguration(
                DiagnosticsService.MaximumServiceByteCapacity,
                maximumPendingAttributionReservations: 1);
            var diagnostics = new DiagnosticsService(
                diagnosticsConfiguration.MaximumRecords,
                diagnosticsConfiguration.DiagnosticsMaximumBytes);
            _diagnostics = diagnostics;
            var diagnosticsServiceOwner = new WorldDiagnosticsServiceOwner(
                diagnostics,
                diagnosticsConfiguration.MaximumRecords,
                maximumPendingAttributionReservations: 1,
                ownsService: false);
            _diagnosticsServiceOwner = diagnosticsServiceOwner;
            _workerPool = new WorldWorkerPool(_options.CpuWorkerCount);
            _admissionDomain = new WorldAdmissionDomain(DefaultAdmissionDomainBytes);
            var sequenceLease = new HostEventSequenceLease();
            _hostEventSequenceLease = sequenceLease;
            var platformOwner = PlatformHostOwner.Create(
                _options,
                sequenceLease,
                (device, recipientId, playerSlot, cancellationToken) =>
                    InvokeOwnerRequestAsync(
                        _ => (_platformInputRouting
                            ?? throw new InvalidOperationException("The EngineHost platform input routing owner is not initialized."))
                            .AssignDevice(device, recipientId, playerSlot),
                        cancellationToken),
                (window, logicalClientSize, cancellationToken) =>
                    InvokeWindowCommandAsync(
                        WindowCommandKind.SetClientSize,
                        window,
                        () => (_platformOwner
                            ?? throw new InvalidOperationException("The EngineHost platform owner is not initialized."))
                            .SetClientSize(window, logicalClientSize),
                        cancellationToken),
                (window, title, cancellationToken) =>
                    InvokeWindowCommandAsync(
                        WindowCommandKind.SetTitle,
                        window,
                        () => (_platformOwner
                            ?? throw new InvalidOperationException("The EngineHost platform owner is not initialized."))
                            .SetTitle(window, title),
                        cancellationToken),
                (window, cancellationToken) =>
                    InvokeWindowCommandAsync(
                        WindowCommandKind.Close,
                        window,
                        () => CloseWindowOnOwner(window),
                        cancellationToken),
                (window, mode, cancellationToken) =>
                    InvokeWindowModeCommandAsync(window, mode, cancellationToken));
            _platformOwner = platformOwner;
            _hostId = platformOwner.OwnerId;
            _platform = platformOwner.Platform;
            _platformInputRouting = new PlatformInputRoutingOwner(
                _options.Profile,
                new PlatformInputRoutingHostAdapter(platformOwner),
                sequenceLease,
                diagnosticsServiceOwner,
                _ownerThreadId,
                _inputRouters,
                _inputTimelineByRouter,
                recipientId => _inputTimelineByRouter.Keys
                    .FirstOrDefault(router => router.RecipientId == recipientId));

            if (_options.Profile is HostProfile.Interactive)
            {
                var registration = _options.GraphicsBackendRegistration
                    ?? throw new InvalidOperationException("Interactive startup has no admitted graphics registration.");
                GraphicsNativeSurfaceBinding? presentationProbeBinding = null;
                WindowHandle? presentationProbeWindow = null;

                if (registration.Backend == GraphicsBackend.Vulkan)
                {
                    presentationProbeWindow = platformOwner.CreateWindow(new WindowOptions("Vulkan surface capability probe") {
                        ClientSize = new Vector2(64.0f, 64.0f),
                        Visible = false,
                    });
                    presentationProbeBinding = platformOwner.GetGraphicsNativeSurfaceBinding(
                        presentationProbeWindow.Value,
                        surfaceEpoch: 1);
                }

                var graphicsDevice = GraphicsDevice.CreateAsync(
                    platformOwner.OwnerId,
                    registration,
                    GraphicsDeviceProfile.Interactive,
                    _options.GraphicsFrameSlotCount,
                    platformOwner.GraphicsPlatformKind,
                    presentationProbeBinding,
                    CancellationToken.None).GetAwaiter().GetResult();
                _graphicsDevice = graphicsDevice;
                graphicsDevice.SetDeviceLostCallback(HandleGraphicsDeviceLost);
                _gpuResidency = new GpuResidencyService(graphicsDevice);

                if (presentationProbeWindow is WindowHandle probeWindow)
                {
                    platformOwner.CloseWindow(probeWindow);
                }
            }

            _renderer = Renderer.Create(
                _graphicsDevice,
                _gpuResidency,
                diagnosticsServiceOwner,
                _hostId);

            foreach (var diagnostic in platformOwner.DrainStartupDiagnostics())
            {
                diagnosticsServiceOwner.WriteUnattributed(diagnostic, executionSessionId: null, worldOwnerId: null);
            }

            if (_platform.GamepadStatus is PlatformServiceStatus.Unavailable)
            {
                var gamepadDiagnostic = new Diagnostic(
                    "platform-gamepad-unavailable",
                    DiagnosticSeverity.Warning,
                    System.Diagnostics.Stopwatch.GetTimestamp(),
                    hostId: null,
                    worldGuid: null,
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
                    operation: "PlatformHost.GameInput",
                    nativeResult: null,
                    message: System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported
                        ? "The documented GameInput PC runtime is unavailable; gamepad capture is disabled and no XInput fallback is used."
                        : "The GameInputSharp COM adapter requires runtime-generated COM stubs, which this process does not support; gamepad capture is disabled and no XInput fallback is used.",
                    context: []);
                diagnosticsServiceOwner.WriteUnattributed(gamepadDiagnostic, executionSessionId: null, worldOwnerId: null);
            }

            _ = _startupCompletion.TrySetResult();

            while (!_ownerRequests.IsCompleted)
            {
                if (_ownerRequests.TryTake(out var request, OwnerPumpIntervalMilliseconds))
                {
                    request.Execute(this);
                }

                var inputRoutingOwner = _platformInputRouting
                    ?? throw new InvalidOperationException("The EngineHost platform input routing owner is not initialized.");
                inputRoutingOwner.SynchronizeTextInputSessions();
                var capturedInput = _platformOwner.Pump();
                RefreshWindowSurfaceMetricsOnOwner();
                var modeDiagnostics = _platformOwner.DrainModeDiagnostics();

                if (!modeDiagnostics.IsEmpty)
                {
                    foreach (var diagnostic in modeDiagnostics)
                    {
                        diagnosticsServiceOwner.WriteUnattributed(diagnostic, executionSessionId: null, worldOwnerId: null);
                    }
                }

                if (!capturedInput.Diagnostics.IsEmpty)
                {
                    foreach (var diagnostic in capturedInput.Diagnostics)
                    {
                        diagnosticsServiceOwner.WriteUnattributed(diagnostic, executionSessionId: null, worldOwnerId: null);
                    }
                }

                inputRoutingOwner.RouteCapturedInput(capturedInput);
                var hostTimestamp = _platformOwner.GetHostTimestamp();
                inputRoutingOwner.ServiceWindowInputRoutes(hostTimestamp);

                foreach (var schedule in _worlds)
                {
                    schedule.World.PumpControlBoundary();
                }

                foreach (var schedule in _worlds)
                {
                    _ = schedule.BeginPumpBoundary(hostTimestamp);
                }

                foreach (var adapter in _inputTimelineByRouter.Values)
                {
                    var schedule = _worlds.First(candidate => ReferenceEquals(candidate.World, adapter.World));

                    if (adapter.IsAwaitingPhysicalInputReconciliation)
                    {
                        inputRoutingOwner.ContinuePhysicalInputReconciliationAtBoundary(adapter, hostTimestamp);
                    }
                    else if (schedule.DidResumeAtLastBoundary
                        && inputRoutingOwner.BeginPhysicalInputReconciliationAtBoundary(adapter, hostTimestamp))
                    {
                        schedule.BeginInputReconciliationWait(hostTimestamp);
                    }
                    else if (schedule.DidResumeAtLastBoundary)
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
                }

                inputRoutingOwner.ServiceSimulationInputRoutes();
                CompletePhysicalInputReconciliationAtBoundary();

                if (_ownerRequests.IsAddingCompleted)
                {
                    continue;
                }

                var scheduledWorldCount = _worlds.Count;

                for (var scheduleIndex = 0; scheduleIndex < scheduledWorldCount; scheduleIndex++)
                {
                    if (scheduleIndex >= _worlds.Count)
                    {
                        break;
                    }

                    var schedule = _worlds[scheduleIndex];
                    var world = schedule.World;
                    var debtTicks = schedule.GetTickDebt(hostTimestamp);

                    if (schedule.IsPaused)
                    {
                        continue;
                    }

                    if (HandleSimulationOverrun(schedule, debtTicks, hostTimestamp))
                    {
                        continue;
                    }

                    var tickLimit = checked((int)Math.Min(debtTicks, MaximumSimulationTicksPerPump));
                    var tickOffset = 0;

                    while (tickOffset < tickLimit)
                    {
                        var nextTick = world.NextTickIndex;
                        var inputAdapter = _inputTimelineByRouter.Values
                            .FirstOrDefault(adapter => ReferenceEquals(adapter.World, world));

                        if (inputAdapter is not null)
                        {
                            _ = inputAdapter.PublishInputStage(nextTick);
                        }

                        _ = world.ExecuteTick();
                        tickOffset++;

                        if ((tickOffset >= tickLimit)
                            || !ServiceCatchUpBoundary(schedule, out var catchUpHostTimestamp))
                        {
                            break;
                        }

                        var remainingDebtTicks = schedule.GetTickDebt(catchUpHostTimestamp);
                        var remainingTickLimit = checked((int)Math.Min(
                            remainingDebtTicks,
                            MaximumSimulationTicksPerPump - tickOffset));
                        tickLimit = checked(tickOffset + remainingTickLimit);
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

    private bool ServiceCatchUpBoundary(HostedWorldSchedule schedule, out long hostTimestamp)
    {
        var processedRequests = 0;

        while ((processedRequests < MaximumOwnerRequestCount)
            && _ownerRequests.TryTake(out var request))
        {
            request.Execute(this);
            processedRequests++;
        }

        hostTimestamp = _platformOwner?.GetHostTimestamp()
            ?? throw new InvalidOperationException("The EngineHost platform owner is not initialized.");
        var inputRoutingOwner = _platformInputRouting
            ?? throw new InvalidOperationException("The EngineHost platform input routing owner is not initialized.");
        inputRoutingOwner.ServiceWindowInputRoutes(hostTimestamp);

        if (_ownerRequests.IsAddingCompleted || (_ownerRequests.Count != 0))
        {
            return false;
        }

        schedule.World.PumpControlBoundary();
        _ = schedule.BeginPumpBoundary(hostTimestamp);
        var inputAdapter = _inputTimelineByRouter.Values
            .FirstOrDefault(adapter => ReferenceEquals(adapter.World, schedule.World));

        if (inputAdapter is not null)
        {
            if (inputAdapter.IsAwaitingPhysicalInputReconciliation)
            {
                inputRoutingOwner.ContinuePhysicalInputReconciliationAtBoundary(inputAdapter, hostTimestamp);
            }
            else if (schedule.DidResumeAtLastBoundary
                && inputRoutingOwner.BeginPhysicalInputReconciliationAtBoundary(inputAdapter, hostTimestamp))
            {
                schedule.BeginInputReconciliationWait(hostTimestamp);
            }
            else if (schedule.DidResumeAtLastBoundary)
            {
                _ = inputAdapter.ReconcileHeadlessInputAtBoundary(
                    [],
                    hostTimestamp,
                    out _,
                    out _);
            }
            else
            {
                _ = inputAdapter.BeginPumpBoundary(hostTimestamp, schedule.IsPaused);
            }

            inputRoutingOwner.ServiceSimulationInputRoutes(inputAdapter);
            CompletePhysicalInputReconciliationAtBoundary();
        }

        var debtTicks = schedule.GetTickDebt(hostTimestamp);
        return !HandleSimulationOverrun(schedule, debtTicks, hostTimestamp) && !schedule.IsPaused;
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Host-owned services are independent; shutdown continues after one retirement failure and aggregates all failures.")]
    private void ShutdownOwnerServices()
    {
        var inputFailures = new List<Exception>();
        var rendererDrainFailed = false;

        if (_renderer is Renderer renderer)
        {
            try
            {
                WaitForGraphicsTask(renderer.StopAndDrainSubmissionsAsync());
            }
            catch (Exception exception)
            {
                inputFailures.Add(exception);
                rendererDrainFailed = true;
            }
        }

        try
        {
            _platformInputRouting?.ClearRoutes();
        }
        catch (Exception exception)
        {
            inputFailures.Add(exception);
        }

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
        _platformInputRouting = null;
        var worldFailures = new List<Exception>();

        for (var index = _worlds.Count - 1; index >= 0; index--)
        {
            var world = _worlds[index].World;

            try
            {
                world.PumpControlBoundary();
            }
            catch (Exception exception)
            {
                worldFailures.Add(exception);
            }

            try
            {
                world.Dispose();
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
        var surfaceRetirementFailed = rendererDrainFailed;
        var graphicsRetirementFailed = false;

        if (!rendererDrainFailed)
        {
            try
            {
                RetireSurfacesBeforePlatformShutdown();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
                surfaceRetirementFailed = true;
            }
        }

        if (!surfaceRetirementFailed && (_graphicsDevice is GraphicsDevice graphicsDevice))
        {
            try
            {
                ShutdownGraphicsDeviceOnOwner(graphicsDevice);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
                graphicsRetirementFailed = true;
            }
        }

        if (_graphicsRecoveryBlocked && !graphicsRetirementFailed)
        {
            failures.Add(new InvalidOperationException(
                "The Platform owner remains retained because graphics recovery could not prove replacement-device retirement."));
            graphicsRetirementFailed = true;
        }

        if ((_platformOwner is not null)
            && !surfaceRetirementFailed
            && !graphicsRetirementFailed
            && !_graphicsRecoveryBlocked)
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

        if (!rendererDrainFailed)
        {
            try
            {
                if (_diagnosticsServiceOwner is WorldDiagnosticsServiceOwner diagnosticsServiceOwner)
                {
                    diagnosticsServiceOwner.CloseService();
                }
                else
                {
                    _diagnostics?.Close();
                }
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
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

        if (_gpuResidency is not null)
        {
            try
            {
                await _gpuResidency.StopAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

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

        if ((_graphicsDevice is GraphicsDevice graphicsDevice)
            && !_graphicsShutdownAttempts.Contains(graphicsDevice)
            && (_platformOwner is null))
        {
            try
            {
                _ = _graphicsShutdownAttempts.Add(graphicsDevice);
                await graphicsDevice.ShutdownAsync().ConfigureAwait(false);
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

    internal sealed class WorldReplacementAdoptionPlan
    {
        private readonly EngineHost _host;
        private readonly long _hostTimestamp;
        private readonly WorldInputTimelineAdapter.WorldReplacementAdoption[] _inputAdoptions;
        private bool _isApplied;
        private readonly World _previousWorld;
        private readonly World _replacementWorld;
        private readonly int _scheduleIndex;
        private readonly SimulationClockSegment _scheduleSegment;
        private readonly long _timeScaleSegmentSequenceAtPause;

        internal WorldReplacementAdoptionPlan(
            EngineHost host,
            int scheduleIndex,
            World previousWorld,
            World replacementWorld,
            long hostTimestamp,
            long timeScaleSegmentSequenceAtPause,
            SimulationClockSegment scheduleSegment,
            WorldInputTimelineAdapter.WorldReplacementAdoption[] inputAdoptions)
        {
            ArgumentNullException.ThrowIfNull(host);
            ArgumentOutOfRangeException.ThrowIfNegative(scheduleIndex);
            ArgumentNullException.ThrowIfNull(previousWorld);
            ArgumentNullException.ThrowIfNull(replacementWorld);
            ArgumentNullException.ThrowIfNull(inputAdoptions);
            _host = host;
            _scheduleIndex = scheduleIndex;
            _previousWorld = previousWorld;
            _replacementWorld = replacementWorld;
            _hostTimestamp = hostTimestamp;
            _timeScaleSegmentSequenceAtPause = timeScaleSegmentSequenceAtPause;
            _scheduleSegment = scheduleSegment;
            _inputAdoptions = inputAdoptions;
        }

        internal void Apply(EngineHost host)
        {
            ArgumentNullException.ThrowIfNull(host);

            if (!ReferenceEquals(host, _host)
                || _isApplied
                || !_scheduleSegment.IsValid
                || (_scheduleSegment.HostTimestampStartTicks != _hostTimestamp)
                || ((uint)_scheduleIndex >= (uint)host._worlds.Count)
                || !ReferenceEquals(host._worlds[_scheduleIndex].World, _previousWorld))
            {
                throw new InvalidOperationException("The prepared World replacement adoption no longer matches its exact host schedule.");
            }

            _isApplied = true;
            host._worlds[_scheduleIndex].ApplyWorldReplacementAdoption(
                _replacementWorld,
                _timeScaleSegmentSequenceAtPause,
                _scheduleSegment);

            for (var index = 0; index < _inputAdoptions.Length; index++)
            {
                _inputAdoptions[index].Apply();
            }
        }
    }

    private abstract class HostActionRequest
    {
        internal abstract Task Completion { get; }

        internal abstract void Execute(EngineHost host);

        internal abstract void Fail(Exception exception);
    }

    private sealed class HostCallbackActionRequest(
        Action<EngineContext> action,
        CancellationToken cancellationToken) : HostActionRequest
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal override Task Completion => _completion.Task;

        [SuppressMessage(
            "Usage",
            "CA1031:Do not catch general exception types",
            Justification = "A user callback failure is delivered through that request's task and must not terminate the host owner loop.")]
        internal override void Execute(EngineHost host)
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

        internal override void Fail(Exception exception)
            => _completion.TrySetException(exception);
    }

    private sealed class HostResultActionRequest<TResult>(
        Func<EngineContext, TResult> action,
        CancellationToken cancellationToken) : HostActionRequest
    {
        private readonly TaskCompletionSource<TResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal override Task Completion => _completion.Task;

        internal Task<TResult> Result => _completion.Task;

        [SuppressMessage(
            "Usage",
            "CA1031:Do not catch general exception types",
            Justification = "A host command failure is delivered through that request's typed task and must not terminate the owner loop.")]
        internal override void Execute(EngineHost host)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _ = _completion.TrySetCanceled(cancellationToken);
                return;
            }

            var context = new EngineContext(host);

            try
            {
                _ = _completion.TrySetResult(action(context));
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

        internal override void Fail(Exception exception)
            => _ = _completion.TrySetException(exception);
    }

    private sealed class HostWindowCommandRequest(
        Func<WindowCommandReceipt> action,
        CancellationToken cancellationToken) : HostActionRequest
    {
        private readonly TaskCompletionSource<WindowCommandReceipt> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal override Task Completion => _completion.Task;

        internal Task<WindowCommandReceipt> Result => _completion.Task;

        internal void CompleteQueueFull(WindowCommandReceipt receipt)
            => _ = _completion.TrySetResult(receipt);

        [SuppressMessage(
            "Usage",
            "CA1031:Do not catch general exception types",
            Justification = "A native window command failure is delivered through that request's task and must not terminate the host owner loop.")]
        internal override void Execute(EngineHost host)
        {
            _ = host;

            if (cancellationToken.IsCancellationRequested)
            {
                _ = _completion.TrySetCanceled(cancellationToken);
                return;
            }

            try
            {
                _ = _completion.TrySetResult(action());
            }
            catch (Exception exception)
            {
                _ = _completion.TrySetException(exception);
            }
        }

        internal override void Fail(Exception exception)
            => _ = _completion.TrySetException(exception);
    }

    private sealed class HostWindowModeCommandRequest(
        Action<Action<WindowCommandReceipt>> action,
        CancellationToken cancellationToken) : HostActionRequest
    {
        private readonly TaskCompletionSource<WindowCommandReceipt> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal override Task Completion => _completion.Task;

        internal Task<WindowCommandReceipt> Result => _completion.Task;

        internal void CompleteQueueFull(WindowCommandReceipt receipt)
            => _ = _completion.TrySetResult(receipt);

        [SuppressMessage(
            "Usage",
            "CA1031:Do not catch general exception types",
            Justification = "A native window mode command failure is delivered through its task while asynchronous confirmation remains owned by PlatformHostOwner.")]
        internal override void Execute(EngineHost host)
        {
            _ = host;

            if (cancellationToken.IsCancellationRequested)
            {
                _ = _completion.TrySetCanceled(cancellationToken);
                return;
            }

            try
            {
                action(receipt => _ = _completion.TrySetResult(receipt));
            }
            catch (Exception exception)
            {
                _ = _completion.TrySetException(exception);
            }
        }

        internal override void Fail(Exception exception)
            => _ = _completion.TrySetException(exception);
    }

    private sealed class HostedWorldSchedule
    {
        private const uint TimeSpanTicksPerSecond = 10_000_000;

        private bool _isAwaitingInputReconciliation;
        private bool _isPaused;
        private bool _isRecoveringOverrun;
        private SimulationClockSegment _segment;
        private readonly HostEventSequenceLease _sequenceLease;
        private long _timeScaleSegmentSequenceAtPause;
        private World _world;

        internal HostedWorldSchedule(World world, HostEventSequenceLease sequenceLease)
        {
            ArgumentNullException.ThrowIfNull(world);
            ArgumentNullException.ThrowIfNull(sequenceLease);
            _world = world;
            _sequenceLease = sequenceLease;
        }

        internal bool DidResumeAtLastBoundary { get; private set; }

        internal bool IsPaused => _isPaused || _isAwaitingInputReconciliation;

        internal World World => _world;

        internal void ApplyWorldReplacementAdoption(
            World replacementWorld,
            long timeScaleSegmentSequenceAtPause,
            SimulationClockSegment segment)
        {
            _world = replacementWorld;
            _isRecoveringOverrun = false;
            DidResumeAtLastBoundary = false;
            _timeScaleSegmentSequenceAtPause = timeScaleSegmentSequenceAtPause;
            _segment = segment;
        }

        internal void BeginInputReconciliationWait(long hostTimestamp)
        {
            if (_isAwaitingInputReconciliation)
            {
                throw new InvalidOperationException("Physical input reconciliation is already pending for this World.");
            }

            _isAwaitingInputReconciliation = true;
            _isRecoveringOverrun = false;
            BeginSegment(hostTimestamp, isPaused: true);
        }

        internal bool BeginPumpBoundary(long hostTimestamp)
        {
            var wasPaused = _isPaused;

            if (_isPaused && (_world.TimeScaleSegmentSequence != _timeScaleSegmentSequenceAtPause))
            {
                _isPaused = false;
                _isRecoveringOverrun = true;
            }

            DidResumeAtLastBoundary = wasPaused && !_isPaused;
            BeginSegment(hostTimestamp, _isPaused || _isAwaitingInputReconciliation);
            return DidResumeAtLastBoundary;
        }

        internal void CompleteInputReconciliationWait(long hostTimestamp)
        {
            if (!_isAwaitingInputReconciliation)
            {
                throw new InvalidOperationException("Physical input reconciliation is not pending for this World.");
            }

            _isAwaitingInputReconciliation = false;
            BeginSegment(hostTimestamp, isPaused: _isPaused);
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
            if (_isPaused || _isAwaitingInputReconciliation || !_segment.IsValid || (_segment.TimeScale == 0.0f))
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

        internal SimulationClockSegment PrepareWorldReplacementAdoption(
            World replacementWorld,
            long hostTimestamp)
        {
            var nextOpenTick = replacementWorld.NextTickIndex;
            return new SimulationClockSegment(
                _segment.IsValid ? checked(_segment.Revision + 1) : 1,
                hostTimestamp,
                _sequenceLease.Allocate(),
                simulationElapsedStartTicks: 0,
                fractionalSimulationTicks: 0,
                replacementWorld.TickRate,
                nextOpenTick,
                replacementWorld.CurrentTimeScale,
                _isPaused || _isAwaitingInputReconciliation);
        }

        internal void ValidateAdoption(World previousWorld, World replacementWorld, long hostTimestamp)
        {
            ArgumentNullException.ThrowIfNull(previousWorld);
            ArgumentNullException.ThrowIfNull(replacementWorld);
            ArgumentOutOfRangeException.ThrowIfNegative(hostTimestamp);

            if (!ReferenceEquals(_world, previousWorld)
                || ReferenceEquals(previousWorld, replacementWorld)
                || (_segment.IsValid && (hostTimestamp < _segment.HostTimestampStartTicks)))
            {
                throw new InvalidOperationException("The host schedule cannot adopt the selected replacement World.");
            }

            _ = replacementWorld.NextTickIndex;
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
