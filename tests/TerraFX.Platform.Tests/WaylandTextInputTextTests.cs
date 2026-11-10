// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Text;
using NUnit.Framework;
using TerraFX;
using TerraFX.Input;

namespace TerraFX.Platform.Tests;

[TestFixture]
internal sealed class WaylandTextInputTextTests
{
    private static WaylandTextInputSurroundingText GetSurroundingText(
        string text,
        int selectionStart,
        int selectionLength)
        => WaylandTextInputText.CreateSurroundingText(text, selectionStart, selectionLength)
            ?? throw new InvalidOperationException("The test input did not produce a surrounding-text window.");

    [Test]
    public void CompositionCommitEndsBeforeReplacementPreeditStarts()
    {
        var changes = WaylandTextInputText.CreateCompositionChanges(
            compositionActive: true,
            committedText: "\u00E9",
            hasPreedit: true,
            preeditText: "\u6F22",
            cursorBegin: 0,
            cursorEnd: 3,
            out var compositionActive);

        Assert.That(changes, Has.Length.EqualTo(4));
        Assert.That(changes[0].Kind, Is.EqualTo(TextInputChangeKind.TextCommitted));
        Assert.That(changes[1].Kind, Is.EqualTo(TextInputChangeKind.CompositionEnded));
        Assert.That(changes[2].Kind, Is.EqualTo(TextInputChangeKind.CompositionStarted));
        Assert.That(changes[3].Kind, Is.EqualTo(TextInputChangeKind.CompositionUpdated));
        Assert.That(changes[3].Text, Is.EqualTo("\u6F22"));
        Assert.That(compositionActive, Is.True);
    }

    [Test]
    public void CompositionUpdateStartsAndUpdatesPreedit()
    {
        var changes = WaylandTextInputText.CreateCompositionChanges(
            compositionActive: false,
            committedText: null,
            hasPreedit: true,
            preeditText: "pre",
            cursorBegin: 1,
            cursorEnd: 2,
            out var compositionActive);

        Assert.That(changes, Has.Length.EqualTo(2));
        Assert.That(changes[0].Kind, Is.EqualTo(TextInputChangeKind.CompositionStarted));
        Assert.That(changes[1].Kind, Is.EqualTo(TextInputChangeKind.CompositionUpdated));
        Assert.That(changes[1].SelectionStart, Is.EqualTo(1));
        Assert.That(changes[1].SelectionLength, Is.EqualTo(1));
        Assert.That(compositionActive, Is.True);
    }

    [Test]
    public void DeleteSurroundingMapsUtf8LengthsToAbsoluteUtf16Range()
    {
        const string Source = "A\uD83D\uDE00B";
        var surrounding = GetSurroundingText(Source, selectionStart: 3, selectionLength: 0);

        Assert.That(
            WaylandTextInputText.TryMapDelete(
                Source.Length,
                surrounding,
                beforeLength: 5,
                afterLength: 1,
                out var editStart,
                out var deleteLength),
            Is.True);
        Assert.That(editStart, Is.Zero);
        Assert.That(deleteLength, Is.EqualTo(Source.Length));
    }

    [Test]
    public void DeleteSurroundingRejectsUtf8OffsetsInsideCodePoints()
    {
        const string Source = "\u00E9x";
        var surrounding = GetSurroundingText(Source, selectionStart: 1, selectionLength: 0);

        Assert.That(
            WaylandTextInputText.TryMapDelete(
                Source.Length,
                surrounding,
                beforeLength: 1,
                afterLength: 0,
                out _,
                out _),
            Is.False);
    }

    [Test]
    public void PreeditSelectionConvertsUtf8OffsetsToUtf16()
    {
        Assert.That(
            WaylandTextInputText.TryGetPreeditSelection(
                "A\uD83D\uDE00B",
                cursorBegin: 1,
                cursorEnd: 5,
                out var selectionStart,
                out var selectionLength),
            Is.True);
        Assert.That(selectionStart, Is.EqualTo(1));
        Assert.That(selectionLength, Is.EqualTo(2));
        Assert.That(
            WaylandTextInputText.TryGetPreeditSelection(
                "A\uD83D\uDE00B",
                cursorBegin: 2,
                cursorEnd: 5,
                out _,
                out _),
            Is.False);
    }

    [Test]
    public void ProtectedContentTypeSuppressesCorrectionHints()
    {
        var options = new TextInputSessionOptions(
            new WindowHandle(ownerId: 1, index: 0, generation: 1),
            TextInputPurpose.Password,
            documentRevision: 1,
            surroundingText: "secret",
            selectionStart: 2,
            selectionLength: 0,
            caretPosition: default,
            caretSize: default) {
            Hints = TextInputHints.SpellCheck | TextInputHints.AutoCorrect,
        };
        var (hint, purpose) = WaylandPlatformBackend.GetTextInputContentType(options);

        Assert.That(purpose, Is.EqualTo(8u));
        Assert.That(hint, Is.EqualTo(0xC0u));
    }

    [Test]
    public void ProtectedSessionsOmitNativeSurroundingText()
    {
        var window = new WindowHandle(ownerId: 1, index: 0, generation: 1);
        var password = new TextInputSessionOptions(
            window,
            TextInputPurpose.Password,
            documentRevision: 1,
            surroundingText: "secret",
            selectionStart: 2,
            selectionLength: 0,
            caretPosition: default,
            caretSize: default);
        var sensitive = new TextInputSessionOptions(
            window,
            TextInputPurpose.FreeForm,
            documentRevision: 1,
            surroundingText: "secret",
            selectionStart: 2,
            selectionLength: 0,
            caretPosition: default,
            caretSize: default) {
            Hints = TextInputHints.Sensitive,
        };

        Assert.That(WaylandTextInputText.GetNativeSurroundingText(password), Is.Null);
        Assert.That(WaylandTextInputText.GetNativeSurroundingText(sensitive), Is.Null);
    }

    [Test]
    public void SurroundingTextUsesUtf8ByteOffsetsForUtf16Selections()
    {
        var surrounding = GetSurroundingText("A\uD83D\uDE00B", selectionStart: 1, selectionLength: 2);

        Assert.That(surrounding.Text, Is.EqualTo("A\uD83D\uDE00B"));
        Assert.That(surrounding.CursorByteOffset, Is.EqualTo(1));
        Assert.That(surrounding.AnchorByteOffset, Is.EqualTo(5));
    }

    [Test]
    public void SurroundingTextWindowIsBoundedAndPreservesTheSelection()
    {
        var source = string.Concat(new string('a', 3000), "\uD83D\uDE00", new string('b', 2000));
        var surrounding = GetSurroundingText(source, selectionStart: 3000, selectionLength: 2);

        Assert.That(Encoding.UTF8.GetByteCount(surrounding.Text), Is.EqualTo(WaylandTextInputText.MaximumSurroundingTextBytes));
        Assert.That(surrounding.Text, Does.Contain("\uD83D\uDE00"));
        Assert.That(surrounding.SnapshotUtf16Offset, Is.LessThan(3000));
        Assert.That(
            surrounding.Text,
            Is.EqualTo(source.Substring(surrounding.SnapshotUtf16Offset, surrounding.Text.Length)));
    }
}
