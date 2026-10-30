// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX;

/// <summary>Stores one immutable structured diagnostic record.</summary>
public sealed class Diagnostic
{
    /// <summary>Initializes an immutable diagnostic record.</summary>
    /// <param name="code">The stable diagnostic code.</param>
    /// <param name="severity">The diagnostic severity.</param>
    /// <param name="monotonicTimestamp">The nonnegative monotonic timestamp.</param>
    /// <param name="hostId">The optional host identity.</param>
    /// <param name="worldGuid">The optional World identity.</param>
    /// <param name="deviceId">The optional device identity.</param>
    /// <param name="surfaceId">The optional surface identity.</param>
    /// <param name="deviceEpoch">The optional device epoch.</param>
    /// <param name="surfaceEpoch">The optional surface epoch.</param>
    /// <param name="swapchainEpoch">The optional swapchain epoch.</param>
    /// <param name="tick">The optional nonnegative simulation tick.</param>
    /// <param name="systemId">The optional system identity.</param>
    /// <param name="passId">The optional pass identity.</param>
    /// <param name="assetId">The optional positive content-set-scoped AssetId.</param>
    /// <param name="commandIdentity">The optional command identity.</param>
    /// <param name="operation">The nonempty operation name.</param>
    /// <param name="nativeResult">The optional native result code.</param>
    /// <param name="message">The human-readable diagnostic message.</param>
    /// <param name="context">The initialized immutable context entries.</param>
    /// <exception cref="ArgumentException">A required name is empty or the context array is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A severity, timestamp, identity, epoch, tick, or AssetId is invalid.</exception>
    public Diagnostic(
        string code,
        DiagnosticSeverity severity,
        long monotonicTimestamp,
        uint? hostId,
        Guid? worldGuid,
        uint? deviceId,
        uint? surfaceId,
        uint? deviceEpoch,
        uint? surfaceEpoch,
        uint? swapchainEpoch,
        long? tick,
        Guid? systemId,
        Guid? passId,
        int? assetId,
        string? commandIdentity,
        string operation,
        int? nativeResult,
        string message,
        ImmutableArray<DiagnosticContextEntry> context)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(message);

        if (code.Length == 0)
        {
            throw new ArgumentException("A diagnostic code cannot be empty.", nameof(code));
        }

        if (operation.Length == 0)
        {
            throw new ArgumentException("A diagnostic operation cannot be empty.", nameof(operation));
        }

        if (severity is < DiagnosticSeverity.Trace or > DiagnosticSeverity.Critical)
        {
            throw new ArgumentOutOfRangeException(nameof(severity));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(monotonicTimestamp);
        ValidateNonzero(hostId, nameof(hostId));
        ValidateGuid(worldGuid, nameof(worldGuid));
        ValidateNonzero(deviceId, nameof(deviceId));
        ValidateNonzero(surfaceId, nameof(surfaceId));
        ValidateNonzero(deviceEpoch, nameof(deviceEpoch));
        ValidateNonzero(surfaceEpoch, nameof(surfaceEpoch));
        ValidateNonzero(swapchainEpoch, nameof(swapchainEpoch));

        if (tick is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tick));
        }

        ValidateGuid(systemId, nameof(systemId));
        ValidateGuid(passId, nameof(passId));

        if (assetId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(assetId));
        }

        if (context.IsDefault)
        {
            throw new ArgumentException("Diagnostic context must be initialized.", nameof(context));
        }

        foreach (var entry in context)
        {
            if (string.IsNullOrEmpty(entry.Key) || (entry.Value is null))
            {
                throw new ArgumentException("Diagnostic context entries must be valid.", nameof(context));
            }
        }

        Code = code;
        Severity = severity;
        MonotonicTimestamp = monotonicTimestamp;
        HostId = hostId;
        WorldGuid = worldGuid;
        DeviceId = deviceId;
        SurfaceId = surfaceId;
        DeviceEpoch = deviceEpoch;
        SurfaceEpoch = surfaceEpoch;
        SwapchainEpoch = swapchainEpoch;
        Tick = tick;
        SystemId = systemId;
        PassId = passId;
        AssetId = assetId;
        CommandIdentity = commandIdentity;
        Operation = operation;
        NativeResult = nativeResult;
        Message = message;
        Context = [.. context];
    }

    /// <summary>Gets the optional content-set-scoped AssetId.</summary>
    public int? AssetId { get; }

    /// <summary>Gets the stable diagnostic code.</summary>
    public string Code { get; }

    /// <summary>Gets the optional command identity.</summary>
    public string? CommandIdentity { get; }

    /// <summary>Gets the immutable diagnostic context.</summary>
    public ImmutableArray<DiagnosticContextEntry> Context { get; }

    /// <summary>Gets the optional device epoch.</summary>
    public uint? DeviceEpoch { get; }

    /// <summary>Gets the optional device identity.</summary>
    public uint? DeviceId { get; }

    /// <summary>Gets the optional host identity.</summary>
    public uint? HostId { get; }

    /// <summary>Gets the diagnostic message.</summary>
    public string Message { get; }

    /// <summary>Gets the monotonic timestamp.</summary>
    public long MonotonicTimestamp { get; }

    /// <summary>Gets the optional native result code.</summary>
    public int? NativeResult { get; }

    /// <summary>Gets the operation name.</summary>
    public string Operation { get; }

    /// <summary>Gets the optional pass identity.</summary>
    public Guid? PassId { get; }

    /// <summary>Gets the diagnostic severity.</summary>
    public DiagnosticSeverity Severity { get; }

    /// <summary>Gets the optional surface epoch.</summary>
    public uint? SurfaceEpoch { get; }

    /// <summary>Gets the optional surface identity.</summary>
    public uint? SurfaceId { get; }

    /// <summary>Gets the optional swapchain epoch.</summary>
    public uint? SwapchainEpoch { get; }

    /// <summary>Gets the optional system identity.</summary>
    public Guid? SystemId { get; }

    /// <summary>Gets the optional simulation tick.</summary>
    public long? Tick { get; }

    /// <summary>Gets the optional World identity.</summary>
    public Guid? WorldGuid { get; }

    private static void ValidateGuid(Guid? value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void ValidateNonzero(uint? value, string parameterName)
    {
        if (value == 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
