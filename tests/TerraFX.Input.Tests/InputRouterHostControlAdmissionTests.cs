// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX;
using TerraFX.Input;

namespace TerraFX.Input.Tests;

[TestFixture]
internal sealed class InputRouterHostControlAdmissionTests
{
    [Test]
    public void CapacityPreflightAccountsForEveryRouteChangeCommand()
    {
        var sequenceLease = new HostEventSequenceLease();
        var router = new InputRouter(
            InputRecipientKind.SimulationTimeline,
            91,
            Environment.CurrentManagedThreadId,
            sequenceLease);

        for (var index = 0; index < 255; index++)
        {
            var receipt = router.CancelInputState(
                InputCancellationReason.DeviceRemoved,
                InputClass.Gamepad,
                playerSlot: 2,
                hostTimestamp: 0,
                hostSequence: sequenceLease.Allocate(),
                discardedInputCount: out _);
            Assert.That(receipt.Status, Is.EqualTo(InputRoutingStatus.Applied));
        }

        var nextSequence = sequenceLease.Allocate();
        var routeChangeAdmission = router.ValidateHostControlBoundaryAdmission(0, nextSequence, commandCount: 2);
        var singleCommandAdmission = router.ValidateHostControlBoundaryAdmission(0, nextSequence);

        Assert.Multiple(() => {
            Assert.That(routeChangeAdmission.Status, Is.EqualTo(InputRoutingStatus.CapacityExceeded));
            Assert.That(routeChangeAdmission.Rejection?.Code, Is.EqualTo("CapacityExceeded"));
            Assert.That(singleCommandAdmission.Status, Is.EqualTo(InputRoutingStatus.Applied));
            Assert.That(singleCommandAdmission.HostSequence, Is.EqualTo(nextSequence));
        });

        _ = router.DisposeAndGetTransitions();
    }

    [Test]
    public void LatchesAControlOverflowForTheRecipientBoundary()
    {
        var sequenceLease = new HostEventSequenceLease();
        var router = new InputRouter(
            InputRecipientKind.SimulationTimeline,
            92,
            Environment.CurrentManagedThreadId,
            sequenceLease);
        var hostSequence = sequenceLease.Allocate();

        router.LatchPlatformInputOverflow(hostTimestamp: 7, hostSequence);
        var overflow = router.ServiceHostControlBoundary();

        Assert.Multiple(() => {
            Assert.That(overflow, Is.Not.Null);
            Assert.That(overflow!.FirstUnacceptedHostSequence, Is.EqualTo(hostSequence));
            Assert.That(overflow.LastUnacceptedHostSequence, Is.EqualTo(hostSequence));
        });

        _ = router.DisposeAndGetTransitions();
    }
}
