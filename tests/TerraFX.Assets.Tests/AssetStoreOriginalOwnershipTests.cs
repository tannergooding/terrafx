// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace TerraFX.Assets.Tests;

[TestFixture]
internal sealed class AssetStoreOriginalOwnershipTests
{
    private const long CandidateBytes = 8_529_464;
    private const long KeepOneEntryBytes = 1_176;
    private const long ServiceBytes = 66_472;
    private const long WarmCreateBytes = 8_595_936;

    internal static async Task WarmOriginalAnchorAsync(AssetStoreOptions options)
    {
        var warm = AssetStore.PreclaimOriginalOpening(options, out var opening);
        warm.ReleaseUnstartedOriginalOpening(opening);
        await warm.DisposeAsync().ConfigureAwait(false);
    }

    private static (
        string Path,
        AssetStore Store,
        AssetStore.OriginalOperation Opening,
        AssetStore.OriginalIssuer Issuer,
        OriginalIoLifetime Lifetime,
        OriginalPackageIo ExecutionState,
        Thread Thread,
        long SourceBefore,
        long SourceClaim) CreateTerminalCandidate()
    {
        var path = AssetPackageReaderTests.CreatePackagePath();
        var options = new AssetStoreOptions(path, new byte[32], AssetPackageReaderTests.CreateRegistration()) {
            CpuAssetBudgetBytes = 20_000_000,
        };
        var store = AssetStore.PreclaimOriginalOpening(options, out var opening);
        var issuer = opening.Issuer;
        var sourceBefore = issuer.SourceCapacity;
        var sourceClaim = OriginalPackageIoSourceCapacity.ExecutionStateBytes
            + OriginalPackageIoSourceCapacity.LifetimeBytes;
        issuer.ClaimSource(sourceClaim, liveWireBytes: 0);

        var lifetime = new OriginalIoLifetime {
            _serviceReturn = opening,
            _sourceClaim = sourceClaim,
        };
        var thread = new Thread(static () => { }) {
            IsBackground = true,
        };
        var executionState = new OriginalPackageIo {
            _issuer = issuer,
            _thread = thread,
        };
        lifetime._executionState = executionState;
        executionState.PublishRetirement(lifetime);
        return (path, store, opening, issuer, lifetime, executionState, thread, sourceBefore, sourceClaim);
    }

    private static long GetGenerationId(AssetPackageBacking backing)
        => (long)(typeof(AssetPackageBacking).GetField(
            "_originalGenerationId",
            BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(backing)
            ?? throw new InvalidOperationException("The original generation identity is missing."));

    private static async Task ReleaseTerminalCandidateAsync(
        string path,
        AssetStore store,
        AssetStore.OriginalOperation opening,
        AssetStore.OriginalIssuer issuer,
        OriginalIoLifetime lifetime,
        OriginalPackageIo executionState,
        Thread thread)
    {
        if (thread.IsAlive)
        {
            thread.Join();
        }

        lifetime._executionState = null;
        lifetime._serviceReturn = null;
        executionState._retiringOwner = null;
        var remainingClaim = lifetime._sourceClaim;

        if (remainingClaim is not 0)
        {
            issuer.ReleaseSource(remainingClaim);
        }

        store.ReleaseUnstartedOriginalOpening(opening);
        await store.DisposeAsync().ConfigureAwait(false);
        File.Delete(path);
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Capture every concurrent Store-creation failure for deterministic assertion.")]
    [Test]
    public async Task ConcurrentColdCreatesWaitForOriginalAnchorInstallation()
    {
        const int CreateCount = 8;
        var package = AssetPackageReaderTests.CreatePackage();
        var path = AssetPackageReaderTests.WritePackage(package);
        var options = AssetPackageReaderTests.CreateOptions(
            path,
            package,
            AssetPackageReaderTests.CreateRegistration());
        var stores = new AssetStore?[CreateCount];
        var failures = new Exception?[CreateCount];
        var threads = new Thread?[CreateCount];
        using var ready = new CountdownEvent(CreateCount);
        using var start = new ManualResetEventSlim();

        try
        {
            for (var index = 0; index < threads.Length; index++)
            {
                var storeIndex = index;
                var thread = new Thread(() => {
                    _ = ready.Signal();
                    start.Wait();

                    try
                    {
                        stores[storeIndex] = AssetStore.CreateAsync(options).GetAwaiter().GetResult();
                    }
                    catch (Exception failure)
                    {
                        failures[storeIndex] = failure;
                    }
                }) {
                    IsBackground = true,
                };
                threads[index] = thread;
                thread.Start();
            }

            Assert.That(ready.Wait(TimeSpan.FromSeconds(10)), Is.True);
            start.Set();

            foreach (var thread in threads)
            {
                Assert.That(thread!.Join(TimeSpan.FromSeconds(10)), Is.True);
            }

            Assert.That(failures, Has.All.Null);
        }
        finally
        {
            start.Set();

            foreach (var thread in threads)
            {
                if (thread is { IsAlive: true })
                {
                    _ = thread.Join(TimeSpan.FromSeconds(10));
                }
            }

            foreach (var store in stores)
            {
                if (store is not null)
                {
                    await store.DisposeAsync().ConfigureAwait(false);
                }
            }

            File.Delete(path);
        }
    }

    [TestCase(0L)]
    [TestCase(1L)]
    [TestCase(8_595_935L)]
    public async Task CreateRefusesInsufficientSourceCapacityBeforeMissingFileAcquisition(long budget)
    {
        var registration = AssetPackageReaderTests.CreateRegistration();
        var path = AssetPackageReaderTests.CreatePackagePath();
        await WarmOriginalAnchorAsync(new AssetStoreOptions(path, new byte[32], registration)).ConfigureAwait(false);
        var options = new AssetStoreOptions(path, new byte[32], registration) {
            CpuAssetBudgetBytes = budget,
        };

        var failure = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await AssetStore.CreateAsync(options).ConfigureAwait(false));

        Assert.That(failure!.Message, Is.EqualTo("The CPU asset budget cannot fund package opening."));
        Assert.That(File.Exists(path), Is.False);
    }

    [Test]
    public async Task ExactCandidateFitsAndReturnsUnleasedDisplacedSourceCapacity()
    {
        var package = AssetPackageReaderTests.CreatePackage(new AssetPackageReaderTests.TestEntry(1, [0xA5]));
        var path = AssetPackageReaderTests.WritePackage(package);
        var registration = AssetPackageReaderTests.CreateRegistration();
        await WarmOriginalAnchorAsync(AssetPackageReaderTests.CreateOptions(path, package, registration)).ConfigureAwait(false);
        var options = new AssetStoreOptions(path, System.Security.Cryptography.SHA256.HashData(package), registration) {
            CpuAssetBudgetBytes = ServiceBytes + KeepOneEntryBytes + CandidateBytes,
        };
        var store = await AssetStore.CreateAsync(options).ConfigureAwait(false);

        try
        {
            for (var index = 0; index < 4; index++)
            {
                await store.ReplacePackageAsync(options).ConfigureAwait(false);
                Assert.That(store.OriginalSourceCapacity, Is.EqualTo(ServiceBytes + KeepOneEntryBytes));
                Assert.That(GetGenerationId(AssetPackageReaderTests.GetSelectedBacking(store)), Is.EqualTo(index + 2));
            }
        }
        finally
        {
            await store.DisposeAsync().ConfigureAwait(false);
            File.Delete(path);
        }
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(3)]
    public async Task ExactCreateSettlesOpeningAndAdoptsActualEntryCapacityOnce(int entryCount)
    {
        var entries = new AssetPackageReaderTests.TestEntry[entryCount];

        for (var index = 0; index < entries.Length; index++)
        {
            entries[index] = new AssetPackageReaderTests.TestEntry(checked((uint)index + 1), [0xA5]);
        }

        var package = AssetPackageReaderTests.CreatePackage(entries);
        var path = AssetPackageReaderTests.WritePackage(package);
        var registration = AssetPackageReaderTests.CreateRegistration();
        await WarmOriginalAnchorAsync(AssetPackageReaderTests.CreateOptions(path, package, registration)).ConfigureAwait(false);
        var options = new AssetStoreOptions(path, System.Security.Cryptography.SHA256.HashData(package), registration) {
            CpuAssetBudgetBytes = WarmCreateBytes,
        };
        var store = await AssetStore.CreateAsync(options).ConfigureAwait(false);

        try
        {
            Assert.That(store.OriginalSourceCapacity, Is.EqualTo(ServiceBytes + 1_144 + (32L * entryCount)));
            Assert.That(AssetPackageReaderTests.GetSelectedBacking(store).Entries, Has.Length.EqualTo(entryCount));
            Assert.That(GetGenerationId(AssetPackageReaderTests.GetSelectedBacking(store)), Is.EqualTo(1));

            await store.DisposeAsync().ConfigureAwait(false);
            Assert.That(store.OriginalSourceCapacity, Is.EqualTo(152 + 64));
            await store.DisposeAsync().ConfigureAwait(false);
            Assert.That(store.OriginalSourceCapacity, Is.EqualTo(152 + 64));
        }
        finally
        {
            await store.DisposeAsync().ConfigureAwait(false);
            File.Delete(path);
        }
    }

    [Test]
    public async Task FailedOpeningKeepsSourceFaultAndCleanupCarriersButReturnsUnusedMetadataPeak()
    {
        var package = AssetPackageReaderTests.CreatePackage();
        var path = AssetPackageReaderTests.WritePackage(package);
        var registration = AssetPackageReaderTests.CreateRegistration();
        await WarmOriginalAnchorAsync(AssetPackageReaderTests.CreateOptions(path, package, registration)).ConfigureAwait(false);
        var store = await AssetStore.CreateAsync(AssetPackageReaderTests.CreateOptions(path, package, registration)).ConfigureAwait(false);

        try
        {
            var badDigest = new AssetStoreOptions(path, new byte[32], registration);
            _ = Assert.ThrowsAsync<InvalidDataException>(
                async () => await store.ReplacePackageAsync(badDigest).ConfigureAwait(false));

            Assert.That(store.OriginalSourceCapacity, Is.EqualTo(ServiceBytes + 1_144 + 1_144 + 688));
            using var current = store.AcquirePackageLease();
            Assert.That(current.PackageSha256, Is.EqualTo(System.Security.Cryptography.SHA256.HashData(package)));
            await store.ReplacePackageAsync(AssetPackageReaderTests.CreateOptions(path, package, registration)).ConfigureAwait(false);
            Assert.That(store.OriginalSourceCapacity, Is.EqualTo(ServiceBytes + (3 * 1_144) + 688));
        }
        finally
        {
            await store.DisposeAsync().ConfigureAwait(false);
            File.Delete(path);
        }
    }

    [Test]
    public async Task FailedTerminalPreconditionsPreserveExecutionStateUntilHandback()
    {
        var candidate = CreateTerminalCandidate();
        var sourceFailure = new InvalidDataException("retained original service failure");
        candidate.Opening.RecordFailure(sourceFailure, cleanup: false);
        var primaryFailureField = typeof(AssetStore.OriginalOperation).GetField(
            "_primary",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The original service failure field is unavailable.");

        try
        {
            _ = Assert.Throws<InvalidOperationException>(
                () => OriginalIoLifetime.CompleteTerminalReturn(candidate.Lifetime));
            Assert.That(
                candidate.Issuer.SourceCapacity,
                Is.EqualTo(candidate.SourceBefore + candidate.SourceClaim));
            Assert.That(candidate.Lifetime._executionState, Is.SameAs(candidate.ExecutionState));

            candidate.Thread.Start();
            OriginalPackageIo.RunTerminalReturnCallback(candidate.ExecutionState);

            Assert.Multiple(() => {
                Assert.That(
                    candidate.Issuer.SourceCapacity,
                    Is.EqualTo(candidate.SourceBefore + OriginalPackageIoSourceCapacity.LifetimeBytes));
                Assert.That(
                    candidate.Lifetime._sourceClaim,
                    Is.EqualTo(OriginalPackageIoSourceCapacity.LifetimeBytes));
                Assert.That(candidate.Lifetime._executionState, Is.Null);
                Assert.That(candidate.Lifetime._serviceReturn, Is.SameAs(candidate.Opening));
                Assert.That(primaryFailureField.GetValue(candidate.Opening), Is.SameAs(sourceFailure));
                Assert.That(candidate.ExecutionState._activeRequest, Is.Null);
                Assert.That(candidate.ExecutionState._issuer, Is.Null);
                Assert.That(candidate.ExecutionState._queueGate, Is.Null);
                Assert.That(candidate.ExecutionState._queueHead, Is.Null);
                Assert.That(candidate.ExecutionState._queueTail, Is.Null);
                Assert.That(candidate.ExecutionState._retirementCallback, Is.Null);
                Assert.That(candidate.ExecutionState._retiringOwner, Is.Null);
                Assert.That(candidate.ExecutionState._returningHead, Is.Null);
                Assert.That(candidate.ExecutionState._thread, Is.Null);
            });

            _ = Assert.Throws<InvalidOperationException>(
                () => OriginalIoLifetime.CompleteTerminalReturn(candidate.Lifetime));
            Assert.That(
                candidate.Issuer.SourceCapacity,
                Is.EqualTo(candidate.SourceBefore + OriginalPackageIoSourceCapacity.LifetimeBytes));
        }
        finally
        {
            await ReleaseTerminalCandidateAsync(
                candidate.Path,
                candidate.Store,
                candidate.Opening,
                candidate.Issuer,
                candidate.Lifetime,
                candidate.ExecutionState,
                candidate.Thread).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task IdenticalRetainedGenerationsCannotRefinanceOneByteShortCandidate()
    {
        var package = AssetPackageReaderTests.CreatePackage(new AssetPackageReaderTests.TestEntry(1, [0xA5]));
        var path = AssetPackageReaderTests.WritePackage(package);
        var missingPath = AssetPackageReaderTests.CreatePackagePath();
        var registration = AssetPackageReaderTests.CreateRegistration();
        await WarmOriginalAnchorAsync(AssetPackageReaderTests.CreateOptions(path, package, registration)).ConfigureAwait(false);
        var originalBudget = ServiceBytes + (3 * KeepOneEntryBytes) + CandidateBytes - 1;
        var options = new AssetStoreOptions(path, System.Security.Cryptography.SHA256.HashData(package), registration) {
            CpuAssetBudgetBytes = originalBudget,
        };
        var store = await AssetStore.CreateAsync(options).ConfigureAwait(false);
        AssetPackageLease? first = null;
        AssetPackageLease? second = null;

        try
        {
            first = store.AcquirePackageLease();
            await store.ReplacePackageAsync(options).ConfigureAwait(false);
            second = store.AcquirePackageLease();
            await store.ReplacePackageAsync(options).ConfigureAwait(false);
            Assert.That(GetGenerationId(AssetPackageReaderTests.GetSelectedBacking(store)), Is.EqualTo(3));
            Assert.That(store.OriginalSourceCapacity, Is.EqualTo(ServiceBytes + (3 * KeepOneEntryBytes)));

            var largerOptions = new AssetStoreOptions(missingPath, options.ExpectedPackageSha256.AsSpan(), registration) {
                CpuAssetBudgetBytes = long.MaxValue,
            };
            var failure = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await store.ReplacePackageAsync(largerOptions).ConfigureAwait(false));

            Assert.Multiple(() => {
                Assert.That(failure!.Message, Does.Contain("original CPU asset source-capacity"));
                Assert.That(File.Exists(missingPath), Is.False);
                Assert.That(first.PackageSha256, Is.EqualTo(options.ExpectedPackageSha256));
                Assert.That(second.PackageSha256, Is.EqualTo(options.ExpectedPackageSha256));
                Assert.That(store.OriginalSourceCapacity, Is.EqualTo(ServiceBytes + (3 * KeepOneEntryBytes)));
            });

            first.Dispose();
            Assert.That(store.OriginalSourceCapacity, Is.EqualTo(ServiceBytes + (2 * KeepOneEntryBytes)));
            first.Dispose();
            Assert.That(store.OriginalSourceCapacity, Is.EqualTo(ServiceBytes + (2 * KeepOneEntryBytes)));

            await store.ReplacePackageAsync(options).ConfigureAwait(false);
            Assert.That(GetGenerationId(AssetPackageReaderTests.GetSelectedBacking(store)), Is.EqualTo(4));
            Assert.That(store.OriginalSourceCapacity, Is.EqualTo(ServiceBytes + (2 * KeepOneEntryBytes)));

            second.Dispose();
            Assert.That(store.OriginalSourceCapacity, Is.EqualTo(ServiceBytes + KeepOneEntryBytes));
        }
        finally
        {
            first?.Dispose();
            second?.Dispose();
            await store.DisposeAsync().ConfigureAwait(false);
            File.Delete(path);
        }
    }

    [Test]
    public async Task MissingFileConstructorPrefixRetainsItsOriginalCreateIssuer()
    {
        var path = AssetPackageReaderTests.CreatePackagePath();
        var registration = AssetPackageReaderTests.CreateRegistration();
        var options = new AssetStoreOptions(path, new byte[32], registration) {
            CpuAssetBudgetBytes = WarmCreateBytes,
        };
        await WarmOriginalAnchorAsync(new AssetStoreOptions(path, new byte[32], registration)).ConfigureAwait(false);

        _ = Assert.ThrowsAsync<FileNotFoundException>(
            async () => await AssetStore.CreateAsync(options).ConfigureAwait(false));
        var failedBacking = typeof(AssetPackageBacking).GetField(
            "s_failedReleaseRoot",
            BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as AssetPackageBacking
            ?? throw new InvalidOperationException("The failed acquisition root is missing.");
        var issuer = typeof(AssetPackageBacking).GetField(
            "_originalIssuer",
            BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(failedBacking) as AssetStore.OriginalIssuer
            ?? throw new InvalidOperationException("The original acquisition issuer is missing.");
        var opening = typeof(AssetPackageBacking).GetField(
            "_originalOpening",
            BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(failedBacking) as AssetStore.OriginalOperation
            ?? throw new InvalidOperationException("The failed opening control is missing.");

        Assert.Multiple(() => {
            Assert.That(issuer.Budget, Is.EqualTo(WarmCreateBytes));
            Assert.That(issuer.SourceCapacity, Is.EqualTo(64 + 1_144 + 688));
            Assert.That(opening.FileAcquisitionAttempted, Is.True);
            Assert.That(failedBacking.OriginalUnreturnedReleaseFailure, Is.TypeOf<FileNotFoundException>());
            Assert.That(File.Exists(path), Is.False);
        });
    }

    [Test]
    public async Task PreCancellationPrecedesPositiveSourceRefusalAndPreservesOriginalToken()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync().ConfigureAwait(false);
        var path = AssetPackageReaderTests.CreatePackagePath();
        var options = new AssetStoreOptions(path, new byte[32], AssetPackageReaderTests.CreateRegistration()) {
            CpuAssetBudgetBytes = 1,
        };
        var task = AssetStore.CreateAsync(options, cancellation.Token);
        var failure = Assert.ThrowsAsync<OperationCanceledException>(
            async () => await task.ConfigureAwait(false));

        Assert.That(task.IsCanceled, Is.True);
        Assert.That(failure!.CancellationToken, Is.EqualTo(cancellation.Token));
        Assert.That(File.Exists(path), Is.False);
    }

    [Test]
    public async Task TerminalCallbackRefusesHandbackWhileNativeUseRemains()
    {
        var candidate = CreateTerminalCandidate();
        candidate.ExecutionState._nativeUse = 1;

        try
        {
            candidate.Thread.Start();
            _ = Assert.Throws<InvalidOperationException>(
                () => OriginalPackageIo.RunTerminalReturnCallback(candidate.ExecutionState));

            Assert.Multiple(() => {
                Assert.That(candidate.Thread.IsAlive, Is.False);
                Assert.That(candidate.Issuer.SourceCapacity, Is.EqualTo(candidate.SourceBefore + candidate.SourceClaim));
                Assert.That(candidate.Lifetime._sourceClaim, Is.EqualTo(candidate.SourceClaim));
                Assert.That(candidate.Lifetime._executionState, Is.SameAs(candidate.ExecutionState));
                Assert.That(candidate.Lifetime._lifecycle, Is.EqualTo(OriginalIoLifetime.TerminalStateLive));
                Assert.That(candidate.ExecutionState._retiringOwner, Is.SameAs(candidate.Lifetime));
                Assert.That(candidate.ExecutionState._thread, Is.SameAs(candidate.Thread));
            });
        }
        finally
        {
            await ReleaseTerminalCandidateAsync(
                candidate.Path,
                candidate.Store,
                candidate.Opening,
                candidate.Issuer,
                candidate.Lifetime,
                candidate.ExecutionState,
                candidate.Thread).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task UnreturnedCandidateFilePrefixKeepsOriginalDebtAndBlocksAnotherReplacement()
    {
        var package = AssetPackageReaderTests.CreatePackage();
        var path = AssetPackageReaderTests.WritePackage(package);
        var missingPath = AssetPackageReaderTests.CreatePackagePath();
        var options = AssetPackageReaderTests.CreateOptions(path, package);
        await WarmOriginalAnchorAsync(options).ConfigureAwait(false);
        var store = await AssetStore.CreateAsync(options).ConfigureAwait(false);

        try
        {
            var candidate = new AssetStoreOptions(missingPath, new byte[32], options.Registration) {
                CpuAssetBudgetBytes = long.MaxValue,
            };
            var firstFailure = Assert.ThrowsAsync<FileNotFoundException>(
                async () => await store.ReplacePackageAsync(candidate).ConfigureAwait(false));

            Assert.That(store.OriginalSourceCapacity, Is.EqualTo(ServiceBytes + (2 * 1_144) + 688));
            var refusal = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await store.ReplacePackageAsync(options).ConfigureAwait(false));
            Assert.That(refusal!.Message, Does.Contain("package-replacement owner"));
            Assert.That(store.OriginalSourceCapacity, Is.EqualTo(ServiceBytes + (2 * 1_144) + 688));
            Assert.That(File.Exists(missingPath), Is.False);
            var closeFailure = Assert.ThrowsAsync<FileNotFoundException>(
                async () => await store.DisposeAsync().ConfigureAwait(false));
            Assert.That(closeFailure, Is.SameAs(firstFailure));
        }
        finally
        {
            _ = Assert.ThrowsAsync<FileNotFoundException>(
                async () => await store.DisposeAsync().ConfigureAwait(false));
            File.Delete(path);
        }
    }
}
