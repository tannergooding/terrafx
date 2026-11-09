// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using NUnit.Framework;
using TerraFX;
using TerraFX.Input;
using TerraFX.Numerics;

namespace TerraFX.Platform.Tests;

[TestFixture]
internal sealed class WindowsTextInputSessionStateTests
{
    [Test]
    public void CreatesProtectedStateWithoutSensitiveSnapshotOrAutocorrectionHints()
    {
        var window = new WindowHandle(1, index: 0, generation: 1);
        var options = new TextInputSessionOptions(
            window,
            TextInputPurpose.Password,
            documentRevision: 1,
            surroundingText: "password",
            selectionStart: 2,
            selectionLength: 3,
            caretPosition: default,
            caretSize: default) {
            Hints = TextInputHints.SpellCheck | TextInputHints.AutoCorrect | TextInputHints.Sensitive,
        };

        var state = WindowsTextInputSessionState.Create(new TextInputSessionSnapshot(1, options));

        Assert.That(state.IsProtected, Is.True);
        Assert.That(state.SurroundingText, Is.Null);
        Assert.That(state.SelectionStart, Is.Zero);
        Assert.That(state.SelectionLength, Is.Zero);
        Assert.That(state.Hints, Is.EqualTo(TextInputHints.Sensitive));
    }

    [Test]
    public void PreservesNonSensitiveSnapshotAndSelection()
    {
        var window = new WindowHandle(2, index: 0, generation: 1);
        var options = new TextInputSessionOptions(
            window,
            TextInputPurpose.FreeForm,
            documentRevision: 3,
            surroundingText: "hello",
            selectionStart: 1,
            selectionLength: 2,
            caretPosition: new Vector2(4.0f, 5.0f),
            caretSize: new Vector2(1.0f, 2.0f)) {
            Hints = TextInputHints.SpellCheck | TextInputHints.Multiline,
        };

        var state = WindowsTextInputSessionState.Create(new TextInputSessionSnapshot(7, options));

        Assert.That(state.IsProtected, Is.False);
        Assert.That(state.SurroundingText, Is.EqualTo("hello"));
        Assert.That(state.SelectionStart, Is.EqualTo(1));
        Assert.That(state.SelectionLength, Is.EqualTo(2));
        Assert.That(state.CaretPosition, Is.EqualTo(new Vector2(4.0f, 5.0f)));
        Assert.That(state.CaretSize, Is.EqualTo(new Vector2(1.0f, 2.0f)));
        Assert.That(state.Hints, Is.EqualTo(TextInputHints.SpellCheck | TextInputHints.Multiline));
    }
}
