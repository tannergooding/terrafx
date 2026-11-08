// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Collections.Immutable;
using TerraFX;

namespace TerraFX.Input;

internal sealed record InputRecordingBaseline(
    uint RecipientId,
    WindowHandle? Window,
    ImmutableArray<InputRecordingMapSnapshot> InitialMaps,
    long LastInsertionSequence,
    long NextOpenTick,
    long LastAssignedInputTick,
    SimulationClockSegment ClockSegment,
    long HostControlTimestamp,
    long LastSeenHostSequence,
    long LastSeenHostTimestamp,
    int ControlCommandsInBoundary,
    long HostControlBoundaryOrdinal);
