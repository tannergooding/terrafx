// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Threading;

namespace TerraFX.Entities;

internal sealed class WorldSystemFactoryStatefulFamily<TSystem, TAuthor, TCreate, TDefaults, TRelease> :
    IWorldSystemFactoryDispatch,
    IWorldSystemCreateControl,
    IWorldSystemDefaultsControl,
    IWorldSystemReleaseControl,
    IWorldSystemValidationControl
    where TSystem : class, IWorldSystem
    where TAuthor : notnull
    where TCreate : struct, IWorldSystemCreate<TSystem>
    where TDefaults : struct, IWorldSystemDefaults<TSystem>
    where TRelease : struct, IWorldSystemRelease<TSystem>
{
    private readonly IWorldSystemStateCodec<TSystem, TAuthor> _codec;
    private readonly TCreate _create;
    private readonly TDefaults _defaults;
    private readonly ImmutableArray<WorldSystemFieldBinding<TSystem, TAuthor>> _fields;
    private readonly TRelease _release;

    internal WorldSystemFactoryStatefulFamily(
        WorldSystemFactoryDescriptor descriptor,
        ImmutableArray<WorldSystemFieldBinding<TSystem, TAuthor>> fields,
        TCreate create,
        TDefaults defaults,
        IWorldSystemStateCodec<TSystem, TAuthor> codec,
        TRelease release)
    {
        Descriptor = descriptor;
        _fields = fields;
        _create = create;
        _defaults = defaults;
        _codec = codec;
        _release = release;
    }

    public IWorldSystemCreateControl CreateControl => this;

    public IWorldSystemDefaultsControl DefaultsControl => this;

    public WorldSystemFactoryDescriptor Descriptor { get; }

    public IWorldSystemReleaseControl ReleaseControl => this;

    public IWorldSystemValidationControl ValidationControl => this;

    private static TSystem ValidateSystem(IWorldSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);

        return system is TSystem typedSystem
            ? typedSystem
            : throw new InvalidOperationException("The World system instance does not match the captured closed type.");
    }

    IWorldSystem IWorldSystemCreateControl.Invoke()
        => _create.Invoke() ?? throw new InvalidOperationException("The system creation callback returned null.");

    void IWorldSystemDefaultsControl.Invoke(IWorldSystem system)
        => _defaults.Invoke(ValidateSystem(system));

    void IWorldSystemReleaseControl.Invoke(IWorldSystem system)
        => _release.Invoke(ValidateSystem(system));

    void IWorldSystemValidationControl.Validate(IWorldSystem system, Guid systemId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(systemId, Guid.Empty);
        var typedSystem = ValidateSystem(system);
        var context = new ValidationContext(Descriptor, _fields, typedSystem, systemId);
        var binding = new WorldSystemValidationBinding(context);

        try
        {
            var diagnostics = _codec.Validate(typedSystem, binding, CancellationToken.None);

            if (context.Failure is { } failure)
            {
                throw failure;
            }

            if (diagnostics.IsDefault)
            {
                throw new InvalidOperationException("The system codec returned a default diagnostics array.");
            }

            foreach (var diagnostic in diagnostics)
            {
                ArgumentNullException.ThrowIfNull(diagnostic);

                if (diagnostic.Severity >= DiagnosticSeverity.Error)
                {
                    throw new InvalidOperationException("A system default instance failed codec validation.");
                }
            }
        }
        finally
        {
            binding.Revoke();
            context.Revoke();
        }
    }

    private sealed class ValidationContext : IWorldSystemValidationContext
    {
        private readonly WorldSystemFactoryDescriptor _descriptor;
        private Exception? _failure;
        private readonly ImmutableArray<WorldSystemFieldBinding<TSystem, TAuthor>> _fields;
        private bool _isActive = true;
        private readonly int _ownerThreadId;
        private readonly TSystem _system;
        private readonly Guid _systemId;

        internal ValidationContext(
            WorldSystemFactoryDescriptor descriptor,
            ImmutableArray<WorldSystemFieldBinding<TSystem, TAuthor>> fields,
            TSystem system,
            Guid systemId)
        {
            ArgumentOutOfRangeException.ThrowIfEqual(systemId, Guid.Empty);
            _descriptor = descriptor;
            _fields = fields;
            _system = system;
            _systemId = systemId;
            _ownerThreadId = Environment.CurrentManagedThreadId;
        }

        public Exception? Failure => _failure;

        public bool IsActive => _isActive;

        public int OwnerThreadId => _ownerThreadId;

        public ContentSchemaReference Schema => _descriptor.StateSchema
            ?? throw new InvalidOperationException("A stateless system has no validation schema.");

        public Guid SystemId => _systemId;

        public Exception RecordFailure(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
            _failure ??= exception;
            return _failure;
        }

        public ImmutableArray<Diagnostic> ValidateFields<TValue>(TValue system)
            where TValue : class, IWorldSystem
        {
            EnsureActive();

            if ((typeof(TValue) != typeof(TSystem)) || !ReferenceEquals(system, _system))
            {
                throw RecordFailure(new ArgumentException("The system does not match this validation operation.", nameof(system)));
            }

            if (system is not TSystem typedSystem)
            {
                throw RecordFailure(new ArgumentException("The system does not match its exact closed CLR type.", nameof(system)));
            }

            foreach (var field in _fields)
            {
                if (field.Descriptor.StateSchema != Schema)
                {
                    throw RecordFailure(new InvalidOperationException("The selected validation field belongs to another SystemState schema."));
                }

                field.ValidateRuntime(in typedSystem);
            }

            return [];
        }

        internal void Revoke() => _isActive = false;

        private void EnsureActive()
        {
            if (!_isActive)
            {
                throw RecordFailure(new InvalidOperationException("The World-system validation context has expired."));
            }

            if (Environment.CurrentManagedThreadId != _ownerThreadId)
            {
                throw RecordFailure(new InvalidOperationException("The World-system validation context is restricted to its issuing thread."));
            }
        }
    }
}
