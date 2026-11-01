// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace TerraFX.Graphics;

/// <summary>Contains one copied shader record layout.</summary>
public sealed class ShaderRecordLayout
{
    private readonly ImmutableArray<ShaderFieldLayout> _fields;

    /// <summary>Initializes a named shader record layout.</summary>
    /// <param name="name">The nonempty record name.</param>
    /// <param name="byteLength">The positive record byte length.</param>
    /// <param name="fields">The initialized, nonempty field layouts.</param>
    /// <exception cref="ArgumentException">Fields are default, empty, duplicated, overlapping, or outside the record.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="byteLength"/> is not positive.</exception>
    public ShaderRecordLayout(string name, int byteLength, ImmutableArray<ShaderFieldLayout> fields)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteLength);

        if (fields.IsDefaultOrEmpty)
        {
            throw new ArgumentException("Shader record fields must be initialized and nonempty.", nameof(fields));
        }

        var sortedFields = fields.ToArray();
        Array.Sort(sortedFields, static (left, right) => {
            var offsetComparison = left.ByteOffset.CompareTo(right.ByteOffset);
            return (offsetComparison != 0)
                ? offsetComparison
                : StringComparer.Ordinal.Compare(left.Name, right.Name);
        });

        var names = new HashSet<string>(StringComparer.Ordinal);
        long previousEnd = 0;

        for (var index = 0; index < sortedFields.Length; index++)
        {
            var field = sortedFields[index];

            if (string.IsNullOrWhiteSpace(field.Name)
                || (field.ValueType == ShaderValueType.None)
                || !Enum.IsDefined(field.ValueType)
                || (field.ByteOffset < 0)
                || (field.ByteLength <= 0)
                || (field.ArrayCount <= 0))
            {
                throw new ArgumentException("Shader record fields must contain valid initialized values.", nameof(fields));
            }

            if (!names.Add(field.Name))
            {
                throw new ArgumentException("Shader record field names must be unique.", nameof(fields));
            }

            if ((field.ArrayCount == 1) ? (field.ArrayStride != 0) : ((field.ArrayStride < field.ByteLength) || ((field.ArrayStride & 15) != 0)))
            {
                throw new ArgumentException("Shader record field array strides are invalid.", nameof(fields));
            }

            var fieldEnd = checked((long)field.ByteOffset + ((long)(field.ArrayCount - 1) * field.ArrayStride) + field.ByteLength);

            if ((field.ByteOffset < previousEnd) || (fieldEnd > byteLength))
            {
                throw new ArgumentException("Shader record field ranges must be nonoverlapping and fit the record.", nameof(fields));
            }

            previousEnd = fieldEnd;
        }

        Name = name;
        ByteLength = byteLength;
        _fields = [.. sortedFields];
    }

    /// <summary>Gets the positive record byte length.</summary>
    public int ByteLength { get; }

    /// <summary>Gets the copied field layouts in byte-offset order.</summary>
    public ImmutableArray<ShaderFieldLayout> Fields => _fields;

    /// <summary>Gets the record name.</summary>
    public string Name { get; }
}
