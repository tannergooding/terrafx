// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

internal sealed class WorldRestoreSourceLease : IDisposable
{
    private readonly WorldRestoreSourceControl _control;
    private ReadOnlyMemory<byte> _ecsRecord;
    private bool _isDisposed;
    private bool _isProviderRead;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private ReadOnlyMemory<byte> _scenesRecord;
    private readonly IDisposable _sourceUse;

    internal WorldRestoreSourceLease(
        IWorldRestoreSourceOwner owner,
        ReadOnlyMemory<byte> ecsRecord,
        ReadOnlyMemory<byte> scenesRecord,
        IDisposable sourceUse)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(sourceUse);
        _control = (sourceUse as WorldRestoreSourceControl.SourceUse)?.Control
            ?? throw new ArgumentException("A source lease requires an actual prepared-load source use.", nameof(sourceUse));
        _control.ValidateSource(owner, ecsRecord, scenesRecord);
        _ecsRecord = ecsRecord;
        _scenesRecord = scenesRecord;
        _sourceUse = sourceUse;
    }

    internal WorldRestoreSourceControl Control => _control;

    internal ReadOnlySpan<byte> EcsRecord
    {
        get
        {
            EnsureActive();
            return _ecsRecord.Span;
        }
    }

    internal ReadOnlySpan<byte> ScenesRecord
    {
        get
        {
            EnsureActive();
            return _scenesRecord.Span;
        }
    }

    internal static ReadOnlySpan<byte> SelectResource(
        ReadOnlySpan<byte> ecsRecord,
        IWorldResourceRegistration registration)
    {
        var root = new EbinRecordReadView(ecsRecord);
        var rows = root.GetField(2);
        ReadOnlySpan<byte> payload = default;
        var matches = 0;

        for (var index = 0; index < checked((int)rows.Count); index++)
        {
            var row = root.GetRecordArrayElement(3, index);
            var id = new Guid(row.GetField(0).Payload, bigEndian: true);

            if (id == registration.ResourceId)
            {
                var record = row.GetRecord(2);
                var header = record.Header;

                if ((header.SchemaId != registration.Schema.SchemaId)
                    || (header.Major != registration.Schema.Major) || (header.Minor != registration.Schema.Minor))
                {
                    throw new ArgumentException("The selected resource row has a different exact schema.", nameof(ecsRecord));
                }

                payload = record.EncodedBytes;
                matches++;
            }
        }

        return matches == 1
            ? payload
            : throw new ArgumentException("The selected resource must occur exactly once in the validated ECS source.", nameof(ecsRecord));
    }

    internal static ReadOnlySpan<byte> SelectWorldAttachment(
        ReadOnlySpan<byte> ecsRecord,
        IWorldAttachmentRegistration registration)
    {
        var root = new EbinRecordReadView(ecsRecord);
        var rows = root.GetField(3);
        ReadOnlySpan<byte> payload = default;
        var matches = 0;

        for (var index = 0; index < checked((int)rows.Count); index++)
        {
            var row = root.GetRecordArrayElement(4, index);
            var record = row.GetRecord(3);
            var header = record.Header;

            if ((header.SchemaId == registration.Descriptor.Schema.SchemaId)
                && (header.Major == registration.Descriptor.Schema.Major)
                && (header.Minor == registration.Descriptor.Schema.Minor)
                && !row.TryGetField(2, out _))
            {
                payload = record.EncodedBytes;
                matches++;
            }
        }

        return matches == 1
            ? payload
            : throw new ArgumentException("The selected World attachment must occur exactly once with no Entity owner.", nameof(ecsRecord));
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        if (_isProviderRead)
        {
            throw RecordFailure(new InvalidOperationException("A source read lease cannot be released during its provider callback."));
        }

        _sourceUse.Dispose();
        _ecsRecord = default;
        _scenesRecord = default;
        _isDisposed = true;
    }

    internal void EnterProviderRead()
    {
        EnsureActive();
        _control.EnterProviderRead(this);
        _isProviderRead = true;
    }

    internal void ExitProviderRead()
    {
        _control.ExitProviderRead(this);
        _isProviderRead = false;
    }

    internal ReadOnlySpan<byte> ReadAttachment(IWorldAttachmentRegistration registration)
    {
        EnsureActive();
        _control.ClaimAttachment(registration);
        return SelectWorldAttachment(EcsRecord, registration);
    }

    internal ReadOnlySpan<byte> ReadResource(IWorldResourceRegistration registration)
    {
        EnsureActive();
        _control.ClaimResource(registration);
        return SelectResource(EcsRecord, registration);
    }

    internal Exception RecordFailure(Exception exception) => _control.RecordFailure(exception);

    internal IDisposable RetainStorage()
    {
        EnsureActive();
        return _control.RetainStorage();
    }

    internal void ThrowIfFailed() => _control.ThrowIfFailed();

    private void EnsureActive()
    {
        if (_isDisposed || (Environment.CurrentManagedThreadId != _ownerThreadId))
        {
            throw RecordFailure(new InvalidOperationException("The prepared source lease is not active on its issuing thread."));
        }

        ThrowIfFailed();
    }
}
