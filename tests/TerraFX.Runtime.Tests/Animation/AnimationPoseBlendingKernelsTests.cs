// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;
using NUnit.Framework;
using TerraFX.Animation;
using TerraFX.Numerics;

namespace TerraFX.Runtime.Tests.Animation;

internal sealed class AnimationPoseBlendingKernelsTests
{
    [Test]
    public void AdditivePositionQuaternionAndScaleUseTheirDeclaredComposition()
    {
        var quarterTurn = new Quaternion(0.0f, 0.0f, MathF.Sqrt(0.5f), MathF.Sqrt(0.5f));
        var position = AnimationClipSamplingKernels.ApplyAdditivePosition(
            new Vector3(1.0f, 1.0f, 1.0f),
            new Vector3(4.0f, 2.0f, 0.0f),
            new Vector3(2.0f, 1.0f, 1.0f),
            weight: 0.5f);
        var rotation = AnimationClipSamplingKernels.ApplyAdditiveQuaternion(
            Quaternion.Identity,
            quarterTurn,
            Quaternion.Identity,
            weight: 0.5f);
        var scale = AnimationClipSamplingKernels.ApplyAdditiveScale(
            new Vector3(-2.0f, 2.0f, 2.0f),
            new Vector3(4.0f, 2.0f, 4.0f),
            new Vector3(2.0f, 2.0f, 2.0f),
            weight: 0.5f);

        Assert.Multiple(() => {
            Assert.That(position, Is.EqualTo(new Vector3(2.0f, 1.5f, 0.5f)));
            Assert.That(rotation.Z, Is.EqualTo(MathF.Sin(float.Pi / 8.0f)).Within(1.0e-6f));
            Assert.That(rotation.W, Is.EqualTo(MathF.Cos(float.Pi / 8.0f)).Within(1.0e-6f));
            Assert.That(scale, Is.EqualTo(new Vector3(-3.0f, 2.0f, 3.0f)));
        });
    }

    [Test]
    public void AdditiveScaleRequiresPositiveReferencesAndAllBlendWeightsAreBounded()
    {
        var negativeScale = AnimationClipSamplingKernels.BlendOverrideScale(
            [new Vector3(-2.0f, 1.0f, 1.0f), new Vector3(-4.0f, 1.0f, 1.0f)],
            [0.5f, 0.5f],
            Vector3.One);

        Assert.That(negativeScale, Is.EqualTo(new Vector3(-3.0f, 1.0f, 1.0f)));

        _ = Assert.Throws<InvalidDataException>(() => AnimationClipSamplingKernels.BlendOverrideScale(
            [Vector3.One, new Vector3(-1.0f, -1.0f, -1.0f)],
            [0.5f, 0.5f],
            Vector3.One));
        _ = Assert.Throws<InvalidDataException>(() => AnimationClipSamplingKernels.ApplyAdditiveScale(
            Vector3.One,
            new Vector3(-2.0f, 1.0f, 1.0f),
            Vector3.One,
            weight: 0.5f));
        _ = Assert.Throws<InvalidDataException>(() => AnimationClipSamplingKernels.BlendOverrideVector3(
            [Vector3.UnitX],
            [1.01f],
            Vector3.Zero));
        _ = Assert.Throws<ArgumentException>(() => AnimationClipSamplingKernels.BlendOverrideQuaternion(
            [Quaternion.Identity],
            [],
            Quaternion.Identity));
    }

    [Test]
    public void OverrideBlendsNormalizeWeightsInStableShortestArcOrder()
    {
        var vector = AnimationClipSamplingKernels.BlendOverrideVector3(
            [Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ],
            [0.25f, 0.75f, 0.0f],
            new Vector3(2.0f, 3.0f, 4.0f));
        var noMotionVector = AnimationClipSamplingKernels.BlendOverrideVector3(
            [Vector3.UnitX],
            [0.0f],
            new Vector3(2.0f, 3.0f, 4.0f));
        var halfAngle = MathF.Sqrt(0.5f);
        var quaternion = AnimationClipSamplingKernels.BlendOverrideQuaternion(
            [
                Quaternion.Identity,
                new Quaternion(0.0f, 0.0f, -halfAngle, -halfAngle),
            ],
            [0.5f, 0.5f],
            Quaternion.Identity);

        Assert.Multiple(() => {
            Assert.That(vector, Is.EqualTo(new Vector3(0.25f, 0.75f, 0.0f)));
            Assert.That(noMotionVector, Is.EqualTo(new Vector3(2.0f, 3.0f, 4.0f)));
            Assert.That(quaternion.Z, Is.EqualTo(MathF.Sin(float.Pi / 8.0f)).Within(1.0e-6f));
            Assert.That(quaternion.W, Is.EqualTo(MathF.Cos(float.Pi / 8.0f)).Within(1.0e-6f));
        });
    }
}
