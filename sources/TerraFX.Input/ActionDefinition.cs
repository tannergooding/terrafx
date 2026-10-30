// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Input;

/// <summary>Defines one action in an input action map.</summary>
public sealed class ActionDefinition
{
    /// <summary>Initializes an action definition.</summary>
    /// <param name="actionId">The stable action identifier.</param>
    /// <param name="name">The display/debug name.</param>
    /// <param name="kind">The action value shape.</param>
    /// <param name="downPressThreshold">The optional axis press threshold.</param>
    /// <param name="downReleaseThreshold">The optional axis release threshold.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    public ActionDefinition(
        Guid actionId,
        string name,
        InputActionKind kind,
        float? downPressThreshold = null,
        float? downReleaseThreshold = null)
    {
        ArgumentNullException.ThrowIfNull(name);

        ActionId = actionId;
        Name = name;
        Kind = kind;
        DownPressThreshold = downPressThreshold;
        DownReleaseThreshold = downReleaseThreshold;
    }

    /// <summary>Gets the stable action identifier.</summary>
    public Guid ActionId { get; }

    /// <summary>Gets the press threshold for an axis action.</summary>
    public float? DownPressThreshold { get; }

    /// <summary>Gets the release threshold for an axis action.</summary>
    public float? DownReleaseThreshold { get; }

    /// <summary>Gets the action value shape.</summary>
    public InputActionKind Kind { get; }

    /// <summary>Gets the display/debug name.</summary>
    public string Name { get; }
}
