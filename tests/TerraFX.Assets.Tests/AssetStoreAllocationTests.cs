// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using NUnit.Framework;
using TerraFX.Assets;

namespace TerraFX.Assets.Tests;

[TestFixture]
internal sealed class AssetStoreAllocationTests
{
    private static long MeasureByteArrayBytes(int byteCount, int sampleCount)
    {
        var warmInstances = new byte[sampleCount][];

        for (var index = 0; index < warmInstances.Length; index++)
        {
            warmInstances[index] = new byte[byteCount];
        }

        GC.KeepAlive(warmInstances);
        var measuredInstances = new byte[sampleCount][];
        var allocationStart = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < measuredInstances.Length; index++)
        {
            measuredInstances[index] = new byte[byteCount];
        }

        var measuredBytes = (GC.GetAllocatedBytesForCurrentThread() - allocationStart) / sampleCount;
        GC.KeepAlive(measuredInstances);
        return measuredBytes;
    }

    private static long MeasureCarrierBytes(Type type, int sampleCount)
    {
        var warmInstances = new object[sampleCount];

        for (var index = 0; index < warmInstances.Length; index++)
        {
            warmInstances[index] = RuntimeHelpers.GetUninitializedObject(type);
        }

        GC.KeepAlive(warmInstances);
        var measuredInstances = new object[sampleCount];
        var allocationStart = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < measuredInstances.Length; index++)
        {
            measuredInstances[index] = RuntimeHelpers.GetUninitializedObject(type);
        }

        var measuredBytes = (GC.GetAllocatedBytesForCurrentThread() - allocationStart) / sampleCount;
        GC.KeepAlive(measuredInstances);
        return measuredBytes;
    }

    [Test]
    public void BoundedNativePortDeclarationsUseExpectedX64Layouts()
    {
        if (RuntimeInformation.ProcessArchitecture is not Architecture.X64)
        {
            Assert.Ignore("The bounded native port layout profile is x64-only.");
        }

        Assert.Multiple(() => {
            Assert.That(Unsafe.SizeOf<AssetBoundedWindowsOverlapped>(), Is.EqualTo(32));
            Assert.That(Marshal.OffsetOf<AssetBoundedWindowsOverlapped>(
                nameof(AssetBoundedWindowsOverlapped.Offset)).ToInt32(), Is.EqualTo(16));
            Assert.That(Marshal.OffsetOf<AssetBoundedWindowsOverlapped>(
                nameof(AssetBoundedWindowsOverlapped.EventHandle)).ToInt32(), Is.EqualTo(24));
            Assert.That(Unsafe.SizeOf<AssetBoundedWindowsStandardInfo>(), Is.EqualTo(24));
            Assert.That(Marshal.OffsetOf<AssetBoundedWindowsStandardInfo>(
                nameof(AssetBoundedWindowsStandardInfo.EndOfFile)).ToInt32(), Is.EqualTo(8));
            Assert.That(Marshal.OffsetOf<AssetBoundedWindowsStandardInfo>(
                nameof(AssetBoundedWindowsStandardInfo.Directory)).ToInt32(), Is.EqualTo(21));
            Assert.That(Unsafe.SizeOf<AssetBoundedLinuxStatX64>(), Is.EqualTo(144));
            Assert.That(Marshal.OffsetOf<AssetBoundedLinuxStatX64>(
                nameof(AssetBoundedLinuxStatX64.Mode)).ToInt32(), Is.EqualTo(24));
            Assert.That(Marshal.OffsetOf<AssetBoundedLinuxStatX64>(
                nameof(AssetBoundedLinuxStatX64.SizeBytes)).ToInt32(), Is.EqualTo(48));
        });

        Assert.That(MeasureCarrierBytes(typeof(AssetBoundedNativeFile), 64), Is.EqualTo(80));
        Assert.That(MeasureCarrierBytes(typeof(AssetBoundedNativePort), 64), Is.EqualTo(144));
        TestContext.WriteLine("Managed declaration layout only; no native target ABI or I/O was exercised.");
    }

    [Test]
    public void BoundedOpeningBufferArraysMatchMeasuredAllocationCharges()
    {
        var profileBufferBytes = MeasureByteArrayBytes(65_536, 64);
        var directoryBufferBytes = MeasureByteArrayBytes(128, 64);

        Assert.Multiple(() => {
            Assert.That(profileBufferBytes, Is.EqualTo(65_560));
            Assert.That(directoryBufferBytes, Is.EqualTo(152));
        });
        TestContext.WriteLine($"Bounded profile buffer={profileBufferBytes}; directory buffer={directoryBufferBytes};");
    }

    [Test]
    public void MeasuresTypedLoadAndPackagePinCarrierShells()
    {
        const int SampleCount = 64;
        var typedLeaseType = typeof(AssetLease<object>);
        var typedLeaseStateType = typedLeaseType.GetNestedType("LeaseState", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The typed AssetLease state is unavailable.");

        if (typedLeaseStateType.ContainsGenericParameters)
        {
            typedLeaseStateType = typedLeaseStateType.MakeGenericType(typeof(object));
        }

        var packageLeaseStateType = typeof(AssetPackageLease).GetNestedType("LeaseState", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The AssetPackageLease state is unavailable.");
        (string Name, Type Type, long ExpectedBytes)[] carrierTypes = [
            (nameof(AssetCodecContext), typeof(AssetCodecContext), 64),
            (nameof(AssetLeaseLifetime), typeof(AssetLeaseLifetime), 72),
            ("AssetLease<T>", typedLeaseType, 32),
            ("AssetLease<T>.LeaseState", typedLeaseStateType, 56),
            (nameof(AssetPackageLease), typeof(AssetPackageLease), 24),
            ("AssetPackageLease.LeaseState", packageLeaseStateType, 56),
            (nameof(Lock), typeof(Lock), 40),
            ("ObjectGate", typeof(object), 24),
            (nameof(AssetStore), typeof(AssetStore), 104),
            (nameof(AssetStoreRegistration), typeof(AssetStoreRegistration), 48),
            (nameof(AssetBoundedNativeFile), typeof(AssetBoundedNativeFile), 80),
            (nameof(AssetBoundedNativePort), typeof(AssetBoundedNativePort), 144),
            (nameof(AssetStoreBoundedProvision), typeof(AssetStoreBoundedProvision), 192),
            (nameof(AssetPackageBacking), typeof(AssetPackageBacking), 168),
            (nameof(AssetPackageBackingToken), typeof(AssetPackageBackingToken), 24),
        ];

        foreach (var (name, type, expectedBytes) in carrierTypes)
        {
            var measuredBytes = MeasureCarrierBytes(type, SampleCount);
            Assert.That(measuredBytes, Is.EqualTo(expectedBytes), name);
            TestContext.WriteLine($"{name}={measuredBytes};");
        }

        TestContext.WriteLine(
            $"Framework={RuntimeInformation.FrameworkDescription}; Architecture={RuntimeInformation.ProcessArchitecture}");
    }

    [Test]
    public void PackageBackingConstructionMatchesMeasuredCarrierShells()
    {
        const int SampleCount = 64;
        var packagePath = Path.Combine(Path.GetTempPath(), $"terrafx-allocation-{Guid.NewGuid():N}.epkg");
        File.WriteAllBytes(packagePath, []);
        var streams = new FileStream?[SampleCount + 1];
        var backings = new AssetPackageBacking?[SampleCount];

        try
        {
            for (var index = 0; index < streams.Length; index++)
            {
                streams[index] = File.OpenRead(packagePath);
            }

            var registration = AssetPackageReaderTests.CreateRegistration();
            var options = new AssetStoreOptions(packagePath, new byte[32], registration);
            var warmStream = streams[0]
                ?? throw new InvalidOperationException("The package stream was not initialized.");
            var warmBacking = new AssetPackageBacking(warmStream, [], default, default, options, packageByteLength: 0);
            warmBacking.ReleaseOwner();

            var allocationStart = GC.GetAllocatedBytesForCurrentThread();

            for (var index = 0; index < backings.Length; index++)
            {
                var stream = streams[index + 1]
                    ?? throw new InvalidOperationException("The package stream was not initialized.");
                backings[index] = new AssetPackageBacking(
                    stream,
                    [],
                    default,
                    default,
                    options,
                    packageByteLength: 0);
            }

            var measuredBytes = (GC.GetAllocatedBytesForCurrentThread() - allocationStart) / SampleCount;
            GC.KeepAlive(backings);
            GC.KeepAlive(streams);

            Assert.That(measuredBytes, Is.EqualTo(296));
            TestContext.WriteLine($"AssetPackageBacking constructor including token, Lock and read limits={measuredBytes};");
            TestContext.WriteLine(
                $"Framework={RuntimeInformation.FrameworkDescription}; Architecture={RuntimeInformation.ProcessArchitecture}");
        }
        finally
        {
            foreach (var backing in backings)
            {
                backing?.ReleaseOwner();
            }

            foreach (var stream in streams)
            {
                stream?.Dispose();
            }

            File.Delete(packagePath);
        }
    }

    [Test]
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The test measures package-pin handle construction without invoking operations on the uninitialized Store/backing owners.")]
    public void PackagePinConstructionMatchesMeasuredCarrierShells()
    {
        const int SampleCount = 64;
        var store = (AssetStore)RuntimeHelpers.GetUninitializedObject(typeof(AssetStore));
        var backing = (AssetPackageBacking)RuntimeHelpers.GetUninitializedObject(typeof(AssetPackageBacking));
        _ = new AssetPackageLease(store, backing);
        var leases = new AssetPackageLease[SampleCount];
        var allocationStart = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < leases.Length; index++)
        {
            leases[index] = new AssetPackageLease(store, backing);
        }

        var measuredBytes = (GC.GetAllocatedBytesForCurrentThread() - allocationStart) / SampleCount;
        GC.KeepAlive(leases);
        GC.KeepAlive(store);
        GC.KeepAlive(backing);

        Assert.That(measuredBytes, Is.EqualTo(104));
        TestContext.WriteLine($"AssetPackageLease+LeaseState+object gate={measuredBytes};");
    }

    [Test]
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The test measures carrier construction without invoking operations on the uninitialized Store/backing owners.")]
    public void TypedLeaseConstructionMatchesMeasuredCarrierShells()
    {
        const int SampleCount = 64;
        var store = (AssetStore)RuntimeHelpers.GetUninitializedObject(typeof(AssetStore));
        var backing = (AssetPackageBacking)RuntimeHelpers.GetUninitializedObject(typeof(AssetPackageBacking));
        var value = new object();
        _ = new AssetLease<object>(new AssetId(1), value, new AssetLeaseLifetime(store, backing, 16));
        var leases = new AssetLease<object>[SampleCount];
        var allocationStart = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < leases.Length; index++)
        {
            leases[index] = new AssetLease<object>(
                new AssetId(index + 1),
                value,
                new AssetLeaseLifetime(store, backing, 16));
        }

        var measuredBytes = (GC.GetAllocatedBytesForCurrentThread() - allocationStart) / SampleCount;
        GC.KeepAlive(leases);
        GC.KeepAlive(value);
        GC.KeepAlive(store);
        GC.KeepAlive(backing);

        Assert.That(measuredBytes, Is.EqualTo(224));
        TestContext.WriteLine(
            $"AssetLeaseLifetime+Lock+AssetLease<T>+LeaseState+object gate={measuredBytes};");
    }
}
