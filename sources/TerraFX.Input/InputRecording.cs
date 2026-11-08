// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Collections.Immutable;

namespace TerraFX.Input;

/// <summary>Contains one immutable, in-memory versioned recording for an input recipient.</summary>
public sealed class InputRecording
{
    internal const int CurrentVersion = 1;

    internal InputRecording(
        InputRecipientKind recipientKind,
        InputRecordingBaseline baseline,
        ImmutableArray<InputRecordingEntry> entries,
        bool isComplete,
        bool hasTextOmission,
        bool hasSensitiveTextRedaction,
        long? firstMissingHostSequence,
        long? lastMissingHostSequence,
        int version = CurrentVersion)
    {
        Version = version;
        RecipientKind = recipientKind;
        Baseline = baseline;
        Entries = entries;
        IsComplete = isComplete;
        HasTextOmission = hasTextOmission;
        HasSensitiveTextRedaction = hasSensitiveTextRedaction;
        FirstMissingHostSequence = firstMissingHostSequence;
        LastMissingHostSequence = lastMissingHostSequence;
    }

    /// <summary>Gets the immutable recipient-local records in captured order.</summary>
    public ImmutableArray<InputRecordingEntry> Entries { get; }

    /// <summary>Gets the first host sequence omitted after the recording exceeded its bound.</summary>
    public long? FirstMissingHostSequence { get; }

    /// <summary>Gets whether sensitive text was redacted from this recording.</summary>
    public bool HasSensitiveTextRedaction { get; }

    /// <summary>Gets whether committed text was omitted because text recording was disabled.</summary>
    public bool HasTextOmission { get; }

    /// <summary>Gets whether this recording contains every accepted record and all requested text payloads.</summary>
    public bool IsComplete { get; }

    /// <summary>Gets the last host sequence omitted after the recording exceeded its bound.</summary>
    public long? LastMissingHostSequence { get; }

    /// <summary>Gets the recipient identity.</summary>
    public uint RecipientId => Baseline.RecipientId;

    /// <summary>Gets the recorded recipient kind.</summary>
    public InputRecipientKind RecipientKind { get; }

    /// <summary>Gets the number of captured input and control records.</summary>
    public int RecordCount => Entries.Length;

    /// <summary>Gets the in-memory recording representation version.</summary>
    public int Version { get; }

    /// <summary>Gets the exact window identity for a HostLiveUI recording.</summary>
    public WindowHandle? Window => Baseline.Window;

    internal InputRecordingBaseline Baseline { get; }
}
