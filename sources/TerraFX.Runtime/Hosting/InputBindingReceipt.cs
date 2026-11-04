// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Input;

namespace TerraFX.Hosting;

/// <summary>Reports the immutable result of binding an input router to one host route.</summary>
public sealed class InputBindingReceipt
{
    internal InputBindingReceipt(
        InputBindingStatus status,
        InputRouter? router,
        WindowHandle? window,
        Guid? worldGuid,
        Diagnostic? rejection)
    {
        Status = status;
        Router = router;
        Window = window;
        WorldGuid = worldGuid;
        Rejection = rejection;
    }

    /// <summary>Gets the stable diagnostic for a rejected binding, or null on success.</summary>
    public Diagnostic? Rejection { get; }

    /// <summary>Gets the bound router, or null when the request was rejected.</summary>
    public InputRouter? Router { get; }

    /// <summary>Gets the binding outcome.</summary>
    public InputBindingStatus Status { get; }

    /// <summary>Gets the selected host window, when the route is window-bound.</summary>
    public WindowHandle? Window { get; }

    /// <summary>Gets the stable identity of the targeted World, when supplied.</summary>
    public Guid? WorldGuid { get; }
}
