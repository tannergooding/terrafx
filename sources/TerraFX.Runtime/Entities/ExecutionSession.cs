// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX.Entities;

internal static class ExecutionSession
{
    // A failed first initialization remains terminal instead of retrying with a different session ID.
    private static readonly Lazy<Guid> s_id = new(CreateId, LazyThreadSafetyMode.ExecutionAndPublication);

    internal static Guid ExecutionSessionId => s_id.Value;

    private static Guid CreateId()
    {
        var id = Guid.NewGuid();

        if (id == Guid.Empty)
        {
            throw new InvalidOperationException("The execution-session identity cannot be empty.");
        }

        return id;
    }
}
