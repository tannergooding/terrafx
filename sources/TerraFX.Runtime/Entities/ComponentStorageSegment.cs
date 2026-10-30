// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers;
using System.Text;

namespace TerraFX.Entities;

/// <summary>Identifies one exact source member or inline-array storage segment.</summary>
public readonly struct ComponentStorageSegment : IEquatable<ComponentStorageSegment>
{
    private const int MaximumMemberNameLength = 128;
    private const int MaximumMemberNameUtf8Bytes = 128;

    private readonly int? _inlineIndex;
    private readonly ComponentStorageSegmentKind _kind;
    private readonly string? _memberName;

    private ComponentStorageSegment(ComponentStorageSegmentKind kind, string? memberName, int? inlineIndex)
    {
        _kind = kind;
        _memberName = memberName;
        _inlineIndex = inlineIndex;
    }

    /// <summary>Gets the inline-array index, or <see langword="null"/> for a member segment.</summary>
    public int? Index => _inlineIndex;

    /// <summary>Gets the segment kind.</summary>
    public ComponentStorageSegmentKind Kind => _kind;

    /// <summary>Gets the exact source member spelling, or <see langword="null"/> for an index segment.</summary>
    public string? MemberName => _memberName;

    internal bool IsValid => _kind switch {
        ComponentStorageSegmentKind.Member => (_inlineIndex is null) && !string.IsNullOrEmpty(_memberName),
        ComponentStorageSegmentKind.InlineIndex => (_memberName is null) && (_inlineIndex is >= 0),
        _ => false,
    };

    /// <summary>Negates equality of the complete storage segment.</summary>
    public static bool operator !=(ComponentStorageSegment left, ComponentStorageSegment right) => !(left == right);

    /// <summary>Compares the complete storage segment.</summary>
    public static bool operator ==(ComponentStorageSegment left, ComponentStorageSegment right) => left.Equals(right);

    /// <summary>Creates a zero-based inline-array index segment.</summary>
    /// <param name="index">The zero-based inline-array index.</param>
    /// <returns>The inline-array index segment.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative.</exception>
    public static ComponentStorageSegment InlineIndex(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return new ComponentStorageSegment(ComponentStorageSegmentKind.InlineIndex, null, index);
    }

    /// <summary>Creates a source-member segment.</summary>
    /// <param name="memberName">The exact nonempty source spelling, bounded to 128 characters and UTF-8 bytes.</param>
    /// <returns>The source-member segment.</returns>
    /// <exception cref="ArgumentException"><paramref name="memberName"/> is empty or contains invalid UTF-16.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="memberName"/> exceeds its supported character or byte limit.</exception>
    public static ComponentStorageSegment Member(string memberName)
    {
        ArgumentNullException.ThrowIfNull(memberName);

        if (memberName.Length == 0)
        {
            throw new ArgumentException("A storage member name cannot be empty.", nameof(memberName));
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(memberName.Length, MaximumMemberNameLength, nameof(memberName));

        var remaining = memberName.AsSpan();
        var utf8ByteCount = 0;

        while (!remaining.IsEmpty)
        {
            var status = Rune.DecodeFromUtf16(remaining, out var rune, out var consumed);

            if (status != OperationStatus.Done)
            {
                throw new ArgumentException("A storage member name must contain valid UTF-16.", nameof(memberName));
            }

            utf8ByteCount = checked(utf8ByteCount + rune.Utf8SequenceLength);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(utf8ByteCount, MaximumMemberNameUtf8Bytes, nameof(memberName));
            remaining = remaining[consumed..];
        }

        return new ComponentStorageSegment(ComponentStorageSegmentKind.Member, memberName, null);
    }

    /// <inheritdoc />
    public bool Equals(ComponentStorageSegment other)
        => _kind == other._kind
        && StringComparer.Ordinal.Equals(_memberName, other._memberName)
        && _inlineIndex == other._inlineIndex;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ComponentStorageSegment other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(_kind, _memberName, _inlineIndex);
}
