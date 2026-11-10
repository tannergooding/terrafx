// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using TerraFX;
using TerraFX.Input;

namespace TerraFX.Platform;

internal sealed partial class X11PlatformBackend
{
    public PlatformTextInputCapabilities UpdateTextInputSession(
        WindowHandle window,
        TextInputSessionSnapshot? snapshot)
    {
        EnsureOwnerThread();

        if (!window.IsValid || (window.OwnerId != _ownerId))
        {
            throw new ArgumentException("The text input session belongs to another X11 platform owner.", nameof(window));
        }

        if (!_windowsByHandle.TryGetValue(window, out var state))
        {
            throw new InvalidOperationException("The text input session targets an inactive X11 window.");
        }

        if (snapshot is not { } session)
        {
            state.TextInputContext?.Dispose(TextCompositionCancelReason.SessionDisposed);
            state.TextInputContext = null;
            _keyboardKeymap?.ResetComposeState();
            return PlatformTextInputCapabilities.None;
        }

        if ((session.SessionId <= 0) || (session.Window != window))
        {
            throw new ArgumentException("The text input snapshot must name its exact session and window.", nameof(snapshot));
        }

        if (state.TextInputContext is X11TextInputContext existingContext)
        {
            if (existingContext.SessionId == session.SessionId)
            {
                existingContext.UpdateSnapshot(session);
                return PlatformTextInputCapabilities.CommittedText | PlatformTextInputCapabilities.Composition;
            }

            existingContext.Dispose(TextCompositionCancelReason.SessionReplaced);
            state.TextInputContext = null;
            _keyboardKeymap?.ResetComposeState();
        }

        if (_inputMethod != 0)
        {
            var inputContext = X11TextInputContext.TryCreate(
                _display,
                _inputMethod,
                window,
                state.NativeWindow,
                session,
                _inputSamples);

            if (inputContext is not null)
            {
                state.TextInputContext = inputContext;

                if (state.IsFocused)
                {
                    inputContext.SetFocus();
                }

                _keyboardKeymap?.ResetComposeState();
                return PlatformTextInputCapabilities.CommittedText | PlatformTextInputCapabilities.Composition;
            }
        }

        return (_keyboardKeymap is null)
            ? PlatformTextInputCapabilities.None
            : PlatformTextInputCapabilities.CommittedText;
    }

    private static unsafe partial class X11InputMethodNative
    {
        [LibraryImport("libX11.so.6", EntryPoint = "XCloseIM")]
        internal static partial int XCloseIM(nint inputMethod);

        [LibraryImport("libX11.so.6", EntryPoint = "XCreateIC")]
        internal static partial nint XCreateIC(
            nint inputMethod,
            nint inputStyleAttribute,
            nuint inputStyle,
            nint clientWindowAttribute,
            nuint clientWindow,
            nint preeditAttributesName,
            nint preeditAttributes,
            nint nullTerminator);

        [LibraryImport("libX11.so.6", EntryPoint = "XDestroyIC")]
        internal static partial void XDestroyIC(nint inputContext);

        [LibraryImport("libX11.so.6", EntryPoint = "XFilterEvent")]
        internal static partial int XFilterEvent(nint display, void* nativeEvent, nuint window);

        [LibraryImport("libX11.so.6", EntryPoint = "XOpenIM")]
        internal static partial nint XOpenIM(nint display, nint database, nint resourceName, nint resourceClass);

        [LibraryImport("libX11.so.6", EntryPoint = "XSetICFocus")]
        internal static partial void XSetICFocus(nint inputContext);

        [LibraryImport("libX11.so.6", EntryPoint = "XSetLocaleModifiers", StringMarshalling = StringMarshalling.Utf8)]
        internal static partial nint XSetLocaleModifiers(string baseName);

        [LibraryImport("libX11.so.6", EntryPoint = "XSupportsLocale")]
        internal static partial int XSupportsLocale();

        [LibraryImport("libX11.so.6", EntryPoint = "XUnsetICFocus")]
        internal static partial void XUnsetICFocus(nint inputContext);

        // The variadic portions of these Xlib calls use only pointer-sized values.
        [LibraryImport("libX11.so.6", EntryPoint = "XVaCreateNestedList")]
        internal static partial nint XVaCreateNestedList(
            int unused,
            nint startCallbackName,
            XimCallback* startCallback,
            nint doneCallbackName,
            XimCallback* doneCallback,
            nint drawCallbackName,
            XimCallback* drawCallback,
            nint caretCallbackName,
            XimCallback* caretCallback,
            nint nullTerminator);

        [LibraryImport("libX11.so.6", EntryPoint = "Xutf8LookupString")]
        internal static partial int Xutf8LookupString(
            nint inputContext,
            void* keyEvent,
            byte* buffer,
            int bufferBytes,
            nuint* keySym,
            int* status);

        [StructLayout(LayoutKind.Sequential)]
        internal struct XimCallback
        {
            internal nint ClientData;
            internal nint Callback;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct XimPreeditCaretCallbackStruct
        {
            internal int Position;
            internal int Direction;
            internal int Style;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct XimPreeditDrawCallbackStruct
        {
            internal int Caret;
            internal int ChangeFirst;
            internal int ChangeLength;
            internal nint Text;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct XimText
        {
            internal ushort Length;
            internal nint Feedback;
            internal int EncodingIsWide;
            internal nint String;
        }
    }

    [SuppressMessage(
        "Design",
        "TFX0002:Order type members by category, staticness, accessibility, and name using ordinal comparison",
        Justification = "Keep the XIM session lifecycle, callback entry points, and their shared preedit state transitions grouped by interop flow.")]
    internal sealed unsafe class X11TextInputContext
    {
        private const int MaximumPreeditCodepoints = 1024;
        private const int MaximumTextBytes = 4096;
        private const int XBufferOverflow = -1;
        private const int XLookupBoth = 4;
        private const int XLookupChars = 2;
        private const int XLookupNone = 1;
        private const nuint XimPreeditCallbacks = 0x0002;
        private const nuint XimStatusNothing = 0x0400;

        private static readonly UTF8Encoding s_utf8Encoding = new(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);

        private ExceptionDispatchInfo? _callbackFailure;
        private GCHandle _clientDataHandle;
        private bool _compositionActive;
        private readonly nint _display;
        private long _documentRevision;
        private bool _ignorePreeditUntilDone;
        private nint _inputContext;
        private readonly PlatformInputSampleQueue _inputSamples;
        private bool _isDisposed;
        private bool _isFocused;
        private int _preeditSelectionStart;
        private string _preeditText = string.Empty;
        private readonly WindowHandle _window;

        private X11TextInputContext(
            nint display,
            nint inputMethod,
            nuint nativeWindow,
            WindowHandle window,
            TextInputSessionSnapshot snapshot,
            PlatformInputSampleQueue inputSamples)
        {
            _display = display;
            _inputSamples = inputSamples;
            _window = window;
            SessionId = snapshot.SessionId;
            _documentRevision = snapshot.Options.DocumentRevision;

            _clientDataHandle = GCHandle.Alloc(this);

            try
            {
                _inputContext = CreateInputContext(inputMethod, nativeWindow);

                if (_inputContext == 0)
                {
                    _clientDataHandle.Free();
                }
            }
            catch
            {
                _clientDataHandle.Free();
                throw;
            }
        }

        internal long SessionId { get; }

        internal static (string Text, int SelectionStart) ApplyPreeditUpdate(
            string currentText,
            int changeFirst,
            int changeLength,
            string replacementText,
            int caret)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(changeLength);
            var start = GetUtf16Offset(currentText, changeFirst);
            var end = GetUtf16Offset(currentText, checked(changeFirst + changeLength));
            var updatedText = string.Concat(currentText.AsSpan(0, start), replacementText.AsSpan(), currentText.AsSpan(end));
            var selectionStart = GetUtf16Offset(updatedText, caret);
            return (updatedText, selectionStart);
        }

        internal static void CloseInputMethod(nint inputMethod)
        {
            if (inputMethod != 0)
            {
                _ = X11InputMethodNative.XCloseIM(inputMethod);
            }
        }

        internal static nint OpenInputMethod(nint display)
        {
            if ((X11InputMethodNative.XSupportsLocale() == 0)
                || (X11InputMethodNative.XSetLocaleModifiers(string.Empty) == 0))
            {
                return 0;
            }

            return X11InputMethodNative.XOpenIM(display, 0, 0, 0);
        }

        internal static X11TextInputContext? TryCreate(
            nint display,
            nint inputMethod,
            WindowHandle window,
            nuint nativeWindow,
            TextInputSessionSnapshot snapshot,
            PlatformInputSampleQueue inputSamples)
        {
            var inputContext = new X11TextInputContext(
                display,
                inputMethod,
                nativeWindow,
                window,
                snapshot,
                inputSamples);

            if (inputContext._inputContext == 0)
            {
                return null;
            }

            return inputContext;
        }

        internal void Dispose(TextCompositionCancelReason cancellationReason)
        {
            if (_isDisposed)
            {
                return;
            }

            CancelComposition(cancellationReason);
            _isDisposed = true;

            if (_isFocused)
            {
                X11InputMethodNative.XUnsetICFocus(_inputContext);
                _isFocused = false;
            }

            X11InputMethodNative.XDestroyIC(_inputContext);
            _inputContext = 0;
            _clientDataHandle.Free();
        }

        internal void LookupString(void* keyEvent)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            if (keyEvent is null)
            {
                throw new ArgumentNullException(nameof(keyEvent));
            }

            Span<byte> bytes = stackalloc byte[MaximumTextBytes];
            nuint keySym = 0;
            var status = 0;
            int byteCount;

            fixed (byte* buffer = bytes)
            {
                byteCount = X11InputMethodNative.Xutf8LookupString(
                    _inputContext,
                    keyEvent,
                    buffer,
                    bytes.Length,
                    &keySym,
                    &status);
            }

            ThrowCallbackFailure();

            if (status is XBufferOverflow)
            {
                _inputSamples.RecordTextInputOverflow(_window, SessionId);
                CancelComposition(TextCompositionCancelReason.Overflow);
                return;
            }

            if (status is XLookupNone)
            {
                return;
            }

            if (status is not (XLookupChars or XLookupBoth))
            {
                throw new InvalidOperationException("X11 returned an unknown UTF-8 input-method lookup status.");
            }

            if ((byteCount < 0) || (byteCount > bytes.Length))
            {
                throw new InvalidOperationException("X11 returned an invalid UTF-8 input-method byte count.");
            }

            if (byteCount != 0)
            {
                var text = s_utf8Encoding.GetString(bytes[..byteCount]);
                _ = TryQueueChange(new TextInputChange(TextInputChangeKind.TextCommitted, text));
            }
        }

        internal void SetFocus()
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            X11InputMethodNative.XSetICFocus(_inputContext);
            _isFocused = true;
            ThrowCallbackFailure();
        }

        internal void UnsetFocus()
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            CancelComposition(TextCompositionCancelReason.FocusLost);
            X11InputMethodNative.XUnsetICFocus(_inputContext);
            _isFocused = false;
            ThrowCallbackFailure();
        }

        internal void UpdateSnapshot(TextInputSessionSnapshot snapshot)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            if (snapshot.SessionId != SessionId)
            {
                throw new ArgumentException("The X11 input context cannot change text-session identity.", nameof(snapshot));
            }

            _documentRevision = snapshot.Options.DocumentRevision;
        }

        internal void ThrowCallbackFailure()
        {
            if (_callbackFailure is ExceptionDispatchInfo failure)
            {
                _callbackFailure = null;
                failure.Throw();
            }
        }

        private static int GetUtf16Offset(string text, int codepointIndex)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(codepointIndex);

            var utf16Offset = 0;

            foreach (var rune in text.EnumerateRunes())
            {
                if (codepointIndex == 0)
                {
                    return utf16Offset;
                }

                utf16Offset += rune.Utf16SequenceLength;
                codepointIndex--;
            }

            if (codepointIndex == 0)
            {
                return utf16Offset;
            }

            throw new InvalidDataException("The X11 input method returned a preedit index outside the current text.");
        }

        private static string ReadPreeditText(X11InputMethodNative.XimText* text)
        {
            if (text->Length == 0)
            {
                return string.Empty;
            }

            if (text->String == 0)
            {
                throw new InvalidDataException("The X11 input method returned a missing preedit string.");
            }

            var builder = new StringBuilder(text->Length);

            if (text->EncodingIsWide is not 0)
            {
                var wideCharacters = (uint*)text->String;

                for (var index = 0; index < text->Length; index++)
                {
                    var codepoint = wideCharacters[index];

                    if (codepoint is > 0x10FFFF or (>= 0xD800 and <= 0xDFFF))
                    {
                        throw new InvalidDataException("The X11 input method returned an invalid wide preedit code point.");
                    }

                    _ = builder.Append(char.ConvertFromUtf32(checked((int)codepoint)));
                }
            }
            else
            {
                var byteCount = 0;

                while ((byteCount <= MaximumTextBytes) && (((byte*)text->String)[byteCount] != 0))
                {
                    byteCount++;
                }

                if (byteCount > MaximumTextBytes)
                {
                    throw new InvalidDataException("The X11 input method returned an oversized preedit string.");
                }

                var bytes = new ReadOnlySpan<byte>((void*)text->String, byteCount);
                _ = builder.Append(s_utf8Encoding.GetString(bytes));
            }

            var result = builder.ToString();

            if (CountCodepoints(result) != text->Length)
            {
                throw new InvalidDataException("The X11 input method returned a preedit length inconsistent with its text.");
            }

            return result;
        }

        private static int CountCodepoints(string text)
        {
            var count = 0;

            foreach (var _ in text.EnumerateRunes())
            {
                count++;
            }

            return count;
        }

        private static X11TextInputContext? GetContext(nint clientData)
        {
            if (clientData == 0)
            {
                return null;
            }

            return GCHandle.FromIntPtr(clientData).Target as X11TextInputContext;
        }

        private static void OnCallbackFailure(X11TextInputContext context, Exception exception)
            => context._callbackFailure ??= ExceptionDispatchInfo.Capture(exception);

        private static bool TryGetContext(nint clientData, out X11TextInputContext? context)
        {
            context = GetContext(clientData);
            return context is not null;
        }

        private void CancelComposition(TextCompositionCancelReason reason)
        {
            if (!_compositionActive)
            {
                _ignorePreeditUntilDone = false;
                return;
            }

            _compositionActive = false;
            _preeditText = string.Empty;
            _preeditSelectionStart = 0;
            _ = TryQueueChange(new TextInputChange(
                TextInputChangeKind.CompositionCanceled,
                cancellationReason: reason));
        }

        private nint CreateInputContext(nint inputMethod, nuint nativeWindow)
        {
            nint inputStyleName = 0;
            nint clientWindowName = 0;
            nint preeditAttributesName = 0;
            nint startCallbackName = 0;
            nint doneCallbackName = 0;
            nint drawCallbackName = 0;
            nint caretCallbackName = 0;

            try
            {
                inputStyleName = Marshal.StringToCoTaskMemUTF8("inputStyle");
                clientWindowName = Marshal.StringToCoTaskMemUTF8("clientWindow");
                preeditAttributesName = Marshal.StringToCoTaskMemUTF8("preeditAttributes");
                startCallbackName = Marshal.StringToCoTaskMemUTF8("preeditStartCallback");
                doneCallbackName = Marshal.StringToCoTaskMemUTF8("preeditDoneCallback");
                drawCallbackName = Marshal.StringToCoTaskMemUTF8("preeditDrawCallback");
                caretCallbackName = Marshal.StringToCoTaskMemUTF8("preeditCaretCallback");
                var clientData = GCHandle.ToIntPtr(_clientDataHandle);
                var startCallback = new X11InputMethodNative.XimCallback {
                    ClientData = clientData,
                    Callback = (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, int>)&OnPreeditStart,
                };
                var doneCallback = new X11InputMethodNative.XimCallback {
                    ClientData = clientData,
                    Callback = (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnPreeditDone,
                };
                var drawCallback = new X11InputMethodNative.XimCallback {
                    ClientData = clientData,
                    Callback = (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnPreeditDraw,
                };
                var caretCallback = new X11InputMethodNative.XimCallback {
                    ClientData = clientData,
                    Callback = (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnPreeditCaret,
                };
                var preeditAttributes = X11InputMethodNative.XVaCreateNestedList(
                    0,
                    startCallbackName,
                    &startCallback,
                    doneCallbackName,
                    &doneCallback,
                    drawCallbackName,
                    &drawCallback,
                    caretCallbackName,
                    &caretCallback,
                    0);

                if (preeditAttributes == 0)
                {
                    return 0;
                }

                try
                {
                    return X11InputMethodNative.XCreateIC(
                        inputMethod,
                        inputStyleName,
                        XimPreeditCallbacks | XimStatusNothing,
                        clientWindowName,
                        nativeWindow,
                        preeditAttributesName,
                        preeditAttributes,
                        0);
                }
                finally
                {
                    _ = X11Native.XFree(preeditAttributes);
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(inputStyleName);
                Marshal.FreeCoTaskMem(clientWindowName);
                Marshal.FreeCoTaskMem(preeditAttributesName);
                Marshal.FreeCoTaskMem(startCallbackName);
                Marshal.FreeCoTaskMem(doneCallbackName);
                Marshal.FreeCoTaskMem(drawCallbackName);
                Marshal.FreeCoTaskMem(caretCallbackName);
            }
        }

        private bool TryQueueChange(TextInputChange change)
        {
            var changes = ImmutableArray.Create(change);
            return _inputSamples.TryEnqueueTextInput(new PlatformTextInputSample(
                _window,
                changes,
                TimeProvider.System.GetTimestamp(),
                SessionId: SessionId,
                BaseDocumentRevision: _documentRevision));
        }

        private void UpdatePreedit(string text, int selectionStart)
        {
            if (_ignorePreeditUntilDone)
            {
                return;
            }

            if (!_compositionActive)
            {
                _compositionActive = true;
                _preeditText = string.Empty;
                _preeditSelectionStart = 0;

                if (!TryQueueChange(new TextInputChange(TextInputChangeKind.CompositionStarted)))
                {
                    _compositionActive = false;
                    _ignorePreeditUntilDone = true;
                    return;
                }
            }

            if (Encoding.UTF8.GetByteCount(text) > MaximumTextBytes)
            {
                _ignorePreeditUntilDone = true;
                CancelComposition(TextCompositionCancelReason.Overflow);
                return;
            }

            _preeditText = text;
            _preeditSelectionStart = selectionStart;

            if (!TryQueueChange(new TextInputChange(
                    TextInputChangeKind.CompositionUpdated,
                    text,
                    selectionStart: _preeditSelectionStart,
                    selectionLength: 0)))
            {
                _ignorePreeditUntilDone = true;
                CancelComposition(TextCompositionCancelReason.Overflow);
            }
        }

        [SuppressMessage(
            "Design",
            "CA1031:Do not catch general exception types",
            Justification = "Exceptions cannot cross the unmanaged callback boundary and are rethrown on the X11 owner thread.")]
        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static int OnPreeditStart(nint inputContext, nint clientData, nint callData)
        {
            if (!TryGetContext(clientData, out var context) || context is null)
            {
                return 0;
            }

            try
            {
                if (context._isDisposed)
                {
                    return 0;
                }

                context._ignorePreeditUntilDone = false;
                context._preeditText = string.Empty;
                context._preeditSelectionStart = 0;
                context._compositionActive = true;

                if (!context.TryQueueChange(new TextInputChange(TextInputChangeKind.CompositionStarted)))
                {
                    context._compositionActive = false;
                    context._ignorePreeditUntilDone = true;
                    return 0;
                }

                return MaximumPreeditCodepoints;
            }
            catch (Exception exception)
            {
                OnCallbackFailure(context, exception);
                return 0;
            }
        }

        [SuppressMessage(
            "Design",
            "CA1031:Do not catch general exception types",
            Justification = "Exceptions cannot cross the unmanaged callback boundary and are rethrown on the X11 owner thread.")]
        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static void OnPreeditDone(nint inputContext, nint clientData, nint callData)
        {
            if (!TryGetContext(clientData, out var context) || (context is null) || context._isDisposed)
            {
                return;
            }

            try
            {
                context._ignorePreeditUntilDone = false;

                if (context._compositionActive)
                {
                    context._compositionActive = false;
                    context._preeditText = string.Empty;
                    context._preeditSelectionStart = 0;
                    _ = context.TryQueueChange(new TextInputChange(TextInputChangeKind.CompositionEnded));
                }
            }
            catch (Exception exception)
            {
                OnCallbackFailure(context, exception);
            }
        }

        [SuppressMessage(
            "Design",
            "CA1031:Do not catch general exception types",
            Justification = "Exceptions cannot cross the unmanaged callback boundary and are rethrown on the X11 owner thread.")]
        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static void OnPreeditDraw(nint inputContext, nint clientData, nint callData)
        {
            if (!TryGetContext(clientData, out var context)
                || (context is null)
                || context._isDisposed
                || context._ignorePreeditUntilDone)
            {
                return;
            }

            try
            {
                if (callData == 0)
                {
                    throw new InvalidDataException("The X11 input method returned a missing preedit draw record.");
                }

                var draw = (X11InputMethodNative.XimPreeditDrawCallbackStruct*)callData;
                var replacement = (draw->Text == 0)
                    ? string.Empty
                    : ReadPreeditText((X11InputMethodNative.XimText*)draw->Text);
                var (text, selectionStart) = ApplyPreeditUpdate(
                    context._preeditText,
                    draw->ChangeFirst,
                    draw->ChangeLength,
                    replacement,
                    draw->Caret);
                context.UpdatePreedit(text, selectionStart);
            }
            catch (Exception exception)
            {
                OnCallbackFailure(context, exception);
            }
        }

        [SuppressMessage(
            "Design",
            "CA1031:Do not catch general exception types",
            Justification = "Exceptions cannot cross the unmanaged callback boundary and are rethrown on the X11 owner thread.")]
        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static void OnPreeditCaret(nint inputContext, nint clientData, nint callData)
        {
            if (!TryGetContext(clientData, out var context)
                || (context is null)
                || context._isDisposed
                || !context._compositionActive)
            {
                return;
            }

            try
            {
                if (callData == 0)
                {
                    throw new InvalidDataException("The X11 input method returned a missing preedit caret record.");
                }

                var caret = ((X11InputMethodNative.XimPreeditCaretCallbackStruct*)callData)->Position;
                var selectionStart = GetUtf16Offset(context._preeditText, caret);
                context.UpdatePreedit(context._preeditText, selectionStart);
            }
            catch (Exception exception)
            {
                OnCallbackFailure(context, exception);
            }
        }
    }
}
