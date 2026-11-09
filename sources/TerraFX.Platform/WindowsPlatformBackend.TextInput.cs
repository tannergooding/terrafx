// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;
using TerraFX.Input;

namespace TerraFX.Platform;

internal sealed unsafe partial class WindowsPlatformBackend
{
    private WindowsTextInputManager? _textInputManager;

    public void UpdateTextInputSession(WindowHandle window, TextInputSessionSnapshot? snapshot)
    {
        EnsureOwnerThread();

        var state = FindWindowState(window)
            ?? throw new InvalidOperationException("The text input session targets a stale or inactive window.");

        if (snapshot is not { } session)
        {
            _textInputManager?.RemoveSession(window);
            return;
        }

        if ((session.SessionId <= 0) || (session.Window != window))
        {
            throw new ArgumentException("The text input snapshot must name its exact session and window.", nameof(snapshot));
        }

        if (state.IsClosing)
        {
            throw new InvalidOperationException("The text input session cannot update a closing window.");
        }

        var stateSnapshot = WindowsTextInputSessionState.Create(session);
        _textInputManager ??= new WindowsTextInputManager(_inputSamples, _timeProvider);
        _textInputManager.UpdateSession(window, state.NativeWindow, stateSnapshot, state.IsFocused);
    }
}
