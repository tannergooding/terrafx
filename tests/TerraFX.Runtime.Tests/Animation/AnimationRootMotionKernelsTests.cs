// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX.Animation;
using TerraFX.Assets;
using TerraFX.Numerics;

namespace TerraFX.Runtime.Tests.Animation;

internal sealed class AnimationRootMotionKernelsTests
{
    private const long DurationTicks = 1_000;

    private static void AssertMatrixNear(Matrix4x4 actual, Matrix4x4 expected)
    {
        Assert.Multiple(() => {
            for (var row = 0; row < 4; row++)
            {
                for (var column = 0; column < 4; column++)
                {
                    Assert.That(actual[row, column], Is.EqualTo(expected[row, column]).Within(1.0e-5f));
                }
            }
        });
    }

    private static AnimationClipAsset CreateClip(Guid rootJointId, byte loopMode)
        => new(
            new AssetId(2),
            DurationTicks,
            loopMode,
            skeletonAssetId: new AssetId(1),
            sceneAssetId: null,
            rootMotion: new AnimationClipAsset.RootMotionInfo(rootJointId, 0),
            bounds: null,
            boundsQualification: 0,
            boundsMargin: 0.0f,
            [
                new AnimationClipAsset.AnimationTrack(
                    rootJointId,
                    1,
                    0,
                    1,
                    [
                        new AnimationClipAsset.Vector3Key(0, Vector3.Zero, null, null),
                        new AnimationClipAsset.Vector3Key(DurationTicks, Vector3.UnitX, null, null),
                    ],
                    [],
                    []),
                new AnimationClipAsset.AnimationTrack(
                    rootJointId,
                    1,
                    1,
                    1,
                    [],
                    [
                        new AnimationClipAsset.QuaternionKey(0, Quaternion.Identity, null, null),
                        new AnimationClipAsset.QuaternionKey(DurationTicks, CreateQuarterTurn(), null, null),
                    ],
                    []),
            ],
            [],
            [0x01]);

    private static Quaternion CreateQuarterTurn()
        => new(0.0f, 0.0f, MathF.Sqrt(0.5f), MathF.Sqrt(0.5f));

    private static Matrix4x4 CreateRootTransform(Vector3 translation, Quaternion rotation)
        => Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(translation);

    private static SkeletonAsset CreateSkeleton(Guid rootJointId)
        => new(
            new AssetId(1),
            rootJointId,
            [
                new SkeletonJoint(
                    rootJointId,
                    ParentIndex: -1,
                    BindTranslation: Vector3.Zero,
                    BindRotation: Quaternion.Identity,
                    BindScale: Vector3.One,
                    InverseBind: Matrix4x4.Identity),
            ],
            [0x01]);

    private static Matrix4x4 Invert(Matrix4x4 matrix)
        => Matrix4x4.TryInvert(matrix, out var inverse)
            ? inverse
            : throw new InvalidOperationException("The test root transform is not invertible.");

    [Test]
    public void ClampRootMotionUsesOnlyTheBoundedClipInterval()
    {
        var rootJointId = Guid.NewGuid();
        var clip = CreateClip(rootJointId, loopMode: 0);
        var skeleton = CreateSkeleton(rootJointId);

        var actual = AnimationRootMotionKernels.CalculateDelta(
            clip,
            skeleton,
            firstLoopOrdinal: 0,
            firstTimeTicks: 0,
            secondLoopOrdinal: 0,
            secondTimeTicks: DurationTicks);

        AssertMatrixNear(actual, CreateRootTransform(Vector3.UnitX, CreateQuarterTurn()));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => AnimationRootMotionKernels.CalculateDelta(
            clip,
            skeleton,
            firstLoopOrdinal: 1,
            firstTimeTicks: 0,
            secondLoopOrdinal: 1,
            secondTimeTicks: DurationTicks));
    }

    [Test]
    public void LongLoopRangesUseCheckedLogarithmicCycleComposition()
    {
        var rootJointId = Guid.NewGuid();
        var clip = CreateClip(rootJointId, loopMode: 1);
        var skeleton = CreateSkeleton(rootJointId);
        var oneCycle = AnimationRootMotionKernels.CalculateDelta(
            clip,
            skeleton,
            firstLoopOrdinal: 0,
            firstTimeTicks: 0,
            secondLoopOrdinal: 1,
            secondTimeTicks: 0);

        var threeCycles = AnimationRootMotionKernels.CalculateDelta(
            clip,
            skeleton,
            firstLoopOrdinal: 0,
            firstTimeTicks: 0,
            secondLoopOrdinal: 3,
            secondTimeTicks: 0);
        var billionCycles = AnimationRootMotionKernels.CalculateDelta(
            clip,
            skeleton,
            firstLoopOrdinal: 0,
            firstTimeTicks: 0,
            secondLoopOrdinal: 1_000_000_000,
            secondTimeTicks: 0);
        var trillionCycles = AnimationRootMotionKernels.CalculateDelta(
            clip,
            skeleton,
            firstLoopOrdinal: 0,
            firstTimeTicks: 0,
            secondLoopOrdinal: 1_000_000_000_000,
            secondTimeTicks: 0);
        var reversedBillionCycles = AnimationRootMotionKernels.CalculateDelta(
            clip,
            skeleton,
            firstLoopOrdinal: 1_000_000_000,
            firstTimeTicks: 0,
            secondLoopOrdinal: 0,
            secondTimeTicks: 0);
        var reversedTrillionCycles = AnimationRootMotionKernels.CalculateDelta(
            clip,
            skeleton,
            firstLoopOrdinal: 1_000_000_000_000,
            firstTimeTicks: 0,
            secondLoopOrdinal: 0,
            secondTimeTicks: 0);

        AssertMatrixNear(threeCycles, oneCycle * oneCycle * oneCycle);
        AssertMatrixNear(billionCycles, Matrix4x4.Identity);
        Assert.That(Matrix4x4.TryInvert(billionCycles, out var billionInverse), Is.True);
        AssertMatrixNear(billionCycles * billionInverse, Matrix4x4.Identity);
        AssertMatrixNear(billionInverse * billionCycles, Matrix4x4.Identity);
        AssertMatrixNear(reversedBillionCycles, Matrix4x4.Identity);
        Assert.That(Matrix4x4.TryInvert(reversedBillionCycles, out var reversedBillionInverse), Is.True);
        AssertMatrixNear(reversedBillionCycles * reversedBillionInverse, Matrix4x4.Identity);
        AssertMatrixNear(reversedBillionInverse * reversedBillionCycles, Matrix4x4.Identity);
        AssertMatrixNear(trillionCycles, Matrix4x4.Identity);
        Assert.That(Matrix4x4.TryInvert(trillionCycles, out _), Is.True);
        AssertMatrixNear(reversedTrillionCycles, Matrix4x4.Identity);
    }

    [Test]
    public void LoopCrossingComposesSegmentsInChronologicalOrderAndReverseInverts()
    {
        var rootJointId = Guid.NewGuid();
        var clip = CreateClip(rootJointId, loopMode: 1);
        var skeleton = CreateSkeleton(rootJointId);
        var halfwayRotation = Quaternion.Slerp(
            Quaternion.Identity,
            CreateQuarterTurn(),
            0.5f);
        var halfwayTransform = CreateRootTransform(new Vector3(0.5f, 0.0f, 0.0f), halfwayRotation);
        var endTransform = CreateRootTransform(Vector3.UnitX, CreateQuarterTurn());
        var firstSegment = endTransform * Invert(halfwayTransform);
        var secondSegment = halfwayTransform;
        var expected = secondSegment * firstSegment;

        var actual = AnimationRootMotionKernels.CalculateDelta(
            clip,
            skeleton,
            firstLoopOrdinal: 0,
            firstTimeTicks: DurationTicks / 2,
            secondLoopOrdinal: 1,
            secondTimeTicks: DurationTicks / 2);
        var reverse = AnimationRootMotionKernels.CalculateDelta(
            clip,
            skeleton,
            firstLoopOrdinal: 1,
            firstTimeTicks: DurationTicks / 2,
            secondLoopOrdinal: 0,
            secondTimeTicks: DurationTicks / 2);

        AssertMatrixNear(actual, expected);
        Assert.That(actual, Is.Not.EqualTo(firstSegment * secondSegment));
        AssertMatrixNear(reverse, Invert(expected));
    }
}
