// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using NUnit.Framework;
using TerraFX.Assets;
using TerraFX.Numerics;
using TerraFX.Rendering;

namespace TerraFX.Runtime.Tests.Scenes;

[TestFixture]
internal sealed class BoundedStaticMeshAssetValidationTests
{
    private static long MeasureClassShell(Type type, int sampleCount)
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

        var allocatedBytes = (GC.GetAllocatedBytesForCurrentThread() - allocationStart) / sampleCount;
        GC.KeepAlive(measuredInstances);
        return allocatedBytes;
    }

    [Test]
    public void BoundedStaticMeshPreclaimCreatesDeliveryWithoutOpeningPackage()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Assert.Ignore("The bounded static-Mesh package profile is not available on this operating system.");
        }

        var packagePath = OperatingSystem.IsWindows()
            ? string.Concat(@"C:\bnd\", Guid.NewGuid().ToString("N"), ".epkg")
            : string.Concat("/tmp/bnd-", Guid.NewGuid().ToString("N"), ".epkg");
        var expectedDigest = new byte[32];
        var allocationStart = GC.GetAllocatedBytesForCurrentThread();

        var refusal = BoundedStaticMeshAssetValidation.TryPreclaim(
            packagePath,
            expectedDigest,
            536_870_912,
            268_435_456,
            268_435_456,
            4096,
            out var delivery,
            CancellationToken.None);

        var firstAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
        Assert.That(refusal, Is.EqualTo(BoundedStaticMeshAssetRefusal.None));
        var admittedDelivery = delivery
            ?? throw new InvalidOperationException("The successful bounded preclaim returned no delivery owner.");
        var firstReservedBytes = admittedDelivery.ReservedBytes;
        Assert.That(File.Exists(packagePath), Is.False);
        var issuerField = typeof(BoundedStaticMeshAssetDelivery).GetField("_issuer", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The bounded delivery lost its original issuer field.");
        var issuer = issuerField.GetValue(admittedDelivery)
            ?? throw new InvalidOperationException("The bounded delivery has no original issuer.");
        var backingField = issuer.GetType().GetField("_currentBacking", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The bounded Store lost its original backing field.");
        var backing = backingField.GetValue(issuer)
            ?? throw new InvalidOperationException("The bounded Store has no opening backing.");
        var portProperty = backing.GetType().GetProperty("BoundedNativePort", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The bounded backing has no native port owner.");
        var port = portProperty.GetValue(backing)
            ?? throw new InvalidOperationException("The bounded backing has no native port.");
        var initializationAttemptedProperty = port.GetType().GetProperty(
            "InitializationAttempted",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The bounded native port has no initialization state.");
        Assert.That(initializationAttemptedProperty.GetValue(port), Is.EqualTo(false));
        var nativeFileProperty = backing.GetType().GetProperty("BoundedNativeFile", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The bounded backing has no native file state.");
        var nativeFile = nativeFileProperty.GetValue(backing)
            ?? throw new InvalidOperationException("The bounded backing has no native file state.");
        var portOwnerField = nativeFile.GetType().GetField("Port", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The bounded file state has no port owner field.");
        Assert.That(portOwnerField.GetValue(nativeFile), Is.Null);
        var provisionField = backing.GetType().GetField("_boundedProvision", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The bounded backing has no original provision field.");
        var provision = provisionField.GetValue(backing)
            ?? throw new InvalidOperationException("The bounded backing has no original provision.");
        var profileBufferField = provision.GetType().GetField(
            "_packageProfileBuffer",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The bounded provision has no preclaimed profile buffer.");
        var profileBuffer = profileBufferField.GetValue(provision) as byte[]
            ?? throw new InvalidOperationException("The bounded provision has no profile buffer owner.");
        Assert.That(profileBuffer.Length, Is.EqualTo(65_536));
        var directoryBufferField = provision.GetType().GetField(
            "_packageDirectoryBuffer",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The bounded provision has no preclaimed directory buffer.");
        var directoryBuffer = directoryBufferField.GetValue(provision) as byte[]
            ?? throw new InvalidOperationException("The bounded provision has no directory buffer owner.");
        Assert.That(directoryBuffer.Length, Is.EqualTo(128));

        var firstRelease = admittedDelivery.ReleaseUnstartedPreclaim();
        Assert.That(firstRelease.OwnerReleased, Is.True);
        Assert.That(firstRelease.Refusal, Is.EqualTo(BoundedStaticMeshAssetRefusal.None));
        Assert.That(firstRelease.HasFailure, Is.False);
        Assert.That(firstRelease.RetainedSharedProvisionBytes, Is.AnyOf(0, 1_048_576));

        var secondAllocationStart = GC.GetAllocatedBytesForCurrentThread();
        var secondRefusal = BoundedStaticMeshAssetValidation.TryPreclaim(
            packagePath,
            expectedDigest,
            536_870_912,
            268_435_456,
            268_435_456,
            4096,
            out var secondDelivery,
            CancellationToken.None);
        var secondAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - secondAllocationStart;

        Assert.That(secondRefusal, Is.EqualTo(BoundedStaticMeshAssetRefusal.None));
        var admittedSecondDelivery = secondDelivery
            ?? throw new InvalidOperationException("The successful bounded preclaim returned no delivery owner.");
        var secondReservedBytes = admittedSecondDelivery.ReservedBytes;
        Assert.That(
            secondReservedBytes,
            Is.EqualTo(firstReservedBytes - firstRelease.RetainedSharedProvisionBytes));
        Assert.That(
            (firstAllocatedBytes <= firstReservedBytes)
                && (secondAllocatedBytes <= secondReservedBytes),
            Is.True,
            $"First preclaim allocated {firstAllocatedBytes} of {firstReservedBytes} reserved bytes; second allocated {secondAllocatedBytes} of {secondReservedBytes}.");
        TestContext.Progress.WriteLine(
            $"Bounded static-Mesh preclaim allocation: cold {firstAllocatedBytes}/{firstReservedBytes} bytes, warm {secondAllocatedBytes}/{secondReservedBytes} bytes.");
        var secondRelease = admittedSecondDelivery.ReleaseUnstartedPreclaim();
        Assert.That(secondRelease.OwnerReleased, Is.True);
        Assert.That(secondRelease.HasFailure, Is.False);
        Assert.That(secondRelease.RetainedSharedProvisionBytes, Is.Zero);

        var shortRefusal = BoundedStaticMeshAssetValidation.TryPreclaim(
            packagePath,
            expectedDigest,
            secondReservedBytes - 1,
            268_435_456,
            268_435_456,
            4096,
            out var shortDelivery,
            CancellationToken.None);

        Assert.That(shortRefusal, Is.EqualTo(BoundedStaticMeshAssetRefusal.Capacity));
        Assert.That(shortDelivery, Is.Null);
    }

    [Test]
    public void BoundedStaticMeshPreclaimRefusesAmbientSynchronizationContext()
    {
        var previousContext = SynchronizationContext.Current;

        try
        {
            SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
            var refusal = BoundedStaticMeshAssetValidation.TryPreclaim(
                OperatingSystem.IsWindows() ? @"C:\bnd\missing.epkg" : "/tmp/bnd-missing.epkg",
                new byte[32],
                536_870_912,
                268_435_456,
                268_435_456,
                4096,
                out var delivery,
                CancellationToken.None);

            Assert.That(refusal, Is.EqualTo(BoundedStaticMeshAssetRefusal.UnsupportedContext));
            Assert.That(delivery, Is.Null);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    [Test]
    public void BoundedStaticMeshPreclaimRefusesInsufficientBudget()
    {
        var refusal = BoundedStaticMeshAssetValidation.TryPreclaim(
            OperatingSystem.IsWindows() ? @"C:\bnd\missing.epkg" : "/tmp/bnd-missing.epkg",
            new byte[32],
            4096,
            268_435_456,
            268_435_456,
            4096,
            out var delivery,
            CancellationToken.None);

        Assert.That(refusal, Is.EqualTo(BoundedStaticMeshAssetRefusal.Capacity));
        Assert.That(delivery, Is.Null);
    }

    [Test]
    public void BoundedStaticMeshPreclaimRefusesSourcedCancellationToken()
    {
        using var cancellationSource = new CancellationTokenSource();
        var refusal = BoundedStaticMeshAssetValidation.TryPreclaim(
            OperatingSystem.IsWindows() ? @"C:\bnd\missing.epkg" : "/tmp/bnd-missing.epkg",
            new byte[32],
            536_870_912,
            268_435_456,
            268_435_456,
            4096,
            out var delivery,
            cancellationSource.Token);

        Assert.That(refusal, Is.EqualTo(BoundedStaticMeshAssetRefusal.UnsupportedToken));
        Assert.That(delivery, Is.Null);
    }

    [Test]
    public void BoundedStaticMeshPreclaimRefusesTraversalPath()
    {
        var refusal = BoundedStaticMeshAssetValidation.TryPreclaim(
            OperatingSystem.IsWindows() ? @"C:\bnd\..\missing.epkg" : "/tmp/bnd/../missing.epkg",
            new byte[32],
            536_870_912,
            268_435_456,
            268_435_456,
            4096,
            out var delivery,
            CancellationToken.None);

        Assert.That(refusal, Is.EqualTo(BoundedStaticMeshAssetRefusal.InvalidOptions));
        Assert.That(delivery, Is.Null);
    }

    [Test]
    public void BoundedStaticMeshPreclaimSupportsMaximumPackagePathLength()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Assert.Ignore("The bounded static-Mesh package profile is not available on this operating system.");
        }

        var root = OperatingSystem.IsWindows() ? @"C:\" : "/";
        var maximumPathLength = OperatingSystem.IsWindows() ? 259 : 4095;
        var suffix = ".epkg";
        var path = string.Concat(
            root,
            new string('a', maximumPathLength - root.Length - suffix.Length),
            suffix);
        Assert.That(path.Length, Is.EqualTo(maximumPathLength));

        var refusal = BoundedStaticMeshAssetValidation.TryPreclaim(
            path,
            new byte[32],
            536_870_912,
            268_435_456,
            268_435_456,
            4096,
            out var delivery,
            CancellationToken.None);

        Assert.That(refusal, Is.EqualTo(BoundedStaticMeshAssetRefusal.None));
        var admittedDelivery = delivery
            ?? throw new InvalidOperationException("The successful bounded preclaim returned no delivery owner.");
        Assert.That(admittedDelivery.ReservedBytes, Is.GreaterThan(0));
        Assert.That(admittedDelivery.ReleaseUnstartedPreclaim().OwnerReleased, Is.True);

        var overlongPath = string.Concat(
            root,
            new string('a', maximumPathLength - root.Length - suffix.Length + 1),
            suffix);
        var overlongRefusal = BoundedStaticMeshAssetValidation.TryPreclaim(
            overlongPath,
            new byte[32],
            536_870_912,
            268_435_456,
            268_435_456,
            4096,
            out var overlongDelivery,
            CancellationToken.None);

        Assert.That(overlongRefusal, Is.EqualTo(BoundedStaticMeshAssetRefusal.InvalidOptions));
        Assert.That(overlongDelivery, Is.Null);
    }

    [Test]
    public void TypedAssetBoundedOperationClaimIsBudgetedAndReturnedBeforeRelease()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Assert.Ignore("The bounded static-Mesh package profile is not available on this operating system.");
        }

        const long CpuAssetBudgetBytes = 536_870_912;
        const int MaximumDecodedAssetBytes = 268_435_456;
        const int MaximumConcurrentDecodeBytes = 268_435_456;
        const int MaximumLiveAssetLeases = 4096;
        const int MeshEnvelopeLength = 128;
        const int MaterialEnvelopeLength = 128;
        var operationBytes = BoundedStaticMeshAssetDecoder.GetMinimumKnownClaimBytes(
            MeshEnvelopeLength,
            MaterialEnvelopeLength);

        var packagePath = OperatingSystem.IsWindows()
            ? @"C:\bnd\bounded-operation.epkg"
            : "/tmp/bnd-bounded-operation.epkg";
        var expectedDigest = new byte[32];

        var warmRefusal = BoundedStaticMeshAssetValidation.TryPreclaim(
            packagePath,
            expectedDigest,
            CpuAssetBudgetBytes,
            MaximumDecodedAssetBytes,
            MaximumConcurrentDecodeBytes,
            MaximumLiveAssetLeases,
            out var warmDelivery,
            CancellationToken.None);
        Assert.That(warmRefusal, Is.EqualTo(BoundedStaticMeshAssetRefusal.None));
        var admittedWarmDelivery = warmDelivery
            ?? throw new InvalidOperationException("The successful bounded preclaim returned no delivery owner.");
        Assert.That(admittedWarmDelivery.ReleaseUnstartedPreclaim().OwnerReleased, Is.True);

        var baselineRefusal = BoundedStaticMeshAssetValidation.TryPreclaim(
            packagePath,
            expectedDigest,
            CpuAssetBudgetBytes,
            MaximumDecodedAssetBytes,
            MaximumConcurrentDecodeBytes,
            MaximumLiveAssetLeases,
            out var baselineDelivery,
            CancellationToken.None);
        Assert.That(baselineRefusal, Is.EqualTo(BoundedStaticMeshAssetRefusal.None));
        var admittedBaselineDelivery = baselineDelivery
            ?? throw new InvalidOperationException("The successful bounded preclaim returned no delivery owner.");
        var baselineReservedBytes = admittedBaselineDelivery.ReservedBytes;
        Assert.That(admittedBaselineDelivery.ReleaseUnstartedPreclaim().OwnerReleased, Is.True);

        var exactBudgetRefusal = BoundedStaticMeshAssetValidation.TryPreclaim(
            packagePath,
            expectedDigest,
            checked(baselineReservedBytes + operationBytes),
            MaximumDecodedAssetBytes,
            MaximumConcurrentDecodeBytes,
            MaximumLiveAssetLeases,
            out var exactBudgetDelivery,
            CancellationToken.None);
        Assert.That(exactBudgetRefusal, Is.EqualTo(BoundedStaticMeshAssetRefusal.None));
        var admittedExactBudgetDelivery = exactBudgetDelivery
            ?? throw new InvalidOperationException("The successful bounded preclaim returned no delivery owner.");
        Assert.That(admittedExactBudgetDelivery.ReservedBytes, Is.EqualTo(baselineReservedBytes));

        var creationRefusal = BoundedStaticMeshAssetDecoder.CreateClaimedCandidateForTests(
            admittedExactBudgetDelivery,
            new AssetId(1),
            new AssetId(1000),
            MeshEnvelopeLength,
            MaterialEnvelopeLength,
            out var creationFailure,
            out var unconsumedClaim,
            out var decoder);
        Assert.That(creationRefusal, Is.EqualTo(AssetBoundedRefusal.None));
        Assert.That(creationFailure.Kind, Is.EqualTo(AssetBoundedFailureKind.None));
        Assert.That(unconsumedClaim.IsActive, Is.False);
        Assert.That(admittedExactBudgetDelivery.ReservedBytes, Is.EqualTo(baselineReservedBytes + operationBytes));
        var duplicateClaimRefusal = admittedExactBudgetDelivery.TryClaimOperation(1, out var duplicateClaim);
        Assert.Multiple(() => {
            Assert.That(duplicateClaimRefusal, Is.EqualTo(AssetBoundedRefusal.Busy));
            Assert.That(duplicateClaim.IsActive, Is.False);
        });
        var blockedRelease = admittedExactBudgetDelivery.ReleaseUnstartedPreclaim();
        Assert.Multiple(() => {
            Assert.That(blockedRelease.OwnerReleased, Is.False);
            Assert.That(blockedRelease.Refusal, Is.EqualTo(BoundedStaticMeshAssetRefusal.Busy));
        });

        decoder.Dispose(out var returnedClaim);
        Assert.That(returnedClaim.IsActive, Is.True);
        decoder.Dispose();
        Assert.That(admittedExactBudgetDelivery.ReservedBytes, Is.EqualTo(baselineReservedBytes + operationBytes));
        returnedClaim.Dispose();
        Assert.That(admittedExactBudgetDelivery.ReservedBytes, Is.EqualTo(baselineReservedBytes));
        Assert.That(admittedExactBudgetDelivery.ReleaseUnstartedPreclaim().OwnerReleased, Is.True);

        var shortBudgetRefusal = BoundedStaticMeshAssetValidation.TryPreclaim(
            packagePath,
            expectedDigest,
            checked(baselineReservedBytes + operationBytes - 1),
            MaximumDecodedAssetBytes,
            MaximumConcurrentDecodeBytes,
            MaximumLiveAssetLeases,
            out var shortBudgetDelivery,
            CancellationToken.None);
        Assert.That(shortBudgetRefusal, Is.EqualTo(BoundedStaticMeshAssetRefusal.None));
        var admittedShortBudgetDelivery = shortBudgetDelivery
            ?? throw new InvalidOperationException("The successful bounded preclaim returned no delivery owner.");
        var shortAllocationStart = GC.GetAllocatedBytesForCurrentThread();
        var shortCreateRefusal = BoundedStaticMeshAssetDecoder.CreateClaimedCandidateForTests(
            admittedShortBudgetDelivery,
            new AssetId(1),
            new AssetId(1000),
            MeshEnvelopeLength,
            MaterialEnvelopeLength,
            out var shortCreateFailure,
            out var shortUnconsumedClaim,
            out var shortDecoder);
        var shortAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - shortAllocationStart;
        Assert.Multiple(() => {
            Assert.That(shortCreateRefusal, Is.EqualTo(AssetBoundedRefusal.Capacity));
            Assert.That(shortCreateFailure.Kind, Is.EqualTo(AssetBoundedFailureKind.None));
            Assert.That(shortUnconsumedClaim.IsActive, Is.False);
            Assert.That(shortAllocatedBytes, Is.Zero);
            Assert.That(admittedShortBudgetDelivery.ReservedBytes, Is.EqualTo(baselineReservedBytes));
        });
        shortDecoder.Dispose();
        Assert.That(admittedShortBudgetDelivery.ReleaseUnstartedPreclaim().OwnerReleased, Is.True);
    }

    [TestCase(64, 4096, 268_435_456, AssetBoundedRefusal.Capacity)]
    [TestCase(268_435_456, 1, 268_435_456, AssetBoundedRefusal.Capacity)]
    [TestCase(268_435_456, 4096, 64, AssetBoundedRefusal.InvalidOptions)]
    public void TypedAssetBoundedOperationEnforcesConfiguredLimits(
        int maximumConcurrentDecodeBytes,
        int maximumLiveAssetLeases,
        int maximumDecodedAssetBytes,
        AssetBoundedRefusal expectedRefusal)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Assert.Ignore("The bounded static-Mesh package profile is not available on this operating system.");
        }

        const int EnvelopeLength = 128;
        const long CpuAssetBudgetBytes = 536_870_912;
        const int MeshEnvelopeLength = EnvelopeLength;
        const int MaterialEnvelopeLength = EnvelopeLength;

        var packagePath = OperatingSystem.IsWindows()
            ? @"C:\bnd\bounded-decode-limits.epkg"
            : "/tmp/bnd-bounded-decode-limits.epkg";
        var preclaimRefusal = BoundedStaticMeshAssetValidation.TryPreclaim(
            packagePath,
            new byte[32],
            CpuAssetBudgetBytes,
            maximumDecodedAssetBytes,
            maximumConcurrentDecodeBytes,
            maximumLiveAssetLeases,
            out var delivery,
            CancellationToken.None);
        Assert.That(preclaimRefusal, Is.EqualTo(BoundedStaticMeshAssetRefusal.None));
        var admittedDelivery = delivery
            ?? throw new InvalidOperationException("The successful bounded preclaim returned no delivery owner.");
        var baselineReservedBytes = admittedDelivery.ReservedBytes;
        var unconsumedClaim = default(AssetBoundedOperationClaim);
        var decoder = default(BoundedStaticMeshAssetDecoder);
        var decoderClaim = default(AssetBoundedOperationClaim);
        BoundedStaticMeshAssetPreclaimRelease release;

        try
        {
            var allocationStart = GC.GetAllocatedBytesForCurrentThread();
            var refusal = BoundedStaticMeshAssetDecoder.CreateClaimedCandidateForTests(
                admittedDelivery,
                new AssetId(1),
                new AssetId(1000),
                MeshEnvelopeLength,
                MaterialEnvelopeLength,
                out var failure,
                out unconsumedClaim,
                out decoder);
            var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
            var requiredBytes = BoundedStaticMeshAssetDecoder.GetMinimumKnownClaimBytes(
                MeshEnvelopeLength,
                MaterialEnvelopeLength);

            Assert.Multiple(() => {
                Assert.That(refusal, Is.EqualTo(expectedRefusal));
                Assert.That(failure.Kind, Is.EqualTo(AssetBoundedFailureKind.None));
                Assert.That(unconsumedClaim.IsActive, Is.True);
                Assert.That(allocatedBytes, Is.Zero);
                Assert.That(admittedDelivery.ReservedBytes, Is.EqualTo(baselineReservedBytes + requiredBytes));
            });

            unconsumedClaim.Dispose();
            Assert.That(admittedDelivery.ReservedBytes, Is.EqualTo(baselineReservedBytes));
        }
        finally
        {
            try
            {
                if (unconsumedClaim.IsActive)
                {
                    unconsumedClaim.Dispose();
                }

                decoder.Dispose(out decoderClaim);

                if (decoderClaim.IsActive)
                {
                    decoderClaim.Dispose();
                }

                decoder.Dispose();
            }
            finally
            {
                release = admittedDelivery.ReleaseUnstartedPreclaim();
            }
        }

        Assert.That(release.OwnerReleased, Is.True);
    }

    [Test]
    public void TypedAssetClassShellAllocationsMatchSelectedRuntime()
    {
        const int SampleCount = 64;

        if (IntPtr.Size != 8)
        {
            Assert.Ignore("The bounded static-Mesh allocation profile is x64-only.");
        }

        var meshShellBytes = MeasureClassShell(typeof(RenderMeshAsset), SampleCount);
        var materialShellBytes = MeasureClassShell(typeof(RenderMaterialAsset), SampleCount);
        Assert.Multiple(() => {
            Assert.That(meshShellBytes, Is.EqualTo(176));
            Assert.That(materialShellBytes, Is.EqualTo(240));
        });
        TestContext.WriteLine(
            $"RenderMeshAsset={meshShellBytes}; RenderMaterialAsset={materialShellBytes}; "
            + $"RuntimeMvid={typeof(RenderMeshAsset).Module.ModuleVersionId};");
    }

    [Test]
    public void TypedAssetResultArrayElementStridesMatchSelectedRuntime()
    {
        if (IntPtr.Size != 8)
        {
            Assert.Ignore("The bounded static-Mesh allocation profile is x64-only.");
        }

        Assert.Multiple(() => {
            Assert.That(Unsafe.SizeOf<Vector2>(), Is.EqualTo(8));
            Assert.That(Unsafe.SizeOf<Vector3>(), Is.EqualTo(12));
            Assert.That(Unsafe.SizeOf<Vector4>(), Is.EqualTo(16));
            Assert.That(Unsafe.SizeOf<RenderMeshGroup>(), Is.EqualTo(12));
            Assert.That(Unsafe.SizeOf<AssetId>(), Is.EqualTo(4));
        });
    }
}
