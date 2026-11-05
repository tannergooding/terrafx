// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics.D3D12;

internal static class D3D12StagingAdmission
{
    internal static bool ShouldUseDedicatedStaging(
        long requestBytes,
        long ringCapacityBytes,
        long? availableBudgetBytes,
        bool ringResourceExists)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ringCapacityBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(availableBudgetBytes.GetValueOrDefault());

        return (requestBytes > ringCapacityBytes)
            || (!ringResourceExists
                && (availableBudgetBytes is { } availableBytes)
                && (availableBytes < ringCapacityBytes));
    }
}
