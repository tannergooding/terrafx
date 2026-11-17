// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace TerraFX.Entities;

internal sealed class WorldAttachmentRegistration<T, TAuthor> : IWorldAttachmentRegistration
    where T : class
    where TAuthor : notnull
{
    private readonly WorldAttachmentFactoryBinding<T, TAuthor> _binding;

    internal WorldAttachmentRegistration(AttachmentScope scope, WorldAttachmentFactoryBinding<T, TAuthor> binding)
    {
        if (scope is not AttachmentScope.World and not AttachmentScope.Entity)
        {
            throw new ArgumentOutOfRangeException(nameof(scope));
        }

        ArgumentNullException.ThrowIfNull(binding);
        Scope = scope;
        _binding = binding;
    }

    public WorldAttachmentFactoryDescriptor Descriptor => _binding.Descriptor;

    public AttachmentScope Scope { get; }

    public Type SourceAuthorType => typeof(TAuthor);

    public Type ValueType => typeof(T);

    internal WorldAttachmentFactoryBinding<T, TAuthor> Binding => _binding;

    public nint CountStateRecord(
        object value,
        Guid? ownerEntityId,
        WorldAttachmentCaptureBinding binding,
        CancellationToken cancellationToken)
    {
        var attachment = ValidateCaptureOperation(value, ownerEntityId, binding);
        var count = _binding.Provider.Count(attachment, binding, cancellationToken);

        if (binding.Failure is { } failure)
        {
            throw failure;
        }

        if (count < 0)
        {
            throw new InvalidOperationException("The attachment provider returned a negative encoded byte count.");
        }

        return count;
    }

    public object ReadStateRecord(
        scoped ReadOnlySpan<byte> source,
        EbinReadLimits limits,
        CancellationToken cancellationToken)
    {
        var result = ReadStateRecordResult(source, limits, cancellationToken);

        if (result.Status is WorldAttachmentReadStatus.Rejected)
        {
            var diagnostic = result.Diagnostics[0];
            throw new System.IO.InvalidDataException($"{diagnostic.Code}: {diagnostic.Message}");
        }

        return result.TryGetAuthorValue(out var author)
            ? author
            : throw new InvalidOperationException("The attachment state read omitted its immutable author.");
    }

    public void Release(object value, WorldAttachmentReleaseBinding binding)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(binding);

        if (value is not T attachment)
        {
            throw new ArgumentException("The released attachment does not match its registered CLR type.", nameof(value));
        }

        _binding.Provider.Release(attachment, binding);
    }

    public object RestoreState(
        object author,
        WorldAttachmentRestoreBinding binding,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(author);
        ArgumentNullException.ThrowIfNull(binding);

        if (author is not TAuthor typedAuthor)
        {
            throw new InvalidOperationException("The attachment restore author has the wrong type.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return _binding.Provider.Restore(in typedAuthor, binding, cancellationToken)
            ?? throw new InvalidOperationException("The attachment restore provider returned null.");
    }

    [SuppressMessage("Usage", "CA1031:Do not catch general exception types", Justification = "The exact source operation preserves its first sticky callback failure even when provider code catches it.")]
    public bool TryReadWorldSource(
        IWorldRestoreSourceOwner owner,
        out IWorldPreparedAttachmentSource? source,
        out ImmutableArray<Diagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        cancellationToken.ThrowIfCancellationRequested();
        source = null;
        diagnostics = [];
        using var lease = owner.AcquireRead();
        var payload = lease.ReadAttachment(this);
        WorldAttachmentReadResult<TAuthor> result;
        lease.EnterProviderRead();

        try
        {
            result = ReadStateRecordResult(payload, owner.Limits, cancellationToken);
        }
        catch (Exception exception)
        {
            throw lease.RecordFailure(exception);
        }
        finally
        {
            lease.ExitProviderRead();
        }

        lease.ThrowIfFailed();
        cancellationToken.ThrowIfCancellationRequested();

        if (result.Status is WorldAttachmentReadStatus.Rejected)
        {
            diagnostics = result.Diagnostics;
            return false;
        }

        if (!result.TryGetAuthorValue(out var author))
        {
            throw lease.RecordFailure(new InvalidOperationException("The selected World attachment reader omitted its immutable author."));
        }

        var ownership = lease.RetainStorage();

        try
        {
            source = new WorldAttachmentSource<TAuthor>(this, owner, author, ownership);
            ownership = null;
            return true;
        }
        finally
        {
            ownership?.Dispose();
        }
    }

    public ImmutableArray<Diagnostic> Validate(
        World world,
        object value,
        WorldAttachmentMembershipSource membershipSource)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(membershipSource);

        if (value is not T attachment)
        {
            throw new ArgumentException("The transferred attachment does not match its registered CLR type.", nameof(value));
        }

        var context = new ValidationContext(
            _binding,
            attachment,
            membershipSource,
            excludedEntity: null,
            excludedEntities: null);
        var scopedBinding = new WorldAttachmentValidationBinding(context);

        try
        {
            context.ValidateRuntimeFields(attachment);
            var diagnostics = _binding.Provider.Validate(attachment, scopedBinding, CancellationToken.None);

            if (context.Failure is { } failure)
            {
                throw failure;
            }

            if (diagnostics.IsDefault)
            {
                throw new InvalidOperationException("The attachment provider returned a default validation diagnostics array.");
            }

            membershipSource.ValidateForCommit();

            foreach (var diagnostic in diagnostics)
            {
                ArgumentNullException.ThrowIfNull(diagnostic);
            }

            return ImmutableArrayCopy.Create(diagnostics);
        }
        finally
        {
            scopedBinding.Revoke();
            context.Revoke();
        }
    }

    public void ValidateEntityReferences(
        World world,
        object value,
        Entity excludedEntity,
        WorldAttachmentMembershipSource membershipSource)
        => ValidateEntityReferences(world, value, membershipSource, excludedEntity, excludedEntities: null);

    public void ValidateEntityReferences(
        World world,
        object value,
        IReadOnlySet<Entity> excludedEntities,
        WorldAttachmentMembershipSource membershipSource)
    {
        ArgumentNullException.ThrowIfNull(excludedEntities);
        ValidateEntityReferences(world, value, membershipSource, excludedEntity: null, excludedEntities: excludedEntities);
    }

    public void ValidateEntityReferences(
        World world,
        object value,
        WorldAttachmentMembershipSource membershipSource)
        => ValidateEntityReferences(world, value, membershipSource, excludedEntity: null, excludedEntities: null);

    public nint WriteStateRecord(
        object value,
        Guid? ownerEntityId,
        scoped Span<byte> destination,
        WorldAttachmentCaptureBinding binding,
        CancellationToken cancellationToken)
    {
        var attachment = ValidateCaptureOperation(value, ownerEntityId, binding);
        var written = _binding.Provider.Write(attachment, destination, binding, cancellationToken);

        if (binding.Failure is { } failure)
        {
            throw failure;
        }

        if ((written < 0) || (written > destination.Length))
        {
            throw new InvalidOperationException("The attachment provider reported an invalid encoded byte count.");
        }

        return written;
    }

    private WorldAttachmentReadResult<TAuthor> ReadStateRecordResult(
        scoped ReadOnlySpan<byte> source,
        EbinReadLimits limits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(limits);
        cancellationToken.ThrowIfCancellationRequested();
        var binding = new WorldAttachmentReadBinding<TAuthor>(Descriptor.Schema, source.Length, limits);

        try
        {
            var result = binding.ValidateComplete(_binding.Provider.Read(source, binding, cancellationToken));

            if (binding.Failure is { } failure)
            {
                throw failure;
            }

            if (result.Status is WorldAttachmentReadStatus.Rejected)
            {
                if ((result.ConsumedBytes != 0) || result.Diagnostics.IsDefaultOrEmpty
                    || result.TryGetAuthorValue(out _))
                {
                    throw new InvalidOperationException("The rejected attachment state read returned an inconsistent result.");
                }
            }
            else if ((result.Status is not WorldAttachmentReadStatus.Read)
                || (result.ConsumedBytes != source.Length) || result.Diagnostics.IsDefault
                || !result.Diagnostics.IsEmpty || !result.TryGetAuthorValue(out var author))
            {
                throw new InvalidOperationException("The successful attachment state read did not return its complete author value.");
            }
            else
            {
                try
                {
                    _binding.ValidateAuthor(in author);
                }
                catch (Exception exception) when (exception is ArgumentException or System.IO.InvalidDataException)
                {
                    throw new InvalidOperationException("The successful attachment reader returned an author outside its captured family contract.", exception);
                }
            }

            return result;
        }
        finally
        {
            binding.Revoke();
        }
    }

    private T ValidateCaptureOperation(
        object value,
        Guid? ownerEntityId,
        WorldAttachmentCaptureBinding binding)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(binding);

        if (value is not T attachment)
        {
            throw new ArgumentException("The captured attachment does not match its registered CLR type.", nameof(value));
        }

        if (Scope == AttachmentScope.World != (ownerEntityId is null) || ownerEntityId == Guid.Empty)
        {
            throw new ArgumentException("The attachment owner identity does not match its registered scope.", nameof(ownerEntityId));
        }

        if (!binding.IsBoundTo(Descriptor))
        {
            throw new ArgumentException("The attachment capture binding belongs to another registered family.", nameof(binding));
        }

        return attachment;
    }

    private void ValidateEntityReferences(
        World world,
        object value,
        WorldAttachmentMembershipSource membershipSource,
        Entity? excludedEntity,
        IReadOnlySet<Entity>? excludedEntities)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(membershipSource);

        if (value is not T attachment)
        {
            throw new ArgumentException("The attachment does not match its registered CLR type.", nameof(value));
        }

        var context = new ValidationContext(_binding, attachment, membershipSource, excludedEntity, excludedEntities);

        try
        {
            context.ValidateRuntimeFields(attachment);
            membershipSource.ValidateForCommit();
        }
        finally
        {
            context.Revoke();
        }
    }

    private sealed class ValidationContext : IWorldAttachmentValidationContext
    {
        private readonly WorldAttachmentFactoryBinding<T, TAuthor> _binding;
        private readonly IReadOnlySet<Entity>? _excludedEntities;
        private readonly Entity? _excludedEntity;
        private Exception? _failure;
        private int _isActive = 1;
        private readonly WorldAttachmentMembershipSource _membershipSource;
        private readonly T _value;

        internal ValidationContext(
            WorldAttachmentFactoryBinding<T, TAuthor> binding,
            T value,
            WorldAttachmentMembershipSource membershipSource,
            Entity? excludedEntity,
            IReadOnlySet<Entity>? excludedEntities)
        {
            _binding = binding;
            _membershipSource = membershipSource;
            _excludedEntity = excludedEntity;
            _excludedEntities = excludedEntities;
            _value = value;
            OwnerThreadId = Environment.CurrentManagedThreadId;
        }

        public Exception? Failure => Volatile.Read(ref _failure);

        public bool IsActive => Volatile.Read(ref _isActive) != 0;

        public int OwnerThreadId { get; }

        public Exception RecordFailure(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
            return Interlocked.CompareExchange(ref _failure, exception, null) ?? exception;
        }

        public void ValidateEntity(Entity entity)
        {
            if (!IsActive || (Environment.CurrentManagedThreadId != OwnerThreadId))
            {
                throw RecordFailure(new InvalidOperationException("The attachment validation context is expired or used from another thread."));
            }

            try
            {
                _membershipSource.ValidateEntity(entity, _excludedEntity, _excludedEntities);
            }
            catch (Exception exception)
            {
                throw RecordFailure(exception);
            }
        }

        [SuppressMessage(
            "Usage",
            "CA1031:Do not catch general exception types",
            Justification = "A stable-ID attachment reference failure must remain sticky if a provider catches it.")]
        public void ValidateEntityId(Guid entityId)
        {
            if (!IsActive || (Environment.CurrentManagedThreadId != OwnerThreadId))
            {
                throw RecordFailure(new InvalidOperationException("The attachment validation context is expired or used from another thread."));
            }

            try
            {
                _membershipSource.ValidateEntityId(entityId, _excludedEntity, _excludedEntities);
            }
            catch (Exception exception)
            {
                throw RecordFailure(exception);
            }
        }

        public ImmutableArray<Diagnostic> ValidateFields<TValue>(TValue value)
            where TValue : class
        {
            if ((value is not T attachment) || !ReferenceEquals(attachment, _value))
            {
                throw RecordFailure(new ArgumentException("The attachment validation binding accepts only its exact operation value.", nameof(value)));
            }

            ValidateRuntimeFields(attachment);
            return [];
        }

        internal void Revoke()
            => Volatile.Write(ref _isActive, 0);

        internal void ValidateRuntimeFields(T attachment)
            => _binding.ValidateRuntime(attachment, this);
    }
}
