// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace TerraFX.Assets.Tests;

[TestFixture]
internal sealed class AssetStoreEntryBoundaryTests
{
    [Test]
    public void CreateAsyncPreCancellationReturnsCanceledTaskWithoutOpeningPackage()
    {
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        var path = Path.Combine(Path.GetTempPath(), $"terrafx-missing-{Guid.NewGuid():N}.epkg");
        var options = new AssetStoreOptions(path, new byte[32], AssetPackageReaderTests.CreateRegistration());

        var task = AssetStore.CreateAsync(options, cancellationSource.Token);

        Assert.Multiple(() => {
            Assert.That(task.IsCanceled, Is.True);
            Assert.That(File.Exists(path), Is.False);
            Assert.That(
                Assert.ThrowsAsync<OperationCanceledException>(async () => await task.ConfigureAwait(false))?.CancellationToken,
                Is.EqualTo(cancellationSource.Token));
        });
    }

    [Test]
    public void CreateAsyncSynchronouslyValidatesOptionsBeforePreCancellation()
    {
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        var options = new AssetStoreOptions(
            Path.Combine(Path.GetTempPath(), $"terrafx-missing-{Guid.NewGuid():N}.epkg"),
            new byte[32],
            AssetPackageReaderTests.CreateRegistration()) {
            CpuAssetBudgetBytes = -1,
        };

        Assert.Multiple(() => {
            _ = Assert.Throws<ArgumentNullException>(() => AssetStore.CreateAsync(null!, cancellationSource.Token));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => AssetStore.CreateAsync(options, cancellationSource.Token));
        });
    }

    [Test]
    public async Task LoadAsyncValidatesDisposedStateThenArgumentsThenPreCancellation()
    {
        var package = AssetPackageReaderTests.CreatePackage();
        var path = AssetPackageReaderTests.WritePackage(package);
        var store = await AssetStore.CreateAsync(
            AssetPackageReaderTests.CreateOptions(path, package)).ConfigureAwait(false);

        try
        {
            using var cancellationSource = new CancellationTokenSource();
            await cancellationSource.CancelAsync().ConfigureAwait(false);

            _ = Assert.Throws<ArgumentOutOfRangeException>(
                () => store.LoadAsync<object>(default, cancellationSource.Token));

            var canceledTask = store.LoadAsync<object>(new AssetId(1), cancellationSource.Token);
            Assert.That(canceledTask.IsCanceled, Is.True);
            Assert.That(
                Assert.ThrowsAsync<OperationCanceledException>(async () => await canceledTask.ConfigureAwait(false))?.CancellationToken,
                Is.EqualTo(cancellationSource.Token));

            await store.DisposeAsync().ConfigureAwait(false);
            _ = Assert.Throws<ObjectDisposedException>(
                () => store.LoadAsync<object>(default, cancellationSource.Token));
        }
        finally
        {
            await store.DisposeAsync().ConfigureAwait(false);
            File.Delete(path);
        }
    }

    [Test]
    public async Task ReplacePackageAsyncValidatesDisposedStateThenOptionsThenPreCancellation()
    {
        var package = AssetPackageReaderTests.CreatePackage();
        var path = AssetPackageReaderTests.WritePackage(package);
        var store = await AssetStore.CreateAsync(
            AssetPackageReaderTests.CreateOptions(path, package)).ConfigureAwait(false);

        try
        {
            using var cancellationSource = new CancellationTokenSource();
            await cancellationSource.CancelAsync().ConfigureAwait(false);
            var invalidOptions = new AssetStoreOptions(
                Path.Combine(Path.GetTempPath(), $"terrafx-missing-{Guid.NewGuid():N}.epkg"),
                new byte[32],
                AssetPackageReaderTests.CreateRegistration()) {
                CpuAssetBudgetBytes = -1,
            };

            _ = Assert.Throws<ArgumentNullException>(
                () => store.ReplacePackageAsync(null!, cancellationSource.Token));
            _ = Assert.Throws<ArgumentOutOfRangeException>(
                () => store.ReplacePackageAsync(invalidOptions, cancellationSource.Token));

            var canceledTask = store.ReplacePackageAsync(
                new AssetStoreOptions(invalidOptions.AbsolutePackagePath, new byte[32], invalidOptions.Registration),
                cancellationSource.Token);
            Assert.That(canceledTask.IsCanceled, Is.True);
            Assert.That(
                Assert.ThrowsAsync<OperationCanceledException>(async () => await canceledTask.ConfigureAwait(false))?.CancellationToken,
                Is.EqualTo(cancellationSource.Token));
            Assert.That(File.Exists(invalidOptions.AbsolutePackagePath), Is.False);

            await store.DisposeAsync().ConfigureAwait(false);
            _ = Assert.Throws<ObjectDisposedException>(
                () => store.ReplacePackageAsync(null!, cancellationSource.Token));
        }
        finally
        {
            await store.DisposeAsync().ConfigureAwait(false);
            File.Delete(path);
        }
    }
}
