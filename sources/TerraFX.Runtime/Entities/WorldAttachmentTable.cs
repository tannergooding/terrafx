// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;

namespace TerraFX.Entities;

internal sealed class WorldAttachmentTable
{
    private const int ArrayHeaderBytes = 64;
    private const int EntryBytes = 32;

    private int _count;
    private AttachmentEntry[] _entries = [];
    private long _ownedByteReservation;

    internal int Count => _count;

    internal bool HasTransferredEntries
    {
        get
        {
            for (var index = 0; index < _count; index++)
            {
                if (_entries[index].Ownership == AttachmentOwnership.Transferred)
                {
                    return true;
                }
            }

            return false;
        }
    }

    internal long OwnedByteReservation => _ownedByteReservation;

    private static long ArrayBytes(int capacity)
        => checked(ArrayHeaderBytes + ((checked((long)capacity * EntryBytes) + 7L) & ~7L));

    internal AttachmentEntry[] CaptureTransferredEntries()
        => CaptureTransferredEntries(ownerEntityId: null, includeWorldScope: true);

    internal AttachmentEntry[] CaptureTransferredOwnerEntries(Guid ownerEntityId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(ownerEntityId, Guid.Empty);
        return CaptureTransferredEntries(ownerEntityId, includeWorldScope: false);
    }

    internal void ClearEntries()
    {
        Array.Clear(_entries, 0, _count);
        _count = 0;
    }

    internal void CommitCapacity(AttachmentEntry[] preparedEntries, long additionalBytes)
    {
        ArgumentNullException.ThrowIfNull(preparedEntries);
        var newOwnedByteReservation = checked(_ownedByteReservation + additionalBytes);

        if ((preparedEntries.Length <= _entries.Length)
            || (preparedEntries.Length < _count)
            || (additionalBytes != checked(ArrayBytes(preparedEntries.Length) - _ownedByteReservation)))
        {
            throw new InvalidOperationException("The prepared World attachment capacity does not match the live table.");
        }

        _entries = preparedEntries;
        _ownedByteReservation = newOwnedByteReservation;
    }

    internal int CountTransferredEntries()
    {
        var count = 0;

        for (var index = 0; index < _count; index++)
        {
            count += _entries[index].Ownership == AttachmentOwnership.Transferred ? 1 : 0;
        }

        return count;
    }

    internal int CountTransferredOwner(Guid ownerEntityId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(ownerEntityId, Guid.Empty);
        var count = 0;

        for (var index = 0; index < _count; index++)
        {
            var entry = _entries[index];
            count += ((entry.OwnerEntityId == ownerEntityId)
                && (entry.Ownership == AttachmentOwnership.Transferred)) ? 1 : 0;
        }

        return count;
    }

    internal long GetAdditionalCapacityBytes()
    {
        if (_count < _entries.Length)
        {
            return 0;
        }

        var newCapacity = (_entries.Length == 0) ? 4 : checked(_entries.Length * 2);
        return checked(ArrayBytes(newCapacity) - _ownedByteReservation);
    }

    internal bool HasTransferredOwner(Guid ownerEntityId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(ownerEntityId, Guid.Empty);

        for (var index = 0; index < _count; index++)
        {
            var entry = _entries[index];

            if ((entry.OwnerEntityId == ownerEntityId)
                && (entry.Ownership == AttachmentOwnership.Transferred))
            {
                return true;
            }
        }

        return false;
    }

    internal AttachmentEntry[] PrepareCapacity()
    {
        if (_count < _entries.Length)
        {
            throw new InvalidOperationException("The World attachment table does not require capacity growth.");
        }

        var newCapacity = (_entries.Length == 0) ? 4 : checked(_entries.Length * 2);
        var preparedEntries = new AttachmentEntry[newCapacity];
        _entries.AsSpan().CopyTo(preparedEntries);
        return preparedEntries;
    }

    internal long ReleaseOwnedStorage()
    {
        var releasedBytes = _ownedByteReservation;
        Array.Clear(_entries);
        _entries = [];
        _count = 0;
        _ownedByteReservation = 0;
        return releasedBytes;
    }

    internal bool Remove(Type valueType, Guid? ownerEntityId)
    {
        ArgumentNullException.ThrowIfNull(valueType);
        var index = Find(valueType, ownerEntityId);

        if (index < 0)
        {
            return false;
        }

        RemoveAt(index);
        return true;
    }

    internal void RemoveOwner(Guid ownerEntityId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(ownerEntityId, Guid.Empty);

        for (var index = _count - 1; index >= 0; index--)
        {
            if (_entries[index].OwnerEntityId == ownerEntityId)
            {
                RemoveAt(index);
            }
        }
    }

    internal void Set(
        Type valueType,
        Guid? ownerEntityId,
        object value,
        AttachmentOwnership ownership = AttachmentOwnership.CallerOwned,
        ulong installationId = 0,
        IWorldAttachmentRegistration? registration = null)
    {
        ArgumentNullException.ThrowIfNull(valueType);
        ArgumentNullException.ThrowIfNull(value);

        if ((ownerEntityId == Guid.Empty) || !valueType.IsInstanceOfType(value))
        {
            throw new ArgumentException("The attachment key or exact value type is invalid.", nameof(value));
        }

        var index = Find(valueType, ownerEntityId);

        if (index >= 0)
        {
            _entries[index] = new AttachmentEntry(valueType, ownerEntityId, value, ownership, installationId, registration);
            return;
        }

        if (_count >= _entries.Length)
        {
            throw new InvalidOperationException("The World attachment table has no prepared append capacity.");
        }

        _entries[_count++] = new AttachmentEntry(valueType, ownerEntityId, value, ownership, installationId, registration);
    }

    internal bool TryGet(Type valueType, Guid? ownerEntityId, out object? value)
    {
        ArgumentNullException.ThrowIfNull(valueType);
        var index = Find(valueType, ownerEntityId);

        if (index >= 0)
        {
            value = _entries[index].Value;
            return true;
        }

        value = null;
        return false;
    }

    internal bool TryGetEntry(Type valueType, Guid? ownerEntityId, out AttachmentEntry entry)
    {
        ArgumentNullException.ThrowIfNull(valueType);
        var index = Find(valueType, ownerEntityId);

        if (index >= 0)
        {
            entry = _entries[index];
            return true;
        }

        entry = default;
        return false;
    }

    internal bool TryRemove(Type valueType, Guid? ownerEntityId, out AttachmentEntry entry)
    {
        ArgumentNullException.ThrowIfNull(valueType);
        var index = Find(valueType, ownerEntityId);

        if (index < 0)
        {
            entry = default;
            return false;
        }

        entry = _entries[index];
        RemoveAt(index);
        return true;
    }

    internal void ValidateAllEntityReferences(World world)
    {
        ArgumentNullException.ThrowIfNull(world);

        for (var index = 0; index < _count; index++)
        {
            var entry = _entries[index];

            if (entry.Ownership != AttachmentOwnership.Transferred)
            {
                continue;
            }

            var registration = entry.Registration
                ?? throw new InvalidOperationException("The transferred attachment has no captured reference validator.");
            registration.ValidateEntityReferences(
                world,
                entry.Value ?? throw new InvalidOperationException("The transferred attachment has no value."),
                default);
        }
    }

    internal void ValidateEntityReferences(World world, Entity target, Guid excludedOwnerId)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentOutOfRangeException.ThrowIfEqual(excludedOwnerId, Guid.Empty);

        for (var index = 0; index < _count; index++)
        {
            var entry = _entries[index];

            if ((entry.Ownership != AttachmentOwnership.Transferred)
                || (entry.OwnerEntityId == excludedOwnerId))
            {
                continue;
            }

            var registration = entry.Registration
                ?? throw new InvalidOperationException("The transferred attachment has no captured reference validator.");
            registration.ValidateEntityReferences(
                world,
                entry.Value ?? throw new InvalidOperationException("The transferred attachment has no value."),
                target);
        }
    }

    internal void ValidateEntityReferencesExcludingOwners(World world, IReadOnlySet<Guid> excludedOwnerIds)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(excludedOwnerIds);

        for (var index = 0; index < _count; index++)
        {
            var entry = _entries[index];

            if ((entry.Ownership != AttachmentOwnership.Transferred)
                || (entry.OwnerEntityId is { } ownerEntityId && excludedOwnerIds.Contains(ownerEntityId)))
            {
                continue;
            }

            var registration = entry.Registration
                ?? throw new InvalidOperationException("The transferred attachment has no captured reference validator.");
            registration.ValidateEntityReferences(
                world,
                entry.Value ?? throw new InvalidOperationException("The transferred attachment has no value."),
                default);
        }
    }

    private AttachmentEntry[] CaptureTransferredEntries(Guid? ownerEntityId, bool includeWorldScope)
    {
        var count = 0;

        for (var index = 0; index < _count; index++)
        {
            var entry = _entries[index];

            if ((entry.Ownership == AttachmentOwnership.Transferred)
                && (includeWorldScope || (entry.OwnerEntityId == ownerEntityId)))
            {
                count++;
            }
        }

        var capturedEntries = new AttachmentEntry[count];
        var capturedIndex = 0;

        for (var index = 0; index < _count; index++)
        {
            var entry = _entries[index];

            if ((entry.Ownership == AttachmentOwnership.Transferred)
                && (includeWorldScope || (entry.OwnerEntityId == ownerEntityId)))
            {
                capturedEntries[capturedIndex++] = entry;
            }
        }

        return capturedEntries;
    }

    private int Find(Type valueType, Guid? ownerEntityId)
    {
        for (var index = 0; index < _count; index++)
        {
            var entry = _entries[index];

            if ((entry.ValueType == valueType) && (entry.OwnerEntityId == ownerEntityId))
            {
                return index;
            }
        }

        return -1;
    }

    private void RemoveAt(int index)
    {
        var lastIndex = --_count;

        if (index != lastIndex)
        {
            _entries[index] = _entries[lastIndex];
        }

        _entries[lastIndex] = default;
    }

    internal readonly struct AttachmentEntry
    {
        internal AttachmentEntry(
            Type valueType,
            Guid? ownerEntityId,
            object value,
            AttachmentOwnership ownership,
            ulong installationId,
            IWorldAttachmentRegistration? registration)
        {
            ValueType = valueType;
            OwnerEntityId = ownerEntityId;
            Value = value;
            Ownership = ownership;
            InstallationId = installationId;
            Registration = registration;
        }

        internal ulong InstallationId { get; }

        internal Guid? OwnerEntityId { get; }

        internal AttachmentOwnership Ownership { get; }

        internal IWorldAttachmentRegistration? Registration { get; }

        internal object? Value { get; }

        internal Type? ValueType { get; }
    }
}
