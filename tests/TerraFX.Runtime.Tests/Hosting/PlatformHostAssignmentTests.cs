// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX;
using TerraFX.Input;
using TerraFX.Platform;

namespace TerraFX.Runtime.Tests.Hosting;

[TestFixture]
internal sealed class PlatformHostAssignmentTests
{
    [Test]
    public async Task AssignDeviceAsyncValidatesHostIdentityAndDelegatesTheCommandAsync()
    {
        var ownerId = OwnerIdAllocator.Allocate();
        var device = new InputDeviceHandle(ownerId, 2, 1);
        var expectedReceipt = new InputRoutingReceipt(InputRoutingStatus.Applied, 17, 41, 5, rejection: null);
        using var cancellationSource = new CancellationTokenSource();
        var expectedCancellationToken = cancellationSource.Token;
        var observedDevice = default(InputDeviceHandle);
        var observedRecipientId = 0u;
        var observedPlayerSlot = (ushort)0;
        var observedCancellationToken = CancellationToken.None;
        var platform = new PlatformHost(
            ownerId,
            PlatformBackend.Headless,
            ClipboardServicePolicy.NotRequested,
            assignDeviceAsync: (assignedDevice, recipientId, playerSlot, cancellationToken) => {
                observedDevice = assignedDevice;
                observedRecipientId = recipientId;
                observedPlayerSlot = playerSlot;
                observedCancellationToken = cancellationToken;
                return Task.FromResult(expectedReceipt);
            });

        try
        {
            var receipt = await platform.AssignDeviceAsync(device, 17, 5, expectedCancellationToken).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(receipt, Is.SameAs(expectedReceipt));
                Assert.That(observedDevice, Is.EqualTo(device));
                Assert.That(observedRecipientId, Is.EqualTo(17));
                Assert.That(observedPlayerSlot, Is.EqualTo(5));
                Assert.That(observedCancellationToken, Is.EqualTo(expectedCancellationToken));
            });

            _ = Assert.Throws<ArgumentException>(() => _ = platform.AssignDeviceAsync(default, 17, 5));
            _ = Assert.Throws<ArgumentException>(() => _ = platform.AssignDeviceAsync(
                new InputDeviceHandle(OwnerIdAllocator.Allocate(), 2, 1),
                17,
                5));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => _ = platform.AssignDeviceAsync(device, 0, 5));
        }
        finally
        {
            platform.DisposeFromOwner();
        }
    }
}
