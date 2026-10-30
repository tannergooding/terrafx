// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace TerraFX.Entities;

public sealed partial class World
{
    private static void ValidateAttachmentOwnership(AttachmentOwnership ownership)
    {
        if (ownership is not AttachmentOwnership.CallerOwned and not AttachmentOwnership.Transferred)
        {
            throw new ArgumentOutOfRangeException(nameof(ownership));
        }
    }

    /// <summary>Removes one World-scoped transient attachment and releases transferred values after detachment.</summary>
    /// <typeparam name="T">The attachment CLR type.</typeparam>
    /// <returns><see langword="true"/> when an attachment was removed.</returns>
    /// <exception cref="InvalidOperationException">The World is not idle on its owner thread or a transferred release callback failed.</exception>
    public bool RemoveAttachment<T>()
        where T : class
    {
        EnterStructuralOperation();

        try
        {
            return DetachAttachment(typeof(T), null);
        }
        finally
        {
            _ = System.Threading.Interlocked.Exchange(ref _isStructuralOperationActive, 0);
        }
    }

    /// <summary>Removes one Entity-scoped transient attachment and releases transferred values after detachment.</summary>
    /// <typeparam name="T">The attachment CLR type.</typeparam>
    /// <param name="entity">The live same-World Entity attachment owner.</param>
    /// <returns><see langword="true"/> when an attachment was removed.</returns>
    /// <exception cref="ArgumentException"><paramref name="entity"/> is default, stale, or belongs to another World.</exception>
    /// <exception cref="InvalidOperationException">The World is not idle on its owner thread or a transferred release callback failed.</exception>
    public bool RemoveAttachment<T>(Entity entity)
        where T : class
    {
        EnterStructuralOperation();

        try
        {
            var ownerEntityId = AttachmentOwnerPersistentId(entity);
            return DetachAttachment(typeof(T), ownerEntityId);
        }
        finally
        {
            _ = System.Threading.Interlocked.Exchange(ref _isStructuralOperationActive, 0);
        }
    }

    /// <summary>Installs or replaces one Entity-scoped transient attachment.</summary>
    /// <typeparam name="T">The attachment CLR type.</typeparam>
    /// <param name="entity">The live same-World Entity attachment owner.</param>
    /// <param name="value">The nonnull caller-owned value.</param>
    /// <param name="ownership">Whether the caller retains ownership or transfers it to the registered provider.</param>
    /// <exception cref="ArgumentException"><paramref name="entity"/> is default, stale, or belongs to another World.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="ownership"/> conflicts with the registered transferred attachment family.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ownership"/> is not defined.</exception>
    /// <exception cref="InvalidOperationException">Transferred ownership has no registered provider, validation fails, capacity is unavailable, or a transferred release callback fails.</exception>
    public void SetAttachment<T>(
        Entity entity,
        T value,
        AttachmentOwnership ownership = AttachmentOwnership.CallerOwned)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(value);
        ValidateAttachmentOwnership(ownership);
        EnterStructuralOperation();

        try
        {
            var ownerEntityId = AttachmentOwnerPersistentId(entity);
            InstallAttachment(ownerEntityId, typeof(T), value, ownership);
        }
        finally
        {
            _ = System.Threading.Interlocked.Exchange(ref _isStructuralOperationActive, 0);
        }
    }

    /// <summary>Installs or replaces one World-scoped transient attachment.</summary>
    /// <typeparam name="T">The attachment CLR type.</typeparam>
    /// <param name="value">The nonnull caller-owned value.</param>
    /// <param name="ownership">Whether the caller retains ownership or transfers it to the registered provider.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    /// <exception cref="ArgumentException">CallerOwned ownership conflicts with the registered transferred attachment family.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ownership"/> is not defined.</exception>
    /// <exception cref="InvalidOperationException">Transferred ownership has no registered provider, validation fails, capacity is unavailable, or a transferred release callback fails.</exception>
    public void SetAttachment<T>(T value, AttachmentOwnership ownership = AttachmentOwnership.CallerOwned)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(value);
        ValidateAttachmentOwnership(ownership);
        EnterStructuralOperation();

        try
        {
            InstallAttachment(null, typeof(T), value, ownership);
        }
        finally
        {
            _ = System.Threading.Interlocked.Exchange(ref _isStructuralOperationActive, 0);
        }
    }

    /// <summary>Gets one Entity-scoped transient attachment without invoking application code.</summary>
    /// <typeparam name="T">The attachment CLR type.</typeparam>
    /// <param name="entity">The live same-World Entity attachment owner.</param>
    /// <param name="value">The stored value, or null when absent.</param>
    /// <returns><see langword="true"/> when the attachment is present.</returns>
    /// <exception cref="ArgumentException"><paramref name="entity"/> is default, stale, or belongs to another World.</exception>
    /// <exception cref="InvalidOperationException">The World is not idle on its owner thread.</exception>
    public bool TryGetAttachment<T>(Entity entity, [NotNullWhen(true)] out T? value)
        where T : class
    {
        EnsureWorldReadAccess();
        var ownerEntityId = AttachmentOwnerPersistentId(entity);

        if (_attachments.TryGet(typeof(T), ownerEntityId, out var storedValue))
        {
            value = storedValue as T
                ?? throw new InvalidOperationException("The stored Entity attachment does not match its exact CLR type key.");
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>Gets one World-scoped transient attachment without invoking application code.</summary>
    /// <typeparam name="T">The attachment CLR type.</typeparam>
    /// <param name="value">The stored value, or null when absent.</param>
    /// <returns><see langword="true"/> when the attachment is present.</returns>
    /// <exception cref="InvalidOperationException">The World is not idle on its owner thread.</exception>
    public bool TryGetAttachment<T>([NotNullWhen(true)] out T? value)
        where T : class
    {
        EnsureWorldReadAccess();

        if (_attachments.TryGet(typeof(T), null, out var storedValue))
        {
            value = storedValue as T
                ?? throw new InvalidOperationException("The stored World attachment does not match its exact CLR type key.");
            return true;
        }

        value = null;
        return false;
    }

    internal void ValidateAttachmentEntityReference(Entity entity)
    {
        _ = GetLiveArchetype(entity, out _, out _);

        if (_identityLedger.GetAssignedId(IdentityKind.Entity, entity.Index) == Guid.Empty)
        {
            throw new InvalidOperationException("The live attachment Entity reference has no stable identity.");
        }
    }

    private Guid AttachmentOwnerPersistentId(Entity entity)
    {
        _ = GetLiveArchetype(entity, out _, out _);
        var entityId = _identityLedger.GetAssignedId(IdentityKind.Entity, entity.Index);

        if (entityId == Guid.Empty)
        {
            throw new InvalidOperationException("The live Entity has no stable attachment owner identity.");
        }

        return entityId;
    }

    private bool DetachAttachment(Type valueType, Guid? ownerEntityId)
    {
        if (!_attachments.TryGetEntry(valueType, ownerEntityId, out var entry))
        {
            return false;
        }

        if (entry.Ownership == AttachmentOwnership.CallerOwned)
        {
            return _attachments.Remove(valueType, ownerEntityId);
        }

        var registration = entry.Registration
            ?? throw new InvalidOperationException("The transferred attachment has no captured release provider.");
        var releaseBinding = PrepareAttachmentReleaseBinding(entry, replacementInstallationId: null);
        var nextMutationRevision = checked(_canonicalMutationRevision + 1);
        var releasePreparation = new WorldAttachmentReleasePreparation(_worldGuid, 1, nextMutationRevision);
        var quarantineReservation = WorldAttachmentInstanceRegistry.ReserveQuarantineCapacity(1);

        if (!_attachments.TryRemove(valueType, ownerEntityId, out var removedEntry))
        {
            quarantineReservation.Release();
            throw new InvalidOperationException("The transferred attachment changed before its prepared removal.");
        }

        try
        {
            _canonicalMutationRevision = nextMutationRevision;
            RunAttachmentRelease(removedEntry, registration, releaseBinding, quarantineReservation, releasePreparation);

            if (releasePreparation.HasFailures)
            {
                throw releasePreparation.CreateException(systemCleanupReport: null);
            }

            return true;
        }
        finally
        {
            quarantineReservation.Release();
        }
    }

    private void InstallAttachment(Guid? ownerEntityId, Type valueType, object value, AttachmentOwnership ownership)
    {
        var scope = ownerEntityId is null ? AttachmentScope.World : AttachmentScope.Entity;

        if (ownership == AttachmentOwnership.Transferred)
        {
            if (!Registration.TryGetAttachment(scope, valueType, out var registration))
            {
                throw new InvalidOperationException("No transferred attachment provider is registered for the selected scope and value type.");
            }

            InstallTransferredAttachment(ownerEntityId, valueType, value, registration);
            return;
        }

        if (Registration.TryGetAttachment(scope, valueType, out _))
        {
            throw new ArgumentException("A registered attachment type requires Transferred ownership.", nameof(ownership));
        }

        if (_attachments.TryGet(valueType, ownerEntityId, out _))
        {
            _attachments.Set(valueType, ownerEntityId, value);
            return;
        }

        var additionalBytes = _attachments.GetAdditionalCapacityBytes();

        if (additionalBytes == 0)
        {
            _attachments.Set(valueType, ownerEntityId, value);
            return;
        }

        var oldOwnedBytes = GetWorldOwnedStateBytes();

        if (checked(oldOwnedBytes + additionalBytes) > _maximumOwnedStateBytes)
        {
            throw new InvalidOperationException("The World-owned state budget cannot admit attachment table growth.");
        }

        if (!_domainReservation.TryReserveAdditional(additionalBytes))
        {
            throw new InvalidOperationException("The admission domain cannot reserve attachment table growth.");
        }

        try
        {
            var preparedEntries = _attachments.PrepareCapacity();
            _attachments.CommitCapacity(preparedEntries, additionalBytes);
        }
        catch
        {
            _domainReservation.ReleaseAdditional(additionalBytes);
            throw;
        }

        _attachments.Set(valueType, ownerEntityId, value);
    }

    private void InstallTransferredAttachment(
        Guid? ownerEntityId,
        Type valueType,
        object value,
        IWorldAttachmentRegistration? registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        if ((registration.ValueType != valueType)
            || (registration.Scope != (ownerEntityId is null ? AttachmentScope.World : AttachmentScope.Entity)))
        {
            throw new InvalidOperationException("The selected transferred attachment registration does not match its owner key.");
        }

        var hasExisting = _attachments.TryGetEntry(valueType, ownerEntityId, out var existing);
        var installationId = checked(_nextAttachmentInstallationId + 1);
        var nextMutationRevision = checked(_canonicalMutationRevision + 1);
        WorldAttachmentReleaseBinding? releaseBinding = null;
        WorldAttachmentReleasePreparation? releasePreparation = null;
        var quarantineReservation = WorldAttachmentInstanceRegistry.ReserveQuarantineCapacity(hasExisting ? 1 : 0);
        var additionalBytes = 0L;
        AttachmentTableCapacity? preparedCapacity = null;
        var capacityCommitted = false;

        try
        {
            if (hasExisting)
            {
                if ((existing.Ownership != AttachmentOwnership.Transferred)
                    || (existing.Registration is null))
                {
                    throw new InvalidOperationException("A registered transferred attachment conflicts with an existing caller-owned value.");
                }

                releaseBinding = PrepareAttachmentReleaseBinding(existing, installationId);
                releasePreparation = new WorldAttachmentReleasePreparation(_worldGuid, 1, nextMutationRevision);
            }

            additionalBytes = _attachments.GetAdditionalCapacityBytes();

            if (additionalBytes != 0)
            {
                var oldOwnedBytes = GetWorldOwnedStateBytes();

                if (checked(oldOwnedBytes + additionalBytes) > _maximumOwnedStateBytes)
                {
                    throw new InvalidOperationException("The World-owned state budget cannot admit attachment table growth.");
                }

                if (!_domainReservation.TryReserveAdditional(additionalBytes))
                {
                    throw new InvalidOperationException("The admission domain cannot reserve attachment table growth.");
                }

                preparedCapacity = new AttachmentTableCapacity(_attachments.PrepareCapacity(), additionalBytes);
            }

            var diagnostics = RunAttachmentChecks(registration, value);

            if (!diagnostics.IsEmpty)
            {
                throw new InvalidOperationException($"The transferred attachment failed validation: {diagnostics[0].Code}.");
            }

            if (preparedCapacity is { } capacity)
            {
                _attachments.CommitCapacity(capacity.Entries, capacity.AdditionalBytes);
                capacityCommitted = true;
            }

            _attachments.Set(valueType, ownerEntityId, value, AttachmentOwnership.Transferred, installationId, registration);
            _nextAttachmentInstallationId = installationId;
            _canonicalMutationRevision = nextMutationRevision;

            if (releaseBinding is not null)
            {
                var preparedRelease = releasePreparation
                    ?? throw new InvalidOperationException("The transferred attachment release report was not prepared.");
                RunAttachmentRelease(
                    existing,
                    existing.Registration
                        ?? throw new InvalidOperationException("The replaced transferred attachment has no captured release provider."),
                    releaseBinding,
                    quarantineReservation,
                    preparedRelease);

                if (preparedRelease.HasFailures)
                {
                    throw preparedRelease.CreateException(systemCleanupReport: null);
                }
            }
        }
        finally
        {
            if ((additionalBytes != 0) && !capacityCommitted)
            {
                _domainReservation.ReleaseAdditional(additionalBytes);
            }

            quarantineReservation.Release();
        }
    }

    private WorldAttachmentReleaseBinding PrepareAttachmentReleaseBinding(
        WorldAttachmentTable.AttachmentEntry entry,
        ulong? replacementInstallationId)
    {
        var registration = entry.Registration
            ?? throw new InvalidOperationException("The transferred attachment has no captured release provider.");
        var scope = entry.OwnerEntityId is null ? AttachmentScope.World : AttachmentScope.Entity;
        return new WorldAttachmentReleaseBinding(
            _worldGuid,
            scope,
            registration.Descriptor.Schema,
            entry.OwnerEntityId,
            entry.InstallationId,
            replacementInstallationId);
    }

    private WorldAttachmentReleaseBinding[] PrepareAttachmentReleaseBindings(
        WorldAttachmentTable.AttachmentEntry[] entries)
    {
        var bindings = new WorldAttachmentReleaseBinding[entries.Length];

        for (var index = 0; index < entries.Length; index++)
        {
            bindings[index] = PrepareAttachmentReleaseBinding(entries[index], replacementInstallationId: null);
        }

        return bindings;
    }

    private void ReleaseAttachmentEntries(
        WorldAttachmentTable.AttachmentEntry[] entries,
        WorldAttachmentReleaseBinding[] bindings,
        WorldAttachmentQuarantineReservation quarantineReservation,
        WorldAttachmentReleasePreparation releasePreparation)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(quarantineReservation);
        ArgumentNullException.ThrowIfNull(releasePreparation);

        if (entries.Length != bindings.Length)
        {
            throw new InvalidOperationException("The prepared attachment release bindings do not match the detached entries.");
        }

        try
        {
            for (var index = 0; index < entries.Length; index++)
            {
                var entry = entries[index];
                var registration = entry.Registration
                    ?? throw new InvalidOperationException("A detached transferred attachment has no captured release provider.");
                RunAttachmentRelease(entry, registration, bindings[index], quarantineReservation, releasePreparation);
            }
        }
        finally
        {
            quarantineReservation.Release();
        }
    }

    [SuppressMessage("Usage", "CA1031:Do not catch general exception types", Justification = "The callback wrapper must prefer its first sticky reentrancy failure over an exception caught or replaced by the provider.")]
    private ImmutableArray<Diagnostic> RunAttachmentChecks(IWorldAttachmentRegistration registration, object value)
    {
        StartAttachmentCallback();
        var diagnostics = default(ImmutableArray<Diagnostic>);
        Exception? callbackFailure = null;

        try
        {
            diagnostics = registration.Validate(this, value);
        }
        catch (Exception exception)
        {
            callbackFailure = exception;
        }
        finally
        {
            StopAttachmentCallback();
        }

        var stickyFailure = Interlocked.Exchange(ref _attachmentCallbackStickyFailure, null);

        if (stickyFailure is not null)
        {
            throw stickyFailure;
        }

        if (callbackFailure is not null)
        {
            throw callbackFailure;
        }

        return diagnostics;
    }

    [SuppressMessage("Usage", "CA1031:Do not catch general exception types", Justification = "A committed transferred attachment must be strongly quarantined when its exactly-once Release callback fails.")]
    private void RunAttachmentRelease(
        WorldAttachmentTable.AttachmentEntry entry,
        IWorldAttachmentRegistration registration,
        WorldAttachmentReleaseBinding binding,
        WorldAttachmentQuarantineReservation quarantineReservation,
        WorldAttachmentReleasePreparation releasePreparation)
    {
        try
        {
            StartAttachmentCallback();
            Exception? callbackFailure = null;

            try
            {
                registration.Release(
                    entry.Value ?? throw new InvalidOperationException("The detached attachment has no value."),
                    binding);
            }
            catch (Exception exception)
            {
                callbackFailure = exception;
            }
            finally
            {
                StopAttachmentCallback();
            }

            var stickyFailure = Interlocked.Exchange(ref _attachmentCallbackStickyFailure, null);

            if (stickyFailure is not null)
            {
                throw stickyFailure;
            }

            if (callbackFailure is not null)
            {
                throw callbackFailure;
            }
        }
        catch (Exception exception)
        {
            WorldAttachmentInstanceRegistry.Quarantine(
                entry.Value ?? throw new InvalidOperationException("The failed attachment release has no value."),
                quarantineReservation,
                _domainReservation);
            releasePreparation.RecordFailure(binding, exception);
        }
    }

    private void StartAttachmentCallback()
    {
        if (Volatile.Read(ref _isAttachmentCallbackActive) != 0)
        {
            throw new InvalidOperationException("A World attachment provider callback is already active.");
        }

        Volatile.Write(ref _attachmentCallbackStickyFailure, null);

        if (Interlocked.CompareExchange(ref _isAttachmentCallbackActive, 1, 0) != 0)
        {
            throw new InvalidOperationException("A World attachment provider callback is already active.");
        }
    }

    private void StopAttachmentCallback()
        => _ = Interlocked.Exchange(ref _isAttachmentCallbackActive, 0);

    private void ThrowIfAttachmentCallbackActive()
    {
        if (Volatile.Read(ref _isAttachmentCallbackActive) != 0)
        {
            var exception = new InvalidOperationException("World operations are unavailable during an attachment provider callback.");
            throw Interlocked.CompareExchange(ref _attachmentCallbackStickyFailure, exception, null) ?? exception;
        }
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Captured attachment field readers must not swallow a reentrant World-operation protocol failure during validation.")]
    private void ValidateAllAttachmentEntityReferences()
    {
        StartAttachmentCallback();
        Exception? callbackFailure = null;

        try
        {
            _attachments.ValidateAllEntityReferences(this);
        }
        catch (Exception exception)
        {
            callbackFailure = exception;
        }
        finally
        {
            StopAttachmentCallback();
        }

        var stickyFailure = Interlocked.Exchange(ref _attachmentCallbackStickyFailure, null);

        if (stickyFailure is not null)
        {
            throw stickyFailure;
        }

        if (callbackFailure is not null)
        {
            throw callbackFailure;
        }
    }

    [SuppressMessage("Usage", "CA1031:Do not catch general exception types", Justification = "Captured attachment field readers must not swallow a reentrant World-operation protocol failure during Entity retirement.")]
    private void ValidateAttachmentEntityReferences(Entity target, Guid excludedOwnerId)
    {
        StartAttachmentCallback();
        Exception? callbackFailure = null;

        try
        {
            _attachments.ValidateEntityReferences(this, target, excludedOwnerId);
        }
        catch (Exception exception)
        {
            callbackFailure = exception;
        }
        finally
        {
            StopAttachmentCallback();
        }

        var stickyFailure = Interlocked.Exchange(ref _attachmentCallbackStickyFailure, null);

        if (stickyFailure is not null)
        {
            throw stickyFailure;
        }

        if (callbackFailure is not null)
        {
            throw callbackFailure;
        }
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Captured attachment field readers must not swallow a reentrant World-operation protocol failure during Entity retirement.")]
    private void ValidateAttachmentEntityReferencesExcludingOwners(IReadOnlySet<Guid> excludedOwnerIds)
    {
        ArgumentNullException.ThrowIfNull(excludedOwnerIds);
        StartAttachmentCallback();
        Exception? callbackFailure = null;

        try
        {
            _attachments.ValidateEntityReferencesExcludingOwners(this, excludedOwnerIds);
        }
        catch (Exception exception)
        {
            callbackFailure = exception;
        }
        finally
        {
            StopAttachmentCallback();
        }

        var stickyFailure = Interlocked.Exchange(ref _attachmentCallbackStickyFailure, null);

        if (stickyFailure is not null)
        {
            throw stickyFailure;
        }

        if (callbackFailure is not null)
        {
            throw callbackFailure;
        }
    }

    private readonly struct AttachmentTableCapacity
    {
        internal AttachmentTableCapacity(WorldAttachmentTable.AttachmentEntry[] entries, long additionalBytes)
        {
            Entries = entries;
            AdditionalBytes = additionalBytes;
        }

        internal long AdditionalBytes { get; }

        internal WorldAttachmentTable.AttachmentEntry[] Entries { get; }
    }
}
