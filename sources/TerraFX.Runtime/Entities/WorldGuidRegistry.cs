// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace TerraFX.Entities;

internal sealed class WorldGuidRegistry
{
    internal const int MaximumBindingCells = 65_536;
    internal const int MaximumChargedBytes = 67_108_864;
    internal const int MaximumClaimsPerBinding = 16;
    internal const int MaximumClaimsPerKey = 16;
    internal const int MaximumPendingSlabAllocations = 16;
    internal const int MaximumPreparationCells = 32_768;
    internal const int MaximumUseCells = 131_072;
    internal const int MaximumWorldGuidKeys = 16_384;

    private const int BindingDirectoryLength = 256;
    private const int CellStateActive = 2;
    private const int CellStateFree = 0;
    private const int CellStateReserved = 1;
    private const int CellStateRetired = 3;
    private const int CellsPerSlab = 256;
    private const int ColdReservationByteCount = 1_060_672;
    private const int FailureCategoryInvariant = 1;
    private const int FailureCategoryOutOfMemory = 2;
    private const int FailureCategoryOverflow = 3;
    private const int FailureCategoryUnexpected = 4;
    private const int IndexCapacity = 32_768;
    private const int InitializationStageBindingDirectory = 5;
    private const int InitializationStageColdReservation = 1;
    private const int InitializationStageIndex = 3;
    private const int InitializationStageKeyDirectory = 4;
    private const int InitializationStagePreparationDirectory = 6;
    private const int InitializationStageReadyPublication = 8;
    private const int InitializationStageRecursiveAccess = 9;
    private const int InitializationStageRegistryControl = 2;
    private const int InitializationStageUseDirectory = 7;
    private const int KeyDirectoryLength = 64;
    private const int PreparationDirectoryLength = 128;
    private const int SlabCategoryBinding = 1;
    private const int SlabCategoryKey = 0;
    private const int SlabCategoryPreparation = 2;
    private const int SlabCategoryUse = 3;
    private const int SlabTicketPending = 1;
    private const int SlabTicketRetired = 2;
    private const int StateFailed = 3;
    private const int StateInitializing = 1;
    private const int StateReady = 2;
    private const int StateUninitialized = 0;
    private const int UseDirectoryLength = 512;
    private const int UseRoleCoordinator = 2;
    private const int UseRoleOwner = 1;

    private static long s_chargedBytes;
    private static int s_failureCategory;
    private static int s_failureStage;
    private static int s_initializationStage;
    private static int s_initializationState;
    private static int s_initializingThreadId;
    private static WorldGuidRegistry? s_partialRegistry;
    private static WorldGuidRegistry? s_selectedRegistry;

    private ReentrantGate _admissionGate;
    private BindingCell[]?[]? _bindingDirectory;
    private int _bindingFreeCellCount;
    private int _bindingFreeCellHead;
    private int _bindingNextCellIndex;
    private int _bindingPageCount;
    private int _bindingRoleCount;
    private GrowthTicketBuffer _growthTickets;
    private IndexBucket[]? _index;
    private int _indexEntryCount;
    private KeyCell[]?[]? _keyDirectory;
    private int _keyFreeCellCount;
    private int _keyFreeCellHead;
    private int _keyNextCellIndex;
    private int _keyPageCount;
    private int _pendingGrowthTicketCount;
    private PreparationCell[]?[]? _preparationDirectory;
    private int _preparationFreeCellCount;
    private int _preparationFreeCellHead;
    private int _preparationNextCellIndex;
    private int _preparationPageCount;
    private int _preparationRoleCount;
    private int _retainedKeyCount;
    private UseCell[]?[]? _useDirectory;
    private int _useFreeCellCount;
    private int _useFreeCellHead;
    private int _useNextCellIndex;
    private int _usePageCount;
    private int _useRoleCount;

    private WorldGuidRegistry()
    {
        _bindingFreeCellHead = -1;
        _keyFreeCellHead = -1;
        _preparationFreeCellHead = -1;
        _useFreeCellHead = -1;
    }

    internal static WorldGuidRegistry GetSelected()
    {
        WorldAdmissionParticipant.Claim(typeof(WorldGuidRegistry));

        var observedState = Interlocked.CompareExchange(ref s_initializationState, StateInitializing, StateUninitialized);

        if (observedState == StateUninitialized)
        {
            return InitializeSelected();
        }

        if (observedState == StateReady)
        {
            return GetPublishedRegistry();
        }

        if (observedState == StateFailed)
        {
            ThrowInitializationFailure();
        }

        return WaitForInitialization();
    }

    private static object AllocateSlabPage(int category)
        => category switch {
            SlabCategoryKey => new KeyCell[CellsPerSlab],
            SlabCategoryBinding => new BindingCell[CellsPerSlab],
            SlabCategoryPreparation => new PreparationCell[CellsPerSlab],
            SlabCategoryUse => new UseCell[CellsPerSlab],
            _ => throw new InvalidOperationException("The WorldGuid slab category is invalid."),
        };

    private static long ArrayCharge(int capacity, int stride)
    {
        var payload = checked((long)capacity * stride);
        var alignedPayload = checked((payload + 15L) & ~15L);
        return checked(64L + alignedPayload);
    }

    private static int GetCategoryCapacityRefusal(int category)
        => category switch {
            SlabCategoryKey => WorldGuidRefusal.KeyCapacity,
            SlabCategoryBinding => WorldGuidRefusal.BindingCapacity,
            SlabCategoryPreparation => WorldGuidRefusal.PreparationCapacity,
            SlabCategoryUse => WorldGuidRefusal.UseCapacity,
            _ => throw new InvalidOperationException("The WorldGuid slab category is invalid."),
        };

    private static long GetColdReservationByteCount()
        => checked(4096L
            + ArrayCharge(IndexCapacity, Unsafe.SizeOf<IndexBucket>())
            + ArrayCharge(KeyDirectoryLength, IntPtr.Size)
            + ArrayCharge(BindingDirectoryLength, IntPtr.Size)
            + ArrayCharge(PreparationDirectoryLength, IntPtr.Size)
            + ArrayCharge(UseDirectoryLength, IntPtr.Size));

    private static int GetFailureCategory(Exception exception)
        => exception switch {
            OutOfMemoryException => FailureCategoryOutOfMemory,
            OverflowException => FailureCategoryOverflow,
            InvalidOperationException => FailureCategoryInvariant,
            _ => FailureCategoryUnexpected,
        };

    private static WorldGuidRegistry GetPublishedRegistry()
    {
        var registry = Volatile.Read(ref s_selectedRegistry);

        if (registry is not null)
        {
            return registry;
        }

        throw new InvalidOperationException("The WorldGuid registry was published without an instance.");
    }

    private static long GetSlabCharge(int category)
        => category switch {
            SlabCategoryKey => ArrayCharge(CellsPerSlab, Unsafe.SizeOf<KeyCell>()),
            SlabCategoryBinding => ArrayCharge(CellsPerSlab, Unsafe.SizeOf<BindingCell>()),
            SlabCategoryPreparation => ArrayCharge(CellsPerSlab, Unsafe.SizeOf<PreparationCell>()),
            SlabCategoryUse => ArrayCharge(CellsPerSlab, Unsafe.SizeOf<UseCell>()),
            _ => throw new InvalidOperationException("The WorldGuid slab category is invalid."),
        };

    private static WorldGuidRegistry InitializeSelected()
    {
        Volatile.Write(ref s_initializingThreadId, Environment.CurrentManagedThreadId);
        Volatile.Write(ref s_initializationStage, InitializationStageColdReservation);
        Volatile.Write(ref s_chargedBytes, ColdReservationByteCount);

        try
        {
            Volatile.Write(ref s_initializationStage, InitializationStageRegistryControl);
            var registry = new WorldGuidRegistry();
            Volatile.Write(ref s_partialRegistry, registry);
            registry.InitializeColdBacking();

            Volatile.Write(ref s_initializationStage, InitializationStageReadyPublication);

            if (Volatile.Read(ref s_chargedBytes) != ColdReservationByteCount)
            {
                ThrowHelper.ThrowInvalidOperationException("The WorldGuid registry cold reservation is inconsistent.");
            }

            if (Volatile.Read(ref s_initializationState) != StateInitializing)
            {
                ThrowHelper.ThrowInvalidOperationException("The WorldGuid registry initialization was invalidated before publication.");
            }

            registry.ValidateColdBacking();
            Volatile.Write(ref s_selectedRegistry, registry);
            Volatile.Write(ref s_initializationState, StateReady);
            return registry;
        }
        catch (Exception exception)
        {
            if (Volatile.Read(ref s_initializationState) == StateInitializing)
            {
                Volatile.Write(ref s_failureStage, Volatile.Read(ref s_initializationStage));
                Volatile.Write(ref s_failureCategory, GetFailureCategory(exception));
                Volatile.Write(ref s_initializationState, StateFailed);
            }

            throw;
        }
        finally
        {
            Volatile.Write(ref s_initializingThreadId, 0);
        }
    }

    [DoesNotReturn]
    private static void ThrowInitializationFailure()
    {
        var stage = Volatile.Read(ref s_failureStage);
        var category = Volatile.Read(ref s_failureCategory);
        var partialRegistry = Volatile.Read(ref s_partialRegistry);

        if ((stage >= InitializationStageIndex) && (partialRegistry is null))
        {
            ThrowHelper.ThrowInvalidOperationException("The WorldGuid registry lost ownership of its failed partial backing.");
        }

        throw new InvalidOperationException(
            $"The WorldGuid registry initialization failed permanently (stage {stage}, category {category}).");
    }

    private static WorldGuidRegistry WaitForInitialization()
    {
        var spinner = new SpinWait();

        while (true)
        {
            var state = Volatile.Read(ref s_initializationState);

            if (state == StateReady)
            {
                return GetPublishedRegistry();
            }

            if (state == StateFailed)
            {
                ThrowInitializationFailure();
            }

            if (state != StateInitializing)
            {
                ThrowHelper.ThrowInvalidOperationException("The WorldGuid registry initialization state is invalid.");
            }

            if (Volatile.Read(ref s_initializingThreadId) == Environment.CurrentManagedThreadId)
            {
                Volatile.Write(ref s_failureStage, InitializationStageRecursiveAccess);
                Volatile.Write(ref s_failureCategory, FailureCategoryInvariant);
                Volatile.Write(ref s_initializationState, StateFailed);
                ThrowHelper.ThrowInvalidOperationException("WorldGuid registry initialization cannot recursively request the selected instance.");
            }

            spinner.SpinOnce();
        }
    }

    internal bool TryCommitFresh(in WorldGuidGrant grant)
    {
        if (!grant.IsOwnedBy(this))
        {
            return false;
        }

        using var scope = ReentrantGate.EnterScope(ref _admissionGate);

        if (!IsKeyCellValid(grant.KeyCellIndex, grant.KeyGeneration)
         || !IsBindingCellValid(grant.BindingCellIndex, grant.BindingGeneration)
         || !IsPreparationCellValid(grant.PreparationCellIndex, grant.PreparationGeneration)
         || !IsUseCellValid(grant.OwnerUseCellIndex, grant.OwnerUseGeneration)
         || !IsUseCellValid(grant.CoordinatorUseCellIndex, grant.CoordinatorUseGeneration))
        {
            return false;
        }

        ref var keyCell = ref GetKeyCell(grant.KeyCellIndex);
        ref var bindingCell = ref GetBindingCell(grant.BindingCellIndex);
        ref var preparationCell = ref GetPreparationCell(grant.PreparationCellIndex);
        ref var ownerUseCell = ref GetUseCell(grant.OwnerUseCellIndex);
        ref var coordinatorUseCell = ref GetUseCell(grant.CoordinatorUseCellIndex);

        if ((keyCell._worldGuid != grant.WorldGuid)
         || (bindingCell._state != CellStateReserved)
         || (preparationCell._state != CellStateReserved)
         || (ownerUseCell._state != CellStateReserved)
         || (coordinatorUseCell._state != CellStateReserved))
        {
            return false;
        }

        bindingCell._state = CellStateActive;
        ReleasePreparationCell(grant.PreparationCellIndex, ref preparationCell);
        ReleaseUseCell(grant.CoordinatorUseCellIndex, ref coordinatorUseCell);
        keyCell._roleCount -= 2;
        return true;
    }

    internal bool TryReleaseFresh(in WorldGuidGrant grant)
    {
        if (!grant.IsOwnedBy(this))
        {
            return false;
        }

        using var scope = ReentrantGate.EnterScope(ref _admissionGate);

        if (!IsKeyCellValid(grant.KeyCellIndex, grant.KeyGeneration)
         || !IsBindingCellValid(grant.BindingCellIndex, grant.BindingGeneration)
         || !IsUseCellValid(grant.OwnerUseCellIndex, grant.OwnerUseGeneration))
        {
            return false;
        }

        ref var keyCell = ref GetKeyCell(grant.KeyCellIndex);
        ref var bindingCell = ref GetBindingCell(grant.BindingCellIndex);
        ref var ownerUseCell = ref GetUseCell(grant.OwnerUseCellIndex);

        if ((keyCell._worldGuid != grant.WorldGuid)
         || (bindingCell._state is not (CellStateReserved or CellStateActive))
         || (ownerUseCell._state != CellStateReserved))
        {
            return false;
        }

        if (bindingCell._state == CellStateReserved)
        {
            if (!IsPreparationCellValid(grant.PreparationCellIndex, grant.PreparationGeneration)
             || !IsUseCellValid(grant.CoordinatorUseCellIndex, grant.CoordinatorUseGeneration))
            {
                return false;
            }

            ref var preparationCell = ref GetPreparationCell(grant.PreparationCellIndex);
            ref var coordinatorUseCell = ref GetUseCell(grant.CoordinatorUseCellIndex);

            if ((preparationCell._state != CellStateReserved)
             || (coordinatorUseCell._state != CellStateReserved))
            {
                return false;
            }

            ReleasePreparationCell(grant.PreparationCellIndex, ref preparationCell);
            ReleaseUseCell(grant.CoordinatorUseCellIndex, ref coordinatorUseCell);
            keyCell._roleCount -= 2;
        }

        ReleaseBindingCell(grant.BindingCellIndex, ref bindingCell);
        ReleaseUseCell(grant.OwnerUseCellIndex, ref ownerUseCell);
        keyCell._roleCount -= 2;
        ReleaseKeyIfUnowned(grant.KeyCellIndex, ref keyCell);
        return true;
    }

    internal bool TryReserveFresh(Guid worldGuid, out WorldGuidGrant grant, out WorldGuidRefusal refusal)
    {
        if (worldGuid == Guid.Empty)
        {
            throw new ArgumentException("A WorldGuid cannot be empty.", nameof(worldGuid));
        }

        using (ReentrantGate.EnterScope(ref _admissionGate))
        {
            if (!CanAdmitFresh(worldGuid, out refusal))
            {
                grant = default;
                return false;
            }
        }

        if (!EnsureCellAvailability(SlabCategoryKey, 1, out refusal)
         || !EnsureCellAvailability(SlabCategoryBinding, 1, out refusal)
         || !EnsureCellAvailability(SlabCategoryPreparation, 1, out refusal)
         || !EnsureCellAvailability(SlabCategoryUse, 2, out refusal))
        {
            grant = default;
            return false;
        }

        using (ReentrantGate.EnterScope(ref _admissionGate))
        {
            if (!CanAdmitFresh(worldGuid, out refusal))
            {
                grant = default;
                return false;
            }

            var keyCellIndex = AllocateKeyCell(worldGuid, out var keyGeneration);
            var bindingCellIndex = AllocateBindingCell(keyCellIndex, keyGeneration, out var bindingGeneration);
            var preparationCellIndex = AllocatePreparationCell(keyCellIndex, keyGeneration, bindingCellIndex, bindingGeneration, out var preparationGeneration);
            var ownerUseCellIndex = AllocateUseCell(keyCellIndex, keyGeneration, bindingCellIndex, bindingGeneration, UseRoleOwner, out var ownerUseGeneration);
            var coordinatorUseCellIndex = AllocateUseCell(keyCellIndex, keyGeneration, bindingCellIndex, bindingGeneration, UseRoleCoordinator, out var coordinatorUseGeneration);
            InsertIndexEntry(worldGuid, keyCellIndex, keyGeneration);
            _retainedKeyCount++;

            grant = new WorldGuidGrant(
                this,
                worldGuid,
                keyCellIndex,
                keyGeneration,
                bindingCellIndex,
                bindingGeneration,
                preparationCellIndex,
                preparationGeneration,
                ownerUseCellIndex,
                ownerUseGeneration,
                coordinatorUseCellIndex,
                coordinatorUseGeneration);

            refusal = default;
            return true;
        }
    }

    private void AbandonSlabGrant(in WorldGuidSlabGrant grant)
    {
        using var scope = ReentrantGate.EnterScope(ref _admissionGate);
        ref var ticket = ref _growthTickets[grant._ticketIndex];

        if ((ticket._state != SlabTicketPending)
         || (ticket._generation != grant._ticketGeneration)
         || (ticket._category != grant._category)
         || (ticket._pageOrdinal != grant._pageOrdinal))
        {
            throw new InvalidOperationException("The WorldGuid slab ticket is no longer pending.");
        }

        Volatile.Write(ref s_chargedBytes, checked(s_chargedBytes - ticket._chargedBytes));
        ticket._chargedBytes = 0;
        ticket._state = ticket._generation == uint.MaxValue ? SlabTicketRetired : CellStateFree;
        _pendingGrowthTicketCount--;
    }

    private int AllocateBindingCell(int keyCellIndex, uint keyGeneration, out uint generation)
    {
        int index;

        if (_bindingFreeCellHead >= 0)
        {
            index = _bindingFreeCellHead;
            ref var freeCell = ref GetBindingCell(index);
            _bindingFreeCellHead = freeCell._freeNextIndex;
            _bindingFreeCellCount--;
            generation = freeCell._generation;
        }
        else
        {
            index = _bindingNextCellIndex++;
            ref var newCell = ref GetBindingCell(index);
            generation = newCell._generation == 0 ? 1U : newCell._generation;
        }

        ref var cell = ref GetBindingCell(index);
        cell._generation = generation;
        cell._keyCellIndex = keyCellIndex;
        cell._keyGeneration = keyGeneration;
        cell._state = CellStateReserved;
        cell._freeNextIndex = -1;
        _bindingRoleCount++;
        return index;
    }

    private int AllocateKeyCell(Guid worldGuid, out uint generation)
    {
        int index;

        if (_keyFreeCellHead >= 0)
        {
            index = _keyFreeCellHead;
            ref var freeCell = ref GetKeyCell(index);
            _keyFreeCellHead = freeCell._freeNextIndex;
            _keyFreeCellCount--;
            generation = freeCell._generation;
        }
        else
        {
            index = _keyNextCellIndex++;
            ref var newCell = ref GetKeyCell(index);
            generation = newCell._generation == 0 ? 1U : newCell._generation;
        }

        ref var cell = ref GetKeyCell(index);
        cell._worldGuid = worldGuid;
        cell._generation = generation;
        cell._roleCount = 4;
        cell._claimCount = 1;
        cell._freeNextIndex = -1;
        cell._state = CellStateReserved;
        return index;
    }

    private int AllocatePreparationCell(int keyCellIndex, uint keyGeneration, int bindingCellIndex, uint bindingGeneration, out uint generation)
    {
        int index;

        if (_preparationFreeCellHead >= 0)
        {
            index = _preparationFreeCellHead;
            ref var freeCell = ref GetPreparationCell(index);
            _preparationFreeCellHead = freeCell._freeNextIndex;
            _preparationFreeCellCount--;
            generation = freeCell._generation;
        }
        else
        {
            index = _preparationNextCellIndex++;
            ref var newCell = ref GetPreparationCell(index);
            generation = newCell._generation == 0 ? 1U : newCell._generation;
        }

        ref var cell = ref GetPreparationCell(index);
        cell._generation = generation;
        cell._keyCellIndex = keyCellIndex;
        cell._keyGeneration = keyGeneration;
        cell._bindingCellIndex = bindingCellIndex;
        cell._bindingGeneration = bindingGeneration;
        cell._state = CellStateReserved;
        cell._freeNextIndex = -1;
        _preparationRoleCount++;
        return index;
    }

    private int AllocateUseCell(int keyCellIndex, uint keyGeneration, int bindingCellIndex, uint bindingGeneration, int role, out uint generation)
    {
        int index;

        if (_useFreeCellHead >= 0)
        {
            index = _useFreeCellHead;
            ref var freeCell = ref GetUseCell(index);
            _useFreeCellHead = freeCell._freeNextIndex;
            _useFreeCellCount--;
            generation = freeCell._generation;
        }
        else
        {
            index = _useNextCellIndex++;
            ref var newCell = ref GetUseCell(index);
            generation = newCell._generation == 0 ? 1U : newCell._generation;
        }

        ref var cell = ref GetUseCell(index);
        cell._generation = generation;
        cell._keyCellIndex = keyCellIndex;
        cell._keyGeneration = keyGeneration;
        cell._bindingCellIndex = bindingCellIndex;
        cell._bindingGeneration = bindingGeneration;
        cell._role = role;
        cell._state = CellStateReserved;
        cell._freeNextIndex = -1;
        _useRoleCount++;
        return index;
    }

    private bool CanAdmitFresh(Guid worldGuid, out WorldGuidRefusal refusal)
    {
        if (FindIndexBucket(worldGuid) >= 0)
        {
            refusal = new WorldGuidRefusal(WorldGuidRefusal.GuidAlreadyRetained);
            return false;
        }

        if ((_retainedKeyCount >= MaximumWorldGuidKeys) || (_indexEntryCount >= MaximumWorldGuidKeys))
        {
            refusal = new WorldGuidRefusal(WorldGuidRefusal.KeyCapacity);
            return false;
        }

        if (_bindingRoleCount >= MaximumBindingCells)
        {
            refusal = new WorldGuidRefusal(WorldGuidRefusal.BindingCapacity);
            return false;
        }

        if (_preparationRoleCount >= MaximumPreparationCells)
        {
            refusal = new WorldGuidRefusal(WorldGuidRefusal.PreparationCapacity);
            return false;
        }

        if (_useRoleCount > (MaximumUseCells - 2))
        {
            refusal = new WorldGuidRefusal(WorldGuidRefusal.UseCapacity);
            return false;
        }

        refusal = default;
        return true;
    }

    private void CompleteSlabGrant(in WorldGuidSlabGrant grant, object page)
    {
        using var scope = ReentrantGate.EnterScope(ref _admissionGate);

        if (!grant.IsValid || !ReferenceEquals(grant._registry, this))
        {
            throw new InvalidOperationException("The WorldGuid slab grant is invalid.");
        }

        ref var ticket = ref _growthTickets[grant._ticketIndex];

        if ((ticket._state != SlabTicketPending)
         || (ticket._generation != grant._ticketGeneration)
         || (ticket._category != grant._category)
         || (ticket._pageOrdinal != grant._pageOrdinal)
         || (ticket._chargedBytes != grant._chargedBytes))
        {
            throw new InvalidOperationException("The WorldGuid slab grant no longer owns its ticket.");
        }

        switch (grant._category)
        {
            case SlabCategoryKey:
            {
                var directory = _keyDirectory ?? throw new InvalidOperationException("The WorldGuid key directory is unavailable.");
                var slab = page as KeyCell[] ?? throw new InvalidOperationException("The WorldGuid key slab has the wrong type.");

                if ((grant._pageOrdinal != _keyPageCount) || (directory[grant._pageOrdinal] is not null))
                {
                    throw new InvalidOperationException("The WorldGuid key slab ordinal is invalid.");
                }

                directory[grant._pageOrdinal] = slab;
                _keyPageCount++;
                break;
            }

            case SlabCategoryBinding:
            {
                var directory = _bindingDirectory ?? throw new InvalidOperationException("The WorldGuid binding directory is unavailable.");
                var slab = page as BindingCell[] ?? throw new InvalidOperationException("The WorldGuid binding slab has the wrong type.");

                if ((grant._pageOrdinal != _bindingPageCount) || (directory[grant._pageOrdinal] is not null))
                {
                    throw new InvalidOperationException("The WorldGuid binding slab ordinal is invalid.");
                }

                directory[grant._pageOrdinal] = slab;
                _bindingPageCount++;
                break;
            }

            case SlabCategoryPreparation:
            {
                var directory = _preparationDirectory ?? throw new InvalidOperationException("The WorldGuid preparation directory is unavailable.");
                var slab = page as PreparationCell[] ?? throw new InvalidOperationException("The WorldGuid preparation slab has the wrong type.");

                if ((grant._pageOrdinal != _preparationPageCount) || (directory[grant._pageOrdinal] is not null))
                {
                    throw new InvalidOperationException("The WorldGuid preparation slab ordinal is invalid.");
                }

                directory[grant._pageOrdinal] = slab;
                _preparationPageCount++;
                break;
            }

            case SlabCategoryUse:
            {
                var directory = _useDirectory ?? throw new InvalidOperationException("The WorldGuid use directory is unavailable.");
                var slab = page as UseCell[] ?? throw new InvalidOperationException("The WorldGuid use slab has the wrong type.");

                if ((grant._pageOrdinal != _usePageCount) || (directory[grant._pageOrdinal] is not null))
                {
                    throw new InvalidOperationException("The WorldGuid use slab ordinal is invalid.");
                }

                directory[grant._pageOrdinal] = slab;
                _usePageCount++;
                break;
            }

            default:
            {
                throw new InvalidOperationException("The WorldGuid slab category is invalid.");
            }
        }

        ticket._chargedBytes = 0;
        ticket._state = CellStateFree;
        _pendingGrowthTicketCount--;
    }

    private bool EnsureCellAvailability(int category, int requiredCount, out WorldGuidRefusal refusal)
    {
        while (true)
        {
            WorldGuidSlabGrant grant = default;
            var waitTicketIndex = -1;
            var waitTicketGeneration = 0U;

            using (ReentrantGate.EnterScope(ref _admissionGate))
            {
                if (GetAvailableCellCount(category) >= requiredCount)
                {
                    refusal = default;
                    return true;
                }

                var pageOrdinal = GetPageCount(category);

                if (pageOrdinal >= GetDirectoryLength(category))
                {
                    refusal = new WorldGuidRefusal(GetCategoryCapacityRefusal(category));
                    return false;
                }

                for (var ticketIndex = 0; ticketIndex < MaximumPendingSlabAllocations; ticketIndex++)
                {
                    ref var ticket = ref _growthTickets[ticketIndex];

                    if ((ticket._state == SlabTicketPending)
                     && (ticket._category == category)
                     && (ticket._pageOrdinal == pageOrdinal))
                    {
                        waitTicketIndex = ticketIndex;
                        waitTicketGeneration = ticket._generation;
                        break;
                    }
                }

                if (waitTicketIndex < 0)
                {
                    if (_pendingGrowthTicketCount == MaximumPendingSlabAllocations)
                    {
                        refusal = new WorldGuidRefusal(WorldGuidRefusal.PendingSlabCapacity);
                        return false;
                    }

                    var chargedBytes = GetSlabCharge(category);

                    if (s_chargedBytes > (MaximumChargedBytes - chargedBytes))
                    {
                        refusal = new WorldGuidRefusal(WorldGuidRefusal.ByteCapacity);
                        return false;
                    }

                    var ticketIndex = GetAvailableTicketIndex();

                    if (ticketIndex < 0)
                    {
                        refusal = new WorldGuidRefusal(WorldGuidRefusal.PendingSlabCapacity);
                        return false;
                    }

                    ref var ticket = ref _growthTickets[ticketIndex];

                    if (ticket._generation == uint.MaxValue)
                    {
                        ticket._state = SlabTicketRetired;
                        continue;
                    }

                    ticket._generation++;
                    ticket._category = category;
                    ticket._pageOrdinal = pageOrdinal;
                    ticket._chargedBytes = chargedBytes;
                    Volatile.Write(ref ticket._state, SlabTicketPending);
                    _pendingGrowthTicketCount++;
                    Volatile.Write(ref s_chargedBytes, checked(s_chargedBytes + chargedBytes));
                    grant = new WorldGuidSlabGrant(this, category, pageOrdinal, ticketIndex, ticket._generation, chargedBytes);
                }
            }

            if (waitTicketIndex >= 0)
            {
                WaitForGrowthTicket(waitTicketIndex, waitTicketGeneration);
                continue;
            }

            object page;

            try
            {
                page = AllocateSlabPage(grant._category);
            }
            catch
            {
                AbandonSlabGrant(grant);
                throw;
            }

            CompleteSlabGrant(grant, page);
        }
    }

    private int FindIndexBucket(Guid worldGuid)
    {
        var index = _index ?? throw new InvalidOperationException("The WorldGuid index is unavailable.");
        var hashCode = unchecked((uint)worldGuid.GetHashCode());

        if (hashCode == 0)
        {
            hashCode = 1;
        }

        var bucketIndex = (int)(hashCode & (IndexCapacity - 1));

        for (var distance = 0; distance < IndexCapacity; distance++)
        {
            var bucket = index[bucketIndex];

            if (bucket._hashCode == 0)
            {
                return -1;
            }

            if ((bucket._hashCode == hashCode) && (bucket._worldGuid == worldGuid))
            {
                return bucketIndex;
            }

            bucketIndex = (bucketIndex + 1) & (IndexCapacity - 1);
        }

        throw new InvalidOperationException("The WorldGuid index has no empty bucket.");
    }

    private int GetAvailableCellCount(int category)
    {
        var (freeCount, nextCellIndex, pageCount) = category switch {
            SlabCategoryKey => (_keyFreeCellCount, _keyNextCellIndex, _keyPageCount),
            SlabCategoryBinding => (_bindingFreeCellCount, _bindingNextCellIndex, _bindingPageCount),
            SlabCategoryPreparation => (_preparationFreeCellCount, _preparationNextCellIndex, _preparationPageCount),
            SlabCategoryUse => (_useFreeCellCount, _useNextCellIndex, _usePageCount),
            _ => throw new InvalidOperationException("The WorldGuid slab category is invalid."),
        };

        return checked(freeCount + (pageCount * CellsPerSlab) - nextCellIndex);
    }

    private int GetAvailableTicketIndex()
    {
        for (var index = 0; index < MaximumPendingSlabAllocations; index++)
        {
            if (_growthTickets[index]._state == CellStateFree)
            {
                return index;
            }
        }

        return -1;
    }

    private ref BindingCell GetBindingCell(int index)
    {
        var directory = _bindingDirectory ?? throw new InvalidOperationException("The WorldGuid binding directory is unavailable.");
        var page = directory[index / CellsPerSlab] ?? throw new InvalidOperationException("The WorldGuid binding slab is unavailable.");
        return ref page[index % CellsPerSlab];
    }

    private int GetDirectoryLength(int category)
    {
        return category switch {
            SlabCategoryKey => (_keyDirectory ?? throw new InvalidOperationException("The WorldGuid key directory is unavailable.")).Length,
            SlabCategoryBinding => (_bindingDirectory ?? throw new InvalidOperationException("The WorldGuid binding directory is unavailable.")).Length,
            SlabCategoryPreparation => (_preparationDirectory ?? throw new InvalidOperationException("The WorldGuid preparation directory is unavailable.")).Length,
            SlabCategoryUse => (_useDirectory ?? throw new InvalidOperationException("The WorldGuid use directory is unavailable.")).Length,
            _ => throw new InvalidOperationException("The WorldGuid slab category is invalid."),
        };
    }

    private ref KeyCell GetKeyCell(int index)
    {
        var directory = _keyDirectory ?? throw new InvalidOperationException("The WorldGuid key directory is unavailable.");
        var page = directory[index / CellsPerSlab] ?? throw new InvalidOperationException("The WorldGuid key slab is unavailable.");
        return ref page[index % CellsPerSlab];
    }

    private int GetPageCount(int category)
        => category switch {
            SlabCategoryKey => _keyPageCount,
            SlabCategoryBinding => _bindingPageCount,
            SlabCategoryPreparation => _preparationPageCount,
            SlabCategoryUse => _usePageCount,
            _ => throw new InvalidOperationException("The WorldGuid slab category is invalid."),
        };

    private ref PreparationCell GetPreparationCell(int index)
    {
        var directory = _preparationDirectory ?? throw new InvalidOperationException("The WorldGuid preparation directory is unavailable.");
        var page = directory[index / CellsPerSlab] ?? throw new InvalidOperationException("The WorldGuid preparation slab is unavailable.");
        return ref page[index % CellsPerSlab];
    }

    private ref UseCell GetUseCell(int index)
    {
        var directory = _useDirectory ?? throw new InvalidOperationException("The WorldGuid use directory is unavailable.");
        var page = directory[index / CellsPerSlab] ?? throw new InvalidOperationException("The WorldGuid use slab is unavailable.");
        return ref page[index % CellsPerSlab];
    }

    private void InitializeColdBacking()
    {
        Volatile.Write(ref s_initializationStage, InitializationStageIndex);
        _index = new IndexBucket[IndexCapacity];

        Volatile.Write(ref s_initializationStage, InitializationStageKeyDirectory);
        _keyDirectory = new KeyCell[KeyDirectoryLength][];

        Volatile.Write(ref s_initializationStage, InitializationStageBindingDirectory);
        _bindingDirectory = new BindingCell[BindingDirectoryLength][];

        Volatile.Write(ref s_initializationStage, InitializationStagePreparationDirectory);
        _preparationDirectory = new PreparationCell[PreparationDirectoryLength][];

        Volatile.Write(ref s_initializationStage, InitializationStageUseDirectory);
        _useDirectory = new UseCell[UseDirectoryLength][];
    }

    private void InsertIndexEntry(Guid worldGuid, int keyCellIndex, uint keyGeneration)
    {
        var index = _index ?? throw new InvalidOperationException("The WorldGuid index is unavailable.");
        var hashCode = unchecked((uint)worldGuid.GetHashCode());

        if (hashCode == 0)
        {
            hashCode = 1;
        }

        var bucketIndex = (int)(hashCode & (IndexCapacity - 1));

        for (var distance = 0; distance < IndexCapacity; distance++)
        {
            ref var bucket = ref index[bucketIndex];

            if (bucket._hashCode == 0)
            {
                bucket._worldGuid = worldGuid;
                bucket._keyCellIndex = keyCellIndex;
                bucket._keyGeneration = keyGeneration;
                bucket._hashCode = hashCode;
                bucket._probeDistance = (uint)distance;
                _indexEntryCount++;
                return;
            }

            bucketIndex = (bucketIndex + 1) & (IndexCapacity - 1);
        }

        throw new InvalidOperationException("The WorldGuid index has no empty bucket.");
    }

    private bool IsBindingCellValid(int index, uint generation)
    {
        var directory = _bindingDirectory;

        if ((index < 0) || (index >= (_bindingPageCount * CellsPerSlab)) || (directory is null))
        {
            return false;
        }

        var page = directory[index / CellsPerSlab];
        return (page is not null)
            && (page[index % CellsPerSlab]._generation == generation)
            && (page[index % CellsPerSlab]._state is CellStateReserved or CellStateActive);
    }

    private bool IsKeyCellValid(int index, uint generation)
    {
        var directory = _keyDirectory;

        if ((index < 0) || (index >= (_keyPageCount * CellsPerSlab)) || (directory is null))
        {
            return false;
        }

        var page = directory[index / CellsPerSlab];
        return (page is not null)
            && (page[index % CellsPerSlab]._generation == generation)
            && (page[index % CellsPerSlab]._state is CellStateReserved or CellStateActive);
    }

    private bool IsPreparationCellValid(int index, uint generation)
    {
        var directory = _preparationDirectory;

        if ((index < 0) || (index >= (_preparationPageCount * CellsPerSlab)) || (directory is null))
        {
            return false;
        }

        var page = directory[index / CellsPerSlab];
        return (page is not null)
            && (page[index % CellsPerSlab]._generation == generation)
            && (page[index % CellsPerSlab]._state is CellStateReserved or CellStateActive);
    }

    private bool IsUseCellValid(int index, uint generation)
    {
        var directory = _useDirectory;

        if ((index < 0) || (index >= (_usePageCount * CellsPerSlab)) || (directory is null))
        {
            return false;
        }

        var page = directory[index / CellsPerSlab];
        return (page is not null)
            && (page[index % CellsPerSlab]._generation == generation)
            && (page[index % CellsPerSlab]._state is CellStateReserved or CellStateActive);
    }

    private void ReleaseBindingCell(int index, ref BindingCell cell)
    {
        _bindingRoleCount--;

        if (cell._generation == uint.MaxValue)
        {
            cell._state = CellStateRetired;
            cell._freeNextIndex = -1;
            return;
        }

        cell._generation++;
        cell._state = CellStateFree;
        cell._freeNextIndex = _bindingFreeCellHead;
        _bindingFreeCellHead = index;
        _bindingFreeCellCount++;
    }

    private void ReleaseKeyCell(int index, ref KeyCell cell)
    {
        if (cell._generation == uint.MaxValue)
        {
            cell._state = CellStateRetired;
            cell._freeNextIndex = -1;
            return;
        }

        cell._generation++;
        cell._state = CellStateFree;
        cell._worldGuid = Guid.Empty;
        cell._claimCount = 0;
        cell._roleCount = 0;
        cell._freeNextIndex = _keyFreeCellHead;
        _keyFreeCellHead = index;
        _keyFreeCellCount++;
    }

    private void ReleaseKeyIfUnowned(int index, ref KeyCell cell)
    {
        if (cell._roleCount != 0)
        {
            return;
        }

        RemoveIndexEntry(cell._worldGuid, index, cell._generation);
        _retainedKeyCount--;
        ReleaseKeyCell(index, ref cell);
    }

    private void ReleasePreparationCell(int index, ref PreparationCell cell)
    {
        _preparationRoleCount--;

        if (cell._generation == uint.MaxValue)
        {
            cell._state = CellStateRetired;
            cell._freeNextIndex = -1;
            return;
        }

        cell._generation++;
        cell._state = CellStateFree;
        cell._freeNextIndex = _preparationFreeCellHead;
        _preparationFreeCellHead = index;
        _preparationFreeCellCount++;
    }

    private void ReleaseUseCell(int index, ref UseCell cell)
    {
        _useRoleCount--;

        if (cell._generation == uint.MaxValue)
        {
            cell._state = CellStateRetired;
            cell._freeNextIndex = -1;
            return;
        }

        cell._generation++;
        cell._state = CellStateFree;
        cell._freeNextIndex = _useFreeCellHead;
        _useFreeCellHead = index;
        _useFreeCellCount++;
    }

    private void RemoveIndexEntry(Guid worldGuid, int keyCellIndex, uint keyGeneration)
    {
        var index = _index ?? throw new InvalidOperationException("The WorldGuid index is unavailable.");
        var bucketIndex = FindIndexBucket(worldGuid);

        if (bucketIndex < 0)
        {
            throw new InvalidOperationException("The WorldGuid key is missing from the process index.");
        }

        ref var target = ref index[bucketIndex];

        if ((target._keyCellIndex != keyCellIndex) || (target._keyGeneration != keyGeneration))
        {
            throw new InvalidOperationException("The WorldGuid index points to a different key generation.");
        }

        var hole = bucketIndex;
        var next = (hole + 1) & (IndexCapacity - 1);

        // A bucket at its home position does not end a cluster; later keys may still have probed across the hole.
        while (index[next]._hashCode != 0)
        {
            ref var candidate = ref index[next];
            var homeBucket = (next - (int)candidate._probeDistance) & (IndexCapacity - 1);
            var distanceToHole = (hole - homeBucket) & (IndexCapacity - 1);

            if (distanceToHole < candidate._probeDistance)
            {
                index[hole] = candidate;
                index[hole]._probeDistance = (uint)distanceToHole;
                hole = next;
            }

            next = (next + 1) & (IndexCapacity - 1);
        }

        index[hole] = default;
        _indexEntryCount--;
    }

    private void ValidateColdBacking()
    {
        if ((_index is null)
         || (_index.Length != IndexCapacity)
         || (_keyDirectory is null)
         || (_keyDirectory.Length != KeyDirectoryLength)
         || (_bindingDirectory is null)
         || (_bindingDirectory.Length != BindingDirectoryLength)
         || (_preparationDirectory is null)
         || (_preparationDirectory.Length != PreparationDirectoryLength)
         || (_useDirectory is null)
         || (_useDirectory.Length != UseDirectoryLength)
         || (Unsafe.SizeOf<IndexBucket>() != 32)
         || (Unsafe.SizeOf<KeyCell>() != 128)
         || (Unsafe.SizeOf<BindingCell>() != 128)
         || (Unsafe.SizeOf<PreparationCell>() != 256)
         || (Unsafe.SizeOf<UseCell>() != 128)
         || (IntPtr.Size != sizeof(long))
         || (GetColdReservationByteCount() != ColdReservationByteCount))
        {
            ThrowHelper.ThrowInvalidOperationException("The WorldGuid registry cold backing does not match its fixed profile.");
        }
    }

    private void WaitForGrowthTicket(int ticketIndex, uint ticketGeneration)
    {
        var spinner = new SpinWait();

        while (true)
        {
            ref var ticket = ref _growthTickets[ticketIndex];

            if ((Volatile.Read(ref ticket._generation) != ticketGeneration)
             || (Volatile.Read(ref ticket._state) != SlabTicketPending))
            {
                return;
            }

            spinner.SpinOnce();
        }
    }

    [StructLayout(LayoutKind.Sequential, Size = 128)]
    private struct BindingCell
    {
        internal uint _generation;
        internal int _keyCellIndex;
        internal uint _keyGeneration;
        internal int _state;
        internal int _freeNextIndex;
    }

    private struct GrowthTicket
    {
        internal int _category;
        internal long _chargedBytes;
        internal uint _generation;
        internal int _pageOrdinal;
        internal int _state;
    }

    [InlineArray(MaximumPendingSlabAllocations)]
    private struct GrowthTicketBuffer
    {
        private GrowthTicket _element0;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4, Size = 32)]
    private struct IndexBucket
    {
        internal Guid _worldGuid;
        internal int _keyCellIndex;
        internal uint _keyGeneration;
        internal uint _hashCode;
        internal uint _probeDistance;
    }

    [StructLayout(LayoutKind.Sequential, Size = 128)]
    private struct KeyCell
    {
        internal Guid _worldGuid;
        internal uint _generation;
        internal int _roleCount;
        internal int _claimCount;
        internal int _freeNextIndex;
        internal int _state;
    }

    [StructLayout(LayoutKind.Sequential, Size = 256)]
    private struct PreparationCell
    {
        internal uint _generation;
        internal int _keyCellIndex;
        internal uint _keyGeneration;
        internal int _bindingCellIndex;
        internal uint _bindingGeneration;
        internal int _state;
        internal int _freeNextIndex;
    }

    [StructLayout(LayoutKind.Sequential, Size = 128)]
    private struct UseCell
    {
        internal uint _generation;
        internal int _keyCellIndex;
        internal uint _keyGeneration;
        internal int _bindingCellIndex;
        internal uint _bindingGeneration;
        internal int _role;
        internal int _state;
        internal int _freeNextIndex;
    }
}
