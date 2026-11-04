// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldPersistenceLiveIdentitySetTests
{
    [Test]
    public void RejectsDuplicateLiveIdentity()
    {
        var identity = Guid.NewGuid();
        var identities = new WorldPersistenceLiveIdentitySet(maximumCount: 1);
        identities.Add(identity, identityKind: 1, nameof(identity));

        _ = Assert.Throws<ArgumentException>(() => identities.Add(identity, identityKind: 2, nameof(identity)));
    }

    [Test]
    public void RejectsMembershipAboveConfiguredMaximum()
    {
        var identities = new WorldPersistenceLiveIdentitySet(maximumCount: 1);
        identities.Add(Guid.NewGuid(), identityKind: 1, nameof(identities));

        _ = Assert.Throws<ArgumentException>(() => identities.Add(Guid.NewGuid(), identityKind: 2, nameof(identities)));
    }

    [Test]
    public void RequiresExactIdentityKindAndCompleteMembership()
    {
        var entityId = Guid.NewGuid();
        var sceneInstanceId = Guid.NewGuid();
        var identities = new WorldPersistenceLiveIdentitySet(maximumCount: 2);
        identities.Add(entityId, identityKind: 1, nameof(entityId));
        identities.Add(sceneInstanceId, identityKind: 2, nameof(sceneInstanceId));

        _ = Assert.Throws<ArgumentException>(() => identities.MatchSceneInstance(entityId, nameof(entityId)));

        var validIdentities = new WorldPersistenceLiveIdentitySet(maximumCount: 2);
        validIdentities.Add(entityId, identityKind: 1, nameof(entityId));
        validIdentities.Add(sceneInstanceId, identityKind: 2, nameof(sceneInstanceId));
        validIdentities.MatchEntity(entityId, nameof(entityId));
        validIdentities.MatchSceneInstance(sceneInstanceId, nameof(sceneInstanceId));
        validIdentities.ClaimSceneEntity(entityId, nameof(entityId));
        _ = Assert.Throws<ArgumentException>(() => validIdentities.ClaimSceneEntity(entityId, nameof(entityId)));
        validIdentities.ThrowIfNotEmpty(nameof(validIdentities));
    }
}
