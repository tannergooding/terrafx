// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX;
using TerraFX.Input;

namespace TerraFX.Input.Tests;

[TestFixture]
internal sealed class InputRouterTests
{
    private static InputActionMapV1 CreateMap(Guid mapId, long revision, params ActionDefinition[] actions)
    {
        var contexts = ImmutableArray.Create(new ContextDefinition(Guid.NewGuid(), 0));
        return new InputActionMapV1(1, mapId, revision, 1, [.. actions], contexts, []);
    }

    private static InputActionMapV1 CreateMapWithContext(Guid mapId, long revision, ContextDefinition context, params ActionDefinition[] actions)
        => new InputActionMapV1(1, mapId, revision, 1, [.. actions], [context], []);

    private static InputActionMapV1 CreateMapWithContexts(Guid mapId, long revision, ImmutableArray<ContextDefinition> contexts, ulong initialActiveContextMask)
        => new InputActionMapV1(1, mapId, revision, initialActiveContextMask, [], contexts, []);

    private static InputRouter CreateRouter(InputRecipientKind recipientKind = InputRecipientKind.HostLiveUI, uint recipientId = 1)
        => new InputRouter(recipientKind, recipientId, Environment.CurrentManagedThreadId, new HostEventSequenceLease());

    [Test]
    public void DisposeIsIdempotentAndClosesRouterOperations()
    {
        var router = CreateRouter();

        router.Dispose();
        router.Dispose();

        _ = Assert.Throws<ObjectDisposedException>(() => router.GetHostActionSnapshot());
    }

    [Test]
    public void EnforcesOwnerThreadForMapMutations()
    {
        var router = CreateRouter();
        var map = CreateMap(Guid.NewGuid(), 1);
        var task = Task.Run(() => router.RegisterMap(map));

        _ = Assert.ThrowsAsync<InvalidOperationException>(async () => await task.ConfigureAwait(false));
        router.Dispose();
    }

    [Test]
    public void RegistersMapsAndKeepsPreviouslyReturnedSnapshotsImmutable()
    {
        var router = CreateRouter();
        var firstMapId = Guid.NewGuid();
        var firstAction = new ActionDefinition(Guid.NewGuid(), "First", InputActionKind.Digital);
        var response = router.RegisterMap(CreateMap(firstMapId, 1, firstAction));

        Assert.That(response.Status, Is.EqualTo(InputMapStatus.Registered));
        Assert.That(response.HostSequence, Is.GreaterThan(0));
        Assert.That(response.PublishedRevision, Is.EqualTo(1));

        var firstSnapshot = router.GetHostActionSnapshot();
        Assert.That(firstSnapshot.Actions, Has.Length.EqualTo(1));
        Assert.That(firstSnapshot.Actions[0].MapId, Is.EqualTo(firstMapId));
        Assert.That(firstSnapshot.Actions[0].ActionId, Is.EqualTo(firstAction.ActionId));
        Assert.That(firstSnapshot.Actions[0].IsDownAtEnd, Is.False);
        Assert.That(firstSnapshot.Actions[0].Transitions, Is.Empty);

        var secondAction = new ActionDefinition(Guid.NewGuid(), "Second", InputActionKind.Axis1);
        var secondResponse = router.RegisterMap(CreateMap(Guid.NewGuid(), 1, secondAction));
        var secondSnapshot = router.GetHostActionSnapshot();

        Assert.That(secondResponse.Status, Is.EqualTo(InputMapStatus.Registered));
        Assert.That(secondSnapshot.Actions, Has.Length.EqualTo(2));
        Assert.That(firstSnapshot.Actions, Has.Length.EqualTo(1));
        router.Dispose();
    }

    [Test]
    public void RejectsInvalidDuplicateAndOverCapacityMapsWithoutSequence()
    {
        var router = CreateRouter();
        var mapId = Guid.NewGuid();
        var invalidMap = new InputActionMapV1(
            2,
            mapId,
            1,
            1,
            [],
            [new ContextDefinition(Guid.NewGuid(), 0)],
            []);

        var invalidResponse = router.RegisterMap(invalidMap);
        Assert.That(invalidResponse.Status, Is.EqualTo(InputMapStatus.Rejected));
        Assert.That(invalidResponse.HostSequence, Is.Null);
        Assert.That(invalidResponse.Rejection?.Code, Is.EqualTo("UnknownSchemaVersion"));

        var firstResponse = router.RegisterMap(CreateMap(mapId, 1));
        var duplicateResponse = router.RegisterMap(CreateMap(mapId, 2));
        Assert.That(duplicateResponse.Status, Is.EqualTo(InputMapStatus.Rejected));
        Assert.That(duplicateResponse.HostSequence, Is.Null);
        Assert.That(duplicateResponse.Rejection?.Code, Is.EqualTo("DuplicateId"));

        for (var index = 1; index < 16; index++)
        {
            Assert.That(router.RegisterMap(CreateMap(Guid.NewGuid(), 1)).Status, Is.EqualTo(InputMapStatus.Registered));
        }

        var fullResponse = router.RegisterMap(CreateMap(Guid.NewGuid(), 1));
        Assert.That(fullResponse.Status, Is.EqualTo(InputMapStatus.CapacityExceeded));
        Assert.That(fullResponse.HostSequence, Is.Null);
        Assert.That(firstResponse.HostSequence, Is.GreaterThan(0));
        router.Dispose();
    }

    [Test]
    public void RejectsMapThatWouldExceedRouterWideActiveContextCapacity()
    {
        var router = CreateRouter();

        for (var mapIndex = 0; mapIndex < 4; mapIndex++)
        {
            var contexts = ImmutableArray.CreateBuilder<ContextDefinition>(64);

            for (var bit = 0; bit < 64; bit++)
            {
                contexts.Add(new ContextDefinition(Guid.NewGuid(), checked((byte)bit)));
            }

            var map = CreateMapWithContexts(Guid.NewGuid(), 1, contexts.MoveToImmutable(), ulong.MaxValue);
            Assert.That(router.RegisterMap(map).Status, Is.EqualTo(InputMapStatus.Registered));
        }

        var overCapacityContexts = ImmutableArray.CreateBuilder<ContextDefinition>(64);

        for (var bit = 0; bit < 64; bit++)
        {
            overCapacityContexts.Add(new ContextDefinition(Guid.NewGuid(), checked((byte)bit)));
        }

        var overCapacityMap = CreateMapWithContexts(Guid.NewGuid(), 1, overCapacityContexts.MoveToImmutable(), ulong.MaxValue);
        var response = router.RegisterMap(overCapacityMap);

        Assert.That(response.Status, Is.EqualTo(InputMapStatus.CapacityExceeded));
        Assert.That(response.HostSequence, Is.Null);
        router.Dispose();
    }

    [Test]
    public void ReplacesOnlyTheExpectedMapRevision()
    {
        var router = CreateRouter();
        var mapId = Guid.NewGuid();
        var context = new ContextDefinition(Guid.NewGuid(), 0);
        var firstAction = new ActionDefinition(Guid.NewGuid(), "First", InputActionKind.Digital);
        var secondAction = new ActionDefinition(Guid.NewGuid(), "Second", InputActionKind.Axis1);
        var firstResponse = router.RegisterMap(CreateMapWithContext(mapId, 1, context, firstAction));
        var previousSnapshot = router.GetHostActionSnapshot();
        var replacement = CreateMapWithContext(mapId, 2, context, secondAction);

        var staleResponse = router.ReplaceMap(replacement, expectedRevision: 0);
        Assert.That(staleResponse.Status, Is.EqualTo(InputMapStatus.Rejected));
        Assert.That(staleResponse.HostSequence, Is.Null);
        Assert.That(router.GetHostActionSnapshot().Actions[0].MapRevision, Is.EqualTo(1));

        var replacementResponse = router.ReplaceMap(replacement, expectedRevision: 1);
        var replacementSnapshot = router.GetHostActionSnapshot();

        Assert.That(replacementResponse.Status, Is.EqualTo(InputMapStatus.Replaced));
        Assert.That(replacementResponse.HostSequence, Is.Not.Null);
        Assert.That(firstResponse.HostSequence, Is.Not.Null);
        Assert.That(
            replacementResponse.HostSequence.GetValueOrDefault(),
            Is.GreaterThan(firstResponse.HostSequence.GetValueOrDefault()));
        Assert.That(replacementResponse.PublishedRevision, Is.EqualTo(2));
        Assert.That(replacementSnapshot.Actions[0].ActionId, Is.EqualTo(secondAction.ActionId));
        Assert.That(replacementSnapshot.Actions[0].MapRevision, Is.EqualTo(2));
        Assert.That(previousSnapshot.Actions[0].ActionId, Is.EqualTo(firstAction.ActionId));
        Assert.That(previousSnapshot.Actions[0].MapRevision, Is.EqualTo(1));
        router.Dispose();
    }

    [Test]
    public void SetActiveContextMaskUsesHostSequenceAndBoundaryCapacity()
    {
        var router = CreateRouter(InputRecipientKind.SimulationTimeline, recipientId: 2);
        var mapId = Guid.NewGuid();
        var mapResponse = router.RegisterMap(CreateMap(mapId, 1));
        router.BeginHostControlBoundary(7);

        var response = router.SetActiveContextMask(mapId, 1, 0);

        Assert.That(response.Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(response.RecipientId, Is.EqualTo(2));
        Assert.That(response.HostSequence, Is.Not.Null);
        Assert.That(mapResponse.HostSequence, Is.Not.Null);
        Assert.That(response.HostSequence.GetValueOrDefault(), Is.GreaterThan(mapResponse.HostSequence.GetValueOrDefault()));
        Assert.That(response.AssignedTick, Is.EqualTo(7));

        var staleResponse = router.SetActiveContextMask(mapId, 2, 1);
        Assert.That(staleResponse.Status, Is.EqualTo(InputRoutingStatus.Rejected));
        Assert.That(staleResponse.HostSequence, Is.Null);

        for (var index = 0; index < 255; index++)
        {
            var mask = (index & 1) == 0 ? 1UL : 0UL;
            Assert.That(router.SetActiveContextMask(mapId, 1, mask).Status, Is.EqualTo(InputRoutingStatus.Applied));
        }

        var capacityResponse = router.SetActiveContextMask(mapId, 1, 0);
        Assert.That(capacityResponse.Status, Is.EqualTo(InputRoutingStatus.CapacityExceeded));
        Assert.That(capacityResponse.HostSequence, Is.Null);

        router.BeginHostControlBoundary(8);
        Assert.That(router.SetActiveContextMask(mapId, 1, 1).Status, Is.EqualTo(InputRoutingStatus.Applied));
        router.Dispose();
    }

    [Test]
    public void SimulationSnapshotsArePublishedForExactTicks()
    {
        var router = CreateRouter(InputRecipientKind.SimulationTimeline, recipientId: 3);
        var action = new ActionDefinition(Guid.NewGuid(), "Move", InputActionKind.Axis1);
        _ = router.RegisterMap(CreateMap(Guid.NewGuid(), 1, action));

        _ = Assert.Throws<InvalidOperationException>(() => router.GetSimulationActionSnapshot(0));
        _ = router.PublishSimulationActionSnapshot(0);

        var snapshot = router.GetSimulationActionSnapshot(0);
        Assert.That(snapshot.RecipientKind, Is.EqualTo(InputRecipientKind.SimulationTimeline));
        Assert.That(snapshot.RecipientId, Is.EqualTo(3));
        Assert.That(snapshot.AssignedTick, Is.Zero);
        Assert.That(snapshot.Actions, Has.Length.EqualTo(1));
        Assert.That(snapshot.Actions[0].Kind, Is.EqualTo(InputActionKind.Axis1));
        router.Dispose();
    }
}
