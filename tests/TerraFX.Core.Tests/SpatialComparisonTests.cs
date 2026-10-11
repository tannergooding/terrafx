// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using NUnit.Framework;

namespace TerraFX.Core.Tests;

[TestFixture]
internal sealed class SpatialComparisonTests
{
    [Test]
    public void ChunkIndexComparisonUsesExactIntegerMagnitudeAndSupportsSignedExtremes()
    {
        var near = new ChunkIndex2(int.MaxValue, 0);
        var far = new ChunkIndex2(int.MinValue, 0);

        Assert.That(GeometryPublicConsumer.Compare(far, near).Comparison, Is.GreaterThan(0));
        Assert.That(GeometryPublicConsumer.Compare(new ChunkIndex2(4, 3), new ChunkIndex2(3, 4)).Comparison, Is.LessThan(0));
        Assert.That(GeometryPublicConsumer.Compare(new ChunkIndex2(-3, 4), new ChunkIndex2(-4, 3)).Comparison, Is.LessThan(0));
        Assert.That(GeometryPublicConsumer.Compare(new ChunkIndex2(-4, -3), new ChunkIndex2(-3, -4)).Comparison, Is.LessThan(0));
        Assert.That(GeometryPublicConsumer.Compare(new ChunkIndex2(3, -4), new ChunkIndex2(4, -3)).Comparison, Is.LessThan(0));
        Assert.That(new ChunkIndex3(int.MinValue, int.MinValue, int.MinValue).CompareTo(new ChunkIndex3(int.MaxValue, int.MaxValue, int.MaxValue)), Is.GreaterThan(0));

        var orthants = new[]
        {
            new ChunkIndex2(1, 1),
            new ChunkIndex2(-1, 1),
            new ChunkIndex2(1, -1),
            new ChunkIndex2(-1, -1),
        };

        ComparisonTestUtilities.AssertOrderingLaws(orthants);

        for (var index = 0; index < orthants.Length - 1; index++)
        {
            Assert.That(orthants[index].CompareTo(orthants[index + 1]), Is.GreaterThan(0));
        }

        var orthants3 =
            new[]
            {
                new ChunkIndex3(1, 1, 1),
                new ChunkIndex3(-1, 1, 1),
                new ChunkIndex3(1, -1, 1),
                new ChunkIndex3(-1, -1, 1),
                new ChunkIndex3(1, 1, -1),
                new ChunkIndex3(-1, 1, -1),
                new ChunkIndex3(1, -1, -1),
                new ChunkIndex3(-1, -1, -1),
            };

        ComparisonTestUtilities.AssertOrderingLaws(orthants3);

        for (var index = 0; index < orthants3.Length - 1; index++)
        {
            Assert.That(orthants3[index].CompareTo(orthants3[index + 1]), Is.GreaterThan(0));
        }

        Assert.That(new ChunkIndex3(0, 4, 3).CompareTo(new ChunkIndex3(0, 3, 4)), Is.LessThan(0));
    }
}
