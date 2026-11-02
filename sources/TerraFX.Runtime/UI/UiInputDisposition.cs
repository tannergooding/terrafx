// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Input;

namespace TerraFX.UI;

/// <summary>Describes the physical input classes captured by one UI routing result.</summary>
public readonly record struct UiInputDisposition
{
    private const InputClass AllClasses = InputClass.Keyboard | InputClass.Pointing | InputClass.Gamepad;

    /// <summary>Initializes one immutable event-time UI capture result.</summary>
    /// <param name="capturedClasses">The physical input classes captured for this event.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capturedClasses"/> contains an unknown class.</exception>
    internal UiInputDisposition(InputClass capturedClasses)
    {
        if ((capturedClasses & ~AllClasses) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capturedClasses));
        }

        CapturedClasses = capturedClasses;
    }

    /// <summary>Gets the physical input classes captured for this event.</summary>
    public InputClass CapturedClasses { get; }
}
