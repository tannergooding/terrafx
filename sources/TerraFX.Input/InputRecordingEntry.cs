// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Input;

/// <summary>Contains one immutable recipient-local input, control, or boundary record.</summary>
public sealed class InputRecordingEntry
{
    internal InputRecordingEntry(
        InputRecordingEntryKind kind,
        long hostTimestamp,
        long hostSequence,
        long? assignedTick,
        long boundaryOrdinal,
        bool late = false,
        PhysicalInputEvent? physicalEvent = null,
        InputInjection? injection = null,
        InputClass uiCapturedClasses = InputClass.None,
        InputActionMapV1? actionMap = null,
        Guid? mapId = null,
        long expectedRevision = 0,
        ulong newMask = 0,
        long lastInsertionSequence = 0,
        InputRecordingControlKind? controlKind = null,
        InputClass inputClasses = InputClass.None,
        ushort? playerSlot = null,
        InputCancellationReason? cancellationReason = null,
        ImmutableArray<InputInjection> reconciliationInputs = default,
        ImmutableArray<PhysicalInputValue> physicalReconciliationInputs = default,
        InputDeviceHandle? physicalSourceDevice = null,
        InputDeviceHandle? device = null,
        bool isAssigned = false,
        TextInputBatch? textBatch = null,
        SimulationClockSegment? clockSegment = null,
        ImmutableArray<long> contextInsertionSequences = default)
    {
        Kind = kind;
        HostTimestamp = hostTimestamp;
        HostSequence = hostSequence;
        AssignedTick = assignedTick;
        BoundaryOrdinal = boundaryOrdinal;
        Late = late;
        PhysicalEvent = physicalEvent;
        Injection = injection;
        UiCapturedClasses = uiCapturedClasses;
        ActionMap = actionMap;
        MapId = mapId;
        ExpectedRevision = expectedRevision;
        NewMask = newMask;
        LastInsertionSequence = lastInsertionSequence;
        ControlKind = controlKind;
        InputClasses = inputClasses;
        PlayerSlot = playerSlot;
        CancellationReason = cancellationReason;
        ReconciliationInputs = reconciliationInputs;
        PhysicalReconciliationInputs = physicalReconciliationInputs;
        PhysicalSourceDevice = physicalSourceDevice;
        Device = device;
        IsAssigned = isAssigned;
        TextBatch = textBatch;
        ClockSegment = clockSegment;
        ContextInsertionSequences = contextInsertionSequences;
    }

    /// <summary>Gets the copied action map for a map-registration or replacement record.</summary>
    public InputActionMapV1? ActionMap { get; }

    /// <summary>Gets the recipient-assigned simulation tick, or null for host-clock data.</summary>
    public long? AssignedTick { get; }

    /// <summary>Gets the one-based recipient control-boundary ordinal associated with the record.</summary>
    public long BoundaryOrdinal { get; }

    /// <summary>Gets the optional cancellation reason for a control record.</summary>
    public InputCancellationReason? CancellationReason { get; }

    /// <summary>Gets the simulation clock segment change, when this is a clock record.</summary>
    public SimulationClockSegment? ClockSegment { get; }

    /// <summary>Gets the copied context insertion sequences for a map/context control.</summary>
    public ImmutableArray<long> ContextInsertionSequences { get; }

    /// <summary>Gets the optional control-command kind.</summary>
    public InputRecordingControlKind? ControlKind { get; }

    /// <summary>Gets the physical device generation for an assignment record.</summary>
    public InputDeviceHandle? Device { get; }

    /// <summary>Gets the optional replacement map revision expected by a map/control operation.</summary>
    public long ExpectedRevision { get; }

    /// <summary>Gets the original process-wide host event sequence.</summary>
    public long HostSequence { get; }

    /// <summary>Gets the nonnegative host-observation timestamp in TimeSpan ticks.</summary>
    public long HostTimestamp { get; }

    /// <summary>Gets the copied explicit Headless injection, when this record is synthetic.</summary>
    public InputInjection? Injection { get; }

    /// <summary>Gets the optional input classes affected by a cancellation or reconciliation.</summary>
    public InputClass InputClasses { get; }

    /// <summary>Gets whether a device assignment record assigns, rather than removes, the device.</summary>
    public bool IsAssigned { get; }

    /// <summary>Gets the immutable recipient recording record kind.</summary>
    public InputRecordingEntryKind Kind { get; }

    /// <summary>Gets the last context insertion sequence after the recorded operation.</summary>
    public long LastInsertionSequence { get; }

    /// <summary>Gets whether this copy was late-clamped to the recipient's next open tick.</summary>
    public bool Late { get; }

    /// <summary>Gets the optional map identifier for a context-mask control.</summary>
    public Guid? MapId { get; }

    /// <summary>Gets the new active context mask for a context-mask control.</summary>
    public ulong NewMask { get; }

    /// <summary>Gets the copied physical input event, when this record is native/normalized input.</summary>
    public PhysicalInputEvent? PhysicalEvent { get; }

    /// <summary>Gets the copied physical-state reconciliation values.</summary>
    public ImmutableArray<PhysicalInputValue> PhysicalReconciliationInputs { get; }

    /// <summary>Gets the optional device identity for a physical device removal.</summary>
    public InputDeviceHandle? PhysicalSourceDevice { get; }

    /// <summary>Gets the assigned gamepad slot or recipient-local player slot, when applicable.</summary>
    public ushort? PlayerSlot { get; }

    /// <summary>Gets the copied Headless reconciliation values.</summary>
    public ImmutableArray<InputInjection> ReconciliationInputs { get; }

    /// <summary>Gets the optional captured text-session batch.</summary>
    public TextInputBatch? TextBatch { get; }

    /// <summary>Gets the event-time UI-captured input classes for this recipient copy.</summary>
    public InputClass UiCapturedClasses { get; }
}
