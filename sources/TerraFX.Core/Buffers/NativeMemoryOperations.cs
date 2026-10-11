// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Runtime.InteropServices;

namespace TerraFX.Buffers;

internal static class NativeMemoryOperations
{
    internal static INativeMemoryOperations Instance { get; } = new DefaultOperations();

    private sealed class DefaultOperations : INativeMemoryOperations
    {
        nuint INativeMemoryOperations.Allocate(nuint byteLength, nuint alignment, bool useAlignedAllocator)
        {
            unsafe
            {
                var address = useAlignedAllocator
                    ? NativeMemory.AlignedAlloc(byteLength, alignment)
                    : NativeMemory.Alloc(byteLength);
                return (nuint)address;
            }
        }

        void INativeMemoryOperations.Clear(nuint address, nuint byteLength)
        {
            unsafe
            {
                NativeMemory.Clear((void*)address, byteLength);
            }
        }

        void INativeMemoryOperations.Free(nuint address, bool useAlignedAllocator)
        {
            unsafe
            {
                if (useAlignedAllocator)
                {
                    NativeMemory.AlignedFree((void*)address);
                }
                else
                {
                    NativeMemory.Free((void*)address);
                }
            }
        }
    }
}
