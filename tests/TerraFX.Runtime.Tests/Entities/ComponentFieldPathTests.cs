// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Linq;
using NUnit.Framework;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class ComponentFieldPathTests
{
    [Test]
    public void ComponentFieldPathRequiresInitializedValidBoundedSegments()
    {
        var path = new ComponentFieldPath([
            ComponentPropertySegment.FieldTag(1),
            ComponentPropertySegment.FixedArrayIndex(0),
        ]);

        Assert.That(path.Segments, Has.Length.EqualTo(2));
        Assert.That(path.Segments[0].Kind, Is.EqualTo(ComponentPropertySegmentKind.FieldTag));
        Assert.That(path.Segments[0].Value, Is.EqualTo(1));
        Assert.That(path.Segments[1].Kind, Is.EqualTo(ComponentPropertySegmentKind.FixedArrayIndex));
        Assert.That(path.Segments[1].Value, Is.Zero);
        Assert.That(() => new ComponentFieldPath(default), Throws.ArgumentException);
        Assert.That(() => new ComponentFieldPath([]), Throws.ArgumentException);
        Assert.That(() => new ComponentFieldPath([default]), Throws.ArgumentException);
        Assert.That(() => new ComponentFieldPath([ComponentPropertySegment.FixedArrayIndex(0)]), Throws.ArgumentException);
        Assert.That(() => ComponentPropertySegment.FieldTag(0), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => ComponentPropertySegment.FixedArrayIndex(-1), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => new ComponentFieldPath([.. Enumerable.Repeat(ComponentPropertySegment.FieldTag(1), 65)]), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void ComponentLayoutRequiresAnIdentityAndSupportedSize()
    {
        var layoutId = new Guid("00112233-4455-4677-8899-aabbccddeeff");
        var layout = new ComponentLayout(layoutId, 32756);

        Assert.That(layout.LayoutId, Is.EqualTo(layoutId));
        Assert.That(layout.LogicalBytes, Is.EqualTo(32756));
        Assert.That(() => new ComponentLayout(Guid.Empty, 1), Throws.ArgumentException);
        Assert.That(() => new ComponentLayout(layoutId, 0), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => new ComponentLayout(layoutId, 32757), Throws.TypeOf<ArgumentOutOfRangeException>());
    }
}
