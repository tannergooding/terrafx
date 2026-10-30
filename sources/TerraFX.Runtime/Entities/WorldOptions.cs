// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Numerics;

namespace TerraFX.Entities;

/// <summary>Defines validated construction inputs and bounded capacities for a standalone World.</summary>
public sealed class WorldOptions
{
    /// <summary>Initializes World options for one immutable registration.</summary>
    /// <param name="registration">The frozen component, tag, and provider registration.</param>
    /// <exception cref="ArgumentNullException"><paramref name="registration"/> is null.</exception>
    public WorldOptions(WorldRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        Registration = registration;
    }

    /// <summary>Gets the optional exact admission domain; null selects an engine-owned default domain.</summary>
    public WorldAdmissionDomain? AdmissionDomain { get; init; }

    /// <summary>Gets the optional standalone CPU worker count; null selects the documented default.</summary>
    public int? CpuWorkerCount { get; init; }

    /// <summary>Gets whether the World admits a 2D frame.</summary>
    public bool Enable2DFrame { get; init; } = true;

    /// <summary>Gets the optional 2D frame extents in the selected spatial units.</summary>
    public (Vector2 Minimum, Vector2 Maximum)? FrameExtent2 { get; init; }

    /// <summary>Gets the optional 3D frame extents in the selected spatial units.</summary>
    public (Vector3 Minimum, Vector3 Maximum)? FrameExtent3 { get; init; }

    /// <summary>Gets the optional initial replay-lineage identity for a fresh history.</summary>
    public Guid? InitialReplayLineageId { get; init; }

    /// <summary>Gets the initial fixed-tick time scale.</summary>
    public float InitialTimeScale { get; init; } = 1;

    /// <summary>Gets the maximum active scene-instance count.</summary>
    public int MaximumActiveSceneInstances { get; init; } = 65_536;

    /// <summary>Gets the maximum archetype count.</summary>
    public int MaximumArchetypes { get; init; } = 4096;

    /// <summary>Gets the maximum registered component and tag schema count.</summary>
    public int MaximumComponentTypes { get; init; } = 256;

    /// <summary>Gets the maximum deferred commands per barrier.</summary>
    public int MaximumDeferredCommands { get; init; } = 65_536;

    /// <summary>Gets the maximum typed entity-reference positions.</summary>
    public int MaximumEntityReferencePositions { get; init; } = 4_194_304;

    /// <summary>Gets the maximum entity-slot count.</summary>
    public int MaximumEntitySlots { get; init; } = 2_097_152;

    /// <summary>Gets the maximum stable identity assignment count.</summary>
    public int MaximumIdentityAssignments { get; init; } = 33_554_432;

    /// <summary>Gets the maximum identity branch count.</summary>
    public int MaximumIdentityBranches { get; init; } = 1024;

    /// <summary>Gets the maximum live entity count.</summary>
    public int MaximumLiveEntities { get; init; } = 1_048_576;

    /// <summary>Gets the maximum pending owner-request count.</summary>
    public int MaximumPendingOwnerRequests { get; init; } = 128;

    /// <summary>Gets the maximum recorded-control count.</summary>
    public int MaximumRecordedControls { get; init; } = 65_536;

    /// <summary>Gets the maximum scene-occurrence count.</summary>
    public int MaximumSceneOccurrences { get; init; } = 1_048_576;

    /// <summary>Gets the selected simulation admission profile.</summary>
    public SimulationProfile Profile { get; init; } = SimulationProfile.Standard;

    /// <summary>Gets the immutable World registration.</summary>
    public WorldRegistration Registration { get; }

    /// <summary>Gets the selected scheduler and diagnostics memory budgets.</summary>
    public WorldSchedulingLimits SchedulingLimits { get; init; } = WorldSchedulingLimits.Default;

    /// <summary>Gets the registered spatial profile identity.</summary>
    public SpatialProfileId SpatialProfileId { get; init; } = SpatialProfileId.Default;

    /// <summary>Gets the exact spatial unit and chunk settings.</summary>
    public SpatialSettings SpatialSettings { get; init; } = SpatialSettings.Default;

    /// <summary>Gets the fixed simulation tick rate.</summary>
    public int TickRate { get; init; } = 60;

    /// <summary>Gets the optional persistent World identity; null selects one UUIDv4 per construction.</summary>
    public Guid? WorldGuid { get; init; }

    private static (Vector2 Minimum, Vector2 Maximum) DefaultFrameExtent2(int targetExponent)
    {
        var minimum = SpatialMath.ConvertQuantity(-4096L, 1L, 0, targetExponent, 1);
        var maximum = SpatialMath.ConvertQuantity(4096L, 1L, 0, targetExponent, 1);
        return (new Vector2(minimum, minimum), new Vector2(maximum, maximum));
    }

    private static (Vector3 Minimum, Vector3 Maximum) DefaultFrameExtent3(int targetExponent)
    {
        var minimum = SpatialMath.ConvertQuantity(-4096L, 1L, 0, targetExponent, 1);
        var maximum = SpatialMath.ConvertQuantity(4096L, 1L, 0, targetExponent, 1);
        return (new Vector3(minimum, minimum, minimum), new Vector3(maximum, maximum, maximum));
    }

    private static void ValidateExtent(Vector2 minimum, Vector2 maximum, string parameterName)
    {
        if (!float.IsFinite(minimum.X)
            || !float.IsFinite(minimum.Y)
            || !float.IsFinite(maximum.X)
            || !float.IsFinite(maximum.Y)
            || (minimum.X > maximum.X)
            || (minimum.Y > maximum.Y))
        {
            throw new ArgumentException("Frame endpoints must be finite and ordered on every axis.", parameterName);
        }
    }

    private static void ValidateExtent(Vector3 minimum, Vector3 maximum, string parameterName)
    {
        if (!float.IsFinite(minimum.X)
            || !float.IsFinite(minimum.Y)
            || !float.IsFinite(minimum.Z)
            || !float.IsFinite(maximum.X)
            || !float.IsFinite(maximum.Y)
            || !float.IsFinite(maximum.Z)
            || (minimum.X > maximum.X)
            || (minimum.Y > maximum.Y)
            || (minimum.Z > maximum.Z))
        {
            throw new ArgumentException("Frame endpoints must be finite and ordered on every axis.", parameterName);
        }
    }

    private static void ValidateLimit(int value, int maximum, string parameterName)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value, parameterName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, maximum, parameterName);
    }

    internal ResolvedWorldOptions ValidateAndResolve()
    {
        ArgumentNullException.ThrowIfNull(Registration);

        if (WorldGuid == Guid.Empty)
        {
            throw new ArgumentException("A WorldGuid cannot be empty.", nameof(WorldGuid));
        }

        if (InitialReplayLineageId == Guid.Empty)
        {
            throw new ArgumentException("An initial replay lineage cannot be empty.", nameof(InitialReplayLineageId));
        }

        if (CpuWorkerCount is { } workerCount)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(workerCount);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(workerCount, 64);
        }

        if (TickRate is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(TickRate));
        }

        ValidateLimit(MaximumLiveEntities, 1_048_576, nameof(MaximumLiveEntities));
        ValidateLimit(MaximumEntitySlots, 2_097_152, nameof(MaximumEntitySlots));
        ValidateLimit(MaximumIdentityAssignments, 33_554_432, nameof(MaximumIdentityAssignments));
        ValidateLimit(MaximumIdentityBranches, 1024, nameof(MaximumIdentityBranches));
        ValidateLimit(MaximumEntityReferencePositions, 4_194_304, nameof(MaximumEntityReferencePositions));
        ValidateLimit(MaximumActiveSceneInstances, 65_536, nameof(MaximumActiveSceneInstances));
        ValidateLimit(MaximumSceneOccurrences, 1_048_576, nameof(MaximumSceneOccurrences));
        ValidateLimit(MaximumRecordedControls, 65_536, nameof(MaximumRecordedControls));
        ValidateLimit(MaximumArchetypes, 4096, nameof(MaximumArchetypes));
        ValidateLimit(MaximumDeferredCommands, 65_536, nameof(MaximumDeferredCommands));
        ValidateLimit(MaximumComponentTypes, 256, nameof(MaximumComponentTypes));
        ValidateLimit(MaximumPendingOwnerRequests, 128, nameof(MaximumPendingOwnerRequests));

        if (MaximumComponentTypes < Registration.RegisteredTypeCount)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumComponentTypes), "The registration contains more component and tag identities than this World admits.");
        }

        if (Profile is not SimulationProfile.Standard and not SimulationProfile.StrictLocal)
        {
            throw new ArgumentOutOfRangeException(nameof(Profile));
        }

        if (!float.IsFinite(InitialTimeScale) || (InitialTimeScale < 0) || (InitialTimeScale > 4))
        {
            throw new ArgumentOutOfRangeException(nameof(InitialTimeScale));
        }

        ArgumentNullException.ThrowIfNull(SchedulingLimits);
        Registration.Schedule.ValidateLimits(SchedulingLimits);

        if (!SpatialProfiles.TryGetSettings(SpatialProfileId, out var registeredSettings)
            || (SpatialSettings != registeredSettings))
        {
            throw new ArgumentException("The selected spatial profile and settings do not match an exact registered declaration.", nameof(SpatialProfileId));
        }

        var targetExponent = SpatialSettings.MetersPerUnitExponent;
        var frameExtent3 = FrameExtent3 ?? DefaultFrameExtent3(targetExponent);
        ValidateExtent(frameExtent3.Minimum, frameExtent3.Maximum, nameof(FrameExtent3));

        (Vector2 Minimum, Vector2 Maximum)? frameExtent2 = null;

        if (Enable2DFrame)
        {
            frameExtent2 = FrameExtent2 ?? DefaultFrameExtent2(targetExponent);
            ValidateExtent(frameExtent2.Value.Minimum, frameExtent2.Value.Maximum, nameof(FrameExtent2));
        }
        else if (FrameExtent2 is not null)
        {
            throw new ArgumentException("FrameExtent2 must be null when 2D frame admission is disabled.", nameof(FrameExtent2));
        }

        var cpuWorkerCount = CpuWorkerCount
            ?? Math.Min(64, Math.Max(1, Environment.ProcessorCount - 2));
        return new ResolvedWorldOptions(this, frameExtent2, frameExtent3, cpuWorkerCount);
    }
}
