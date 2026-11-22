// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;
using NUnit.Framework;
using TerraFX.Assets;

namespace TerraFX.Assets.Tests;

[TestFixture]
internal sealed class AssetStoreAllocationTests
{
    private static int GetManagedFieldWidth(Type type)
    {
        if (!type.IsValueType)
        {
            return IntPtr.Size;
        }

        if (type.IsEnum)
        {
            type = Enum.GetUnderlyingType(type);
        }

        if ((type == typeof(bool)) || (type == typeof(byte)))
        {
            return 1;
        }

        if (type == typeof(ushort))
        {
            return 2;
        }

        if ((type == typeof(int)) || (type == typeof(uint)))
        {
            return 4;
        }

        if ((type == typeof(long)) || (type == typeof(ulong)))
        {
            return 8;
        }

        if ((type == typeof(IntPtr)) || (type == typeof(UIntPtr)))
        {
            return IntPtr.Size;
        }

        if (type == typeof(SpatialProfileId))
        {
            return Unsafe.SizeOf<SpatialProfileId>();
        }

        if (type == typeof(Guid))
        {
            return Unsafe.SizeOf<Guid>();
        }

        if (type == typeof(SpatialSettings))
        {
            return Unsafe.SizeOf<SpatialSettings>();
        }

        if (type == typeof(CancellationToken))
        {
            return Unsafe.SizeOf<CancellationToken>();
        }

        if (type == typeof(ManualResetValueTaskSourceCore<int>))
        {
            return Unsafe.SizeOf<ManualResetValueTaskSourceCore<int>>();
        }

        if (type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(System.Collections.Immutable.ImmutableArray<>)))
        {
            return Unsafe.SizeOf<System.Collections.Immutable.ImmutableArray<byte>>();
        }

        throw new InvalidOperationException($"The source declaration contains an unpriced managed field: {type}.");
    }

    private static (long Raw, long Capacity) GetSourceFieldCapacity(Type type)
    {
        var raw = 0L;
        var slots = 0L;

        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                var width = GetManagedFieldWidth(field.FieldType);
                raw = checked(raw + width);
                slots = checked(slots + ((width + 7L) & ~7L));
            }
        }

        return (raw, Math.Max(24, checked((16 + slots + 7) & ~7L)));
    }

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
            (nameof(AssetStore), typeof(AssetStore), 136),
            (nameof(AssetStoreRegistration), typeof(AssetStoreRegistration), 48),
            (nameof(AssetBoundedNativeFile), typeof(AssetBoundedNativeFile), 80),
            (nameof(AssetBoundedNativePort), typeof(AssetBoundedNativePort), 144),
            (nameof(AssetStoreBoundedProvision), typeof(AssetStoreBoundedProvision), 216),
            (nameof(AssetPackageBacking), typeof(AssetPackageBacking), 264),
            (nameof(AssetPackageBackingToken), typeof(AssetPackageBackingToken), 24),
            ("OriginalIssuer", typeof(AssetStore.OriginalIssuer), 64),
            ("OriginalOperation", typeof(AssetStore.OriginalOperation), 136),
            (nameof(OriginalIoLifetime), typeof(OriginalIoLifetime), 96),
            (nameof(OriginalPackageIo), typeof(OriginalPackageIo), 128),
            ("OriginalOpeningStorage", typeof(AssetStore.OriginalOpeningStorage), 88),
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
    public void OriginalPackageIoCandidateCapacitiesMatchFieldDeclarations()
    {
        if (!OperatingSystem.IsWindows()
            || (RuntimeInformation.ProcessArchitecture is not Architecture.X64)
            || !StringComparer.Ordinal.Equals(
                RuntimeInformation.FrameworkDescription,
                ".NET 11.0.0-rc.3.26522.148"))
        {
            Assert.Ignore("The original package-I/O source layout is qualified only for Windows x64 CoreCLR .NET 11 RC3.");
        }

        (Type Type, long Raw, long Capacity)[] carriers = [
            (typeof(OriginalIoLifetime), 80, 112),
            (typeof(OriginalIoRequest), 188, 256),
            (typeof(OriginalPackageIo), 112, 160),
        ];

        foreach (var (type, raw, capacity) in carriers)
        {
            Assert.That(GetSourceFieldCapacity(type), Is.EqualTo((raw, capacity)), type.Name);
        }

        Assert.Multiple(() => {
            Assert.That(Unsafe.SizeOf<ManualResetValueTaskSourceCore<int>>(), Is.EqualTo(40));
            Assert.That(OriginalPackageIoSourceCapacity.ServiceBytes, Is.EqualTo(66_752));
            Assert.That(OriginalPackageIoSourceCapacity.MaximumOpeningRequestCount, Is.EqualTo(131_077));
            Assert.That(OriginalPackageIoSourceCapacity.OpeningRequestBytes, Is.EqualTo(33_555_712));
            Assert.That(
                OriginalPackageIoSourceCapacity.GetKeepSourceBytes(131_072, 1_048),
                Is.EqualTo(4_196_760));
            Assert.That(OriginalPackageIoSourceCapacity.OpeningBaseBytes, Is.EqualTo(4_334_040));
            Assert.That(OriginalPackageIoSourceCapacity.GetPathBufferSourceBytes(512), Is.EqualTo(1_048));
            Assert.That(OriginalPackageIoSourceCapacity.GetReportedPathSourceBytes(511), Is.EqualTo(1_048));
            Assert.That(
                OriginalPackageIoSourceCapacity.GetColdCreateSourceBytes(2_096, 1_048, ownsAnchor: true),
                Is.EqualTo(42_155_392));
        });
        TestContext.WriteLine("Declared source capacity only; no worker, Thread, path buffer, native block or file is materialized.");
    }

    [Test]
    public async Task OriginalPreclaimConstructionMatchesSourceAndExcludedFrameworkShellsWithoutFileAcquisition()
    {
        const int SampleCount = 64;
        var packagePath = AssetPackageReaderTests.CreatePackagePath();
        var registration = AssetPackageReaderTests.CreateRegistration();
        var options = new AssetStoreOptions(packagePath, new byte[32], registration);
        await AssetStoreOriginalOwnershipTests.WarmOriginalAnchorAsync(options).ConfigureAwait(false);
        var stores = new AssetStore[SampleCount];
        var openings = new AssetStore.OriginalOperation[SampleCount];
        var constructedCount = 0;
        var excludedCompletionShellBytes =
            MeasureCarrierBytes(typeof(TaskCompletionSource), SampleCount) + MeasureCarrierBytes(typeof(Task), SampleCount);

        try
        {
            var allocationStart = GC.GetAllocatedBytesForCurrentThread();

            for (var index = 0; index < stores.Length; index++)
            {
                stores[index] = AssetStore.PreclaimOriginalOpening(options, out var opening);
                openings[index] = opening;
                constructedCount++;
            }

            var measuredBytes = (GC.GetAllocatedBytesForCurrentThread() - allocationStart) / SampleCount;
            GC.KeepAlive(stores);
            GC.KeepAlive(openings);

            // Replace the three original operation shells and add the E/L source carriers.
            var sourceCarrierBytes = 1_024
                + (3 * (MeasureCarrierBytes(typeof(AssetStore.OriginalOperation), SampleCount) - 128))
                + MeasureCarrierBytes(typeof(OriginalIoLifetime), SampleCount)
                + MeasureCarrierBytes(typeof(OriginalPackageIo), SampleCount);
            Assert.That(measuredBytes, Is.EqualTo(sourceCarrierBytes + (3 * 40) + excludedCompletionShellBytes));

            for (var index = 0; index < stores.Length; index++)
            {
                Assert.That(stores[index].OriginalSourceCapacity, Is.EqualTo(8_596_232));
                Assert.That(openings[index].FileAcquisitionAttempted, Is.False);
                Assert.That(openings[index].Storage.Stream, Is.Null);
            }

            Assert.That(File.Exists(packagePath), Is.False);
            TestContext.WriteLine($"Unstarted source preclaim=8596232; measured managed constructor allocations={measuredBytes};");
            TestContext.WriteLine("The native scratch/OVERLAPPED capacities are unspent; no worker, hash, file, native close or caller-return qualification.");
            TestContext.WriteLine(
                $"Framework={RuntimeInformation.FrameworkDescription}; Architecture={RuntimeInformation.ProcessArchitecture}");
        }
        finally
        {
            for (var index = 0; index < constructedCount; index++)
            {
                stores[index].ReleaseUnstartedOriginalOpening(openings[index]);
                await stores[index].DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    [Test]
    public void OriginalSourceExpressionsCoverFinalFieldMembershipAndEntryStride()
    {
        (Type Type, long Raw, long Capacity)[] carriers = [
            (typeof(AssetStore), 117, 152),
            (typeof(AssetStore.OriginalIssuer), 44, 64),
            (typeof(AssetPackageBacking), 246, 328),
            (typeof(EbinReadLimits), 44, 80),
            (typeof(AssetPackageBackingToken), 0, 24),
            (typeof(AssetStore.OriginalOperation), 120, 152),
            (typeof(AssetStore.OriginalOpeningStorage), 72, 88),
        ];

        foreach (var (type, raw, capacity) in carriers)
        {
            Assert.That(GetSourceFieldCapacity(type), Is.EqualTo((raw, capacity)), type.Name);
        }

        var core = typeof(EbinReadLimits).Assembly;
        (string Name, long Capacity)[] profileCarriers = [
            ("FoundationProfileBinding", 88),
            ("FoundationCodecContext", 48),
            ("EbinField", 56),
            ("EbinRecord", 56),
        ];

        foreach (var (name, capacity) in profileCarriers)
        {
            var type = core.GetType($"TerraFX.{name}", throwOnError: true)
                ?? throw new InvalidOperationException("The profile source declaration is missing.");
            Assert.That(GetSourceFieldCapacity(type).Capacity, Is.EqualTo(capacity), name);
        }

        var anchorSlots = 0L;

        foreach (var field in typeof(AssetPackageBacking).GetFields(BindingFlags.Static | BindingFlags.NonPublic))
        {
            anchorSlots = checked(anchorSlots + ((GetManagedFieldWidth(field.FieldType) + 7L) & ~7L));
        }

        Assert.Multiple(() => {
            Assert.That(Unsafe.SizeOf<AssetPackageEntry>(), Is.EqualTo(32));
            Assert.That(anchorSlots, Is.EqualTo(32));
            Assert.That(AssetStore.OriginalProfileSourceBytes, Is.EqualTo(73_104));
            Assert.That(AssetStore.OriginalServiceSourceBytes, Is.EqualTo(66_752));
            Assert.That(AssetStore.GetOriginalKeepSourceBytes(131_072), Is.EqualTo(4_195_456));
            Assert.That(AssetStore.OriginalOpeningSourceBytes, Is.EqualTo(4_334_024));
        });
        TestContext.WriteLine("Declared source capacity includes field slots and selected data, not excluded framework/native allocation graphs.");
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
