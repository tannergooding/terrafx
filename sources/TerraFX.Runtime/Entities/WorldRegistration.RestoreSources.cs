// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Threading;

namespace TerraFX.Entities;

public sealed partial class WorldRegistration
{
    internal bool TryReadResourceSource<TAuthor>(
        Guid resourceId,
        IWorldRestoreSourceOwner owner,
        out WorldResourceSource<TAuthor>? source,
        out ImmutableArray<Diagnostic> diagnostics,
        CancellationToken cancellationToken)
        where TAuthor : notnull
    {
        ArgumentNullException.ThrowIfNull(owner);
        cancellationToken.ThrowIfCancellationRequested();
        IWorldResourceRegistration? selected = null;

        using (var lease = owner.AcquireRead())
        {
            if (!ReferenceEquals(this, owner.Registration))
            {
                throw lease.RecordFailure(new InvalidOperationException("The source owner belongs to another frozen World registration."));
            }

            foreach (var registration in _resources)
            {
                if (registration.ResourceId == resourceId)
                {
                    selected = registration;
                    break;
                }
            }

            if ((selected is null) || (selected.SourceAuthorType != typeof(TAuthor)))
            {
                throw lease.RecordFailure(new InvalidOperationException("The selected resource or captured source author family is not registered."));
            }
        }

        if (!selected.TryReadSource(owner, out var prepared, out diagnostics, cancellationToken))
        {
            source = null;
            return false;
        }

        source = prepared as WorldResourceSource<TAuthor>
            ?? throw new InvalidOperationException("The registered resource dispatch returned another prepared source family.");
        return true;
    }

    internal bool TryReadWorldAttachmentSource<TAuthor>(
        ContentSchemaReference schema,
        IWorldRestoreSourceOwner owner,
        out WorldAttachmentSource<TAuthor>? source,
        out ImmutableArray<Diagnostic> diagnostics,
        CancellationToken cancellationToken)
        where TAuthor : notnull
    {
        ArgumentNullException.ThrowIfNull(owner);
        cancellationToken.ThrowIfCancellationRequested();
        IWorldAttachmentRegistration? selected = null;

        using (var lease = owner.AcquireRead())
        {
            if (!ReferenceEquals(this, owner.Registration))
            {
                throw lease.RecordFailure(new InvalidOperationException("The source owner belongs to another frozen World registration."));
            }

            foreach (var registration in _attachments)
            {
                if ((registration.Scope == AttachmentScope.World) && (registration.Descriptor.Schema == schema))
                {
                    selected = registration;
                    break;
                }
            }

            if ((selected is null) || (selected.SourceAuthorType != typeof(TAuthor)))
            {
                throw lease.RecordFailure(new InvalidOperationException("The selected World attachment or captured source author family is not registered."));
            }
        }

        if (!selected.TryReadWorldSource(owner, out var prepared, out diagnostics, cancellationToken))
        {
            source = null;
            return false;
        }

        source = prepared as WorldAttachmentSource<TAuthor>
            ?? throw new InvalidOperationException("The registered attachment dispatch returned another prepared source family.");
        return true;
    }
}
