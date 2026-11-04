// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX.Entities;
using TerraFX.Hosting;

namespace TerraFX.Runtime.Tests.Hosting;

[TestFixture]
internal sealed class LoadOptionsTests
{
    [Test]
    public void BranchRequiresBranchIdentityAndAllowsSourceDependentLineageChoice()
    {
        var missingIdentity = new LoadOptions(WorldRestoreIntent.Branch);
        _ = Assert.Throws<ArgumentException>(() => missingIdentity.ValidateForPrepareLoad(LoadOptions.MaximumSnapshotBytesLimit));

        var compatibleBranch = new LoadOptions(WorldRestoreIntent.Branch) {
            IdentityBranchId = Guid.NewGuid(),
        };
        Assert.DoesNotThrow(() => compatibleBranch.ValidateForPrepareLoad(LoadOptions.MaximumSnapshotBytesLimit));

        var lineageChangingBranch = new LoadOptions(WorldRestoreIntent.Branch) {
            IdentityBranchId = Guid.NewGuid(),
            NewReplayLineageId = Guid.NewGuid(),
        };
        Assert.DoesNotThrow(() => lineageChangingBranch.ValidateForPrepareLoad(LoadOptions.MaximumSnapshotBytesLimit));

        _ = Assert.Throws<ArgumentException>(() => _ = new LoadOptions(WorldRestoreIntent.Branch) {
            IdentityBranchId = Guid.Empty,
        });
    }

    [TestCase(WorldRestoreIntent.None)]
    [TestCase((WorldRestoreIntent)byte.MaxValue)]
    public void ConstructorRejectsMissingOrUndefinedIntent(WorldRestoreIntent intent)
        => _ = Assert.Throws<ArgumentOutOfRangeException>(() => _ = new LoadOptions(intent));

    [Test]
    public void IndependentForkRequiresFreshWorldAndLineageIdentities()
    {
        var missingLineage = new LoadOptions(WorldRestoreIntent.IndependentFork) {
            ForkWorldGuid = Guid.NewGuid(),
        };
        _ = Assert.Throws<ArgumentException>(() => missingLineage.ValidateForPrepareLoad(LoadOptions.MaximumSnapshotBytesLimit));

        var validFork = new LoadOptions(WorldRestoreIntent.IndependentFork) {
            ForkWorldGuid = Guid.NewGuid(),
            NewReplayLineageId = Guid.NewGuid(),
        };
        Assert.DoesNotThrow(() => validFork.ValidateForPrepareLoad(LoadOptions.MaximumSnapshotBytesLimit));

        _ = Assert.Throws<ArgumentException>(() => _ = new LoadOptions(WorldRestoreIntent.IndependentFork) {
            ForkWorldGuid = Guid.Empty,
        });
        _ = Assert.Throws<ArgumentException>(() => _ = new LoadOptions(WorldRestoreIntent.IndependentFork) {
            ForkWorldGuid = Guid.NewGuid(),
            NewReplayLineageId = Guid.Empty,
        });
    }

    [Test]
    public void RecordedContinuationRequiresOnlyLivePreparedContinuation()
    {
        var missingContinuation = new LoadOptions(WorldRestoreIntent.RecordedContinuation);
        _ = Assert.Throws<ArgumentException>(
            () => missingContinuation.ValidateForPrepareLoad(LoadOptions.MaximumSnapshotBytesLimit));

        using var sourceOwner = new CountingOwner();
        var checkpointDigest = new byte[32];
        var payloadDigest = new byte[32];
        var replayDigest = new byte[32];
        var continuation = new WorldRecordedContinuation(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            firstTick: 1,
            endTickExclusive: 4,
            checkpointDigest,
            payloadDigest,
            replayDigest,
            sourceOwner);
        var options = new LoadOptions(WorldRestoreIntent.RecordedContinuation) {
            RecordedContinuation = continuation,
        };
        Assert.DoesNotThrow(() => options.ValidateForPrepareLoad(LoadOptions.MaximumSnapshotBytesLimit));

        checkpointDigest[0] = 1;
        payloadDigest[0] = 1;
        replayDigest[0] = 1;
        Assert.That(continuation.SourceCheckpointDigest[0], Is.Zero);
        Assert.That(continuation.SourcePayloadDigest[0], Is.Zero);
        Assert.That(continuation.ReplayDigest[0], Is.Zero);

        continuation.Dispose();
        Assert.That(continuation.SourceWorldGuid, Is.Not.EqualTo(Guid.Empty));
        _ = Assert.Throws<ObjectDisposedException>(
            () => options.ValidateForPrepareLoad(LoadOptions.MaximumSnapshotBytesLimit));
        continuation.Dispose();
        Assert.That(sourceOwner.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public void RestoreIntentsRejectConflictingOptionFamilies()
    {
        var branchWithFork = new LoadOptions(WorldRestoreIntent.Branch) {
            IdentityBranchId = Guid.NewGuid(),
            ForkWorldGuid = Guid.NewGuid(),
        };
        _ = Assert.Throws<ArgumentException>(
            () => branchWithFork.ValidateForPrepareLoad(LoadOptions.MaximumSnapshotBytesLimit));

        var forkWithBranch = new LoadOptions(WorldRestoreIntent.IndependentFork) {
            ForkWorldGuid = Guid.NewGuid(),
            NewReplayLineageId = Guid.NewGuid(),
            IdentityBranchId = Guid.NewGuid(),
        };
        _ = Assert.Throws<ArgumentException>(
            () => forkWithBranch.ValidateForPrepareLoad(LoadOptions.MaximumSnapshotBytesLimit));
    }

    [Test]
    public void SnapshotLimitUsesClosedSupportedRangeAndCannotExceedTarget()
    {
        var defaults = new LoadOptions(WorldRestoreIntent.Branch) {
            IdentityBranchId = Guid.NewGuid(),
        };
        Assert.That(defaults.MaximumSnapshotBytes, Is.EqualTo(2_147_483_584));
        Assert.DoesNotThrow(() => defaults.ValidateForPrepareLoad(2_147_483_584));

        var minimum = new LoadOptions(WorldRestoreIntent.Branch) {
            IdentityBranchId = Guid.NewGuid(),
            MaximumSnapshotBytes = LoadOptions.MinimumSnapshotBytesLimit,
        };
        Assert.DoesNotThrow(() => minimum.ValidateForPrepareLoad(LoadOptions.MinimumSnapshotBytesLimit));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => minimum.ValidateForPrepareLoad(LoadOptions.MinimumSnapshotBytesLimit - 1));

        _ = Assert.Throws<ArgumentOutOfRangeException>(() => _ = new LoadOptions(WorldRestoreIntent.Branch) {
            IdentityBranchId = Guid.NewGuid(),
            MaximumSnapshotBytes = LoadOptions.MinimumSnapshotBytesLimit - 1,
        });
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => _ = new LoadOptions(WorldRestoreIntent.Branch) {
            IdentityBranchId = Guid.NewGuid(),
            MaximumSnapshotBytes = LoadOptions.MaximumSnapshotBytesLimit + 1,
        });
        _ = Assert.Throws<ArgumentException>(() => defaults.ValidateForPrepareLoad(1_048_576));
    }

    private sealed class CountingOwner : IDisposable
    {
        private bool _isDisposed;

        internal int DisposeCount { get; private set; }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                DisposeCount++;
            }
        }
    }
}
