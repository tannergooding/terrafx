// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using TerraFX;
using TerraFX.Input;
using TerraFX.Numerics;

namespace TerraFX.Platform;

internal sealed class WindowsTextInputSessionState
{
    private WindowsTextInputSessionState(
        WindowHandle window,
        long sessionId,
        long documentRevision,
        TextInputPurpose purpose,
        TextInputHints hints,
        string? surroundingText,
        int selectionStart,
        int selectionLength,
        Vector2 caretPosition,
        Vector2 caretSize,
        bool isProtected)
    {
        CaretPosition = caretPosition;
        CaretSize = caretSize;
        DocumentRevision = documentRevision;
        Hints = hints;
        IsProtected = isProtected;
        Purpose = purpose;
        SelectionLength = selectionLength;
        SelectionStart = selectionStart;
        SessionId = sessionId;
        SurroundingText = surroundingText;
        Window = window;
    }

    internal Vector2 CaretPosition { get; }

    internal Vector2 CaretSize { get; }

    internal long DocumentRevision { get; }

    internal TextInputHints Hints { get; }

    internal bool IsProtected { get; }

    internal TextInputPurpose Purpose { get; }

    internal int SelectionLength { get; }

    internal int SelectionStart { get; }

    internal long SessionId { get; }

    internal string? SurroundingText { get; }

    internal WindowHandle Window { get; }

    internal static WindowsTextInputSessionState Create(TextInputSessionSnapshot snapshot)
    {
        var options = snapshot.Options;
        var isProtected = (options.Purpose is TextInputPurpose.Password)
            || ((options.Hints & TextInputHints.Sensitive) != 0);
        var hints = isProtected
            ? options.Hints & ~(TextInputHints.SpellCheck | TextInputHints.AutoCorrect)
            : options.Hints;

        return new WindowsTextInputSessionState(
            options.Window,
            snapshot.SessionId,
            options.DocumentRevision,
            options.Purpose,
            hints,
            isProtected ? null : options.SurroundingText,
            isProtected ? 0 : options.SelectionStart,
            isProtected ? 0 : options.SelectionLength,
            options.CaretPosition,
            options.CaretSize,
            isProtected);
    }
}
