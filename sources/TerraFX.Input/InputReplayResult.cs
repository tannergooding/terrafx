// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Collections.Immutable;

namespace TerraFX.Input;

/// <summary>Contains one completed playback of a recipient-local input recording.</summary>
public sealed class InputReplayResult
{
    internal InputReplayResult(
        InputRecipientKind recipientKind,
        bool isComplete,
        ImmutableArray<InputActionSnapshot> snapshots,
        ImmutableArray<InputActionTransition> terminalTransitions,
        ImmutableArray<TextInputBatch> textBatches)
    {
        RecipientKind = recipientKind;
        IsComplete = isComplete;
        Snapshots = snapshots;
        TerminalTransitions = terminalTransitions;
        TextBatches = textBatches;
    }

    /// <summary>Gets whether the source recording contains no overflow, omitted text, or redacted content.</summary>
    public bool IsComplete { get; }

    /// <summary>Gets the recipient kind replayed without consulting a live platform source.</summary>
    public InputRecipientKind RecipientKind { get; }

    /// <summary>Gets the host-boundary and simulation-tick snapshots produced by playback.</summary>
    public ImmutableArray<InputActionSnapshot> Snapshots { get; }

    /// <summary>Gets terminal action transitions produced by an explicitly recorded recipient disposal.</summary>
    public ImmutableArray<InputActionTransition> TerminalTransitions { get; }

    /// <summary>Gets nonsensitive text-session batches in recording order.</summary>
    public ImmutableArray<TextInputBatch> TextBatches { get; }
}
