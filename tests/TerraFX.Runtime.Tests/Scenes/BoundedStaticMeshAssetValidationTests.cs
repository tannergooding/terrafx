// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using TerraFX.Rendering;

namespace TerraFX.Runtime.Tests.Scenes;

[TestFixture]
internal sealed class BoundedStaticMeshAssetValidationTests
{
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
        Assert.That(firstRelease.RetainedSharedProvisionBytes, Is.EqualTo(1_048_576));

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
}
