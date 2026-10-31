// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Entities;

internal readonly struct ComponentRecordRestoreResult
{
    private readonly ComponentInitialization? _initialization;

    private ComponentRecordRestoreResult(
        ComponentReadStatus status,
        ComponentInitialization? initialization,
        ImmutableArray<Diagnostic> diagnostics)
    {
        if (status is ComponentReadStatus.Read)
        {
            ArgumentNullException.ThrowIfNull(initialization);

            if (diagnostics.IsDefault || !diagnostics.IsEmpty)
            {
                throw new ArgumentException("A successful component restore must have initialized empty diagnostics.", nameof(diagnostics));
            }
        }
        else if (status is ComponentReadStatus.Rejected)
        {
            if (initialization is not null)
            {
                throw new ArgumentException("A rejected component restore cannot contain an initialization.", nameof(initialization));
            }

            if (diagnostics.IsDefaultOrEmpty)
            {
                throw new ArgumentException("A rejected component restore must retain diagnostics.", nameof(diagnostics));
            }

            foreach (var diagnostic in diagnostics)
            {
                ArgumentNullException.ThrowIfNull(diagnostic);
            }
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        _initialization = initialization;
        Status = status;
        Diagnostics = ImmutableArrayCopy.Create(diagnostics);
    }

    internal ImmutableArray<Diagnostic> Diagnostics { get; }

    internal ComponentReadStatus Status { get; }

    internal static ComponentRecordRestoreResult Read(ComponentInitialization initialization)
        => new(ComponentReadStatus.Read, initialization, []);

    internal static ComponentRecordRestoreResult Rejected(ImmutableArray<Diagnostic> diagnostics)
        => new(ComponentReadStatus.Rejected, initialization: null, diagnostics);

    internal bool TryGetInitialization(out ComponentInitialization? initialization)
    {
        initialization = (Status == ComponentReadStatus.Read) ? _initialization : null;
        return initialization is not null;
    }
}
