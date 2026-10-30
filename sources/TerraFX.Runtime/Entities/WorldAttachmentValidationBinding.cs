// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Entities;

/// <summary>Provides scoped schema-field validation access for one attachment provider.</summary>
public sealed class WorldAttachmentValidationBinding
{
    private readonly IWorldAttachmentValidationContext _context;
    private bool _isActive = true;

    internal WorldAttachmentValidationBinding(IWorldAttachmentValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <summary>Validates any common schema-field constraints for one immutable attachment value.</summary>
    /// <typeparam name="T">The attachment CLR type.</typeparam>
    /// <param name="value">The value whose declared fields are checked.</param>
    /// <returns>The initialized immutable diagnostics for invalid declared fields.</returns>
    /// <exception cref="ArgumentException"><typeparamref name="T"/> or the value does not match this attachment operation.</exception>
    /// <exception cref="InvalidOperationException">The binding is expired, used from another thread, or the context returned invalid diagnostics.</exception>
    public ImmutableArray<Diagnostic> ValidateFields<T>(T value)
        where T : class
    {
        EnsureActive();

        try
        {
            ArgumentNullException.ThrowIfNull(value);
            var diagnostics = _context.ValidateFields(value);

            if (diagnostics.IsDefault)
            {
                throw new InvalidOperationException("The attachment validation context returned a default diagnostics array.");
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
            throw _context.RecordFailure(new InvalidOperationException("The World attachment validation binding has expired."));
        }

        if (Environment.CurrentManagedThreadId != _context.OwnerThreadId)
        {
            throw _context.RecordFailure(new InvalidOperationException("The World attachment validation binding is restricted to its issuing thread."));
        }
    }
}
