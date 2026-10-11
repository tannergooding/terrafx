// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Buffers;

internal interface INativeMemoryOperations
{
    nuint Allocate(nuint byteLength, nuint alignment, bool useAlignedAllocator);

    void Clear(nuint address, nuint byteLength);

    void Free(nuint address, bool useAlignedAllocator);
}
