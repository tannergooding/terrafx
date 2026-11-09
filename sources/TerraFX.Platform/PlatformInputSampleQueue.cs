// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using TerraFX;
using TerraFX.Input;

namespace TerraFX.Platform;

internal sealed class PlatformInputSampleQueue
{
    internal const int DefaultCapacity = 4096;
    private const int MaximumOverflowedTextWindows = 256;

    private bool _allTextInputSamplesOverflowed;
    private long _captureOrder;
    private int _count;
    private long _discardedSampleCount;
    private long _discardedTextSampleCount;
    private int _head;
    private readonly int _ownerThreadId;
    private int _pendingTextBytes;
    private int _pendingTextChangeCount;
    private readonly PlatformInputSample[] _samples;
    private int _textCount;
    private int _textHead;
    private readonly Dictionary<(WindowHandle Window, long? SessionId), long> _textOverflowsByWindow;
    private readonly PlatformTextInputSample[] _textSamples;

    internal PlatformInputSampleQueue(int ownerThreadId, int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ownerThreadId, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        _ownerThreadId = ownerThreadId;
        _samples = new PlatformInputSample[capacity];
        _textSamples = new PlatformTextInputSample[capacity];
        _textOverflowsByWindow = [];
    }

    internal bool HasCapacity
    {
        get
        {
            EnsureOwnerThread();
            return _count < _samples.Length;
        }
    }

    internal PlatformInputSampleBatch Drain()
    {
        EnsureOwnerThread();
        var samples = ImmutableArray.CreateBuilder<PlatformInputSample>(_count);
        var textSamples = ImmutableArray.CreateBuilder<PlatformTextInputSample>(_textCount);

        while (_count != 0)
        {
            samples.Add(_samples[_head]);
            _samples[_head] = default;
            _head = (_head + 1) % _samples.Length;
            _count--;
        }

        while (_textCount != 0)
        {
            textSamples.Add(_textSamples[_textHead]);
            _textSamples[_textHead] = default;
            _textHead = (_textHead + 1) % _textSamples.Length;
            _textCount--;
        }

        _head = 0;
        _textHead = 0;
        var discardedSampleCount = _discardedSampleCount;
        _discardedSampleCount = 0;
        var textOverflows = ImmutableArray.CreateBuilder<PlatformTextInputOverflow>(_textOverflowsByWindow.Count);

        foreach (var (key, discardedTextCount) in _textOverflowsByWindow)
        {
            textOverflows.Add(new PlatformTextInputOverflow(key.Window, key.SessionId, discardedTextCount));
        }

        _textOverflowsByWindow.Clear();
        var allTextInputSamplesOverflowed = _allTextInputSamplesOverflowed;
        _allTextInputSamplesOverflowed = false;
        var discardedTextSampleCount = _discardedTextSampleCount;
        _discardedTextSampleCount = 0;
        _pendingTextChangeCount = 0;
        _pendingTextBytes = 0;

        return new PlatformInputSampleBatch(
            samples.MoveToImmutable(),
            discardedSampleCount,
            textSamples.MoveToImmutable(),
            textOverflows.MoveToImmutable(),
            allTextInputSamplesOverflowed,
            discardedTextSampleCount);
    }

    internal void RecordTextInputOverflow(WindowHandle window, long sessionId)
    {
        EnsureOwnerThread();

        if (!window.IsValid)
        {
            throw new ArgumentException("A text input overflow must identify a valid window.", nameof(window));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sessionId);
        RecordTextOverflow(window, sessionId);
    }

    internal bool TryEnqueue(in PlatformInputSample sample)
    {
        EnsureOwnerThread();

        if (!Enum.IsDefined(sample.Kind) || (sample.Kind == PhysicalInputEventKind.None))
        {
            throw new ArgumentOutOfRangeException(nameof(sample));
        }

        if (!Enum.IsDefined(sample.DeviceKind)
            || (sample.Window is { IsValid: false })
            || ((sample.NativeDeviceIdentity == 0)
                && (sample.Kind is PhysicalInputEventKind.DeviceAdded or PhysicalInputEventKind.DeviceRemoved))
            || ((sample.UnmappedGamepadButtonMask != 0)
                && ((sample.Kind != PhysicalInputEventKind.StateReconciliation)
                    || (sample.DeviceKind != PlatformInputDeviceKind.Gamepad))))
        {
            throw new ArgumentException("The platform input sample has an invalid source.", nameof(sample));
        }

        if (_count == _samples.Length)
        {
            _discardedSampleCount = checked(_discardedSampleCount + 1);
            return false;
        }

        var tail = (_head + _count) % _samples.Length;
        _samples[tail] = sample with { CaptureOrder = checked(++_captureOrder) };
        _count++;
        return true;
    }

    internal bool TryEnqueueTextInput(in PlatformTextInputSample sample)
    {
        EnsureOwnerThread();

        if (!sample.Window.IsValid || sample.Changes.IsDefaultOrEmpty
            || (sample.SessionId is <= 0)
            || (sample.BaseDocumentRevision is <= 0)
            || (sample.GroupWithOtherCommits
                && ((sample.SessionId is not null) || (sample.BaseDocumentRevision is not null))))
        {
            throw new ArgumentException("The platform text input sample has an invalid source or change array.", nameof(sample));
        }

        if (sample.Changes.Length > TextInputBatchBudget.MaximumPendingChanges)
        {
            RecordTextOverflow(sample.Window, sample.SessionId);
            return false;
        }

        var textBytes = 0;

        foreach (var change in sample.Changes)
        {
            if ((change.Kind == TextInputChangeKind.Invalid) || !Enum.IsDefined(change.Kind))
            {
                throw new ArgumentException("A platform text input sample cannot contain a default or undefined change.", nameof(sample));
            }

            textBytes = checked(textBytes + change.Utf8TextBytes);

            if (textBytes > TextInputBatchBudget.MaximumPendingTextBytes)
            {
                RecordTextOverflow(sample.Window, sample.SessionId);
                return false;
            }
        }

        if ((_textCount == _textSamples.Length)
            || (sample.Changes.Length > (TextInputBatchBudget.MaximumPendingChanges - _pendingTextChangeCount))
            || (textBytes > (TextInputBatchBudget.MaximumPendingTextBytes - _pendingTextBytes)))
        {
            RecordTextOverflow(sample.Window, sample.SessionId);
            return false;
        }

        var tail = (_textHead + _textCount) % _textSamples.Length;
        _textSamples[tail] = sample with { CaptureOrder = checked(++_captureOrder) };
        _textCount++;
        _pendingTextChangeCount += sample.Changes.Length;
        _pendingTextBytes += textBytes;
        return true;
    }

    private void EnsureOwnerThread()
    {
        if (_ownerThreadId != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException("The platform input sample queue is restricted to its owner thread.");
        }
    }

    private void RecordTextOverflow(WindowHandle window, long? sessionId)
    {
        _discardedTextSampleCount = checked(_discardedTextSampleCount + 1);
        var key = (window, sessionId);

        if (_allTextInputSamplesOverflowed || _textOverflowsByWindow.ContainsKey(key))
        {
            if (_textOverflowsByWindow.TryGetValue(key, out var discardedTextCount))
            {
                _textOverflowsByWindow[key] = checked(discardedTextCount + 1);
            }

            return;
        }

        if (_textOverflowsByWindow.Count == MaximumOverflowedTextWindows)
        {
            _textOverflowsByWindow.Clear();
            _allTextInputSamplesOverflowed = true;
            return;
        }

        _textOverflowsByWindow.Add(key, 1);
    }
}
