// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using NUnit.Framework;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests;

[TestFixture]
internal sealed class EntityQueryDescriptionTests
{
    private const int MaximumSchemaCount = 256;
    private static readonly Guid s_requiredSchemaId = CreateGuid(1);

    private static ImmutableArray<SystemResourceAccess> CreateAccesses(int count)
    {
        var builder = ImmutableArray.CreateBuilder<SystemResourceAccess>(count);

        for (var index = 0; index < count; index++)
        {
            builder.Add(new SystemResourceAccess(SystemResourceKind.Component, CreateGuid(index + 100), SystemAccessMode.Read));
        }

        return builder.MoveToImmutable();
    }

    private static Guid CreateGuid(int value) => new(value, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1);

    private static EntityQueryDescription CreateQuery(
        ImmutableArray<Guid> requiredSchemaIds = default,
        ImmutableArray<Guid> excludedSchemaIds = default,
        ImmutableArray<SystemResourceAccess> access = default)
    {
        return new EntityQueryDescription(
            requiredSchemaIds.IsDefault ? [] : requiredSchemaIds,
            excludedSchemaIds.IsDefault ? [] : excludedSchemaIds,
            access.IsDefault ? [] : access);
    }

    private static ImmutableArray<Guid> CreateSchemaIds(int count)
    {
        var builder = ImmutableArray.CreateBuilder<Guid>(count);

        for (var index = 0; index < count; index++)
        {
            builder.Add(CreateGuid(index + 100));
        }

        return builder.MoveToImmutable();
    }

    [Test]
    public void AccessCountAdmitsMaximumAndRejectsFirstExcess()
    {
        var maximum = new EntityQueryDescription(
            CreateSchemaIds(MaximumSchemaCount),
            [],
            CreateAccesses(MaximumSchemaCount));
        Assert.That(maximum.Access.Length, Is.EqualTo(MaximumSchemaCount));

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new EntityQueryDescription(
            maximum.RequiredSchemaIds,
            [],
            CreateAccesses(MaximumSchemaCount + 1)));

        Assert.That(exception!.ParamName, Is.EqualTo("access"));
    }

    [Test]
    public void ConstructorCopiesQueryArrays()
    {
        var required = new[] { s_requiredSchemaId };
        var excluded = new[] { CreateGuid(2) };
        var access = new[] { new SystemResourceAccess(SystemResourceKind.Component, s_requiredSchemaId, SystemAccessMode.Read) };
        var query = new EntityQueryDescription(
            ImmutableCollectionsMarshal.AsImmutableArray(required),
            ImmutableCollectionsMarshal.AsImmutableArray(excluded),
            ImmutableCollectionsMarshal.AsImmutableArray(access));

        required[0] = Guid.Empty;
        excluded[0] = Guid.Empty;
        access[0] = default;

        Assert.That(query.RequiredSchemaIds[0], Is.EqualTo(s_requiredSchemaId));
        Assert.That(query.ExcludedSchemaIds[0], Is.EqualTo(CreateGuid(2)));
        Assert.That(query.Access[0].IsValid, Is.True);
    }

    [Test]
    public void ConstructorDerivesACommonRangeFromDeclaredAccesses()
    {
        var range = new SystemPartitionRange(2, 2);
        var access = ImmutableArray.Create(
            new SystemResourceAccess(SystemResourceKind.Component, s_requiredSchemaId, SystemAccessMode.Read, range),
            new SystemResourceAccess(SystemResourceKind.Tag, CreateGuid(3), SystemAccessMode.Read, range));
        var query = new EntityQueryDescription(
            [s_requiredSchemaId, CreateGuid(3)],
            [],
            access);

        Assert.That(query.Range, Is.EqualTo(range));
    }

    [Test]
    public void EmptyQueryIsValidAndHasNoRange()
    {
        var query = CreateQuery();

        Assert.That(query.RequiredSchemaIds, Is.Empty);
        Assert.That(query.ExcludedSchemaIds, Is.Empty);
        Assert.That(query.Access, Is.Empty);
        Assert.That(query.Range, Is.Null);
    }

    [Test]
    public void ExplicitRangeAdmitsFilterOnlyQuery()
    {
        var range = new SystemPartitionRange(3, 2);
        var query = new EntityQueryDescription(
            [s_requiredSchemaId],
            [],
            [],
            range);

        Assert.That(query.Range, Is.EqualTo(range));
        Assert.That(query.Access, Is.Empty);
    }

    [Test]
    public void ExplicitRangeRequiresAccessesToDeclareTheSameRange()
    {
        var access = ImmutableArray.Create(
            new SystemResourceAccess(SystemResourceKind.Component, s_requiredSchemaId, SystemAccessMode.Read));

        var exception = Assert.Throws<ArgumentException>(() => new EntityQueryDescription(
            [s_requiredSchemaId],
            [],
            access,
            new SystemPartitionRange(0, 2)));

        Assert.That(exception!.ParamName, Is.EqualTo("access"));
    }

    [Test]
    public void QueryAccessMustBeRequiredAndComponentOrTag()
    {
        var unrequired = new SystemResourceAccess(SystemResourceKind.Component, CreateGuid(4), SystemAccessMode.Read);
        var exception = Assert.Throws<ArgumentException>(() => CreateQuery(access: [unrequired]));

        Assert.That(exception!.ParamName, Is.EqualTo("access"));
        _ = Assert.Throws<ArgumentException>(() => new EntityQueryDescription(
            [s_requiredSchemaId],
            [],
            [new SystemResourceAccess(SystemResourceKind.Resource, s_requiredSchemaId, SystemAccessMode.Read)]));
    }

    [Test]
    public void QueryRejectsDefaultArrays()
    {
        Assert.That(
            Assert.Throws<ArgumentException>(() => new EntityQueryDescription(
                default,
                [],
                []))!.ParamName,
            Is.EqualTo("requiredSchemaIds"));
        Assert.That(
            Assert.Throws<ArgumentException>(() => new EntityQueryDescription(
                [],
                default,
                []))!.ParamName,
            Is.EqualTo("excludedSchemaIds"));
        Assert.That(
            Assert.Throws<ArgumentException>(() => new EntityQueryDescription(
                [],
                [],
                default))!.ParamName,
            Is.EqualTo("access"));
    }

    [Test]
    public void QueryRejectsDefaultExplicitRange()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new EntityQueryDescription(
            [],
            [],
            [],
            default));

        Assert.That(exception!.ParamName, Is.EqualTo("range"));
    }

    [Test]
    public void QueryRejectsDuplicateAccessIdentity()
    {
        var access = ImmutableArray.Create(
            new SystemResourceAccess(SystemResourceKind.Component, s_requiredSchemaId, SystemAccessMode.Read),
            new SystemResourceAccess(SystemResourceKind.Component, s_requiredSchemaId, SystemAccessMode.Write));
        var exception = Assert.Throws<ArgumentException>(() => CreateQuery(
            requiredSchemaIds: [s_requiredSchemaId],
            access: access));

        Assert.That(exception!.ParamName, Is.EqualTo("access"));
    }

    [Test]
    public void QueryRejectsDuplicateAndOverlappingSchemaSets()
    {
        _ = Assert.Throws<ArgumentException>(() => CreateQuery(requiredSchemaIds: [s_requiredSchemaId, s_requiredSchemaId]));
        _ = Assert.Throws<ArgumentException>(() => CreateQuery(excludedSchemaIds: [s_requiredSchemaId, s_requiredSchemaId]));
        _ = Assert.Throws<ArgumentException>(() => CreateQuery(
            requiredSchemaIds: [s_requiredSchemaId],
            excludedSchemaIds: [s_requiredSchemaId]));
    }

    [Test]
    public void QueryRejectsEmptySchemaIdentity()
    {
        var exception = Assert.Throws<ArgumentException>(() => CreateQuery(requiredSchemaIds: [Guid.Empty]));

        Assert.That(exception!.ParamName, Is.EqualTo("requiredSchemaIds"));
    }

    [Test]
    public void QueryRejectsMismatchedRangeAmongAccesses()
    {
        var access = ImmutableArray.Create(
            new SystemResourceAccess(SystemResourceKind.Component, s_requiredSchemaId, SystemAccessMode.Read),
            new SystemResourceAccess(SystemResourceKind.Tag, CreateGuid(3), SystemAccessMode.Read, new SystemPartitionRange(0, 2)));
        var exception = Assert.Throws<ArgumentException>(() => new EntityQueryDescription(
            [s_requiredSchemaId, CreateGuid(3)],
            [],
            access));

        Assert.That(exception!.ParamName, Is.EqualTo("access"));
    }

    [Test]
    public void QuerySchemaCountAdmitsMaximumAndRejectsFirstExcess()
    {
        var maximum = CreateSchemaIds(MaximumSchemaCount);
        var excess = CreateSchemaIds(MaximumSchemaCount + 1);

        Assert.That(CreateQuery(requiredSchemaIds: maximum).RequiredSchemaIds.Length, Is.EqualTo(MaximumSchemaCount));

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => CreateQuery(excludedSchemaIds: excess));

        Assert.That(exception!.ParamName, Is.EqualTo("excludedSchemaIds"));
    }
}
