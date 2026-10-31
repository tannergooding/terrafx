// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX;
using TerraFX.Input;
using TerraFX.Numerics;

namespace TerraFX.Input.Tests;

[TestFixture]
internal sealed class TextInputSessionOptionsTests
{
    private static readonly WindowHandle s_window = new(1, 0, 1);

    private static TextInputSessionOptions CreateOptions(
        string? text = null,
        int selectionStart = 0,
        int selectionLength = 0,
        TextInputPurpose purpose = TextInputPurpose.FreeForm,
        Vector2? caretPosition = null,
        Vector2? caretSize = null,
        TextInputHints hints = TextInputHints.None)
    {
        return new TextInputSessionOptions(
            s_window,
            purpose,
            1,
            text,
            selectionStart,
            selectionLength,
            caretPosition ?? Vector2.Zero,
            caretSize ?? Vector2.Zero) {
            Hints = hints,
        };
    }

    [Test]
    public void CopiesValidTextSelectionAndCaret()
    {
        var options = CreateOptions(
            "A\U0001F642B",
            selectionStart: 1,
            selectionLength: 2,
            caretPosition: new Vector2(12, 24),
            caretSize: new Vector2(1, 18),
            hints: TextInputHints.SpellCheck | TextInputHints.Multiline);

        Assert.That(options.Window, Is.EqualTo(s_window));
        Assert.That(options.DocumentRevision, Is.EqualTo(1));
        Assert.That(options.SurroundingText, Is.EqualTo("A\U0001F642B"));
        Assert.That(options.SelectionStart, Is.EqualTo(1));
        Assert.That(options.SelectionLength, Is.EqualTo(2));
        Assert.That(options.CaretPosition, Is.EqualTo(new Vector2(12, 24)));
        Assert.That(options.CaretSize, Is.EqualTo(new Vector2(1, 18)));
        Assert.That(options.Hints, Is.EqualTo(TextInputHints.SpellCheck | TextInputHints.Multiline));
    }

    [Test]
    public void RejectsInvalidWindowPurposeRevisionAndHints()
    {
        Assert.That(
            () => new TextInputSessionOptions(default, TextInputPurpose.FreeForm, 1, null, 0, 0, Vector2.Zero, Vector2.Zero),
            Throws.TypeOf<ArgumentException>());
        Assert.That(() => CreateOptions(purpose: (TextInputPurpose)byte.MaxValue), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(
            () => new TextInputSessionOptions(s_window, TextInputPurpose.FreeForm, 0, null, 0, 0, Vector2.Zero, Vector2.Zero),
            Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(
            () => new TextInputSessionOptions(s_window, TextInputPurpose.FreeForm, 1, null, 0, 0, Vector2.Zero, Vector2.Zero) {
                Hints = (TextInputHints)16,
            },
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void RejectsMalformedTextAndSelectionBoundaries()
    {
        Assert.That(() => CreateOptions("\uD800"), Throws.TypeOf<ArgumentException>());
        Assert.That(() => CreateOptions("\uDC00"), Throws.TypeOf<ArgumentException>());
        Assert.That(() => CreateOptions("abc", selectionStart: -1), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => CreateOptions("abc", selectionLength: -1), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => CreateOptions("abc", selectionStart: 2, selectionLength: 2), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => CreateOptions("A\U0001F642B", selectionStart: 2), Throws.TypeOf<ArgumentException>());
        Assert.That(() => CreateOptions("A\U0001F642B", selectionStart: 1, selectionLength: 1), Throws.TypeOf<ArgumentException>());
        Assert.That(
            () => new TextInputSessionOptions(s_window, TextInputPurpose.FreeForm, 1, null, 1, 0, Vector2.Zero, Vector2.Zero),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void RejectsOversizedTextAndInvalidCaret()
    {
        Assert.That(() => CreateOptions(new string('x', 65_537)), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => CreateOptions(caretPosition: new Vector2(float.NaN, 0)), Throws.TypeOf<ArgumentException>());
        Assert.That(() => CreateOptions(caretSize: new Vector2(-1, 0)), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => CreateOptions(caretSize: new Vector2(0, float.PositiveInfinity)), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void SurroundingTextLimitIsInclusive()
    {
        var text = new string('x', 65_536);
        var options = CreateOptions(text, selectionStart: text.Length);

        Assert.That(options.SurroundingText, Has.Length.EqualTo(65_536));
        Assert.That(options.SelectionStart, Is.EqualTo(65_536));
    }
}
