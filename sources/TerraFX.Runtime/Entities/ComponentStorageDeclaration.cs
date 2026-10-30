// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Entities;

/// <summary>Describes complete nonoverlapping semantic storage coverage for one component type.</summary>
public sealed class ComponentStorageDeclaration
{
    /// <summary>Initializes a complete component storage declaration.</summary>
    /// <param name="logicalBytes">The physical component size in bytes.</param>
    /// <param name="leaves">The byte-ordered semantic storage leaves.</param>
    /// <param name="padding">The byte-ordered padding intervals.</param>
    /// <exception cref="ArgumentException">A collection is default, a path repeats, or intervals do not exactly cover the layout.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="logicalBytes"/> is outside the component layout range.</exception>
    public ComponentStorageDeclaration(
        int logicalBytes,
        ImmutableArray<ComponentStorageLeaf> leaves,
        ImmutableArray<ComponentPaddingInterval> padding)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(logicalBytes, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(logicalBytes, 32756);

        if (leaves.IsDefault)
        {
            throw new ArgumentException("The component storage leaf array must be initialized.", nameof(leaves));
        }

        if (padding.IsDefault)
        {
            throw new ArgumentException("The component padding array must be initialized.", nameof(padding));
        }

        var intervalCount = checked(leaves.Length + padding.Length);
        var intervals = new (int Offset, int Extent)[intervalCount];
        var intervalIndex = 0;
        var previousLeafOffset = -1;

        for (var index = 0; index < leaves.Length; index++)
        {
            var leaf = leaves[index] ?? throw new ArgumentException("The component storage leaf array cannot contain null entries.", nameof(leaves));

            if (leaf.ByteOffset <= previousLeafOffset)
            {
                throw new ArgumentException("Component storage leaves must be ordered by byte offset.", nameof(leaves));
            }

            for (var previousIndex = 0; previousIndex < index; previousIndex++)
            {
                if (leaf.Path.Equals(leaves[previousIndex]?.Path))
                {
                    throw new ArgumentException("Component storage paths must be unique.", nameof(leaves));
                }
            }

            intervals[intervalIndex++] = (leaf.ByteOffset, leaf.ByteExtent);
            previousLeafOffset = leaf.ByteOffset;
        }

        var previousPaddingOffset = -1;

        foreach (var interval in padding)
        {
            if ((interval.ByteOffset < 0)
                || (interval.ByteExtent <= 0)
                || (interval.ByteOffset <= previousPaddingOffset))
            {
                throw new ArgumentException("Component padding intervals must be valid and ordered by byte offset.", nameof(padding));
            }

            intervals[intervalIndex++] = (interval.ByteOffset, interval.ByteExtent);
            previousPaddingOffset = interval.ByteOffset;
        }

        Array.Sort(intervals, static (left, right) => left.Offset.CompareTo(right.Offset));

        var coveredBytes = 0;

        foreach (var (offset, extent) in intervals)
        {
            if (offset != coveredBytes)
            {
                throw new ArgumentException("Component storage leaves and padding must cover the layout without gaps or overlap.", nameof(leaves));
            }

            coveredBytes = checked(offset + extent);

            if (coveredBytes > logicalBytes)
            {
                throw new ArgumentException("Component storage coverage cannot exceed the declared logical size.", nameof(leaves));
            }
        }

        if (coveredBytes != logicalBytes)
        {
            throw new ArgumentException("Component storage leaves and padding must cover the complete logical size.", nameof(leaves));
        }

        LogicalBytes = logicalBytes;
        Leaves = ImmutableArrayCopy.Create(leaves);
        Padding = ImmutableArrayCopy.Create(padding);
    }

    /// <summary>Gets the immutable byte-ordered storage leaves.</summary>
    public ImmutableArray<ComponentStorageLeaf> Leaves { get; }

    /// <summary>Gets the logical component size in bytes.</summary>
    public int LogicalBytes { get; }

    /// <summary>Gets the immutable byte-ordered padding intervals.</summary>
    public ImmutableArray<ComponentPaddingInterval> Padding { get; }
}
