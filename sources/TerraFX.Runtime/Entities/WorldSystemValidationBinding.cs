// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Entities;

/// <summary>Provides operation-scoped schema-field validation access for one system codec.</summary>
public sealed class WorldSystemValidationBinding
{
    private readonly IWorldSystemValidationContext _context;
    private bool _isActive = true;

    internal WorldSystemValidationBinding(IWorldSystemValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <summary>Gets the exact SystemState schema tuple.</summary>
    public ContentSchemaReference Schema
    {
        get
        {
            EnsureActive();
            return _context.Schema;
        }
    }

    /// <summary>Gets the stable identity of the system being validated.</summary>
    public Guid SystemId
    {
        get
        {
            EnsureActive();
            return _context.SystemId;
        }
    }

    /// <summary>Validates any common schema-field constraints for one exact system instance.</summary>
    /// <typeparam name="TSystem">The exact closed World system type.</typeparam>
    /// <param name="system">The system whose declared fields are checked.</param>
    /// <returns>The initialized immutable diagnostics for invalid declared fields.</returns>
    /// <exception cref="ArgumentException"><typeparamref name="TSystem"/> or the system instance does not match this registration.</exception>
    /// <exception cref="InvalidOperationException">The binding is expired, used from another thread, or the context returned invalid diagnostics.</exception>
    public ImmutableArray<Diagnostic> ValidateFields<TSystem>(TSystem system)
        where TSystem : class, IWorldSystem
    {
        EnsureActive();

        try
        {
            ArgumentNullException.ThrowIfNull(system);
            var diagnostics = _context.ValidateFields(system);

            if (diagnostics.IsDefault)
            {
                throw new InvalidOperationException("The system validation context returned a default diagnostics array.");
            }

            foreach (var diagnostic in diagnostics)
            {
                ArgumentNullException.ThrowIfNull(diagnostic);
            }

            return ImmutableArrayCopy.Create(diagnostics);
        }
        catch (Exception exception)
        {
            throw _context.RecordFailure(exception);
        }
    }

    internal void Revoke() => _isActive = false;

    private void EnsureActive()
    {
        if (!_isActive || !_context.IsActive)
        {
            throw _context.RecordFailure(new InvalidOperationException("The World-system validation binding has expired."));
        }

        if (Environment.CurrentManagedThreadId != _context.OwnerThreadId)
        {
            throw _context.RecordFailure(new InvalidOperationException("The World-system validation binding is restricted to its issuing thread."));
        }
    }
}
