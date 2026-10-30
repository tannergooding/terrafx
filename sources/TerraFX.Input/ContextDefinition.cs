// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Input;

/// <summary>Defines one prioritized input context.</summary>
public sealed class ContextDefinition
{
    /// <summary>Initializes a context definition.</summary>
    /// <param name="contextId">The stable context identifier.</param>
    /// <param name="bit">The context-mask bit.</param>
    /// <param name="priority">The context priority.</param>
    /// <param name="captureClasses">The captured physical classes.</param>
    public ContextDefinition(Guid contextId, byte bit, int priority = 0, InputClass captureClasses = InputClass.None)
    {
        ContextId = contextId;
        Bit = bit;
        Priority = priority;
        CaptureClasses = captureClasses;
    }

    /// <summary>Gets the context-mask bit.</summary>
    public byte Bit { get; }

    /// <summary>Gets the captured physical classes.</summary>
    public InputClass CaptureClasses { get; }

    /// <summary>Gets the stable context identifier.</summary>
    public Guid ContextId { get; }

    /// <summary>Gets the context priority.</summary>
    public int Priority { get; }
}
