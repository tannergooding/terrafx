// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
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

    public Type ValueType => typeof(T);

    internal WorldAttachmentFactoryBinding<T, TAuthor> Binding => _binding;

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

    public ImmutableArray<Diagnostic> Validate(World world, object value)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(value);

        if (value is not T attachment)
        {
            throw new ArgumentException("The transferred attachment does not match its registered CLR type.", nameof(value));
        }

        var context = new ValidationContext(world, _binding, attachment, null);
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

    public void ValidateEntityReferences(World world, object value, Entity excludedEntity)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(value);

        if (value is not T attachment)
        {
            throw new ArgumentException("The attachment does not match its registered CLR type.", nameof(value));
        }

        var context = new ValidationContext(world, _binding, attachment, excludedEntity);

        try
        {
            context.ValidateRuntimeFields(attachment);
        }
        finally
        {
            context.Revoke();
        }
    }

    private sealed class ValidationContext : IWorldAttachmentValidationContext
    {
        private readonly WorldAttachmentFactoryBinding<T, TAuthor> _binding;
        private readonly Entity? _excludedEntity;
        private Exception? _failure;
        private int _isActive = 1;
        private readonly T _value;
        private readonly World _world;

        internal ValidationContext(
            World world,
            WorldAttachmentFactoryBinding<T, TAuthor> binding,
            T value,
            Entity? excludedEntity)
        {
            _world = world;
            _binding = binding;
            _excludedEntity = excludedEntity;
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

            _world.ValidateAttachmentEntityReference(entity);

            if ((_excludedEntity is { } excludedEntity) && entity.Equals(excludedEntity))
            {
                throw new InvalidOperationException("A live attachment reference prevents destruction of its target Entity.");
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
        {
            foreach (var field in _binding.Fields)
            {
                field.ValidateRuntime(attachment, this);
            }
        }
    }
}
