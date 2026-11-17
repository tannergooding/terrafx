// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using NUnit.Framework;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Scenes;

[TestFixture]
[SuppressMessage(
    "Reliability",
    "CA2000:Dispose objects before losing scope",
    Justification = "Each FailingDisposable is transferred to PreparedAssetsLifetime; its failing cleanup is the behavior under test.")]
internal sealed class PreparedAssetsLifetimeTests
{
    [Test]
    public void FailedLastLeaseReleaseRetainsValueWithoutRetryingDispose()
    {
        var value = new FailingDisposable();
        var lifetime = new PreparedAssetsLifetime(value);
        var lease = lifetime.Acquire();
        lifetime.Dispose();

        Assert.That(lease.Value, Is.SameAs(value));
        _ = Assert.Throws<InvalidOperationException>(lease.Dispose);

        Assert.That(lifetime.GetValue(), Is.SameAs(value));
        Assert.That(() => lease.Value, Throws.TypeOf<ObjectDisposedException>());
        lease.Dispose();
        Assert.That(value.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public void OwnerRetainsValueAfterFinalDisposalFailure()
    {
        var value = new FailingDisposable();
        var lifetime = new PreparedAssetsLifetime(value);

        _ = Assert.Throws<InvalidOperationException>(lifetime.Dispose);

        Assert.That(lifetime.GetValue(), Is.SameAs(value));
        lifetime.Dispose();
        Assert.That(value.DisposeCount, Is.EqualTo(1));
    }

    private sealed class FailingDisposable : IDisposable
    {
        private int _disposeCount;

        internal int DisposeCount => Volatile.Read(ref _disposeCount);

        public void Dispose()
        {
            _ = Interlocked.Increment(ref _disposeCount);
            throw new InvalidOperationException("The prepared asset cleanup failed.");
        }
    }
}
