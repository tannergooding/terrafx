// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;
using System.Threading;
using NUnit.Framework;
using TerraFX;
using TerraFX.Input;
using TerraFX.Interop.Windows;

namespace TerraFX.Platform.Tests;

[TestFixture]
[NonParallelizable]
internal sealed unsafe class WindowsTextInputStoreTests
{
    [Test]
    [Apartment(ApartmentState.STA)]
    [SupportedOSPlatform("windows10.0.22000")]
    public void ReplacesSupplementaryTextThroughASynchronousTsfLock()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            Assert.Ignore("The TSF text-store witness requires Windows 11.");
            return;
        }

        var window = new WindowHandle(OwnerIdAllocator.Allocate(), index: 0, generation: 1);
        var options = new TextInputSessionOptions(
            window,
            TextInputPurpose.FreeForm,
            documentRevision: 1,
            surroundingText: "A\uD83D\uDE00Z",
            selectionStart: 1,
            selectionLength: 2,
            caretPosition: default,
            caretSize: default);
        var state = WindowsTextInputSessionState.Create(new TextInputSessionSnapshot(1, options));
        var inputSamples = new PlatformInputSampleQueue(Environment.CurrentManagedThreadId);
        var store = new WindowsTextInputStore(window, new nint(1), state, inputSamples, TimeProvider.System);
        var sink = new WindowsTextStoreTestSink();
        var comWrappers = new StrategyBasedComWrappers();
        var sinkUnknown = comWrappers.GetOrCreateComInterfaceForObject(sink, CreateComInterfaceFlags.None);
        nint storeInterface = 0;

        try
        {
            var sinkIid = typeof(ITextStoreACPSink).GUID;
            Assert.That(store.AdviseSink(&sinkIid, sinkUnknown, (uint)TS.TS_AS_ALL_SINKS), Is.Zero);
            var storeUnknown = store.GetIUnknownPointer();
            var storeIid = typeof(ITextStoreACP).GUID;
            void* storePointer = null;
            var queryResult = ((IUnknown*)storeUnknown)->QueryInterface(&storeIid, &storePointer);
            Assert.That(queryResult.Value, Is.Zero);
            storeInterface = (nint)storePointer;
            var compositionSinkIid = typeof(IWindowsTextStoreCompositionSink).GUID;
            void* compositionSinkPointer = null;
            queryResult = ((IUnknown*)storeUnknown)->QueryInterface(&compositionSinkIid, &compositionSinkPointer);
            Assert.That(queryResult.Value, Is.Zero);
            _ = ((IUnknown*)compositionSinkPointer)->Release();
            var invalidTextExt = default(RECT);
            var clipped = 0;
            Assert.That(
                store.GetTextExt(0, -1, 0, &invalidTextExt, &clipped),
                Is.EqualTo(TS.TS_E_INVALIDPOS));

            var replacement = "\u00E9";
            sink.LockGranted = () => {
                var change = default(TS_TEXTCHANGE);

                fixed (char* replacementPointer = replacement)
                {
                    sink.SetTextResult = ((ITextStoreACP*)storeInterface)->SetText(
                        0,
                        1,
                        3,
                        replacementPointer,
                        1,
                        &change).Value;
                }

                sink.TextChange = change;
            };

            var sessionResult = 0;
            Assert.That(
                store.RequestLock(
                    (uint)TS.TS_LF_READWRITE | (uint)TS.TS_LF_SYNC,
                    &sessionResult),
                Is.Zero);
            Assert.That(sessionResult, Is.Zero);
            Assert.That(sink.SetTextResult, Is.Zero);
            Assert.That(sink.TextChange.acpStart, Is.EqualTo(1));
            Assert.That(sink.TextChange.acpOldEnd, Is.EqualTo(3));
            Assert.That(sink.TextChange.acpNewEnd, Is.EqualTo(2));

            var batch = inputSamples.Drain();
            Assert.That(batch.TextSamples, Has.Length.EqualTo(1));
            Assert.That(batch.TextSamples[0].SessionId, Is.EqualTo(1));
            Assert.That(batch.TextSamples[0].BaseDocumentRevision, Is.EqualTo(1));
            Assert.That(batch.TextSamples[0].Changes, Has.Length.EqualTo(1));
            Assert.That(batch.TextSamples[0].Changes[0].Kind, Is.EqualTo(TextInputChangeKind.TextCommitted));
            Assert.That(batch.TextSamples[0].Changes[0].Text, Is.EqualTo(replacement));
            Assert.That(batch.TextSamples[0].Changes[0].EditStart, Is.EqualTo(1));
            Assert.That(batch.TextSamples[0].Changes[0].DeleteLength, Is.EqualTo(2));

            var updatedState = WindowsTextInputSessionState.Create(new TextInputSessionSnapshot(
                1,
                new TextInputSessionOptions(
                    window,
                    TextInputPurpose.FreeForm,
                    documentRevision: 2,
                    surroundingText: "A\u00E9Z",
                    selectionStart: 2,
                    selectionLength: 0,
                    caretPosition: default,
                    caretSize: default)));
            store.UpdateSnapshot(updatedState);
            sink.SetTextResult = int.MinValue;
            sink.LockGranted = () => {
                var change = default(TS_TEXTCHANGE);
                var asynchronousText = "x";

                fixed (char* textPointer = asynchronousText)
                {
                    sink.SetTextResult = ((ITextStoreACP*)storeInterface)->SetText(
                        0,
                        2,
                        2,
                        textPointer,
                        1,
                        &change).Value;
                }

                sink.TextChange = change;
            };

            sessionResult = 0;
            Assert.That(store.RequestLock((uint)TS.TS_LF_READWRITE, &sessionResult), Is.Zero);
            Assert.That(sessionResult, Is.EqualTo(TS.TS_S_ASYNC));
            Assert.That(sink.SetTextResult, Is.EqualTo(int.MinValue));
            Assert.That(store.GrantPendingLock(), Is.Zero);
            Assert.That(sink.SetTextResult, Is.Zero);

            var asynchronousBatch = inputSamples.Drain();
            Assert.That(asynchronousBatch.TextSamples, Has.Length.EqualTo(1));
            Assert.That(asynchronousBatch.TextSamples[0].BaseDocumentRevision, Is.EqualTo(2));
            Assert.That(asynchronousBatch.TextSamples[0].Changes[0].Text, Is.EqualTo("x"));
            Assert.That(asynchronousBatch.TextSamples[0].Changes[0].EditStart, Is.EqualTo(2));
            Assert.That(asynchronousBatch.TextSamples[0].Changes[0].DeleteLength, Is.Zero);

            store.UpdateSnapshot(WindowsTextInputSessionState.Create(new TextInputSessionSnapshot(
                1,
                new TextInputSessionOptions(
                    window,
                    TextInputPurpose.FreeForm,
                    documentRevision: 3,
                    surroundingText: "A\uD83D\uDE00Z",
                    selectionStart: 1,
                    selectionLength: 2,
                    caretPosition: default,
                    caretSize: default))));
            sink.LockGranted = () => {
                var invalidReplacement = "x";
                var change = default(TS_TEXTCHANGE);

                fixed (char* replacementPointer = invalidReplacement)
                {
                    sink.SetTextResult = ((ITextStoreACP*)storeInterface)->SetText(
                        0,
                        2,
                        2,
                        replacementPointer,
                        1,
                        &change).Value;
                }

                var textBuffer = new char[4];
                var textLength = 0u;
                var runInfo = default(TS_RUNINFO);
                var runInfoCount = 0u;
                var next = 0;

                fixed (char* textPointer = textBuffer)
                {
                    sink.ReadTextResult = ((ITextStoreACP*)storeInterface)->GetText(
                        0,
                        -1,
                        textPointer,
                        4,
                        &textLength,
                        &runInfo,
                        1,
                        &runInfoCount,
                        &next).Value;
                }

                sink.SnapshotText = new string(textBuffer, 0, checked((int)textLength));
            };

            sessionResult = 0;
            Assert.That(
                store.RequestLock(
                    (uint)TS.TS_LF_READWRITE | (uint)TS.TS_LF_SYNC,
                    &sessionResult),
                Is.Zero);
            Assert.That(sessionResult, Is.Zero);
            Assert.That(sink.SetTextResult, Is.EqualTo(TS.TS_E_INVALIDPOS));
            Assert.That(sink.ReadTextResult, Is.Zero);
            Assert.That(sink.SnapshotText, Is.EqualTo("A\uD83D\uDE00Z"));
            var rejectedBatch = inputSamples.Drain();
            Assert.That(rejectedBatch.TextSamples, Has.Length.EqualTo(1));
            Assert.That(
                rejectedBatch.TextSamples[0].Changes[0].Kind,
                Is.EqualTo(TextInputChangeKind.TextEditRejected));

            store.UpdateSnapshot(WindowsTextInputSessionState.Create(new TextInputSessionSnapshot(
                1,
                new TextInputSessionOptions(
                    window,
                    TextInputPurpose.FreeForm,
                    documentRevision: 4,
                    surroundingText: "ab",
                    selectionStart: 1,
                    selectionLength: 1,
                    caretPosition: default,
                    caretSize: default))));

            var compositionAccepted = 0;
            Assert.That(store.OnStartComposition(0, &compositionAccepted), Is.Zero);
            Assert.That(compositionAccepted, Is.EqualTo(1));
            sink.LockGranted = () => {
                var preedit = "\u4E2D";
                var change = default(TS_TEXTCHANGE);

                fixed (char* preeditPointer = preedit)
                {
                    sink.SetTextResult = ((ITextStoreACP*)storeInterface)->SetText(
                        0,
                        1,
                        2,
                        preeditPointer,
                        1,
                        &change).Value;
                }
            };

            sessionResult = 0;
            Assert.That(
                store.RequestLock(
                    (uint)TS.TS_LF_READWRITE | (uint)TS.TS_LF_SYNC,
                    &sessionResult),
                Is.Zero);
            Assert.That(sessionResult, Is.Zero);
            Assert.That(sink.SetTextResult, Is.Zero);
            var compositionStartBatch = inputSamples.Drain();
            Assert.That(compositionStartBatch.TextSamples, Has.Length.EqualTo(1));
            Assert.That(
                compositionStartBatch.TextSamples[0].Changes[0].Kind,
                Is.EqualTo(TextInputChangeKind.CompositionStarted));

            Assert.That(store.OnUpdateComposition(0, 0), Is.Zero);
            Assert.That(store.OnEndComposition(0), Is.Zero);
            store.FlushPendingTextInput();
            var compositionEndBatch = inputSamples.Drain();
            Assert.That(compositionEndBatch.TextSamples, Has.Length.EqualTo(1));
            Assert.That(compositionEndBatch.TextSamples[0].Changes, Has.Length.EqualTo(3));
            Assert.That(
                compositionEndBatch.TextSamples[0].Changes[0].Kind,
                Is.EqualTo(TextInputChangeKind.CompositionUpdated));
            Assert.That(compositionEndBatch.TextSamples[0].Changes[0].Text, Is.EqualTo("\u4E2D"));
            Assert.That(
                compositionEndBatch.TextSamples[0].Changes[1].Kind,
                Is.EqualTo(TextInputChangeKind.TextCommitted));
            Assert.That(compositionEndBatch.TextSamples[0].Changes[1].EditStart, Is.EqualTo(1));
            Assert.That(compositionEndBatch.TextSamples[0].Changes[1].DeleteLength, Is.EqualTo(1));
            Assert.That(
                compositionEndBatch.TextSamples[0].Changes[2].Kind,
                Is.EqualTo(TextInputChangeKind.CompositionEnded));

            store.UpdateSnapshot(WindowsTextInputSessionState.Create(new TextInputSessionSnapshot(
                1,
                new TextInputSessionOptions(
                    window,
                    TextInputPurpose.FreeForm,
                    documentRevision: 5,
                    surroundingText: "a\u4E2D",
                    selectionStart: 2,
                    selectionLength: 0,
                    caretPosition: default,
                    caretSize: default))));

            compositionAccepted = 0;
            Assert.That(store.OnStartComposition(0, &compositionAccepted), Is.Zero);
            Assert.That(compositionAccepted, Is.EqualTo(1));
            sink.LockGranted = () => {
                var preedit = "\u6587";
                var change = default(TS_TEXTCHANGE);

                fixed (char* preeditPointer = preedit)
                {
                    sink.SetTextResult = ((ITextStoreACP*)storeInterface)->SetText(
                        0,
                        2,
                        2,
                        preeditPointer,
                        1,
                        &change).Value;
                }
            };

            sessionResult = 0;
            Assert.That(
                store.RequestLock(
                    (uint)TS.TS_LF_READWRITE | (uint)TS.TS_LF_SYNC,
                    &sessionResult),
                Is.Zero);
            Assert.That(sessionResult, Is.Zero);
            Assert.That(sink.SetTextResult, Is.Zero);
            _ = inputSamples.Drain();
            store.CancelComposition(TextCompositionCancelReason.FocusLost);
            store.FlushPendingTextInput();
            var canceledCompositionBatch = inputSamples.Drain();
            Assert.That(canceledCompositionBatch.TextSamples, Has.Length.EqualTo(1));
            Assert.That(
                canceledCompositionBatch.TextSamples[0].Changes[0].Kind,
                Is.EqualTo(TextInputChangeKind.CompositionCanceled));
        }
        finally
        {
            if (storeInterface != 0)
            {
                _ = Marshal.Release(storeInterface);
            }

            store.Dispose();
            _ = Marshal.Release(sinkUnknown);
        }
    }
}
