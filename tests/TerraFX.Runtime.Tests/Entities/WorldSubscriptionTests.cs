// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldSubscriptionTests
{
    private static World CreateWorld()
    {
        var registration = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();
        return World.Create(new WorldOptions(registration) {
            CpuWorkerCount = 0,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
    }

    private static WorldSubscription Subscribe(World world, Action<CommittedWorldPublication> callback)
    {
        if (!world.TrySubscribe(callback, out var subscription) || (subscription is null))
        {
            throw new InvalidOperationException("The test subscription was not admitted.");
        }

        return subscription;
    }

    private static (bool Accepted, WorldSubscription? Subscription) TrySubscribe(
        World world,
        Action<CommittedWorldPublication> callback)
    {
        var accepted = world.TrySubscribe(callback, out var subscription);
        return (accepted, subscription);
    }

    [Test]
    public void CallbackSubscriptionChangesAffectOnlyLaterCapturedPublications()
    {
        using var world = CreateWorld();
        using var subscriptions = new SubscriptionScope();
        WorldSubscription? secondSubscription = null;
        WorldSubscription? thirdSubscription = null;
        var firstCount = 0;
        var secondCount = 0;
        var thirdCount = 0;

        var firstSubscription = subscriptions.Add(Subscribe(world, _ => {
            firstCount++;

            if (firstCount == 1)
            {
                secondSubscription!.Dispose();
                thirdSubscription = subscriptions.Add(Subscribe(world, _ => thirdCount++));
            }
        }));
        secondSubscription = subscriptions.Add(Subscribe(world, _ => secondCount++));

        var firstReceipt = world.Step();
        var secondReceipt = world.Step();

        Assert.That(firstCount, Is.EqualTo(2));
        Assert.That(secondCount, Is.EqualTo(1));
        Assert.That(thirdCount, Is.EqualTo(1));
        Assert.That(firstSubscription.SubscriptionId, Is.EqualTo(1));
        Assert.That(thirdSubscription!.SubscriptionId, Is.EqualTo(3));
        Assert.That(firstReceipt.DeliveryReport.Results.Select(static result => result.SubscriptionId), Is.EqualTo([1, 2]));
        Assert.That(secondReceipt.DeliveryReport.Results.Select(static result => result.SubscriptionId), Is.EqualTo([1, 3]));
    }

    [Test]
    public void CapacityRefusalCoalescesWithoutConsumingIdsAndSnapshotSurvivesDisposal()
    {
        using var world = CreateWorld();
        using var subscriptionScope = new SubscriptionScope();
        var subscriptions = new WorldSubscription[64];

        for (var index = 0; index < subscriptions.Length; index++)
        {
            subscriptions[index] = subscriptionScope.Add(Subscribe(world, static _ => { }));
            Assert.That(subscriptions[index].SubscriptionId, Is.EqualTo(index + 1));
        }

        Assert.That(
            () => world.TrySubscribe(null!, out _),
            Throws.ArgumentNullException);

        for (var index = 0; index < 3; index++)
        {
            var (accepted, refused) = TrySubscribe(world, static _ => { });
            Assert.That(accepted, Is.False);
            Assert.That(refused, Is.Null);
        }

        var diagnosticSnapshot = world.Diagnostics.GetSnapshot(
            new DiagnosticQuery(16, DiagnosticSeverity.Warning, world.WorldGuid, null));
        Assert.That(diagnosticSnapshot.Records, Has.Length.EqualTo(1));
        var warning = diagnosticSnapshot.Records[0];
        Assert.That(warning.Code, Is.EqualTo("WorldSubscriptionCapacityExceeded"));
        Assert.That(warning.Operation, Is.EqualTo("TerraFX.Entities.World.TrySubscribe"));
        Assert.That(warning.Tick, Is.Null);
        Assert.That(warning.WorldGuid, Is.EqualTo(world.WorldGuid));
        Assert.That(warning.Context, Has.Length.EqualTo(3));
        Assert.That(warning.Context[0], Is.EqualTo(new DiagnosticContextEntry("ActiveSubscriptions", "64")));
        Assert.That(warning.Context[1], Is.EqualTo(new DiagnosticContextEntry("MaximumSubscriptions", "64")));
        Assert.That(warning.Context[2], Is.EqualTo(new DiagnosticContextEntry("OccurrenceCount", "3")));
        Assert.That(diagnosticSnapshot.CoalescedCount, Is.EqualTo(2));
        Assert.That(diagnosticSnapshot.DroppedCount, Is.Zero);
        Assert.That(
            world.Diagnostics.GetSnapshot(new DiagnosticQuery(16, DiagnosticSeverity.Trace, world.WorldGuid, 0)).Records,
            Is.Empty);

        subscriptions[0].Dispose();
        var nextSubscription = subscriptionScope.Add(Subscribe(world, static _ => { }));
        Assert.That(nextSubscription.SubscriptionId, Is.EqualTo(65));
        world.Dispose();

        Assert.That(diagnosticSnapshot.Records[0], Is.SameAs(warning));
    }

    [Test]
    public void SubscriberFailureIsRetainedAndLaterCallbacksStillRun()
    {
        using var world = CreateWorld();
        using var subscriptions = new SubscriptionScope();
        var invocationOrder = new List<int>();
        var expected = new InvalidOperationException("subscriber failure");

        _ = subscriptions.Add(Subscribe(world, _ => {
            invocationOrder.Add(1);
            throw expected;
        }));
        _ = subscriptions.Add(Subscribe(world, _ => invocationOrder.Add(2)));

        var receipt = world.Step();
        var report = receipt.DeliveryReport;

        Assert.That(invocationOrder, Is.EqualTo([1, 2]));
        Assert.That(report.Results, Has.Length.EqualTo(2));
        Assert.That(report.Results[0].Status, Is.EqualTo(SubscriberDeliveryStatus.Failed));
        var failure = report.Results[0].Failure
            ?? throw new InvalidOperationException("A failed delivery did not retain its failure record.");
        Assert.That(failure.OriginalException, Is.SameAs(expected));
        Assert.That(failure.Diagnostic.Code, Is.EqualTo("SubscriberDeliveryFailed"));
        Assert.That(failure.Diagnostic.Context, Has.Length.EqualTo(1));
        Assert.That(
            failure.Diagnostic.Context[0],
            Is.EqualTo(new DiagnosticContextEntry("TerraFX.Entities.SubscriberThrowStack.v1", "U:Unqualified")));
        Assert.That(report.Results[1].Status, Is.EqualTo(SubscriberDeliveryStatus.Delivered));
        Assert.That(world.LastFault, Is.Null);

        _ = world.Step();
        world.Dispose();

        Assert.That(report.Publication.PublicationSequence, Is.EqualTo(1));
        Assert.That(failure.OriginalException, Is.SameAs(expected));
    }

    private sealed class SubscriptionScope : IDisposable
    {
        private readonly List<WorldSubscription> _subscriptions = [];

        public void Dispose()
        {
            foreach (var subscription in _subscriptions)
            {
                subscription.Dispose();
            }
        }

        internal WorldSubscription Add(WorldSubscription subscription)
        {
            _subscriptions.Add(subscription);
            return subscription;
        }
    }
}
