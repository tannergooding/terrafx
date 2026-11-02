// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using NUnit.Framework;
using TerraFX;
using TerraFX.Input;
using TerraFX.Numerics;

namespace TerraFX.Input.Tests;

[TestFixture]
internal sealed class TextInputSessionTests
{
    private static readonly WindowHandle s_window = new(7, 0, 1);

    private static ImmutableArray<TextInputChange> CreateCompositionChanges(int count)
    {
        var changes = ImmutableArray.CreateBuilder<TextInputChange>(count);

        for (var index = 0; index < count; index++)
        {
            changes.Add(new TextInputChange(TextInputChangeKind.CompositionStarted));
        }

        return changes.MoveToImmutable();
    }

    private static TextInputSessionOptions CreateOptions(
        WindowHandle window,
        long revision,
        string? text,
        int selectionStart,
        int selectionLength)
        => new(window, TextInputPurpose.FreeForm, revision, text, selectionStart, selectionLength, Vector2.Zero, Vector2.Zero);

    private static InputRouter CreateRouter(HostEventSequenceLease sequenceLease, WindowHandle window)
        => new(
            InputRecipientKind.HostLiveUI,
            7,
            Environment.CurrentManagedThreadId,
            sequenceLease,
            window);

    [Test]
    public void BindsSessionToOneHostLiveWindowAndPreservesBatchesAfterDispose()
    {
        Assert.That(
            () => new InputRouter(
                InputRecipientKind.HostLiveUI,
                7,
                Environment.CurrentManagedThreadId,
                new HostEventSequenceLease()),
            Throws.TypeOf<ArgumentException>());

        var timelineRouter = new InputRouter(
            InputRecipientKind.SimulationTimeline,
            11,
            Environment.CurrentManagedThreadId,
            new HostEventSequenceLease());
        Assert.That(
            () => timelineRouter.BeginTextSession(CreateOptions(s_window, 1, null, 0, 0)),
            Throws.TypeOf<InvalidOperationException>());
        timelineRouter.Dispose();

        var sequenceLease = new HostEventSequenceLease();
        var router = CreateRouter(sequenceLease, s_window);
        var otherWindow = new WindowHandle(7, 1, 1);

        Assert.That(
            () => router.BeginTextSession(CreateOptions(otherWindow, 1, null, 0, 0)),
            Throws.TypeOf<ArgumentException>());

        var session = router.BeginTextSession(CreateOptions(s_window, 1, null, 0, 0));
        Assert.That(
            () => router.BeginTextSession(CreateOptions(s_window, 1, null, 0, 0)),
            Throws.TypeOf<InvalidOperationException>());

        _ = session.PublishNativeBatch(
            10,
            sequenceLease.Allocate(),
            1,
            [
                new TextInputChange(TextInputChangeKind.CompositionStarted),
                new TextInputChange(TextInputChangeKind.CompositionUpdated, "x", 1, 0),
            ]);
        var focusSequence = sequenceLease.Allocate();
        var focusLost = new PhysicalInputEvent(
            PhysicalInputEventKind.FocusLost,
            11,
            focusSequence,
            window: s_window);
        var focusReceipt = router.EnqueueHostPhysicalEvent(focusLost, out var discardedInputCount);

        Assert.That(focusReceipt.Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(focusReceipt.AssignedTick, Is.Null);
        Assert.That(discardedInputCount, Is.Zero);
        Assert.That(session.IsActive, Is.False);
        Assert.That(router.ServiceInputControlBoundary(), Is.Empty);
        Assert.That(session.TryReadBatch(out var first), Is.True);
        Assert.That(first!.Changes[0].Kind, Is.EqualTo(TextInputChangeKind.CompositionStarted));
        Assert.That(session.TryReadBatch(out var focusCanceled), Is.True);
        Assert.That(focusCanceled!.Changes[0].Kind, Is.EqualTo(TextInputChangeKind.CompositionCanceled));
        Assert.That(focusCanceled.Changes[0].CancellationReason, Is.EqualTo(TextCompositionCancelReason.FocusLost));
        Assert.That(session.TryReadBatch(out _), Is.False);

        var disposedSession = router.BeginTextSession(CreateOptions(s_window, 1, null, 0, 0));
        _ = disposedSession.PublishNativeBatch(
            12,
            sequenceLease.Allocate(),
            1,
            [new TextInputChange(TextInputChangeKind.CompositionStarted)]);
        disposedSession.Dispose();

        Assert.That(disposedSession.IsActive, Is.False);
        Assert.That(disposedSession.TryReadBatch(out _), Is.True);
        Assert.That(disposedSession.TryReadBatch(out var disposeCanceled), Is.True);
        Assert.That(disposeCanceled!.Changes[0].Kind, Is.EqualTo(TextInputChangeKind.CompositionCanceled));
        Assert.That(disposeCanceled.Changes[0].CancellationReason, Is.EqualTo(TextCompositionCancelReason.SessionDisposed));
        Assert.That(disposedSession.TryReadBatch(out _), Is.False);

        var routerDisposedSession = router.BeginTextSession(CreateOptions(s_window, 1, null, 0, 0));
        _ = routerDisposedSession.PublishNativeBatch(
            13,
            sequenceLease.Allocate(),
            1,
            [new TextInputChange(TextInputChangeKind.CompositionStarted)]);
        router.Dispose();
        Assert.That(routerDisposedSession.IsActive, Is.False);
        Assert.That(routerDisposedSession.TryReadBatch(out _), Is.True);
        Assert.That(routerDisposedSession.TryReadBatch(out var routerEnd), Is.True);
        Assert.That(routerEnd!.Changes[0].Kind, Is.EqualTo(TextInputChangeKind.CompositionCanceled));
        Assert.That(routerEnd.Changes[0].CancellationReason, Is.EqualTo(TextCompositionCancelReason.SessionDisposed));
    }

    [Test]
    public void NativeBatchAppliesOrderedEditsAsOneDocumentRevision()
    {
        var sequenceLease = new HostEventSequenceLease();
        var router = CreateRouter(sequenceLease, s_window);
        var session = router.BeginTextSession(CreateOptions(s_window, 1, "abcd", 1, 1));

        Assert.That(
            session.PublishNativeBatch(
                10,
                sequenceLease.Allocate(),
                1,
                [
                    new TextInputChange(TextInputChangeKind.TextEditRequested, editStart: 1, deleteLength: 1),
                    new TextInputChange(TextInputChangeKind.TextCommitted, "XY"),
                    new TextInputChange(TextInputChangeKind.TextCommitted, "!"),
                ]),
            Is.EqualTo(TextInputBatchPublishStatus.Published));

        Assert.That(session.TryReadBatch(out var batch), Is.True);
        Assert.That(batch!.BaseDocumentRevision, Is.EqualTo(1));
        Assert.That(batch.ResultDocumentRevision, Is.EqualTo(2));
        Assert.That(batch.Changes, Has.Length.EqualTo(3));
        Assert.That(batch.Changes[0].Kind, Is.EqualTo(TextInputChangeKind.TextEditRequested));
        Assert.That(batch.Changes[1].Kind, Is.EqualTo(TextInputChangeKind.TextCommitted));
        Assert.That(batch.Changes[1].EditStart, Is.EqualTo(1));
        Assert.That(batch.Changes[1].DeleteLength, Is.Zero);
        Assert.That(batch.Changes[2].EditStart, Is.EqualTo(3));
        Assert.That(batch.Changes[2].DeleteLength, Is.Zero);
        Assert.That(session.DocumentRevision, Is.EqualTo(1));

        Assert.That(
            session.PublishNativeBatch(
                11,
                sequenceLease.Allocate(),
                2,
                [new TextInputChange(TextInputChangeKind.TextCommitted, "z")]),
            Is.EqualTo(TextInputBatchPublishStatus.StaleMirror));
        Assert.That(session.TryReadBatch(out var staleBatch), Is.True);
        Assert.That(staleBatch!.Changes[0].Kind, Is.EqualTo(TextInputChangeKind.StaleMirror));

        session.Update(CreateOptions(s_window, 2, "aXY!cd", 4, 0));
        Assert.That(
            session.PublishNativeBatch(
                12,
                sequenceLease.Allocate(),
                2,
                [new TextInputChange(TextInputChangeKind.TextCommitted, "z")]),
            Is.EqualTo(TextInputBatchPublishStatus.Published));
        Assert.That(session.TryReadBatch(out var resumedBatch), Is.True);
        Assert.That(resumedBatch!.BaseDocumentRevision, Is.EqualTo(2));
        Assert.That(resumedBatch.ResultDocumentRevision, Is.EqualTo(3));
        Assert.That(resumedBatch.Changes[0].EditStart, Is.EqualTo(4));

        session.Dispose();
        router.Dispose();
    }

    [Test]
    public void ReadsOrderedCommittedTextAndRequiresAnAuthoritativeUpdate()
    {
        var sequenceLease = new HostEventSequenceLease();
        var router = CreateRouter(sequenceLease, s_window);
        var session = router.BeginTextSession(CreateOptions(s_window, 1, "abc", 1, 1));
        var foreignWindow = new WindowHandle(7, 2, 1);

        Assert.That(
            () => session.Update(CreateOptions(foreignWindow, 2, "abc", 1, 1)),
            Throws.TypeOf<ArgumentException>());
        Assert.That(
            () => session.Update(CreateOptions(s_window, 1, "abc", 1, 1)),
            Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(
            () => session.Update(new TextInputSessionOptions(s_window, TextInputPurpose.Password, 2, "abc", 1, 1, Vector2.Zero, Vector2.Zero)),
            Throws.TypeOf<ArgumentException>());
        Assert.That(
            () => session.Update(new TextInputSessionOptions(s_window, TextInputPurpose.FreeForm, 2, "abc", 1, 1, Vector2.Zero, Vector2.Zero) {
                Hints = TextInputHints.Sensitive,
            }),
            Throws.TypeOf<ArgumentException>());
        Assert.That(session.DocumentRevision, Is.EqualTo(1));

        var started = session.PublishNativeBatch(
            10,
            sequenceLease.Allocate(),
            1,
            [new TextInputChange(TextInputChangeKind.CompositionStarted)]);
        Assert.That(started, Is.EqualTo(TextInputBatchPublishStatus.Published));

        var invalidSequence = sequenceLease.Allocate();
        Assert.That(
            () => session.PublishNativeBatch(10, invalidSequence, 1, [default]),
            Throws.TypeOf<ArgumentException>());

        var committed = session.PublishNativeBatch(
            11,
            sequenceLease.Allocate(),
            1,
            [
                new TextInputChange(TextInputChangeKind.CompositionUpdated, "pre", 1, 1),
                new TextInputChange(TextInputChangeKind.TextCommitted, "x"),
                new TextInputChange(TextInputChangeKind.CompositionEnded),
            ]);
        Assert.That(committed, Is.EqualTo(TextInputBatchPublishStatus.Published));

        Assert.That(session.TryReadBatch(out var startBatch), Is.True);
        Assert.That(startBatch!.Changes, Has.Length.EqualTo(1));
        Assert.That(startBatch.Changes[0].Kind, Is.EqualTo(TextInputChangeKind.CompositionStarted));
        Assert.That(startBatch.Window, Is.EqualTo(s_window));
        Assert.That(startBatch.SessionId, Is.EqualTo(session.SessionId));

        Assert.That(session.TryReadBatch(out var textBatch), Is.True);
        Assert.That(textBatch!.HostSequence, Is.GreaterThan(startBatch.HostSequence));
        Assert.That(textBatch.BaseDocumentRevision, Is.EqualTo(1));
        Assert.That(textBatch.ResultDocumentRevision, Is.EqualTo(2));
        Assert.That(textBatch.Changes, Has.Length.EqualTo(3));
        Assert.That(textBatch.Changes[0].Kind, Is.EqualTo(TextInputChangeKind.CompositionUpdated));
        Assert.That(textBatch.Changes[1].Kind, Is.EqualTo(TextInputChangeKind.TextCommitted));
        Assert.That(textBatch.Changes[1].EditStart, Is.EqualTo(1));
        Assert.That(textBatch.Changes[1].DeleteLength, Is.EqualTo(1));
        Assert.That(textBatch.Changes[2].Kind, Is.EqualTo(TextInputChangeKind.CompositionEnded));
        Assert.That(session.DocumentRevision, Is.EqualTo(1));

        Assert.That(
            session.PublishNativeBatch(
                12,
                sequenceLease.Allocate(),
                2,
                [new TextInputChange(TextInputChangeKind.TextCommitted, "y")]),
            Is.EqualTo(TextInputBatchPublishStatus.StaleMirror));
        Assert.That(session.TryReadBatch(out var staleBatch), Is.True);
        Assert.That(staleBatch!.Changes[0].Kind, Is.EqualTo(TextInputChangeKind.StaleMirror));
        Assert.That(session.LastDiagnostic?.Code, Is.EqualTo("TextInput.StaleMirror"));

        Assert.That(
            session.PublishNativeBatch(
                13,
                sequenceLease.Allocate(),
                2,
                [new TextInputChange(TextInputChangeKind.TextCommitted, "y")]),
            Is.EqualTo(TextInputBatchPublishStatus.StaleMirror));
        Assert.That(session.TryReadBatch(out var repeatedStaleBatch), Is.True);
        Assert.That(repeatedStaleBatch!.Changes[0].Kind, Is.EqualTo(TextInputChangeKind.StaleMirror));

        session.Update(CreateOptions(s_window, 2, "axc", 2, 0));
        Assert.That(session.DocumentRevision, Is.EqualTo(2));
        Assert.That(session.LastDiagnostic, Is.Null);
        Assert.That(session.TryReadBatch(out var noBatch), Is.False);
        Assert.That(noBatch, Is.Null);

        Assert.That(
            session.PublishNativeBatch(
                14,
                sequenceLease.Allocate(),
                2,
                [new TextInputChange(TextInputChangeKind.TextEditRequested, editStart: 4, deleteLength: 1)]),
            Is.EqualTo(TextInputBatchPublishStatus.Published));
        Assert.That(session.TryReadBatch(out var rejectedEdit), Is.True);
        Assert.That(rejectedEdit!.ResultDocumentRevision, Is.EqualTo(2));
        Assert.That(rejectedEdit.Changes[0].Kind, Is.EqualTo(TextInputChangeKind.TextEditRejected));
        Assert.That(session.LastDiagnostic?.Code, Is.EqualTo("TextInput.TextEditRejected"));

        session.Dispose();
        router.Dispose();
    }

    [Test]
    public void TextEditRejectionPreservesTheAuthoritativeSelectionAndShadow()
    {
        var sequenceLease = new HostEventSequenceLease();
        var router = CreateRouter(sequenceLease, s_window);
        var session = router.BeginTextSession(CreateOptions(s_window, 1, "A\U0001F642B", 1, 2));

        Assert.That(
            session.PublishNativeBatch(
                10,
                sequenceLease.Allocate(),
                1,
                [
                    new TextInputChange(TextInputChangeKind.TextEditRequested, editStart: 2, deleteLength: 1),
                    new TextInputChange(TextInputChangeKind.TextCommitted, "x"),
                ]),
            Is.EqualTo(TextInputBatchPublishStatus.Published));

        Assert.That(session.TryReadBatch(out var batch), Is.True);
        Assert.That(batch!.ResultDocumentRevision, Is.EqualTo(2));
        Assert.That(batch.Changes, Has.Length.EqualTo(2));
        Assert.That(batch.Changes[0].Kind, Is.EqualTo(TextInputChangeKind.TextEditRejected));
        Assert.That(batch.Changes[0].Diagnostic?.Code, Is.EqualTo("TextInput.TextEditRejected"));
        Assert.That(batch.Changes[1].Kind, Is.EqualTo(TextInputChangeKind.TextCommitted));
        Assert.That(batch.Changes[1].EditStart, Is.EqualTo(1));
        Assert.That(batch.Changes[1].DeleteLength, Is.EqualTo(2));

        session.Update(CreateOptions(s_window, 2, "AxB", 2, 0));
        Assert.That(session.DocumentRevision, Is.EqualTo(2));
        Assert.That(session.LastDiagnostic, Is.Null);
        session.Dispose();
        router.Dispose();
    }

    [Test]
    public void TextOutputBudgetIsExactAndOverflowUsesRecipientRecovery()
    {
        var sequenceLease = new HostEventSequenceLease();
        var router = CreateRouter(sequenceLease, s_window);
        var session = router.BeginTextSession(CreateOptions(s_window, 1, null, 0, 0));
        var text = new string('x', 4096);
        var hostTimestamp = 0L;

        _ = session.PublishNativeBatch(
            ++hostTimestamp,
            sequenceLease.Allocate(),
            1,
            [new TextInputChange(TextInputChangeKind.CompositionStarted)]);

        for (var index = 0; index < 64; index++)
        {
            Assert.That(
                session.PublishNativeBatch(
                    ++hostTimestamp,
                    sequenceLease.Allocate(),
                    1,
                    [new TextInputChange(TextInputChangeKind.CompositionUpdated, text, 0, 0)]),
                Is.EqualTo(TextInputBatchPublishStatus.Published));
        }

        Assert.That(
            session.PublishNativeBatch(
                ++hostTimestamp,
                sequenceLease.Allocate(),
                1,
                [new TextInputChange(TextInputChangeKind.CompositionUpdated, text, 0, 0)]),
            Is.EqualTo(TextInputBatchPublishStatus.CapacityExceeded));
        Assert.That(session.IsActive, Is.False);
        Assert.That(session.LastDiagnostic?.Code, Is.EqualTo("TextInput.Overflow"));
        Assert.That(router.ServiceHostControlBoundary(), Is.Not.Null);

        var readCount = 0;

        while (session.TryReadBatch(out _))
        {
            readCount++;
        }

        Assert.That(readCount, Is.EqualTo(65));
        router.Dispose();
    }

    [Test]
    public void TextTransactionChangeCountAcceptsExactMaximum()
    {
        var sequenceLease = new HostEventSequenceLease();
        var router = CreateRouter(sequenceLease, s_window);
        var session = router.BeginTextSession(CreateOptions(s_window, 1, null, 0, 0));

        Assert.That(
            session.PublishNativeBatch(
                10,
                sequenceLease.Allocate(),
                1,
                CreateCompositionChanges(TextInputBatchBudget.MaximumPendingChanges)),
            Is.EqualTo(TextInputBatchPublishStatus.Published));
        Assert.That(session.IsActive, Is.True);
        Assert.That(session.TryReadBatch(out var batch), Is.True);
        Assert.That(batch!.Changes, Has.Length.EqualTo(TextInputBatchBudget.MaximumPendingChanges));

        session.Dispose();
        router.Dispose();
    }

    [Test]
    public void TextTransactionChangeCountOverflowUsesRecipientRecovery()
    {
        var sequenceLease = new HostEventSequenceLease();
        var router = CreateRouter(sequenceLease, s_window);
        var session = router.BeginTextSession(CreateOptions(s_window, 1, null, 0, 0));

        Assert.That(
            session.PublishNativeBatch(
                10,
                sequenceLease.Allocate(),
                1,
                CreateCompositionChanges(TextInputBatchBudget.MaximumPendingChanges + 1)),
            Is.EqualTo(TextInputBatchPublishStatus.CapacityExceeded));
        Assert.That(session.IsActive, Is.False);
        Assert.That(session.LastDiagnostic?.Code, Is.EqualTo("TextInput.Overflow"));
        Assert.That(session.TryReadBatch(out _), Is.False);
        Assert.That(router.ServiceHostControlBoundary(), Is.Not.Null);
        router.Dispose();
    }
}
