// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

/// <summary>Describes the immutable size, allowed uses, and memory kind of one GPU buffer.</summary>
public sealed class BufferDescription
{
    private const BufferUsage KnownUsages =
        BufferUsage.Vertex
        | BufferUsage.Index
        | BufferUsage.Uniform
        | BufferUsage.Storage
        | BufferUsage.CopySource
        | BufferUsage.CopyDestination;

    /// <summary>Initializes a validated GPU buffer description.</summary>
    /// <param name="byteLength">The positive byte length.</param>
    /// <param name="usage">The nonempty set of permitted GPU operations.</param>
    /// <param name="memoryKind">The selected CPU/GPU visibility.</param>
    /// <param name="structuredStride">The positive element stride for a structured buffer, or <see langword="null"/>.</param>
    /// <param name="debugName">The optional diagnostic name; an empty string is permitted.</param>
    /// <exception cref="ArgumentException">The usage or structured byte length is inconsistent.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A length, usage, memory kind, or stride is invalid.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="debugName"/> is <see langword="null"/>.</exception>
    public BufferDescription(
        long byteLength,
        BufferUsage usage,
        GpuMemoryKind memoryKind,
        int? structuredStride,
        string debugName)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteLength);

        if ((usage == BufferUsage.None) || ((usage & ~KnownUsages) != 0))
        {
            throw new ArgumentOutOfRangeException(nameof(usage));
        }

        if ((memoryKind == GpuMemoryKind.None) || !Enum.IsDefined(memoryKind))
        {
            throw new ArgumentOutOfRangeException(nameof(memoryKind));
        }

        if ((memoryKind == GpuMemoryKind.Upload)
            && ((usage & (BufferUsage.Storage | BufferUsage.CopyDestination)) != 0))
        {
            throw new ArgumentException("Upload buffers cannot be GPU-writable.", nameof(usage));
        }

        if ((memoryKind == GpuMemoryKind.Readback) && (usage != BufferUsage.CopyDestination))
        {
            throw new ArgumentException("Readback buffers support only copy-destination usage.", nameof(usage));
        }

        if (structuredStride is int stride)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stride);

            if ((byteLength % stride) != 0)
            {
                throw new ArgumentException("Structured buffer byte length must be a multiple of its element stride.", nameof(structuredStride));
            }
        }

        ArgumentNullException.ThrowIfNull(debugName);

        ByteLength = byteLength;
        DebugName = debugName;
        MemoryKind = memoryKind;
        StructuredStride = structuredStride;
        Usage = usage;
    }

    /// <summary>Gets the positive byte length.</summary>
    public long ByteLength { get; }

    /// <summary>Gets the optional diagnostic name.</summary>
    public string DebugName { get; }

    /// <summary>Gets the selected CPU/GPU visibility.</summary>
    public GpuMemoryKind MemoryKind { get; }

    /// <summary>Gets the positive structured element stride, or <see langword="null"/> for a non-structured buffer.</summary>
    public int? StructuredStride { get; }

    /// <summary>Gets the nonempty set of permitted GPU operations.</summary>
    public BufferUsage Usage { get; }
}
