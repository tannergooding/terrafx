// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Linq;
using NUnit.Framework;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class ComponentStorageDeclarationTests
{
    [Test]
    public void ComponentStorageDeclarationRequiresExactGaplessCoverage()
    {
        var valuePath = new ComponentStoragePath([ComponentStorageSegment.Member("value")]);
        var inlinePath = new ComponentStoragePath([
            ComponentStorageSegment.Member("items"),
            ComponentStorageSegment.InlineIndex(0),
        ]);
        var value = new ComponentStorageLeaf(
            valuePath,
            ComponentStorageKind.Value,
            0,
            4,
            0,
            0,
            ComponentStoragePurpose.SchemaValue);
        var inline = new ComponentStorageLeaf(
            inlinePath,
            ComponentStorageKind.InlineArray,
            8,
            4,
            2,
            2,
            ComponentStoragePurpose.DeclaredRuntimeOnly);
        var padding = new ComponentPaddingInterval(4, 4);
        var declaration = new ComponentStorageDeclaration(12, [value, inline], [padding]);

        Assert.That(declaration.LogicalBytes, Is.EqualTo(12));
        Assert.That(declaration.Leaves, Has.Length.EqualTo(2));
        Assert.That(declaration.Padding, Is.EqualTo([padding]));
        Assert.That(() => new ComponentStorageDeclaration(12, [value, new ComponentStorageLeaf(
            inlinePath,
            ComponentStorageKind.Value,
            9,
            3,
            0,
            0,
            ComponentStoragePurpose.SchemaValue)], []), Throws.ArgumentException);
        Assert.That(() => new ComponentStorageDeclaration(12, [
            value,
            new ComponentStorageLeaf(
                new ComponentStoragePath([ComponentStorageSegment.Member("other")]),
                ComponentStorageKind.Value,
                2,
                4,
                0,
                0,
                ComponentStoragePurpose.SchemaValue),
        ], []), Throws.ArgumentException);
        Assert.That(() => new ComponentStorageDeclaration(12, [value, value], [padding]), Throws.ArgumentException);
        Assert.That(() => new ComponentStorageDeclaration(12, [inline, value], [padding]), Throws.ArgumentException);
        Assert.That(() => new ComponentStorageDeclaration(12, [value, inline], [default]), Throws.ArgumentException);
    }

    [Test]
    public void ComponentStorageLeafValidatesInlineShapeAndPurpose()
    {
        var path = new ComponentStoragePath([ComponentStorageSegment.Member("values")]);

        Assert.That(() => new ComponentStorageLeaf(path, ComponentStorageKind.None, 0, 4, 0, 0, ComponentStoragePurpose.SchemaValue), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => new ComponentStorageLeaf(path, ComponentStorageKind.Value, 0, 4, 1, 4, ComponentStoragePurpose.SchemaValue), Throws.ArgumentException);
        Assert.That(() => new ComponentStorageLeaf(path, ComponentStorageKind.InlineArray, 0, 5, 2, 2, ComponentStoragePurpose.SchemaValue), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => new ComponentStorageLeaf(path, ComponentStorageKind.Value, 0, 4, 0, 0, ComponentStoragePurpose.None), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void ComponentStoragePathsAreImmutableAndCompareByExactSegments()
    {
        var path = new ComponentStoragePath([
            ComponentStorageSegment.Member("Values"),
            ComponentStorageSegment.InlineIndex(0),
        ]);
        var equalPath = new ComponentStoragePath([
            ComponentStorageSegment.Member("Values"),
            ComponentStorageSegment.InlineIndex(0),
        ]);
        var differentPath = new ComponentStoragePath([
            ComponentStorageSegment.Member("values"),
            ComponentStorageSegment.InlineIndex(0),
        ]);

        Assert.That(path.Equals(equalPath), Is.True);
        Assert.That(path.Equals(differentPath), Is.False);
        Assert.That(() => new ComponentStoragePath(default), Throws.ArgumentException);
        Assert.That(() => new ComponentStoragePath([]), Throws.ArgumentException);
        Assert.That(() => new ComponentStoragePath([default]), Throws.ArgumentException);
        Assert.That(() => new ComponentStoragePath([.. Enumerable.Repeat(ComponentStorageSegment.Member("field"), 65)]), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void ComponentStorageSegmentsPreserveSourceNamesAndBoundTheirEncoding()
    {
        var member = ComponentStorageSegment.Member("value");
        var index = ComponentStorageSegment.InlineIndex(0);

        Assert.That(member.MemberName, Is.EqualTo("value"));
        Assert.That(member.Kind, Is.EqualTo(ComponentStorageSegmentKind.Member));
        Assert.That(index.Index, Is.Zero);
        Assert.That(index.Kind, Is.EqualTo(ComponentStorageSegmentKind.InlineIndex));
        Assert.That(ComponentStorageSegment.Member("Field"), Is.Not.EqualTo(ComponentStorageSegment.Member("field")));
        Assert.That(() => ComponentStorageSegment.Member(string.Empty), Throws.ArgumentException);
        Assert.That(() => ComponentStorageSegment.Member(new string('a', 129)), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => ComponentStorageSegment.Member(string.Concat(Enumerable.Repeat("\U0001F600", 64))), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => ComponentStorageSegment.Member(new string('\uD83D', 64)), Throws.ArgumentException);
        Assert.That(() => ComponentStorageSegment.InlineIndex(-1), Throws.TypeOf<ArgumentOutOfRangeException>());
    }
}
