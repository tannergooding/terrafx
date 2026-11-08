// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace TerraFX.Input;

/// <summary>Captures a bounded, recipient-local input stream for in-memory replay.</summary>
public sealed class InputRecorder : IDisposable
{
    internal const int MaximumRecordCount = 65_536;

    private readonly InputRecordingBaseline _baseline;
    private InputRecording? _completedRecording;
    private readonly List<InputRecordingEntry> _entries = [];
    private long? _firstMissingHostSequence;
    private bool _hasRecordingOverflow;
    private bool _hasSensitiveTextRedaction;
    private bool _hasTextOmission;
    private bool _isComplete = true;
    private bool _isDisposed;
    private long? _lastMissingHostSequence;
    private readonly int _ownerThreadId;
    private readonly bool _recordTextBatches;
    private int _recordedTextBytes;
    private readonly InputRouter _router;

    internal InputRecorder(
        InputRouter router,
        bool recordTextBatches,
        InputRecordingBaseline baseline)
    {
        ArgumentNullException.ThrowIfNull(router);
        _router = router;
        _ownerThreadId = Environment.CurrentManagedThreadId;
        _recordTextBatches = recordTextBatches;
        _baseline = baseline;
    }

    /// <summary>Gets whether every admitted record and requested text payload is present.</summary>
    /// <exception cref="InvalidOperationException">The property is accessed from another thread.</exception>
    public bool IsComplete
    {
        get
        {
            EnsureOwnerThread();
            return _isComplete && !_hasTextOmission && !_hasSensitiveTextRedaction;
        }
    }

    internal InputRecordingBaseline Baseline => _baseline;

    internal ImmutableArray<InputRecordingEntry> Entries => [.. _entries];

    internal long? FirstMissingHostSequence => _firstMissingHostSequence;

    internal bool HasSensitiveTextRedaction => _hasSensitiveTextRedaction;

    internal bool HasTextOmission => _hasTextOmission;

    internal bool IsDisposed => _isDisposed;

    internal long? LastMissingHostSequence => _lastMissingHostSequence;

    internal bool RecordTextBatches => _recordTextBatches;

    private static int GetTextByteCount(TextInputBatch batch)
    {
        var textBytes = 0;

        foreach (var change in batch.Changes)
        {
            textBytes = checked(textBytes + change.Utf8TextBytes);
        }

        return textBytes;
    }

    /// <summary>Completes the recording and returns its immutable in-memory snapshot.</summary>
    /// <returns>The completed input recording.</returns>
    /// <exception cref="InvalidOperationException">The recorder has already completed or was disposed.</exception>
    public InputRecording Complete()
    {
        EnsureOwnerThread();

        if (_completedRecording is { } completedRecording)
        {
            return completedRecording;
        }

        ObjectDisposedException.ThrowIf(_isDisposed, this);
        return _router.CompleteRecording(this);
    }

    /// <summary>Stops recording and discards the resulting snapshot if it has not already completed.</summary>
    public void Dispose()
    {
        EnsureOwnerThread();

        if (_isDisposed || (_completedRecording is not null))
        {
            return;
        }

        _router.DiscardRecording(this);
        _isDisposed = true;
    }

    internal void MarkCompleted(InputRecording recording)
    {
        ArgumentNullException.ThrowIfNull(recording);
        _completedRecording = recording;
        _isDisposed = true;
    }

    internal void MarkDiscontinuity(long hostSequence)
    {
        if (_hasRecordingOverflow)
        {
            return;
        }

        _isComplete = false;
        _hasRecordingOverflow = true;
        RecordMissingSequence(hostSequence);
    }

    internal void MarkDisposed()
        => _isDisposed = true;

    internal void Record(InputRecordingEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (_isDisposed || _hasRecordingOverflow)
        {
            return;
        }

        if (_entries.Count >= MaximumRecordCount)
        {
            _isComplete = false;
            _hasRecordingOverflow = true;
            RecordMissingSequence(entry.HostSequence);
            return;
        }

        if (entry.TextBatch is { } textBatch)
        {
            var textBytes = GetTextByteCount(textBatch);

            if (textBytes > (TextInputBatchBudget.MaximumPendingTextBytes - _recordedTextBytes))
            {
                _isComplete = false;
                _hasTextOmission = true;
                RecordMissingSequence(entry.HostSequence);
                _entries.Add(new InputRecordingEntry(
                    InputRecordingEntryKind.TextOmitted,
                    entry.HostTimestamp,
                    entry.HostSequence,
                    entry.AssignedTick,
                    entry.BoundaryOrdinal,
                    entry.Late));
                return;
            }

            _recordedTextBytes = checked(_recordedTextBytes + textBytes);
        }

        _entries.Add(entry);
    }

    internal void RecordText(TextInputBatch batch, bool isSensitive, long boundaryOrdinal)
    {
        ArgumentNullException.ThrowIfNull(batch);

        if (_isDisposed)
        {
            return;
        }

        if (isSensitive)
        {
            _hasSensitiveTextRedaction = true;
            Record(new InputRecordingEntry(
                InputRecordingEntryKind.SensitiveTextRedacted,
                batch.HostTimestamp,
                batch.HostSequence,
                assignedTick: null,
                boundaryOrdinal,
                textBatch: null));
            _isComplete = false;
            RecordMissingSequence(batch.HostSequence);
        }
        else if (_recordTextBatches)
        {
            Record(new InputRecordingEntry(
                InputRecordingEntryKind.TextBatch,
                batch.HostTimestamp,
                batch.HostSequence,
                assignedTick: null,
                boundaryOrdinal,
                textBatch: batch));
        }
        else
        {
            _hasTextOmission = true;
            _isComplete = false;
            RecordMissingSequence(batch.HostSequence);
            Record(new InputRecordingEntry(
                InputRecordingEntryKind.TextOmitted,
                batch.HostTimestamp,
                batch.HostSequence,
                assignedTick: null,
                boundaryOrdinal));
        }
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException("An input recorder is restricted to its host owner thread.");
        }
    }

    private void RecordMissingSequence(long hostSequence)
    {
        if (hostSequence > 0)
        {
            _firstMissingHostSequence ??= hostSequence;
            _lastMissingHostSequence = hostSequence;
        }
    }
}
