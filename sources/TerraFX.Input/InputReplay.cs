// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;

namespace TerraFX.Input;

/// <summary>Replays one recipient's immutable in-memory recording without native input access.</summary>
public static class InputReplay
{
    /// <summary>Plays one versioned recipient recording in an isolated router.</summary>
    /// <param name="recording">The immutable recipient recording.</param>
    /// <returns>Action snapshots, terminal transitions, and nonsensitive text batches produced by playback.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="recording"/> is null.</exception>
    /// <exception cref="InvalidDataException">The recording version, baseline, or admitted record sequence is invalid.</exception>
    public static InputReplayResult Play(InputRecording recording)
    {
        ArgumentNullException.ThrowIfNull(recording);

        if (recording.Version != InputRecording.CurrentVersion)
        {
            throw new InvalidDataException($"Input recording version {recording.Version} is not supported.");
        }

        return InputRouter.PlayRecording(recording);
    }
}
