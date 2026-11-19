// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using TerraFX.Numerics;

namespace TerraFX.Entities;

/// <summary>Owns one admitted standalone World identity, registration, diagnostics service, and spatial frame.</summary>
public sealed partial class World : IDisposable
{
    private const long DefaultAdmissionDomainBytes = 8_589_934_592L;
    private const long RestoreContextControlBytes = 128;
    private static readonly long FreshWorldControlBytes = checked(4096L + (4L * IntPtr.Size));

    private long _acceptedOwnerRequests;
    private long _activeAttachmentMembershipSourceToken;
    private HashSet<Entity>? _activeCommandExcludedEntityHandles;
    private WorldCommandStaging? _activeCommandMembershipRoot;
    private HashSet<Entity>? _activeCommandProjectedEntityHandles;
    private long _activePersistenceAdmissionBytes;
    private readonly long[] _activeQueryEpochs;
    private readonly int[] _activeQueryWriteGrants;
    private int _activeSystemInvocationCount;
    private readonly WorldSystemInvocationDispatch?[] _activeSystemInvocations;
    private readonly WorldAdmissionDomain _admissionDomain;
    private int _archetypeCount;
    private WorldArchetype?[] _archetypes;
    private Exception? _attachmentCallbackStickyFailure;
    private long _attachmentMembershipSourceTokenSequence;
    private readonly WorldAttachmentTable _attachments = new();
    private long _canonicalMutationRevision;
    private long _committedMutationRevision;
    private long _committedOwnerRequests;
    private CommittedWorldPublication? _committedPublication;
    private long _completedOwnerRequests;
    private WorldChunkWorkPlanEntry[] _componentTagWorkPlan = [];
    private long _componentTagWorkPlanByteReservation;
    private int _componentTagWorkPlanCount;
    private long _componentTagWorkPlanEpoch;
    private int _controlRecordCount;
    private WorldControlRecord[] _controlRecords = [];
    private long _controlStorageByteReservation;
    private WorldFrameCohort _currentFrameCohort;
    private int _currentIdentityBranchIndex;
    private WorldDiagnosticFundingControl? _diagnosticFundingControl;
    private readonly WorldDiagnosticsCapability _diagnosticsCapability;
    private WorldDiagnosticsServiceOwner? _diagnosticsServiceOwner;
    private int _disposed;
    private readonly WorldDomainReservation _domainReservation;
    private readonly EntitySlotTable _entitySlots;
    private readonly Guid _executionSessionId;
    private int _faulted;
    private int _identityBranchCount;
    private Guid _identityBranchId;
    private WorldIdentityBranchRecord[] _identityBranches = [];
    private readonly StableIdentityLedger _identityLedger;
    private SpatialFrame2? _initialFrame2;
    private SpatialFrame3 _initialFrame3;
    private readonly float _initialTimeScale;
    private int _isAttachmentCallbackActive;
    private bool _isCandidateInitialized;
    private readonly bool _isRestoreCandidate;
    private bool _isRestoreClockStateRestored;
    private bool _isRestoreEcsRowsRestored;
    private bool _isRestoreHierarchyInstalled;
    private int _isStructuralOperationActive;
    private int _isSystemCleanupActive;
    private int _isTerminalCloseInvocation;
    private int _isTickActive;
    private Diagnostic? _lastFault;
    private WorldSystemCleanupReport? _lastSystemCleanupReport;
    private readonly WorldLifetimeControl _lifetimeControl;
    private readonly WorldLineageGrant _lineageGrant;
    private readonly int _maximumArchetypes;
    private readonly long _maximumCommandStagingBytes;
    private readonly int _maximumDeferredCommands;
    private readonly int _maximumIdentityBranches;
    private readonly int _maximumLiveEntities;
    private readonly long _maximumOwnedStateBytes;
    private readonly int _maximumPendingOwnerRequests;
    private readonly long _maximumPersistenceAdmissionBytes;
    private readonly int _maximumRecordedControls;
    private readonly long _maximumSnapshotBytes;
    private readonly long _maximumWorkPlanBytes;
    private ulong _nextAttachmentInstallationId;
    private long _nextChunkOrdinal;
    private long _nextControlSequence = 1;
    private long _nextOwnerRequestSequence = 1;
    private long _nextQueryEpoch;
    private long _nextSystemInvocationEpoch;
    private long _nextTickIndex;
    private readonly WorldOptions _options;
    private long _ownerCommandPreparationByteReservation;
    private readonly Lock _ownerControlGate = new();
    private readonly long _ownerControlQueueByteReservation;
    private int _ownerControlRequestCount;
    private int _ownerControlRequestHead;
    private readonly WorldOwnerControlRequest?[] _ownerControlRequests;
    private readonly uint _ownerId;
    private readonly int _ownerThreadId;
    private readonly bool _ownsWorkerPool;
    private readonly Lock _persistenceAdmissionGate = new();
    private long _publicationSequence;
    private WorldArchetype?[] _queryArchetypes;
    private readonly WorldGuidRegistry _registry;
    private readonly Guid _replayLineageId;
    private ImmutableArray<object> _resourceStorage;
    private readonly long _resourceStorageByteReservation;
    private readonly int _restoreAttachmentCount;
    private long _restoreComponentWorkspaceByteReservation;
    private readonly WorldComponentRestoreContext? _restoreContext;
    private readonly long _restoreEcsArchetypeMetadataByteReservation;
    private WorldRestoreEcsPlan? _restoreEcsPlan;
    private readonly long _restoreEcsStorageByteReservation;
    private WorldRestoreSourceControl? _restoreSourceControl;
    private long _restoreValidationWorkspaceByteReservation;
    private readonly long _restoreValidationWorkspaceCapacity;
    private readonly long _restoreWorkPlanByteReservation;
    private readonly int _restoreWorkPlanCapacity;
    private long _sceneControlPayloadByteReservation;
    private IWorldSceneParticipant? _sceneParticipant;
    private readonly Lock _sceneStorageGate = new();
    private readonly long _sceneSupportByteReservation;
    private readonly int[] _scheduledSystemRegistrationIndices;
    private readonly SpatialProfileId _spatialProfileId;
    private readonly SpatialSettings _spatialSettings;
    private readonly WorldSystemCleanupPreparation _systemCleanupPreparation;
    private Exception? _systemCleanupStickyFailure;
    private readonly WorldSystemInstanceClaim _systemInstanceClaim;
    private readonly long _systemInstanceStorageByteReservation;
    private IWorldSystem?[] _systemInstances;
    private readonly long _systemInvocationStorageByteReservation;
    private readonly WorldSystemQuarantineReservation _systemQuarantineReservation;
    private readonly int _tickRate;
    private float _timeScale;
    private long _timeScaleSegmentSequence = 1;
    private readonly WorldWorkerPool _workerPool;
    private readonly Guid _worldGuid;

    private World(WorldConstructionPlan constructionPlan)
    {
        ArgumentNullException.ThrowIfNull(constructionPlan);
        constructionPlan.ValidateForConstruction();
        constructionPlan.AdmissionDomain.InitializeSceneConstructionBytes(
            constructionPlan.DomainReservation,
            constructionPlan.RestoreSceneHierarchyWorkspaceByteReservation);
        var systemQuarantineReservation = constructionPlan.SystemQuarantineReservation;
        var workerPool = constructionPlan.WorkerPool;

        var options = constructionPlan.Options;
        var resolvedOptions = constructionPlan.ResolvedOptions;
        var resources = options.Registration.Resources;
        var resourceStorage = ImmutableArray.CreateBuilder<object>(resources.Length);
        var resourceStorageBytes = 0L;
        var systems = options.Registration.Systems;

        foreach (var resource in resources)
        {
            resourceStorageBytes = checked(resourceStorageBytes + resource.GetStorageByteReservation());
            resourceStorage.Add(resource.CreateStorage());
        }

        _admissionDomain = constructionPlan.AdmissionDomain;
        _options = options;
        _resourceStorage = resourceStorage.MoveToImmutable();
        _resourceStorageByteReservation = resourceStorageBytes;
        _systemInstanceClaim = new WorldSystemInstanceClaim();
        _systemInstanceStorageByteReservation = options.Registration.SystemInstanceStorageByteReservation;
        _systemInstances = new IWorldSystem?[systems.Length];
        var invocationSlotCount = checked(resolvedOptions.CpuWorkerCount + 1);
        _activeSystemInvocations = new WorldSystemInvocationDispatch?[invocationSlotCount];
        _activeQueryEpochs = new long[invocationSlotCount];
        _activeQueryWriteGrants = new int[invocationSlotCount];
        _systemInvocationStorageByteReservation = CalculateSystemInvocationStorageByteReservation(resolvedOptions.CpuWorkerCount);
        _subscriptionStorageByteReservation = CalculateSubscriptionStorageByteReservation();
        _subscriptions = new WorldSubscriptionEntry[MaximumSubscriptions];
        _scheduledSystemRegistrationIndices = new int[systems.Length];

        for (var executionIndex = 0; executionIndex < _scheduledSystemRegistrationIndices.Length; executionIndex++)
        {
            var scheduledDescription = options.Registration.Schedule.GetSystemAtExecutionPlanPosition(executionIndex);
            var registrationIndex = -1;

            for (var systemIndex = 0; systemIndex < systems.Length; systemIndex++)
            {
                if (systems[systemIndex].Description.SystemId == scheduledDescription.SystemId)
                {
                    registrationIndex = systemIndex;
                    break;
                }
            }

            if (registrationIndex < 0)
            {
                throw new InvalidOperationException("The compiled schedule references a missing system registration.");
            }

            _scheduledSystemRegistrationIndices[executionIndex] = registrationIndex;
        }

        _archetypes = new WorldArchetype?[options.MaximumArchetypes];
        _queryArchetypes = new WorldArchetype?[options.MaximumArchetypes];
        _domainReservation = constructionPlan.DomainReservation;
        var restoreIdentityState = constructionPlan.RestoreIdentityState;
        _isRestoreCandidate = restoreIdentityState is not null;
        _restoreEcsStorageByteReservation = constructionPlan.RestoreEcsStorageByteReservation;
        _restoreEcsArchetypeMetadataByteReservation = constructionPlan.RestoreEcsArchetypeMetadataByteReservation;
        _restoreEcsPlan = constructionPlan.RestoreEcsPlan;
        _restoreSourceControl = constructionPlan.PreparedReplacement?.PreparedSourceControl;
        _restoreAttachmentCount = constructionPlan.RestoreAttachmentCount;
        _restoreWorkPlanByteReservation = constructionPlan.RestoreWorkPlanByteReservation;
        _restoreWorkPlanCapacity = constructionPlan.RestoreWorkPlanCapacity;
        _restoreComponentWorkspaceByteReservation = constructionPlan.RestoreComponentWorkspaceByteReservation;
        _controlRecords = (constructionPlan.RestoreControlRecordCount == 0)
            ? []
            : new WorldControlRecord[constructionPlan.RestoreControlRecordCount];
        _controlStorageByteReservation = constructionPlan.RestoreControlStorageByteReservation;
        _sceneControlPayloadByteReservation = constructionPlan.RestoreSceneControlPayloadByteReservation;
        _restoreValidationWorkspaceByteReservation = constructionPlan.RestoreValidationWorkspaceByteReservation;
        _restoreValidationWorkspaceCapacity = _restoreValidationWorkspaceByteReservation;

        if (constructionPlan.RestoreAttachmentCount != 0)
        {
            if (constructionPlan.RestoreAttachmentStorageByteReservation
                != WorldAttachmentTable.GetStorageByteReservationForCount(constructionPlan.RestoreAttachmentCount))
            {
                throw new InvalidOperationException("The restore attachment table reservation does not match its saved count.");
            }

            var preparedAttachments = _attachments.PrepareCapacityForCount(constructionPlan.RestoreAttachmentCount);
            _attachments.CommitCapacity(
                preparedAttachments,
                constructionPlan.RestoreAttachmentStorageByteReservation);
        }

        _entitySlots = restoreIdentityState?.EntitySlots ?? new EntitySlotTable(options.MaximumEntitySlots);
        _executionSessionId = constructionPlan.ExecutionSessionId;
        _identityBranchId = constructionPlan.IdentityBranchId;
        _identityLedger = constructionPlan.IdentityLedger;
        _identityBranches = restoreIdentityState?.Branches
            ?? [
                new WorldIdentityBranchRecord(
                    constructionPlan.IdentityBranchId,
                    constructionPlan.WorldGuid,
                    constructionPlan.ReplayLineageId,
                    parentBranchId: null,
                    createdAtNextTick: 0,
                    firstAssignmentOrdinal: 1,
                    directoryIndex: 0),
            ];
        _identityBranchCount = _identityBranches.Length;
        _currentIdentityBranchIndex = restoreIdentityState?.ActiveBranchIndex ?? 0;
        _nextTickIndex = restoreIdentityState?.NextTick ?? 0;
        _lineageGrant = constructionPlan.LineageGrant;
        _maximumArchetypes = options.MaximumArchetypes;
        _maximumIdentityBranches = options.MaximumIdentityBranches;
        _maximumCommandStagingBytes = options.SchedulingLimits.MaximumCommandStagingBytes;
        _maximumDeferredCommands = options.MaximumDeferredCommands;
        _maximumLiveEntities = options.MaximumLiveEntities;
        _maximumPendingOwnerRequests = options.MaximumPendingOwnerRequests;
        _maximumOwnedStateBytes = options.SchedulingLimits.MaximumOwnedStateBytes;
        _maximumPersistenceAdmissionBytes = options.MaximumPersistenceAdmissionBytes;
        _ownerControlQueueByteReservation = ArrayBytes(options.MaximumPendingOwnerRequests, IntPtr.Size);
        _ownerControlRequests = new WorldOwnerControlRequest?[options.MaximumPendingOwnerRequests];
        _maximumWorkPlanBytes = options.SchedulingLimits.MaximumWorkPlanBytes;
        _componentTagWorkPlanByteReservation = 0;
        _maximumRecordedControls = options.MaximumRecordedControls;
        _maximumSnapshotBytes = options.MaximumSnapshotBytes;
        _ownsWorkerPool = constructionPlan.OwnsWorkerPool;
        _registry = constructionPlan.Registry;
        _replayLineageId = constructionPlan.ReplayLineageId;
        _spatialProfileId = options.SpatialProfileId;
        _spatialSettings = options.SpatialSettings;
        _initialTimeScale = options.InitialTimeScale;
        _tickRate = options.TickRate;
        _timeScale = options.InitialTimeScale;
        _lifetimeControl = constructionPlan.LifetimeControl;
        _worldGuid = constructionPlan.WorldGuid;
        _systemCleanupPreparation = new WorldSystemCleanupPreparation(_worldGuid, systems.Length);
        _sceneSupportByteReservation = options.Registration.SceneSupport?.GetWorldStorageByteReservation(options) ?? 0;
        _cpuExtractionSupportByteReservation =
            options.Registration.CpuExtractionSupport?.GetWorldStorageByteReservation(options) ?? 0;
        _systemQuarantineReservation = systemQuarantineReservation;
        _workerPool = workerPool;
        Registration = options.Registration;
        Profile = options.Profile;
        _diagnosticsCapability = constructionPlan.DiagnosticsCapability;

        _ownerId = OwnerIdAllocator.Allocate();
        _ownerThreadId = Environment.CurrentManagedThreadId;
        _restoreContext = _isRestoreCandidate ? new WorldComponentRestoreContext(this) : null;
        _diagnosticsCapability.BindOwner(_ownerId);
        _archetypes[0] = new WorldArchetype(_ownerId, 0, _maximumLiveEntities, [], []);
        _queryArchetypes[0] = _archetypes[0];
        _archetypeCount = 1;
        RebuildComponentTagWorkPlan();
        var currentFrame3 = new SpatialFrame3(
            _ownerId,
            _spatialProfileId,
            _spatialSettings,
            new SpatialPosition3(new ChunkIndex3(0, 0, 0), new Vector3(0, 0, 0)),
            1,
            resolvedOptions.FrameExtent3.Minimum,
            resolvedOptions.FrameExtent3.Maximum);
        SpatialFrame2? currentFrame2 = null;

        if (resolvedOptions.FrameExtent2 is { } frameExtent2)
        {
            currentFrame2 = new SpatialFrame2(
                _ownerId,
                _spatialProfileId,
                _spatialSettings,
                new SpatialPosition2(new ChunkIndex2(0, 0), new Vector2(0, 0)),
                1,
                frameExtent2.Minimum,
                frameExtent2.Maximum);
        }

        _initialFrame3 = currentFrame3;
        _initialFrame2 = currentFrame2;
        _currentFrameCohort = new WorldFrameCohort(currentFrame3, currentFrame2, cohortGeneration: 1);
        _admissionDomain.EnrollWorld(
            _domainReservation,
            _lifetimeControl,
            this,
            constructionPlan.RestoreSceneHierarchyWorkspaceByteReservation,
            _lineageGrant);
        constructionPlan.TransferRestoreResourcesToWorld();
    }

    /// <summary>Gets the current 2D spatial frame, when 2D frame admission is enabled.</summary>
    /// <exception cref="ObjectDisposedException">The World has been disposed.</exception>
    /// <exception cref="InvalidOperationException">The World is not idle on its owner thread.</exception>
    public SpatialFrame2? CurrentFrame2
    {
        get
        {
            EnsureWorldReadAccess();
            return _currentFrameCohort.Frame2;
        }
    }

    /// <summary>Gets the current 3D spatial frame.</summary>
    /// <exception cref="ObjectDisposedException">The World has been disposed.</exception>
    /// <exception cref="InvalidOperationException">The World is not idle on its owner thread.</exception>
    public SpatialFrame3 CurrentFrame3
    {
        get
        {
            EnsureWorldReadAccess();
            return _currentFrameCohort.Frame3;
        }
    }

    /// <summary>Gets the Core-owned diagnostic service while the World is live.</summary>
    /// <exception cref="ObjectDisposedException">The World has been disposed.</exception>
    public DiagnosticsService Diagnostics
    {
        get
        {
            ThrowIfDisposed();
            return _diagnosticsCapability.Diagnostics;
        }
    }

    /// <summary>Gets the active identity branch for future stable Entity assignments.</summary>
    /// <exception cref="InvalidOperationException">The World is not idle on its owner thread.</exception>
    /// <exception cref="ObjectDisposedException">The World has been disposed.</exception>
    public Guid IdentityBranchId
    {
        get
        {
            EnsureWorldReadAccess();
            return _identityBranchId;
        }
    }

    /// <summary>Gets the first diagnostic that faulted this World, or null while it remains usable.</summary>
    /// <exception cref="ObjectDisposedException">The World has been disposed.</exception>
    public Diagnostic? LastFault
    {
        get
        {
            ThrowIfDisposed();
            return _lastFault;
        }
    }

    /// <summary>Gets the sealed system cleanup report, when World system cleanup has completed.</summary>
    public WorldSystemCleanupReport? LastSystemCleanupReport => _lastSystemCleanupReport;

    /// <summary>Gets the selected simulation admission profile.</summary>
    public SimulationProfile Profile { get; }

    /// <summary>Gets the frozen component and tag registration.</summary>
    public WorldRegistration Registration { get; }

    /// <summary>Gets the initial replay lineage identity.</summary>
    public Guid ReplayLineageId => _replayLineageId;

    /// <summary>Gets the exact spatial profile identity.</summary>
    public SpatialProfileId SpatialProfileId => _spatialProfileId;

    /// <summary>Gets the exact spatial unit and chunk settings.</summary>
    public SpatialSettings SpatialSettings => _spatialSettings;

    /// <summary>Gets the fixed simulation tick rate.</summary>
    public int TickRate => _tickRate;

    /// <summary>Gets the persistent identity of this logical World.</summary>
    public Guid WorldGuid => _worldGuid;

    internal long CanonicalMutationRevision => _canonicalMutationRevision;

    internal int CpuWorkerCount => _workerPool.WorkerCount;

    internal Guid ExecutionSessionId => _executionSessionId;

    internal StableIdentityLedger IdentityLedger => _identityLedger;

    internal int MaximumDeferredCommands => _maximumDeferredCommands;

    internal long MaximumOwnedStateBytes => _maximumOwnedStateBytes;

    internal long MaximumPersistenceAdmissionBytes => _maximumPersistenceAdmissionBytes;

    internal long MaximumSnapshotBytes => _maximumSnapshotBytes;

    internal WorldOptions Options => _options;

    internal uint OwnerId => _ownerId;

    /// <summary>Creates a standalone World from a frozen registration and validated options.</summary>
    /// <param name="options">The construction options and immutable registration.</param>
    /// <returns>The admitted standalone World.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">An identity, registration, spatial profile, or extent is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A capacity, tick rate, time scale, or worker count is invalid.</exception>
    /// <exception cref="InvalidOperationException">A WorldGuid/domain/lineage admission is refused or owner issuance is exhausted.</exception>
    public static World Create(WorldOptions options)
        => AdmitWorld(options, sharedWorkerPool: null, sharedDiagnosticsOwner: null, hostAdmissionDomain: null);

    internal static World CreateRestoreCandidate(WorldConstructionPlan constructionPlan)
    {
        ArgumentNullException.ThrowIfNull(constructionPlan);
        constructionPlan.ValidateForConstruction();

        if (constructionPlan.RestoreIdentityState is null)
        {
            throw new ArgumentException("A restore candidate requires a validated target identity state.", nameof(constructionPlan));
        }

        World? candidate = null;

        try
        {
            candidate = new World(constructionPlan) {
                _diagnosticsServiceOwner = constructionPlan.TakeDiagnosticsServiceOwner(),
                _diagnosticFundingControl = constructionPlan.TakeDiagnosticFundingControl(),
            };
            (constructionPlan.ReplacementPreparation
                ?? throw new InvalidOperationException("The restore construction plan omitted its replacement preparation."))
                .TransferReplacementGrantToCandidate(candidate._lifetimeControl);
            return candidate;
        }
        catch
        {
            candidate?.Dispose();
            throw;
        }
    }

    internal static void InitializeRestoreCandidate(World candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        candidate.EnsureOwnerThread();
        candidate.ThrowIfDisposed();

        if (!candidate._isRestoreCandidate || candidate._isCandidateInitialized)
        {
            throw new InvalidOperationException("The World is not an uninitialized restore candidate.");
        }

        candidate._isCandidateInitialized = true;
        candidate.InitializeSceneSupport(candidate._options);
        candidate.InitializeCpuExtractionSupport(candidate._options);
        candidate.InitializeSystemInstances();

        for (var index = 0; index < candidate._options.Registration.Resources.Length; index++)
        {
            candidate._options.Registration.Resources[index].InitializeAndValidate(candidate._resourceStorage[index], candidate);
        }

        candidate.ConfigureOwnedStateBudget();
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The reserved identity, domain charge, and returned World transfer together only after both admission commits; the finally path releases them on failure.")]
    [SuppressMessage("Usage", "CA2219:Do not raise exceptions in finally clauses", Justification = "An unrecoverable WorldGuid retirement failure during unpublished construction unwind must surface rather than hide a retained process binding.")]
    private static World AdmitWorld(
        WorldOptions options,
        WorldWorkerPool? sharedWorkerPool,
        WorldDiagnosticsServiceOwner? sharedDiagnosticsOwner,
        WorldAdmissionDomain? hostAdmissionDomain)
    {
        ArgumentNullException.ThrowIfNull(options);
        var resolvedOptions = options.ValidateAndResolve();

        if (sharedWorkerPool is not null)
        {
            if ((options.CpuWorkerCount is int configuredWorkerCount)
                && (configuredWorkerCount != sharedWorkerPool.WorkerCount))
            {
                throw new ArgumentException("A hosted World worker count must match the host-owned pool.", nameof(options));
            }

            resolvedOptions = resolvedOptions with { CpuWorkerCount = sharedWorkerPool.WorkerCount };
        }

        if (options.Registration.CosmeticCreditStorageSelected)
        {
            throw new InvalidOperationException(WorldRegistration.CosmeticCreditStorageUnavailableMessage);
        }

        if (options.SchedulingLimits.MaximumDiagnosticBytes > options.SchedulingLimits.MaximumOwnedStateBytes)
        {
            throw new InvalidOperationException("The diagnostic source envelope exceeds the World-owned state budget.");
        }

        var ownsWorkerPool = sharedWorkerPool is null;
        var standaloneDiagnosticService = sharedDiagnosticsOwner is null;
        var worldGuid = options.WorldGuid ?? Guid.NewGuid();

        if (worldGuid == Guid.Empty)
        {
            throw new InvalidOperationException("The generated WorldGuid cannot be empty.");
        }

        WorldGuidRegistry? registry = null;
        WorldGuidGrant worldGuidGrant = default;
        WorldAdmissionDomain? admissionDomain = null;
        var ownsAdmissionDomain = false;
        WorldDefaultDomainOwner? defaultDomainOwner = null;
        WorldLifetimeControl? lifetimeControl = null;
        WorldDomainReservation? domainReservation = null;
        WorldSystemQuarantineReservation? systemQuarantineReservation = null;
        WorldWorkerPool? workerPool = null;
        WorldLineageGrant lineageGrant = default;
        DiagnosticsService? diagnostics = null;
        WorldDiagnosticsServiceOwner? diagnosticsServiceOwner = null;
        WorldDomainOperationCredit? diagnosticFundingCredit = null;
        WorldDiagnosticFundingControl? diagnosticFundingControl = null;
        var diagnosticsServiceOwnerUseAcquired = false;
        WorldDiagnosticsCapability? diagnosticsCapability = null;
        var lineageCommitted = false;
        var publicationCommitted = false;
        var constructionCleanupFailed = false;
        World? unpublishedWorld = null;

        try
        {
            registry = WorldGuidRegistry.GetSelected();

            if (!registry.TryReserveFresh(worldGuid, out worldGuidGrant, out var worldGuidRefusal))
            {
                if (worldGuidRefusal.Code == WorldGuidRefusal.GuidAlreadyRetained)
                {
                    throw new ArgumentException("The WorldGuid is already active or reserved.", nameof(options));
                }

                throw new InvalidOperationException($"WorldGuid admission was refused with code {worldGuidRefusal.Code}.");
            }

            admissionDomain = options.AdmissionDomain
                ?? hostAdmissionDomain
                ?? new WorldAdmissionDomain(DefaultAdmissionDomainBytes);
            ownsAdmissionDomain = (options.AdmissionDomain is null) && (hostAdmissionDomain is null);

            var maximumDiagnosticBytes = options.SchedulingLimits.MaximumDiagnosticBytes;
            var diagnosticFundingControlBytes = WorldDiagnosticFundingControl.OwnedByteReservation;
            var diagnosticRecordCount = 0;
            var diagnosticServiceMaximumBytes = 0L;
            var diagnosticServiceOwnerBytes = 0L;
            var diagnosticAccountMaximumBytes = 0L;

            if (standaloneDiagnosticService)
            {
                var diagnosticServiceConfiguration = WorldDiagnosticsServiceOwner.GetConfiguration(
                    maximumDiagnosticBytes
                        - WorldDiagnosticAttributionAccount.ControlByteCharge
                        - diagnosticFundingControlBytes,
                    maximumPendingAttributionReservations: 0);
                diagnosticRecordCount = diagnosticServiceConfiguration.MaximumRecords;
                diagnosticServiceMaximumBytes = diagnosticServiceConfiguration.DiagnosticsMaximumBytes;
                diagnosticServiceOwnerBytes = diagnosticServiceConfiguration.OwnerByteReservation;
            }
            else
            {
                var sharedOwner = sharedDiagnosticsOwner
                    ?? throw new InvalidOperationException("The hosted World omitted its diagnostic service owner.");
                diagnosticRecordCount = sharedOwner.MaximumRecords;
                diagnosticServiceMaximumBytes = sharedOwner.Diagnostics.MaximumByteReservation;
                diagnosticServiceOwnerBytes = sharedOwner.OwnedByteReservation;
            }

            var diagnosticCoreStorageBytes = DiagnosticsService.GetOwnedByteReservation(diagnosticRecordCount);
            diagnosticAccountMaximumBytes = maximumDiagnosticBytes - diagnosticFundingControlBytes
                - (standaloneDiagnosticService
                    ? checked(diagnosticServiceOwnerBytes + diagnosticCoreStorageBytes)
                    : 0);

            if (diagnosticAccountMaximumBytes < WorldDiagnosticAttributionAccount.ControlByteCharge)
            {
                throw new InvalidOperationException("The diagnostic source envelope cannot fit its required service and control backing.");
            }

            var archetypeDirectoryBytes = checked(2 * ArrayBytes(options.MaximumArchetypes, IntPtr.Size));
            var resourceBytes = 0L;
            var systemBytes = checked(
                options.Registration.SystemInstanceStorageByteReservation
                + CalculateSystemInvocationStorageByteReservation(resolvedOptions.CpuWorkerCount));

            foreach (var resource in options.Registration.Resources)
            {
                resourceBytes = checked(resourceBytes + resource.GetStorageByteReservation());
            }

            var resourceAndSystemStateBytes = checked(resourceBytes + systemBytes);
            var sceneSupportBytes = options.Registration.SceneSupport?.GetWorldStorageByteReservation(options) ?? 0;
            var cpuExtractionSupportBytes =
                options.Registration.CpuExtractionSupport?.GetWorldStorageByteReservation(options) ?? 0;

            if (resourceAndSystemStateBytes > options.SchedulingLimits.MaximumResourceAndSystemStateBytes)
            {
                throw new InvalidOperationException("The selected resource partitions and system instances exceed the resource and system state budget.");
            }

            var ownerControlQueueBytes = ArrayBytes(options.MaximumPendingOwnerRequests, IntPtr.Size);
            var worldOwnedBytes = checked(
                FreshWorldControlBytes
                + archetypeDirectoryBytes
                + ownerControlQueueBytes
                + resourceAndSystemStateBytes
                + sceneSupportBytes
                + cpuExtractionSupportBytes
                + CalculateSubscriptionStorageByteReservation());

            if (!admissionDomain.TryReserveWorld(worldOwnedBytes, out domainReservation)
                || (domainReservation is null))
            {
                throw new InvalidOperationException("The admission domain cannot reserve the World and its owned diagnostics service.");
            }

            if (!admissionDomain.TryReserveRetainedDiagnosticSourceCredit(maximumDiagnosticBytes, out diagnosticFundingCredit)
                || (diagnosticFundingCredit is null))
            {
                throw new InvalidOperationException("The admission domain cannot reserve the World diagnostic source envelope.");
            }

            defaultDomainOwner = ownsAdmissionDomain
                ? new WorldDefaultDomainOwner(admissionDomain, worldGuidGrant)
                : null;

            if (standaloneDiagnosticService)
            {
                diagnostics = new DiagnosticsService(diagnosticRecordCount, diagnosticServiceMaximumBytes);
                diagnosticsServiceOwner = new WorldDiagnosticsServiceOwner(
                    diagnostics,
                    diagnosticRecordCount,
                    maximumPendingAttributionReservations: 0,
                    ownsService: true);
            }
            else
            {
                diagnosticsServiceOwner = sharedDiagnosticsOwner
                    ?? throw new InvalidOperationException("The hosted World omitted its diagnostic service owner.");
                diagnostics = diagnosticsServiceOwner.Diagnostics;
            }

            var selectedDiagnosticsServiceOwner = diagnosticsServiceOwner
                ?? throw new InvalidOperationException("The World diagnostic service owner was not initialized.");
            selectedDiagnosticsServiceOwner.RetainWorldUse();
            diagnosticsServiceOwnerUseAcquired = true;
            var selectedDiagnostics = diagnostics
                ?? throw new InvalidOperationException("The World diagnostic service was not initialized.");
            var executionSessionId = ExecutionSession.ExecutionSessionId;
            var selectedDiagnosticsCapability = selectedDiagnostics.CreateBorrowedWorldCapability(
                worldGuid,
                executionSessionId,
                diagnosticAccountMaximumBytes);
            diagnosticsCapability = selectedDiagnosticsCapability;
            var selectedDiagnosticFundingControl = new WorldDiagnosticFundingControl(
                selectedDiagnosticsCapability.AttributionAccount,
                diagnosticFundingCredit
                    ?? throw new InvalidOperationException("The World diagnostic envelope credit was not initialized."));
            diagnosticFundingControl = selectedDiagnosticFundingControl;
            diagnosticFundingCredit = null;

            if (standaloneDiagnosticService)
            {
                selectedDiagnosticsServiceOwner.SetBaselineFundingControl(selectedDiagnosticFundingControl);
            }

            var replayLineageId = options.InitialReplayLineageId ?? Guid.NewGuid();

            if (replayLineageId == Guid.Empty)
            {
                throw new InvalidOperationException("The generated replay-lineage identity cannot be empty.");
            }

            var identityBranchId = FoundationNameUuid.Create(worldGuid, "world/identity-branch/root/v1");

            if (!admissionDomain.TryReserveFreshLineage(
                domainReservation,
                worldGuid,
                replayLineageId,
                identityBranchId,
                out lineageGrant,
                out var lineageRefusal,
                issuanceNamespace: worldGuid))
            {
                if (lineageRefusal == WorldAdmissionRefusal.LineageAlreadyRetained
                    && options.InitialReplayLineageId.HasValue)
                {
                    throw new ArgumentException("The replay lineage is already registered in this admission domain.", nameof(options));
                }

                throw new InvalidOperationException($"Replay-lineage admission was refused with code {lineageRefusal}.");
            }

            lifetimeControl = new WorldLifetimeControl(
                registry,
                admissionDomain,
                worldGuidGrant,
                defaultDomainOwner,
                domainReservation,
                lineageGrant,
                isReplacementCandidate: false);
            defaultDomainOwner = null;

            var identityLedger = StableIdentityLedger.CreateForWorld(
                options.MaximumIdentityAssignments,
                options.MaximumEntitySlots,
                options.MaximumActiveSceneInstances);
            systemQuarantineReservation = WorldSystemInstanceRegistry.ReserveQuarantineCapacity(options.Registration.Systems.Length);
            workerPool = sharedWorkerPool ?? new WorldWorkerPool(resolvedOptions.CpuWorkerCount);
            var constructionPlan = new WorldConstructionPlan(
                resolvedOptions,
                worldGuid,
                replayLineageId,
                identityBranchId,
                executionSessionId,
                admissionDomain,
                lifetimeControl,
                ownsWorkerPool,
                domainReservation,
                lineageGrant,
                registry,
                worldGuidGrant,
                selectedDiagnosticsCapability,
                selectedDiagnosticsServiceOwner,
                selectedDiagnosticFundingControl,
                identityLedger,
                systemQuarantineReservation,
                workerPool);
            var world = new World(constructionPlan) {
                _diagnosticsServiceOwner = constructionPlan.TakeDiagnosticsServiceOwner(),
                _diagnosticFundingControl = constructionPlan.TakeDiagnosticFundingControl(),
            };
            unpublishedWorld = world;

            try
            {
                world.InitializeSceneSupport(options);
                world.InitializeCpuExtractionSupport(options);
                world.InitializeSystemInstances();

                for (var index = 0; index < options.Registration.Resources.Length; index++)
                {
                    options.Registration.Resources[index].InitializeAndValidate(world._resourceStorage[index], world);
                }

                world.ConfigureOwnedStateBudget();

                if (!admissionDomain.TryCommitFreshLineage(lineageGrant))
                {
                    throw new InvalidOperationException("The fresh replay-lineage admission could not be committed.");
                }

                lineageCommitted = true;

                if (!registry.TryCommitFresh(worldGuidGrant))
                {
                    throw new InvalidOperationException("The reserved WorldGuid binding could not be committed.");
                }

                world._lifetimeControl.PublishFresh();
                publicationCommitted = true;
                diagnosticsServiceOwnerUseAcquired = false;
                diagnosticFundingControl = null;
                return world;
            }
            catch (Exception exception)
            {
                constructionCleanupFailed = true;
                world._cpuExtractionParticipant?.Dispose();
                world._sceneParticipant?.Dispose();
                var cleanupReport = world.ReleaseSystemInstances(
                    WorldSystemCleanupOperation.ConstructionUnwind,
                    exception);

                if (cleanupReport.FailureCount != 0)
                {
                    throw world._systemCleanupPreparation.CreateCleanupException();
                }

                constructionCleanupFailed = false;
                throw;
            }
        }
        finally
        {
            if (!publicationCommitted)
            {
                WorldAdmissionDomain? closingDomain = null;
                WorldDomainReservation? closingReservation = null;
                var unwindSucceeded = false;
                var lifetimeCapacityReturned = false;

                try
                {
                    if ((admissionDomain is { } domain)
                        && (domainReservation is { } parent)
                        && domain.IsEnrolledWorld(parent))
                    {
                        domain.BeginWorldClose(parent);
                        closingDomain = domain;
                        closingReservation = parent;

                        if (unpublishedWorld is not null)
                        {
                            _ = Interlocked.Exchange(ref unpublishedWorld._disposed, 1);
                        }

                        if ((lifetimeControl is null) || !lifetimeControl.BeginRetirement())
                        {
                            throw new InvalidOperationException("The unpublished World could not revoke its exact original binding.");
                        }
                    }

                    if (ownsWorkerPool)
                    {
                        workerPool?.Dispose();
                    }

                    if (diagnosticsCapability is not null)
                    {
                        if (diagnosticsServiceOwner is not null)
                        {
                            diagnosticsServiceOwner.CloseCapability(diagnosticsCapability);
                        }
                        else
                        {
                            diagnosticsCapability.Close();
                        }
                    }
                    else if ((diagnosticsServiceOwner is null) && (diagnostics is not null))
                    {
                        diagnostics?.Close();
                    }

                    if (unpublishedWorld is not null)
                    {
                        unpublishedWorld._diagnosticFundingControl = null;
                        unpublishedWorld._diagnosticsServiceOwner = null;
                    }

                    if (diagnosticsServiceOwnerUseAcquired)
                    {
                        diagnosticsServiceOwner?.ReleaseWorldUse();
                        diagnosticsServiceOwnerUseAcquired = false;
                    }
                    else if ((diagnosticsServiceOwner is not null)
                        && standaloneDiagnosticService
                        && (diagnostics is not null))
                    {
                        diagnosticsServiceOwner.CloseService();
                    }

                    diagnosticFundingControl?.ReleaseUse();
                    diagnosticFundingControl = null;
                    diagnosticFundingCredit?.Dispose();
                    diagnosticFundingCredit = null;

                    var retainsQuarantineDomain = systemQuarantineReservation?.RetainsDomainReservation == true;

                    if ((closingDomain is null) && lineageGrant.IsValid && (admissionDomain is not null))
                    {
                        if (lineageCommitted)
                        {
                            _ = admissionDomain.TryRollbackFreshLineage(lineageGrant);
                        }
                        else
                        {
                            _ = admissionDomain.TryReleaseFreshLineage(lineageGrant);
                        }
                    }

                    systemQuarantineReservation?.Release();

                    if ((closingDomain is not null)
                        && (closingReservation is not null)
                        && (lifetimeControl is not null)
                        && !lineageCommitted)
                    {
                        _ = closingDomain.TryReleaseUnpublishedWorldLineage(
                            closingReservation,
                            lifetimeControl,
                            lineageGrant);
                    }

                    if (lifetimeControl is not null)
                    {
                        if (constructionCleanupFailed)
                        {
                            if (!lifetimeControl.BeginRetirement())
                            {
                                throw new InvalidOperationException("The failed World construction could not revoke its WorldGuid binding.");
                            }

                            if (!lifetimeControl.QuarantineAfterCleanupFailure())
                            {
                                throw new InvalidOperationException("The failed World construction could not quarantine its WorldGuid binding.");
                            }

                            WorldSystemInstanceRegistry.QuarantineLifetimeControl(
                                lifetimeControl,
                                systemQuarantineReservation
                                    ?? throw new InvalidOperationException("The failed World construction has no lifetime quarantine reservation."));
                            lifetimeControl.CloseDefaultDomain();
                        }
                        else if (closingDomain is not null)
                        {
                            lifetimeControl.CloseDefaultDomain();
                        }
                        else
                        {
                            lifetimeControl.AbandonUnpublished();
                        }
                    }
                    else if ((registry is not null) && worldGuidGrant.IsValid)
                    {
                        _ = registry.TryReleaseFresh(worldGuidGrant);
                    }

                    defaultDomainOwner?.Close();

                    if (ownsAdmissionDomain)
                    {
                        admissionDomain?.Dispose();
                    }

                    unwindSucceeded = !constructionCleanupFailed && !retainsQuarantineDomain;
                }
                finally
                {
                    try
                    {
                        if (systemQuarantineReservation is not null)
                        {
                            WorldSystemInstanceRegistry.ReleaseLifetimeControlCapacity(systemQuarantineReservation);
                        }

                        lifetimeCapacityReturned = true;
                    }
                    finally
                    {
                        if ((closingDomain is not null) && (closingReservation is not null))
                        {
                            closingDomain.CompleteWorldClose(
                                closingReservation,
                                unwindSucceeded && lifetimeCapacityReturned);
                        }
                        else if (unwindSucceeded && lifetimeCapacityReturned)
                        {
                            domainReservation?.Dispose();
                        }
                    }
                }
            }
        }
    }

    private static long ArrayBytes(int capacity, int elementSize)
    {
        var payloadBytes = checked((long)capacity * elementSize);
        var roundedBytes = checked((payloadBytes + 7L) & ~7L);
        return checked(64L + roundedBytes);
    }

    private static long CalculateComponentTagWorkPlanByteReservation(int capacity)
    {
        if (capacity == 0)
        {
            return 0;
        }

        var payloadBytes = checked((long)capacity * Unsafe.SizeOf<WorldChunkWorkPlanEntry>());
        return checked(64L + ((payloadBytes + 15L) & ~15L));
    }

    private static long CalculateSystemInvocationStorageByteReservation(int workerCount)
    {
        var slotCount = checked(workerCount + 1);
        return checked(
            ArrayBytes(slotCount, IntPtr.Size)
            + ArrayBytes(slotCount, sizeof(long))
            + ArrayBytes(slotCount, sizeof(int)));
    }

    private static bool ContainsComponentInitialization(EntityDescription description, Type componentType)
    {
        foreach (var initialization in description.Components)
        {
            if (initialization.ComponentType == componentType)
            {
                return true;
            }
        }

        return false;
    }

    private static ComponentInitialization FindInitialization(EntityDescription description, Type componentType)
    {
        foreach (var initialization in description.Components)
        {
            if (initialization.ComponentType == componentType)
            {
                return initialization;
            }
        }

        throw new InvalidOperationException("The archetype component has no matching EntityDescription initialization.");
    }

    private static bool IsReservedUuidV8(Guid value)
    {
        Span<byte> bytes = stackalloc byte[16];
        _ = value.TryWriteBytes(bytes, bigEndian: true, out _);
        return ((bytes[6] & 0xF0) == 0x80) && ((bytes[8] & 0xC0) == 0x80);
    }

    private static void ThrowIfValidationFailed(
        ImmutableArray<Diagnostic> diagnostics,
        string? parameterName,
        string message)
    {
        if (diagnostics.IsDefault)
        {
            throw new InvalidOperationException("The component validation dispatcher returned a default diagnostic array.");
        }

        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic is null)
            {
                throw new InvalidOperationException("The component validation dispatcher returned a null diagnostic.");
            }
        }

        if (!diagnostics.IsEmpty)
        {
            if (parameterName is null)
            {
                throw new InvalidOperationException(message);
            }

            throw new ArgumentException(message, parameterName);
        }
    }

    private static void ValidateDerivedDependencies(ImmutableArray<IComponentRegistration> components)
    {
        foreach (var component in components)
        {
            foreach (var dependency in component.Descriptor.DerivedDependencies)
            {
                var registered = false;

                foreach (var candidate in components)
                {
                    if ((candidate.ComponentType == dependency.ComponentType)
                        && (candidate.SchemaReference == dependency.Schema))
                    {
                        registered = true;
                        break;
                    }
                }

                if (!registered)
                {
                    throw new InvalidOperationException("The destination archetype omits a registered Derived dependency.");
                }
            }
        }
    }

    /// <summary>Creates one Entity after preflighting its identity, archetype, columns, and World-owned memory.</summary>
    /// <param name="description">The immutable initial identity, component values, and registered tags.</param>
    /// <returns>The live Entity in this World.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="description"/> is null.</exception>
    /// <exception cref="ArgumentException">The description contains an unregistered component/tag or a reserved identity.</exception>
    /// <exception cref="InvalidOperationException">The World is unavailable, an admission limit is reached, or a protocol invariant fails.</exception>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The prepared identity candidate is disposed by Cleanup on both operation outcomes; its finally path also releases the World operation gate.")]
    public Entity CreateEntity(EntityDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);
        EnterStructuralOperation();

        var oldOwnedBytes = 0L;
        var reservedPeakBytes = 0L;
        StableIdentityLedger.PreparedChange? identityChange = null;
        WorldArchetype? candidateArchetype = null;
        WorldArchetype? targetArchetype = null;
        var componentRowInitialized = false;
        var entityAppended = false;
        var candidateInstalled = false;
        var operationCommitted = false;
        var slotAllocated = false;
        var usedFreeSlot = false;
        uint allocatedSlot = 0;
        uint allocatedGeneration = 0;
        var chunkIndex = -1;
        var row = -1;
        var appendedChunkHadOrdinal = false;
        var previousNextChunkOrdinal = 0L;
        var candidateWorkPlanByteReservation = 0L;
        WorldChunkWorkPlanEntry[]? candidateWorkPlan = null;
        var createdEntity = default(Entity);

        try
        {
            try
            {
                oldOwnedBytes = GetWorldOwnedStateBytes();
                ValidateEntityDescription(description);

                if (_entitySlots.LiveEntityCount >= _maximumLiveEntities)
                {
                    throw new InvalidOperationException("The configured live Entity limit was reached.");
                }

                targetArchetype = FindArchetype(description);
                var createArchetype = targetArchetype is null;

                if (createArchetype && (_archetypeCount >= _maximumArchetypes))
                {
                    throw new InvalidOperationException("The configured archetype limit was reached.");
                }

                var componentCount = description.Components.Length;
                var tagCount = description.Tags.Length;
                var candidateMetadataBytes = createArchetype
                ? WorldArchetype.GetMetadataByteReservation(componentCount, tagCount)
                : 0L;
                var defaultCount = 0;

                foreach (var initialization in description.Components)
                {
                    defaultCount += initialization.IsDefault ? 1 : 0;
                }

                var initializationBytes = (componentCount == 0)
                ? 0
                : checked(ArrayBytes(componentCount, IntPtr.Size) + ((long)defaultCount * 128));
                var nextAllocatedSlotCount = checked(
                _entitySlots.AllocatedSlotCount + ((_entitySlots.FreeSlotCount == 0) ? 1 : 0));
                var requiredFreeSlotCount = _entitySlots.FreeSlotCount;
                var slotPreparationBytes = _entitySlots.GetCapacityPreparationBytes(nextAllocatedSlotCount, requiredFreeSlotCount);
                var identityPreparationBytes = _identityLedger.GetPreparationByteReservation(1, 0);
                var identityGrowthBytes = checked(identityPreparationBytes - _identityLedger.OwnedByteReservation);
                var storagePreparationBytes = createArchetype
                ? 0
                : targetArchetype!.Storage.GetCapacityPreparationBytes(targetArchetype.Storage.EntityCount + 1);
                var initialPeakBytes = checked(
                identityGrowthBytes
                + slotPreparationBytes
                + storagePreparationBytes
                + candidateMetadataBytes
                + initializationBytes);

                if (checked(oldOwnedBytes + initialPeakBytes) > _maximumOwnedStateBytes)
                {
                    throw new InvalidOperationException("The World-owned state budget cannot admit the complete Entity preparation peak.");
                }

                if (!_domainReservation.TryReserveAdditional(initialPeakBytes))
                {
                    throw new InvalidOperationException("The admission domain cannot reserve the complete Entity preparation peak.");
                }

                reservedPeakBytes = initialPeakBytes;

                if (createArchetype)
                {
                    var components = BuildComponentRegistrations(description);
                    var tags = BuildTagRegistrations(description);
                    var newArchetype = new WorldArchetype(
                    _ownerId,
                    _archetypeCount,
                    _maximumLiveEntities,
                    components,
                    tags);
                    candidateArchetype = newArchetype;
                    targetArchetype = newArchetype;
                    storagePreparationBytes = newArchetype.Storage.GetCapacityPreparationBytes(1);

                    if (checked(oldOwnedBytes + reservedPeakBytes + storagePreparationBytes) > _maximumOwnedStateBytes)
                    {
                        throw new InvalidOperationException("The admission domain cannot reserve the new archetype's complete storage peak.");
                    }

                    if (!_domainReservation.TryReserveAdditional(storagePreparationBytes))
                    {
                        throw new InvalidOperationException("The admission domain cannot reserve the new archetype's complete storage peak.");
                    }

                    reservedPeakBytes = checked(reservedPeakBytes + storagePreparationBytes);
                }

                var archetype = targetArchetype
                ?? throw new InvalidOperationException("The EntityDescription did not resolve an archetype.");

                if (!_entitySlots.TryGetNextAllocation(out var expectedSlot, out var expectedGeneration))
                {
                    throw new InvalidOperationException("The configured Entity-slot limit was reached.");
                }

                var identityAssignment = description.PersistentId is { } stableId
                ? new IdentityAssignment(IdentityKind.Entity, IdentityOrigin.Explicit, expectedSlot, _currentIdentityBranchIndex, stableId)
                : new IdentityAssignment(
                    IdentityKind.Entity,
                    IdentityOrigin.Automatic,
                    expectedSlot,
                    _currentIdentityBranchIndex,
                    issuanceNamespace: _identityBranches[_currentIdentityBranchIndex].NamespaceId);
                identityChange = _identityLedger.Prepare([identityAssignment], [], _maximumOwnedStateBytes);

                var initializations = new ComponentInitialization[componentCount];

                for (var componentIndex = 0; componentIndex < componentCount; componentIndex++)
                {
                    var registration = archetype.Components[componentIndex];
                    var initialization = FindInitialization(description, registration.ComponentType);
                    initializations[componentIndex] = registration.CreateInitializedValue(initialization, _spatialSettings);
                }

                var requiredSlotCapacity = _entitySlots.AllocatedSlotCount + ((_entitySlots.FreeSlotCount == 0) ? 1 : 0);

                if (!_entitySlots.PrepareCapacity(requiredSlotCapacity, requiredFreeSlotCount))
                {
                    throw new InvalidOperationException("The preflighted Entity-slot capacity could not be prepared.");
                }

                if (!archetype.Storage.PrepareCapacity(archetype.Storage.EntityCount + 1))
                {
                    throw new InvalidOperationException("The preflighted archetype capacity could not be prepared.");
                }

                if (!archetype.Storage.TryGetNextAppendLocation(_nextChunkOrdinal, out chunkIndex, out row))
                {
                    throw new InvalidOperationException("The shared World chunk-ordinal cursor is exhausted.");
                }

                var requiredWorkPlanCount = checked(_componentTagWorkPlanCount + ((row % 64 == 0) ? 1 : 0));
                var candidateWorkPlanCapacity = GetComponentTagWorkPlanCapacityFor(requiredWorkPlanCount);

                if (candidateWorkPlanCapacity > _componentTagWorkPlan.Length)
                {
                    candidateWorkPlanByteReservation = CalculateComponentTagWorkPlanByteReservation(candidateWorkPlanCapacity);

                    if (checked(oldOwnedBytes + reservedPeakBytes + candidateWorkPlanByteReservation) > _maximumOwnedStateBytes)
                    {
                        throw new InvalidOperationException("The World-owned state budget cannot admit component/tag work-plan growth.");
                    }

                    if (!_domainReservation.TryReserveAdditional(candidateWorkPlanByteReservation))
                    {
                        throw new InvalidOperationException("The admission domain cannot reserve component/tag work-plan growth.");
                    }

                    reservedPeakBytes = checked(reservedPeakBytes + candidateWorkPlanByteReservation);
                    candidateWorkPlan = new WorldChunkWorkPlanEntry[candidateWorkPlanCapacity];
                }

                componentRowInitialized = componentCount != 0;

                for (var componentIndex = 0; componentIndex < componentCount; componentIndex++)
                {
                    archetype.Storage.InitializeColumnRow(
                        componentIndex,
                        chunkIndex,
                        row,
                        initializations[componentIndex],
                        _spatialSettings);
                }

                for (var componentIndex = 0; componentIndex < componentCount; componentIndex++)
                {
                    ThrowIfValidationFailed(
                        archetype.Storage.ValidateColumnRow(componentIndex, chunkIndex, row, CancellationToken.None),
                        nameof(description),
                        "A component value in the EntityDescription failed its registered validation.");
                    archetype.Storage.ValidateEntityReferencesColumnRow(
                        componentIndex,
                        chunkIndex,
                        row,
                        this,
                        excludedEntity: null,
                        nameof(description));
                }

                identityChange.ValidateForCommit();
                var nextMutationRevision = checked(_canonicalMutationRevision + 1);
                usedFreeSlot = _entitySlots.FreeSlotCount != 0;

                if (!_entitySlots.TryAllocate(out var slotIndex, out var generation))
                {
                    throw new InvalidOperationException("The preflighted Entity slot could not be allocated.");
                }

                allocatedSlot = slotIndex;
                allocatedGeneration = generation;
                slotAllocated = true;

                if ((slotIndex != expectedSlot) || (generation != expectedGeneration))
                {
                    throw new InvalidOperationException("The preflighted Entity slot changed before commit.");
                }

                var entity = CreateEntityHandle(slotIndex, generation);
                createdEntity = entity;
                previousNextChunkOrdinal = _nextChunkOrdinal;
                appendedChunkHadOrdinal = archetype.Storage.TryGetChunkOrdinal(chunkIndex, out _);

                if (!archetype.Storage.TryAppend(entity, _entitySlots, ref _nextChunkOrdinal))
                {
                    throw new InvalidOperationException("The preflighted archetype row could not be committed.");
                }

                entityAppended = true;
                identityChange.Commit();

                if (candidateArchetype is not null)
                {
                    InstallArchetype(candidateArchetype);
                    candidateInstalled = true;
                }

                if (candidateWorkPlan is not null)
                {
                    _componentTagWorkPlan = candidateWorkPlan;
                    _componentTagWorkPlanByteReservation = candidateWorkPlanByteReservation;
                }

                _canonicalMutationRevision = nextMutationRevision;
                operationCommitted = true;
                RebuildComponentTagWorkPlan();
            }
            catch
            {
                Cleanup();
                throw;
            }

            Cleanup();
            return createdEntity;
        }
        finally
        {
            _ = Interlocked.Exchange(ref _isStructuralOperationActive, 0);
        }

        void Cleanup()
        {
            try
            {
                if (!operationCommitted && entityAppended)
                {
                    var archetype = targetArchetype
                        ?? throw new InvalidOperationException("The allocated Entity has no target archetype.");
                    _ = archetype.Storage.Remove(createdEntity, _entitySlots);

                    if (!appendedChunkHadOrdinal)
                    {
                        archetype.Storage.ClearRolledBackChunkOrdinal(chunkIndex, previousNextChunkOrdinal);
                        _nextChunkOrdinal = previousNextChunkOrdinal;
                    }

                    _entitySlots.RollbackAllocation(allocatedSlot, allocatedGeneration, usedFreeSlot);
                }
                else if (!operationCommitted && slotAllocated)
                {
                    _entitySlots.RollbackAllocation(allocatedSlot, allocatedGeneration, usedFreeSlot);
                }

                if (!operationCommitted && componentRowInitialized && !entityAppended && (targetArchetype is not null) && (chunkIndex >= 0))
                {
                    targetArchetype.Storage.ClearUncommittedRow(chunkIndex, row);
                }

                if (!candidateInstalled)
                {
                    candidateArchetype?.DiscardUncommittedStorage();
                }

                if (reservedPeakBytes != 0)
                {
                    var persistentGrowthBytes = checked(GetWorldOwnedStateBytes() - oldOwnedBytes);

                    if (persistentGrowthBytes < 0)
                    {
                        throw new InvalidOperationException("World-owned storage decreased during Entity creation.");
                    }

                    var temporaryBytes = reservedPeakBytes - persistentGrowthBytes;

                    if (temporaryBytes > 0)
                    {
                        _domainReservation.ReleaseAdditional(temporaryBytes);
                    }
                    else if (temporaryBytes < 0 && !_domainReservation.TryReserveAdditional(-temporaryBytes))
                    {
                        throw new InvalidOperationException("The admission domain cannot retain the prepared World-owned storage.");
                    }
                }
            }
            finally
            {
                identityChange?.Dispose();
            }
        }
    }

    /// <summary>Disposes the World and releases its active identity, fresh lineage root, diagnostics, and domain interest.</summary>
    /// <exception cref="InvalidOperationException">The call is not on the owner thread, another World operation is active, or an identity retirement invariant fails.</exception>
    [SuppressMessage("Design", "CA1065:Do not raise exceptions in unexpected locations", Justification = "A failed identity retirement is an invariant violation; surfacing it prevents disposal from appearing successful while the process binding remains active.")]
    [SuppressMessage("Usage", "CA1031:Do not catch general exception types", Justification = "Transferred attachment Release failures are retained and reported after all prepared safe cleanup callbacks run.")]
    public void Dispose()
    {
        ThrowIfAttachmentCallbackActive();
        ThrowIfSystemInvocationActive();

        if (Volatile.Read(ref _isSystemCleanupActive) != 0)
        {
            var exception = new InvalidOperationException("The World cannot be disposed from a system Release callback.");
            _systemCleanupStickyFailure ??= exception;
            throw _systemCleanupStickyFailure;
        }

        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        if (_lifetimeControl.HasTerminalAnchor
            && (Volatile.Read(ref _isTerminalCloseInvocation) == 0))
        {
            EnsureOwnerThread();
            _lifetimeControl.Registry.InvokeTerminalWorldClose(this, _lifetimeControl);
            return;
        }

        EnsureOwnerThread();

        if (Interlocked.CompareExchange(ref _isStructuralOperationActive, 1, 0) != 0)
        {
            throw new InvalidOperationException("The World cannot be disposed during a structural operation.");
        }

        WorldAttachmentQuarantineReservation? attachmentQuarantineReservation = null;
        Exception? attachmentCleanupException = null;
        var admissionDomain = _admissionDomain;
        var domainReservation = _domainReservation;
        var closeStarted = false;
        var closeSucceeded = false;

        try
        {
            var transferredAttachments = _attachments.CaptureTransferredEntries();
            var releaseBindings = PrepareAttachmentReleaseBindings(transferredAttachments);
            var attachmentReleasePreparation = (transferredAttachments.Length == 0)
                ? null
                : new WorldAttachmentReleasePreparation(
                    _worldGuid,
                    transferredAttachments.Length,
                    _canonicalMutationRevision);
            attachmentQuarantineReservation = WorldAttachmentInstanceRegistry.ReserveQuarantineCapacity(transferredAttachments.Length);

            lock (_sceneStorageGate)
            {
                lock (_persistenceAdmissionGate)
                {
                    lock (_ownerControlGate)
                    {
                        if (Interlocked.Exchange(ref _disposed, 1) != 0)
                        {
                            return;
                        }
                    }
                }

                admissionDomain.BeginWorldClose(domainReservation);
                closeStarted = true;
            }

            if (!_lifetimeControl.BeginRetirement())
            {
                if (_registry.IsActive(_lifetimeControl.WorldGuidGrant))
                {
                    lock (_sceneStorageGate)
                    {
                        if (admissionDomain.TryReopenWorldClose(domainReservation))
                        {
                            _ = Interlocked.Exchange(ref _disposed, 0);
                            closeStarted = false;
                        }
                    }
                }

                throw new InvalidOperationException("The WorldGuid binding could not be revoked by its exact close attempt.");
            }

            RejectPendingOwnerControlRequestsForDisposal();

            if (_ownsWorkerPool)
            {
                _workerPool.Dispose();
            }

            _cpuExtractionParticipant?.Dispose();
            _cpuExtractionParticipant = null;
            _sceneParticipant?.Dispose();
            _sceneParticipant = null;
            ClearSubscriptionsForDisposal();
            _attachments.ClearEntries();

            if (transferredAttachments.Length != 0)
            {
                ReleaseAttachmentEntries(
                    transferredAttachments,
                    releaseBindings,
                    attachmentQuarantineReservation,
                    attachmentReleasePreparation
                        ?? throw new InvalidOperationException("The attachment release report was not prepared."));
            }

            var systemCleanupReport = ReleaseSystemInstances(WorldSystemCleanupOperation.Disposal, null);
            _lastSystemCleanupReport = systemCleanupReport;

            if (attachmentReleasePreparation is { HasFailures: true })
            {
                attachmentCleanupException = attachmentReleasePreparation.CreateException(
                    (systemCleanupReport.FailureCount != 0) ? systemCleanupReport : null);
            }

            var attachmentQuarantineRetainsReservation =
                WorldAttachmentInstanceRegistry.HasRetainedDomainReservation(_domainReservation);
            var cleanupRequiresQuarantine = (systemCleanupReport.FailureCount != 0)
                || (attachmentCleanupException is not null)
                || _systemQuarantineReservation.RetainsDomainReservation
                || attachmentQuarantineRetainsReservation;
            var diagnosticsServiceOwner = _diagnosticsServiceOwner
                ?? throw new InvalidOperationException("The disposed World lost its diagnostic service owner.");
            diagnosticsServiceOwner.CloseCapability(_diagnosticsCapability);
            _identityLedger.ReleaseOwnedStorage();
            _entitySlots.ReleaseOwnedStorage();
            _archetypes = [];
            _queryArchetypes = [];
            _componentTagWorkPlan = [];
            _componentTagWorkPlanByteReservation = 0;
            _componentTagWorkPlanCount = 0;
            _componentTagWorkPlanEpoch = 0;
            _controlRecords = [];
            _controlRecordCount = 0;
            _controlStorageByteReservation = 0;
            _sceneControlPayloadByteReservation = 0;
            _identityBranches = [];
            _identityBranchCount = 0;
            _resourceStorage = [];
            _archetypeCount = 0;
            _ = _attachments.ReleaseOwnedStorage();

            if (cleanupRequiresQuarantine)
            {
                if (!_lifetimeControl.QuarantineAfterCleanupFailure())
                {
                    throw new InvalidOperationException("The disposed World lifetime could not be quarantined after cleanup failure.");
                }

                WorldSystemInstanceRegistry.QuarantineLifetimeControl(_lifetimeControl, _systemQuarantineReservation);
            }

            _lifetimeControl.CloseDefaultDomain();
            diagnosticsServiceOwner.ReleaseWorldUse();
            _diagnosticsServiceOwner = null;
            var diagnosticFundingControl = _diagnosticFundingControl
                ?? throw new InvalidOperationException("The disposed World lost its diagnostic funding use.");
            diagnosticFundingControl.ReleaseUse();
            _diagnosticFundingControl = null;

            if (attachmentCleanupException is not null)
            {
                throw attachmentCleanupException;
            }

            if (systemCleanupReport.FailureCount != 0)
            {
                throw _systemCleanupPreparation.CreateCleanupException();
            }

            closeSucceeded = !cleanupRequiresQuarantine;
        }
        finally
        {
            var restoreLifetimeLease = _restoreLifetimeLease;
            var restoreReturned = false;
            var systemsReturned = false;
            var capacityReturned = false;
            var attachmentsReturned = false;

            try
            {
                restoreLifetimeLease?.Dispose();
                _restoreLifetimeLease = null;
                restoreReturned = true;
            }
            finally
            {
                try
                {
                    _systemQuarantineReservation.Release();
                    systemsReturned = true;
                }
                finally
                {
                    try
                    {
                        WorldSystemInstanceRegistry.ReleaseLifetimeControlCapacity(_systemQuarantineReservation);
                        capacityReturned = true;
                    }
                    finally
                    {
                        try
                        {
                            attachmentQuarantineReservation?.Release();
                            attachmentsReturned = true;
                        }
                        finally
                        {
                            _ = Interlocked.Exchange(ref _isStructuralOperationActive, 0);

                            if (closeStarted)
                            {
                                admissionDomain.CompleteWorldClose(
                                    domainReservation,
                                    closeSucceeded && restoreReturned && systemsReturned && capacityReturned && attachmentsReturned);
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>Gets the stable Entity identity for a live Entity in this World.</summary>
    /// <param name="entity">The live Entity handle to resolve.</param>
    /// <returns>The persistent Entity identity.</returns>
    /// <exception cref="ArgumentException"><paramref name="entity"/> is default, stale, or belongs to another World.</exception>
    /// <exception cref="InvalidOperationException">The World is not accessed on its owner thread or an identity row is missing.</exception>
    public Guid GetEntityId(Entity entity)
    {
        EnsureWorldReadAccess();
        _ = GetLiveArchetype(entity, out _, out _);
        return _identityLedger.GetAssignedId(IdentityKind.Entity, entity.Index);
    }

    /// <summary>Gets the World-, lineage-, and branch-qualified identity for a live Entity.</summary>
    /// <param name="entity">The live Entity handle to resolve.</param>
    /// <returns>The qualified stable identity.</returns>
    /// <exception cref="ArgumentException"><paramref name="entity"/> is default, stale, or belongs to another World.</exception>
    /// <exception cref="InvalidOperationException">The World is not accessed on its owner thread or an identity row is missing.</exception>
    public WorldEntityIdentity GetEntityIdentity(Entity entity)
    {
        var entityId = GetEntityId(entity);
        return new WorldEntityIdentity(_worldGuid, _replayLineageId, GetCreationBranchId(entity.Index), entityId);
    }

    /// <summary>Returns whether a live Entity has one registered component type.</summary>
    /// <typeparam name="T">The unmanaged component type.</typeparam>
    /// <param name="entity">The live Entity handle to inspect.</param>
    /// <returns><see langword="true"/> when the Entity has the registered component.</returns>
    /// <exception cref="ArgumentException"><paramref name="entity"/> is default, stale, or belongs to another World.</exception>
    /// <exception cref="InvalidOperationException">The World is not idle on its owner thread or T is not registered.</exception>
    public bool Has<T>(Entity entity)
        where T : unmanaged
    {
        EnsureWorldReadAccess();
        var archetype = GetLiveArchetype(entity, out _, out _);
        var component = GetComponentRegistration<T>();
        return archetype.GetComponentIndex(component.ComponentType) >= 0;
    }

    /// <summary>Returns whether a live Entity has one registered tag.</summary>
    /// <param name="entity">The live Entity handle to inspect.</param>
    /// <param name="schemaId">The registered tag schema identity.</param>
    /// <returns><see langword="true"/> when the Entity has the tag.</returns>
    /// <exception cref="ArgumentException"><paramref name="entity"/> is default, stale, or belongs to another World.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="schemaId"/> is empty.</exception>
    /// <exception cref="KeyNotFoundException">The tag schema identity is not registered.</exception>
    /// <exception cref="InvalidOperationException">The World is not idle on its owner thread.</exception>
    public bool HasTag(Entity entity, Guid schemaId)
    {
        EnsureWorldReadAccess();
        ArgumentOutOfRangeException.ThrowIfEqual(schemaId, Guid.Empty);
        var archetype = GetLiveArchetype(entity, out _, out _);
        EnsureRegisteredTag(schemaId);
        return archetype.HasTag(schemaId);
    }

    /// <summary>Replaces one registered component value on a live Entity.</summary>
    /// <typeparam name="T">The unmanaged component type.</typeparam>
    /// <param name="entity">The live Entity handle to update.</param>
    /// <param name="value">The replacement component value.</param>
    /// <exception cref="ArgumentException"><paramref name="entity"/> is default, stale, or belongs to another World.</exception>
    /// <exception cref="InvalidOperationException">The World is not idle on its owner thread, T is not registered, or the Entity lacks T.</exception>
    public void Set<T>(Entity entity, in T value)
        where T : unmanaged
    {
        EnterStructuralOperation();

        try
        {
            var archetype = GetLiveArchetype(entity, out var chunkIndex, out var row);
            var component = GetComponentRegistration<T>();
            var componentIndex = archetype.GetComponentIndex(component.ComponentType);

            if (componentIndex < 0)
            {
                throw new InvalidOperationException("The live Entity does not have the selected component.");
            }

            ThrowIfValidationFailed(
                component.ValidateValue(in value, CancellationToken.None),
                nameof(value),
                "The component value failed its registered validation.");
            component.ValidateEntityReferencesValue(in value, this, null, nameof(value));
            var valueSpan = archetype.Storage.GetColumnSpan<T>(componentIndex, chunkIndex, row, 1);
            _canonicalMutationRevision = checked(_canonicalMutationRevision + 1);
            valueSpan[0] = value;
        }
        finally
        {
            _ = Interlocked.Exchange(ref _isStructuralOperationActive, 0);
        }
    }

    /// <summary>Copies the value of one registered component from a live Entity.</summary>
    /// <typeparam name="T">The unmanaged component type.</typeparam>
    /// <param name="entity">The live Entity handle to inspect.</param>
    /// <param name="value">The copied component value, or exactly default when the live Entity lacks T.</param>
    /// <returns><see langword="true"/> when the live Entity has T.</returns>
    /// <exception cref="ArgumentException"><paramref name="entity"/> is default, stale, or belongs to another World.</exception>
    /// <exception cref="InvalidOperationException">The World is not idle on its owner thread or T is not registered.</exception>
    public bool TryGet<T>(Entity entity, out T value)
        where T : unmanaged
    {
        EnsureWorldReadAccess();
        var archetype = GetLiveArchetype(entity, out var chunkIndex, out var row);
        var component = GetComponentRegistration<T>();
        var componentIndex = archetype.GetComponentIndex(component.ComponentType);

        if (componentIndex < 0)
        {
            value = default;
            return false;
        }

        value = archetype.Storage.GetReadOnlyColumnSpan<T>(componentIndex, chunkIndex, row, 1)[0];
        return true;
    }

    /// <summary>Looks up a live Entity by its stable identity in this World.</summary>
    /// <param name="stableEntityId">The stable Entity identity to resolve.</param>
    /// <param name="entity">The live Entity, or exactly default when the identity is absent or retired.</param>
    /// <returns><see langword="true"/> when the stable identity belongs to a live Entity.</returns>
    /// <exception cref="ArgumentException"><paramref name="stableEntityId"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">The World is not idle on its owner thread or the identity/slot tables disagree.</exception>
    public bool TryGetEntity(Guid stableEntityId, out Entity entity)
    {
        EnsureWorldReadAccess();

        if (stableEntityId == Guid.Empty)
        {
            throw new ArgumentException("A nonempty stable Entity identity is required.", nameof(stableEntityId));
        }

        if (!_identityLedger.TryGetLiveLocator(stableEntityId, IdentityKind.Entity, out var locator))
        {
            entity = default;
            return false;
        }

        if (!_entitySlots.TryGetLive(locator, out var generation))
        {
            throw new InvalidOperationException("The stable identity ledger references a non-live Entity slot.");
        }

        if (!_entitySlots.TryGetLocation(locator, generation, out _, out _, out _))
        {
            throw new InvalidOperationException("The stable identity ledger references an Entity slot without a live storage location.");
        }

        entity = CreateEntityHandle(locator, generation);
        return true;
    }

    /// <summary>Looks up a live Entity by its World-qualified stable identity.</summary>
    /// <param name="identity">The qualified stable Entity identity.</param>
    /// <param name="entity">The live Entity, or exactly default when the qualified identity is absent.</param>
    /// <returns><see langword="true"/> when the qualified identity belongs to a live Entity in this World.</returns>
    /// <exception cref="ArgumentException"><paramref name="identity"/> is invalid.</exception>
    /// <exception cref="InvalidOperationException">The World is not idle on its owner thread.</exception>
    public bool TryGetEntity(WorldEntityIdentity identity, out Entity entity)
    {
        EnsureWorldReadAccess();

        if (!identity.IsValid)
        {
            throw new ArgumentException("A valid qualified Entity identity is required.", nameof(identity));
        }

        if ((identity.WorldGuid != _worldGuid)
            || (identity.CreationLineageId != _replayLineageId)
            || !IsIdentityBranchAdmitted(identity.CreationBranchId))
        {
            entity = default;
            return false;
        }

        return TryGetEntity(identity.EntityId, out entity);
    }

    internal WorldGuidUse AcquireGuidUse(long workspaceLimit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(workspaceLimit);
        ThrowIfAttachmentCallbackActive();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ThrowIfFaulted();
        return _lifetimeControl.AcquireUse(workspaceLimit);
    }

    internal void ApplyHierarchyParentChange(
        IWorldSceneParticipant participant,
        Entity child,
        Entity? parent)
    {
        ArgumentNullException.ThrowIfNull(participant);
        EnterStructuralOperation();

        try
        {
            if (!ReferenceEquals(_sceneParticipant, participant))
            {
                throw new ArgumentException("The Scene participant is not owned by this exact World.", nameof(participant));
            }

            _ = GetLiveArchetype(child, out _, out _);

            if (parent is Entity parentEntity)
            {
                _ = GetLiveArchetype(parentEntity, out _, out _);
            }

            var hasCurrentParent = _entitySlots.TryGetParent(
                child.Index,
                child.Generation,
                out var currentParentIndex,
                out var currentParentGeneration);

            var hasRequestedParent = parent.HasValue;
            var requestedParent = parent.GetValueOrDefault();

            if (hasCurrentParent == hasRequestedParent
                && (!hasCurrentParent
                    || ((currentParentIndex == requestedParent.Index)
                        && (currentParentGeneration == requestedParent.Generation))))
            {
                return;
            }

            var nextMutationRevision = checked(_canonicalMutationRevision + 1);
            _entitySlots.SetParent(
                child.Index,
                child.Generation,
                hasRequestedParent ? requestedParent.Index : 0,
                hasRequestedParent ? requestedParent.Generation : 0);
            _canonicalMutationRevision = nextMutationRevision;
        }
        finally
        {
            _ = Interlocked.Exchange(ref _isStructuralOperationActive, 0);
        }
    }

    internal Diagnostic CreateDiagnostic(
        string code,
        DiagnosticSeverity severity,
        string operation,
        string message,
        int? assetId = null)
        => new(
            code,
            severity,
            _diagnosticsCapability.GetTimestamp(),
            null,
            _worldGuid,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            assetId,
            null,
            operation,
            null,
            message,
            []);

    internal void DisposeWithinTerminalInvocation(WorldGuidRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        if (!ReferenceEquals(_lifetimeControl.Registry, registry)
            || (Interlocked.CompareExchange(ref _isTerminalCloseInvocation, 1, 0) != 0))
        {
            throw new InvalidOperationException("The terminal close invocation does not own this World.");
        }

        try
        {
            Dispose();
        }
        finally
        {
            Volatile.Write(ref _isTerminalCloseInvocation, 0);
        }
    }

    internal SpatialFrame3 GetCurrentSceneHierarchyFrame(IWorldSceneParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(participant);
        ThrowIfDisposed();

        if (!ReferenceEquals(_sceneParticipant, participant))
        {
            throw new ArgumentException("The Scene participant is not owned by this exact World.", nameof(participant));
        }

        return _currentFrameCohort.Frame3;
    }

    internal IWorldSceneParticipant GetSceneParticipant(SceneSupportRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ThrowIfDisposed();

        if (!ReferenceEquals(Registration.SceneSupport, registration))
        {
            throw new ArgumentException("The Scene support registration is not the exact registration captured by this World.", nameof(registration));
        }

        return _sceneParticipant
            ?? throw new InvalidOperationException("The World Scene participant has not been initialized.");
    }

    internal long GetWorldOwnedStateBytes()
    {
        var (constructionBytes, storageBytes) = _admissionDomain.GetSceneReservationBytes(_domainReservation);
        return checked(GetOtherWorldOwnedStateBytes() + constructionBytes + storageBytes);
    }

    internal bool HasComponentFromScene(Entity entity, ContentSchemaReference schema)
    {
        var archetype = GetLiveArchetype(entity, out _, out _);
        var registration = Registration.GetComponentRegistration(schema);
        return archetype.GetComponentIndex(registration.ComponentType) >= 0;
    }

    internal bool IsHostedBy(
        WorldWorkerPool workerPool,
        WorldDiagnosticsServiceOwner diagnosticsServiceOwner,
        WorldAdmissionDomain admissionDomain)
        => (_lifetimeControl.DefaultDomainOwner is null)
            && !_ownsWorkerPool
            && ReferenceEquals(_workerPool, workerPool)
            && ReferenceEquals(_diagnosticsServiceOwner, diagnosticsServiceOwner)
            && ReferenceEquals(_admissionDomain, admissionDomain);

    internal bool TryAttachTerminalAnchorForDefaultDomain()
    {
        EnsureOwnerThread();

        if ((Volatile.Read(ref _disposed) != 0)
            || (Volatile.Read(ref _isStructuralOperationActive) != 0))
        {
            return false;
        }

        return _lifetimeControl.TryAttachTerminalAnchor(_domainReservation);
    }

    internal bool TryGetEntityByIdentityOrdinal(uint ordinal, out Entity entity)
    {
        EnsureWorldReadAccess();

        if (!_identityLedger.TryGetLiveLocatorByOrdinal(ordinal, IdentityKind.Entity, out var locator))
        {
            entity = default;
            return false;
        }

        if (!_entitySlots.TryGetLive(locator, out var generation)
            || !_entitySlots.TryGetLocation(locator, generation, out _, out _, out _))
        {
            throw new InvalidOperationException("The stable identity ledger references a non-live Entity slot.");
        }

        entity = CreateEntityHandle(locator, generation);
        return true;
    }

    internal bool TryGetFirstHierarchyChild(Entity parent, out Entity child)
    {
        EnsureWorldReadAccess();
        _ = GetLiveArchetype(parent, out _, out _);

        if (_sceneParticipant is null)
        {
            throw new InvalidOperationException("The World was not constructed with Scene support.");
        }

        if (!_entitySlots.TryGetFirstChild(
                parent.Index,
                parent.Generation,
                out var childIndex,
                out var childGeneration))
        {
            child = default;
            return false;
        }

        child = CreateEntityHandle(childIndex, childGeneration);
        return true;
    }

    internal bool TryGetHierarchyParent(Entity child, out Entity parent)
    {
        EnsureWorldReadAccess();
        _ = GetLiveArchetype(child, out _, out _);

        if (_sceneParticipant is null)
        {
            throw new InvalidOperationException("The World was not constructed with Scene support.");
        }

        if (!_entitySlots.TryGetParent(
                child.Index,
                child.Generation,
                out var parentIndex,
                out var parentGeneration))
        {
            parent = default;
            return false;
        }

        parent = CreateEntityHandle(parentIndex, parentGeneration);
        return true;
    }

    internal bool TryGetLiveEntityIdentityOrdinal(Entity entity, out uint ordinal)
    {
        EnsureWorldReadAccess();

        if (!entity.IsValid
            || (entity.OwnerId != _ownerId)
            || !_entitySlots.TryGetLocation(entity.Index, entity.Generation, out _, out _, out _))
        {
            ordinal = 0;
            return false;
        }

        ordinal = _identityLedger.GetLiveOrdinal(IdentityKind.Entity, entity.Index);

        if (ordinal == 0)
        {
            throw new InvalidOperationException("The live Entity has no stable identity assignment.");
        }

        return true;
    }

    internal bool TryGetNextHierarchySibling(Entity child, out Entity sibling)
    {
        EnsureWorldReadAccess();
        _ = GetLiveArchetype(child, out _, out _);

        if (_sceneParticipant is null)
        {
            throw new InvalidOperationException("The World was not constructed with Scene support.");
        }

        if (!_entitySlots.TryGetNextSibling(
                child.Index,
                child.Generation,
                out var siblingIndex,
                out var siblingGeneration))
        {
            sibling = default;
            return false;
        }

        sibling = CreateEntityHandle(siblingIndex, siblingGeneration);
        return true;
    }

    internal WorldSceneStorageReservation? TryReserveRestoredSceneStorage(
        IWorldSceneParticipant participant,
        long bytes)
        => TryReserveSceneReservation(participant, bytes, null, workspace: false, restoredCapacity: _isRestoreCandidate);

    internal WorldSceneStorageReservation? TryReserveSceneStorage(
        IWorldSceneParticipant participant,
        long bytes,
        WorldGuidUseControl? useControl = null)
        => TryReserveSceneReservation(participant, bytes, useControl, workspace: false, restoredCapacity: false);

    internal WorldSceneStorageReservation? TryReserveSceneWorkspace(
        IWorldSceneParticipant participant,
        WorldGuidUseControl useControl,
        long bytes)
    {
        ArgumentNullException.ThrowIfNull(useControl);
        return TryReserveSceneReservation(participant, bytes, useControl, workspace: true, restoredCapacity: false);
    }

    internal void ValidateEntityReference(Entity target, Entity? excludedEntity, string? parameterName)
    {
        var isLiveInThisWorld = target.IsValid
            && (target.OwnerId == _ownerId)
            && _entitySlots.TryGetLocation(target.Index, target.Generation, out _, out _, out _);
        var isProjectedInThisWorld = target.IsValid
            && (target.OwnerId == _ownerId)
            && (_activeCommandProjectedEntityHandles?.Contains(target) ?? false);
        var isExcluded = excludedEntity is { } excluded && (target == excluded);
        var isExcludedByCommand = _activeCommandExcludedEntityHandles?.Contains(target) ?? false;

        if ((isLiveInThisWorld || isProjectedInThisWorld) && !isExcluded && !isExcludedByCommand)
        {
            return;
        }

        const string message = "A declared Entity reference must target a live Entity in this World.";

        if (parameterName is null)
        {
            throw new InvalidOperationException(message);
        }

        throw new ArgumentException(message, parameterName);
    }

    private ImmutableArray<IComponentRegistration> BuildComponentRegistrations(EntityDescription description)
    {
        var registrations = ImmutableArray.CreateBuilder<IComponentRegistration>(description.Components.Length);

        foreach (var registration in Registration.Components)
        {
            if (ContainsComponentInitialization(description, registration.ComponentType))
            {
                registrations.Add(registration);
            }
        }

        if (registrations.Count != description.Components.Length)
        {
            throw new InvalidOperationException("The resolved component registration count does not match the EntityDescription.");
        }

        return registrations.MoveToImmutable();
    }

    private ImmutableArray<Guid> BuildTagRegistrations(EntityDescription description)
    {
        var tags = ImmutableArray.CreateBuilder<Guid>(description.Tags.Length);

        foreach (var tag in Registration.Tags)
        {
            if (description.Tags.Contains(tag.SchemaId))
            {
                tags.Add(tag.SchemaId);
            }
        }

        if (tags.Count != description.Tags.Length)
        {
            throw new InvalidOperationException("The resolved tag registration count does not match the EntityDescription.");
        }

        return tags.MoveToImmutable();
    }

    private void ConfigureOwnedStateBudget()
        => _admissionDomain.ConfigureWorldOwnedStateBudget(
            _domainReservation,
            _maximumOwnedStateBytes,
            GetOtherWorldOwnedStateBytes());

    private Entity CreateEntityHandle(uint slotIndex, uint generation)
        => new(_ownerId, slotIndex, generation);

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException("The standalone World is restricted to its creating thread.");
        }
    }

    private void EnsureRegisteredTag(Guid schemaId)
    {
        foreach (var tag in Registration.Tags)
        {
            if (tag.SchemaId == schemaId)
            {
                return;
            }
        }

        throw new KeyNotFoundException("The entity tag schema identity is not registered.");
    }

    private void EnsureWorldReadAccess()
    {
        ThrowIfAttachmentCallbackActive();
        ThrowIfSystemInvocationActive();
        ThrowIfDisposed();
        ThrowIfFaulted();
        EnsureOwnerThread();

        if (Volatile.Read(ref _isStructuralOperationActive) != 0
            && ((!_isFrameReadLeaseActive && !_isPersistenceCaptureLeaseActive)
                || (Environment.CurrentManagedThreadId != _ownerThreadId)))
        {
            throw new InvalidOperationException("World reads are unavailable during a structural operation.");
        }
    }

    private void EnterStructuralOperation()
    {
        ThrowIfAttachmentCallbackActive();
        ThrowIfSystemInvocationActive();
        ThrowIfDisposed();
        ThrowIfFaulted();
        EnsureOwnerThread();

        if (Interlocked.CompareExchange(ref _isStructuralOperationActive, 1, 0) != 0)
        {
            throw new InvalidOperationException("The World already has an active structural operation.");
        }
    }

    private void FaultWorld(
        Diagnostic? validationDiagnostic,
        Exception exception,
        string fallbackCode = "WORLD_COMPONENT_VALIDATION_FAILED",
        string operation = "World.ComponentValidation")
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (Volatile.Read(ref _faulted) != 0)
        {
            return;
        }

        var fault = new Diagnostic(
            validationDiagnostic?.Code ?? fallbackCode,
            DiagnosticSeverity.Error,
            _diagnosticsCapability.GetTimestamp(),
            null,
            _worldGuid,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            operation,
            null,
            validationDiagnostic?.Message ?? exception.Message,
            []);
        WriteDiagnostic(fault);
        _lastFault = fault;
        Volatile.Write(ref _faulted, 1);
    }

    private WorldArchetype? FindArchetype(EntityDescription description)
    {
        for (var index = 0; index < _archetypeCount; index++)
        {
            var archetype = _archetypes[index]
                ?? throw new InvalidOperationException("The World archetype directory contains an empty active slot.");

            if (archetype.MatchesDescription(description))
            {
                return archetype;
            }
        }

        return null;
    }

    private IComponentRegistration FindComponentRegistration(Type componentType)
    {
        ArgumentNullException.ThrowIfNull(componentType);

        foreach (var component in Registration.Components)
        {
            if (component.ComponentType == componentType)
            {
                return component;
            }
        }

        throw new InvalidOperationException("The component type is not registered in this World.");
    }

    private IComponentRegistration GetComponentRegistration<T>()
        where T : unmanaged
        => FindComponentRegistration(typeof(T));

    private WorldArchetype GetLiveArchetype(Entity entity, out int chunkIndex, out int row)
    {
        chunkIndex = -1;

        if (!entity.IsValid)
        {
            throw new ArgumentException("A valid live Entity handle is required.", nameof(entity));
        }

        if (entity.OwnerId != _ownerId)
        {
            throw new ArgumentException("The Entity belongs to another World.", nameof(entity));
        }

        if (!_entitySlots.TryGetLocation(entity.Index, entity.Generation, out var archetypeIndex, out var chunkOrdinal, out row))
        {
            throw new ArgumentException("The Entity is stale or has no live archetype location.", nameof(entity));
        }

        if ((uint)archetypeIndex >= (uint)_archetypeCount)
        {
            throw new InvalidOperationException("The live Entity references a missing archetype or chunk.");
        }

        var archetype = _archetypes[archetypeIndex]
            ?? throw new InvalidOperationException("The live Entity references a missing archetype or chunk.");

        if (!archetype.Storage.TryFindChunkIndex(chunkOrdinal, out chunkIndex))
        {
            throw new InvalidOperationException("The live Entity references a missing archetype or chunk.");
        }

        return archetype;
    }

    private long GetOtherWorldOwnedStateBytes()
    {
        var bytes = checked(
            _identityLedger.OwnedByteReservation
            + _entitySlots.OwnedByteReservation
            + _attachments.OwnedByteReservation
            + _resourceStorageByteReservation
            + Interlocked.Read(ref _resourceOperationByteReservation)
            + _systemInstanceStorageByteReservation
            + _systemInvocationStorageByteReservation
            + _sceneSupportByteReservation
            + _componentTagWorkPlanByteReservation
            + _controlStorageByteReservation
            + _sceneControlPayloadByteReservation
            + _cpuExtractionSupportByteReservation
            + (_diagnosticFundingControl?.EnvelopeBytes ?? 0)
            + _subscriptionStorageByteReservation
            + _ownerCommandPreparationByteReservation
            + _ownerControlQueueByteReservation);

        for (var index = 0; index < _archetypeCount; index++)
        {
            var archetype = _archetypes[index]
                ?? throw new InvalidOperationException("The World archetype directory contains an empty active slot.");
            bytes = checked(bytes + archetype.MetadataByteReservation + archetype.Storage.OwnedByteReservation);
        }

        return bytes;
    }

    private void InitializeSceneSupport(WorldOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var sceneSupport = Registration.SceneSupport;

        if (sceneSupport is null)
        {
            return;
        }

        var participant = sceneSupport.CreateParticipant(this, options);

        if (participant.OwnedByteReservation != _sceneSupportByteReservation)
        {
            participant.Dispose();
            throw new InvalidOperationException("The Scene participant storage reservation does not match World admission.");
        }

        _sceneParticipant = participant;
    }

    private void InitializeSystemInstances()
    {
        var systems = Registration.Systems;

        for (var index = 0; index < systems.Length; index++)
        {
            var registration = systems[index];
            var instance = registration.Factory.CreateControl.Invoke();

            if (instance.GetType() != registration.Factory.Descriptor.SystemType)
            {
                throw new InvalidOperationException("The system creation callback returned an instance with the wrong exact type.");
            }

            if (!WorldSystemInstanceRegistry.TryClaim(instance, _systemInstanceClaim))
            {
                throw new InvalidOperationException("The system creation callback returned an instance already owned by a World.");
            }

            _systemInstances[index] = instance;
            registration.Factory.DefaultsControl.Invoke(instance);
            registration.Factory.ValidationControl.Validate(instance, registration.Description.SystemId);
        }
    }

    private void InstallArchetype(WorldArchetype archetype)
    {
        if ((archetype.Index != _archetypeCount) || (_archetypeCount >= _archetypes.Length))
        {
            throw new InvalidOperationException("The prepared archetype index is not the next World directory slot.");
        }

        _archetypes[_archetypeCount] = archetype;
        var insertionIndex = _archetypeCount;

        while ((insertionIndex > 0) && (_queryArchetypes[insertionIndex - 1]!.CompareKey(archetype) > 0))
        {
            _queryArchetypes[insertionIndex] = _queryArchetypes[insertionIndex - 1];
            insertionIndex--;
        }

        _queryArchetypes[insertionIndex] = archetype;
        _archetypeCount++;
    }

    [SuppressMessage("Usage", "CA1031:Do not catch general exception types", Justification = "Every claimed system must receive its one safe Release attempt even when one provider throws.")]
    private WorldSystemCleanupReport ReleaseSystemInstances(
        WorldSystemCleanupOperation operation,
        Exception? constructionPrimaryException)
    {
        if (Interlocked.CompareExchange(ref _isSystemCleanupActive, 1, 0) != 0)
        {
            throw new InvalidOperationException("World system cleanup is already active.");
        }

        var systems = Registration.Systems;
        _systemCleanupPreparation.Begin(operation, constructionPrimaryException);

        try
        {
            for (var index = 0; index < systems.Length; index++)
            {
                var instance = _systemInstances[index];

                if (instance is null)
                {
                    continue;
                }

                var releaseAttemptIndex = _systemCleanupPreparation.RecordReleaseAttempt();

                try
                {
                    _systemCleanupStickyFailure = null;
                    systems[index].Factory.ReleaseControl.Invoke(instance);

                    if (_systemCleanupStickyFailure is { } stickyFailure)
                    {
                        throw stickyFailure;
                    }

                    if (!WorldSystemInstanceRegistry.IsClaimedBy(instance, _systemInstanceClaim))
                    {
                        throw new InvalidOperationException("The World system instance claim could not be verified.");
                    }
                }
                catch (Exception exception)
                {
                    WorldSystemInstanceRegistry.Quarantine(
                        instance,
                        _systemInstanceClaim,
                        _systemQuarantineReservation,
                        _domainReservation);
                    var registration = systems[index];
                    _systemCleanupPreparation.RecordFailure(
                        registration.Description.SystemId,
                        registration.Factory.Descriptor.FactoryId,
                        registration.Factory.Descriptor.FactoryVersion,
                        releaseAttemptIndex,
                        exception);
                }
                finally
                {
                    _systemInstances[index] = null;
                }
            }

            _systemInstances = [];
            _systemCleanupPreparation.Seal();
            return _systemCleanupPreparation.Report;
        }
        finally
        {
            _ = Interlocked.Exchange(ref _isSystemCleanupActive, 0);
        }
    }

    private void ThrowIfDisposed()
    {
        ThrowIfAttachmentCallbackActive();
        ThrowIfSystemInvocationActive();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    private void ThrowIfFaulted()
    {
        if (Volatile.Read(ref _faulted) != 0)
        {
            throw new InvalidOperationException(
                _lastFault is { } fault
                    ? $"The World is faulted: {fault.Code}."
                    : "The World is faulted and cannot be used.");
        }
    }

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Installation transfers the receiver to its caller; an uninstalled receiver is ended atomically with its exact original construction claim, not through its installed-only Dispose path.")]
    private WorldSceneStorageReservation? TryReserveSceneReservation(
        IWorldSceneParticipant participant,
        long dataBytes,
        WorldGuidUseControl? useControl,
        bool workspace,
        bool restoredCapacity)
    {
        ArgumentNullException.ThrowIfNull(participant);
        var bytes = WorldSceneStorageReservation.GetRequiredBytes(dataBytes);
        var domain = _admissionDomain;
        var parent = _domainReservation;

        lock (_sceneStorageGate)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return null;
            }

            if (!ReferenceEquals(_sceneParticipant, participant))
            {
                throw new ArgumentException("The Scene participant is not owned by this exact World.", nameof(participant));
            }

            if (!domain.TryClaimSceneReservationConstruction(parent, useControl, bytes, restoredCapacity))
            {
                return null;
            }
        }

        WorldSceneStorageReservation? reservation = null;
        var installed = false;

        try
        {
            reservation = new WorldSceneStorageReservation(parent, bytes, useControl);
            installed = domain.TryCompleteSceneReservationConstruction(parent, reservation, workspace);
            return installed ? reservation : null;
        }
        finally
        {
            if (!installed)
            {
                domain.CancelSceneReservationConstruction(parent, reservation, useControl, bytes, restoredCapacity);
            }
        }
    }

    private void ValidateEntityDescription(EntityDescription description)
    {
        if (description.PersistentId is { } persistentId && IsReservedUuidV8(persistentId))
        {
            throw new ArgumentException("An explicit Entity identity cannot use the engine-reserved RFC UUIDv8 domain.", nameof(description));
        }

        var rowBytes = 12L;

        foreach (var initialization in description.Components)
        {
            var registered = false;

            foreach (var registration in Registration.Components)
            {
                if (registration.ComponentType == initialization.ComponentType)
                {
                    rowBytes = checked(rowBytes + registration.Layout.LogicalBytes);
                    registered = true;
                    break;
                }
            }

            if (!registered)
            {
                throw new ArgumentException("The EntityDescription contains an unregistered component type.", nameof(description));
            }
        }

        foreach (var component in Registration.Components)
        {
            if (!ContainsComponentInitialization(description, component.ComponentType))
            {
                continue;
            }

            foreach (var dependency in component.Descriptor.DerivedDependencies)
            {
                var registeredDependency = false;

                foreach (var registration in Registration.Components)
                {
                    if ((registration.ComponentType == dependency.ComponentType)
                        && (registration.SchemaReference == dependency.Schema))
                    {
                        registeredDependency = ContainsComponentInitialization(description, registration.ComponentType);
                        break;
                    }
                }

                if (!registeredDependency)
                {
                    throw new ArgumentException("The EntityDescription omits a registered Derived dependency.", nameof(description));
                }
            }
        }

        if (rowBytes > 32768)
        {
            throw new ArgumentOutOfRangeException(nameof(description), "The EntityDescription exceeds the logical archetype row limit.");
        }

        foreach (var tagId in description.Tags)
        {
            var registered = false;

            foreach (var tag in Registration.Tags)
            {
                if (tag.SchemaId == tagId)
                {
                    registered = true;
                    break;
                }
            }

            if (!registered)
            {
                throw new ArgumentException("The EntityDescription contains an unregistered tag schema identity.", nameof(description));
            }
        }
    }

    private void ValidateNoEntityReferencesTo(Entity destroyedEntity)
    {
        for (var archetypeIndex = 0; archetypeIndex < _archetypeCount; archetypeIndex++)
        {
            var archetype = _archetypes[archetypeIndex]
                ?? throw new InvalidOperationException("The World archetype directory contains an empty active slot.");

            for (var chunkIndex = 0; chunkIndex < archetype.Storage.ChunkCount; chunkIndex++)
            {
                if (!archetype.Storage.TryGetChunkOrdinal(chunkIndex, out _))
                {
                    continue;
                }

                var entities = archetype.Storage.GetEntities(chunkIndex);

                for (var row = 0; row < entities.Length; row++)
                {
                    if (entities[row] == destroyedEntity)
                    {
                        continue;
                    }

                    for (var componentIndex = 0; componentIndex < archetype.Components.Length; componentIndex++)
                    {
                        archetype.Storage.ValidateEntityReferencesColumnRow(
                            componentIndex,
                            chunkIndex,
                            row,
                            this,
                            destroyedEntity,
                            parameterName: null);
                    }
                }
            }

            var destroyedOwnerId = _identityLedger.GetAssignedId(IdentityKind.Entity, destroyedEntity.Index);

            if (destroyedOwnerId == Guid.Empty)
            {
                throw new InvalidOperationException("The live Entity has no stable attachment owner identity.");
            }

            ValidateAttachmentEntityReferences(destroyedEntity, destroyedOwnerId);
        }
    }
}
