// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Threading;

namespace TerraFX;

internal static class HostEventSequenceAllocator
{
    private static long s_lastAllocated;

    internal static long Allocate()
    {
        while (true)
        {
            var lastAllocated = Volatile.Read(ref s_lastAllocated);
            var next = GetNext(lastAllocated);
            var observed = Interlocked.CompareExchange(ref s_lastAllocated, next, lastAllocated);

            if (observed == lastAllocated)
            {
                return next;
            }
        }
    }

    internal static long GetNext(long lastAllocated)
    {
        if (lastAllocated is < 0 or long.MaxValue)
        {
            ThrowHelper.ThrowInvalidOperationException("The process-wide host event sequence is exhausted or invalid.");
        }

        return checked(lastAllocated + 1);
    }
}
