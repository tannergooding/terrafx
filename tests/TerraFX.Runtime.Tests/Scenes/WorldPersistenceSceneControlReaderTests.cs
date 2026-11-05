// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX.Entities;

namespace TerraFX.Scenes.Tests;

[TestFixture]
internal sealed class WorldPersistenceSceneControlReaderTests
{
    [Test]
    public void RejectsEmptyOrReservedRequestedSceneIdentities()
    {
        var targetWorldGuid = new Guid("11111111-2222-4333-8444-555555555555");
        var targetIdentity = StableIdentityLedger.CreateAutomaticIdentity(
            targetWorldGuid,
            IdentityKind.SceneInstance,
            ordinal: 1);
        var sourceNamespace = new Guid("bbbbbbbb-cccc-4ddd-8eee-ffffffffffff");
        var identity = new WorldPersistenceIdentityScratchEntry(
            targetIdentity,
            kind: 2,
            assignmentOrdinal: 1,
            branchIndex: 0,
            sourceNamespace,
            origin: (byte)IdentityOrigin.ForkImported);

        _ = Assert.Throws<ArgumentException>(() => WorldPersistenceSceneControlReader.ValidateActivationIdentity(
            targetIdentity,
            Guid.Empty,
            Guid.Empty,
            targetWorldGuid,
            identity,
            nameof(identity)));

        var reservedIdentity = new Guid("80000000-0000-8000-8000-000000000001");
        _ = Assert.Throws<ArgumentException>(() => WorldPersistenceSceneControlReader.ValidateActivationIdentity(
            targetIdentity,
            reservedIdentity,
            reservedIdentity,
            targetWorldGuid,
            identity,
            nameof(identity)));
    }

    [Test]
    public void ValidatesForkImportedAutomaticSceneIdentityAgainstSourceAndTargetNamespaces()
    {
        var targetWorldGuid = new Guid("11111111-2222-4333-8444-555555555555");
        var sourceNamespace = new Guid("bbbbbbbb-cccc-4ddd-8eee-ffffffffffff");
        var targetIdentity = new Guid("20aeced5-8d92-8423-8f43-4eba3673e317");
        var originalIdentity = new Guid("a4852d11-abef-8919-8df7-dcc083c07d61");
        var identity = new WorldPersistenceIdentityScratchEntry(
            targetIdentity,
            kind: 2,
            assignmentOrdinal: 1,
            branchIndex: 0,
            sourceNamespace,
            origin: (byte)IdentityOrigin.ForkImported);

        Assert.That(
            StableIdentityLedger.CreateAutomaticIdentity(targetWorldGuid, IdentityKind.SceneInstance, ordinal: 1),
            Is.EqualTo(targetIdentity));
        Assert.That(
            StableIdentityLedger.CreateAutomaticIdentity(sourceNamespace, IdentityKind.SceneInstance, ordinal: 1),
            Is.EqualTo(originalIdentity));
        Assert.DoesNotThrow(() => WorldPersistenceSceneControlReader.ValidateActivationIdentity(
            targetIdentity,
            originalIdentity,
            requestedSceneInstanceId: null,
            targetWorldGuid,
            identity,
            nameof(identity)));
        _ = Assert.Throws<ArgumentException>(() => WorldPersistenceSceneControlReader.ValidateActivationIdentity(
            targetIdentity,
            Guid.NewGuid(),
            requestedSceneInstanceId: null,
            targetWorldGuid,
            identity,
            nameof(identity)));
    }

    [Test]
    public void ValidatesForkImportedExplicitSceneIdentityAsAnImmutableRequest()
    {
        var targetWorldGuid = new Guid("11111111-2222-4333-8444-555555555555");
        var sourceNamespace = new Guid("bbbbbbbb-cccc-4ddd-8eee-ffffffffffff");
        var targetIdentity = new Guid("20aeced5-8d92-8423-8f43-4eba3673e317");
        var requestedIdentity = new Guid("40000000-0000-4000-8000-000000000001");
        var identity = new WorldPersistenceIdentityScratchEntry(
            targetIdentity,
            kind: 2,
            assignmentOrdinal: 1,
            branchIndex: 0,
            sourceNamespace,
            origin: (byte)IdentityOrigin.ForkImported);

        Assert.DoesNotThrow(() => WorldPersistenceSceneControlReader.ValidateActivationIdentity(
            targetIdentity,
            requestedIdentity,
            requestedIdentity,
            targetWorldGuid,
            identity,
            nameof(identity)));
        _ = Assert.Throws<ArgumentException>(() => WorldPersistenceSceneControlReader.ValidateActivationIdentity(
            targetIdentity,
            Guid.NewGuid(),
            requestedIdentity,
            targetWorldGuid,
            identity,
            nameof(identity)));
    }
}
