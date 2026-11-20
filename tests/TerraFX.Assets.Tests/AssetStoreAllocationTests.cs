// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
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
}
