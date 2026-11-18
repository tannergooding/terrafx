// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX.Assets;

namespace TerraFX.Assets.Tests;

[TestFixture]
internal sealed class AssetPackageLeaseTests
{
    [Test]
    public async Task PackageLeaseConcurrentDisposeReleasesItsCapacityOnce()
    {
        var registration = AssetPackageReaderTests.CreateRegistration();
        var package = AssetPackageReaderTests.CreatePackage(new AssetPackageReaderTests.TestEntry(1, [0xA1]));
        var path = AssetPackageReaderTests.WritePackage(package);
        AssetStore? store = null;

        try
        {
            var options = new AssetStoreOptions(path, SHA256.HashData(package), registration) {
                MaximumLiveAssetLeases = 1,
            };
            store = await AssetStore.CreateAsync(options, CancellationToken.None).ConfigureAwait(false);
            var lease = store.AcquirePackageLease();
            using var ready = new CountdownEvent(4);
            using var start = new ManualResetEventSlim();
            var disposers = new Task[4];

            for (var index = 0; index < disposers.Length; index++)
            {
                disposers[index] = Task.Run(() => {
                    _ = ready.Signal();
                    start.Wait();
                    lease.Dispose();
                });
            }

            var allDisposersReady = ready.Wait(TimeSpan.FromSeconds(10));
            start.Set();
            await Task.WhenAll(disposers).ConfigureAwait(false);
            Assert.That(allDisposersReady, Is.True);
            using var replacementLease = store.AcquirePackageLease();
        }
        finally
        {
            if (store is not null)
            {
                await store.DisposeAsync().ConfigureAwait(false);
            }

            File.Delete(path);
        }
    }

    [Test]
    public async Task PackageLeaseConsumesAndReleasesLiveLeaseCapacity()
    {
        var registration = AssetPackageReaderTests.CreateRegistration();
        var package = AssetPackageReaderTests.CreatePackage(new AssetPackageReaderTests.TestEntry(1, [0xA1]));
        var path = AssetPackageReaderTests.WritePackage(package);
        AssetStore? store = null;

        try
        {
            var options = new AssetStoreOptions(path, SHA256.HashData(package), registration) {
                MaximumLiveAssetLeases = 1,
            };
            store = await AssetStore.CreateAsync(options, CancellationToken.None).ConfigureAwait(false);
            var lease = store.AcquirePackageLease();

            _ = Assert.Throws<InvalidOperationException>(() => store.AcquirePackageLease());

            lease.Dispose();
            using var replacementLease = store.AcquirePackageLease();
        }
        finally
        {
            if (store is not null)
            {
                await store.DisposeAsync().ConfigureAwait(false);
            }

            File.Delete(path);
        }
    }

    [Test]
    public async Task PackageLeaseRetainsExactBackingAcrossReplacementAndStoreDisposal()
    {
        var registration = AssetPackageReaderTests.CreateRegistration();
        var firstPackage = AssetPackageReaderTests.CreatePackage(new AssetPackageReaderTests.TestEntry(1, [0xA1]));
        var secondPackage = AssetPackageReaderTests.CreatePackage(new AssetPackageReaderTests.TestEntry(1, [0xB2]));
        var firstPath = AssetPackageReaderTests.WritePackage(firstPackage);
        var secondPath = AssetPackageReaderTests.WritePackage(secondPackage);
        AssetPackageLease? firstLease = null;
        AssetPackageLease? secondLease = null;
        AssetStore? store = null;

        try
        {
            store = await AssetStore.CreateAsync(
                AssetPackageReaderTests.CreateOptions(firstPath, firstPackage, registration),
                CancellationToken.None).ConfigureAwait(false);
            firstLease = store.AcquirePackageLease();

            await store.ReplacePackageAsync(
                AssetPackageReaderTests.CreateOptions(secondPath, secondPackage, registration),
                CancellationToken.None).ConfigureAwait(false);
            secondLease = store.AcquirePackageLease();

            await store.DisposeAsync().ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(firstLease.PackageSha256.AsSpan().ToArray(), Is.EqualTo(SHA256.HashData(firstPackage)));
                Assert.That(secondLease.PackageSha256.AsSpan().ToArray(), Is.EqualTo(SHA256.HashData(secondPackage)));
            });

            var completedFirstLease = firstLease
                ?? throw new InvalidOperationException("The first package lease was not acquired.");
            completedFirstLease.Dispose();
            firstLease = null;
            _ = Assert.Throws<ObjectDisposedException>(() => _ = completedFirstLease.PackageSha256);
        }
        finally
        {
            firstLease?.Dispose();
            secondLease?.Dispose();

            if (store is not null)
            {
                await store.DisposeAsync().ConfigureAwait(false);
            }

            File.Delete(firstPath);
            File.Delete(secondPath);
        }
    }

    [Test]
    public async Task StoreConcurrentDisposeIsIdempotentAndJoinsTheRelease()
    {
        var registration = AssetPackageReaderTests.CreateRegistration();
        var package = AssetPackageReaderTests.CreatePackage(new AssetPackageReaderTests.TestEntry(1, [0xA1]));
        var path = AssetPackageReaderTests.WritePackage(package);
        AssetStore? store = null;

        try
        {
            store = await AssetStore.CreateAsync(
                AssetPackageReaderTests.CreateOptions(path, package, registration),
                CancellationToken.None).ConfigureAwait(false);
            using var ready = new CountdownEvent(4);
            using var start = new ManualResetEventSlim();
            var disposers = new Task[4];

            for (var index = 0; index < disposers.Length; index++)
            {
                disposers[index] = Task.Run(async () => {
                    _ = ready.Signal();
                    start.Wait();
                    await store.DisposeAsync().ConfigureAwait(false);
                });
            }

            var allDisposersReady = ready.Wait(TimeSpan.FromSeconds(10));
            start.Set();
            await Task.WhenAll(disposers).ConfigureAwait(false);
            Assert.That(allDisposersReady, Is.True);
            _ = Assert.Throws<ObjectDisposedException>(() => store.AcquirePackageLease());
        }
        finally
        {
            if (store is not null)
            {
                await store.DisposeAsync().ConfigureAwait(false);
            }

            File.Delete(path);
        }
    }
}
