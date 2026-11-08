// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Input;

/// <summary>Identifies one kind of input recording record.</summary>
public enum InputRecordingEntryKind
{
    /// <summary>No recording entry kind.</summary>
    None = 0,

    /// <summary>A recipient-local native/normalized physical input copy.</summary>
    PhysicalInput = 1,

    /// <summary>An explicit headless/synthetic injection.</summary>
    HeadlessInjection = 2,

    /// <summary>An admitted recipient control operation.</summary>
    Control = 3,

    /// <summary>An action-map registration.</summary>
    MapRegistered = 4,

    /// <summary>An action-map revision replacement.</summary>
    MapReplaced = 5,

    /// <summary>An opted-in nonsensitive text-session batch.</summary>
    TextBatch = 6,

    /// <summary>A sensitive text payload that was deliberately redacted.</summary>
    SensitiveTextRedacted = 7,

    /// <summary>A host-control pump boundary.</summary>
    HostControlBoundary = 8,

    /// <summary>A simulation clock-segment change.</summary>
    ClockSegment = 9,

    /// <summary>An input/control sequence gap or discontinuity.</summary>
    Discontinuity = 10,

    /// <summary>A published simulation action snapshot boundary.</summary>
    SimulationTick = 11,

    /// <summary>Terminal disposal of this recipient.</summary>
    RecipientDisposed = 12,

    /// <summary>A text batch omitted because payload recording was disabled or over budget.</summary>
    TextOmitted = 13,

    /// <summary>A gamepad generation was assigned to or removed from a recipient player slot.</summary>
    DeviceAssignment = 14,

    /// <summary>A recipient intentionally discarded a captured event while paused.</summary>
    PausedInputDiscard = 15,

    /// <summary>A simulation input copy had no eligible tick in its recorded clock segment.</summary>
    NoEligibleTickDiscard = 16,
}
