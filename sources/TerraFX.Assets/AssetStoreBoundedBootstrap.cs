// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Threading;

namespace TerraFX.Assets;

internal static class AssetStoreBoundedBootstrap
{
    private const int StateEmpty = 0;
    private const int StateFailed = 3;
    private const int StateInitializing = 1;
    private const int StateReady = 2;

    private static long s_sharedOriginalBudgetBytes;
    private static long s_sharedProvisionBytes;
    private static int s_state;

    internal static void Cancel(bool ownsSharedProvision)
    {
        if (ownsSharedProvision && (Volatile.Read(ref s_state) == StateInitializing))
        {
            _ = Interlocked.Exchange(ref s_sharedProvisionBytes, 0);
            _ = Interlocked.Exchange(ref s_sharedOriginalBudgetBytes, 0);
            _ = Interlocked.CompareExchange(ref s_state, StateEmpty, StateInitializing);
        }
    }

    internal static bool CompleteInitialization(bool ownsSharedProvision)
        => ownsSharedProvision
            ? Interlocked.CompareExchange(ref s_state, StateReady, StateInitializing) == StateInitializing
            : Volatile.Read(ref s_state) == StateReady;

    internal static void Fail(bool ownsSharedProvision)
    {
        if (ownsSharedProvision)
        {
            _ = Interlocked.CompareExchange(ref s_state, StateFailed, StateInitializing);
        }
    }

    internal static AssetBoundedRefusal TryBegin(
        long cpuAssetBudgetBytes,
        long localProvisionBytes,
        long sharedProvisionBytes,
        out bool ownsSharedProvision,
        out long reservedBytes)
    {
        ownsSharedProvision = false;
        reservedBytes = 0;

        while (true)
        {
            var state = Volatile.Read(ref s_state);

            if (state is StateReady)
            {
                if (Volatile.Read(ref s_sharedProvisionBytes) != sharedProvisionBytes)
                {
                    return AssetBoundedRefusal.ProfileUnavailable;
                }

                if (Volatile.Read(ref s_sharedOriginalBudgetBytes) < sharedProvisionBytes)
                {
                    return AssetBoundedRefusal.ProfileUnavailable;
                }

                if (localProvisionBytes > cpuAssetBudgetBytes)
                {
                    return AssetBoundedRefusal.Capacity;
                }

                reservedBytes = localProvisionBytes;
                return AssetBoundedRefusal.None;
            }

            if (state is StateInitializing)
            {
                return AssetBoundedRefusal.Busy;
            }

            if (state is StateFailed)
            {
                return AssetBoundedRefusal.ProfileUnavailable;
            }

            var firstProvisionBytes = checked(localProvisionBytes + sharedProvisionBytes);

            if (firstProvisionBytes > cpuAssetBudgetBytes)
            {
                return AssetBoundedRefusal.Capacity;
            }

            if (Interlocked.CompareExchange(ref s_state, StateInitializing, StateEmpty) == StateEmpty)
            {
                _ = Interlocked.Exchange(ref s_sharedProvisionBytes, sharedProvisionBytes);
                _ = Interlocked.Exchange(ref s_sharedOriginalBudgetBytes, cpuAssetBudgetBytes);
                ownsSharedProvision = true;
                reservedBytes = firstProvisionBytes;
                return AssetBoundedRefusal.None;
            }
        }
    }
}
