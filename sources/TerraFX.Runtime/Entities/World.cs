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
    private const long FreshWorldControlBytes = 4096;

    private HashSet<Entity>? _activeCommandExcludedEntityHandles;
    private HashSet<Entity>? _activeCommandProjectedEntityHandles;
    private readonly long[] _activeQueryEpochs;
    private readonly int[] _activeQueryWriteGrants;
    private int _activeSystemInvocationCount;
    private readonly WorldSystemInvocationDispatch?[] _activeSystemInvocations;
    private readonly WorldAdmissionDomain _admissionDomain;
    private int _archetypeCount;
    private WorldArchetype?[] _archetypes;
    private Exception? _attachmentCallbackStickyFailure;
    private readonly WorldAttachmentTable _attachments = new();
    private long _canonicalMutationRevision;
    private long _committedMutationRevision;
    private CommittedWorldPublication? _committedPublication;
    private WorldChunkWorkPlanEntry[] _componentTagWorkPlan = [];
    private long _componentTagWorkPlanByteReservation;
    private int _componentTagWorkPlanCount;
    private long _componentTagWorkPlanEpoch;
    private int _controlRecordCount;
    private WorldControlRecord[] _controlRecords = [];
    private long _controlStorageByteReservation;
    private WorldFrameCohort _currentFrameCohort;
    private int _currentIdentityBranchIndex;
    private readonly WorldDiagnosticsCapability _diagnosticsCapability;
    private int _disposed;
    private readonly WorldDomainReservation _domainReservation;
    private readonly EntitySlotTable _entitySlots;
    private readonly Guid _executionSessionId;
    private int _faulted;
    private int _identityBranchCount;
    private Guid _identityBranchId;
    private WorldIdentityBranchRecord[] _identityBranches = [];
    private readonly StableIdentityLedger _identityLedger;
    private readonly float _initialTimeScale;
    private int _isAttachmentCallbackActive;
    private int _isStructuralOperationActive;
    private int _isSystemCleanupActive;
    private int _isTickActive;
    private Diagnostic? _lastFault;
    private WorldSystemCleanupReport? _lastSystemCleanupReport;
    private readonly WorldLineageGrant _lineageGrant;
    private readonly int _maximumArchetypes;
    private readonly long _maximumCommandStagingBytes;
    private readonly int _maximumDeferredCommands;
    private readonly int _maximumIdentityBranches;
    private readonly int _maximumLiveEntities;
    private readonly long _maximumOwnedStateBytes;
    private readonly int _maximumRecordedControls;
    private readonly long _maximumWorkPlanBytes;
    private ulong _nextAttachmentInstallationId;
    private long _nextChunkOrdinal;
    private long _nextControlSequence = 1;
    private long _nextQueryEpoch;
    private long _nextSystemInvocationEpoch;
    private long _nextTickIndex;
    private readonly uint _ownerId;
    private readonly int _ownerThreadId;
    private readonly bool _ownsAdmissionDomain;
    private long _publicationSequence;
    private WorldArchetype?[] _queryArchetypes;
    private readonly WorldGuidRegistry _registry;
    private readonly Guid _replayLineageId;
    private ImmutableArray<object> _resourceStorage;
    private readonly long _resourceStorageByteReservation;
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
    private readonly WorldGuidGrant _worldGuidGrant;

    private World(
        WorldConstructionPlan constructionPlan,
        WorldSystemQuarantineReservation systemQuarantineReservation,
        WorldWorkerPool workerPool)
    {
        ArgumentNullException.ThrowIfNull(constructionPlan);
        ArgumentNullException.ThrowIfNull(systemQuarantineReservation);
        ArgumentNullException.ThrowIfNull(workerPool);
        constructionPlan.ValidateForConstruction();

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
        _entitySlots = new EntitySlotTable(options.MaximumEntitySlots);
        _executionSessionId = constructionPlan.ExecutionSessionId;
        _identityBranchId = constructionPlan.IdentityBranchId;
        _identityLedger = constructionPlan.IdentityLedger;
        _lineageGrant = constructionPlan.LineageGrant;
        _maximumArchetypes = options.MaximumArchetypes;
        _maximumIdentityBranches = options.MaximumIdentityBranches;
        _maximumCommandStagingBytes = options.SchedulingLimits.MaximumCommandStagingBytes;
        _maximumDeferredCommands = options.MaximumDeferredCommands;
        _maximumLiveEntities = options.MaximumLiveEntities;
        _maximumOwnedStateBytes = options.SchedulingLimits.MaximumOwnedStateBytes;
        _maximumWorkPlanBytes = options.SchedulingLimits.MaximumWorkPlanBytes;
        _componentTagWorkPlanByteReservation = 0;
        _maximumRecordedControls = options.MaximumRecordedControls;
        _ownsAdmissionDomain = constructionPlan.OwnsAdmissionDomain;
        _registry = constructionPlan.Registry;
        _replayLineageId = constructionPlan.ReplayLineageId;
        _spatialProfileId = options.SpatialProfileId;
        _spatialSettings = options.SpatialSettings;
        _initialTimeScale = options.InitialTimeScale;
        _tickRate = options.TickRate;
        _timeScale = options.InitialTimeScale;
        _worldGuidGrant = constructionPlan.WorldGuidGrant;
        _worldGuid = constructionPlan.WorldGuid;
        _systemCleanupPreparation = new WorldSystemCleanupPreparation(_worldGuid, systems.Length);
        _systemQuarantineReservation = systemQuarantineReservation;
        _workerPool = workerPool;
        Registration = options.Registration;
        Profile = options.Profile;
        _diagnosticsCapability = constructionPlan.DiagnosticsCapability;
        _identityBranches =
        [
            new WorldIdentityBranchRecord(
                constructionPlan.IdentityBranchId,
                _worldGuid,
                _replayLineageId,
                parentBranchId: null,
                createdAtNextTick: 0,
                firstAssignmentOrdinal: 1,
                directoryIndex: 0),
        ];
        _identityBranchCount = 1;

        _ownerId = OwnerIdAllocator.Allocate();
        _ownerThreadId = Environment.CurrentManagedThreadId;
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

        _currentFrameCohort = new WorldFrameCohort(currentFrame3, currentFrame2, cohortGeneration: 1);
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

    internal Guid ExecutionSessionId => _executionSessionId;

    internal StableIdentityLedger IdentityLedger => _identityLedger;

    internal uint OwnerId => _ownerId;

    /// <summary>Creates a standalone World from a frozen registration and validated options.</summary>
    /// <param name="options">The construction options and immutable registration.</param>
    /// <returns>The admitted standalone World.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">An identity, registration, spatial profile, or extent is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A capacity, tick rate, time scale, or worker count is invalid.</exception>
    /// <exception cref="InvalidOperationException">A WorldGuid/domain/lineage admission is refused or owner issuance is exhausted.</exception>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The reserved identity, domain charge, and returned World transfer together only after both admission commits; the finally path releases them on failure.")]
    public static World Create(WorldOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var resolvedOptions = options.ValidateAndResolve();
        var worldGuid = options.WorldGuid ?? Guid.NewGuid();

        if (worldGuid == Guid.Empty)
        {
            throw new InvalidOperationException("The generated WorldGuid cannot be empty.");
        }

        WorldGuidRegistry? registry = null;
        WorldGuidGrant worldGuidGrant = default;
        WorldAdmissionDomain? admissionDomain = null;
        var ownsAdmissionDomain = false;
        WorldDomainReservation? domainReservation = null;
        WorldSystemQuarantineReservation? systemQuarantineReservation = null;
        WorldWorkerPool? workerPool = null;
        WorldLineageGrant lineageGrant = default;
        DiagnosticsService? diagnostics = null;
        WorldDiagnosticsCapability? diagnosticsCapability = null;
        var lineageCommitted = false;
        var publicationCommitted = false;

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

            admissionDomain = options.AdmissionDomain ?? new WorldAdmissionDomain(DefaultAdmissionDomainBytes);
            ownsAdmissionDomain = options.AdmissionDomain is null;

            var maximumDiagnosticBytes = options.SchedulingLimits.MaximumDiagnosticBytes;
            var diagnosticRecordCount = DiagnosticsService.GetMaximumRecordCount(maximumDiagnosticBytes);
            var diagnosticBytes = DiagnosticsService.GetOwnedByteReservation(diagnosticRecordCount);
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

            if (resourceAndSystemStateBytes > options.SchedulingLimits.MaximumResourceAndSystemStateBytes)
            {
                throw new InvalidOperationException("The selected resource partitions and system instances exceed the resource and system state budget.");
            }

            var worldOwnedBytes = checked(
                FreshWorldControlBytes
                + archetypeDirectoryBytes
                + diagnosticBytes
                + resourceAndSystemStateBytes
                + CalculateSubscriptionStorageByteReservation());

            if (!admissionDomain.TryReserveWorld(worldOwnedBytes, out domainReservation)
                || (domainReservation is null))
            {
                throw new InvalidOperationException("The admission domain cannot reserve the standalone World and diagnostic service.");
            }

            diagnostics = new DiagnosticsService(diagnosticRecordCount);
            var executionSessionId = ExecutionSession.ExecutionSessionId;
            diagnosticsCapability = diagnostics.CreateWorldCapability(worldGuid, executionSessionId);
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
                out var lineageRefusal))
            {
                if (lineageRefusal == WorldAdmissionRefusal.LineageAlreadyRetained
                    && options.InitialReplayLineageId.HasValue)
                {
                    throw new ArgumentException("The replay lineage is already registered in this admission domain.", nameof(options));
                }

                throw new InvalidOperationException($"Replay-lineage admission was refused with code {lineageRefusal}.");
            }

            var identityLedger = StableIdentityLedger.CreateForWorld(
                options.MaximumIdentityAssignments,
                options.MaximumEntitySlots,
                options.MaximumActiveSceneInstances);
            var constructionPlan = new WorldConstructionPlan(
                resolvedOptions,
                worldGuid,
                replayLineageId,
                identityBranchId,
                executionSessionId,
                admissionDomain,
                ownsAdmissionDomain,
                domainReservation,
                lineageGrant,
                registry,
                worldGuidGrant,
                diagnosticsCapability,
                identityLedger);
            systemQuarantineReservation = WorldSystemInstanceRegistry.ReserveQuarantineCapacity(options.Registration.Systems.Length);
            workerPool = new WorldWorkerPool(resolvedOptions.CpuWorkerCount);
            var world = new World(constructionPlan, systemQuarantineReservation, workerPool);

            try
            {
                world.InitializeSystemInstances();

                for (var index = 0; index < options.Registration.Resources.Length; index++)
                {
                    options.Registration.Resources[index].InitializeAndValidate(world._resourceStorage[index]);
                }

                if (!admissionDomain.TryCommitFreshLineage(lineageGrant))
                {
                    throw new InvalidOperationException("The fresh replay-lineage admission could not be committed.");
                }

                lineageCommitted = true;

                if (!registry.TryCommitFresh(worldGuidGrant))
                {
                    throw new InvalidOperationException("The reserved WorldGuid binding could not be committed.");
                }

                publicationCommitted = true;
                return world;
            }
            catch (Exception exception)
            {
                var cleanupReport = world.ReleaseSystemInstances(
                    WorldSystemCleanupOperation.ConstructionUnwind,
                    exception);

                if (cleanupReport.FailureCount != 0)
                {
                    throw world._systemCleanupPreparation.CreateCleanupException();
                }

                throw;
            }
        }
        finally
        {
            if (!publicationCommitted)
            {
                workerPool?.Dispose();

                if (diagnosticsCapability is not null)
                {
                    diagnosticsCapability.Close();
                }
                else
                {
                    diagnostics?.Close();
                }

                if (lineageGrant.IsValid && (admissionDomain is not null))
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

                if (systemQuarantineReservation?.RetainsDomainReservation != true)
                {
                    domainReservation?.Dispose();
                }

                if ((registry is not null) && worldGuidGrant.IsValid)
                {
                    _ = registry.TryReleaseFresh(worldGuidGrant);
                }

                if (ownsAdmissionDomain)
                {
                    admissionDomain?.Dispose();
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
                    initializations[componentIndex] = registration.CreateInitializedValue(initialization);
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
                    archetype.Storage.InitializeColumnRow(componentIndex, chunkIndex, row, initializations[componentIndex]);
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

                var entity = new Entity(_ownerId, slotIndex, generation);
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

        EnsureOwnerThread();

        if (Interlocked.CompareExchange(ref _isStructuralOperationActive, 1, 0) != 0)
        {
            throw new InvalidOperationException("The World cannot be disposed during a structural operation.");
        }

        WorldAttachmentQuarantineReservation? attachmentQuarantineReservation = null;
        Exception? attachmentCleanupException = null;

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

            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            if (!_registry.TryReleaseFresh(_worldGuidGrant))
            {
                _ = Interlocked.Exchange(ref _disposed, 0);
                throw new InvalidOperationException("The WorldGuid binding could not be retired; the World remains active.");
            }

            _workerPool.Dispose();
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

            var lineageRetired = false;

            try
            {
                lineageRetired = _admissionDomain.TryRollbackFreshLineage(_lineageGrant);
            }
            finally
            {
                _diagnosticsCapability.Close();
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
                _identityBranches = [];
                _identityBranchCount = 0;
                _resourceStorage = [];
                _archetypeCount = 0;
                _ = _attachments.ReleaseOwnedStorage();

                var attachmentQuarantineRetainsReservation = WorldAttachmentInstanceRegistry.HasRetainedDomainReservation(_domainReservation);

                if (!_systemQuarantineReservation.RetainsDomainReservation && !attachmentQuarantineRetainsReservation)
                {
                    _domainReservation.Dispose();
                }

                if (_ownsAdmissionDomain && !attachmentQuarantineRetainsReservation)
                {
                    _admissionDomain.Dispose();
                }
            }

            if (!lineageRetired)
            {
                var lineageFailure = new InvalidOperationException("The World replay-lineage root could not be retired; the World is already closed.");
                var cleanupFailures = new List<Exception>(3) { lineageFailure };

                if (systemCleanupReport.FailureCount != 0)
                {
                    var cleanupException = _systemCleanupPreparation.CreateCleanupException();
                    cleanupFailures.Add(cleanupException);
                }

                if (attachmentCleanupException is not null)
                {
                    cleanupFailures.Add(attachmentCleanupException);
                }

                throw (cleanupFailures.Count == 1) ? lineageFailure : new AggregateException(cleanupFailures);
            }

            if (attachmentCleanupException is not null)
            {
                throw attachmentCleanupException;
            }

            if (systemCleanupReport.FailureCount != 0)
            {
                throw _systemCleanupPreparation.CreateCleanupException();
            }
        }
        finally
        {
            attachmentQuarantineReservation?.Release();
            _ = Interlocked.Exchange(ref _isStructuralOperationActive, 0);
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

        entity = new Entity(_ownerId, locator, generation);
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

        if (Volatile.Read(ref _isStructuralOperationActive) != 0)
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
        _diagnosticsCapability.Write(fault);
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

    private long GetWorldOwnedStateBytes()
    {
        var bytes = checked(
            _identityLedger.OwnedByteReservation
            + _entitySlots.OwnedByteReservation
            + _attachments.OwnedByteReservation
            + _resourceStorageByteReservation
            + _systemInstanceStorageByteReservation
            + _systemInvocationStorageByteReservation
            + _componentTagWorkPlanByteReservation
            + _controlStorageByteReservation
            + _subscriptionStorageByteReservation);

        for (var index = 0; index < _archetypeCount; index++)
        {
            var archetype = _archetypes[index]
                ?? throw new InvalidOperationException("The World archetype directory contains an empty active slot.");
            bytes = checked(bytes + archetype.MetadataByteReservation + archetype.Storage.OwnedByteReservation);
        }

        return bytes;
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
            _systemQuarantineReservation.Release();
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
