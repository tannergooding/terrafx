// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;

namespace TerraFX.Entities;

internal sealed class IdentityGuidIndex
{
    internal const int HeaderSize = 32;
    internal const int MaximumInternalEntries = 508;
    internal const int MaximumLeafEntries = 1016;
    internal const int MaximumPageCount = 66_315;
    internal const int PageSize = 4096;

    private readonly byte[][] _ledgerPages;
    private readonly byte[]?[] _pages;
    private readonly int _root;

    internal IdentityGuidIndex(byte[][] ledgerPages, byte[]?[] pages, int pageCount, int root)
    {
        _ledgerPages = ledgerPages;
        _pages = pages;
        _root = root;
        PageCount = pageCount;
    }

    internal int DirectoryCapacity => _pages.Length;

    internal int Height => _root < 0 ? 0 : ReadInt32(GetPage(_root), 4) + 1;

    internal int PageCount { get; }

    internal int Root => _root;

    internal static int GetMaximumPages(int assignmentCount)
    {
        if (assignmentCount <= MaximumLeafEntries)
        {
            return assignmentCount == 0 ? 0 : 1;
        }

        // Nonroot splits retain at least 508 leaf ordinals or 254 children.
        // At the production ceiling, these two nonroot levels plus the root
        // require no more than 66053 + 261 + 1 pages.
        var leaves = (assignmentCount + 507) / 508;
        return leaves + ((leaves + 253) / 254) + 1;
    }

    private static int FindChild(byte[] page, ReadOnlySpan<byte> id, byte[][] ledgerPages)
    {
        var minimum = 1;
        var maximum = ReadInt32(page, 0);

        while (minimum < maximum)
        {
            var midpoint = minimum + ((maximum - minimum) / 2);

            if (GetId(ledgerPages, ReadOrdinal(page, midpoint, 8)).SequenceCompareTo(id) <= 0)
            {
                minimum = midpoint + 1;
            }
            else
            {
                maximum = midpoint;
            }
        }

        return minimum - 1;
    }

    private static ReadOnlySpan<byte> GetId(byte[][] ledgerPages, uint ordinal)
    {
        var index = checked((int)ordinal - 1);
        return ledgerPages[index / StableIdentityLedger.RecordsPerPage].AsSpan(
            index % StableIdentityLedger.RecordsPerPage * StableIdentityLedger.RecordSize, 16);
    }

    private static int LowerBound(byte[] page, int count, ReadOnlySpan<byte> id, byte[][] ledgerPages, int stride)
    {
        var minimum = 0;
        var maximum = count;

        while (minimum < maximum)
        {
            var midpoint = minimum + ((maximum - minimum) / 2);

            if (GetId(ledgerPages, ReadOrdinal(page, midpoint, stride)).SequenceCompareTo(id) < 0)
            {
                minimum = midpoint + 1;
            }
            else
            {
                maximum = midpoint;
            }
        }

        return minimum;
    }

    private static int ReadInt32(byte[] page, int offset) => BinaryPrimitives.ReadInt32LittleEndian(page.AsSpan(offset, 4));

    private static uint ReadOrdinal(byte[] page, int index, int stride)
        => BinaryPrimitives.ReadUInt32LittleEndian(page.AsSpan(HeaderSize + (index * stride), 4));

    private static void WriteInt32(byte[] page, int offset, int value)
        => BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(offset, 4), value);

    internal void CopyPage(int pageIndex, Span<byte> destination)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pageIndex, PageCount);
        GetPage(pageIndex).CopyTo(destination);
    }

    internal uint Find(scoped ReadOnlySpan<byte> id)
    {
        if (_root < 0)
        {
            return 0;
        }

        var page = GetPage(_root);

        while (ReadInt32(page, 4) != 0)
        {
            var child = FindChild(page, id, _ledgerPages);
            page = GetPage(ReadInt32(page, HeaderSize + (child * 8) + 4));
        }

        var count = ReadInt32(page, 0);
        var position = LowerBound(page, count, id, _ledgerPages, 4);

        if (position < count)
        {
            var ordinal = ReadOrdinal(page, position, 4);

            if (GetId(_ledgerPages, ordinal).SequenceEqual(id))
            {
                return ordinal;
            }
        }

        return 0;
    }

    private byte[] GetPage(int index) => _pages[index] ?? throw new InvalidOperationException("The identity index page is missing.");

    internal sealed class Builder
    {
        private readonly byte[][] _ledgerPages;
        private readonly bool[] _owned;
        private int _pageCount;
        private readonly byte[]?[] _pages;
        private int _root;

        internal Builder(IdentityGuidIndex source, byte[][] ledgerPages, int directoryCapacity)
        {
            _ledgerPages = ledgerPages;
            _pages = new byte[directoryCapacity][];
            _owned = new bool[directoryCapacity];
            Array.Copy(source._pages, _pages, source.PageCount);
            _pageCount = source.PageCount;
            _root = source._root;
        }

        private static void InsertEntry(Span<byte> entries, int count, int position, ReadOnlySpan<byte> entry, int stride)
        {
            entries.Slice(position * stride, (count - position) * stride).CopyTo(entries[((position + 1) * stride)..]);
            entry.CopyTo(entries[(position * stride)..]);
        }

        internal IdentityGuidIndex Finish() => new(_ledgerPages, _pages, _pageCount, _root);

        internal void Insert(uint ordinal)
        {
            if (_root < 0)
            {
                _root = AddPage(0);
                WriteInt32(GetWritablePage(_root), 0, 1);
                BinaryPrimitives.WriteUInt32LittleEndian(GetWritablePage(_root).AsSpan(HeaderSize, 4), ordinal);
                return;
            }

            var split = Insert(_root, ordinal);

            if (split >= 0)
            {
                var oldRoot = _root;
                _root = AddPage(ReadInt32(GetWritablePage(oldRoot), 4) + 1);
                var page = GetWritablePage(_root);
                WriteInt32(page, 0, 2);
                WriteChild(page, 0, oldRoot);
                WriteChild(page, 1, split);
            }
        }

        private int AddPage(int level)
        {
            if (_pageCount == _pages.Length)
            {
                throw new InvalidOperationException("The reserved identity index page bound was exceeded.");
            }

            var index = _pageCount++;
            _pages[index] = new byte[PageSize];
            _owned[index] = true;
            WriteInt32(GetWritablePage(index), 4, level);
            return index;
        }

        private byte[] GetWritablePage(int index)
        {
            var page = _pages[index] ?? throw new InvalidOperationException("The identity index page is missing.");

            if (!_owned[index])
            {
                page = (byte[])page.Clone();
                _pages[index] = page;
                _owned[index] = true;
            }

            return page;
        }

        private int Insert(int pageIndex, uint ordinal)
        {
            var page = GetWritablePage(pageIndex);
            var level = ReadInt32(page, 4);
            var count = ReadInt32(page, 0);
            var stride = level == 0 ? 4 : 8;
            var capacity = level == 0 ? MaximumLeafEntries : MaximumInternalEntries;
            var id = GetId(_ledgerPages, ordinal);
            Span<byte> entry = stackalloc byte[8];
            int position;

            if (level == 0)
            {
                position = LowerBound(page, count, id, _ledgerPages, 4);
                BinaryPrimitives.WriteUInt32LittleEndian(entry, ordinal);
            }
            else
            {
                var childPosition = FindChild(page, id, _ledgerPages);
                var childIndex = ReadInt32(page, HeaderSize + (childPosition * 8) + 4);
                var splitChild = Insert(childIndex, ordinal);
                WriteChild(page, childPosition, childIndex);

                if (splitChild < 0)
                {
                    return -1;
                }

                position = childPosition + 1;
                BinaryPrimitives.WriteUInt32LittleEndian(entry, ReadOrdinal(GetWritablePage(splitChild), 0, stride));
                BinaryPrimitives.WriteInt32LittleEndian(entry[4..], splitChild);
            }

            if (count < capacity)
            {
                InsertEntry(page.AsSpan(HeaderSize), count, position, entry[..stride], stride);
                WriteInt32(page, 0, count + 1);
                return -1;
            }

            // The overflow entry lives on the stack, not beyond the 4096-byte page.
            Span<byte> overflow = stackalloc byte[PageSize - HeaderSize + 8];
            page.AsSpan(HeaderSize).CopyTo(overflow);
            InsertEntry(overflow, count, position, entry[..stride], stride);
            var leftCount = (count + 1) / 2;
            var rightCount = count + 1 - leftCount;
            var rightIndex = AddPage(level);
            var right = GetWritablePage(rightIndex);
            page.AsSpan(HeaderSize).Clear();
            overflow[..(leftCount * stride)].CopyTo(page.AsSpan(HeaderSize));
            overflow.Slice(leftCount * stride, rightCount * stride).CopyTo(right.AsSpan(HeaderSize));
            WriteInt32(page, 0, leftCount);
            WriteInt32(right, 0, rightCount);
            return rightIndex;
        }

        private void WriteChild(byte[] page, int position, int childIndex)
        {
            var entry = page.AsSpan(HeaderSize + (position * 8), 8);
            BinaryPrimitives.WriteUInt32LittleEndian(entry, ReadOrdinal(GetWritablePage(childIndex), 0, 4));
            BinaryPrimitives.WriteInt32LittleEndian(entry[4..], childIndex);
        }
    }
}
