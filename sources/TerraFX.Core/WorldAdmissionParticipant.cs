// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX;

internal static class WorldAdmissionParticipant
{
    private static Type? s_participantType;

    internal static void Claim(Type participantType)
    {
        ArgumentNullException.ThrowIfNull(participantType);

        var selectedParticipantType = Interlocked.CompareExchange(ref s_participantType, participantType, null);

        if ((selectedParticipantType is null) || ReferenceEquals(selectedParticipantType, participantType))
        {
            return;
        }

        ThrowHelper.ThrowInvalidOperationException("The Core World-admission participant type has already been selected.");
    }
}
