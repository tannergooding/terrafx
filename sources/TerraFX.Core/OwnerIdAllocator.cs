// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Threading;

namespace TerraFX;

internal static class OwnerIdAllocator
{
    private static long s_lastAllocated;

    internal static uint Allocate()
    {
        while (true)
        {
            var lastAllocated = Volatile.Read(ref s_lastAllocated);
            var next = GetNext(lastAllocated);

            if (Interlocked.CompareExchange(ref s_lastAllocated, next, lastAllocated) == lastAllocated)
            {
                return next;
            }
        }
    }

    internal static uint GetNext(long lastAllocated)
    {
        if (lastAllocated is < 0 or >= uint.MaxValue)
        {
            ThrowHelper.ThrowInvalidOperationException("The process-wide owner identity domain is exhausted or invalid.");
        }

        return checked((uint)(lastAllocated + 1));
    }
}
