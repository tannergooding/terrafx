// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Entities;

namespace TerraFX.Hosting;

/// <summary>Defines the explicit intent and bounded source limit for one World restore.</summary>
public sealed class LoadOptions
{
    internal const long MaximumSnapshotBytesLimit = 2_147_483_584;
    internal const long MinimumSnapshotBytesLimit = 64;

    private Guid? _forkWorldGuid;
    private Guid? _identityBranchId;
    private long _maximumSnapshotBytes = MaximumSnapshotBytesLimit;
    private Guid? _newReplayLineageId;

    /// <summary>Initializes one restore request with an explicit identity and replay intent.</summary>
    /// <param name="restoreIntent">The required restore intent.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="restoreIntent"/> is None or undefined.</exception>
    public LoadOptions(WorldRestoreIntent restoreIntent)
    {
        if ((restoreIntent is WorldRestoreIntent.None) || !Enum.IsDefined(restoreIntent))
        {
            throw new ArgumentOutOfRangeException(nameof(restoreIntent));
        }

        RestoreIntent = restoreIntent;
    }

    /// <summary>Gets or initializes the fresh World identity required by IndependentFork.</summary>
    /// <exception cref="ArgumentException">The supplied identity is empty.</exception>
    public Guid? ForkWorldGuid
    {
        get
        {
            return _forkWorldGuid;
        }
        init
        {
            if (value == Guid.Empty)
            {
                throw new ArgumentException("The fork WorldGuid must be nonempty.", nameof(ForkWorldGuid));
            }

            _forkWorldGuid = value;
        }
    }

    /// <summary>Gets or initializes the new branch identity required by Branch.</summary>
    /// <exception cref="ArgumentException">The supplied identity is empty.</exception>
    public Guid? IdentityBranchId
    {
        get
        {
            return _identityBranchId;
        }
        init
        {
            if (value == Guid.Empty)
            {
                throw new ArgumentException("The selected identity branch must be nonempty.", nameof(IdentityBranchId));
            }

            _identityBranchId = value;
        }
    }

    /// <summary>Gets or initializes the maximum source snapshot size admitted by this request.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The limit is outside the supported range.</exception>
    public long MaximumSnapshotBytes
    {
        get
        {
            return _maximumSnapshotBytes;
        }
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, MinimumSnapshotBytesLimit);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaximumSnapshotBytesLimit);
            _maximumSnapshotBytes = value;
        }
    }

    /// <summary>Gets or initializes the requested new replay lineage, when the admitted transformation requires one.</summary>
    /// <exception cref="ArgumentException">The supplied lineage identity is empty.</exception>
    public Guid? NewReplayLineageId
    {
        get
        {
            return _newReplayLineageId;
        }
        init
        {
            if (value == Guid.Empty)
            {
                throw new ArgumentException("The requested replay lineage must be nonempty.", nameof(NewReplayLineageId));
            }

            _newReplayLineageId = value;
        }
    }

    /// <summary>Gets or initializes the prepared continuation required by RecordedContinuation.</summary>
    public WorldRecordedContinuation? RecordedContinuation { get; init; }

    /// <summary>Gets the explicit restore intent.</summary>
    public WorldRestoreIntent RestoreIntent { get; }

    internal void ValidateForPrepareLoad(long targetMaximumSnapshotBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(targetMaximumSnapshotBytes, MinimumSnapshotBytesLimit);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(targetMaximumSnapshotBytes, MaximumSnapshotBytesLimit);

        if (MaximumSnapshotBytes > targetMaximumSnapshotBytes)
        {
            throw new ArgumentException("The request snapshot limit cannot exceed the target World's snapshot limit.");
        }

        switch (RestoreIntent)
        {
            case WorldRestoreIntent.RecordedContinuation:
            {
                if ((RecordedContinuation is null)
                    || IdentityBranchId.HasValue
                    || ForkWorldGuid.HasValue
                    || NewReplayLineageId.HasValue)
                {
                    throw new ArgumentException("RecordedContinuation requires only its prepared continuation.");
                }

                RecordedContinuation.ThrowIfDisposed();
                break;
            }

            case WorldRestoreIntent.Branch:
            {
                if (!IdentityBranchId.HasValue || ForkWorldGuid.HasValue || RecordedContinuation is not null)
                {
                    throw new ArgumentException("Branch requires an identity branch and forbids continuation and fork identities.");
                }

                break;
            }

            case WorldRestoreIntent.IndependentFork:
            {
                if (!ForkWorldGuid.HasValue
                    || !NewReplayLineageId.HasValue
                    || IdentityBranchId.HasValue
                    || RecordedContinuation is not null)
                {
                    throw new ArgumentException("IndependentFork requires a fork WorldGuid and new lineage only.");
                }

                break;
            }

            default:
            {
                throw new InvalidOperationException("The restore intent is not a supported value.");
            }
        }
    }
}
