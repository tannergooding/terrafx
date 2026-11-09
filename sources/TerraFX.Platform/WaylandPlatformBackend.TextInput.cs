// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;
using TerraFX.Input;

namespace TerraFX.Platform;

internal sealed partial class WaylandPlatformBackend
{
    public PlatformTextInputCapabilities UpdateTextInputSession(
        WindowHandle window,
        TextInputSessionSnapshot? snapshot)
    {
        EnsureOwnerThread();

        if (!window.IsValid || (window.OwnerId != _ownerId))
        {
            throw new ArgumentException("The text input session belongs to another Wayland platform owner.", nameof(window));
        }

        if (!_windowsByHandle.ContainsKey(window))
        {
            throw new InvalidOperationException("The text input session targets an inactive Wayland window.");
        }

        if (snapshot is not { } session)
        {
            return PlatformTextInputCapabilities.None;
        }

        if ((session.SessionId <= 0) || (session.Window != window))
        {
            throw new ArgumentException("The text input snapshot must name its exact session and window.", nameof(snapshot));
        }

        return (_keyboardKeymap is null)
            ? PlatformTextInputCapabilities.None
            : PlatformTextInputCapabilities.CommittedText;
    }
}
