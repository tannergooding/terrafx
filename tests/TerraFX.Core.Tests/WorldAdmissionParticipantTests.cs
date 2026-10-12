// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading.Tasks;
using NUnit.Framework;

namespace TerraFX.Core.Tests;

[TestFixture]
internal sealed class WorldAdmissionParticipantTests
{
    [Test]
    public void ClaimIsConcurrentAndSelectsOneExactTypeForTheProcess()
    {
        var participantType = typeof(FirstParticipant);

        _ = Parallel.For(0, 64, static _ => WorldAdmissionParticipant.Claim(typeof(FirstParticipant)));

        WorldAdmissionParticipant.Claim(participantType);

        _ = Assert.Throws<InvalidOperationException>(() => WorldAdmissionParticipant.Claim(typeof(SecondParticipant)));
    }

    private sealed class FirstParticipant
    {
    }

    private sealed class SecondParticipant
    {
    }
}
