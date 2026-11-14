// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading.Tasks;
using TerraFX.Entities;

namespace TerraFX.Hosting;

/// <summary>Contains the outcome of one World checkpoint commit.</summary>
public sealed class WorldLoadReceipt
{
    internal WorldLoadReceipt(
        WorldLoadStatus status,
        World? replacementWorld,
        Task? oldWorldRetirement,
        WorldIdentityRemap? identityRemap,
        Guid? sourceWorldGuid,
        Guid? targetRecordWorldGuid,
        long? loadedTick,
        Diagnostic? rejection)
    {
        if (!Enum.IsDefined(status)
            || (status is WorldLoadStatus.Loaded) != (replacementWorld is not null)
            || (status is WorldLoadStatus.Loaded) != (oldWorldRetirement is not null)
            || (status is WorldLoadStatus.Loaded) != (identityRemap is not null)
            || (status is WorldLoadStatus.Loaded) != sourceWorldGuid.HasValue
            || (status is WorldLoadStatus.Loaded) != targetRecordWorldGuid.HasValue
            || (status is WorldLoadStatus.Loaded) != loadedTick.HasValue
            || (status is WorldLoadStatus.Loaded) == (rejection is not null))
        {
            throw new ArgumentException("The World load receipt status and results are inconsistent.");
        }

        Status = status;
        ReplacementWorld = replacementWorld;
        OldWorldRetirement = oldWorldRetirement;
        IdentityRemap = identityRemap;
        SourceWorldGuid = sourceWorldGuid;
        TargetRecordWorldGuid = targetRecordWorldGuid;
        LoadedTick = loadedTick;
        Rejection = rejection;
    }

    /// <summary>Gets the independently owned source-to-target identity map when loaded.</summary>
    public WorldIdentityRemap? IdentityRemap { get; }

    /// <summary>Gets the restored next-tick value when loaded.</summary>
    public long? LoadedTick { get; }

    /// <summary>Gets the task that reports retirement of the replaced World.</summary>
    public Task? OldWorldRetirement { get; }

    /// <summary>Gets the rejection diagnostic when no replacement was published.</summary>
    public Diagnostic? Rejection { get; }

    /// <summary>Gets the published replacement World.</summary>
    public World? ReplacementWorld { get; }

    /// <summary>Gets the source checkpoint's World identity when loaded.</summary>
    public Guid? SourceWorldGuid { get; }

    /// <summary>Gets the terminal load status.</summary>
    public WorldLoadStatus Status { get; }

    /// <summary>Gets the semantic World identity used by the replacement records.</summary>
    public Guid? TargetRecordWorldGuid { get; }

    /// <summary>Gets the semantic replacement World identity, or null when rejected.</summary>
    public Guid? WorldGuid => TargetRecordWorldGuid;
}
