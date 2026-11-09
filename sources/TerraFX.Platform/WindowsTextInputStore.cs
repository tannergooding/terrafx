// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;
using System.Text;
using TerraFX;
using TerraFX.Input;
using TerraFX.Interop.Windows;
using TerraFX.Numerics;
using TS = TerraFX.Interop.Windows.TS;

namespace TerraFX.Platform;

[GeneratedComClass]
[SupportedOSPlatform("windows10.0.22000")]
internal sealed unsafe partial class WindowsTextInputStore :
    IWindowsTextStoreACP,
    IWindowsTextStoreCompositionSink,
    IDisposable
{
    private const int EFail = unchecked((int)0x80004005);
    private const int EInvalidArgument = unchecked((int)0x80070057);
    private const int ENoInterface = unchecked((int)0x80004002);
    private const int MaximumChangeTextBytes = 4_096;
    private const int MaximumSnapshotCharacters = 65_536;
    private const int Succeeded = 0;
    private const uint TextStoreReadLock = (uint)TS.TS_LF_READ;
    private const uint TextStoreReadWriteLock = (uint)TS.TS_LF_READWRITE;
    private const uint TextStoreSynchronousLock = (uint)TS.TS_LF_SYNC;

    private static readonly ComWrappers s_comWrappers = new StrategyBasedComWrappers();
    private static readonly UTF8Encoding s_strictUtf8Encoding = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private bool _awaitingSnapshot;
    private nint _comInterface;
    private bool _compositionActive;
    private int _compositionDeleteLength;
    private bool _compositionHasText;
    private string _compositionOriginalText = string.Empty;
    private int _compositionSelectionLength;
    private int _compositionSelectionStart;
    private int _compositionStart;
    private string _compositionText = string.Empty;
    private bool _hasSurroundingText;
    private readonly PlatformInputSampleQueue _inputSamples;
    private bool _isDisposed;
    private bool _isTerminal;
    private uint _lockFlags;
    private readonly nint _nativeWindow;
    private readonly List<TextInputChange> _pendingChanges = [];
    private uint _pendingLockFlags;
    private int _pendingTextBytes;
    private int _selectionLength;
    private int _selectionStart;
    private ITextStoreACPSink* _sink;
    private uint _sinkMask;
    private nint _sinkUnknown;
    private WindowsTextInputSessionState _state;
    private string _text;
    private readonly TimeProvider _timeProvider;
    private readonly WindowHandle _window;

    internal WindowsTextInputStore(
        WindowHandle window,
        nint nativeWindow,
        WindowsTextInputSessionState state,
        PlatformInputSampleQueue inputSamples,
        TimeProvider timeProvider)
    {
        if (!window.IsValid || (state.Window != window))
        {
            throw new ArgumentException("The text-store snapshot must name its exact live window.", nameof(state));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero((long)nativeWindow);
        ArgumentNullException.ThrowIfNull(inputSamples);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _window = window;
        _nativeWindow = nativeWindow;
        _inputSamples = inputSamples;
        _timeProvider = timeProvider;
        _state = state;
        _text = state.SurroundingText ?? string.Empty;
        _hasSurroundingText = state.SurroundingText is not null;
        _selectionStart = state.SelectionStart;
        _selectionLength = state.SelectionLength;
    }

    internal long DocumentRevision => _state.DocumentRevision;

    internal long SessionId => _state.SessionId;

    private bool HasReadLock => (_lockFlags & TextStoreReadLock) != 0;

    private bool HasWriteLock => (_lockFlags & TextStoreReadWriteLock) == TextStoreReadWriteLock;

    [LibraryImport("user32.dll", EntryPoint = "ClientToScreen", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ClientToScreenNative(nint window, TextCaretPoint* point);

    private static TextInputChange CreateRejectedChange(string code, string message)
    {
        var diagnostic = new Diagnostic(
            code,
            DiagnosticSeverity.Error,
            Stopwatch.GetTimestamp(),
            hostId: null,
            worldGuid: null,
            deviceId: null,
            deviceEpoch: null,
            surfaceId: null,
            surfaceEpoch: null,
            swapchainEpoch: null,
            tick: null,
            systemId: null,
            passId: null,
            assetId: null,
            commandIdentity: null,
            operation: "WindowsTextInputStore",
            nativeResult: null,
            message: message,
            context: []);
        return new TextInputChange(TextInputChangeKind.TextEditRejected, diagnostic: diagnostic);
    }

    [LibraryImport("user32.dll", EntryPoint = "GetDpiForWindow", SetLastError = true)]
    private static partial uint GetDpiForWindowNative(nint window);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowRect", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRectNative(nint window, RECT* rect);

    private static int HResultFromWin32(int error)
        => (error <= 0) ? EFail : unchecked((int)(0x80070000u | ((uint)error & 0xFFFFu)));

    private static bool IsValidUtf16(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (char.IsHighSurrogate(text[index]))
            {
                if ((index + 1 >= text.Length) || !char.IsLowSurrogate(text[index + 1]))
                {
                    return false;
                }

                index++;
            }
            else if (char.IsLowSurrogate(text[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SplitsSurrogatePair(string text, int index)
        => (index > 0)
            && (index < text.Length)
            && char.IsHighSurrogate(text[index - 1])
            && char.IsLowSurrogate(text[index]);

    public int AdviseSink(Guid* riid, nint punk, uint mask)
    {
        if ((riid is null) || (punk == 0))
        {
            return EInvalidArgument;
        }

        if (*riid != typeof(ITextStoreACPSink).GUID)
        {
            return ENoInterface;
        }

        if (_sink is not null)
        {
            return (punk == _sinkUnknown) ? Succeeded : EFail;
        }

        var source = (IUnknown*)punk;
        var iidUnknown = typeof(IUnknown).GUID;
        void* sinkUnknown = null;
        var result = source->QueryInterface(&iidUnknown, &sinkUnknown);

        if (result.Value < 0)
        {
            return result.Value;
        }

        var sinkIid = typeof(ITextStoreACPSink).GUID;
        void* sink = null;
        result = source->QueryInterface(&sinkIid, &sink);

        if (result.Value < 0)
        {
            _ = ((IUnknown*)sinkUnknown)->Release();
            return result.Value;
        }

        _sinkUnknown = (nint)sinkUnknown;
        _sink = (ITextStoreACPSink*)sink;
        _sinkMask = mask;
        return Succeeded;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        CancelComposition(TextCompositionCancelReason.SessionDisposed);
        _pendingChanges.Clear();
        _pendingTextBytes = 0;

        if (_sink is not null)
        {
            _ = _sink->Release();
            _sink = null;
        }

        if (_sinkUnknown != 0)
        {
            _ = ((IUnknown*)_sinkUnknown)->Release();
            _sinkUnknown = 0;
        }

        if (_comInterface != 0)
        {
            _ = Marshal.Release(_comInterface);
            _comInterface = 0;
        }
    }

    public int FindNextAttrTransition(
        int start,
        int halt,
        uint count,
        Guid* attributes,
        uint flags,
        int* next,
        int* found,
        int* transition)
    {
        _ = count;
        _ = attributes;
        _ = flags;

        if ((next is null) || (found is null) || (transition is null) || (halt < start))
        {
            return EInvalidArgument;
        }

        *next = halt;
        *found = 0;
        *transition = 0;
        return Succeeded;
    }

    public int GetACPFromPoint(uint view, POINT* point, uint flags, int* acp)
    {
        _ = flags;

        if ((view != 0) || (point is null) || (acp is null))
        {
            return EInvalidArgument;
        }

        *acp = _selectionStart;
        return Succeeded;
    }

    public int GetActiveView(uint* view)
    {
        if (view is null)
        {
            return EInvalidArgument;
        }

        *view = 0;
        return Succeeded;
    }

    public int GetEmbedded(int position, Guid* service, Guid* iid, nint* unknown)
    {
        _ = position;
        _ = service;
        _ = iid;

        if (unknown is null)
        {
            return EInvalidArgument;
        }

        *unknown = 0;
        return TS.TS_E_NOOBJECT;
    }

    public int GetEndACP(int* end)
    {
        if (end is null)
        {
            return EInvalidArgument;
        }

        *end = _text.Length;
        return Succeeded;
    }

    public int GetFormattedText(int start, int end, nint* dataObject)
    {
        _ = start;
        _ = end;

        if (dataObject is null)
        {
            return EInvalidArgument;
        }

        *dataObject = 0;
        return TS.TS_E_FORMAT;
    }

    public int GetScreenExt(uint view, RECT* rect)
    {
        if ((view != 0) || (rect is null))
        {
            return EInvalidArgument;
        }

        if (!GetWindowRectNative(_nativeWindow, rect))
        {
            return HResultFromWin32(Marshal.GetLastPInvokeError());
        }

        return Succeeded;
    }

    public int GetSelection(uint index, uint count, TS_SELECTION_ACP* selection, uint* fetched)
    {
        if (!HasReadLock)
        {
            return TS.TS_E_NOLOCK;
        }

        if (fetched is null)
        {
            return EInvalidArgument;
        }

        *fetched = 0;

        if (count == 0)
        {
            return Succeeded;
        }

        if ((selection is null) || ((index != TS.TS_DEFAULT_SELECTION) && (index != 0)))
        {
            return EInvalidArgument;
        }

        selection->acpStart = _selectionStart;
        selection->acpEnd = checked(_selectionStart + _selectionLength);
        selection->style.ase = TsActiveSelEnd.TS_AE_END;
        selection->style.fInterimChar = 0;
        *fetched = 1;
        return Succeeded;
    }

    public int GetStatus(TS_STATUS* status)
    {
        if (status is null)
        {
            return EInvalidArgument;
        }

        status->dwStaticFlags = (_awaitingSnapshot || _isTerminal) ? (uint)TS.TS_SD_READONLY : 0;
        status->dwDynamicFlags = 0;
        return Succeeded;
    }

    public int GetText(
        int start,
        int end,
        char* text,
        uint textCapacity,
        uint* textLength,
        TS_RUNINFO* runInfo,
        uint runInfoCapacity,
        uint* runInfoCount,
        int* next)
    {
        if (!HasReadLock)
        {
            return TS.TS_E_NOLOCK;
        }

        if ((textLength is null) || (next is null)
            || ((textCapacity > 0) && (text is null))
            || ((runInfoCapacity > 0) && ((runInfo is null) || (runInfoCount is null))))
        {
            return EInvalidArgument;
        }

        if ((start < 0) || (start > _text.Length) || (end < -1))
        {
            return TS.TS_E_INVALIDPOS;
        }

        var actualEnd = (end == -1) ? _text.Length : end;

        if ((actualEnd < start)
            || (actualEnd > _text.Length)
            || SplitsSurrogatePair(_text, start)
            || SplitsSurrogatePair(_text, actualEnd))
        {
            return TS.TS_E_INVALIDPOS;
        }

        var copyLength = checked((int)Math.Min(checked((uint)(actualEnd - start)), textCapacity));

        if (copyLength > 0)
        {
            fixed (char* source = _text)
            {
                Buffer.MemoryCopy(
                    source + start,
                    text,
                    checked((long)copyLength * sizeof(char)),
                    checked((long)copyLength * sizeof(char)));
            }
        }

        *textLength = checked((uint)copyLength);
        *next = checked(start + copyLength);

        if (runInfoCount is not null)
        {
            *runInfoCount = 0;
        }

        if ((runInfoCapacity > 0) && (copyLength > 0))
        {
            runInfo[0].uCount = checked((uint)copyLength);
            runInfo[0].type = TsRunType.TS_RT_PLAIN;
            *runInfoCount = 1;
        }

        return Succeeded;
    }

    public int GetTextExt(uint view, int start, int end, RECT* rect, int* clipped)
    {
        if ((view != 0) || (rect is null) || (clipped is null))
        {
            return EInvalidArgument;
        }

        if ((start < 0) || (end < start) || (end > _text.Length)
            || !IsValidRange(start, end - start))
        {
            return TS.TS_E_INVALIDPOS;
        }

        var dpi = GetDpiForWindowNative(_nativeWindow);

        if (dpi == 0)
        {
            return HResultFromWin32(Marshal.GetLastPInvokeError());
        }

        try
        {
            var scale = dpi / 96.0;
            var point = new TextCaretPoint {
                X = checked((int)Math.Round(_state.CaretPosition.X * scale, MidpointRounding.ToEven)),
                Y = checked((int)Math.Round(_state.CaretPosition.Y * scale, MidpointRounding.ToEven)),
            };

            if (!ClientToScreenNative(_nativeWindow, &point))
            {
                return HResultFromWin32(Marshal.GetLastPInvokeError());
            }

            var width = checked((int)Math.Round(_state.CaretSize.X * scale, MidpointRounding.ToEven));
            var height = checked((int)Math.Round(_state.CaretSize.Y * scale, MidpointRounding.ToEven));
            var rectValues = (int*)rect;
            rectValues[0] = point.X;
            rectValues[1] = point.Y;
            rectValues[2] = checked(point.X + width);
            rectValues[3] = checked(point.Y + height);
            *clipped = 0;
            return Succeeded;
        }
        catch (OverflowException)
        {
            return EInvalidArgument;
        }
    }

    public int GetWnd(uint view, nint* window)
    {
        if ((view != 0) || (window is null))
        {
            return EInvalidArgument;
        }

        *window = _nativeWindow;
        return Succeeded;
    }

    public int InsertEmbedded(uint flags, int start, int end, nint dataObject, TS_TEXTCHANGE* change)
    {
        _ = flags;
        _ = start;
        _ = end;
        _ = dataObject;
        _ = change;
        return TS.TS_E_FORMAT;
    }

    public int InsertEmbeddedAtSelection(uint flags, nint dataObject, int* start, int* end, TS_TEXTCHANGE* change)
    {
        _ = flags;
        _ = dataObject;
        _ = start;
        _ = end;
        _ = change;
        return TS.TS_E_FORMAT;
    }

    public int InsertTextAtSelection(
        uint flags,
        char* text,
        uint textLength,
        int* start,
        int* end,
        TS_TEXTCHANGE* change)
    {
        if (!HasWriteLock)
        {
            return TS.TS_E_NOLOCK;
        }

        if ((textLength > 0) && (text is null))
        {
            return EInvalidArgument;
        }

        if (textLength > MaximumChangeTextBytes)
        {
            RecordOverflow();
            return TS.TS_E_NOLOCK;
        }

        var insertion = (textLength == 0) ? string.Empty : new string(text, 0, checked((int)textLength));
        var insertionStart = _selectionStart;
        var insertionLength = _selectionLength;
        var result = ReplaceText(insertionStart, insertionLength, insertion, flags, change);

        if (result >= 0)
        {
            if (start is not null)
            {
                *start = insertionStart;
            }

            if (end is not null)
            {
                *end = checked(insertionStart + insertion.Length);
            }
        }

        return result;
    }

    public int OnEndComposition(nint compositionView)
    {
        _ = compositionView;

        if (!_compositionActive)
        {
            return Succeeded;
        }

        var changesQueued = true;

        if (_compositionText.Length > 0)
        {
            var change = _hasSurroundingText
                ? new TextInputChange(
                    TextInputChangeKind.TextCommitted,
                    _compositionText,
                    editStart: _compositionStart,
                    deleteLength: _compositionDeleteLength)
                : new TextInputChange(TextInputChangeKind.TextCommitted, _compositionText);

            if (!TryQueueChange(change))
            {
                changesQueued = false;
            }
        }

        changesQueued &= TryQueueChange(new TextInputChange(TextInputChangeKind.CompositionEnded));
        ResetCompositionState();
        return changesQueued ? Succeeded : TS.TS_E_NOLOCK;
    }

    public int OnStartComposition(nint compositionView, int* accepted)
    {
        _ = compositionView;

        if (accepted is null)
        {
            return EInvalidArgument;
        }

        if (_compositionActive || _awaitingSnapshot || _isTerminal)
        {
            *accepted = 0;
            return Succeeded;
        }

        _compositionActive = true;
        _compositionOriginalText = _text;
        _compositionStart = _selectionStart;
        _compositionDeleteLength = _selectionLength;
        _compositionSelectionStart = 0;
        _compositionSelectionLength = 0;
        _compositionText = string.Empty;
        _compositionHasText = false;

        if (!TryQueueChange(new TextInputChange(TextInputChangeKind.CompositionStarted)))
        {
            ResetCompositionState();
            *accepted = 0;
            return Succeeded;
        }

        *accepted = 1;
        return Succeeded;
    }

    public int OnUpdateComposition(nint compositionView, nint range)
    {
        _ = compositionView;
        _ = range;

        if (!_compositionActive)
        {
            return TS.TS_E_NOLOCK;
        }

        var selectionStart = Math.Clamp(_compositionSelectionStart, 0, _compositionText.Length);
        var selectionLength = Math.Clamp(
            _compositionSelectionLength,
            0,
            _compositionText.Length - selectionStart);
        return TryQueueChange(new TextInputChange(
            TextInputChangeKind.CompositionUpdated,
            _compositionText,
            selectionStart,
            selectionLength))
            ? Succeeded
            : TS.TS_E_NOLOCK;
    }

    public int QueryInsert(int start, int end, uint textLength, int* insertStart, int* insertEnd)
    {
        if ((insertStart is null) || (insertEnd is null))
        {
            return EInvalidArgument;
        }

        if ((start < 0) || (end < start) || (end > _text.Length)
            || (textLength > MaximumSnapshotCharacters)
            || SplitsSurrogatePair(_text, start)
            || SplitsSurrogatePair(_text, end))
        {
            return TS.TS_E_INVALIDPOS;
        }

        var endPosition = checked(start + (int)textLength);

        if (endPosition > MaximumSnapshotCharacters)
        {
            return TS.TS_E_INVALIDPOS;
        }

        *insertStart = start;
        *insertEnd = endPosition;
        return Succeeded;
    }

    public int QueryInsertEmbedded(Guid* service, nint formatEtc, int* insertable)
    {
        _ = service;
        _ = formatEtc;

        if (insertable is null)
        {
            return EInvalidArgument;
        }

        *insertable = 0;
        return Succeeded;
    }

    public int RequestAttrsAtPosition(int position, uint count, Guid* attributes, uint flags)
    {
        _ = position;
        _ = count;
        _ = attributes;
        _ = flags;
        return Succeeded;
    }

    public int RequestAttrsTransitioningAtPosition(int position, uint count, Guid* attributes, uint flags)
    {
        _ = position;
        _ = count;
        _ = attributes;
        _ = flags;
        return Succeeded;
    }

    public int RequestLock(uint lockFlags, int* sessionResult)
    {
        if (sessionResult is null)
        {
            return EInvalidArgument;
        }

        if (_isTerminal)
        {
            *sessionResult = TS.TS_E_NOLOCK;
            return Succeeded;
        }

        if (_sink is null)
        {
            *sessionResult = TS.TS_E_NOLOCK;
            return Succeeded;
        }

        if (_lockFlags != 0)
        {
            *sessionResult = TS.TS_E_SYNCHRONOUS;
            return Succeeded;
        }

        if (_pendingLockFlags != 0)
        {
            *sessionResult = TS.TS_E_SYNCHRONOUS;
            return Succeeded;
        }

        if (((lockFlags & TextStoreReadLock) == 0)
            || ((lockFlags & ~(TextStoreReadWriteLock | TextStoreSynchronousLock)) != 0))
        {
            *sessionResult = EInvalidArgument;
            return Succeeded;
        }

        if ((lockFlags & TextStoreSynchronousLock) == 0)
        {
            _pendingLockFlags = lockFlags;
            *sessionResult = TS.TS_S_ASYNC;
            return Succeeded;
        }

        *sessionResult = GrantLock(lockFlags);
        return Succeeded;
    }

    public int RequestSupportedAttrs(uint flags, uint count, Guid* attributes)
    {
        _ = flags;
        _ = count;
        _ = attributes;
        return Succeeded;
    }

    public int RetrieveRequestedAttrs(uint count, TS_ATTRVAL* attributes, uint* fetched)
    {
        _ = count;
        _ = attributes;

        if (fetched is null)
        {
            return EInvalidArgument;
        }

        *fetched = 0;
        return Succeeded;
    }

    public int SetSelection(uint count, TS_SELECTION_ACP* selection)
    {
        if (!HasWriteLock)
        {
            return TS.TS_E_NOLOCK;
        }

        if (_awaitingSnapshot || _isTerminal)
        {
            return TS.TS_E_NOLOCK;
        }

        if ((count != 1) || (selection is null))
        {
            return EInvalidArgument;
        }

        var selectionStart = selection->acpStart;
        var selectionEnd = selection->acpEnd;

        if ((selectionStart < 0)
            || (selectionEnd < selectionStart)
            || (selectionEnd > _text.Length)
            || SplitsSurrogatePair(_text, selectionStart)
            || SplitsSurrogatePair(_text, selectionEnd))
        {
            return TS.TS_E_INVALIDPOS;
        }

        var selectionLength = selectionEnd - selectionStart;
        var compositionSelectionStart = 0;

        if (_compositionActive)
        {
            compositionSelectionStart = selectionStart - _compositionStart;

            if ((compositionSelectionStart < 0)
                || (compositionSelectionStart > _compositionText.Length)
                || (selectionLength > (_compositionText.Length - compositionSelectionStart))
                || SplitsSurrogatePair(_compositionText, compositionSelectionStart)
                || SplitsSurrogatePair(_compositionText, compositionSelectionStart + selectionLength))
            {
                return TS.TS_E_INVALIDPOS;
            }
        }

        _selectionStart = selectionStart;
        _selectionLength = selectionLength;

        if (_compositionActive)
        {
            _compositionSelectionStart = compositionSelectionStart;
            _compositionSelectionLength = selectionLength;
        }

        return Succeeded;
    }

    public int SetText(uint flags, int start, int end, char* text, uint textLength, TS_TEXTCHANGE* change)
    {
        if (!HasWriteLock)
        {
            return TS.TS_E_NOLOCK;
        }

        if ((textLength > 0) && (text is null))
        {
            return EInvalidArgument;
        }

        if (textLength > MaximumChangeTextBytes)
        {
            RecordOverflow();
            return TS.TS_E_NOLOCK;
        }

        if ((start < 0) || (end < start))
        {
            return TS.TS_E_INVALIDPOS;
        }

        var replacement = (textLength == 0) ? string.Empty : new string(text, 0, checked((int)textLength));
        return ReplaceText(start, end - start, replacement, flags, change);
    }

    public int UnadviseSink(nint punk)
    {
        if ((_sink is null) || (_sinkUnknown == 0) || (punk == 0))
        {
            return TS.TS_E_NOSERVICE;
        }

        var source = (IUnknown*)punk;
        var iidUnknown = typeof(IUnknown).GUID;
        void* canonicalUnknown = null;
        var result = source->QueryInterface(&iidUnknown, &canonicalUnknown);

        if (result.Value < 0)
        {
            return result.Value;
        }

        if ((nint)canonicalUnknown != _sinkUnknown)
        {
            _ = ((IUnknown*)canonicalUnknown)->Release();
            return EInvalidArgument;
        }

        _ = ((IUnknown*)canonicalUnknown)->Release();
        _ = _sink->Release();
        _ = ((IUnknown*)_sinkUnknown)->Release();
        _sink = null;
        _sinkMask = 0;
        _sinkUnknown = 0;
        return Succeeded;
    }

    internal void CancelComposition(TextCompositionCancelReason reason)
    {
        if (!_compositionActive)
        {
            return;
        }

        _text = _compositionOriginalText;
        _selectionStart = _compositionStart;
        _selectionLength = _compositionDeleteLength;
        _ = TryQueueChange(new TextInputChange(
            TextInputChangeKind.CompositionCanceled,
            cancellationReason: reason));
        ResetCompositionState();
    }

    internal void FlushPendingTextInput()
    {
        if (_pendingChanges.Count == 0)
        {
            return;
        }

        var changes = ImmutableArray.CreateRange(_pendingChanges);
        var documentChanged = false;

        foreach (var change in changes)
        {
            documentChanged |= change.ChangesDocument;
        }

        var sample = new PlatformTextInputSample(
            _window,
            changes,
            _timeProvider.GetTimestamp(),
            SessionId: _state.SessionId,
            BaseDocumentRevision: _state.DocumentRevision);

        if (!_inputSamples.TryEnqueueTextInput(sample))
        {
            _isTerminal = true;
        }
        else if (documentChanged)
        {
            _awaitingSnapshot = true;

            if (_state.IsProtected)
            {
                _text = string.Empty;
                _selectionStart = 0;
                _selectionLength = 0;
            }
        }

        _pendingChanges.Clear();
        _pendingTextBytes = 0;
    }

    internal nint GetIUnknownPointer()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _comInterface = (_comInterface == 0)
            ? s_comWrappers.GetOrCreateComInterfaceForObject(this, CreateComInterfaceFlags.None)
            : _comInterface;
        return _comInterface;
    }

    internal int GrantPendingLock()
    {
        if (_isTerminal)
        {
            _pendingLockFlags = 0;
            return Succeeded;
        }

        if (_pendingLockFlags == 0)
        {
            return Succeeded;
        }

        var lockFlags = _pendingLockFlags;
        _pendingLockFlags = 0;
        return GrantLock(lockFlags);
    }

    internal void UpdateSnapshot(WindowsTextInputSessionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (state.SessionId != _state.SessionId)
        {
            throw new ArgumentException("A text-store update must preserve the active session identity.", nameof(state));
        }

        if (state.DocumentRevision <= _state.DocumentRevision)
        {
            throw new ArgumentOutOfRangeException(nameof(state), "A text-store update must advance the document revision.");
        }

        if (_compositionActive)
        {
            throw new InvalidOperationException("The authoritative text snapshot cannot replace an active native composition.");
        }

        FlushPendingTextInput();
        var oldTextLength = _text.Length;
        var oldSelectionStart = _selectionStart;
        var oldSelectionLength = _selectionLength;
        _state = state;
        _text = state.SurroundingText ?? string.Empty;
        _hasSurroundingText = state.SurroundingText is not null;
        _selectionStart = state.SelectionStart;
        _selectionLength = state.SelectionLength;
        _awaitingSnapshot = false;
        _isTerminal = false;

        if (_sink is not null)
        {
            if ((_sinkMask & (uint)TS.TS_AS_TEXT_CHANGE) != 0)
            {
                var change = new TS_TEXTCHANGE {
                    acpStart = 0,
                    acpOldEnd = oldTextLength,
                    acpNewEnd = _text.Length,
                };
                var result = _sink->OnTextChange((uint)TS.TS_TC_CORRECTION, &change);

                if (result.Value < 0)
                {
                    throw new WindowsTextInputInteropException("text-store snapshot notification", result.Value);
                }
            }

            if (((_sinkMask & (uint)TS.TS_AS_SEL_CHANGE) != 0)
                && ((oldSelectionStart != _selectionStart) || (oldSelectionLength != _selectionLength)))
            {
                var result = _sink->OnSelectionChange();

                if (result.Value < 0)
                {
                    throw new WindowsTextInputInteropException("text-store selection notification", result.Value);
                }
            }
        }
    }

    private int GrantLock(uint lockFlags)
    {
        if (_lockFlags != 0)
        {
            return TS.TS_E_SYNCHRONOUS;
        }

        if (_sink is null)
        {
            return TS.TS_E_NOLOCK;
        }

        _lockFlags = lockFlags;
        var result = _sink->OnLockGranted(lockFlags).Value;
        _lockFlags = 0;
        FlushPendingTextInput();
        return result;
    }

    private bool IsValidRange(int start, int length)
        => (start >= 0)
            && (length >= 0)
            && (start <= _text.Length)
            && (length <= _text.Length - start)
            && !SplitsSurrogatePair(_text, start)
            && !SplitsSurrogatePair(_text, start + length);

    private void RecordOverflow()
    {
        _inputSamples.RecordTextInputOverflow(_window, _state.SessionId);
        _pendingChanges.Clear();
        _pendingTextBytes = 0;
        _isTerminal = true;
    }

    private int ReplaceText(int start, int length, string replacement, uint flags, TS_TEXTCHANGE* change)
    {
        if (change is null)
        {
            return EInvalidArgument;
        }

        if (_isTerminal)
        {
            return TS.TS_E_NOLOCK;
        }

        if (!IsValidRange(start, length)
            || !IsValidUtf16(replacement))
        {
            _ = TryQueueChange(CreateRejectedChange(
                "TextInput.InvalidNativeText",
                "The Windows TSF text store received an invalid UTF-16 range or replacement."));
            change->acpStart = Math.Max(0, start);
            change->acpOldEnd = change->acpStart;
            change->acpNewEnd = change->acpStart;
            return TS.TS_E_INVALIDPOS;
        }

        int replacementBytes;

        try
        {
            replacementBytes = s_strictUtf8Encoding.GetByteCount(replacement);
        }
        catch (EncoderFallbackException)
        {
            _ = TryQueueChange(CreateRejectedChange(
                "TextInput.InvalidNativeText",
                "The Windows TSF text store received an unpaired UTF-16 surrogate."));
            return TS.TS_E_INVALIDPOS;
        }

        if (replacementBytes > MaximumChangeTextBytes)
        {
            RecordOverflow();
            return TS.TS_E_NOLOCK;
        }

        var newTextLength = checked(_text.Length - length + replacement.Length);

        if (newTextLength > MaximumSnapshotCharacters)
        {
            RecordOverflow();
            return TS.TS_E_NOLOCK;
        }

        var changed = new TS_TEXTCHANGE {
            acpStart = start,
            acpOldEnd = checked(start + length),
            acpNewEnd = checked(start + replacement.Length),
        };
        *change = changed;

        if ((flags & (uint)TS.TS_IAS_QUERYONLY) != 0)
        {
            return Succeeded;
        }

        if (_awaitingSnapshot && !_compositionActive)
        {
            if (!TryQueueDocumentChange(start, length, replacement))
            {
                return TS.TS_E_NOLOCK;
            }

            return TS.TS_E_NOLOCK;
        }

        string updatedText;

        try
        {
            updatedText = string.Concat(
                _text.AsSpan(0, start),
                replacement.AsSpan(),
                _text.AsSpan(start + length));
        }
        catch (ArgumentOutOfRangeException)
        {
            return TS.TS_E_INVALIDPOS;
        }

        if (_compositionActive)
        {
            if (!_compositionHasText)
            {
                if ((start != _compositionStart) || (length != _compositionDeleteLength))
                {
                    _ = TryQueueChange(CreateRejectedChange(
                        "TextInput.InvalidNativeText",
                        "The initial TSF composition edit did not match the selected surrounding-text range."));
                    return TS.TS_E_INVALIDPOS;
                }

                _compositionText = replacement;
                _compositionHasText = true;
            }
            else
            {
                var compositionOffset = start - _compositionStart;

                if ((compositionOffset < 0)
                    || (compositionOffset > _compositionText.Length)
                    || (length > (_compositionText.Length - compositionOffset)))
                {
                    _ = TryQueueChange(CreateRejectedChange(
                        "TextInput.InvalidNativeText",
                        "The TSF composition update extended outside its tracked preedit range."));
                    return TS.TS_E_INVALIDPOS;
                }

                _compositionText = string.Concat(
                    _compositionText.AsSpan(0, compositionOffset),
                    replacement.AsSpan(),
                    _compositionText.AsSpan(compositionOffset + length));
            }

            _text = updatedText;
            _selectionStart = checked(start + replacement.Length);
            _selectionLength = 0;
            _compositionSelectionStart = _selectionStart - _compositionStart;
            _compositionSelectionLength = 0;
            return Succeeded;
        }

        if (!TryQueueDocumentChange(start, length, replacement))
        {
            return TS.TS_E_NOLOCK;
        }

        _text = updatedText;
        _selectionStart = checked(start + replacement.Length);
        _selectionLength = 0;
        return Succeeded;
    }

    private void ResetCompositionState()
    {
        _compositionActive = false;
        _compositionDeleteLength = 0;
        _compositionHasText = false;
        _compositionOriginalText = string.Empty;
        _compositionSelectionLength = 0;
        _compositionSelectionStart = 0;
        _compositionStart = 0;
        _compositionText = string.Empty;
    }

    private bool TryQueueChange(TextInputChange change)
    {
        if (_isTerminal)
        {
            return false;
        }

        var textBytes = change.Utf8TextBytes;

        if ((_pendingChanges.Count >= TextInputBatchBudget.MaximumPendingChanges)
            || (textBytes > (TextInputBatchBudget.MaximumPendingTextBytes - _pendingTextBytes)))
        {
            RecordOverflow();
            return false;
        }

        _pendingChanges.Add(change);
        _pendingTextBytes += textBytes;
        return true;
    }

    private bool TryQueueDocumentChange(int start, int length, string replacement)
    {
        if (replacement.Length == 0)
        {
            if (length == 0)
            {
                return true;
            }

            if (!_hasSurroundingText)
            {
                _ = TryQueueChange(CreateRejectedChange(
                    "TextInput.TextEditRejected",
                    "A TSF deletion requires a surrounding-text snapshot."));
                return false;
            }

            return TryQueueChange(new TextInputChange(
                TextInputChangeKind.TextEditRequested,
                editStart: start,
                deleteLength: length));
        }

        var change = _hasSurroundingText
            ? new TextInputChange(
                TextInputChangeKind.TextCommitted,
                replacement,
                editStart: start,
                deleteLength: length)
            : new TextInputChange(TextInputChangeKind.TextCommitted, replacement);
        return TryQueueChange(change);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TextCaretPoint
    {
        internal int X;
        internal int Y;
    }
}
