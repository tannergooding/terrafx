// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX;
using TerraFX.Numerics;
using TerraFX.Rendering;

namespace TerraFX.Runtime.Tests.Rendering;

[TestFixture]
internal sealed class CosmeticParticleKernelsTests
{
    [Test]
    public void ExistingParticlesExpireBeforeIntegrationAndSurvivorsAdvance()
    {
        var settings = SpatialSettings.Default;
        var position = new SpatialPosition3(default, Vector3.Zero);
        var velocity = new Vector3(2, 0, 0);
        var lifeSteps = CosmeticParticleKernels.GetLifeSteps(10_000_000, 2);

        var survives = CosmeticParticleKernels.TryAdvanceParticle(
            position: position,
            velocity: velocity,
            ageSteps: 0,
            lifeSteps: lifeSteps,
            tickRate: 2,
            settings: settings,
            out var firstPosition,
            out var firstAge);
        var expires = CosmeticParticleKernels.TryAdvanceParticle(
            position: firstPosition,
            velocity: velocity,
            ageSteps: firstAge,
            lifeSteps: lifeSteps,
            tickRate: 2,
            settings: settings,
            out var expiredPosition,
            out var expiredAge);
        var oneStepExpires = CosmeticParticleKernels.TryAdvanceParticle(
            position: position,
            velocity: velocity,
            ageSteps: 0,
            lifeSteps: 1,
            tickRate: 2,
            settings: settings,
            out var oneStepPosition,
            out var oneStepAge);

        Assert.Multiple(() => {
            Assert.That(survives, Is.True);
            Assert.That(firstAge, Is.EqualTo(1));
            Assert.That(firstPosition.Offset, Is.EqualTo(new Vector3(1, 0, 0)));
            Assert.That(expires, Is.False);
            Assert.That(expiredAge, Is.EqualTo(2));
            Assert.That(expiredPosition, Is.EqualTo(firstPosition));
            Assert.That(oneStepExpires, Is.False);
            Assert.That(oneStepAge, Is.EqualTo(1));
            Assert.That(oneStepPosition, Is.EqualTo(position));
        });
    }

    [Test]
    public void LifeStepsUseCheckedCeiling()
    {
        Assert.Multiple(() => {
            Assert.That(CosmeticParticleKernels.GetLifeSteps(10_000_000, 2), Is.EqualTo(2));
            Assert.That(CosmeticParticleKernels.GetLifeSteps(5_000_000, 2), Is.EqualTo(1));
            Assert.That(CosmeticParticleKernels.GetLifeSteps(1, 60), Is.EqualTo(1));
            Assert.That(CosmeticParticleKernels.GetLifeSteps(long.MaxValue, 1000), Is.EqualTo(922_337_203_685_478));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => CosmeticParticleKernels.GetLifeSteps(0, 60));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => CosmeticParticleKernels.GetLifeSteps(1, 0));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => CosmeticParticleKernels.GetLifeSteps(1, 1001));
        });
    }

    [Test]
    public void NormalizedAgeUsesExactNearestEvenRounding()
    {
        Assert.Multiple(() => {
            Assert.That(CosmeticParticleKernels.GetNormalizedAge(0, 3), Is.Zero);
            Assert.That(CosmeticParticleKernels.GetNormalizedAge(1, 2), Is.EqualTo(0.5f));
            Assert.That(CosmeticParticleKernels.GetNormalizedAge(2, 2), Is.EqualTo(1.0f));
            Assert.That(CosmeticParticleKernels.GetNormalizedAge(16_777_217, 33_554_432), Is.EqualTo(0.5f));
            Assert.That(
                CosmeticParticleKernels.GetNormalizedAge(16_777_219, 33_554_432),
                Is.EqualTo(float.BitIncrement(float.BitIncrement(0.5f))));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => CosmeticParticleKernels.GetNormalizedAge(1, 0));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => CosmeticParticleKernels.GetNormalizedAge(3, 2));
        });
    }

    [Test]
    public void ParticleCurvesUseExactKeysAndLinearInterpolation()
    {
        CosmeticParticleEmitterAsset.ColorKey[] colorKeys = [
            new(0.0f, Vector4.One),
            new(1.0f, new Vector4(1, 0, 0, 0)),
        ];
        CosmeticParticleEmitterAsset.SizeKey[] sizeKeys = [
            new(0.0f, Vector2.One),
            new(1.0f, new Vector2(4, 4)),
        ];

        Assert.Multiple(() => {
            Assert.That(CosmeticParticleKernels.SampleColor(colorKeys, 0.0f), Is.EqualTo(Vector4.One));
            Assert.That(CosmeticParticleKernels.SampleColor(colorKeys, 0.5f), Is.EqualTo(new Vector4(1, 0.5f, 0.5f, 0.5f)));
            Assert.That(CosmeticParticleKernels.SampleColor(colorKeys, 1.0f), Is.EqualTo(new Vector4(1, 0, 0, 0)));
            Assert.That(CosmeticParticleKernels.SampleSize(sizeKeys, 0.5f), Is.EqualTo(new Vector2(2.5f, 2.5f)));
        });

        _ = Assert.Throws<ArgumentOutOfRangeException>(() => CosmeticParticleKernels.SampleColor(colorKeys, -0.01f));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => CosmeticParticleKernels.SampleSize(sizeKeys, 1.01f));
    }

    [Test]
    public void Pcg32MatchesTheCommittedSequenceAndStreamBounds()
    {
        var random = new CosmeticParticleKernels.Pcg32(42, 54);
        uint[] expected = [0xA15C02B7, 0x7B47F409, 0xBA1D3330, 0x83D2F293, 0xBFA4784B, 0xCBED606E];

        foreach (var word in expected)
        {
            Assert.That(random.NextUInt(), Is.EqualTo(word));
        }

        var maximumStream = new CosmeticParticleKernels.Pcg32(0, long.MaxValue);
        Assert.That(maximumStream.Increment, Is.EqualTo(ulong.MaxValue));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new CosmeticParticleKernels.Pcg32(0, (ulong)long.MaxValue + 1));
        _ = Assert.Throws<InvalidOperationException>(() => default(CosmeticParticleKernels.Pcg32).NextUInt());
    }

    [Test]
    public void VelocitySamplingConsumesThreeWordsInAxisOrder()
    {
        var random = new CosmeticParticleKernels.Pcg32(42, 54);
        var velocity = CosmeticParticleKernels.SampleVelocity(
            ref random,
            Vector3.Zero,
            new Vector3(1, 1, 0));

        Assert.Multiple(() => {
            Assert.That(velocity.X, Is.EqualTo((float)(0xA15C02 / 16_777_216.0)));
            Assert.That(velocity.Y, Is.EqualTo((float)(0x7B47F4 / 16_777_216.0)));
            Assert.That(velocity.Z, Is.Zero);
            Assert.That(random.NextUInt(), Is.EqualTo(0x83D2F293));
        });
    }

    [Test]
    public void VelocityValidationPrecedesRandomStateMutation()
    {
        var random = new CosmeticParticleKernels.Pcg32(42, 54);

        _ = Assert.Throws<ArgumentException>(() =>
            CosmeticParticleKernels.SampleVelocity(ref random, Vector3.One, Vector3.Zero));

        Assert.That(random.NextUInt(), Is.EqualTo(0xA15C02B7));
    }
}
